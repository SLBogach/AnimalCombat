using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;

namespace Battle.Core.UnitTests.Decisions;

public sealed class Wp08CrossCuttingDecisionDeterminismTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_006_CatalogAndStateMapInsertionOrderCannotChangeDecision()
    {
        var paw = DecisionTestFixture.Action("bear_paw", baseWeight: 1_000);
        var retreat = DecisionTestFixture.System(
            "sys_retreat",
            DecisionMovementMode.Retreat,
            weight: 337);
        var forwardSnapshot = CreateSnapshot(
            new Dictionary<StableId, int>
            {
                [new StableId("cooldown_z")] = 2,
                [new StableId("cooldown_a")] = 1,
            },
            new Dictionary<StableId, int>
            {
                [paw.Id] = 2,
                [retreat.Id] = 1,
            });
        var reverseSnapshot = CreateSnapshot(
            new Dictionary<StableId, int>
            {
                [new StableId("cooldown_a")] = 1,
                [new StableId("cooldown_z")] = 2,
            },
            new Dictionary<StableId, int>
            {
                [retreat.Id] = 1,
                [paw.Id] = 2,
            });

        var forward = EvaluateAndSelect(
            new[] { retreat, paw },
            forwardSnapshot,
            FighterId.FighterA,
            1_200);
        var reverse = EvaluateAndSelect(
            new[] { paw, retreat },
            reverseSnapshot,
            FighterId.FighterA,
            1_200);

        AssertScoresEqual(forward.Scores, reverse.Scores);
        Assert.Equal(forward.Selection.ActionId, reverse.Selection.ActionId);
        Assert.Equal(forward.Selection.LegalActionIds, reverse.Selection.LegalActionIds);
        Assert.Equal(forward.Selection.ChosenWeight, reverse.Selection.ChosenWeight);
        Assert.Equal(forward.Selection.WeightSum, reverse.Selection.WeightSum);
        Assert.Equal(forward.Selection.Rng, reverse.Selection.Rng);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_007_MirroredPureDecisionEvaluationInverseNormalizesExactly()
    {
        const int arenaMinimum = 0;
        const int arenaMaximum = 10_000;
        var paw = DecisionTestFixture.Action("bear_paw", baseWeight: 1_000);
        var retreat = DecisionTestFixture.System(
            "sys_retreat",
            DecisionMovementMode.Retreat,
            weight: 337);
        var actions = new[] { retreat, paw };
        var oldActorState = new Dictionary<StableId, int>
        {
            [paw.Id] = 2,
            [retreat.Id] = 1,
        };
        var oldSnapshot = DecisionTestFixture.Snapshot(
            fighterA: DecisionTestFixture.Fighter(
                FighterId.FighterA,
                4_000,
                debts: oldActorState),
            fighterB: DecisionTestFixture.Fighter(FighterId.FighterB, 5_540));
        var mirroredSnapshot = DecisionTestFixture.Snapshot(
            fighterA: DecisionTestFixture.Fighter(
                FighterId.FighterA,
                MirrorPosition(5_540, arenaMinimum, arenaMaximum)),
            fighterB: DecisionTestFixture.Fighter(
                FighterId.FighterB,
                MirrorPosition(4_000, arenaMinimum, arenaMaximum),
                debts: oldActorState));

        var original = EvaluateAndSelect(actions, oldSnapshot, FighterId.FighterA, 1_200);
        var mirrored = EvaluateAndSelect(actions.Reverse(), mirroredSnapshot, FighterId.FighterB, 1_200);

        AssertScoresEqual(original.Scores, mirrored.Scores);
        Assert.Equal(original.Selection.ActionId, mirrored.Selection.ActionId);
        Assert.Equal(original.Selection.ChosenWeight, mirrored.Selection.ChosenWeight);
        Assert.Equal(original.Selection.WeightSum, mirrored.Selection.WeightSum);
        Assert.Equal(original.Selection.Rng, mirrored.Selection.Rng);

        var originalDescriptor = ProjectTarget(original.Selection.ActionId, oldSnapshot, FighterId.FighterA);
        var mirroredDescriptor = ProjectTarget(
            mirrored.Selection.ActionId,
            mirroredSnapshot,
            FighterId.FighterB);
        var normalizedMirror = new TargetProjection(
            Swap(mirroredDescriptor.TargetId),
            MirrorPosition(mirroredDescriptor.TargetPosition, arenaMinimum, arenaMaximum),
            MirrorDirection(mirroredDescriptor.Direction));

        Assert.Equal(originalDescriptor, normalizedMirror);
    }

    private static DecisionBatchSnapshot CreateSnapshot(
        IReadOnlyDictionary<StableId, int> cooldowns,
        IReadOnlyDictionary<StableId, int> debts) =>
        DecisionTestFixture.Snapshot(
            fighterA: DecisionTestFixture.Fighter(
                FighterId.FighterA,
                4_000,
                cooldowns: cooldowns,
                debts: debts),
            fighterB: DecisionTestFixture.Fighter(FighterId.FighterB, 5_540));

    private static EvaluationResult EvaluateAndSelect(
        IEnumerable<DecisionActionProfile> actions,
        DecisionBatchSnapshot snapshot,
        FighterId actorId,
        int draw)
    {
        var catalog = DecisionCatalogBuilder.BuildCheckedCatalog(
            actions,
            snapshot.Get(actorId).Build.AnimalId);
        var context = DecisionTestFixture.Context(
            catalog,
            snapshot,
            actorId,
            arenaMinimum: 0,
            arenaMaximum: 10_000,
            neutralMinimum: 1_500,
            neutralMaximum: 1_600);
        var scores = DecisionAvailabilityEvaluator
            .EvaluateCatalog(catalog, context)
            .Select(evaluation => Score(evaluation, context))
            .ToArray();
        var selection = DecisionSelector.Select(
            scores,
            emergency: false,
            new FixedDecisionDrawSource(draw));
        return new EvaluationResult(scores, selection);
    }

    private static CandidateScore Score(
        DecisionCandidateEvaluation evaluation,
        DecisionAvailabilityContext context)
    {
        if (!evaluation.Legal)
        {
            return CandidateScore.Illegal(evaluation);
        }

        var action = evaluation.Action;
        var actor = context.Actor;
        var opponent = context.Opponent;
        var settings = DecisionTestFixture.WeightSettings;
        var variety = DecisionVariety.Calculate(
            action,
            actor.History,
            settings.FixedPointScale,
            repeatSameActionFixedPoint: 800,
            repeatSameCategoryFixedPoint: 900,
            tacticRepeatPenaltyFixedPoint: 950);
        var opportunity = DecisionOpportunity.CalculateMultiplier(
            evaluation.OpportunityDebt,
            action.OpportunityCapFixedPoint,
            globalCapFixedPoint: 2_500,
            growthFixedPoint: 100,
            settings.FixedPointScale);
        var multipliers = new DecisionStageMultipliers(
            DecisionTacticMultiplierCalculator.Calculate(
                action,
                DecisionTestFixture.NeutralTactic,
                settings),
            DecisionSituationMultiplierCalculator.Calculate(
                action,
                actor,
                opponent,
                DecisionTestFixture.NeutralTactic,
                settings,
                lowHealthThresholdFixedPoint: null,
                arenaMinimum: 0,
                arenaMaximum: 10_000,
                wallZoneSize: 500),
            settings.FixedPointScale,
            DecisionCounterMultiplierCalculator.Calculate(
                action,
                DecisionTestFixture.NeutralTactic,
                telegraphObserved: false,
                settings.FixedPointScale),
            variety.MultiplierFixedPoint,
            opportunity);
        return DecisionWeightCalculator.Calculate(evaluation, multipliers, settings);
    }

    private static void AssertScoresEqual(
        IReadOnlyList<CandidateScore> expected,
        IReadOnlyList<CandidateScore> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].ActionId, actual[index].ActionId);
            Assert.Equal(expected[index].Legal, actual[index].Legal);
            Assert.Equal(expected[index].FirstRejectionCode, actual[index].FirstRejectionCode);
            Assert.Equal(expected[index].BaseWeight, actual[index].BaseWeight);
            Assert.Equal(expected[index].Modifiers, actual[index].Modifiers);
            Assert.Equal(expected[index].FinalWeight, actual[index].FinalWeight);
            Assert.Equal(expected[index].OpportunityDebt, actual[index].OpportunityDebt);
            Assert.Equal(expected[index].HardOpportunityReady, actual[index].HardOpportunityReady);
        }
    }

    private static TargetProjection ProjectTarget(
        StableId actionId,
        DecisionBatchSnapshot snapshot,
        FighterId actorId)
    {
        var actor = snapshot.Get(actorId);
        var opponent = snapshot.GetOpponent(actorId);
        var toward = opponent.Position > actor.Position
            ? CommitDirection.Right
            : CommitDirection.Left;
        var direction = actionId == new StableId("sys_retreat")
            ? MirrorDirection(toward)
            : toward;
        return new TargetProjection(opponent.FighterId, opponent.Position, direction);
    }

    private static int MirrorPosition(int position, int arenaMinimum, int arenaMaximum) =>
        checked(arenaMinimum + arenaMaximum - position);

    private static FighterId Swap(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterId.FighterB,
        FighterId.FighterB => FighterId.FighterA,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    private static CommitDirection MirrorDirection(CommitDirection direction) => direction switch
    {
        CommitDirection.Left => CommitDirection.Right,
        CommitDirection.Right => CommitDirection.Left,
        CommitDirection.None => CommitDirection.None,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private sealed record EvaluationResult(
        IReadOnlyList<CandidateScore> Scores,
        DecisionSelection Selection);

    private readonly record struct TargetProjection(
        FighterId TargetId,
        int TargetPosition,
        CommitDirection Direction);
}
