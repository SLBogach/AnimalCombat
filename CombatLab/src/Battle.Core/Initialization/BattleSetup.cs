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
    SystemActionDefinition SystemWait);

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
