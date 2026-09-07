using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Replay.Journal;
using Battle.Replay.Verification;
using CombatLab.IntegrationTests.Decisions;
using CombatLab.IntegrationTests.EngineShell;

namespace CombatLab.IntegrationTests.Resolution;

[Trait("WorkPackage", "WP09")]
public sealed class Wp09ResolutionIntegrationTests
{
    [Fact]
    [Trait("AcceptanceId", "WP09-DEF-012")]
    [Trait("AcceptanceId", "WP09-CTL-008")]
    [Trait("AcceptanceId", "WP09-OUT-006")]
    [Trait("AcceptanceId", "WP09-INT-001")]
    public void WP09_INT_001_BasicResolutionEndsBeforeTimeoutAndReplayVerifies()
    {
        var run = Wp09ResolutionEngineFixture.Run(ResolutionScenario.Basic);

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        Assert.Equal(BattleOutcome.FighterAWin, run.Result.Summary!.Outcome);
        Assert.Equal(BattleEndReason.Defeat, run.Result.Summary.EndReason);
        Assert.DoesNotContain(run.Journal.Events, item => item.Draft.EventType == CombatEventType.TimeoutReached);
        AssertOrdered(run, CombatEventType.AttackHit, CombatEventType.DamageApplied,
            CombatEventType.StateChanged, CombatEventType.FighterDefeated, CombatEventType.BattleEnded);
        Verify(run, ResolutionScenario.Basic);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-INT-002")]
    [Trait("AcceptanceId", "WP09-DET-004")]
    public void WP09_INT_002_EqualStrikeTradeProducesDoubleKo()
    {
        var run = Wp09ResolutionEngineFixture.Run(ResolutionScenario.DoubleKo);

        Assert.Equal(BattleOutcome.Draw, run.Result.Summary!.Outcome);
        Assert.Equal(BattleEndReason.DoubleKO, run.Result.Summary.EndReason);
        var events = run.Journal.Events.Select(item => item.Draft).ToArray();
        var damage = events.Select((item, index) => (item, index))
            .Where(pair => pair.item.EventType == CombatEventType.DamageApplied).ToArray();
        var firstDefeat = Array.FindIndex(events, item => item.EventType == CombatEventType.FighterDefeated);
        Assert.Equal(2, damage.Length);
        Assert.True(damage.Max(pair => pair.index) < firstDefeat);
        AssertOrdered(run, CombatEventType.DrawDeclared, CombatEventType.BattleEnded);
        Verify(run, ResolutionScenario.DoubleKo);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-FRC-008")]
    [Trait("AcceptanceId", "WP09-GRB-008")]
    [Trait("AcceptanceId", "WP09-INT-003")]
    public void WP09_INT_003_GrabThrowWallChainIsContiguousAndVerifiable()
    {
        var run = Wp09ResolutionEngineFixture.Run(ResolutionScenario.WallGrab);

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        AssertOrdered(run,
            CombatEventType.GrabStarted,
            CombatEventType.AttackHit,
            CombatEventType.DamageApplied,
            CombatEventType.KnockbackApplied,
            CombatEventType.WallImpact,
            CombatEventType.GrabEnded,
            CombatEventType.FighterDefeated,
            CombatEventType.BattleEnded);
        Verify(run, ResolutionScenario.WallGrab);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-MOV-006")]
    [Trait("AcceptanceId", "WP09-OUT-007")]
    [Trait("AcceptanceId", "WP09-INT-004")]
    [Trait("AcceptanceId", "WP09-INT-005")]
    [Trait("AcceptanceId", "WP09-INT-006")]
    [Trait("AcceptanceId", "WP09-INT-007")]
    [Trait("AcceptanceId", "WP09-DET-001")]
    public void WP09_INT_004_StandardDiagnosticAndRepeatedRunsAreCanonicalIdentical()
    {
        Assert.Equal(
            File.ReadAllBytes(FixturePath("wait-equal-l1.engine-0.4.0.json")),
            CanonicalReplayArtifactWriter.Write(
                EngineShellFixture.RunCanonical().Journal,
                new ReplayArtifactMetadata(
                    new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
                    new ExternalId("combat-lab-wp06-target-probe"),
                    fixture: true,
                    notes: "Current-engine wait_equal_l1 determinism probe")));
        Assert.Equal(
            File.ReadAllBytes(FixturePath("decision-weighted-l1.engine-0.4.0.json")),
            CanonicalReplayArtifactWriter.Write(
                DecisionEngineFixture.Run().Journal,
                new ReplayArtifactMetadata(
                    new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                    new ExternalId("combat-lab-wp08-target-probe"),
                    fixture: true,
                    notes: "WP-08 decision_weighted_l1 target determinism probe")));

        var standard = Wp09ResolutionEngineFixture.Run(ResolutionScenario.Basic);
        var diagnostic = Wp09ResolutionEngineFixture.Run(
            ResolutionScenario.Basic,
            Battle.Contracts.Replay.JournalProfile.DiagnosticReplay);
        Assert.Equal(
            standard.Journal.Events.Select(item => item.EventDigest),
            diagnostic.Journal.Events.Select(item => item.EventDigest));
        Assert.Equal(standard.Result.Summary!.Outcome, diagnostic.Result.Summary!.Outcome);
        Assert.Equal(standard.Result.Summary.EndReason, diagnostic.Result.Summary.EndReason);
        Assert.Equal(standard.Result.Summary.EndTick, diagnostic.Result.Summary.EndTick);
        Assert.Equal(standard.Result.Summary.EventCount, diagnostic.Result.Summary.EventCount);
        Assert.Equal(standard.Result.FinalDigest, diagnostic.Result.FinalDigest);

        foreach (var scenario in Enum.GetValues<ResolutionScenario>())
        {
            var expected = File.ReadAllBytes(FixturePath(Slug(scenario) + ".engine-0.4.0.json"));
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var current = Wp09ResolutionEngineFixture.Run(scenario);
                Assert.Equal(expected, Wp09ResolutionEngineFixture.Write(scenario, current));
            }
        }
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-SAFE-008")]
    [Trait("AcceptanceId", "WP09-INT-008")]
    public void WP09_SAFE_008_CanonicalScenarioCompletesWithoutFallbackOrInvariantFailure()
    {
        var run = Wp09ResolutionEngineFixture.Run(ResolutionScenario.Basic);
        Assert.Null(run.Result.InvariantFailure);
        Assert.Empty(run.Result.RejectionErrors);
        Assert.True(run.Journal.IsCompleted);
    }

    private static void Verify(ResolutionRun run, ResolutionScenario scenario)
    {
        var verification = new ReplayVerifier(File.ReadAllBytes(Wp09ResolutionEngineFixture.SchemaPath()))
            .Verify(Wp09ResolutionEngineFixture.Write(scenario, run));
        Assert.True(verification.IsValid, string.Join(Environment.NewLine, verification.Issues.Select(issue =>
            issue.Code + " " + issue.Path + ": " + issue.Message)));
    }

    private static void AssertOrdered(ResolutionRun run, params CombatEventType[] expected)
    {
        var actual = run.Journal.Events.Select(item => item.Draft.EventType).ToArray();
        var cursor = -1;
        foreach (var eventType in expected)
        {
            cursor = Array.FindIndex(actual, cursor + 1, item => item == eventType);
            Assert.True(cursor >= 0, "Missing ordered event " + eventType + ". Trace: " + string.Join(", ", actual));
        }
    }

    private static string FixturePath(string name) => Path.Combine(
        Wp09ResolutionEngineFixture.Root(), "fixtures", "replay", "v0.1", name);

    private static string Slug(ResolutionScenario scenario) => scenario switch
    {
        ResolutionScenario.Basic => "resolution-basic-l1",
        ResolutionScenario.DoubleKo => "resolution-double-ko-l1",
        ResolutionScenario.WallGrab => "resolution-wall-grab-l1",
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };
}
