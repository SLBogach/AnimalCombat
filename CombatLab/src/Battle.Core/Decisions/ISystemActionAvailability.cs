using Battle.Core.Engine;
using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal interface ISystemActionAvailability
{
    IReadOnlyList<SystemActionCandidate> GetLegalCandidates(
        BattleState state,
        TickSnapshot snapshot,
        FighterId actorId,
        SystemActionDefinition systemWait);
}

internal sealed class Wp06SystemActionAvailability : ISystemActionAvailability
{
    internal static Wp06SystemActionAvailability Instance { get; } = new();

    private Wp06SystemActionAvailability()
    {
    }

    public IReadOnlyList<SystemActionCandidate> GetLegalCandidates(
        BattleState state,
        TickSnapshot snapshot,
        FighterId actorId,
        SystemActionDefinition systemWait) =>
        new[] { new SystemActionCandidate(systemWait.Id, systemWait.Weight) };
}
