using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectArithmeticGuardTests
{
    private static EffectRuntimeDefinition WithGear(EffectRuntimeDefinition d, int multiplier) =>
        new(d.FixedPointScale, d.TimeLimitTicks, d.MaximumTriggerDepth, d.MaximumTriggersPerTick, d.MaximumInstancesPerFighter,
            d.FatigueThreshold, d.KnockdownFallTicks, d.KnockdownGroundedTicks, d.KnockdownGetupTicks, d.StunMinimumTicks,
            d.StunMaximumTicks, d.ControlK, d.Bounds.ToDictionary(x => x.Key,
                x => x.Key is "ControlPower" or "ControlResistance" ? new StatBounds(0, 1000) : x.Value), d.Effects, d.Rules, d.Interrupts,
            d.Fighters.Select(x => new EffectFighterDefinition(x.Fighter, x.BaseStats.ToDictionary(y => y.Key, y => y.Value),
                [new InitialStatSource("Power", 0, new StableId("gear_test"), 0, EffectModifierOperation.Multiply, multiplier)],
                x.InitialStats.ToDictionary(y => y.Key, y => y.Value), x.SelectedActions)));

    [Fact]
    public void NullProofInputsAreRejectedAndZeroProductNeedsNoEffectFold()
    {
        var definition = new EffectRuntimeFixture([], []).Definition;
        Assert.Throws<ArgumentNullException>(() => EffectArithmeticProof.Validate(null!, new List<EffectSetupIssue>()));
        Assert.Throws<ArgumentNullException>(() => EffectArithmeticProof.Validate(definition, null!));
        var issues = new List<EffectSetupIssue>();
        EffectArithmeticProof.Validate(WithGear(definition, 0), issues); Assert.Empty(issues);
    }

    [Fact]
    public void PreClampMagnitudeAndNegativeSignedFoldAreConservativelyChecked()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.Power, value: 1_200_000_000,
            policy: Battle.Contracts.Events.EffectStackPolicy.Refresh)], []);
        var issues = new List<EffectSetupIssue>(); EffectArithmeticProof.Validate(WithGear(test.Definition, 2000), issues);
        Assert.Contains(issues, x => x.Code == "EffectArithmeticOverflowRisk" && x.Path.EndsWith("/stats/Power", StringComparison.Ordinal));
        var signed = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.Power, operation: EffectModifierOperation.Multiply,
            value: -1500, policy: Battle.Contracts.Events.EffectStackPolicy.Refresh)], []);
        var signedDefinition = WithGear(signed.Definition, 1000);
        var signedIssues = new List<EffectSetupIssue>(); EffectArithmeticProof.Validate(signedDefinition, signedIssues); Assert.Empty(signedIssues);
        Assert.Equal(new StatBounds(0, 151), EffectArithmeticProof.ReachableRange(signedDefinition, signedDefinition.Fighters[0], EffectModifierTarget.Power));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalEnemySpecialIsUnreachableWithoutExplicitBuildSelection(bool corruptOwner)
    {
        var test = new EffectRuntimeFixture([], []);
        var issues = new List<EffectSetupIssue>();
        // Fighter B's kit is present in the catalog but is not Fighter A's selected special kit.
        var fighter = test.Settings.Decisions.GetFighter(FighterId.FighterA);
        var settings = test.Settings.Decisions;
        var action = settings.Actions.First(x => x.Slot == Battle.Core.Decisions.DecisionActionSlot.Special && settings.Availability.IsActionAllowed(x.Id));
        var duplicate = Battle.Core.UnitTests.Decisions.DecisionTestFixture.Action(action.Id.Value,
            ownerAnimalId: fighter.BuildView.AnimalId, slot: Battle.Core.Decisions.DecisionActionSlot.Special);
        if (!corruptOwner && fighter.BuildView.SpecialActionIds.Contains(duplicate.Id))
            duplicate = Battle.Core.UnitTests.Decisions.DecisionTestFixture.Action(action.Id.Value,
                ownerAnimalId: settings.FighterB.BuildView.AnimalId, slot: Battle.Core.Decisions.DecisionActionSlot.Special);
        if (corruptOwner)
            // Non-system null ownership cannot pass typed construction; inject it only for this defensive reachability check.
            typeof(Battle.Core.Decisions.DecisionActionProfile).GetField("<OwnerAnimalId>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(duplicate, null);
        var runtime = new Battle.Core.Decisions.DecisionRuntimeSettings(settings.BattleId, settings.EngineVersion, settings.MasterSeed,
            settings.ConfigHash, settings.ModeRules, settings.Actions.Where(x => x.Id != duplicate.Id).Append(duplicate),
            settings.FighterA, settings.FighterB, settings.Availability, settings.Weights, settings.Timing,
            settings.RepeatSameActionFixedPoint, settings.RepeatSameCategoryFixedPoint, settings.OpportunityGrowthFixedPoint,
            settings.OpportunityCapFixedPoint, settings.HardOpportunityMisses, settings.WallZoneSize);
        EffectConsumerArithmeticProof.Validate(test.Definition, test.Settings with { Decisions = runtime }, test.State, issues);
        Assert.Empty(issues);
    }
}
