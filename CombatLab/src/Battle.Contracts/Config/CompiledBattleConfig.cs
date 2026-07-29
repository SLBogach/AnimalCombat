namespace Battle.Contracts.Config;

public sealed class CompiledBattleConfig
{
    public CompiledBattleConfig(ConfigReference reference)
    {
        Reference = reference;
    }

    public ConfigReference Reference { get; }
}
