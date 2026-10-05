using Battle.Contracts.Config;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core.Effects;
using Battle.Core.Initialization;
using Battle.Core.UnitTests.Engine;
using static Battle.Core.UnitTests.Effects.EffectSetupFixture;

namespace Battle.Core.UnitTests.Effects;

// Real external v0.2 compiled input through the versioned factory/Simulate; no definition injection.
// Default public producer remains 0.4 until versioned replay/golden release gates are completed.
[Trait("WorkPackage", "WP10")]
public sealed class EffectVersionedSetupTests
{
    [Fact]
    public void PhysicalV02SetupInitializesBothFightersAndEffectsAtomicallyWithoutDraws()
    {
        var config = Config();
        var result = BattleSetupFactory.Create(Request(config), config, BattleSetupFactory.EffectsEngineVersion);
        Assert.Empty(result.Errors);
        var setup = Assert.IsType<BattleSetup>(result.Setup);
        Assert.Equal(1650, setup.State.FighterA.Health);
        Assert.Equal(93, setup.State.FighterA.Armor);
        Assert.Equal(142, setup.State.FighterA.Power);
        Assert.Equal(82, setup.State.FighterA.MoveSpeed);
        Assert.NotNull(setup.State.Effects);
        Assert.Empty(setup.State.FighterA.Effects); Assert.Empty(setup.State.FighterB.Effects);
        Assert.Equal(0UL, setup.State.Rng.Decision.NextDrawIndex); Assert.Equal(0UL, setup.State.Rng.Resolution.NextDrawIndex);
        var journal = Simulate(config);
        Assert.Equal(1, journal.BeginCount); Assert.Equal(1, journal.CompleteCount);
        Assert.Equal(0, journal.Drafts[0].Sequence); Assert.Equal(CombatEventType.BattleStarted, journal.Drafts[0].EventType);
        Assert.Equal("battle.core/0.5.0", journal.Drafts[0].EngineVersion.ToString());
        Assert.All(journal.Summary!.FinalFrames, frame => Assert.Empty(frame.Effects));
    }

