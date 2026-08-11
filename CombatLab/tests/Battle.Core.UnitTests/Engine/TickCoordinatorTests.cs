using Battle.Core;
using Battle.Core.Engine;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Core.UnitTests.Engine;

public sealed class TickCoordinatorTests
{
    [Fact]
    public void WP06_PIPE_001_EveryActiveTickCallsAllTwelvePhasesInOrder()
    {
        var observer = new RecordingTickObserver();
        var journal = new RecordingJournal();

        var result = new CombatEngine(observer).Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(),
            journal);

        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Equal(
            Enum.GetValues<TickPhase>(),
            observer.Phases.Select(item => item.Phase));
        Assert.All(observer.Phases, item => Assert.Equal(0, item.Tick));
    }

    [Fact]
    public void WP06_PIPE_002_TimeLimitTwoRunsTicksZeroAndOneButNoPhasesAtBoundaryTwo()
    {
        var observer = new RecordingTickObserver();

        var result = new CombatEngine(observer).Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: 2),
            new RecordingJournal());

        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Equal(24, observer.Phases.Count);
        Assert.Equal(12, observer.Phases.Count(item => item.Tick == 0));
        Assert.Equal(12, observer.Phases.Count(item => item.Tick == 1));
        Assert.DoesNotContain(observer.Phases, item => item.Tick == 2);
        Assert.Equal(2, result.Summary!.EndTick);
        Assert.Equal(2, result.Summary.DurationTicks);
    }

    [Fact]
    public void WP06_PIPE_003_BothDecisionsReadTheSameImmutableTickSnapshot()
    {
        var observer = new RecordingTickObserver();

        _ = new CombatEngine(observer).Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(),
            new RecordingJournal());

        Assert.Equal(2, observer.DecisionSnapshots.Count);
        Assert.Equal(FighterId.FighterA, observer.DecisionSnapshots[0].FighterId);
        Assert.Equal(FighterId.FighterB, observer.DecisionSnapshots[1].FighterId);
        Assert.Same(
            observer.DecisionSnapshots[0].Snapshot,
            observer.DecisionSnapshots[1].Snapshot);
        Assert.Equal(FighterState.DecisionReady, observer.DecisionSnapshots[1].Snapshot.FighterA.State);
    }

    [Fact]
    public void WP06_PIPE_004_EmitsDecisionsABeforeBThenCommitsABeforeB()
    {
        var journal = new RecordingJournal();

        _ = new CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(),
            journal);

        Assert.Equal(
            new[]
            {
                CombatEventType.BattleStarted,
                CombatEventType.DecisionMade,
                CombatEventType.DecisionMade,
                CombatEventType.ActionCommitted,
                CombatEventType.ActionCommitted,
                CombatEventType.TimeoutReached,
                CombatEventType.DrawDeclared,
                CombatEventType.BattleEnded,
            },
            journal.Drafts.Select(draft => draft.EventType));
        Assert.Equal(FighterId.FighterA, journal.Drafts[1].ActorId);
        Assert.Equal(FighterId.FighterB, journal.Drafts[2].ActorId);
        Assert.Equal(FighterId.FighterA, journal.Drafts[3].ActorId);
        Assert.Equal(FighterId.FighterB, journal.Drafts[4].ActorId);
        Assert.Equal(FighterId.FighterB, ((BattleStartedPayload)journal.Drafts[0].Payload).InitiativeOrder[0]);
    }

    [Fact]
    public void WP06_TERM_001_BattleStateRejectsMutationAfterTerminal()
    {
        var state = EngineTestFixture.CreateSetup().State;
        state.MarkTerminal();

        var failure = Assert.Throws<EngineInvariantException>(() => state.AdvanceTick());

        Assert.Equal("TerminalMutation", failure.Code.Value);
        Assert.Throws<EngineInvariantException>(() => state.CreateSnapshot());
    }
}
