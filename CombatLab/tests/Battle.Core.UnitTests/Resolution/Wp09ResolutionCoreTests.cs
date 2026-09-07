using System.Globalization;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Movement;
using Battle.Core.Random;
using Battle.Core.Resolution;
using Battle.Core.UnitTests.Engine;

namespace Battle.Core.UnitTests.Resolution;

[Trait("WorkPackage", "WP09")]
public sealed class Wp09ResolutionCoreTests
{
    [Fact]
    [Trait("AcceptanceId", "WP09-CFG-001")]
    [Trait("AcceptanceId", "WP09-CFG-002")]
    [Trait("AcceptanceId", "WP09-CFG-005")]
    [Trait("AcceptanceId", "WP09-CFG-006")]
    public void WP09_CFG_001_ResolutionSettingsAreStrictTypedAndMaterializedBeforeRuntime()
    {
        var setup = EngineTestFixture.CreateSetup(10);

        Assert.Equal(7, setup.Settings.Resolution.Actions.Count);
        Assert.All(setup.Settings.Resolution.Actions, action => Assert.NotNull(action.InterruptProfile));
        Assert.Equal(145 + 12, setup.State.FighterA.Power);
        Assert.Equal(135 + 18, setup.State.FighterA.Armor);
        Assert.Equal(140 + 15, setup.State.FighterB.Precision);
        Assert.Equal(1_000, setup.Settings.Resolution.Global.FixedPointScale);
        Assert.Equal(600, setup.Settings.Resolution.Global.DamageCap);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-CFG-003")]
    [Trait("AcceptanceId", "WP09-CFG-004")]
    public void WP09_CFG_003_InvalidResolutionDataRejectsBeforeJournalBeginWithSortedErrors()
    {
        var invalid = EngineTestFixture.CreateConfig(
            changeActions: actions => EngineTestFixture.ReindexCatalog(actions.Select(action =>
                action.Id.Value == "bear_earthbreaker"
                    ? EngineTestFixture.WithProperty(
                        EngineTestFixture.WithProperty(
                            action,
                            "hit_schedule",
                            Battle.Contracts.Config.ConfigValue.FromString("hit:0|hit:0")),
                        "tags",
                        Battle.Contracts.Config.ConfigValue.FromString("strike|strike"))
                    : action)));
        var journal = new RecordingJournal();

        var result = new Battle.Core.CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            invalid,
            journal);

        Assert.Equal(Battle.Contracts.Results.BattleResultStatus.Rejected, result.Status);
        Assert.Equal(0, journal.BeginCount);
        Assert.Contains(result.RejectionErrors, error => error.Code.Value == "InvalidHitSchedule");
        Assert.Contains(result.RejectionErrors, error => error.Code.Value == "InvalidResolutionTags");
        Assert.Equal(
            result.RejectionErrors.OrderBy(error => error.Path, StringComparer.Ordinal)
                .ThenBy(error => error.Code.Value, StringComparer.Ordinal)
                .ThenBy(error => error.EntityId?.Value, StringComparer.Ordinal),
            result.RejectionErrors);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-SCH-001")]
    [Trait("AcceptanceId", "WP09-SCH-002")]
    [Trait("AcceptanceId", "WP09-SCH-003")]
    [Trait("AcceptanceId", "WP09-SCH-004")]
    [Trait("AcceptanceId", "WP09-SCH-006")]
    public void WP09_SCH_001_TypedScheduleAndStableIdentifiersAreExact()
    {
        var decision = new DecisionId("dec-fighter_a-000001");
        var schedule = new[]
        {
            new HitScheduleEntry(HitPrimitiveKind.Counter, 0, 0),
            new HitScheduleEntry(HitPrimitiveKind.Grab, 3, 1),
            new HitScheduleEntry(HitPrimitiveKind.Throw, 5, 2),
            new HitScheduleEntry(HitPrimitiveKind.Wall, 6, 3),
        };

        Assert.Equal(
            new[] { HitPrimitiveKind.Counter, HitPrimitiveKind.Grab, HitPrimitiveKind.Throw, HitPrimitiveKind.Wall },
            schedule.Select(entry => entry.Kind));
        Assert.Equal("intent:dec-fighter_a-000001:00", ResolutionIdentifiers.Intent(decision, 0).Value);
        Assert.Equal("impact:dec-fighter_a-000001:03", ResolutionIdentifiers.Impact(decision, 3).Value);
        Assert.Equal("hit-group:dec-fighter_a-000001:02", ResolutionIdentifiers.HitGroup(decision, 2).Value);
        Assert.Equal("resolution:0000000012:0007", ResolutionIdentifiers.ResolutionGroup(12, 7).Value);
        Assert.Equal("damage:resolution:0000000012:0007:01", ResolutionIdentifiers.Damage(
            ResolutionIdentifiers.ResolutionGroup(12, 7), 1).Value);

        var profile = Profile("timed", new[] { new HitScheduleEntry(HitPrimitiveKind.Hit, 6, 0) }, activeTicks: 7);
        var descriptor = Descriptor(FighterId.FighterA, FighterId.FighterB, profile, commitTick: 10, startupTicks: 4);
        Assert.Equal(20, descriptor.AbsoluteImpactTick(profile.Schedule[0]));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-GRP-001")]
    [Trait("AcceptanceId", "WP09-GRP-002")]
    [Trait("AcceptanceId", "WP09-GRP-003")]
    [Trait("AcceptanceId", "WP09-GRP-004")]
    [Trait("AcceptanceId", "WP09-GRP-005")]
    [Trait("AcceptanceId", "WP09-GRP-006")]
    [Trait("AcceptanceId", "WP09-GRP-007")]
    public void WP09_GRP_001_IntentOrderingAndTradeGroupingAreCanonical()
    {
        Assert.Throws<ArgumentNullException>(() => ImpactIntentOrderer.Order(null!));
        var strikeA = Profile("strike_a", Hit(), actionPriority: 10, clashPriority: 20);
        var strikeB = Profile("strike_b", Hit(), actionPriority: 10, clashPriority: 20);
        var context = Context(new[] { strikeA, strikeB });
        Commit(context.State.FighterA, context.State.FighterB, strikeA);
        Commit(context.State.FighterB, context.State.FighterA, strikeB);

        var intents = ImpactIntentCollector.Collect(context.State);
        var groups = ImpactIntentOrderer.BuildGroups(intents.Reverse(), 0);

        var group = Assert.Single(groups);
        Assert.True(group.IsTrade);
        Assert.Equal(2, group.Intents.Count);
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, group.Intents.Select(item => item.ActorId));
        Assert.Equal(0UL, context.State.Rng.Resolution.NextDrawIndex);
    }

    [Theory]
    [InlineData(800, CombatEventType.AttackHit)]
    [InlineData(801, CombatEventType.AttackMissed)]
    [Trait("AcceptanceId", "WP09-GEO-001")]
    [Trait("AcceptanceId", "WP09-GEO-002")]
    [Trait("AcceptanceId", "WP09-GEO-003")]
    [Trait("AcceptanceId", "WP09-GEO-004")]
    [Trait("AcceptanceId", "WP09-GEO-005")]
    [Trait("AcceptanceId", "WP09-GEO-006")]
    public void WP09_GEO_001_LiveBodyGapUsesInclusiveBoundaries(int gap, CombatEventType expected)
    {
        var strike = Profile("range_strike", Hit(), hitRangeMin: 800, hitRangeMax: 800);
        var context = Context(new[] { strike }, fighterBPosition: checked(2_000 + 100 + 100 + gap));
        Commit(context.State.FighterA, context.State.FighterB, strike);

        Resolve(context);

        Assert.Equal(expected, context.Journal.Drafts[0].EventType);
        Assert.Equal(expected == CombatEventType.AttackHit, context.State.FighterB.Health < 1_000);
        Assert.Equal(0UL, context.State.Rng.Resolution.NextDrawIndex);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-DEF-001")]
    [Trait("AcceptanceId", "WP09-DEF-002")]
    [Trait("AcceptanceId", "WP09-DEF-003")]
    [Trait("AcceptanceId", "WP09-DEF-004")]
    [Trait("AcceptanceId", "WP09-DEF-005")]
    [Trait("AcceptanceId", "WP09-DEF-006")]
    [Trait("AcceptanceId", "WP09-DEF-007")]
    [Trait("AcceptanceId", "WP09-DEF-008")]
    [Trait("AcceptanceId", "WP09-DEF-009")]
    [Trait("AcceptanceId", "WP09-DEF-010")]
    [Trait("AcceptanceId", "WP09-DEF-011")]
    public void WP09_DEF_001_DodgeBlockAndCounterRespectChanceBoundariesAndPrecedence()
    {
        var global = Globals();
        var strike = Profile("strike", Hit(), baseDamage: 100, powerRatio: 500, chipMin: 10);
        var block = Profile(
            "block",
            Array.Empty<HitScheduleEntry>(),
            tags: new[] { "block" },
            baseDamage: 0,
            hitCount: 0,
            blockBase: 500,
            blockReduction: 500);
        var dodge = Profile(
            "dodge",
            Array.Empty<HitScheduleEntry>(),
            tags: new[] { "dodge" },
            baseDamage: 0,
            hitCount: 0,
            dodgeBase: 500);
        Assert.Equal(560, ResolutionMath.ComputeBlockChance(global, block, 100, 80));
        Assert.Equal(500, ResolutionMath.ComputeDodgeChance(global, dodge, 120, 120));

        var blockSeed = SeedForResult(559, 1_000);
        var blocked = Context(new[] { strike, block }, seed: blockSeed);
        Commit(blocked.State.FighterA, blocked.State.FighterB, strike);
        Commit(blocked.State.FighterB, blocked.State.FighterA, block);
        Resolve(blocked);
        Assert.Equal(new[] { CombatEventType.Blocked, CombatEventType.DamageApplied },
            blocked.Journal.Drafts.Select(item => item.EventType));
        Assert.Equal(950, blocked.State.FighterB.Health);
        Assert.Equal(1UL, blocked.State.Rng.Resolution.NextDrawIndex);

        var dodgeSeed = SeedForResult(499, 1_000);
        var dodged = Context(new[] { strike, dodge }, seed: dodgeSeed);
        Commit(dodged.State.FighterA, dodged.State.FighterB, strike);
        Commit(dodged.State.FighterB, dodged.State.FighterA, dodge);
        Resolve(dodged);
        Assert.Equal(CombatEventType.Dodged, Assert.Single(dodged.Journal.Drafts).EventType);
        Assert.Equal(1_000, dodged.State.FighterB.Health);

        var counter = Profile(
            "counter",
            new[] { new HitScheduleEntry(HitPrimitiveKind.Counter, 0, 0) },
            tags: new[] { "counter" },
            resolutionPriority: 10);
        var countered = Context(new[] { strike, counter });
        Commit(countered.State.FighterA, countered.State.FighterB, strike);
        Commit(countered.State.FighterB, countered.State.FighterA, counter);
        Resolve(countered);
        Assert.Equal(CombatEventType.Countered, countered.Journal.Drafts[0].EventType);
        Assert.Null(countered.State.FighterA.ActiveCombatAction);
        Assert.Equal(0UL, countered.State.Rng.Resolution.NextDrawIndex);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-DMG-001")]
    [Trait("AcceptanceId", "WP09-DMG-002")]
    [Trait("AcceptanceId", "WP09-DMG-003")]
    [Trait("AcceptanceId", "WP09-DMG-004")]
    [Trait("AcceptanceId", "WP09-DMG-005")]
    [Trait("AcceptanceId", "WP09-DMG-006")]
    [Trait("AcceptanceId", "WP09-DMG-007")]
    [Trait("AcceptanceId", "WP09-DMG-008")]
    [Trait("AcceptanceId", "WP09-DMG-010")]
    public void WP09_DMG_001_FixedPointDamageChipCapAndOverkillMatchOracle()
    {
        var strike = Profile("damage", Hit(), baseDamage: 100, powerRatio: 500, minimumDamage: 10, chipMin: 10);
        var damage = ResolutionMath.ComputeDamage(Globals(), strike, 100, 100);
        Assert.Equal(new DamageComputation(150, 150, 100, 100, 100), damage);
        Assert.Equal(new DamageMutation(damage, 70, 0, 70, 30, true), ResolutionMath.ApplyHealth(damage, 70));

        var capped = Profile("cap", Hit(), baseDamage: 1_000, minimumDamage: 0);
        Assert.Equal(600, ResolutionMath.ComputeDamage(Globals(), capped, 0, 0).Final);
        Assert.Equal(3, ResolutionMath.MultiplyFixedPoint(10, 333, 1_000));
        Assert.Throws<OverflowException>(() => ResolutionMath.MultiplyFixedPoint(int.MaxValue, int.MaxValue, 1));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-CTL-001")]
    [Trait("AcceptanceId", "WP09-CTL-002")]
    [Trait("AcceptanceId", "WP09-CTL-003")]
    [Trait("AcceptanceId", "WP09-CTL-004")]
    [Trait("AcceptanceId", "WP09-CTL-005")]
    [Trait("AcceptanceId", "WP09-CTL-006")]
    [Trait("AcceptanceId", "WP09-CTL-007")]
    public void WP09_CTL_001_ControlRatioThresholdResetAndHalfOpenExpiryMatchOracle()
    {
        var strike = Profile("control", Hit(), baseStagger: 60, baseStun: 6);
        var result = ResolutionMath.ComputeControl(Globals(), strike, 100, 100, 1_000);
        Assert.Equal(new ControlComputation(1_000, 60, 6), result);

        var fighter = Fighter(FighterId.FighterB, 3_000);
        Assert.Equal(60, fighter.ApplyStagger(60)!.Value.After);
        Assert.Equal(120, fighter.ApplyStagger(60)!.Value.After);
        _ = fighter.ApplyHardControl(6);
        Assert.Equal(FighterState.Stunned, fighter.State);
        Assert.Equal(0, fighter.ResetStagger()!.Value.After);
        for (var tick = 0; tick < 5; tick++) Assert.Null(fighter.AdvanceControlExpiry());
        Assert.Equal(FighterState.DecisionReady, fighter.AdvanceControlExpiry()!.Value.To);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-MOV-001")]
    [Trait("AcceptanceId", "WP09-MOV-002")]
    [Trait("AcceptanceId", "WP09-MOV-003")]
    [Trait("AcceptanceId", "WP09-MOV-004")]
    [Trait("AcceptanceId", "WP09-MOV-005")]
    public void WP09_MOV_001_MoveSelfBudgetUsesQuotientRemainderWithoutDrift()
    {
        Assert.Equal(new[] { 4, 3, 3 }, Enumerable.Range(0, 3)
            .Select(index => ResolutionMath.ActiveTickBudget(10, 3, index)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionMath.ActiveTickBudget(10, 3, 3));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-FRC-001")]
    [Trait("AcceptanceId", "WP09-FRC-002")]
    [Trait("AcceptanceId", "WP09-FRC-003")]
    [Trait("AcceptanceId", "WP09-FRC-004")]
    [Trait("AcceptanceId", "WP09-FRC-005")]
    [Trait("AcceptanceId", "WP09-FRC-006")]
    [Trait("AcceptanceId", "WP09-FRC-007")]
    public void WP09_FRC_001_ForcePushPullSwapAndWallMathMatchOracle()
    {
        var action = Profile(
            "force",
            Hit(),
            movement: ResolutionMovementMode.Push,
            baseKnockback: 600,
            knockbackMax: 1_000,
            wallImpact: true,
            wallDamagePerUnit: 100,
            wallDamageMin: 10,
            wallDamageMax: 100,
            tags: new[] { "strike", "wall_impact" });
        Assert.Equal(3_000, ResolutionMath.ComputeForceRatio(Globals(), action, 120));
        Assert.Equal(1_000, ResolutionMath.ComputeRequestedMove(Globals(), action, 120));
        Assert.Equal(60, ResolutionMath.ComputeWallDamage(Globals(), action, 600));

        var arena = new ArenaInterval(0, 10_000);
        var push = ForcedMovementResolver.Resolve(
            arena, ResolutionMovementMode.Push, FighterId.FighterA, 8_000, 100,
            FighterId.FighterB, 9_500, 100, MovementDirection.Right, 1_000);
        Assert.Equal(400, push.ActualMove);
        Assert.Equal(600, push.BlockedByWall);
        var pull = ForcedMovementResolver.Resolve(
            arena, ResolutionMovementMode.Pull, FighterId.FighterA, 2_000, 100,
            FighterId.FighterB, 3_000, 100, MovementDirection.Right, 1_000);
        Assert.Equal(2_200, pull.TargetTo);
        var swap = ForcedMovementResolver.Resolve(
            arena, ResolutionMovementMode.Swap, FighterId.FighterA, 2_000, 100,
            FighterId.FighterB, 3_000, 100, MovementDirection.Right, 1_000);
        Assert.Equal((3_000, 2_000), (swap.ActorTo, swap.TargetTo));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-GRB-001")]
    [Trait("AcceptanceId", "WP09-GRB-002")]
    [Trait("AcceptanceId", "WP09-GRB-003")]
    [Trait("AcceptanceId", "WP09-GRB-004")]
    [Trait("AcceptanceId", "WP09-GRB-005")]
    [Trait("AcceptanceId", "WP09-GRB-006")]
    [Trait("AcceptanceId", "WP09-GRB-007")]
    public void WP09_GRB_001_GrabLifecycleUsesHalfOpenHoldAndLockoutIntervals()
    {
        var state = new BattleState(Fighter(FighterId.FighterA, 2_000), Fighter(FighterId.FighterB, 3_000), 0);
        var grab = new ExternalId("grab:dec-fighter_a-000001:00");
        state.StartGrab(grab, FighterId.FighterA, FighterId.FighterB, 5, 12);
        Assert.Equal(17, state.ActiveGrab!.Value.EndExclusiveTick);
        Assert.Equal(grab, state.ActiveGrabId);
        _ = state.EndGrab(17, 20);
        Assert.False(state.CanBeGrabbed(FighterId.FighterB, 36));
        Assert.True(state.CanBeGrabbed(FighterId.FighterB, 37));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-OUT-001")]
    [Trait("AcceptanceId", "WP09-OUT-002")]
    [Trait("AcceptanceId", "WP09-OUT-003")]
    [Trait("AcceptanceId", "WP09-OUT-004")]
    [Trait("AcceptanceId", "WP09-OUT-005")]
    public void WP09_OUT_001_TradeCommitsBothLethalDamageEventsBeforeDefeatChains()
    {
        var a = Profile("trade_a", Hit(), baseDamage: 200, clashPriority: 10);
        var b = Profile("trade_b", Hit(), baseDamage: 200, clashPriority: 10);
        var context = Context(new[] { a, b });
        context.State.FighterA.SetHealthForTesting(100);
        context.State.FighterB.SetHealthForTesting(100);
        Commit(context.State.FighterA, context.State.FighterB, a);
        Commit(context.State.FighterB, context.State.FighterA, b);
        Resolve(context);

        var types = context.Journal.Drafts.Select(item => item.EventType).ToArray();
        var damageIndices = types.Select((type, index) => (type, index))
            .Where(item => item.type == CombatEventType.DamageApplied).Select(item => item.index).ToArray();
        var firstDefeat = Array.FindIndex(types, type => type == CombatEventType.FighterDefeated);
        Assert.Equal(2, damageIndices.Length);
        Assert.True(damageIndices.Max() < firstDefeat);
        Assert.Equal(FighterState.Defeated, context.State.FighterA.State);
        Assert.Equal(FighterState.Defeated, context.State.FighterB.State);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-SAFE-001")]
    [Trait("AcceptanceId", "WP09-SAFE-002")]
    [Trait("AcceptanceId", "WP09-SAFE-003")]
    [Trait("AcceptanceId", "WP09-SAFE-004")]
    [Trait("AcceptanceId", "WP09-SAFE-005")]
    [Trait("AcceptanceId", "WP09-SAFE-006")]
    [Trait("AcceptanceId", "WP09-SAFE-007")]
    public void WP09_SAFE_001_GroupPreflightRejectsBeforeStateRngOrJournalMutation()
    {
        var action = Profile("large_group", Hit(), baseStagger: 120, baseStun: 6);
        var context = Context(new[] { action }, maximumEvents: 4);
        Commit(context.State.FighterA, context.State.FighterB, action);
        var before = context.State.FighterB.ToFrame();
        var groups = ImpactIntentOrderer.BuildGroups(ImpactIntentCollector.Collect(context.State), 0);

        var failure = Assert.Throws<EngineInvariantException>(() =>
            ResolutionSystem.ResolveTick(context.State, context.Settings, context.Emitter, groups));

        Assert.Equal(EngineFailureCodes.EventCapExceeded, failure.Code);
        var after = context.State.FighterB.ToFrame();
        Assert.Equal(before.Health, after.Health);
        Assert.Equal(before.Stagger, after.Stagger);
        Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.ActionId, after.ActionId);
        Assert.Equal(0UL, context.State.Rng.Resolution.NextDrawIndex);
        Assert.Empty(context.Journal.Drafts);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-DET-002")]
    [Trait("AcceptanceId", "WP09-DET-003")]
    [Trait("AcceptanceId", "WP09-DET-008")]
    public void WP09_DET_002_CultureAndInsertionOrderDoNotChangeIdsOrderingOrArithmetic()
    {
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            var values = new List<(string Id, int Damage)>();
            foreach (var culture in new[] { "en-US", "ru-RU", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var action = Profile("culture_strike", Hit(), baseDamage: 100, powerRatio: 500);
                values.Add((
                    ResolutionIdentifiers.ResolutionGroup(12, 7).Value,
                    ResolutionMath.ComputeDamage(Globals(), action, 100, 100).Final));
            }

            Assert.All(values, value => Assert.Equal(values[0], value));
        }
        finally
        {
            CultureInfo.CurrentCulture = priorCulture;
        }
    }

    private static void Resolve(ResolutionContext context)
    {
        var intents = ImpactIntentCollector.Collect(context.State);
        var groups = ImpactIntentOrderer.BuildGroups(intents, context.State.Tick);
        _ = ResolutionSystem.ResolveTick(context.State, context.Settings, context.Emitter, groups);
    }

    private static ResolutionContext Context(
        IReadOnlyList<ResolutionActionProfile> actions,
        ulong seed = 0,
        int fighterBPosition = 3_000,
        int maximumEvents = 200_000)
    {
        var state = new BattleState(
            Fighter(FighterId.FighterA, 2_000),
            Fighter(FighterId.FighterB, fighterBPosition),
            seed);
        var baseline = EngineTestFixture.CreateSetup(10).Settings;
        var settings = baseline with
        {
            Arena = new ArenaSnapshot(new StableId("resolution_arena"), 0, 10_000, 2_000, fighterBPosition),
            MaximumEvents = maximumEvents,
            Resolution = new ResolutionRuntimeSettings(Globals(), actions),
            InitiativeOrder = new[] { FighterId.FighterA, FighterId.FighterB },
        };
        var journal = new RecordingJournal();
        var emitter = new CombatEventEmitter(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: 10, maximumEvents: maximumEvents),
            journal,
            maximumEvents);
        return new ResolutionContext(state, settings, journal, emitter);
    }

    private static FighterRuntimeState Fighter(FighterId id, int position) => new(
        id,
        id == FighterId.FighterA ? FighterSide.A : FighterSide.B,
        new StableId(id == FighterId.FighterA ? "fixture_a" : "fixture_b"),
        position,
        id == FighterId.FighterA ? Facing.Right : Facing.Left,
        maximumHealth: 1_000,
        maximumEnergy: 1_000,
        resourceId: new StableId("fixture_resource"),
        resource: 0,
        maximumResource: 1_000,
        staggerThreshold: 120,
        initiative: id == FighterId.FighterA ? 100 : 90,
        actionSpeed: 100,
        moveSpeed: 100,
        collisionRadius: 100,
        power: 100,
        armor: 100,
        precision: 120,
        evasion: 120,
        guard: 100,
        guardBreak: 80,
        controlPower: 100,
        controlResistance: 100,
        mass: id == FighterId.FighterA ? 80 : 120);

    private static void Commit(
        FighterRuntimeState actor,
        FighterRuntimeState target,
        ResolutionActionProfile profile)
    {
        var decision = actor.PeekNextDecisionId();
        actor.CommitDecisionId(decision);
        actor.CommitCombatAction(Descriptor(actor.FighterId, target.FighterId, profile, decisionId: decision));
    }

    private static CombatActionDescriptor Descriptor(
        FighterId actor,
        FighterId target,
        ResolutionActionProfile profile,
        int commitTick = 0,
        int startupTicks = 0,
        DecisionId? decisionId = null) => new(
        profile.Id,
        profile.Category,
        decisionId ?? new DecisionId(actor == FighterId.FighterA ? "dec-fighter_a-000001" : "dec-fighter_b-000001"),
        target,
        target == FighterId.FighterA ? 2_000 : 3_000,
        actor == FighterId.FighterA ? CommitDirection.Right : CommitDirection.Left,
        0,
        0,
        startupTicks,
        profile.ActiveTicks,
        0,
        0,
        profile,
        profile.TrackTarget,
        movesTowardTarget: true,
        commitTick);

    private static HitScheduleEntry[] Hit() =>
        new[] { new HitScheduleEntry(HitPrimitiveKind.Hit, 0, 0) };

    private static ResolutionActionProfile Profile(
        string id,
        IReadOnlyList<HitScheduleEntry> schedule,
        string[]? tags = null,
        int activeTicks = 1,
        int? hitCount = null,
        int actionPriority = 10,
        int resolutionPriority = 10,
        int clashPriority = 10,
        int hitRangeMin = 0,
        int hitRangeMax = 10_000,
        int baseDamage = 100,
        int powerRatio = 0,
        int minimumDamage = 0,
        int chipMin = 10,
        int blockBase = 0,
        int blockReduction = 0,
        int dodgeBase = 0,
        int baseStagger = 0,
        int baseStun = 3,
        ResolutionMovementMode movement = ResolutionMovementMode.None,
        int baseKnockback = 0,
        int knockbackMax = 0,
        bool wallImpact = false,
        int wallDamagePerUnit = 0,
        int wallDamageMin = 0,
        int wallDamageMax = 0) => new(
        new StableId(id),
        "Basic",
        "Fixture",
        movement,
        (tags ?? (schedule.Count == 0 ? Array.Empty<string>() : new[] { "strike" }))
            .Select(value => new StableId(value)),
        schedule,
        activeTicks,
        hitCount ?? schedule.Count(entry => entry.Kind != HitPrimitiveKind.Grab),
        actionPriority,
        resolutionPriority,
        clashPriority,
        grabPriority: 10,
        hitRangeMin,
        hitRangeMax,
        baseDamage,
        powerRatio,
        minimumDamage,
        blockable: true,
        dodgeable: true,
        undodgeable: false,
        blockBase,
        blockReduction,
        dodgeBase,
        chipMin,
        baseStagger,
        baseStun,
        baseKnockback,
        knockbackMinimum: 0,
        knockbackMaximum: knockbackMax,
        moveDistance: 0,
        trackTarget: false,
        wallImpact,
        wallDamagePerUnit,
        wallDamageMin,
        wallDamageMax,
        "Cancelable");

    private static ResolutionGlobalSettings Globals() => new(
        FixedPointScale: 1_000,
        ArmorK: 200,
        DamageFloor: 1,
        DamageCap: 600,
        BlockSlope: 3,
        BlockMinimum: 100,
        BlockMaximum: 900,
        DodgeSlope: 3,
        DodgeMinimum: 50,
        DodgeMaximum: 850,
        ControlK: 150,
        ForceK: 120,
        StunMinimumTicks: 3,
        StunMaximumTicks: 20,
        MaximumHoldTicks: 12,
        GrabLockoutTicks: 20);

    private static ulong SeedForResult(int expected, int maximumExclusive)
    {
        for (ulong seed = 0; seed < 100_000; seed++)
        {
            if (Pcg32Stream.CreateResolution(seed).NextInt(0, maximumExclusive, RngOperation.ChanceCheck).Result == expected)
            {
                return seed;
            }
        }

        throw new InvalidOperationException("Could not find deterministic fixture seed.");
    }

    private sealed record ResolutionContext(
        BattleState State,
        RuntimeBattleSettings Settings,
        RecordingJournal Journal,
        CombatEventEmitter Emitter);
}
