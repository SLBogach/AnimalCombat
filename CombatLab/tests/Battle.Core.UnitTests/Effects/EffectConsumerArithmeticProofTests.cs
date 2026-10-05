using Battle.Contracts.Effects;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Core.Effects;
using Battle.Core.Decisions;
using Battle.Core.Initialization;
using Battle.Core.Resolution;
using Battle.Core.UnitTests.Decisions;
using Battle.Core.UnitTests.Engine;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectConsumerArithmeticProofTests
{
    [Fact]
    public void ActualReachableStatsDoNotUseArbitrarySchemaMaximum()
    {
        var test = new EffectRuntimeFixture([], []);
        Assert.Equal(int.MaxValue, test.Definition.Bounds["Power"].Maximum);
        Assert.Equal(new StatBounds(100, 100), EffectArithmeticProof.ReachableRange(test.Definition,
            test.Definition.Fighters[0], EffectModifierTarget.Power));
        var issues = new List<EffectSetupIssue>();
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings, test.State, issues);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(EffectModifierTarget.ControlPower, int.MaxValue - 100)]
    [InlineData(EffectModifierTarget.ControlResistance, int.MaxValue - 100)]
    [InlineData(EffectModifierTarget.Mass, int.MaxValue - 100)]
    [InlineData(EffectModifierTarget.GrabPriority, int.MaxValue)]
    [InlineData(EffectModifierTarget.Precision, int.MaxValue)]
    public void ReachableConsumerOverflowRejectsControlledSimulateBeforeBegin(EffectModifierTarget target, int value)
    {
        var test = new EffectRuntimeFixture([Effect(target: target, value: value, cap: 1)], [Rule()]);
        AssertPreBeginRejection(test);
    }

    [Fact]
    public void DamageChannelsRejectIntermediateOverflowBeforeFinalDamageCap()
    {
        var test = new EffectRuntimeFixture([Effect("dealt", target: EffectModifierTarget.DamageDealt, value: int.MaxValue,
            operation: EffectModifierOperation.Multiply, cap: 1), Effect("taken", target: EffectModifierTarget.DamageTaken,
            value: int.MaxValue, operation: EffectModifierOperation.Multiply, cap: 1)], [Rule("dealt", "dealt"), Rule("taken", "taken")]);
        AssertPreBeginRejection(test);
    }

    [Fact]
    public void CombinedWeightChannelsRejectEvenIfIndividualChannelsFit()
    {
        var test = new EffectRuntimeFixture([Effect("block", target: EffectModifierTarget.BlockWeight, value: 2_000_000,
            operation: EffectModifierOperation.Multiply, cap: 1), Effect("punish", target: EffectModifierTarget.PunishWeight,
            value: 2_000_000, operation: EffectModifierOperation.Multiply, cap: 1)], []);
        Assert.Equal(2_000_000, EffectArithmeticProof.ReachableRange(test.Definition, test.Definition.Fighters[0], EffectModifierTarget.BlockWeight).Maximum);
        Assert.Throws<OverflowException>(() => EffectArithmeticProof.WeightUpper(test.Definition, ["block", "punish"]));
    }

    [Theory]
    [InlineData(EffectModifierOperation.Add, -100, 0, 100)]
    [InlineData(EffectModifierOperation.Add, 100, 100, 200)]
    [InlineData(EffectModifierOperation.Multiply, 1500, 0, 150)]
    [InlineData(EffectModifierOperation.Override, 900, 100, 900)]
    public void ReachableRangeIncludesAbsentAndAppliedProfile(EffectModifierOperation operation, int value, int minimum, int maximum)
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.Power, value: value, operation: operation, cap: 1)], []);
        Assert.Equal(new StatBounds(minimum, maximum), EffectArithmeticProof.ReachableRange(test.Definition,
            test.Definition.Fighters[0], EffectModifierTarget.Power));
    }

    [Fact]
    public void MutuallyExclusiveProfilesUseOnePerGroupAdditiveEnvelope()
    {
        var test = new EffectRuntimeFixture([Effect("a", "shared", value: 200, target: EffectModifierTarget.Power,
            policy: EffectStackPolicy.Replace, cap: 1), Effect("b", "shared", value: 300, target: EffectModifierTarget.Power,
            policy: EffectStackPolicy.Replace, cap: 1)], []);
        Assert.Equal(new StatBounds(100, 400), EffectArithmeticProof.ReachableRange(test.Definition,
            test.Definition.Fighters[0], EffectModifierTarget.Power));
    }

    [Fact]
    public void GearOverrideDoesNotConcealOverflowAndClampedEffectRangeIsBounded()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.Power, value: int.MaxValue, cap: 1)], []);
        Assert.Throws<OverflowException>(() => EffectArithmeticProof.ReachableRange(test.Definition,
            test.Definition.Fighters[0], EffectModifierTarget.Power));
        var gear = new[] { new InitialStatSource("Power", 0, new StableId("gear_a"), 0, EffectModifierOperation.Override, 500) };
        var fighter = new EffectFighterDefinition(FighterId.FighterA, test.Definition.Fighters[0].BaseStats.ToDictionary(x => x.Key, x => x.Value),
            gear, test.Definition.Fighters[0].InitialStats.ToDictionary(x => x.Key, x => x.Value), []);
        Assert.Throws<OverflowException>(() => EffectArithmeticProof.ReachableRange(test.Definition, fighter, EffectModifierTarget.Power));
    }

    [Fact]
    public void PhysicalV02SnapshotPassesConsumerProofAgainstCurrentMaterializedRuntime()
    {
        var config = EffectSetupFixture.Config();
        var issues = new List<EffectSetupIssue>();
        var definition = EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(config), config, issues);
        Assert.Empty(issues); Assert.NotNull(definition);
        var setup = EngineTestFixture.CreateSetup(100);
        EffectConsumerArithmeticProof.Validate(definition!, setup.Settings, setup.State, issues);
        Assert.Empty(issues);
        // This is a proof unit, not production engine0.5 acceptance or replay integration.
    }

    [Fact]
    public void ScaleMismatchIsRejectedBeforeAnyJournalCalls()
    {
        var test = new EffectRuntimeFixture([], []);
        var d = test.Definition;
        var changed = new EffectRuntimeDefinition(1001, d.TimeLimitTicks, d.MaximumTriggerDepth, d.MaximumTriggersPerTick,
            d.MaximumInstancesPerFighter, d.FatigueThreshold, d.KnockdownFallTicks, d.KnockdownGroundedTicks, d.KnockdownGetupTicks,
            d.StunMinimumTicks, d.StunMaximumTicks, d.ControlK, d.Bounds.ToDictionary(x => x.Key, x => x.Value), d.Effects, d.Rules, d.Interrupts, d.Fighters);
        var journal = new RecordingJournal();
        var result = new CombatEngine(changed).Simulate(EngineTestFixture.CreateRequest(), EngineTestFixture.CreateConfig(), journal);
        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Equal("EffectScaleMismatch", Assert.Single(result.RejectionErrors).Code.Value);
        Assert.Equal(0, journal.BeginCount); Assert.Empty(journal.Drafts);
    }

    [Fact]
    public void TimingDivisionRejectsBeforeConfiguredMaximumCouldHideOverflow()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.ActionSpeed, value: -99, cap: 1)], []);
        var original = test.Settings.Decisions;
        var action = DecisionTestFixture.Action(original.FighterA.BuildView.SpecialActionIds[0].Value,
            slot: DecisionActionSlot.Special, startupBaseTicks: 1_500_000_000, startupMaximumTicks: 1_500_000_000);
        var runtime = CopyRuntime(original, actions: original.Actions.Select(x => x.Id == action.Id ? action : x));
        var issues = new List<EffectSetupIssue>();
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { Decisions = runtime }, test.State, issues);
        Assert.Contains(issues, x => x.Path.EndsWith("/timing", StringComparison.Ordinal));
    }

    [Fact]
    public void SixStageDecisionIntermediatesRejectBeforeFinalWeightMaximumClamp()
    {
        var test = new EffectRuntimeFixture([], []);
        var original = test.Settings.Decisions;
        var p = original.FighterA;
        var profile = new DecisionFighterProfile(p.Build, p.BuildView, p.Tactic with { BlockFixedPoint = 3000, CounterFixedPoint = 3000,
            LowHealthFixedPoint = 3000, TargetRecoveryFixedPoint = 3000 }, p.Passive, p.OffenseGear, p.DefenseGear, p.UtilityGear, p.LowHealthThresholdFixedPoint);
        var action = DecisionTestFixture.Action(p.BuildView.SpecialActionIds[0].Value, slot: DecisionActionSlot.Special,
            tags: ["block", "counter"], baseWeight: 100_000_000);
        var runtime = CopyRuntime(original, actions: original.Actions.Select(x => x.Id == action.Id ? action : x), fighterA: profile);
        var issues = new List<EffectSetupIssue>();
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { Decisions = runtime }, test.State, issues);
        Assert.Contains(issues, x => x.Path.EndsWith("/decision", StringComparison.Ordinal));
    }

    [Fact]
    public void ForceDenominatorAndControlTimelineOverflowAreChecked()
    {
        var test = new EffectRuntimeFixture([], []);
        var issues = new List<EffectSetupIssue>();
        var changed = new ResolutionRuntimeSettings(test.Settings.Resolution.Global with { ForceK = int.MaxValue }, test.Settings.Resolution.Actions);
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { Resolution = changed }, test.State, issues);
        Assert.Contains(issues, x => x.Path.EndsWith("/resolution", StringComparison.Ordinal));
        issues.Clear();
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { TimeLimitTicks = int.MaxValue }, test.State, issues);
        Assert.Contains(issues, x => x.Path.EndsWith("/timing", StringComparison.Ordinal));
        Assert.Contains(issues, x => x.Path.EndsWith("/resolution", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(int.MaxValue, 32)]
    [InlineData(128, int.MaxValue)]
    public void CorruptedInternalQueueAndCleanupReserveCannotOverflowBudgetArithmetic(int triggers, int instances)
    {
        var test = new EffectRuntimeFixture([], []);
        var d = test.Definition;
        var changed = new EffectRuntimeDefinition(d.FixedPointScale, d.TimeLimitTicks, d.MaximumTriggerDepth,
            triggers, instances, d.FatigueThreshold, d.KnockdownFallTicks, d.KnockdownGroundedTicks, d.KnockdownGetupTicks,
            d.StunMinimumTicks, d.StunMaximumTicks, d.ControlK, d.Bounds.ToDictionary(x => x.Key, x => x.Value),
            d.Effects, d.Rules, d.Interrupts, d.Fighters);
        var issues = new List<EffectSetupIssue>();
        EffectConsumerArithmeticProof.Validate(changed, test.Settings, test.State, issues);
        Assert.Contains(issues, x => x.Code == "EffectArithmeticOverflowRisk" && x.Path == "/config/control/event_reserve");
    }

    [Fact]
    public void DisallowedCatalogEntryIsExcludedFromConsumerReachability()
    {
        var test = new EffectRuntimeFixture([], []);
        var original = test.Settings.Decisions;
        var unreachable = DecisionTestFixture.Action("not_allowed", startupBaseTicks: int.MaxValue, startupMaximumTicks: int.MaxValue);
        var runtime = CopyRuntime(original, actions: original.Actions.Append(unreachable));
        var issues = new List<EffectSetupIssue>();
        // No resolution profile exists for this entry; neither timing nor resolution may read it.
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { Decisions = runtime }, test.State, issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void TypedDecisionFoldOverflowBecomesPreStartRiskRatherThanEscapingAsInvariant()
    {
        var test = new EffectRuntimeFixture([], []);
        var original = test.Settings.Decisions;
        var p = original.FighterA;
        var profile = new DecisionFighterProfile(p.Build, p.BuildView,
            p.Tactic with { BlockFixedPoint = int.MaxValue, GrabFixedPoint = int.MaxValue },
            p.Passive, p.OffenseGear, p.DefenseGear, p.UtilityGear, p.LowHealthThresholdFixedPoint);
        var action = DecisionTestFixture.Action(p.BuildView.SpecialActionIds[0].Value,
            slot: DecisionActionSlot.Special, tags: ["block", "grab"]);
        var runtime = CopyRuntime(original, actions: original.Actions.Select(x => x.Id == action.Id ? action : x),
            fighterA: profile, weights: new DecisionWeightSettings(1000, 0, int.MaxValue, 100_000_000));
        var issues = new List<EffectSetupIssue>();
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { Decisions = runtime }, test.State, issues);
        Assert.Contains(issues, x => x.Code == "EffectArithmeticOverflowRisk" && x.Path == "/fighters/0/actions/" + action.Id + "/decision");
    }

    private static DecisionRuntimeSettings CopyRuntime(DecisionRuntimeSettings r, IEnumerable<DecisionActionProfile>? actions = null,
        DecisionFighterProfile? fighterA = null, DecisionWeightSettings? weights = null) => new(r.BattleId, r.EngineVersion, r.MasterSeed, r.ConfigHash, r.ModeRules,
            actions ?? r.Actions, fighterA ?? r.FighterA, r.FighterB, r.Availability, weights ?? r.Weights, r.Timing,
            r.RepeatSameActionFixedPoint, r.RepeatSameCategoryFixedPoint, r.OpportunityGrowthFixedPoint,
            r.OpportunityCapFixedPoint, r.HardOpportunityMisses, r.WallZoneSize);

    private static void AssertPreBeginRejection(EffectRuntimeFixture test)
    {
        var journal = new RecordingJournal();
        var config = EngineTestFixture.CreateConfig(changeActions: actions => actions.Select(action => action.Id.Value == "bear_earthbreaker"
            ? EffectSetupFixture.Patch(action, ("grab_priority", ConfigValue.FromInteger(1)),
                ("tags", ConfigValue.FromString("grab|special")), ("hit_schedule", ConfigValue.FromString("grab:0")),
                ("hit_count", ConfigValue.FromInteger(0))) : action));
        var result = new CombatEngine(test.Definition).Simulate(EngineTestFixture.CreateRequest(), config, journal);
        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.All(result.RejectionErrors, x => Assert.Equal("EffectArithmeticOverflowRisk", x.Code.Value));
        Assert.Equal(result.RejectionErrors.OrderBy(x => x.Path, StringComparer.Ordinal).Select(x => x.Path), result.RejectionErrors.Select(x => x.Path));
        Assert.Equal(0, journal.BeginCount); Assert.Equal(0, journal.CompleteCount); Assert.Empty(journal.Drafts);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
        Assert.Equal(0UL, test.State.Rng.Decision.NextDrawIndex);
    }
}
