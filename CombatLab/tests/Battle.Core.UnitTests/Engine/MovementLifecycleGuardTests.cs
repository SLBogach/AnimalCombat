using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.UnitTests.Engine;

public sealed class MovementLifecycleGuardTests
{
    private static readonly DecisionId Decision = new("dec-fighter_a-000001");

    [Fact]
    public void WP07_LIFE_001_CommitCoversWaitMovementModesAndDescriptorGuards()
    {
        var waitFighter = CreateFighter();
        waitFighter.CommitSystemAction(WaitAction(), Decision, CommitDirection.None, 8_000);
        Assert.Equal(FighterState.Idle, waitFighter.State);
        Assert.False(waitFighter.IsActiveMovement);

        var approach = CreateFighter();
        approach.CommitSystemAction(MovementAction(SystemMovementMode.Approach), Decision, CommitDirection.Right, 8_000);
        Assert.Equal(FighterState.Approach, approach.State);
        Assert.Equal(ActionPhase.Startup, approach.ActionPhase);
        Assert.False(approach.IsActiveMovement);
        Assert.Throws<EngineInvariantException>(() => approach.CommitSystemAction(
            MovementAction(SystemMovementMode.Approach),
            Decision,
            CommitDirection.Right,
            8_000));

        var retreat = CreateFighter();
        retreat.CommitSystemAction(
            MovementAction(SystemMovementMode.Retreat, startup: 0),
            Decision,
            CommitDirection.Left,
            8_000);
        Assert.Equal(FighterState.Retreat, retreat.State);
        Assert.Equal(ActionPhase.Active, retreat.ActionPhase);
        Assert.True(retreat.IsActiveMovement);

        Assert.Throws<ArgumentNullException>(() => CreateFighter().CommitSystemAction(
            null!,
            Decision,
            CommitDirection.Right,
            8_000));
        Assert.Throws<EngineInvariantException>(() => CreateFighter().CommitSystemAction(
            MovementAction(SystemMovementMode.Approach),
            Decision,
            CommitDirection.None,
            8_000));
        Assert.Throws<EngineInvariantException>(() => CreateFighter().CommitSystemAction(
            MovementAction(SystemMovementMode.Approach, active: 0),
            Decision,
            CommitDirection.Right,
            8_000));
        Assert.Throws<EngineInvariantException>(() => CreateFighter(moveSpeed: 0).CommitSystemAction(
            MovementAction(SystemMovementMode.Approach),
            Decision,
            CommitDirection.Right,
            8_000));
    }

    [Fact]
    public void WP07_LIFE_002_LifecycleCoversStartupActiveRecoveryAndZeroRecovery()
    {
        var fighter = CreateFighter();
        fighter.CommitSystemAction(
            MovementAction(SystemMovementMode.Approach, startup: 2, active: 3, recovery: 2),
            Decision,
            CommitDirection.Right,
            8_000);

        Assert.Null(fighter.AdvanceMovementLifecycle());
        Assert.Equal(1, fighter.StateTicksRemaining);
        var startup = fighter.AdvanceMovementLifecycle();
        Assert.Equal(ActionPhase.Startup, startup?.FromPhase);
        Assert.Equal(ActionPhase.Active, startup?.ToPhase);
        Assert.Equal(3, fighter.StateTicksRemaining);
        Assert.True(fighter.IsActiveMovement);

        Assert.Null(fighter.AdvanceMovementLifecycle());
        Assert.Equal(2, fighter.StateTicksRemaining);
        fighter.SetActionTimerForTesting(1);
        Assert.Null(fighter.AdvanceMovementLifecycle());
        Assert.Equal(1, fighter.StateTicksRemaining);

        fighter.MarkMovementStarted(EventId.FromSequence(1));
        fighter.CompleteMovement(EventId.FromSequence(2));
        Assert.False(fighter.IsActiveMovement);
        var recovery = fighter.AdvanceMovementLifecycle();
        Assert.Equal(ActionPhase.Active, recovery?.FromPhase);
        Assert.Equal(ActionPhase.Recovery, recovery?.ToPhase);
        Assert.Equal(2, fighter.StateTicksRemaining);
        Assert.Null(fighter.AdvanceMovementLifecycle());
        Assert.Equal(1, fighter.StateTicksRemaining);
        var completed = fighter.AdvanceMovementLifecycle();
        Assert.Equal(ActionPhase.Recovery, completed?.FromPhase);
        Assert.Null(completed?.ToPhase);
        Assert.True(fighter.IsDecisionReady);

        var noRecovery = CreateFighter();
        noRecovery.CommitSystemAction(
            MovementAction(SystemMovementMode.Retreat, startup: 0, recovery: 0),
            Decision,
            CommitDirection.Left,
            8_000);
        noRecovery.MarkMovementStarted(EventId.FromSequence(3));
        noRecovery.CompleteMovement(EventId.FromSequence(4));
        var direct = noRecovery.AdvanceMovementLifecycle();
        Assert.Equal(ActionPhase.Active, direct?.FromPhase);
        Assert.Null(direct?.ToPhase);
        Assert.True(noRecovery.IsDecisionReady);
    }

