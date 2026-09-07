using System.Collections.ObjectModel;
using System.Globalization;
using Battle.Contracts.Ids;
using Battle.Contracts.Events;

namespace Battle.Core.Resolution;

internal enum HitPrimitiveKind
{
    Hit,
    Counter,
    Grab,
    Throw,
    Wall,
}

internal enum ResolutionClass
{
    Counter = 0,
    Defense = 1,
    Strike = 2,
    Grab = 3,
    ForcedMove = 4,
}

internal enum ResolutionMovementMode
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

internal readonly record struct HitScheduleEntry
{
    internal HitScheduleEntry(HitPrimitiveKind kind, int relativeTick, int ordinal)
    {
        if (!Enum.IsDefined(typeof(HitPrimitiveKind), kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (relativeTick < 0 || ordinal < 0 || ordinal >= ResolutionActionProfile.MaximumScheduleEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeTick));
        }

        Kind = kind;
        RelativeTick = relativeTick;
        Ordinal = ordinal;
    }

    internal HitPrimitiveKind Kind { get; }

    internal int RelativeTick { get; }

    internal int Ordinal { get; }

    internal bool IsDamageCapable => Kind is not HitPrimitiveKind.Grab;
}

internal sealed class ResolutionActionProfile
{
    internal const int MaximumScheduleEntries = 32;

    private readonly ReadOnlyCollection<StableId> _tags;
    private readonly ReadOnlyCollection<HitScheduleEntry> _schedule;

    internal ResolutionActionProfile(
        StableId id,
        string slotType,
        string category,
        ResolutionMovementMode movementMode,
        IEnumerable<StableId> tags,
        IEnumerable<HitScheduleEntry> schedule,
        int activeTicks,
        int hitCount,
        int actionPriority,
        int resolutionPriority,
        int clashPriority,
        int grabPriority,
        int hitRangeMinimum,
        int hitRangeMaximum,
        int baseDamage,
        int powerRatioFixedPoint,
        int minimumDamage,
        bool blockable,
        bool dodgeable,
        bool undodgeable,
        int blockBaseChanceFixedPoint,
        int blockReductionFixedPoint,
        int dodgeBaseChanceFixedPoint,
        int chipMinimum,
        int baseStagger,
        int baseStunTicks,
        int baseKnockback,
        int knockbackMinimum,
        int knockbackMaximum,
        int moveDistance,
        bool trackTarget,
        bool wallImpact,
        int wallDamagePerUnitFixedPoint,
        int wallDamageMinimum,
        int wallDamageMaximum,
        string interruptProfile)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("An action ID is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(slotType) || string.IsNullOrWhiteSpace(category) ||
            string.IsNullOrWhiteSpace(interruptProfile))
        {
            throw new ArgumentException("Action text properties are required.");
        }

        if (!Enum.IsDefined(typeof(ResolutionMovementMode), movementMode))
        {
            throw new ArgumentOutOfRangeException(nameof(movementMode));
        }

