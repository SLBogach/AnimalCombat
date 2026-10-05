namespace Battle.Contracts.Effects;

/// <summary>Explicit DATA bounds, never an engine-supplied fallback.</summary>
public readonly record struct StatBounds
{
    public StatBounds(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(maximum));
        Minimum = minimum;
        Maximum = maximum;
    }

    public int Minimum { get; }
    public int Maximum { get; }

    public int Clamp(int value) => value < Minimum ? Minimum : value > Maximum ? Maximum : value;
}
