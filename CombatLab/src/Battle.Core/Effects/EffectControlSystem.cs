using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Effects;

/// <summary>Control and cancellation mutations belong to the enclosing atomic battle preview.</summary>
internal static class EffectControlSystem
{
    internal static bool Allows(BattleState state, FighterId targetId, ControlCategory category)
    {
        if (category == ControlCategory.Defeat) return true;
        var runtime = state.Effects!;
        var target = state.Get(targetId);
        if (runtime.Channel(targetId, EffectModifierTarget.HardControlAllowed) == 0) return false;
        if (category == ControlCategory.Grab && runtime.Channel(targetId, EffectModifierTarget.GrabAllowed) == 0) return false;
        if (category == ControlCategory.Knockdown && runtime.Channel(targetId, EffectModifierTarget.KnockdownAllowed) == 0) return false;
        if (category == ControlCategory.Stun && target.State == FighterState.KnockedDown) return false;
        return !target.ActionId.HasValue || !target.ActionPhase.HasValue ||
            runtime.Interrupt(target.ActionId.Value)?.AllowsControl(target.ActionPhase.Value, category) != false;
    }

    internal static IReadOnlyList<ExternalId> SurvivingIntents(BattleState state, FighterId fighter,
        IEnumerable<Battle.Core.Resolution.ImpactIntent> currentGroup)
    {
        var target = state.Get(fighter);
        if (!target.ActionId.HasValue || state.Effects!.Interrupt(target.ActionId.Value)?.Kind != ActionInterruptKind.UninterruptibleImpact)
            return Array.Empty<ExternalId>();
        var decision = target.ActiveDecisionId ?? throw new EngineInvariantException(EngineFailureCodes.EffectInvalidMutation,
            TickPhase.Resolve.ToString(), "An active interrupt profile requires a committed decision identity.");
        return currentGroup.Where(x => x.ActorId == fighter && x.DecisionId == decision)
            .Select(x => x.IntentId).Distinct().OrderBy(x => x).ToArray();
    }

    internal static EventId? Cancel(BattleState state, CombatEventEmitter emitter, FighterId targetId,
        string reason, EventId? source, ExternalId? group, IReadOnlyList<ExternalId>? surviving = null)
    {
        var fighter = state.Get(targetId);
        var before = fighter.ToFrame();
        var cancelled = fighter.CancelCurrentAction();
        if (!cancelled.HasValue) return source;
        var cause = source ?? emitter.LastEventId ?? throw MissingCause(TickPhase.Resolve);
        return emitter.Emit(state.Tick, new ActionCancelledPayload(new[] { cause }, cancelled.Value.Phase,
            new ReasonCode(reason), surviving ?? Array.Empty<ExternalId>()), targetId,
            actionId: cancelled.Value.ActionId, decisionId: cancelled.Value.DecisionId,
            resolutionGroupId: group, sourceEventId: cause,
            reasonCodes: new[] { new ReasonCode(reason) },
            before: new FramePair(before, null), after: new FramePair(fighter.ToFrame(), null)).EventId;
    }

    internal static EventId Stun(BattleState state, CombatEventEmitter emitter, FighterId targetId,
        FighterId actor, DecisionId decision, StableId action, int duration, int ratio, int fatigue,
        bool allowed, EventId source, ExternalId group, IReadOnlyList<ExternalId> surviving)
    {
        var target = state.Get(targetId);
        if (target.Health == 0 || target.HasControlSource(actor, decision, ControlCategory.Stun)) return source;
        if (!allowed) return Prevented(state, emitter, targetId, action, decision, source, group, "StunPrevented", ratio, fatigue);
        source = Cancel(state, emitter, targetId, "HardControl", source, group, surviving) ?? source;
        var before = target.ToFrame();
        _ = target.BeginStun(state.Tick, duration, actor, decision);
        return Changed(state, emitter, targetId, before, source, action, decision, group,
            duration, ratio, fatigue, "StaggerThreshold");
    }

    internal static EventId Knockdown(BattleState state, CombatEventEmitter emitter, FighterId targetId,
        FighterId actor, DecisionId decision, StableId action, KnockdownTimeline timeline, int ratio, int fatigue,
        bool allowed, EventId source, ExternalId group, IReadOnlyList<ExternalId> surviving)
    {
        var target = state.Get(targetId);
        if (target.Health == 0 || target.HasControlSource(actor, decision, ControlCategory.Knockdown)) return source;
        if (!allowed) return Prevented(state, emitter, targetId, action, decision, source, group, "KnockdownPrevented", ratio, fatigue);
        source = Cancel(state, emitter, targetId, "Knockdown", source, group, surviving) ?? source;
        var before = target.ToFrame();
        _ = target.BeginKnockdown(timeline, actor, decision);
        return Changed(state, emitter, targetId, before, source, action, decision, group,
            timeline.RemainingAt(state.Tick), ratio, fatigue, "Knockdown");
    }

    internal static EventId Prevented(BattleState state, CombatEventEmitter emitter, FighterId target,
        StableId action, DecisionId decision, EventId source, ExternalId group, string reason,
        int? ratio = null, int? fatigue = null)
    {
        var frame = state.Get(target).ToFrame();
        return emitter.Emit(state.Tick, new StateChangedPayload(new[] { source }, frame.State, frame.State,
            0, ratio, fatigue, ImmunityResult.Prevented), target, actionId: action,
            decisionId: decision, resolutionGroupId: group, sourceEventId: source,
            reasonCodes: new[] { new ReasonCode(reason) },
            before: new FramePair(frame, null), after: new FramePair(frame, null)).EventId;
    }

    internal static void Expire(BattleState state, CombatEventEmitter emitter, FighterId fighter)
    {
        var target = state.Get(fighter);
        var before = target.ToFrame();
        var transition = target.AdvanceEffectControlExpiry(state.Tick);
        if (!transition.HasValue) return;
        var source = emitter.LastEventId ?? throw MissingCause(TickPhase.Expiry);
        _ = emitter.Emit(state.Tick, new StateChangedPayload(new[] { source },
            transition.Value.From, transition.Value.To, transition.Value.Duration, null, null, ImmunityResult.NotChecked),
            fighter, sourceEventId: source, reasonCodes: new[] { new ReasonCode(transition.Value.Reason) },
            before: new FramePair(before, null), after: new FramePair(target.ToFrame(), null));
    }

    private static EventId Changed(BattleState state, CombatEventEmitter emitter, FighterId target,
        FighterFrame before, EventId source, StableId action, DecisionId decision, ExternalId group,
        int duration, int ratio, int fatigue, string reason) => emitter.Emit(state.Tick,
            new StateChangedPayload(new[] { source }, before.State, state.Get(target).State,
                duration, ratio, fatigue, ImmunityResult.Allowed), target, actionId: action,
            decisionId: decision, resolutionGroupId: group, sourceEventId: source,
            reasonCodes: new[] { new ReasonCode(reason) }, before: new FramePair(before, null),
            after: new FramePair(state.Get(target).ToFrame(), null)).EventId;

    private static EngineInvariantException MissingCause(TickPhase phase) => new(EngineFailureCodes.EffectInvalidMutation,
        phase.ToString(), "Control/cancellation requires an earlier canonical event.");
}
