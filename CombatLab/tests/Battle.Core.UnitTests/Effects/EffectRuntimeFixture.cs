using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Resolution;
using Battle.Core.UnitTests.Engine;

namespace Battle.Core.UnitTests.Effects;

// Controlled internal harness, deliberately not a claim that production Engine0.5 is released.
internal sealed class EffectRuntimeFixture
{
    internal EffectRuntimeFixture(IEnumerable<EffectProfile> effects, IEnumerable<EffectRuleProfile> rules,
        int maximumEvents = 200000, int depth = 8, int triggers = 128, int instances = 32, ulong seed = 7,
        IEnumerable<ActionInterruptProfile>? interrupts = null)
    {
        var setup = EngineTestFixture.CreateSetup(100);
        State = new BattleState(setup.State.FighterA.Clone(), setup.State.FighterB.Clone(), seed);
        var stats = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Power"] = 100, ["Armor"] = 100, ["Precision"] = 120, ["Evasion"] = 120,
            ["Guard"] = 100, ["GuardBreak"] = 80, ["MoveSpeed"] = 100, ["ActionSpeed"] = 100,
            ["Initiative"] = 100, ["ControlPower"] = 100, ["ControlResistance"] = 100,
            ["Mass"] = 100, ["EnergyRegen"] = 10,
        };
        var bounds = stats.ToDictionary(x => x.Key, x => new StatBounds(
            x.Key is "MoveSpeed" or "ActionSpeed" or "Mass" ? 1 : 0, int.MaxValue), StringComparer.Ordinal);
        var fighters = new[] { FighterId.FighterA, FighterId.FighterB }.Select(fighter =>
            new EffectFighterDefinition(fighter, stats, Array.Empty<InitialStatSource>(), stats,
                new[] { new StableId("action_a"), new StableId("action_b"), new StableId("block"), new StableId("dodge") }));
        Definition = new EffectRuntimeDefinition(1000, 100, depth, triggers, instances,
            3, 2, 6, 3, 3, 20, 150, bounds, effects, rules, interrupts ?? Array.Empty<ActionInterruptProfile>(), fighters);
        State.InitializeEffects(Definition);
        Settings = setup.Settings with { MaximumEvents = maximumEvents };
        Journal = new RecordingJournal();
        var request = EngineTestFixture.CreateRequest();
        var start = new CombatJournalStart(request.BattleId, request.EngineVersion,
            Battle.Contracts.Versions.ContractVersions.Rng, Battle.Contracts.Versions.ContractVersions.Ordering,
            EngineTestFixture.CreateConfig().Reference, new BattleInputSnapshot(request.MasterSeed, request.ModeRules.Id, Settings.Arena),
            new CombatJournalFighterStart(request.BuildA, State.FighterA.ToFrame()),
            new CombatJournalFighterStart(request.BuildB, State.FighterB.ToFrame()));
        _ = Journal.Begin(in start);
        Emitter = new CombatEventEmitter(request, EngineTestFixture.CreateConfig(), Journal, maximumEvents);
    }
    internal BattleState State { get; }
    internal RuntimeBattleSettings Settings { get; }
    internal EffectRuntimeDefinition Definition { get; }
    internal RecordingJournal Journal { get; }
    internal CombatEventEmitter Emitter { get; }
    internal EffectRuntime Runtime => State.Effects!;

    internal void At(int tick)
    {
        while (State.Tick < tick) State.AdvanceTick();
        Runtime.SynchronizeFrames(State);
    }
    internal void Start()
    {
        _ = Emitter.Emit(State.Tick, new BattleStartedPayload(Array.Empty<EventId>(), EngineTestFixture.InputDigest,
            State.FinalFrames(), new[] { FighterId.FighterA, FighterId.FighterB }, InitiativeTieBreak.StatThenSeededHash));
        var started = Emitter.LastDraft!;
        AtomicBattleBatch.Execute(State, Emitter, TickPhase.Snapshot, (state, emitter, _) =>
            state.Effects!.CloseEvents(state, emitter, new[] { started }));
    }
    internal void EndTick() => AtomicBattleBatch.Execute(State, Emitter, TickPhase.EndTick, (state, emitter, _) =>
    {
        state.Effects!.EndOfTick(state, emitter);
        state.Effects.Expire(state, emitter, EffectExpiryBoundary.ExpireAfterTick);
    });
    internal void Expire(EffectExpiryBoundary boundary) => AtomicBattleBatch.Execute(State, Emitter, TickPhase.Expiry,
        (state, emitter, _) => state.Effects!.Expire(state, emitter, boundary));
    internal void Pulse(int damage = 0, string? action = null, bool draw = false, Action<BattleState>? mutate = null) => AtomicBattleBatch.Execute(State, Emitter,
        TickPhase.Resolve, (state, emitter, drafts) =>
        {
            mutate?.Invoke(state);
            if (draw) _ = state.Rng.Resolution.NextInt(0, 1000, RngOperation.ChanceCheck);
            var before = new FramePair(state.FighterA.ToFrame(), state.FighterB.ToFrame());
            var health = state.FighterB.Health;
            _ = state.FighterB.ApplyDamage(damage);
            _ = emitter.Emit(state.Tick, new DamageAppliedPayload(Array.Empty<EventId>(), new ExternalId("impact:test"),
                new ExternalId("damage:test"), new DamageBreakdown(damage, damage, damage, damage, damage, 0, 600, 0),
                health, state.FighterB.Health, Array.Empty<StableId>(), state.FighterB.Health == 0),
                FighterId.FighterA, FighterId.FighterB, action is null ? null : new StableId(action),
                resolutionGroupId: new ExternalId("resolution:test"), before: before,
                after: new FramePair(state.FighterA.ToFrame(), state.FighterB.ToFrame()));
            state.Effects!.CloseEvents(state, emitter, drafts.ToArray());
        });
    internal void Cleanup() => AtomicBattleBatch.Execute(State, Emitter, TickPhase.Outcome,
        (state, emitter, _) => state.Effects!.Cleanup(state, emitter, Emitter.LastEventId));
    internal void Finish()
    {
        Cleanup();
        var summary = new BattleSummary(BattleOutcome.Draw, null, BattleEndReason.TimeoutEqualHealthFraction, State.Tick, State.Tick,
            Emitter.EventCount + 1, Array.Empty<EventId>(), State.FinalFrames());
        _ = Emitter.Emit(State.Tick, new BattleEndedPayload(Array.Empty<EventId>(), summary));
        State.MarkTerminal();
        _ = Journal.Complete(in summary);
    }
    internal static EffectProfile Effect(string id = "effect_a", string? group = null, int duration = 3,
        EffectStackPolicy policy = EffectStackPolicy.AddStacks, int cap = 3,
        EffectExpiryBoundary boundary = EffectExpiryBoundary.ExpireBeforeTick, int value = 8,
        EffectModifierTarget target = EffectModifierTarget.Precision, EffectModifierOperation operation = EffectModifierOperation.Add,
        EffectCompareKey? compare = null, int priority = 0, int cooldown = 0, int tickCap = 1000, int battleCap = 1000,
        EffectRefreshRule refresh = EffectRefreshRule.ResetDuration) => new(new StableId(id), new StableId(group ?? id),
            duration, boundary, policy, cap, compare, priority, refresh, EffectSemanticRole.None, cooldown, tickCap, battleCap,
            new[] { new EffectModifier(target, operation, value, 0) }, Array.Empty<int>());
    internal static EffectRuleProfile Rule(string id = "rule_a", string effect = "effect_a", EffectTrigger trigger = EffectTrigger.BattleStart,
        EffectOwnerKind ownerKind = EffectOwnerKind.Global, string owner = "global", EffectRecipient recipient = EffectRecipient.Self,
        EffectPrimitive primitive = EffectPrimitive.ApplyEffect, EffectCondition condition = EffectCondition.Always,
        int priority = 0, int cooldown = 0, int tickCap = 1000, int battleCap = 1000) => new(new StableId(id), ownerKind,
            new StableId(owner), trigger, recipient, condition, primitive, new StableId(effect), priority, cooldown, tickCap, battleCap, true);
}
