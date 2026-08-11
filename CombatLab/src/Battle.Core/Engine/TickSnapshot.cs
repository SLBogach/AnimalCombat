using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.Engine;

internal sealed class TickSnapshot
{
    internal TickSnapshot(long identity, int tick, FighterFrame fighterA, FighterFrame fighterB)
    {
        Identity = identity;
        Tick = tick;
        FighterA = fighterA ?? throw new ArgumentNullException(nameof(fighterA));
        FighterB = fighterB ?? throw new ArgumentNullException(nameof(fighterB));
    }

    internal long Identity { get; }

    internal int Tick { get; }

    internal FighterFrame FighterA { get; }

    internal FighterFrame FighterB { get; }

    internal FighterFrame Get(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterA,
        FighterId.FighterB => FighterB,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    internal FighterFrame GetOpponent(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterB,
        FighterId.FighterB => FighterA,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };
}
