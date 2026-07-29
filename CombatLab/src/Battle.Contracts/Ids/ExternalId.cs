namespace Battle.Contracts.Ids;

public readonly record struct ExternalId : IComparable<ExternalId>
{
    public ExternalId(string value)
    {
        if (!ContractText.IsExternalId(value))
        {
            throw new ArgumentException(
                "An external ID must match ^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ExternalId Parse(string value) => new(value);

    public static bool TryParse(string? value, out ExternalId result)
    {
        if (!ContractText.IsExternalId(value))
        {
            result = default;
            return false;
        }

        result = new ExternalId(value!);
        return true;
    }

    public int CompareTo(ExternalId other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
