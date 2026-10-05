using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Outcome;
using Battle.Core.Decisions;
using Battle.Core.Effects;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core;

public sealed class CombatEngine
{
    private readonly ITickCoordinatorObserver _observer;
    private readonly ISystemActionAvailability _systemActionAvailability;
    private readonly EffectRuntimeDefinition? _effectDefinition;
    private readonly ArtifactVersion _producerVersion = ContractVersions.HistoricalEngine;

    public CombatEngine()
        : this(
            NullTickCoordinatorObserver.Instance,
            Wp07SystemActionAvailability.Instance)
    {
        _producerVersion = ContractVersions.Engine;
    }

    internal CombatEngine(ITickCoordinatorObserver observer)
        : this(observer, Wp07SystemActionAvailability.Instance)
    {
    }

    // Controlled test seam until strict v0.2 setup/consumer/replay gates enable the new producer.
    // Public constructors never accept or inject partially materialized effect DATA.
    internal CombatEngine(EffectRuntimeDefinition effectDefinition)
        : this(NullTickCoordinatorObserver.Instance, Wp07SystemActionAvailability.Instance)
    {
        _effectDefinition = effectDefinition ?? throw new ArgumentNullException(nameof(effectDefinition));
    }

    // Explicit historical producer selection for regression harnesses; public producer is0.5.
    // Unlike the definition-injection harness, this consumes and validates the external v0.2 snapshot.
    internal CombatEngine(ArtifactVersion producerVersion, ITickCoordinatorObserver? observer = null)
        : this(observer ?? NullTickCoordinatorObserver.Instance, Wp07SystemActionAvailability.Instance)
    {
        if (producerVersion != ContractVersions.Engine && producerVersion != ContractVersions.HistoricalEngine)
            throw new ArgumentOutOfRangeException(nameof(producerVersion));
        _producerVersion = producerVersion;
    }

    internal CombatEngine(
        ITickCoordinatorObserver observer,
        ISystemActionAvailability systemActionAvailability)
    {
        _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        _systemActionAvailability = systemActionAvailability ??
            throw new ArgumentNullException(nameof(systemActionAvailability));
    }

    public BattleResult Simulate(
        BattleRequest request,
        CompiledBattleConfig config,
        ICombatEventJournal journal)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (journal is null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        var setupResult = BattleSetupFactory.Create(request, config, _producerVersion);
        if (!setupResult.IsSuccess)
        {
            return BattleResult.Rejected(setupResult.Errors);
        }

        var setup = setupResult.Setup!;
        var state = setup.State;
        if (_effectDefinition is not null)
        {
            var issues = new List<EffectSetupIssue>();
            EffectConsumerArithmeticProof.Validate(_effectDefinition, setup.Settings, state, issues);
            if (issues.Count != 0)
                return BattleResult.Rejected(issues.Distinct().OrderBy(x => x.Path, StringComparer.Ordinal)
                    .ThenBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Entity, StringComparer.Ordinal)
                    .Select(x => new BattleRejectionError(new ReasonCode(x.Code), x.Path,
                        x.Entity is null ? null : new ExternalId(x.Entity), new StableId("battle_rejected_validation"),
                        Array.Empty<BattleRejectionDetail>())));
            state.InitializeEffects(_effectDefinition);
        }
        var initialFrameA = state.FighterA.ToFrame();
        var initialFrameB = state.FighterB.ToFrame();
        var journalStart = new CombatJournalStart(
            request.BattleId,
            request.EngineVersion,
            ContractVersions.Rng,
            ContractVersions.Ordering,
            config.Reference,
            new BattleInputSnapshot(request.MasterSeed, request.ModeRules.Id, setup.Settings.Arena),
            new CombatJournalFighterStart(request.BuildA, initialFrameA),
            new CombatJournalFighterStart(request.BuildB, initialFrameB));
        var begin = journal.Begin(in journalStart);
        var emitter = new CombatEventEmitter(
            request,
            config,
            journal,
            setup.Settings.MaximumEvents);

