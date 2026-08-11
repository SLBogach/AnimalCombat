using Battle.Core.Decisions;
using Battle.Core.Initialization;
using Battle.Core.Movement;
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
            Wp07SystemActionAvailability.Instance;
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
        RunActionLifecycle(state, settings, emitter);

        Observe(state, TickPhase.Decisions);
        RunDecisions(state, snapshot, settings, emitter);

        Observe(state, TickPhase.VoluntaryMovement);
        RunVoluntaryMovement(state, settings, emitter);
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
        RuntimeBattleSettings settings,
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
            var action = settings.GetSystemAction(intent.Selection.ActionId);
            var direction = ResolveCommitDirection(actor.Position, target.Position, action.MovementMode);
            actor.CommitSystemAction(action, intent.DecisionId, direction, target.Position);
            var after = new FramePair(actor.ToFrame(), target.ToFrame());
            var payload = new ActionCommittedPayload(
                new[] { intent.DecisionEvent.EventId },
                intent.TargetId,
                action.EnergyCost,
                action.ResourceCost,
                action.StartupTicks,
                action.ActiveTicks,
                action.RecoveryTicks,
                action.CooldownTicks,
                direction,
                target.Position);
            var committed = emitter.Emit(
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
            actor.RecordActionEvent(committed.EventId);
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
                    settings));
            intents.Add(new DecisionIntent(
                fighterId,
                fighterId == FighterId.FighterA ? FighterId.FighterB : FighterId.FighterA,
                actor.NextDecisionId(),
                selection));
        }
    }

    private static void RunActionLifecycle(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter)
    {
        foreach (var fighterId in settings.InitiativeOrder)
        {
            var fighter = state.Get(fighterId);
            var before = fighter.ToFrame();
            var transition = fighter.AdvanceMovementLifecycle();
            if (!transition.HasValue)
            {
                continue;
            }

            var value = transition.Value;
            var related = value.SourceEventId.HasValue
                ? new[] { value.SourceEventId.Value }
                : Array.Empty<EventId>();
            var changed = emitter.Emit(
                state.Tick,
                new ActionPhaseChangedPayload(
                    related,
                    value.FromPhase,
                    value.ToPhase,
                    value.PhaseTicks),
                actorId: fighterId,
                actionId: value.ActionId,
                decisionId: value.DecisionId,
                sourceEventId: value.SourceEventId,
                reasonCodes: new[] { value.ReasonCode },
                before: new FramePair(before, null),
                after: new FramePair(fighter.ToFrame(), null));
            fighter.RecordActionEvent(changed.EventId);
        }
    }

    private static void RunVoluntaryMovement(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter)
    {
        var active = settings.InitiativeOrder
            .Select(state.Get)
            .Where(fighter => fighter.IsActiveMovement)
            .ToArray();
        if (active.Length == 0)
        {
            return;
        }

        var mode = active[0].ActiveSystemAction!.MovementMode;
        if (active.Any(fighter => fighter.ActiveSystemAction!.MovementMode != mode))
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.VoluntaryMovement.ToString(),
                "Mixed Approach and Retreat segments cannot share one movement batch.");
        }

        MovementPairResult result;
        try
        {
            var pairMode = mode == SystemMovementMode.Approach
                ? GapMovementMode.Approach
                : GapMovementMode.Retreat;
            var targetGap = pairMode == GapMovementMode.Approach
                ? settings.SystemRetreat.PreferredRangeMinimum
                : settings.SystemApproach.PreferredRangeMaximum;
            result = MovementPairResolver.Resolve(
                new ArenaInterval(settings.Arena.MinimumPosition, settings.Arena.MaximumPosition),
                pairMode,
                targetGap,
                Participant(state.FighterA),
                Participant(state.FighterB),
                settings.InitiativeOrder);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.VoluntaryMovement.ToString(),
                exception.Message);
        }

        foreach (var fighter in active)
        {
            if (fighter.MovementStarted)
            {
                continue;
            }

            var source = fighter.LastActionEventId;
            var related = source.HasValue ? new[] { source.Value } : Array.Empty<EventId>();
            var frame = fighter.ToFrame();
            var started = emitter.Emit(
                state.Tick,
                new MoveStartedPayload(
                    related,
                    fighter.Position,
                    ToMovementDirection(fighter.CommitDirection),
                    fighter.MoveSpeed,
                    mode == SystemMovementMode.Approach
                        ? MoveStartKind.Approach
                        : MoveStartKind.Retreat,
                    new[]
                    {
                        new ReasonCode("WallReached"),
                        new ReasonCode("PreferredRangeReached"),
                        new ReasonCode("SegmentExpired"),
                    }),
                actorId: fighter.FighterId,
                actionId: fighter.ActionId,
                decisionId: fighter.ActiveDecisionId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode("MovementStarted") },
                before: new FramePair(frame, null),
                after: new FramePair(frame, null));
            fighter.MarkMovementStarted(started.EventId);
        }

        EmitResolvedPositionChanges(state, emitter, result, active);

        foreach (var fighter in active)
        {
            var actorResult = ActorResult(result, fighter.FighterId);
            ReasonCode? stopReason = null;
            if (actorResult.WasWallClipped)
            {
                stopReason = new ReasonCode("WallReached");
            }
            else if (result.TargetBandReached)
            {
                stopReason = new ReasonCode("PreferredRangeReached");
            }
            else if (fighter.StateTicksRemaining == 1)
            {
                stopReason = new ReasonCode("SegmentExpired");
            }

            if (!stopReason.HasValue)
            {
                continue;
            }

            var source = fighter.LastActionEventId;
            var related = source.HasValue ? new[] { source.Value } : Array.Empty<EventId>();
            var frame = fighter.ToFrame();
            var ended = emitter.Emit(
                state.Tick,
                new MoveEndedPayload(
                    related,
                    fighter.MovementStartPosition!.Value,
                    fighter.Position,
                    stopReason.Value),
                actorId: fighter.FighterId,
                actionId: fighter.ActionId,
                decisionId: fighter.ActiveDecisionId,
                sourceEventId: source,
                reasonCodes: new[] { stopReason.Value },
                before: new FramePair(frame, null),
                after: new FramePair(frame, null));
            fighter.CompleteMovement(ended.EventId);
        }

        MovementParticipant Participant(FighterRuntimeState fighter) => new(
            fighter.FighterId,
            fighter.Position,
            fighter.CollisionRadius,
            fighter.IsActiveMovement ? fighter.FrozenMoveSpeed ?? fighter.MoveSpeed : 0,
            fighter.IsActiveMovement);
    }

    internal static void EmitResolvedPositionChanges(
        BattleState state,
        CombatEventEmitter emitter,
        MovementPairResult result,
        IReadOnlyList<FighterRuntimeState> active)
    {
        var voluntaryEvents = new Dictionary<FighterId, EventId>();
        foreach (var fighter in active)
        {
            var actorResult = ActorResult(result, fighter.FighterId);
            var before = fighter.ToFrame();
            fighter.ApplyPosition(actorResult.ProvisionalPosition);
            var source = fighter.MoveStartedEventId!.Value;
            var changed = emitter.Emit(
                state.Tick,
                new PositionChangedPayload(
                    new[] { source },
                    actorResult.FromPosition,
                    actorResult.ProvisionalPosition,
                    actorResult.RequestedDelta,
                    actorResult.VoluntaryActualDelta,
                    actorResult.BlockedByWall,
                    PositionChangeKind.Voluntary),
                actorId: fighter.FighterId,
                actionId: fighter.ActionId,
                decisionId: fighter.ActiveDecisionId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode("VoluntaryMovement") },
                before: new FramePair(before, null),
                after: new FramePair(fighter.ToFrame(), null));
            voluntaryEvents.Add(fighter.FighterId, changed.EventId);
            fighter.RecordActionEvent(changed.EventId);
        }

        var relatedMovementEvents = voluntaryEvents.Values.OrderBy(id => id).ToArray();
        foreach (var fighter in active)
        {
            var actorResult = ActorResult(result, fighter.FighterId);
            if (actorResult.SeparationDelta == 0)
            {
                continue;
            }

            var before = fighter.ToFrame();
            fighter.ApplyPosition(actorResult.FinalPosition);
            var source = voluntaryEvents[fighter.FighterId];
            var separated = emitter.Emit(
                state.Tick,
                new PositionChangedPayload(
                    relatedMovementEvents,
                    actorResult.ProvisionalPosition,
                    actorResult.FinalPosition,
                    actorResult.SeparationDelta,
                    actorResult.SeparationDelta,
                    0,
                    PositionChangeKind.Separation),
                actorId: fighter.FighterId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode("SeparationCorrection") },
                before: new FramePair(before, null),
                after: new FramePair(fighter.ToFrame(), null));
            fighter.RecordActionEvent(separated.EventId);
        }

        state.FighterA.ApplyPosition(result.Left.FinalPosition);
        state.FighterB.ApplyPosition(result.Right.FinalPosition);
        state.FighterA.SetFacing(result.Left.Facing);
        state.FighterB.SetFacing(result.Right.Facing);
    }

    private static ResolvedMovementActor ActorResult(
        MovementPairResult result,
        FighterId fighterId) =>
        result.Left.FighterId == fighterId ? result.Left : result.Right.FighterId == fighterId
            ? result.Right
            : throw new ArgumentOutOfRangeException(nameof(fighterId));

    private static CommitDirection ResolveCommitDirection(
        int actorPosition,
        int targetPosition,
        SystemMovementMode movementMode)
    {
        if (movementMode == SystemMovementMode.None)
        {
            return CommitDirection.None;
        }

        if (actorPosition == targetPosition)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                "A movement direction cannot be derived from equal positions.");
        }

        var targetIsRight = targetPosition > actorPosition;
        return movementMode == SystemMovementMode.Approach
            ? targetIsRight ? CommitDirection.Right : CommitDirection.Left
            : targetIsRight ? CommitDirection.Left : CommitDirection.Right;
    }

    private static MovementDirection ToMovementDirection(CommitDirection direction) => direction switch
    {
        CommitDirection.Left => MovementDirection.Left,
        CommitDirection.Right => MovementDirection.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

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
