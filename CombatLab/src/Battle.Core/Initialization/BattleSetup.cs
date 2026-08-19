using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace Battle.Core.Initialization;

internal sealed record RuntimeBattleSettings(
    int TimeLimitTicks,
    int MaximumEvents,
    int MaximumZeroProgressTicks,
    int FixedPointScale,
    ArenaSnapshot Arena,
    SystemActionDefinition SystemApproach,
    SystemActionDefinition SystemRetreat,
    SystemActionDefinition SystemWait,
    IReadOnlyList<StableId> AllowedSystemActionIds,
    DecisionRuntimeSettings Decisions,
    IReadOnlyList<FighterId> InitiativeOrder)
{
    internal SystemActionDefinition GetSystemAction(StableId actionId)
    {
        if (actionId == SystemApproach.Id)
        {
            return SystemApproach;
        }

        if (actionId == SystemRetreat.Id)
        {
            return SystemRetreat;
        }

        if (actionId == SystemWait.Id)
        {
            return SystemWait;
        }

        throw new EngineInvariantException(
            EngineFailureCodes.NoLegalSystemAction,
            TickPhase.Decisions.ToString(),
            $"Unsupported system action '{actionId}'.");
    }
}

internal sealed record BattleSetup(
    BattleState State,
    RuntimeBattleSettings Settings,
    IReadOnlyList<FighterId> InitiativeOrder);

internal sealed class BattleSetupResult
{
    internal BattleSetupResult(BattleSetup? setup, IReadOnlyList<BattleRejectionError> errors)
    {
        Setup = setup;
        Errors = errors;
    }

    internal BattleSetup? Setup { get; }

    internal IReadOnlyList<BattleRejectionError> Errors { get; }

    internal bool IsSuccess => Setup is not null && Errors.Count == 0;
}
