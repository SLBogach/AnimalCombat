namespace Battle.Contracts.Ids;

public readonly record struct DecisionId : IComparable<DecisionId>
{
    public DecisionId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "A decision ID must match ^dec-fighter_[ab]-[0-9]{6}$.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static bool TryParse(string? value, out DecisionId result)
    {
        if (!IsValid(value))
        {
            result = default;
            return false;
        }

        result = new DecisionId(value!);
        return true;
    }

    public int CompareTo(DecisionId other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;

    private static bool IsValid(string? value)
    {
        if (value is null || value.Length != 20)
        {
            return false;
        }

        var hasValidPrefix =
            value.StartsWith("dec-fighter_a-", StringComparison.Ordinal) ||
            value.StartsWith("dec-fighter_b-", StringComparison.Ordinal);
        if (!hasValidPrefix)
        {
            return false;
        }

        for (var index = 14; index < value.Length; index++)
        {
            if (!ContractText.IsAsciiDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }
}
