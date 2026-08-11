using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.ConformanceTests.Replay;

internal static class FixtureCombatEventReader
{
    internal static CombatEventDraft ReadDraft(JsonElement element, BattleSummary terminalSummary)
    {
        var payload = ReadPayload(
            element.GetProperty("event_type").GetString()!,
            element.GetProperty("payload"),
            terminalSummary);

        return new CombatEventDraft(
            new ArtifactVersion(element.GetProperty("schema_version").GetString()!),
            new ArtifactVersion(element.GetProperty("engine_version").GetString()!),
            new Sha256Digest(element.GetProperty("config_hash").GetString()!),
            new ExternalId(element.GetProperty("battle_id").GetString()!),
            element.GetProperty("tick").GetInt32(),
            element.GetProperty("sequence").GetInt64(),
            new EventId(element.GetProperty("event_id").GetString()!),
            ReadNullable(element.GetProperty("source_event_id"), static value => new EventId(value)),
            ReadFighterId(element.GetProperty("actor_id")),
            ReadFighterId(element.GetProperty("target_id")),
            ReadNullable(element.GetProperty("action_id"), static value => new StableId(value)),
            ReadNullable(element.GetProperty("effect_id"), static value => new StableId(value)),
            ReadNullable(element.GetProperty("decision_id"), static value => new DecisionId(value)),
            ReadNullable(element.GetProperty("resolution_group_id"), static value => new ExternalId(value)),
            element.GetProperty("reason_codes").EnumerateArray()
                .Select(item => new ReasonCode(item.GetString()!)),
            ReadRng(element.GetProperty("rng")),
            ReadFramePair(element.GetProperty("before")),
            ReadFramePair(element.GetProperty("after")),
            payload);
    }

    internal static BattleSummary ReadSummary(JsonElement element) =>
        new(
            Enum.Parse<BattleOutcome>(element.GetProperty("outcome").GetString()!),
            ReadFighterId(element.GetProperty("winner_fighter_id")),
            Enum.Parse<BattleEndReason>(element.GetProperty("end_reason").GetString()!),
            element.GetProperty("end_tick").GetInt32(),
            element.GetProperty("duration_ticks").GetInt32(),
            element.GetProperty("event_count").GetInt64(),
            ReadEventIds(element.GetProperty("pivotal_event_ids")),
            element.GetProperty("final_frames").EnumerateArray().Select(ReadFrame));

