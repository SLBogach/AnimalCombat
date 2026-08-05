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

namespace CombatLab.IntegrationTests.Movement;

internal static class MovementEngineFixture
{
    internal static MovementEngineRun Run(
        string caseId,
        int startPositionA,
        int startPositionB,
        JournalProfile profile = JournalProfile.StandardReplay,
        int timeLimit = 3,
        int? maximumEvents = null,
        int? maximumZeroProgressTicks = null)
    {
        var config = CompileConfig(
            startPositionA,
            startPositionB,
            timeLimit,
            maximumEvents,
            maximumZeroProgressTicks);
        var journal = new CanonicalReplayJournal(new ExternalId("replay-wp07-" + caseId), profile);
        var result = new CombatEngine().Simulate(CreateRequest(caseId, config), config, journal);
        return new MovementEngineRun(result, journal);
    }

    internal static MovementSummaryEngineRun RunSummaryOnly(
        string caseId,
        int startPositionA,
        int startPositionB,
        int timeLimit = 3)
    {
        var config = CompileConfig(
            startPositionA,
            startPositionB,
            timeLimit,
            maximumEvents: null,
            maximumZeroProgressTicks: null);
        var journal = new SummaryOnlyEventJournal(new ExternalId("replay-wp07-" + caseId));
        var result = new CombatEngine().Simulate(CreateRequest(caseId, config), config, journal);
        return new MovementSummaryEngineRun(result, journal);
    }

    private static CompiledBattleConfig CompileConfig(
        int startPositionA,
        int startPositionB,
        int timeLimit,
        int? maximumEvents,
        int? maximumZeroProgressTicks)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(GeneratedConfigPath()))?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must be a JSON object.");
        var settings = root["settings"]?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must contain settings.");
        settings["battle.time_limit_ticks"] = timeLimit;
        settings["global.arena.start_position_a"] = startPositionA;
        settings["global.arena.start_position_b"] = startPositionB;
        if (maximumEvents.HasValue)
        {
            settings["global.sim.max_events_per_battle"] = maximumEvents.Value;
        }

        if (maximumZeroProgressTicks.HasValue)
        {
            settings["global.sim.max_zero_progress_ticks"] = maximumZeroProgressTicks.Value;
        }

        var compilation = new BattleConfigCompiler().Compile(
            Encoding.UTF8.GetBytes(root.ToJsonString()));
        if (!compilation.IsSuccess || compilation.Config is null)
        {
            throw new InvalidDataException(
                "Synthetic WP-07 config did not compile:" + Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    compilation.Issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
        }

        return compilation.Config;
    }

    private static BattleRequest CreateRequest(string caseId, CompiledBattleConfig config)
    {
        var buildA = new FighterBuildSnapshot(
            FighterId.FighterA,
            FighterSide.A,
            new StableId("bear"),
            null,
            new[] { new StableId("bear_earthbreaker"), new StableId("bear_rampage_charge") },
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
            new[] { new StableId("kangaroo_flying_kick"), new StableId("kangaroo_tail_counter") },
            new StableId("kangaroo_never_still"),
            new GearSelection(
                new StableId("gear_offense_precision_lens"),
                new StableId("gear_defense_reinforced_hide"),
                new StableId("gear_utility_sprint_soles")),
            new StableId("tactic_position"));
        var modeRules = new ModeRulesSnapshot(
            new StableId("movement_" + caseId.Replace('-', '_') + "_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { buildA.AnimalId, buildB.AnimalId },
            buildA.SpecialActionIds
                .Concat(buildB.SpecialActionIds)
                .Concat(new[]
                {
                    new StableId("sys_approach"),
                    new StableId("sys_retreat"),
                    new StableId("sys_wait"),
                }),
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
            new ExternalId("battle-wp07-" + caseId),
            ContractVersions.Engine,
            config.Reference.ConfigHash,
            modeRules,
            2_026_072_901UL,
            buildA,
            buildB);
    }

    private static string GeneratedConfigPath() => Path.Combine(
        RepositoryRoot(),
        "config",
        "generated",
        "combat.balance.v0.1.json");

    internal static string SchemaPath() => Path.Combine(
        RepositoryRoot(),
        "schemas",
        "replay",
        "v0.1",
        "combat-replay.schema.json");

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

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln.");
    }
}

internal sealed record MovementEngineRun(BattleResult Result, CanonicalReplayJournal Journal);

internal sealed record MovementSummaryEngineRun(BattleResult Result, SummaryOnlyEventJournal Journal);
