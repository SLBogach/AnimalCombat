using Battle.Contracts.Config;
using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Effects;

namespace Battle.Core.UnitTests.Effects;

// Pure materializer assertions; versioned Simulate pre-Begin assertions live in EffectVersionedSetupTests.
[Trait("WorkPackage", "WP10")]
public sealed class EffectSetupMaterializerTests
{
    private static EffectRuntimeDefinition Create(CompiledBattleConfig config)
    {
        var issues = new List<EffectSetupIssue>();
        var result = EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(config), config, issues);
        Assert.Empty(issues);
        return Assert.IsType<EffectRuntimeDefinition>(result);
    }
    private static IReadOnlyList<EffectSetupIssue> Reject(CompiledBattleConfig config, string code, string path)
    {
        var issues = new List<EffectSetupIssue>();
        Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(config), config, issues));
        Assert.Contains(issues, x => x.Code == code && x.Path == path);
        Assert.Equal(issues.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Entity, StringComparer.Ordinal).Distinct(), issues);
        return issues;
    }

    [Fact]
    public void PhysicalV02MaterializesImmutableTypedProfilesBoundsRulesAndStaticGearForBothFighters()
    {
        var config = EffectSetupFixture.Config();
        var definition = Create(config);
        Assert.Equal(15, definition.Bounds.Count);
        Assert.Equal(5, definition.Effects.Count);
        Assert.Equal(5, definition.Rules.Count);
        Assert.Equal(24, definition.Interrupts.Count);
        Assert.Equal(2, definition.Fighters.Count);
        Assert.Equal(8, definition.MaximumTriggerDepth); Assert.Equal(128, definition.MaximumTriggersPerTick);
        Assert.Equal(32, definition.MaximumInstancesPerFighter);
        var bear = definition.Fighters.Single(x => x.Fighter == FighterId.FighterA);
        Assert.Equal(1650, bear.InitialStats["MaxHealth"]);
        Assert.Equal(93, bear.InitialStats["Armor"]);
        Assert.Equal(142, bear.InitialStats["Power"]);
        Assert.Equal(82, bear.InitialStats["MoveSpeed"]);
        Assert.Equal(75, bear.BaseStats["Armor"]);
        Assert.DoesNotContain(definition.Effects, x => x.EffectId.Value == "effect_gorilla_wall_pressure");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)bear.BaseStats).Add("Injected", 1));
        Assert.Throws<NotSupportedException>(() => ((IList<EffectProfile>)definition.Effects).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, StatBounds>)definition.Bounds).Clear());
        var repeated = Create(config);
        Assert.NotSame(definition, repeated); Assert.NotSame(definition.Fighters[0], repeated.Fighters[0]);
        Assert.Equal(bear.InitialStats, repeated.Fighters[0].InitialStats);
    }

    [Fact]
    public void EveryRequiredBoundAndQueueKnockdownKeyIsRequiredWithoutDefaultOrException()
    {
        var config = EffectSetupFixture.Config();
        foreach (var stat in EffectSetupMaterializer.StatDomains())
            foreach (var bound in new[] { "min", "max" })
            {
                var key = "stat." + stat.Data + "." + bound;
                Reject(EffectSetupFixture.Change(config, settings: config.Settings.Where(x => x.Name != key)),
                    "MissingStatBounds", "/config/settings/" + key);
            }
        foreach (var name in new[] { "max_trigger_depth", "max_triggers_per_tick", "max_effect_instances_per_fighter",
            "knockdown_fall_ticks", "knockdown_grounded_ticks", "knockdown_getup_ticks" })
        {
            var key = "global.control." + name;
            Reject(EffectSetupFixture.Change(config, settings: config.Settings.Where(x => x.Name != key)), "MissingEffectSetting", "/config/settings/" + key);
        }
    }

    [Theory]
    [InlineData("stat.move_speed.min", 0, "InvalidStatBounds")]
    [InlineData("stat.action_speed.max", 501, "InvalidStatBounds")]
    [InlineData("stat.mass.min", 0, "InvalidStatBounds")]
    [InlineData("stat.max_health.min", 0, "InvalidStatBounds")]
    [InlineData("stat.guard.min", 1001, "InvalidStatBounds")]
    [InlineData("global.control.max_trigger_depth", 33, "InvalidEffectSetting")]
    [InlineData("global.control.max_triggers_per_tick", 4097, "InvalidEffectSetting")]
    [InlineData("global.control.max_effect_instances_per_fighter", 129, "InvalidEffectSetting")]
    [InlineData("global.control.knockdown_fall_ticks", 0, "InvalidEffectSetting")]
    [InlineData("global.control.control_k", 0, "InvalidEffectSetting")]
    [InlineData("global.control.force_k", 0, "InvalidEffectSetting")]
    [InlineData("global.damage.armor_k", 0, "InvalidEffectSetting")]
    [InlineData("global.sim.fp_scale", 0, "InvalidEffectSetting")]
    [InlineData("battle.time_limit_ticks", 1000000, "InvalidEffectSetting")]
    public void InvalidNumericDomainsRejectWithStablePath(string key, int value, string code) =>
        Reject(EffectSetupFixture.Setting(EffectSetupFixture.Config(), key, ConfigValue.FromInteger(value)), code, "/config/settings/" + key);

    [Fact]
    public void InvertedBoundsAndWrongValueKindsCannotBecomeDefaultBounds()
    {
        var config = EffectSetupFixture.Setting(EffectSetupFixture.Config(), "stat.move_speed.min", ConfigValue.FromInteger(100));
        config = EffectSetupFixture.Setting(config, "stat.move_speed.max", ConfigValue.FromInteger(50));
        Reject(config, "InvalidStatBounds", "/config/settings/stat.move_speed.min");
        foreach (var value in new[] { ConfigValue.FromString("1"), ConfigValue.FromBoolean(true), ConfigValue.FromInteger(long.MaxValue) })
            Reject(EffectSetupFixture.Setting(EffectSetupFixture.Config(), "stat.move_speed.min", value), "InvalidStatBounds", "/config/settings/stat.move_speed.min");
    }

    [Theory]
    [InlineData("duration_ticks", 0)]
    [InlineData("stack_cap", 0)]
    [InlineData("stack_cap", 256)]
    public void EffectDurationAndStackCapReject(string key, int value) =>
        Reject(EffectSetupFixture.Effect(EffectSetupFixture.Config(), "effect_guard_broken", (key, ConfigValue.FromInteger(value))),
            "InvalidEffectGroup", "/config/effects/effect_guard_broken/" + key);

    [Theory]
    [InlineData("modifier_stat1", "MaxHealth", "UnsupportedEffectModifierTarget")]
    [InlineData("modifier_stat1", "MaxEnergy", "UnsupportedEffectModifierTarget")]
    [InlineData("modifier_stat1", "CollisionRadius", "UnsupportedEffectModifierTarget")]
    [InlineData("modifier_stat1", "StaggerThreshold", "UnsupportedEffectModifierTarget")]
    [InlineData("modifier_stat1", "UnknownStat", "UnsupportedEffectModifierTarget")]
    [InlineData("operation1", "UnknownOperation", "UnsupportedEffectModifierTarget")]
    [InlineData("expiry_boundary", "UnknownExpiry", "InvalidEffectGroup")]
    [InlineData("stack_policy", "99", "InvalidEffectGroup")]
    [InlineData("refresh_rule", "resetduration", "InvalidEffectGroup")]
    [InlineData("semantic_role", "MissingRole", "InvalidEffectReference")]
    public void UnsupportedEffectVocabularyRejectsRatherThanSilentlySkipping(string key, string value, string code) =>
        Reject(EffectSetupFixture.Effect(EffectSetupFixture.Config(), "effect_guard_broken", (key, ConfigValue.FromString(value))),
            code, "/config/effects/effect_guard_broken/" + key);

    [Theory]
    [InlineData("trigger", "Unknown")]
    [InlineData("trigger", "1")]
    [InlineData("owner_kind", "Passive")]
    [InlineData("owner_id", "unknown_action")]
    [InlineData("recipient", "Both")]
    [InlineData("condition", "eval()")]
    [InlineData("primitive", "Damage")]
    public void RuleVocabularyIsClosedAndErrorsAreSorted(string key, string value)
    {
        var config = EffectSetupFixture.Config();
        config = EffectSetupFixture.Change(config, rules: config.EffectRules.Select(x => x.Id.Value == "rule_control_fatigue"
            ? EffectSetupFixture.Patch(x, (key, ConfigValue.FromString(value))) : x));
        Reject(config, "InvalidEffectRule", "/config/effect_rules/rule_control_fatigue/" + key);
    }

    [Fact]
    public void UnknownReferencesAndFalseOncePerEventReject()
    {
        var config = EffectSetupFixture.Config();
        foreach (var patch in new[] { ("effect_id", ConfigValue.FromString("effect_missing")), ("once_per_event", ConfigValue.FromBoolean(false)) })
        {
            var changed = EffectSetupFixture.Change(config, rules: config.EffectRules.Select(x => x.Id.Value == "rule_control_fatigue" ? EffectSetupFixture.Patch(x, patch) : x));
            Reject(changed, patch.Item1 == "effect_id" ? "InvalidEffectReference" : "InvalidEffectRule", "/config/effect_rules/rule_control_fatigue/" + patch.Item1);
        }
        config = EffectSetupFixture.Change(config, rules: config.EffectRules.Append(EffectSetupFixture.Rule("rule_missing_owner", "effect_exposed", "Action", "unknown_action")));
        Reject(config, "InvalidEffectRule", "/config/effect_rules/rule_missing_owner/owner_id");
    }

    [Fact]
    public void MissingCompareLookupAndDuplicateSpecialRolesReject()
    {
        var config = EffectSetupFixture.Config();
        Reject(EffectSetupFixture.Change(config, effects: config.Effects.Select(x => x.Id.Value == "effect_bear_thick_hide" ? EffectSetupFixture.Without(x, "compare_key") : x)),
            "InvalidEffectGroup", "/config/effects/effect_bear_thick_hide/compare_key");
        Reject(EffectSetupFixture.Effect(config, "effect_control_fatigue", ("lookup_profile", ConfigValue.FromString("1000|750|500"))),
            "InvalidEffectLookup", "/config/effects/effect_control_fatigue/lookup_profile");
        Reject(EffectSetupFixture.Effect(config, "effect_bear_thick_hide", ("semantic_role", ConfigValue.FromString("ControlImmunity"))),
            "InvalidEffectReference", "/config/effects/semantic_role/ControlImmunity");
    }

    [Fact]
    public void FatigueThresholdLookupDomainsAndDuplicateDurationsAgree()
    {
        var config = EffectSetupFixture.Config();
        Reject(EffectSetupFixture.Setting(config, "global.control.fatigue_threshold", ConfigValue.FromInteger(4)),
            "InvalidEffectLookup", "/config/effects/effect_control_fatigue/lookup_profile");
        foreach (var lookup in new[] { "1000|750|500|-1", "1000|750|500|1001", "1000|750|oops|250", "1000||500|250" })
            Reject(EffectSetupFixture.Effect(config, "effect_control_fatigue", ("lookup_profile", ConfigValue.FromString(lookup))),
                "InvalidEffectLookup", "/config/effects/effect_control_fatigue/lookup_profile");
        foreach (var id in new[] { "effect_control_fatigue", "effect_control_immunity", "effect_grab_lockout", "effect_wakeup_immunity" })
            Reject(EffectSetupFixture.Effect(config, id, ("duration_ticks", ConfigValue.FromInteger(13))), "InvalidControlDuration", "/config/effects/" + id + "/duration_ticks");
    }

    [Fact]
    public void GroupPoliciesTargetDomainsAndStrongestCompareKeysMustBeCompatible()
    {
        var config = EffectSetupFixture.Config();
        var first = EffectSetupFixture.CustomEffect(config, "effect_a", "shared", "Armor");
        var second = EffectSetupFixture.CustomEffect(config, "effect_b", "shared", "Armor");
        Reject(EffectSetupFixture.Change(config, effects: config.Effects.Concat([first, second])), "InvalidEffectGroup", "/config/effects/effect_a/stack_group");
        first = EffectSetupFixture.Patch(first, ("stack_policy", ConfigValue.FromString("Replace")));
        second = EffectSetupFixture.Patch(second, ("stack_policy", ConfigValue.FromString("Replace")));
        Assert.NotNull(Create(EffectSetupFixture.Change(config, effects: config.Effects.Concat([first, second]))));
        second = EffectSetupFixture.Patch(second, ("modifier_stat1", ConfigValue.FromString("Power")));
        Reject(EffectSetupFixture.Change(config, effects: config.Effects.Concat([first, second])), "InvalidEffectGroup", "/config/effects/effect_b/stack_group");
        first = EffectSetupFixture.Patch(first, ("stack_policy", ConfigValue.FromString("StrongestWins")), ("compare_key", ConfigValue.FromString("Value1")));
        second = EffectSetupFixture.Patch(second, ("modifier_stat1", ConfigValue.FromString("Armor")), ("stack_policy", ConfigValue.FromString("StrongestWins")), ("compare_key", ConfigValue.FromString("DurationTicks")));
        Reject(EffectSetupFixture.Change(config, effects: config.Effects.Concat([first, second])), "InvalidEffectGroup", "/config/effects/effect_b/stack_group");
    }

    [Fact]
    public void DormantResourceTargetsRemainCatalogOnlyAndUnselectedActionRuleCannotMakeThemReachable()
    {
        var config = EffectSetupFixture.Config();
        var dormant = "effect_gorilla_wall_pressure";
        Assert.DoesNotContain(Create(config).Effects, x => x.EffectId.Value == dormant);
        config = EffectSetupFixture.Reachable(config, dormant, "Action", "gorilla_wall_breaker");
        Assert.DoesNotContain(Create(config).Rules, x => x.RuleId.Value == "rule_test");
        config = EffectSetupFixture.Config();
        Reject(EffectSetupFixture.Reachable(config, dormant), "UnsupportedEffectModifierTarget", "/config/effects/" + dormant + "/modifier_stat2");
        Reject(EffectSetupFixture.Reachable(config, dormant, "Action", "bear_earthbreaker"),
            "UnsupportedEffectModifierTarget", "/config/effects/" + dormant + "/modifier_stat2");
    }

    [Fact]
    public void EffectOwnerReachabilityUsesTransitiveFixedPointAndDoesNotActivateAnUnrootedCycle()
    {
        var config = EffectSetupFixture.Config();
        var rules = new[] { EffectSetupFixture.Rule("rule_a", "effect_exposed", "Effect", "effect_bear_thick_hide"),
            EffectSetupFixture.Rule("rule_b", "effect_bear_thick_hide", "Effect", "effect_exposed") };
        var changed = EffectSetupFixture.Change(config, rules: config.EffectRules.Concat(rules));
        Assert.DoesNotContain(Create(changed).Effects, x => x.EffectId.Value == "effect_exposed");
        changed = EffectSetupFixture.Reachable(changed, "effect_exposed");
        var definition = Create(changed);
        Assert.Contains(definition.Effects, x => x.EffectId.Value == "effect_bear_thick_hide");
        Assert.Contains(definition.Rules, x => x.RuleId.Value == "rule_a");
        Assert.Contains(definition.Rules, x => x.RuleId.Value == "rule_b");
    }

    [Theory]
    [InlineData("effect_control_immunity", "operation1", "Multiply")]
    [InlineData("effect_guard_broken", "operation2", "Add")]
    public void InvalidOperationChannelPairsReject(string id, string key, string operation) =>
        Reject(EffectSetupFixture.Effect(EffectSetupFixture.Config(), id, (key, ConfigValue.FromString(operation))),
            "UnsupportedEffectModifierTarget", "/config/effects/" + id + "/" + key);

    [Fact]
    public void FlagOverrideRequiresIntegerZeroOrOneWithoutCoercion()
    {
        var config = EffectSetupFixture.Config();
        foreach (var value in new[] { ConfigValue.FromInteger(2), ConfigValue.FromInteger(-1), ConfigValue.FromBoolean(false), ConfigValue.FromString("0") })
        {
            var changed = EffectSetupFixture.Effect(config, "effect_control_immunity", ("value1", value));
            var issues = new List<EffectSetupIssue>();
            Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(changed), changed, issues));
            Assert.Contains(issues, x => x.Path.StartsWith("/config/effects/effect_control_immunity/", StringComparison.Ordinal));
        }
        Create(EffectSetupFixture.Effect(config, "effect_control_immunity", ("value1", ConfigValue.FromInteger(1))));
    }

    [Fact]
    public void ReachableArithmeticRiskRejectsBeforeClampAndOverrideWhileDormantRiskDoesNot()
    {
        var config = EffectSetupFixture.Config();
        var effect = EffectSetupFixture.CustomEffect(config, "effect_overflow", "overflow", "Armor", value: int.MaxValue, policy: "AddStacks", cap: 2);
        config = EffectSetupFixture.Change(config, effects: config.Effects.Append(effect));
        Create(config); // An unbound dormant profile cannot cause a gameplay overflow.
        Reject(EffectSetupFixture.Reachable(config, "effect_overflow"), "EffectArithmeticOverflowRisk", "/fighters/0/stats/Armor");
        config = EffectSetupFixture.Effect(config, "effect_overflow", ("stack_cap", ConfigValue.FromInteger(1)),
            ("modifier_stat2", ConfigValue.FromString("Armor")), ("operation2", ConfigValue.FromString("Override")), ("value2", ConfigValue.FromInteger(0)));
        Reject(EffectSetupFixture.Reachable(config, "effect_overflow"), "EffectArithmeticOverflowRisk", "/fighters/0/stats/Armor");
    }

    [Fact]
    public void ProductTimerAndControlRatioRisksRejectWithTypedIssues()
    {
        var config = EffectSetupFixture.Config();
        var effect = EffectSetupFixture.CustomEffect(config, "effect_overflow", "overflow", "Armor", "Multiply", int.MaxValue, "AddStacks", 2);
        config = EffectSetupFixture.Reachable(EffectSetupFixture.Change(config, effects: config.Effects.Append(effect)), "effect_overflow");
        Reject(config, "EffectArithmeticOverflowRisk", "/fighters/0/stats/Armor");
        config = EffectSetupFixture.Effect(EffectSetupFixture.Config(), "effect_guard_broken", ("duration_ticks", ConfigValue.FromInteger(int.MaxValue)));
        Reject(config, "EffectArithmeticOverflowRisk", "/config/effects/effect_guard_broken/duration_ticks");
        config = EffectSetupFixture.Setting(EffectSetupFixture.Config(), "global.control.control_k", ConfigValue.FromInteger(int.MaxValue));
        Reject(config, "EffectArithmeticOverflowRisk", "/config/control/knockdown");
    }

    [Fact]
    public void MutuallyExclusiveStrongestProfilesDoNotMultiplyTogetherInTheProof()
    {
        var config = EffectSetupFixture.Config();
        var first = EffectSetupFixture.CustomEffect(config, "effect_a", "shared", "Armor", "Multiply", 1000000, "StrongestWins");
        first = EffectSetupFixture.Patch(first, ("compare_key", ConfigValue.FromString("Value1")));
        var second = EffectSetupFixture.CustomEffect(config, "effect_b", "shared", "Armor", "Multiply", 1000000, "StrongestWins");
        second = EffectSetupFixture.Patch(second, ("compare_key", ConfigValue.FromString("Value1")));
        config = EffectSetupFixture.Change(config, effects: config.Effects.Concat([first, second]),
            rules: config.EffectRules.Concat([EffectSetupFixture.Rule("rule_a", "effect_a"), EffectSetupFixture.Rule("rule_b", "effect_b")]));
        Assert.Equal(7, Create(config).Effects.Count);
    }

    [Fact]
    public void StaticMaxHealthGearIsClampedAtSetupButNoDynamicMaxHealthTargetIsExecutable()
    {
        var config = EffectSetupFixture.Config();
        config = EffectSetupFixture.Change(config, gear: config.Gear.Select(x => x.Id.Value == "gear_utility_sprint_soles"
            ? EffectSetupFixture.Patch(x, ("stat1", ConfigValue.FromString("MaxHealth")), ("value1", ConfigValue.FromInteger(120))) : x));
        Assert.Equal(1770, Create(config).Fighters[0].InitialStats["MaxHealth"]);
        config = EffectSetupFixture.Setting(config, "stat.max_health.max", ConfigValue.FromInteger(1700));
        Assert.Equal(1700, Create(config).Fighters[0].InitialStats["MaxHealth"]);
    }

    [Fact]
    public void ReferenceAndExplicitVersionSettingsMustAgreeWithoutImplicitV01Migration()
    {
        var config = EffectSetupFixture.Config();
        Reject(EffectSetupFixture.Setting(config, "global.sim.schema_version", ConfigValue.FromString("combat.balance/0.1")),
            "UnsupportedBalanceSchema", "/config/settings/global.sim.schema_version");
        Reject(EffectSetupFixture.Setting(config, "global.sim.config_version", ConfigValue.FromInteger(2)),
            "UnsupportedConfigVersion", "/config/settings/global.sim.config_version");
        var issues = new List<EffectSetupIssue>();
        var legacy = Battle.Core.UnitTests.Engine.EngineTestFixture.CreateConfig();
        Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(legacy), legacy, issues));
        Assert.Contains(issues, x => x.Code == "UnsupportedBalanceSchema" && x.Path == "/config/reference");
    }

    [Fact]
    public void StaticRawBaseCanExceedFinalClampButOverflowCannotBeHiddenByTheClamp()
    {
        var config = EffectSetupFixture.Config();
        config = EffectSetupFixture.Change(config, fighters: config.Fighters.Select(x => x.Id.Value == "bear"
            ? EffectSetupFixture.Patch(x, ("power", ConfigValue.FromInteger(1500))) : x));
        Assert.Equal(1500, Create(config).Fighters[0].BaseStats["Power"]);
        Assert.Equal(1000, Create(config).Fighters[0].InitialStats["Power"]);
        config = EffectSetupFixture.Change(config, fighters: config.Fighters.Select(x => x.Id.Value == "bear"
            ? EffectSetupFixture.Patch(x, ("power", ConfigValue.FromInteger(int.MaxValue))) : x));
        Reject(config, "EffectArithmeticOverflowRisk", "/fighters/0/stats/Power");
    }

    [Fact]
    public void MalformedGearCannotHideAnUnknownStatOperationOrAnUnboundSecondValue()
    {
        var config = EffectSetupFixture.Config();
        foreach (var patch in new[] { ("stat1", ConfigValue.FromString("UnknownStat")),
            ("operation1", ConfigValue.FromString("1")), ("value2", ConfigValue.FromInteger(10)),
            ("slot", ConfigValue.FromString("Offense")), ("priority", ConfigValue.FromString("0")) })
        {
            var changed = EffectSetupFixture.Change(config, gear: config.Gear.Select(x => x.Id.Value == "gear_defense_reinforced_hide"
                ? EffectSetupFixture.Patch(x, patch) : x));
            var issues = new List<EffectSetupIssue>();
            Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(changed), changed, issues));
            Assert.Contains(issues, x => x.Entity == "gear_defense_reinforced_hide");
        }
    }

    [Fact]
    public void RuleCooldownTimersAndMissingRequiredEffectMetadataReject()
    {
        var config = EffectSetupFixture.Config();
        config = EffectSetupFixture.Change(config, rules: config.EffectRules.Select(x => x.Id.Value == "rule_guard_broken"
            ? EffectSetupFixture.Patch(x, ("internal_cooldown_ticks", ConfigValue.FromInteger(int.MaxValue))) : x));
        Reject(config, "EffectArithmeticOverflowRisk", "/config/effect_rules/rule_guard_broken/internal_cooldown_ticks");
        config = EffectSetupFixture.Config();
        Reject(EffectSetupFixture.Change(config, effects: config.Effects.Select(x => x.Id.Value == "effect_guard_broken" ? EffectSetupFixture.Without(x, "refresh_rule") : x)),
            "InvalidEffectGroup", "/config/effects/effect_guard_broken/refresh_rule");
        Reject(EffectSetupFixture.Effect(config, "effect_guard_broken", ("modifier_stat2", ConfigValue.FromString(""))),
            "UnsupportedEffectModifierTarget", "/config/effects/effect_guard_broken/modifier_stat2");
    }

    [Fact]
    public void SignedMultipliersHaveAFiniteConservativeProofWithoutRuntimeFloatingPoint()
    {
        var config = EffectSetupFixture.Config();
        var effect = EffectSetupFixture.CustomEffect(config, "effect_signed", "signed", "Armor", "Multiply", -500, "AddStacks", 3);
        config = EffectSetupFixture.Reachable(EffectSetupFixture.Change(config, effects: config.Effects.Append(effect)), "effect_signed");
        Assert.Contains(Create(config).Effects, x => x.EffectId.Value == "effect_signed");
    }

    [Fact]
    public void UnknownRuleReferenceIsCheckedEvenForAnUnselectedOwner()
    {
        var config = EffectSetupFixture.Config();
        config = EffectSetupFixture.Reachable(config, "effect_missing", "Action", "gorilla_wall_breaker");
        Reject(config, "InvalidEffectReference", "/config/effect_rules/rule_test/effect_id");
    }

    [Fact]
    public void NullMaterializerArgumentsFailAtTheApiBoundary()
    {
        var config = EffectSetupFixture.Config();
        Assert.Throws<ArgumentNullException>(() => EffectSetupMaterializer.TryCreate(null!, config, []));
        Assert.Throws<ArgumentNullException>(() => EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(config), null!, []));
        Assert.Throws<ArgumentNullException>(() => EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(config), config, null!));
    }
}
