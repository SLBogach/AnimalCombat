using Battle.Core;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Engine;

public sealed class MovementSetupValidationTests
{
    [Theory]
    [InlineData("missing_speed")]
    [InlineData("missing_radius")]
    [InlineData("wrong_speed_type")]
    [InlineData("wrong_radius_type")]
    [InlineData("zero_speed")]
    [InlineData("zero_radius")]
    [InlineData("raw_speed_overflow")]
    [InlineData("raw_radius_overflow")]
    [InlineData("derived_speed_nonpositive")]
    [InlineData("derived_speed_overflow")]
    [InlineData("derived_radius_nonpositive")]
    [InlineData("derived_radius_overflow")]
    public void WP07_VAL_001_InvalidMovementStatsAreRejectedBeforeBegin(string mutation)
    {
        var config = EngineTestFixture.CreateConfig(
            changeFighters: fighters => EngineTestFixture.ReindexCatalog(fighters.Select(fighter =>
                fighter.Id.Value != "bear"
                    ? fighter
                    : mutation switch
                    {
                        "missing_speed" => WithoutProperty(fighter, "move_speed"),
                        "missing_radius" => WithoutProperty(fighter, "collision_radius"),
                        "wrong_speed_type" => EngineTestFixture.WithProperty(
                            fighter,
                            "move_speed",
                            ConfigValue.FromString("70")),
                        "wrong_radius_type" => EngineTestFixture.WithProperty(
                            fighter,
                            "collision_radius",
                            ConfigValue.FromString("520")),
                        "zero_speed" => EngineTestFixture.WithProperty(
                            fighter,
                            "move_speed",
                            ConfigValue.FromInteger(0)),
                        "zero_radius" => EngineTestFixture.WithProperty(
                            fighter,
                            "collision_radius",
                            ConfigValue.FromInteger(0)),
                        "raw_speed_overflow" => EngineTestFixture.WithProperty(
                            fighter,
                            "move_speed",
                            ConfigValue.FromInteger(long.MaxValue)),
                        "raw_radius_overflow" => EngineTestFixture.WithProperty(
                            fighter,
                            "collision_radius",
                            ConfigValue.FromInteger(long.MaxValue)),
                        _ => fighter,
                    })),
            changeGear: mutation.StartsWith("derived_", StringComparison.Ordinal)
                ? gear => EngineTestFixture.ReindexCatalog(gear.Select(item =>
                    item.Id.Value == "gear_utility_sprint_soles"
                        ? mutation switch
                        {
                            "derived_speed_nonpositive" => EngineTestFixture.WithProperty(
                                item,
                                "value1",
                                ConfigValue.FromInteger(-1_000)),
                            "derived_speed_overflow" => EngineTestFixture.WithProperty(
                                item,
                                "value1",
                                ConfigValue.FromInteger(int.MaxValue)),
                            "derived_radius_nonpositive" => EngineTestFixture.WithProperty(
                                EngineTestFixture.WithProperty(
                                    item,
                                    "stat1",
                                    ConfigValue.FromString("CollisionRadius")),
                                "value1",
                                ConfigValue.FromInteger(-1_000)),
                            "derived_radius_overflow" => EngineTestFixture.WithProperty(
                                EngineTestFixture.WithProperty(
                                    item,
                                    "stat1",
                                    ConfigValue.FromString("CollisionRadius")),
                                "value1",
                                ConfigValue.FromInteger(int.MaxValue)),
                            _ => item,
                        }
                        : item))
                : null);

        AssertRejectedBeforeBegin(config);
    }

    [Theory]
    [InlineData("body_bound")]
    [InlineData("overlap")]
    [InlineData("equal")]
    [InlineData("impossible_arena")]
    [InlineData("reversed_arena")]
    [InlineData("crossed")]
    [InlineData("center_overflow")]
    [InlineData("radius_sum_overflow")]
    public void WP07_VAL_002_InvalidBodyAwareArenaGeometryIsRejectedAndSorted(string mutation)
    {
        var config = EngineTestFixture.CreateConfig(changeSettings: settings => settings.Select(property =>
        {
            if (mutation == "body_bound" && property.Name == "global.arena.start_position_a")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(519));
            }

            if (mutation == "overlap" && property.Name == "global.arena.start_position_b")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(2_800));
            }

