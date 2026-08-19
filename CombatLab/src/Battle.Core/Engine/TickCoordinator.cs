using Battle.Core.Decisions;
using Battle.Core.Initialization;
using Battle.Core.Movement;
using Battle.Core.Outcome;
using Battle.Core.Random;
using Battle.Core.Safety;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Versions;

namespace Battle.Core.Engine;

internal sealed class TickCoordinator
{
    private readonly ITickCoordinatorObserver _observer;
    private readonly ISystemActionAvailability _systemActionAvailability;
    private readonly ICombatDecisionDiagnostics? _decisionDiagnostics;
    private readonly ZeroProgressWatchdog _watchdog;

    internal TickCoordinator(
        int maximumZeroProgressTicks,
        ITickCoordinatorObserver? observer = null,
        ISystemActionAvailability? systemActionAvailability = null,
        ICombatDecisionDiagnostics? decisionDiagnostics = null)
    {
        _watchdog = new ZeroProgressWatchdog(maximumZeroProgressTicks);
        _observer = observer ?? NullTickCoordinatorObserver.Instance;
        _systemActionAvailability = systemActionAvailability ??
            Wp07SystemActionAvailability.Instance;
        _decisionDiagnostics = decisionDiagnostics;
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
        state.FighterA.DecrementCooldowns();
        state.FighterB.DecrementCooldowns();

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
        TickSnapshot phaseOneSnapshot,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter)
    {
        var frameA = state.FighterA.ToFrame();
        var frameB = state.FighterB.ToFrame();
        var decisionSnapshot = CaptureDecisionSnapshot(state, settings);
        var causalRoot = emitter.LastEventId;
        var intents = new List<DecisionIntent>(2);
        CreateIntent(FighterId.FighterA);
        CreateIntent(FighterId.FighterB);
        if (intents.Count == 0)
        {
            return;
        }

        var decisionNextIndex = state.Rng.Decision.NextDrawIndex;
        var preview = state.Rng.Decision.CreatePreview();
        var drawSource = new PreviewDecisionDrawSource(preview);
        foreach (var intent in intents)
        {
            intent.Selection = DecisionSelector.Select(
                intent.Candidates,
                decisionSnapshot.Get(intent.ActorId).Emergency,
                drawSource);
            intent.FrozenCommit = FreezeCommit(
                intent,
                decisionSnapshot,
                settings,
                state.Tick);
        }

        var batchEventCount = intents.Sum(intent => intent.FrozenCommit.RequiredEventCount);
        emitter.PreflightNonterminalBatch(batchEventCount);
        state.Rng.Decision.CommitPreview(preview);
        foreach (var intent in intents)
        {
            state.Get(intent.ActorId).CommitDecisionId(intent.DecisionId);
        }

        Sha256Digest? snapshotDigest = null;
        if (_decisionDiagnostics?.IsEnabled == true)
        {
            snapshotDigest = _decisionDiagnostics.ComputeSnapshotDigest(
                CreateDiagnosticProjection(state, settings, frameA, frameB, decisionNextIndex));
        }

        foreach (var intent in intents)
        {
            var actorFrame = intent.ActorId == FighterId.FighterA ? frameA : frameB;
            var targetFrame = intent.FrozenCommit.TargetId switch
            {
                FighterId.FighterA => frameA,
                FighterId.FighterB => frameB,
                _ => null,
            };
            var frames = new FramePair(actorFrame, targetFrame);
            var chosen = intent.Candidates.Single(candidate =>
                candidate.ActionId == intent.Selection!.ActionId);
            var dominant = DominantModifiers(chosen, settings.FixedPointScale);
            var payload = new DecisionMadePayload(
                causalRoot.HasValue ? new[] { causalRoot.Value } : Array.Empty<EventId>(),
                intent.Selection!.ActionId,
                intent.Selection.LegalActionIds,
                intent.Selection.LegalActionIds.Count,
                intent.Selection.ChosenWeight,
                intent.Selection.WeightSum,
                intent.Selection.SelectionMode,
                dominant);
            var reasonCodes = new[] { intent.Selection.ReasonCode }
                .Concat(dominant.Select(item => item.Code))
                .ToArray();
            intent.DecisionEvent = emitter.Emit(
                state.Tick,
                payload,
                intent.ActorId,
                intent.FrozenCommit.TargetId,
                actionId: intent.Selection.ActionId,
                decisionId: intent.DecisionId,
                sourceEventId: causalRoot,
                reasonCodes: reasonCodes,
                rng: intent.Selection.Rng,
                before: frames,
                after: frames);

            if (snapshotDigest.HasValue)
            {
                _decisionDiagnostics!.AppendDecisionTrace(new DecisionTrace(
                    intent.DecisionId,
                    state.Tick,
                    intent.DecisionEvent.Sequence,
                    intent.ActorId,
                    snapshotDigest.Value,
                    intent.Candidates.Select(ToDiagnosticCandidate)));
            }
        }

        foreach (var intent in intents)
        {
            var actor = state.Get(intent.ActorId);
            var target = intent.FrozenCommit.TargetId.HasValue
                ? state.Get(intent.FrozenCommit.TargetId.Value)
                : null;
            var before = new FramePair(actor.ToFrame(), target?.ToFrame());
            if (intent.FrozenCommit.SystemAction is not null)
            {
                actor.CommitSystemAction(
                    intent.FrozenCommit.SystemAction,
                    intent.DecisionId,
                    intent.FrozenCommit.Direction,
                    intent.FrozenCommit.TargetPositionAtCommit!.Value);
            }
            else
            {
                actor.CommitCombatAction(intent.FrozenCommit.CombatAction!);
            }

            actor.RecordCommittedHistory(intent.FrozenCommit.ActionId, intent.FrozenCommit.Category);
            actor.UpdateOpportunityDebts(
                settings.Decisions.GetFighter(intent.ActorId).BuildView.SpecialActionIds,
                intent.Candidates
                    .Where(candidate => candidate.Legal && candidate.Slot == DecisionActionSlot.Special)
                    .Select(candidate => candidate.ActionId),
                intent.FrozenCommit.ActionId);
            var after = new FramePair(actor.ToFrame(), target?.ToFrame());
            var payload = new ActionCommittedPayload(
                new[] { intent.DecisionEvent.EventId },
                intent.FrozenCommit.TargetId,
                intent.FrozenCommit.EnergyCost,
                intent.FrozenCommit.ResourceCost,
                intent.FrozenCommit.StartupTicks,
                intent.FrozenCommit.ActiveTicks,
                intent.FrozenCommit.RecoveryTicks,
                intent.FrozenCommit.CooldownTicks,
                intent.FrozenCommit.Direction,
                intent.FrozenCommit.TargetPositionAtCommit);
            var committed = emitter.Emit(
                state.Tick,
                payload,
                intent.ActorId,
                intent.FrozenCommit.TargetId,
                actionId: intent.FrozenCommit.ActionId,
                decisionId: intent.DecisionId,
                sourceEventId: intent.DecisionEvent.EventId,
                reasonCodes: new[] { new ReasonCode("ActionSelected") },
                before: before,
                after: after);
            intent.CommitEvent = committed;
            if (intent.FrozenCommit.CombatAction is null)
            {
                actor.RecordActionEvent(committed.EventId);
            }
            else
            {
                actor.RecordCombatCommit(committed.EventId);
            }
        }

        foreach (var intent in intents)
        {
            EmitCost(intent, energy: true);
            EmitCost(intent, energy: false);
        }

        foreach (var intent in intents.Where(item => item.FrozenCommit.HasTelegraph))
        {
            var actor = state.Get(intent.ActorId);
            var target = intent.FrozenCommit.TargetId.HasValue
                ? state.Get(intent.FrozenCommit.TargetId.Value)
                : null;
            var frames = new FramePair(actor.ToFrame(), target?.ToFrame());
            _ = emitter.Emit(
                state.Tick,
                new AttackPreparedPayload(
                    new[] { intent.CommitEvent.EventId },
                    state.Tick,
                    intent.FrozenCommit.AbsoluteImpactTicks,
                    directionLocked: true,
                    intent.FrozenCommit.TargetId),
                intent.ActorId,
                intent.FrozenCommit.TargetId,
                actionId: intent.FrozenCommit.ActionId,
                decisionId: intent.DecisionId,
                sourceEventId: intent.CommitEvent.EventId,
                reasonCodes: new[] { new ReasonCode("AttackPrepared") },
                before: frames,
                after: frames);
        }

        void CreateIntent(FighterId fighterId)
        {
            var actor = state.Get(fighterId);
            if (!actor.IsDecisionReady)
            {
                return;
            }

            _observer.OnDecisionSnapshot(fighterId, phaseOneSnapshot);
            if (_systemActionAvailability.GetLegalCandidates(
                    state,
                    phaseOneSnapshot,
                    fighterId,
                    settings).Count == 0)
            {
                throw new EngineInvariantException(
                    EngineFailureCodes.NoLegalSystemAction,
                    TickPhase.Decisions.ToString(),
                    "System availability must retain one fail-closed action.");
            }

            intents.Add(new DecisionIntent(
                fighterId,
                actor.PeekNextDecisionId(),
                DecisionEvaluator.Evaluate(decisionSnapshot, fighterId, settings.Decisions)));
        }

        void EmitCost(DecisionIntent intent, bool energy)
        {
            var actor = state.Get(intent.ActorId);
            var before = actor.ToFrame();
            var mutation = energy
                ? actor.ApplyEnergyCost(intent.FrozenCommit.EnergyCost)
                : actor.ApplyUniqueResourceCost(intent.FrozenCommit.ResourceCost);
            if (!mutation.HasValue)
            {
                return;
            }

            var value = mutation.Value;
            _ = emitter.Emit(
                state.Tick,
                new ResourceChangedPayload(
                    new[] { intent.CommitEvent.EventId },
                    value.Kind,
                    value.ResourceId,
                    value.Before,
                    value.Delta,
                    value.After,
                    value.Minimum,
                    value.Maximum,
                    null),
                intent.ActorId,
                actionId: intent.FrozenCommit.ActionId,
                decisionId: intent.DecisionId,
                sourceEventId: intent.CommitEvent.EventId,
                reasonCodes: new[] { new ReasonCode("ActionCost") },
                before: new FramePair(before, null),
                after: new FramePair(actor.ToFrame(), null));
        }
    }

