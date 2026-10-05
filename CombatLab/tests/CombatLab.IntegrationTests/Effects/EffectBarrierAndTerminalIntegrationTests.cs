using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Core.Engine;
using static CombatLab.IntegrationTests.Effects.EffectEngineFixture;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectBarrierAndTerminalIntegrationTests
{
    [Fact, Trait("AcceptanceId", "WP10-MOD-012")]
    public void TradeFreezesBothImpactsButNextGroupAndNextTickUseFreshStatsWithoutRetroactiveSorting()
    {
        var source = Source(3);
        foreach (var id in new[] { "bear_earthbreaker", "kangaroo_flying_kick" })
        {
            var attack = Attack(source, id, damage: 100, active: 2, schedule: "0|1");
            attack["cooldown_ticks"] = 100; attack["action_priority"] = 0;
            attack["hit_interruptible_phases"] = "Startup";
        }
        Entity(source, "fighters", "animal_id", "bear")["initiative"] = 200;
        Entity(source, "fighters", "animal_id", "kangaroo")["initiative"] = 100;
        Effect(source, "effect_armor", duration: 2);
        Rule(source, "rule_armor", "effect_armor", "DamageDealt");
        var trade = Run(source); trade.Completed();
        var damage = trade.Events.Where(x => x.Payload is DamageAppliedPayload).ToArray();
        Assert.Equal(new[] { 0, 0, 1, 1 }, damage.Select(x => x.Tick));
        Assert.Equal(new[] { 66, 66, 50, 50 }, damage.Select(x => Assert.IsType<DamageAppliedPayload>(x.Payload).HealthBefore - Assert.IsType<DamageAppliedPayload>(x.Payload).HealthAfter));
        Assert.Equal(damage[0].ResolutionGroupId, damage[1].ResolutionGroupId);
        Assert.All(trade.Events.Where(x => x.EffectId?.Value == "effect_armor" && x.EventType == CombatEventType.EffectAdded),
            x => Assert.True(x.Sequence > damage[1].Sequence));
        AssertPostExpirySnapshot(trade);

        // Unequal clash priorities create two groups. The first adds armor to its actor and
        // initiative to its opponent; that opponent's already-ordered intent remains second.
        Entity(source, "actions", "action_id", "kangaroo_flying_kick")["clash_priority"] = 9;
        Effect(source, "effect_initiative", "Initiative", 500, duration: 2);
        Rule(source, "rule_initiative", "effect_initiative", "DamageDealt", "Action", "bear_earthbreaker", "Opponent");
        var separate = Run(source); separate.Completed();
        damage = separate.Events.Where(x => x.Payload is DamageAppliedPayload).ToArray();
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, damage.Where(x => x.Tick == 0).Select(x => x.ActorId!.Value));
        Assert.Equal(new[] { 66, 50 }, damage.Where(x => x.Tick == 0).Select(x => Assert.IsType<DamageAppliedPayload>(x.Payload).HealthBefore - Assert.IsType<DamageAppliedPayload>(x.Payload).HealthAfter));
        Assert.NotEqual(damage[0].ResolutionGroupId, damage[1].ResolutionGroupId);
        var initiative = Assert.Single(separate.Events, x => x.EffectId?.Value == "effect_initiative" && x.EventType == CombatEventType.EffectAdded);
        Assert.True(damage[0].Sequence < initiative.Sequence && initiative.Sequence < damage[1].Sequence);
        Assert.Equal(new[] { FighterId.FighterB, FighterId.FighterA }, damage.Where(x => x.Tick == 1).Select(x => x.ActorId!.Value));
        AssertPostExpirySnapshot(separate);
    }

    [Theory, Trait("AcceptanceId", "WP10-SAFE-006")]
    [InlineData(false), InlineData(true)]
    public void TimeoutOrLethalTickCleansOnceWithoutNormalHooksOrRemovalReapplication(bool lethal)
    {
        var source = Source(lethal ? 5 : 1);
        Effect(source, "effect_survivor", duration: 20); Rule(source, "rule_start", "effect_survivor");
        Effect(source, "effect_forbidden", "Power", 10, duration: 20);
        Effect(source, "effect_normal_end", "Precision", 1, duration: 20);
        Rule(source, "rule_end", "effect_normal_end", "EndOfTick");
        Rule(source, "rule_cleanup", "effect_forbidden", "EffectRemoved", "Effect", "effect_survivor");
        if (lethal)
        {
            Entity(source, "fighters", "animal_id", "kangaroo")["max_health"] = 100;
            Attack(source, damage: 3000)["cooldown_ticks"] = 100;
        }
        var run = Run(source); run.Completed();
        Assert.Equal(lethal ? 0 : 1, run.Result.Summary!.EndTick);
        Assert.Equal(lethal ? BattleEndReason.Defeat : BattleEndReason.TimeoutEqualHealthFraction, run.Result.Summary.EndReason);
        Assert.DoesNotContain(run.Events, x => x.EffectId?.Value == "effect_forbidden");
        Assert.DoesNotContain(run.Events, x => x.EffectId?.Value == "effect_normal_end" && x.EventType == CombatEventType.EffectAdded && x.Tick == run.Result.Summary.EndTick);
        var removals = run.Events.Where(x => x.Payload is EffectRemovedPayload).ToArray();
        Assert.Equal(lethal ? 2 : 4, removals.Length);
        Assert.All(removals, x => Assert.Equal(EffectRemoveReason.BattleEnded, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason));
        Assert.All(removals, x => Assert.True(x.Sequence < run.Events[^1].Sequence));
        Assert.Single(run.Events, x => x.EventType == CombatEventType.BattleEnded);
        Assert.Empty(run.Observer.FinalState!.Effects!.Store(FighterId.FighterA).Active);
        Assert.Empty(run.Observer.FinalState.Effects.Store(FighterId.FighterB).Active);
    }

    [Fact, Trait("AcceptanceId", "WP10-SAFE-007")]
    public void LethalGroupSuppressesLateEffectOnZeroHpSubjectBeforeDefeatAndCannotChangeOutcome()
    {
        var source = Source(4); Attack(source, damage: 3000);
        Entity(source, "fighters", "animal_id", "kangaroo")["max_health"] = 100;
        Effect(source, "effect_late", "Power", 10, duration: 20);
        Rule(source, "rule_after_damage", "effect_late", "DamageTaken");
        var run = Run(source); run.Completed();
        var damage = Assert.Single(run.Events, x => x.Payload is DamageAppliedPayload);
        Assert.Equal(0, damage.After.Target!.Health);
        var defeated = Assert.Single(run.Events, x => x.EventType == CombatEventType.FighterDefeated);
        Assert.True(damage.Sequence < defeated.Sequence);
        Assert.Equal(BattleOutcome.FighterAWin, run.Result.Summary!.Outcome);
        Assert.Equal(0, run.Result.Summary.EndTick);
        Assert.DoesNotContain(run.Events, x => x.EventType == CombatEventType.EffectAdded);
        Assert.Equal(0, run.Observer.FinalState!.Effects!.Queue.Budget(FighterId.FighterB, new StableId("rule_after_damage")).BattleCount);
        var victim = run.Result.Summary.FinalFrames.Single(x => x.FighterId == FighterId.FighterB);
        Assert.Equal(0, victim.Health); Assert.Equal(FighterState.Defeated, victim.State);
        Assert.DoesNotContain(run.Events, x => x.ActorId == FighterId.FighterB && x.Before.Actor?.Health == 0 && x.After.Actor?.Health > 0);
    }

    private static void AssertPostExpirySnapshot(EffectRun run)
    {
        var samples = run.Observer.Samples.Where(x => x.Tick == 2 && x.Phase == TickPhase.Decisions).ToArray();
        var snapshot = Assert.Single(samples);
        Assert.Equal(100, snapshot.ArmorA); Assert.Equal(100, snapshot.ArmorB);
        Assert.Empty(snapshot.A.Effects); Assert.Empty(snapshot.B.Effects);
        var decisions = run.Events.Where(x => x.Tick == 2 && x.EventType == CombatEventType.DecisionMade).ToArray();
        Assert.Equal(2, decisions.Length);
        Assert.All(decisions, x => { Assert.Empty(x.Before.Actor!.Effects); Assert.Empty(x.Before.Target!.Effects); });
    }
}
