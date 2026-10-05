using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Replay.Journal;
using CombatLab.IntegrationTests.Resolution;

namespace CombatLab.IntegrationTests.Effects;

// Synthetic JSON lives only in memory and passes the production canonical compiler and strict
// versioned setup. No injected runtime definitions, state mutation or fixture overwrites.
internal static class EffectEngineFixture
{
    internal static JsonObject Source(int time = 6)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Wp09ResolutionEngineFixture.Root(),
            "config/generated/combat.balance.v0.2.json")))!.AsObject();
        var settings = root["settings"]!.AsObject();
        settings["battle.time_limit_ticks"] = time;
        settings["global.arena.start_position_a"] = 4000;
        settings["global.arena.start_position_b"] = 5750;
        foreach (var fighter in root["fighters"]!.AsArray().Select(x => x!.AsObject()))
        {
            fighter["max_health"] = 1000; fighter["collision_radius"] = 100;
            fighter["action_speed"] = 100; fighter["move_speed"] = 100;
            fighter["power"] = 100; fighter["armor"] = 100;
            fighter["control_power"] = 100; fighter["control_resistance"] = 100;
            fighter["stagger_threshold"] = 100;
        }
        foreach (var gear in root["gear"]!.AsArray().Select(x => x!.AsObject()))
        {
            gear["operation1"] = "Add"; gear["value1"] = 0;
            gear["operation2"] = "Add"; gear["value2"] = 0;
        }
        foreach (var action in root["actions"]!.AsArray().Select(x => x!.AsObject()))
            if (action["slot_type"]!.GetValue<string>() != "System")
            { action["energy_cost"] = 2000; action["resource_cost"] = 0; }
        foreach (var tactic in root["tactics"]!.AsArray().Select(x => x!.AsObject()))
            foreach (var key in tactic.Select(x => x.Key).Where(x => x.EndsWith("_fp", StringComparison.Ordinal)).ToArray())
                tactic[key] = 1000;
        return root;
    }

    internal static JsonObject Entity(JsonObject root, string catalog, string key, string id) =>
        root[catalog]!.AsArray().Select(x => x!.AsObject()).Single(x => x[key]!.GetValue<string>() == id);

    internal static JsonObject Attack(JsonObject root, string id = "bear_earthbreaker", int damage = 30,
        int startup = 0, int active = 1, int recovery = 0, string schedule = "0", string tags = "strike")
    {
        var action = Entity(root, "actions", "action_id", id);
        action["energy_cost"] = 0; action["resource_cost"] = 0; action["base_weight"] = 100_000_000;
        action["startup_base_ticks"] = startup; action["startup_min_ticks"] = 0; action["startup_max_ticks"] = 20;
        action["active_ticks"] = active;
        action["recovery_base_ticks"] = recovery; action["recovery_min_ticks"] = 0; action["recovery_max_ticks"] = 20;
        action["cooldown_ticks"] = 0; action["movement_mode"] = "None"; action["move_distance"] = 0;
        action["base_damage"] = damage; action["power_ratio_fp"] = 0; action["min_damage"] = 0;
        action["hit_count"] = schedule.Length == 0 ? 0 : schedule.Split('|').Count(x => !x.StartsWith("grab:", StringComparison.Ordinal));
        action["hit_schedule"] = schedule;
        action["preferred_range_min"] = 0; action["preferred_range_max"] = 10000;
        action["hit_range_min"] = 0; action["hit_range_max"] = 10000;
        action["base_stagger"] = 0; action["base_stun_ticks"] = 0;
        action["base_knockback"] = 0; action["knockback_min"] = 0; action["knockback_max"] = 0;
        action["wall_impact"] = false; action["wall_damage_per_unit_fp"] = 0;
        action["wall_damage_min"] = 0; action["wall_damage_max"] = 0;
        action["blockable"] = false; action["dodgeable"] = false; action["undodgeable"] = true;
        action["clash_priority"] = 10; action["tags"] = tags;
        return action;
    }

    internal static JsonObject Effect(JsonObject root, string id, string target = "Armor", int value = 100,
        int duration = 2, string boundary = "ExpireBeforeTick", string policy = "Refresh", string? group = null)
    {
        var effect = (JsonObject)Entity(root, "effects", "effect_id", "effect_exposed").DeepClone();
        effect["effect_id"] = id; effect["stack_group"] = group ?? id; effect["semantic_role"] = "None";
        effect["duration_ticks"] = duration; effect["expiry_boundary"] = boundary;
        effect["stack_policy"] = policy; effect["stack_cap"] = 1;
        effect["modifier_stat1"] = target; effect["operation1"] = "Add"; effect["value1"] = value;
        effect["modifier_stat2"] = ""; effect["operation2"] = "Override"; effect["value2"] = 0;
        root["effects"]!.AsArray().Add(effect);
        return effect;
    }

    internal static JsonObject Rule(JsonObject root, string id, string effect, string trigger = "BattleStart",
        string ownerKind = "Global", string owner = "global", string recipient = "Self", int battleCap = 1)
    {
        var rule = new JsonObject
        {
            ["rule_id"] = id, ["owner_kind"] = ownerKind, ["owner_id"] = owner,
            ["trigger"] = trigger, ["recipient"] = recipient, ["condition"] = "LivingTarget",
            ["primitive"] = "ApplyEffect", ["effect_id"] = effect, ["priority"] = 0,
            ["internal_cooldown_ticks"] = 0, ["max_activations_per_tick"] = 1,
            ["max_activations_per_battle"] = battleCap, ["once_per_event"] = true,
        };
        root["effect_rules"]!.AsArray().Add(rule);
        return rule;
    }

    internal static EffectRun Run(JsonObject source, ulong seed = 0)
    {
        var compilation = new BattleConfigCompiler().Compile(Encoding.UTF8.GetBytes(source.ToJsonString()));
        Assert.True(compilation.IsSuccess, string.Join("; ", compilation.Issues.Select(x => x.Code + ":" + x.Path + ":" + x.Message)));
        var config = compilation.Config!;
        var a = Build(FighterId.FighterA, FighterSide.A, "bear", "bear_earthbreaker", "bear_rampage_charge", "bear_thick_hide", "tactic_pressure");
        var b = Build(FighterId.FighterB, FighterSide.B, "kangaroo", "kangaroo_flying_kick", "kangaroo_tail_counter", "kangaroo_never_still", "tactic_position");
        var mode = new ModeRulesSnapshot(new StableId("wp10_effects_integration"), ContractVersions.ModeRules, NormalizationMode.None,
            [a.AnimalId, b.AnimalId], a.SpecialActionIds.Concat(b.SpecialActionIds).Concat(
                [new StableId("sys_wait"), new StableId("sys_approach"), new StableId("sys_retreat")]),
            [a.PassiveId, b.PassiveId], [a.Gear.Offense, a.Gear.Defense, a.Gear.Utility], [a.TacticId, b.TacticId]);
        var request = new BattleRequest(new ExternalId("wp10-integration"), BattleSetupFactory.EffectsEngineVersion,
            config.Reference.ConfigHash, mode, seed, a, b);
        var observer = new EffectObserver(); var journal = new CountedEffectJournal();
        var result = new CombatEngine(BattleSetupFactory.EffectsEngineVersion, observer).Simulate(request, config, journal);
        return new EffectRun(result, journal, observer, config);
    }

    private static FighterBuildSnapshot Build(FighterId id, FighterSide side, string animal, string special1,
        string special2, string passive, string tactic) => new(id, side, new StableId(animal), null,
            [new StableId(special1), new StableId(special2)], new StableId(passive),
            new GearSelection(new StableId("gear_offense_power_wraps"), new StableId("gear_defense_reinforced_hide"),
                new StableId("gear_utility_sprint_soles")), new StableId(tactic));
}

