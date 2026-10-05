using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Config;
using Battle.Config.Compiler;
using Battle.Config.Manifest;
using Battle.Config.Schema;
using Battle.Config.Semantic;
using Battle.ConformanceTests.Config;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10ConfigFoundationTests
{
    [Fact]
    public void SyntheticV02CompilesWithoutWarningsAndLegacyV01StillLoads()
    {
        var result = Compile(Wp10ConfigFixture.Create());
        Assert.True(result.IsSuccess, Describe(result));
        Assert.Empty(result.Issues);
        Assert.Equal("combat.balance/0.2", result.Config!.Reference.BalanceSchemaVersion.ToString());
        Assert.Equal("v0.2", result.Config.Reference.ConfigVersion.ToString());
        Assert.Equal(5, result.Config.EffectRules.Count);
        Assert.True(result.Config.TryGetEffectRule(new StableId("rule_guard_broken"), out var rule));
        Assert.True(rule!.TryGetProperty("owner_id", out var owner));
        Assert.Equal("global", owner.AsString());
        Assert.True(new BattleConfigLoader().Load(ConfigFixture.ReadConfigBytes(), ConfigFixture.ReadManifestBytes()).IsSuccess);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-DATA-001")]
    public void EveryNewRequiredSettingRejectsOmissionWithoutDefaults()
    {
        var keys = Wp10ConfigFixture.Bounds.SelectMany(x => new[] { "stat." + x.Stat + ".min", "stat." + x.Stat + ".max" })
            .Concat(new[] { "max_trigger_depth", "max_triggers_per_tick", "max_effect_instances_per_fighter",
                "knockdown_fall_ticks", "knockdown_grounded_ticks", "knockdown_getup_ticks" }.Select(x => "global.control." + x)).ToArray();
        Assert.Equal(36, keys.Length);
        foreach (var key in keys)
        {
            var root = Wp10ConfigFixture.Create();
            Assert.True(root["settings"]!.AsObject().Remove(key));
            var result = Compile(root);
            Assert.False(result.IsSuccess, key);
            Assert.Null(result.Config);
            Assert.Contains(result.Issues, x => x.Code == (key.StartsWith("stat.", StringComparison.Ordinal)
                ? ConfigValidationCodes.MissingStatBounds : ConfigValidationCodes.MissingRequiredConfigKey));
        }
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-BASE-002")]
    public void RuleCatalogCopiesInputRejectsDuplicateAndUnknownOwnerAndPreservesLegacyConstruction()
    {
        var config = Compile(Wp10ConfigFixture.Create()).Config!;
        var list = config.EffectRules.ToList();
        var copy = new CompiledBattleConfig(config.Reference, config.Settings, config.Fighters, config.Actions,
            config.Passives, config.Effects, config.Tactics, config.Gear, list);
        list.Clear();
        Assert.Equal(5, copy.EffectRules.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<CompiledConfigEntity>)copy.EffectRules).Clear());
        var legacy = new CompiledBattleConfig(config.Reference, config.Settings, config.Fighters, config.Actions,
            config.Passives, config.Effects, config.Tactics, config.Gear);
        Assert.Empty(legacy.EffectRules);
        Assert.False(legacy.TryGetEffectRule(new StableId("rule_guard_broken"), out _));
        Assert.Throws<ArgumentException>(() => new CompiledBattleConfig(config.Reference, config.Settings,
            config.Fighters, config.Actions, config.Passives, config.Effects, config.Tactics, config.Gear,
            new[] { config.EffectRules[0], config.EffectRules[0] }));
        Assert.Throws<ArgumentNullException>(() => new CompiledBattleConfig(config.Reference, config.Settings,
            config.Fighters, config.Actions, config.Passives, config.Effects, config.Tactics, config.Gear, null!));
        var root = Wp10ConfigFixture.Create();
        root["effect_rules"]!.AsArray().Add(root["effect_rules"]![0]!.DeepClone());
        Reject(root, ConfigValidationCodes.DuplicateStableId);
        foreach (var kind in new[] { "Global", "Action", "Effect" })
        {
            root = Wp10ConfigFixture.Create();
            root["effect_rules"]![0]!["owner_kind"] = kind;
            root["effect_rules"]![0]!["owner_id"] = "missing_owner";
            Reject(root, ConfigValidationCodes.InvalidEffectRule);
        }
    }

    [Theory]
    [InlineData("stat.guard.min", 1001, "InvalidStatBounds")]
    [InlineData("stat.move_speed.min", 0, "InvalidStatBounds")]
    [InlineData("stat.mass.max", 2001, "InvalidStatBounds")]
    [InlineData("global.control.max_trigger_depth", 0, "NumericOutOfRange")]
    [InlineData("global.control.max_trigger_depth", 33, "NumericOutOfRange")]
    [InlineData("global.control.max_triggers_per_tick", 4097, "NumericOutOfRange")]
    [InlineData("global.control.max_effect_instances_per_fighter", 129, "NumericOutOfRange")]
    [InlineData("global.control.knockdown_getup_ticks", 0, "NumericOutOfRange")]
    public void NewSettingsUseExplicitDomains(string key, int value, string code)
    {
        var root = Wp10ConfigFixture.Create();
        root["settings"]![key] = value;
        Reject(root, code);
    }

    [Theory]
    [InlineData("duration_ticks", 0, "InvalidDuration")]
    [InlineData("stack_cap", 0, "InvalidEffectGroup")]
    [InlineData("stack_cap", 256, "InvalidEffectGroup")]
    [InlineData("max_activations_per_tick", 0, "InvalidEffectRule")]
    [InlineData("internal_cooldown_ticks", -1, "InvalidEffectRule")]
    public void EffectIntegerDomainsRejectInvalidValues(string key, int value, string code)
    {
        var root = Wp10ConfigFixture.Create();
        Wp10ConfigFixture.Effect(root, "effect_guard_broken")[key] = value;
        Reject(root, code);
    }

    [Theory]
    [InlineData("trigger", "Unknown")]
    [InlineData("condition", "Unknown")]
    [InlineData("recipient", "Unknown")]
    [InlineData("primitive", "Unknown")]
    [InlineData("owner_kind", "global")]
    public void RulesUseClosedCaseSensitiveVocabulary(string key, string value)
    {
        var root = Wp10ConfigFixture.Create();
        root["effect_rules"]![0]![key] = value;
        Reject(root, ConfigValidationCodes.InvalidEnumValue);
    }

    [Fact]
    public void MissingRulesUnknownReferencesAndFalseOnceRejectDeterministically()
    {
        var root = Wp10ConfigFixture.Create();
        root.Remove("effect_rules");
        Reject(root, ConfigValidationCodes.MissingRequiredConfigKey);
        root = Wp10ConfigFixture.Create();
        root["effect_rules"]![0]!["effect_id"] = "missing_effect";
        root["effect_rules"]![1]!["once_per_event"] = false;
        var result = Compile(root);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, x => x.Code == ConfigValidationCodes.InvalidEffectReference);
        Assert.Contains(result.Issues, x => x.Code == ConfigValidationCodes.InvalidEffectRule);
        Assert.Equal(result.Issues.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal), result.Issues);
    }

    [Theory]
    [InlineData("MaxHealth", "Add", 10)]
    [InlineData("MaxEnergy", "Add", 10)]
    [InlineData("CollisionRadius", "Add", 10)]
    [InlineData("StaggerThreshold", "Add", 10)]
    [InlineData("Unknown", "Add", 10)]
    [InlineData("GrabAllowed", "Multiply", 0)]
    [InlineData("GrabAllowed", "Override", 2)]
    [InlineData("BlockWeight", "Add", 650)]
    public void ForbiddenTargetsAndOperationsReject(string target, string operation, int value)
    {
        var root = Wp10ConfigFixture.Create();
        var effect = Wp10ConfigFixture.Effect(root, "effect_guard_broken");
        effect["modifier_stat1"] = target;
        effect["operation1"] = operation;
        effect["value1"] = value;
        Reject(root, ConfigValidationCodes.UnsupportedEffectModifierTarget);
    }

    [Fact]
    public void DormantResourceChannelIsAllowedButItsReachableActivationRejects()
    {
        var root = Wp10ConfigFixture.Create();
        Assert.True(Compile(root).IsSuccess);
        root["effect_rules"]!.AsArray().Add(Wp10ConfigFixture.Rule("rule_dormant", "BattleStart", "effect_gorilla_wall_pressure"));
        Reject(root, ConfigValidationCodes.UnsupportedEffectModifierTarget);
        root = Wp10ConfigFixture.Create();
        var rule = Wp10ConfigFixture.Rule("rule_dormant", "EffectAdded", "effect_gorilla_wall_pressure");
        rule["owner_kind"] = "Effect";
        rule["owner_id"] = "effect_exposed";
        root["effect_rules"]!.AsArray().Add(rule);
        Assert.True(Compile(root).IsSuccess);
        root["effect_rules"]!.AsArray().Add(Wp10ConfigFixture.Rule("rule_exposed", "BattleStart", "effect_exposed"));
        Reject(root, ConfigValidationCodes.UnsupportedEffectModifierTarget);
    }

    [Fact]
    public void FatigueLookupRolesAndDurationBindingsAreValidated()
    {
        var root = Wp10ConfigFixture.Create();
        Wp10ConfigFixture.Effect(root, "effect_control_fatigue")["lookup_profile"] = "1000|750|500";
        Reject(root, ConfigValidationCodes.InvalidEffectLookup);
        root = Wp10ConfigFixture.Create();
        Wp10ConfigFixture.Effect(root, "effect_exposed")["semantic_role"] = "ControlImmunity";
        Reject(root, ConfigValidationCodes.InvalidEffectReference);
        root = Wp10ConfigFixture.Create();
        Wp10ConfigFixture.Effect(root, "effect_control_immunity")["duration_ticks"] = 26;
        Reject(root, ConfigValidationCodes.InvalidDuration);
    }

    [Fact]
    public void GroupsAndInterruptListsRejectAmbiguity()
    {
        var root = Wp10ConfigFixture.Create();
        Wp10ConfigFixture.Effect(root, "effect_bear_thick_hide")["compare_key"] = "";
        Reject(root, ConfigValidationCodes.InvalidEffectGroup);
        root = Wp10ConfigFixture.Create();
        Wp10ConfigFixture.Effect(root, "effect_exposed")["stack_group"] = Wp10ConfigFixture.Effect(root, "effect_guard_broken")["stack_group"]!.DeepClone();
        Reject(root, ConfigValidationCodes.InvalidEffectGroup);
        foreach (var tokens in new[] { "Startup|Startup", "Startup|Unknown", "1", "Startup|" })
        {
            root = Wp10ConfigFixture.Create();
            root["actions"]![0]!["hit_interruptible_phases"] = tokens;
            Reject(root, ConfigValidationCodes.InvalidInterruptProfile);
        }
        root = Wp10ConfigFixture.Create();
        root["actions"]![0]!["ignored_control_categories"] = "Grab";
        Reject(root, ConfigValidationCodes.InvalidInterruptProfile);
    }

    [Fact]
    public void ManifestIncludesAndVerifiesRuleCountsOnlyForV02()
    {
        var result = Compile(Wp10ConfigFixture.Create());
        var config = result.Config!;
        var counts = new ConfigEntityCounts(config.Fighters.Count, config.Actions.Count, config.Passives.Count,
            config.Effects.Count, config.Tactics.Count, config.Gear.Count, 12, config.EffectRules.Count);
        var manifest = ConfigManifest.Create(config.Reference, new Sha256Digest("sha256:" + new string('a', 64)),
            "test", DateTimeOffset.Parse("2026-10-03T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), counts, 0);
        var bytes = ConfigManifestJson.Write(manifest);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(5, json.RootElement.GetProperty("entity_counts").GetProperty("effect_rules").GetInt32());
        Assert.True(new BattleConfigLoader().Load(result.GetCanonicalJson(), bytes).IsSuccess);
        var mutated = JsonNode.Parse(bytes)!.AsObject();
        mutated["entity_counts"]!["effect_rules"] = 4;
        var load = new BattleConfigLoader().Load(result.GetCanonicalJson(), Wp10ConfigFixture.Bytes(mutated));
        Assert.False(load.IsSuccess);
        Assert.Contains(load.Issues, x => x.Code == ConfigValidationCodes.ManifestMismatch);
        using var old = JsonDocument.Parse(ConfigFixture.ReadManifestBytes());
        Assert.False(old.RootElement.GetProperty("entity_counts").TryGetProperty("effect_rules", out _));
    }

    [Fact]
    public void V02SchemaIsSeparateAndCanonicalCatalogOrderIsPermutationIndependent()
    {
        using var schema = JsonDocument.Parse(BalanceSchemaJson.Write("combat.balance/0.2"));
        Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("effect_rules", out _));
        Assert.Throws<ArgumentException>(() => BalanceSchemaJson.Write("combat.balance/999"));
        var root = Wp10ConfigFixture.Create();
        var expected = Compile(root);
        var rules = root["effect_rules"]!.AsArray().Reverse().Select(x => x!.DeepClone()).ToArray();
        root["effect_rules"] = new JsonArray(rules);
        var actual = Compile(root);
        Assert.Equal(expected.GetCanonicalJson(), actual.GetCanonicalJson());
        Assert.Equal(expected.ConfigHash, actual.ConfigHash);
        root["settings"]!["global.sim.config_version"] = "v0.1";
        Reject(root, ConfigValidationCodes.InvalidEnumValue);
    }

    private static Battle.Config.Compiler.ConfigCompilationResult Compile(JsonObject root) =>
        new BattleConfigCompiler().Compile(Wp10ConfigFixture.Bytes(root));

    private static void Reject(JsonObject root, string code)
    {
        var result = Compile(root);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Config);
        Assert.Contains(result.Issues, x => x.Code == code);
    }

    private static string Describe(Battle.Config.Compiler.ConfigCompilationResult result) =>
        string.Join(Environment.NewLine, result.Issues.Select(x => x.Code + " " + x.Path + ": " + x.Message));
}
