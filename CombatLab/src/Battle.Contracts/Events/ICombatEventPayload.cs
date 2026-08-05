using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public interface ICombatEventPayload
{
    CombatEventType EventType { get; }
}

public interface IRelatedCombatEventPayload : ICombatEventPayload
{
    IReadOnlyList<EventId> RelatedEventIds { get; }
}

public abstract class CombatEventPayload : IRelatedCombatEventPayload
{
    private readonly ReadOnlyCollection<EventId> _relatedEventIds;

    internal CombatEventPayload(IEnumerable<EventId> relatedEventIds)
    {
        _relatedEventIds = PayloadContract.Copy(
            relatedEventIds,
            0,
            32,
            nameof(relatedEventIds),
            requireUnique: true);
    }

    public abstract CombatEventType EventType { get; }

    public IReadOnlyList<EventId> RelatedEventIds => _relatedEventIds;
}
