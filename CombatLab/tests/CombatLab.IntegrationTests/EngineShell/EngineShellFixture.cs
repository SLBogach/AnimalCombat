using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Replay.Journal;

namespace CombatLab.IntegrationTests.EngineShell;

internal static class EngineShellFixture
{
    internal static readonly ExternalId BattleId = new("battle-wp06-wait-equal-l1");
    internal static readonly ExternalId ReplayId = new("replay-wp06-wait-equal-l1");
    internal static readonly StableId WaitActionId = new("sys_wait");

    private static readonly Lazy<CompiledBattleConfig> GoldenConfig = new(
        () => CompileGoldenConfig());

    internal static CanonicalEngineRun RunCanonical(
        JournalProfile profile = JournalProfile.StandardReplay)
    {
        var config = GoldenConfig.Value;
        var journal = new CanonicalReplayJournal(ReplayId, profile);
        var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
        return new CanonicalEngineRun(result, journal);
    }

    internal static SummaryEngineRun RunSummaryOnly()
    {
        var config = GoldenConfig.Value;
        var journal = new SummaryOnlyEventJournal(ReplayId);
        var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
        return new SummaryEngineRun(result, journal);
    }

    internal static EngineShellCase CreateCase(int maximumEvents)
    {
        var config = CompileGoldenConfig(maximumEvents);
        return new EngineShellCase(CreateRequest(config), config);
    }

    private static CompiledBattleConfig CompileGoldenConfig(int? maximumEvents = null)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(GeneratedConfigPath()))?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must be a JSON object.");
        var settings = root["settings"]?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must contain settings.");
        settings["battle.time_limit_ticks"] = 1;
        settings["global.arena.start_position_a"] = 2_000;
        settings["global.arena.start_position_b"] = 4_500;
        if (maximumEvents.HasValue)
        {
            settings["global.sim.max_events_per_battle"] = maximumEvents.Value;
        }

        var candidate = Encoding.UTF8.GetBytes(root.ToJsonString());
        var compilation = new BattleConfigCompiler().Compile(candidate);
        if (!compilation.IsSuccess || compilation.Config is null)
        {
            throw new InvalidDataException(
                "Synthetic WP-06 config did not compile:" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    compilation.Issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
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

    private static string GeneratedConfigPath() =>
        Path.Combine(
            RepositoryRoot(),
            "config",
            "generated",
            "combat.balance.v0.1.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CombatLab.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate CombatLab.sln from the test output directory.");
    }
}

internal sealed record CanonicalEngineRun(
    BattleResult Result,
    CanonicalReplayJournal Journal);

internal sealed record SummaryEngineRun(
    BattleResult Result,
    SummaryOnlyEventJournal Journal);

internal sealed record EngineShellCase(
    BattleRequest Request,
    CompiledBattleConfig Config);