    private static DecisionBatchSnapshot CaptureDecisionSnapshot(
        BattleState state,
        RuntimeBattleSettings settings)
    {
        return new DecisionBatchSnapshot(
            state.Tick,
            state.Tick,
            CaptureFighter(FighterId.FighterA),
            CaptureFighter(FighterId.FighterB),
            settings.InitiativeOrder);

        DecisionFighterView CaptureFighter(FighterId fighterId)
        {
            var fighter = state.Get(fighterId);
            var profile = settings.Decisions.GetFighter(fighterId);
            var telegraph = fighter.ObservableActionId.HasValue
                ? new DecisionTelegraphView(
                    fighter.ObservableActionId.Value,
                    fighter.ObservableCommitTick!.Value)
                : null;
            return new DecisionFighterView(
                fighterId,
                profile.BuildView,
                fighter.Position,
                fighter.Facing,
                fighter.State,
                fighter.ActionId,
                fighter.CollisionRadius,
                fighter.Health,
                fighter.MaximumHealth,
                fighter.Energy,
                fighter.MaximumEnergy,
                fighter.ResourceId,
                fighter.Resource,
                fighter.MaximumResource,
                fighter.ActionSpeed,
                profile.Tactic.PerceptionDelayTicks,
                fighter.Cooldowns,
                new DecisionRepeatHistory(
                    fighter.LastCommittedActionId,
                    fighter.LastCommittedCategory,
                    fighter.SameActionStreak,
                    fighter.SameCategoryStreak),
                fighter.OpportunityDebts,
                telegraph,
                fighter.Emergency);
        }
    }

