using Battle.Contracts.Results;

namespace Battle.Contracts.Events;

public sealed class BattleEndedPayload : CombatEventPayload
{
    public BattleEndedPayload(BattleSummary summary)
    {
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    public override CombatEventType EventType => CombatEventType.BattleEnded;

    public BattleSummary Summary { get; }
}
