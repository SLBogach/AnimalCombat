using Battle.Contracts.Versions;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Requests;

public sealed class BattleRequest
{
    public BattleRequest(
        ExternalId battleId,
        ArtifactVersion engineVersion,
        Sha256Digest configHash,
        ModeRulesSnapshot modeRules,
        ulong masterSeed,
        FighterBuildSnapshot buildA,
        FighterBuildSnapshot buildB)
    {
        if (string.IsNullOrEmpty(battleId.Value))
        {
            throw new ArgumentException("A battle ID is required.", nameof(battleId));
        }

        if (modeRules is null)
        {
            throw new ArgumentNullException(nameof(modeRules));
        }

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

        BattleId = battleId;
        EngineVersion = engineVersion;
        ConfigHash = configHash;
        ModeRules = modeRules;
        MasterSeed = masterSeed;
        BuildA = buildA;
        BuildB = buildB;
    }

    public ExternalId BattleId { get; }

    public ArtifactVersion EngineVersion { get; }

    public Sha256Digest ConfigHash { get; }

    public ModeRulesSnapshot ModeRules { get; }

    public ulong MasterSeed { get; }

    public FighterBuildSnapshot BuildA { get; }

    public FighterBuildSnapshot BuildB { get; }
}
