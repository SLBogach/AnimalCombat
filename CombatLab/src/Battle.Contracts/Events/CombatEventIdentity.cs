using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public readonly record struct CombatEventIdentity(
    EventId EventId,
    long Sequence);
