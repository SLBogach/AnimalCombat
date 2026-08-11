using Battle.Contracts.Config;
using Battle.Contracts.Ids;

namespace Battle.Config.Json;

internal sealed class BalanceJsonDocument
{
    public BalanceJsonDocument(
        SortedDictionary<string, ConfigValue> settings,
        IReadOnlyDictionary<string, List<BalanceJsonEntity>> catalogs)
    {
        Settings = settings;
        Catalogs = catalogs;
    }

    public SortedDictionary<string, ConfigValue> Settings { get; }

    public IReadOnlyDictionary<string, List<BalanceJsonEntity>> Catalogs { get; }
}

internal sealed class BalanceJsonEntity
{
    public BalanceJsonEntity(StableId id, SortedDictionary<string, ConfigValue> properties)
    {
        Id = id;
        Properties = properties;
    }

    public StableId Id { get; }

    public SortedDictionary<string, ConfigValue> Properties { get; }
}
