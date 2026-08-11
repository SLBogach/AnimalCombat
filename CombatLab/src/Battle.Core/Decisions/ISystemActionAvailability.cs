using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Movement;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal interface ISystemActionAvailability
{
    IReadOnlyList<SystemActionCandidate> GetLegalCandidates(
        BattleState state,
        TickSnapshot snapshot,
        FighterId actorId,
        RuntimeBattleSettings settings);
}

internal sealed class Wp07SystemActionAvailability : ISystemActionAvailability
{
    internal static Wp07SystemActionAvailability Instance { get; } = new();

    private Wp07SystemActionAvailability()
    {
    }

    public IReadOnlyList<SystemActionCandidate> GetLegalCandidates(
        BattleState state,
        TickSnapshot snapshot,
        FighterId actorId,
        RuntimeBattleSettings settings)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var actor = state.Get(actorId);
        var actorFrame = snapshot.Get(actorId);
        var opponentFrame = snapshot.GetOpponent(actorId);
        var gap = ArenaGeometry.OrderedSurfaceGap(
            snapshot.FighterA.Position,
            state.FighterA.CollisionRadius,
            snapshot.FighterB.Position,
            state.FighterB.CollisionRadius);
        var action = settings.SystemWait;
        var inner = settings.SystemApproach.PreferredRangeMaximum;
        var outer = settings.SystemRetreat.PreferredRangeMinimum;

        if (gap < inner && IsAllowed(settings, settings.SystemRetreat.Id))
        {
            var outwardDirection = actorFrame.Position < opponentFrame.Position
                ? MovementDirection.Left
                : MovementDirection.Right;
            var headroom = ArenaGeometry.GetDirectionalHeadroom(
                new ArenaInterval(settings.Arena.MinimumPosition, settings.Arena.MaximumPosition),
                actorFrame.Position,
                actor.CollisionRadius,
                outwardDirection);
            if (headroom > 0)
            {
                action = settings.SystemRetreat;
            }
        }
        else if (gap > outer && IsAllowed(settings, settings.SystemApproach.Id))
        {
            action = settings.SystemApproach;
        }

        if (!IsAllowed(settings, action.Id))
        {
            action = settings.SystemWait;
        }

        return new[] { new SystemActionCandidate(action.Id, action.Weight) };
    }

    private static bool IsAllowed(RuntimeBattleSettings settings, StableId actionId) =>
        settings.AllowedSystemActionIds.Contains(actionId);
}
