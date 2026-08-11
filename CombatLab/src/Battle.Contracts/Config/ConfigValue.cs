namespace Battle.Contracts.Config;

public readonly record struct ConfigValue
{
    private ConfigValue(ConfigValueKind kind, long integer, bool boolean, string? text)
    {
        Kind = kind;
        Integer = integer;
        Boolean = boolean;
        Text = text;
    }

    public ConfigValueKind Kind { get; }

    private long Integer { get; }

    private bool Boolean { get; }

    private string? Text { get; }

    public static ConfigValue FromInteger(long value) =>
        new(ConfigValueKind.Integer, value, default, default);

    public static ConfigValue FromBoolean(bool value) =>
        new(ConfigValueKind.Boolean, default, value, default);

    public static ConfigValue FromString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new ConfigValue(ConfigValueKind.String, default, default, value);
    }

    public long AsInteger() => Kind == ConfigValueKind.Integer
        ? Integer
        : throw WrongKind(ConfigValueKind.Integer);

    public bool AsBoolean() => Kind == ConfigValueKind.Boolean
        ? Boolean
        : throw WrongKind(ConfigValueKind.Boolean);

    public string AsString() => Kind == ConfigValueKind.String
        ? Text!
        : throw WrongKind(ConfigValueKind.String);

    public override string ToString() => Kind switch
    {
        ConfigValueKind.Integer => Integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ConfigValueKind.Boolean => Boolean ? "true" : "false",
        ConfigValueKind.String => Text!,
        _ => string.Empty,
    };

    private InvalidOperationException WrongKind(ConfigValueKind expected) =>
        new($"The config value is {Kind}, not {expected}.");
}