    private static CombatEventPayload ReadPayload(
        string eventType,
        JsonElement payload,
        BattleSummary terminalSummary)
    {
        var related = ReadEventIds(payload.GetProperty("related_event_ids"));
        return eventType switch
        {
            nameof(CombatEventType.BattleStarted) => new BattleStartedPayload(
                related,
                new Sha256Digest(payload.GetProperty("input_digest").GetString()!),
                payload.GetProperty("initial_frames").EnumerateArray().Select(ReadFrame),
                payload.GetProperty("initiative_order").EnumerateArray().Select(ReadRequiredFighterId),
                Enum.Parse<InitiativeTieBreak>(payload.GetProperty("initiative_tie_break").GetString()!)),
            nameof(CombatEventType.DecisionMade) => new DecisionMadePayload(
                related,
                new StableId(payload.GetProperty("chosen_action_id").GetString()!),
                payload.GetProperty("legal_action_ids").EnumerateArray()
                    .Select(item => new StableId(item.GetString()!)),
                payload.GetProperty("candidate_count").GetInt32(),
                payload.GetProperty("chosen_weight").GetInt32(),
                payload.GetProperty("weight_sum").GetInt32(),
                Enum.Parse<DecisionSelectionMode>(payload.GetProperty("selection_mode").GetString()!),
                payload.GetProperty("dominant_modifiers").EnumerateArray().Select(ReadModifier)),
            nameof(CombatEventType.ActionCommitted) => new ActionCommittedPayload(
                related,
                ReadFighterId(payload.GetProperty("target_fighter_id")),
                payload.GetProperty("energy_cost").GetInt32(),
                payload.GetProperty("resource_cost").GetInt32(),
                payload.GetProperty("startup_ticks").GetInt32(),
                payload.GetProperty("active_ticks").GetInt32(),
                payload.GetProperty("recovery_ticks").GetInt32(),
                payload.GetProperty("cooldown_ticks").GetInt32(),
                Enum.Parse<CommitDirection>(payload.GetProperty("commit_direction").GetString()!),
                ReadNullableInt32(payload.GetProperty("target_position_at_commit"))),
            nameof(CombatEventType.AttackPrepared) => new AttackPreparedPayload(
                related,
                payload.GetProperty("telegraph_tick").GetInt32(),
                payload.GetProperty("impact_ticks").EnumerateArray().Select(item => item.GetInt32()),
                payload.GetProperty("direction_locked").GetBoolean(),
                ReadFighterId(payload.GetProperty("target_fighter_id"))),
            nameof(CombatEventType.ActionPhaseChanged) => new ActionPhaseChangedPayload(
                related,
                ReadEnum<ActionPhase>(payload.GetProperty("from_phase")),
                ReadEnum<ActionPhase>(payload.GetProperty("to_phase")),
                payload.GetProperty("phase_ticks").GetInt32()),
            nameof(CombatEventType.FinisherTriggered) => new FinisherTriggeredPayload(
                related,
                new EventId(payload.GetProperty("predicted_lethal_event_id").GetString()!),
                Enum.Parse<FinisherMarkerKind>(payload.GetProperty("marker_kind").GetString()!),
                Enum.Parse<FinisherConfidence>(payload.GetProperty("confidence").GetString()!)),
            nameof(CombatEventType.AttackHit) => new AttackHitPayload(
                related,
                new ExternalId(payload.GetProperty("impact_id").GetString()!),
                new ExternalId(payload.GetProperty("hit_group_id").GetString()!),
                payload.GetProperty("gap").GetInt32(),
                payload.GetProperty("hit_range_min").GetInt32(),
                payload.GetProperty("hit_range_max").GetInt32(),
                Enum.Parse<MovementDirection>(payload.GetProperty("hit_direction").GetString()!),
                payload.GetProperty("attack_tags").EnumerateArray()
                    .Select(item => new StableId(item.GetString()!))),
            nameof(CombatEventType.DamageApplied) => ReadDamageApplied(related, payload),
            nameof(CombatEventType.StateChanged) => new StateChangedPayload(
                related,
                Enum.Parse<FighterState>(payload.GetProperty("old_state").GetString()!),
                Enum.Parse<FighterState>(payload.GetProperty("new_state").GetString()!),
                ReadNullableInt32(payload.GetProperty("duration_ticks")),
                ReadNullableInt32(payload.GetProperty("control_ratio_fp")),
                ReadNullableInt32(payload.GetProperty("fatigue_multiplier_fp")),
                Enum.Parse<ImmunityResult>(payload.GetProperty("immunity_result").GetString()!)),
            nameof(CombatEventType.FighterDefeated) => new FighterDefeatedPayload(
                related,
                ReadRequiredFighterId(payload.GetProperty("defeated_fighter_id")),
                ReadNullable(payload.GetProperty("lethal_source_event_id"), static value => new EventId(value)),
                ReadNullable(payload.GetProperty("simultaneous_group_id"), static value => new ExternalId(value)),
                payload.GetProperty("final_health").GetInt32()),
            nameof(CombatEventType.BattleEnded) => new BattleEndedPayload(related, terminalSummary),
            _ => throw new InvalidOperationException($"Unsupported normative fixture event '{eventType}'."),
        };
    }

    private static DamageAppliedPayload ReadDamageApplied(
        IReadOnlyList<EventId> related,
        JsonElement payload)
    {
        var breakdown = payload.GetProperty("breakdown");
        return new DamageAppliedPayload(
            related,
            new ExternalId(payload.GetProperty("impact_id").GetString()!),
            new ExternalId(payload.GetProperty("damage_id").GetString()!),
            new DamageBreakdown(
                breakdown.GetProperty("power_term").GetInt32(),
                breakdown.GetProperty("raw").GetInt32(),
                breakdown.GetProperty("after_armor").GetInt32(),
                breakdown.GetProperty("after_block").GetInt32(),
                breakdown.GetProperty("final").GetInt32(),
                breakdown.GetProperty("minimum").GetInt32(),
                breakdown.GetProperty("cap").GetInt32(),
                breakdown.GetProperty("overkill").GetInt32()),
            payload.GetProperty("hp_before").GetInt32(),
            payload.GetProperty("hp_after").GetInt32(),
            payload.GetProperty("damage_tags").EnumerateArray()
                .Select(item => new StableId(item.GetString()!)),
            payload.GetProperty("lethal").GetBoolean());
    }

