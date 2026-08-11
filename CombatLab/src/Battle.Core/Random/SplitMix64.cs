namespace Battle.Core.Random;

internal static class SplitMix64
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;
    private const ulong FirstMultiplier = 0xBF58476D1CE4E5B9UL;
    private const ulong SecondMultiplier = 0x94D049BB133111EBUL;

    internal static ulong Mix(ulong value)
    {
        var mixed = unchecked(value + GoldenGamma);
        mixed = unchecked((mixed ^ (mixed >> 30)) * FirstMultiplier);
        mixed = unchecked((mixed ^ (mixed >> 27)) * SecondMultiplier);
        return mixed ^ (mixed >> 31);
    }
}
