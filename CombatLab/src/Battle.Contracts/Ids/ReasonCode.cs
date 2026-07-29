namespace Battle.Contracts.Ids;

public readonly record struct ReasonCode : IComparable<ReasonCode>
{
    public ReasonCode(string value)
    {
        if (!ContractText.IsReasonCode(value))
        {
            throw new ArgumentException(
                "A reason code must match ^[A-Z][A-Za-z0-9]{0,63}$.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ReasonCode Parse(string value) => new(value);

    public static bool TryParse(string? value, out ReasonCode result)
    {
        if (!ContractText.IsReasonCode(value))
        {
            result = default;
            return false;
        }

        result = new ReasonCode(value!);
        return true;
    }

    public int CompareTo(ReasonCode other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;
}
