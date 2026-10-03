using System;
using System.Collections.Generic;
using System.Text;
using AnimalCombat.ReplayViewer.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimalCombat.ReplayViewer.Runtime.Loading
{
    public sealed class ReplayLoadResult
    {
        ReplayLoadResult(ReplayDocument document, string error)
        {
            Document = document;
            Error = error;
        }

        public ReplayDocument Document { get; }
        public string Error { get; }
        public bool Success => Document != null;

        public static ReplayLoadResult Loaded(ReplayDocument document) => new ReplayLoadResult(document, null);
        public static ReplayLoadResult Failed(string error) => new ReplayLoadResult(null, error);
    }

    public sealed class ReplayLoader
    {
        public const string SupportedSchemaVersion = "combat.replay/0.1";
        public const int MaximumUtf8Bytes = 8 * 1024 * 1024;

        public ReplayLoadResult Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return ReplayLoadResult.Failed("Replay is empty.");

            if (Encoding.UTF8.GetByteCount(json) > MaximumUtf8Bytes)
                return ReplayLoadResult.Failed("Replay exceeds the 8 MiB v0.1 viewer limit.");

            try
            {
                return ReplayLoadResult.Loaded(Parse(json));
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is FormatException ||
                exception is InvalidOperationException)
            {
                return ReplayLoadResult.Failed(exception.Message);
            }
        }

        static ReplayDocument Parse(string json)
        {
            var root = JObject.Parse(json);
            string schemaVersion = RequiredString(root, "schema_version");
            if (!string.Equals(schemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
                throw new FormatException($"Unsupported replay schema '{schemaVersion}'. Expected '{SupportedSchemaVersion}'.");

            var input = RequiredObject(root, "input");
            var arenaToken = RequiredObject(input, "arena");
            var arena = new ArenaDefinition(
                RequiredString(arenaToken, "arena_id"),
                RequiredLong(arenaToken, "min_position"),
                RequiredLong(arenaToken, "max_position"));
            if (arena.MaxPosition <= arena.MinPosition)
                throw new FormatException("Arena max_position must be greater than min_position.");

            var fighterTokens = RequiredArray(input, "fighters");
            var fighters = new List<FighterDefinition>(fighterTokens.Count);
            var inputFrames = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (JToken token in fighterTokens)
            {
                var fighter = token as JObject ?? throw new FormatException("Every input fighter must be an object.");
                string fighterId = RequiredString(fighter, "fighter_id");
                var initialFrameToken = RequiredObject(fighter, "initial_frame");
                if (!inputFrames.TryAdd(fighterId, initialFrameToken))
                    throw new FormatException($"Duplicate fighter_id '{fighterId}'.");

                fighters.Add(new FighterDefinition(
                    fighterId,
                    RequiredString(fighter, "animal_id"),
                    RequiredString(fighter, "side"),
                    ParseFrame(initialFrameToken)));
            }

            if (fighters.Count != 2)
                throw new FormatException("Replay Viewer v0.1 requires exactly two fighters.");

            var eventTokens = RequiredArray(root, "events");
            if (eventTokens.Count == 0)
                throw new FormatException("Replay must contain at least one event.");

            var firstEventToken = eventTokens[0] as JObject ?? throw new FormatException("Event 0 must be an object.");
            if (!string.Equals(RequiredString(firstEventToken, "event_type"), "BattleStarted", StringComparison.Ordinal))
                throw new FormatException("Event 0 must be BattleStarted.");

            var initialFrameTokens = RequiredArray(RequiredObject(firstEventToken, "payload"), "initial_frames");
            var initialFrames = new List<FighterFrame>(initialFrameTokens.Count);
            foreach (JToken token in initialFrameTokens)
            {
                var frameToken = token as JObject ?? throw new FormatException("Every initial frame must be an object.");
                string fighterId = RequiredString(frameToken, "fighter_id");
                if (!inputFrames.TryGetValue(fighterId, out JToken inputFrame))
                    throw new FormatException($"Initial frame references unknown fighter '{fighterId}'.");
                if (!JToken.DeepEquals(inputFrame, frameToken))
                    throw new FormatException($"BattleStarted frame for '{fighterId}' differs from input.initial_frame.");
                initialFrames.Add(ParseFrame(frameToken));
            }

            if (initialFrames.Count != fighters.Count)
                throw new FormatException("BattleStarted initial_frames must cover every fighter.");

            var events = new List<ReplayEvent>(eventTokens.Count);
            long previousTick = -1;
            for (int index = 0; index < eventTokens.Count; index++)
            {
                var eventToken = eventTokens[index] as JObject ?? throw new FormatException($"Event {index} must be an object.");
                long sequence = RequiredLong(eventToken, "sequence");
                long tick = RequiredLong(eventToken, "tick");
                if (sequence != index)
                    throw new FormatException($"Event sequence must be contiguous: expected {index}, got {sequence}.");
                if (tick < previousTick)
                    throw new FormatException($"Event tick decreased at sequence {sequence}.");

                previousTick = tick;
                events.Add(ParseEvent(eventToken, sequence, tick));
            }

            return new ReplayDocument(
                schemaVersion,
                RequiredString(root, "replay_id"),
                RequiredString(root, "battle_id"),
                root.SelectToken("engine.engine_version")?.Value<string>() ?? "unknown",
                arena,
                fighters,
                initialFrames,
                events);
        }

        static ReplayEvent ParseEvent(JObject token, long sequence, long tick)
        {
            var after = token["after"] as JObject;
            var payload = token["payload"] as JObject ?? new JObject();
            var finalFrames = new List<FighterFrame>();
            if (payload["final_frames"] is JArray finalFrameTokens)
            {
                foreach (JToken finalFrameToken in finalFrameTokens)
                {
                    if (finalFrameToken is JObject finalFrame)
                        finalFrames.Add(ParseFrame(finalFrame));
                }
            }

            return new ReplayEvent(
                sequence,
                tick,
                RequiredString(token, "event_id"),
                RequiredString(token, "event_type"),
                OptionalString(token, "actor_id"),
                OptionalString(token, "target_id"),
                OptionalString(token, "action_id"),
                after?["actor"] is JObject actor ? ParseFrame(actor) : null,
                after?["target"] is JObject target ? ParseFrame(target) : null,
                finalFrames,
                (JObject)payload.DeepClone());
        }

        static FighterFrame ParseFrame(JObject token)
        {
            return new FighterFrame(
                RequiredString(token, "fighter_id"),
                RequiredLong(token, "health"),
                RequiredLong(token, "max_health"),
                RequiredLong(token, "position"),
                RequiredString(token, "facing"),
                RequiredString(token, "state"),
                RequiredLong(token, "energy"),
                RequiredLong(token, "max_energy"),
                RequiredLong(token, "stagger"),
                RequiredLong(token, "stagger_threshold"));
        }

        static JObject RequiredObject(JObject parent, string name)
        {
            return parent[name] as JObject ?? throw new FormatException($"Required object '{name}' is missing.");
        }

        static JArray RequiredArray(JObject parent, string name)
        {
            return parent[name] as JArray ?? throw new FormatException($"Required array '{name}' is missing.");
        }

        static string RequiredString(JObject parent, string name)
        {
            string value = parent[name]?.Type == JTokenType.String ? parent[name]?.Value<string>() : null;
            return !string.IsNullOrEmpty(value) ? value : throw new FormatException($"Required string '{name}' is missing.");
        }

        static string OptionalString(JObject parent, string name)
        {
            JToken token = parent[name];
            return token == null || token.Type == JTokenType.Null ? null : token.Value<string>();
        }

        static long RequiredLong(JObject parent, string name)
        {
            JToken token = parent[name];
            if (token == null || token.Type != JTokenType.Integer)
                throw new FormatException($"Required integer '{name}' is missing.");
            return token.Value<long>();
        }
    }
}
