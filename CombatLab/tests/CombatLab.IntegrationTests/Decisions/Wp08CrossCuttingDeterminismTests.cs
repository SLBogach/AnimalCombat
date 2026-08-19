using System.Globalization;
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

namespace CombatLab.IntegrationTests.Decisions;

public sealed class Wp08CrossCuttingDeterminismTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_001_WeightedDecisionIsByteExactAcrossOneHundredDiagnosticRuns()
    {
        var baseline = WeightedDecisionFixture.RunCanonical(JournalProfile.DiagnosticReplay);
        var baselineArtifact = WeightedDecisionFixture.WriteArtifact(baseline.Journal);

        Assert.Equal(2, baseline.Journal.DecisionTraces.Count);
        for (var repetition = 1; repetition < 100; repetition++)
        {
            var current = WeightedDecisionFixture.RunCanonical(JournalProfile.DiagnosticReplay);

            Assert.Equal(BattleResultStatus.Completed, current.Result.Status);
            Assert.Equal(baseline.Journal.InputDigest, current.Journal.InputDigest);
            Assert.Equal(baseline.Journal.FinalDigest, current.Journal.FinalDigest);
            Assert.Equal(baseline.Result.FinalDigest, current.Result.FinalDigest);
            Assert.Equal(baselineArtifact, WeightedDecisionFixture.WriteArtifact(current.Journal));
        }
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_002_JournalProfilesIsolateDiagnosticsWithoutChangingGameplayOrRng()
    {
        var standard = WeightedDecisionFixture.RunCanonical(JournalProfile.StandardReplay);
        var diagnostic = WeightedDecisionFixture.RunCanonical(JournalProfile.DiagnosticReplay);
        var summaryOnly = WeightedDecisionFixture.RunSummaryOnly();

        Assert.Equal(standard.Journal.InputDigest, diagnostic.Journal.InputDigest);
        Assert.Equal(standard.Journal.InputDigest, summaryOnly.Journal.InputDigest);
        Assert.Equal(standard.Journal.FinalDigest, diagnostic.Journal.FinalDigest);
        Assert.Equal(standard.Journal.FinalDigest, summaryOnly.Journal.FinalDigest);
        Assert.Equal(standard.Result.FinalDigest, diagnostic.Result.FinalDigest);
        Assert.Equal(standard.Result.FinalDigest, summaryOnly.Result.FinalDigest);
        Assert.Empty(standard.Journal.DecisionTraces);
        Assert.Equal(2, diagnostic.Journal.DecisionTraces.Count);
        Assert.Null(summaryOnly.Result.ReplayId);
        Assert.Equal(9, summaryOnly.Journal.EventCount);
        Assert.Equal(2, summaryOnly.Journal.RngDrawCounts[RngStream.Decision]);
        Assert.Equal(0, summaryOnly.Journal.RngDrawCounts[RngStream.Resolution]);
        AssertSummariesEqual(standard.Result.Summary!, diagnostic.Result.Summary!);
        AssertSummariesEqual(standard.Result.Summary!, summaryOnly.Result.Summary!);
        Assert.Equal(standard.Journal.Events.Count, diagnostic.Journal.Events.Count);
        for (var index = 0; index < standard.Journal.Events.Count; index++)
        {
            Assert.Equal(
                standard.Journal.Events[index].CanonicalJson.ToArray(),
                diagnostic.Journal.Events[index].CanonicalJson.ToArray());
        }
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_003_CanonicalWeightedReplayIsCultureInvariant()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            byte[]? baseline = null;
            foreach (var cultureName in new[] { "en-US", "ru-RU", "tr-TR" })
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                var run = WeightedDecisionFixture.RunCanonical(JournalProfile.DiagnosticReplay);
                var artifact = WeightedDecisionFixture.WriteArtifact(run.Journal);

                baseline ??= artifact;
                Assert.Equal(baseline, artifact);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_REG_006_TimeoutBoundaryEmitsNoLaterDecisionOrActionEvent()
    {
        var run = WeightedDecisionFixture.RunCanonical(JournalProfile.StandardReplay);
        var events = run.Journal.Events.Select(item => item.Draft).ToArray();
        var terminalBoundary = Array.FindIndex(
            events,
            item => item.EventType == CombatEventType.TimeoutReached);

        Assert.Equal(6, terminalBoundary);
        Assert.Equal(
            new[]
            {
                CombatEventType.TimeoutReached,
                CombatEventType.DrawDeclared,
                CombatEventType.BattleEnded,
            },
            events.Skip(terminalBoundary).Select(item => item.EventType));
        Assert.DoesNotContain(
            events.Skip(terminalBoundary + 1),
            item => item.EventType is CombatEventType.DecisionMade or
                CombatEventType.ActionCommitted or CombatEventType.AttackPrepared);
    }

    private static void AssertSummariesEqual(BattleSummary expected, BattleSummary actual)
    {
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.WinnerFighterId, actual.WinnerFighterId);
        Assert.Equal(expected.EndReason, actual.EndReason);
        Assert.Equal(expected.EndTick, actual.EndTick);
        Assert.Equal(expected.DurationTicks, actual.DurationTicks);
        Assert.Equal(expected.EventCount, actual.EventCount);
        Assert.Equal(expected.PivotalEventIds, actual.PivotalEventIds);
        Assert.Equal(expected.FinalFrames.Count, actual.FinalFrames.Count);
        for (var index = 0; index < expected.FinalFrames.Count; index++)
        {
            var left = expected.FinalFrames[index];
            var right = actual.FinalFrames[index];
            Assert.Equal(left.FighterId, right.FighterId);
            Assert.Equal(left.Position, right.Position);
            Assert.Equal(left.Facing, right.Facing);
            Assert.Equal(left.State, right.State);
            Assert.Equal(left.StateTicksRemaining, right.StateTicksRemaining);
            Assert.Equal(left.ActionId, right.ActionId);
            Assert.Equal(left.ActionPhase, right.ActionPhase);
            Assert.Equal(left.Health, right.Health);
            Assert.Equal(left.Energy, right.Energy);
            Assert.Equal(left.UniqueResource, right.UniqueResource);
            Assert.Equal(left.Effects, right.Effects);
        }
    }
}

