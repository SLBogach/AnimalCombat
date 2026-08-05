using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Contracts.Events;

public sealed class BattleEndedPayload : CombatEventPayload
{
    public BattleEndedPayload(BattleSummary summary)
        : this(Array.Empty<EventId>(), summary)
    {
    }

    public BattleEndedPayload(
        IEnumerable<EventId> relatedEventIds,
        BattleSummary summary)
        : base(relatedEventIds)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public override CombatEventType EventType => CombatEventType.BattleEnded;

    public BattleSummary Summary { get; }
}
