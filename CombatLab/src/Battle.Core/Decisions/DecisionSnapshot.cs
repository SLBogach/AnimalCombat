using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal sealed class DecisionBuildView
{
    private readonly ReadOnlyCollection<StableId> _specialActionIds;

    internal DecisionBuildView(
        StableId animalId,
        IEnumerable<StableId> specialActionIds,
        StableId passiveId,
        StableId offenseGearId,
        StableId defenseGearId,
        StableId utilityGearId,
        StableId tacticId)
    {
        RequireId(animalId, nameof(animalId));
        RequireId(passiveId, nameof(passiveId));
        RequireId(offenseGearId, nameof(offenseGearId));
        RequireId(defenseGearId, nameof(defenseGearId));
        RequireId(utilityGearId, nameof(utilityGearId));
        RequireId(tacticId, nameof(tacticId));
        if (specialActionIds is null)
        {
            throw new ArgumentNullException(nameof(specialActionIds));
        }

        var specials = specialActionIds.ToArray();
        if (specials.Length != 2 || specials[0] == specials[1] ||
            specials.Any(item => string.IsNullOrEmpty(item.Value)))
        {
            throw new ArgumentException(
                "A decision build requires two distinct Special action IDs.",
                nameof(specialActionIds));
        }

        AnimalId = animalId;
        _specialActionIds = new ReadOnlyCollection<StableId>(specials);
        PassiveId = passiveId;
        OffenseGearId = offenseGearId;
        DefenseGearId = defenseGearId;
        UtilityGearId = utilityGearId;
        TacticId = tacticId;
    }

    internal StableId AnimalId { get; }

    /// <summary>Canonical input order is preserved; scoring performs its own ordinal sort.</summary>
    internal IReadOnlyList<StableId> SpecialActionIds => _specialActionIds;

    internal StableId PassiveId { get; }

    internal StableId OffenseGearId { get; }

    internal StableId DefenseGearId { get; }

    internal StableId UtilityGearId { get; }

    internal StableId TacticId { get; }

    private static void RequireId(StableId value, string parameterName)
    {
        if (string.IsNullOrEmpty(value.Value))
        {
            throw new ArgumentException("A non-default stable ID is required.", parameterName);
        }
    }
}

internal sealed record DecisionRepeatHistory(
    StableId? LastActionId,
    string? LastCategory,
    int ConsecutiveActionUses,
    int ConsecutiveCategoryUses)
{
    internal static DecisionRepeatHistory Empty { get; } = new(null, null, 0, 0);
}

internal sealed record DecisionTelegraphView(StableId ActionId, int CommitTick);

internal sealed class DecisionFighterView
{
    private readonly ReadOnlyDictionary<StableId, int> _cooldowns;
    private readonly ReadOnlyDictionary<StableId, int> _opportunityDebts;

