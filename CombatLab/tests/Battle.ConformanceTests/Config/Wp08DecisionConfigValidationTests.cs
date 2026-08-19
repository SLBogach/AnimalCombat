using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Config.Semantic;
using Battle.Contracts.Config;

namespace Battle.ConformanceTests.Config;

[Trait("WorkPackage", "WP08")]
public sealed class Wp08DecisionConfigValidationTests
{
    private static readonly string[] RequiredDecisionSettings =
    {
        "global.sim.fp_scale",
        "global.sim.multiplier_min",
        "global.sim.multiplier_max",
        "global.sim.decision_weight_max",
        "global.ai.repeat_same_action_fp",
        "global.ai.repeat_same_category_fp",
        "global.ai.opportunity_growth_fp",
        "global.ai.opportunity_cap_fp",
        "global.ai.hard_opportunity_misses",
        "global.ai.default_perception_delay_ticks",
    };

    public static IEnumerable<object[]> WP08_CFG_002_MissingSettingCases() =>
        RequiredDecisionSettings.Select(key => new object[] { key });

    public static IEnumerable<object[]> WP08_CFG_003_InvalidSettingCases()
    {
        foreach (var key in RequiredDecisionSettings)
        {
            yield return new object[] { key, "1000", ConfigValidationCodes.InvalidInteger };
            yield return new object[] { key, 1.5d, ConfigValidationCodes.InvalidInteger };
            yield return new object[]
            {
                key,
                key == "global.sim.fp_scale" ? 0L : MinimumFor(key) - 1,
                key == "global.sim.fp_scale"
                    ? ConfigValidationCodes.ZeroDivisor
                    : ConfigValidationCodes.NumericOutOfRange,
            };
            yield return new object[] { key, (long)int.MaxValue + 1, ConfigValidationCodes.NumericOutOfRange };
        }
    }

    public static IEnumerable<object[]> WP08_CFG_004_RelationshipCases()
    {
        yield return new object[] { "minimum_above_scale", "$.settings" };
        yield return new object[] { "maximum_below_scale", "$.settings" };
        yield return new object[] { "repeat_action_below_minimum", "$.settings" };
        yield return new object[] { "repeat_category_above_maximum", "$.settings" };
        yield return new object[] { "global_opportunity_above_maximum", "$.settings" };
        yield return new object[] { "tactic_multiplier_above_maximum", "$.tactics[tactic_pressure].light_fp" };
        yield return new object[] { "passive_multiplier_below_minimum", "$.passives[bear_thick_hide].weight_multiplier_fp" };
        yield return new object[] { "gear_multiplier_above_maximum", "$.gear[gear_offense_power_wraps].normalized_value" };
        yield return new object[] { "base_weight_negative", "$.actions[bear_paw_jab].base_weight" };
        yield return new object[] { "base_weight_above_maximum", "$.actions[bear_paw_jab].base_weight" };
        yield return new object[] { "max_consecutive_zero", "$.actions[bear_paw_jab].max_consecutive_uses" };
        yield return new object[] { "action_opportunity_below_scale", "$.actions[bear_paw_jab].opportunity_cap_fp" };
        yield return new object[] { "action_opportunity_above_global", "$.actions[bear_paw_jab].opportunity_cap_fp" };
        yield return new object[] { "action_hard_above_global", "$.actions[bear_earthbreaker].hard_opportunity_misses" };
        yield return new object[] { "tactic_delay_negative", "$.tactics[tactic_pressure].perception_delay_ticks" };
    }

    public static IEnumerable<object[]> WP08_CFG_006_TagCases()
    {
        yield return new object[] { "strike|light|strike" };
        yield return new object[] { "strike|Light" };
        yield return new object[] { "strike||light" };
        yield return new object[] { "strike|light|not-canonical" };
    }

    public static IEnumerable<object[]> WP08_CFG_006_HitScheduleCases()
    {
        yield return new object[] { "duplicate", "0|0", 2, 2 };
        yield return new object[] { "out_of_order", "1|0", 2, 2 };
        yield return new object[] { "outside_active", "1", 1, 1 };
        yield return new object[] { "unknown_primitive", "impact:0", 1, 1 };
        yield return new object[] { "hit_count_mismatch", "0", 2, 1 };
        yield return new object[] { "noncanonical_tick", "00", 1, 1 };
    }

    public static IEnumerable<object[]> WP08_CFG_007_AmbiguousTargetCases()
    {
        yield return new object[] { "bear_guarded_advance", "Follow" };
        yield return new object[] { "bear_paw_jab", "Retreat" };
    }

