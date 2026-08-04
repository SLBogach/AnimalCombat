using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Battle.Config.Json;
using Battle.Contracts.Config;

namespace Battle.Config.Canonical;

internal static class CanonicalBalanceWriter
{
    public static byte[] Write(BalanceJsonDocument document)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.Default,
                       Indented = false,
                       SkipValidation = false,
                   }))
        {
            writer.WriteStartObject();

            foreach (var catalog in document.Catalogs.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (StringComparer.Ordinal.Compare(catalog.Key, "settings") >= 0)
                {
                    continue;
                }

                WriteCatalog(writer, catalog.Key, catalog.Value);
            }

            writer.WritePropertyName("settings");
            writer.WriteStartObject();
            foreach (var setting in document.Settings)
            {
                WriteValue(writer, setting.Key, setting.Value);
            }

            writer.WriteEndObject();

            foreach (var catalog in document.Catalogs.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (StringComparer.Ordinal.Compare(catalog.Key, "settings") < 0)
                {
                    continue;
                }

                WriteCatalog(writer, catalog.Key, catalog.Value);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCatalog(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<BalanceJsonEntity> entities)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var entity in entities.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            foreach (var property in entity.Properties)
            {
                WriteValue(writer, property.Key, property.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, ConfigValue value)
    {
        switch (value.Kind)
        {
            case ConfigValueKind.Integer:
                writer.WriteNumber(name, value.AsInteger());
                break;
            case ConfigValueKind.Boolean:
                writer.WriteBoolean(name, value.AsBoolean());
                break;
            case ConfigValueKind.String:
                writer.WriteString(name, value.AsString());
                break;
            default:
                throw new InvalidOperationException($"Unsupported config value kind '{value.Kind}'.");
        }
    }
}
