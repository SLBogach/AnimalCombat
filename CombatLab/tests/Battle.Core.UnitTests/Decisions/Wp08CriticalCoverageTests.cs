using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;

namespace Battle.Core.UnitTests.Decisions;

public sealed class Wp08AvailabilityCoverageTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void AvailabilityPublicEntryPointsRejectNullArguments()
    {
        var action = DecisionTestFixture.Action();
        var context = DecisionTestFixture.Context(new[] { action });

        Assert.Throws<ArgumentNullException>(() =>
            DecisionAvailabilityEvaluator.Evaluate(null!, context));
        Assert.Throws<ArgumentNullException>(() =>
            DecisionAvailabilityEvaluator.Evaluate(action, null!));
        Assert.Throws<ArgumentNullException>(() =>
            DecisionAvailabilityEvaluator.EvaluateCatalog(null!, context));
        Assert.Throws<ArgumentNullException>(() =>
            DecisionAvailabilityEvaluator.EvaluateCatalog(new[] { action }, null!));
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void CatalogRepeatFilteringKeepsIllegalItemsAndHandlesNoBaseLegalItems()
    {
        var capped = DecisionTestFixture.Action("action_capped", maximumConsecutiveUses: 1);
        var alternative = DecisionTestFixture.Action("action_alternative");
        var outOfRange = DecisionTestFixture.Action(
            "action_out_of_range",
            hitRangeMinimum: 9_000,
            hitRangeMaximum: 10_000);
        var actor = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            history: new DecisionRepeatHistory(capped.Id, capped.Category, 1, 1));
        var actions = new[] { capped, alternative, outOfRange };

        var partial = DecisionAvailabilityEvaluator.EvaluateCatalog(
            actions,
            DecisionTestFixture.Context(
                actions,
                DecisionTestFixture.Snapshot(fighterA: actor)));

        Assert.Equal(DecisionRejectionCodes.MaxConsecutiveUses,
            partial.Single(item => item.Action.Id == capped.Id).FirstRejectionCode);
        Assert.True(partial.Single(item => item.Action.Id == alternative.Id).Legal);
        Assert.Equal(DecisionRejectionCodes.OutOfDecisionRange,
            partial.Single(item => item.Action.Id == outOfRange.Id).FirstRejectionCode);

        var noneLegal = DecisionAvailabilityEvaluator.EvaluateCatalog(
            new[] { outOfRange },
            DecisionTestFixture.Context(new[] { outOfRange }));
        Assert.False(Assert.Single(noneLegal).Legal);
    }

    [Theory]
    [InlineData((int)DecisionMovementMode.None, 500, 0, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Approach, 1_001, 0, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Approach, 1_000, 0, 1_000, false)]
    [InlineData((int)DecisionMovementMode.Adaptive, 499, 500, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Adaptive, 1_001, 500, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Adaptive, 750, 500, 1_000, false)]
    [InlineData((int)DecisionMovementMode.Pull, 500, 0, 1_000, false)]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void SelfTargetRangeModesCoverEveryDecisionBranch(
        int movementModeValue,
        int surfaceGap,
        int preferredMinimum,
        int preferredMaximum,
        bool expectedLegal)
    {
        var movementMode = (DecisionMovementMode)movementModeValue;
        var action = DecisionTestFixture.Action(
            id: "bear_self",
            targetKind: DecisionTargetKind.Self,
            movementMode: movementMode,
            preferredRangeMinimum: preferredMinimum,
            preferredRangeMaximum: preferredMaximum,
            hitRangeMaximum: 10_000);

        var result = EvaluateAtGap(action, surfaceGap);

        Assert.Equal(expectedLegal, result.Legal);
    }

    [Theory]
    [InlineData((int)DecisionMovementMode.Approach, 499, 500, 1_000, false)]
    [InlineData((int)DecisionMovementMode.Approach, 500, 500, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Approach, 1_001, 500, 1_000, false)]
    [InlineData((int)DecisionMovementMode.Follow, 500, 500, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Push, 500, 500, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Pull, 500, 500, 1_000, true)]
    [InlineData((int)DecisionMovementMode.Swap, 500, 500, 1_000, true)]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void OpponentTargetMovementModesCoverPreferredAndHitRangeBranches(
        int movementModeValue,
        int surfaceGap,
        int rangeMinimum,
        int rangeMaximum,
        bool expectedLegal)
    {
        var movementMode = (DecisionMovementMode)movementModeValue;
        var action = DecisionTestFixture.Action(
            id: "bear_opponent",
            movementMode: movementMode,
            preferredRangeMinimum: rangeMinimum,
            preferredRangeMaximum: rangeMaximum,
            hitRangeMinimum: rangeMinimum,
            hitRangeMaximum: rangeMaximum);

        var result = EvaluateAtGap(action, surfaceGap);

        Assert.Equal(expectedLegal, result.Legal);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void OverlapRightFacingAndUnsupportedSystemModeAreEvaluatedDeterministically()
    {
        var overlap = DecisionTestFixture.Action(hitRangeMinimum: 0, hitRangeMaximum: 0);
        var samePosition = DecisionTestFixture.Fighter(FighterId.FighterB, 4_000);
        Assert.True(DecisionAvailabilityEvaluator.Evaluate(
            overlap,
            DecisionTestFixture.Context(
                new[] { overlap },
                DecisionTestFixture.Snapshot(fighterB: samePosition))).Legal);

        var unsupportedSystem = DecisionTestFixture.System(
            "sys_adaptive_probe",
            DecisionMovementMode.Adaptive);
        var evaluation = DecisionAvailabilityEvaluator.Evaluate(
            unsupportedSystem,
            DecisionTestFixture.Context(new[] { unsupportedSystem }));
        Assert.Equal(DecisionRejectionCodes.SystemBandUnavailable, evaluation.FirstRejectionCode);

        var leftOpponent = DecisionTestFixture.Fighter(FighterId.FighterB, 2_460);
        var pushLeft = DecisionTestFixture.Action(
            "bear_push_left",
            movementMode: DecisionMovementMode.Push,
            hitRangeMaximum: 1_000);
        Assert.True(DecisionAvailabilityEvaluator.Evaluate(
            pushLeft,
            DecisionTestFixture.Context(
                new[] { pushLeft },
                DecisionTestFixture.Snapshot(fighterB: leftOpponent))).Legal);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void RepeatCapCoversNullAndBelowThresholdInputs()
    {
        var action = DecisionTestFixture.Action(maximumConsecutiveUses: 2);

        Assert.Throws<ArgumentNullException>(() =>
            DecisionVariety.IsAtRepeatCap(null!, DecisionRepeatHistory.Empty));
        Assert.Throws<ArgumentNullException>(() =>
            DecisionVariety.IsAtRepeatCap(action, null!));
        Assert.False(DecisionVariety.IsAtRepeatCap(
            action,
            new DecisionRepeatHistory(action.Id, action.Category, 1, 1)));
        Assert.False(DecisionVariety.IsAtRepeatCap(
            action,
            new DecisionRepeatHistory(new StableId("other"), action.Category, 2, 2)));
    }

    private static DecisionCandidateEvaluation EvaluateAtGap(
        DecisionActionProfile action,
        int surfaceGap)
    {
        var opponent = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            4_000 + 520 + 520 + surfaceGap);
        return DecisionAvailabilityEvaluator.Evaluate(
            action,
            DecisionTestFixture.Context(
                new[] { action },
                DecisionTestFixture.Snapshot(fighterB: opponent)));
    }
}

public sealed class Wp08SelectorCoverageTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void SelectorRejectsNullNullElementAndDuplicateCandidates()
    {
        var score = DecisionTestFixture.Score("action_unique", 1);

        Assert.Throws<ArgumentNullException>(() =>
            DecisionSelector.Select(null!, emergency: false, drawSource: null));
        Assert.Throws<ArgumentException>(() =>
            DecisionSelector.Select(new CandidateScore[] { null! }, false, null));
        Assert.Throws<ArgumentException>(() =>
            DecisionSelector.Select(new[] { score, score }, false, null));
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WeightedSelectionRequiresDrawSource()
    {
        var candidates = new[]
        {
            DecisionTestFixture.Score("action_a", 1),
            DecisionTestFixture.Score("action_b", 1),
        };

        Assert.Throws<ArgumentNullException>(() =>
            DecisionSelector.Select(candidates, emergency: true, drawSource: null));
    }
}
