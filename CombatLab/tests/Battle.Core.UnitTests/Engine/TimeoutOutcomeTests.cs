using Battle.Core;
using Battle.Core.Engine;
using Battle.Core.Outcome;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Core.UnitTests.Engine;

public sealed class TimeoutOutcomeTests
{
    [Fact]
    public void WP06_TIME_001_LargerHealthFractionAwardsFighterA()
    {
        var outcome = TimeoutOutcomeResolver.Resolve(75, 100, 50, 100);

        Assert.Equal(BattleOutcome.FighterAWin, outcome.Outcome);
        Assert.Equal(FighterId.FighterA, outcome.WinnerFighterId);
        Assert.Equal(7_500, outcome.LeftCrossProduct);
        Assert.Equal(5_000, outcome.RightCrossProduct);
    }

    [Fact]
    public void WP06_TIME_002_LargerHealthFractionAwardsFighterB()
    {
        var outcome = TimeoutOutcomeResolver.Resolve(50, 100, 75, 100);

        Assert.Equal(BattleOutcome.FighterBWin, outcome.Outcome);
        Assert.Equal(FighterId.FighterB, outcome.WinnerFighterId);
    }

    [Fact]
    public void WP06_TIME_003_EqualReducedFractionsDeclareDraw()
    {
        var outcome = TimeoutOutcomeResolver.Resolve(1, 3, 2, 6);

        Assert.Equal(BattleOutcome.Draw, outcome.Outcome);
        Assert.Null(outcome.WinnerFighterId);
        Assert.Equal(6, outcome.LeftCrossProduct);
        Assert.Equal(6, outcome.RightCrossProduct);
    }

    [Fact]
    public void WP06_TIME_004_Int32BoundaryUsesExactInt64CrossProducts()
    {
        var maximum = int.MaxValue;

        var outcome = TimeoutOutcomeResolver.Resolve(maximum, maximum, maximum - 1, maximum);

        Assert.Equal(checked((long)maximum * maximum), outcome.LeftCrossProduct);
        Assert.Equal(checked((long)(maximum - 1) * maximum), outcome.RightCrossProduct);
        Assert.Equal(BattleOutcome.FighterAWin, outcome.Outcome);
    }

