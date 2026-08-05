using Battle.Contracts.Events;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class EngineShellDeterminismTests
{
    [Fact]
    public void WaitEqualL1_IsByteDeterministicAcrossOneHundredRuns()
    {
        var baseline = EngineShellFixture.RunCanonical();
        var baselineBodies = baseline.Journal.Events
            .Select(item => item.CanonicalJson.ToArray())
            .ToArray();

        for (var repetition = 1; repetition < 100; repetition++)
        {
            var current = EngineShellFixture.RunCanonical();

            Assert.Equal(BattleResultStatus.Completed, current.Result.Status);
            Assert.Equal(baseline.Journal.InputDigest, current.Journal.InputDigest);
            Assert.Equal(baseline.Journal.FinalDigest, current.Journal.FinalDigest);
            Assert.Equal(baseline.Result.FinalDigest, current.Result.FinalDigest);
            AssertSummaryEquivalent(baseline.Result.Summary!, current.Result.Summary!);
            Assert.Equal(baselineBodies.Length, current.Journal.Events.Count);
            for (var eventIndex = 0; eventIndex < baselineBodies.Length; eventIndex++)
            {
                Assert.Equal(
                    baselineBodies[eventIndex],
                    current.Journal.Events[eventIndex].CanonicalJson.ToArray());
                Assert.Null(current.Journal.Events[eventIndex].Draft.Rng);
            }
        }
    }

    [Fact]
    public void JournalProfiles_ShareInputAndFinalDigestForOneReplayIdentity()
    {
        var standard = EngineShellFixture.RunCanonical(JournalProfile.StandardReplay);
        var diagnostic = EngineShellFixture.RunCanonical(JournalProfile.DiagnosticReplay);
        var summaryOnly = EngineShellFixture.RunSummaryOnly();

        Assert.Equal(BattleResultStatus.Completed, standard.Result.Status);
        Assert.Equal(BattleResultStatus.Completed, diagnostic.Result.Status);
        Assert.Equal(BattleResultStatus.Completed, summaryOnly.Result.Status);
        Assert.Equal(standard.Journal.InputDigest, diagnostic.Journal.InputDigest);
        Assert.Equal(standard.Journal.InputDigest, summaryOnly.Journal.InputDigest);
        Assert.Equal(standard.Journal.FinalDigest, diagnostic.Journal.FinalDigest);
        Assert.Equal(standard.Journal.FinalDigest, summaryOnly.Journal.FinalDigest);
        Assert.Equal(standard.Result.FinalDigest, diagnostic.Result.FinalDigest);
        Assert.Equal(standard.Result.FinalDigest, summaryOnly.Result.FinalDigest);
        Assert.Equal(EngineShellFixture.ReplayId, standard.Result.ReplayId);
        Assert.Equal(EngineShellFixture.ReplayId, diagnostic.Result.ReplayId);
        Assert.Null(summaryOnly.Result.ReplayId);
        Assert.True(summaryOnly.Journal.IsCompleted);
        Assert.Equal(8, summaryOnly.Journal.EventCount);
        Assert.Equal(1, summaryOnly.Journal.EventTypeCounts[CombatEventType.BattleStarted]);
        Assert.Equal(2, summaryOnly.Journal.EventTypeCounts[CombatEventType.DecisionMade]);
        Assert.Equal(2, summaryOnly.Journal.EventTypeCounts[CombatEventType.ActionCommitted]);
        Assert.Equal(1, summaryOnly.Journal.EventTypeCounts[CombatEventType.TimeoutReached]);
        Assert.Equal(1, summaryOnly.Journal.EventTypeCounts[CombatEventType.DrawDeclared]);
        Assert.Equal(1, summaryOnly.Journal.EventTypeCounts[CombatEventType.BattleEnded]);
        Assert.All(summaryOnly.Journal.RngDrawCounts.Values, count => Assert.Equal(0, count));
        AssertSummaryEquivalent(standard.Result.Summary!, diagnostic.Result.Summary!);
        AssertSummaryEquivalent(standard.Result.Summary!, summaryOnly.Result.Summary!);

        Assert.Equal(standard.Journal.Events.Count, diagnostic.Journal.Events.Count);
        for (var index = 0; index < standard.Journal.Events.Count; index++)
        {
            Assert.Equal(
                standard.Journal.Events[index].CanonicalJson.ToArray(),
                diagnostic.Journal.Events[index].CanonicalJson.ToArray());
        }
    }

    private static void AssertSummaryEquivalent(BattleSummary expected, BattleSummary actual)
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
            var expectedFrame = expected.FinalFrames[index];
            var actualFrame = actual.FinalFrames[index];
            Assert.Equal(expectedFrame.FighterId, actualFrame.FighterId);
            Assert.Equal(expectedFrame.Position, actualFrame.Position);
            Assert.Equal(expectedFrame.Facing, actualFrame.Facing);
            Assert.Equal(expectedFrame.State, actualFrame.State);
            Assert.Equal(expectedFrame.StateTicksRemaining, actualFrame.StateTicksRemaining);
            Assert.Equal(expectedFrame.ActionId, actualFrame.ActionId);
            Assert.Equal(expectedFrame.ActionPhase, actualFrame.ActionPhase);
            Assert.Equal(expectedFrame.Health, actualFrame.Health);
            Assert.Equal(expectedFrame.MaxHealth, actualFrame.MaxHealth);
            Assert.Equal(expectedFrame.Energy, actualFrame.Energy);
            Assert.Equal(expectedFrame.MaxEnergy, actualFrame.MaxEnergy);
            Assert.Equal(expectedFrame.UniqueResource, actualFrame.UniqueResource);
            Assert.Equal(expectedFrame.Stagger, actualFrame.Stagger);
            Assert.Equal(expectedFrame.StaggerThreshold, actualFrame.StaggerThreshold);
            Assert.Equal(expectedFrame.Effects, actualFrame.Effects);
        }
    }
}
