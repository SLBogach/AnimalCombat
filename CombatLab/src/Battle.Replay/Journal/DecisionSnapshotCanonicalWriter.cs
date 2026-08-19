using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;
using Canonicalizer = Battle.Replay.CanonicalJson.CanonicalJson;

namespace Battle.Replay.Journal;

internal static class DecisionSnapshotCanonicalWriter
{
    private static readonly byte[] Domain =
        Encoding.ASCII.GetBytes("decision.batch-snapshot/0.1\0");

    internal static Sha256Digest ComputeDigest(DecisionBatchSnapshotProjection snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var canonical = Write(snapshot);
        var input = new byte[checked(Domain.Length + canonical.Length)];
        Domain.CopyTo(input, 0);
        canonical.CopyTo(input, Domain.Length);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(input);
        return new Sha256Digest("sha256:" + ToLowerHex(hash));
    }

    internal static byte[] Write(DecisionBatchSnapshotProjection snapshot)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Canonicalizer.GetWriterOptions()))
        {
            writer.WriteStartObject();
            writer.WriteString("battle_id", snapshot.BattleId.Value);
            writer.WriteString("config_hash", snapshot.ConfigHash.Value);
            writer.WriteString(
                "decision_next_index",
                snapshot.DecisionNextIndex.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("engine_version", snapshot.EngineVersion.ToString());
            writer.WriteStartArray("fighters");
            foreach (var fighter in snapshot.Fighters)
            {
                WriteFighter(writer, fighter);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("initiative_order");
            foreach (var fighterId in snapshot.InitiativeOrder)
            {
                writer.WriteStringValue(FighterIdText(fighterId));
            }

            writer.WriteEndArray();
            writer.WriteString(
                "master_seed",
                snapshot.MasterSeed.ToString(CultureInfo.InvariantCulture));
            WriteModeRules(writer, snapshot.ModeRules);
            writer.WriteNumber("tick", snapshot.Tick);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Canonicalizer.Canonicalize(buffer.WrittenMemory);
    }

    private static void WriteFighter(
        Utf8JsonWriter writer,
        DecisionFighterSnapshot fighter)
    {
        writer.WriteStartObject();
        WriteBuild(writer, fighter.Build);
        writer.WriteStartArray("cooldowns");
        foreach (var cooldown in fighter.Cooldowns)
        {
            writer.WriteStartObject();
            writer.WriteString("action_id", cooldown.ActionId.Value);
            writer.WriteNumber("ticks_remaining", cooldown.TicksRemaining);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteBoolean("emergency", fighter.Emergency);
        writer.WriteStartObject("history");
        WriteNullableStableId(writer, "last_action_id", fighter.LastActionId);
        if (fighter.LastActionCategory is null)
        {
            writer.WriteNull("last_category");
        }
        else
        {
            writer.WriteString("last_category", fighter.LastActionCategory);
        }

        writer.WriteNumber("same_action_streak", fighter.SameActionStreak);
        writer.WriteNumber("same_category_streak", fighter.SameCategoryStreak);
        writer.WriteEndObject();
        writer.WriteStartArray("opportunity_debts");
        foreach (var opportunity in fighter.OpportunityDebts)
        {
            writer.WriteStartObject();
            writer.WriteString("action_id", opportunity.ActionId.Value);
            writer.WriteNumber("debt", opportunity.Debt);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (fighter.ObservableActionId.HasValue)
        {
            writer.WriteStartObject("observable_telegraph");
            writer.WriteString("action_id", fighter.ObservableActionId.Value.Value);
            writer.WriteNumber("commit_tick", fighter.ObservableCommitTick!.Value);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("observable_telegraph");
        }

        writer.WritePropertyName("public_frame");
        using (var document = JsonDocument.Parse(
                   EventDraftJsonWriter.WriteFrame(fighter.PublicFrame)))
        {
            Canonicalizer.WriteCanonical(writer, document.RootElement, "$.fighters.public_frame");
        }

        writer.WriteEndObject();
    }

    private static void WriteBuild(Utf8JsonWriter writer, FighterBuildSnapshot build)
    {
        writer.WriteStartObject("build");
        writer.WriteString("animal_id", build.AnimalId.Value);
        if (build.BuildId.HasValue)
        {
            writer.WriteString("build_id", build.BuildId.Value.Value);
        }
        else
        {
            writer.WriteNull("build_id");
        }

        writer.WriteString("fighter_id", FighterIdText(build.FighterId));
        writer.WriteStartObject("gear");
        writer.WriteString("defense", build.Gear.Defense.Value);
        writer.WriteString("offense", build.Gear.Offense.Value);
        writer.WriteString("utility", build.Gear.Utility.Value);
        writer.WriteEndObject();
        writer.WriteString("passive_id", build.PassiveId.Value);
        writer.WriteString("side", build.Side.ToString());
        writer.WriteStartArray("special_action_ids");
        foreach (var actionId in build.SpecialActionIds)
        {
            writer.WriteStringValue(actionId.Value);
        }

        writer.WriteEndArray();
        writer.WriteString("tactic_id", build.TacticId.Value);
        writer.WriteEndObject();
    }

    private static void WriteModeRules(Utf8JsonWriter writer, ModeRulesSnapshot mode)
    {
        writer.WriteStartObject("mode_rules");
        WriteIds(writer, "allowed_action_ids", mode.AllowedActionIds);
        WriteIds(writer, "allowed_animal_ids", mode.AllowedAnimalIds);
        WriteIds(writer, "allowed_gear_ids", mode.AllowedGearIds);
        WriteIds(writer, "allowed_passive_ids", mode.AllowedPassiveIds);
        WriteIds(writer, "allowed_tactic_ids", mode.AllowedTacticIds);
        writer.WriteString("id", mode.Id.Value);
        writer.WriteString("normalization_mode", mode.NormalizationMode.ToString());
        writer.WriteString("version", mode.Version.ToString());
        writer.WriteEndObject();
    }

    private static void WriteIds(
        Utf8JsonWriter writer,
        string name,
        IEnumerable<StableId> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value.Value);
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableStableId(
        Utf8JsonWriter writer,
        string name,
        StableId? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(name, value.Value.Value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static string FighterIdText(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => "fighter_a",
        FighterId.FighterB => "fighter_b",
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    private static string ToLowerHex(IEnumerable<byte> bytes)
    {
        var builder = new StringBuilder(64);
        foreach (var value in bytes)
        {
            _ = builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
