using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal enum SystemMovementMode
{
    None,
    Approach,
    Retreat,
}

internal sealed record SystemActionDefinition(
    StableId Id,
    int Weight,
    int EnergyCost,
    int ResourceCost,
    int StartupTicks,
    int ActiveTicks,
    int RecoveryTicks,
    int CooldownTicks,
    SystemMovementMode MovementMode,
    int PreferredRangeMinimum,
    int PreferredRangeMaximum,
    bool TrackTarget)
{
    internal bool IsMovement => MovementMode is
        SystemMovementMode.Approach or SystemMovementMode.Retreat;
}
