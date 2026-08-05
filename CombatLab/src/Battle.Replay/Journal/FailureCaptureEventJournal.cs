using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Replay.Journal;

/// <summary>
/// Keeps a bounded diagnostic tail of typed drafts. The captured tail is not a
/// complete replay and must never be presented as one.
/// </summary>
public sealed class FailureCaptureEventJournal : ICombatEventJournal
{
    private readonly JournalIntegrityChain _integrity;
    private readonly int _capacity;
    private readonly Queue<CombatEventDraft> _capturedDrafts;
    private readonly JournalSequenceGuard _guard = new();

    public FailureCaptureEventJournal(ExternalId replayId, int capacity)
    {
        if (capacity is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _integrity = new JournalIntegrityChain(replayId);
        _capacity = capacity;
        _capturedDrafts = new Queue<CombatEventDraft>(capacity);
    }

    public JournalProfile Profile => JournalProfile.FailureCapture;

    public bool PublishesReplay => false;

    public bool IsCompleted { get; private set; }

    public long EventCount => _guard.EventCount;

    public BattleSummary? Summary { get; private set; }

    public CombatJournalStart? Start => _integrity.Start;

    public Sha256Digest? InputDigest => _integrity.InputDigest;

    public Sha256Digest? FinalDigest { get; private set; }

    public IReadOnlyList<CombatEventDraft> CapturedDrafts =>
        new ReadOnlyCollection<CombatEventDraft>(_capturedDrafts.ToList());

    public JournalBeginResult Begin(in CombatJournalStart start)
    {
        if (IsCompleted || _guard.EventCount != 0)
        {
            throw new InvalidOperationException("Cannot begin a journal after event processing.");
        }

        return _integrity.Begin(start);
    }

    public CombatEventIdentity Append(in CombatEventDraft draft)
    {
        if (draft is null)
        {
            throw new ArgumentNullException(nameof(draft));
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot append after failure capture is complete.");
        }

        _integrity.ValidateDraft(draft, _guard.EventCount);
        _guard.ValidateAndAdvance(draft);
        _integrity.AppendValidated(draft);
        if (_capturedDrafts.Count == _capacity)
        {
            _capturedDrafts.Dequeue();
        }

        _capturedDrafts.Enqueue(draft);
        return new CombatEventIdentity(draft.EventId, draft.Sequence);
    }

    public JournalCompletion Complete(in BattleSummary summary)
    {
        if (summary is null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Failure capture is already complete.");
        }

        if (!_guard.HasBattleEnded || _guard.LastDraft?.Payload is not BattleEndedPayload ended)
        {
            throw new InvalidOperationException("Failure capture must end with BattleEnded before completion.");
        }

        if (summary.EventCount != _guard.EventCount || !SummariesEqual(ended.Summary, summary))
        {
            throw new InvalidOperationException(
                "Completed summary must equal BattleEnded and the counted event total.");
        }

        Summary = summary;
        FinalDigest = _integrity.Complete();
        IsCompleted = true;
        return new JournalCompletion(FinalDigest.Value, null);
    }

    private static bool SummariesEqual(BattleSummary left, BattleSummary right) =>
        EventDraftJsonWriter.WriteSummary(left, includeEventCount: true)
            .AsSpan()
            .SequenceEqual(EventDraftJsonWriter.WriteSummary(right, includeEventCount: true));
}