    [Fact]
    public void AllThirtySixNewRequiredKeysRejectBeforeBeginWithoutDefaults()
    {
        var config = Config();
        foreach (var stat in EffectSetupMaterializer.StatDomains())
            foreach (var bound in new[] { "min", "max" })
                Reject(Change(config, settings: config.Settings.Where(x => x.Name != "stat." + stat.Data + "." + bound)), "MissingStatBounds");
        foreach (var setting in new[] { "max_trigger_depth", "max_triggers_per_tick", "max_effect_instances_per_fighter",
                     "knockdown_fall_ticks", "knockdown_grounded_ticks", "knockdown_getup_ticks" })
            Reject(Change(config, settings: config.Settings.Where(x => x.Name != "global.control." + setting)), "MissingEffectSetting");
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-002")]
    public void InvalidBoundsDivisorsStackDurationAndInstanceDomainsRejectBeforeBegin()
    {
        var config = Config();
        Reject(Setting(Setting(config, "stat.guard.min", ConfigValue.FromInteger(100)), "stat.guard.max", ConfigValue.FromInteger(50)), "InvalidStatBounds");
        foreach (var key in new[] { "stat.move_speed.min", "stat.action_speed.min", "stat.mass.min" })
            Reject(Setting(config, key, ConfigValue.FromInteger(0)), "InvalidStatBounds");
        foreach (var key in new[] { "global.control.control_k", "global.control.force_k", "global.damage.armor_k" })
            Reject(Setting(config, key, ConfigValue.FromInteger(0)));
        foreach (var cap in new[] { 0, 256 }) Reject(Effect(config, "effect_guard_broken", ("stack_cap", ConfigValue.FromInteger(cap))), "InvalidEffectGroup");
        Reject(Effect(config, "effect_guard_broken", ("duration_ticks", ConfigValue.FromInteger(0))), "InvalidEffectGroup");
        Reject(Setting(config, "global.control.max_effect_instances_per_fighter", ConfigValue.FromInteger(129)), "InvalidEffectSetting");
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-005")]
    public void MissingCompareLookupIncompatibleGroupAndDuplicateRolesRejectBeforeBegin()
    {
        var config = Config();
        var strongest = Without(CustomEffect(config, "effect_strongest", "strongest", "Armor", policy: "StrongestWins"), "compare_key");
        Reject(Change(config, effects: config.Effects.Append(strongest)), "InvalidEffectGroup");
        Reject(Effect(config, "effect_control_fatigue", ("lookup_profile", ConfigValue.FromString("1000|750|500"))), "InvalidEffectLookup");
        var a = CustomEffect(config, "effect_a", "shared", "Armor", policy: "Replace");
        var b = CustomEffect(config, "effect_b", "shared", "Power", policy: "Replace");
        Reject(Change(config, effects: config.Effects.Concat([a, b])), "InvalidEffectGroup");
        Reject(Effect(config, "effect_exposed", ("semantic_role", ConfigValue.FromString("ControlImmunity"))), "InvalidEffectReference");
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-004")]
    public void UnknownVocabularyReferencesAndPhaseTokensRejectWithStableSortedPaths()
    {
        var config = Config();
        foreach (var (key, code) in new[] { ("modifier_stat1", "UnsupportedEffectModifierTarget"),
                     ("operation1", "UnsupportedEffectModifierTarget"), ("expiry_boundary", "InvalidEffectGroup") })
            RejectPath(Effect(config, "effect_guard_broken", (key, ConfigValue.FromString("Unknown"))), code,
                "/config/effects/effect_guard_broken/" + key);
        foreach (var (key, value, code) in new[] { ("trigger", "Unknown", "InvalidEffectRule"),
                     ("condition", "Unknown", "InvalidEffectRule"), ("recipient", "Unknown", "InvalidEffectRule"),
                     ("owner_id", "unknown_owner", "InvalidEffectRule"), ("effect_id", "unknown_effect", "InvalidEffectReference") })
            RejectPath(Change(config, rules: config.EffectRules.Select(x => x.Id.Value == "rule_control_fatigue"
                ? Patch(x, (key, ConfigValue.FromString(value))) : x)), code, "/config/effect_rules/rule_control_fatigue/" + key);
        foreach (var key in new[] { "hit_interruptible_phases", "protected_phases", "ignored_control_categories" })
            RejectPath(Change(config, actions: config.Actions.Select(x => x.Id.Value == "bear_earthbreaker"
                ? Patch(x, (key, ConfigValue.FromString("Unknown"))) : x)), "InvalidInterruptProfile", "/config/actions/bear_earthbreaker/" + key);
        var setup = BattleSetupFactory.Create(Request(config), config, BattleSetupFactory.EffectsEngineVersion).Setup!;
        Assert.Equal(0UL, setup.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(0UL, setup.State.Rng.Resolution.NextDrawIndex);
        // Rejections cannot leave draw/counter state in the reusable compiled snapshot.
        Assert.Equal(Simulate(config).Drafts.Select(x => x.Rng), Simulate(config).Drafts.Select(x => x.Rng));
    }

    [Fact]
    public void ReachableGeometryAndConsumerDenominatorRisksRejectBeforeBegin()
    {
        var config = Config();
        RejectPath(Setting(Setting(config, "global.arena.min_position", ConfigValue.FromInteger(int.MinValue)),
            "global.arena.max_position", ConfigValue.FromInteger(int.MaxValue)), "EffectArithmeticOverflowRisk", "/config/arena/arithmetic");
        foreach (var key in new[] { "global.damage.armor_k", "global.control.control_k", "global.control.force_k" })
            Reject(Setting(config, key, ConfigValue.FromInteger(int.MaxValue)));
        var grab = CustomEffect(config, "effect_grab_priority_risk", "grab_priority_risk", "GrabPriority", value: int.MaxValue);
        var risk = Change(config, effects: config.Effects.Append(grab), actions: config.Actions.Select(x => x.Id.Value == "bear_earthbreaker"
            ? Patch(x, ("grab_priority", ConfigValue.FromInteger(1)), ("hit_schedule", ConfigValue.FromString("grab:0")),
                ("hit_count", ConfigValue.FromInteger(0)), ("tags", ConfigValue.FromString("grab|special"))) : x));
        Reject(Reachable(risk, grab.Id.Value), "EffectArithmeticOverflowRisk");
    }

    [Fact]
    public void SmallEventCapDoesNotRejectValidPotentialBudgetButFailsActualBatchAtomically()
    {
        var config = Setting(Setting(Setting(Config(), "global.control.max_effect_instances_per_fighter", ConfigValue.FromInteger(128)),
            "global.control.max_triggers_per_tick", ConfigValue.FromInteger(4096)), "global.sim.max_events_per_battle", ConfigValue.FromInteger(4));
        var setup = BattleSetupFactory.Create(Request(config), config, BattleSetupFactory.EffectsEngineVersion);
        Assert.Empty(setup.Errors); Assert.NotNull(setup.Setup);
        var journal = new RecordingJournal();
        var result = new CombatEngine(BattleSetupFactory.EffectsEngineVersion).Simulate(Request(config), config, journal);
        Assert.Equal(BattleResultStatus.FailedInvariant, result.Status);
        Assert.Equal("EventCapExceeded", result.InvariantFailure!.Code.Value);
        Assert.Equal(1, journal.BeginCount); Assert.Equal(1, journal.CompleteCount);
        Assert.Equal(new[] { CombatEventType.BattleStarted, CombatEventType.BattleEnded }, journal.Drafts.Select(x => x.EventType));
        Assert.Equal(BattleOutcome.Invalid, journal.Summary!.Outcome);
        Assert.Equal(BattleEndReason.BattleInvalid, journal.Summary.EndReason);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void VersionedSimulationsReuseCompiledDataWithoutLeakingEffectsOrCounters()
    {
        var plain = Config();
        var active = Reachable(plain, "effect_exposed");
        var first = Simulate(active); var middle = Simulate(plain); var last = Simulate(active);
        Assert.Contains(first.Drafts, x => x.EffectId?.Value == "effect_exposed");
        Assert.DoesNotContain(middle.Drafts, x => x.EffectId?.Value == "effect_exposed");
        Assert.Equal(first.Drafts.Select(x => (x.Tick, x.Sequence, x.EventType, x.ActorId, x.EffectId, x.Rng)),
            last.Drafts.Select(x => (x.Tick, x.Sequence, x.EventType, x.ActorId, x.EffectId, x.Rng)));
        Assert.All(new[] { first, middle, last }, x => Assert.All(x.Summary!.FinalFrames, frame => Assert.Empty(frame.Effects)));
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-006")]
    public void FatigueThresholdLookupAndRoleDurationSettingsAgreeBeforeBegin()
    {
        var config = Config();
        Reject(Setting(config, "global.control.fatigue_threshold", ConfigValue.FromInteger(4)), "InvalidEffectLookup");
        foreach (var id in new[] { "effect_control_fatigue", "effect_control_immunity", "effect_grab_lockout", "effect_wakeup_immunity" })
            Reject(Effect(config, id, ("duration_ticks", ConfigValue.FromInteger(101))), "InvalidControlDuration");
        Reject(Effect(config, "effect_control_fatigue", ("lookup_profile", ConfigValue.FromString("1000|750|500"))), "InvalidEffectLookup");
        _ = Simulate(config);
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-007")]
    public void ReachableAddProductTimersAndConsumerRisksRejectBeforeBeginWithoutClampMasking()
    {
        var baseline = Config();
        var add = CustomEffect(baseline, "effect_risk", "risk", "Armor", value: int.MaxValue, policy: "AddStacks", cap: 2);
        Reject(Reachable(Change(baseline, effects: baseline.Effects.Append(add)), "effect_risk"), "EffectArithmeticOverflowRisk");
        var product = CustomEffect(baseline, "effect_risk", "risk", "Armor", "Multiply", int.MaxValue, "AddStacks", 2);
        Reject(Reachable(Change(baseline, effects: baseline.Effects.Append(product)), "effect_risk"), "EffectArithmeticOverflowRisk");
        var overridden = Patch(add, ("stack_cap", ConfigValue.FromInteger(1)), ("modifier_stat2", ConfigValue.FromString("Armor")),
            ("operation2", ConfigValue.FromString("Override")), ("value2", ConfigValue.FromInteger(0)));
        Reject(Reachable(Change(baseline, effects: baseline.Effects.Append(overridden)), "effect_risk"), "EffectArithmeticOverflowRisk");
        var timer = Patch(CustomEffect(baseline, "effect_risk", "risk", "Armor"), ("duration_ticks", ConfigValue.FromInteger(int.MaxValue)));
        Reject(Reachable(Change(baseline, effects: baseline.Effects.Append(timer)), "effect_risk"), "EffectArithmeticOverflowRisk");
        var cooldown = Patch(Rule("rule_risk", "effect_exposed"), ("internal_cooldown_ticks", ConfigValue.FromInteger(int.MaxValue)));
        Reject(Change(baseline, rules: baseline.EffectRules.Append(cooldown)), "EffectArithmeticOverflowRisk");
        var dealt = CustomEffect(baseline, "effect_risk", "risk", "DamageDealt", "Multiply", int.MaxValue);
        var taken = CustomEffect(baseline, "effect_risk_taken", "risk_taken", "DamageTaken", "Multiply", int.MaxValue);
        Reject(Change(baseline, effects: baseline.Effects.Concat([dealt, taken]),
            rules: baseline.EffectRules.Concat([Rule("rule_risk", "effect_risk"), Rule("rule_risk_taken", "effect_risk_taken")])), "EffectArithmeticOverflowRisk");
        var guarded = Change(baseline, rules: baseline.EffectRules.Append(Patch(Rule("rule_large_cap", "effect_exposed"),
            ("max_activations_per_tick", ConfigValue.FromInteger(int.MaxValue)), ("max_activations_per_battle", ConfigValue.FromInteger(int.MaxValue)))));
        _ = Simulate(guarded); // Counter increment is guarded at cap; Int32.MaxValue itself is not overflow.
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-010")]
    public void DormantResourceEffectIsNotActivatedByPassiveOrUnselectedAction()
    {
        var config = Reachable(Config(), "effect_gorilla_wall_pressure", "Action", "gorilla_wall_breaker");
        var journal = Simulate(config);
        Assert.DoesNotContain(journal.Drafts, x => x.EffectId?.Value == "effect_gorilla_wall_pressure");
        Reject(Reachable(Config(), "effect_gorilla_wall_pressure"), "UnsupportedEffectModifierTarget");
        Reject(Reachable(Config(), "effect_gorilla_wall_pressure", "Action", "bear_earthbreaker"), "UnsupportedEffectModifierTarget");
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-011")]
    public void FlagDomainsAndOperationPairsAreStrictBeforeBegin()
    {
        var config = Config();
        foreach (var value in new[] { ConfigValue.FromInteger(-1), ConfigValue.FromInteger(2), ConfigValue.FromBoolean(false), ConfigValue.FromString("0") })
            Reject(Effect(config, "effect_control_immunity", ("value1", value)), "UnsupportedEffectModifierTarget");
        Reject(Effect(config, "effect_control_immunity", ("operation1", ConfigValue.FromString("Multiply"))), "UnsupportedEffectModifierTarget");
        Reject(Effect(config, "effect_guard_broken", ("operation2", ConfigValue.FromString("Add"))), "UnsupportedEffectModifierTarget");
        foreach (var value in new[] { 0, 1 }) _ = Simulate(Effect(config, "effect_control_immunity", ("value1", ConfigValue.FromInteger(value))));
    }

    [Fact, Trait("AcceptanceId", "WP10-MOD-014")]
    public void StaticMaxHealth1770StartsFullAndRuntimeStructuralModifiersAreRejected()
    {
        var config = Config();
        var changed = Change(config, gear: config.Gear.Select(x => x.Id.Value == "gear_utility_sprint_soles"
            ? Patch(x, ("stat1", ConfigValue.FromString("MaxHealth")), ("value1", ConfigValue.FromInteger(120))) : x));
        var journal = Simulate(changed);
        Assert.Equal(1770, journal.Start!.FighterA.InitialFrame.Health); Assert.Equal(1770, journal.Start.FighterA.InitialFrame.MaxHealth);
        foreach (var stat in new[] { "MaxHealth", "MaxEnergy", "CollisionRadius", "StaggerThreshold" })
            Reject(Effect(config, "effect_guard_broken", ("modifier_stat1", ConfigValue.FromString(stat))), "UnsupportedEffectModifierTarget");
    }

    [Fact]
    public void StaticAggregatedGearFormulaAndInitialMaximaClampDoNotUseLegacySequentialPipeline()
    {
        var config = Change(Config(), gear: Config().Gear.Select(x => x.Id.Value switch
        {
            "gear_offense_power_wraps" => Patch(x, ("stat1", ConfigValue.FromString("Guard")), ("value1", ConfigValue.FromInteger(10))),
            "gear_defense_reinforced_hide" => Patch(x, ("stat1", ConfigValue.FromString("Guard")), ("value1", ConfigValue.FromInteger(-30))),
            "gear_utility_sprint_soles" => Patch(x, ("stat1", ConfigValue.FromString("Guard")), ("operation1", ConfigValue.FromString("Multiply")), ("value1", ConfigValue.FromInteger(1500))),
            _ => x,
        }));
        var result = BattleSetupFactory.Create(Request(config), config, BattleSetupFactory.EffectsEngineVersion);
        Assert.Empty(result.Errors);
        var definition = result.Setup!.State.Effects!.Definition;
        var expected = checked((definition.Fighters[0].BaseStats["Guard"] - 20) * 1500 / 1000);
        Assert.Equal(expected, result.Setup.State.FighterA.Guard);
        config = Setting(config, "stat.max_health.max", ConfigValue.FromInteger(1000));
        var journal = Simulate(config);
        Assert.Equal(1000, journal.Start!.FighterA.InitialFrame.MaxHealth);
        Assert.Equal(1000, journal.Start.FighterA.InitialFrame.Health);
    }

    [Fact]
    public void InvalidStructuralStateOfEitherFighterLeavesJournalUnstarted()
    {
        var config = Config();
        foreach (var animal in new[] { "bear", "kangaroo" })
            Reject(Change(config, fighters: config.Fighters.Select(x => x.Id.Value == animal
                ? Patch(x, ("start_resource", ConfigValue.FromInteger(-1))) : x)), "InvalidInitialState");
    }

    [Fact]
    public void ExplicitVersionedSetupRejectsV01AndDefaultProducerStillRejectsV02()
    {
        var legacy = EngineTestFixture.CreateConfig();
        var journal = new RecordingJournal();
        var request = EngineTestFixture.CreateRequest(engineVersion: BattleSetupFactory.EffectsEngineVersion);
        var result = new CombatEngine(BattleSetupFactory.EffectsEngineVersion).Simulate(request, legacy, journal);
        Assert.Equal(BattleResultStatus.Rejected, result.Status); Assert.Equal(0, journal.BeginCount);
        Assert.Contains(result.RejectionErrors, x => x.Code.Value == "BalanceSchemaVersionMismatch");
        var config = Config(); journal = new RecordingJournal();
        result = new CombatEngine(ContractVersions.HistoricalEngine).Simulate(Request(config), config, journal);
        Assert.Equal(BattleResultStatus.Rejected, result.Status); Assert.Equal(0, journal.BeginCount);
        Assert.Contains(result.RejectionErrors, x => x.Code.Value == "EngineVersionMismatch");
        Assert.Throws<ArgumentOutOfRangeException>(() => new CombatEngine(new ArtifactVersion("battle.core/9.0.0")));
    }

    [Fact]
    public void VersionedBeforeBoundaryEffectCompletesThroughActualJournalLifecycle()
    {
        var baseline = Config();
        var effect = Patch(CustomEffect(baseline, "effect_trial", "trial", "Precision", value: 8),
            ("duration_ticks", ConfigValue.FromInteger(1)), ("expiry_boundary", ConfigValue.FromString("ExpireBeforeTick")));
        var config = Setting(Reachable(Change(baseline, effects: baseline.Effects.Append(effect)), "effect_trial"), "battle.time_limit_ticks", ConfigValue.FromInteger(3));
        var journal = Simulate(config);
        Assert.All(journal.Start!.FighterA.InitialFrame.Effects, _ => Assert.Fail("Initial frames precede BattleStart closure."));
        Assert.Equal(2, journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_trial"));
        var removed = journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved && x.EffectId?.Value == "effect_trial").ToArray();
        Assert.Equal(2, removed.Length);
        Assert.All(removed, x => { Assert.Equal(1, x.Tick); Assert.Equal(EffectRemoveReason.ExpiredBeforeTick, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason); });
        Assert.Equal(CombatEventType.BattleEnded, journal.Drafts[^1].EventType);
    }

    private static BattleRequest Request(CompiledBattleConfig config) => EngineTestFixture.CreateRequest(
        engineVersion: BattleSetupFactory.EffectsEngineVersion, configHash: config.Reference.ConfigHash,
        allowedActions: config.Actions.Where(x => x.TryGetProperty("animal_id", out var owner) &&
            owner.Kind == ConfigValueKind.String && owner.AsString() is "all" or "bear" or "kangaroo").Select(x => x.Id));

    private static RecordingJournal Simulate(CompiledBattleConfig config)
    {
        var journal = new RecordingJournal();
        var result = new CombatEngine(BattleSetupFactory.EffectsEngineVersion).Simulate(Request(config), config, journal);
        Assert.True(result.Status == BattleResultStatus.Completed, string.Join("; ", result.RejectionErrors.Select(x => x.Code + ":" + x.Path)) + result.InvariantFailure?.Message);
        return journal;
    }

    private static BattleResult Reject(CompiledBattleConfig config, string? code = null)
    {
        var journal = new RecordingJournal();
        var result = new CombatEngine(BattleSetupFactory.EffectsEngineVersion).Simulate(Request(config), config, journal);
        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        if (code is not null) Assert.Contains(result.RejectionErrors, x => x.Code.Value == code);
        Assert.Equal(result.RejectionErrors.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Code.Value, StringComparer.Ordinal)
            .ThenBy(x => x.EntityId?.Value, StringComparer.Ordinal), result.RejectionErrors);
        Assert.Equal(0, journal.BeginCount); Assert.Equal(0, journal.CompleteCount); Assert.Empty(journal.Drafts);
        Assert.Null(result.Summary);
        return result;
    }

    private static void RejectPath(CompiledBattleConfig config, string code, string path) =>
        Assert.Contains(Reject(config, code).RejectionErrors, x => x.Code.Value == code && x.Path == path);
}
