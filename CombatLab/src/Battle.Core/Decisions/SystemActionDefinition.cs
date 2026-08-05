using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal sealed record SystemActionDefinition(
    StableId Id,
    int Weight,
    int EnergyCost,
    int ResourceCost,
    int StartupTicks,
    int ActiveTicks,
    int RecoveryTicks,
    int CooldownTicks);
