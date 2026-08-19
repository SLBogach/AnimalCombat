using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Engine;

internal static class EngineTestFixture
{
    internal static Sha256Digest ConfigDigest { get; } = new(
        "sha256:0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f");

    internal static Sha256Digest InputDigest { get; } = new(
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");

    internal static Sha256Digest FinalDigest { get; } = new(
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    internal static BattleRequest CreateRequest(
        NormalizationMode normalization = NormalizationMode.None,
        ArtifactVersion? engineVersion = null,
        Sha256Digest? configHash = null,
        IEnumerable<StableId>? allowedActions = null,
        FighterBuildSnapshot? buildA = null,
        FighterBuildSnapshot? buildB = null,
        ArtifactVersion? modeVersion = null,
        IEnumerable<StableId>? allowedAnimals = null,
        IEnumerable<StableId>? allowedPassives = null,
        IEnumerable<StableId>? allowedGear = null,
        IEnumerable<StableId>? allowedTactics = null) =>
        new(
            new ExternalId("battle-wp06-unit"),
            engineVersion ?? ContractVersions.Engine,
            configHash ?? ConfigDigest,
            new ModeRulesSnapshot(
                new StableId("engine_shell_wait_v01"),
                modeVersion ?? ContractVersions.ModeRules,
                normalization,
                allowedAnimals ?? new[] { new StableId("bear"), new StableId("kangaroo") },
                allowedActions ?? ActionIds(),
                allowedPassives ?? new[] { new StableId("bear_thick_hide"), new StableId("kangaroo_never_still") },
                allowedGear ?? GearIds(),
                allowedTactics ?? new[] { new StableId("tactic_position"), new StableId("tactic_pressure") }),
            2_026_072_901UL,
            buildA ?? CreateBuildA(),
            buildB ?? CreateBuildB());

    internal static CompiledBattleConfig CreateConfig(
        int timeLimit = 1,
        int maximumEvents = 200_000,
        int maximumZeroProgressTicks = 100,
        Func<IEnumerable<ConfigProperty>, IEnumerable<ConfigProperty>>? changeSettings = null,
        Func<IEnumerable<CompiledConfigEntity>, IEnumerable<CompiledConfigEntity>>? changeFighters = null,
        Func<IEnumerable<CompiledConfigEntity>, IEnumerable<CompiledConfigEntity>>? changeActions = null,
        Func<IEnumerable<CompiledConfigEntity>, IEnumerable<CompiledConfigEntity>>? changePassives = null,
        Func<IEnumerable<CompiledConfigEntity>, IEnumerable<CompiledConfigEntity>>? changeTactics = null,
        Func<IEnumerable<CompiledConfigEntity>, IEnumerable<CompiledConfigEntity>>? changeGear = null,
        ArtifactVersion? balanceSchemaVersion = null,
        ArtifactVersion? configVersion = null,
        Sha256Digest? configHash = null)
    {
        var settings = Properties(
            ("battle.time_limit_ticks", ConfigValue.FromInteger(timeLimit)),
            ("global.arena.max_position", ConfigValue.FromInteger(10_000)),
            ("global.arena.min_position", ConfigValue.FromInteger(0)),
            ("global.arena.start_position_a", ConfigValue.FromInteger(2_000)),
            ("global.arena.start_position_b", ConfigValue.FromInteger(4_500)),
            ("global.arena.wall_zone_size", ConfigValue.FromInteger(1_200)),
            ("global.ai.default_perception_delay_ticks", ConfigValue.FromInteger(5)),
            ("global.ai.hard_opportunity_misses", ConfigValue.FromInteger(4)),
            ("global.ai.opportunity_cap_fp", ConfigValue.FromInteger(2_500)),
            ("global.ai.opportunity_growth_fp", ConfigValue.FromInteger(250)),
            ("global.ai.repeat_same_action_fp", ConfigValue.FromInteger(550)),
            ("global.ai.repeat_same_category_fp", ConfigValue.FromInteger(800)),
            ("global.damage.speed_baseline", ConfigValue.FromInteger(100)),
            ("global.damage.speed_max", ConfigValue.FromInteger(1_500)),
            ("global.damage.speed_min", ConfigValue.FromInteger(600)),
            ("global.damage.speed_slope", ConfigValue.FromInteger(5)),
            ("global.sim.config_version", ConfigValue.FromString("v0.1")),
            ("global.sim.decision_weight_max", ConfigValue.FromInteger(100_000_000)),
            ("global.sim.fp_scale", ConfigValue.FromInteger(1_000)),
            ("global.sim.max_events_per_battle", ConfigValue.FromInteger(maximumEvents)),
            ("global.sim.max_zero_progress_ticks", ConfigValue.FromInteger(maximumZeroProgressTicks)),
            ("global.sim.multiplier_max", ConfigValue.FromInteger(3_000)),
            ("global.sim.multiplier_min", ConfigValue.FromInteger(250)),
            ("global.sim.ordering_version", ConfigValue.FromString(ContractVersions.Ordering.ToString())),
            ("global.sim.rng_version", ConfigValue.FromString(ContractVersions.Rng.ToString())),
            ("global.sim.schema_version", ConfigValue.FromString(ContractVersions.BalanceSchema.ToString())));
        var fighters = new[]
        {
            Entity(
                "bear",
                0,
                ("collision_radius", ConfigValue.FromInteger(520)),
                ("initiative", ConfigValue.FromInteger(85)),
                ("action_speed", ConfigValue.FromInteger(85)),
                ("max_energy", ConfigValue.FromInteger(1_000)),
                ("max_health", ConfigValue.FromInteger(1_650)),
                ("max_resource", ConfigValue.FromInteger(1_000)),
                ("move_speed", ConfigValue.FromInteger(70)),
                ("resource_id", ConfigValue.FromString("rage")),
                ("stagger_threshold", ConfigValue.FromInteger(260)),
                ("start_resource", ConfigValue.FromInteger(0))),
            Entity(
                "kangaroo",
                1,
                ("collision_radius", ConfigValue.FromInteger(430)),
                ("initiative", ConfigValue.FromInteger(130)),
                ("action_speed", ConfigValue.FromInteger(130)),
                ("max_energy", ConfigValue.FromInteger(1_000)),
                ("max_health", ConfigValue.FromInteger(1_150)),
                ("max_resource", ConfigValue.FromInteger(1_000)),
                ("move_speed", ConfigValue.FromInteger(135)),
                ("resource_id", ConfigValue.FromString("tempo")),
                ("stagger_threshold", ConfigValue.FromInteger(180)),
                ("start_resource", ConfigValue.FromInteger(0))),
        };

        return new CompiledBattleConfig(
            new ConfigReference(
                balanceSchemaVersion ?? ContractVersions.BalanceSchema,
                configVersion ?? new ArtifactVersion("v0.1"),
                configHash ?? ConfigDigest),
            changeSettings?.Invoke(settings) ?? settings,
            changeFighters?.Invoke(fighters) ?? fighters,
            changeActions?.Invoke(CreateActions()) ?? CreateActions(),
            changePassives?.Invoke(CreatePassives()) ?? CreatePassives(),
            Array.Empty<CompiledConfigEntity>(),
            changeTactics?.Invoke(CreateTactics()) ?? CreateTactics(),
            changeGear?.Invoke(CreateGear()) ?? CreateGear());
    }

    internal static BattleSetup CreateSetup(int timeLimit = 1)
    {
        var result = BattleSetupFactory.Create(CreateRequest(), CreateConfig(timeLimit));
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code.Value)));
        return result.Setup!;
    }

    internal static FighterBuildSnapshot CreateBuildA() => new(
        FighterId.FighterA,
        FighterSide.A,
        new StableId("bear"),
        null,
        new[] { new StableId("bear_earthbreaker"), new StableId("bear_rampage_charge") },
        new StableId("bear_thick_hide"),
        new GearSelection(
            new StableId("gear_offense_power_wraps"),
            new StableId("gear_defense_reinforced_hide"),
            new StableId("gear_utility_sprint_soles")),
        new StableId("tactic_pressure"));

    internal static FighterBuildSnapshot CreateBuildB() => new(
        FighterId.FighterB,
        FighterSide.B,
        new StableId("kangaroo"),
        null,
        new[] { new StableId("kangaroo_flying_kick"), new StableId("kangaroo_tail_counter") },
        new StableId("kangaroo_never_still"),
        new GearSelection(
            new StableId("gear_offense_precision_lens"),
            new StableId("gear_defense_reinforced_hide"),
            new StableId("gear_utility_sprint_soles")),
        new StableId("tactic_position"));

    internal static IReadOnlyList<StableId> ActionIds() => new[]
    {
        new StableId("bear_earthbreaker"),
        new StableId("bear_rampage_charge"),
        new StableId("kangaroo_flying_kick"),
        new StableId("kangaroo_tail_counter"),
        new StableId("sys_wait"),
    };

    internal static IReadOnlyList<StableId> GearIds() => new[]
    {
        new StableId("gear_defense_reinforced_hide"),
        new StableId("gear_offense_power_wraps"),
        new StableId("gear_offense_precision_lens"),
        new StableId("gear_utility_sprint_soles"),
    };

    internal static IReadOnlyList<ConfigProperty> Properties(
        params (string Name, ConfigValue Value)[] values) =>
        values
            .OrderBy(value => value.Name, StringComparer.Ordinal)
            .Select(value => new ConfigProperty(value.Name, value.Value))
            .ToArray();

    internal static CompiledConfigEntity Entity(
        string id,
        int handle,
        params (string Name, ConfigValue Value)[] values) =>
        new(new StableId(id), handle, Properties(values));

    internal static CompiledConfigEntity WithProperty(
        CompiledConfigEntity entity,
        string name,
        ConfigValue value) =>
        new(
            entity.Id,
            entity.DenseHandle,
            entity.Properties
                .Where(property => !string.Equals(property.Name, name, StringComparison.Ordinal))
                .Append(new ConfigProperty(name, value))
                .OrderBy(property => property.Name, StringComparer.Ordinal));

    internal static IReadOnlyList<CompiledConfigEntity> ReindexCatalog(
        IEnumerable<CompiledConfigEntity> source) =>
        source
            .OrderBy(entity => entity.Id)
            .Select((entity, index) => new CompiledConfigEntity(entity.Id, index, entity.Properties))
            .ToArray();

    private static IReadOnlyList<CompiledConfigEntity> CreateActions()
    {
        var specs = new[]
        {
            ("bear_earthbreaker", "bear", "Special"),
            ("bear_rampage_charge", "bear", "Special"),
            ("kangaroo_flying_kick", "kangaroo", "Special"),
            ("kangaroo_tail_counter", "kangaroo", "Special"),
        };
        var result = specs.Select((spec, index) => CombatAction(
            spec.Item1,
            spec.Item2,
            spec.Item3,
            index)).ToList();
        result.Add(SystemAction("sys_approach", result.Count, "Movement", "Approach", 650, 0, 1_500, 1, 5, 1, true));
        result.Add(SystemAction("sys_retreat", result.Count, "Movement", "Retreat", 450, 1_600, 3_000, 1, 5, 1, true));
        result.Add(SystemAction("sys_wait", result.Count, "Wait", "None", 150, 0, 10_000, 0, 3, 0, false));
        return result;
    }

    private static CompiledConfigEntity CombatAction(
        string id,
        string animalId,
        string slot,
        int handle) => Entity(
        id,
        handle,
        ("active_ticks", ConfigValue.FromInteger(1)),
        ("animal_id", ConfigValue.FromString(animalId)),
        ("base_weight", ConfigValue.FromInteger(500)),
        ("category", ConfigValue.FromString("SignatureStrike")),
        ("cooldown_ticks", ConfigValue.FromInteger(10)),
        ("energy_cost", ConfigValue.FromInteger(0)),
        ("hard_opportunity_misses", ConfigValue.FromInteger(4)),
        ("hit_count", ConfigValue.FromInteger(1)),
        ("hit_range_max", ConfigValue.FromInteger(10_000)),
        ("hit_range_min", ConfigValue.FromInteger(0)),
        ("hit_schedule", ConfigValue.FromString("0")),
        ("max_consecutive_uses", ConfigValue.FromInteger(1)),
        ("movement_mode", ConfigValue.FromString("None")),
        ("opportunity_cap_fp", ConfigValue.FromInteger(2_500)),
        ("preferred_range_max", ConfigValue.FromInteger(10_000)),
        ("preferred_range_min", ConfigValue.FromInteger(0)),
        ("recovery_base_ticks", ConfigValue.FromInteger(1)),
        ("recovery_max_ticks", ConfigValue.FromInteger(1)),
        ("recovery_min_ticks", ConfigValue.FromInteger(1)),
        ("resource_cost", ConfigValue.FromInteger(100)),
        ("slot_type", ConfigValue.FromString(slot)),
        ("startup_base_ticks", ConfigValue.FromInteger(1)),
        ("startup_max_ticks", ConfigValue.FromInteger(1)),
        ("startup_min_ticks", ConfigValue.FromInteger(1)),
        ("tags", ConfigValue.FromString("signature|special")),
        ("track_target", ConfigValue.FromBoolean(false)));

    private static CompiledConfigEntity SystemAction(
        string id,
        int handle,
        string category,
        string movementMode,
        int weight,
        int preferredRangeMinimum,
        int preferredRangeMaximum,
        int startupTicks,
        int activeTicks,
        int recoveryTicks,
        bool trackTarget) => Entity(
        id,
        handle,
        ("active_ticks", ConfigValue.FromInteger(activeTicks)),
        ("animal_id", ConfigValue.FromString("all")),
        ("base_damage", ConfigValue.FromInteger(0)),
        ("base_knockback", ConfigValue.FromInteger(0)),
        ("base_stagger", ConfigValue.FromInteger(0)),
        ("base_stun_ticks", ConfigValue.FromInteger(0)),
        ("base_weight", ConfigValue.FromInteger(weight)),
        ("block_base_chance_fp", ConfigValue.FromInteger(0)),
        ("block_reduction_fp", ConfigValue.FromInteger(0)),
        ("blockable", ConfigValue.FromBoolean(false)),
        ("category", ConfigValue.FromString(category)),
        ("chip_min", ConfigValue.FromInteger(0)),
        ("clash_priority", ConfigValue.FromInteger(0)),
        ("cooldown_ticks", ConfigValue.FromInteger(0)),
        ("dodge_base_chance_fp", ConfigValue.FromInteger(0)),
        ("dodgeable", ConfigValue.FromBoolean(false)),
        ("energy_cost", ConfigValue.FromInteger(0)),
        ("grab_priority", ConfigValue.FromInteger(0)),
        ("hard_opportunity_misses", ConfigValue.FromInteger(0)),
        ("hit_count", ConfigValue.FromInteger(0)),
        ("hit_range_max", ConfigValue.FromInteger(0)),
        ("hit_range_min", ConfigValue.FromInteger(0)),
        ("hit_schedule", ConfigValue.FromString(string.Empty)),
        ("knockback_max", ConfigValue.FromInteger(0)),
        ("knockback_min", ConfigValue.FromInteger(0)),
        ("max_consecutive_uses", ConfigValue.FromInteger(4)),
        ("min_damage", ConfigValue.FromInteger(0)),
        ("move_distance", ConfigValue.FromInteger(0)),
        ("movement_mode", ConfigValue.FromString(movementMode)),
        ("opportunity_cap_fp", ConfigValue.FromInteger(1_000)),
        ("power_ratio_fp", ConfigValue.FromInteger(0)),
        ("preferred_range_max", ConfigValue.FromInteger(preferredRangeMaximum)),
        ("preferred_range_min", ConfigValue.FromInteger(preferredRangeMinimum)),
        ("recovery_base_ticks", ConfigValue.FromInteger(recoveryTicks)),
        ("recovery_max_ticks", ConfigValue.FromInteger(recoveryTicks)),
        ("recovery_min_ticks", ConfigValue.FromInteger(recoveryTicks)),
        ("resource_cost", ConfigValue.FromInteger(0)),
        ("slot_type", ConfigValue.FromString("System")),
        ("startup_base_ticks", ConfigValue.FromInteger(startupTicks)),
        ("startup_max_ticks", ConfigValue.FromInteger(startupTicks)),
        ("startup_min_ticks", ConfigValue.FromInteger(startupTicks)),
        ("tags", ConfigValue.FromString(
            movementMode == "None" ? "system|wait" : "system|movement|" + movementMode.ToLowerInvariant())),
        ("track_target", ConfigValue.FromBoolean(trackTarget)),
        ("undodgeable", ConfigValue.FromBoolean(false)),
        ("wall_damage_max", ConfigValue.FromInteger(0)),
        ("wall_damage_min", ConfigValue.FromInteger(0)),
        ("wall_damage_per_unit_fp", ConfigValue.FromInteger(0)),
        ("wall_impact", ConfigValue.FromBoolean(false)));

    private static IReadOnlyList<CompiledConfigEntity> CreatePassives() => new[]
    {
        Entity(
            "bear_thick_hide",
            0,
            ("animal_id", ConfigValue.FromString("bear")),
            ("tags", ConfigValue.FromString("defense")),
            ("weight_multiplier_fp", ConfigValue.FromInteger(1_000))),
        Entity(
            "kangaroo_never_still",
            1,
            ("animal_id", ConfigValue.FromString("kangaroo")),
            ("tags", ConfigValue.FromString("movement")),
            ("weight_multiplier_fp", ConfigValue.FromInteger(1_000))),
    };

    private static IReadOnlyList<CompiledConfigEntity> CreateTactics() => new[]
    {
        Tactic("tactic_position", 0),
        Tactic("tactic_pressure", 1),
    };

    private static CompiledConfigEntity Tactic(string id, int handle) => Entity(
        id,
        handle,
        ("approach_fp", ConfigValue.FromInteger(1_000)),
        ("block_fp", ConfigValue.FromInteger(1_000)),
        ("counter_fp", ConfigValue.FromInteger(1_000)),
        ("dodge_fp", ConfigValue.FromInteger(1_000)),
        ("grab_fp", ConfigValue.FromInteger(1_000)),
        ("heavy_fp", ConfigValue.FromInteger(1_000)),
        ("light_fp", ConfigValue.FromInteger(1_000)),
        ("low_hpfp", ConfigValue.FromInteger(1_000)),
        ("perception_delay_ticks", ConfigValue.FromInteger(5)),
        ("repeat_penalty_fp", ConfigValue.FromInteger(1_000)),
        ("resource_generator_fp", ConfigValue.FromInteger(1_000)),
        ("resource_spender_fp", ConfigValue.FromInteger(1_000)),
        ("retreat_fp", ConfigValue.FromInteger(1_000)),
        ("self_wall_fp", ConfigValue.FromInteger(1_000)),
        ("signature_fp", ConfigValue.FromInteger(1_000)),
        ("target_recovery_fp", ConfigValue.FromInteger(1_000)),
        ("target_wall_fp", ConfigValue.FromInteger(1_000)));

    private static IReadOnlyList<CompiledConfigEntity> CreateGear() => new[]
    {
        Gear("gear_defense_reinforced_hide", 0, "Defense", "Armor", 18),
        Gear("gear_offense_power_wraps", 1, "Offense", "Power", 12),
        Gear("gear_offense_precision_lens", 2, "Offense", "Precision", 15),
        Gear("gear_utility_sprint_soles", 3, "Utility", "MoveSpeed", 12),
    };

    private static CompiledConfigEntity Gear(
        string id,
        int handle,
        string slot,
        string stat,
        int value) => Entity(
        id,
        handle,
        ("operation1", ConfigValue.FromString("Add")),
        ("normalized_value", ConfigValue.FromInteger(1_000)),
        ("slot", ConfigValue.FromString(slot)),
        ("stat1", ConfigValue.FromString(stat)),
        ("tags", ConfigValue.FromString(stat.ToLowerInvariant())),
        ("value1", ConfigValue.FromInteger(value)));
}

