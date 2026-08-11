using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Battle.Config.Semantic;
using Battle.Contracts.Config;
using Battle.Contracts.Versions;

namespace Battle.Config.Manifest;

public static class ConfigManifestJson
{
    private static readonly string[] RootMembers =
    {
        "config_hash",
        "config_version",
        "entity_counts",
        "exporter_version",
        "generated_utc",
        "schema_version",
        "source_workbook_sha256",
        "validation_summary",
    };

    public static byte[] Write(ConfigManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.Default,
                       Indented = false,
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString("config_hash", manifest.Reference.ConfigHash.Value);
            writer.WriteString("config_version", manifest.Reference.ConfigVersion.ToString());
            writer.WritePropertyName("entity_counts");
            writer.WriteStartObject();
            writer.WriteNumber("actions", manifest.EntityCounts.Actions);
            writer.WriteNumber("builds", manifest.EntityCounts.Builds);
            writer.WriteNumber("effects", manifest.EntityCounts.Effects);
            writer.WriteNumber("fighters", manifest.EntityCounts.Fighters);
            writer.WriteNumber("gear", manifest.EntityCounts.Gear);
            writer.WriteNumber("passives", manifest.EntityCounts.Passives);
            writer.WriteNumber("tactics", manifest.EntityCounts.Tactics);
            writer.WriteEndObject();
            writer.WriteString("exporter_version", manifest.ExporterVersion);
            writer.WriteString("generated_utc", manifest.GeneratedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString("schema_version", manifest.Reference.BalanceSchemaVersion.ToString());
            writer.WriteString("source_workbook_sha256", manifest.SourceWorkbookHash.Value);
            writer.WritePropertyName("validation_summary");
            writer.WriteStartObject();
            writer.WriteNumber("error_count", manifest.ErrorCount);
            writer.WriteNumber("warning_count", manifest.WarningCount);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    internal static ConfigManifest? Read(
        ReadOnlyMemory<byte> json,
        ICollection<ConfigValidationIssue> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !ValidateMembers(root, RootMembers, "$manifest", issues))
            {
                return null;
            }

            if (!TryRequiredString(root, "schema_version", issues, out var schemaVersion) ||
                !TryRequiredString(root, "config_version", issues, out var configVersion) ||
                !TryRequiredString(root, "config_hash", issues, out var hashText) ||
                !TryRequiredString(root, "source_workbook_sha256", issues, out var sourceHashText) ||
                !TryRequiredString(root, "exporter_version", issues, out var exporterVersion) ||
                !TryRequiredString(root, "generated_utc", issues, out var generatedText))
            {
                return null;
            }

            if (!Sha256Digest.TryParse(hashText, out var hash) ||
                !Sha256Digest.TryParse(sourceHashText, out var sourceHash))
            {
                Add(issues, ConfigValidationCodes.InvalidConfigManifest, "$manifest", "Manifest hashes must be canonical SHA-256 digests.");
                return null;
            }

            if (!DateTimeOffset.TryParseExact(
                    generatedText,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var generatedUtc))
            {
                Add(issues, ConfigValidationCodes.InvalidConfigManifest, "$manifest.generated_utc", "generated_utc must be an ISO-8601 round-trip timestamp.");
                return null;
            }

            if (!root.TryGetProperty("entity_counts", out var countsElement) ||
                countsElement.ValueKind != JsonValueKind.Object ||
                !ValidateMembers(
                    countsElement,
                    new[] { "actions", "builds", "effects", "fighters", "gear", "passives", "tactics" },
                    "$manifest.entity_counts",
                    issues) ||
                !TryCount(countsElement, "fighters", issues, out var fighters) ||
                !TryCount(countsElement, "actions", issues, out var actions) ||
                !TryCount(countsElement, "passives", issues, out var passives) ||
                !TryCount(countsElement, "effects", issues, out var effects) ||
                !TryCount(countsElement, "tactics", issues, out var tactics) ||
                !TryCount(countsElement, "gear", issues, out var gear) ||
                !TryCount(countsElement, "builds", issues, out var builds))
            {
                return null;
            }

            if (!root.TryGetProperty("validation_summary", out var validationElement) ||
                validationElement.ValueKind != JsonValueKind.Object ||
                !ValidateMembers(
                    validationElement,
                    new[] { "error_count", "warning_count" },
                    "$manifest.validation_summary",
                    issues) ||
                !TryCount(validationElement, "error_count", issues, out var errorCount) ||
                !TryCount(validationElement, "warning_count", issues, out var warningCount))
            {
                return null;
            }

            try
            {
                return ConfigManifest.FromJson(
                    new ConfigReference(
                        new ArtifactVersion(schemaVersion),
                        new ArtifactVersion(configVersion),
                        hash),
                    sourceHash,
                    exporterVersion,
                    generatedUtc,
                    new ConfigEntityCounts(fighters, actions, passives, effects, tactics, gear, builds),
                    errorCount,
                    warningCount);
            }
            catch (ArgumentException exception)
            {
                Add(issues, ConfigValidationCodes.InvalidConfigManifest, "$manifest", exception.Message);
                return null;
            }
        }
        catch (JsonException exception)
        {
            Add(issues, ConfigValidationCodes.InvalidConfigManifest, "$manifest", exception.Message);
            return null;
        }
    }

    private static bool ValidateMembers(
        JsonElement element,
        IEnumerable<string> requiredMembers,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        var allowed = new HashSet<string>(requiredMembers, StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        var valid = true;
        foreach (var property in element.EnumerateObject())
        {
            if (!actual.Add(property.Name))
            {
                Add(issues, ConfigValidationCodes.DuplicateJsonMember, path, $"Duplicate manifest member '{property.Name}'.");
                valid = false;
            }
            else if (!allowed.Contains(property.Name))
            {
                Add(issues, ConfigValidationCodes.UnknownJsonMember, path + "." + property.Name, "Unknown manifest member.");
                valid = false;
            }
        }

        foreach (var required in allowed)
        {
            if (!actual.Contains(required))
            {
                Add(issues, ConfigValidationCodes.MissingRequiredConfigKey, path, $"Required manifest member '{required}' is missing.");
                valid = false;
            }
        }

        return valid;
    }

    private static bool TryRequiredString(
        JsonElement parent,
        string name,
        ICollection<ConfigValidationIssue> issues,
        out string value)
    {
        if (parent.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString()!;
            return true;
        }

        Add(issues, ConfigValidationCodes.InvalidConfigManifest, "$manifest." + name, "A string value is required.");
        value = string.Empty;
        return false;
    }

    private static bool TryCount(
        JsonElement parent,
        string name,
        ICollection<ConfigValidationIssue> issues,
        out int value)
    {
        if (parent.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) &&
            value >= 0 &&
            element.GetRawText() == value.ToString(CultureInfo.InvariantCulture))
        {
            return true;
        }

        Add(issues, ConfigValidationCodes.InvalidConfigManifest, "$manifest." + name, "A non-negative integer is required.");
        value = default;
        return false;
    }

    private static void Add(
        ICollection<ConfigValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new ConfigValidationIssue(code, path, message));
}
