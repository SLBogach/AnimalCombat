using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Battle.Replay.Schema;

internal sealed class ReplaySchemaIssue
{
    public ReplaySchemaIssue(string path, string message)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public string Path { get; }

    public string Message { get; }
}

/// <summary>
/// Validates the closed JSON Schema subset used by combat-replay.schema.json.
/// Unsupported validation keywords are reported as schema errors instead of
/// being silently ignored.
/// </summary>
internal static class ReplaySchemaValidator
{
    private const int MaximumValidationDepth = 256;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        "array",
        "boolean",
        "integer",
        "null",
        "number",
        "object",
        "string",
    };

    private static readonly HashSet<string> KnownKeywords = new(StringComparer.Ordinal)
    {
        "$defs",
        "$id",
        "$ref",
        "$schema",
        "additionalProperties",
        "allOf",
        "const",
        "default",
        "description",
        "enum",
        "examples",
        "format",
        "if",
        "items",
        "maximum",
        "maxItems",
        "maxLength",
        "maxProperties",
        "minimum",
        "minItems",
        "minLength",
        "minProperties",
        "not",
        "oneOf",
        "pattern",
        "properties",
        "propertyNames",
        "required",
        "then",
        "title",
        "type",
        "uniqueItems",
    };

    public static IReadOnlyList<ReplaySchemaIssue> Validate(
        JsonElement instance,
        JsonElement schema)
    {
        var issues = new List<ReplaySchemaIssue>();
        InspectSchema(schema, schema, "$schema", issues, 0);
        if (issues.Count > 0)
        {
            return issues;
        }

        ValidateInstance(instance, schema, schema, "$", issues, 0);
        return issues;
    }

    private static void InspectSchema(
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (depth > MaximumValidationDepth)
        {
            Add(issues, path, "Schema nesting exceeds the supported depth.");
            return;
        }

        if (schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return;
        }

        if (schema.ValueKind != JsonValueKind.Object)
        {
            Add(issues, path, "A schema must be an object or a boolean.");
            return;
        }

        foreach (var keyword in schema.EnumerateObject())
        {
            var keywordPath = AppendPath(path, keyword.Name);
            if (!KnownKeywords.Contains(keyword.Name))
            {
                Add(issues, keywordPath, $"Unsupported schema keyword '{keyword.Name}'.");
                continue;
            }

            switch (keyword.Name)
            {
                case "$schema":
                case "$id":
                case "title":
                case "description":
                    RequireKind(keyword.Value, JsonValueKind.String, keywordPath, issues);
                    break;

                case "$ref":
                    InspectReference(keyword.Value, rootSchema, keywordPath, issues);
                    break;

                case "$defs":
                case "properties":
                    InspectSchemaMap(keyword.Value, rootSchema, keywordPath, issues, depth + 1);
                    break;

                case "additionalProperties":
                    InspectSchema(keyword.Value, rootSchema, keywordPath, issues, depth + 1);
                    break;

                case "propertyNames":
                case "items":
                case "not":
                case "if":
                case "then":
                    InspectSchema(keyword.Value, rootSchema, keywordPath, issues, depth + 1);
                    break;

                case "allOf":
                case "oneOf":
                    InspectSchemaArray(keyword.Value, rootSchema, keywordPath, issues, depth + 1);
                    break;

                case "type":
                    InspectType(keyword.Value, keywordPath, issues);
                    break;

                case "required":
                    InspectUniqueStringArray(keyword.Value, keywordPath, issues, requireNonEmpty: false);
                    break;

                case "enum":
                    InspectEnum(keyword.Value, keywordPath, issues);
                    break;

                case "pattern":
                    InspectPattern(keyword.Value, keywordPath, issues);
                    break;

                case "format":
                    InspectFormat(keyword.Value, keywordPath, issues);
                    break;

                case "minLength":
                case "maxLength":
                case "minItems":
                case "maxItems":
                case "minProperties":
                case "maxProperties":
                    InspectNonNegativeInteger(keyword.Value, keywordPath, issues);
                    break;

                case "minimum":
                case "maximum":
                    InspectNumber(keyword.Value, keywordPath, issues);
                    break;

                case "uniqueItems":
                    RequireKind(keyword.Value, JsonValueKind.True, JsonValueKind.False, keywordPath, issues);
                    break;

                case "const":
                case "default":
                case "examples":
                    break;
            }
        }

        InspectBounds(schema, "minLength", "maxLength", path, issues);
        InspectBounds(schema, "minItems", "maxItems", path, issues);
        InspectBounds(schema, "minProperties", "maxProperties", path, issues);
        InspectNumericBounds(schema, path, issues);
    }

    private static void InspectReference(
        JsonElement value,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            Add(issues, path, "$ref must be a string.");
            return;
        }

        var reference = value.GetString()!;
        if (!TryResolveReference(rootSchema, reference, out _))
        {
            Add(issues, path, $"Only resolvable local references are supported; got '{reference}'.");
        }
    }

    private static void InspectSchemaMap(
        JsonElement value,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            Add(issues, path, "The keyword value must be an object containing schemas.");
            return;
        }

        foreach (var property in value.EnumerateObject())
        {
            InspectSchema(property.Value, rootSchema, AppendPath(path, property.Name), issues, depth);
        }
    }

    private static void InspectSchemaArray(
        JsonElement value,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            Add(issues, path, "The keyword value must be a non-empty array of schemas.");
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            InspectSchema(item, rootSchema, AppendIndex(path, index), issues, depth);
            index++;
        }
    }

    private static void InspectType(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            InspectTypeName(value.GetString()!, path, issues);
            return;
        }

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            Add(issues, path, "type must be a type name or a non-empty array of type names.");
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = AppendIndex(path, index);
            if (item.ValueKind != JsonValueKind.String)
            {
                Add(issues, itemPath, "A type name must be a string.");
            }
            else
            {
                var name = item.GetString()!;
                InspectTypeName(name, itemPath, issues);
                if (!names.Add(name))
                {
                    Add(issues, itemPath, $"Duplicate type name '{name}'.");
                }
            }

            index++;
        }
    }

    private static void InspectTypeName(
        string name,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (!KnownTypes.Contains(name))
        {
            Add(issues, path, $"Unsupported JSON type '{name}'.");
        }
    }

    private static void InspectUniqueStringArray(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        bool requireNonEmpty)
    {
        if (value.ValueKind != JsonValueKind.Array || (requireNonEmpty && value.GetArrayLength() == 0))
        {
            Add(issues, path, "The keyword value must be an array of strings.");
            return;
        }

        var values = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = AppendIndex(path, index);
            if (item.ValueKind != JsonValueKind.String)
            {
                Add(issues, itemPath, "The array item must be a string.");
            }
            else if (!values.Add(item.GetString()!))
            {
                Add(issues, itemPath, $"Duplicate value '{item.GetString()}'.");
            }

            index++;
        }
    }

    private static void InspectEnum(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            Add(issues, path, "enum must be a non-empty array.");
            return;
        }

        var items = value.EnumerateArray().ToArray();
        for (var left = 0; left < items.Length; left++)
        {
            for (var right = left + 1; right < items.Length; right++)
            {
                if (JsonElement.DeepEquals(items[left], items[right]))
                {
                    Add(issues, AppendIndex(path, right), "enum values must be unique.");
                }
            }
        }
    }

    private static void InspectPattern(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            Add(issues, path, "pattern must be a string.");
            return;
        }

        try
        {
            _ = new Regex(value.GetString()!, RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            Add(issues, path, $"Invalid regular expression: {exception.Message}");
        }
    }

    private static void InspectFormat(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.String ||
            !string.Equals(value.GetString(), "date-time", StringComparison.Ordinal))
        {
            Add(issues, path, "Only the date-time format is supported.");
        }
    }

    private static void InspectNonNegativeInteger(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (!TryGetInteger(value, out var number) || number < 0)
        {
            Add(issues, path, "The keyword value must be a non-negative integer.");
        }
    }

    private static void InspectNumber(
        JsonElement value,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (!TryGetNumber(value, out _))
        {
            Add(issues, path, "The keyword value must be a finite JSON number.");
        }
    }

    private static void InspectBounds(
        JsonElement schema,
        string minimumName,
        string maximumName,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (schema.TryGetProperty(minimumName, out var minimum) &&
            schema.TryGetProperty(maximumName, out var maximum) &&
            TryGetInteger(minimum, out var minimumValue) &&
            TryGetInteger(maximum, out var maximumValue) &&
            minimumValue > maximumValue)
        {
            Add(issues, path, $"{minimumName} cannot exceed {maximumName}.");
        }
    }

    private static void InspectNumericBounds(
        JsonElement schema,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (schema.TryGetProperty("minimum", out var minimum) &&
            schema.TryGetProperty("maximum", out var maximum) &&
            TryGetNumber(minimum, out var minimumValue) &&
            TryGetNumber(maximum, out var maximumValue) &&
            minimumValue > maximumValue)
        {
            Add(issues, path, "minimum cannot exceed maximum.");
        }
    }

    private static void ValidateInstance(
        JsonElement instance,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (depth > MaximumValidationDepth)
        {
            Add(issues, path, "Instance nesting exceeds the supported validation depth.");
            return;
        }

        if (schema.ValueKind == JsonValueKind.True)
        {
            return;
        }

        if (schema.ValueKind == JsonValueKind.False)
        {
            Add(issues, path, "The value is rejected by a false schema.");
            return;
        }

        if (schema.TryGetProperty("$ref", out var reference) &&
            TryResolveReference(rootSchema, reference.GetString()!, out var referencedSchema))
        {
            ValidateInstance(instance, referencedSchema, rootSchema, path, issues, depth + 1);
        }

        if (schema.TryGetProperty("type", out var type) && !MatchesType(instance, type))
        {
            Add(issues, path, $"Expected {DescribeTypes(type)} but found {DescribeKind(instance.ValueKind)}.");
            return;
        }

        if (schema.TryGetProperty("const", out var constant) &&
            !JsonElement.DeepEquals(instance, constant))
        {
            Add(issues, path, "The value does not match const.");
        }

        if (schema.TryGetProperty("enum", out var enumeration) &&
            !enumeration.EnumerateArray().Any(candidate => JsonElement.DeepEquals(instance, candidate)))
        {
            Add(issues, path, "The value is not one of the allowed enum values.");
        }

        ValidateCombinators(instance, schema, rootSchema, path, issues, depth);
        ValidateString(instance, schema, path, issues);
        ValidateNumber(instance, schema, path, issues);
        ValidateArray(instance, schema, rootSchema, path, issues, depth);
        ValidateObject(instance, schema, rootSchema, path, issues, depth);
    }

    private static void ValidateCombinators(
        JsonElement instance,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var candidate in allOf.EnumerateArray())
            {
                ValidateInstance(instance, candidate, rootSchema, path, issues, depth + 1);
            }
        }

        if (schema.TryGetProperty("oneOf", out var oneOf))
        {
            var matches = 0;
            foreach (var candidate in oneOf.EnumerateArray())
            {
                var candidateIssues = new List<ReplaySchemaIssue>();
                ValidateInstance(instance, candidate, rootSchema, path, candidateIssues, depth + 1);
                if (candidateIssues.Count == 0)
                {
                    matches++;
                }
            }

            if (matches != 1)
            {
                Add(issues, path, $"The value must match exactly one oneOf branch; matched {matches}.");
            }
        }

        if (schema.TryGetProperty("not", out var notSchema))
        {
            var candidateIssues = new List<ReplaySchemaIssue>();
            ValidateInstance(instance, notSchema, rootSchema, path, candidateIssues, depth + 1);
            if (candidateIssues.Count == 0)
            {
                Add(issues, path, "The value matches a forbidden not schema.");
            }
        }

        if (schema.TryGetProperty("if", out var ifSchema))
        {
            var conditionIssues = new List<ReplaySchemaIssue>();
            ValidateInstance(instance, ifSchema, rootSchema, path, conditionIssues, depth + 1);
            if (conditionIssues.Count == 0 && schema.TryGetProperty("then", out var thenSchema))
            {
                ValidateInstance(instance, thenSchema, rootSchema, path, issues, depth + 1);
            }
        }
    }

    private static void ValidateString(
        JsonElement instance,
        JsonElement schema,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (instance.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var value = instance.GetString()!;
        var length = CountUnicodeScalars(value);
        if (schema.TryGetProperty("minLength", out var minimum) &&
            length < GetNonNegativeInteger(minimum))
        {
            Add(issues, path, $"String length must be at least {minimum.GetRawText()}.");
        }

        if (schema.TryGetProperty("maxLength", out var maximum) &&
            length > GetNonNegativeInteger(maximum))
        {
            Add(issues, path, $"String length must not exceed {maximum.GetRawText()}.");
        }

        if (schema.TryGetProperty("pattern", out var pattern))
        {
            try
            {
                if (!Regex.IsMatch(
                        value,
                        pattern.GetString()!,
                        RegexOptions.CultureInvariant,
                        RegexTimeout))
                {
                    Add(issues, path, $"String does not match pattern '{pattern.GetString()}'.");
                }
            }
            catch (RegexMatchTimeoutException)
            {
                Add(issues, path, "Pattern evaluation exceeded the supported timeout.");
            }
        }

        if (schema.TryGetProperty("format", out _) && !IsUtcRfc3339(value))
        {
            Add(issues, path, "String must be an RFC 3339 UTC date-time ending in Z.");
        }
    }

    private static void ValidateNumber(
        JsonElement instance,
        JsonElement schema,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (!TryGetNumber(instance, out var value))
        {
            return;
        }

        if (schema.TryGetProperty("minimum", out var minimum) &&
            TryGetNumber(minimum, out var minimumValue) &&
            value < minimumValue)
        {
            Add(issues, path, $"Number must be at least {minimum.GetRawText()}.");
        }

        if (schema.TryGetProperty("maximum", out var maximum) &&
            TryGetNumber(maximum, out var maximumValue) &&
            value > maximumValue)
        {
            Add(issues, path, $"Number must not exceed {maximum.GetRawText()}.");
        }
    }

    private static void ValidateArray(
        JsonElement instance,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (instance.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var items = instance.EnumerateArray().ToArray();
        if (schema.TryGetProperty("minItems", out var minimum) &&
            items.Length < GetNonNegativeInteger(minimum))
        {
            Add(issues, path, $"Array must contain at least {minimum.GetRawText()} items.");
        }

        if (schema.TryGetProperty("maxItems", out var maximum) &&
            items.Length > GetNonNegativeInteger(maximum))
        {
            Add(issues, path, $"Array must contain at most {maximum.GetRawText()} items.");
        }

        if (schema.TryGetProperty("uniqueItems", out var uniqueItems) && uniqueItems.GetBoolean())
        {
            for (var left = 0; left < items.Length; left++)
            {
                for (var right = left + 1; right < items.Length; right++)
                {
                    if (JsonElement.DeepEquals(items[left], items[right]))
                    {
                        Add(issues, AppendIndex(path, right), "Array items must be unique.");
                        left = items.Length;
                        break;
                    }
                }
            }
        }

        if (!schema.TryGetProperty("items", out var itemSchema))
        {
            return;
        }

        for (var index = 0; index < items.Length; index++)
        {
            ValidateInstance(
                items[index],
                itemSchema,
                rootSchema,
                AppendIndex(path, index),
                issues,
                depth + 1);
        }
    }

    private static void ValidateObject(
        JsonElement instance,
        JsonElement schema,
        JsonElement rootSchema,
        string path,
        ICollection<ReplaySchemaIssue> issues,
        int depth)
    {
        if (instance.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var properties = instance.EnumerateObject().ToArray();
        if (schema.TryGetProperty("minProperties", out var minimum) &&
            properties.Length < GetNonNegativeInteger(minimum))
        {
            Add(issues, path, $"Object must contain at least {minimum.GetRawText()} properties.");
        }

        if (schema.TryGetProperty("maxProperties", out var maximum) &&
            properties.Length > GetNonNegativeInteger(maximum))
        {
            Add(issues, path, $"Object must contain at most {maximum.GetRawText()} properties.");
        }

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var requiredName in required.EnumerateArray())
            {
                var name = requiredName.GetString()!;
                if (!instance.TryGetProperty(name, out _))
                {
                    Add(issues, AppendPath(path, name), $"Required property '{name}' is missing.");
                }
            }
        }

        schema.TryGetProperty("properties", out var propertySchemas);
        schema.TryGetProperty("additionalProperties", out var additionalProperties);
        var hasPropertySchemas = propertySchemas.ValueKind == JsonValueKind.Object;
        var hasAdditionalProperties = additionalProperties.ValueKind != JsonValueKind.Undefined;

        foreach (var property in properties)
        {
            var propertyPath = AppendPath(path, property.Name);
            if (schema.TryGetProperty("propertyNames", out var propertyNameSchema))
            {
                var propertyName = JsonSerializer.SerializeToElement(property.Name);
                ValidateInstance(
                    propertyName,
                    propertyNameSchema,
                    rootSchema,
                    propertyPath,
                    issues,
                    depth + 1);
            }

            if (hasPropertySchemas && propertySchemas.TryGetProperty(property.Name, out var propertySchema))
            {
                ValidateInstance(
                    property.Value,
                    propertySchema,
                    rootSchema,
                    propertyPath,
                    issues,
                    depth + 1);
                continue;
            }

            if (!hasAdditionalProperties || additionalProperties.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (additionalProperties.ValueKind == JsonValueKind.False)
            {
                Add(issues, propertyPath, $"Property '{property.Name}' is not allowed.");
                continue;
            }

            ValidateInstance(
                property.Value,
                additionalProperties,
                rootSchema,
                propertyPath,
                issues,
                depth + 1);
        }
    }

    private static bool MatchesType(JsonElement instance, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            return MatchesType(instance, type.GetString()!);
        }

        foreach (var candidate in type.EnumerateArray())
        {
            if (MatchesType(instance, candidate.GetString()!))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesType(JsonElement instance, string type) => type switch
    {
        "array" => instance.ValueKind == JsonValueKind.Array,
        "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => TryGetInteger(instance, out _),
        "null" => instance.ValueKind == JsonValueKind.Null,
        "number" => instance.ValueKind == JsonValueKind.Number,
        "object" => instance.ValueKind == JsonValueKind.Object,
        "string" => instance.ValueKind == JsonValueKind.String,
        _ => false,
    };

    private static string DescribeTypes(JsonElement type) =>
        type.ValueKind == JsonValueKind.String
            ? $"type '{type.GetString()}'"
            : "one of types " + string.Join(
                ", ",
                type.EnumerateArray().Select(item => $"'{item.GetString()}'"));

    private static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "an array",
        JsonValueKind.False or JsonValueKind.True => "a boolean",
        JsonValueKind.Null => "null",
        JsonValueKind.Number => "a number",
        JsonValueKind.Object => "an object",
        JsonValueKind.String => "a string",
        _ => "an undefined value",
    };

    private static bool TryResolveReference(
        JsonElement rootSchema,
        string reference,
        out JsonElement result)
    {
        result = rootSchema;
        if (string.Equals(reference, "#", StringComparison.Ordinal))
        {
            return true;
        }

        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        string pointer;
        try
        {
            pointer = Uri.UnescapeDataString(reference[1..]);
        }
        catch (UriFormatException)
        {
            return false;
        }

        foreach (var encodedSegment in pointer.Split('/').Skip(1))
        {
            var segment = encodedSegment.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (result.ValueKind == JsonValueKind.Object)
            {
                if (!result.TryGetProperty(segment, out result))
                {
                    return false;
                }
            }
            else if (result.ValueKind == JsonValueKind.Array &&
                     int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                     index >= 0 &&
                     index < result.GetArrayLength())
            {
                result = result[index];
            }
            else
            {
                return false;
            }
        }

        return result.ValueKind is JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False;
    }

    private static bool TryGetInteger(JsonElement value, out decimal result)
    {
        result = default;
        if (value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var text = value.GetRawText();
        if (text.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
        {
            return false;
        }

        return decimal.TryParse(
            text,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryGetNumber(JsonElement value, out decimal result)
    {
        result = default;
        return value.ValueKind == JsonValueKind.Number &&
               decimal.TryParse(
                   value.GetRawText(),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out result);
    }

    private static int GetNonNegativeInteger(JsonElement value)
    {
        _ = value.TryGetInt32(out var result);
        return result;
    }

    private static int CountUnicodeScalars(string value)
    {
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                index++;
            }

            count++;
        }

        return count;
    }

    private static bool IsUtcRfc3339(string value)
    {
        if (!Regex.IsMatch(
                value,
                @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?Z$",
                RegexOptions.CultureInvariant,
                RegexTimeout))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed) &&
            parsed.Offset == TimeSpan.Zero;
    }

    private static void RequireKind(
        JsonElement value,
        JsonValueKind expected,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind != expected)
        {
            Add(issues, path, $"Expected schema value kind {expected}.");
        }
    }

    private static void RequireKind(
        JsonElement value,
        JsonValueKind first,
        JsonValueKind second,
        string path,
        ICollection<ReplaySchemaIssue> issues)
    {
        if (value.ValueKind != first && value.ValueKind != second)
        {
            Add(issues, path, $"Expected schema value kind {first} or {second}.");
        }
    }

    private static string AppendPath(string path, string propertyName) =>
        path + "/" + propertyName.Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);

    private static string AppendIndex(string path, int index) =>
        path + "/" + index.ToString(CultureInfo.InvariantCulture);

    private static void Add(
        ICollection<ReplaySchemaIssue> issues,
        string path,
        string message) =>
        issues.Add(new ReplaySchemaIssue(path, message));
}
