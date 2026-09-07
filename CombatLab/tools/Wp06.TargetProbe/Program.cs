using System.Text;
using System.Text.Json.Nodes;
using System.Reflection;
using System.Runtime.Versioning;
using Battle.Config.Compiler;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Replay.Journal;

namespace Wp06.TargetProbe;

internal static class Program
{
    private static readonly StableId WaitActionId = new("sys_wait");
    private static readonly StableId ApproachActionId = new("sys_approach");
    private static readonly StableId RetreatActionId = new("sys_retreat");

    public static int Main(string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            Console.Error.WriteLine(
                "Usage: Wp06.TargetProbe <CombatLab root> <netstandard2.1|net10.0> " +
                "[wait|approach|decision|resolution-basic|resolution-double-ko|resolution-wall-grab] " +
                "[create-output-path]");
            return 2;
        }

        try
        {
            var combatLabRoot = Path.GetFullPath(args[0]);
            ValidateAssemblyTargets(args[1]);
            var scenario = args.Length >= 3 ? ProbeScenario.Parse(args[2]) : ProbeScenario.Wait;
            var config = CompileGoldenConfig(combatLabRoot, scenario);
            var journal = new CanonicalReplayJournal(scenario.ReplayId);
            var result = new CombatEngine().Simulate(CreateRequest(config, scenario), config, journal);
            if (result.Status != BattleResultStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"{scenario.Name} ended with unexpected status '{result.Status}': " +
                    string.Join(", ", result.RejectionErrors.Select(error =>
                        error.Code.Value + "@" + error.Path)));
            }

