using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Core.Engine;
using Battle.Core.Random;
using static CombatLab.IntegrationTests.Effects.EffectEngineFixture;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectRemainingControlAndStackTests
{
    [Theory, Trait("AcceptanceId", "WP10-STACK-012")]
    [InlineData(32), InlineData(128)]
    public void ReplacementAtCapNeverCreatesExtraPublicEntryAndNewGroupFailsAtomically(int cap)
    {
        var source = Source(2);
        source["settings"]!["global.control.max_effect_instances_per_fighter"] = cap;
        source["settings"]!["global.control.max_triggers_per_tick"] = cap * 2 + 4;
        for (var index = 0; index < cap; index++)
        {
            var id = "effect_slot_" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
            Effect(source, id, value: 1, duration: 20, policy: "Replace", group: id);
            Rule(source, "rule_slot_" + index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), id);
        }
        Effect(source, "effect_replacement", value: 10, duration: 20, policy: "Replace", group: "effect_slot_000");
        Rule(source, "rule_replace", "effect_replacement", "EndOfTick");
        var run = Run(source); run.Completed();
        var deltas = run.Events.Where(x => x.Tick == 0 && x.EffectId?.Value is "effect_slot_000" or "effect_replacement" &&
            x.Sequence > cap * 2).ToArray();
        Assert.Equal(new[] { CombatEventType.EffectRemoved, CombatEventType.EffectAdded, CombatEventType.EffectRemoved, CombatEventType.EffectAdded }, deltas.Select(x => x.EventType));
        Assert.All(deltas.Where(x => x.EventType == CombatEventType.EffectRemoved), x =>
            Assert.Equal(EffectRemoveReason.Replaced, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason));
        Assert.All(run.Events.SelectMany(x => new[] { x.Before.Actor, x.Before.Target, x.After.Actor, x.After.Target }).Where(x => x is not null), frame =>
        {
            Assert.InRange(frame!.Effects.Count, 0, cap);
            Assert.Equal(frame.Effects.Count, frame.Effects.Select(x => x.EffectId).Distinct().Count());
        });
        Assert.Equal(cap, Assert.Single(run.Observer.Samples, x => x.Tick == 1 && x.Phase == TickPhase.Decisions).A.Effects.Count);

        // A later new group cannot commit even the preceding replacement in this EndOfTick batch.
        Effect(source, "effect_overflow", value: 1, duration: 20); Rule(source, "rule_z_overflow", "effect_overflow", "EndOfTick");
        run = Run(source);
        Assert.Equal(BattleResultStatus.FailedInvariant, run.Result.Status);
        Assert.Equal("EffectInstanceCapExceeded", run.Result.InvariantFailure!.Code.Value);
        Assert.DoesNotContain(run.Events, x => x.EffectId?.Value is "effect_replacement" or "effect_overflow");
        Assert.Equal(cap * 2, run.Events.Count(x => x.Payload is EffectRemovedPayload { RemoveReason: EffectRemoveReason.BattleEnded }));
        Assert.Equal(1, run.Journal.Begins); Assert.Equal(1, run.Journal.Completes);
        Assert.All(run.Journal.Canonical.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
        Assert.Equal(0, run.Observer.FinalState!.Effects!.Queue.Budget(FighterId.FighterA, new StableId("rule_replace")).BattleCount);
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-008")]
    public void GrabEndAt10BlocksBothAvailableGrabIdsThrough29AndAllowsNewGrabAt30()
    {
        var source = Source(32);
        // Align the next decision with tick30, rather than observing an actor still busy in wait.
        Entity(source, "actions", "action_id", "sys_wait")["active_ticks"] = 5;
        Attack(source, startup: 9, schedule: "grab:0", tags: "grab")["cooldown_ticks"] = 100;
        var alternate = Attack(source, "bear_rampage_charge", schedule: "grab:0", tags: "grab");
        alternate["base_weight"] = 1; alternate["cooldown_ticks"] = 100;
        var run = Run(source); run.Completed();
        var start = run.Events.Where(x => x.EventType == CombatEventType.GrabStarted).ToArray();
        Assert.Equal(new[] { 9, 30 }, start.Select(x => x.Tick));
        Assert.Equal(new[] { "bear_earthbreaker", "bear_rampage_charge" }, start.Select(x => x.ActionId!.Value.Value));
        var firstEnd = run.Events.First(x => x.EventType == CombatEventType.GrabEnded);
        Assert.Equal(10, firstEnd.Tick);
        var locked = run.Events.First(x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_grab_lockout");
        Assert.Equal(firstEnd.EventId, locked.SourceEventId); Assert.Equal(FighterId.FighterB, locked.ActorId);
        var removed = run.Events.First(x => x.EventType == CombatEventType.EffectRemoved && x.EffectId?.Value == "effect_grab_lockout");
        Assert.Equal(29, removed.Tick); Assert.Equal(EffectRemoveReason.ExpiredAfterTick, Assert.IsType<EffectRemovedPayload>(removed.Payload).RemoveReason);
        foreach (var sample in run.Observer.Samples.Where(x => x.Tick is >= 10 and <= 29 && x.Phase == TickPhase.Decisions))
            Assert.Contains(sample.B.Effects, x => x.EffectId.Value == "effect_grab_lockout");
        Assert.DoesNotContain(run.Events, x => x.Tick is >= 10 and <= 29 && x.ActorId == FighterId.FighterA &&
            x.EventType == CombatEventType.ActionCommitted && x.ActionId?.Value is "bear_earthbreaker" or "bear_rampage_charge");
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-010")]
    public void FailedEligibleBlockActivatesGuardBreakAfterGroupAndUpdatesNextGroupAndDecision()
    {
        var source = Source(10);
        Entity(source, "fighters", "animal_id", "kangaroo")["guard"] = 100;
        var attack = Attack(source, active: 2, schedule: "0|1", tags: "guard_break|strike");
        attack["blockable"] = true; attack["cooldown_ticks"] = 100; attack["hit_interrupt_strength"] = 0;
        var block = Attack(source, "kangaroo_flying_kick", active: 8, schedule: "", tags: "block");
        block["block_base_chance_fp"] = 500; block["block_reduction_fp"] = 500; block["cooldown_ticks"] = 0;
        ulong seed = 0;
        while (seed < 100000 && Pcg32Stream.CreateResolution(seed).NextInt(0, 1000, RngOperation.ChanceCheck).Result != 999) seed++;
        Assert.True(seed < 100000);
        var run = Run(source, seed); run.Completed();
        var hit = Assert.Single(run.Events, x => x.Tick == 0 && x.EventType == CombatEventType.AttackHit);
        Assert.Contains(hit.ReasonCodes, x => x.Value == "GuardBreak");
        var added = Assert.Single(run.Events, x => x.Tick == 0 && x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_guard_broken");
        var damage = Assert.Single(run.Events, x => x.Tick == 0 && x.EventType == CombatEventType.DamageApplied);
        Assert.True(damage.Sequence < added.Sequence); Assert.Equal(hit.EventId, added.SourceEventId);
        var next = Assert.Single(run.Observer.Samples, x => x.Tick == 1 && x.Phase == TickPhase.Resolve);
        Assert.Equal(70, next.GuardB); Assert.Equal(650, next.BlockWeightB);
        var nextDecision = Assert.Single(run.Events, x => x.Tick == 8 && x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DecisionMade);
        Assert.Contains(nextDecision.Before.Actor!.Effects, x => x.EffectId.Value == "effect_guard_broken");
        var view = Assert.Single(run.Observer.Samples, x => x.Tick == 8 && x.Phase == TickPhase.Decisions);
        Assert.Equal(70, view.GuardB); Assert.Equal(650, view.BlockWeightB);
        Assert.Equal(2, run.Events.Count(x => x.Rng?.Operation == RngOperation.ChanceCheck));
    }

    [Theory, Trait("AcceptanceId", "WP10-CTRL-012")]
    [InlineData("maxhold"), InlineData("release"), InlineData("throw"), InlineData("lethal_throw")]
    public void ExistingMaxHoldReleaseThrowAndDeadTargetRulesAreNotExtendedByEffects(string scenario)
    {
        var source = Source(scenario == "maxhold" ? 14 : 4);
        var attack = Attack(source, damage: scenario == "lethal_throw" ? 3000 : 30,
            active: scenario == "maxhold" ? 20 : scenario == "release" ? 1 : 2,
            schedule: scenario is "throw" or "lethal_throw" ? "grab:0|throw:1" : "grab:0", tags: "grab");
        attack["cooldown_ticks"] = 100;
        if (scenario == "lethal_throw") Entity(source, "fighters", "animal_id", "kangaroo")["max_health"] = 100;
        if (scenario == "maxhold")
        {
            var immunity = Effect(source, "effect_mid_grab_immunity", "HardControlAllowed", 0, duration: 30);
            immunity["operation1"] = "Override"; Rule(source, "rule_mid_grab", "effect_mid_grab_immunity", "EndOfTick");
        }
        var run = Run(source); run.Completed();
        var end = Assert.Single(run.Events, x => x.EventType == CombatEventType.GrabEnded);
        Assert.Equal(scenario == "maxhold" ? 12 : 1, end.Tick);
        Assert.Equal(scenario == "maxhold" ? GrabEndReason.MaxHoldReached : scenario == "release" ? GrabEndReason.Release : GrabEndReason.Throw,
            Assert.IsType<GrabEndedPayload>(end.Payload).EndReason);
        Assert.Single(run.Events, x => x.EventType == CombatEventType.GrabStarted);
        if (scenario == "lethal_throw")
        {
            Assert.Equal(BattleOutcome.FighterAWin, run.Result.Summary!.Outcome); Assert.Equal(1, run.Result.Summary.EndTick);
            Assert.DoesNotContain(run.Events, x => x.EffectId?.Value is "effect_control_fatigue" or "effect_grab_lockout");
        }
        else
        {
            Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_grab_lockout");
            Assert.Single(run.Events, x => x.EventType == CombatEventType.EffectAdded && x.EffectId?.Value == "effect_control_fatigue");
        }
    }

    [Fact]
    public void ImmunityDoesNotPreventSameGroupLethalTradeOrTurnDoubleKoIntoControlExit()
    {
        var source = Source(2);
        foreach (var id in new[] { "bear", "kangaroo" }) Entity(source, "fighters", "animal_id", id)["max_health"] = 100;
        Attack(source, damage: 3000); Attack(source, "kangaroo_flying_kick", damage: 3000);
        var immunity = Effect(source, "effect_immunity", "HardControlAllowed", 0, duration: 30); immunity["operation1"] = "Override";
        Rule(source, "rule_immunity", "effect_immunity");
        var run = Run(source); run.Completed();
        Assert.Equal(BattleEndReason.DoubleKO, run.Result.Summary!.EndReason);
        var damage = run.Events.Where(x => x.EventType == CombatEventType.DamageApplied).ToArray();
        Assert.Equal(2, damage.Length); Assert.Equal(damage[0].ResolutionGroupId, damage[1].ResolutionGroupId);
        Assert.Equal(2, run.Events.Count(x => x.EventType == CombatEventType.FighterDefeated));
        Assert.DoesNotContain(run.Events, x => x.EffectId?.Value == "effect_control_fatigue");
    }
}
