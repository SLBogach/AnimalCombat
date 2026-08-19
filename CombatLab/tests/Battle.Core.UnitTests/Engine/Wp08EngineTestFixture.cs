using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Outcome;

namespace Battle.Core.UnitTests.Engine;

internal sealed record Wp08ActionSpec(
    string ActionId,
    int BaseWeight = 500,
    int EnergyCost = 0,
    int ResourceCost = 0,
    int CooldownTicks = 0,
    int MaximumConsecutiveUses = 100,
    int StartupBaseTicks = 1,
    int StartupMinimumTicks = 1,
    int StartupMaximumTicks = 1,
    int ActiveTicks = 1,
    int RecoveryBaseTicks = 1,
    int RecoveryMinimumTicks = 1,
    int RecoveryMaximumTicks = 1,
    int HitCount = 1,
    string HitSchedule = "0",
    string MovementMode = "None",
    int PreferredRangeMinimum = 0,
    int PreferredRangeMaximum = 10_000,
    int HitRangeMinimum = 0,
    int HitRangeMaximum = 10_000,
    bool TrackTarget = false);

internal static class Wp08EngineTestFixture
{
    internal static StableId BearPrimaryId { get; } = new("bear_earthbreaker");

    internal static StableId BearSecondaryId { get; } = new("bear_rampage_charge");

    internal static StableId KangarooPrimaryId { get; } = new("kangaroo_flying_kick");

    internal static StableId KangarooSecondaryId { get; } = new("kangaroo_tail_counter");

    internal static CompiledBattleConfig CreateConfig(
        IEnumerable<Wp08ActionSpec>? actionSpecs = null,
        int timeLimit = 10,
        int maximumEvents = 200_000,
        int maximumZeroProgressTicks = 100,
        int? bearActionSpeed = null,
        int? kangarooActionSpeed = null,
        Func<IEnumerable<ConfigProperty>, IEnumerable<ConfigProperty>>? changeSettings = null)
    {
        var specs = (actionSpecs ?? Array.Empty<Wp08ActionSpec>())
            .ToDictionary(spec => new StableId(spec.ActionId));
        return EngineTestFixture.CreateConfig(
            timeLimit,
            maximumEvents,
            maximumZeroProgressTicks,
            changeSettings,
            fighters => fighters.Select(fighter => fighter.Id.Value switch
            {
                "bear" when bearActionSpeed.HasValue => Patch(
                    fighter,
                    ("action_speed", ConfigValue.FromInteger(bearActionSpeed.Value))),
                "kangaroo" when kangarooActionSpeed.HasValue => Patch(
                    fighter,
                    ("action_speed", ConfigValue.FromInteger(kangarooActionSpeed.Value))),
                _ => fighter,
            }),
            actions => actions.Select(action => specs.TryGetValue(action.Id, out var spec)
                ? Apply(action, spec)
                : action));
    }

    internal static BattleRequest CreateSymmetricBearRequest() =>
        EngineTestFixture.CreateRequest(buildB: new FighterBuildSnapshot(
            FighterId.FighterB,
            FighterSide.B,
            new StableId("bear"),
            null,
            new[] { BearPrimaryId, BearSecondaryId },
            new StableId("bear_thick_hide"),
            new GearSelection(
                new StableId("gear_offense_power_wraps"),
                new StableId("gear_defense_reinforced_hide"),
                new StableId("gear_utility_sprint_soles")),
            new StableId("tactic_pressure")));

    internal static Wp08TickHarness CreateHarness(
        CompiledBattleConfig config,
        BattleRequest? request = null,
        int? emitterMaximumEvents = null)
    {
        request ??= EngineTestFixture.CreateRequest();
        var result = BattleSetupFactory.Create(request, config);
        Assert.True(
            result.IsSuccess,
            string.Join(",", result.Errors.Select(error =>
                error.Code.Value + "@" + error.Path + "=" + error.EntityId)));
        return new Wp08TickHarness(
            request,
            config,
            result.Setup!,
            emitterMaximumEvents ?? result.Setup!.Settings.MaximumEvents);
    }

