using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;

namespace Battle.Core.UnitTests.Decisions;

internal static class DecisionTestFixture
{
    internal static readonly StableId BearId = new("bear");
    internal static readonly StableId GorillaId = new("gorilla");
    internal static readonly StableId SpecialAId = new("bear_special_a");
    internal static readonly StableId SpecialBId = new("bear_special_b");

    internal static DecisionWeightSettings WeightSettings { get; } = new(1_000, 250, 3_000, 100_000_000);

    internal static DecisionTacticProfile NeutralTactic { get; } = new(
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        1_000,
        5);

    internal static DecisionActionProfile Action(
        string id = "bear_light",
        StableId? ownerAnimalId = null,
        DecisionActionSlot slot = DecisionActionSlot.Basic,
        string category = "Light",
        DecisionMovementMode movementMode = DecisionMovementMode.None,
        DecisionTargetKind targetKind = DecisionTargetKind.Opponent,
        IEnumerable<string>? tags = null,
        int baseWeight = 1_000,
        int energyCost = 0,
        int resourceCost = 0,
        int cooldownTicks = 0,
        int maximumConsecutiveUses = 3,
        int hardOpportunityMisses = 4,
        int opportunityCapFixedPoint = 2_500,
        int startupBaseTicks = 1,
        int startupMinimumTicks = 0,
        int startupMaximumTicks = 10,
        int activeTicks = 1,
        int recoveryBaseTicks = 1,
        int recoveryMinimumTicks = 0,
        int recoveryMaximumTicks = 10,
        int preferredRangeMinimum = 0,
        int preferredRangeMaximum = 1_000,
        int hitRangeMinimum = 0,
        int hitRangeMaximum = 1_000,
        IEnumerable<int>? hitScheduleTicks = null,
        bool trackTarget = true)
    {
        StableId? owner = slot == DecisionActionSlot.System
            ? null
            : ownerAnimalId ?? BearId;
        return new DecisionActionProfile(
            new StableId(id),
            owner,
            slot,
            category,
            movementMode,
            targetKind,
            (tags ?? new[] { "light" }).Select(value => new StableId(value)),
            baseWeight,
            energyCost,
            resourceCost,
            cooldownTicks,
            maximumConsecutiveUses,
            hardOpportunityMisses,
            opportunityCapFixedPoint,
            startupBaseTicks,
            startupMinimumTicks,
            startupMaximumTicks,
            activeTicks,
            recoveryBaseTicks,
            recoveryMinimumTicks,
            recoveryMaximumTicks,
            preferredRangeMinimum,
            preferredRangeMaximum,
            hitRangeMinimum,
            hitRangeMaximum,
            hitScheduleTicks ?? Array.Empty<int>(),
            trackTarget);
    }

    internal static DecisionActionProfile System(
        string id,
        DecisionMovementMode movementMode,
        int weight = 100) => Action(
            id,
            slot: DecisionActionSlot.System,
            category: movementMode == DecisionMovementMode.None ? "Wait" : "Movement",
            movementMode: movementMode,
            tags: new[] { "system" },
            baseWeight: weight,
            preferredRangeMaximum: 10_000,
            hitRangeMaximum: 10_000);

    internal static DecisionBuildView Build(StableId? animalId = null) => new(
        animalId ?? BearId,
        new[] { SpecialAId, SpecialBId },
        new StableId("passive_test"),
        new StableId("gear_offense_test"),
        new StableId("gear_defense_test"),
        new StableId("gear_utility_test"),
        new StableId("tactic_test"));

    internal static DecisionFighterView Fighter(
        FighterId fighterId,
        int position,
        DecisionBuildView? build = null,
        FighterState state = FighterState.DecisionReady,
        StableId? currentActionId = null,
        int collisionRadius = 520,
        int health = 1_000,
        int maximumHealth = 1_000,
        int energy = 1_000,
        int resource = 1_000,
        int actionSpeed = 100,
        int perceptionDelayTicks = 5,
        IReadOnlyDictionary<StableId, int>? cooldowns = null,
        DecisionRepeatHistory? history = null,
        IReadOnlyDictionary<StableId, int>? debts = null,
        DecisionTelegraphView? telegraph = null,
        bool emergency = false) => new(
            fighterId,
            build ?? Build(),
            position,
            fighterId == FighterId.FighterA ? Facing.Right : Facing.Left,
            state,
            currentActionId,
            collisionRadius,
            health,
            maximumHealth,
            energy,
            1_000,
            new StableId("rage"),
            resource,
            1_000,
            actionSpeed,
            perceptionDelayTicks,
            cooldowns,
            history,
            debts,
            telegraph,
            emergency);

