using System.Text;
using System.Text.Json;

namespace Battle.Replay.Json;

internal static class StrictReplayJsonReader
{
    // A technical safety cap. The schema still provides the authoritative item limits.
    private const int MaximumReplayBytes = 128 * 1024 * 1024;

    public static bool TryParse(
        ReadOnlyMemory<byte> utf8Json,
        out JsonDocument? document,
        out ReplayJsonSyntaxIssue? issue)
    {
        document = null;
        issue = null;

        if (utf8Json.Length == 0)
        {
            issue = new ReplayJsonSyntaxIssue("json.empty", "$", "Replay JSON is empty.");
            return false;
        }

        if (utf8Json.Length > MaximumReplayBytes)
        {
            issue = new ReplayJsonSyntaxIssue(
                "json.too_large",
                "$",
                $"Replay JSON exceeds the {MaximumReplayBytes} byte safety limit.");
            return false;
        }

        var span = utf8Json.Span;
        if (span.Length >= 3 && span[0] == 0xef && span[1] == 0xbb && span[2] == 0xbf)
        {
            issue = new ReplayJsonSyntaxIssue("json.bom", "$", "A UTF-8 BOM is not allowed.");
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(utf8Json.ToArray());
        }
        catch (DecoderFallbackException)
        {
            issue = new ReplayJsonSyntaxIssue("json.invalid_utf8", "$", "Replay is not valid UTF-8.");
            return false;
        }

        if (!ValidateTokens(span, out issue))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(
                utf8Json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                });
            return true;
        }
        catch (JsonException exception)
        {
            issue = new ReplayJsonSyntaxIssue("json.invalid", "$", exception.Message);
            return false;
        }
    }

    private static bool ValidateTokens(
        ReadOnlySpan<byte> utf8Json,
        out ReplayJsonSyntaxIssue? issue)
    {
        issue = null;
        var containers = new Stack<HashSet<string>?>();

        try
        {
            var reader = new Utf8JsonReader(
                utf8Json,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 128,
                });

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        containers.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;

                    case JsonTokenType.StartArray:
                        containers.Push(null);
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        containers.Pop();
                        break;

                    case JsonTokenType.PropertyName:
                    {
                        var names = containers.Peek();
                        var name = reader.GetString()!;
                        if (names is not null && !names.Add(name))
                        {
                            issue = new ReplayJsonSyntaxIssue(
                                "json.duplicate_member",
                                "$",
                                $"JSON member '{name}' occurs more than once in the same object.");
                            return false;
                        }

                        break;
                    }

                    case JsonTokenType.Number:
                        if (!IsCanonicalInteger(reader.ValueSpan))
                        {
                            issue = new ReplayJsonSyntaxIssue(
                                "json.non_integer_number",
                                "$",
                                "Canonical replay numbers must be minimally encoded integers; fractions, exponents and -0 are forbidden.");
                            return false;
                        }

                        break;
                }
            }

            return true;
        }
        catch (JsonException exception)
        {
            issue = new ReplayJsonSyntaxIssue("json.invalid", "$", exception.Message);
            return false;
        }
    }

    private static bool IsCanonicalInteger(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var index = 0;
        if (value[0] == (byte)'-')
        {
            if (value.Length == 1 || value[1] == (byte)'0')
            {
                return false;
            }

            index = 1;
        }

        if (value[index] == (byte)'0' && value.Length - index > 1)
        {
            return false;
        }

        for (; index < value.Length; index++)
        {
            if (value[index] is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class ReplayJsonSyntaxIssue
{
    public ReplayJsonSyntaxIssue(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }

    public string Code { get; }

    public string Path { get; }

    public string Message { get; }
}
