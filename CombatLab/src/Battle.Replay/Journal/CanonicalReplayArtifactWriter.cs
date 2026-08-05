using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Replay.Integrity;
using Canonicalizer = Battle.Replay.CanonicalJson.CanonicalJson;

namespace Battle.Replay.Journal;

/// <summary>
/// Assembles a completed standard journal into a self-contained canonical replay.
/// </summary>
public static class CanonicalReplayArtifactWriter
{
    /// <summary>
    /// Writes a completed standard replay without consulting wall-clock time or
    /// any other ambient source of nondeterminism.
    /// </summary>
    public static byte[] Write(
        CanonicalReplayJournal journal,
        ReplayArtifactMetadata metadata)
    {
        if (journal is null)
        {
            throw new ArgumentNullException(nameof(journal));
        }

        if (metadata is null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (journal.Profile != JournalProfile.StandardReplay)
        {
            throw new InvalidOperationException(
                "Only a StandardReplay journal can be published by this writer.");
        }

        if (!journal.IsCompleted ||
            journal.Start is null ||
            journal.Summary is null ||
            !journal.InputDigest.HasValue ||
            !journal.FinalDigest.HasValue)
        {
            throw new InvalidOperationException(
                "The replay journal must complete successfully before publication.");
        }

        var events = journal.Events;
        if (events.Count < 2)
        {
            throw new InvalidOperationException(
                "A complete replay must contain at least BattleStarted and BattleEnded.");
        }

        var start = journal.Start;
        var summary = journal.Summary;
        var startKeyframe = WriteKeyframe(
            events[0].Draft.Tick,
            events[0].Draft.Sequence,
            new[] { start.FighterA.InitialFrame, start.FighterB.InitialFrame });
        var endKeyframe = WriteKeyframe(
            events[^1].Draft.Tick,
            events[^1].Draft.Sequence,
            summary.FinalFrames);

        using var inputDocument = JsonDocument.Parse(journal.InputProjection);
        using var summaryDocument = JsonDocument.Parse(
            EventDraftJsonWriter.WriteSummary(summary, includeEventCount: true));
        using var startKeyframeDocument = JsonDocument.Parse(startKeyframe);
        using var endKeyframeDocument = JsonDocument.Parse(endKeyframe);

        var eventDocuments = new List<JsonDocument>(events.Count);
        try
        {
            foreach (var item in events)
            {
                eventDocuments.Add(JsonDocument.Parse(item.CanonicalJson));
            }

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, Canonicalizer.GetWriterOptions()))
            {
                var input = inputDocument.RootElement;
                writer.WriteStartObject();
                WriteElement(writer, "schema_version", input.GetProperty("schema_version"));
                WriteElement(writer, "replay_id", input.GetProperty("replay_id"));
                WriteElement(writer, "battle_id", input.GetProperty("battle_id"));
                writer.WriteString("profile", "standard");
                WriteElement(writer, "engine", input.GetProperty("engine"));
                WriteElement(writer, "config", input.GetProperty("config"));
                WriteElement(writer, "input", input.GetProperty("input"));
                WriteElement(writer, "summary", summaryDocument.RootElement);

                writer.WriteStartArray("keyframes");
                Canonicalizer.WriteCanonical(writer, startKeyframeDocument.RootElement, "$.keyframes[0]");
                Canonicalizer.WriteCanonical(writer, endKeyframeDocument.RootElement, "$.keyframes[1]");
                writer.WriteEndArray();

                writer.WriteStartArray("events");
                for (var index = 0; index < eventDocuments.Count; index++)
                {
                    Canonicalizer.WriteCanonical(
                        writer,
                        eventDocuments[index].RootElement,
                        "$.events[" + index.ToString(CultureInfo.InvariantCulture) + "]");
                }

                writer.WriteEndArray();
                writer.WriteNull("diagnostics");

                writer.WriteStartObject("integrity");
                writer.WriteString("canonicalization", "combat-canonical-json/1");
                writer.WriteNumber("event_count", events.Count);
                writer.WriteString("final_digest", journal.FinalDigest.Value.Value);
                writer.WriteString("hash_algorithm", "sha256");
                writer.WriteString("input_digest", journal.InputDigest.Value.Value);
                writer.WriteNull("signature");
                writer.WriteEndObject();

                writer.WriteStartObject("metadata");
                writer.WriteString(
                    "created_at_utc",
                    metadata.CreatedAtUtc.ToString(
                        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                        CultureInfo.InvariantCulture));
                writer.WriteStartObject("extensions");
                writer.WriteEndObject();
                writer.WriteBoolean("fixture", metadata.Fixture);
                if (metadata.Notes is null)
                {
                    writer.WriteNull("notes");
                }
                else
                {
                    writer.WriteString("notes", metadata.Notes);
                }

                writer.WriteString("producer", metadata.Producer.Value);
                writer.WriteEndObject();

                writer.WriteStartObject("extensions");
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.Flush();
            }

            return Canonicalizer.Canonicalize(buffer.WrittenMemory);
        }
        finally
        {
            foreach (var document in eventDocuments)
            {
                document.Dispose();
            }
        }
    }

    private static byte[] WriteKeyframe(
        int tick,
        long afterSequence,
        IReadOnlyList<FighterFrame> fighters)
    {
        var stateBuffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(stateBuffer, Canonicalizer.GetWriterOptions()))
        {
            WriteKeyframeState(writer, tick, afterSequence, fighters);
            writer.Flush();
        }

        var stateDigest = ReplayIntegrity.ComputeKeyframeStateDigest(stateBuffer.WrittenMemory);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Canonicalizer.GetWriterOptions()))
        {
            writer.WriteStartObject();
            writer.WriteNull("active_grab_id");
            writer.WriteNumber("after_sequence", afterSequence);
            WriteFighters(writer, fighters);
            writer.WriteString("scope", "public_playback");
            writer.WriteString("state_digest", stateDigest.Value);
            writer.WriteNumber("tick", tick);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Canonicalizer.Canonicalize(buffer.WrittenMemory);
    }

    private static void WriteKeyframeState(
        Utf8JsonWriter writer,
        int tick,
        long afterSequence,
        IReadOnlyList<FighterFrame> fighters)
    {
        writer.WriteStartObject();
        writer.WriteNull("active_grab_id");
        writer.WriteNumber("after_sequence", afterSequence);
        WriteFighters(writer, fighters);
        writer.WriteNumber("tick", tick);
        writer.WriteEndObject();
    }

    private static void WriteFighters(
        Utf8JsonWriter writer,
        IReadOnlyList<FighterFrame> fighters)
    {
        writer.WriteStartArray("fighters");
        for (var index = 0; index < fighters.Count; index++)
        {
            using var frameDocument = JsonDocument.Parse(
                EventDraftJsonWriter.WriteFrame(fighters[index]));
            Canonicalizer.WriteCanonical(
                writer,
                frameDocument.RootElement,
                "$.fighters[" + index.ToString(CultureInfo.InvariantCulture) + "]");
        }

        writer.WriteEndArray();
    }

    private static void WriteElement(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement value)
    {
        writer.WritePropertyName(propertyName);
        Canonicalizer.WriteCanonical(writer, value, "$." + propertyName);
    }
}
