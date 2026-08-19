using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public enum DecisionSelectionMode
{
    WeightedRng,
    OnlyLegalAction,
    ZeroWeightFallback,
    HardOpportunity,
}

public enum CommitDirection
{
    Left,
    Right,
    None,
}

public enum MovementDirection
{
    Left,
    Right,
}

public enum MoveStartKind
{
    Voluntary,
    Dodge,
    Approach,
    Retreat,
}

public enum PositionChangeKind
{
    Voluntary,
    Forced,
    Dodge,
    Separation,
    Swap,
}

public enum ConflictResolutionResult
{
    AWin,
    BWin,
    Trade,
    BothCancelled,
}

public enum ConflictTieBreakMethod
{
    Priority,
    Initiative,
    SeededHash,
    NotNeeded,
}

public enum AttackMissReason
{
    OutOfRange,
    InvalidTarget,
    WrongDirection,
    HitGroupConsumed,
    DefeatedTarget,
}

public enum ResourceKind
{
    Energy,
    UniqueResource,
    Stagger,
}

public enum ResourceClampReason
{
    Minimum,
    Maximum,
    Defeated,
    NoChange,
}

public enum EffectStackPolicy
{
    Reject,
    Refresh,
    Replace,
    StrongestWins,
    AddStacks,
}

public enum EffectRemoveReason
{
    ExpiredBeforeTick,
    ExpiredAfterTick,
    Replaced,
    Dispelled,
    Consumed,
    BattleEnded,
}

public enum ImmunityResult
{
    NotChecked,
    Allowed,
    Prevented,
}

public enum GrabPriorityResult
{
    Uncontested,
    Priority,
    Initiative,
    SeededTieBreak,
}

public enum GrabEndReason
{
    Throw,
    Release,
    Escape,
    Interrupted,
    GrabberDefeated,
    TargetDefeated,
    MaxHoldReached,
}

public enum FinisherMarkerKind
{
    PredictedLethalImpact,
    SignatureFinish,
    DoubleKORisk,
}

public enum FinisherConfidence
{
    GuaranteedByCurrentIntent,
}

public enum DrawReason
{
    DoubleKO,
    TimeoutEqualHealthFraction,
}

public readonly record struct ModifierTrace
{
    public ModifierTrace(ReasonCode code, int multiplierFixedPoint)
    {
        if (multiplierFixedPoint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplierFixedPoint));
        }

        Code = code;
        MultiplierFixedPoint = multiplierFixedPoint;
    }

    public ReasonCode Code { get; }

    public int MultiplierFixedPoint { get; }
}

public readonly record struct DamageBreakdown
{
    public DamageBreakdown(
        int powerTerm,
        int raw,
        int afterArmor,
        int afterBlock,
        int final,
        int minimum,
        int cap,
        int overkill)
    {
        PayloadContract.RequireNonNegative(powerTerm, nameof(powerTerm));
        PayloadContract.RequireNonNegative(raw, nameof(raw));
        PayloadContract.RequireNonNegative(afterArmor, nameof(afterArmor));
        PayloadContract.RequireNonNegative(afterBlock, nameof(afterBlock));
        PayloadContract.RequireNonNegative(final, nameof(final));
        PayloadContract.RequireNonNegative(minimum, nameof(minimum));
        PayloadContract.RequireNonNegative(cap, nameof(cap));
        PayloadContract.RequireNonNegative(overkill, nameof(overkill));

        PowerTerm = powerTerm;
        Raw = raw;
        AfterArmor = afterArmor;
        AfterBlock = afterBlock;
        Final = final;
        Minimum = minimum;
        Cap = cap;
        Overkill = overkill;
    }

    public int PowerTerm { get; }

    public int Raw { get; }

    public int AfterArmor { get; }

    public int AfterBlock { get; }

    public int Final { get; }

    public int Minimum { get; }

    public int Cap { get; }

    public int Overkill { get; }
}

internal static class PayloadContract
{
    public static ReadOnlyCollection<T> Copy<T>(
        IEnumerable<T> values,
        int minimumCount,
        int maximumCount,
        string parameterName,
        bool requireUnique = false)
    {
        if (values is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var copy = new List<T>(values);
        if (copy.Count < minimumCount || copy.Count > maximumCount)
        {
            throw new ArgumentException(
                $"The collection must contain between {minimumCount} and {maximumCount} entries.",
                parameterName);
        }

        if (requireUnique && new HashSet<T>(copy).Count != copy.Count)
        {
            throw new ArgumentException("The collection must not contain duplicate entries.", parameterName);
        }

        return new ReadOnlyCollection<T>(copy);
    }

    public static void RequireDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void RequireKnownFighter(FighterId value, string parameterName)
    {
        if (value is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void RequireKnownFighter(FighterId? value, string parameterName)
    {
        if (value.HasValue)
        {
            RequireKnownFighter(value.Value, parameterName);
        }
    }

    public static void RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void RequireNonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void RequireFixedPoint(int value, string parameterName)
    {
        if (value is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static void RequireStrictlySorted<T>(
        IReadOnlyList<T> values,
        Comparison<T> comparison,
        string parameterName)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (comparison(values[index - 1], values[index]) >= 0)
            {
                throw new ArgumentException(
                    "The collection must be in strict canonical order.",
                    parameterName);
            }
        }
    }
}
