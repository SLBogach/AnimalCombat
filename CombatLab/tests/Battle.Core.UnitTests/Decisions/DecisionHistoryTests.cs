using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;

namespace Battle.Core.UnitTests.Decisions;

public sealed class DecisionVarietyTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_VAR_001_SameActionAndCategoryFoldActionCategoryThenTactic()
    {
        var action = DecisionTestFixture.Action("bear_light", category: "Light");
        var history = new DecisionRepeatHistory(action.Id, "Light", 1, 1);

        var result = DecisionVariety.Calculate(action, history, 1_000, 550, 800, 850);

        Assert.True(result.SameAction);
        Assert.True(result.SameCategory);
        Assert.Equal(374, result.MultiplierFixedPoint);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_VAR_002_DifferentActionSameCategoryUsesCategoryAndTacticOnly()
    {
        var action = DecisionTestFixture.Action("bear_light_b", category: "Light");
        var history = new DecisionRepeatHistory(new StableId("bear_light_a"), "Light", 1, 2);

        var result = DecisionVariety.Calculate(action, history, 1_000, 550, 800, 850);

        Assert.False(result.SameAction);
        Assert.True(result.SameCategory);
        Assert.Equal(680, result.MultiplierFixedPoint);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_VAR_003_DifferentCategoryUsesIdentityAndCommitResetsBothStreaks()
    {
        var action = DecisionTestFixture.Action("bear_heavy", category: "Heavy");
        var history = new DecisionRepeatHistory(new StableId("bear_light"), "Light", 3, 3);

        var result = DecisionVariety.Calculate(action, history, 1_000, 550, 800, 850);
        var next = DecisionVariety.AfterCommit(history, action.Id, action.Category);

        Assert.Equal(1_000, result.MultiplierFixedPoint);
        Assert.False(result.SameAction);
        Assert.False(result.SameCategory);
        Assert.Equal(1, next.ConsecutiveActionUses);
        Assert.Equal(1, next.ConsecutiveCategoryUses);
        Assert.Equal("Heavy", next.LastCategory);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_VAR_004_CappedCandidateIsRejectedWhenNonCappedAlternativeExists()
    {
        var capped = DecisionTestFixture.Action("action_capped", maximumConsecutiveUses: 2);
        var alternative = DecisionTestFixture.Action("action_other");
        var history = new DecisionRepeatHistory(capped.Id, capped.Category, 2, 2);
        var actor = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            history: history);
        var actions = new[] { capped, alternative };

        var results = DecisionAvailabilityEvaluator.EvaluateCatalog(
            actions,
            DecisionTestFixture.Context(
                actions,
                DecisionTestFixture.Snapshot(fighterA: actor)));

        var cappedResult = results.Single(item => item.Action.Id == capped.Id);
        Assert.False(cappedResult.Legal);
        Assert.Equal(DecisionRejectionCodes.MaxConsecutiveUses, cappedResult.FirstRejectionCode);
        Assert.True(results.Single(item => item.Action.Id == alternative.Id).Legal);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_VAR_005_SoleBaseLegalCappedCandidateRemainsLegalAndPenalized()
    {
        var action = DecisionTestFixture.Action("action_capped", maximumConsecutiveUses: 1);
        var history = new DecisionRepeatHistory(action.Id, action.Category, 1, 1);
        var actor = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            history: history);

        var result = Assert.Single(DecisionAvailabilityEvaluator.EvaluateCatalog(
            new[] { action },
            DecisionTestFixture.Context(
                new[] { action },
                DecisionTestFixture.Snapshot(fighterA: actor))));
        var variety = DecisionVariety.Calculate(action, history, 1_000, 550, 800, 850);

        Assert.True(result.Legal);
        Assert.Equal(374, variety.MultiplierFixedPoint);
    }
}

public sealed class DecisionOpportunityTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_001_LegalUnselectedSpecialIncrementsDebtOnce()
    {
        Assert.Equal(4, DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            3,
            fullyLegal: true,
            selected: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_002_IllegalSpecialNeverChangesDebt(bool selected)
    {
        Assert.Equal(3, DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            3,
            fullyLegal: false,
            selected));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_003_SelectedCommitResetsDebtAndLaterInterruptionCannotRefundIt()
    {
        var afterCommit = DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            4,
            fullyLegal: true,
            selected: true);

        Assert.Equal(0, afterCommit);
        Assert.Equal(0, DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            afterCommit,
            fullyLegal: false,
            selected: false));
    }

    [Theory]
    [InlineData(0, 1_000)]
    [InlineData(1, 1_250)]
    [InlineData(2, 1_500)]
    [InlineData(4, 2_000)]
    [InlineData(20, 2_200)]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_004_GrowthUsesDebtAndMinimumActionGlobalCap(int debt, int expected)
    {
        Assert.Equal(expected, DecisionOpportunity.CalculateMultiplier(
            debt,
            actionCapFixedPoint: 2_200,
            globalCapFixedPoint: 2_500,
            growthFixedPoint: 250,
            fixedPointScale: 1_000));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_005_HardOpportunityActivatesFromFourPriorMisses(int debt, bool expected)
    {
        Assert.Equal(expected, DecisionOpportunity.IsHardReady(debt, 4, 4));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_006_ZeroActionThresholdDisablesHardButNotGrowth()
    {
        Assert.False(DecisionOpportunity.IsHardReady(100, 0, 4));
        Assert.Equal(2_500, DecisionOpportunity.CalculateMultiplier(100, 2_500, 2_500, 250, 1_000));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_007_EmergencySuppressesHardAndUsesOrdinaryWeightedDraw()
    {
        var draw = new FixedDecisionDrawSource(0);
        var result = DecisionSelector.Select(
            new[]
            {
                DecisionTestFixture.Score("action_a", 1),
                DecisionTestFixture.Score("action_hard", 100, hard: true, debt: 10),
            },
            emergency: true,
            draw);

        Assert.Equal(DecisionSelectionMode.WeightedRng, result.SelectionMode);
        Assert.Equal("action_a", result.ActionId.Value);
        Assert.Equal(1, draw.CallCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_OPP_008_PerActorDebtUpdatesAreIndependentAfterSharedSelection()
    {
        var debtA = 1;
        var debtB = 4;
        var nextA = DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            debtA,
            fullyLegal: true,
            selected: false);
        var nextB = DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            debtB,
            fullyLegal: true,
            selected: true);

        Assert.Equal(2, nextA);
        Assert.Equal(0, nextB);
        Assert.Equal(1, debtA);
        Assert.Equal(4, debtB);
    }
}
