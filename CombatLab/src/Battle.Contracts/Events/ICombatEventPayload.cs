namespace Battle.Contracts.Events;

public interface ICombatEventPayload
{
    CombatEventType EventType { get; }
}

public abstract class CombatEventPayload : ICombatEventPayload
{
    internal CombatEventPayload()
    {
    }

    public abstract CombatEventType EventType { get; }
}