            if (mutation == "equal" && property.Name == "global.arena.start_position_b")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(2_000));
            }

            if (mutation == "impossible_arena" && property.Name == "global.arena.max_position")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(900));
            }

            if (mutation == "reversed_arena" && property.Name == "global.arena.max_position")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(0));
            }

            if (mutation == "crossed" && property.Name == "global.arena.start_position_b")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(1_000));
            }

            if (mutation == "center_overflow" && property.Name == "global.arena.min_position")
            {
                return new ConfigProperty(property.Name, ConfigValue.FromInteger(1));
            }

            if (mutation == "radius_sum_overflow")
            {
                return property.Name switch
                {
                    "global.arena.min_position" => new ConfigProperty(
                        property.Name,
                        ConfigValue.FromInteger(-2_147_483_647)),
                    "global.arena.max_position" => new ConfigProperty(
                        property.Name,
                        ConfigValue.FromInteger(2_147_483_647)),
                    "global.arena.start_position_a" => new ConfigProperty(
                        property.Name,
                        ConfigValue.FromInteger(-900_000_000)),
                    "global.arena.start_position_b" => new ConfigProperty(
                        property.Name,
                        ConfigValue.FromInteger(900_000_000)),
                    _ => property,
                };
            }

            return property;
        }), changeFighters: mutation is "center_overflow" or "radius_sum_overflow"
            ? fighters => EngineTestFixture.ReindexCatalog(fighters.Select(fighter =>
                EngineTestFixture.WithProperty(
                    fighter,
                    "collision_radius",
                    ConfigValue.FromInteger(
                        mutation == "center_overflow" && fighter.Id.Value == "bear"
                            ? int.MaxValue
                            : mutation == "radius_sum_overflow"
                                ? 1_200_000_000
                                : fighter.Properties
                                    .Single(property => property.Name == "collision_radius")
                                    .Value.AsInteger()))))
            : null);
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(EngineTestFixture.CreateRequest(), config, journal);

        AssertRejected(result, journal);
        var sorted = result.RejectionErrors
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code.Value, StringComparer.Ordinal)
            .ThenBy(error => error.EntityId?.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(sorted, result.RejectionErrors);
    }

    [Theory]
    [InlineData("global.arena.min_position", "missing")]
    [InlineData("global.arena.max_position", "missing")]
    [InlineData("global.arena.start_position_a", "missing")]
    [InlineData("global.arena.start_position_b", "missing")]
    [InlineData("global.arena.min_position", "wrong_type")]
    [InlineData("global.arena.max_position", "wrong_type")]
    [InlineData("global.arena.start_position_a", "wrong_type")]
    [InlineData("global.arena.start_position_b", "wrong_type")]
    [InlineData("global.arena.min_position", "raw_range")]
    [InlineData("global.arena.max_position", "raw_range")]
    [InlineData("global.arena.start_position_a", "raw_range")]
    [InlineData("global.arena.start_position_b", "raw_range")]
    public void WP07_VAL_002_RequiredArenaSettingsRejectMissingTypeAndRawRange(
        string setting,
        string mutation)
    {
        var config = EngineTestFixture.CreateConfig(changeSettings: settings => settings
            .Where(property => mutation != "missing" || property.Name != setting)
            .Select(property => property.Name != setting
                ? property
                : new ConfigProperty(
                    property.Name,
                    mutation == "wrong_type"
                        ? ConfigValue.FromString("invalid")
                        : ConfigValue.FromInteger(long.MaxValue))));

        AssertRejectedBeforeBegin(config);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong_owner")]
    [InlineData("wrong_slot")]
    [InlineData("wrong_category")]
    [InlineData("wrong_mode")]
    [InlineData("wrong_track")]
    [InlineData("zero_weight")]
    [InlineData("nonzero_cost")]
    [InlineData("nonzero_resource")]
    [InlineData("nonzero_cooldown")]
    [InlineData("nonzero_move_distance")]
    [InlineData("zero_active")]
    [InlineData("timing_mismatch")]
    [InlineData("recovery_mismatch")]
    [InlineData("invalid_range")]
    [InlineData("combat_hit")]
    [InlineData("combat_boolean")]
    public void WP07_VAL_003_InvalidSystemMovementShapeIsRejectedBeforeBegin(string mutation)
    {
        var config = EngineTestFixture.CreateConfig(changeActions: actions =>
        {
            var source = mutation == "missing"
                ? actions.Where(action => action.Id.Value != "sys_approach")
                : actions.Select(action => action.Id.Value != "sys_approach"
                    ? action
                    : mutation switch
                    {
                        "wrong_owner" => EngineTestFixture.WithProperty(
                            action,
                            "animal_id",
                            ConfigValue.FromString("bear")),
                        "wrong_slot" => EngineTestFixture.WithProperty(
                            action,
                            "slot_type",
                            ConfigValue.FromString("Basic")),
                        "wrong_category" => EngineTestFixture.WithProperty(
                            action,
                            "category",
                            ConfigValue.FromString("Wait")),
                        "wrong_mode" => EngineTestFixture.WithProperty(
                            action,
                            "movement_mode",
                            ConfigValue.FromString("Retreat")),
                        "wrong_track" => EngineTestFixture.WithProperty(
                            action,
                            "track_target",
                            ConfigValue.FromBoolean(false)),
                        "zero_weight" => EngineTestFixture.WithProperty(
                            action,
                            "base_weight",
                            ConfigValue.FromInteger(0)),
                        "nonzero_cost" => EngineTestFixture.WithProperty(
                            action,
                            "energy_cost",
                            ConfigValue.FromInteger(1)),
                        "nonzero_resource" => EngineTestFixture.WithProperty(
                            action,
                            "resource_cost",
                            ConfigValue.FromInteger(1)),
                        "nonzero_cooldown" => EngineTestFixture.WithProperty(
                            action,
                            "cooldown_ticks",
                            ConfigValue.FromInteger(1)),
                        "nonzero_move_distance" => EngineTestFixture.WithProperty(
                            action,
                            "move_distance",
                            ConfigValue.FromInteger(1)),
                        "zero_active" => EngineTestFixture.WithProperty(
                            action,
                            "active_ticks",
                            ConfigValue.FromInteger(0)),
                        "timing_mismatch" => EngineTestFixture.WithProperty(
                            action,
                            "startup_max_ticks",
                            ConfigValue.FromInteger(2)),
                        "recovery_mismatch" => EngineTestFixture.WithProperty(
                            action,
                            "recovery_max_ticks",
                            ConfigValue.FromInteger(2)),
                        "invalid_range" => EngineTestFixture.WithProperty(
                            action,
                            "preferred_range_min",
                            ConfigValue.FromInteger(1_501)),
                        "combat_hit" => EngineTestFixture.WithProperty(
                            action,
                            "hit_schedule",
                            ConfigValue.FromString("0")),
                        "combat_boolean" => EngineTestFixture.WithProperty(
                            action,
                            "blockable",
                            ConfigValue.FromBoolean(true)),
                        _ => action,
                    });
            return EngineTestFixture.ReindexCatalog(source);
        });

        AssertRejectedBeforeBegin(config);
    }

    public static IEnumerable<object[]> ZeroOnlyCombatIntegerFields() =>
        new[]
        {
            "base_damage",
            "base_knockback",
            "base_stagger",
            "base_stun_ticks",
            "block_base_chance_fp",
            "block_reduction_fp",
            "chip_min",
            "clash_priority",
            "dodge_base_chance_fp",
            "grab_priority",
            "hit_count",
            "hit_range_min",
            "hit_range_max",
            "knockback_min",
            "knockback_max",
            "min_damage",
            "move_distance",
            "power_ratio_fp",
            "wall_damage_min",
            "wall_damage_max",
            "wall_damage_per_unit_fp",
        }.Select(field => new object[] { field });

    [Theory]
    [MemberData(nameof(ZeroOnlyCombatIntegerFields))]
    public void WP07_VAL_003_AllCombatIntegerFieldsMustRemainZero(string field)
    {
        var config = MutateApproach(action => EngineTestFixture.WithProperty(
            action,
            field,
            ConfigValue.FromInteger(1)));

        AssertRejectedBeforeBegin(config);
    }

    [Theory]
    [InlineData("blockable")]
    [InlineData("dodgeable")]
    [InlineData("undodgeable")]
    [InlineData("wall_impact")]
    public void WP07_VAL_003_AllCombatBooleanFieldsMustRemainFalse(string field)
    {
        var config = MutateApproach(action => EngineTestFixture.WithProperty(
            action,
            field,
            ConfigValue.FromBoolean(true)));

        AssertRejectedBeforeBegin(config);
    }

    [Fact]
    public void WP07_VAL_004_OverlappingNeutralBandDefinitionsAreRejected()
    {
        var config = EngineTestFixture.CreateConfig(changeActions: actions =>
            EngineTestFixture.ReindexCatalog(actions.Select(action =>
                action.Id.Value == "sys_approach"
                    ? EngineTestFixture.WithProperty(
                        action,
                        "preferred_range_max",
                        ConfigValue.FromInteger(1_700))
                    : action)));

        var result = AssertRejectedBeforeBegin(config);

        Assert.Contains(result.RejectionErrors, error => error.Code.Value == "InvalidNeutralBand");
    }

    [Fact]
    public void WP07_VAL_005_MathScaleAliasDoesNotReplaceRequiredFpScale()
    {
        var config = EngineTestFixture.CreateConfig(changeSettings: settings => settings
            .Where(property => property.Name != "global.sim.fp_scale")
            .Append(new ConfigProperty("global.sim.math_scale", ConfigValue.FromInteger(1_000)))
            .OrderBy(property => property.Name, StringComparer.Ordinal));

        var result = AssertRejectedBeforeBegin(config);

        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "MissingRequiredConfigKey" &&
                     error.Path.EndsWith("global.sim.fp_scale", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wrong_type")]
    [InlineData("zero")]
    [InlineData("raw_range")]
    public void WP07_VAL_005_FpScaleRejectsWrongTypeZeroAndRawRange(string mutation)
    {
        var config = EngineTestFixture.CreateConfig(changeSettings: settings => settings.Select(property =>
            property.Name != "global.sim.fp_scale"
                ? property
                : new ConfigProperty(
                    property.Name,
                    mutation switch
                    {
                        "wrong_type" => ConfigValue.FromString("1000"),
                        "zero" => ConfigValue.FromInteger(0),
                        _ => ConfigValue.FromInteger(long.MaxValue),
                    })));

        AssertRejectedBeforeBegin(config);
    }

    [Theory]
    [InlineData("battle.core/0.1.0")]
    [InlineData("battle.core/9.9.9")]
    public void WP07_VAL_006_CurrentEngineIs020AndOldOrUnknownRequestsAreRejected(
        string engineVersion)
    {
        Assert.Equal("battle.core/0.2.0", ContractVersions.Engine.ToString());
        var journal = new RecordingJournal();
        var request = EngineTestFixture.CreateRequest(
            engineVersion: new ArtifactVersion(engineVersion));

        var result = new CombatEngine().Simulate(request, EngineTestFixture.CreateConfig(), journal);

        AssertRejected(result, journal);
        Assert.Contains(result.RejectionErrors, error => error.Code.Value == "EngineVersionMismatch");
    }

    private static CompiledBattleConfig MutateApproach(
        Func<CompiledConfigEntity, CompiledConfigEntity> mutation) =>
        EngineTestFixture.CreateConfig(changeActions: actions =>
            EngineTestFixture.ReindexCatalog(actions.Select(action =>
                action.Id.Value == "sys_approach" ? mutation(action) : action)));

    private static BattleResult AssertRejectedBeforeBegin(CompiledBattleConfig config)
    {
        var journal = new RecordingJournal();
        var result = new CombatEngine().Simulate(EngineTestFixture.CreateRequest(), config, journal);
        AssertRejected(result, journal);
        return result;
    }

    private static void AssertRejected(BattleResult result, RecordingJournal journal)
    {
        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Equal(0, journal.BeginCount);
        Assert.Equal(0, journal.CompleteCount);
        Assert.Empty(journal.Drafts);
    }

    private static CompiledConfigEntity WithoutProperty(CompiledConfigEntity entity, string name) =>
        new(
            entity.Id,
            entity.DenseHandle,
            entity.Properties.Where(property => property.Name != name));
}
