using Battle.Config.Canonical;
using Battle.Config.Json;
using Battle.Config.Schema;
using Battle.Config.Semantic;
using Battle.Contracts.Config;
using Battle.Contracts.Versions;

namespace Battle.Config.Compiler;

public sealed class BattleConfigCompiler
{
    public ConfigCompilationResult Compile(ReadOnlyMemory<byte> candidateJson)
    {
        var issues = new List<ConfigValidationIssue>();
        var document = StrictBalanceJsonReader.Read(candidateJson, issues);
        if (document is not null)
        {
            BalanceSemanticValidator.Validate(document, issues);
        }

        var orderedIssues = issues
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();
        if (document is null || orderedIssues.Any(item => item.Severity == ConfigValidationSeverity.Error))
        {
            return new ConfigCompilationResult(orderedIssues, null, null, null);
        }

        var canonicalJson = CanonicalBalanceWriter.Write(document);
        var configHash = ConfigHash.Compute(canonicalJson);
        var reference = new ConfigReference(
            new ArtifactVersion(BalanceV01Schema.SchemaVersion),
            new ArtifactVersion(document.Settings[BalanceV01Schema.ConfigVersionSetting].AsString()),
            configHash);
        var compiled = CompileSnapshot(reference, document);

        return new ConfigCompilationResult(orderedIssues, canonicalJson, configHash, compiled);
    }

    private static CompiledBattleConfig CompileSnapshot(
        ConfigReference reference,
        BalanceJsonDocument document) =>
        new(
            reference,
            document.Settings.Select(item => new ConfigProperty(item.Key, item.Value)),
            CompileCatalog(document.Catalogs["fighters"]),
            CompileCatalog(document.Catalogs["actions"]),
            CompileCatalog(document.Catalogs["passives"]),
            CompileCatalog(document.Catalogs["effects"]),
            CompileCatalog(document.Catalogs["tactics"]),
            CompileCatalog(document.Catalogs["gear"]));

    private static IReadOnlyList<CompiledConfigEntity> CompileCatalog(
        IEnumerable<BalanceJsonEntity> source) =>
        source
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select((item, handle) => new CompiledConfigEntity(
                item.Id,
                handle,
                item.Properties.Select(property => new ConfigProperty(property.Key, property.Value))))
            .ToArray();
}
