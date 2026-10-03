using AnimalCombat.ReplayViewer.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimalCombat.ReplayViewer.Presentation
{
    static class EventCueFormatter
    {
        public static string Format(ReplayEvent replayEvent)
        {
            JObject payload = replayEvent.Payload;
            switch (replayEvent.EventType)
            {
                case "BattleStarted":
                    return "Battle initialized from authoritative initial_frames";
                case "DecisionMade":
                    return $"{Actor(replayEvent)} chose {Text(payload, "chosen_action_id")} · {Text(payload, "selection_mode")}";
                case "ActionCommitted":
                    return $"{Actor(replayEvent)} committed {Action(replayEvent)} · direction {Text(payload, "commit_direction")}";
                case "AttackPrepared":
                    return $"{Actor(replayEvent)} prepared {Action(replayEvent)} · impact ticks {Compact(payload["impact_ticks"])}";
                case "AttackHit":
                    return $"Hit {Target(replayEvent)} · gap {Text(payload, "gap")} · direction {Text(payload, "hit_direction")}";
                case "AttackMissed":
                    return $"Missed {Target(replayEvent)} · {Text(payload, "miss_reason")}";
                case "DamageApplied":
                    return $"{Target(replayEvent)} HP {Text(payload, "hp_before")} → {Text(payload, "hp_after")} · recorded damage {Text(payload.SelectToken("breakdown.final"))}";
                case "StateChanged":
                    return $"{Actor(replayEvent)} state {Text(payload, "old_state")} → {Text(payload, "new_state")}";
                case "FighterDefeated":
                    return $"{Text(payload, "defeated_fighter_id")} defeated · final HP {Text(payload, "final_health")}";
                case "PositionChanged":
                    return $"{Actor(replayEvent)} {Text(payload, "movement_kind")} · {Text(payload, "from_position")} → {Text(payload, "to_position")}";
                case "KnockbackApplied":
                    return $"Forced movement {Target(replayEvent)} · {Text(payload, "from_position")} → {Text(payload, "to_position")}";
                case "GrabStarted":
                    return $"Grab {Text(payload, "grab_id")} · {Text(payload, "grabber_id")} grabbed {Text(payload, "grabbed_id")}";
                case "GrabEnded":
                    return $"Grab ended · {Text(payload, "end_reason")} · throw {Text(payload, "throw_action_id")}";
                case "WallImpact":
                    return $"Wall impact {Text(payload, "wall_side")} · damage {Text(payload, "wall_damage")} · stagger {Text(payload, "wall_stagger")}";
                case "ResourceChanged":
                    return $"{Actor(replayEvent)} {Text(payload, "resource_kind")} · {Text(payload, "before")} → {Text(payload, "after")}";
                case "DrawDeclared":
                    return $"Draw declared · {Text(payload, "draw_reason")}";
                case "BattleEnded":
                    return $"Battle ended · {Text(payload, "outcome")} · {Text(payload, "end_reason")}";
                default:
                    return $"Unknown event '{replayEvent.EventType}' preserved and skipped safely";
            }
        }

        static string Actor(ReplayEvent replayEvent) => string.IsNullOrEmpty(replayEvent.ActorId) ? "—" : replayEvent.ActorId;
        static string Target(ReplayEvent replayEvent) => string.IsNullOrEmpty(replayEvent.TargetId) ? "—" : replayEvent.TargetId;
        static string Action(ReplayEvent replayEvent) => string.IsNullOrEmpty(replayEvent.ActionId) ? "—" : replayEvent.ActionId;

        static string Text(JObject payload, string name) => Text(payload[name]);

        static string Text(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "—";
            return token.Type == JTokenType.String ? token.Value<string>() : Compact(token);
        }

        static string Compact(JToken token)
        {
            return token == null || token.Type == JTokenType.Null
                ? "—"
                : token.ToString(Formatting.None);
        }
    }
}