internal sealed class RecordingJournal : ICombatEventJournal
{
    private readonly List<CombatEventDraft> _drafts = new();

    internal int BeginCount { get; private set; }

    internal int CompleteCount { get; private set; }

    internal IReadOnlyList<CombatEventDraft> Drafts => _drafts;

    internal CombatJournalStart? Start { get; private set; }

    internal BattleSummary? Summary { get; private set; }

    public JournalBeginResult Begin(in CombatJournalStart start)
    {
        BeginCount++;
        Start = start;
        return new JournalBeginResult(EngineTestFixture.InputDigest);
    }

    public CombatEventIdentity Append(in CombatEventDraft draft)
    {
        _drafts.Add(draft);
        return new CombatEventIdentity(draft.EventId, draft.Sequence);
    }

    public JournalCompletion Complete(in BattleSummary summary)
    {
        CompleteCount++;
        Summary = summary;
        return new JournalCompletion(EngineTestFixture.FinalDigest, null);
    }
}

internal sealed class RecordingTickObserver : ITickCoordinatorObserver
{
    internal List<(int Tick, TickPhase Phase)> Phases { get; } = new();

    internal List<(FighterId FighterId, TickSnapshot Snapshot)> DecisionSnapshots { get; } = new();

    public void OnPhase(BattleState state, TickPhase phase) => Phases.Add((state.Tick, phase));

    public void OnDecisionSnapshot(FighterId fighterId, TickSnapshot snapshot) =>
        DecisionSnapshots.Add((fighterId, snapshot));
}
