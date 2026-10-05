using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;
using static CombatLab.IntegrationTests.Effects.EffectEngineFixture;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectControlIntegrationTests
{
    [Fact, Trait("AcceptanceId", "WP10-CTRL-002")]
    public void EachLivingStunOrGrabExitChargesOneFatigueLifecycleOnly()
    {
        var source = Source(10); var attack = Attack(source); attack["base_stagger"] = 100;
        attack["base_stun_ticks"] = 8; attack["cooldown_ticks"] = 100;
        var run = Run(source); run.Completed();
        var exit = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.Payload is StateChangedPayload
            { OldState: FighterState.Stunned, NewState: FighterState.DecisionReady });
        Assert.Equal(8, exit.Tick);
        var fatigue = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_fatigue");
        Assert.Equal(exit.EventId, fatigue.SourceEventId); Assert.Equal(1, Assert.IsType<EffectAddedPayload>(fatigue.Payload).StacksAfter);

        // Release is lifecycle-driven at Active completion, not a hit-schedule primitive.
        source = Source(3); attack = Attack(source, schedule: "grab:0", tags: "grab");
        attack["cooldown_ticks"] = 100;
        run = Run(source); run.Completed();
        var end = Assert.Single(run.Events, x => x.EventType == CombatEventType.GrabEnded);
        fatigue = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_fatigue");
        Assert.Equal(end.EventId, fatigue.SourceEventId); Assert.Equal(FighterId.FighterB, fatigue.ActorId);
        Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_grab_lockout");
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-004")]
    public void ThresholdCrossingCreatesOneImmunityAndAtCapRefreshCannotRenewIt()
    {
        var source = Source(72); var attack = Attack(source); attack["base_stagger"] = 100;
        attack["base_stun_ticks"] = 8; attack["cooldown_ticks"] = 12;
        var run = Run(source); run.Completed();
        var fatigue = run.Events.Where(x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_fatigue").ToArray();
        Assert.True(fatigue.Length >= 4, Trace(run));
        Assert.Equal(new[] { 1, 2, 3, 3 }, fatigue.Take(4).Select(x => Assert.IsType<EffectAddedPayload>(x.Payload).StacksAfter));
        var immunity = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_immunity");
        Assert.Equal(fatigue[2].Tick, immunity.Tick); Assert.Equal(fatigue[2].EventId, immunity.SourceEventId);
        Assert.Equal(25, Assert.IsType<EffectAddedPayload>(immunity.Payload).DurationTicks);
        var expiry = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectRemoved && x.EffectId?.Value == "effect_control_immunity");
        Assert.Equal(immunity.Tick + 24, expiry.Tick);
        Assert.Equal(EffectRemoveReason.ExpiredAfterTick, Assert.IsType<EffectRemovedPayload>(expiry.Payload).RemoveReason);
        Assert.Contains(run.Events, x => x.Tick > immunity.Tick && x.Tick < expiry.Tick && x.Payload is StateChangedPayload { ImmunityResult: ImmunityResult.Prevented });
        Assert.True(fatigue[3].Tick > expiry.Tick);
        Assert.Equal(3, Assert.IsType<EffectAddedPayload>(fatigue[3].Payload).StacksBefore);
    }

    [Fact, Trait("AcceptanceId", "WP10-KDN-003"), Trait("AcceptanceId", "WP10-KDN-006")]
    public void KnockdownStagesRemainActionlessAndWakeupRulesPrecedeTick21Decisions()
    {
        var source = Source(47); var attack = Attack(source, startup: 10, tags: "strike|knockdown"); attack["cooldown_ticks"] = 100;
        var run = Run(source); run.Completed();
        var stages = run.Events.Where(x => x.ActorId == FighterId.FighterB && x.Payload is StateChangedPayload
            { OldState: FighterState.KnockedDown, NewState: FighterState.KnockedDown }).ToArray();
        Assert.Equal(new[] { 12, 18 }, stages.Select(x => x.Tick));
        Assert.Equal(new[] { "KnockdownGrounded", "KnockdownGetUp" }, stages.Select(x => x.ReasonCodes.Single().Value));
        foreach (var sample in run.Observer.Samples.Where(x => x.Tick is >= 11 and < 21 && x.Phase == TickPhase.Decisions))
        {
            Assert.Equal(FighterState.KnockedDown, sample.B.State); Assert.Null(sample.B.ActionId); Assert.Null(sample.B.ActionPhase);
            Assert.Equal(21 - sample.Tick, sample.B.StateTicksRemaining);
        }
        Assert.DoesNotContain(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DecisionMade && x.Tick is >= 10 and < 21);
        var wakeup = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.Payload is StateChangedPayload
            { OldState: FighterState.KnockedDown, NewState: FighterState.DecisionReady });
        Assert.Equal(21, wakeup.Tick);
        var immune = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_wakeup_immunity");
        var fatigue = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_fatigue");
        Assert.Equal(wakeup.EventId, immune.SourceEventId); Assert.Equal(wakeup.EventId, fatigue.SourceEventId);
        var decision = Assert.Single(run.Events, x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DecisionMade && x.Tick == 21);
        Assert.True(immune.Sequence < decision.Sequence); Assert.True(fatigue.Sequence < decision.Sequence);
        Assert.Contains(decision.Before.Actor!.Effects, x => x.EffectId.Value == "effect_wakeup_immunity");
        var expired = Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectRemoved && x.EffectId?.Value == "effect_wakeup_immunity");
        Assert.Equal(45, expired.Tick); Assert.Equal(EffectRemoveReason.ExpiredAfterTick, Assert.IsType<EffectRemovedPayload>(expired.Payload).RemoveReason);
    }

    [Fact, Trait("AcceptanceId", "WP10-KDN-005")]
    public void GroundHitsDoNotRefreshSameSourceKnockdownAndLethalDamageWinsBeforeWakeup()
    {
        var source = Source(23); var attack = Attack(source, damage: 100, startup: 10, active: 3,
            schedule: "0|1|2", tags: "strike|knockdown|ground_hit"); attack["cooldown_ticks"] = 100;
        var run = Run(source); run.Completed();
        Assert.Equal(new[] { 10, 11, 12 }, run.Events.Where(x => x.Payload is DamageAppliedPayload).Select(x => x.Tick));
        Assert.Single(run.Events, x => x.Payload is StateChangedPayload { NewState: FighterState.KnockedDown, OldState: not FighterState.KnockedDown });
        Assert.Contains(run.Events, x => x.Tick == 21 && x.Payload is StateChangedPayload { OldState: FighterState.KnockedDown, NewState: FighterState.DecisionReady });
        Entity(source, "fighters", "animal_id", "kangaroo")["max_health"] = 150;
        attack["base_damage"] = 150;
        run = Run(source); run.Completed();
        Assert.Equal(11, run.Result.Summary!.EndTick);
        Assert.Equal(FighterState.Defeated, run.Result.Summary.FinalFrames.Single(x => x.FighterId == FighterId.FighterB).State);
        Assert.Single(run.Events, x => x.EventType == CombatEventType.FighterDefeated);
        Assert.DoesNotContain(run.Events, x => x.Payload is StateChangedPayload { OldState: FighterState.KnockedDown, NewState: FighterState.DecisionReady });
        Assert.DoesNotContain(run.Events, x => x.EffectId?.Value == "effect_wakeup_immunity");
    }

    [Fact, Trait("AcceptanceId", "WP10-KDN-008")]
    public void ThrowForceAndWallConsequencesPrecedeOneKnockdownAndDistinctLifecycleFatigue()
    {
        var source = Source(14); source["settings"]!["global.arena.start_position_a"] = 8000; source["settings"]!["global.arena.start_position_b"] = 9500;
        var attack = Attack(source, active: 2, schedule: "grab:0|throw:1", tags: "grab|knockdown|wall_impact");
        attack["cooldown_ticks"] = 100; attack["movement_mode"] = "Push";
        attack["base_knockback"] = 1000; attack["knockback_max"] = 1000;
        attack["wall_impact"] = true; attack["wall_damage_per_unit_fp"] = 100;
        attack["wall_damage_min"] = 10; attack["wall_damage_max"] = 100;
        var run = Run(source); run.Completed();
        var knockdown = Assert.Single(run.Events, x => x.Payload is StateChangedPayload { NewState: FighterState.KnockedDown, OldState: not FighterState.KnockedDown });
        var force = Assert.Single(run.Events, x => x.EventType == CombatEventType.KnockbackApplied);
        var wall = Assert.Single(run.Events, x => x.EventType == CombatEventType.WallImpact);
        Assert.True(force.Sequence < knockdown.Sequence); Assert.True(wall.Sequence < knockdown.Sequence);
        Assert.Single(run.Events, x => x.EventType == CombatEventType.GrabEnded);
        Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_grab_lockout");
        Assert.Single(run.Events, x => x.EventType == CombatEventType.ActionCancelled && x.ActorId == FighterId.FighterB);
        var fatigue = run.Events.Where(x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_fatigue").ToArray();
        Assert.Equal(2, fatigue.Length); Assert.Equal(new[] { 1, 2 }, fatigue.Select(x => Assert.IsType<EffectAddedPayload>(x.Payload).StacksAfter));
        Assert.Equal(2, fatigue.Select(x => x.SourceEventId).Distinct().Count());
    }

    private static string Trace(EffectRun run) => string.Join("; ", run.Events.Where(x => x.Payload is StateChangedPayload || x.EffectId.HasValue)
        .Select(x => x.Tick + ":" + x.EventType + ":" + x.EffectId + ":" + string.Join(",", x.ReasonCodes)));
}
