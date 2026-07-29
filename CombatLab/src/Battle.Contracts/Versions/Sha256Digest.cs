namespace Battle.Contracts.Versions;

public readonly record struct Sha256Digest : IComparable<Sha256Digest>
{
    private const string Prefix = "sha256:";

    public Sha256Digest(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "A SHA-256 digest must use the sha256: prefix and 64 lowercase hexadecimal digits.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static bool TryParse(string? value, out Sha256Digest result)
    {
        if (!IsValid(value))
        {
            result = default;
            return false;
        }

        result = new Sha256Digest(value!);
        return true;
    }

    public int CompareTo(Sha256Digest other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;

    private static bool IsValid(string? value)
    {
        if (value is null ||
            value.Length != Prefix.Length + 64 ||
            !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = Prefix.Length; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character is >= '0' and <= '9') || (character is >= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
