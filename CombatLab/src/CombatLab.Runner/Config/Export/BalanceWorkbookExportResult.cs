using System.Collections.ObjectModel;
using Battle.Contracts.Versions;

namespace CombatLab.Runner.Config.Export;

public sealed class BalanceWorkbookExportResult
{
    private readonly byte[] candidateJson;

    internal BalanceWorkbookExportResult(
        byte[] candidateJson,
        string mapCsv,
        Sha256Digest? sourceWorkbookHash,
        IReadOnlyDictionary<string, int> entityCounts,
        IReadOnlyList<BalanceExportIssue> issues)
    {
        this.candidateJson = candidateJson.ToArray();
        MapCsv = mapCsv;
        SourceWorkbookHash = sourceWorkbookHash;
        EntityCounts = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(entityCounts, StringComparer.Ordinal));
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public bool IsSuccess =>
        SourceWorkbookHash is not null &&
        Issues.All(issue => issue.Severity != BalanceExportIssueSeverity.Error);

    public byte[] CandidateJson => candidateJson.ToArray();

    public string MapCsv { get; }

    public Sha256Digest? SourceWorkbookHash { get; }

    public IReadOnlyDictionary<string, int> EntityCounts { get; }

    public IReadOnlyList<BalanceExportIssue> Issues { get; }
}
