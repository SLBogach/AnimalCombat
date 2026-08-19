using Battle.Contracts.Replay;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Ports;

/// <summary>
/// Optional diagnostic replay extension. Gameplay never depends on this port
/// for selection or state mutation; implementations own canonical projection
/// and hashing of the supplied immutable snapshot.
/// </summary>
public interface ICombatDecisionDiagnostics
{
    bool IsEnabled { get; }

    Sha256Digest ComputeSnapshotDigest(DecisionBatchSnapshotProjection snapshot);

    void AppendDecisionTrace(DecisionTrace trace);
}