    internal static void ForceCombat(
        Wp08TickHarness harness,
        FighterId fighterId,
        StableId actionId,
        int resource = 500)
    {
        var fighter = harness.State.Get(fighterId);
        fighter.SetResourceForTesting(resource);
        fighter.SetOpportunityDebtForTesting(actionId, 4);
    }

    internal static void MakeBusy(
        Wp08TickHarness harness,
        FighterId fighterId,
        int ticks = 100) =>
        harness.State.Get(fighterId).CommitSystemWait(new StableId("fixture_busy"), ticks);

    internal static CombatActionDescriptor Descriptor(
        FighterRuntimeState actor,
        FighterRuntimeState? target,
        DecisionId decisionId,
        string actionId = "fixture_combat",
        int startupTicks = 1,
        int activeTicks = 1,
        int recoveryTicks = 1,
        int cooldownTicks = 0,
        IEnumerable<int>? relativeImpactTicks = null,
        int commitTick = 0) => new(
        new StableId(actionId),
        "FixtureCombat",
        decisionId,
        target?.FighterId,
        target?.Position,
        target is null
            ? CommitDirection.None
            : target.Position > actor.Position ? CommitDirection.Right : CommitDirection.Left,
        energyCost: 0,
        resourceCost: 0,
        startupTicks,
        activeTicks,
        recoveryTicks,
        cooldownTicks,
        relativeImpactTicks ?? Array.Empty<int>(),
        trackTarget: false,
        commitTick);

    internal static CombatEventIdentity SeedCommittedCombat(
        Wp08TickHarness harness,
        FighterId actorId,
        int startupTicks,
        int activeTicks,
        int recoveryTicks,
        string actionId = "fixture_combat")
    {
        var actor = harness.State.Get(actorId);
        var target = harness.State.GetOpponent(actorId);
        var before = new FramePair(actor.ToFrame(), target.ToFrame());
        var decisionId = actor.PeekNextDecisionId();
        actor.CommitDecisionId(decisionId);
        var descriptor = Descriptor(
            actor,
            target,
            decisionId,
            actionId,
            startupTicks,
            activeTicks,
            recoveryTicks);
        actor.CommitCombatAction(descriptor);
        var payload = new ActionCommittedPayload(
            new[] { harness.StartedEvent.EventId },
            target.FighterId,
            0,
            0,
            startupTicks,
            activeTicks,
            recoveryTicks,
            0,
            descriptor.CommitDirection,
            target.Position);
        var committed = harness.Emitter.Emit(
            harness.State.Tick,
            payload,
            actorId,
            target.FighterId,
            descriptor.ActionId,
            decisionId: decisionId,
            sourceEventId: harness.StartedEvent.EventId,
            reasonCodes: new[] { new ReasonCode("ActionSelected") },
            before: before,
            after: new FramePair(actor.ToFrame(), target.ToFrame()));
        actor.RecordCombatCommit(committed.EventId);
        return committed;
    }

    internal static CompiledConfigEntity Patch(
        CompiledConfigEntity entity,
        params (string Name, ConfigValue Value)[] values)
    {
        foreach (var (name, value) in values)
        {
            entity = EngineTestFixture.WithProperty(entity, name, value);
        }

        return entity;
    }

