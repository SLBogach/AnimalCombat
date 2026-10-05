using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Battle.Contracts.Config;

namespace Battle.Config.Schema;

public static class BalanceSchemaJson
{
    public static byte[] Write() => Write(BalanceV01Schema.SchemaVersion);

    public static byte[] Write(string schemaVersion)
    {
        var schema = BalanceSchemaDefinition.Find(schemaVersion)
            ?? throw new ArgumentException("Unsupported balance schema version.", nameof(schemaVersion));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.Default,
                       Indented = true,
                       // Persisted schema bytes must not depend on Environment.NewLine.
                       NewLine = "\n",
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString("$id", "https://combatlab.local/schemas/balance/" + schema.ConfigVersion + "/combat.balance.schema.json");
            writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            writer.WriteBoolean("additionalProperties", false);
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var rootMember in schema.RootMembers)
            {
                writer.WritePropertyName(rootMember);
                if (rootMember == "settings")
                {
                    WriteObjectSchema(writer, schema.Settings, schema.Version == "combat.balance/0.2");
                }
                else
                {
                    WriteCatalogSchema(writer, schema.Catalogs[rootMember], schema.Version == "combat.balance/0.2");
                }
            }

            writer.WriteEndObject();
            WriteStringArray(writer, "required", schema.RootMembers);
            writer.WriteString("title", "Combat Lab balance configuration " + schema.ConfigVersion);
            writer.WriteString("type", "object");
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCatalogSchema(Utf8JsonWriter writer, CatalogSchema schema, bool isV02)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        WriteObjectSchema(writer, schema, isV02);
        writer.WriteNumber("maxItems", 4096);
        writer.WriteString("type", "array");
        writer.WriteEndObject();
    }

    private static void WriteObjectSchema(Utf8JsonWriter writer, CatalogSchema schema, bool isV02)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("additionalProperties", false);
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        foreach (var field in schema.Fields.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(field.Key);
            writer.WriteStartObject();
            if (field.Value.EnumValues.Count > 0)
            {
                WriteStringArray(writer, "enum", field.Value.EnumValues.OrderBy(item => item, StringComparer.Ordinal));
            }

            if (field.Value.Kind == ConfigValueKind.Integer)
            {
                var metadataInt32 = isV02 && field.Key is "priority" or "internal_cooldown_ticks" or
                    "max_activations_per_tick" or "max_activations_per_battle";
                writer.WriteNumber("maximum", metadataInt32 ? int.MaxValue : 1_000_000_000);
                writer.WriteNumber("minimum", metadataInt32 ? int.MinValue : -1_000_000_000);
            }
            else if (field.Value.Kind == ConfigValueKind.String)
            {
                writer.WriteNumber("maxLength", 4096);
            }

            writer.WriteString("type", TypeName(field.Value.Kind));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        WriteStringArray(
            writer,
            "required",
            schema.Fields.Where(item => item.Value.Required)
                .Select(item => item.Key)
                .OrderBy(item => item, StringComparer.Ordinal));
        writer.WriteString("type", "object");
        writer.WriteEndObject();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<string> values)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string TypeName(ConfigValueKind kind) => kind switch
    {
        ConfigValueKind.Integer => "integer",
        ConfigValueKind.Boolean => "boolean",
        ConfigValueKind.String => "string",
        _ => throw new InvalidOperationException($"Unsupported schema value kind '{kind}'."),
    };
}
