using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Replay.Journal;

namespace CombatLab.IntegrationTests.Resolution;

internal enum ResolutionScenario
{
    Basic,
    DoubleKo,
    WallGrab,
}

internal static class Wp09ResolutionEngineFixture
{
    internal static ResolutionRun Run(
        ResolutionScenario scenario,
        JournalProfile profile = JournalProfile.StandardReplay)
    {
        var config = Compile(scenario);
        var journal = new CanonicalReplayJournal(
            new ExternalId("replay-wp09-" + Slug(scenario)),
            profile);
        var result = new CombatEngine().Simulate(Request(scenario, config), config, journal);
        return new ResolutionRun(result, journal);
    }

    internal static byte[] Write(ResolutionScenario scenario, ResolutionRun run) =>
        CanonicalReplayArtifactWriter.Write(
            run.Journal,
            new ReplayArtifactMetadata(
                new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
                new ExternalId("combat-lab-wp09-tests"),
                fixture: true,
                notes: "WP-09 " + Slug(scenario) + " resolution oracle"));

    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CombatLab.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln.");
    }

    internal static string SchemaPath() => Path.Combine(
        Root(), "schemas", "replay", "v0.1", "combat-replay.schema.json");

    private static CompiledBattleConfig Compile(ResolutionScenario scenario)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(Path.Combine(
            Root(), "config", "generated", "combat.balance.v0.1.json")))!.AsObject();
        var settings = root["settings"]!.AsObject();
        settings["battle.time_limit_ticks"] = scenario == ResolutionScenario.WallGrab ? 3 : 2;
        settings["global.arena.start_position_a"] = scenario == ResolutionScenario.WallGrab ? 8_000 : 4_000;
        settings["global.arena.start_position_b"] = scenario == ResolutionScenario.WallGrab ? 9_500 : 5_200;

        var fighters = root["fighters"]!.AsArray();
        PatchFighter(fighters, "bear", "max_health", 100);
        PatchFighter(fighters, "kangaroo", "max_health", 100);
        var actions = root["actions"]!.AsArray();
        foreach (var action in actions.Select(item => item!.AsObject()))
        {
            if (action["slot_type"]!.GetValue<string>() != "System")
            {
                action["base_weight"] = 1;
                action["energy_cost"] = 2_000;
                action["resource_cost"] = 0;
            }
        }

        PatchAction(actions, "sys_wait", action => action["base_weight"] = 1);
        if (scenario == ResolutionScenario.Basic)
        {
            MakeLethalStrike(actions, "bear_earthbreaker", clashPriority: 10);
        }
        else if (scenario == ResolutionScenario.DoubleKo)
        {
            MakeLethalStrike(actions, "bear_earthbreaker", clashPriority: 10);
            MakeLethalStrike(actions, "kangaroo_flying_kick", clashPriority: 10);
        }
        else
        {
            PatchAction(actions, "bear_earthbreaker", action =>
            {
                action["active_ticks"] = 2;
                action["base_damage"] = 600;
                action["base_knockback"] = 1_000;
                action["base_stagger"] = 60;
                action["base_stun_ticks"] = 6;
                action["base_weight"] = 100_000_000;
                action["chip_min"] = 10;
                action["clash_priority"] = 10;
                action["energy_cost"] = 0;
                action["hit_count"] = 1;
                action["hit_range_min"] = 0;
                action["hit_range_max"] = 1_000;
                action["hit_schedule"] = "grab:0|throw:1";
                action["knockback_min"] = 0;
                action["knockback_max"] = 1_000;
                action["min_damage"] = 600;
                action["movement_mode"] = "Push";
                action["power_ratio_fp"] = 0;
                action["recovery_base_ticks"] = 0;
                action["recovery_min_ticks"] = 0;
                action["recovery_max_ticks"] = 0;
                action["startup_base_ticks"] = 0;
                action["startup_min_ticks"] = 0;
                action["startup_max_ticks"] = 0;
                action["tags"] = "grab|wall_impact";
                action["undodgeable"] = true;
                action["wall_impact"] = true;
                action["wall_damage_per_unit_fp"] = 100;
                action["wall_damage_min"] = 10;
                action["wall_damage_max"] = 100;
            });
        }

        var compilation = new BattleConfigCompiler().Compile(Encoding.UTF8.GetBytes(root.ToJsonString()));
        if (!compilation.IsSuccess || compilation.Config is null)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, compilation.Issues.Select(issue =>
                issue.Code + " " + issue.Path + ": " + issue.Message)));
        }

        return compilation.Config;
    }

    private static void MakeLethalStrike(JsonArray actions, string id, int clashPriority) =>
        PatchAction(actions, id, action =>
        {
            action["active_ticks"] = 1;
            action["base_damage"] = 600;
            action["base_knockback"] = 0;
            action["base_stagger"] = 0;
            action["base_stun_ticks"] = 0;
            action["base_weight"] = 100_000_000;
            action["clash_priority"] = clashPriority;
            action["energy_cost"] = 0;
            action["hit_count"] = 1;
            action["hit_range_min"] = 0;
            action["hit_range_max"] = 1_000;
            action["hit_schedule"] = "0";
            action["knockback_min"] = 0;
            action["knockback_max"] = 0;
            action["min_damage"] = 600;
            action["movement_mode"] = "None";
            action["power_ratio_fp"] = 0;
            action["recovery_base_ticks"] = 0;
            action["recovery_min_ticks"] = 0;
            action["recovery_max_ticks"] = 0;
            action["startup_base_ticks"] = 0;
            action["startup_min_ticks"] = 0;
            action["startup_max_ticks"] = 0;
            action["tags"] = "strike";
            action["undodgeable"] = true;
            action["wall_impact"] = false;
            action["wall_damage_per_unit_fp"] = 0;
            action["wall_damage_min"] = 0;
            action["wall_damage_max"] = 0;
        });

    private static BattleRequest Request(ResolutionScenario scenario, CompiledBattleConfig config)
    {
        var buildA = Build(FighterId.FighterA, FighterSide.A, "bear", "bear_earthbreaker", "bear_rampage_charge",
            "bear_thick_hide", "tactic_pressure");
        var buildB = Build(FighterId.FighterB, FighterSide.B, "kangaroo", "kangaroo_flying_kick", "kangaroo_tail_counter",
            "kangaroo_never_still", "tactic_position");
        var allowed = new[]
        {
            new StableId("bear_earthbreaker"),
            new StableId("bear_rampage_charge"),
            new StableId("kangaroo_flying_kick"),
            new StableId("kangaroo_tail_counter"),
            new StableId("sys_retreat"),
            new StableId("sys_wait"),
        };
        return new BattleRequest(
            new ExternalId("battle-wp09-" + Slug(scenario)),
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            new ModeRulesSnapshot(
                new StableId("wp09_" + Slug(scenario).Replace('-', '_') + "_v01"),
                ContractVersions.ModeRules,
                NormalizationMode.None,
                new[] { buildA.AnimalId, buildB.AnimalId },
                allowed,
                new[] { buildA.PassiveId, buildB.PassiveId },
                new[]
                {
                    buildA.Gear.Offense, buildA.Gear.Defense, buildA.Gear.Utility,
                },
                new[] { buildA.TacticId, buildB.TacticId }),
            0,
            buildA,
            buildB);
    }

    private static FighterBuildSnapshot Build(
        FighterId id,
        FighterSide side,
        string animal,
        string special1,
        string special2,
        string passive,
        string tactic) => new(
        id,
        side,
        new StableId(animal),
        null,
        new[] { new StableId(special1), new StableId(special2) },
        new StableId(passive),
        new GearSelection(
            new StableId("gear_offense_power_wraps"),
            new StableId("gear_defense_reinforced_hide"),
            new StableId("gear_utility_sprint_soles")),
        new StableId(tactic));

    private static void PatchFighter(JsonArray fighters, string id, string field, int value)
    {
        var fighter = fighters.Select(item => item!.AsObject())
            .Single(item => item["animal_id"]!.GetValue<string>() == id);
        fighter[field] = value;
    }

    private static void PatchAction(JsonArray actions, string id, Action<JsonObject> patch)
    {
        var action = actions.Select(item => item!.AsObject())
            .Single(item => item["action_id"]!.GetValue<string>() == id);
        patch(action);
    }

    private static string Slug(ResolutionScenario scenario) => scenario switch
    {
        ResolutionScenario.Basic => "resolution-basic-l1",
        ResolutionScenario.DoubleKo => "resolution-double-ko-l1",
        ResolutionScenario.WallGrab => "resolution-wall-grab-l1",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };
}

internal sealed record ResolutionRun(
    Battle.Contracts.Results.BattleResult Result,
    CanonicalReplayJournal Journal);
