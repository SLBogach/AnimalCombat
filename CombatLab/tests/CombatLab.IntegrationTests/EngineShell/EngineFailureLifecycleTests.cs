using Battle.Contracts.Events;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;
using Battle.Core;
using Battle.Replay.Journal;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class EngineFailureLifecycleTests
{
    [Fact]
    public void WP06_FAIL_001_PostStartInvariantCompletesFailureCaptureButNotResult()
    {
        var battleCase = EngineShellFixture.CreateCase(maximumEvents: 4);
        var capture = new FailureCaptureEventJournal(EngineShellFixture.ReplayId, capacity: 16);
        var journal = new CompletionCountingJournal(capture);

        var result = new CombatEngine().Simulate(
            battleCase.Request,
            battleCase.Config,
            journal);

        Assert.Equal(BattleResultStatus.FailedInvariant, result.Status);
        Assert.Equal("EventCapExceeded", result.InvariantFailure!.Code.Value);
        Assert.Null(result.Summary);
        Assert.Null(result.FinalDigest);
        Assert.Null(result.ReplayId);
        Assert.Empty(result.Metrics);
        Assert.Empty(result.RejectionErrors);

        Assert.Equal(1, journal.BeginCount);
        Assert.Equal(1, journal.CompleteCount);
        Assert.True(capture.IsCompleted);
        Assert.False(capture.PublishesReplay);
        Assert.Equal(4, capture.EventCount);
        Assert.Equal(4, capture.CapturedDrafts.Count);
        var terminalDraft = capture.CapturedDrafts[^1];
        Assert.Equal(CombatEventType.BattleEnded, terminalDraft.EventType);
        var ended = Assert.IsType<BattleEndedPayload>(terminalDraft.Payload);
        Assert.Equal(BattleOutcome.Invalid, ended.Summary.Outcome);
        Assert.Null(ended.Summary.WinnerFighterId);
        Assert.Equal(BattleEndReason.BattleInvalid, ended.Summary.EndReason);
        Assert.Equal(4, ended.Summary.EventCount);
        Assert.Same(ended.Summary, capture.Summary);
    }

    private sealed class CompletionCountingJournal : ICombatEventJournal
    {
        private readonly ICombatEventJournal _inner;

        internal CompletionCountingJournal(ICombatEventJournal inner)
        {
            _inner = inner;
        }

        internal int BeginCount { get; private set; }

        internal int CompleteCount { get; private set; }

        public JournalBeginResult Begin(in CombatJournalStart start)
        {
            BeginCount++;
            return _inner.Begin(in start);
        }

        public CombatEventIdentity Append(in CombatEventDraft draft) =>
            _inner.Append(in draft);

        public JournalCompletion Complete(in BattleSummary summary)
        {
            CompleteCount++;
            return _inner.Complete(in summary);
        }
    }
}
