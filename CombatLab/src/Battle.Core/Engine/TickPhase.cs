namespace Battle.Core.Engine;

internal enum TickPhase
{
    Snapshot = 1,
    Expiry = 2,
    Resource = 3,
    ActionPhaseEnd = 4,
    Decisions = 5,
    VoluntaryMovement = 6,
    CollectIntents = 7,
    SortIntents = 8,
    Resolve = 9,
    WallsAndGrabs = 10,
    Outcome = 11,
    EndTick = 12,
}
