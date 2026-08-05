using Battle.Core.Decisions;
using Battle.Core.Initialization;
using Battle.Core.Outcome;
using Battle.Core.Safety;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.Engine;

internal sealed class TickCoordinator
{
    private readonly ITickCoordinatorObserver _observer;
    private readonly ISystemActionAvailability _systemActionAvailability;
    private readonly ZeroProgressWatchdog _watchdog;

    internal TickCoordinator(
        int maximumZeroProgressTicks,
        ITickCoordinatorObserver? observer = null,
        ISystemActionAvailability? systemActionAvailability = null)
    {
        _watchdog = new ZeroProgressWatchdog(maximumZeroProgressTicks);
        _observer = observer ?? NullTickCoordinatorObserver.Instance;
        _systemActionAvailability = systemActionAvailability ??
            Wp06SystemActionAvailability.Instance;
    }

    internal int ZeroProgressCounter => _watchdog.Counter;

    internal ImmediateOutcome? RunActiveTick(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (emitter is null)
        {
            throw new ArgumentNullException(nameof(emitter));
        }

        state.EnsureMutable();
        if (state.Tick >= settings.TimeLimitTicks)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.TickLimitExceeded,
                "TickBoundary",
                $"Tick {state.Tick} is outside active range 0..{settings.TimeLimitTicks - 1}.");
        }

        var beforeTick = ProgressStamp.Capture(state);
        TickSnapshot? snapshot = null;
        ImmediateOutcome? immediateOutcome = null;

        Observe(state, TickPhase.Snapshot);
        snapshot = state.CreateSnapshot();

        Observe(state, TickPhase.Expiry);
        Observe(state, TickPhase.Resource);

        Observe(state, TickPhase.ActionPhaseEnd);
        state.FighterA.AdvanceActionLifecycle();
        state.FighterB.AdvanceActionLifecycle();

        Observe(state, TickPhase.Decisions);
        RunDecisions(state, snapshot, settings.SystemWait, emitter);

        Observe(state, TickPhase.VoluntaryMovement);
        Observe(state, TickPhase.CollectIntents);
        Observe(state, TickPhase.SortIntents);
        Observe(state, TickPhase.Resolve);
        Observe(state, TickPhase.WallsAndGrabs);

        Observe(state, TickPhase.Outcome);
        immediateOutcome = ImmediateOutcomeResolver.Resolve(
            state.FighterA.Health,
            state.FighterB.Health);
        if (immediateOutcome.HasValue)
        {
            state.RecordOutcome(
                immediateOutcome.Value.Outcome,
                immediateOutcome.Value.WinnerFighterId,
                immediateOutcome.Value.EndReason);
        }

        Observe(state, TickPhase.EndTick);
        if (!immediateOutcome.HasValue)
        {
            _watchdog.Observe(beforeTick, ProgressStamp.Capture(state));
            state.AdvanceTick();
        }

        return immediateOutcome;
    }

    private void RunDecisions(
        BattleState state,
        TickSnapshot snapshot,
        SystemActionDefinition systemWait,
        CombatEventEmitter emitter)
    {
        var causalRoot = emitter.LastEventId;
        var intents = new List<DecisionIntent>(2);
        CreateIntent(FighterId.FighterA);
        CreateIntent(FighterId.FighterB);

        foreach (var intent in intents)
        {
            var actorFrame = snapshot.Get(intent.ActorId);
            var targetFrame = snapshot.GetOpponent(intent.ActorId);
            var frames = new FramePair(actorFrame, targetFrame);
            var payload = new DecisionMadePayload(
                causalRoot.HasValue ? new[] { causalRoot.Value } : Array.Empty<EventId>(),
                intent.Selection.ActionId,
                new[] { intent.Selection.ActionId },
                1,
                intent.Selection.ChosenWeight,
                intent.Selection.WeightSum,
                intent.Selection.SelectionMode,
                Array.Empty<ModifierTrace>());
            intent.DecisionEvent = emitter.Emit(
                state.Tick,
                payload,
                intent.ActorId,
                intent.TargetId,
                actionId: intent.Selection.ActionId,
                decisionId: intent.DecisionId,
                sourceEventId: causalRoot,
                reasonCodes: new[] { intent.Selection.ReasonCode },
                before: frames,
                after: frames);
        }

        foreach (var intent in intents)
        {
            var actor = state.Get(intent.ActorId);
            var target = state.Get(intent.TargetId);
            var before = new FramePair(actor.ToFrame(), target.ToFrame());
            actor.CommitSystemWait(intent.Selection.ActionId, systemWait.ActiveTicks);
            var after = new FramePair(actor.ToFrame(), target.ToFrame());
            var payload = new ActionCommittedPayload(
                new[] { intent.DecisionEvent.EventId },
                intent.TargetId,
                systemWait.EnergyCost,
                systemWait.ResourceCost,
                systemWait.StartupTicks,
                systemWait.ActiveTicks,
                systemWait.RecoveryTicks,
                systemWait.CooldownTicks,
                CommitDirection.None,
                target.Position);
            _ = emitter.Emit(
                state.Tick,
                payload,
                intent.ActorId,
                intent.TargetId,
                actionId: intent.Selection.ActionId,
                decisionId: intent.DecisionId,
                sourceEventId: intent.DecisionEvent.EventId,
                reasonCodes: new[] { new ReasonCode("ActionSelected") },
                before: before,
                after: after);
        }

        void CreateIntent(FighterId fighterId)
        {
            var actor = state.Get(fighterId);
            if (!actor.IsDecisionReady)
            {
                return;
            }

            _observer.OnDecisionSnapshot(fighterId, snapshot);
            var selection = SystemActionSelector.Select(
                _systemActionAvailability.GetLegalCandidates(
                    state,
                    snapshot,
                    fighterId,
                    systemWait));
            intents.Add(new DecisionIntent(
                fighterId,
                fighterId == FighterId.FighterA ? FighterId.FighterB : FighterId.FighterA,
                actor.NextDecisionId(),
                selection));
        }
    }

    private void Observe(BattleState state, TickPhase phase) =>
        _observer.OnPhase(state, phase);

    private sealed class DecisionIntent
    {
        internal DecisionIntent(
            FighterId actorId,
            FighterId targetId,
            DecisionId decisionId,
            SystemActionSelection selection)
        {
            ActorId = actorId;
            TargetId = targetId;
            DecisionId = decisionId;
            Selection = selection;
        }

        internal FighterId ActorId { get; }

        internal FighterId TargetId { get; }

        internal DecisionId DecisionId { get; }

        internal SystemActionSelection Selection { get; }

        internal CombatEventIdentity DecisionEvent { get; set; }
    }
}