    private static FrozenCommitDescriptor FreezeCommit(
        DecisionIntent intent,
        DecisionBatchSnapshot snapshot,
        RuntimeBattleSettings settings,
        int tick)
    {
        var action = settings.Decisions.GetAction(intent.Selection!.ActionId);
        var actor = snapshot.Get(intent.ActorId);
        var opponentId = intent.ActorId == FighterId.FighterA
            ? FighterId.FighterB
            : FighterId.FighterA;
        var opponent = snapshot.Get(opponentId);
        var targetId = action.TargetKind == DecisionTargetKind.Opponent
            ? opponentId
            : (FighterId?)null;
        var targetPosition = targetId.HasValue ? opponent.Position : (int?)null;

        if (action.Slot == DecisionActionSlot.System)
        {
            var system = settings.GetSystemAction(action.Id);
            var systemDirection = ResolveCommitDirection(
                actor.Position,
                opponent.Position,
                system.MovementMode);
            return FrozenCommitDescriptor.System(
                action,
                system,
                targetId!.Value,
                targetPosition!.Value,
                systemDirection);
        }

        var direction = ResolveCombatDirection(action, actor, opponent);
        var startup = ScaleTiming(
            action.StartupBaseTicks,
            action.StartupMinimumTicks,
            action.StartupMaximumTicks,
            actor.ActionSpeed,
            settings.Decisions);
        var recovery = ScaleTiming(
            action.RecoveryBaseTicks,
            action.RecoveryMinimumTicks,
            action.RecoveryMaximumTicks,
            actor.ActionSpeed,
            settings.Decisions);
        var descriptor = new CombatActionDescriptor(
            action.Id,
            action.Category,
            intent.DecisionId,
            targetId,
            targetPosition,
            direction,
            action.EnergyCost,
            action.ResourceCost,
            startup,
            action.ActiveTicks,
            recovery,
            action.CooldownTicks,
            action.HitScheduleTicks,
            action.TrackTarget,
            tick);
        return FrozenCommitDescriptor.Combat(action, descriptor);
    }