        try
        {
            var startedPayload = new BattleStartedPayload(
                Array.Empty<EventId>(),
                begin.InputDigest,
                new[] { initialFrameA, initialFrameB },
                setup.InitiativeOrder,
                InitiativeTieBreak.StatThenSeededHash);
            _ = emitter.Emit(0, startedPayload, reasonCodes: new[] { new ReasonCode("Initialization") });
            if (state.Effects is not null)
            {
                var started = emitter.LastDraft!;
                AtomicBattleBatch.Execute(state, emitter, TickPhase.Snapshot, (preview, events, _) =>
                    preview.Effects!.CloseEvents(preview, events, new[] { started }));
            }

            var coordinator = new TickCoordinator(
                setup.Settings.MaximumZeroProgressTicks,
                _observer,
                _systemActionAvailability,
                journal as ICombatDecisionDiagnostics);
            while (state.Tick < setup.Settings.TimeLimitTicks)
            {
                var immediate = coordinator.RunActiveTick(state, setup.Settings, emitter);
                if (immediate.HasValue)
                {
                    return CompleteImmediateOutcome(
                        immediate.Value,
                        state,
                        emitter,
                        journal);
                }
            }

            if (state.Tick != setup.Settings.TimeLimitTicks)
            {
                throw new EngineInvariantException(
                    EngineFailureCodes.TickLimitExceeded,
                    "TickBoundary",
                    $"State tick {state.Tick} exceeded configured limit {setup.Settings.TimeLimitTicks}.");
            }

            return CompleteTimeout(state, emitter, journal);
        }
        catch (EngineInvariantException failure)
        {
            CompleteInvalidBattle(state, emitter, journal, failure.Code);
            return BattleResult.FailedInvariant(
                new BattleInvariantFailure(
                    failure.Code,
                    failure.Phase,
                    state.Tick,
                    failure.Message));
        }
    }

    private static BattleResult CompleteTimeout(
        BattleState state,
        CombatEventEmitter emitter,
        ICombatEventJournal journal)
    {
        var timeout = TimeoutOutcomeResolver.Resolve(
            state.FighterA.Health,
            state.FighterA.MaximumHealth,
            state.FighterB.Health,
            state.FighterB.MaximumHealth);
        if (state.Effects is not null)
            emitter.PreflightNonterminalBatch(timeout.Outcome == BattleOutcome.Draw ? 2 : 1, TickPhase.Outcome);
        state.RecordOutcome(
            timeout.Outcome,
            timeout.WinnerFighterId,
            timeout.EndReason);
        var timeoutPayload = new TimeoutReachedPayload(
            Array.Empty<EventId>(),
            state.FighterA.Health,
            state.FighterA.MaximumHealth,
            state.FighterB.Health,
            state.FighterB.MaximumHealth,
            timeout.LeftCrossProduct,
            timeout.RightCrossProduct);
        var timeoutEvent = emitter.Emit(
            state.Tick,
            timeoutPayload,
            reasonCodes: new[] { new ReasonCode("TimeLimitReached") });
        var pivotal = new List<EventId> { timeoutEvent.EventId };
        var terminalSource = timeoutEvent.EventId;

        if (timeout.Outcome == BattleOutcome.Draw)
        {
            var drawPayload = new DrawDeclaredPayload(
                new[] { timeoutEvent.EventId },
                DrawReason.TimeoutEqualHealthFraction,
                new[] { FighterId.FighterA, FighterId.FighterB },
                null);
            var drawEvent = emitter.Emit(
                state.Tick,
                drawPayload,
                sourceEventId: timeoutEvent.EventId,
                reasonCodes: new[] { new ReasonCode("TimeoutEqualHealthFraction") });
            pivotal.Add(drawEvent.EventId);
            terminalSource = drawEvent.EventId;
        }

        CleanupEffects(state, emitter, terminalSource);
        var summary = new BattleSummary(
            timeout.Outcome,
            timeout.WinnerFighterId,
            timeout.EndReason,
            state.Tick,
            state.Tick,
            checked(emitter.EventCount + 1),
            pivotal,
            state.FinalFrames());
        EmitBattleEnded(state, emitter, summary, terminalSource, timeout.EndReason.ToString());
        var completion = journal.Complete(in summary);
        return BattleResult.Completed(
            summary,
            completion.FinalDigest,
            completion.PublishedReplayId);
    }

    private static BattleResult CompleteImmediateOutcome(
        ImmediateOutcome outcome,
        BattleState state,
        CombatEventEmitter emitter,
        ICombatEventJournal journal)
    {
        var pivotal = new List<EventId>(state.PivotalEventIds);
        var hasResolutionPivotalEvents = pivotal.Count != 0;
        if (!hasResolutionPivotalEvents && state.FighterA.Health == 0)
        {
            pivotal.Add(EmitDefeat(state, emitter, FighterId.FighterA).EventId);
        }

        if (!hasResolutionPivotalEvents && state.FighterB.Health == 0)
        {
            pivotal.Add(EmitDefeat(state, emitter, FighterId.FighterB).EventId);
        }

        EventId terminalSource = pivotal[^1];
        if (outcome.Outcome == BattleOutcome.Draw)
        {
            var draw = emitter.Emit(
                state.Tick,
                new DrawDeclaredPayload(
                    pivotal,
                    DrawReason.DoubleKO,
                    new[] { FighterId.FighterA, FighterId.FighterB },
                    state.TerminalResolutionGroupId),
                resolutionGroupId: state.TerminalResolutionGroupId,
                sourceEventId: terminalSource,
                reasonCodes: new[] { new ReasonCode("DoubleKO") });
            pivotal.Add(draw.EventId);
            terminalSource = draw.EventId;
        }

        CleanupEffects(state, emitter, terminalSource);
        var summary = new BattleSummary(
            outcome.Outcome,
            outcome.WinnerFighterId,
            outcome.EndReason,
            state.Tick,
            state.Tick,
            checked(emitter.EventCount + 1),
            pivotal,
            state.FinalFrames());
        EmitBattleEnded(state, emitter, summary, terminalSource, outcome.EndReason.ToString());
        var completion = journal.Complete(in summary);
        return BattleResult.Completed(
            summary,
            completion.FinalDigest,
            completion.PublishedReplayId);
    }

    private static CombatEventIdentity EmitDefeat(
        BattleState state,
        CombatEventEmitter emitter,
        FighterId defeatedId)
    {
        var fighter = state.Get(defeatedId);
        var frame = fighter.ToFrame();
        var source = emitter.LastEventId;
        return emitter.Emit(
            state.Tick,
            new FighterDefeatedPayload(
                source.HasValue ? new[] { source.Value } : Array.Empty<EventId>(),
                defeatedId,
                null,
                null,
                fighter.Health),
            actorId: defeatedId,
            sourceEventId: source,
            reasonCodes: new[] { new ReasonCode("Defeat") },
            before: new FramePair(frame, null),
            after: new FramePair(frame, null));
    }

    private static void CompleteInvalidBattle(
        BattleState state,
        CombatEventEmitter emitter,
        ICombatEventJournal journal,
        ReasonCode failureCode)
    {
        if (emitter.IsTerminal)
        {
            return;
        }

        state.RecordOutcome(
            BattleOutcome.Invalid,
            null,
            BattleEndReason.BattleInvalid);
        var source = emitter.LastEventId;
        CleanupEffects(state, emitter, source);
        var summary = new BattleSummary(
            BattleOutcome.Invalid,
            null,
            BattleEndReason.BattleInvalid,
            state.Tick,
            state.Tick,
            checked(emitter.EventCount + 1),
            Array.Empty<EventId>(),
            state.FinalFrames());
        EmitBattleEnded(state, emitter, summary, source, failureCode.Value);
        _ = journal.Complete(in summary);
    }

    private static void EmitBattleEnded(
        BattleState state,
        CombatEventEmitter emitter,
        BattleSummary summary,
        EventId? source,
        string reason)
    {
        _ = emitter.Emit(
            state.Tick,
            new BattleEndedPayload(
                source.HasValue ? new[] { source.Value } : Array.Empty<EventId>(),
                summary),
            sourceEventId: source,
            reasonCodes: new[] { new ReasonCode(reason) });
        state.MarkTerminal();
    }

    private static void CleanupEffects(BattleState state, CombatEventEmitter emitter, EventId? source)
    {
        if (state.Effects is not null)
            AtomicBattleBatch.Execute(state, emitter, TickPhase.Outcome, (preview, events, _) =>
                preview.Effects!.Cleanup(preview, events, source));
    }
}
