using Battle.Contracts.Config;
using Battle.Contracts.Versions;

namespace Battle.Config.Manifest;

public sealed class ConfigManifest
{
    private ConfigManifest(
        ConfigReference reference,
        Sha256Digest sourceWorkbookHash,
        string exporterVersion,
        DateTimeOffset generatedUtc,
        ConfigEntityCounts entityCounts,
        int errorCount,
        int warningCount)
    {
        Reference = reference;
        SourceWorkbookHash = sourceWorkbookHash;
        ExporterVersion = exporterVersion;
        GeneratedUtc = generatedUtc.ToUniversalTime();
        EntityCounts = entityCounts;
        ErrorCount = errorCount;
        WarningCount = warningCount;
    }

    public ConfigReference Reference { get; }

    public Sha256Digest SourceWorkbookHash { get; }

    public string ExporterVersion { get; }

    public DateTimeOffset GeneratedUtc { get; }

    public ConfigEntityCounts EntityCounts { get; }

    public int ErrorCount { get; }

    public int WarningCount { get; }

    public static ConfigManifest Create(
        ConfigReference reference,
        Sha256Digest sourceWorkbookHash,
        string exporterVersion,
        DateTimeOffset generatedUtc,
        ConfigEntityCounts counts,
        int warningCount)
    {
        if (string.IsNullOrWhiteSpace(exporterVersion) || exporterVersion.Length > 128)
        {
            throw new ArgumentException("An exporter version is required.", nameof(exporterVersion));
        }

        if (counts is null)
        {
            throw new ArgumentNullException(nameof(counts));
        }

        if (warningCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warningCount));
        }

        return new ConfigManifest(
            reference,
            sourceWorkbookHash,
            exporterVersion,
            generatedUtc,
            counts,
            errorCount: 0,
            warningCount);
    }

    internal static ConfigManifest FromJson(
        ConfigReference reference,
        Sha256Digest sourceWorkbookHash,
        string exporterVersion,
        DateTimeOffset generatedUtc,
        ConfigEntityCounts counts,
        int errorCount,
        int warningCount) =>
        new(
            reference,
            sourceWorkbookHash,
            exporterVersion,
            generatedUtc,
            counts,
            errorCount,
            warningCount);
}