    [Fact]
    public void WP08_CFG_001_CanonicalDecisionProfilesMaterializeWithoutDefaultsOrWarnings()
    {
        var result = Compile(ConfigFixture.ReadConfigObject());

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Empty(result.Issues);
        var config = Assert.IsType<CompiledBattleConfig>(result.Config);
        Assert.Equal(24, config.Actions.Count);
        Assert.Equal(4, config.Tactics.Count);

        var expected = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["global.sim.fp_scale"] = 1_000,
            ["global.sim.multiplier_min"] = 250,
            ["global.sim.multiplier_max"] = 3_000,
            ["global.sim.decision_weight_max"] = 100_000_000,
            ["global.ai.repeat_same_action_fp"] = 550,
            ["global.ai.repeat_same_category_fp"] = 800,
            ["global.ai.opportunity_growth_fp"] = 250,
            ["global.ai.opportunity_cap_fp"] = 2_500,
            ["global.ai.hard_opportunity_misses"] = 4,
            ["global.ai.default_perception_delay_ticks"] = 5,
        };

        foreach (var item in expected)
        {
            Assert.True(config.TryGetSetting(item.Key, out var value));
            Assert.Equal(item.Value, value.AsInteger());
        }
    }

    [Theory]
    [MemberData(nameof(WP08_CFG_002_MissingSettingCases))]
    public void WP08_CFG_002_EachRequiredSettingMissingAloneHasStableCodeAndPath(string key)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        candidate["settings"]!.AsObject().Remove(key);

        var result = Compile(candidate);

        AssertRejected(result, ConfigValidationCodes.MissingRequiredConfigKey, "$.settings");
        Assert.Contains(result.Issues, issue => issue.Message.Contains("'" + key + "'", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(WP08_CFG_003_InvalidSettingCases))]
    public void WP08_CFG_003_SettingTypesAndIntegerDomainsHaveStableCodeAndPath(
        string key,
        object invalidValue,
        string expectedCode)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        SetValue(candidate["settings"]!.AsObject(), key, invalidValue);

        var result = Compile(candidate);

        AssertRejected(result, expectedCode, "$.settings." + key);
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.MissingRequiredConfigKey &&
                     issue.Message.Contains("'" + key + "'", StringComparison.Ordinal));
    }

    [Fact]
    public void WP08_CFG_003_NonminimalIntegralJsonNumberIsInvalidIntegerAtExactPath()
    {
        var source = Encoding.UTF8.GetString(ConfigFixture.ReadConfigBytes());
        const string original = "\"global.sim.fp_scale\":1000";
        const string replacement = "\"global.sim.fp_scale\":1.0";
        Assert.Contains(original, source, StringComparison.Ordinal);

        var result = new BattleConfigCompiler().Compile(
            Encoding.UTF8.GetBytes(source.Replace(original, replacement, StringComparison.Ordinal)));

        AssertRejected(
            result,
            ConfigValidationCodes.InvalidInteger,
            "$.settings.global.sim.fp_scale");
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.MissingRequiredConfigKey &&
                     issue.Message.Contains("'global.sim.fp_scale'", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(WP08_CFG_004_RelationshipCases))]
    public void WP08_CFG_004_DecisionRelationshipsRejectWithoutRepair(
        string mutation,
        string expectedPath)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        ApplyRelationshipMutation(candidate, mutation);

        var result = Compile(candidate);

        AssertRejected(result, ConfigValidationCodes.NumericOutOfRange, expectedPath);
    }

    [Theory]
    [MemberData(nameof(WP08_CFG_006_TagCases))]
    public void WP08_CFG_006_InvalidOrDuplicateTagTokenHasStableCodeAndPath(string tags)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["tags"] = tags;

        var result = Compile(candidate);

        AssertRejected(
            result,
            ConfigValidationCodes.InvalidTagSet,
            "$.actions[bear_paw_jab].tags");
    }

    [Fact]
    public void WP08_CFG_006_TagTokenOrderDoesNotMakeTheSetInvalid()
    {
        var candidate = ConfigFixture.ReadConfigObject();
        ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["tags"] =
            "light|fallback|strike";

        var result = Compile(candidate);

        Assert.True(result.IsSuccess, Describe(result));
    }

    [Theory]
    [MemberData(nameof(WP08_CFG_006_HitScheduleCases))]
    public void WP08_CFG_006_InvalidHitScheduleHasStableCodeAndPath(
        string _,
        string schedule,
        int hitCount,
        int activeTicks)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        var action = ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab");
        action["hit_schedule"] = schedule;
        action["hit_count"] = hitCount;
        action["active_ticks"] = activeTicks;

        var result = Compile(candidate);

        AssertRejected(
            result,
            ConfigValidationCodes.InvalidHitSchedule,
            "$.actions[bear_paw_jab].hit_schedule");
    }

    [Fact]
    public void WP08_CFG_006_ThirtyThreeImpactsRejectHitCountAndScheduleAtExactPaths()
    {
        var candidate = ConfigFixture.ReadConfigObject();
        var action = ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab");
        action["hit_schedule"] = string.Join("|", Enumerable.Range(0, 33));
        action["hit_count"] = 33;
        action["active_ticks"] = 33;

        var result = Compile(candidate);

        AssertRejected(
            result,
            ConfigValidationCodes.NumericOutOfRange,
            "$.actions[bear_paw_jab].hit_count");
        Assert.Contains(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.InvalidHitSchedule &&
                     issue.Path == "$.actions[bear_paw_jab].hit_schedule");
    }

    [Fact]
    public void WP08_CFG_006_ThirtyThreeGrabEntriesRejectEvenWhenHitCountIsZero()
    {
        var candidate = ConfigFixture.ReadConfigObject();
        var action = ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab");
        action["hit_schedule"] = string.Join(
            "|",
            Enumerable.Range(0, 33).Select(tick => "grab:" + tick));
        action["hit_count"] = 0;
        action["active_ticks"] = 33;

        var result = Compile(candidate);

        AssertRejected(
            result,
            ConfigValidationCodes.InvalidHitSchedule,
            "$.actions[bear_paw_jab].hit_schedule");
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.NumericOutOfRange &&
                     issue.Path == "$.actions[bear_paw_jab].hit_count");
    }

    [Theory]
    [MemberData(nameof(WP08_CFG_007_AmbiguousTargetCases))]
    public void WP08_CFG_007_InferredTargetAndMovementMustBeCompatible(
        string actionId,
        string movementMode)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        ConfigFixture.Entity(candidate, "actions", "action_id", actionId)["movement_mode"] = movementMode;

        var result = Compile(candidate);

        AssertRejected(
            result,
            ConfigValidationCodes.AmbiguousTargetProfile,
            "$.actions[" + actionId + "].movement_mode");
    }

    [Fact]
    public void WP08_CFG_008_ActionHardMissesZeroIsValidAndDisablesTheOverrideInData()
    {
        var candidate = ConfigFixture.ReadConfigObject();
        ConfigFixture.Entity(
            candidate,
            "actions",
            "action_id",
            "bear_earthbreaker")["hard_opportunity_misses"] = 0;

        var result = Compile(candidate);

        Assert.True(result.IsSuccess, Describe(result));
        var action = Assert.IsType<CompiledBattleConfig>(result.Config).Actions
            .Single(item => item.Id.Value == "bear_earthbreaker");
        Assert.True(action.TryGetProperty("hard_opportunity_misses", out var hardMisses));
        Assert.Equal(0, hardMisses.AsInteger());
    }

    [Fact]
    public void WP08_CFG_008_GlobalHardMissesZeroIsValidWhenAllActionsDisableHardOverride()
    {
        var candidate = ConfigFixture.ReadConfigObject();
        candidate["settings"]!.AsObject()["global.ai.hard_opportunity_misses"] = 0;
        foreach (var item in candidate["actions"]!.AsArray())
        {
            item!.AsObject()["hard_opportunity_misses"] = 0;
        }

        var result = Compile(candidate);

        Assert.True(result.IsSuccess, Describe(result));
    }

    [Fact]
    public void DiagnosticCheckedCatalogUsesIts256EntryLimitInsteadOfTheLegal128EntryLimit()
    {
        var withinDiagnosticLimit = ConfigFixture.ReadConfigObject();
        ExpandBearCheckedCatalog(withinDiagnosticLimit, targetCount: 256);

        var accepted = Compile(withinDiagnosticLimit);

        Assert.True(accepted.IsSuccess, Describe(accepted));

        var aboveDiagnosticLimit = ConfigFixture.ReadConfigObject();
        ExpandBearCheckedCatalog(aboveDiagnosticLimit, targetCount: 257);

        var rejected = Compile(aboveDiagnosticLimit);

        AssertRejected(rejected, ConfigValidationCodes.NumericOutOfRange, "$.actions");
    }

    [Fact]
    public void ExtraWellShapedSystemActionIsRejectedWithStableCodeAndPath()
    {
        var candidate = ConfigFixture.ReadConfigObject();
        var actions = candidate["actions"]!.AsArray();
        var extra = ConfigFixture.Entity(candidate, "actions", "action_id", "sys_wait")
            .DeepClone()
            .AsObject();
        extra["action_id"] = "sys_taunt";
        actions.Add(extra);

        var result = Compile(candidate);

        AssertRejected(
            result,
            ConfigValidationCodes.InvalidSystemAction,
            "$.actions[sys_taunt]");
    }

    private static void ExpandBearCheckedCatalog(JsonObject candidate, int targetCount)
    {
        var actions = candidate["actions"]!.AsArray();
        var template = ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab");
        var existingCount = actions.Count(item =>
        {
            var action = item!.AsObject();
            return action["slot_type"]!.GetValue<string>() == "System" ||
                   action["animal_id"]!.GetValue<string>() == "bear";
        });

        for (var index = existingCount; index < targetCount; index++)
        {
            var clone = template.DeepClone().AsObject();
            clone["action_id"] = $"bear_catalog_probe_{index:D3}";
            actions.Add(clone);
        }
    }

    private static long MinimumFor(string key) => key switch
    {
        "global.sim.fp_scale" => 1,
        "global.sim.multiplier_max" => 1,
        "global.sim.decision_weight_max" => 1,
        "global.ai.opportunity_cap_fp" => 1,
        _ => 0,
    };

    private static void ApplyRelationshipMutation(JsonObject candidate, string mutation)
    {
        var settings = candidate["settings"]!.AsObject();
        switch (mutation)
        {
            case "minimum_above_scale":
                settings["global.sim.multiplier_min"] = 1_001;
                break;
            case "maximum_below_scale":
                settings["global.sim.multiplier_max"] = 999;
                break;
            case "repeat_action_below_minimum":
                settings["global.ai.repeat_same_action_fp"] = 249;
                break;
            case "repeat_category_above_maximum":
                settings["global.ai.repeat_same_category_fp"] = 3_001;
                break;
            case "global_opportunity_above_maximum":
                settings["global.ai.opportunity_cap_fp"] = 3_001;
                break;
            case "tactic_multiplier_above_maximum":
                ConfigFixture.Entity(candidate, "tactics", "tactic_id", "tactic_pressure")["light_fp"] = 3_001;
                break;
            case "passive_multiplier_below_minimum":
                ConfigFixture.Entity(candidate, "passives", "passive_id", "bear_thick_hide")["weight_multiplier_fp"] = 249;
                break;
            case "gear_multiplier_above_maximum":
                ConfigFixture.Entity(candidate, "gear", "gear_id", "gear_offense_power_wraps")["normalized_value"] = 3_001;
                break;
            case "base_weight_negative":
                ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["base_weight"] = -1;
                break;
            case "base_weight_above_maximum":
                ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["base_weight"] = 100_000_001;
                break;
            case "max_consecutive_zero":
                ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["max_consecutive_uses"] = 0;
                break;
            case "action_opportunity_below_scale":
                ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["opportunity_cap_fp"] = 999;
                break;
            case "action_opportunity_above_global":
                ConfigFixture.Entity(candidate, "actions", "action_id", "bear_paw_jab")["opportunity_cap_fp"] = 2_501;
                break;
            case "action_hard_above_global":
                ConfigFixture.Entity(candidate, "actions", "action_id", "bear_earthbreaker")["hard_opportunity_misses"] = 5;
                break;
            case "tactic_delay_negative":
                ConfigFixture.Entity(candidate, "tactics", "tactic_id", "tactic_pressure")["perception_delay_ticks"] = -1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown WP-08 relationship mutation.");
        }
    }

    private static void SetValue(JsonObject target, string key, object value)
    {
        target[key] = value switch
        {
            long integer => JsonValue.Create(integer),
            double number => JsonValue.Create(number),
            string text => JsonValue.Create(text),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported JSON test value."),
        };
    }

    private static ConfigCompilationResult Compile(JsonObject candidate) =>
        new BattleConfigCompiler().Compile(Encoding.UTF8.GetBytes(candidate.ToJsonString()));

    private static void AssertRejected(
        ConfigCompilationResult result,
        string expectedCode,
        string expectedPath)
    {
        Assert.False(result.IsSuccess, Describe(result));
        Assert.Null(result.Config);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == expectedCode && issue.Path == expectedPath);
    }

    private static string Describe(ConfigCompilationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Issues.Select(issue => issue.Code + " " + issue.Path + ": " + issue.Message));
}
