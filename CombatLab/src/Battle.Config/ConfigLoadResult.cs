using Battle.Config.Semantic;
using Battle.Contracts.Config;

namespace Battle.Config;

public sealed class ConfigLoadResult
{
    internal ConfigLoadResult(CompiledBattleConfig? config, IEnumerable<ConfigValidationIssue> issues)
    {
        Config = config;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public bool IsSuccess => Config is not null &&
                             !Issues.Any(item => item.Severity == ConfigValidationSeverity.Error);

    public CompiledBattleConfig? Config { get; }

    public IReadOnlyList<ConfigValidationIssue> Issues { get; }
}
