using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Battle.Replay.Verification;

/// <summary>Public effect/control witnesses for Engine0.5 only; never infers private DATA or rule budgets.</summary>
internal static class EffectReplaySemanticValidator
{
    internal static void Validate(JsonElement replay, IReadOnlyList<JsonElement> events, ICollection<ReplayVerificationIssue> issues)
    {
        if (!IsCompatible(replay.GetProperty("engine").GetProperty("engine_version").GetString()!)) return;
        try { new Witnesses(replay, events, issues).Validate(); }
        catch (Exception exception) when (exception is ArithmeticException or FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        { Add(issues, "$/events", "Malformed effect/control semantics: " + exception.Message); }
    }

    private sealed class EffectWitness
    {
        internal EffectWitness(BigInteger stacks, BigInteger end, string boundary, string application)
        { Stacks = stacks; End = end; Boundary = boundary; Application = application; }
        internal BigInteger Stacks { get; }
        internal BigInteger End { get; }
        internal string Boundary { get; }
        internal string Application { get; }
    }
    private sealed class KnockdownWitness
    {
        internal KnockdownWitness(BigInteger end, int stage) { End = end; Stage = stage; }
        internal BigInteger End { get; }
        internal int Stage { get; }
    }

    private sealed class Witnesses
    {
        private readonly JsonElement _replay;
        private readonly IReadOnlyList<JsonElement> _events;
        private readonly ICollection<ReplayVerificationIssue> _issues;
        private readonly Dictionary<string, Dictionary<string, EffectWitness>> _effects = new(StringComparer.Ordinal)
        { ["fighter_a"] = new(StringComparer.Ordinal), ["fighter_b"] = new(StringComparer.Ordinal) };
        private readonly Dictionary<string, KnockdownWitness> _knockdown = new(StringComparer.Ordinal);
        private readonly Dictionary<string, JsonElement> _prior = new(StringComparer.Ordinal);
        private bool _cleanup;

        internal Witnesses(JsonElement replay, IReadOnlyList<JsonElement> events, ICollection<ReplayVerificationIssue> issues)
        { _replay = replay; _events = events; _issues = issues; }

        internal void Validate()
        {
            var config = _replay.GetProperty("config");
            if (Text(config, "balance_schema_version") != "combat.balance/0.2" || Text(config, "config_version") != "v0.2")
                Error("$/config", "Engine0.5 requires explicit balance0.2/config v0.2; public replay metadata cannot imply migration.");
            foreach (var fighter in _replay.GetProperty("input").GetProperty("fighters").EnumerateArray())
                if (fighter.GetProperty("initial_frame").GetProperty("effects").GetArrayLength() != 0)
                    Error("$/input/fighters", "Engine0.5 initial effects must be empty before BattleStart closure.");
            // Only actual advisory keyframes need a witness; no O(events * active-effects) history copies.
            var keyframes = _replay.GetProperty("keyframes").EnumerateArray().Select((frame, index) => (Frame: frame, Index: index))
                .GroupBy(x => Integer(x.Frame.GetProperty("after_sequence"))).ToDictionary(x => x.Key, x => x.ToArray());
            for (var index = 0; index < _events.Count; index++)
            {
                var item = _events[index]; var type = Text(item, "event_type")!;
                var tick = Integer(item.GetProperty("tick")); var path = "$/events/" + index.ToString(CultureInfo.InvariantCulture);
                Pair(item.GetProperty("before"), tick, path + "/before");
                if (_cleanup && type != "BattleEnded" && !(type == "EffectRemoved" && Text(item.GetProperty("payload"), "remove_reason") == "BattleEnded"))
                    Error(path, "Terminal effect cleanup cannot execute normal events or reapplication triggers.");
                if (type is "DecisionMade" or "ActionCommitted" or "AttackHit" or "DamageApplied")
                    foreach (var store in _effects.Values)
                        if (store.Values.Any(x => x.End <= tick)) Error(path, "A normal action cannot retain an effect from an earlier expiry boundary.");
                if (type is "EffectAdded" or "EffectRemoved") Mutation(item, index, tick, path);
                if (type == "StateChanged") Control(item, tick, path);
                if (type == "FighterDefeated" && Text(item, "actor_id") is { } defeated) _knockdown.Remove(defeated);
                Pair(item.GetProperty("after"), tick, path + "/after");
                if (type == "BattleEnded")
                {
                    if (_effects.Values.Any(x => x.Count != 0)) Error(path, "BattleEnded requires all surviving effects to have canonical cleanup removals.");
                    foreach (var frame in item.GetProperty("payload").GetProperty("final_frames").EnumerateArray())
                        Frame(frame, tick, path + "/payload/final_frames");
                }
                if (keyframes.TryGetValue(index, out var atSequence))
                    foreach (var keyframe in atSequence)
                        foreach (var frame in keyframe.Frame.GetProperty("fighters").EnumerateArray())
                            Frame(frame, tick, "$/keyframes/" + keyframe.Index.ToString(CultureInfo.InvariantCulture) + "/fighters",
                                advisory: keyframe.Index > 0 && keyframe.Index < _replay.GetProperty("keyframes").GetArrayLength() - 1);
                _prior[Text(item, "event_id")!] = item;
            }
            foreach (var frame in _replay.GetProperty("summary").GetProperty("final_frames").EnumerateArray())
                if (frame.GetProperty("effects").GetArrayLength() != 0) Error("$/summary/final_frames", "Terminal frames must have empty effect membership.");
        }

        private void Mutation(JsonElement item, int index, BigInteger tick, string path)
        {
            var actor = Text(item, "actor_id"); var id = Text(item, "effect_id"); var source = Text(item, "source_event_id");
            if (actor is null || !_effects.TryGetValue(actor, out var store) || id is null)
            { Error(path, "Effect mutations require the affected actor and effect_id."); return; }
            var payload = item.GetProperty("payload");
            if (source is null || !_prior.ContainsKey(source) ||
                !payload.GetProperty("related_event_ids").EnumerateArray().Any(x => x.GetString() == source))
                Error(path + "/source_event_id", "Effect mutation requires an earlier real primary cause included in related_event_ids.");
            if (source is not null && _prior.TryGetValue(source, out var cause))
            {
                foreach (var field in new[] { "action_id", "decision_id", "resolution_group_id" })
                    if (Text(item, field) is { } identity && identity != Text(cause, field))
                        Error(path + "/" + field, "Non-null action/decision/group lineage must be inherited from the primary cause.");
            }
            if (item.GetProperty("rng").ValueKind != JsonValueKind.Null) Error(path + "/rng", "Effects never draw RNG.");
            var before = item.GetProperty("before").GetProperty("actor"); var after = item.GetProperty("after").GetProperty("actor");
            if (before.ValueKind != JsonValueKind.Object || after.ValueKind != JsonValueKind.Object)
            { Error(path, "Effect mutation requires before and after affected frames."); return; }
            foreach (var property in before.EnumerateObject())
                if (property.Name != "effects" && !JsonElement.DeepEquals(property.Value, after.GetProperty(property.Name)))
                    Error(path + "/after/actor/" + property.Name, "An effect mutation cannot change non-effect fighter fields.");
            if (!JsonElement.DeepEquals(item.GetProperty("before").GetProperty("target"), item.GetProperty("after").GetProperty("target")))
                Error(path + "/after/target", "The source fighter cannot be mutated by the affected actor's effect event.");
            var beforeEffects = Entries(before, path + "/before/actor/effects"); var afterEffects = Entries(after, path + "/after/actor/effects");
            foreach (var pair in beforeEffects.Where(x => x.Key != id))
                if (!afterEffects.TryGetValue(pair.Key, out var unchanged) || !JsonElement.DeepEquals(pair.Value, unchanged))
                    Error(path, "Effect mutation changed an unrelated membership entry.");
            if (afterEffects.Keys.Any(x => x != id && !beforeEffects.ContainsKey(x))) Error(path, "Effect mutation introduced an unrelated entry.");
            store.TryGetValue(id, out var incumbent);
            var oldStacks = incumbent?.Stacks ?? BigInteger.Zero;
            if (Integer(payload.GetProperty("stacks_before")) != oldStacks) Error(path + "/payload/stacks_before", "stacks_before disagrees with authoritative public membership.");
            if (Text(item, "event_type") == "EffectAdded")
            {
                if (Integer(after.GetProperty("health")) <= 0 || Text(after, "state") == "Defeated") Error(path, "A zero-HP/defeated recipient cannot receive late effects.");
                if (!afterEffects.TryGetValue(id, out var entry)) { Error(path, "Added effect is absent from after frame."); return; }
                var stacks = Integer(entry.GetProperty("stacks")); var remaining = Integer(entry.GetProperty("ticks_remaining"));
                var duration = Integer(payload.GetProperty("duration_ticks")); var policy = Text(payload, "stack_policy");
                var boundary = Text(payload, "expiry_boundary")!;
                if (duration <= 0 || remaining != duration || Integer(payload.GetProperty("stacks_after")) != stacks || Text(entry, "expiry_boundary") != boundary)
                    Error(path + "/payload", "Added payload and positive after duration/stacks/boundary must agree.");
                if (incumbent is null)
                {
                    if (stacks != 1 || remaining != duration) Error(path, "A new occupant starts with one stack and exact declared duration.");
                }
                else
                {
                    var oldRemaining = BigInteger.Max(0, incumbent.End - tick);
                    if (boundary != incumbent.Boundary) Error(path, "An existing profile cannot change expiry policy.");
                    if (policy != "Refresh" && policy != "AddStacks") Error(path, "Reject/Replace/Strongest cannot emit Added on an occupied profile without removal.");
                    if (policy == "Refresh" && (stacks != oldStacks || remaining != duration || remaining == oldRemaining))
                        Error(path, "Refresh retains stacks and emits only a real lifetime change.");
                    if (policy == "AddStacks" && (stacks < oldStacks || stacks > oldStacks + 1 ||
                        remaining != duration && remaining != oldRemaining || stacks == oldStacks && remaining == oldRemaining))
                        Error(path, "AddStacks increments at most once or refreshes lifetime at cap; admitted no-op is silent.");
                }
                store[id] = new EffectWitness(stacks, tick + remaining, boundary, Text(item, "event_id")!);
            }
            else
            {
                var reason = Text(payload, "remove_reason");
                if (incumbent is null || afterEffects.ContainsKey(id) || Integer(payload.GetProperty("stacks_after")) != 0)
                    Error(path, "Removed effect must exist before and be absent after, with stacks_after=0.");
                if (reason is "ExpiredBeforeTick" or "ExpiredAfterTick")
                {
                    var boundary = reason == "ExpiredBeforeTick" ? "ExpireBeforeTick" : "ExpireAfterTick";
                    if (incumbent is null || incumbent.Boundary != boundary || tick != incumbent.End - (reason == "ExpiredAfterTick" ? 1 : 0) || source != incumbent.Application)
                        Error(path + "/payload/remove_reason", "Expiry requires the exact boundary and latest successful application as cause.");
                }
                if (reason == "Replaced")
                {
                    var next = index + 1 < _events.Count ? _events[index + 1] : default;
                    if (next.ValueKind != JsonValueKind.Object || Text(next, "event_type") != "EffectAdded" || Text(next, "actor_id") != actor ||
                        Text(next, "source_event_id") != source || Text(next.GetProperty("payload"), "stack_policy") is not ("Replace" or "StrongestWins") ||
                        new[] { "action_id", "decision_id", "resolution_group_id", "target_id" }.Any(field => Text(next, field) != Text(item, field)))
                        Error(path, "Replacement removal must immediately precede the affected actor's Added occupant with shared cause/lineage.");
                }
                if (reason == "BattleEnded") _cleanup = true;
                store.Remove(id);
            }
        }

        private void Control(JsonElement item, BigInteger tick, string path)
        {
            var actor = Text(item, "actor_id"); if (actor is null) return;
            var before = item.GetProperty("before").GetProperty("actor"); var after = item.GetProperty("after").GetProperty("actor");
            if (before.ValueKind != JsonValueKind.Object || after.ValueKind != JsonValueKind.Object) return;
            var payload = item.GetProperty("payload"); var oldState = Text(payload, "old_state"); var newState = Text(payload, "new_state");
            var durationValue = payload.GetProperty("duration_ticks");
            var duration = durationValue.ValueKind == JsonValueKind.Null ? BigInteger.Zero : Integer(durationValue);
            if (oldState != Text(before, "state") || newState != Text(after, "state")) Error(path, "StateChanged payload must agree with before/after states.");
            if (Text(payload, "immunity_result") == "Prevented" && (durationValue.ValueKind == JsonValueKind.Null || duration != 0 || oldState != newState || !JsonElement.DeepEquals(before, after)))
                Error(path, "Prevented control cannot change state/action/timer/effects or extend an immunity interval.");
            if (oldState == "Defeated" && newState != "Defeated") Error(path, "Control cannot resurrect a defeated fighter.");
            var reasons = item.GetProperty("reason_codes").EnumerateArray().Select(x => x.GetString()).ToArray();
            if (newState == "KnockedDown")
            {
                if (oldState != "KnockedDown" || reasons.Contains("Knockdown", StringComparer.Ordinal))
                {
                    if (duration <= 0) Error(path, "New knockdown requires positive total duration.");
                    _knockdown[actor] = new KnockdownWitness(tick + duration, 0);
                }
                else if (reasons.Contains("KnockdownGrounded", StringComparer.Ordinal) || reasons.Contains("KnockdownGetUp", StringComparer.Ordinal))
                {
                    var stage = reasons.Contains("KnockdownGrounded", StringComparer.Ordinal) ? 1 : 2;
                    if (!_knockdown.TryGetValue(actor, out var current) || stage != current.Stage + 1 || duration != current.End - tick || duration <= 0)
                        Error(path, "Knockdown stages advance once in order without changing the shared ready tick.");
                    else _knockdown[actor] = new KnockdownWitness(current.End, stage);
                }
            }
            if (oldState == "KnockedDown" && newState == "DecisionReady")
            {
                if (!_knockdown.TryGetValue(actor, out var current) || tick != current.End || current.Stage != 2)
                    Error(path, "Wakeup follows all stages at the exact public total-duration boundary.");
                _knockdown.Remove(actor);
            }
        }

        private void Pair(JsonElement pair, BigInteger tick, string path)
        { Frame(pair.GetProperty("actor"), tick, path + "/actor"); Frame(pair.GetProperty("target"), tick, path + "/target"); }
        private void Frame(JsonElement frame, BigInteger tick, string path, bool advisory = false)
        {
            if (frame.ValueKind != JsonValueKind.Object || Text(frame, "fighter_id") is not { } id || !_effects.TryGetValue(id, out var store)) return;
            var entries = Entries(frame, path + "/effects", advisory);
            if (entries.Count != store.Count || entries.Any(x => !store.TryGetValue(x.Key, out var witness) ||
                Integer(x.Value.GetProperty("stacks")) != witness.Stacks || Text(x.Value, "expiry_boundary") != witness.Boundary ||
                Integer(x.Value.GetProperty("ticks_remaining")) != BigInteger.Max(0, witness.End - tick)))
                Error(path + "/effects", "Frame membership/stacks/countdown disagree with canonical effect mutations.", advisory);
            if (Text(frame, "state") == "KnockedDown")
            {
                if (frame.GetProperty("action_id").ValueKind != JsonValueKind.Null || frame.GetProperty("action_phase").ValueKind != JsonValueKind.Null)
                    Error(path, "KnockedDown is actionless in every stage.", advisory);
                if (_knockdown.TryGetValue(id, out var knockdown) && Integer(frame.GetProperty("state_ticks_remaining")) != BigInteger.Max(0, knockdown.End - tick))
                    Error(path, "Knockdown total remaining must follow the public ready boundary.", advisory);
            }
        }
        private Dictionary<string, JsonElement> Entries(JsonElement frame, string path, bool advisory = false)
        {
            var entries = new Dictionary<string, JsonElement>(StringComparer.Ordinal); string? previous = null;
            foreach (var entry in frame.GetProperty("effects").EnumerateArray())
            {
                var id = Text(entry, "effect_id")!;
                if (previous is not null && StringComparer.Ordinal.Compare(previous, id) >= 0) Error(path, "Effect entries must be unique and strictly ordinal.", advisory);
                previous = id; entries[id] = entry;
            }
            return entries;
        }
        private void Error(string path, string message, bool advisory = false) => _issues.Add(new ReplayVerificationIssue(
            ReplayVerificationLayer.Semantic, advisory ? ReplayVerificationSeverity.Warning : ReplayVerificationSeverity.Error,
            advisory ? ReplayVerificationCodes.KeyframeMismatch : ReplayVerificationCodes.EffectInvalid, path, message));
    }

    private static bool IsCompatible(string value)
    {
        const string prefix = "battle.core/0.5.";
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var patch = value.AsSpan(prefix.Length);
        if (patch.IsEmpty || patch.Length > 1 && patch[0] == '0') return false;
        foreach (var digit in patch) if (digit is < '0' or > '9') return false;
        return true;
    }
    private static BigInteger Integer(JsonElement value) => BigInteger.Parse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static string? Text(JsonElement item, string name) => item.GetProperty(name).ValueKind == JsonValueKind.Null ? null : item.GetProperty(name).GetString();
    private static void Add(ICollection<ReplayVerificationIssue> issues, string path, string message) => issues.Add(new ReplayVerificationIssue(
        ReplayVerificationLayer.Semantic, ReplayVerificationSeverity.Error, ReplayVerificationCodes.EffectInvalid, path, message));
}
