using Battle.Contracts.Events;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace Battle.Contracts.Ports;

public interface ICombatEventJournal
{
    JournalBeginResult Begin(in CombatJournalStart start);

    CombatEventIdentity Append(in CombatEventDraft draft);

    JournalCompletion Complete(in BattleSummary summary);
}
