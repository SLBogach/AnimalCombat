using Battle.Contracts.Config;

namespace Battle.Config.Schema;

internal static class BalanceV01Schema
{
    public const string SchemaVersion = "combat.balance/0.1";
    public const string ConfigVersionSetting = "global.sim.config_version";
    public const string SchemaVersionSetting = "global.sim.schema_version";

    public static readonly string[] RootMembers =
    {
        "actions",
        "effects",
        "fighters",
        "gear",
        "passives",
        "settings",
        "tactics",
    };

    public static readonly IReadOnlyDictionary<string, CatalogSchema> Catalogs =
        new Dictionary<string, CatalogSchema>(StringComparer.Ordinal)
        {
            ["actions"] = CreateActions(),
            ["effects"] = CreateEffects(),
            ["fighters"] = CreateFighters(),
            ["gear"] = CreateGear(),
            ["passives"] = CreatePassives(),
            ["tactics"] = CreateTactics(),
        };

    public static readonly CatalogSchema Settings = CreateSettings();

    private static CatalogSchema CreateSettings() => new(
        idProperty: null,
        requiredIntegers: "analysis.benchmark_armor|battle.time_limit_ticks|fighter.bear.low_health_rage_multiplier_fp|fighter.bear.low_health_threshold_fp|fighter.bear.rage_per_blocked_heavy|fighter.bear.rage_per_damage_fp|fighter.bear.rage_per_heavy|fighter.bear.rage_tier_1|fighter.bear.rage_tier_2|fighter.bear.rage_tier_3|fighter.gorilla.disengage_grace_ticks|fighter.gorilla.disengage_loss_per_tick|fighter.gorilla.grip_per_block|fighter.gorilla.grip_per_grab|fighter.gorilla.grip_per_shove|fighter.gorilla.grip_per_wall_tick|fighter.gorilla.grip_tier_1|fighter.gorilla.grip_tier_2|fighter.gorilla.grip_tier_3|fighter.gorilla.hard_control_loss|fighter.gorilla.wall_tick_interval|fighter.kangaroo.break_loss|fighter.kangaroo.decay_grace_ticks|fighter.kangaroo.dodge_bonus|fighter.kangaroo.idle_decay_per_tick|fighter.kangaroo.rhythm_bonus|fighter.kangaroo.tempo_per_unit_fp|fighter.kangaroo.tempo_tier_1|fighter.kangaroo.tempo_tier_2|fighter.kangaroo.tempo_tier_3|global.ai.default_perception_delay_ticks|global.ai.hard_opportunity_misses|global.ai.opportunity_cap_fp|global.ai.opportunity_growth_fp|global.ai.repeat_same_action_fp|global.ai.repeat_same_category_fp|global.arena.max_position|global.arena.min_position|global.arena.start_position_a|global.arena.start_position_b|global.arena.wall_zone_size|global.control.control_k|global.control.fatigue_decay_ticks|global.control.fatigue_threshold|global.control.force_k|global.control.grab_lockout_ticks|global.control.immunity_ticks|global.control.max_hold_ticks|global.control.stun_max_ticks|global.control.stun_min_ticks|global.control.wakeup_immunity_ticks|global.damage.armor_k|global.damage.block_max|global.damage.block_min|global.damage.block_slope|global.damage.cap|global.damage.dodge_max|global.damage.dodge_min|global.damage.dodge_slope|global.damage.floor|global.damage.speed_baseline|global.damage.speed_max|global.damage.speed_min|global.damage.speed_slope|global.sim.decision_weight_max|global.sim.fp_scale|global.sim.multiplier_max|global.sim.multiplier_min|global.sim.probability_max|global.sim.probability_min|global.sim.tick_ms|state.active.energy_regen_fp|state.block.energy_regen_fp|state.dodge.energy_regen_fp|state.grabbed.energy_regen_fp|state.idle.energy_regen_fp|state.knocked_down.energy_regen_fp|state.prepare.energy_regen_fp|state.recovery.energy_regen_fp|state.stunned.energy_regen_fp",
        requiredBooleans: string.Empty,
        requiredStrings: "global.sim.config_version|global.sim.ordering_version|global.sim.rng_version|global.sim.schema_version",
        optionalIntegers: string.Empty,
        optionalBooleans: string.Empty,
        optionalStrings: string.Empty);

    private static CatalogSchema CreateFighters() => new(
        "animal_id",
        "action_speed|armor|collision_radius|control_power|control_resistance|energy_regen|evasion|guard|guard_break|initiative|mass|max_energy|max_health|max_resource|move_speed|power|precision|stagger_threshold|start_resource",
        string.Empty,
        "animal_id|resource_id",
        string.Empty,
        string.Empty,
        string.Empty,
        ("animal_id", "bear|gorilla|kangaroo"));

