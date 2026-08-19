using System.Reflection;
using Battle.Contracts.Events;
using Battle.Core.Random;

namespace Battle.Core.UnitTests.Random;

public sealed class GameplayRngTests
{
    public static TheoryData<ulong, RngStream, uint[]> StreamGoldenVectors =>
        new()
        {
            {
                0UL,
                RngStream.Decision,
                new uint[]
                {
                    2_879_411_843U,
                    495_049_527U,
                    4_034_860_953U,
                    53_069_154U,
                    1_600_103_088U,
                }
            },
            {
                0UL,
                RngStream.Resolution,
                new uint[]
                {
                    1_974_259_998U,
                    561_987_190U,
                    3_036_340_347U,
                    1_517_356_993U,
                    3_062_084_946U,
                }
            },
            {
                1UL,
                RngStream.Decision,
                new uint[]
                {
                    188_846_872U,
                    487_924_466U,
                    719_648_782U,
                    1_823_234_329U,
                    3_024_311_219U,
                }
            },
            {
                1UL,
                RngStream.Resolution,
                new uint[]
                {
                    2_251_341_377U,
                    240_642_153U,
                    3_668_381_410U,
                    112_224_381U,
                    866_665_506U,
                }
            },
            {
                ulong.MaxValue,
                RngStream.Decision,
                new uint[]
                {
                    2_707_707_362U,
                    4_137_630_125U,
                    564_018_303U,
                    4_194_948_619U,
                    745_525_726U,
                }
            },
            {
                ulong.MaxValue,
                RngStream.Resolution,
                new uint[]
                {
                    472_178_838U,
                    3_174_972_662U,
                    466_966_808U,
                    3_223_721_063U,
                    134_102_065U,
                }
            },
        };

    [Theory]
    [MemberData(nameof(StreamGoldenVectors))]
    public void Streams_MatchGoldenRawSequencesAndLogicalIndexes(
        ulong masterSeed,
        RngStream streamKind,
        uint[] expectedRawValues)
    {
        var random = new GameplayRng(masterSeed);
        var stream = streamKind == RngStream.Decision
            ? random.Decision
            : random.Resolution;
        var operation = streamKind == RngStream.Decision
            ? RngOperation.NextInt
            : RngOperation.ChanceCheck;

        Assert.Equal(0UL, stream.NextDrawIndex);

        for (var index = 0; index < expectedRawValues.Length; index++)
        {
            var draw = stream.NextInt(0, 1, operation);

            Assert.Equal(streamKind, draw.Stream);
            Assert.Equal((ulong)index, draw.Index);
            Assert.Equal(operation, draw.Operation);
            Assert.Equal(0, draw.RangeMinimumInclusive);
            Assert.Equal(1, draw.RangeMaximumExclusive);
            Assert.Equal(expectedRawValues[index], draw.RawValue);
            Assert.Equal(0, draw.Result);
            Assert.Equal(0, draw.NormalizedFixedPoint);
        }

        Assert.Equal((ulong)expectedRawValues.Length, stream.NextDrawIndex);
    }

