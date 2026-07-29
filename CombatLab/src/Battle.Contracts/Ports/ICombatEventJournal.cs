using Battle.Contracts.Events;
using Battle.Contracts.Results;

namespace Battle.Contracts.Ports;

public interface ICombatEventJournal
{
    CombatEventIdentity Append(in CombatEventDraft draft);

    void Complete(in BattleSummary summary);
}