internal sealed record EffectRun(BattleResult Result, CountedEffectJournal Journal, EffectObserver Observer, CompiledBattleConfig Config)
{
    internal CombatEventDraft[] Events => Journal.Canonical.Events.Select(x => x.Draft).ToArray();
    internal void Completed()
    {
        Assert.True(Result.Status == BattleResultStatus.Completed, string.Join("; ", Result.RejectionErrors.Select(x => x.Code + ":" + x.Path)) + Result.InvariantFailure?.Message);
        Assert.Equal(1, Journal.Begins); Assert.Equal(1, Journal.Completes); Assert.True(Journal.Canonical.IsCompleted);
        Assert.Equal(CombatEventType.BattleStarted, Events[0].EventType); Assert.Equal(0, Events[0].Sequence);
        Assert.Equal(CombatEventType.BattleEnded, Events[^1].EventType);
        Assert.Equal(Enumerable.Range(0, Events.Length), Events.Select(x => (int)x.Sequence));
        Assert.All(Result.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
    }
}

internal sealed record EffectSample(int Tick, TickPhase Phase, FighterFrame A, FighterFrame B,
    int ArmorA, int ArmorB, int PowerA, int PowerB, int SpeedA, int SpeedB, int? FrozenMoveA, int? FrozenMoveB,
    int GuardB, int BlockWeightB);

internal sealed class EffectObserver : ITickCoordinatorObserver
{
    internal List<EffectSample> Samples { get; } = new();
    internal BattleState? FinalState { get; private set; }
    public void OnPhase(BattleState state, TickPhase phase)
    {
        FinalState = state;
        Samples.Add(new EffectSample(state.Tick, phase, state.FighterA.ToFrame(), state.FighterB.ToFrame(),
            state.FighterA.Armor, state.FighterB.Armor, state.FighterA.Power, state.FighterB.Power,
            state.FighterA.ActionSpeed, state.FighterB.ActionSpeed, state.FighterA.FrozenMoveSpeed, state.FighterB.FrozenMoveSpeed,
            state.FighterB.Guard, state.Effects!.Channel(FighterId.FighterB, EffectModifierTarget.BlockWeight)));
    }
    public void OnDecisionSnapshot(FighterId fighterId, TickSnapshot snapshot) { }
}

internal sealed class CountedEffectJournal : ICombatEventJournal
{
    internal CanonicalReplayJournal Canonical { get; } = new(new ExternalId("wp10-integration-replay"));
    internal int Begins { get; private set; }
    internal int Completes { get; private set; }
    public JournalBeginResult Begin(in CombatJournalStart start) { Begins++; return Canonical.Begin(in start); }
    public CombatEventIdentity Append(in CombatEventDraft draft) => Canonical.Append(in draft);
    public JournalCompletion Complete(in BattleSummary summary) { Completes++; return Canonical.Complete(in summary); }
}