        if (activeTicks < 1 || hitCount is < 0 or > MaximumScheduleEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(activeTicks));
        }

        ValidateNonNegative(
            actionPriority, resolutionPriority, clashPriority, grabPriority,
            hitRangeMinimum, hitRangeMaximum, baseDamage, powerRatioFixedPoint,
            minimumDamage, blockBaseChanceFixedPoint, blockReductionFixedPoint,
            dodgeBaseChanceFixedPoint, chipMinimum, baseStagger, baseStunTicks,
            baseKnockback, knockbackMinimum, knockbackMaximum, moveDistance,
            wallDamagePerUnitFixedPoint, wallDamageMinimum, wallDamageMaximum);
        if (hitRangeMaximum < hitRangeMinimum || knockbackMaximum < knockbackMinimum ||
            wallDamageMaximum < wallDamageMinimum)
        {
            throw new ArgumentException("Resolution min/max relations are invalid.");
        }

        var tagCopy = tags?.OrderBy(tag => tag).ToArray() ??
            throw new ArgumentNullException(nameof(tags));
        if (tagCopy.Any(tag => string.IsNullOrEmpty(tag.Value)) ||
            tagCopy.Distinct().Count() != tagCopy.Length)
        {
            throw new ArgumentException("Action tags must be valid and unique.", nameof(tags));
        }

        var scheduleCopy = schedule?.ToArray() ?? throw new ArgumentNullException(nameof(schedule));
        if (scheduleCopy.Length > MaximumScheduleEntries)
        {
            throw new ArgumentException("An action schedule contains too many entries.", nameof(schedule));
        }

        for (var index = 0; index < scheduleCopy.Length; index++)
        {
            var entry = scheduleCopy[index];
            if (entry.Ordinal != index || entry.RelativeTick >= activeTicks ||
                (index > 0 && scheduleCopy[index - 1].RelativeTick >= entry.RelativeTick))
            {
                throw new ArgumentException(
                    "Schedule ordinals must be contiguous and ticks strictly increasing inside Active.",
                    nameof(schedule));
            }
        }

        if (scheduleCopy.Count(entry => entry.IsDamageCapable) != hitCount)
        {
            throw new ArgumentException("hit_count must equal damage-capable schedule entries.", nameof(hitCount));
        }

        Id = id;
        SlotType = slotType;
        Category = category;
        MovementMode = movementMode;
        _tags = new ReadOnlyCollection<StableId>(tagCopy);
        _schedule = new ReadOnlyCollection<HitScheduleEntry>(scheduleCopy);
        ActiveTicks = activeTicks;
        HitCount = hitCount;
        ActionPriority = actionPriority;
        ResolutionPriority = resolutionPriority;
        ClashPriority = clashPriority;
        GrabPriority = grabPriority;
        HitRangeMinimum = hitRangeMinimum;
        HitRangeMaximum = hitRangeMaximum;
        BaseDamage = baseDamage;
        PowerRatioFixedPoint = powerRatioFixedPoint;
        MinimumDamage = minimumDamage;
        Blockable = blockable;
        Dodgeable = dodgeable;
        Undodgeable = undodgeable;
        BlockBaseChanceFixedPoint = blockBaseChanceFixedPoint;
        BlockReductionFixedPoint = blockReductionFixedPoint;
        DodgeBaseChanceFixedPoint = dodgeBaseChanceFixedPoint;
        ChipMinimum = chipMinimum;
        BaseStagger = baseStagger;
        BaseStunTicks = baseStunTicks;
        BaseKnockback = baseKnockback;
        KnockbackMinimum = knockbackMinimum;
        KnockbackMaximum = knockbackMaximum;
        MoveDistance = moveDistance;
        TrackTarget = trackTarget;
        WallImpact = wallImpact;
        WallDamagePerUnitFixedPoint = wallDamagePerUnitFixedPoint;
        WallDamageMinimum = wallDamageMinimum;
        WallDamageMaximum = wallDamageMaximum;
        InterruptProfile = interruptProfile;
    }

    internal StableId Id { get; }
    internal string SlotType { get; }
    internal string Category { get; }
    internal ResolutionMovementMode MovementMode { get; }
    internal IReadOnlyList<StableId> Tags => _tags;
    internal IReadOnlyList<HitScheduleEntry> Schedule => _schedule;
    internal int ActiveTicks { get; }
    internal int HitCount { get; }
    internal int ActionPriority { get; }
    internal int ResolutionPriority { get; }
    internal int ClashPriority { get; }
    internal int GrabPriority { get; }
    internal int HitRangeMinimum { get; }
    internal int HitRangeMaximum { get; }
    internal int BaseDamage { get; }
    internal int PowerRatioFixedPoint { get; }
    internal int MinimumDamage { get; }
    internal bool Blockable { get; }
    internal bool Dodgeable { get; }
    internal bool Undodgeable { get; }
    internal int BlockBaseChanceFixedPoint { get; }
    internal int BlockReductionFixedPoint { get; }
    internal int DodgeBaseChanceFixedPoint { get; }
    internal int ChipMinimum { get; }
    internal int BaseStagger { get; }
    internal int BaseStunTicks { get; }
    internal int BaseKnockback { get; }
    internal int KnockbackMinimum { get; }
    internal int KnockbackMaximum { get; }
    internal int MoveDistance { get; }
    internal bool TrackTarget { get; }
    internal bool WallImpact { get; }
    internal int WallDamagePerUnitFixedPoint { get; }
    internal int WallDamageMinimum { get; }
    internal int WallDamageMaximum { get; }
    internal string InterruptProfile { get; }

    internal bool HasTag(string value) => _tags.Any(tag =>
        StringComparer.Ordinal.Equals(tag.Value, value));

    internal ResolutionClass ClassFor(HitPrimitiveKind kind) => kind switch
    {
        HitPrimitiveKind.Counter => ResolutionClass.Counter,
        HitPrimitiveKind.Hit => ResolutionClass.Strike,
        HitPrimitiveKind.Grab or HitPrimitiveKind.Throw or HitPrimitiveKind.Wall => ResolutionClass.Grab,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void ValidateNonNegative(params int[] values)
    {
        if (values.Any(value => value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(values));
        }
    }
}

