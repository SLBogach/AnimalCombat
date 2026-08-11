using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Battle.Contracts.Config;

namespace Battle.Config.Schema;

public static class BalanceSchemaJson
{
    public static byte[] Write()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.Default,
                       Indented = true,
                   }))
        {
            writer.WriteStartObject();
            writer.WriteString("$id", "https://combatlab.local/schemas/balance/v0.1/combat.balance.schema.json");
            writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            writer.WriteBoolean("additionalProperties", false);
            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (var rootMember in BalanceV01Schema.RootMembers)
            {
                writer.WritePropertyName(rootMember);
                if (rootMember == "settings")
                {
                    WriteObjectSchema(writer, BalanceV01Schema.Settings);
                }
                else
                {
                    WriteCatalogSchema(writer, BalanceV01Schema.Catalogs[rootMember]);
                }
            }

            writer.WriteEndObject();
            WriteStringArray(writer, "required", BalanceV01Schema.RootMembers);
            writer.WriteString("title", "Combat Lab balance configuration v0.1");
            writer.WriteString("type", "object");
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCatalogSchema(Utf8JsonWriter writer, CatalogSchema schema)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("items");
        WriteObjectSchema(writer, schema);
        writer.WriteNumber("maxItems", 4096);
        writer.WriteString("type", "array");
        writer.WriteEndObject();
    }

    private static void WriteObjectSchema(Utf8JsonWriter writer, CatalogSchema schema)
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
                writer.WriteNumber("maximum", 1_000_000_000);
                writer.WriteNumber("minimum", -1_000_000_000);
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
