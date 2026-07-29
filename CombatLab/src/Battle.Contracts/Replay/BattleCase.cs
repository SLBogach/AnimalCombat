using Battle.Contracts.Requests;

namespace Battle.Contracts.Replay;

public readonly record struct BattleCase(
    string CaseId,
    BattleRequest Request,
    JournalProfile Profile);
