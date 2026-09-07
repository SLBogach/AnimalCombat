using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Movement;

namespace Battle.Core.Resolution;

internal readonly record struct ForcedMovementResult(
    bool Applied,
    PositionChangeKind Kind,
    int ActorFrom,
    int ActorTo,
    int TargetFrom,
    int TargetTo,
    int RequestedMove,
    int ActualMove,
    int BlockedByWall,
    MovementDirection Direction);

internal static class ForcedMovementResolver
{
    internal static ForcedMovementResult Resolve(
        ArenaInterval arena,
        ResolutionMovementMode mode,
        FighterId actorId,
        int actorPosition,
        int actorRadius,
        FighterId targetId,
        int targetPosition,
        int targetRadius,
        MovementDirection hitDirection,
        int requestedMove)
    {
        if (requestedMove < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedMove));
        }

        return mode switch
        {
            ResolutionMovementMode.Push => ResolvePush(),
            ResolutionMovementMode.Pull => ResolvePull(),
            ResolutionMovementMode.Swap => ResolveSwap(),
            _ => new ForcedMovementResult(
                false,
                PositionChangeKind.Forced,
                actorPosition,
                actorPosition,
                targetPosition,
                targetPosition,
                requestedMove,
                0,
                0,
                hitDirection),
        };

        ForcedMovementResult ResolvePush()
        {
            var delta = MovementPairResolver.ApplyDirection(requestedMove, hitDirection);
            var clamp = ArenaGeometry.ClampCenter(arena, targetPosition, targetRadius, delta);
            return new ForcedMovementResult(
                clamp.ActualDelta != 0,
                PositionChangeKind.Forced,
                actorPosition,
                actorPosition,
                targetPosition,
                clamp.ToPosition,
                requestedMove,
                checked((int)ArenaGeometry.Magnitude(clamp.ActualDelta)),
                clamp.BlockedByWall,
                hitDirection);
        }

        ForcedMovementResult ResolvePull()
        {
            if (actorPosition == targetPosition)
            {
                return Empty(hitDirection);
            }

            var direction = targetPosition > actorPosition
                ? MovementDirection.Left
                : MovementDirection.Right;
            var surfaceGap = ArenaGeometry.SurfaceGap(actorPosition, actorRadius, targetPosition, targetRadius);
            var actualRequest = System.Math.Min(requestedMove, surfaceGap);
            var delta = MovementPairResolver.ApplyDirection(actualRequest, direction);
            var clamp = ArenaGeometry.ClampCenter(arena, targetPosition, targetRadius, delta);
            return new ForcedMovementResult(
                clamp.ActualDelta != 0,
                PositionChangeKind.Forced,
                actorPosition,
                actorPosition,
                targetPosition,
                clamp.ToPosition,
                requestedMove,
                checked((int)ArenaGeometry.Magnitude(clamp.ActualDelta)),
                checked(requestedMove - (int)ArenaGeometry.Magnitude(clamp.ActualDelta)),
                direction);
        }

        ForcedMovementResult ResolveSwap()
        {
            try
            {
                var actorBounds = ArenaGeometry.GetCenterInterval(arena, actorRadius);
                var targetBounds = ArenaGeometry.GetCenterInterval(arena, targetRadius);
                if (targetPosition < actorBounds.MinimumPosition || targetPosition > actorBounds.MaximumPosition ||
                    actorPosition < targetBounds.MinimumPosition || actorPosition > targetBounds.MaximumPosition)
                {
                    return Empty(hitDirection);
                }

                return new ForcedMovementResult(
                    true,
                    PositionChangeKind.Swap,
                    actorPosition,
                    targetPosition,
                    targetPosition,
                    actorPosition,
                    checked((int)ArenaGeometry.Magnitude(targetPosition - actorPosition)),
                    checked((int)ArenaGeometry.Magnitude(targetPosition - actorPosition)),
                    0,
                    hitDirection);
            }
            catch (OverflowException)
            {
                return Empty(hitDirection);
            }
        }

        ForcedMovementResult Empty(MovementDirection direction) => new(
            false,
            mode == ResolutionMovementMode.Swap ? PositionChangeKind.Swap : PositionChangeKind.Forced,
            actorPosition,
            actorPosition,
            targetPosition,
            targetPosition,
            requestedMove,
            0,
            requestedMove,
            direction);
    }
}
