using System.Text;
using System.Text.Json;
using Battle.Config.Schema;
using Battle.Config.Semantic;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;

namespace Battle.Config.Json;

internal static class StrictBalanceJsonReader
{
    private const int MaxDocumentBytes = 4 * 1024 * 1024;
    private const int MaxEntityCount = 4096;
    private const int MaxStringLength = 4096;

    public static BalanceJsonDocument? Read(
        ReadOnlyMemory<byte> utf8Json,
        ICollection<ConfigValidationIssue> issues)
    {
        if (utf8Json.Length == 0 || utf8Json.Length > MaxDocumentBytes)
        {
            Add(issues, ConfigValidationCodes.InvalidJson, "$", "The config JSON size is outside the supported range.");
            return null;
        }

        if (!HasValidUtf8(utf8Json, issues) || !HasUniqueMembers(utf8Json, issues))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(
                utf8Json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });

            return ReadRoot(document.RootElement, issues);
        }
        catch (JsonException exception)
        {
            Add(issues, ConfigValidationCodes.InvalidJson, "$", exception.Message);
            return null;
        }
    }

    private static BalanceJsonDocument? ReadRoot(
        JsonElement root,
        ICollection<ConfigValidationIssue> issues)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            Add(issues, ConfigValidationCodes.InvalidJson, "$", "The config root must be an object.");
            return null;
        }

        var rootNames = new HashSet<string>(StringComparer.Ordinal);
        var allowedRootNames = new HashSet<string>(BalanceV01Schema.RootMembers, StringComparer.Ordinal);
        var settings = new SortedDictionary<string, ConfigValue>(StringComparer.Ordinal);
        var catalogs = new Dictionary<string, List<BalanceJsonEntity>>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            rootNames.Add(property.Name);
            if (!allowedRootNames.Contains(property.Name))
            {
                Add(
                    issues,
                    ConfigValidationCodes.UnknownJsonMember,
                    "$." + property.Name,
                    $"Unknown root member '{property.Name}'.");
                continue;
            }

            if (property.Name == "settings")
            {
                ReadSettings(property.Value, settings, issues);
            }
            else
            {
                catalogs[property.Name] = ReadCatalog(
                    property.Name,
                    property.Value,
                    BalanceV01Schema.Catalogs[property.Name],
                    issues);
            }
        }

        foreach (var required in BalanceV01Schema.RootMembers)
        {
            if (!rootNames.Contains(required))
            {
                Add(
                    issues,
                    ConfigValidationCodes.MissingRequiredConfigKey,
                    "$",
                    $"Required root member '{required}' is missing.");
            }

            if (required != "settings" && !catalogs.ContainsKey(required))
            {
                catalogs[required] = new List<BalanceJsonEntity>();
            }
        }

        return new BalanceJsonDocument(settings, catalogs);
    }

    private static void ReadSettings(
        JsonElement element,
        SortedDictionary<string, ConfigValue> target,
        ICollection<ConfigValidationIssue> issues)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            Add(issues, ConfigValidationCodes.InvalidJson, "$.settings", "Settings must be an object.");
            return;
        }

        var actualNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var path = "$.settings." + property.Name;
            if (!BalanceV01Schema.Settings.Fields.TryGetValue(property.Name, out var schema))
            {
                Add(issues, ConfigValidationCodes.UnknownJsonMember, path, $"Unknown setting '{property.Name}'.");
                continue;
            }

            actualNames.Add(property.Name);
            if (TryReadValue(property.Value, schema, path, issues, out var value))
            {
                target.Add(property.Name, value);
            }
        }

        AddMissingFields(BalanceV01Schema.Settings, actualNames, "$.settings", issues);
    }

    private static List<BalanceJsonEntity> ReadCatalog(
        string catalogName,
        JsonElement element,
        CatalogSchema schema,
        ICollection<ConfigValidationIssue> issues)
    {
        var result = new List<BalanceJsonEntity>();
        var catalogPath = "$." + catalogName;
        if (element.ValueKind != JsonValueKind.Array)
        {
            Add(issues, ConfigValidationCodes.InvalidJson, catalogPath, "A config catalog must be an array.");
            return result;
        }

        if (element.GetArrayLength() > MaxEntityCount)
        {
            Add(issues, ConfigValidationCodes.NumericOutOfRange, catalogPath, "The catalog has too many entities.");
            return result;
        }

        var index = 0;
        var ids = new HashSet<StableId>();
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = catalogPath + "[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            index++;
            if (item.ValueKind != JsonValueKind.Object)
            {
                Add(issues, ConfigValidationCodes.InvalidJson, itemPath, "A config entity must be an object.");
                continue;
            }

            var properties = new SortedDictionary<string, ConfigValue>(StringComparer.Ordinal);
            var actualNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                var propertyPath = itemPath + "." + property.Name;
                if (!schema.Fields.TryGetValue(property.Name, out var field))
                {
                    Add(issues, ConfigValidationCodes.UnknownJsonMember, propertyPath, $"Unknown member '{property.Name}'.");
                    continue;
                }

                actualNames.Add(property.Name);
                if (TryReadValue(property.Value, field, propertyPath, issues, out var value))
                {
                    properties.Add(property.Name, value);
                }
            }

            AddMissingFields(schema, actualNames, itemPath, issues);
            if (schema.IdProperty is null ||
                !properties.TryGetValue(schema.IdProperty, out var idValue) ||
                idValue.Kind != ConfigValueKind.String)
            {
                continue;
            }

            if (!StableId.TryParse(idValue.AsString(), out var id))
            {
                Add(
                    issues,
                    ConfigValidationCodes.InvalidStableId,
                    itemPath + "." + schema.IdProperty,
                    $"'{idValue.AsString()}' is not a canonical Stable ID.");
                continue;
            }

            if (!ids.Add(id))
            {
                Add(
                    issues,
                    ConfigValidationCodes.DuplicateStableId,
                    itemPath + "." + schema.IdProperty,
                    $"Stable ID '{id}' occurs more than once in '{catalogName}'.");
                continue;
            }

            result.Add(new BalanceJsonEntity(id, properties));
        }

        return result;
    }

    private static bool TryReadValue(
        JsonElement element,
        FieldSchema schema,
        string path,
        ICollection<ConfigValidationIssue> issues,
        out ConfigValue value)
    {
        value = default;
        switch (schema.Kind)
        {
            case ConfigValueKind.Integer:
                if (element.ValueKind != JsonValueKind.Number ||
                    !IsMinimalInteger(element.GetRawText()) ||
                    !element.TryGetInt64(out var integer))
                {
                    Add(issues, ConfigValidationCodes.InvalidInteger, path, "An integer JSON number is required; floats and numeric strings are forbidden.");
                    return false;
                }

                value = ConfigValue.FromInteger(integer);
                return true;

            case ConfigValueKind.Boolean:
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    Add(issues, ConfigValidationCodes.InvalidBoolean, path, "A JSON boolean is required.");
                    return false;
                }

                value = ConfigValue.FromBoolean(element.GetBoolean());
                return true;

            case ConfigValueKind.String:
                if (element.ValueKind != JsonValueKind.String)
                {
                    Add(issues, ConfigValidationCodes.InvalidJson, path, "A JSON string is required.");
                    return false;
                }

                var text = element.GetString()!;
                if (text.Length > MaxStringLength)
                {
                    Add(issues, ConfigValidationCodes.NumericOutOfRange, path, "The string exceeds the supported length.");
                    return false;
                }

                if (schema.EnumValues.Count > 0 && !schema.EnumValues.Contains(text, StringComparer.Ordinal))
                {
                    Add(issues, ConfigValidationCodes.InvalidEnumValue, path, $"'{text}' is not a canonical enum value.");
                    return false;
                }

                value = ConfigValue.FromString(text);
                return true;

            default:
                Add(issues, ConfigValidationCodes.InvalidJson, path, "Unsupported config value type.");
                return false;
        }
    }

    private static void AddMissingFields(
        CatalogSchema schema,
        IEnumerable<string> actualNames,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        var actual = new HashSet<string>(actualNames, StringComparer.Ordinal);
        foreach (var field in schema.Fields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (field.Value.Required && !actual.Contains(field.Key))
            {
                Add(
                    issues,
                    ConfigValidationCodes.MissingRequiredConfigKey,
                    path,
                    $"Required member '{field.Key}' is missing.");
            }
        }
    }

    private static bool HasValidUtf8(
        ReadOnlyMemory<byte> input,
        ICollection<ConfigValidationIssue> issues)
    {
        var span = input.Span;
        if (span.Length >= 3 && span[0] == 0xef && span[1] == 0xbb && span[2] == 0xbf)
        {
            Add(issues, ConfigValidationCodes.InvalidUtf8, "$", "A UTF-8 BOM is not allowed.");
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, true).GetString(input.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            Add(issues, ConfigValidationCodes.InvalidUtf8, "$", "The input is not valid UTF-8.");
            return false;
        }
    }

    private static bool HasUniqueMembers(
        ReadOnlyMemory<byte> input,
        ICollection<ConfigValidationIssue> issues)
    {
        try
        {
            var reader = new Utf8JsonReader(
                input.Span,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            var containers = new Stack<HashSet<string>?>();

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
                        var names = containers.Peek();
                        var name = reader.GetString()!;
                        if (names is not null && !names.Add(name))
                        {
                            Add(
                                issues,
                                ConfigValidationCodes.DuplicateJsonMember,
                                "$",
                                $"JSON member '{name}' occurs more than once in the same object.");
                            return false;
                        }

                        break;
                }
            }

            return true;
        }
        catch (JsonException exception)
        {
            Add(issues, ConfigValidationCodes.InvalidJson, "$", exception.Message);
            return false;
        }
    }

    private static bool IsMinimalInteger(string text)
    {
        var index = 0;
        if (text.Length == 0)
        {
            return false;
        }

        if (text[0] == '-')
        {
            index = 1;
            if (index == text.Length)
            {
                return false;
            }
        }

        if (text[index] == '0' && index + 1 < text.Length)
        {
            return false;
        }

        for (; index < text.Length; index++)
        {
            if (text[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static void Add(
        ICollection<ConfigValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new ConfigValidationIssue(code, path, message));
}
