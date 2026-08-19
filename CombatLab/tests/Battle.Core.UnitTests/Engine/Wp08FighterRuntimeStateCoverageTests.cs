using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Engine;

[Trait("Category", "WP08")]
[Trait("WorkPackage", "WP08")]
public sealed class Wp08FighterRuntimeStateCoverageTests
{
    [Fact]
    public void CombatCommitRejectsNullBusyAndSelfTargetDescriptors()
    {
        var actor = CreateFighter(FighterId.FighterA);
        var opponent = CreateFighter(FighterId.FighterB);

        Assert.Throws<ArgumentNullException>(() => actor.CommitCombatAction(null!));

        actor.CommitSystemWait(new StableId("fixture_busy"), activeTicks: 1);
        var busyDescriptor = Descriptor(actor, opponent);
        AssertInvalidTransition(() => actor.CommitCombatAction(busyDescriptor));

        var freshActor = CreateFighter(FighterId.FighterA);
        var selfTargetDescriptor = Descriptor(freshActor, freshActor);
        AssertInvalidTransition(() => freshActor.CommitCombatAction(selfTargetDescriptor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CombatLifecycleRejectsEveryIncompleteIdentityOrTimerCombination(int corruption)
    {
        var fighter = CommittedCombat(startupTicks: 1, activeTicks: 1, recoveryTicks: 1);
        switch (corruption)
        {
            case 0:
                fighter.SetActionIdForTesting(null);
                break;
            case 1:
                fighter.SetActiveDecisionIdForTesting(null);
                break;
            case 2:
                fighter.SetActionPhaseForTesting(null);
                break;
            case 3:
                fighter.SetActionTimerForTesting(null);
                break;
            case 4:
                fighter.SetActionTimerForTesting(0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        AssertInvalidTransition(() => fighter.AdvanceMovementLifecycle());
    }

    [Fact]
    public void CombatLifecycleRejectsUnknownPhaseAndDecrementsLongRecovery()
    {
        var corrupt = CommittedCombat(startupTicks: 1, activeTicks: 1, recoveryTicks: 1);
        corrupt.SetActionPhaseForTesting((ActionPhase)int.MaxValue);
        AssertInvalidTransition(() => corrupt.AdvanceMovementLifecycle());

        var recovering = CommittedCombat(startupTicks: 0, activeTicks: 1, recoveryTicks: 2);
        var toRecovery = Assert.IsType<ActionLifecycleTransition>(recovering.AdvanceMovementLifecycle());
        Assert.Equal(ActionPhase.Recovery, toRecovery.ToPhase);

        Assert.Null(recovering.AdvanceMovementLifecycle());
        Assert.Equal(1, recovering.StateTicksRemaining);

        var completed = Assert.IsType<ActionLifecycleTransition>(recovering.AdvanceMovementLifecycle());
        Assert.Null(completed.ToPhase);
        Assert.True(recovering.IsDecisionReady);
    }

    [Fact]
    public void CombatCommitAndResourceMutationGuardsRejectInvalidInput()
    {
        var fighter = CreateFighter(FighterId.FighterA);

        AssertInvalidTransition(() => fighter.RecordCombatCommit(EventId.FromSequence(1)));
        AssertInvalidTransition(() => fighter.ApplyEnergyCost(-1));
        AssertInvalidTransition(() => fighter.ApplyEnergyCost(fighter.MaximumEnergy + 1));
        AssertInvalidTransition(() => fighter.ApplyUniqueResourceCost(-1));
        AssertInvalidTransition(() => fighter.ApplyUniqueResourceCost(fighter.MaximumResource + 1));
        Assert.Throws<ArgumentException>(() =>
            fighter.RecordCommittedHistory(new StableId("fixture_action"), string.Empty));
        Assert.Throws<ArgumentNullException>(() => fighter.UpdateOpportunityDebts(
            null!,
            Array.Empty<StableId>(),
            new StableId("fixture_action")));
        Assert.Throws<ArgumentNullException>(() => fighter.UpdateOpportunityDebts(
            Array.Empty<StableId>(),
            null!,
            new StableId("fixture_action")));
        AssertInvalidTransition(() =>
            fighter.CommitDecisionId(new DecisionId("dec-fighter_a-000002")));
    }

    [Fact]
    public void TestingHooksRejectBothSidesOfEveryRangeGuardAndExerciseRemovalPaths()
    {
        var fighter = CreateFighter(FighterId.FighterA);
        var actionId = new StableId("fixture_action");

        Assert.Throws<ArgumentOutOfRangeException>(() => fighter.SetEnergyForTesting(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fighter.SetEnergyForTesting(fighter.MaximumEnergy + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => fighter.SetResourceForTesting(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fighter.SetResourceForTesting(fighter.MaximumResource + 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fighter.SetCooldownForTesting(actionId, -1));
        fighter.SetCooldownForTesting(actionId, 1);
        fighter.SetCooldownForTesting(actionId, 0);
        Assert.Equal(0, fighter.CooldownFor(actionId));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            fighter.SetOpportunityDebtForTesting(actionId, -1));
        fighter.SetOpportunityDebtForTesting(actionId, 1);
        fighter.SetOpportunityDebtForTesting(actionId, 0);
        Assert.Equal(0, fighter.OpportunityDebtFor(actionId));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void DecisionHistoryTestingHookRejectsEachNegativeCounter(
        int decisionCount,
        int sameActionStreak,
        int sameCategoryStreak)
    {
        var fighter = CreateFighter(FighterId.FighterA);

        Assert.Throws<ArgumentOutOfRangeException>(() => fighter.SetDecisionHistoryForTesting(
            decisionCount,
            null,
            null,
            sameActionStreak,
            sameCategoryStreak));
    }

    private static FighterRuntimeState CommittedCombat(
        int startupTicks,
        int activeTicks,
        int recoveryTicks)
    {
        var actor = CreateFighter(FighterId.FighterA);
        var target = CreateFighter(FighterId.FighterB);
        actor.CommitCombatAction(Descriptor(
            actor,
            target,
            startupTicks,
            activeTicks,
            recoveryTicks));
        return actor;
    }

    private static CombatActionDescriptor Descriptor(
        FighterRuntimeState actor,
        FighterRuntimeState target,
        int startupTicks = 1,
        int activeTicks = 1,
        int recoveryTicks = 1) =>
        Wp08EngineTestFixture.Descriptor(
            actor,
            target,
            actor.PeekNextDecisionId(),
            actionId: "fixture_runtime_coverage",
            startupTicks,
            activeTicks,
            recoveryTicks);

    private static FighterRuntimeState CreateFighter(FighterId fighterId) => new(
        fighterId,
        fighterId == FighterId.FighterA ? FighterSide.A : FighterSide.B,
        new StableId(fighterId == FighterId.FighterA ? "bear" : "kangaroo"),
        fighterId == FighterId.FighterA ? 2_000 : 4_500,
        fighterId == FighterId.FighterA ? Facing.Right : Facing.Left,
        maximumHealth: 1_650,
        maximumEnergy: 1_000,
        new StableId("fixture_resource"),
        resource: 500,
        maximumResource: 1_000,
        staggerThreshold: 260,
        initiative: 85,
        actionSpeed: 85,
        moveSpeed: 82,
        collisionRadius: 520);

    private static void AssertInvalidTransition(Action action)
    {
        var failure = Assert.Throws<EngineInvariantException>(action);
        Assert.Equal(EngineFailureCodes.InvalidStateTransition, failure.Code);
    }
}