    internal static DecisionBatchSnapshot Snapshot(
        DecisionFighterView? fighterA = null,
        DecisionFighterView? fighterB = null,
        int tick = 0) => new(
            1,
            tick,
            fighterA ?? Fighter(FighterId.FighterA, 4_000),
            fighterB ?? Fighter(FighterId.FighterB, 5_540),
            new[] { FighterId.FighterA, FighterId.FighterB });

    internal static DecisionAvailabilityContext Context(
        IEnumerable<DecisionActionProfile> actions,
        DecisionBatchSnapshot? snapshot = null,
        FighterId actorId = FighterId.FighterA,
        IEnumerable<string>? categories = null,
        bool targetExists = true,
        int arenaMinimum = 0,
        int arenaMaximum = 10_000,
        int neutralMinimum = 1_500,
        int neutralMaximum = 1_600) => new(
            snapshot ?? Snapshot(),
            actorId,
            new DecisionAvailabilitySettings(
                actions.Select(action => action.Id),
                categories,
                arenaMinimum,
                arenaMaximum,
                neutralMinimum,
                neutralMaximum),
            targetExists);

    internal static DecisionCandidateEvaluation LegalEvaluation(
        DecisionActionProfile action,
        DecisionBatchSnapshot? snapshot = null)
    {
        var result = DecisionAvailabilityEvaluator.Evaluate(
            action,
            Context(new[] { action }, snapshot));
        if (!result.Legal)
        {
            throw new InvalidOperationException("The test action was expected to be legal: " + result.FirstRejectionCode);
        }

        return result;
    }

    internal static CandidateScore Score(
        string id,
        int weight,
        DecisionActionSlot slot = DecisionActionSlot.Basic,
        bool hard = false,
        int debt = 0)
    {
        var action = Action(
            id,
            slot: slot,
            movementMode: slot == DecisionActionSlot.System && id == "sys_retreat"
                ? DecisionMovementMode.Retreat
                : slot == DecisionActionSlot.System && id == "sys_approach"
                    ? DecisionMovementMode.Approach
                    : DecisionMovementMode.None,
            baseWeight: weight,
            hitRangeMaximum: 10_000);
        var actor = Fighter(
            FighterId.FighterA,
            4_000,
            debts: debt == 0 ? null : new Dictionary<StableId, int> { [action.Id] = debt });
        var opponentPosition = id switch
        {
            "sys_approach" => 8_000,
            "sys_wait" => 6_590,
            _ => 5_540,
        };
        var evaluation = LegalEvaluation(
            action,
            Snapshot(
                fighterA: actor,
                fighterB: Fighter(FighterId.FighterB, opponentPosition)));
        return DecisionWeightCalculator.Calculate(
            evaluation,
            new DecisionStageMultipliers(1_000, 1_000, 1_000, 1_000, 1_000, 1_000),
            WeightSettings,
            hard);
    }
}

internal sealed class FixedDecisionDrawSource : IDecisionDrawSource
{
    private readonly Queue<(int Result, RngStream Stream, RngOperation Operation)> _draws;

    internal FixedDecisionDrawSource(params int[] results)
        : this(results.Select(result => (result, RngStream.Decision, RngOperation.NextInt)).ToArray())
    {
    }

    internal FixedDecisionDrawSource(
        params (int Result, RngStream Stream, RngOperation Operation)[] draws)
    {
        _draws = new Queue<(int Result, RngStream Stream, RngOperation Operation)>(draws);
    }

    internal int CallCount { get; private set; }

    public ulong NextDrawIndex => checked((ulong)CallCount);

    public RngProvenance NextInt(int minimumInclusive, int maximumExclusive)
    {
        CallCount++;
        var draw = _draws.Dequeue();
        var bound = checked((uint)((long)maximumExclusive - minimumInclusive));
        var threshold = unchecked(0U - bound) % bound;
        var offset = checked((uint)(draw.Result - minimumInclusive));
        var rawValue = offset < threshold ? checked(offset + bound) : offset;
        return new RngProvenance(
            draw.Stream,
            (ulong)(CallCount - 1),
            draw.Operation,
            minimumInclusive,
            maximumExclusive,
            rawValue,
            draw.Result,
            checked((int)((long)offset * 1_000 / bound)));
    }
}
