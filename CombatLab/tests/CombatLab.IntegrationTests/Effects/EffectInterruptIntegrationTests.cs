using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;
using static CombatLab.IntegrationTests.Effects.EffectEngineFixture;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectInterruptIntegrationTests
{
    [Theory, Trait("AcceptanceId", "WP10-INT-005")]
    [InlineData(false), InlineData(true)]
    public void OrdinaryActiveAndRecoveryHitsDoNotCancelCombat(bool recovery)
    {
        var source = Source(7);
        var a = Attack(source, startup: recovery ? 2 : 1); a["cooldown_ticks"] = 100;
        var b = Attack(source, "kangaroo_flying_kick", active: recovery ? 1 : 5,
            recovery: recovery ? 4 : 0, schedule: recovery ? "0" : "4");
        b["cooldown_ticks"] = 100; b["hit_interruptible_phases"] = "Startup";
        b["hit_interrupt_strength"] = 0; // Isolate A's incoming hit from B interrupting A first.
        var run = Run(source); run.Completed();
        var hit = Assert.Single(run.Events, x => x.EventType == CombatEventType.DamageApplied && x.ActorId == FighterId.FighterA);
        Assert.Equal(recovery ? ActionPhase.Recovery : ActionPhase.Active, hit.Before.Target!.ActionPhase);
        Assert.DoesNotContain(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.ActionCancelled);
        Assert.Contains(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DamageApplied);
    }

    [Theory] // Additional assertions of INT-005's explicitly interruptible movement phases.
    [InlineData(false), InlineData(true)]
    public void MovementHitInterruptRespectsExplicitStartupAndActiveData(bool active)
    {
        var source = Source(7);
        var a = Attack(source, startup: 1); a["cooldown_ticks"] = 100;
        var b = Attack(source, "kangaroo_flying_kick", startup: active ? 0 : 3, active: 4, schedule: "3");
        b["cooldown_ticks"] = 100; b["movement_mode"] = "Approach"; b["move_distance"] = 300;
        b["interrupt_profile"] = "Cancelable"; b["hit_interruptible_phases"] = "Startup|Active";
        b["hit_interrupt_min_strength"] = 1;
        var run = Run(source); run.Completed();
        var cancelled = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.ActionCancelled);
        Assert.Equal(active ? ActionPhase.Active : ActionPhase.Startup, Assert.IsType<ActionCancelledPayload>(cancelled.Payload).CancelledPhase);
        Assert.Equal("HitInterrupt", Assert.IsType<ActionCancelledPayload>(cancelled.Payload).CancelReason.Value);
        Assert.DoesNotContain(run.Events, x => x.EventType == CombatEventType.DamageApplied && x.ActorId == FighterId.FighterB);
    }

    [Fact, Trait("AcceptanceId", "WP10-INT-006")]
    public void StartupCancellationRetainsPaidCostsAndCooldownAndRemovesFutureSchedule()
    {
        var source = Source(7); var a = Attack(source); a["cooldown_ticks"] = 100;
        var b = Attack(source, "kangaroo_flying_kick", startup: 4, active: 2, schedule: "0|1");
        b["energy_cost"] = 12; b["cooldown_ticks"] = 100;
        b["interrupt_profile"] = "Cancelable"; b["hit_interruptible_phases"] = "Startup"; b["hit_interrupt_min_strength"] = 1;
        var run = Run(source); run.Completed();
        var commit = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.ActionId?.Value == "kangaroo_flying_kick" && x.EventType == CombatEventType.ActionCommitted);
        var cost = Assert.IsType<ActionCommittedPayload>(commit.Payload);
        Assert.Equal(12, cost.EnergyCost); Assert.Equal(0, cost.ResourceCost); Assert.Equal(100, cost.CooldownTicks);
        var paid = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.Payload is ResourceChangedPayload { ResourceKind: ResourceKind.Energy, Delta: -12 });
        Assert.Equal(commit.EventId, paid.SourceEventId);
        Assert.Equal(12, paid.Before.Actor!.Energy - paid.After.Actor!.Energy);
        var cancel = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.ActionCancelled);
        Assert.Equal(commit.DecisionId, cancel.DecisionId); Assert.Equal(commit.ActionId, cancel.ActionId);
        Assert.Equal(paid.After.Actor.Energy, cancel.After.Actor!.Energy);
        Assert.Equal(cancel.Before.Actor!.Energy, cancel.After.Actor.Energy);
        var after = Assert.Single(run.Observer.Samples, x => x.Tick == 0 && x.Phase == TickPhase.WallsAndGrabs);
        Assert.Null(after.B.ActionId); Assert.Null(after.B.ActionPhase);
        // Tick0 commit is not decremented; normal ticks1..6 decrement, terminal tick7 does not.
        Assert.Equal(94, run.Observer.FinalState!.FighterB.CooldownFor(new StableId("kangaroo_flying_kick")));
        // Its original telegraph precedes cancellation and remains truthful history.
        Assert.DoesNotContain(run.Events, x => x.ActorId == FighterId.FighterB && x.Sequence > cancel.Sequence &&
            x.EventType is CombatEventType.AttackPrepared or CombatEventType.DamageApplied);
    }

    [Theory, Trait("AcceptanceId", "WP10-INT-007")]
    [InlineData(false), InlineData(true)]
    public void OnlyUninterruptibleCreatedTradeIntentSurvivesAndNeverItsFutureHit(bool protectedIntent)
    {
        var source = Source(3); var a = Attack(source, active: 2, schedule: "0|1");
        a["base_stagger"] = 100; a["base_stun_ticks"] = 8; a["cooldown_ticks"] = 100;
        var b = Attack(source, "kangaroo_flying_kick", active: 2, schedule: "0|1"); b["cooldown_ticks"] = 100;
        b["interrupt_profile"] = protectedIntent ? "UninterruptibleImpact" : "Cancelable";
        a["action_priority"] = 0; b["action_priority"] = 0;
        Entity(source, "fighters", "animal_id", "bear")["initiative"] = 200;
        Entity(source, "fighters", "animal_id", "kangaroo")["initiative"] = 100;
        var run = Run(source); run.Completed();
        var cancelled = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.ActionCancelled);
        Assert.Equal(protectedIntent ? 1 : 0, Assert.IsType<ActionCancelledPayload>(cancelled.Payload).SurvivingIntentIds.Count);
        Assert.Equal(protectedIntent ? 2 : 1, run.Events.Count(x => x.Tick == 0 && x.EventType == CombatEventType.DamageApplied));
        Assert.DoesNotContain(run.Events, x => x.Tick > 0 && x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DamageApplied);
        if (protectedIntent)
        {
            var surviving = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DamageApplied);
            Assert.True(surviving.Sequence > cancelled.Sequence);
            Assert.Equal(cancelled.ResolutionGroupId, surviving.ResolutionGroupId);
        }
    }
}
