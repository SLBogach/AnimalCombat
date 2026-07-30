using Battle.Contracts.Ids;
using Battle.Core.Math;

namespace Battle.Core.UnitTests.Math;

public sealed class FixedMathTests
{
    [Theory]
    [InlineData(0L, 1L, 0)]
    [InlineData(1L, 1L, 1)]
    [InlineData(-1L, 1L, -1)]
    [InlineData(1L, 2L, 0)]
    [InlineData(-1L, 2L, -1)]
    [InlineData(1L, -2L, -1)]
    [InlineData(-1L, -2L, 0)]
    [InlineData(7L, 3L, 2)]
    [InlineData(-7L, 3L, -3)]
    [InlineData(7L, -3L, -3)]
    [InlineData(-7L, -3L, 2)]
    [InlineData(int.MaxValue, 1L, int.MaxValue)]
    [InlineData(int.MinValue, 1L, int.MinValue)]
    [InlineData(long.MinValue, long.MaxValue, -2)]
    [InlineData(long.MaxValue, long.MinValue, -1)]
    [InlineData(long.MinValue, long.MinValue, 1)]
    public void FloorDiv_UsesMathematicalFloor(
        long numerator,
        long denominator,
        int expected)
    {
        Assert.Equal(expected, FixedMath.FloorDiv(numerator, denominator));
    }

    [Fact]
    public void FloorDiv_SatisfiesFloorInequalityAcrossSignMatrix()
    {
        for (var numerator = -50; numerator <= 50; numerator++)
        {
            for (var denominator = -10; denominator <= 10; denominator++)
            {
                if (denominator == 0)
                {
                    continue;
                }

                var normalizedNumerator = (long)numerator;
                var normalizedDenominator = (long)denominator;

                if (normalizedDenominator < 0)
                {
                    normalizedNumerator = -normalizedNumerator;
                    normalizedDenominator = -normalizedDenominator;
                }

                var quotient = FixedMath.FloorDiv(numerator, denominator);

                Assert.True((long)quotient * normalizedDenominator <= normalizedNumerator);
                Assert.True(normalizedNumerator < (long)(quotient + 1) * normalizedDenominator);
            }
        }
    }

    [Fact]
    public void FloorDiv_RejectsZeroDenominator()
    {
        Assert.Throws<DivideByZeroException>(() => FixedMath.FloorDiv(1, 0));
    }

    [Fact]
    public void FloorDiv_RejectsResultsOutsideInt32()
    {
        Assert.Throws<OverflowException>(
            () => FixedMath.FloorDiv((long)int.MaxValue + 1, 1));
        Assert.Throws<OverflowException>(
            () => FixedMath.FloorDiv((long)int.MinValue - 1, 1));
        Assert.Throws<OverflowException>(
            () => FixedMath.FloorDiv(long.MinValue, -1));
    }

    [Theory]
    [InlineData(0, int.MaxValue, 1_000, 0)]
    [InlineData(1, 1, 1_000, 0)]
    [InlineData(-1, 1, 1_000, -1)]
    [InlineData(1, -1, 1_000, -1)]
    [InlineData(-1, -1, 1_000, 0)]
    [InlineData(999, 1_000, 1_000, 999)]
    [InlineData(1_000, 1_000, 1_000, 1_000)]
    [InlineData(1_001, 1_000, 1_000, 1_001)]
    [InlineData(999, 999, 1_000, 998)]
    [InlineData(1_001, 1_001, 1_000, 1_002)]
    [InlineData(999, 1_001, 1_000, 999)]
    [InlineData(-999, 1_001, 1_000, -1_000)]
    [InlineData(int.MaxValue, 1_000, 1_000, int.MaxValue)]
    [InlineData(int.MinValue, 1_000, 1_000, int.MinValue)]
    public void Mul_MatchesCanonicalGoldenVectors(int a, int b, int scale, int expected)
    {
        Assert.Equal(expected, FixedMath.Mul(a, b, scale));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Mul_RejectsNonPositiveScale(int scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FixedMath.Mul(1, 1, scale));
    }

    [Fact]
    public void Mul_RejectsInt32ResultOverflow()
    {
        Assert.Throws<OverflowException>(
            () => FixedMath.Mul(int.MaxValue, int.MaxValue, 1));
        Assert.Throws<OverflowException>(
            () => FixedMath.Mul(int.MinValue, int.MinValue, 1));
    }

