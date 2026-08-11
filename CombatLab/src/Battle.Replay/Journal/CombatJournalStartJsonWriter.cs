using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using CanonicalJsonWriter = Battle.Replay.CanonicalJson.CanonicalJson;

namespace Battle.Replay.Journal;

internal static class CombatJournalStartJsonWriter
{
    public static byte[] WriteInputProjection(
        CombatJournalStart start,
        ExternalId replayId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CanonicalJsonWriter.GetWriterOptions()))
        {
            writer.WriteStartObject();
            writer.WriteString("battle_id", start.BattleId.Value);

            writer.WriteStartObject("config");
            writer.WriteString(
                "balance_schema_version",
                start.Config.BalanceSchemaVersion.ToString());
            writer.WriteString("config_hash", start.Config.ConfigHash.Value);
            writer.WriteString("config_version", start.Config.ConfigVersion.ToString());
            writer.WriteEndObject();

            writer.WriteStartObject("engine");
            writer.WriteString("engine_version", start.EngineVersion.ToString());
            writer.WriteString("ordering_version", start.OrderingVersion.ToString());
            writer.WriteString("rng_version", start.RngVersion.ToString());
            writer.WriteEndObject();

            writer.WritePropertyName("input");
            WriteInput(writer, start);
            writer.WriteString("replay_id", replayId.Value);
            writer.WriteString("schema_version", Battle.Contracts.Versions.ContractVersions.Replay.ToString());
            writer.WriteEndObject();
            writer.Flush();
        }

        return CanonicalJsonWriter.Canonicalize(buffer.WrittenMemory);
    }

    private static void WriteInput(Utf8JsonWriter writer, CombatJournalStart start)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("arena");
        writer.WriteString("arena_id", start.Input.Arena.ArenaId.Value);
        writer.WriteNumber("max_position", start.Input.Arena.MaximumPosition);
        writer.WriteNumber("min_position", start.Input.Arena.MinimumPosition);
        writer.WriteNumber("start_position_a", start.Input.Arena.StartPositionA);
        writer.WriteNumber("start_position_b", start.Input.Arena.StartPositionB);
        writer.WriteEndObject();

        writer.WriteStartArray("fighters");
        WriteFighterStart(writer, start.FighterA);
        WriteFighterStart(writer, start.FighterB);
        writer.WriteEndArray();

        writer.WriteString(
            "master_seed",
            start.Input.MasterSeed.ToString(CultureInfo.InvariantCulture));
        writer.WriteString("mode_rules_id", start.Input.ModeRulesId.Value);
        writer.WriteEndObject();
    }

    private static void WriteFighterStart(
        Utf8JsonWriter writer,
        CombatJournalFighterStart fighter)
    {
        var build = fighter.Build;
        writer.WriteStartObject();
        writer.WriteString("animal_id", build.AnimalId.Value);
        WriteNullableString(writer, "build_id", build.BuildId?.Value);
        writer.WriteString("fighter_id", FighterIdText(build.FighterId));

        writer.WriteStartObject("gear");
        writer.WriteString("defense", build.Gear.Defense.Value);
        writer.WriteString("offense", build.Gear.Offense.Value);
        writer.WriteString("utility", build.Gear.Utility.Value);
        writer.WriteEndObject();

        writer.WritePropertyName("initial_frame");
        WriteFrame(writer, fighter.InitialFrame);
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

    private static void WriteFrame(Utf8JsonWriter writer, FighterFrame frame)
    {
        writer.WriteStartObject();
        WriteNullableString(writer, "action_id", frame.ActionId?.Value);
        WriteNullableString(writer, "action_phase", frame.ActionPhase?.ToString());

        writer.WriteStartArray("effects");
        foreach (var effect in frame.Effects)
        {
            writer.WriteStartObject();
            writer.WriteString("effect_id", effect.EffectId.Value);
            writer.WriteString("expiry_boundary", effect.ExpiryBoundary.ToString());
            writer.WriteNumber("stacks", effect.Stacks);
            writer.WriteNumber("ticks_remaining", effect.TicksRemaining);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteNumber("energy", frame.Energy);
        writer.WriteString("facing", frame.Facing.ToString());
        writer.WriteString("fighter_id", FighterIdText(frame.FighterId));
        writer.WriteNumber("health", frame.Health);
        writer.WriteNumber("max_energy", frame.MaxEnergy);
        writer.WriteNumber("max_health", frame.MaxHealth);
        writer.WriteNumber("position", frame.Position);
        writer.WriteNumber("stagger", frame.Stagger);
        writer.WriteNumber("stagger_threshold", frame.StaggerThreshold);
        writer.WriteString("state", frame.State.ToString());
        if (frame.StateTicksRemaining.HasValue)
        {
            writer.WriteNumber("state_ticks_remaining", frame.StateTicksRemaining.Value);
        }
        else
        {
            writer.WriteNull("state_ticks_remaining");
        }

        writer.WriteStartObject("unique_resource");
        writer.WriteNumber("max", frame.UniqueResource.Maximum);
        writer.WriteString("resource_id", frame.UniqueResource.ResourceId.Value);
        writer.WriteNumber("value", frame.UniqueResource.Value);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static string FighterIdText(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => "fighter_a",
        FighterId.FighterB => "fighter_b",
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };
}
