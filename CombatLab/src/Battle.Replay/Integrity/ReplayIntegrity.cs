using System.Buffers;
using System.Text.Json;
using Battle.Contracts.Versions;
using CanonicalJsonWriter = Battle.Replay.CanonicalJson.CanonicalJson;

namespace Battle.Replay.Integrity;

/// <summary>
/// Builds the normative replay integrity projections and computes their
/// <c>combat-canonical-json/1</c> SHA-256 digests.
/// </summary>
public static class ReplayIntegrity
{
    private static readonly string[] InputProjectionMemberNames =
    {
        "battle_id",
        "config",
        "engine",
        "input",
        "replay_id",
        "schema_version",
    };

    private static readonly string[] KeyframeStateProjectionMemberNames =
    {
        "active_grab_id",
        "after_sequence",
        "fighters",
        "tick",
    };

    /// <summary>
    /// Computes the replay input digest from the exact six-field v0.1 input projection.
    /// </summary>
    /// <param name="replayUtf8Json">A complete replay JSON object encoded as UTF-8.</param>
    /// <returns>The canonical input projection digest.</returns>
    /// <exception cref="ArgumentException">
    /// The value is not an object, a required projection member is missing, or the
    /// input violates the canonical JSON subset.
    /// </exception>
    /// <exception cref="JsonException">The input is not valid UTF-8 JSON.</exception>
    public static Sha256Digest ComputeInputDigest(ReadOnlyMemory<byte> replayUtf8Json)
    {
        using var document = ParseCanonical(replayUtf8Json);
        return ComputeInputDigest(document.RootElement);
    }

    /// <summary>
    /// Computes an event digest after replacing the existing
    /// <c>integrity.event_digest</c> member with an explicit JSON <c>null</c>.
    /// </summary>
    /// <param name="eventUtf8Json">A complete combat event JSON object encoded as UTF-8.</param>
    /// <returns>The canonical event projection digest.</returns>
    /// <exception cref="ArgumentException">
    /// The event or integrity value is not an object, <c>event_digest</c> is absent,
    /// or the input violates the canonical JSON subset.
    /// </exception>
    /// <exception cref="JsonException">The input is not valid UTF-8 JSON.</exception>
    public static Sha256Digest ComputeEventDigest(ReadOnlyMemory<byte> eventUtf8Json)
    {
        using var document = ParseCanonical(eventUtf8Json);
        return ComputeEventDigest(document.RootElement);
    }

    /// <summary>
    /// Computes the state digest for a v0.1 public playback keyframe.
    /// </summary>
    /// <remarks>
    /// The normative v0.1 machine fixture hashes <c>tick</c>,
    /// <c>after_sequence</c>, <c>fighters</c>, and <c>active_grab_id</c>. The
    /// schema-constant <c>scope</c> and the stored <c>state_digest</c> are excluded.
    /// </remarks>
    /// <param name="keyframeUtf8Json">A complete keyframe JSON object encoded as UTF-8.</param>
    /// <returns>The canonical four-field keyframe state digest.</returns>
    /// <exception cref="ArgumentException">
    /// The value is not an object, a required state member is missing, or the input
    /// violates the canonical JSON subset.
    /// </exception>
    /// <exception cref="JsonException">The input is not valid UTF-8 JSON.</exception>
    public static Sha256Digest ComputeKeyframeStateDigest(ReadOnlyMemory<byte> keyframeUtf8Json)
    {
        using var document = ParseCanonical(keyframeUtf8Json);
        return ComputeKeyframeStateDigest(document.RootElement);
    }

    internal static Sha256Digest ComputeInputDigest(JsonElement replay) =>
        CanonicalJsonWriter.HashCanonicalBytes(CreateInputProjection(replay));

    internal static Sha256Digest ComputeEventDigest(JsonElement combatEvent) =>
        CanonicalJsonWriter.HashCanonicalBytes(CreateEventProjection(combatEvent));

    internal static Sha256Digest ComputeKeyframeStateDigest(JsonElement keyframe) =>
        CanonicalJsonWriter.HashCanonicalBytes(CreateKeyframeStateProjection(keyframe));

