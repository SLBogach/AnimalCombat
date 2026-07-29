using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Requests;

public sealed class FighterBuildSnapshot
{
    private readonly ReadOnlyCollection<StableId> _specialActionIds;

    public FighterBuildSnapshot(
        FighterId fighterId,
        FighterSide side,
        StableId animalId,
        StableId? buildId,
        IEnumerable<StableId> specialActionIds,
        StableId passiveId,
        GearSelection gear,
        StableId tacticId)
    {
        if (specialActionIds is null)
        {
            throw new ArgumentNullException(nameof(specialActionIds));
        }

        if (fighterId is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterId));
        }

        if (side is not FighterSide.A and not FighterSide.B)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if ((fighterId == FighterId.FighterA) != (side == FighterSide.A))
        {
            throw new ArgumentException("Fighter ID and side must describe the same slot.", nameof(side));
        }

        var specials = new List<StableId>(specialActionIds);
        if (specials.Count != 2)
        {
            throw new ArgumentException("A fighter build must select exactly two special actions.", nameof(specialActionIds));
        }

        if (specials[0] == specials[1])
        {
            throw new ArgumentException("The two special actions must be distinct.", nameof(specialActionIds));
        }

        FighterId = fighterId;
        Side = side;
        AnimalId = animalId;
        BuildId = buildId;
        _specialActionIds = new ReadOnlyCollection<StableId>(specials);
        PassiveId = passiveId;
        Gear = gear;
        TacticId = tacticId;
    }

    public FighterId FighterId { get; }

    public FighterSide Side { get; }

    public StableId AnimalId { get; }

    public StableId? BuildId { get; }

    public IReadOnlyList<StableId> SpecialActionIds => _specialActionIds;

    public StableId PassiveId { get; }

    public GearSelection Gear { get; }

    public StableId TacticId { get; }
}
