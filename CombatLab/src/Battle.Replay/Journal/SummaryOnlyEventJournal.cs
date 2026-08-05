using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace Battle.Replay.Journal;

/// <summary>
/// Counts the same typed drafts consumed by replay journals while discarding
/// event bodies. It never publishes a canonical replay artifact.
/// </summary>
public sealed class SummaryOnlyEventJournal : ICombatEventJournal
{
    private readonly JournalSequenceGuard _guard = new();
    private readonly long[] _eventTypeCounts = new long[Enum.GetValues(typeof(CombatEventType)).Length];
    private readonly long[] _rngDrawCounts = new long[Enum.GetValues(typeof(RngStream)).Length];

    public JournalProfile Profile => JournalProfile.SummaryOnly;

    public bool PublishesReplay => false;

    public long EventCount => _guard.EventCount;

    public bool IsCompleted { get; private set; }

    public BattleSummary? Summary { get; private set; }

    public IReadOnlyDictionary<CombatEventType, long> EventTypeCounts =>
        new ReadOnlyDictionary<CombatEventType, long>(
            Enum.GetValues(typeof(CombatEventType))
                .Cast<CombatEventType>()
                .ToDictionary(eventType => eventType, eventType => _eventTypeCounts[(int)eventType]));

    public IReadOnlyDictionary<RngStream, long> RngDrawCounts =>
        new ReadOnlyDictionary<RngStream, long>(
            Enum.GetValues(typeof(RngStream))
                .Cast<RngStream>()
                .ToDictionary(stream => stream, stream => _rngDrawCounts[(int)stream]));

    public CombatEventIdentity Append(in CombatEventDraft draft)
    {
        if (draft is null)
        {
            throw new ArgumentNullException(nameof(draft));
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot append after the summary journal is complete.");
        }

        _guard.ValidateAndAdvance(draft);
        _eventTypeCounts[(int)draft.EventType]++;
        if (draft.Rng.HasValue)
        {
            _rngDrawCounts[(int)draft.Rng.Value.Stream]++;
        }

        return new CombatEventIdentity(draft.EventId, draft.Sequence);
    }

    public void Complete(in BattleSummary summary)
    {
        if (summary is null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Summary journal is already complete.");
        }

        if (!_guard.HasBattleEnded || _guard.LastDraft?.Payload is not BattleEndedPayload ended)
        {
            throw new InvalidOperationException("Summary journal must end with BattleEnded.");
        }

        if (summary.EventCount != _guard.EventCount || !SummariesEqual(ended.Summary, summary))
        {
            throw new InvalidOperationException(
                "Completed summary must equal BattleEnded and the counted event total.");
        }

        Summary = summary;
        IsCompleted = true;
    }

    private static bool SummariesEqual(BattleSummary left, BattleSummary right) =>
        EventDraftJsonWriter.WriteSummary(left, includeEventCount: true)
            .AsSpan()
            .SequenceEqual(EventDraftJsonWriter.WriteSummary(right, includeEventCount: true));
}