    [Fact]
    public void DecisionAndResolution_AreIndependentWhenInterleaved()
    {
        var baseline = new GameplayRng(0UL);
        var expectedResolution =
            baseline.Resolution.NextInt(0, 1_000, RngOperation.ChanceCheck);
        var random = new GameplayRng(0UL);

        _ = random.Decision.NextInt(0, 1_000, RngOperation.NextInt);
        _ = random.Decision.NextInt(0, 1_000, RngOperation.NextInt);
        var actualResolution =
            random.Resolution.NextInt(0, 1_000, RngOperation.ChanceCheck);

        Assert.Equal(expectedResolution, actualResolution);
        Assert.Equal(2UL, random.Decision.NextDrawIndex);
        Assert.Equal(1UL, random.Resolution.NextDrawIndex);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void PreviewCommit_AcceptsOwnedForwardPreviewAndRejectsEveryForeignShape()
    {
        var stream = new GameplayRng(42UL).Decision;

        Assert.Throws<ArgumentNullException>(() => stream.CommitPreview(null!));
        Assert.Throws<ArgumentException>(() =>
            stream.CommitPreview(new GameplayRng(42UL).Resolution.CreatePreview()));
        Assert.Throws<ArgumentException>(() =>
            stream.CommitPreview(new GameplayRng(43UL).Decision.CreatePreview()));

        var stale = stream.CreatePreview();
        _ = stream.NextInt(0, 1_000, RngOperation.NextInt);
        Assert.Throws<ArgumentException>(() => stream.CommitPreview(stale));

        var forward = stream.CreatePreview();
        var previewedDraw = forward.NextInt(0, 1_000, RngOperation.NextInt);
        stream.CommitPreview(forward);

        Assert.Equal(2UL, stream.NextDrawIndex);
        Assert.Equal(1UL, previewedDraw.Index);
        Assert.Equal(
            forward.NextInt(0, 1_000, RngOperation.NextInt),
            stream.NextInt(0, 1_000, RngOperation.NextInt));
    }

    [Fact]
    public void NextInt_UsesRejectionSamplingButConsumesOneLogicalIndex()
    {
        var stream = new GameplayRng(1UL).Decision;

        var first = stream.NextInt(
            0,
            1_500_000_000,
            RngOperation.NextInt);
        var second = stream.NextInt(
            0,
            1_500_000_000,
            RngOperation.NextInt);

        Assert.Equal(0UL, first.Index);
        Assert.Equal(1_823_234_329U, first.RawValue);
        Assert.Equal(323_234_329, first.Result);
        Assert.Equal(215, first.NormalizedFixedPoint);
        Assert.Equal(1UL, second.Index);
        Assert.Equal(3_024_311_219U, second.RawValue);
        Assert.Equal(24_311_219, second.Result);
        Assert.Equal(16, second.NormalizedFixedPoint);
        Assert.Equal(2UL, stream.NextDrawIndex);
    }

    [Fact]
    public void NextInt_ProducesExactProvenanceAndRangeRelativeNormalization()
    {
        var stream = new GameplayRng(0UL).Decision;

        var draw = stream.NextInt(0, 1_150, RngOperation.NextInt);

        Assert.Equal(RngStream.Decision, draw.Stream);
        Assert.Equal(0UL, draw.Index);
        Assert.Equal(RngOperation.NextInt, draw.Operation);
        Assert.Equal(0, draw.RangeMinimumInclusive);
        Assert.Equal(1_150, draw.RangeMaximumExclusive);
        Assert.Equal(2_879_411_843U, draw.RawValue);
        Assert.Equal(443, draw.Result);
        Assert.Equal(385, draw.NormalizedFixedPoint);
    }

    [Fact]
    public void NextInt_SupportsTheWidestInt32RangeWithoutIntermediateOverflow()
    {
        var stream = new GameplayRng(0UL).Decision;

        var draw = stream.NextInt(
            int.MinValue,
            int.MaxValue,
            RngOperation.NextInt);

        Assert.Equal(2_879_411_843U, draw.RawValue);
        Assert.Equal(731_928_195, draw.Result);
        Assert.Equal(670, draw.NormalizedFixedPoint);
    }

    [Theory]
    [InlineData(RngStream.Decision, RngOperation.NextInt)]
    [InlineData(RngStream.Resolution, RngOperation.TieBreak)]
    [InlineData(RngStream.Resolution, RngOperation.ChanceCheck)]
    public void NextInt_PreservesEverySupportedOperation(
        RngStream streamKind,
        RngOperation operation)
    {
        var random = new GameplayRng(0UL);
        var stream = streamKind == RngStream.Decision
            ? random.Decision
            : random.Resolution;

        var draw = stream.NextInt(-100, 101, operation);

        Assert.Equal(operation, draw.Operation);
        Assert.InRange(draw.Result, -100, 100);
        Assert.InRange(draw.NormalizedFixedPoint, 0, 999);
    }

    [Theory]
    [InlineData(RngStream.Decision, RngOperation.TieBreak)]
    [InlineData(RngStream.Decision, RngOperation.ChanceCheck)]
    [InlineData(RngStream.Resolution, RngOperation.NextInt)]
    public void OperationForWrongStream_DoesNotConsumeStateOrIndex(
        RngStream streamKind,
        RngOperation invalidOperation)
    {
        var baselineRandom = new GameplayRng(0UL);
        var random = new GameplayRng(0UL);
        var baseline = streamKind == RngStream.Decision
            ? baselineRandom.Decision
            : baselineRandom.Resolution;
        var stream = streamKind == RngStream.Decision
            ? random.Decision
            : random.Resolution;
        var validOperation = streamKind == RngStream.Decision
            ? RngOperation.NextInt
            : RngOperation.TieBreak;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => stream.NextInt(0, 100, invalidOperation));

        Assert.Equal(0UL, stream.NextDrawIndex);
        Assert.Equal(
            baseline.NextInt(0, 100, validOperation),
            stream.NextInt(0, 100, validOperation));
    }

    [Fact]
    public void InvalidOperation_DoesNotConsumeStateOrIndex()
    {
        var baseline = new GameplayRng(0UL).Decision;
        var stream = new GameplayRng(0UL).Decision;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => stream.NextInt(0, 100, (RngOperation)(-1)));

        Assert.Equal(0UL, stream.NextDrawIndex);
        Assert.Equal(
            baseline.NextInt(0, 100, RngOperation.NextInt),
            stream.NextInt(0, 100, RngOperation.NextInt));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    public void InvalidRange_DoesNotConsumeStateOrIndex(
        int minimumInclusive,
        int maximumExclusive)
    {
        var baseline = new GameplayRng(0UL).Decision;
        var stream = new GameplayRng(0UL).Decision;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => stream.NextInt(
                minimumInclusive,
                maximumExclusive,
                RngOperation.NextInt));

        Assert.Equal(0UL, stream.NextDrawIndex);
        Assert.Equal(
            baseline.NextInt(0, 100, RngOperation.NextInt),
            stream.NextInt(0, 100, RngOperation.NextInt));
    }

    [Fact]
    public void GameplayRng_ExposesOnlyGameplayStreams()
    {
        var propertyNames = typeof(GameplayRng)
            .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(new[] { "Decision", "Resolution" }, propertyNames);
    }

    [Fact]
    public void ReinitializingSameSeed_RepeatsExactSequenceOneHundredTimes()
    {
        var expectedRandom = new GameplayRng(1UL);
        var expectedDecision = Enumerable
            .Range(0, 16)
            .Select(_ => expectedRandom.Decision.NextInt(
                -10_000,
                10_001,
                RngOperation.NextInt))
            .ToArray();
        var expectedResolution = Enumerable
            .Range(0, 16)
            .Select(_ => expectedRandom.Resolution.NextInt(
                0,
                1_000,
                RngOperation.ChanceCheck))
            .ToArray();

        for (var run = 0; run < 100; run++)
        {
            var random = new GameplayRng(1UL);
            var actualDecision = Enumerable
                .Range(0, 16)
                .Select(_ => random.Decision.NextInt(
                    -10_000,
                    10_001,
                    RngOperation.NextInt));
            var actualResolution = Enumerable
                .Range(0, 16)
                .Select(_ => random.Resolution.NextInt(
                    0,
                    1_000,
                    RngOperation.ChanceCheck));

            Assert.Equal(expectedDecision, actualDecision);
            Assert.Equal(expectedResolution, actualResolution);
        }
    }
}
