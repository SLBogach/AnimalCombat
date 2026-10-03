using System;
using AnimalCombat.ReplayViewer.Contracts;
using Newtonsoft.Json.Linq;

namespace AnimalCombat.ReplayViewer.Presentation
{
    public sealed class EventPresentation
    {
        public EventPresentation(
            string cue,
            string cueClass,
            string detail,
            string value = null,
            string emphasizedFighterId = null,
            string wallSide = null,
            string grabId = null,
            bool startsGrab = false,
            bool endsGrab = false,
            bool isUnknown = false)
        {
            Cue = cue;
            CueClass = cueClass;
            Detail = detail;
            Value = value;
            EmphasizedFighterId = emphasizedFighterId;
            WallSide = wallSide;
            GrabId = grabId;
            StartsGrab = startsGrab;
            EndsGrab = endsGrab;
            IsUnknown = isUnknown;
        }

        public string Cue { get; }
        public string CueClass { get; }
        public string Detail { get; }
        public string Value { get; }
        public string EmphasizedFighterId { get; }
        public string WallSide { get; }
        public string GrabId { get; }
        public bool StartsGrab { get; }
        public bool EndsGrab { get; }
        public bool IsUnknown { get; }
    }

    public static class EventPresentationMapper
    {
        public static EventPresentation Map(ReplayEvent replayEvent)
        {
            if (replayEvent == null)
                throw new ArgumentNullException(nameof(replayEvent));

            JObject payload = replayEvent.Payload ?? new JObject();
            string detail = EventCueFormatter.Format(replayEvent);

            switch (replayEvent.EventType)
            {
                case "BattleStarted":
                    return new EventPresentation("FIGHT", "cue-neutral", detail);
                case "DecisionMade":
                    return new EventPresentation("DECISION", "cue-intent", detail, ActionLabel(replayEvent), replayEvent.ActorId);
                case "ActionCommitted":
                    return new EventPresentation("LOCKED IN", "cue-intent", detail, ActionLabel(replayEvent), replayEvent.ActorId);
                case "AttackPrepared":
                    return new EventPresentation("PREPARE", "cue-prepared", detail, ActionLabel(replayEvent), replayEvent.ActorId);
                case "AttackHit":
                    return new EventPresentation("HIT", "cue-hit", detail, null, replayEvent.TargetId);
                case "AttackMissed":
                    return new EventPresentation("MISS", "cue-miss", detail, Text(payload, "miss_reason"), replayEvent.TargetId);
                case "DamageApplied":
                    return new EventPresentation("DAMAGE", "cue-damage", detail, Negative(Text(payload.SelectToken("breakdown.final"))), replayEvent.TargetId);
                case "StateChanged":
                    return new EventPresentation(Upper(Text(payload, "new_state"), "STATE"), "cue-state", detail, null, replayEvent.ActorId);
                case "FighterDefeated":
                    return new EventPresentation("KO", "cue-defeat", detail, null, TextOr(payload, "defeated_fighter_id", replayEvent.ActorId));
                case "PositionChanged":
                    return new EventPresentation("MOVE", "cue-move", detail, Text(payload, "movement_kind"), replayEvent.ActorId);
                case "KnockbackApplied":
                    return new EventPresentation("KNOCKBACK", "cue-move", detail, Distance(payload), replayEvent.TargetId);
                case "GrabStarted":
                    return new EventPresentation(
                        "GRAB",
                        "cue-grab",
                        detail,
                        null,
                        TextOr(payload, "grabbed_id", replayEvent.TargetId),
                        grabId: Text(payload, "grab_id"),
                        startsGrab: true);
                case "GrabEnded":
                    return new EventPresentation(
                        Is(payload, "end_reason", "Throw") ? "THROW" : "RELEASE",
                        "cue-throw",
                        detail,
                        ActionLabel(replayEvent),
                        TextOr(payload, "grabbed_id", replayEvent.TargetId),
                        grabId: Text(payload, "grab_id"),
                        endsGrab: true);
                case "WallImpact":
                    return new EventPresentation(
                        "WALL IMPACT",
                        "cue-wall",
                        detail,
                        Negative(Text(payload, "wall_damage")),
                        replayEvent.TargetId,
                        Text(payload, "wall_side"));
                case "ResourceChanged":
                    return new EventPresentation(
                        Upper(Text(payload, "resource_kind"), "RESOURCE"),
                        "cue-state",
                        detail,
                        Signed(Text(payload, "delta")),
                        replayEvent.ActorId);
                case "DrawDeclared":
                    return new EventPresentation(
                        Is(payload, "draw_reason", "DoubleKO") ? "DOUBLE KO" : "DRAW",
                        "cue-defeat",
                        detail);
                case "BattleEnded":
                    return new EventPresentation("BATTLE ENDED", "cue-result", detail);
                default:
                    return new EventPresentation(
                        "UNKNOWN EVENT",
                        "cue-unknown",
                        detail,
                        replayEvent.EventType,
                        replayEvent.ActorId,
                        isUnknown: true);
            }
        }

        static string Distance(JObject payload)
        {
            string actual = Text(payload, "actual_move");
            return actual == "—" ? null : $"{actual} units";
        }

        static string ActionLabel(ReplayEvent replayEvent)
        {
            string value = replayEvent.ActionId;
            if (string.IsNullOrWhiteSpace(value))
                value = Text(replayEvent.Payload, "chosen_action_id");
            if (string.IsNullOrWhiteSpace(value) || value == "—")
                return null;

            int underscore = value.LastIndexOf('_');
            string shortValue = underscore >= 0 && underscore + 1 < value.Length
                ? value.Substring(underscore + 1)
                : value;
            return shortValue.Replace('_', ' ').ToUpperInvariant();
        }

        static string Negative(string value)
        {
            return string.IsNullOrEmpty(value) || value == "—" ? null : $"−{value.TrimStart('-', '−')}";
        }

        static string Signed(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "—")
                return null;
            return value[0] == '-' || value[0] == '−' ? value : $"+{value}";
        }

        static string Upper(string value, string fallback)
        {
            return string.IsNullOrEmpty(value) || value == "—" ? fallback : value.Replace('_', ' ').ToUpperInvariant();
        }

        static bool Is(JObject payload, string name, string expected)
        {
            return string.Equals(Text(payload, name), expected, StringComparison.OrdinalIgnoreCase);
        }

        static string TextOr(JObject payload, string name, string fallback)
        {
            string value = Text(payload, name);
            return value == "—" ? fallback : value;
        }

        static string Text(JObject payload, string name)
        {
            return Text(payload?[name]);
        }

        static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "—";
            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }
    }
}
