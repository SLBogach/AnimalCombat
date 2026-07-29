using Battle.Contracts.Ids;

namespace Battle.Contracts.Requests;

public readonly record struct GearSelection(
    StableId Offense,
    StableId Defense,
    StableId Utility);