internal static class WeightedDecisionFixture
{
    private static readonly ExternalId BattleId = new("battle-wp08-decision-weighted-l1");
    private static readonly ExternalId ReplayId = new("replay-wp08-decision-weighted-l1");
    private static readonly Lazy<CompiledBattleConfig> Config = new(CompileConfig);

    internal static WeightedCanonicalRun RunCanonical(JournalProfile profile)
    {
        var config = Config.Value;
        var journal = new CanonicalReplayJournal(ReplayId, profile);
        var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
        AssertCompleted(result);
        return new WeightedCanonicalRun(result, journal);
    }

    internal static WeightedSummaryRun RunSummaryOnly()
    {
        var config = Config.Value;
        var journal = new SummaryOnlyEventJournal(ReplayId);
        var result = new CombatEngine().Simulate(CreateRequest(config), config, journal);
        AssertCompleted(result);
        return new WeightedSummaryRun(result, journal);
    }

    internal static byte[] WriteArtifact(CanonicalReplayJournal journal) =>
        CanonicalReplayArtifactWriter.Write(
            journal,
            new ReplayArtifactMetadata(
                new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                new ExternalId("combat-lab-wp08-tests"),
                fixture: true,
                notes: "WP-08 cross-cutting determinism oracle"));

    private static CompiledBattleConfig CompileConfig()
    {
        var root = JsonNode.Parse(File.ReadAllBytes(GeneratedConfigPath()))?.AsObject()
            ?? throw new InvalidDataException("Generated balance config must be an object.");
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
                "WP-08 weighted decision config did not compile:" + Environment.NewLine +
                string.Join(Environment.NewLine, compilation.Issues.Select(
                    issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
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
        var buildA = new FighterBuildSnapshot(
            FighterId.FighterA,
            FighterSide.A,
            new StableId("bear"),
            null,
            specials,
            new StableId("bear_thick_hide"),
            gear,
            new StableId("tactic_pressure"));
        var buildB = new FighterBuildSnapshot(
            FighterId.FighterB,
            FighterSide.B,
            new StableId("bear"),
            null,
            specials,
            new StableId("bear_thick_hide"),
            gear,
            new StableId("tactic_pressure"));
        var modeRules = new ModeRulesSnapshot(
            new StableId("decision_weighted_l1_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { new StableId("bear") },
            new[]
            {
                new StableId("bear_earthbreaker"),
                new StableId("bear_fury_maul"),
                new StableId("bear_paw_jab"),
                new StableId("sys_retreat"),
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
    }

    private static void AssertCompleted(BattleResult result)
    {
        if (result.Status != BattleResultStatus.Completed)
        {
            throw new InvalidOperationException(
                "WP-08 weighted decision run was rejected: " +
                string.Join(", ", result.RejectionErrors.Select(error => error.Code.Value + "@" + error.Path)));
        }
    }

    private static string GeneratedConfigPath() =>
        Path.Combine(RepositoryRoot(), "config", "generated", "combat.balance.v0.1.json");

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

internal sealed record WeightedCanonicalRun(
    BattleResult Result,
    CanonicalReplayJournal Journal);

internal sealed record WeightedSummaryRun(
    BattleResult Result,
    SummaryOnlyEventJournal Journal);
