using Battle.Contracts.Ids;

namespace Battle.Core.Math;

/// <summary>
/// A fixed-point multiplier with the canonical ordering key required by the combat rules.
/// </summary>
public readonly struct Modifier
{
    public Modifier(int priority, StableId stableId, int value)
    {
        if (string.IsNullOrEmpty(stableId.Value))
        {
            throw new ArgumentException(
                "A modifier must have a non-default stable ID.",
                nameof(stableId));
        }

        Priority = priority;
        StableId = stableId;
        Value = value;
    }

    public int Priority { get; }

    public StableId StableId { get; }

    public int Value { get; }
}
