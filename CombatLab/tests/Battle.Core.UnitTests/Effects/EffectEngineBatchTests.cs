using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Contracts.Requests;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Random;
using Battle.Core.Resolution;
using Battle.Core.Safety;
using Battle.Core.UnitTests.Engine;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectEngineBatchTests
{
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-009")]
    public void DamageContextPrecedesArmorAndChipButNotDirectWallDamage()
    {
        var global = EngineTestFixture.CreateSetup().Settings.Resolution.Global;
        var action = Profile("action_a", powerRatio: 500, reduction: 500, wall: true);
        var normal = ResolutionMath.ComputeDamage(global, action, 100, 100, damageTakenFixedPoint: 1250);
        var blocked = ResolutionMath.ComputeDamage(global, action, 100, 100, blocked: true, damageTakenFixedPoint: 1250);
        Assert.Equal(150, normal.PowerTerm); Assert.Equal(187, normal.Raw); Assert.Equal(124, normal.AfterArmor);
        Assert.Equal(124, normal.Final); Assert.Equal(62, blocked.Final);
        Assert.Equal(100, ResolutionMath.ComputeDamage(global, action, 100, 100).Final);
        Assert.Equal(50, ResolutionMath.ComputeDamage(global, action, 100, 100, blocked: true).Final);
        var health = ResolutionMath.ApplyHealth(blocked, 40);
        Assert.Equal(0, health.HealthAfter); Assert.Equal(40, health.ActualHealthLoss); Assert.Equal(22, health.Overkill); Assert.True(health.Lethal);
        Assert.Equal(6, ResolutionMath.ComputeWallDamage(global, action, 20));
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-010")]
    [Trait("AcceptanceId", "WP10-DET-008")]
    public void OffsetsPrecedeClampAndEligibleDrawEqualityFails()
    {
        var global = EngineTestFixture.CreateSetup().Settings.Resolution.Global;
        var block = Profile("block", defense: "block"); var dodge = Profile("dodge", defense: "dodge");
        Assert.Equal(610, ResolutionMath.ComputeBlockChance(global, block, 100, 80, 50));
        Assert.Equal(450, ResolutionMath.ComputeDodgeChance(global, dodge, 120, 120, -50));
        Assert.Equal(900, ResolutionMath.ComputeBlockChance(global, block, 100, 80, int.MaxValue));
        Assert.Equal(50, ResolutionMath.ComputeDodgeChance(global, dodge, 120, 120, int.MinValue));
        foreach (var defense in new[] { block, dodge })
        {
            var chance = defense.Id.Value == "block" ? 610 : 450;
            var target = defense.Id.Value == "block" ? EffectModifierTarget.BlockChanceOffset : EffectModifierTarget.DodgeChanceOffset;
            var test = new EffectRuntimeFixture([Effect(target: target, value: defense.Id.Value == "block" ? 50 : -50)],
                [Rule()], seed: SeedFor(chance));
            test.Start(); Commit(test, FighterId.FighterA, Profile("action_a")); Commit(test, FighterId.FighterB, defense);
            Resolve(test);
            var hit = Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.AttackHit);
            Assert.Equal(chance, hit.Rng!.Value.Result);
            Assert.DoesNotContain(test.Journal.Drafts, x => x.EventType is CombatEventType.Blocked or CombatEventType.Dodged);
            Assert.Equal(1UL, test.State.Rng.Resolution.NextDrawIndex);
        }
        var inactive = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.BlockChanceOffset, value: 50)], [Rule()]);
        inactive.Start(); Commit(inactive, FighterId.FighterA, Profile("action_a")); Resolve(inactive);
        Assert.Equal(0UL, inactive.State.Rng.Resolution.NextDrawIndex);
        Assert.Null(Assert.Single(inactive.Journal.Drafts, x => x.EventType == CombatEventType.AttackHit).Rng);
    }

    [Fact]
    public void SuccessfulBlockAndControlUseEffectiveChannelsInAtomicResolution()
    {
        var blocked = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.DamageTaken, value: 1250,
            operation: EffectModifierOperation.Multiply)], [Rule()], seed: SeedFor(100));
        blocked.Start(); Commit(blocked, FighterId.FighterA, Profile("action_a", powerRatio: 500));
        Commit(blocked, FighterId.FighterB, Profile("block", defense: "block", reduction: 500)); Resolve(blocked);
        Assert.Contains(blocked.Journal.Drafts, x => x.EventType == CombatEventType.Blocked);
        Assert.Equal(62, Assert.IsType<DamageAppliedPayload>(Assert.Single(blocked.Journal.Drafts,
            x => x.EventType == CombatEventType.DamageApplied).Payload).Breakdown.Final);
        var control = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.HardControlDuration, value: 750,
            operation: EffectModifierOperation.Multiply)], [Rule()]);
        control.Start(); Commit(control, FighterId.FighterA, Profile("action_a", stagger: 10)); Resolve(control);
        Assert.Equal(10, control.State.FighterB.Stagger);
        Assert.Equal(0UL, control.State.Rng.Resolution.NextDrawIndex);
    }

    [Fact]
    public void EqualTradeReadsPreGroupArmorAndNextGroupReadsUpdatedArmor()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.Armor, value: 200, duration: 6, policy: EffectStackPolicy.Refresh)],
            [Rule(trigger: EffectTrigger.DamageTaken)]);
        test.Start(); Commit(test, FighterId.FighterA, Profile("action_a", hits: 2)); Commit(test, FighterId.FighterB, Profile("action_b", hits: 2));
        Resolve(test);
        var first = test.Journal.Drafts.Where(x => x.EventType == CombatEventType.DamageApplied).ToArray();
        Assert.Equal(2, first.Length);
        Assert.All(first, draft => Assert.Equal(66, Assert.IsType<DamageAppliedPayload>(draft.Payload).Breakdown.Final));
        var added = test.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectAdded).ToArray();
        Assert.All(added, draft => Assert.True(draft.Sequence > first.Max(x => x.Sequence)));
        Assert.Equal(300, test.State.FighterA.Armor); Assert.Equal(300, test.State.FighterB.Armor);
        test.At(1); Resolve(test);
        Assert.All(test.Journal.Drafts.Where(x => x.EventType == CombatEventType.DamageApplied && x.Tick == 1),
            draft => Assert.Equal(40, Assert.IsType<DamageAppliedPayload>(draft.Payload).Breakdown.Final));
        Assert.Equal(4, test.Journal.Drafts.Count(x => x.EventType == CombatEventType.DamageApplied));
    }

    [Fact]
    public void ResolutionCapFailureRollsBackImpactRngDamageConsumptionAndEffects()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.DamageTaken)], maximumEvents: 4);
        test.Start(); Commit(test, FighterId.FighterA, Profile("action_a")); Commit(test, FighterId.FighterB, Profile("block", defense: "block"));
        var before = ProgressStamp.Capture(test.State);
        var group = Assert.Single(ImpactIntentOrderer.BuildGroups(ImpactIntentCollector.Collect(test.State), 0));
        var error = Assert.Throws<EngineInvariantException>(() => ResolutionSystem.ResolveTick(test.State, test.Settings, test.Emitter, [group]));
        Assert.Equal(EngineFailureCodes.EventCapExceeded, error.Code);
        Assert.Equal(before, ProgressStamp.Capture(test.State)); Assert.Single(test.Journal.Drafts);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
        Assert.False(test.State.IsHitGroupConsumed(group.Intents[0].HitGroupId, group.Intents[0].TargetId));
        Assert.Equal(0, test.Runtime.Queue.AdmittedThisTick); Assert.Equal(0, test.Runtime.ActiveCount);
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-013")]
    public void AOnlyEffectDoesNotDriftBCacheOrLeakThroughReusedEngineAndConfig()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.DamageTaken, value: 1250,
                operation: EffectModifierOperation.Multiply), Effect("stagger", target: EffectModifierTarget.ControlResistance, value: 100)],
            [Rule(trigger: EffectTrigger.DamageDealt), Rule("rule_stagger", "stagger", EffectTrigger.DamageDealt)]);
        test.Start(); test.Pulse();
        Assert.Equal(1250, test.Runtime.Channel(FighterId.FighterA, EffectModifierTarget.DamageTaken));
        Assert.Equal(1000, test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.DamageTaken));
        Assert.Equal(200, test.State.FighterA.ControlResistance); Assert.Equal(100, test.State.FighterB.ControlResistance);
        var isolation = new EffectRuntimeFixture([Effect()], [Rule(battleCap: 1)]);
        var engine = new Battle.Core.CombatEngine(isolation.Definition);
        var config = EngineTestFixture.CreateConfig(timeLimit: 2);
        var request = EngineTestFixture.CreateRequest();
        var first = new RecordingJournal(); var second = new RecordingJournal(); var third = new RecordingJournal();
        Assert.Equal(BattleResultStatus.Completed, engine.Simulate(request, config, first).Status);
        var different = new BattleRequest(request.BattleId, request.EngineVersion, request.ConfigHash, request.ModeRules,
            request.MasterSeed + 1, request.BuildA, request.BuildB);
        Assert.Equal(BattleResultStatus.Completed, engine.Simulate(different, config, second).Status);
        Assert.Equal(BattleResultStatus.Completed, engine.Simulate(request, config, third).Status);
        Assert.Equal(first.Drafts.Select(x => (x.EventType, x.ActorId, x.ActionId, x.Sequence)), third.Drafts.Select(x => (x.EventType, x.ActorId, x.ActionId, x.Sequence)));
        Assert.All(first.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
        Assert.All(new[] { first, second, third }, journal =>
            Assert.Equal(2, journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded)));
    }

    [Fact]
    public void ControlledSimulateRunsStartAfterBothFramesAndCleanupBeforeComplete()
    {
        var fixture = new EffectRuntimeFixture([Effect(duration: 100)], [Rule(), Rule("rule_reapply", trigger: EffectTrigger.EffectRemoved)]);
        var journal = new RecordingJournal();
        var result = new Battle.Core.CombatEngine(fixture.Definition).Simulate(EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: 1), journal);
        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Equal(1, journal.BeginCount); Assert.Equal(1, journal.CompleteCount);
        Assert.All(journal.Start!.FighterA.InitialFrame.Effects, _ => Assert.Fail("Initial frames precede BattleStart rules."));
        Assert.Empty(journal.Start.FighterB.InitialFrame.Effects);
        Assert.Equal(CombatEventType.BattleStarted, journal.Drafts[0].EventType);
        Assert.Equal(2, journal.Drafts.Count(x => x.EventType == CombatEventType.EffectAdded));
        Assert.All(journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved), x =>
            Assert.Equal(EffectRemoveReason.BattleEnded, Assert.IsType<EffectRemovedPayload>(x.Payload).RemoveReason));
        Assert.All(result.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
        Assert.Equal(CombatEventType.BattleEnded, journal.Drafts.Last().EventType);
    }

    [Fact]
    public void FailedInitialClosureStillHasStartedThenInvalidEndedJournalLifecycle()
    {
        var fixture = new EffectRuntimeFixture([Effect()], [Rule()]);
        var journal = new RecordingJournal();
        var result = new Battle.Core.CombatEngine(fixture.Definition).Simulate(EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(maximumEvents: 5), journal);
        Assert.Equal(BattleResultStatus.FailedInvariant, result.Status);
        Assert.Equal(EngineFailureCodes.EventCapExceeded, result.InvariantFailure!.Code);
        Assert.Equal(new[] { CombatEventType.BattleStarted, CombatEventType.BattleEnded }, journal.Drafts.Select(x => x.EventType));
        Assert.Equal(BattleOutcome.Invalid, journal.Summary!.Outcome);
        Assert.All(journal.Summary.FinalFrames, x => Assert.Empty(x.Effects));
        Assert.Equal(1, journal.BeginCount); Assert.Equal(1, journal.CompleteCount);
    }

    [Fact]
    public void ControlledSimulateFailureWithActiveEffectsUsesReservedInvalidCleanup()
    {
        var fixture = new EffectRuntimeFixture([Effect(policy: EffectStackPolicy.Replace, duration: 100)],
            [Rule(), Rule("rule_tick", trigger: EffectTrigger.EndOfTick)]);
        var journal = new RecordingJournal();
        var result = new Battle.Core.CombatEngine(fixture.Definition).Simulate(EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: 3, maximumEvents: 12), journal);
        Assert.Equal(BattleResultStatus.FailedInvariant, result.Status);
        Assert.Equal(BattleOutcome.Invalid, journal.Summary!.Outcome);
        Assert.Equal(EngineFailureCodes.EventCapExceeded, result.InvariantFailure!.Code);
        Assert.All(journal.Summary.FinalFrames, x => Assert.Empty(x.Effects));
        Assert.Equal(1, journal.CompleteCount); Assert.Equal(CombatEventType.BattleEnded, journal.Drafts.Last().EventType);
        Assert.InRange(journal.Drafts.Count, 2, 12);
    }

    [Fact]
    public void CoordinatorExpiryUpdatesStatsBeforeBothDecisionsAndHandlesControlLifecycle()
    {
        var fixture = new EffectRuntimeFixture([Effect(duration: 1, value: 30)], [Rule()]);
        fixture.Start();
        _ = fixture.State.FighterA.BeginStun(0, 1, FighterId.FighterB, fixture.State.FighterB.NextDecisionId());
        fixture.At(1);
        var coordinator = new TickCoordinator(100);
        _ = coordinator.RunActiveTick(fixture.State, fixture.Settings, fixture.Emitter);
        Assert.Empty(fixture.State.FighterA.Effects); Assert.Equal(120, fixture.State.FighterA.Precision);
        var firstDecision = fixture.Journal.Drafts.First(x => x.EventType == CombatEventType.DecisionMade);
        Assert.All(fixture.Journal.Drafts.Where(x => x.EventType == CombatEventType.EffectRemoved), x => Assert.True(x.Sequence < firstDecision.Sequence));
        Assert.Contains(fixture.Journal.Drafts, x => x.Payload is StateChangedPayload change && change.OldState == FighterState.Stunned);
    }

    private static void Resolve(EffectRuntimeFixture fixture) => ResolutionSystem.ResolveTick(fixture.State, fixture.Settings,
        fixture.Emitter, ImpactIntentOrderer.BuildGroups(ImpactIntentCollector.Collect(fixture.State), fixture.State.Tick));
    private static void Commit(EffectRuntimeFixture fixture, FighterId fighter, ResolutionActionProfile profile)
    {
        var actor = fixture.State.Get(fighter); var target = fixture.State.GetOpponent(fighter);
        var decision = actor.NextDecisionId();
        actor.CommitCombatAction(new CombatActionDescriptor(profile.Id, profile.Category, decision,
            target.FighterId, target.Position, fighter == FighterId.FighterA ? CommitDirection.Right : CommitDirection.Left,
            0, 0, 0, profile.ActiveTicks, 0, 0, profile, false, true, fixture.State.Tick));
    }
    private static ResolutionActionProfile Profile(string id, int hits = 1, string? defense = null, int powerRatio = 0,
        int reduction = 0, bool wall = false, int stagger = 0) => new(new StableId(id), "Basic", "Fixture", ResolutionMovementMode.None,
            new[] { new StableId(defense ?? "strike") }, defense is null
                ? Enumerable.Range(0, hits).Select(i => new HitScheduleEntry(HitPrimitiveKind.Hit, i, i)).ToArray() : [],
            hits, defense is null ? hits : 0, 10, 10, 10, 10, 0, 10000, 100, powerRatio, 10,
            true, true, false, 500, reduction, 500, 10, stagger, 3, 0, 0, 0, 0, false, wall, 300, 0, 600, "Cancelable");
    private static ulong SeedFor(int expected)
    {
        for (ulong seed = 0; seed < 100000; seed++)
            if (Pcg32Stream.CreateResolution(seed).NextInt(0, 1000, RngOperation.ChanceCheck).Result == expected) return seed;
        throw new InvalidOperationException("A matching deterministic fixture seed was not found.");
    }
}
