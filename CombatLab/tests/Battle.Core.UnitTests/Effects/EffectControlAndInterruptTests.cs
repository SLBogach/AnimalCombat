using Battle.Contracts.Config;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectControlAndInterruptTests
{
    private static ActionInterruptProfile Profile(ActionInterruptKind kind = ActionInterruptKind.Armored,
        HitInterruptStrength minimum = HitInterruptStrength.Heavy) => new(new StableId("action_test"), kind,
            HitInterruptStrength.Light, minimum, [ActionPhase.Startup, ActionPhase.Active], [ActionPhase.Startup],
            kind == ActionInterruptKind.Unstoppable ? [ControlCategory.Stun, ControlCategory.Knockdown] : []);

    [Fact]
    [Trait("AcceptanceId", "WP10-INT-001")]
    public void DataStrengthAndPhaseVocabularyValidateAndThresholdEqualityInterrupts()
    {
        var config = EffectSetupFixture.Config();
        foreach (var patch in new[] { ("hit_interrupt_strength", ConfigValue.FromInteger(-1)),
            ("hit_interrupt_strength", ConfigValue.FromInteger(4)), ("hit_interrupt_min_strength", ConfigValue.FromInteger(0)),
            ("hit_interrupt_min_strength", ConfigValue.FromInteger(4)), ("hit_interruptible_phases", ConfigValue.FromString("startup")),
            ("hit_interruptible_phases", ConfigValue.FromString("Startup|Startup")), ("protected_phases", ConfigValue.FromString("9")),
            ("ignored_control_categories", ConfigValue.FromString("Grab")) })
        {
            var changed = EffectSetupFixture.Change(config, actions: config.Actions.Select(x => x.Id.Value == "bear_earthbreaker" ? EffectSetupFixture.Patch(x, patch) : x));
            var issues = new List<EffectSetupIssue>();
            Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(changed), changed, issues));
            Assert.Contains(issues, x => x.Code == "InvalidInterruptProfile" && x.Path == "/config/actions/bear_earthbreaker/" + patch.Item1);
        }
        for (var strength = 0; strength <= 3; strength++)
        {
            var changed = EffectSetupFixture.Change(config, actions: config.Actions.Select(x => x.Id.Value == "bear_earthbreaker"
                ? EffectSetupFixture.Patch(x, ("hit_interrupt_strength", ConfigValue.FromInteger(strength))) : x));
            var issues = new List<EffectSetupIssue>();
            Assert.NotNull(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(changed), changed, issues));
            Assert.Empty(issues);
        }
        var profile = Profile();
        Assert.False(profile.AllowsHitInterrupt(ActionPhase.Startup, HitInterruptStrength.None));
        Assert.False(profile.AllowsHitInterrupt(ActionPhase.Startup, HitInterruptStrength.Light));
        Assert.False(profile.AllowsHitInterrupt(ActionPhase.Startup, HitInterruptStrength.Medium));
        Assert.True(profile.AllowsHitInterrupt(ActionPhase.Startup, HitInterruptStrength.Heavy));
    }

    [Fact]
    public void ArmoredProtectionUsesOnlyDeclaredPhasesAndRecoveryIsNotAnOrdinaryHitCancel()
    {
        var profile = Profile();
        Assert.False(profile.AllowsHitInterrupt(ActionPhase.Startup, HitInterruptStrength.Medium));
        Assert.True(profile.AllowsHitInterrupt(ActionPhase.Active, HitInterruptStrength.Light));
        Assert.False(profile.AllowsHitInterrupt(ActionPhase.Recovery, HitInterruptStrength.Heavy));
        Assert.True(profile.AllowsControl(ActionPhase.Startup, ControlCategory.Stun));
    }

    [Fact]
    public void UnstoppableProtectsOnlyConfiguredStunAndKnockdownNeverGrabOrDefeat()
    {
        var profile = Profile(ActionInterruptKind.Unstoppable, HitInterruptStrength.Light);
        foreach (var category in new[] { ControlCategory.Stun, ControlCategory.Knockdown })
        {
            Assert.False(profile.AllowsControl(ActionPhase.Startup, category));
            Assert.True(profile.AllowsControl(ActionPhase.Active, category));
            Assert.True(profile.AllowsControl(ActionPhase.Recovery, category));
        }
        Assert.True(profile.AllowsControl(ActionPhase.Startup, ControlCategory.Grab));
        Assert.True(profile.AllowsControl(ActionPhase.Startup, ControlCategory.Defeat));
        Assert.True(profile.AllowsHitInterrupt(ActionPhase.Startup, HitInterruptStrength.Light));
    }

    [Fact]
    public void FilterInputsAreCopiedAndCannotBeMutatedOrIncludeUnknownEnums()
    {
        var phases = new[] { ActionPhase.Startup };
        var ignored = new[] { ControlCategory.Stun };
        var profile = new ActionInterruptProfile(new StableId("action_test"), ActionInterruptKind.Unstoppable,
            HitInterruptStrength.None, HitInterruptStrength.Light, phases, phases, ignored);
        phases[0] = ActionPhase.Recovery; ignored[0] = ControlCategory.Grab;
        Assert.Equal(ActionPhase.Startup, Assert.Single(profile.ProtectedPhases));
        Assert.Equal(ControlCategory.Stun, Assert.Single(profile.IgnoredCategories));
        Assert.Throws<NotSupportedException>(() => ((IList<ActionPhase>)profile.InterruptiblePhases).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.AllowsHitInterrupt((ActionPhase)99, HitInterruptStrength.Heavy));
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.AllowsHitInterrupt(ActionPhase.Startup, (HitInterruptStrength)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.AllowsControl(ActionPhase.Startup, (ControlCategory)99));
        Assert.Throws<ArgumentException>(() => new ActionInterruptProfile(default, ActionInterruptKind.Cancelable,
            HitInterruptStrength.None, HitInterruptStrength.Light, [], [], []));
        Assert.Throws<ArgumentException>(() => new ActionInterruptProfile(new StableId("action_test"), ActionInterruptKind.Unstoppable,
            HitInterruptStrength.None, HitInterruptStrength.Light, [ActionPhase.Startup, ActionPhase.Startup], [], []));
        Assert.Throws<ArgumentException>(() => new ActionInterruptProfile(new StableId("action_test"), ActionInterruptKind.Unstoppable,
            HitInterruptStrength.None, HitInterruptStrength.Light, [], [], [ControlCategory.Defeat]));
        Assert.Throws<ArgumentException>(() => new ActionInterruptProfile(new StableId("action_test"), ActionInterruptKind.Armored,
            HitInterruptStrength.None, HitInterruptStrength.Light, [], [], [ControlCategory.Stun]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActionInterruptProfile(new StableId("action_test"), ActionInterruptKind.Cancelable,
            HitInterruptStrength.None, HitInterruptStrength.None, [], [], []));
        Assert.Throws<ArgumentNullException>(() => new ActionInterruptProfile(new StableId("action_test"), ActionInterruptKind.Cancelable,
            HitInterruptStrength.None, HitInterruptStrength.Light, null!, [], []));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-CTRL-003")]
    public void StunAppliesResistanceThenFatigueThenExistingClampWithoutRng()
    {
        var ratio = EffectControlMath.ControlRatio(150, 100, 100, 1000);
        Assert.Equal(1000, ratio);
        Assert.Equal(new[] { 8, 6, 4, 3 }, new[] { 1000, 750, 500, 250 }
            .Select(fatigue => EffectControlMath.StunTicks(8, ratio, fatigue, 1000, 3, 20)));
        Assert.Equal(1, EffectControlMath.StunTicks(3, 750, 750, 1000, 1, 20)); // floor each fold:3→2→1
    }

    [Fact]
    public void KnockdownHasHalfOpenFallGroundedGetupAndZeroRemainingOnlyAtReady()
    {
        var timeline = EffectControlMath.Knockdown(10, 2, 6, 3, 1000, 1000, 1000);
        Assert.Equal(new KnockdownTimeline(10, 12, 18, 21), timeline);
        for (var tick = 10; tick < 12; tick++) Assert.Equal(KnockdownStage.Fall, timeline.StageAt(tick));
        for (var tick = 12; tick < 18; tick++) Assert.Equal(KnockdownStage.Grounded, timeline.StageAt(tick));
        for (var tick = 18; tick < 21; tick++) Assert.Equal(KnockdownStage.GetUp, timeline.StageAt(tick));
        Assert.Equal(KnockdownStage.Completed, timeline.StageAt(21));
        Assert.Equal(1, timeline.RemainingAt(20)); Assert.Equal(0, timeline.RemainingAt(21));
        Assert.Equal(0, timeline.RemainingAt(100));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.StageAt(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.RemainingAt(9));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-KDN-002")]
    public void Fatigue750KnockdownStagesAreOneFourTwoAndEachStageHasMinimumOneWithCheckedTotal()
    {
        var timeline = EffectControlMath.Knockdown(10, 2, 6, 3, 1000, 750, 1000);
        Assert.Equal(new KnockdownTimeline(10, 11, 15, 17), timeline);
        Assert.Equal(7, timeline.RemainingAt(10));
        Assert.Equal(new KnockdownTimeline(10, 11, 12, 13), EffectControlMath.Knockdown(10, 2, 6, 3, 1000, 0, 1000));
        Assert.Throws<OverflowException>(() => EffectControlMath.Knockdown(int.MaxValue - 1, 2, 6, 3, 1000, 1000, 1000));
        Assert.Throws<OverflowException>(() => EffectControlMath.Knockdown(0, int.MaxValue, 1, 1, 1000, 1000, 1000));
        Assert.Throws<OverflowException>(() => EffectControlMath.Knockdown(0, 100, 1, 1, int.MaxValue, int.MaxValue, 1));
    }

    [Fact]
    public void ControlMathRejectsInvalidInputsAndNeverWrapsIntermediateRatioOrDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.ControlRatio(0, 1, 1, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.ControlRatio(1, -1, 1, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.ControlRatio(1, 1, -1, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.ControlRatio(1, 1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.StunTicks(-1, 1000, 1000, 1000, 3, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.StunTicks(8, -1, 1000, 1000, 3, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.StunTicks(8, 1000, -1, 1000, 3, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.StunTicks(8, 1000, 1000, 0, 3, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.StunTicks(8, 1000, 1000, 1000, 20, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.StunTicks(8, 1000, 1000, 1000, 0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.Knockdown(-1, 2, 6, 3, 1000, 1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.Knockdown(0, 0, 6, 3, 1000, 1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.Knockdown(0, 2, 0, 3, 1000, 1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.Knockdown(0, 2, 6, 0, 1000, 1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.Knockdown(0, 2, -1, 3, 1000, 1000, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectControlMath.Knockdown(0, 2, 6, -1, 1000, 1000, 1000));
        Assert.Throws<OverflowException>(() => EffectControlMath.ControlRatio(int.MaxValue, 1, 0, 1000));
        Assert.Throws<OverflowException>(() => EffectControlMath.StunTicks(int.MaxValue, int.MaxValue, 1000, 1000, 3, 20));
    }
}
