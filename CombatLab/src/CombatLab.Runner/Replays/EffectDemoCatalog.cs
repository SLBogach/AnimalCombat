using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Replay.Journal;

namespace CombatLab.Runner.Replays;

/// <summary>Explicit synthetic demos, not balance defaults or automatic passive bindings.</summary>
public static class EffectDemoCatalog
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
    { "wait", "decision", "resolution-basic", "resolution-double-ko", "resolution-wall-grab",
        "effects-stack-expiry", "effects-impact-snapshot", "effects-control-chain", "effects-knockdown" });

    public static string FixtureName(string name)
    {
        RequireName(name);
        return (name == "wait" ? "wait-equal" : name == "decision" ? "decision-weighted" : name) + "-l1.engine-0.5.0.json";
    }

    public static JsonObject CreateSource(string root, string name)
    {
        RequireName(name);
        var source = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "config/generated/combat.balance.v0.2.json")))!.AsObject();
        var settings = source["settings"]!;
        settings["battle.time_limit_ticks"] = name switch
        { "wait" or "decision" => 1, "effects-control-chain" => 72, "effects-knockdown" => 47, "effects-stack-expiry" => 10, _ => 3 };
        settings["global.arena.start_position_a"] = 4000; settings["global.arena.start_position_b"] = 5750;
        foreach (var fighter in source["fighters"]!.AsArray())
        {
            fighter!["max_health"] = 1000; fighter["collision_radius"] = 100;
            fighter["power"] = 100; fighter["armor"] = 100; fighter["action_speed"] = 100; fighter["move_speed"] = 100;
            fighter["control_power"] = 100; fighter["control_resistance"] = 100; fighter["stagger_threshold"] = 100;
        }
        foreach (var gear in source["gear"]!.AsArray())
        { gear!["operation1"] = "Add"; gear["value1"] = 0; gear["operation2"] = "Add"; gear["value2"] = 0; }
        foreach (var action in source["actions"]!.AsArray())
            if (action!["slot_type"]!.GetValue<string>() != "System") { action["energy_cost"] = 2000; action["resource_cost"] = 0; }
        foreach (var tactic in source["tactics"]!.AsArray())
            foreach (var key in tactic!.AsObject().Select(x => x.Key).Where(x => x.EndsWith("_fp", StringComparison.Ordinal)).ToArray()) tactic[key] = 1000;
        switch (name)
        {
            case "decision":
                Attack(source, "bear_earthbreaker", startup: 2)["base_weight"] = 100;
                Attack(source, "bear_rampage_charge", startup: 2)["base_weight"] = 100; break;
            case "resolution-basic":
            case "resolution-double-ko":
                Entity(source, "fighters", "animal_id", "bear")["max_health"] = 100;
                Entity(source, "fighters", "animal_id", "kangaroo")["max_health"] = 100;
                Attack(source, "bear_earthbreaker", damage: 3000);
                if (name == "resolution-double-ko") Attack(source, "kangaroo_flying_kick", damage: 3000); break;
            case "resolution-wall-grab":
                WallThrow(source, startup: 0, knockdown: false);
                Entity(source, "fighters", "animal_id", "kangaroo")["max_health"] = 100; break;
            case "effects-stack-expiry":
                var stacks = Effect(source, "effect_demo_stacks", "Precision", 8, 3, "ExpireBeforeTick", "AddStacks");
                stacks["stack_cap"] = 3; stacks["modifier_stat2"] = "ActionSpeed"; stacks["operation2"] = "Add"; stacks["value2"] = 5;
                Rule(source, "rule_demo_stacks_start", "effect_demo_stacks");
                Rule(source, "rule_demo_stacks_tick", "effect_demo_stacks", "EndOfTick", cap: 3);
                Effect(source, "effect_demo_after", "Armor", 100, 3, "ExpireAfterTick"); Rule(source, "rule_demo_after", "effect_demo_after");
                Effect(source, "effect_demo_refresh", "Power", 10, 2); Rule(source, "rule_demo_refresh_start", "effect_demo_refresh");
                Rule(source, "rule_demo_refresh_tick", "effect_demo_refresh", "EndOfTick", cap: 2); break;
            case "effects-impact-snapshot":
                foreach (var id in new[] { "bear_earthbreaker", "kangaroo_flying_kick" })
                { var hit = Attack(source, id, damage: 100, active: 2, schedule: "0|1"); hit["cooldown_ticks"] = 100; hit["action_priority"] = 0; }
                Entity(source, "fighters", "animal_id", "bear")["initiative"] = 200;
                Entity(source, "fighters", "animal_id", "kangaroo")["initiative"] = 100;
                Effect(source, "effect_demo_armor", "Armor", 100, 2); Rule(source, "rule_demo_armor", "effect_demo_armor", "DamageTaken"); break;
            case "effects-control-chain":
                var stun = Attack(source, "bear_earthbreaker"); stun["base_stagger"] = 100; stun["base_stun_ticks"] = 8; stun["cooldown_ticks"] = 12; break;
            case "effects-knockdown":
                WallThrow(source, startup: 9, knockdown: true);
                var suppress = Effect(source, "effect_demo_delay", "PunishWeight", 0, 11); suppress["operation1"] = "Multiply";
                Rule(source, "rule_demo_delay", "effect_demo_delay");
                Attack(source, "bear_rampage_charge", tags: "strike|punish")["cooldown_ticks"] = 100;
                var ground = Attack(source, "bear_paw_jab", tags: "ground_hit|punish|strike"); ground["cooldown_ticks"] = 100;
                ground["base_weight"] = 10_000_000; break;
        }
        return source;
    }

    public static CompiledBattleConfig Compile(JsonObject source)
    {
        var result = new BattleConfigCompiler().Compile(Encoding.UTF8.GetBytes(source.ToJsonString()));
        if (!result.IsSuccess) throw new InvalidDataException(string.Join("; ", result.Issues.Select(x => x.Code + "@" + x.Path + ":" + x.Message)));
        return result.Config!;
    }

    public static BattleRequest Request(string name, CompiledBattleConfig config, ulong seed = 0)
    {
        RequireName(name);
        var a = Build(FighterId.FighterA, FighterSide.A, "bear", "bear_earthbreaker", "bear_rampage_charge", "bear_thick_hide", "tactic_pressure");
        var b = Build(FighterId.FighterB, FighterSide.B, "kangaroo", "kangaroo_flying_kick", "kangaroo_tail_counter", "kangaroo_never_still", "tactic_position");
        var actions = name == "wait" ? a.SpecialActionIds.Concat(b.SpecialActionIds).Concat(new[] { new StableId("sys_wait") }).ToArray() : a.SpecialActionIds.Concat(b.SpecialActionIds).Concat(
            new[] { new StableId("sys_wait"), new StableId("sys_approach"), new StableId("sys_retreat"), new StableId("bear_paw_jab") }).ToArray();
        var mode = new ModeRulesSnapshot(new StableId("wp10_" + name.Replace('-', '_')), ContractVersions.ModeRules, NormalizationMode.None,
            new[] { a.AnimalId, b.AnimalId }, actions, new[] { a.PassiveId, b.PassiveId },
            new[] { a.Gear.Offense, a.Gear.Defense, a.Gear.Utility }, new[] { a.TacticId, b.TacticId });
        return new BattleRequest(new ExternalId("battle-wp10-" + name), new ArtifactVersion("battle.core/0.5.0"), config.Reference.ConfigHash, mode, seed, a, b);
    }

    public static EffectDemoRun Run(string root, string name, JournalProfile profile = JournalProfile.StandardReplay, ulong seed = 0, JsonObject? source = null)
    {
        var config = Compile(source ?? CreateSource(root, name)); var request = Request(name, config, seed);
        var journal = new CanonicalReplayJournal(new ExternalId("replay-wp10-" + name), profile);
        var result = new CombatEngine().Simulate(request, config, journal);
        return new EffectDemoRun(config, request, journal, result);
    }

    public static byte[] Write(string name, EffectDemoRun run) => CanonicalReplayArtifactWriter.Write(run.Journal,
        new ReplayArtifactMetadata(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), new ExternalId("combat-lab-wp10"), true, "WP10 synthetic " + name));

    private static JsonObject Entity(JsonObject root, string catalog, string key, string id) => root[catalog]!.AsArray()
        .Select(x => x!.AsObject()).Single(x => x[key]!.GetValue<string>() == id);
    private static JsonObject Attack(JsonObject root, string id, int damage = 30, int startup = 0, int active = 1, string schedule = "0", string tags = "strike")
    {
        var action = Entity(root, "actions", "action_id", id);
        action["energy_cost"] = 0; action["resource_cost"] = 0; action["base_weight"] = 100_000_000;
        action["startup_base_ticks"] = startup; action["startup_min_ticks"] = 0; action["startup_max_ticks"] = 20;
        action["active_ticks"] = active; action["cooldown_ticks"] = 0;
        action["recovery_base_ticks"] = 0; action["recovery_min_ticks"] = 0; action["recovery_max_ticks"] = 20;
        action["movement_mode"] = "None"; action["move_distance"] = 0; action["base_damage"] = damage;
        action["power_ratio_fp"] = 0; action["min_damage"] = 0; action["hit_schedule"] = schedule;
        action["hit_count"] = schedule.Split('|').Count(x => !x.StartsWith("grab:", StringComparison.Ordinal));
        action["hit_range_min"] = 0; action["hit_range_max"] = 10000; action["preferred_range_min"] = 0; action["preferred_range_max"] = 10000;
        action["base_knockback"] = 0; action["knockback_min"] = 0; action["knockback_max"] = 0;
        action["base_stagger"] = 0; action["base_stun_ticks"] = 0; action["wall_impact"] = false;
        action["wall_damage_per_unit_fp"] = 0; action["wall_damage_min"] = 0; action["wall_damage_max"] = 0;
        action["blockable"] = false; action["dodgeable"] = false; action["undodgeable"] = true;
        action["clash_priority"] = 10; action["tags"] = tags; return action;
    }
    private static void WallThrow(JsonObject source, int startup, bool knockdown)
    {
        source["settings"]!["global.arena.start_position_a"] = 8000; source["settings"]!["global.arena.start_position_b"] = 9500;
        var grab = Attack(source, "bear_earthbreaker", damage: knockdown ? 30 : 3000, startup: startup, active: 2,
            schedule: "grab:0|throw:1", tags: knockdown ? "grab|knockdown|wall_impact" : "grab|wall_impact");
        grab["cooldown_ticks"] = 100; grab["movement_mode"] = "Push"; grab["base_knockback"] = 1000; grab["knockback_max"] = 1000;
        grab["wall_impact"] = true; grab["wall_damage_per_unit_fp"] = 100; grab["wall_damage_min"] = 10; grab["wall_damage_max"] = 100;
    }
    private static JsonObject Effect(JsonObject source, string id, string target, int value, int duration,
        string boundary = "ExpireBeforeTick", string policy = "Refresh")
    {
        var effect = Entity(source, "effects", "effect_id", "effect_exposed").DeepClone().AsObject();
        effect["effect_id"] = id; effect["stack_group"] = id; effect["semantic_role"] = "None"; effect["duration_ticks"] = duration;
        effect["expiry_boundary"] = boundary; effect["stack_policy"] = policy; effect["stack_cap"] = 1;
        effect["modifier_stat1"] = target; effect["operation1"] = "Add"; effect["value1"] = value;
        effect["modifier_stat2"] = ""; effect["operation2"] = "Override"; effect["value2"] = 0;
        source["effects"]!.AsArray().Add(effect); return effect;
    }
    private static void Rule(JsonObject source, string id, string effect, string trigger = "BattleStart", int cap = 1) =>
        source["effect_rules"]!.AsArray().Add(new JsonObject { ["rule_id"] = id, ["owner_kind"] = "Global", ["owner_id"] = "global",
            ["trigger"] = trigger, ["recipient"] = "Self", ["condition"] = "LivingTarget", ["primitive"] = "ApplyEffect", ["effect_id"] = effect,
            ["priority"] = 0, ["internal_cooldown_ticks"] = 0, ["max_activations_per_tick"] = 1,
            ["max_activations_per_battle"] = cap, ["once_per_event"] = true });
    private static FighterBuildSnapshot Build(FighterId id, FighterSide side, string animal, string first, string second, string passive, string tactic) =>
        new(id, side, new StableId(animal), null, new[] { new StableId(first), new StableId(second) }, new StableId(passive),
            new GearSelection(new StableId("gear_offense_power_wraps"), new StableId("gear_defense_reinforced_hide"), new StableId("gear_utility_sprint_soles")), new StableId(tactic));
    private static void RequireName(string name)
    { if (!Names.Contains(name, StringComparer.Ordinal)) throw new ArgumentException("Unknown demo: " + name, nameof(name)); }
}

public sealed class EffectDemoRun
{
    public EffectDemoRun(CompiledBattleConfig config, BattleRequest request, CanonicalReplayJournal journal, BattleResult result)
    { Config = config; Request = request; Journal = journal; Result = result; }
    public CompiledBattleConfig Config { get; }
    public BattleRequest Request { get; }
    public CanonicalReplayJournal Journal { get; }
    public BattleResult Result { get; }
}