            var replay = CanonicalReplayArtifactWriter.Write(
                journal,
                new ReplayArtifactMetadata(
                    scenario.CreatedAtUtc,
                    scenario.Producer,
                    fixture: true,
                    notes: scenario.Notes));
            if (args.Length == 4)
            {
                var outputPath = Path.GetFullPath(args[3]);
                using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(replay);
                Console.Out.Write(outputPath);
            }
            else
            {
                Console.Out.Write(Encoding.UTF8.GetString(replay));
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ValidateAssemblyTargets(string combatTarget)
    {
        var expectedFramework = combatTarget switch
        {
            "netstandard2.1" => ".NETStandard,Version=v2.1",
            "net10.0" => ".NETCoreApp,Version=v10.0",
            _ => throw new ArgumentException(
                "Combat target must be netstandard2.1 or net10.0.",
                nameof(combatTarget)),
        };
        var assemblies = new[]
        {
            typeof(BattleConfigCompiler).Assembly,
            typeof(BattleRequest).Assembly,
            typeof(CombatEngine).Assembly,
            typeof(CanonicalReplayJournal).Assembly,
        };

        foreach (var assembly in assemblies)
        {
            var actualFramework = assembly
                .GetCustomAttribute<TargetFrameworkAttribute>()?
                .FrameworkName;
            if (!StringComparer.Ordinal.Equals(actualFramework, expectedFramework))
            {
                throw new InvalidOperationException(
                    $"Assembly '{assembly.GetName().Name}' targets '{actualFramework}', " +
                    $"expected '{expectedFramework}'.");
            }
        }
    }

    private static CompiledBattleConfig CompileGoldenConfig(
        string combatLabRoot,
        ProbeScenario scenario)
    {
        var configPath = Path.Combine(
            combatLabRoot,
            "config",
            "generated",
            "combat.balance.v0.1.json");
        var root = JsonNode.Parse(File.ReadAllBytes(configPath))?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must be a JSON object.");
        var settings = root["settings"]?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must contain settings.");
        settings["battle.time_limit_ticks"] = scenario.TimeLimitTicks;
        settings["global.arena.start_position_a"] = scenario.StartPositionA;
        settings["global.arena.start_position_b"] = scenario.StartPositionB;
        if (scenario.ResolutionKind.HasValue)
        {
            PatchResolutionConfig(root, scenario.ResolutionKind.Value);
        }

        var compilation = new BattleConfigCompiler().Compile(
            Encoding.UTF8.GetBytes(root.ToJsonString()));
        if (!compilation.IsSuccess || compilation.Config is null)
        {
            throw new InvalidDataException(
                "Synthetic WP-06 config did not compile:" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    compilation.Issues.Select(
                        issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
        }

        return compilation.Config;
    }

    private static BattleRequest CreateRequest(
        CompiledBattleConfig config,
        ProbeScenario scenario)
    {
        if (scenario.DecisionWeighted)
        {
            return CreateWeightedDecisionRequest(config, scenario);
        }

        if (scenario.ResolutionKind.HasValue)
        {
            return CreateResolutionRequest(config, scenario);
        }

        var buildA = new FighterBuildSnapshot(
            FighterId.FighterA,
            FighterSide.A,
            new StableId("bear"),
            null,
            new[]
            {
                new StableId("bear_earthbreaker"),
                new StableId("bear_rampage_charge"),
            },
            new StableId("bear_thick_hide"),
            new GearSelection(
                new StableId("gear_offense_power_wraps"),
                new StableId("gear_defense_reinforced_hide"),
                new StableId("gear_utility_sprint_soles")),
            new StableId("tactic_pressure"));
        var buildB = new FighterBuildSnapshot(
            FighterId.FighterB,
            FighterSide.B,
            new StableId("kangaroo"),
            null,
            new[]
            {
                new StableId("kangaroo_flying_kick"),
                new StableId("kangaroo_tail_counter"),
            },
            new StableId("kangaroo_never_still"),
            new GearSelection(
                new StableId("gear_offense_precision_lens"),
                new StableId("gear_defense_reinforced_hide"),
                new StableId("gear_utility_sprint_soles")),
            new StableId("tactic_position"));
        var modeRules = new ModeRulesSnapshot(
            scenario.ModeRulesId,
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { buildA.AnimalId, buildB.AnimalId },
            buildA.SpecialActionIds
                .Concat(buildB.SpecialActionIds)
                .Concat(scenario.IncludeMovementActions
                    ? new[] { ApproachActionId, RetreatActionId, WaitActionId }
                    : new[] { WaitActionId }),
            new[] { buildA.PassiveId, buildB.PassiveId },
            new[]
            {
                buildA.Gear.Offense,
                buildA.Gear.Defense,
                buildA.Gear.Utility,
                buildB.Gear.Offense,
            },
            new[] { buildA.TacticId, buildB.TacticId });

        return new BattleRequest(
            scenario.BattleId,
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            modeRules,
            scenario.MasterSeed,
            buildA,
            buildB);
    }

    private static BattleRequest CreateWeightedDecisionRequest(
        CompiledBattleConfig config,
        ProbeScenario scenario)
    {
        var specialActionIds = new[]
        {
            new StableId("bear_earthbreaker"),
            new StableId("bear_fury_maul"),
        };
        var gear = new GearSelection(
            new StableId("gear_offense_power_wraps"),
            new StableId("gear_defense_reinforced_hide"),
            new StableId("gear_utility_sprint_soles"));
        var buildA = new FighterBuildSnapshot(
            FighterId.FighterA,
            FighterSide.A,
            new StableId("bear"),
            null,
            specialActionIds,
            new StableId("bear_thick_hide"),
            gear,
            new StableId("tactic_pressure"));
        var buildB = new FighterBuildSnapshot(
            FighterId.FighterB,
            FighterSide.B,
            new StableId("bear"),
            null,
            specialActionIds,
            new StableId("bear_thick_hide"),
            gear,
            new StableId("tactic_pressure"));
        var modeRules = new ModeRulesSnapshot(
            scenario.ModeRulesId,
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { new StableId("bear") },
            new[]
            {
                new StableId("bear_earthbreaker"),
                new StableId("bear_fury_maul"),
                new StableId("bear_paw_jab"),
                RetreatActionId,
                WaitActionId,
            },
            new[] { new StableId("bear_thick_hide") },
            new[]
            {
                new StableId("gear_defense_reinforced_hide"),
                new StableId("gear_offense_power_wraps"),
                new StableId("gear_utility_sprint_soles"),
            },
            new[] { new StableId("tactic_pressure") });

        return new BattleRequest(
            scenario.BattleId,
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            modeRules,
            scenario.MasterSeed,
            buildA,
            buildB);
    }

    private static BattleRequest CreateResolutionRequest(
        CompiledBattleConfig config,
        ProbeScenario scenario)
    {
        var buildA = Build(
            FighterId.FighterA,
            FighterSide.A,
            "bear",
            "bear_earthbreaker",
            "bear_rampage_charge",
            "bear_thick_hide",
            "tactic_pressure");
        var buildB = Build(
            FighterId.FighterB,
            FighterSide.B,
            "kangaroo",
            "kangaroo_flying_kick",
            "kangaroo_tail_counter",
            "kangaroo_never_still",
            "tactic_position");
        var allowed = new[]
        {
            new StableId("bear_earthbreaker"),
            new StableId("bear_rampage_charge"),
            new StableId("kangaroo_flying_kick"),
            new StableId("kangaroo_tail_counter"),
            RetreatActionId,
            WaitActionId,
        };
        var modeRules = new ModeRulesSnapshot(
            scenario.ModeRulesId,
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { buildA.AnimalId, buildB.AnimalId },
            allowed,
            new[] { buildA.PassiveId, buildB.PassiveId },
            new[] { buildA.Gear.Offense, buildA.Gear.Defense, buildA.Gear.Utility },
            new[] { buildA.TacticId, buildB.TacticId });

        return new BattleRequest(
            scenario.BattleId,
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            modeRules,
            scenario.MasterSeed,
            buildA,
            buildB);

        static FighterBuildSnapshot Build(
            FighterId fighterId,
            FighterSide side,
            string animal,
            string special1,
            string special2,
            string passive,
            string tactic) => new(
            fighterId,
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
    }

    private static void PatchResolutionConfig(JsonObject root, ResolutionProbeKind kind)
    {
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
        if (kind is ResolutionProbeKind.Basic or ResolutionProbeKind.DoubleKo)
        {
            MakeLethalStrike(actions, "bear_earthbreaker");
        }

        if (kind == ResolutionProbeKind.DoubleKo)
        {
            MakeLethalStrike(actions, "kangaroo_flying_kick");
        }

        if (kind == ResolutionProbeKind.WallGrab)
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
    }

    private static void MakeLethalStrike(JsonArray actions, string id) =>
        PatchAction(actions, id, action =>
        {
            action["active_ticks"] = 1;
            action["base_damage"] = 600;
            action["base_knockback"] = 0;
            action["base_stagger"] = 0;
            action["base_stun_ticks"] = 0;
            action["base_weight"] = 100_000_000;
            action["clash_priority"] = 10;
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

    private enum ResolutionProbeKind
    {
        Basic,
        DoubleKo,
        WallGrab,
    }

    private sealed record ProbeScenario(
        string Name,
        ExternalId BattleId,
        ExternalId ReplayId,
        StableId ModeRulesId,
        int TimeLimitTicks,
        int StartPositionA,
        int StartPositionB,
        bool IncludeMovementActions,
        ulong MasterSeed,
        bool DecisionWeighted,
        ResolutionProbeKind? ResolutionKind,
        DateTimeOffset CreatedAtUtc,
        ExternalId Producer,
        string Notes)
    {
        internal static ProbeScenario Wait { get; } = new(
            "wait_equal_l1",
            new ExternalId("battle-wp06-wait-equal-l1"),
            new ExternalId("replay-wp06-wait-equal-l1"),
            new StableId("engine_shell_wait_v01"),
            1,
            2_000,
            4_500,
            false,
            2_026_072_901,
            false,
            null,
            new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
            new ExternalId("combat-lab-wp06-target-probe"),
            "Current-engine wait_equal_l1 determinism probe");

        internal static ProbeScenario Approach { get; } = new(
            "approach_band_l3",
            new ExternalId("battle-wp07-approach-band-l3"),
            new ExternalId("replay-wp07-approach-band-l3"),
            new StableId("movement_approach_band_l3_v01"),
            3,
            4_000,
            6_555,
            true,
            2_026_072_901,
            false,
            null,
            new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
            new ExternalId("combat-lab-wp07-target-probe"),
            "WP-07 approach_band_l3 target determinism probe");

        internal static ProbeScenario Decision { get; } = new(
            "decision_weighted_l1",
            new ExternalId("battle-wp08-decision-weighted-l1"),
            new ExternalId("replay-wp08-decision-weighted-l1"),
            new StableId("decision_weighted_l1_v01"),
            1,
            4_000,
            5_540,
            false,
            0,
            true,
            null,
            new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
            new ExternalId("combat-lab-wp08-target-probe"),
            "WP-08 decision_weighted_l1 target determinism probe");

        internal static ProbeScenario ResolutionBasic { get; } = Resolution(
            "resolution-basic-l1", ResolutionProbeKind.Basic, 2, 4_000, 5_200);

        internal static ProbeScenario ResolutionDoubleKo { get; } = Resolution(
            "resolution-double-ko-l1", ResolutionProbeKind.DoubleKo, 2, 4_000, 5_200);

        internal static ProbeScenario ResolutionWallGrab { get; } = Resolution(
            "resolution-wall-grab-l1", ResolutionProbeKind.WallGrab, 3, 8_000, 9_500);

        private static ProbeScenario Resolution(
            string slug,
            ResolutionProbeKind kind,
            int timeLimitTicks,
            int startPositionA,
            int startPositionB) => new(
            slug,
            new ExternalId("battle-wp09-" + slug),
            new ExternalId("replay-wp09-" + slug),
            new StableId("wp09_" + slug.Replace('-', '_') + "_v01"),
            timeLimitTicks,
            startPositionA,
            startPositionB,
            false,
            0,
            false,
            kind,
            new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            new ExternalId("combat-lab-wp09-tests"),
            "WP-09 " + slug + " resolution oracle");

        internal static ProbeScenario Parse(string value) => value switch
        {
            "wait" => Wait,
            "approach" => Approach,
            "decision" => Decision,
            "resolution-basic" => ResolutionBasic,
            "resolution-double-ko" => ResolutionDoubleKo,
            "resolution-wall-grab" => ResolutionWallGrab,
            _ => throw new ArgumentException(
                "Probe scenario must be wait, approach, decision, resolution-basic, " +
                "resolution-double-ko, or resolution-wall-grab.",
                nameof(value)),
        };
    }
}
