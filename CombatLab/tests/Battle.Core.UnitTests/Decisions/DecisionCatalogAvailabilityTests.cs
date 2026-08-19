using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Decisions;

public sealed class DecisionCatalogTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void DecisionActionProfileRejectsMoreThanThirtyTwoScheduleEntries()
    {
        var ticks = Enumerable.Range(0, 33).ToArray();

        var failure = Assert.Throws<ArgumentException>(() => DecisionTestFixture.Action(
            activeTicks: 33,
            hitScheduleTicks: ticks));

        Assert.Equal("hitScheduleTicks", failure.ParamName);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CAT_001_CheckedCatalogContainsSystemAndActorOwnedActionsInOrdinalOrder()
    {
        var actions = new[]
        {
            DecisionTestFixture.Action("gorilla_basic", DecisionTestFixture.GorillaId),
            DecisionTestFixture.Action("bear_special_c", slot: DecisionActionSlot.Special),
            DecisionTestFixture.System("sys_wait", DecisionMovementMode.None),
            DecisionTestFixture.Action("bear_basic_b"),
            DecisionTestFixture.System("sys_retreat", DecisionMovementMode.Retreat),
            DecisionTestFixture.Action("bear_basic_a"),
            DecisionTestFixture.System("sys_approach", DecisionMovementMode.Approach),
        };

        var catalog = DecisionCatalogBuilder.BuildCheckedCatalog(actions, DecisionTestFixture.BearId);

        Assert.Equal(
            new[]
            {
                "bear_basic_a", "bear_basic_b", "bear_special_c",
                "sys_approach", "sys_retreat", "sys_wait",
            },
            catalog.Select(action => action.Id.Value));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CAT_002_CatalogAndEvaluationIgnoreInsertionOrder()
    {
        var basic = DecisionTestFixture.Action("bear_basic");
        var wait = DecisionTestFixture.System("sys_wait", DecisionMovementMode.None);
        var source = new[] { wait, basic };
        var forward = DecisionCatalogBuilder.BuildCheckedCatalog(source, DecisionTestFixture.BearId);
        var reverse = DecisionCatalogBuilder.BuildCheckedCatalog(source.Reverse(), DecisionTestFixture.BearId);
        var context = DecisionTestFixture.Context(source);

        var forwardEvaluation = DecisionAvailabilityEvaluator.EvaluateCatalog(forward, context);
        var reverseEvaluation = DecisionAvailabilityEvaluator.EvaluateCatalog(reverse, context);

        Assert.Equal(forward.Select(action => action.Id), reverse.Select(action => action.Id));
        Assert.Equal(
            forwardEvaluation.Select(item => (item.Action.Id, item.Legal, item.FirstRejectionCode)),
            reverseEvaluation.Select(item => (item.Action.Id, item.Legal, item.FirstRejectionCode)));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CAT_003_OwnerSlotLoadoutAndModeFailuresHaveExactCodes()
    {
        var wrongOwner = DecisionTestFixture.Action("gorilla_basic", DecisionTestFixture.GorillaId);
        var wrongSlot = DecisionTestFixture.Action(DecisionTestFixture.SpecialAId.Value);
        var unselected = DecisionTestFixture.Action("bear_special_c", slot: DecisionActionSlot.Special);
        var modeExcluded = DecisionTestFixture.Action("bear_mode_excluded");
        var allowedOther = DecisionTestFixture.Action("bear_allowed_other");

        AssertRejected(wrongOwner, DecisionTestFixture.Context(new[] { wrongOwner }), DecisionRejectionCodes.WrongOwner);
        AssertRejected(wrongSlot, DecisionTestFixture.Context(new[] { wrongSlot }), DecisionRejectionCodes.WrongSlot);
        AssertRejected(unselected, DecisionTestFixture.Context(new[] { unselected }), DecisionRejectionCodes.ActionNotInLoadout);
        AssertRejected(
            modeExcluded,
            DecisionTestFixture.Context(new[] { allowedOther }),
            DecisionRejectionCodes.ActionNotAllowedByMode);
    }

    private static void AssertRejected(
        DecisionActionProfile action,
        DecisionAvailabilityContext context,
        ReasonCode expected)
    {
        var result = DecisionAvailabilityEvaluator.Evaluate(action, context);
        Assert.False(result.Legal);
        Assert.Equal(expected, result.FirstRejectionCode);
    }
}

public sealed class DecisionAvailabilityTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_001_ActorMustBeDecisionReady()
    {
        var action = DecisionTestFixture.Action();
        var actor = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            state: FighterState.Recovery,
            currentActionId: new StableId("prior_action"));

        var result = Evaluate(action, fighterA: actor);

        AssertRejected(result, DecisionRejectionCodes.ActorNotDecisionReady);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_002_CooldownAndCostsUseExactZeroAndOneUnitBoundaries()
    {
        var action = DecisionTestFixture.Action(energyCost: 500, resourceCost: 400);
        var exact = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            energy: 500,
            resource: 400);
        Assert.True(Evaluate(action, fighterA: exact).Legal);

        var cooldown = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            energy: 500,
            resource: 400,
            cooldowns: new Dictionary<StableId, int> { [action.Id] = 1 });
        AssertRejected(Evaluate(action, fighterA: cooldown), DecisionRejectionCodes.CooldownActive);

        var energyDeficit = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            energy: 499,
            resource: 400);
        AssertRejected(Evaluate(action, fighterA: energyDeficit), DecisionRejectionCodes.InsufficientEnergy);

        var resourceDeficit = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            energy: 500,
            resource: 399);
        AssertRejected(Evaluate(action, fighterA: resourceDeficit), DecisionRejectionCodes.InsufficientResource);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_003_TargetAvailabilityPrecedesRangeAndTelegraph()
    {
        var counter = DecisionTestFixture.Action(tags: new[] { "counter" }, hitRangeMinimum: 10_000, hitRangeMaximum: 10_001);
        var missing = DecisionAvailabilityEvaluator.Evaluate(
            counter,
            DecisionTestFixture.Context(new[] { counter }, targetExists: false));
        AssertRejected(missing, DecisionRejectionCodes.TargetUnavailable);

        var defeated = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            5_540,
            state: FighterState.Defeated);
        AssertRejected(
            Evaluate(counter, fighterB: defeated),
            DecisionRejectionCodes.TargetDefeated);
    }

    [Theory]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(1_000, true)]
    [InlineData(1_001, false)]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_004_OpponentRangeIsBodyAwareAndInclusive(int surfaceGap, bool expectedLegal)
    {
        var action = DecisionTestFixture.Action(hitRangeMinimum: 500, hitRangeMaximum: 1_000);
        var opponent = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            4_000 + 520 + 520 + surfaceGap);

        var result = Evaluate(action, fighterB: opponent);

        Assert.Equal(expectedLegal, result.Legal);
        if (!expectedLegal)
        {
            Assert.Equal(DecisionRejectionCodes.OutOfDecisionRange, result.FirstRejectionCode);
        }
    }

    [Theory]
    [InlineData(520, false)]
    [InlineData(521, true)]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_005_SelfRetreatRequiresOneUnitOfBodyAwareHeadroom(
        int actorPosition,
        bool expectedLegal)
    {
        var action = DecisionTestFixture.Action(
            targetKind: DecisionTargetKind.Self,
            movementMode: DecisionMovementMode.Retreat,
            hitRangeMaximum: 10_000);
        var actor = DecisionTestFixture.Fighter(FighterId.FighterA, actorPosition);
        var opponent = DecisionTestFixture.Fighter(FighterId.FighterB, 5_000);

        var result = Evaluate(action, fighterA: actor, fighterB: opponent);

        Assert.Equal(expectedLegal, result.Legal);
        if (!expectedLegal)
        {
            Assert.Equal(DecisionRejectionCodes.NoMovementHeadroom, result.FirstRejectionCode);
        }
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_006_CounterUsesObservedTelegraphAtExactPerceptionDelay()
    {
        var action = DecisionTestFixture.Action(tags: new[] { "counter" });
        var actor = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            perceptionDelayTicks: 5);
        var tooRecent = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            5_540,
            telegraph: new DecisionTelegraphView(new StableId("opponent_attack"), 6));
        var observed = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            5_540,
            telegraph: new DecisionTelegraphView(new StableId("opponent_attack"), 5));

        AssertRejected(
            Evaluate(action, actor, tooRecent, tick: 10),
            DecisionRejectionCodes.TelegraphNotObserved);
        Assert.True(Evaluate(action, actor, observed, tick: 10).Legal);
        AssertRejected(
            Evaluate(action, actor, DecisionTestFixture.Fighter(FighterId.FighterB, 5_540), tick: 10),
            DecisionRejectionCodes.TelegraphNotObserved);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_007_OnlyEarliestPredicateFailureIsRecorded()
    {
        var action = DecisionTestFixture.Action(energyCost: 1_000, hitRangeMinimum: 9_000, hitRangeMaximum: 10_000);
        var notReady = DecisionTestFixture.Fighter(
            FighterId.FighterA,
            4_000,
            state: FighterState.Recovery,
            currentActionId: new StableId("prior_action"),
            energy: 0);
        var result = DecisionAvailabilityEvaluator.Evaluate(
            action,
            DecisionTestFixture.Context(
                new[] { action },
                DecisionTestFixture.Snapshot(fighterA: notReady),
                categories: new[] { "Heavy" }));

        AssertRejected(result, DecisionRejectionCodes.ActorNotDecisionReady);

        var ready = DecisionTestFixture.Fighter(FighterId.FighterA, 4_000, energy: 0);
        result = DecisionAvailabilityEvaluator.Evaluate(
            action,
            DecisionTestFixture.Context(
                new[] { action },
                DecisionTestFixture.Snapshot(fighterA: ready),
                categories: new[] { "Heavy" }));
        AssertRejected(result, DecisionRejectionCodes.CategoryUnavailable);
    }

    [Theory]
    [InlineData(4_000, 500, "sys_retreat")]
    [InlineData(520, 500, "sys_wait")]
    [InlineData(4_000, 1_550, "sys_wait")]
    [InlineData(4_000, 2_000, "sys_approach")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_008_SystemBandRegressionKeepsExactlyOneLegalAction(
        int actorPosition,
        int surfaceGap,
        string expectedActionId)
    {
        var actions = new[]
        {
            DecisionTestFixture.System("sys_approach", DecisionMovementMode.Approach),
            DecisionTestFixture.System("sys_retreat", DecisionMovementMode.Retreat),
            DecisionTestFixture.System("sys_wait", DecisionMovementMode.None),
        };
        var opponent = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            actorPosition + 520 + 520 + surfaceGap);
        var actor = DecisionTestFixture.Fighter(FighterId.FighterA, actorPosition);
        var result = DecisionAvailabilityEvaluator.EvaluateCatalog(
            actions,
            DecisionTestFixture.Context(
                actions,
                DecisionTestFixture.Snapshot(fighterA: actor, fighterB: opponent)));

        Assert.Equal(expectedActionId, Assert.Single(result, item => item.Legal).Action.Id.Value);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_009_NoLegalCandidateFailsClosedWithoutWeakeningPredicates()
    {
        var action = DecisionTestFixture.Action(hitRangeMinimum: 2_000, hitRangeMaximum: 3_000);
        var evaluations = DecisionAvailabilityEvaluator.EvaluateCatalog(
            new[] { action },
            DecisionTestFixture.Context(new[] { action }));
        var score = DecisionWeightCalculator.Calculate(
            Assert.Single(evaluations),
            new DecisionStageMultipliers(1_000, 1_000, 1_000, 1_000, 1_000, 1_000),
            DecisionTestFixture.WeightSettings);

        var exception = Assert.Throws<EngineInvariantException>(() =>
            DecisionSelector.Select(new[] { score }, false, null));
        Assert.Equal(DecisionFailureCodes.NoLegalAction, exception.Code);
        Assert.Equal(DecisionRejectionCodes.OutOfDecisionRange, evaluations[0].FirstRejectionCode);
    }

    [Theory]
    [InlineData(4_000, 2_000, "sys_approach")]
    [InlineData(4_000, 500, "sys_retreat")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_AVL_009_NoRequiredFallbackActionLeavesPredicatesStrict(
        int actorPosition,
        int surfaceGap,
        string excludedRequiredActionId)
    {
        var actions = new[]
        {
            DecisionTestFixture.System("sys_approach", DecisionMovementMode.Approach),
            DecisionTestFixture.System("sys_retreat", DecisionMovementMode.Retreat),
            DecisionTestFixture.System("sys_wait", DecisionMovementMode.None),
        };
        var actor = DecisionTestFixture.Fighter(FighterId.FighterA, actorPosition);
        var opponent = DecisionTestFixture.Fighter(
            FighterId.FighterB,
            actorPosition + 520 + 520 + surfaceGap);
        var snapshot = DecisionTestFixture.Snapshot(fighterA: actor, fighterB: opponent);
        var settings = new DecisionAvailabilitySettings(
            actions
                .Where(action => action.Id.Value != excludedRequiredActionId)
                .Select(action => action.Id),
            permittedCategories: null,
            arenaMinimum: 0,
            arenaMaximum: 10_000,
            systemNeutralMinimum: 1_500,
            systemNeutralMaximum: 1_600);

        var evaluations = DecisionAvailabilityEvaluator.EvaluateCatalog(
            actions,
            new DecisionAvailabilityContext(snapshot, FighterId.FighterA, settings));

        Assert.DoesNotContain(evaluations, item => item.Legal);
        var wait = Assert.Single(evaluations, item => item.Action.Id.Value == "sys_wait");
        Assert.Equal(DecisionRejectionCodes.SystemBandUnavailable, wait.FirstRejectionCode);
        var scores = evaluations
            .Select(item => DecisionWeightCalculator.Calculate(
                item,
                new DecisionStageMultipliers(1_000, 1_000, 1_000, 1_000, 1_000, 1_000),
                DecisionTestFixture.WeightSettings))
            .ToArray();
        var failure = Assert.Throws<EngineInvariantException>(() =>
            DecisionSelector.Select(scores, emergency: false, drawSource: null));
        Assert.Equal(DecisionFailureCodes.NoLegalAction, failure.Code);
    }

    private static DecisionCandidateEvaluation Evaluate(
        DecisionActionProfile action,
        DecisionFighterView? fighterA = null,
        DecisionFighterView? fighterB = null,
        int tick = 0)
    {
        var snapshot = DecisionTestFixture.Snapshot(fighterA, fighterB, tick);
        return DecisionAvailabilityEvaluator.Evaluate(
            action,
            DecisionTestFixture.Context(new[] { action }, snapshot));
    }

    private static void AssertRejected(DecisionCandidateEvaluation result, ReasonCode code)
    {
        Assert.False(result.Legal);
        Assert.Equal(code, result.FirstRejectionCode);
    }
}
