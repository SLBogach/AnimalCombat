using System.Globalization;

namespace Battle.Contracts.Ids;

public readonly record struct EventId : IComparable<EventId>
{
    public const long MaximumSequence = 9_999_999_999;

    public EventId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                "An event ID must match ^evt-[0-9]{10}$.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static EventId FromSequence(long sequence)
    {
        if (sequence is < 0 or > MaximumSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return new EventId($"evt-{sequence.ToString("D10", CultureInfo.InvariantCulture)}");
    }

    public static bool TryParse(string? value, out EventId result)
    {
        if (!IsValid(value))
        {
            result = default;
            return false;
        }

        result = new EventId(value!);
        return true;
    }

    public int CompareTo(EventId other) =>
        StringComparer.Ordinal.Compare(Value, other.Value);

    public override string ToString() => Value;

    private static bool IsValid(string? value)
    {
        if (value is null || value.Length != 14 || !value.StartsWith("evt-", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 4; index < value.Length; index++)
        {
            if (!ContractText.IsAsciiDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }
}
