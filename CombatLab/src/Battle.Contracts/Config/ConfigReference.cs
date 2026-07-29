using Battle.Contracts.Versions;

namespace Battle.Contracts.Config;

public readonly record struct ConfigReference(
    ArtifactVersion BalanceSchemaVersion,
    ArtifactVersion ConfigVersion,
    Sha256Digest ConfigHash);
