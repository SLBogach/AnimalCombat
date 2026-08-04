using Battle.Config.Semantic;
using Battle.Contracts.Config;
using Battle.Contracts.Versions;

namespace Battle.Config.Compiler;

public sealed class ConfigCompilationResult
{
    internal ConfigCompilationResult(
        IEnumerable<ConfigValidationIssue> issues,
        byte[]? canonicalJson,
        Sha256Digest? configHash,
        CompiledBattleConfig? config)
    {
        Issues = Array.AsReadOnly(issues.ToArray());
        canonicalJsonBytes = canonicalJson?.ToArray();
        ConfigHash = configHash;
        Config = config;
    }

    private readonly byte[]? canonicalJsonBytes;

    public bool IsSuccess => Config is not null &&
                             !Issues.Any(item => item.Severity == ConfigValidationSeverity.Error);

    public IReadOnlyList<ConfigValidationIssue> Issues { get; }

    public Sha256Digest? ConfigHash { get; }

    public CompiledBattleConfig? Config { get; }

    public byte[] GetCanonicalJson() => canonicalJsonBytes?.ToArray() ?? Array.Empty<byte>();
}
