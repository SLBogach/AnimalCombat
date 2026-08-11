using Battle.Core.Engine;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace Battle.Core.UnitTests.Engine;

public sealed class StateTransitionGuardTests
{
    [Fact]
    public void WP06_TERM_001_BattleStateRequiresBothFighters()
    {
        var setup = EngineTestFixture.CreateSetup();

        Assert.Throws<ArgumentNullException>(() =>
            new BattleState(null!, setup.State.FighterB, masterSeed: 1));
        Assert.Throws<ArgumentNullException>(() =>
            new BattleState(setup.State.FighterA, null!, masterSeed: 1));
    }

    [Fact]
    public void WP06_TERM_001_SystemWaitLifecycleUsesOnlyValidStateTransitions()
    {
        var fighter = EngineTestFixture.CreateSetup().State.FighterA;
        var wait = new StableId("sys_wait");

        fighter.CommitSystemWait(wait, activeTicks: 2);
        Assert.Equal(FighterState.Idle, fighter.State);
        Assert.Equal(wait, fighter.ActionId);
        Assert.Equal(ActionPhase.Active, fighter.ActionPhase);
        Assert.Equal(2, fighter.StateTicksRemaining);

        fighter.AdvanceActionLifecycle();
        Assert.Equal(1, fighter.StateTicksRemaining);
        fighter.AdvanceActionLifecycle();
        Assert.Equal(FighterState.DecisionReady, fighter.State);
        Assert.Null(fighter.ActionId);
        Assert.Null(fighter.ActionPhase);
        Assert.Null(fighter.StateTicksRemaining);

        fighter.AdvanceActionLifecycle();
        Assert.Equal(FighterState.DecisionReady, fighter.State);
    }

    [Fact]
    public void WP06_TERM_001_SystemWaitRejectsInvalidCommitTransitionsAndDuration()
    {
        var fighter = EngineTestFixture.CreateSetup().State.FighterA;
        var wait = new StableId("sys_wait");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fighter.CommitSystemWait(wait, activeTicks: 0));
        fighter.CommitSystemWait(wait, activeTicks: 1);
        var failure = Assert.Throws<EngineInvariantException>(() =>
            fighter.CommitSystemWait(wait, activeTicks: 1));

