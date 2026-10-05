using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Effects;

public readonly record struct EffectModifier
{
    public EffectModifier(EffectModifierTarget target, EffectModifierOperation operation, int value, int ordinal)
    {
        if (!Enum.IsDefined(typeof(EffectModifierTarget), target)) throw new ArgumentOutOfRangeException(nameof(target));
        if (!Enum.IsDefined(typeof(EffectModifierOperation), operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        if (ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (target is EffectModifierTarget.HardControlAllowed or EffectModifierTarget.GrabAllowed or EffectModifierTarget.KnockdownAllowed)
        {
            if (operation != EffectModifierOperation.Override || value is < 0 or > 1)
                throw new ArgumentException("Allow channels require Override 0/1.", nameof(operation));
        }
        else if (target is EffectModifierTarget.DamageTaken or EffectModifierTarget.DamageDealt or
            EffectModifierTarget.BlockWeight or EffectModifierTarget.PunishWeight or
            EffectModifierTarget.WallActionWeight or EffectModifierTarget.HardControlDuration)
        {
            if (operation != EffectModifierOperation.Multiply) throw new ArgumentException("This channel requires Multiply.", nameof(operation));
        }
        else if (target is EffectModifierTarget.BlockChanceOffset or EffectModifierTarget.DodgeChanceOffset or EffectModifierTarget.GrabPriority)
        {
            if (operation != EffectModifierOperation.Add) throw new ArgumentException("This channel requires Add.", nameof(operation));
        }
        Target = target; Operation = operation; Value = value; Ordinal = ordinal;
    }
    public EffectModifierTarget Target { get; }
    public EffectModifierOperation Operation { get; }
    public int Value { get; }
    public int Ordinal { get; }
}

/// <summary>Immutable, typed WP-10 profile. Dormant WP-11 profiles are not executable profiles.</summary>
public sealed class EffectProfile
{
    public EffectProfile(StableId effectId, StableId stackGroup, int durationTicks, EffectExpiryBoundary expiryBoundary,
        EffectStackPolicy stackPolicy, int stackCap, EffectCompareKey? compareKey, int priority,
        EffectRefreshRule refreshRule, EffectSemanticRole semanticRole, int internalCooldownTicks,
        int maxActivationsPerTick, int maxActivationsPerBattle, IEnumerable<EffectModifier> modifiers,
        IEnumerable<int> lookup)
    {
        if (!StableId.TryParse(effectId.Value, out _)) throw new ArgumentException("A stable effect ID is required.", nameof(effectId));
        if (!StableId.TryParse(stackGroup.Value, out _)) throw new ArgumentException("A stable stack group is required.", nameof(stackGroup));
        if (durationTicks < 1) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        if (stackCap is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(stackCap));
        if (!Enum.IsDefined(typeof(EffectExpiryBoundary), expiryBoundary)) throw new ArgumentOutOfRangeException(nameof(expiryBoundary));
        if (!Enum.IsDefined(typeof(EffectStackPolicy), stackPolicy)) throw new ArgumentOutOfRangeException(nameof(stackPolicy));
        if (!Enum.IsDefined(typeof(EffectRefreshRule), refreshRule)) throw new ArgumentOutOfRangeException(nameof(refreshRule));
        if (!Enum.IsDefined(typeof(EffectSemanticRole), semanticRole)) throw new ArgumentOutOfRangeException(nameof(semanticRole));
        if (compareKey.HasValue && !Enum.IsDefined(typeof(EffectCompareKey), compareKey.Value)) throw new ArgumentOutOfRangeException(nameof(compareKey));
        if (stackPolicy == EffectStackPolicy.StrongestWins && !compareKey.HasValue) throw new ArgumentException("StrongestWins requires CompareKey.", nameof(compareKey));
        if (internalCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(internalCooldownTicks));
        if (maxActivationsPerTick < 1) throw new ArgumentOutOfRangeException(nameof(maxActivationsPerTick));
        if (maxActivationsPerBattle < 1) throw new ArgumentOutOfRangeException(nameof(maxActivationsPerBattle));
        if (modifiers is null) throw new ArgumentNullException(nameof(modifiers));
        if (lookup is null) throw new ArgumentNullException(nameof(lookup));
        var copiedModifiers = modifiers.OrderBy(x => x.Ordinal).ToArray();
        if (copiedModifiers.Select(x => x.Ordinal).Distinct().Count() != copiedModifiers.Length)
            throw new ArgumentException("Modifier ordinals must be unique.", nameof(modifiers));
        if (stackPolicy == EffectStackPolicy.StrongestWins && compareKey == EffectCompareKey.Value1 && copiedModifiers.Length == 0)
            throw new ArgumentException("Value1 requires a modifier.", nameof(modifiers));
        var copiedLookup = lookup.ToArray();
        if (semanticRole == EffectSemanticRole.ControlFatigue &&
            (copiedLookup.Length != stackCap + 1 || copiedLookup.Any(x => x < 0)))
            throw new ArgumentException("Fatigue requires nonnegative lookup[0..stack_cap].", nameof(lookup));
        EffectId = effectId; StackGroup = stackGroup; DurationTicks = durationTicks; ExpiryBoundary = expiryBoundary;
        StackPolicy = stackPolicy; StackCap = stackCap; CompareKey = compareKey; Priority = priority;
        RefreshRule = refreshRule; SemanticRole = semanticRole; InternalCooldownTicks = internalCooldownTicks;
        MaxActivationsPerTick = maxActivationsPerTick; MaxActivationsPerBattle = maxActivationsPerBattle;
        Modifiers = Array.AsReadOnly(copiedModifiers); Lookup = Array.AsReadOnly(copiedLookup);
    }
    public StableId EffectId { get; }
    public StableId StackGroup { get; }
    public int DurationTicks { get; }
    public EffectExpiryBoundary ExpiryBoundary { get; }
    public EffectStackPolicy StackPolicy { get; }
    public int StackCap { get; }
    public EffectCompareKey? CompareKey { get; }
    public int Priority { get; }
    public EffectRefreshRule RefreshRule { get; }
    public EffectSemanticRole SemanticRole { get; }
    public int InternalCooldownTicks { get; }
    public int MaxActivationsPerTick { get; }
    public int MaxActivationsPerBattle { get; }
    public IReadOnlyList<EffectModifier> Modifiers { get; }
    public IReadOnlyList<int> Lookup { get; }
}
