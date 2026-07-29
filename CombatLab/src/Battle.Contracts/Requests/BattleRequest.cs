using Battle.Contracts.Versions;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Requests;

public sealed class BattleRequest
{
    public BattleRequest(
        ArtifactVersion engineVersion,
        Sha256Digest configHash,
        ArtifactVersion modeRulesVersion,
        ulong masterSeed,
        FighterBuildSnapshot buildA,
        FighterBuildSnapshot buildB)
    {
        if (buildA is null)
        {
            throw new ArgumentNullException(nameof(buildA));
        }

        if (buildB is null)
        {
            throw new ArgumentNullException(nameof(buildB));
        }

        if (buildA.Side != FighterSide.A)
        {
            throw new ArgumentException("Build A must occupy side A.", nameof(buildA));
        }

        if (buildB.Side != FighterSide.B)
        {
            throw new ArgumentException("Build B must occupy side B.", nameof(buildB));
        }

        EngineVersion = engineVersion;
        ConfigHash = configHash;
        ModeRulesVersion = modeRulesVersion;
        MasterSeed = masterSeed;
        BuildA = buildA;
        BuildB = buildB;
    }

    public ArtifactVersion EngineVersion { get; }

    public Sha256Digest ConfigHash { get; }

    public ArtifactVersion ModeRulesVersion { get; }

    public ulong MasterSeed { get; }

    public FighterBuildSnapshot BuildA { get; }

    public FighterBuildSnapshot BuildB { get; }
}
