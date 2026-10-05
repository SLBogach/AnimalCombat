using System.Reflection;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectStoreGuardTests
{
    private static EffectProfile Profile(string id = "effect_a", string group = "group_a", EffectStackPolicy policy = EffectStackPolicy.AddStacks,
        EffectCompareKey? compare = null, EffectModifierTarget target = EffectModifierTarget.Precision, int tickCap = 20, int battleCap = 100, int cooldown = 0) =>
        new(new StableId(id), new StableId(group), 3, EffectExpiryBoundary.ExpireBeforeTick, policy, 3, compare, 0,
            EffectRefreshRule.ResetDuration, EffectSemanticRole.None, cooldown, tickCap, battleCap,
            [new EffectModifier(target, EffectModifierOperation.Add, 8, 0)], []);
    private static EffectOrigin Origin => new(new StableId("owner"), null, new StableId("rule_a"));
    private static EventId Event => EventId.FromSequence(0);

    [Theory]
    [InlineData(0)]
    [InlineData(129)]
    public void InstanceCapacityBoundsRejectBeforeAllocation(int cap) => Assert.Throws<ArgumentOutOfRangeException>(() => new EffectStore(cap));

    [Theory]
    [InlineData("null")]
    [InlineData("tick")]
    [InlineData("owner")]
    [InlineData("rule")]
    [InlineData("action")]
    [InlineData("event")]
    public void InvalidApplicationCannotMutateMembershipOrChargeBudgets(string invalid)
    {
        var store = new EffectStore(1); var profile = Profile(); var origin = Origin; var tick = 0; var application = Event;
        switch (invalid)
        {
            case "null": profile = null!; break;
            case "tick": tick = -1; break;
            case "owner": origin = origin with { OwnerId = default }; break;
            case "rule": origin = origin with { RuleId = default }; break;
            case "action": origin = origin with { ActionId = default(StableId) }; break;
            case "event": application = default; break;
        }
        Assert.ThrowsAny<ArgumentException>(() => store.Apply(profile, origin, tick, application));
        Assert.Empty(store.Active); Assert.Equal(default, store.Budget(new StableId("effect_a")));
    }

    [Fact]
    public void ValidActionOriginAndEveryIndependentCanApplyBudgetBoundaryAreExplicit()
    {
        var store = new EffectStore(128); var profile = Profile(tickCap: 1, battleCap: 2);
        Assert.True(store.CanApply(profile, 0));
        Assert.Single(store.Apply(profile, Origin with { ActionId = new StableId("action_a") }, 0, Event));
        Assert.False(store.CanApply(profile, 0)); Assert.True(store.CanApply(profile, 1));
        Assert.Single(store.Apply(profile, Origin, 1, EventId.FromSequence(1)));
        Assert.False(store.CanApply(profile, 2));
        var cooling = new EffectStore(1); var cooldown = Profile(cooldown: 5);
        cooling.Apply(cooldown, Origin, 0, Event);
        Assert.False(cooling.CanApply(cooldown, 4)); Assert.True(cooling.CanApply(cooldown, 5));
    }

    [Fact]
    public void EmptyProjectionAndChangedPreconditionAreTypedFailuresWithoutPublishing()
    {
        var store = new EffectStore(1);
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => store.Project(default)).Code);
        var plan = Assert.Single(store.Clone().Apply(Profile(), Origin, 0, Event));
        store.Project(plan); var active = Assert.Single(store.Active);
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => store.Project(plan)).Code);
        Assert.Equal(active, Assert.Single(store.Active));
        store.Project(new EffectMutation(active, null, EffectRemoveReason.Dispelled)); Assert.Empty(store.Active);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("due-tick")]
    [InlineData("due-boundary")]
    public void InvalidRemovalAndExpiryQueriesPreserveMembership(string invalid)
    {
        var store = new EffectStore(1); store.Apply(Profile(), Origin, 0, Event); var active = Assert.Single(store.Active);
        if (invalid == "remove") Assert.Throws<ArgumentOutOfRangeException>(() => store.Remove(active.Profile.EffectId, (EffectRemoveReason)99));
        else Assert.Throws<ArgumentOutOfRangeException>(() => store.Due(invalid == "due-tick" ? -1 : 0,
            invalid == "due-boundary" ? (EffectExpiryBoundary)99 : EffectExpiryBoundary.ExpireBeforeTick));
        Assert.Equal(active, Assert.Single(store.Active));
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("compare")]
    [InlineData("targets")]
    [InlineData("duplicate")]
    public void IncompatibleOrDuplicatePublicIdentityNeverPartiallyReplaces(string invalid)
    {
        var store = new EffectStore(2); var first = Profile(policy: EffectStackPolicy.Replace);
        store.Apply(first, Origin, 0, Event); var active = Assert.Single(store.Active);
        var incoming = invalid switch
        {
            "policy" => Profile(policy: EffectStackPolicy.Refresh),
            "compare" => Profile(policy: EffectStackPolicy.Replace, compare: EffectCompareKey.DurationTicks),
            "targets" => Profile(policy: EffectStackPolicy.Replace, target: EffectModifierTarget.Armor),
            _ => Profile(group: "different_group", policy: EffectStackPolicy.Replace),
        };
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => store.Apply(incoming, Origin, 1, EventId.FromSequence(1))).Code);
        Assert.Equal(active, Assert.Single(store.Active)); Assert.Equal(1, store.Budget(first.EffectId).BattleCount);
    }

    [Theory]
    [InlineData("StackPolicy")]
    [InlineData("CompareKey")]
    public void CorruptedInternalEnumRemainsTypedFailureRatherThanFallback(string property)
    {
        // Fault injection bypasses the typed constructor exclusively to exercise runtime corruption guards.
        var profile = Profile(policy: EffectStackPolicy.StrongestWins, compare: EffectCompareKey.DurationTicks);
        var field = typeof(EffectProfile).GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(profile, property == "StackPolicy" ? (object)(EffectStackPolicy)99 : (EffectCompareKey?)((EffectCompareKey)99));
        var store = new EffectStore(1); store.Apply(profile, Origin, 0, Event); var active = Assert.Single(store.Active);
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => store.Apply(profile, Origin, 1, EventId.FromSequence(1))).Code);
        Assert.Equal(active, Assert.Single(store.Active));
    }
}
