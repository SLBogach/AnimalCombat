using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Core.Engine;
using static CombatLab.IntegrationTests.Effects.EffectEngineFixture;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectLifecycleIntegrationTests
{
    [Fact, Trait("AcceptanceId", "WP10-TRG-002")]
    public void BattleStartClosureSeesBothFullyInitializedFightersAfterSequenceZero()
    {
        var source = Source(); Effect(source, "effect_trial"); Rule(source, "rule_trial", "effect_trial");
        var run = Run(source); run.Completed();
        var started = Assert.IsType<BattleStartedPayload>(run.Events[0].Payload);
        Assert.All(started.InitialFrames, x => { Assert.Empty(x.Effects); Assert.Equal(1000, x.Health); });
        Assert.Empty(run.Journal.Canonical.Start!.FighterA.InitialFrame.Effects);
        Assert.Empty(run.Journal.Canonical.Start.FighterB.InitialFrame.Effects);
        var added = run.Events.Where(x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_trial").ToArray();
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, added.Select(x => x.ActorId!.Value));
        Assert.All(added, x => { Assert.True(x.Sequence > 0); Assert.Equal(run.Events[0].EventId, x.SourceEventId); Assert.Null(x.TargetId); Assert.Null(x.Rng); });
        var snapshot = Assert.Single(run.Observer.Samples, x => x.Tick == 0 && x.Phase == TickPhase.Snapshot);
        Assert.Equal(200, snapshot.ArmorA); Assert.Equal(200, snapshot.ArmorB);
    }

    [Theory, Trait("AcceptanceId", "WP10-EXP-003")]
    [InlineData("ExpireBeforeTick", 1, EffectRemoveReason.ExpiredBeforeTick)]
    [InlineData("ExpireAfterTick", 0, EffectRemoveReason.ExpiredAfterTick)]
    public void DurationOneUsesExactPhaseBoundary(string boundary, int tick, EffectRemoveReason reason)
    {
        var source = Source(3); Effect(source, "effect_trial", duration: 1, boundary: boundary); Rule(source, "rule_trial", "effect_trial");
        var run = Run(source); run.Completed();
        var removed = run.Events.Where(x => x.EventType == CombatEventType.EffectRemoved && x.EffectId?.Value == "effect_trial").ToArray();
        Assert.Equal(2, removed.Length);
        Assert.All(removed, x => { Assert.Equal(tick, x.Tick); Assert.Equal(reason, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason); });
        var commits = run.Events.Where(x => x.EventType == CombatEventType.ActionCommitted && x.Tick == tick).ToArray();
        if (boundary == "ExpireAfterTick") Assert.All(commits, x => Assert.True(x.Sequence < removed[0].Sequence));
        else Assert.All(run.Observer.Samples.Where(x => x.Tick == 1 && x.Phase == TickPhase.Decisions), x => Assert.Empty(x.B.Effects));
        Assert.All(removed, x => Assert.Equal(0, Assert.IsType<EffectRemovedPayload>(x.Payload).StacksAfter));
    }

    [Theory, Trait("AcceptanceId", "WP10-EXP-006")]
    [InlineData("ExpireBeforeTick")]
    [InlineData("ExpireAfterTick")]
    public void ExpiryRecomputesBeforeDecisionsWithoutWeakeningCurrentTickImpacts(string boundary)
    {
        var source = Source(3); Attack(source, damage: 100, active: 3, schedule: "0|1|2");
        Effect(source, "effect_trial", duration: 2, boundary: boundary); Rule(source, "rule_trial", "effect_trial");
        var run = Run(source); run.Completed();
        var damage = run.Events.Where(x => x.Payload is DamageAppliedPayload && x.ActorId == FighterId.FighterA).ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, damage.Select(x => x.Tick));
        Assert.Equal(new[] { 50, 50, 66 }, damage.Select(x => Assert.IsType<DamageAppliedPayload>(x.Payload).HealthBefore - Assert.IsType<DamageAppliedPayload>(x.Payload).HealthAfter));
        var atDecision = Assert.Single(run.Observer.Samples, x => x.Tick == 2 && x.Phase == TickPhase.Decisions);
        Assert.Equal(100, atDecision.ArmorB); Assert.Empty(atDecision.B.Effects);
        var t1Impact = Assert.Single(run.Observer.Samples, x => x.Tick == 1 && x.Phase == TickPhase.Resolve);
        Assert.Equal(200, t1Impact.ArmorB);
        var removed = run.Events.First(x => x.EventType == CombatEventType.EffectRemoved);
        Assert.True(boundary == "ExpireAfterTick" ? removed.Sequence > damage[1].Sequence : removed.Sequence < damage[2].Sequence);
    }

    [Fact, Trait("AcceptanceId", "WP10-EXP-008"), Trait("AcceptanceId", "WP10-TRG-012")]
    public void RefreshedExpiryReferencesLatestApplicationAndRemovalOwnerRunsBoundedChildClosure()
    {
        var source = Source(5); Effect(source, "effect_trial")["max_activations_per_tick"] = 2;
        Effect(source, "effect_child", "Power", 10, duration: 4);
        Rule(source, "rule_start", "effect_trial"); Rule(source, "rule_refresh", "effect_trial", "EndOfTick", battleCap: 2);
        Rule(source, "rule_child", "effect_child", "EffectRemoved", "Effect", "effect_trial");
        var run = Run(source); run.Completed();
        foreach (var actor in new[] { FighterId.FighterA, FighterId.FighterB })
        {
            var refresh = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.ActorId == actor && x.EffectId?.Value == "effect_trial" && x.Tick == 1);
            var removed = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectRemoved && x.ActorId == actor && x.EffectId?.Value == "effect_trial");
            Assert.Equal(3, removed.Tick); Assert.Equal(refresh.EventId, removed.SourceEventId);
            var child = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.ActorId == actor && x.EffectId?.Value == "effect_child");
            var parent = run.Events.Single(x => x.EventId == child.SourceEventId);
            Assert.Equal(CombatEventType.EffectRemoved, parent.EventType); Assert.Equal("effect_trial", parent.EffectId!.Value.Value);
            Assert.True(child.Sequence > parent.Sequence);
            if (actor == FighterId.FighterA) Assert.Equal(removed.EventId, child.SourceEventId); // Pre-removal subscription survives.
            Assert.Null(child.TargetId); Assert.Null(child.Rng);
        }
        var sample = Assert.Single(run.Observer.Samples, x => x.Tick == 3 && x.Phase == TickPhase.Decisions);
        Assert.Equal(100, sample.ArmorA); Assert.Equal(100, sample.ArmorB);
        Assert.Equal(110, sample.PowerA); Assert.Equal(110, sample.PowerB);
    }

    [Fact, Trait("AcceptanceId", "WP10-EXP-009")]
    public void AfterExpiryChildDurationOneDrainsAtTheSameBoundary()
    {
        var source = Source(2); Effect(source, "effect_trial", duration: 1, boundary: "ExpireAfterTick");
        Effect(source, "effect_child", "Power", 10, duration: 1, boundary: "ExpireAfterTick");
        Rule(source, "rule_start", "effect_trial"); Rule(source, "rule_child", "effect_child", "EffectRemoved", "Effect", "effect_trial");
        var run = Run(source); run.Completed();
        foreach (var actor in new[] { FighterId.FighterA, FighterId.FighterB })
        {
            var events = run.Events.Where(x => x.ActorId == actor && x.EffectId.HasValue).ToArray();
            Assert.Equal(4, events.Length);
            Assert.All(events, x => Assert.Equal(0, x.Tick));
            var child = Assert.Single(events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_child");
            var removedChild = Assert.Single(events, x => x.EventType == CombatEventType.EffectRemoved && x.EffectId?.Value == "effect_child");
            var parent = run.Events.Single(x => x.EventId == child.SourceEventId);
            Assert.Equal(CombatEventType.EffectRemoved, parent.EventType); Assert.Equal("effect_trial", parent.EffectId!.Value.Value);
            Assert.Equal(child.EventId, removedChild.SourceEventId); Assert.True(removedChild.Sequence > child.Sequence);
        }
        Assert.All(run.Observer.Samples.Where(x => x.Tick == 1), x => { Assert.Empty(x.A.Effects); Assert.Empty(x.B.Effects); });
        Assert.Equal(0UL, run.Observer.FinalState!.Rng.Resolution.NextDrawIndex);
    }

    [Fact, Trait("AcceptanceId", "WP10-TRG-011")]
    public void EndOfTickClosurePrecedesAfterExpiryAndSuppressesNormalHooksOnTerminalTick()
    {
        var source = Source(2); Effect(source, "effect_trial", duration: 1, boundary: "ExpireAfterTick");
        Rule(source, "rule_end", "effect_trial", "EndOfTick", battleCap: 10);
        var run = Run(source); run.Completed();
        Assert.Equal(new[] { 0, 0, 1, 1 }, run.Events.Where(x => x.EventType == CombatEventType.EffectAdded).Select(x => x.Tick));
        Assert.DoesNotContain(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.Tick == 2);
        foreach (var added in run.Events.Where(x => x.EventType == CombatEventType.EffectAdded))
        {
            Assert.True(run.Events.Single(x => x.EventId == added.SourceEventId).Sequence < added.Sequence);
            var removed = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectRemoved && x.SourceEventId == added.EventId);
            Assert.Equal(added.Tick, removed.Tick); Assert.True(removed.Sequence > added.Sequence);
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-TRG-014")]
    public void SilentEndOfTickOccurrencesShareEarlierSourceButHaveIndependentAdmission()
    {
        var source = Source(3); Effect(source, "effect_absent");
        var rule = Rule(source, "rule_remove_absent", "effect_absent", "EndOfTick", battleCap: 3); rule["primitive"] = "RemoveEffect";
        var run = Run(source); run.Completed();
        Assert.DoesNotContain(run.Events, x => x.Tick is 1 or 2);
        foreach (var fighter in new[] { FighterId.FighterA, FighterId.FighterB })
            Assert.Equal(3, run.Observer.FinalState!.Effects!.Queue.Budget(fighter, new StableId("rule_remove_absent")).BattleCount);
        Assert.DoesNotContain(run.Events, x => x.EffectId.HasValue);
        Assert.Equal(0UL, run.Observer.FinalState!.Rng.Decision.NextDrawIndex);
        Assert.Equal(0UL, run.Observer.FinalState.Rng.Resolution.NextDrawIndex);
    }

    [Fact, Trait("AcceptanceId", "WP10-MOD-008")]
    public void CommittedTimingsAndSystemSegmentSpeedStayFrozenAndNextCommitReadsNewStats()
    {
        var source = Source(16); Attack(source, startup: 4, recovery: 4);
        Effect(source, "effect_speed", "ActionSpeed", 100, duration: 20); Rule(source, "rule_speed", "effect_speed", "EndOfTick");
        var run = Run(source); run.Completed();
        var commits = run.Events.Where(x => x.EventType == CombatEventType.ActionCommitted && x.ActionId?.Value == "bear_earthbreaker").ToArray();
        Assert.True(commits.Length >= 2);
        Assert.Equal(4, Assert.IsType<ActionCommittedPayload>(commits[0].Payload).StartupTicks);
        Assert.Equal(4, Assert.IsType<ActionCommittedPayload>(commits[0].Payload).RecoveryTicks);
        Assert.True(Assert.IsType<ActionCommittedPayload>(commits[1].Payload).StartupTicks < 4);
        Assert.True(Assert.IsType<ActionCommittedPayload>(commits[1].Payload).RecoveryTicks < 4);
        Assert.Contains(run.Observer.Samples, x => x.Tick == 1 && x.SpeedA == 200 && x.A.StateTicksRemaining == 4);
        source = Source(16); source["settings"]!["global.arena.start_position_a"] = 1000; source["settings"]!["global.arena.start_position_b"] = 6200;
        Effect(source, "effect_delay", "Precision", 0, duration: 2); Rule(source, "rule_delay", "effect_delay");
        Effect(source, "effect_move", "MoveSpeed", 100, duration: 20);
        Rule(source, "rule_move", "effect_move", "EffectRemoved", "Effect", "effect_delay");
        run = Run(source); run.Completed();
        var starts = run.Events.Where(x => x.Payload is MoveStartedPayload && x.ActorId == FighterId.FighterA).ToArray();
        Assert.True(starts.Length >= 2);
        Assert.Equal(100, Assert.IsType<MoveStartedPayload>(starts[0].Payload).SpeedPerTick);
        Assert.Equal(200, Assert.IsType<MoveStartedPayload>(starts[1].Payload).SpeedPerTick);
        Assert.Contains(run.Observer.Samples, x => x.Tick == 2 && x.FrozenMoveA == 100);
    }

    [Fact, Trait("AcceptanceId", "WP10-SAFE-009")]
    public void FailedBatchWithActiveEffectsKeepsAllCleanupAndInvalidTerminalReserved()
    {
        var source = Source(3); source["settings"]!["global.sim.max_events_per_battle"] = 10;
        Effect(source, "effect_survivor", duration: 20); Effect(source, "effect_over_cap", "Power", 10, duration: 20);
        Rule(source, "rule_survivor", "effect_survivor"); Rule(source, "rule_fail", "effect_over_cap", "EndOfTick");
        Rule(source, "rule_no_terminal_reapply", "effect_over_cap", "EffectRemoved", "Effect", "effect_survivor");
        var run = Run(source);
        Assert.Equal(BattleResultStatus.FailedInvariant, run.Result.Status);
        Assert.Equal("EventCapExceeded", run.Result.InvariantFailure!.Code.Value);
        Assert.Equal(1, run.Journal.Begins); Assert.Equal(1, run.Journal.Completes);
        Assert.Equal(10, run.Events.Length); Assert.Equal(CombatEventType.BattleEnded, run.Events[^1].EventType);
        Assert.DoesNotContain(run.Events, x => x.EffectId?.Value == "effect_over_cap");
        var cleanup = run.Events.Where(x => x.Payload is EffectRemovedPayload).ToArray();
        Assert.Equal(2, cleanup.Length);
        Assert.All(cleanup, x => Assert.Equal(EffectRemoveReason.BattleEnded, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason));
        Assert.All(run.Journal.Canonical.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
        Assert.Equal(BattleOutcome.Invalid, run.Journal.Canonical.Summary.Outcome);
        Assert.Equal(0, run.Observer.FinalState!.Effects!.Queue.Budget(FighterId.FighterA, new StableId("rule_fail")).BattleCount);
    }
}