    [Fact]
    public void WP07_LIFE_004_CorruptMovementLifecycleAndUnsupportedPhaseFailInvariant()
    {
        var corruptions = new Action<FighterRuntimeState>[]
        {
            fighter => fighter.SetActionIdForTesting(null),
            fighter => fighter.SetActiveDecisionIdForTesting(null),
            fighter => fighter.SetActionPhaseForTesting(null),
            fighter => fighter.SetActionTimerForTesting(null),
            fighter => fighter.SetActionTimerForTesting(0),
        };

        foreach (var corrupt in corruptions)
        {
            var fighter = CommittedActiveFighter();
            corrupt(fighter);
            Assert.Throws<EngineInvariantException>(() => fighter.AdvanceMovementLifecycle());
        }

        var unsupported = CommittedActiveFighter();
        unsupported.SetActionPhaseForTesting(ActionPhase.Hold);
        Assert.Throws<EngineInvariantException>(() => unsupported.AdvanceMovementLifecycle());
    }

    [Fact]
    public void WP07_LIFE_004_MovementStartAndCompletionMarkersHaveStrictGuards()
    {
        Assert.Throws<EngineInvariantException>(() =>
            CreateFighter().MarkMovementStarted(EventId.FromSequence(0)));
        Assert.Throws<EngineInvariantException>(() =>
            CreateFighter().CompleteMovement(EventId.FromSequence(0)));

        var notStarted = CommittedActiveFighter();
        Assert.Throws<EngineInvariantException>(() =>
            notStarted.CompleteMovement(EventId.FromSequence(1)));

        var fighter = CommittedActiveFighter();
        fighter.MarkMovementStarted(EventId.FromSequence(2));
        Assert.Throws<EngineInvariantException>(() =>
            fighter.MarkMovementStarted(EventId.FromSequence(3)));
        fighter.CompleteMovement(EventId.FromSequence(4));
        Assert.Throws<EngineInvariantException>(() =>
            fighter.CompleteMovement(EventId.FromSequence(5)));
    }

    private static FighterRuntimeState CommittedActiveFighter()
    {
        var fighter = CreateFighter();
        fighter.CommitSystemAction(
            MovementAction(SystemMovementMode.Approach, startup: 0),
            Decision,
            CommitDirection.Right,
            8_000);
        return fighter;
    }

    private static FighterRuntimeState CreateFighter(int moveSpeed = 82) => new(
        FighterId.FighterA,
        FighterSide.A,
        new StableId("bear"),
        4_000,
        Facing.Right,
        1_650,
        1_000,
        new StableId("rage"),
        0,
        1_000,
        260,
        85,
        moveSpeed,
        520);

    private static SystemActionDefinition MovementAction(
        SystemMovementMode mode,
        int startup = 1,
        int active = 5,
        int recovery = 1) => new(
        mode == SystemMovementMode.Approach
            ? new StableId("sys_approach")
            : new StableId("sys_retreat"),
        650,
        0,
        0,
        startup,
        active,
        recovery,
        0,
        mode,
        0,
        1_500,
        true);

    private static SystemActionDefinition WaitAction() => new(
        new StableId("sys_wait"),
        150,
        0,
        0,
        0,
        3,
        0,
        0,
        SystemMovementMode.None,
        0,
        10_000,
        false);
}
