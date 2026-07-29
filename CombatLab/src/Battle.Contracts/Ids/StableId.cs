namespace Battle.Contracts.Ids;

public readonly record struct StableId : IComparable<StableId>
{
    public StableId(string value)
    {
        if (!ContractText.IsStableId(value))
        {
            throw new ArgumentException(
                "A stable ID must match ^[a-z][a-z0-9_]{0,63}$.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static StableId Parse(string value) => new(value);

    public static bool TryParse(string? value, out StableId result)
    {
        if (!ContractText.IsStableId(value))
        {
            result = default;
            return false;
        }

        result = new StableId(value!);
        return true;
    }

    public int CompareTo(StableId other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