    private static CompiledConfigEntity Apply(
        CompiledConfigEntity entity,
        Wp08ActionSpec spec) => Patch(
        entity,
        ("base_weight", ConfigValue.FromInteger(spec.BaseWeight)),
        ("energy_cost", ConfigValue.FromInteger(spec.EnergyCost)),
        ("resource_cost", ConfigValue.FromInteger(spec.ResourceCost)),
        ("cooldown_ticks", ConfigValue.FromInteger(spec.CooldownTicks)),
        ("max_consecutive_uses", ConfigValue.FromInteger(spec.MaximumConsecutiveUses)),
        ("startup_base_ticks", ConfigValue.FromInteger(spec.StartupBaseTicks)),
        ("startup_min_ticks", ConfigValue.FromInteger(spec.StartupMinimumTicks)),
        ("startup_max_ticks", ConfigValue.FromInteger(spec.StartupMaximumTicks)),
        ("active_ticks", ConfigValue.FromInteger(spec.ActiveTicks)),
        ("recovery_base_ticks", ConfigValue.FromInteger(spec.RecoveryBaseTicks)),
        ("recovery_min_ticks", ConfigValue.FromInteger(spec.RecoveryMinimumTicks)),
        ("recovery_max_ticks", ConfigValue.FromInteger(spec.RecoveryMaximumTicks)),
        ("hit_count", ConfigValue.FromInteger(spec.HitCount)),
        ("hit_schedule", ConfigValue.FromString(spec.HitSchedule)),
        ("movement_mode", ConfigValue.FromString(spec.MovementMode)),
        ("preferred_range_min", ConfigValue.FromInteger(spec.PreferredRangeMinimum)),
        ("preferred_range_max", ConfigValue.FromInteger(spec.PreferredRangeMaximum)),
        ("hit_range_min", ConfigValue.FromInteger(spec.HitRangeMinimum)),
        ("hit_range_max", ConfigValue.FromInteger(spec.HitRangeMaximum)),
        ("track_target", ConfigValue.FromBoolean(spec.TrackTarget)));
}

internal sealed class Wp08TickHarness
{
    private bool _started;

    internal Wp08TickHarness(
        BattleRequest request,
        CompiledBattleConfig config,
        BattleSetup setup,
        int maximumEvents)
    {
        Request = request;
        Config = config;
        Setup = setup;
        Journal = new RecordingJournal();
        Emitter = new CombatEventEmitter(request, config, Journal, maximumEvents);
        Coordinator = new TickCoordinator(setup.Settings.MaximumZeroProgressTicks);
    }

    internal BattleRequest Request { get; }

    internal CompiledBattleConfig Config { get; }

    internal BattleSetup Setup { get; }

    internal BattleState State => Setup.State;

    internal RecordingJournal Journal { get; }

    internal CombatEventEmitter Emitter { get; }

    internal TickCoordinator Coordinator { get; }

    internal CombatEventIdentity StartedEvent { get; private set; }

    internal void Start()
    {
        Assert.False(_started);
        _started = true;
        StartedEvent = Emitter.Emit(
            State.Tick,
            new BattleStartedPayload(
                Array.Empty<EventId>(),
                EngineTestFixture.InputDigest,
                State.FinalFrames(),
                Setup.InitiativeOrder,
                InitiativeTieBreak.StatThenSeededHash),
            reasonCodes: new[] { new ReasonCode("Initialization") });
    }

    internal ImmediateOutcome? RunTick()
    {
        Assert.True(_started);
        return Coordinator.RunActiveTick(State, Setup.Settings, Emitter);
    }

    internal CombatEventIdentity EndInvalid(ReasonCode reason)
    {
        State.RecordOutcome(BattleOutcome.Invalid, null, BattleEndReason.BattleInvalid);
        var summary = new BattleSummary(
            BattleOutcome.Invalid,
            null,
            BattleEndReason.BattleInvalid,
            State.Tick,
            State.Tick,
            checked(Emitter.EventCount + 1),
            Array.Empty<EventId>(),
            State.FinalFrames());
        var source = Emitter.LastEventId;
        var ended = Emitter.Emit(
            State.Tick,
            new BattleEndedPayload(
                source.HasValue ? new[] { source.Value } : Array.Empty<EventId>(),
                summary),
            sourceEventId: source,
            reasonCodes: new[] { reason });
        State.MarkTerminal();
        return ended;
    }
}
