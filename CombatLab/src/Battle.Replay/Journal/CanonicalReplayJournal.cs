using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using IntegrityCalculator = Battle.Replay.Integrity.ReplayIntegrity;

namespace Battle.Replay.Journal;

/// <summary>
/// Per-battle typed canonical event journal. The simulation owns tick and event
/// identity; the journal verifies them, builds the digest chain, and freezes the
/// resulting canonical bytes.
/// </summary>
public sealed class CanonicalReplayJournal : ICombatEventJournal
{
    private readonly Sha256Digest _inputDigest;
    private readonly List<JournaledCombatEvent> _events = new();
    private ArtifactVersion? _schemaVersion;
    private ArtifactVersion? _engineVersion;
    private Sha256Digest? _configHash;
    private ExternalId? _battleId;
    private bool _battleEnded;

    public CanonicalReplayJournal(Sha256Digest inputDigest)
        : this(inputDigest, JournalProfile.StandardReplay)
    {
    }

    public CanonicalReplayJournal(Sha256Digest inputDigest, JournalProfile profile)
    {
        if (profile is not JournalProfile.StandardReplay and not JournalProfile.DiagnosticReplay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                "Canonical replay journal supports StandardReplay and DiagnosticReplay profiles only.");
        }

        _inputDigest = inputDigest;
        Profile = profile;
    }

    public JournalProfile Profile { get; }

    public IReadOnlyList<JournaledCombatEvent> Events =>
        new ReadOnlyCollection<JournaledCombatEvent>(_events.ToList());

    public BattleSummary? Summary { get; private set; }

    public Sha256Digest? FinalDigest { get; private set; }

    public bool IsCompleted { get; private set; }

    public CombatEventIdentity Append(in CombatEventDraft draft)
    {
        if (draft is null)
        {
            throw new ArgumentNullException(nameof(draft));
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Cannot append after the replay journal is complete.");
        }

        if (_battleEnded)
        {
            throw new InvalidOperationException("No canonical events may follow BattleEnded.");
        }

        ValidateIdentityAndOrder(draft);
        ValidateRolesAndFrames(draft);
        ValidateCausality(draft);
        ValidateLifecyclePayload(draft);

        var previousDigest = _events.Count == 0
            ? _inputDigest
            : _events[^1].EventDigest;
        var projectionJson = EventDraftJsonWriter.Write(draft, previousDigest, null);
        var eventDigest = IntegrityCalculator.ComputeEventDigest(projectionJson);
        var canonicalJson = EventDraftJsonWriter.Write(draft, previousDigest, eventDigest);
        var journaled = new JournaledCombatEvent(
            draft,
            previousDigest,
            eventDigest,
            canonicalJson);

        if (_events.Count == 0)
        {
            _schemaVersion = draft.SchemaVersion;
            _engineVersion = draft.EngineVersion;
            _configHash = draft.ConfigHash;
            _battleId = draft.BattleId;
        }

        _events.Add(journaled);
        _battleEnded = draft.EventType == CombatEventType.BattleEnded;
        return journaled.Identity;
    }

    public void Complete(in BattleSummary summary)
    {
        if (summary is null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        if (IsCompleted)
        {
            throw new InvalidOperationException("Replay journal is already complete.");
        }

        if (_events.Count == 0 || !_battleEnded)
        {
            throw new InvalidOperationException("Replay journal must end with BattleEnded before completion.");
        }

        if (_events.Count != summary.EventCount)
        {
            throw new InvalidOperationException(
                "BattleSummary.EventCount must equal the number of canonical events.");
        }

        var finalEvent = _events[^1];
        if (finalEvent.Draft.Tick != summary.EndTick)
        {
            throw new InvalidOperationException("BattleSummary.EndTick must equal the BattleEnded tick.");
        }

        var ended = finalEvent.Draft.Payload as BattleEndedPayload
            ?? throw new InvalidOperationException("BattleEnded must use BattleEndedPayload.");
        if (!SummariesEqual(ended.Summary, summary))
        {
            throw new InvalidOperationException(
                "The completed summary must equal the summary carried by BattleEnded.");
        }

        var eventIds = new HashSet<EventId>(_events.Select(item => item.Draft.EventId));
        if (summary.PivotalEventIds.Any(eventId => !eventIds.Contains(eventId)))
        {
            throw new InvalidOperationException(
                "Every pivotal event ID must exist in the canonical event log.");
        }

        ValidateFinisherPredictions();

        Summary = summary;
        FinalDigest = finalEvent.EventDigest;
        IsCompleted = true;
    }

    private void ValidateIdentityAndOrder(CombatEventDraft draft)
    {
        var expectedSequence = _events.Count;
        if (draft.Sequence != expectedSequence || draft.EventId != EventId.FromSequence(expectedSequence))
        {
            throw new InvalidOperationException(
                $"Expected sequence/event_id {expectedSequence}/'{EventId.FromSequence(expectedSequence)}'.");
        }

        if (draft.SchemaVersion != ContractVersions.Event)
        {
            throw new InvalidOperationException(
                $"Unsupported event schema version '{draft.SchemaVersion}'.");
        }

        if (_events.Count == 0)
        {
            if (draft.EventType != CombatEventType.BattleStarted || draft.Tick != 0)
            {
                throw new InvalidOperationException(
                    "The first canonical event must be BattleStarted at tick 0.");
            }

            return;
        }

        if (draft.EventType == CombatEventType.BattleStarted)
        {
            throw new InvalidOperationException("BattleStarted may occur only once as sequence 0.");
        }

        if (draft.Tick < _events[^1].Draft.Tick)
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

    private void ValidateCausality(CombatEventDraft draft)
    {
        var earlier = new HashSet<EventId>(_events.Select(item => item.Draft.EventId));
        if (draft.SourceEventId.HasValue && !earlier.Contains(draft.SourceEventId.Value))
        {
            throw new InvalidOperationException("source_event_id must reference an earlier event.");
        }

        EventId? previous = null;
        foreach (var relatedEventId in draft.Payload.RelatedEventIds)
        {
            if (!earlier.Contains(relatedEventId))
            {
                throw new InvalidOperationException(
                    "related_event_ids may contain only earlier events.");
            }

            if (previous.HasValue && previous.Value.CompareTo(relatedEventId) >= 0)
            {
                throw new InvalidOperationException(
                    "related_event_ids must be strictly sorted by ordinal event ID.");
            }

            previous = relatedEventId;
        }

        if (draft.Payload is FighterDefeatedPayload defeated &&
            defeated.LethalSourceEventId.HasValue &&
            !earlier.Contains(defeated.LethalSourceEventId.Value))
        {
            throw new InvalidOperationException(
                "lethal_source_event_id must reference an earlier event.");
        }
    }

    private void ValidateLifecyclePayload(CombatEventDraft draft)
    {
        if (draft.EventType == CombatEventType.BattleStarted)
        {
            var started = draft.Payload as BattleStartedPayload
                ?? throw new InvalidOperationException("BattleStarted must use BattleStartedPayload.");
            if (started.InputDigest != _inputDigest)
            {
                throw new InvalidOperationException(
                    "BattleStarted input digest must equal the journal input digest.");
            }
        }

        if (draft.EventType == CombatEventType.BattleEnded)
        {
            if (_events.Count == 0)
            {
                throw new InvalidOperationException("BattleEnded cannot be the first canonical event.");
            }

            var ended = draft.Payload as BattleEndedPayload
                ?? throw new InvalidOperationException("BattleEnded must use BattleEndedPayload.");
            if (ended.Summary.EventCount != draft.Sequence + 1 ||
                ended.Summary.EndTick != draft.Tick)
            {
                throw new InvalidOperationException(
                    "BattleEnded summary event_count/end_tick must match the event identity.");
            }
        }
    }

    private static void ValidateRolesAndFrames(CombatEventDraft draft)
    {
        var role = GetRoleRule(draft.EventType);
        var roleIsValid = role switch
        {
            EventRoleRule.None => !draft.ActorId.HasValue && !draft.TargetId.HasValue,
            EventRoleRule.ActorOnly => draft.ActorId.HasValue && !draft.TargetId.HasValue,
            EventRoleRule.ActorAndTarget =>
                draft.ActorId.HasValue &&
                draft.TargetId.HasValue &&
                draft.ActorId.Value != draft.TargetId.Value,
            EventRoleRule.ActorWithOptionalTarget =>
                draft.ActorId.HasValue &&
                (!draft.TargetId.HasValue || draft.ActorId.Value != draft.TargetId.Value),
            _ => false,
        };
        if (!roleIsValid)
        {
            throw new InvalidOperationException(
                $"Actor/target roles are invalid for event type '{draft.EventType}'.");
        }

        ValidateFramePair(draft.Before, draft.ActorId, draft.TargetId);
        ValidateFramePair(draft.After, draft.ActorId, draft.TargetId);

        if (draft.Payload is FighterDefeatedPayload defeated &&
            draft.ActorId != defeated.DefeatedFighterId)
        {
            throw new InvalidOperationException(
                "FighterDefeated actor must equal payload.defeated_fighter_id.");
        }
    }

    private static void ValidateFramePair(
        FramePair pair,
        FighterId? actorId,
        FighterId? targetId)
    {
        if (!FrameMatches(pair.Actor, actorId) || !FrameMatches(pair.Target, targetId))
        {
            throw new InvalidOperationException(
                "Frame nullability and fighter IDs must agree with actor/target roles.");
        }
    }

    private static bool FrameMatches(FighterFrame? frame, FighterId? fighterId) =>
        fighterId.HasValue
            ? frame is not null && frame.FighterId == fighterId.Value
            : frame is null;

    private void ValidateFinisherPredictions()
    {
        var byId = _events.ToDictionary(item => item.Draft.EventId);
        foreach (var marker in _events.Where(
                     item => item.Draft.Payload is FinisherTriggeredPayload))
        {
            var payload = (FinisherTriggeredPayload)marker.Draft.Payload;
            if (!byId.TryGetValue(payload.PredictedLethalEventId, out var predicted) ||
                predicted.Draft.Sequence <= marker.Draft.Sequence ||
                marker.Draft.ResolutionGroupId is null ||
                marker.Draft.ResolutionGroupId != predicted.Draft.ResolutionGroupId ||
                !IsLethal(predicted.Draft.Payload))
            {
                throw new InvalidOperationException(
                    "Finisher prediction must resolve to a later lethal event in the same resolution group.");
            }
        }
    }

    private static bool IsLethal(CombatEventPayload payload) => payload switch
    {
        DamageAppliedPayload damage => damage.Lethal,
        FighterDefeatedPayload => true,
        _ => false,
    };

    private static bool SummariesEqual(BattleSummary left, BattleSummary right) =>
        EventDraftJsonWriter.WriteSummary(left, includeEventCount: true)
            .AsSpan()
            .SequenceEqual(EventDraftJsonWriter.WriteSummary(right, includeEventCount: true));

    private static EventRoleRule GetRoleRule(CombatEventType eventType) => eventType switch
    {
        CombatEventType.BattleStarted or CombatEventType.TimeoutReached or
            CombatEventType.DrawDeclared or CombatEventType.BattleEnded => EventRoleRule.None,
        CombatEventType.FighterDefeated or CombatEventType.MoveStarted or
            CombatEventType.PositionChanged or CombatEventType.MoveEnded or
            CombatEventType.ResourceChanged or CombatEventType.StateChanged => EventRoleRule.ActorOnly,
        CombatEventType.DecisionMade or CombatEventType.KnockbackApplied or
            CombatEventType.WallImpact or CombatEventType.ConflictResolved or
            CombatEventType.AttackHit or CombatEventType.Blocked or CombatEventType.Dodged or
            CombatEventType.Countered or CombatEventType.DamageApplied or
            CombatEventType.GrabStarted or CombatEventType.GrabEnded or
            CombatEventType.FinisherTriggered => EventRoleRule.ActorAndTarget,
        CombatEventType.ActionCommitted or CombatEventType.AttackPrepared or
            CombatEventType.ActionPhaseChanged or CombatEventType.ActionCancelled or
            CombatEventType.AttackMissed or CombatEventType.EffectAdded or
            CombatEventType.EffectRemoved => EventRoleRule.ActorWithOptionalTarget,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
    };

    private enum EventRoleRule
    {
        None,
        ActorOnly,
        ActorAndTarget,
        ActorWithOptionalTarget,
    }
}
