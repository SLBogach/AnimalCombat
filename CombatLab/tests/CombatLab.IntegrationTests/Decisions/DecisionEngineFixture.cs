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

namespace CombatLab.IntegrationTests.Decisions;

internal static class DecisionEngineFixture
{
    internal static readonly ExternalId BattleId =
        new("battle-wp08-decision-weighted-l1");
    internal static readonly ExternalId ReplayId =
        new("replay-wp08-decision-weighted-l1");
    internal static readonly StableId PawJabId = new("bear_paw_jab");
    internal static readonly StableId RetreatId = new("sys_retreat");

    private static readonly Lazy<CompiledBattleConfig> GoldenConfig =
        new(CompileGoldenConfig);

    internal static DecisionEngineRun Run(
        JournalProfile profile = JournalProfile.StandardReplay)
    {
        var config = GoldenConfig.Value;
        var journal = new CanonicalReplayJournal(ReplayId, profile);
        var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
        return new DecisionEngineRun(result, journal);
    }

    internal static DecisionSummaryEngineRun RunSummaryOnly()
    {
        var config = GoldenConfig.Value;
        var journal = new SummaryOnlyEventJournal(ReplayId);
        var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
        return new DecisionSummaryEngineRun(result, journal);
    }

    internal static byte[] WriteReplay(DecisionEngineRun run) =>
        CanonicalReplayArtifactWriter.Write(
            run.Journal,
            new ReplayArtifactMetadata(
                new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                new ExternalId("combat-lab-wp08-tests"),
                fixture: true,
                notes: "WP-08 decision_weighted_l1 integration oracle"));

    internal static string SchemaPath() => Path.Combine(
        RepositoryRoot(),
        "schemas",
        "replay",
        "v0.1",
        "combat-replay.schema.json");

    internal static string RepositoryRoot()
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

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln.");
    }

    private static CompiledBattleConfig CompileGoldenConfig()
    {
        var path = Path.Combine(
            RepositoryRoot(),
            "config",
            "generated",
            "combat.balance.v0.1.json");
        var root = JsonNode.Parse(File.ReadAllBytes(path))?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must be a JSON object.");
        var settings = root["settings"]?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must contain settings.");
        settings["battle.time_limit_ticks"] = 1;
        settings["global.arena.start_position_a"] = 4_000;
        settings["global.arena.start_position_b"] = 5_540;

        var compilation = new BattleConfigCompiler().Compile(
            Encoding.UTF8.GetBytes(root.ToJsonString()));
        if (!compilation.IsSuccess || compilation.Config is null)
        {
            throw new InvalidDataException(
                "Synthetic WP-08 config did not compile:" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    compilation.Issues.Select(issue =>
                        $"{issue.Code} {issue.Path}: {issue.Message}")));
        }

        return compilation.Config;
    }

    private static BattleRequest CreateRequest(CompiledBattleConfig config)
    {
        var specials = new[]
        {
            new StableId("bear_earthbreaker"),
            new StableId("bear_fury_maul"),
        };
        var gear = new GearSelection(
            new StableId("gear_offense_power_wraps"),
            new StableId("gear_defense_reinforced_hide"),
            new StableId("gear_utility_sprint_soles"));
        var buildA = Build(FighterId.FighterA, FighterSide.A);
        var buildB = Build(FighterId.FighterB, FighterSide.B);
        var modeRules = new ModeRulesSnapshot(
            new StableId("decision_weighted_l1_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { new StableId("bear") },
            new[]
            {
                new StableId("bear_earthbreaker"),
                new StableId("bear_fury_maul"),
                PawJabId,
                RetreatId,
                new StableId("sys_wait"),
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
            BattleId,
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            modeRules,
            0,
            buildA,
            buildB);

        FighterBuildSnapshot Build(FighterId fighterId, FighterSide side) => new(
            fighterId,
            side,
            new StableId("bear"),
            null,
            specials,
            new StableId("bear_thick_hide"),
            gear,
            new StableId("tactic_pressure"));
    }
}

internal sealed record DecisionEngineRun(
    BattleResult Result,
    CanonicalReplayJournal Journal);

internal sealed record DecisionSummaryEngineRun(
    BattleResult Result,
    SummaryOnlyEventJournal Journal);
