using Battle.Contracts.Ids;

namespace Battle.Core.Engine;

internal interface ITickCoordinatorObserver
{
    void OnPhase(BattleState state, TickPhase phase);

    void OnDecisionSnapshot(FighterId fighterId, TickSnapshot snapshot);
}

internal sealed class NullTickCoordinatorObserver : ITickCoordinatorObserver
{
    internal static NullTickCoordinatorObserver Instance { get; } = new();

    private NullTickCoordinatorObserver()
    {
    }

    public void OnPhase(BattleState state, TickPhase phase)
    {
    }

    public void OnDecisionSnapshot(FighterId fighterId, TickSnapshot snapshot)
    {
    }
}
