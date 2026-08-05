using System.Buffers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Battle.Contracts.Versions;

namespace Battle.Replay.CanonicalJson;

/// <summary>
/// Produces the <c>combat-canonical-json/1</c> representation used by replay
/// integrity hashes.
/// </summary>
public static class CanonicalJson
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.Default,
        Indented = false,
        SkipValidation = false,
    };

    /// <summary>
    /// Parses JSON and returns its deterministic UTF-8 canonical form.
    /// </summary>
    /// <param name="utf8Json">A complete UTF-8 JSON value without a byte-order mark.</param>
    /// <returns>Compact canonical UTF-8 bytes without a byte-order mark or trailing whitespace.</returns>
    /// <exception cref="ArgumentException">
    /// The input starts with a UTF-8 byte-order mark, contains duplicate or non-ASCII
    /// object member names, or contains a non-canonical JSON number.
    /// </exception>
    /// <exception cref="JsonException">The input is not valid UTF-8 JSON.</exception>
    public static byte[] Canonicalize(ReadOnlyMemory<byte> utf8Json)
    {
        if (HasUtf8ByteOrderMark(utf8Json.Span))
        {
            throw new ArgumentException(
                "Canonical JSON input must not start with a UTF-8 byte-order mark.",
                nameof(utf8Json));
        }

        using var document = JsonDocument.Parse(utf8Json, DocumentOptions);
        return Canonicalize(document.RootElement);
    }

    /// <summary>
    /// Canonicalizes a JSON value and computes its SHA-256 digest.
    /// </summary>
    /// <param name="utf8Json">A complete UTF-8 JSON value without a byte-order mark.</param>
    /// <returns>A lowercase <c>sha256:</c>-prefixed digest of the canonical bytes.</returns>
    /// <exception cref="ArgumentException">
    /// The input violates the canonical JSON subset.
    /// </exception>
    /// <exception cref="JsonException">The input is not valid UTF-8 JSON.</exception>
    public static Sha256Digest ComputeDigest(ReadOnlyMemory<byte> utf8Json) =>
        HashCanonicalBytes(Canonicalize(utf8Json));

    internal static byte[] Canonicalize(JsonElement element)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteCanonical(writer, element, "$");
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    internal static Sha256Digest HashCanonicalBytes(ReadOnlyMemory<byte> canonicalJson)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(canonicalJson.ToArray());
        var characters = new char[hash.Length * 2];
        const string digits = "0123456789abcdef";

        for (var index = 0; index < hash.Length; index++)
        {
            characters[index * 2] = digits[hash[index] >> 4];
            characters[(index * 2) + 1] = digits[hash[index] & 0x0f];
        }

        return new Sha256Digest("sha256:" + new string(characters));
    }

    internal static void WriteCanonical(
        Utf8JsonWriter writer,
        JsonElement element,
        string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in GetSortedObjectProperties(element, path))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, AppendPath(path, property.Name));
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item, $"{path}[{index}]");
                    index++;
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                var number = element.GetRawText();
                if (!IsMinimalInteger(number))
                {
                    throw new ArgumentException(
                        $"JSON number at '{path}' must be a minimally encoded integer; " +
                        "fractions, exponents, leading zeroes, and -0 are forbidden.",
                        nameof(element));
                }

                writer.WriteRawValue(number, skipInputValidation: false);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new ArgumentException(
                    $"Unsupported JSON value kind '{element.ValueKind}' at '{path}'.",
                    nameof(element));
        }
    }

    internal static List<JsonProperty> GetSortedObjectProperties(
        JsonElement element,
        string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"JSON value at '{path}' must be an object.",
                nameof(element));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var properties = new List<JsonProperty>();
        foreach (var property in element.EnumerateObject())
        {
            if (!IsAscii(property.Name))
            {
                throw new ArgumentException(
                    $"Object member name '{property.Name}' at '{path}' must contain ASCII characters only.",
                    nameof(element));
            }

            if (!names.Add(property.Name))
            {
                throw new ArgumentException(
                    $"Object member '{property.Name}' occurs more than once at '{path}'.",
                    nameof(element));
            }

            properties.Add(property);
        }

        properties.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Name, right.Name));
        return properties;
    }

    internal static JsonWriterOptions GetWriterOptions() => WriterOptions;

    private static bool HasUtf8ByteOrderMark(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 &&
        bytes[0] == 0xef &&
        bytes[1] == 0xbb &&
        bytes[2] == 0xbf;

    private static bool IsMinimalInteger(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var firstDigit = 0;
        if (value[0] == '-')
        {
            if (value.Length == 1 || value[1] == '0')
            {
                return false;
            }

            firstDigit = 1;
        }

        if (value[firstDigit] == '0')
        {
            return value.Length == 1;
        }

        if (value[firstDigit] is < '1' or > '9')
        {
            return false;
        }

        for (var index = firstDigit + 1; index < value.Length; index++)
        {
            if (value[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAscii(string value)
    {
        foreach (var character in value)
        {
            if (character > 0x7f)
            {
                return false;
            }
        }

        return true;
    }

    private static string AppendPath(string path, string propertyName) =>
        path == "$" ? "$.'" + propertyName + "'" : path + ".'" + propertyName + "'";
}