internal sealed record ResolutionGlobalSettings(
    int FixedPointScale,
    int ArmorK,
    int DamageFloor,
    int DamageCap,
    int BlockSlope,
    int BlockMinimum,
    int BlockMaximum,
    int DodgeSlope,
    int DodgeMinimum,
    int DodgeMaximum,
    int ControlK,
    int ForceK,
    int StunMinimumTicks,
    int StunMaximumTicks,
    int MaximumHoldTicks,
    int GrabLockoutTicks)
{
    internal void Validate()
    {
        if (FixedPointScale < 1 || ArmorK < 1 || DamageFloor < 0 || DamageCap < DamageFloor ||
            BlockSlope < 0 || BlockMinimum < 0 || BlockMaximum < BlockMinimum ||
            BlockMaximum > FixedPointScale || DodgeSlope < 0 || DodgeMinimum < 0 ||
            DodgeMaximum < DodgeMinimum || DodgeMaximum > FixedPointScale || ControlK < 1 ||
            ForceK < 1 || StunMinimumTicks < 1 || StunMaximumTicks < StunMinimumTicks ||
            MaximumHoldTicks < 1 || GrabLockoutTicks < 0)
        {
            throw new ArgumentException("Resolution global settings are inconsistent.");
        }
    }
}

internal sealed class ResolutionRuntimeSettings
{
    private readonly IReadOnlyDictionary<StableId, ResolutionActionProfile> _actions;

    internal ResolutionRuntimeSettings(
        ResolutionGlobalSettings global,
        IEnumerable<ResolutionActionProfile> actions)
    {
        Global = global ?? throw new ArgumentNullException(nameof(global));
        Global.Validate();
        var ordered = actions?.OrderBy(action => action.Id).ToArray() ??
            throw new ArgumentNullException(nameof(actions));
        if (ordered.Length == 0 || ordered.Select(action => action.Id).Distinct().Count() != ordered.Length)
        {
            throw new ArgumentException("Resolution action catalog must be non-empty and unique.", nameof(actions));
        }

        _actions = new ReadOnlyDictionary<StableId, ResolutionActionProfile>(
            ordered.ToDictionary(action => action.Id));
    }

    internal ResolutionGlobalSettings Global { get; }

    internal IReadOnlyList<ResolutionActionProfile> Actions => _actions.Values.OrderBy(item => item.Id).ToArray();

    internal ResolutionActionProfile GetAction(StableId id) =>
        _actions.TryGetValue(id, out var action)
            ? action
            : throw new KeyNotFoundException("Unknown resolution action '" + id + "'.");
}

internal static class ResolutionIdentifiers
{
    internal static ExternalId Intent(DecisionId decisionId, int ordinal) =>
        Format("intent:", decisionId, ordinal, "D2");

    internal static ExternalId Impact(DecisionId decisionId, int ordinal) =>
        Format("impact:", decisionId, ordinal, "D2");

    internal static ExternalId HitGroup(DecisionId decisionId, int ordinal) =>
        Format("hit-group:", decisionId, ordinal, "D2");

    internal static ExternalId Grab(DecisionId decisionId, int ordinal) =>
        Format("grab:", decisionId, ordinal, "D2");

    internal static ExternalId ResolutionGroup(int tick, int ordinal)
    {
        if (tick < 0 || ordinal < 0 || ordinal > 9_999)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        return new ExternalId(
            "resolution:" + tick.ToString("D10", CultureInfo.InvariantCulture) + ":" +
            ordinal.ToString("D4", CultureInfo.InvariantCulture));
    }

    internal static ExternalId Damage(ExternalId resolutionId, int ordinal) =>
        Format("damage:", resolutionId.Value, ordinal, "D2");

    internal static ExternalId Conflict(ExternalId resolutionId, int ordinal) =>
        Format("conflict:", resolutionId.Value, ordinal, "D2");

    private static ExternalId Format(string prefix, DecisionId decisionId, int ordinal, string format) =>
        Format(prefix, decisionId.Value, ordinal, format);

    private static ExternalId Format(string prefix, string owner, int ordinal, string format)
    {
        if (ordinal < 0 || ordinal > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        return new ExternalId(prefix + owner + ":" + ordinal.ToString(format, CultureInfo.InvariantCulture));
    }
}