    internal static byte[] CreateInputProjection(JsonElement replay)
    {
        var members = SelectRequiredMembers(
            replay,
            InputProjectionMemberNames,
            "$",
            "replay input projection");

        return WriteProjection(writer =>
        {
            writer.WriteStartObject();
            foreach (var name in InputProjectionMemberNames)
            {
                writer.WritePropertyName(name);
                CanonicalJsonWriter.WriteCanonical(writer, members[name], "$.'" + name + "'");
            }

            writer.WriteEndObject();
        });
    }

    internal static byte[] CreateEventProjection(JsonElement combatEvent)
    {
        var eventMembers = CanonicalJsonWriter.GetSortedObjectProperties(combatEvent, "$event");
        var integrity = FindRequiredMember(eventMembers, "integrity", "$event");
        var integrityMembers = CanonicalJsonWriter.GetSortedObjectProperties(
            integrity.Value,
            "$event.'integrity'");
        _ = FindRequiredMember(integrityMembers, "event_digest", "$event.'integrity'");

        return WriteProjection(writer =>
        {
            writer.WriteStartObject();
            foreach (var property in eventMembers)
            {
                writer.WritePropertyName(property.Name);
                if (StringComparer.Ordinal.Equals(property.Name, "integrity"))
                {
                    WriteEventIntegrityProjection(writer, integrityMembers);
                }
                else
                {
                    CanonicalJsonWriter.WriteCanonical(
                        writer,
                        property.Value,
                        "$event.'" + property.Name + "'");
                }
            }

            writer.WriteEndObject();
        });
    }

    internal static byte[] CreateKeyframeStateProjection(JsonElement keyframe)
    {
        var members = SelectRequiredMembers(
            keyframe,
            KeyframeStateProjectionMemberNames,
            "$keyframe",
            "keyframe state projection");

        return WriteProjection(writer =>
        {
            writer.WriteStartObject();
            foreach (var name in KeyframeStateProjectionMemberNames)
            {
                writer.WritePropertyName(name);
                CanonicalJsonWriter.WriteCanonical(
                    writer,
                    members[name],
                    "$keyframe.'" + name + "'");
            }

            writer.WriteEndObject();
        });
    }

    private static JsonDocument ParseCanonical(ReadOnlyMemory<byte> utf8Json)
    {
        var canonicalJson = CanonicalJsonWriter.Canonicalize(utf8Json);
        return JsonDocument.Parse(canonicalJson);
    }

    private static Dictionary<string, JsonElement> SelectRequiredMembers(
        JsonElement value,
        IReadOnlyList<string> requiredNames,
        string path,
        string projectionName)
    {
        var properties = CanonicalJsonWriter.GetSortedObjectProperties(value, path);
        var required = new HashSet<string>(requiredNames, StringComparer.Ordinal);
        var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in properties)
        {
            if (required.Contains(property.Name))
            {
                selected.Add(property.Name, property.Value);
            }
        }

        foreach (var requiredName in requiredNames)
        {
            if (!selected.ContainsKey(requiredName))
            {
                throw new ArgumentException(
                    $"Required member '{requiredName}' is missing from the {projectionName} at '{path}'.",
                    nameof(value));
            }
        }

        return selected;
    }

    private static JsonProperty FindRequiredMember(
        IEnumerable<JsonProperty> properties,
        string requiredName,
        string path)
    {
        foreach (var property in properties)
        {
            if (StringComparer.Ordinal.Equals(property.Name, requiredName))
            {
                return property;
            }
        }

        throw new ArgumentException(
            $"Required member '{requiredName}' is missing from object at '{path}'.",
            nameof(properties));
    }

    private static void WriteEventIntegrityProjection(
        Utf8JsonWriter writer,
        IEnumerable<JsonProperty> integrityMembers)
    {
        writer.WriteStartObject();
        foreach (var property in integrityMembers)
        {
            writer.WritePropertyName(property.Name);
            if (StringComparer.Ordinal.Equals(property.Name, "event_digest"))
            {
                writer.WriteNullValue();
            }
            else
            {
                CanonicalJsonWriter.WriteCanonical(
                    writer,
                    property.Value,
                    "$event.'integrity'.'" + property.Name + "'");
            }
        }

        writer.WriteEndObject();
    }

    private static byte[] WriteProjection(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CanonicalJsonWriter.GetWriterOptions()))
        {
            write(writer);
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
