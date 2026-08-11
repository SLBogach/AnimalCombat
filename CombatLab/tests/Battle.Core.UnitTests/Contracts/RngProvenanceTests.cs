using Battle.Contracts.Events;

namespace Battle.Core.UnitTests.Contracts;

public sealed class RngProvenanceTests
{
    [Fact]
    public void RngVocabulary_MatchesReplaySchema()
    {
        Assert.Equal(
            new[] { "Decision", "Resolution" },
            Enum.GetNames<RngStream>());
        Assert.Equal(
            new[] { "NextInt", "TieBreak", "ChanceCheck" },
            Enum.GetNames<RngOperation>());
    }

    [Fact]
    public void Constructor_RejectsUnknownStream()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(stream: (RngStream)(-1)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Constructor_RejectsUnknownOperation(int operation)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(operation: (RngOperation)operation));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void Constructor_RejectsEmptyOrInvertedRange(
        int minimumInclusive,
        int maximumExclusive)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(
                minimumInclusive: minimumInclusive,
                maximumExclusive: maximumExclusive));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Constructor_RejectsResultOutsideRange(int result)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(result: result));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_001)]
    public void Constructor_RejectsNormalizedValueOutsideSchemaBounds(
        int normalizedFixedPoint)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(normalizedFixedPoint: normalizedFixedPoint));
    }

    [Fact]
    public void Constructor_AcceptsInclusiveNormalizedUpperBound()
    {
        var provenance = Create(
            stream: RngStream.Resolution,
            operation: RngOperation.ChanceCheck,
            normalizedFixedPoint: 1_000);

        Assert.Equal(RngStream.Resolution, provenance.Stream);
        Assert.Equal(7UL, provenance.Index);
        Assert.Equal(RngOperation.ChanceCheck, provenance.Operation);
        Assert.Equal(0, provenance.RangeMinimumInclusive);
        Assert.Equal(100, provenance.RangeMaximumExclusive);
        Assert.Equal(42U, provenance.RawValue);
        Assert.Equal(42, provenance.Result);
        Assert.Equal(1_000, provenance.NormalizedFixedPoint);
    }

    private static RngProvenance Create(
        RngStream stream = RngStream.Decision,
        RngOperation operation = RngOperation.NextInt,
        int minimumInclusive = 0,
        int maximumExclusive = 100,
        int result = 42,
        int normalizedFixedPoint = 420) =>
        new(
            stream,
            7UL,
            operation,
            minimumInclusive,
            maximumExclusive,
            42U,
            result,
            normalizedFixedPoint);
}
