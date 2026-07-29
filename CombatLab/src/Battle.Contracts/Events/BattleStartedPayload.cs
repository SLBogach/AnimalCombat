using System.Collections.ObjectModel;
using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Events;

public enum InitiativeTieBreak
{
    StatThenSeededHash,
    SeededHash,
}

public sealed class BattleStartedPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<EventId> _relatedEventIds;
    private readonly ReadOnlyCollection<FighterFrame> _initialFrames;
    private readonly ReadOnlyCollection<FighterId> _initiativeOrder;

    public BattleStartedPayload(
        IEnumerable<EventId> relatedEventIds,
        Sha256Digest inputDigest,
        IEnumerable<FighterFrame> initialFrames,
        IEnumerable<FighterId> initiativeOrder,
        InitiativeTieBreak initiativeTieBreak)
    {
        if (relatedEventIds is null)
        {
            throw new ArgumentNullException(nameof(relatedEventIds));
        }

        if (initialFrames is null)
        {
            throw new ArgumentNullException(nameof(initialFrames));
        }

        if (initiativeOrder is null)
        {
            throw new ArgumentNullException(nameof(initiativeOrder));
        }

        var relatedIds = new List<EventId>(relatedEventIds);
        if (relatedIds.Count > 32 || HasDuplicates(relatedIds))
        {
            throw new ArgumentException(
                "Related event IDs must be unique and contain at most 32 entries.",
                nameof(relatedEventIds));
        }

        var frames = new List<FighterFrame>(initialFrames);
        if (frames.Count != 2 ||
            frames[0].FighterId != FighterId.FighterA ||
            frames[1].FighterId != FighterId.FighterB)
        {
            throw new ArgumentException(
                "Initial frames must contain fighter A followed by fighter B.",
                nameof(initialFrames));
        }

        var initiative = new List<FighterId>(initiativeOrder);
        if (initiative.Count != 2 ||
            !IsKnown(initiative[0]) ||
            !IsKnown(initiative[1]) ||
            initiative[0] == initiative[1])
        {
            throw new ArgumentException(
                "Initiative order must contain both fighters exactly once.",
                nameof(initiativeOrder));
        }

        if (initiativeTieBreak is not InitiativeTieBreak.StatThenSeededHash and not InitiativeTieBreak.SeededHash)
        {
            throw new ArgumentOutOfRangeException(nameof(initiativeTieBreak));
        }

        _relatedEventIds = new ReadOnlyCollection<EventId>(relatedIds);
        InputDigest = inputDigest;
        _initialFrames = new ReadOnlyCollection<FighterFrame>(frames);
        _initiativeOrder = new ReadOnlyCollection<FighterId>(initiative);
        InitiativeTieBreak = initiativeTieBreak;
    }

    public override CombatEventType EventType => CombatEventType.BattleStarted;

    public IReadOnlyList<EventId> RelatedEventIds => _relatedEventIds;

    public Sha256Digest InputDigest { get; }

    public IReadOnlyList<FighterFrame> InitialFrames => _initialFrames;

    public IReadOnlyList<FighterId> InitiativeOrder => _initiativeOrder;

    public InitiativeTieBreak InitiativeTieBreak { get; }

    private static bool HasDuplicates(IReadOnlyList<EventId> values)
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

    private static bool IsKnown(FighterId fighterId) =>
        fighterId is FighterId.FighterA or FighterId.FighterB;
}
