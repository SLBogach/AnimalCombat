using System.Collections.ObjectModel;
using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Events;

public sealed class CombatEventDraft
{
    private readonly ReadOnlyCollection<ReasonCode> _reasonCodes;

    public CombatEventDraft(
        ArtifactVersion schemaVersion,
        ArtifactVersion engineVersion,
        Sha256Digest configHash,
        ExternalId battleId,
        int tick,
        long sequence,
        EventId eventId,
        EventId? sourceEventId,
        FighterId? actorId,
        FighterId? targetId,
        StableId? actionId,
        StableId? effectId,
        DecisionId? decisionId,
        ExternalId? resolutionGroupId,
        IEnumerable<ReasonCode> reasonCodes,
        RngProvenance? rng,
        FramePair before,
        FramePair after,
        CombatEventPayload payload)
    {
        if (reasonCodes is null)
        {
            throw new ArgumentNullException(nameof(reasonCodes));
        }

        if (before is null)
        {
            throw new ArgumentNullException(nameof(before));
        }

        if (after is null)
        {
            throw new ArgumentNullException(nameof(after));
        }

        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        if (sequence is < 0 or > EventId.MaximumSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (eventId != Battle.Contracts.Ids.EventId.FromSequence(sequence))
        {
            throw new ArgumentException("Event ID must be derived from sequence.", nameof(eventId));
        }

        if (actorId.HasValue &&
            actorId.Value is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(actorId));
        }

        if (targetId.HasValue &&
            targetId.Value is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(targetId));
        }

        var reasonCodeList = new List<ReasonCode>(reasonCodes);
        if (reasonCodeList.Count > 8 || HasDuplicates(reasonCodeList))
        {
            throw new ArgumentException(
                "Reason codes must be unique and contain at most eight entries.",
                nameof(reasonCodes));
        }

        SchemaVersion = schemaVersion;
        EngineVersion = engineVersion;
        ConfigHash = configHash;
        BattleId = battleId;
        Tick = tick;
        Sequence = sequence;
        EventId = eventId;
        SourceEventId = sourceEventId;
        ActorId = actorId;
        TargetId = targetId;
        ActionId = actionId;
        EffectId = effectId;
        DecisionId = decisionId;
        ResolutionGroupId = resolutionGroupId;
        _reasonCodes = new ReadOnlyCollection<ReasonCode>(reasonCodeList);
        Rng = rng;
        Before = before;
        After = after;
        Payload = payload;
    }

    public ArtifactVersion SchemaVersion { get; }

    public ArtifactVersion EngineVersion { get; }

    public Sha256Digest ConfigHash { get; }

    public ExternalId BattleId { get; }

    public int Tick { get; }

    public long Sequence { get; }

    public EventId EventId { get; }

    public EventId? SourceEventId { get; }

    public CombatEventType EventType => Payload.EventType;

    public FighterId? ActorId { get; }

    public FighterId? TargetId { get; }

    public StableId? ActionId { get; }

    public StableId? EffectId { get; }

    public DecisionId? DecisionId { get; }

    public ExternalId? ResolutionGroupId { get; }

    public IReadOnlyList<ReasonCode> ReasonCodes => _reasonCodes;

    public RngProvenance? Rng { get; }

    public FramePair Before { get; }

    public FramePair After { get; }

    public CombatEventPayload Payload { get; }

    private static bool HasDuplicates(IReadOnlyList<ReasonCode> values)
    {
        for (var left = 0; left < values.Count; left++)
        {
            for (var right = left + 1; right < values.Count; right++)
            {
                if (values[left] == values[right])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