    private static int ScaleTiming(
        int configured,
        int minimum,
        int maximum,
        int actionSpeed,
        DecisionRuntimeSettings runtime)
    {
        try
        {
            var candidate = checked(
                (long)runtime.Weights.FixedPointScale +
                checked((long)(actionSpeed - runtime.Timing.SpeedBaseline) * runtime.Timing.SpeedSlope));
            var multiplier = candidate < runtime.Timing.SpeedMinimumFixedPoint
                ? runtime.Timing.SpeedMinimumFixedPoint
                : candidate > runtime.Timing.SpeedMaximumFixedPoint
                    ? runtime.Timing.SpeedMaximumFixedPoint
                    : checked((int)candidate);
            return global::Battle.Core.Math.FixedMath.Clamp(
                global::Battle.Core.Math.FixedMath.Div(
                    configured,
                    multiplier,
                    runtime.Weights.FixedPointScale),
                minimum,
                maximum);
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.DecisionArithmeticOverflow,
                TickPhase.Decisions.ToString(),
                "Combat timing arithmetic overflowed: " + exception.Message);
        }
    }

    private static CommitDirection ResolveCombatDirection(
        DecisionActionProfile action,
        DecisionFighterView actor,
        DecisionFighterView opponent)
    {
        if (action.MovementMode == DecisionMovementMode.None &&
            action.TargetKind == DecisionTargetKind.Self)
        {
            return CommitDirection.None;
        }

        if (actor.Position == opponent.Position)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                "A combat direction cannot be derived from equal positions.");
        }

        var toward = opponent.Position > actor.Position
            ? CommitDirection.Right
            : CommitDirection.Left;
        var away = toward == CommitDirection.Right
            ? CommitDirection.Left
            : CommitDirection.Right;
        if (action.MovementMode == DecisionMovementMode.Retreat)
        {
            return away;
        }

        if (action.MovementMode == DecisionMovementMode.Adaptive)
        {
            var gap = global::System.Math.Max(
                0L,
                global::System.Math.Abs((long)actor.Position - opponent.Position) -
                actor.CollisionRadius - opponent.CollisionRadius);
            return gap > action.PreferredRangeMaximum ? toward : away;
        }

        return toward;
    }

    private static IReadOnlyList<ModifierTrace> DominantModifiers(
        CandidateScore chosen,
        int fixedPointScale) => chosen.Modifiers
        .Select((modifier, index) => new
        {
            Modifier = modifier,
            Index = index,
            Deviation = global::System.Math.Abs((long)modifier.MultiplierFixedPoint - fixedPointScale),
        })
        .Where(item => item.Deviation != 0)
        .OrderByDescending(item => item.Deviation)
        .ThenBy(item => item.Index)
        .Take(6)
        .Select(item => item.Modifier)
        .ToArray();

    private static DecisionCandidateTrace ToDiagnosticCandidate(CandidateScore candidate) => new(
        candidate.ActionId,
        candidate.Legal,
        candidate.FirstRejectionCode,
        candidate.BaseWeight,
        candidate.Modifiers,
        candidate.FinalWeight);

    private static DecisionBatchSnapshotProjection CreateDiagnosticProjection(
        BattleState state,
        RuntimeBattleSettings settings,
        FighterFrame frameA,
        FighterFrame frameB,
        ulong decisionNextIndex)
    {
        return new DecisionBatchSnapshotProjection(
            settings.Decisions.BattleId,
            settings.Decisions.EngineVersion,
            settings.Decisions.MasterSeed,
            settings.Decisions.ConfigHash,
            settings.Decisions.ModeRules,
            state.Tick,
            settings.InitiativeOrder,
            decisionNextIndex,
            new[]
            {
                Project(FighterId.FighterA, frameA),
                Project(FighterId.FighterB, frameB),
            });

        DecisionFighterSnapshot Project(FighterId fighterId, FighterFrame frame)
        {
            var fighter = state.Get(fighterId);
            var profile = settings.Decisions.GetFighter(fighterId);
            return new DecisionFighterSnapshot(
                frame,
                profile.Build,
                fighter.Cooldowns
                    .Where(pair => pair.Value > 0)
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new DecisionCooldownSnapshot(pair.Key, pair.Value)),
                fighter.LastCommittedActionId,
                fighter.LastCommittedCategory,
                fighter.SameActionStreak,
                fighter.SameCategoryStreak,
                profile.BuildView.SpecialActionIds
                    .OrderBy(id => id)
                    .Select(id => new DecisionOpportunitySnapshot(id, fighter.OpportunityDebtFor(id))),
                fighter.ObservableActionId,
                fighter.ObservableCommitTick,
                fighter.Emergency);
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
            var wasCombat = fighter.IsActiveCombat;
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
            if (wasCombat)
            {
                fighter.RecordCombatLifecycleEvent(changed.EventId);
            }
            else
            {
                fighter.RecordActionEvent(changed.EventId);
            }
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
            DecisionId decisionId,
            IReadOnlyList<CandidateScore> candidates)
        {
            ActorId = actorId;
            DecisionId = decisionId;
            Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        }

        internal FighterId ActorId { get; }

        internal DecisionId DecisionId { get; }

        internal IReadOnlyList<CandidateScore> Candidates { get; }

        internal DecisionSelection? Selection { get; set; }

        internal FrozenCommitDescriptor FrozenCommit { get; set; } = null!;

        internal CombatEventIdentity DecisionEvent { get; set; }

        internal CombatEventIdentity CommitEvent { get; set; }
    }

    private sealed class FrozenCommitDescriptor
    {
        private FrozenCommitDescriptor(
            StableId actionId,
            string category,
            FighterId? targetId,
            int? targetPositionAtCommit,
            CommitDirection direction,
            int energyCost,
            int resourceCost,
            int startupTicks,
            int activeTicks,
            int recoveryTicks,
            int cooldownTicks,
            SystemActionDefinition? systemAction,
            CombatActionDescriptor? combatAction,
            IReadOnlyList<int> absoluteImpactTicks)
        {
            ActionId = actionId;
            Category = category;
            TargetId = targetId;
            TargetPositionAtCommit = targetPositionAtCommit;
            Direction = direction;
            EnergyCost = energyCost;
            ResourceCost = resourceCost;
            StartupTicks = startupTicks;
            ActiveTicks = activeTicks;
            RecoveryTicks = recoveryTicks;
            CooldownTicks = cooldownTicks;
            SystemAction = systemAction;
            CombatAction = combatAction;
            AbsoluteImpactTicks = absoluteImpactTicks;
        }

        internal StableId ActionId { get; }

        internal string Category { get; }

        internal FighterId? TargetId { get; }

        internal int? TargetPositionAtCommit { get; }

        internal CommitDirection Direction { get; }

        internal int EnergyCost { get; }

        internal int ResourceCost { get; }

        internal int StartupTicks { get; }

        internal int ActiveTicks { get; }

        internal int RecoveryTicks { get; }

        internal int CooldownTicks { get; }

        internal SystemActionDefinition? SystemAction { get; }

        internal CombatActionDescriptor? CombatAction { get; }

        internal IReadOnlyList<int> AbsoluteImpactTicks { get; }

        internal bool HasTelegraph => AbsoluteImpactTicks.Count != 0;

        internal int RequiredEventCount =>
            2 +
            (EnergyCost == 0 ? 0 : 1) +
            (ResourceCost == 0 ? 0 : 1) +
            (HasTelegraph ? 1 : 0);

        internal static FrozenCommitDescriptor System(
            DecisionActionProfile profile,
            SystemActionDefinition action,
            FighterId targetId,
            int targetPosition,
            CommitDirection direction) => new(
            action.Id,
            profile.Category,
            targetId,
            targetPosition,
            direction,
            action.EnergyCost,
            action.ResourceCost,
            action.StartupTicks,
            action.ActiveTicks,
            action.RecoveryTicks,
            action.CooldownTicks,
            action,
            null,
            Array.Empty<int>());

        internal static FrozenCommitDescriptor Combat(
            DecisionActionProfile profile,
            CombatActionDescriptor action) => new(
            action.ActionId,
            profile.Category,
            action.TargetFighterId,
            action.TargetPositionAtCommit,
            action.CommitDirection,
            action.EnergyCost,
            action.ResourceCost,
            action.StartupTicks,
            action.ActiveTicks,
            action.RecoveryTicks,
            action.CooldownTicks,
            null,
            action,
            action.AbsoluteImpactTicks());
    }

    private sealed class PreviewDecisionDrawSource : IDecisionDrawSource
    {
        private readonly Pcg32Stream _preview;

        internal PreviewDecisionDrawSource(Pcg32Stream preview)
        {
            _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        }

        public ulong NextDrawIndex => _preview.NextDrawIndex;

        public RngProvenance NextInt(int minimumInclusive, int maximumExclusive) =>
            _preview.NextInt(minimumInclusive, maximumExclusive, RngOperation.NextInt);
    }
}
