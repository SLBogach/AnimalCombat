namespace Battle.Core.Math;

/// <summary>
/// Canonical integer and fixed-point operations used by gameplay code.
/// </summary>
public static class FixedMath
{
    public static int Mul(int a, int b, int scale)
    {
        ValidateScale(scale);
        var numerator = checked((long)a * b);
        return FloorDiv(numerator, scale);
    }

    public static int Div(int a, int b, int scale)
    {
        ValidateScale(scale);

        if (b == 0)
        {
            throw new DivideByZeroException("A fixed-point divisor cannot be zero.");
        }

        var numerator = checked((long)a * scale);
        return FloorDiv(numerator, b);
    }

    public static int FloorDiv(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException("A denominator cannot be zero.");
        }

        var quotient = numerator / denominator;
        var remainder = numerator % denominator;

        if (remainder != 0 && ((numerator < 0) != (denominator < 0)))
        {
            quotient = checked(quotient - 1);
        }

        return checked((int)quotient);
    }

    public static int Clamp(int value, int min, int max)
    {
        if (min > max)
        {
            throw new ArgumentException(
                "The minimum bound cannot be greater than the maximum bound.",
                nameof(min));
        }

        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    public static int ProductSorted(ReadOnlySpan<Modifier> modifiers, int scale)
    {
        ValidateScale(scale);

        if (modifiers.IsEmpty)
        {
            return scale;
        }

        var ordered = new Modifier[modifiers.Length];
        modifiers.CopyTo(ordered);
        ValidateStableIds(ordered);
        Array.Sort(ordered, CanonicalModifierComparer.Instance);
        ValidateUniqueOrderingKeys(ordered);

        var product = scale;

        foreach (var modifier in ordered)
        {
            product = Mul(product, modifier.Value, scale);
        }

        return product;
    }

    private static void ValidateScale(int scale)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale),
                scale,
                "A fixed-point scale must be greater than zero.");
        }
    }

    private static void ValidateStableIds(ReadOnlySpan<Modifier> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (string.IsNullOrEmpty(modifier.StableId.Value))
            {
                throw new ArgumentException(
                    "Every modifier must have a non-default stable ID.",
                    nameof(modifiers));
            }
        }
    }

    private static void ValidateUniqueOrderingKeys(ReadOnlySpan<Modifier> modifiers)
    {
        for (var index = 1; index < modifiers.Length; index++)
        {
            var previous = modifiers[index - 1];
            var current = modifiers[index];

            if (previous.Priority == current.Priority &&
                previous.StableId == current.StableId)
            {
                throw new ArgumentException(
                    "Modifier ordering keys must be unique.",
                    nameof(modifiers));
            }
        }
    }

    private sealed class CanonicalModifierComparer : IComparer<Modifier>
    {
        public static CanonicalModifierComparer Instance { get; } = new();

        public int Compare(Modifier left, Modifier right)
        {
            var priorityComparison = left.Priority.CompareTo(right.Priority);
            return priorityComparison != 0
                ? priorityComparison
                : left.StableId.CompareTo(right.StableId);
        }
    }
}
