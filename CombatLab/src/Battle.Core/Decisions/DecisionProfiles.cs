using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal enum DecisionActionSlot
{
    System,
    Basic,
    Special,
}

internal enum DecisionMovementMode
{
    None,
    Approach,
    Retreat,
    Adaptive,
    Follow,
    Push,
    Pull,
    Swap,
}

internal enum DecisionTargetKind
{
    Self,
    Opponent,
}

/// <summary>
/// Immutable, config-materialized input used by the decision model. It deliberately
/// contains no mutable engine state and no references to Battle.Config.
/// </summary>
internal sealed class DecisionActionProfile
{
    internal const int MaximumHitScheduleEntries = 32;

    private readonly ReadOnlyCollection<StableId> _tags;
    private readonly ReadOnlyCollection<int> _hitScheduleTicks;

    internal DecisionActionProfile(
        StableId id,
        StableId? ownerAnimalId,
        DecisionActionSlot slot,
        string category,
        DecisionMovementMode movementMode,
        DecisionTargetKind targetKind,
        IEnumerable<StableId> tags,
        int baseWeight,
        int energyCost,
        int resourceCost,
        int cooldownTicks,
        int maximumConsecutiveUses,
        int hardOpportunityMisses,
        int opportunityCapFixedPoint,
        int startupBaseTicks,
        int startupMinimumTicks,
        int startupMaximumTicks,
        int activeTicks,
        int recoveryBaseTicks,
        int recoveryMinimumTicks,
        int recoveryMaximumTicks,
        int preferredRangeMinimum,
        int preferredRangeMaximum,
        int hitRangeMinimum,
        int hitRangeMaximum,
        IEnumerable<int> hitScheduleTicks,
        bool trackTarget)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("An action ID is required.", nameof(id));
        }

        RequireDefined(slot, nameof(slot));
        RequireDefined(movementMode, nameof(movementMode));
        RequireDefined(targetKind, nameof(targetKind));
        if (slot == DecisionActionSlot.System)
        {
            if (ownerAnimalId.HasValue)
            {
                throw new ArgumentException("A System action cannot have an animal owner.", nameof(ownerAnimalId));
            }
        }
        else if (!ownerAnimalId.HasValue || string.IsNullOrEmpty(ownerAnimalId.Value.Value))
        {
            throw new ArgumentException("A non-System action requires an animal owner.", nameof(ownerAnimalId));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("An action category is required.", nameof(category));
        }

        RequireNonNegative(baseWeight, nameof(baseWeight));
        RequireNonNegative(energyCost, nameof(energyCost));
        RequireNonNegative(resourceCost, nameof(resourceCost));
        RequireNonNegative(cooldownTicks, nameof(cooldownTicks));
        if (maximumConsecutiveUses < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConsecutiveUses));
        }

        RequireNonNegative(hardOpportunityMisses, nameof(hardOpportunityMisses));
        if (opportunityCapFixedPoint < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opportunityCapFixedPoint));
        }

        ValidateTiming(
            startupBaseTicks,
            startupMinimumTicks,
            startupMaximumTicks,
            nameof(startupBaseTicks));
        RequireNonNegative(activeTicks, nameof(activeTicks));
        ValidateTiming(
            recoveryBaseTicks,
            recoveryMinimumTicks,
            recoveryMaximumTicks,
            nameof(recoveryBaseTicks));
        ValidateRange(preferredRangeMinimum, preferredRangeMaximum, nameof(preferredRangeMinimum));
        ValidateRange(hitRangeMinimum, hitRangeMaximum, nameof(hitRangeMinimum));

        Id = id;
        OwnerAnimalId = ownerAnimalId;
        Slot = slot;
        Category = category;
        MovementMode = movementMode;
        TargetKind = targetKind;
        _tags = CopySortedUniqueTags(tags);
        BaseWeight = baseWeight;
        EnergyCost = energyCost;
        ResourceCost = resourceCost;
        CooldownTicks = cooldownTicks;
        MaximumConsecutiveUses = maximumConsecutiveUses;
        HardOpportunityMisses = hardOpportunityMisses;
        OpportunityCapFixedPoint = opportunityCapFixedPoint;
        StartupBaseTicks = startupBaseTicks;
        StartupMinimumTicks = startupMinimumTicks;
        StartupMaximumTicks = startupMaximumTicks;
        ActiveTicks = activeTicks;
        RecoveryBaseTicks = recoveryBaseTicks;
        RecoveryMinimumTicks = recoveryMinimumTicks;
        RecoveryMaximumTicks = recoveryMaximumTicks;
        PreferredRangeMinimum = preferredRangeMinimum;
        PreferredRangeMaximum = preferredRangeMaximum;
        HitRangeMinimum = hitRangeMinimum;
        HitRangeMaximum = hitRangeMaximum;
        _hitScheduleTicks = CopyHitSchedule(hitScheduleTicks, activeTicks);
        TrackTarget = trackTarget;
    }

    internal StableId Id { get; }

    internal StableId? OwnerAnimalId { get; }

    internal DecisionActionSlot Slot { get; }

    internal string Category { get; }

    internal DecisionMovementMode MovementMode { get; }

    internal DecisionTargetKind TargetKind { get; }

    internal IReadOnlyList<StableId> Tags => _tags;

    internal int BaseWeight { get; }

    internal int EnergyCost { get; }

    internal int ResourceCost { get; }

    internal int CooldownTicks { get; }

    internal int MaximumConsecutiveUses { get; }

    internal int HardOpportunityMisses { get; }

    internal int OpportunityCapFixedPoint { get; }

    internal int StartupBaseTicks { get; }

    internal int StartupMinimumTicks { get; }

    internal int StartupMaximumTicks { get; }

    internal int ActiveTicks { get; }

    internal int RecoveryBaseTicks { get; }

    internal int RecoveryMinimumTicks { get; }

    internal int RecoveryMaximumTicks { get; }

    internal int PreferredRangeMinimum { get; }

    internal int PreferredRangeMaximum { get; }

    internal int HitRangeMinimum { get; }

    internal int HitRangeMaximum { get; }

    internal IReadOnlyList<int> HitScheduleTicks => _hitScheduleTicks;

    internal bool TrackTarget { get; }

    internal bool HasTag(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return _tags.Any(tag => StringComparer.Ordinal.Equals(tag.Value, value));
    }

    private static ReadOnlyCollection<StableId> CopySortedUniqueTags(IEnumerable<StableId> tags)
    {
        if (tags is null)
        {
            throw new ArgumentNullException(nameof(tags));
        }

        var copy = tags.ToArray();
        foreach (var tag in copy)
        {
            if (string.IsNullOrEmpty(tag.Value))
            {
                throw new ArgumentException("Action tags cannot contain a default ID.", nameof(tags));
            }
        }

        Array.Sort(copy, static (left, right) => left.CompareTo(right));
        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1] == copy[index])
            {
                throw new ArgumentException("Action tags must be unique.", nameof(tags));
            }
        }

        return new ReadOnlyCollection<StableId>(copy);
    }

    private static ReadOnlyCollection<int> CopyHitSchedule(
        IEnumerable<int> hitScheduleTicks,
        int activeTicks)
    {
        if (hitScheduleTicks is null)
        {
            throw new ArgumentNullException(nameof(hitScheduleTicks));
        }

        var copy = hitScheduleTicks.ToArray();
        if (copy.Length > MaximumHitScheduleEntries)
        {
            throw new ArgumentException(
                $"A hit schedule cannot contain more than {MaximumHitScheduleEntries} entries.",
                nameof(hitScheduleTicks));
        }

        var previous = -1;
        foreach (var tick in copy)
        {
            if (tick < 0 || tick >= activeTicks || tick <= previous)
            {
                throw new ArgumentException(
                    "Hit schedule ticks must be unique, increasing, and inside Active.",
                    nameof(hitScheduleTicks));
            }

            previous = tick;
        }

        return new ReadOnlyCollection<int>(copy);
    }

    private static void ValidateTiming(int value, int minimum, int maximum, string parameterName)
    {
        if (minimum < 0 || value < minimum || maximum < value)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ValidateRange(int minimum, int maximum, string parameterName)
    {
        if (minimum < 0 || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void RequireDefined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

internal sealed record DecisionTacticProfile(
    int ApproachFixedPoint,
    int BlockFixedPoint,
    int DodgeFixedPoint,
    int GrabFixedPoint,
    int HeavyFixedPoint,
    int LightFixedPoint,
    int ResourceGeneratorFixedPoint,
    int ResourceSpenderFixedPoint,
    int RetreatFixedPoint,
    int SignatureFixedPoint,
    int CounterFixedPoint,
    int LowHealthFixedPoint,
    int SelfWallFixedPoint,
    int TargetWallFixedPoint,
    int TargetRecoveryFixedPoint,
    int RepeatPenaltyFixedPoint,
    int PerceptionDelayTicks);

internal sealed class DecisionTagMultiplierProfile
{
    private readonly ReadOnlyCollection<StableId> _tags;

    internal DecisionTagMultiplierProfile(IEnumerable<StableId> tags, int multiplierFixedPoint)
    {
        if (tags is null)
        {
            throw new ArgumentNullException(nameof(tags));
        }

        var copy = tags.ToArray();
        if (copy.Any(tag => string.IsNullOrEmpty(tag.Value)))
        {
            throw new ArgumentException("Multiplier tags cannot contain a default ID.", nameof(tags));
        }

        Array.Sort(copy, static (left, right) => left.CompareTo(right));
        if (copy.Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("Multiplier tags must be unique.", nameof(tags));
        }

        if (multiplierFixedPoint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplierFixedPoint));
        }

        _tags = new ReadOnlyCollection<StableId>(copy);
        MultiplierFixedPoint = multiplierFixedPoint;
    }

    internal IReadOnlyList<StableId> Tags => _tags;

    internal int MultiplierFixedPoint { get; }
}