    internal DecisionFighterView(
        FighterId fighterId,
        DecisionBuildView build,
        int position,
        Facing facing,
        FighterState state,
        StableId? currentActionId,
        int collisionRadius,
        int health,
        int maximumHealth,
        int energy,
        int maximumEnergy,
        StableId resourceId,
        int resource,
        int maximumResource,
        int actionSpeed,
        int perceptionDelayTicks,
        IReadOnlyDictionary<StableId, int>? cooldowns = null,
        DecisionRepeatHistory? history = null,
        IReadOnlyDictionary<StableId, int>? opportunityDebts = null,
        DecisionTelegraphView? telegraph = null,
        bool emergency = false,
        int? stateTicksRemaining = null,
        ActionPhase? actionPhase = null,
        int stagger = 0,
        int staggerThreshold = 1,
        IEnumerable<EffectFrame>? effects = null)
    {
        if (fighterId is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterId));
        }

        Build = build ?? throw new ArgumentNullException(nameof(build));
        if (!Enum.IsDefined(typeof(Facing), facing))
        {
            throw new ArgumentOutOfRangeException(nameof(facing));
        }

        if (!Enum.IsDefined(typeof(FighterState), state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (collisionRadius < 1 || maximumHealth < 1 || health < 0 || health > maximumHealth ||
            maximumEnergy < 0 || energy < 0 || energy > maximumEnergy || maximumResource < 0 ||
            resource < 0 || resource > maximumResource || actionSpeed < 0 || perceptionDelayTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(health), "Fighter scalar values are outside their domain.");
        }

        if (string.IsNullOrEmpty(resourceId.Value))
        {
            throw new ArgumentException("A unique-resource ID is required.", nameof(resourceId));
        }

        FighterId = fighterId;
        Position = position;
        Facing = facing;
        State = state;
        CurrentActionId = currentActionId;
        CollisionRadius = collisionRadius;
        Health = health;
        MaximumHealth = maximumHealth;
        Energy = energy;
        MaximumEnergy = maximumEnergy;
        ResourceId = resourceId;
        Resource = resource;
        MaximumResource = maximumResource;
        ActionSpeed = actionSpeed;
        PerceptionDelayTicks = perceptionDelayTicks;
        _cooldowns = CopyNonNegativeMap(cooldowns, requirePositiveValues: true, nameof(cooldowns));
        History = ValidateHistory(history ?? DecisionRepeatHistory.Empty);
        _opportunityDebts = CopyNonNegativeMap(
            opportunityDebts,
            requirePositiveValues: false,
            nameof(opportunityDebts));
        Telegraph = telegraph;
        Emergency = emergency;
        PublicFrame = new FighterFrame(
            fighterId,
            position,
            facing,
            state,
            stateTicksRemaining,
            currentActionId,
            actionPhase,
            health,
            maximumHealth,
            energy,
            maximumEnergy,
            new ResourceFrame(resourceId, resource, maximumResource),
            stagger,
            staggerThreshold,
            effects ?? Array.Empty<EffectFrame>());
    }

    internal FighterId FighterId { get; }

    internal FighterFrame PublicFrame { get; }

    internal DecisionBuildView Build { get; }

    internal int Position { get; }

    internal Facing Facing { get; }

    internal FighterState State { get; }

    internal StableId? CurrentActionId { get; }

    internal int CollisionRadius { get; }

    internal int Health { get; }

    internal int MaximumHealth { get; }

    internal int Energy { get; }

    internal int MaximumEnergy { get; }

    internal StableId ResourceId { get; }

    internal int Resource { get; }

    internal int MaximumResource { get; }

    internal int ActionSpeed { get; }

    internal int PerceptionDelayTicks { get; }

    internal IReadOnlyDictionary<StableId, int> Cooldowns => _cooldowns;

    internal DecisionRepeatHistory History { get; }

    internal IReadOnlyDictionary<StableId, int> OpportunityDebts => _opportunityDebts;

    internal DecisionTelegraphView? Telegraph { get; }

    internal bool Emergency { get; }

    internal bool IsDecisionReady => State == FighterState.DecisionReady && !CurrentActionId.HasValue;

    internal int CooldownFor(StableId actionId) =>
        _cooldowns.TryGetValue(actionId, out var value) ? value : 0;

    internal int OpportunityDebtFor(StableId actionId) =>
        _opportunityDebts.TryGetValue(actionId, out var value) ? value : 0;

    private static ReadOnlyDictionary<StableId, int> CopyNonNegativeMap(
        IReadOnlyDictionary<StableId, int>? source,
        bool requirePositiveValues,
        string parameterName)
    {
        var copy = new Dictionary<StableId, int>();
        if (source is not null)
        {
            foreach (var pair in source.OrderBy(item => item.Key))
            {
                if (string.IsNullOrEmpty(pair.Key.Value) ||
                    pair.Value < 0 ||
                    (requirePositiveValues && pair.Value == 0))
                {
                    throw new ArgumentException("Decision state maps contain an invalid entry.", parameterName);
                }

                copy.Add(pair.Key, pair.Value);
            }
        }

        return new ReadOnlyDictionary<StableId, int>(copy);
    }

    private static DecisionRepeatHistory ValidateHistory(DecisionRepeatHistory history)
    {
        if (history.ConsecutiveActionUses < 0 || history.ConsecutiveCategoryUses < 0 ||
            (history.LastActionId.HasValue != (history.ConsecutiveActionUses > 0)) ||
            ((history.LastCategory is not null) != (history.ConsecutiveCategoryUses > 0)))
        {
            throw new ArgumentException("Decision repeat history is inconsistent.", nameof(history));
        }

        return history;
    }
}

internal sealed class DecisionBatchSnapshot
{
    private readonly ReadOnlyCollection<FighterId> _initiativeOrder;

