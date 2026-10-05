using Battle.Contracts.Effects;
using Battle.Contracts.Ids;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10EffectContractTests
{
    [Theory]
    [InlineData(-20, 0)]
    [InlineData(0, 0)]
    [InlineData(7, 7)]
    [InlineData(1000, 1000)]
    [InlineData(1001, 1000)]
    public void ExplicitBoundsClampAtInclusiveEndpoints(int value, int expected) =>
        Assert.Equal(expected, new StatBounds(0, 1000).Clamp(value));

    [Fact]
    public void InvertedBoundsRejectAndSingletonAndSignedDomainsAreExplicit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StatBounds(2, 1));
        Assert.Equal(5, new StatBounds(5, 5).Clamp(100));
        Assert.Equal(-2, new StatBounds(-3, -1).Clamp(-2));
    }

    [Fact]
    public void TypedRuleRetainsExactMetadataWithoutMutableState()
    {
        var rule = Rule(priority: int.MinValue);
        Assert.Equal(new StableId("rule_test"), rule.RuleId);
        Assert.Equal(EffectOwnerKind.Global, rule.OwnerKind);
        Assert.Equal(new StableId("global"), rule.OwnerId);
        Assert.Equal(EffectTrigger.ControlEnded, rule.Trigger);
        Assert.Equal(EffectRecipient.Self, rule.Recipient);
        Assert.Equal(EffectCondition.LivingTarget, rule.Condition);
        Assert.Equal(EffectPrimitive.ApplyEffect, rule.Primitive);
        Assert.Equal(new StableId("effect_control_fatigue"), rule.EffectId);
        Assert.Equal(int.MinValue, rule.Priority);
        Assert.Equal(0, rule.InternalCooldownTicks);
        Assert.Equal(1, rule.MaxActivationsPerTick);
        Assert.Equal(999, rule.MaxActivationsPerBattle);
        Assert.True(rule.OncePerEvent);
    }

    [Fact]
    public void TypedRuleRejectsInvalidIdentityEnumsBudgetsAndOwnerLiteral()
    {
        Assert.Throws<ArgumentException>(() => Rule(id: default(StableId)));
        Assert.Throws<ArgumentException>(() => Rule(owner: default(StableId)));
        Assert.Throws<ArgumentException>(() => Rule(effect: default(StableId)));
        Assert.Throws<ArgumentException>(() => Rule(owner: new StableId("not_global")));
        Assert.Throws<ArgumentException>(() => Rule(kind: EffectOwnerKind.Action));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(kind: (EffectOwnerKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(trigger: (EffectTrigger)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(recipient: (EffectRecipient)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(condition: (EffectCondition)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(primitive: (EffectPrimitive)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(cooldown: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(tickCap: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rule(battleCap: 0));
        Assert.Throws<ArgumentException>(() => Rule(once: false));
        Assert.Equal(EffectOwnerKind.Action, Rule(kind: EffectOwnerKind.Action, owner: new StableId("action_test")).OwnerKind);
        Assert.Equal(EffectOwnerKind.Effect, Rule(kind: EffectOwnerKind.Effect, owner: new StableId("effect_test")).OwnerKind);
    }

    private static EffectRuleProfile Rule(StableId? id = null, EffectOwnerKind kind = EffectOwnerKind.Global,
        StableId? owner = null, EffectTrigger trigger = EffectTrigger.ControlEnded,
        EffectRecipient recipient = EffectRecipient.Self, EffectCondition condition = EffectCondition.LivingTarget,
        EffectPrimitive primitive = EffectPrimitive.ApplyEffect, StableId? effect = null,
        int priority = 0, int cooldown = 0, int tickCap = 1, int battleCap = 999, bool once = true) => new(
            id ?? new StableId("rule_test"), kind, owner ?? new StableId("global"), trigger, recipient,
            condition, primitive, effect ?? new StableId("effect_control_fatigue"), priority, cooldown, tickCap, battleCap, once);
}