        Assert.Equal("InvalidStateTransition", failure.Code.Value);
        Assert.Equal(TickPhase.Decisions.ToString(), failure.Phase);
    }

    [Fact]
    public void WP06_TERM_001_ActionLifecycleGuardsCorruptTimers()
    {
        var fighter = EngineTestFixture.CreateSetup().State.FighterA;
        fighter.CommitSystemWait(new StableId("sys_wait"), activeTicks: 1);

        fighter.SetActionTimerForTesting(null);
        var missingTimer = Assert.Throws<EngineInvariantException>(
            fighter.AdvanceActionLifecycle);
        Assert.Equal("InvalidStateTransition", missingTimer.Code.Value);

        fighter.SetActionTimerForTesting(0);
        var failure = Assert.Throws<EngineInvariantException>(
            fighter.AdvanceActionLifecycle);
        Assert.Equal("InvalidStateTransition", failure.Code.Value);
        Assert.Equal(TickPhase.ActionPhaseEnd.ToString(), failure.Phase);
    }

    [Fact]
    public void WP06_TERM_001_ActionLifecycleRejectsTimerWithoutActionIdentity()
    {
        var fighter = EngineTestFixture.CreateSetup().State.FighterA;
        fighter.SetActionTimerForTesting(1);

        var failure = Assert.Throws<EngineInvariantException>(
            fighter.AdvanceActionLifecycle);

        Assert.Equal("InvalidStateTransition", failure.Code.Value);
        Assert.Equal(TickPhase.ActionPhaseEnd.ToString(), failure.Phase);
    }

    [Fact]
    public void WP06_TERM_001_ActionIdentityStillBlocksDecisionReadyState()
    {
        var fighter = EngineTestFixture.CreateSetup().State.FighterA;
        fighter.CommitSystemWait(new StableId("sys_wait"), activeTicks: 1);
        fighter.SetStateForTesting(FighterState.DecisionReady);

        Assert.False(fighter.IsDecisionReady);
        var failure = Assert.Throws<EngineInvariantException>(() =>
            fighter.CommitSystemWait(new StableId("sys_wait"), activeTicks: 1));
        Assert.Equal("InvalidStateTransition", failure.Code.Value);
    }

    [Fact]
    public void WP06_TERM_001_HealthAndStateLookupGuardsRejectInvalidValues()
    {
        var state = EngineTestFixture.CreateSetup().State;

        Assert.Same(state.FighterA, state.Get(FighterId.FighterA));
        Assert.Same(state.FighterB, state.Get(FighterId.FighterB));
        Assert.Same(state.FighterB, state.GetOpponent(FighterId.FighterA));
        Assert.Same(state.FighterA, state.GetOpponent(FighterId.FighterB));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Get((FighterId)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetOpponent((FighterId)99));

        state.FighterA.SetHealthForTesting(0);
        Assert.Equal(0, state.FighterA.Health);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.FighterB.SetHealthForTesting(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.FighterB.SetHealthForTesting(state.FighterB.MaximumHealth + 1));
    }

    [Fact]
    public void WP06_TERM_001_TerminalStateRejectsOutcomeAndCoordinatorMutation()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var setup = EngineTestFixture.CreateSetup();
        var state = setup.State;
        state.RecordOutcome(
            BattleOutcome.Draw,
            null,
            BattleEndReason.TimeoutEqualHealthFraction);
        state.MarkTerminal();
        var emitter = new CombatEventEmitter(
            request,
            config,
            new RecordingJournal(),
            setup.Settings.MaximumEvents);
        var coordinator = new TickCoordinator(setup.Settings.MaximumZeroProgressTicks);

        Assert.Throws<EngineInvariantException>(() => state.MarkTerminal());
        Assert.Throws<EngineInvariantException>(() => state.RecordOutcome(
            BattleOutcome.Draw,
            null,
            BattleEndReason.TimeoutEqualHealthFraction));
        var failure = Assert.Throws<EngineInvariantException>(() =>
            coordinator.RunActiveTick(state, setup.Settings, emitter));
        Assert.Equal("TerminalMutation", failure.Code.Value);
    }

    [Fact]
    public void WP06_TERM_001_EmitterRejectsJournalIdentityMismatch()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var setup = EngineTestFixture.CreateSetup();
        var emitter = new CombatEventEmitter(
            request,
            config,
            new MismatchingIdentityJournal(mismatchEventId: true),
            setup.Settings.MaximumEvents);

        var failure = Assert.Throws<EngineInvariantException>(() =>
            emitter.Emit(
                0,
                new BattleStartedPayload(
                    Array.Empty<EventId>(),
                    EngineTestFixture.InputDigest,
                    setup.State.FinalFrames(),
                    setup.InitiativeOrder,
                    InitiativeTieBreak.StatThenSeededHash)));

        Assert.Equal("InvalidStateTransition", failure.Code.Value);
        Assert.Equal("EventEmitter", failure.Phase);
        Assert.Equal(0, emitter.EventCount);
    }

    [Fact]
    public void WP06_TERM_001_EmitterRejectsJournalSequenceMismatch()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var setup = EngineTestFixture.CreateSetup();
        var emitter = new CombatEventEmitter(
            request,
            config,
            new MismatchingIdentityJournal(mismatchEventId: false),
            setup.Settings.MaximumEvents);

        var failure = Assert.Throws<EngineInvariantException>(() =>
            emitter.Emit(
                0,
                new BattleStartedPayload(
                    Array.Empty<EventId>(),
                    EngineTestFixture.InputDigest,
                    setup.State.FinalFrames(),
                    setup.InitiativeOrder,
                    InitiativeTieBreak.StatThenSeededHash)));

        Assert.Equal("InvalidStateTransition", failure.Code.Value);
        Assert.Equal(0, emitter.EventCount);
    }

    [Fact]
    public void WP06_TERM_001_EmitterRejectsInvalidConstructionAndNullPayload()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var journal = new RecordingJournal();

        Assert.Throws<ArgumentNullException>(() =>
            new CombatEventEmitter(null!, config, journal, maximumEvents: 4));
        Assert.Throws<ArgumentNullException>(() =>
            new CombatEventEmitter(request, null!, journal, maximumEvents: 4));
        Assert.Throws<ArgumentNullException>(() =>
            new CombatEventEmitter(request, config, null!, maximumEvents: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatEventEmitter(request, config, journal, maximumEvents: 3));

        var emitter = new CombatEventEmitter(request, config, journal, maximumEvents: 4);
        Assert.Null(emitter.LastEventId);
        Assert.Throws<ArgumentNullException>(() => emitter.Emit(0, null!));
    }

    private sealed class MismatchingIdentityJournal : ICombatEventJournal
    {
        private readonly bool _mismatchEventId;

        internal MismatchingIdentityJournal(bool mismatchEventId)
        {
            _mismatchEventId = mismatchEventId;
        }

        public JournalBeginResult Begin(in CombatJournalStart start) =>
            new(EngineTestFixture.InputDigest);

        public CombatEventIdentity Append(in CombatEventDraft draft) =>
            _mismatchEventId
                ? new CombatEventIdentity(
                    EventId.FromSequence(draft.Sequence + 1),
                    draft.Sequence)
                : new CombatEventIdentity(
                    draft.EventId,
                    draft.Sequence + 1);

        public JournalCompletion Complete(in BattleSummary summary) =>
            throw new InvalidOperationException("Completion is not expected in this guard test.");
    }
}