    private static ModifierTrace ReadModifier(JsonElement element) =>
        new(
            new ReasonCode(element.GetProperty("code").GetString()!),
            element.GetProperty("multiplier_fp").GetInt32());

    private static FramePair ReadFramePair(JsonElement element) =>
        new(
            ReadNullableFrame(element.GetProperty("actor")),
            ReadNullableFrame(element.GetProperty("target")));

    private static FighterFrame? ReadNullableFrame(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : ReadFrame(element);

    private static FighterFrame ReadFrame(JsonElement element)
    {
        var resource = element.GetProperty("unique_resource");
        return new FighterFrame(
            ReadRequiredFighterId(element.GetProperty("fighter_id")),
            element.GetProperty("position").GetInt32(),
            Enum.Parse<Facing>(element.GetProperty("facing").GetString()!),
            Enum.Parse<FighterState>(element.GetProperty("state").GetString()!),
            ReadNullableInt32(element.GetProperty("state_ticks_remaining")),
            ReadNullable(element.GetProperty("action_id"), static value => new StableId(value)),
            ReadEnum<ActionPhase>(element.GetProperty("action_phase")),
            element.GetProperty("health").GetInt32(),
            element.GetProperty("max_health").GetInt32(),
            element.GetProperty("energy").GetInt32(),
            element.GetProperty("max_energy").GetInt32(),
            new ResourceFrame(
                new StableId(resource.GetProperty("resource_id").GetString()!),
                resource.GetProperty("value").GetInt32(),
                resource.GetProperty("max").GetInt32()),
            element.GetProperty("stagger").GetInt32(),
            element.GetProperty("stagger_threshold").GetInt32(),
            element.GetProperty("effects").EnumerateArray().Select(ReadEffect));
    }

    private static EffectFrame ReadEffect(JsonElement element) =>
        new(
            new StableId(element.GetProperty("effect_id").GetString()!),
            element.GetProperty("stacks").GetInt32(),
            element.GetProperty("ticks_remaining").GetInt32(),
            Enum.Parse<EffectExpiryBoundary>(element.GetProperty("expiry_boundary").GetString()!));

    private static RngProvenance? ReadRng(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new RngProvenance(
            Enum.Parse<RngStream>(element.GetProperty("stream").GetString()!),
            ulong.Parse(element.GetProperty("index").GetString()!, CultureInfo.InvariantCulture),
            Enum.Parse<RngOperation>(element.GetProperty("operation").GetString()!),
            element.GetProperty("range_min_inclusive").GetInt32(),
            element.GetProperty("range_max_exclusive").GetInt32(),
            uint.Parse(element.GetProperty("raw_u32").GetString()!, CultureInfo.InvariantCulture),
            element.GetProperty("result").GetInt32(),
            element.GetProperty("normalized_fp").GetInt32());
    }

    private static IReadOnlyList<EventId> ReadEventIds(JsonElement element) =>
        element.EnumerateArray().Select(item => new EventId(item.GetString()!)).ToArray();

    private static FighterId ReadRequiredFighterId(JsonElement element) =>
        ReadFighterId(element) ?? throw new InvalidOperationException("A fighter ID is required.");

    private static FighterId? ReadFighterId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.GetString() switch
        {
            "fighter_a" => FighterId.FighterA,
            "fighter_b" => FighterId.FighterB,
            _ => throw new InvalidOperationException("Unknown fixture fighter ID."),
        };
    }

    private static int? ReadNullableInt32(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();

    private static TEnum? ReadEnum<TEnum>(JsonElement element)
        where TEnum : struct, Enum =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : Enum.Parse<TEnum>(element.GetString()!);

    private static TValue? ReadNullable<TValue>(
        JsonElement element,
        Func<string, TValue> factory)
        where TValue : struct =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : factory(element.GetString()!);
}
