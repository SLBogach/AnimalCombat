using System.Collections.Concurrent;
using Battle.Config.Compiler;
using Battle.Config.Manifest;
using Battle.Config.Semantic;
using Battle.Contracts.Config;

namespace Battle.Config;

public sealed class BattleConfigLoader
{
    private static readonly ConcurrentDictionary<string, CompiledBattleConfig> Cache =
        new(StringComparer.Ordinal);

    private readonly BattleConfigCompiler compiler = new();

    public ConfigLoadResult Load(
        ReadOnlyMemory<byte> canonicalJson,
        ReadOnlyMemory<byte> manifestJson)
    {
        var compilation = compiler.Compile(canonicalJson);
        var issues = compilation.Issues.ToList();
        if (!compilation.IsSuccess || compilation.Config is null || compilation.ConfigHash is null)
        {
            return Result(null, issues);
        }

        if (!canonicalJson.Span.SequenceEqual(compilation.GetCanonicalJson()))
        {
            issues.Add(new ConfigValidationIssue(
                ConfigValidationCodes.ConfigNotCanonical,
                "$",
                "Runtime config bytes must exactly match the canonical representation."));
        }

        var manifest = ConfigManifestJson.Read(manifestJson, issues);
        if (manifest is not null)
        {
            VerifyManifest(compilation.Config, manifest, issues);
        }

        if (manifest is null || issues.Any(item => item.Severity == ConfigValidationSeverity.Error))
        {
            return Result(null, issues);
        }

        var config = Cache.GetOrAdd(
            compilation.ConfigHash.Value.Value,
            _ => compilation.Config);
        return Result(config, issues);
    }

    private static void VerifyManifest(
        CompiledBattleConfig config,
        ConfigManifest manifest,
        ICollection<ConfigValidationIssue> issues)
    {
        if (manifest.Reference.ConfigHash != config.Reference.ConfigHash)
        {
            issues.Add(new ConfigValidationIssue(
                ConfigValidationCodes.ConfigHashMismatch,
                "$manifest.config_hash",
                "Manifest config_hash does not match canonical config bytes."));
        }

        if (manifest.Reference.BalanceSchemaVersion != config.Reference.BalanceSchemaVersion ||
            manifest.Reference.ConfigVersion != config.Reference.ConfigVersion)
        {
            issues.Add(new ConfigValidationIssue(
                ConfigValidationCodes.ManifestMismatch,
                "$manifest",
                "Manifest schema/config version does not match the compiled config."));
        }

        if (manifest.ErrorCount != 0)
        {
            issues.Add(new ConfigValidationIssue(
                ConfigValidationCodes.InvalidConfigManifest,
                "$manifest.validation_summary.error_count",
                "A runtime manifest must record zero validation errors."));
        }

        var counts = manifest.EntityCounts;
        if (counts.Fighters != config.Fighters.Count ||
            counts.Actions != config.Actions.Count ||
            counts.Passives != config.Passives.Count ||
            counts.Effects != config.Effects.Count ||
            counts.Tactics != config.Tactics.Count ||
            counts.Gear != config.Gear.Count)
        {
            issues.Add(new ConfigValidationIssue(
                ConfigValidationCodes.ManifestMismatch,
                "$manifest.entity_counts",
                "Manifest runtime entity counts do not match the compiled config."));
        }
    }

    private static ConfigLoadResult Result(
        CompiledBattleConfig? config,
        IEnumerable<ConfigValidationIssue> issues) =>
        new(
            config,
            issues.OrderBy(item => item.Path, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal));
}
