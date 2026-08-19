using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Decisions;

public sealed class DecisionWeightTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_001_ReferenceVectorFloorsAfterEveryCanonicalStage()
    {
        var vector = new[] { 1_250, 800, 1_100, 900, 550, 1_500 };
        var expected = new[] { 1_250, 1_000, 1_100, 990, 544, 816 };
        var current = 1_000;
        for (var index = 0; index < vector.Length; index++)
        {
            current = global::Battle.Core.Math.FixedMath.Mul(current, vector[index], 1_000);
            Assert.Equal(expected[index], current);
        }

        var score = Calculate(DecisionTestFixture.Action(), new DecisionStageMultipliers(
            vector[0], vector[1], vector[2], vector[3], vector[4], vector[5]));

        Assert.Equal(816, score.FinalWeight);
        Assert.Equal(
            new[] { "Tactic", "Situation", "Synergy", "Counter", "Variety", "Opportunity" },
            score.Modifiers.Select(item => item.Code.Value));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_002_PermutingStagesChangesTheFlooredResult()
    {
        var action = DecisionTestFixture.Action();
        var canonical = Calculate(action, new DecisionStageMultipliers(1_250, 800, 1_100, 900, 550, 1_500));
        var reverse = Calculate(action, new DecisionStageMultipliers(1_500, 550, 900, 1_100, 800, 1_250));

        Assert.Equal(816, canonical.FinalWeight);
        Assert.Equal(815, reverse.FinalWeight);
        Assert.NotEqual(canonical.FinalWeight, reverse.FinalWeight);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_003_EachStageMultiplierUsesExactGlobalClamp()
    {
        var score = Calculate(
            DecisionTestFixture.Action(),
            new DecisionStageMultipliers(0, 4_000, 1_000, 1_000, 1_000, 1_000));

        Assert.Equal(750, score.FinalWeight);
        Assert.Equal(250, score.Modifiers[0].MultiplierFixedPoint);
        Assert.Equal(3_000, score.Modifiers[1].MultiplierFixedPoint);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_004_FinalWeightClampsWithoutWrapping()
    {
        var settings = new DecisionWeightSettings(1_000, 250, 3_000, 1_000);
        var score = Calculate(
            DecisionTestFixture.Action(),
            new DecisionStageMultipliers(3_000, 1_000, 1_000, 1_000, 1_000, 1_000),
            settings);

        Assert.Equal(1_000, score.FinalWeight);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_005_ZeroBaseRemainsZeroAcrossPositiveStages()
    {
        var score = Calculate(
            DecisionTestFixture.Action(baseWeight: 0),
            new DecisionStageMultipliers(3_000, 3_000, 3_000, 3_000, 3_000, 3_000));

        Assert.Equal(0, score.FinalWeight);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_006_CorruptIntermediateOverflowFailsInvariantWithoutClampRepair()
    {
        var action = DecisionTestFixture.Action(baseWeight: int.MaxValue);
        var settings = new DecisionWeightSettings(1_000, 250, 3_000, int.MaxValue);

        var exception = Assert.Throws<EngineInvariantException>(() => Calculate(
            action,
            new DecisionStageMultipliers(3_000, 1_000, 1_000, 1_000, 1_000, 1_000),
            settings));

        Assert.Equal(DecisionFailureCodes.DecisionArithmeticOverflow, exception.Code);
        Assert.Equal("Decisions", exception.Phase);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_007_PressureLightTagProducesPawJabWeight1150()
    {
        var action = DecisionTestFixture.Action("bear_paw_jab", tags: new[] { "light", "strike" });
        var tactic = PressureTactic();
        var tacticMultiplier = DecisionTacticMultiplierCalculator.Calculate(
            action,
            tactic,
            DecisionTestFixture.WeightSettings);
        var score = Calculate(
            action,
            new DecisionStageMultipliers(tacticMultiplier, 1_000, 1_000, 1_000, 1_000, 1_000));

        Assert.Equal(1_150, tacticMultiplier);
        Assert.Equal(1_150, score.FinalWeight);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_008_PressureRetreatFloors450Times750To337()
    {
        var action = DecisionTestFixture.System("sys_retreat", DecisionMovementMode.Retreat, 450);
        action = DecisionTestFixture.Action(
            "sys_retreat",
            slot: DecisionActionSlot.System,
            category: "Movement",
            movementMode: DecisionMovementMode.Retreat,
            tags: new[] { "retreat", "system" },
            baseWeight: 450,
            hitRangeMaximum: 10_000);
        var tacticMultiplier = DecisionTacticMultiplierCalculator.Calculate(
            action,
            PressureTactic(),
            DecisionTestFixture.WeightSettings);
        var score = Calculate(
            action,
            new DecisionStageMultipliers(tacticMultiplier, 1_000, 1_000, 1_000, 1_000, 1_000));

        Assert.Equal(750, tacticMultiplier);
        Assert.Equal(337, score.FinalWeight);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_009_MultiTagTacticFoldUsesTheApprovedSuborder()
    {
        var action = DecisionTestFixture.Action(
            tags: new[] { "approach", "block", "heavy", "rage_generator", "retreat", "signature" },
            resourceCost: 1);
        var tactic = new DecisionTacticProfile(
            1_101, 1_102, 1_103, 1_104, 1_105, 1_106,
            1_107, 1_108, 1_109, 1_110, 1_111, 1_112,
            1_113, 1_114, 1_115, 1_116, 5);
        var expected = DecisionMultiplierFolder.Fold(
            new[] { 1_101, 1_102, 1_105, 1_107, 1_108, 1_109, 1_110 },
            DecisionTestFixture.WeightSettings);

        var actual = DecisionTacticMultiplierCalculator.Calculate(
            action,
            tactic,
            DecisionTestFixture.WeightSettings);

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_010_SituationPredicatesActivateAtExactHealthWallAndRecoveryBoundaries()
    {
        var action = DecisionTestFixture.Action(tags: new[] { "position", "retreat" });
        var actor = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            1_720,
            health: 350,
            maximumHealth: 1_000);
        var target = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            8_280,
            state: FighterState.Recovery);
        var tactic = new DecisionTacticProfile(
            1_000, 1_000, 1_000, 1_000, 1_000, 1_000,
            1_000, 1_000, 1_000, 1_000, 1_000,
            1_100, 1_200, 1_300, 1_400, 1_000, 5);
        var expected = DecisionMultiplierFolder.Fold(
            new[] { 1_100, 1_200, 1_300, 1_400 },
            DecisionTestFixture.WeightSettings);

        var actual = DecisionSituationMultiplierCalculator.Calculate(
            action,
            actor,
            target,
            tactic,
            DecisionTestFixture.WeightSettings,
            350,
            0,
            10_000,
            1_200);

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_011_PassiveAndGearSynergyRequiresExactTagIntersectionInSlotOrder()
    {
        var action = DecisionTestFixture.Action(tags: new[] { "strike" });
        var passive = TagProfile(1_100, "strike");
        var offense = TagProfile(1_000, "strike");
        var defense = TagProfile(1_200, "strike");
        var utility = TagProfile(2_000, "striker");
        var expected = DecisionMultiplierFolder.Fold(
            new[] { 1_100, 1_000, 1_200 },
            DecisionTestFixture.WeightSettings);

        var actual = DecisionSynergyMultiplierCalculator.Calculate(
            action,
            passive,
            offense,
            defense,
            utility,
            DecisionTestFixture.WeightSettings);

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_012_CounterMultiplierUsesOnlyObservedPublicTelegraph()
    {
        var action = DecisionTestFixture.Action(tags: new[] { "counter" });
        var tactic = PressureTactic() with { CounterFixedPoint = 1_450 };

        Assert.Equal(1_450, DecisionCounterMultiplierCalculator.Calculate(action, tactic, true, 1_000));
        Assert.Equal(1_000, DecisionCounterMultiplierCalculator.Calculate(action, tactic, false, 1_000));
        Assert.Equal(1_000, DecisionCounterMultiplierCalculator.Calculate(
            DecisionTestFixture.Action(tags: new[] { "strike" }),
            tactic,
            true,
            1_000));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_WGT_013_IllegalCandidateHasNoStagesAndCannotContributeWeight()
    {
        var action = DecisionTestFixture.Action(hitRangeMinimum: 2_000, hitRangeMaximum: 3_000);
        var evaluation = DecisionAvailabilityEvaluator.Evaluate(
            action,
            DecisionTestFixture.Context(new[] { action }));

        var score = DecisionWeightCalculator.Calculate(
            evaluation,
            new DecisionStageMultipliers(3_000, 3_000, 3_000, 3_000, 3_000, 3_000),
            DecisionTestFixture.WeightSettings,
            hardOpportunityReady: true);

        Assert.False(score.Legal);
        Assert.Empty(score.Modifiers);
        Assert.Equal(0, score.FinalWeight);
        Assert.False(score.HardOpportunityReady);
        Assert.Equal(DecisionRejectionCodes.OutOfDecisionRange, score.FirstRejectionCode);
    }

    private static CandidateScore Calculate(
        DecisionActionProfile action,
        DecisionStageMultipliers multipliers,
        DecisionWeightSettings? settings = null) => DecisionWeightCalculator.Calculate(
            DecisionTestFixture.LegalEvaluation(action, SnapshotFor(action)),
            multipliers,
            settings ?? DecisionTestFixture.WeightSettings);

    private static DecisionBatchSnapshot SnapshotFor(DecisionActionProfile action)
    {
        var targetPosition = action.Slot == DecisionActionSlot.System &&
                             action.MovementMode == DecisionMovementMode.Retreat
            ? 5_540
            : 5_540;
        return DecisionTestFixture.Snapshot(
            fighterB: DecisionTestFixture.Fighter(FighterId.FighterB, targetPosition));
    }

    private static DecisionTacticProfile PressureTactic() => DecisionTestFixture.NeutralTactic with
    {
        ApproachFixedPoint = 1_250,
        HeavyFixedPoint = 1_100,
        LightFixedPoint = 1_150,
        RetreatFixedPoint = 750,
        SignatureFixedPoint = 1_050,
        RepeatPenaltyFixedPoint = 850,
    };

    private static DecisionTagMultiplierProfile TagProfile(int value, params string[] tags) => new(
        tags.Select(tag => new StableId(tag)),
        value);
}