    private static CatalogSchema CreateActions() => new(
        "action_id",
        "action_priority|active_ticks|base_damage|base_knockback|base_stagger|base_stun_ticks|base_weight|block_base_chance_fp|block_reduction_fp|chip_min|clash_priority|cooldown_ticks|dodge_base_chance_fp|energy_cost|grab_priority|hard_opportunity_misses|hit_count|hit_range_max|hit_range_min|knockback_max|knockback_min|max_consecutive_uses|min_damage|move_distance|opportunity_cap_fp|power_ratio_fp|preferred_range_max|preferred_range_min|recovery_base_ticks|recovery_max_ticks|recovery_min_ticks|resolution_priority|resource_cost|startup_base_ticks|startup_max_ticks|startup_min_ticks|wall_damage_max|wall_damage_min|wall_damage_per_unit_fp",
        "blockable|dodgeable|track_target|undodgeable|wall_impact",
        "action_id|animal_id|category|hit_schedule|interrupt_profile|movement_mode|slot_type|tags",
        string.Empty,
        string.Empty,
        string.Empty,
        ("animal_id", "all|bear|gorilla|kangaroo"),
        ("interrupt_profile", "Armored|Cancelable|Committed|UninterruptibleImpact|Unstoppable"),
        ("movement_mode", "Adaptive|Approach|Follow|None|Pull|Push|Retreat|Swap"),
        ("slot_type", "Basic|Special|System"));

    private static CatalogSchema CreatePassives() => new(
        "passive_id",
        "duration_ticks|internal_cooldown_ticks|max_activations_per_battle|max_activations_per_tick|resource_gain|stack_cap|value1|value2|weight_multiplier_fp",
        string.Empty,
        "animal_id|conditions|modifier_stat1|modifier_stat2|operation1|operation2|passive_id|tags|trigger",
        string.Empty,
        string.Empty,
        "effect_id",
        ("animal_id", "bear|gorilla|kangaroo"),
        ("operation1", "Add|Multiply|Override"),
        ("operation2", "Add|Multiply|Override"));

    private static CatalogSchema CreateEffects() => new(
        "effect_id",
        "duration_ticks|internal_cooldown_ticks|max_activations_per_battle|max_activations_per_tick|stack_cap|value1",
        string.Empty,
        "compare_key|effect_id|expiry_boundary|modifier_stat1|operation1|stack_group|stack_policy|tags",
        "value2",
        string.Empty,
        "lookup_profile|modifier_stat2|operation2",
        ("expiry_boundary", "ExpireAfterTick|ExpireBeforeTick"),
        ("operation1", "Add|Multiply|Override"),
        ("operation2", "Add|Multiply|Override"),
        ("stack_policy", "AddStacks|Refresh|Reject|Replace|StrongestWins"));

    private static CatalogSchema CreateTactics() => new(
        "tactic_id",
        "approach_fp|block_fp|counter_fp|dodge_fp|grab_fp|heavy_fp|light_fp|low_hpfp|perception_delay_ticks|repeat_penalty_fp|resource_generator_fp|resource_spender_fp|retreat_fp|self_wall_fp|signature_fp|target_recovery_fp|target_wall_fp",
        string.Empty,
        "tactic_id",
        string.Empty,
        string.Empty,
        string.Empty);

    private static CatalogSchema CreateGear() => new(
        "gear_id",
        "normalized_value|value1",
        string.Empty,
        "gear_id|operation1|slot|stat1|tags",
        "value2",
        string.Empty,
        "operation2|stat2",
        ("operation1", "Add|Multiply|Override"),
        ("operation2", "Add|Multiply|Override"),
        ("slot", "Defense|Offense|Utility"));
}

internal sealed class CatalogSchema
{
    public CatalogSchema(
        string? idProperty,
        string requiredIntegers,
        string requiredBooleans,
        string requiredStrings,
        string optionalIntegers,
        string optionalBooleans,
        string optionalStrings,
        params (string Field, string Values)[] enumValues)
    {
        IdProperty = idProperty;
        var fields = new Dictionary<string, FieldSchema>(StringComparer.Ordinal);
        Add(fields, requiredIntegers, ConfigValueKind.Integer, true);
        Add(fields, requiredBooleans, ConfigValueKind.Boolean, true);
        Add(fields, requiredStrings, ConfigValueKind.String, true);
        Add(fields, optionalIntegers, ConfigValueKind.Integer, false);
        Add(fields, optionalBooleans, ConfigValueKind.Boolean, false);
        Add(fields, optionalStrings, ConfigValueKind.String, false);

        foreach (var item in enumValues)
        {
            fields[item.Field].SetEnum(item.Values.Split('|'));
        }

        Fields = fields;
    }

    public string? IdProperty { get; }

    public IReadOnlyDictionary<string, FieldSchema> Fields { get; }

    private static void Add(
        IDictionary<string, FieldSchema> target,
        string names,
        ConfigValueKind kind,
        bool required)
    {
        if (names.Length == 0)
        {
            return;
        }

        foreach (var name in names.Split('|'))
        {
            target.Add(name, new FieldSchema(kind, required));
        }
    }
}

internal sealed class FieldSchema
{
    public FieldSchema(ConfigValueKind kind, bool required)
    {
        Kind = kind;
        Required = required;
        EnumValues = Array.Empty<string>();
    }

    public ConfigValueKind Kind { get; }

    public bool Required { get; }

    public IReadOnlyList<string> EnumValues { get; private set; }

    public void SetEnum(IReadOnlyList<string> values) => EnumValues = values;
}
