using Battle.Contracts.Events;

namespace Battle.Core.Random;

internal sealed class Pcg32Stream
{
    private const ulong DecisionDomain = 0x4445434953494F4EUL;
    private const ulong ResolutionDomain = 0x5245534F4C555449UL;
    private const ulong Multiplier = 6_364_136_223_846_793_005UL;
    private const int NormalizedScale = 1_000;

    private readonly ulong _increment;
    private ulong _state;

    private Pcg32Stream(RngStream stream, ulong masterSeed, ulong domain)
    {
        Stream = stream;

        var seed = SplitMix64.Mix(masterSeed ^ domain);
        var sequence = SplitMix64.Mix(unchecked(masterSeed + domain));

        _increment = unchecked((sequence << 1) | 1UL);
        _state = 0;
        _ = NextUInt32Core();
        _state = unchecked(_state + seed);
        _ = NextUInt32Core();
        NextDrawIndex = 0;
    }

    internal RngStream Stream { get; }

    internal ulong NextDrawIndex { get; private set; }

    internal static Pcg32Stream CreateDecision(ulong masterSeed) =>
        new(RngStream.Decision, masterSeed, DecisionDomain);

    internal static Pcg32Stream CreateResolution(ulong masterSeed) =>
        new(RngStream.Resolution, masterSeed, ResolutionDomain);

    internal RngProvenance NextInt(
        int minimumInclusive,
        int maximumExclusive,
        RngOperation operation)
    {
        if (!IsOperationSupported(operation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(operation),
                operation,
                "The operation is not valid for this gameplay RNG stream.");
        }

        if (maximumExclusive <= minimumInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumExclusive),
                "The exclusive upper bound must be greater than the inclusive lower bound.");
        }

        var index = NextDrawIndex;
        var nextIndex = checked(index + 1);
        var bound = checked((uint)((long)maximumExclusive - minimumInclusive));
        var threshold = unchecked(0U - bound) % bound;
        uint rawValue;

        do
        {
            rawValue = NextUInt32Core();
        }
        while (rawValue < threshold);

        var offset = rawValue % bound;
        var result = checked((int)((long)minimumInclusive + offset));

        // v0.1's replay fixture expresses normalized_fp as the range-relative
        // floor at scale 1,000. It is explanatory and never drives the result.
        var normalizedFixedPoint =
            checked((int)((long)offset * NormalizedScale / bound));
        var provenance = new RngProvenance(
            Stream,
            index,
            operation,
            minimumInclusive,
            maximumExclusive,
            rawValue,
            result,
            normalizedFixedPoint);

        NextDrawIndex = nextIndex;
        return provenance;
    }

    private bool IsOperationSupported(RngOperation operation) =>
        (Stream, operation) switch
        {
            (RngStream.Decision, RngOperation.NextInt) => true,
            (RngStream.Resolution, RngOperation.TieBreak) => true,
            (RngStream.Resolution, RngOperation.ChanceCheck) => true,
            _ => false,
        };

    private uint NextUInt32Core()
    {
        var oldState = _state;
        _state = unchecked(oldState * Multiplier + _increment);

        var xorshifted = unchecked((uint)(((oldState >> 18) ^ oldState) >> 27));
        var rotation = (int)(oldState >> 59);
        return (xorshifted >> rotation) |
               (xorshifted << ((-rotation) & 31));
    }
}
