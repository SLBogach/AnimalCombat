using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Effects;

// Store plans are tested independently; canonical effect events/closure integration remains WP-10 work.
[Trait("WorkPackage", "WP10")]
public sealed class EffectStoreFoundationTests
{
    private static EffectOrigin Origin(string owner = "fighter_a", string rule = "rule_a", string? action = null) =>
        new(new StableId(owner), action == null ? null : new StableId(action), new StableId(rule));
    private static EffectProfile Profile(string id = "effect_a", string group = "group_a", int duration = 3,
        EffectStackPolicy policy = EffectStackPolicy.AddStacks, int cap = 3, int value = 8,
        EffectCompareKey? compare = null, EffectExpiryBoundary boundary = EffectExpiryBoundary.ExpireBeforeTick,
        EffectRefreshRule refresh = EffectRefreshRule.ResetDuration, int priority = 0, int cooldown = 0,
        int tickCap = 20, int battleCap = 100) => new(new StableId(id), new StableId(group), duration, boundary,
            policy, cap, compare, priority, refresh, EffectSemanticRole.None, cooldown, tickCap, battleCap,
            [new EffectModifier(EffectModifierTarget.Precision, EffectModifierOperation.Add, value, 0)], []);

    [Theory]
    [InlineData(EffectExpiryBoundary.ExpireBeforeTick, 8)]
    [InlineData(EffectExpiryBoundary.ExpireAfterTick, 7)]
    public void HalfOpenExpiryRemainingAndLatestApplicationLineageAreExact(EffectExpiryBoundary boundary, int expiryTick)
    {
        var store = new EffectStore(32);
        var added = Assert.Single(store.Apply(Profile(boundary: boundary), Origin(), 5, EventId.FromSequence(10)));
        Assert.Null(added.Before); Assert.NotNull(added.After); Assert.Null(added.RemoveReason);
        Assert.Equal(8, added.After.EndExclusiveTick);
        for (var tick = 5; tick < 8; tick++) Assert.Equal(8 - tick, Assert.Single(store.Frames(tick)).TicksRemaining);
        Assert.Empty(store.Due(expiryTick - 1, boundary));
        var due = Assert.Single(store.Due(expiryTick, boundary));
        var removal = Assert.Single(store.Remove(due.Profile.EffectId, boundary == EffectExpiryBoundary.ExpireBeforeTick ? EffectRemoveReason.ExpiredBeforeTick : EffectRemoveReason.ExpiredAfterTick));
        Assert.Equal(EventId.FromSequence(10), removal.Before!.LatestApplicationEventId);
        Assert.True(removal.IsRemoval); Assert.Empty(store.Active); Assert.Empty(store.Remove(due.Profile.EffectId, EffectRemoveReason.Dispelled));
    }
    [Fact]
    public void OneTickAfterIsDueAtApplicationTickButBeforeIsNot()
    {
        var store = new EffectStore(32);
        store.Apply(Profile(duration: 1, boundary: EffectExpiryBoundary.ExpireAfterTick), Origin(), 5, EventId.FromSequence(1));
        Assert.Single(store.Due(5, EffectExpiryBoundary.ExpireAfterTick));
        var before = new EffectStore(32);
        before.Apply(Profile(duration: 1), Origin(), 5, EventId.FromSequence(1));
        Assert.Empty(before.Due(5, EffectExpiryBoundary.ExpireBeforeTick));
        Assert.Single(before.Due(6, EffectExpiryBoundary.ExpireBeforeTick));
    }
    [Fact]
    public void StackCapRefreshAndSameExpiryNoopChargeOnlyRealEffectMutations()
    {
        var store = new EffectStore(32);
        var profile = Profile();
        for (var count = 1; count <= 3; count++)
            Assert.Equal(count, Assert.Single(store.Apply(profile, Origin(rule: "rule_" + count), 5, EventId.FromSequence(count))).After!.Stacks);
        var budget = store.Budget(profile.EffectId);
        Assert.Empty(store.Apply(profile, Origin(), 5, EventId.FromSequence(4)));
        Assert.Equal(budget, store.Budget(profile.EffectId));
        var refreshed = Assert.Single(store.Apply(profile, Origin(), 6, EventId.FromSequence(5)));
        Assert.Equal(3, refreshed.Before!.Stacks); Assert.Equal(3, refreshed.After!.Stacks);
        Assert.Equal(9, refreshed.After.EndExclusiveTick); Assert.Equal(EventId.FromSequence(5), refreshed.After.LatestApplicationEventId);
        Assert.Equal(24, Assert.Single(store.Modifiers(EffectModifierTarget.Precision)).Modifier.Value * 3);
        Assert.Single(store.Frames(6));
    }
    [Fact]
    public void RejectDoesNotExtendLifetimeOrConsumeEffectBudget()
    {
        var store = new EffectStore(32);
        var profile = Profile(policy: EffectStackPolicy.Reject);
        store.Apply(profile, Origin(), 5, EventId.FromSequence(1));
        var previous = Assert.Single(store.Active);
        var budget = store.Budget(profile.EffectId);
        Assert.Empty(store.Apply(profile, Origin(), 6, EventId.FromSequence(2)));
        Assert.Equal(previous, Assert.Single(store.Active));
        Assert.Equal(budget, store.Budget(profile.EffectId));
    }
    [Theory]
    [InlineData(EffectRefreshRule.ResetDuration, 8)]
    [InlineData(EffectRefreshRule.KeepLonger, 15)]
    public void RefreshMayShortenOnlyWhenExplicitResetRule(EffectRefreshRule refresh, int end)
    {
        var store = new EffectStore(32);
        store.Apply(Profile(policy: EffectStackPolicy.Refresh, duration: 10, refresh: refresh), Origin(), 5, EventId.FromSequence(1));
        var changes = store.Apply(Profile(policy: EffectStackPolicy.Refresh, duration: 2, refresh: refresh), Origin(), 6, EventId.FromSequence(2));
        Assert.Equal(end, Assert.Single(store.Active).EndExclusiveTick);
        Assert.Equal(refresh == EffectRefreshRule.KeepLonger ? 0 : 1, changes.Count);
        Assert.Equal(1, Assert.Single(store.Active).Stacks);
    }
    [Fact]
    public void ReplaceAtInstanceCapPlansRemoveThenAddWithoutTransientExtraOccupant()
    {
        var store = new EffectStore(32);
        for (var index = 0; index < 32; index++) store.Apply(Profile("effect_" + index, "group_" + index, policy: EffectStackPolicy.Replace), Origin(), 0, EventId.FromSequence(index));
        var mutations = store.Apply(Profile("replacement", "group_0", policy: EffectStackPolicy.Replace), Origin(), 1, EventId.FromSequence(40));
        Assert.Equal(2, mutations.Count);
        Assert.Equal(EffectRemoveReason.Replaced, mutations[0].RemoveReason);
        Assert.Null(mutations[0].After); Assert.Null(mutations[1].Before); Assert.NotNull(mutations[1].After);
        Assert.Equal(32, store.Active.Count); Assert.Equal(32, store.Frames(1).Count);
        var failure = Assert.Throws<EngineInvariantException>(() => store.Apply(Profile("extra", "extra_group"), Origin(), 1, EventId.FromSequence(41)));
        Assert.Equal(EngineFailureCodes.EffectInstanceCapExceeded, failure.Code);
        Assert.Equal(32, store.Active.Count); Assert.Equal(default, store.Budget(new StableId("extra")));
    }
    [Fact]
    public void StrongestUsesSignedDeclaredValueAndCanonicalIdentityInsteadOfRefreshingWeaker()
    {
        var store = new EffectStore(32);
        var weaker = Profile("effect_z", value: -30, policy: EffectStackPolicy.StrongestWins, compare: EffectCompareKey.Value1);
        var stronger = Profile("effect_a", value: -10, policy: EffectStackPolicy.StrongestWins, compare: EffectCompareKey.Value1);
        store.Apply(weaker, Origin(), 5, EventId.FromSequence(1));
        Assert.Equal(2, store.Apply(stronger, Origin(), 6, EventId.FromSequence(3)).Count);
        var incumbent = Assert.Single(store.Active);
        Assert.Empty(store.Apply(weaker, Origin(), 7, EventId.FromSequence(4)));
        Assert.Empty(store.Apply(stronger, Origin(), 7, EventId.FromSequence(5)));
        Assert.Equal(incumbent, Assert.Single(store.Active));
        Assert.Equal(2, store.Apply(stronger, Origin(owner: "fighter_0"), 7, EventId.FromSequence(6)).Count);
        Assert.Equal("fighter_0", Assert.Single(store.Active).Origin.OwnerId.Value);
    }
    [Theory]
    [InlineData(EffectCompareKey.DurationTicks, true)]
    [InlineData(EffectCompareKey.StackCount, false)]
    public void StrongestUsesDeclaredDurationOrStackDomain(EffectCompareKey compare, bool shouldReplace)
    {
        var store = new EffectStore(32);
        store.Apply(Profile("effect_a", duration: 3, policy: EffectStackPolicy.StrongestWins, compare: compare), Origin(), 1, EventId.FromSequence(1));
        var mutations = store.Apply(Profile("effect_b", duration: 8, policy: EffectStackPolicy.StrongestWins, compare: compare), Origin(), 2, EventId.FromSequence(2));
        Assert.Equal(shouldReplace ? 2 : 0, mutations.Count);
        Assert.Equal(shouldReplace ? "effect_b" : "effect_a", Assert.Single(store.Active).Profile.EffectId.Value);
    }
    [Fact]
    public void EffectCooldownAndIndependentTickBattleBudgetsUseExactIntegerBoundaries()
    {
        var store = new EffectStore(32);
        var profile = Profile(cooldown: 5, battleCap: 2);
        store.Apply(profile, Origin(), 10, EventId.FromSequence(1));
        for (var tick = 10; tick < 15; tick++) Assert.Empty(store.Apply(profile, Origin(), tick, EventId.FromSequence(2)));
        Assert.Single(store.Apply(profile, Origin(), 15, EventId.FromSequence(3)));
        Assert.Empty(store.Apply(profile, Origin(), 20, EventId.FromSequence(4)));
        Assert.Equal(2, store.Budget(profile.EffectId).BattleCount);
        var tickStore = new EffectStore(32);
        var perTick = Profile(tickCap: 1);
        Assert.Single(tickStore.Apply(perTick, Origin(), 5, EventId.FromSequence(1)));
        Assert.Empty(tickStore.Apply(perTick, Origin(), 5, EventId.FromSequence(2)));
        Assert.Single(tickStore.Apply(perTick, Origin(), 6, EventId.FromSequence(3)));
        Assert.Equal(1, tickStore.Budget(perTick.EffectId).TickCount);
    }
    [Fact]
    public void PreviewIsolationRollbackAndTimerOverflowDoNotMutateAuthoritativeStore()
    {
        var authoritative = new EffectStore(32);
        var profile = Profile();
        authoritative.Apply(profile, Origin(), 5, EventId.FromSequence(1));
        var previous = Assert.Single(authoritative.Active);
        var preview = authoritative.Clone();
        preview.Apply(profile, Origin(), 6, EventId.FromSequence(2));
        Assert.Equal(1, previous.Stacks); Assert.Equal(previous, Assert.Single(authoritative.Active));
        Assert.Equal(2, Assert.Single(preview.Active).Stacks);
        var budget = preview.Budget(profile.EffectId);
        var active = Assert.Single(preview.Active);
        var failure = Assert.Throws<EngineInvariantException>(() => preview.Apply(profile, Origin(), int.MaxValue, EventId.FromSequence(3)));
        Assert.Equal(EngineFailureCodes.EffectArithmeticOverflow, failure.Code);
        Assert.Equal(budget, preview.Budget(profile.EffectId)); Assert.Equal(active, Assert.Single(preview.Active));
        Assert.Single(authoritative.Frames(8)); Assert.Equal(0, authoritative.Frames(8)[0].TicksRemaining);
    }
    [Fact]
    public void FatigueLookupIsAppliedOnceRatherThanValueAndPerStackProduct()
    {
        var store = new EffectStore(32);
        var profile = new EffectProfile(new StableId("fatigue"), new StableId("fatigue"), 100, EffectExpiryBoundary.ExpireAfterTick,
            EffectStackPolicy.AddStacks, 3, EffectCompareKey.StackCount, 0, EffectRefreshRule.ResetDuration,
            EffectSemanticRole.ControlFatigue, 0, 20, 100,
            [new EffectModifier(EffectModifierTarget.HardControlDuration, EffectModifierOperation.Multiply, 1000, 0)], [1000, 750, 500, 250]);
        foreach (var expected in new[] { 750, 500, 250 })
        {
            store.Apply(profile, Origin(rule: "rule_" + expected), 5, EventId.FromSequence(expected));
            var modifier = Assert.Single(store.Modifiers(EffectModifierTarget.HardControlDuration));
            Assert.Equal(1, modifier.Stacks); Assert.Equal(expected, modifier.Modifier.Value);
            Assert.Equal(expected, EffectStatMath.Compute(1000, new StatBounds(0, 1000), [modifier], 1000));
        }
    }
    [Fact]
    public void PublicMembershipIsOrdinalWhileExpiryPlansUsePriorityThenId()
    {
        var store = new EffectStore(32);
        store.Apply(Profile("z", "group_z", priority: -1), Origin(), 5, EventId.FromSequence(1));
        store.Apply(Profile("b", "group_b", priority: 1), Origin(), 5, EventId.FromSequence(2));
        store.Apply(Profile("a", "group_a", priority: 1), Origin(), 5, EventId.FromSequence(3));
        Assert.Equal(new[] { "a", "b", "z" }, store.Frames(5).Select(x => x.EffectId.Value));
        Assert.Equal(new[] { "z", "a", "b" }, store.Due(8, EffectExpiryBoundary.ExpireBeforeTick).Select(x => x.Profile.EffectId.Value));
    }
    [Fact]
    public void RuntimeInconsistentGroupIsTypedFailureAndLeavesPreviewUnchanged()
    {
        var store = new EffectStore(32);
        store.Apply(Profile(), Origin(), 5, EventId.FromSequence(1));
        var previous = Assert.Single(store.Active);
        var failure = Assert.Throws<EngineInvariantException>(() => store.Apply(Profile("other"), Origin(), 6, EventId.FromSequence(2)));
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, failure.Code);
        Assert.Equal(previous, Assert.Single(store.Active));
    }
}
