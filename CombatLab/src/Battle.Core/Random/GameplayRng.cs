namespace Battle.Core.Random;

internal sealed class GameplayRng
{
    internal GameplayRng(ulong masterSeed)
    {
        Decision = Pcg32Stream.CreateDecision(masterSeed);
        Resolution = Pcg32Stream.CreateResolution(masterSeed);
    }

    internal Pcg32Stream Decision { get; }

    internal Pcg32Stream Resolution { get; }
}
