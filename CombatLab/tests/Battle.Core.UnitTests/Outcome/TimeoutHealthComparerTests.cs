using Battle.Core.Outcome;

namespace Battle.Core.UnitTests.Outcome;

public sealed class TimeoutHealthComparerTests
{
    [Theory]
    [InlineData(50, 100, 40, 100, 1)]
    [InlineData(40, 100, 50, 100, -1)]
    [InlineData(1, 3, 2, 6, 0)]
    [InlineData(0, 100, 0, 1, 0)]
    [InlineData(100, 100, 1, 2, 1)]
    [InlineData(1, 2, 100, 100, -1)]
    public void Compare_UsesExactCrossMultiplication(
        int leftCurrent,
        int leftMaximum,
        int rightCurrent,
        int rightMaximum,
        int expected)
    {
        Assert.Equal(
            expected,
            TimeoutHealthComparer.Compare(
                leftCurrent,
                leftMaximum,
                rightCurrent,
                rightMaximum));
    }

    [Fact]
    public void Compare_HandlesInt32BoundariesWithoutOverflow()
    {
        var maximum = int.MaxValue;

        var comparison = TimeoutHealthComparer.Compare(
            maximum - 1,
            maximum,
            maximum - 2,
            maximum - 1);

        Assert.Equal(1, comparison);
    }

    [Fact]
    public void Compare_MatchesCrossProductOracleAcrossSmallHealthRanges()
    {
        for (var leftMaximum = 1; leftMaximum <= 16; leftMaximum++)
        {
            for (var leftCurrent = 0; leftCurrent <= leftMaximum; leftCurrent++)
            {
                for (var rightMaximum = 1; rightMaximum <= 16; rightMaximum++)
                {
                    for (var rightCurrent = 0; rightCurrent <= rightMaximum; rightCurrent++)
                    {
                        var expected = System.Math.Sign(
                            ((long)leftCurrent * rightMaximum) -
                            ((long)rightCurrent * leftMaximum));
                        var actual = TimeoutHealthComparer.Compare(
                            leftCurrent,
                            leftMaximum,
                            rightCurrent,
                            rightMaximum);
                        var reversed = TimeoutHealthComparer.Compare(
                            rightCurrent,
                            rightMaximum,
                            leftCurrent,
                            leftMaximum);

                        Assert.Equal(expected, actual);
                        Assert.Equal(-actual, reversed);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(-1, 100, 50, 100)]
    [InlineData(101, 100, 50, 100)]
    [InlineData(0, 0, 50, 100)]
    [InlineData(50, 100, -1, 100)]
    [InlineData(50, 100, 101, 100)]
    [InlineData(50, 100, 0, 0)]
    public void Compare_RejectsInvalidHealthFractions(
        int leftCurrent,
        int leftMaximum,
        int rightCurrent,
        int rightMaximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TimeoutHealthComparer.Compare(
                leftCurrent,
                leftMaximum,
                rightCurrent,
                rightMaximum));
    }
}
