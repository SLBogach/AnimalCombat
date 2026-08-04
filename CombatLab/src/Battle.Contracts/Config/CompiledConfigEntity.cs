using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Config;

public sealed class CompiledConfigEntity
{
    private readonly IReadOnlyDictionary<string, ConfigValue> propertiesByName;

    public CompiledConfigEntity(
        StableId id,
        int denseHandle,
        IEnumerable<ConfigProperty> properties)
    {
        if (!StableId.TryParse(id.Value, out _))
        {
            throw new ArgumentException("A valid Stable ID is required.", nameof(id));
        }

        if (denseHandle < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denseHandle));
        }

        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

        var items = properties.ToArray();
        var dictionary = new Dictionary<string, ConfigValue>(items.Length, StringComparer.Ordinal);
        string? previousName = null;

        foreach (var property in items)
        {
            if (property is null)
            {
                throw new ArgumentException("A config property cannot be null.", nameof(properties));
            }

            if (!dictionary.TryAdd(property.Name, property.Value))
            {
                throw new ArgumentException($"Duplicate property '{property.Name}'.", nameof(properties));
            }

            if (previousName is not null && StringComparer.Ordinal.Compare(previousName, property.Name) >= 0)
            {
                throw new ArgumentException("Entity properties must be in strict ordinal name order.", nameof(properties));
            }

            previousName = property.Name;
        }

        Id = id;
        DenseHandle = denseHandle;
        Properties = Array.AsReadOnly(items);
        propertiesByName = new ReadOnlyDictionary<string, ConfigValue>(dictionary);
    }

    public StableId Id { get; }

    public int DenseHandle { get; }

    public IReadOnlyList<ConfigProperty> Properties { get; }

    public bool TryGetProperty(string name, out ConfigValue value) =>
        propertiesByName.TryGetValue(name, out value);
}
