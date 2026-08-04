using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Config;

public sealed class CompiledBattleConfig
{
    private readonly IReadOnlyDictionary<string, ConfigValue> settingsByName;
    private readonly IReadOnlyDictionary<StableId, CompiledConfigEntity> fightersById;
    private readonly IReadOnlyDictionary<StableId, CompiledConfigEntity> actionsById;
    private readonly IReadOnlyDictionary<StableId, CompiledConfigEntity> passivesById;
    private readonly IReadOnlyDictionary<StableId, CompiledConfigEntity> effectsById;
    private readonly IReadOnlyDictionary<StableId, CompiledConfigEntity> tacticsById;
    private readonly IReadOnlyDictionary<StableId, CompiledConfigEntity> gearById;

    public CompiledBattleConfig(ConfigReference reference)
        : this(
            reference,
            Array.Empty<ConfigProperty>(),
            Array.Empty<CompiledConfigEntity>(),
            Array.Empty<CompiledConfigEntity>(),
            Array.Empty<CompiledConfigEntity>(),
            Array.Empty<CompiledConfigEntity>(),
            Array.Empty<CompiledConfigEntity>(),
            Array.Empty<CompiledConfigEntity>())
    {
    }

    public CompiledBattleConfig(
        ConfigReference reference,
        IEnumerable<ConfigProperty> settings,
        IEnumerable<CompiledConfigEntity> fighters,
        IEnumerable<CompiledConfigEntity> actions,
        IEnumerable<CompiledConfigEntity> passives,
        IEnumerable<CompiledConfigEntity> effects,
        IEnumerable<CompiledConfigEntity> tactics,
        IEnumerable<CompiledConfigEntity> gear)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (fighters is null)
        {
            throw new ArgumentNullException(nameof(fighters));
        }

        if (actions is null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        if (passives is null)
        {
            throw new ArgumentNullException(nameof(passives));
        }

        if (effects is null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        if (tactics is null)
        {
            throw new ArgumentNullException(nameof(tactics));
        }

        if (gear is null)
        {
            throw new ArgumentNullException(nameof(gear));
        }

        Reference = reference;
        Settings = CopySettings(settings, out settingsByName);
        Fighters = CopyCatalog(fighters, out fightersById);
        Actions = CopyCatalog(actions, out actionsById);
        Passives = CopyCatalog(passives, out passivesById);
        Effects = CopyCatalog(effects, out effectsById);
        Tactics = CopyCatalog(tactics, out tacticsById);
        Gear = CopyCatalog(gear, out gearById);
    }

    public ConfigReference Reference { get; }

    public IReadOnlyList<ConfigProperty> Settings { get; }

    public IReadOnlyList<CompiledConfigEntity> Fighters { get; }

    public IReadOnlyList<CompiledConfigEntity> Actions { get; }

    public IReadOnlyList<CompiledConfigEntity> Passives { get; }

    public IReadOnlyList<CompiledConfigEntity> Effects { get; }

    public IReadOnlyList<CompiledConfigEntity> Tactics { get; }

    public IReadOnlyList<CompiledConfigEntity> Gear { get; }

    public bool TryGetSetting(string name, out ConfigValue value) =>
        settingsByName.TryGetValue(name, out value);

    public bool TryGetFighter(StableId id, out CompiledConfigEntity? entity) =>
        TryGet(fightersById, id, out entity);

    public bool TryGetAction(StableId id, out CompiledConfigEntity? entity) =>
        TryGet(actionsById, id, out entity);

    public bool TryGetPassive(StableId id, out CompiledConfigEntity? entity) =>
        TryGet(passivesById, id, out entity);

    public bool TryGetEffect(StableId id, out CompiledConfigEntity? entity) =>
        TryGet(effectsById, id, out entity);

    public bool TryGetTactic(StableId id, out CompiledConfigEntity? entity) =>
        TryGet(tacticsById, id, out entity);

    public bool TryGetGear(StableId id, out CompiledConfigEntity? entity) =>
        TryGet(gearById, id, out entity);

    private static IReadOnlyList<ConfigProperty> CopySettings(
        IEnumerable<ConfigProperty> source,
        out IReadOnlyDictionary<string, ConfigValue> lookup)
    {
        var items = source.ToArray();
        var dictionary = new Dictionary<string, ConfigValue>(items.Length, StringComparer.Ordinal);
        string? previousName = null;

        foreach (var item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("A setting cannot be null.", nameof(source));
            }

            if (!dictionary.TryAdd(item.Name, item.Value))
            {
                throw new ArgumentException($"Duplicate setting '{item.Name}'.", nameof(source));
            }

            if (previousName is not null && StringComparer.Ordinal.Compare(previousName, item.Name) >= 0)
            {
                throw new ArgumentException("Settings must be in strict ordinal name order.", nameof(source));
            }

            previousName = item.Name;
        }

        lookup = new ReadOnlyDictionary<string, ConfigValue>(dictionary);
        return Array.AsReadOnly(items);
    }

    private static IReadOnlyList<CompiledConfigEntity> CopyCatalog(
        IEnumerable<CompiledConfigEntity> source,
        out IReadOnlyDictionary<StableId, CompiledConfigEntity> lookup)
    {
        var items = source.ToArray();
        var dictionary = new Dictionary<StableId, CompiledConfigEntity>(items.Length);

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            if (item is null)
            {
                throw new ArgumentException("A config entity cannot be null.", nameof(source));
            }

            if (!dictionary.TryAdd(item.Id, item))
            {
                throw new ArgumentException($"Duplicate config entity '{item.Id}'.", nameof(source));
            }

            if (item.DenseHandle != index)
            {
                throw new ArgumentException("Dense handles must be contiguous and match catalog order.", nameof(source));
            }

            if (index > 0 && items[index - 1].Id.CompareTo(item.Id) >= 0)
            {
                throw new ArgumentException("Config entities must be in strict ordinal Stable ID order.", nameof(source));
            }
        }

        lookup = new ReadOnlyDictionary<StableId, CompiledConfigEntity>(dictionary);
        return Array.AsReadOnly(items);
    }

    private static bool TryGet(
        IReadOnlyDictionary<StableId, CompiledConfigEntity> source,
        StableId id,
        out CompiledConfigEntity? entity)
    {
        if (source.TryGetValue(id, out var found))
        {
            entity = found;
            return true;
        }

        entity = null;
        return false;
    }
}
