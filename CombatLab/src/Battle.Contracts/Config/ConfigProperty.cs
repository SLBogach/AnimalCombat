namespace Battle.Contracts.Config;

public sealed record ConfigProperty
{
    public ConfigProperty(string name, ConfigValue value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A config property name is required.", nameof(name));
        }

        if (value.Kind is not (ConfigValueKind.Integer or ConfigValueKind.Boolean or ConfigValueKind.String))
        {
            throw new ArgumentException("A config property requires a supported value kind.", nameof(value));
        }

        Name = name;
        Value = value;
    }

    public string Name { get; }

    public ConfigValue Value { get; }
}
