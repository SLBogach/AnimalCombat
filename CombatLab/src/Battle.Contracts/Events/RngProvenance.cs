namespace Battle.Contracts.Events;

public readonly record struct RngProvenance
{
    public RngProvenance(
        RngStream stream,
        ulong index,
        RngOperation operation,
        int rangeMinimumInclusive,
        int rangeMaximumExclusive,
        uint rawValue,
        int result,
        int normalizedFixedPoint)
    {
        if (stream is not RngStream.Decision and not RngStream.Resolution)
        {
            throw new ArgumentOutOfRangeException(nameof(stream));
        }

        if (operation is < RngOperation.NextInt or > RngOperation.ChanceCheck)
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (rangeMaximumExclusive <= rangeMinimumInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rangeMaximumExclusive),
                "The exclusive upper bound must be greater than the inclusive lower bound.");
        }

        if (result < rangeMinimumInclusive || result >= rangeMaximumExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(result));
        }

        if (normalizedFixedPoint is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedFixedPoint));
        }

        Stream = stream;
        Index = index;
        Operation = operation;
        RangeMinimumInclusive = rangeMinimumInclusive;
        RangeMaximumExclusive = rangeMaximumExclusive;
        RawValue = rawValue;
        Result = result;
        NormalizedFixedPoint = normalizedFixedPoint;
    }

    public RngStream Stream { get; }

    public ulong Index { get; }

    public RngOperation Operation { get; }

    public int RangeMinimumInclusive { get; }

    public int RangeMaximumExclusive { get; }

    public uint RawValue { get; }

    public int Result { get; }

    public int NormalizedFixedPoint { get; }
}
