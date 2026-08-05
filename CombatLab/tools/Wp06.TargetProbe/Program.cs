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
    private static readonly ExternalId BattleId = new("battle-wp06-wait-equal-l1");
    private static readonly ExternalId ReplayId = new("replay-wp06-wait-equal-l1");
    private static readonly StableId WaitActionId = new("sys_wait");

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: Wp06.TargetProbe <CombatLab root> <netstandard2.1|net10.0>");
            return 2;
        }

        try
        {
            var combatLabRoot = Path.GetFullPath(args[0]);
            ValidateAssemblyTargets(args[1]);
            var config = CompileGoldenConfig(combatLabRoot);
            var journal = new CanonicalReplayJournal(ReplayId);
            var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
            if (result.Status != BattleResultStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"wait_equal_l1 ended with unexpected status '{result.Status}'.");
            }

            var replay = CanonicalReplayArtifactWriter.Write(
                journal,
                new ReplayArtifactMetadata(
                    new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
                    new ExternalId("combat-lab-wp06-target-probe"),
                    fixture: true,
                    notes: "WP-06 target determinism probe"));
            Console.Out.Write(Encoding.UTF8.GetString(replay));
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

    private static CompiledBattleConfig CompileGoldenConfig(string combatLabRoot)
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
        settings["battle.time_limit_ticks"] = 1;

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

    private static BattleRequest CreateRequest(CompiledBattleConfig config)
    {
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
            new StableId("engine_shell_wait_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { buildA.AnimalId, buildB.AnimalId },
            buildA.SpecialActionIds.Concat(buildB.SpecialActionIds).Append(WaitActionId),
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
            BattleId,
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            modeRules,
            2_026_072_901,
            buildA,
            buildB);
    }
}
