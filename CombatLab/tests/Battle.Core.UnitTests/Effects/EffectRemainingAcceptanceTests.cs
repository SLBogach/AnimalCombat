using Battle.Contracts.Config;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Safety;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectRemainingAcceptanceTests
{
    [Fact, Trait("AcceptanceId", "WP10-STACK-007")]
    public void StrongestComparisonUsesDeclaredDurationOrStackDomainNotRemainingOrModifierValue()
    {
        var origin = new EffectOrigin(new StableId("fighter_a"), null, new StableId("rule_a"));
        var store = new EffectStore(32);
        store.Apply(Effect("effect_a", "group", duration: 10, policy: EffectStackPolicy.StrongestWins,
            compare: EffectCompareKey.DurationTicks, value: -100), origin, 0, EventId.FromSequence(1));
        Assert.Empty(store.Apply(Effect("effect_b", "group", duration: 8, policy: EffectStackPolicy.StrongestWins,
            compare: EffectCompareKey.DurationTicks, value: 1000), origin, 9, EventId.FromSequence(2)));
        Assert.Equal(10, Assert.Single(store.Active).EndExclusiveTick);
        Assert.Equal(2, store.Apply(Effect("effect_b", "group", duration: 11, policy: EffectStackPolicy.StrongestWins,
            compare: EffectCompareKey.DurationTicks, value: -1000), origin, 9, EventId.FromSequence(3)).Count);
        Assert.Equal(20, Assert.Single(store.Active).EndExclusiveTick);
        store = new EffectStore(32);
        store.Apply(Effect("effect_a", "group", duration: 3, policy: EffectStackPolicy.StrongestWins,
            compare: EffectCompareKey.StackCount, value: -100), origin, 0, EventId.FromSequence(1));
        Assert.Empty(store.Apply(Effect("effect_b", "group", duration: 100, policy: EffectStackPolicy.StrongestWins,
            compare: EffectCompareKey.StackCount, value: 1000), origin, 1, EventId.FromSequence(2)));
        Assert.Equal("effect_a", Assert.Single(store.Active).Profile.EffectId.Value);
        Assert.Throws<ArgumentException>(() => Effect(policy: EffectStackPolicy.StrongestWins));
        var config = EffectSetupFixture.Config();
        var invalid = EffectSetupFixture.Effect(config, "effect_bear_thick_hide", ("compare_key", ConfigValue.FromString("RemainingTicks")));
        var issues = new List<EffectSetupIssue>();
        Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(invalid), invalid, issues));
        Assert.Contains(issues, x => x.Code == "InvalidEffectGroup" && x.Path.EndsWith("/compare_key", StringComparison.Ordinal));
    }

    [Theory, Trait("AcceptanceId", "WP10-STACK-011")]
    [InlineData(EffectStackPolicy.Replace), InlineData(EffectStackPolicy.StrongestWins)]
    public void CompatibleMultiProfileGroupHasOnePublicOccupantButRefreshOrAddStacksCannotShareId(EffectStackPolicy policy)
    {
        var test = new EffectRuntimeFixture(
            [Effect("effect_a", "group", policy: policy, value: 10, compare: EffectCompareKey.Value1),
             Effect("effect_b", "group", policy: policy, value: 20, compare: EffectCompareKey.Value1)],
            [Rule("rule_a", "effect_a"), Rule("rule_b", "effect_b")]);
        test.Start();
        foreach (var fighter in new[] { FighterId.FighterA, FighterId.FighterB })
        {
            Assert.Equal("effect_b", Assert.Single(test.Runtime.Store(fighter).Active).Profile.EffectId.Value);
            Assert.Equal("effect_b", Assert.Single(test.State.Get(fighter).Effects).EffectId.Value);
        }
        Assert.All(test.Journal.Drafts.Where(x => x.EffectId.HasValue), x =>
        { Assert.InRange(x.Before.Actor!.Effects.Count, 0, 1); Assert.InRange(x.After.Actor!.Effects.Count, 0, 1); });
        foreach (var invalidPolicy in new[] { "Refresh", "AddStacks" })
        {
            var config = EffectSetupFixture.Config();
            var a = EffectSetupFixture.CustomEffect(config, "effect_a", "shared", "Armor", policy: invalidPolicy);
            var b = EffectSetupFixture.CustomEffect(config, "effect_b", "shared", "Armor", policy: invalidPolicy);
            var invalid = EffectSetupFixture.Change(config, effects: config.Effects.Concat([a, b]));
            var issues = new List<EffectSetupIssue>();
            Assert.Null(EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(invalid), invalid, issues));
            Assert.Contains(issues, x => x.Code == "InvalidEffectGroup" && x.Path.EndsWith("/stack_group", StringComparison.Ordinal));
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-TRG-003")]
    public void LivingConditionAndOwnerEligibilityRejectBeforeEnqueueWithoutDormantPassiveActivation()
    {
        var test = new EffectRuntimeFixture([Effect(), Effect("subscriber")],
            [Rule(trigger: EffectTrigger.DamageTaken, condition: EffectCondition.PositiveDamage),
             Rule("rule_subscriber", "subscriber", EffectTrigger.DamageTaken, EffectOwnerKind.Effect, "effect_a"),
             Rule("rule_unselected", trigger: EffectTrigger.DamageTaken, ownerKind: EffectOwnerKind.Action, owner: "unselected_action")]);
        test.Start(); test.Pulse(action: "unselected_action");
        Assert.Equal(0, test.Runtime.Queue.AdmittedThisTick); Assert.Equal(0, test.Runtime.Queue.PendingCount);
        Assert.Empty(test.State.FighterA.Effects); Assert.Empty(test.State.FighterB.Effects);
        Assert.Equal(default, test.Runtime.Queue.Budget(FighterId.FighterA, new StableId("rule_unselected")));
        test.State.FighterB.SetHealthForTesting(0); test.Pulse();
        Assert.Equal(0, test.Runtime.Queue.AdmittedThisTick); Assert.Contains(EffectAdmission.ConditionRejected, test.Runtime.Diagnostics);
        // Actual v0.2 setup retains the selected passive as dormant, without a text-driven binding.
        var config = EffectSetupFixture.Config(); var issues = new List<EffectSetupIssue>();
        var definition = EffectSetupMaterializer.TryCreate(EffectSetupFixture.Request(config), config, issues);
        Assert.NotNull(definition); Assert.Empty(issues);
        Assert.DoesNotContain(definition.Effects, x => x.EffectId.Value == "effect_bear_thick_hide");
        Assert.Equal(0UL, test.State.Rng.Decision.NextDrawIndex); Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
    }

    [Fact, Trait("AcceptanceId", "WP10-TRG-007")]
    public void IndependentRuleAndEffectTickBattleCapsAndAdmittedNoopChargeExactlyTheirOwnBudget()
    {
        var effect = new StableId("effect_a"); var rule = new StableId("rule_a");
        var test = new EffectRuntimeFixture([Effect(tickCap: 1, battleCap: 2)], [Rule(trigger: EffectTrigger.DamageTaken, tickCap: 2, battleCap: 3)]);
        test.Start(); test.Pulse(); test.Pulse();
        Assert.Equal(1, test.Runtime.Queue.Budget(FighterId.FighterB, rule).TickCount);
        Assert.Equal(1, test.Runtime.Store(FighterId.FighterB).Budget(effect).TickCount);
        Assert.Contains(EffectAdmission.EffectBudgetRejected, test.Runtime.Diagnostics);
        test.At(1); test.Pulse(); test.At(2); test.Pulse();
        Assert.Equal(2, test.Runtime.Queue.Budget(FighterId.FighterB, rule).BattleCount);
        Assert.Equal(2, test.Runtime.Store(FighterId.FighterB).Budget(effect).BattleCount);

        test = new EffectRuntimeFixture([Effect(tickCap: 99, battleCap: 99)], [Rule(trigger: EffectTrigger.DamageTaken, tickCap: 1, battleCap: 2)]);
        test.Start(); test.Pulse(); test.Pulse(); test.At(1); test.Pulse(); test.At(2); test.Pulse();
        Assert.Equal(2, test.Runtime.Queue.Budget(FighterId.FighterB, rule).BattleCount);
        Assert.Equal(2, test.Runtime.Store(FighterId.FighterB).Budget(effect).BattleCount);
        Assert.Contains(EffectAdmission.RuleBudgetRejected, test.Runtime.Diagnostics);

        test = new EffectRuntimeFixture([Effect(policy: EffectStackPolicy.Reject)],
            [Rule("rule_seed"), Rule(trigger: EffectTrigger.DamageTaken, tickCap: 2, battleCap: 2)]);
        test.Start(); var progress = ProgressStamp.Capture(test.State); var budget = test.Runtime.Store(FighterId.FighterB).Budget(effect);
        test.Pulse(); test.Pulse(); test.Pulse();
        Assert.Equal(2, test.Runtime.Queue.Budget(FighterId.FighterB, rule).BattleCount);
        Assert.Equal(budget, test.Runtime.Store(FighterId.FighterB).Budget(effect));
        Assert.Equal(progress, ProgressStamp.Capture(test.State));
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded));
    }

    [Fact, Trait("AcceptanceId", "WP10-TRG-013")]
    public void SimultaneousControlExitsHaveIndependentGlobalSubjectBudgetsAndOneFatigueEach()
    {
        var fatigue = new EffectProfile(new StableId("fatigue"), new StableId("fatigue"), 100,
            EffectExpiryBoundary.ExpireAfterTick, EffectStackPolicy.AddStacks, 3, EffectCompareKey.StackCount,
            0, EffectRefreshRule.ResetDuration, EffectSemanticRole.ControlFatigue, 0, 1, 999,
            [new EffectModifier(EffectModifierTarget.HardControlDuration, EffectModifierOperation.Multiply, 1000, 0)], [1000, 750, 500, 250]);
        var test = new EffectRuntimeFixture([fatigue], [Rule("rule_fatigue", "fatigue", EffectTrigger.ControlEnded, tickCap: 1)]);
        test.Start();
        _ = test.State.FighterA.BeginStun(0, 1, FighterId.FighterB, test.State.FighterB.NextDecisionId());
        _ = test.State.FighterB.BeginStun(0, 1, FighterId.FighterA, test.State.FighterA.NextDecisionId());
        test.At(1);
        AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Expiry, (state, emitter, drafts) =>
        {
            EffectControlSystem.Expire(state, emitter, FighterId.FighterA);
            EffectControlSystem.Expire(state, emitter, FighterId.FighterB);
            state.Effects!.CloseEvents(state, emitter, drafts.ToArray());
        });
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.Payload is StateChangedPayload));
        var applied = test.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectAdded).ToArray();
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, applied.Select(x => x.ActorId!.Value));
        foreach (var fighter in new[] { FighterId.FighterA, FighterId.FighterB })
        {
            Assert.Equal(1, test.Runtime.Queue.Budget(fighter, new StableId("rule_fatigue")).TickCount);
            Assert.Equal(1, Assert.Single(test.State.Get(fighter).Effects).Stacks);
            var addition = Assert.Single(applied, x => x.ActorId == fighter);
            Assert.Equal(fighter, test.Journal.Drafts.Single(x => x.EventId == addition.SourceEventId).ActorId);
            Assert.Null(addition.Rng);
        }
    }
}
