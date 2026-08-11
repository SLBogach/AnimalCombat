using System.Globalization;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Replay.Journal;

internal sealed class JournalSequenceGuard
{
    private ArtifactVersion? _schemaVersion;
    private ArtifactVersion? _engineVersion;
    private Sha256Digest? _configHash;
    private ExternalId? _battleId;
    private int _lastTick;

    public long EventCount { get; private set; }

    public bool HasBattleEnded { get; private set; }

    public CombatEventDraft? LastDraft { get; private set; }

    public void ValidateAndAdvance(CombatEventDraft draft)
    {
        if (HasBattleEnded)
        {
            throw new InvalidOperationException("No canonical events may follow BattleEnded.");
        }

        if (draft.Sequence != EventCount || draft.EventId != EventId.FromSequence(EventCount))
        {
            throw new InvalidOperationException(
                $"Expected sequence/event_id {EventCount.ToString(CultureInfo.InvariantCulture)}/" +
                $"'{EventId.FromSequence(EventCount)}'.");
        }

        if (draft.SchemaVersion != ContractVersions.Event)
        {
            throw new InvalidOperationException(
                $"Unsupported event schema version '{draft.SchemaVersion}'.");
        }

        if (EventCount == 0)
        {
            if (draft.EventType != CombatEventType.BattleStarted || draft.Tick != 0)
            {
                throw new InvalidOperationException(
                    "The first canonical event must be BattleStarted at tick 0.");
            }
        }
        else
        {
            if (draft.EventType == CombatEventType.BattleStarted)
            {
                throw new InvalidOperationException("BattleStarted may occur only at sequence 0.");
            }

            if (draft.Tick < _lastTick)
            {
                throw new InvalidOperationException("Canonical event ticks must be nondecreasing.");
            }

            if (draft.SchemaVersion != _schemaVersion ||
                draft.EngineVersion != _engineVersion ||
                draft.ConfigHash != _configHash ||
                draft.BattleId != _battleId)
            {
                throw new InvalidOperationException(
                    "Event schema, engine, config hash and battle identity must remain constant.");
            }
        }

        ValidateEarlierReference(draft.SourceEventId, EventCount, "source_event_id");
        EventId? previous = null;
        foreach (var related in draft.Payload.RelatedEventIds)
        {
            ValidateEarlierReference(related, EventCount, "related_event_ids");
            if (previous.HasValue && previous.Value.CompareTo(related) >= 0)
            {
                throw new InvalidOperationException(
                    "related_event_ids must be strictly sorted by ordinal event ID.");
            }

            previous = related;
        }

        if (draft.Payload is BattleEndedPayload ended &&
            (ended.Summary.EventCount != EventCount + 1 || ended.Summary.EndTick != draft.Tick))
        {
            throw new InvalidOperationException(
                "BattleEnded summary event_count/end_tick must match the event identity.");
        }

        if (EventCount == 0)
        {
            _schemaVersion = draft.SchemaVersion;
            _engineVersion = draft.EngineVersion;
            _configHash = draft.ConfigHash;
            _battleId = draft.BattleId;
        }

        _lastTick = draft.Tick;
        EventCount++;
        HasBattleEnded = draft.EventType == CombatEventType.BattleEnded;
        LastDraft = draft;
    }

    private static void ValidateEarlierReference(
        EventId? eventId,
        long eventCount,
        string fieldName)
    {
        if (!eventId.HasValue)
        {
            return;
        }

        var value = eventId.Value.Value;
        if (!long.TryParse(
                value.Substring(4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var sequence) ||
            sequence >= eventCount)
        {
            throw new InvalidOperationException($"{fieldName} must reference an earlier event.");
        }
    }
}
