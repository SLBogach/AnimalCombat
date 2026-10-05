using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Safety;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectRuntimeTests
{
    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-001")]
    public void EmptyGroupAddsExactlyOncePerFighterWithExactPublicDelta()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule()]);
        test.Start();
        Assert.Equal(CombatEventType.BattleStarted, test.Journal.Drafts[0].EventType);
        Assert.Equal(2, test.Runtime.ActiveCount);
        foreach (var draft in test.Journal.Drafts.Skip(1))
        {
            var added = Assert.IsType<EffectAddedPayload>(draft.Payload);
            Assert.Equal(new StableId("effect_a"), draft.EffectId);
            Assert.Empty(draft.Before.Actor!.Effects);
            Assert.Equal(1, Assert.Single(draft.After.Actor!.Effects).Stacks);
            Assert.Equal(3, Assert.Single(draft.After.Actor.Effects).TicksRemaining);
            Assert.Equal(0, added.StacksBefore); Assert.Equal(1, added.StacksAfter);
            Assert.Null(draft.TargetId); Assert.Null(draft.Rng);
            Assert.Equal(EventId.FromSequence(0), draft.SourceEventId);
            Assert.Equal(new[] { EventId.FromSequence(0) }, draft.Payload.RelatedEventIds);
        }
        Assert.Equal(128, test.State.FighterA.Precision);
        Assert.Equal(128, test.State.FighterB.Precision);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-EXP-001")]
    public void BeforeExpiryAtEightUsesLatestApplicationAndRestoresBase()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule()]);
        test.At(5); test.Start();
        var applications = test.Journal.Drafts.Skip(1).ToArray();
        for (var tick = 5; tick <= 7; tick++)
        {
            test.At(tick); test.Expire(EffectExpiryBoundary.ExpireBeforeTick);
            Assert.Equal(8 - tick, Assert.Single(test.State.FighterA.Effects).TicksRemaining);
            Assert.Equal(128, test.State.FighterA.Precision);
        }
        Assert.Equal(3, test.Journal.Drafts.Count);
        test.At(8); test.Expire(EffectExpiryBoundary.ExpireBeforeTick);
        Assert.Empty(test.State.FighterA.Effects); Assert.Equal(120, test.State.FighterA.Precision);
        var removals = test.Journal.Drafts.Skip(3).ToArray();
        for (var index = 0; index < 2; index++)
        {
            Assert.Equal(applications[index].EventId, removals[index].SourceEventId);
            Assert.Equal(EffectRemoveReason.ExpiredBeforeTick, Assert.IsType<EffectRemovedPayload>(removals[index].Payload).RemoveReason);
            Assert.Equal(0, Assert.Single(removals[index].Before.Actor!.Effects).TicksRemaining);
            Assert.Empty(removals[index].After.Actor!.Effects);
        }
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-EXP-002")]
    public void AfterExpiryAtSevenDoesNotWeakenEarlierImpactSnapshot()
    {
        var test = new EffectRuntimeFixture([Effect(boundary: EffectExpiryBoundary.ExpireAfterTick)], [Rule()]);
        test.At(5); test.Start();
        test.At(6); test.EndTick(); Assert.Equal(128, test.State.FighterA.Precision);
        test.At(7); var impactStat = test.State.FighterA.Precision;
        Assert.Equal(1, Assert.Single(test.State.FighterA.Effects).TicksRemaining);
        test.EndTick(); Assert.Equal(128, impactStat); Assert.Equal(120, test.State.FighterA.Precision);
        Assert.All(test.Journal.Drafts.Skip(3), draft =>
        {
            Assert.Equal(7, draft.Tick);
            Assert.Equal(EffectRemoveReason.ExpiredAfterTick, Assert.IsType<EffectRemovedPayload>(draft.Payload).RemoveReason);
        });
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-EXP-004")]
    public void CountdownIsSilentAndAlwaysNonnegative()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule()]);
        test.At(5); test.Start();
        foreach (var tick in new[] { 5, 6, 7, 8, 20 })
        {
            test.At(tick);
            Assert.Equal(System.Math.Max(0, 8 - tick), Assert.Single(test.State.FighterA.Effects).TicksRemaining);
        }
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-EXP-005")]
    public void ResetCanShortenWhereKeepLongerPreservesIncumbentEnd()
    {
        foreach (var refresh in new[] { EffectRefreshRule.ResetDuration, EffectRefreshRule.KeepLonger })
        {
            var effect = Effect(duration: 3, policy: EffectStackPolicy.Refresh, refresh: refresh);
            var test = new EffectRuntimeFixture([effect], [Rule(trigger: EffectTrigger.DamageTaken)]);
            test.At(5); test.Start(); test.Pulse(); // end8
            test.At(6); test.Pulse(); Assert.Equal(9, Assert.Single(test.Runtime.Store(FighterId.FighterB).Active).EndExclusiveTick);
        }
        // A compatible Refresh stores the incoming lifetime, not a fixed profile fallback.
        foreach (var refresh in new[] { EffectRefreshRule.ResetDuration, EffectRefreshRule.KeepLonger })
        {
            var store = new EffectStore(32);
            var origin = new EffectOrigin(new StableId("fighter_a"), null, new StableId("rule_a"));
            store.Apply(Effect(duration: 5, policy: EffectStackPolicy.Refresh, refresh: refresh), origin, 5, EventId.FromSequence(1));
            store.Apply(Effect(duration: 3, policy: EffectStackPolicy.Refresh, refresh: refresh), origin, 6, EventId.FromSequence(2));
            Assert.Equal(refresh == EffectRefreshRule.ResetDuration ? 9 : 10, Assert.Single(store.Active).EndExclusiveTick);
        }
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-EXP-007")]
    public void ExpiriesArePriorityThenIdThenFighterIndependentOfRegistration()
    {
        var effects = new[] { Effect("effect_b", priority: 1), Effect("effect_a", priority: 1), Effect("effect_z", priority: 0) };
        var rules = effects.Select(x => Rule("rule_" + x.EffectId.Value, x.EffectId.Value)).ToArray();
        string[] Run(bool reverse)
        {
            var test = new EffectRuntimeFixture(reverse ? effects.Reverse() : effects, reverse ? rules.Reverse() : rules);
            test.Start(); test.At(3); test.Expire(EffectExpiryBoundary.ExpireBeforeTick);
            return test.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved)
                .Select(x => x.EffectId!.Value.Value + ":" + x.ActorId).ToArray();
        }
        Assert.Equal(new[] { "effect_z:FighterA", "effect_z:FighterB", "effect_a:FighterA", "effect_a:FighterB", "effect_b:FighterA", "effect_b:FighterB" }, Run(false));
        Assert.Equal(Run(false), Run(true));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-002")]
    public void RejectOccupiedGroupDoesNotRefreshChargeOrEmit()
    {
        var test = new EffectRuntimeFixture([Effect(policy: EffectStackPolicy.Reject)],
            [Rule(), Rule("rule_tick", trigger: EffectTrigger.EndOfTick)]);
        test.Start(); var budget = test.Runtime.Store(FighterId.FighterA).Budget(new StableId("effect_a"));
        test.At(1); test.EndTick();
        Assert.Equal(3, test.Journal.Drafts.Count);
        Assert.Equal(3, Assert.Single(test.Runtime.Store(FighterId.FighterA).Active).EndExclusiveTick);
        Assert.Equal(budget, test.Runtime.Store(FighterId.FighterA).Budget(new StableId("effect_a")));
        Assert.Equal(1, test.Runtime.Queue.Budget(FighterId.FighterA, new StableId("rule_tick")).BattleCount);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-003")]
    public void RefreshRetainsOneProfileAndUpdatesOnlyRealLifetimeAndLineage()
    {
        var test = new EffectRuntimeFixture([Effect(policy: EffectStackPolicy.Refresh)], [Rule(trigger: EffectTrigger.DamageTaken)]);
        test.Start(); test.Pulse(); var first = test.Journal.Drafts.Last();
        test.Pulse(); Assert.Equal(1, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded));
        test.At(1); test.Pulse();
        var added = test.Journal.Drafts.Last(); var payload = Assert.IsType<EffectAddedPayload>(added.Payload);
        Assert.Equal(1, payload.StacksBefore); Assert.Equal(1, payload.StacksAfter);
        Assert.Equal(128, test.State.FighterB.Precision); Assert.Equal(first.EffectId, added.EffectId);
        Assert.Equal(4, Assert.Single(test.Runtime.Store(FighterId.FighterB).Active).EndExclusiveTick);
        Assert.Equal(added.EventId, Assert.Single(test.Runtime.Store(FighterId.FighterB).Active).LatestApplicationEventId);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-004")]
    public void ReplacePublishesRemovedThenAddedWithEmptyIntermediateOccupant()
    {
        var test = new EffectRuntimeFixture([Effect("effect_a", "group", policy: EffectStackPolicy.Replace),
            Effect("effect_b", "group", policy: EffectStackPolicy.Replace, value: 20)],
            [Rule(), Rule("rule_b", "effect_b", EffectTrigger.EndOfTick)]);
        test.Start(); test.EndTick();
        var deltas = test.Journal.Drafts.Skip(3).ToArray();
        Assert.Equal(new[] { CombatEventType.EffectRemoved, CombatEventType.EffectAdded, CombatEventType.EffectRemoved, CombatEventType.EffectAdded }, deltas.Select(x => x.EventType));
        foreach (var removed in deltas.Where(x => x.EventType == CombatEventType.EffectRemoved))
        {
            Assert.Equal(EffectRemoveReason.Replaced, Assert.IsType<EffectRemovedPayload>(removed.Payload).RemoveReason);
            Assert.Equal(new StableId("effect_a"), Assert.Single(removed.Before.Actor!.Effects).EffectId);
            Assert.Empty(removed.After.Actor!.Effects);
        }
        foreach (var added in deltas.Where(x => x.EventType == CombatEventType.EffectAdded))
        {
            Assert.Empty(added.Before.Actor!.Effects);
            Assert.Equal(new StableId("effect_b"), Assert.Single(added.After.Actor!.Effects).EffectId);
            Assert.Equal(added.EventId, Assert.Single(test.Runtime.Store(added.ActorId!.Value).Active).LatestApplicationEventId);
        }
        Assert.Equal(140, test.State.FighterA.Precision);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-005")]
    public void StrongerValueWinsAndWeakerCannotRefresh()
    {
        var test = new EffectRuntimeFixture([Effect("effect_a", "group", policy: EffectStackPolicy.StrongestWins, value: 1150,
            target: EffectModifierTarget.DamageTaken, operation: EffectModifierOperation.Multiply, compare: EffectCompareKey.Value1),
            Effect("effect_b", "group", policy: EffectStackPolicy.StrongestWins, value: 1300,
                target: EffectModifierTarget.DamageTaken, operation: EffectModifierOperation.Multiply, compare: EffectCompareKey.Value1)],
            [Rule(), Rule("rule_b", "effect_b", EffectTrigger.EndOfTick), Rule("rule_weak", trigger: EffectTrigger.DamageTaken)]);
        test.Start(); test.EndTick(); test.At(1); test.Pulse();
        Assert.Equal(1300, test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.DamageTaken));
        Assert.Equal(3, Assert.Single(test.Runtime.Store(FighterId.FighterB).Active).EndExclusiveTick);
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectRemoved));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-006")]
    public void ExactStrengthTieUsesStableEffectAndOriginIdentityWithoutRng()
    {
        var store = new EffectStore(32);
        var a = Effect("effect_a", "group", policy: EffectStackPolicy.StrongestWins, compare: EffectCompareKey.Value1);
        var b = Effect("effect_b", "group", policy: EffectStackPolicy.StrongestWins, compare: EffectCompareKey.Value1);
        var origin = new EffectOrigin(new StableId("fighter_b"), new StableId("action_b"), new StableId("rule_b"));
        store.Apply(b, origin, 0, EventId.FromSequence(1));
        Assert.Equal(2, store.Apply(a, origin, 1, EventId.FromSequence(2)).Count);
        Assert.Empty(store.Apply(a, origin, 2, EventId.FromSequence(3)));
        var lower = origin with { OwnerId = new StableId("fighter_a") };
        Assert.Equal(2, store.Apply(a, lower, 2, EventId.FromSequence(4)).Count);
        lower = lower with { ActionId = new StableId("action_a") };
        Assert.Equal(2, store.Apply(a, lower, 2, EventId.FromSequence(5)).Count);
        lower = lower with { RuleId = new StableId("rule_a") };
        Assert.Equal(2, store.Apply(a, lower, 2, EventId.FromSequence(6)).Count);
        Assert.Equal(lower, Assert.Single(store.Active).Origin);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-008")]
    [Trait("AcceptanceId", "WP10-STACK-009")]
    public void AddStacksCapsThenRefreshesOnlyWhenExpiryChanges()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.DamageTaken)]);
        test.Start();
        for (var stack = 1; stack <= 3; stack++)
        {
            test.Pulse(); Assert.Equal(stack, Assert.Single(test.State.FighterB.Effects).Stacks);
        }
        var progress = ProgressStamp.Capture(test.State);
        var effectBudget = test.Runtime.Store(FighterId.FighterB).Budget(new StableId("effect_a"));
        test.Pulse(); Assert.Equal(progress, ProgressStamp.Capture(test.State));
        Assert.Equal(effectBudget, test.Runtime.Store(FighterId.FighterB).Budget(new StableId("effect_a")));
        Assert.Equal(4, test.Runtime.Queue.Budget(FighterId.FighterB, new StableId("rule_a")).BattleCount);
        Assert.Equal(3, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded));
        test.At(1); test.Pulse(); var refreshed = Assert.IsType<EffectAddedPayload>(test.Journal.Drafts.Last().Payload);
        Assert.Equal(3, refreshed.StacksBefore); Assert.Equal(3, refreshed.StacksAfter);
        Assert.Equal(4, Assert.Single(test.Runtime.Store(FighterId.FighterB).Active).EndExclusiveTick);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-010")]
    public void ExplicitRemoveIsOnceAndSurvivingModifiersRemain()
    {
        var test = new EffectRuntimeFixture([Effect(), Effect("armor", target: EffectModifierTarget.Armor, value: 18)],
            [Rule(), Rule("rule_armor", "armor"), Rule("rule_remove", trigger: EffectTrigger.EndOfTick, primitive: EffectPrimitive.RemoveEffect)]);
        test.Start(); test.EndTick(); var count = test.Journal.Drafts.Count;
        test.At(1); test.EndTick(); Assert.Equal(count, test.Journal.Drafts.Count);
        Assert.Equal(118, test.State.FighterA.Armor); Assert.Equal(120, test.State.FighterA.Precision);
        Assert.Equal(new StableId("armor"), Assert.Single(test.State.FighterA.Effects).EffectId);
        Assert.All(test.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved),
            x => Assert.Equal(EffectRemoveReason.Dispelled, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-STACK-013")]
    [Trait("AcceptanceId", "WP10-TRG-005")]
    public void DistinctSourcesStackTwiceButRepeatedRealSourceDoesNot()
    {
        var test = new EffectRuntimeFixture([Effect(tickCap: 2)], [Rule(trigger: EffectTrigger.DamageTaken, tickCap: 2)]);
        test.Start(); test.Pulse(); var source = test.Journal.Drafts.Single(x => x.EventType == CombatEventType.DamageApplied);
        AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Resolve,
            (state, emitter, _) => state.Effects!.CloseEvents(state, emitter, [source]));
        test.Pulse();
        Assert.Equal(2, Assert.Single(test.State.FighterB.Effects).Stacks);
        Assert.Equal(2, test.Runtime.Store(FighterId.FighterB).Budget(new StableId("effect_a")).TickCount);
        Assert.Equal(2, test.Runtime.Queue.Budget(FighterId.FighterB, new StableId("rule_a")).TickCount);
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-TRG-001")]
    public void OwnerAndRecipientResolutionAreTypedNotAnimalSpecific()
    {
        var test = new EffectRuntimeFixture([Effect("seed"), Effect("global"), Effect("action"), Effect("subscriber")],
            [Rule("rule_seed", "seed"), Rule("rule_global", "global", EffectTrigger.DamageTaken),
                Rule("rule_action", "action", EffectTrigger.DamageTaken, EffectOwnerKind.Action, "action_a", EffectRecipient.Opponent),
                Rule("rule_sub", "subscriber", EffectTrigger.DamageTaken, EffectOwnerKind.Effect, "seed", EffectRecipient.Opponent)]);
        test.Start(); test.Pulse(action: "action_a");
        var action = Assert.Single(test.Journal.Drafts, x => x.EffectId?.Value == "action");
        Assert.Equal(FighterId.FighterB, action.ActorId); Assert.Equal(FighterId.FighterA, action.TargetId);
        Assert.Equal(new StableId("action_a"), action.ActionId);
        Assert.Equal(new ExternalId("resolution:test"), action.ResolutionGroupId);
        var global = Assert.Single(test.Journal.Drafts, x => x.EffectId?.Value == "global");
        Assert.Equal(FighterId.FighterB, global.ActorId); Assert.Null(global.TargetId);
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.EffectId?.Value == "subscriber"));
    }

    [Fact]
    public void ConditionsAndDeadRecipientRejectBeforeEnqueue()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.DamageTaken, condition: EffectCondition.PositiveDamage)]);
        test.Start(); test.Pulse(); Assert.Equal(0, test.Runtime.Queue.AdmittedThisTick);
        Assert.Empty(test.State.FighterB.Effects);
        test.State.FighterB.SetHealthForTesting(0); test.Pulse();
        Assert.Equal(0, test.Runtime.Queue.AdmittedThisTick); Assert.Contains(EffectAdmission.ConditionRejected, test.Runtime.Diagnostics);
        // The selected animal passive catalog is never consulted or automatically executed here.
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-TRG-006")]
    public void RuleAndEffectCooldownUseTickBoundaryExactly()
    {
        var test = new EffectRuntimeFixture([Effect(cooldown: 5)], [Rule(trigger: EffectTrigger.DamageTaken, cooldown: 5)]);
        test.Start(); test.At(10); test.Pulse();
        for (var tick = 10; tick < 15; tick++) { test.At(tick); test.Pulse(); }
        Assert.Equal(1, test.Runtime.Queue.Budget(FighterId.FighterB, new StableId("rule_a")).BattleCount);
        test.At(15); test.Pulse();
        Assert.Equal(2, test.Runtime.Queue.Budget(FighterId.FighterB, new StableId("rule_a")).BattleCount);
        Assert.Equal(20, test.Runtime.Store(FighterId.FighterB).Budget(new StableId("effect_a")).NextAllowedTick);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-TRG-008")]
    public void RepeatedRootNodeIsDiagnosedWithoutMarkersOrRng()
    {
        var test = new EffectRuntimeFixture([Effect("effect_a", cap: 255), Effect("effect_b", cap: 255)],
            [Rule(), Rule("rule_ab", "effect_b", EffectTrigger.EffectAdded, EffectOwnerKind.Effect, "effect_a", EffectRecipient.Opponent),
                Rule("rule_ba", "effect_a", EffectTrigger.EffectAdded, EffectOwnerKind.Effect, "effect_b", EffectRecipient.Opponent)]);
        test.Start();
        Assert.Contains(EffectAdmission.TriggerCycleSuppressed, test.Runtime.Diagnostics);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex); Assert.Equal(0UL, test.State.Rng.Decision.NextDrawIndex);
        Assert.All(test.Journal.Drafts.Skip(1), draft => Assert.Equal(CombatEventType.EffectAdded, draft.EventType));
        Assert.InRange(test.Journal.Drafts.Count, 3, 30);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-TRG-009")]
    [Trait("AcceptanceId", "WP10-SAFE-002")]
    public void DepthEightIsLegalButNineRollsBackEntireOriginatingClosure()
    {
        EffectRuntimeFixture Chain(int last)
        {
            var effects = Enumerable.Range(0, last + 1).Select(i => Effect("effect_" + i)).ToArray();
            var rules = new[] { Rule(effect: "effect_0", trigger: EffectTrigger.DamageTaken) }.Concat(
                Enumerable.Range(1, last).Select(i => Rule("rule_" + i, "effect_" + i, EffectTrigger.EffectAdded,
                    EffectOwnerKind.Effect, "effect_" + (i - 1))));
            return new EffectRuntimeFixture(effects, rules);
        }
        var legal = Chain(8); legal.Start(); legal.Pulse(); Assert.Equal(9, legal.State.FighterB.Effects.Count);
        var failing = Chain(9); failing.Start(); var before = ProgressStamp.Capture(failing.State);
        var failure = Assert.Throws<EngineInvariantException>(() => failing.Pulse(damage: 10, draw: true, mutate: state =>
        {
            _ = state.Rng.Decision.NextInt(0, 5, RngOperation.NextInt);
            state.FighterA.SetCooldownForTesting(new StableId("action_a"), 5);
            state.FighterA.SetOpportunityDebtForTesting(new StableId("action_a"), 9);
            state.FighterA.ApplyHardControl(10);
        }));
        Assert.Equal(EngineFailureCodes.EffectTriggerDepthExceeded, failure.Code);
        Assert.Equal(before, ProgressStamp.Capture(failing.State)); Assert.Single(failing.Journal.Drafts);
        Assert.Equal(0UL, failing.State.Rng.Resolution.NextDrawIndex); Assert.Equal(0, failing.Runtime.Queue.AdmittedThisTick);
        Assert.Equal(0UL, failing.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(120, failing.State.FighterB.Precision);
        Assert.Equal(0, failing.State.FighterA.OpportunityDebtFor(new StableId("action_a")));
        Assert.Equal(default, failing.Runtime.Store(FighterId.FighterB).Budget(new StableId("effect_0")));
        Assert.Empty(failing.Runtime.Store(FighterId.FighterB).Active);
        Assert.Equal(default, failing.Runtime.Queue.Budget(FighterId.FighterB, new StableId("rule_a")));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-TRG-010")]
    public void TriggerTickLimitIsExactAndResetsOnFollowingTick()
    {
        EffectRuntimeFixture Nodes(int count) => new([Effect(cap: 255)], Enumerable.Range(0, count)
            .Select(i => Rule("rule_" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), trigger: EffectTrigger.DamageTaken)));
        var legal = Nodes(128); legal.Start(); legal.Pulse(); Assert.Equal(128, legal.Runtime.Queue.AdmittedThisTick);
        Assert.Equal(128, Assert.Single(legal.State.FighterB.Effects).Stacks);
        legal.At(1); legal.Pulse(); Assert.Equal(128, legal.Runtime.Queue.AdmittedThisTick);
        var failing = Nodes(129); failing.Start();
        var error = Assert.Throws<EngineInvariantException>(() => failing.Pulse(damage: 1));
        Assert.Equal(EngineFailureCodes.EffectTriggerTickCapExceeded, error.Code);
        Assert.Single(failing.Journal.Drafts); Assert.Empty(failing.State.FighterB.Effects);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-SAFE-001")]
    public void EventCapIncludesExactClosureCleanupAndBattleEndedReserve()
    {
        var low = new EffectRuntimeFixture([Effect()], [Rule()], maximumEvents: 5);
        var before = ProgressStamp.Capture(low.State);
        var error = Assert.Throws<EngineInvariantException>(low.Start);
        Assert.Equal(EngineFailureCodes.EventCapExceeded, error.Code);
        Assert.Equal(CombatEventType.BattleStarted, Assert.Single(low.Journal.Drafts).EventType);
        Assert.Equal(before, ProgressStamp.Capture(low.State));
        Assert.Equal(0, low.Runtime.Queue.AdmittedThisTick);
        var exact = new EffectRuntimeFixture([Effect()], [Rule()], maximumEvents: 6);
        exact.Start(); Assert.Equal(2, exact.Emitter.TerminalCleanupReserve);
        exact.Finish(); Assert.Equal(6, exact.Journal.Drafts.Count);
        Assert.Equal(CombatEventType.BattleEnded, exact.Journal.Drafts.Last().EventType);
        Assert.Empty(exact.State.FighterA.Effects); Assert.Equal(1, exact.Journal.BeginCount); Assert.Equal(1, exact.Journal.CompleteCount);
    }

    [Fact]
    public void ClosureFailureRollsBackBothRngTimersCachesAndRuleVisitedState()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.DamageTaken)], maximumEvents: 4);
        test.Start(); var before = ProgressStamp.Capture(test.State);
        var error = Assert.Throws<EngineInvariantException>(() => AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Resolve,
            (preview, events, drafts) =>
            {
                _ = preview.Rng.Decision.NextInt(0, 5, RngOperation.NextInt);
                _ = preview.Rng.Resolution.NextInt(0, 1000, RngOperation.ChanceCheck);
                preview.FighterA.SetCooldownForTesting(new StableId("action_a"), 5);
                preview.FighterA.SetOpportunityDebtForTesting(new StableId("action_a"), 9);
                preview.FighterA.ApplyHardControl(10);
                // Two new effects + cleanup reserve cannot fit this current batch.
                preview.Effects!.CloseEvents(preview, events, [test.Journal.Drafts[0]]);
                _ = events.Emit(0, new TimeoutReachedPayload(Array.Empty<EventId>(), 1, 1, 1, 1, 1, 1));
                _ = events.Emit(0, new TimeoutReachedPayload(Array.Empty<EventId>(), 1, 1, 1, 1, 1, 1));
                _ = events.Emit(0, new TimeoutReachedPayload(Array.Empty<EventId>(), 1, 1, 1, 1, 1, 1));
            }));
        Assert.Equal(EngineFailureCodes.EventCapExceeded, error.Code);
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Single(test.Journal.Drafts);
        Assert.Equal(0UL, test.State.Rng.Decision.NextDrawIndex); Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
        Assert.Equal(0, test.State.FighterA.OpportunityDebtFor(new StableId("action_a")));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-SAFE-003")]
    public void ThirtyTwoGroupsAreLegalThirtyThreeFailAndReplacementAtCapIsLegal()
    {
        EffectRuntimeFixture Groups(int count) => new(Enumerable.Range(0, count).Select(i => Effect("effect_" + i, policy: EffectStackPolicy.Replace)),
            Enumerable.Range(0, count).Select(i => Rule("rule_" + i, "effect_" + i)));
        var legal = Groups(32); legal.Start(); Assert.Equal(32, legal.State.FighterA.Effects.Count);
        var failed = Groups(33);
        Assert.Equal(EngineFailureCodes.EffectInstanceCapExceeded, Assert.Throws<EngineInvariantException>(failed.Start).Code);
        Assert.Equal(CombatEventType.BattleStarted, Assert.Single(failed.Journal.Drafts).EventType);
        Assert.Equal(0, failed.Runtime.ActiveCount);
        var effects = Enumerable.Range(0, 32).Select(i => Effect("effect_" + i, policy: EffectStackPolicy.Replace))
            .Append(Effect("replacement", "effect_0", policy: EffectStackPolicy.Replace));
        var rules = Enumerable.Range(0, 32).Select(i => Rule("rule_" + i, "effect_" + i))
            .Append(Rule("rule_replace", "replacement", EffectTrigger.EndOfTick));
        var replacement = new EffectRuntimeFixture(effects, rules); replacement.Start(); replacement.EndTick();
        Assert.Equal(32, replacement.State.FighterA.Effects.Count);
        Assert.Contains(replacement.State.FighterA.Effects, x => x.EffectId.Value == "replacement");
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-SAFE-004")]
    public void CorruptedRuntimeOverflowHasTypedFailureWithoutPublicationOrCacheDrift()
    {
        var test = new EffectRuntimeFixture([Effect(value: int.MaxValue)], [Rule(trigger: EffectTrigger.DamageTaken)]);
        test.Start(); var before = ProgressStamp.Capture(test.State);
        var error = Assert.Throws<EngineInvariantException>(() => test.Pulse(damage: 10, draw: true));
        Assert.Equal(EngineFailureCodes.EffectArithmeticOverflow, error.Code);
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Equal(120, test.State.FighterB.Precision);
        Assert.Single(test.Journal.Drafts); Assert.Equal(0, test.Runtime.ActiveCount);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
        var inconsistent = Assert.Throws<EngineInvariantException>(() => AtomicBattleBatch.Execute(test.State, test.Emitter,
            TickPhase.Expiry, (preview, emitter, drafts) =>
            {
                var absent = new ActiveEffect(Effect(), 1, 3, new EffectOrigin(new StableId("fighter_a"), null,
                    new StableId("rule_a")), EventId.FromSequence(0));
                preview.Effects!.Store(FighterId.FighterB).Project(new EffectMutation(absent, null, EffectRemoveReason.Dispelled));
            }));
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, inconsistent.Code);
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Single(test.Journal.Drafts);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-TRG-004")]
    public void CausalLevelsPrecedeChildPriorityAndRegistrationOrder()
    {
        var effects = new[] { Effect("effect_a"), Effect("effect_b"), Effect("effect_c") };
        var rules = new[] { Rule("rule_a", priority: 10), Rule("rule_b", "effect_b", priority: 20),
            Rule("rule_child", "effect_c", EffectTrigger.EffectAdded, EffectOwnerKind.Effect, "effect_a", priority: -100) };
        (string? Effect, FighterId? Fighter, EventId? Source)[] Run(bool reverse)
        {
            var fixture = new EffectRuntimeFixture(reverse ? effects.Reverse() : effects, reverse ? rules.Reverse() : rules);
            fixture.Start();
            return fixture.Journal.Drafts.Skip(1).Select(x => (x.EffectId?.Value, x.ActorId, x.SourceEventId)).ToArray();
        }
        var ordered = Run(false);
        Assert.Equal(new[] { "effect_a", "effect_a", "effect_b", "effect_b", "effect_c", "effect_c" }, ordered.Select(x => x.Effect));
        Assert.Equal(new FighterId?[] { FighterId.FighterA, FighterId.FighterB, FighterId.FighterA, FighterId.FighterB, FighterId.FighterA, FighterId.FighterB }, ordered.Select(x => x.Fighter));
        Assert.All(ordered.Take(4), x => Assert.Equal(EventId.FromSequence(0), x.Source));
        Assert.Equal(EventId.FromSequence(1), ordered[4].Source); Assert.Equal(EventId.FromSequence(2), ordered[5].Source);
        Assert.Equal(ordered, Run(true));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-SAFE-005")]
    public void ProgressIgnoresRejectedSuppressedQueueRngAndEventOnlyWork()
    {
        var fixture = new EffectRuntimeFixture([Effect(policy: EffectStackPolicy.Reject)],
            [Rule(), Rule("rule_tick", trigger: EffectTrigger.EndOfTick)]);
        var initial = ProgressStamp.Capture(fixture.State); fixture.Start();
        Assert.NotEqual(initial, ProgressStamp.Capture(fixture.State));
        var active = ProgressStamp.Capture(fixture.State); fixture.EndTick();
        Assert.Equal(active, ProgressStamp.Capture(fixture.State));
        AtomicBattleBatch.Execute(fixture.State, fixture.Emitter, TickPhase.EndTick, (state, emitter, drafts) =>
        {
            _ = state.Rng.Decision.NextInt(0, 5, RngOperation.NextInt);
            _ = emitter.Emit(0, new TimeoutReachedPayload(Array.Empty<EventId>(), 1, 1, 1, 1, 1, 1));
            state.Effects!.CloseEvents(state, emitter, [fixture.Journal.Drafts[0]]);
        });
        Assert.Contains(EffectAdmission.TriggerCycleSuppressed, fixture.Runtime.Diagnostics);
        Assert.Equal(active, ProgressStamp.Capture(fixture.State));
        var events = fixture.Journal.Drafts.Count; fixture.At(1);
        Assert.NotEqual(active, ProgressStamp.Capture(fixture.State)); Assert.Equal(events, fixture.Journal.Drafts.Count);
        var countdown = ProgressStamp.Capture(fixture.State);
        fixture.At(3); fixture.Expire(EffectExpiryBoundary.ExpireBeforeTick);
        Assert.NotEqual(countdown, ProgressStamp.Capture(fixture.State)); Assert.Equal(120, fixture.State.FighterA.Precision);
    }

    [Fact]
    public void TerminalCleanupIsTriggerFreeAndRecomputesAllSurvivingStats()
    {
        var test = new EffectRuntimeFixture([Effect(), Effect("effect_b")], [Rule(),
            Rule("rule_reapply", "effect_b", EffectTrigger.EffectRemoved), Rule("rule_end", "effect_b", EffectTrigger.EndOfTick)]);
        test.Start(); test.Finish();
        Assert.Equal(0, test.Runtime.ActiveCount);
        Assert.All(test.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved), draft =>
            Assert.Equal(EffectRemoveReason.BattleEnded, Assert.IsType<EffectRemovedPayload>(draft.Payload).RemoveReason));
        Assert.DoesNotContain(test.Journal.Drafts, x => x.EffectId?.Value == "effect_b");
        Assert.Equal(120, test.State.FighterA.Precision); Assert.Equal(120, test.State.FighterB.Precision);
        Assert.All(test.Journal.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
    }

    [Fact]
    public void RemovedEffectOwnerCanObserveItsOwnRemovalAndAfterD1IsDrained()
    {
        var test = new EffectRuntimeFixture([Effect(duration: 1, boundary: EffectExpiryBoundary.ExpireAfterTick),
            Effect("effect_b", duration: 1, boundary: EffectExpiryBoundary.ExpireAfterTick)], [Rule(),
            Rule("rule_remove", "effect_b", EffectTrigger.EffectRemoved, EffectOwnerKind.Effect, "effect_a")]);
        test.Start(); test.EndTick();
        Assert.Equal(0, test.Runtime.ActiveCount);
        Assert.Equal(3, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_b"));
        Assert.All(test.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved), x => Assert.Equal(0, x.Tick));
    }

    [Fact]
    public void ConsecutiveSilentEndTicksUseDistinctOccurrencesNotFakeActionLineage()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.EndOfTick, primitive: EffectPrimitive.RemoveEffect)]);
        test.Start(); test.EndTick(); test.At(1); test.EndTick();
        Assert.Empty(test.State.FighterA.Effects); Assert.Single(test.Journal.Drafts);
        Assert.Equal(2, test.Runtime.Queue.Budget(FighterId.FighterA, new StableId("rule_a")).BattleCount);
        Assert.All(test.Journal.Drafts.Skip(1), draft => { Assert.Null(draft.ActionId); Assert.Null(draft.DecisionId); Assert.Null(draft.ResolutionGroupId); });
    }
}