    [Theory]
    [InlineData(false, true, BattleOutcome.FighterAWin, BattleEndReason.Defeat)]
    [InlineData(true, false, BattleOutcome.FighterBWin, BattleEndReason.Defeat)]
    [InlineData(true, true, BattleOutcome.Draw, BattleEndReason.DoubleKO)]
    public void WP06_TIME_005_LastActiveTickOutcomePrecedesTimeout(
        bool defeatA,
        bool defeatB,
        BattleOutcome expectedOutcome,
        BattleEndReason expectedEndReason)
    {
        var observer = new OutcomeInjectionObserver(defeatA, defeatB);
        var journal = new RecordingJournal();

        var result = new CombatEngine(observer).Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: 1),
            journal);

        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Equal(expectedOutcome, result.Summary!.Outcome);
        Assert.Equal(expectedEndReason, result.Summary.EndReason);
        Assert.Equal(0, result.Summary.EndTick);
        Assert.DoesNotContain(
            journal.Drafts,
            draft => draft.EventType == CombatEventType.TimeoutReached);
        Assert.Equal(CombatEventType.BattleEnded, journal.Drafts[^1].EventType);
    }

    [Theory]
    [InlineData(-1, 1, "fighterAHealth")]
    [InlineData(1, -1, "fighterBHealth")]
    public void ImmediateOutcomeRejectsNegativeHealth(
        int healthA,
        int healthB,
        string parameterName)
    {
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ImmediateOutcomeResolver.Resolve(healthA, healthB));

        Assert.Equal(parameterName, failure.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WP06_TIME_006_NonPositiveTickLimitIsRejectedBeforeBegin(int limit)
    {
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: limit),
            journal);

        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Equal(0, journal.BeginCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void WP06_TIME_006_CoordinatorRejectsStateAtOrBeyondTickLimit(int advances)
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig(timeLimit: 1);
        var setup = Battle.Core.Initialization.BattleSetupFactory.Create(request, config).Setup!;
        for (var index = 0; index < advances; index++)
        {
            setup.State.AdvanceTick();
        }

        var emitter = new CombatEventEmitter(
            request,
            config,
            new RecordingJournal(),
            setup.Settings.MaximumEvents);
        var coordinator = new TickCoordinator(setup.Settings.MaximumZeroProgressTicks);

        var failure = Assert.Throws<EngineInvariantException>(() =>
            coordinator.RunActiveTick(setup.State, setup.Settings, emitter));

        Assert.Equal("TickLimitExceeded", failure.Code.Value);
        Assert.Equal("TickBoundary", failure.Phase);
        Assert.Equal(advances, setup.State.Tick);
    }

    [Fact]
    public void WP06_TRACE_001_LimitOneProducesExactWaitDrawPayloadAndFinalState()
    {
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(),
            journal);

        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Equal(8, journal.Drafts.Count);
        Assert.Equal(
            Enumerable.Range(0, 8).Select(index => EventId.FromSequence(index)),
            journal.Drafts.Select(draft => draft.EventId));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 1, 1 }, journal.Drafts.Select(draft => draft.Tick));

        var decisionA = Assert.IsType<DecisionMadePayload>(journal.Drafts[1].Payload);
        Assert.Equal("sys_wait", decisionA.ChosenActionId.Value);
        Assert.Equal(150, decisionA.ChosenWeight);
        Assert.Equal(150, decisionA.WeightSum);
        Assert.Equal(DecisionSelectionMode.OnlyLegalAction, decisionA.SelectionMode);
        Assert.Null(journal.Drafts[1].Rng);

        var commitA = Assert.IsType<ActionCommittedPayload>(journal.Drafts[3].Payload);
        Assert.Equal(0, commitA.EnergyCost);
        Assert.Equal(0, commitA.ResourceCost);
        Assert.Equal(0, commitA.StartupTicks);
        Assert.Equal(3, commitA.ActiveTicks);
        Assert.Equal(0, commitA.RecoveryTicks);
        Assert.Equal(0, commitA.CooldownTicks);
        Assert.Equal(CommitDirection.None, commitA.CommitDirection);
        Assert.Equal(4_500, commitA.TargetPositionAtCommit);

        var timeout = Assert.IsType<TimeoutReachedPayload>(journal.Drafts[5].Payload);
        Assert.Equal(1_897_500, timeout.LeftCrossProduct);
        Assert.Equal(1_897_500, timeout.RightCrossProduct);
        Assert.IsType<DrawDeclaredPayload>(journal.Drafts[6].Payload);
        Assert.IsType<BattleEndedPayload>(journal.Drafts[7].Payload);

        Assert.Equal(BattleOutcome.Draw, result.Summary!.Outcome);
        Assert.Equal(BattleEndReason.TimeoutEqualHealthFraction, result.Summary.EndReason);
        Assert.Equal(1, result.Summary.EndTick);
        Assert.Equal(1, result.Summary.DurationTicks);
        Assert.Equal(8, result.Summary.EventCount);
        Assert.Equal(EngineTestFixture.FinalDigest, result.FinalDigest);
        Assert.All(result.Summary.FinalFrames, frame =>
        {
            Assert.Equal(FighterState.Idle, frame.State);
            Assert.Equal("sys_wait", frame.ActionId?.Value);
            Assert.Equal(ActionPhase.Active, frame.ActionPhase);
            Assert.Equal(3, frame.StateTicksRemaining);
        });
        Assert.Equal(1, journal.BeginCount);
        Assert.Equal(1, journal.CompleteCount);
        Assert.Equal(EngineTestFixture.InputDigest, ((BattleStartedPayload)journal.Drafts[0].Payload).InputDigest);
    }

    private sealed class OutcomeInjectionObserver : ITickCoordinatorObserver
    {
        private readonly bool _defeatA;
        private readonly bool _defeatB;

        internal OutcomeInjectionObserver(bool defeatA, bool defeatB)
        {
            _defeatA = defeatA;
            _defeatB = defeatB;
        }

        public void OnPhase(BattleState state, TickPhase phase)
        {
            if (state.Tick != 0 || phase != TickPhase.Outcome)
            {
                return;
            }

            if (_defeatA)
            {
                state.FighterA.SetHealthForTesting(0);
            }

            if (_defeatB)
            {
                state.FighterB.SetHealthForTesting(0);
            }
        }

        public void OnDecisionSnapshot(FighterId fighterId, TickSnapshot snapshot)
        {
        }
    }
}
