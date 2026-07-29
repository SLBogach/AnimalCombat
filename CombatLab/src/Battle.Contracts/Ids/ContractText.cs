namespace Battle.Contracts.Ids;

internal static class ContractText
{
    public static bool IsStableId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiLower(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsAsciiLower(character) && !IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsExternalId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsAsciiAlphaNumeric(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!IsAsciiAlphaNumeric(character) &&
                character != '.' &&
                character != '_' &&
                character != ':' &&
                character != '/' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsReasonCode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsAsciiUpper(value[0]))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!IsAsciiAlphaNumeric(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private static bool IsAsciiLower(char value) => value is >= 'a' and <= 'z';

    private static bool IsAsciiUpper(char value) => value is >= 'A' and <= 'Z';

    private static bool IsAsciiAlphaNumeric(char value) =>
        IsAsciiLower(value) || IsAsciiUpper(value) || IsAsciiDigit(value);
}