    internal DecisionBatchSnapshot(
        long identity,
        int tick,
        DecisionFighterView fighterA,
        DecisionFighterView fighterB,
        IEnumerable<FighterId> initiativeOrder)
    {
        if (identity < 0 || tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(identity));
        }

        FighterA = fighterA ?? throw new ArgumentNullException(nameof(fighterA));
        FighterB = fighterB ?? throw new ArgumentNullException(nameof(fighterB));
        if (fighterA.FighterId != FighterId.FighterA || fighterB.FighterId != FighterId.FighterB)
        {
            throw new ArgumentException("Decision snapshot fighters must use canonical A/B slots.");
        }

        if (initiativeOrder is null)
        {
            throw new ArgumentNullException(nameof(initiativeOrder));
        }

        var order = initiativeOrder.ToArray();
        if (order.Length != 2 || order.Distinct().Count() != 2 ||
            !order.Contains(FighterId.FighterA) || !order.Contains(FighterId.FighterB))
        {
            throw new ArgumentException("Initiative order must contain A and B exactly once.", nameof(initiativeOrder));
        }

        Identity = identity;
        Tick = tick;
        _initiativeOrder = new ReadOnlyCollection<FighterId>(order);
    }

    internal long Identity { get; }

    internal int Tick { get; }

    internal DecisionFighterView FighterA { get; }

    internal DecisionFighterView FighterB { get; }

    internal IReadOnlyList<FighterId> InitiativeOrder => _initiativeOrder;

    internal DecisionFighterView Get(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterA,
        FighterId.FighterB => FighterB,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    internal DecisionFighterView GetOpponent(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterB,
        FighterId.FighterB => FighterA,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };
}

internal sealed class DecisionAvailabilitySettings
{
    private readonly ReadOnlyCollection<StableId> _allowedActionIds;
    private readonly ReadOnlyCollection<string> _permittedCategories;

    internal DecisionAvailabilitySettings(
        IEnumerable<StableId> allowedActionIds,
        IEnumerable<string>? permittedCategories,
        int arenaMinimum,
        int arenaMaximum,
        int systemNeutralMinimum,
        int systemNeutralMaximum)
    {
        if (allowedActionIds is null)
        {
            throw new ArgumentNullException(nameof(allowedActionIds));
        }

        var actions = allowedActionIds.ToArray();
        if (actions.Length == 0 || actions.Any(item => string.IsNullOrEmpty(item.Value)))
        {
            throw new ArgumentException("At least one allowed action ID is required.", nameof(allowedActionIds));
        }

        Array.Sort(actions, static (left, right) => left.CompareTo(right));
        if (actions.Distinct().Count() != actions.Length)
        {
            throw new ArgumentException("Allowed action IDs must be unique.", nameof(allowedActionIds));
        }

        var categories = permittedCategories?.ToArray() ?? Array.Empty<string>();
        if (categories.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Permitted categories cannot be empty.", nameof(permittedCategories));
        }

        Array.Sort(categories, StringComparer.Ordinal);
        if (categories.Distinct(StringComparer.Ordinal).Count() != categories.Length)
        {
            throw new ArgumentException("Permitted categories must be unique.", nameof(permittedCategories));
        }

        if (arenaMaximum <= arenaMinimum || systemNeutralMinimum < 0 ||
            systemNeutralMaximum < systemNeutralMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(arenaMaximum));
        }

        _allowedActionIds = new ReadOnlyCollection<StableId>(actions);
        _permittedCategories = new ReadOnlyCollection<string>(categories);
        ArenaMinimum = arenaMinimum;
        ArenaMaximum = arenaMaximum;
        SystemNeutralMinimum = systemNeutralMinimum;
        SystemNeutralMaximum = systemNeutralMaximum;
    }

    internal IReadOnlyList<StableId> AllowedActionIds => _allowedActionIds;

    /// <summary>An empty collection means that every category is permitted.</summary>
    internal IReadOnlyList<string> PermittedCategories => _permittedCategories;

    internal int ArenaMinimum { get; }

    internal int ArenaMaximum { get; }

    internal int SystemNeutralMinimum { get; }

    internal int SystemNeutralMaximum { get; }

    internal bool IsActionAllowed(StableId actionId) => _allowedActionIds.Contains(actionId);

    internal bool IsCategoryPermitted(string category) =>
        _permittedCategories.Count == 0 || _permittedCategories.Contains(category, StringComparer.Ordinal);
}