    [Theory]
    [InlineData(0, 1, 1_000, 0)]
    [InlineData(1, 1, 1_000, 1_000)]
    [InlineData(1, 3, 1_000, 333)]
    [InlineData(-1, 3, 1_000, -334)]
    [InlineData(1, -3, 1_000, -334)]
    [InlineData(-1, -3, 1_000, 333)]
    [InlineData(999, 1_000, 1_000, 999)]
    [InlineData(1_000, 1_000, 1_000, 1_000)]
    [InlineData(1_001, 1_000, 1_000, 1_001)]
    [InlineData(1, 1_001, 1_000, 0)]
    [InlineData(-1, 1_001, 1_000, -1)]
    [InlineData(int.MaxValue, int.MaxValue, 1_000, 1_000)]
    [InlineData(int.MinValue, int.MaxValue, 1_000, -1_001)]
    [InlineData(int.MaxValue, int.MinValue, 1_000, -1_000)]
    [InlineData(int.MinValue, int.MinValue, 1_000, 1_000)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, int.MaxValue, int.MaxValue, int.MinValue)]
    public void Div_MatchesCanonicalGoldenVectors(int a, int b, int scale, int expected)
    {
        Assert.Equal(expected, FixedMath.Div(a, b, scale));
    }

    [Fact]
    public void Div_RejectsZeroDivisor()
    {
        Assert.Throws<DivideByZeroException>(() => FixedMath.Div(1, 0, 1_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Div_RejectsNonPositiveScale(int scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FixedMath.Div(1, 1, scale));
    }

    [Fact]
    public void Div_RejectsInt32ResultOverflow()
    {
        Assert.Throws<OverflowException>(
            () => FixedMath.Div(int.MaxValue, 1, int.MaxValue));
        Assert.Throws<OverflowException>(
            () => FixedMath.Div(int.MinValue, -1, 1));
    }

    [Theory]
    [InlineData(-1, 0, 10, 0)]
    [InlineData(0, 0, 10, 0)]
    [InlineData(5, 0, 10, 5)]
    [InlineData(10, 0, 10, 10)]
    [InlineData(11, 0, 10, 10)]
    [InlineData(int.MinValue, int.MinValue, int.MaxValue, int.MinValue)]
    [InlineData(int.MaxValue, int.MinValue, int.MaxValue, int.MaxValue)]
    [InlineData(7, 7, 7, 7)]
    public void Clamp_UsesInclusiveBounds(int value, int min, int max, int expected)
    {
        Assert.Equal(expected, FixedMath.Clamp(value, min, max));
    }

    [Fact]
    public void Clamp_RejectsInvertedBounds()
    {
        Assert.Throws<ArgumentException>(() => FixedMath.Clamp(0, 1, 0));
    }

    [Fact]
    public void ProductSorted_ReturnsMultiplicativeIdentityForEmptyInput()
    {
        Assert.Equal(1_000, FixedMath.ProductSorted(ReadOnlySpan<Modifier>.Empty, 1_000));
    }

    [Fact]
    public void ProductSorted_UsesPriorityAndDoesNotMutateInput()
    {
        var modifiers = new[]
        {
            CreateModifier(30, "modifier_c", 20),
            CreateModifier(10, "modifier_a", 1),
            CreateModifier(20, "modifier_b", 5),
        };
        var original = modifiers.ToArray();

        var product = FixedMath.ProductSorted(modifiers, 10);

        Assert.Equal(0, product);
        Assert.Equal(original, modifiers);
    }

    [Fact]
    public void ProductSorted_UsesOrdinalStableIdAsTieBreak()
    {
        var modifiers = new[]
        {
            CreateModifier(10, "modifier_c", 5),
            CreateModifier(10, "modifier_a", 1),
            CreateModifier(10, "modifier_b", 20),
        };

        Assert.Equal(1, FixedMath.ProductSorted(modifiers, 10));
    }

    [Fact]
    public void ProductSorted_UsesLexicalRatherThanNaturalStableIdOrder()
    {
        var modifiers = new[]
        {
            CreateModifier(10, "m_3", 999),
            CreateModifier(10, "m_2", 999),
            CreateModifier(10, "m_10", 1_500),
        };

        Assert.Equal(1_496, FixedMath.ProductSorted(modifiers, 1_000));
    }

    [Fact]
    public void ProductSorted_HandlesFullPriorityRangeWithoutSubtractionOverflow()
    {
        var modifiers = new[]
        {
            CreateModifier(int.MaxValue, "last", 1_499),
            CreateModifier(0, "middle", 501),
            CreateModifier(int.MinValue, "first", 501),
        };

        Assert.Equal(376, FixedMath.ProductSorted(modifiers, 1_000));
    }

    [Fact]
    public void ProductSorted_UsesFloorAtEveryStep()
    {
        var modifiers = new[]
        {
            CreateModifier(10, "negative_half", -1),
            CreateModifier(0, "positive_half", 500),
        };

        Assert.Equal(-1, FixedMath.ProductSorted(modifiers, 1_000));
    }

    [Fact]
    public void ProductSorted_HasGoldenVectorForOrderSensitiveFlooring()
    {
        var canonicalOrder = new[]
        {
            CreateModifier(10, "first", 501),
            CreateModifier(20, "second", 501),
            CreateModifier(30, "third", 1_499),
        };
        var differentPriorityOrder = new[]
        {
            CreateModifier(10, "first", 501),
            CreateModifier(30, "second", 501),
            CreateModifier(20, "third", 1_499),
        };

        Assert.Equal(376, FixedMath.ProductSorted(canonicalOrder, 1_000));
        Assert.Equal(375, FixedMath.ProductSorted(differentPriorityOrder, 1_000));
    }

    [Fact]
    public void ProductSorted_RejectsAmbiguousOrInvalidOrderingKeys()
    {
        var duplicateKey = new[]
        {
            CreateModifier(1, "same_modifier", 900),
            CreateModifier(1, "same_modifier", 1_100),
        };
        var defaultStableId = new[]
        {
            default(Modifier),
        };

        Assert.Throws<ArgumentException>(
            () => FixedMath.ProductSorted(duplicateKey, 1_000));
        Assert.Throws<ArgumentException>(
            () => FixedMath.ProductSorted(defaultStableId, 1_000));
        Assert.Throws<ArgumentException>(
            () => new Modifier(1, default, 1_000));
    }

    [Fact]
    public void ProductSorted_RejectsInvalidScaleAndOverflow()
    {
        var modifiers = new[]
        {
            CreateModifier(0, "first", int.MaxValue),
            CreateModifier(1, "second", int.MaxValue),
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => FixedMath.ProductSorted(modifiers, 0));
        Assert.Throws<OverflowException>(
            () => FixedMath.ProductSorted(modifiers, 1));
    }

    private static Modifier CreateModifier(int priority, string stableId, int value) =>
        new(priority, new StableId(stableId), value);
}
