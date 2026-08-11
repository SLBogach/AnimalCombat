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
    private readonly ReadOnlyCollection<FighterFrame> _initialFrames;
    private readonly ReadOnlyCollection<FighterId> _initiativeOrder;

    public BattleStartedPayload(
        IEnumerable<EventId> relatedEventIds,
        Sha256Digest inputDigest,
        IEnumerable<FighterFrame> initialFrames,
        IEnumerable<FighterId> initiativeOrder,
        InitiativeTieBreak initiativeTieBreak)
        : base(relatedEventIds)
    {
        if (initialFrames is null)
        {
            throw new ArgumentNullException(nameof(initialFrames));
        }

        if (initiativeOrder is null)
        {
            throw new ArgumentNullException(nameof(initiativeOrder));
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

        InputDigest = inputDigest;
        _initialFrames = new ReadOnlyCollection<FighterFrame>(frames);
        _initiativeOrder = new ReadOnlyCollection<FighterId>(initiative);
        InitiativeTieBreak = initiativeTieBreak;
    }

    public override CombatEventType EventType => CombatEventType.BattleStarted;

    public Sha256Digest InputDigest { get; }

    public IReadOnlyList<FighterFrame> InitialFrames => _initialFrames;

    public IReadOnlyList<FighterId> InitiativeOrder => _initiativeOrder;

    public InitiativeTieBreak InitiativeTieBreak { get; }

    private static bool IsKnown(FighterId fighterId) =>
        fighterId is FighterId.FighterA or FighterId.FighterB;
}
