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
                "[wait|approach|decision] [create-output-path]");
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
            new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
            new ExternalId("combat-lab-wp08-target-probe"),
            "WP-08 decision_weighted_l1 target determinism probe");

        internal static ProbeScenario Parse(string value) => value switch
        {
            "wait" => Wait,
            "approach" => Approach,
            "decision" => Decision,
            _ => throw new ArgumentException("Probe scenario must be wait, approach, or decision.", nameof(value)),
        };
    }
}
