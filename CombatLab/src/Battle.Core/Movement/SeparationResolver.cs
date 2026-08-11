using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.Movement;

internal readonly record struct MovementParticipant(
    FighterId FighterId,
    int Position,
    int CollisionRadius,
    int FrozenSpeed,
    bool IsActive = true);

internal readonly record struct ResolvedMovementActor(
    FighterId FighterId,
    int FromPosition,
    int RequestedDelta,
    int ProvisionalPosition,
    int VoluntaryActualDelta,
    int BlockedByWall,
    int SeparationDelta,
    int FinalPosition,
    int FinalDelta,
    Facing Facing)
{
    public bool WasWallClipped => BlockedByWall > 0;
}

internal readonly record struct SeparationPairResult(
    ResolvedMovementActor Left,
    ResolvedMovementActor Right,
    int Penetration,
    PairAllocation RollbackAllocation);

internal static class SeparationResolver
{
    public static SeparationPairResult Resolve(
        ArenaInterval arena,
        MovementParticipant left,
        int leftRequestedDelta,
        MovementParticipant right,
        int rightRequestedDelta,
        IReadOnlyList<FighterId> initiativeOrder)
    {
        ValidateParticipants(arena, left, right);

        var leftClamp = ArenaGeometry.ClampCenter(
            arena,
            left.Position,
            left.CollisionRadius,
            leftRequestedDelta);
        var rightClamp = ArenaGeometry.ClampCenter(
            arena,
            right.Position,
            right.CollisionRadius,
            rightRequestedDelta);
        var signedProvisionalDistance = checked((long)rightClamp.ToPosition - leftClamp.ToPosition);
        var radiusSum = checked((long)left.CollisionRadius + right.CollisionRadius);
        var penetrationValue = radiusSum - signedProvisionalDistance;
        var penetration = penetrationValue <= 0 ? 0 : checked((int)penetrationValue);
        var leftCause = leftClamp.ActualDelta > 0 ? leftClamp.ActualDelta : 0;
        var rightCause = rightClamp.ActualDelta < 0
            ? checked((int)ArenaGeometry.Magnitude(rightClamp.ActualDelta))
            : 0;
        var rollback = AllocateRollback(
            penetration,
            left.FighterId,
            leftCause,
            right.FighterId,
            rightCause,
            initiativeOrder);
        var leftSeparationDelta = checked(-rollback.FirstAmount);
        var rightSeparationDelta = rollback.SecondAmount;
        var leftFinalPosition = checked((int)((long)leftClamp.ToPosition + leftSeparationDelta));
        var rightFinalPosition = checked((int)((long)rightClamp.ToPosition + rightSeparationDelta));

        ValidateFinalPair(
            arena,
            leftFinalPosition,
            left.CollisionRadius,
            rightFinalPosition,
            right.CollisionRadius);

        var leftFinalDelta = checked((int)((long)leftFinalPosition - left.Position));
        var rightFinalDelta = checked((int)((long)rightFinalPosition - right.Position));
        var leftResult = new ResolvedMovementActor(
            left.FighterId,
            left.Position,
            leftRequestedDelta,
            leftClamp.ToPosition,
            leftClamp.ActualDelta,
            leftClamp.BlockedByWall,
            leftSeparationDelta,
            leftFinalPosition,
            leftFinalDelta,
            ArenaGeometry.GetFacing(leftFinalPosition, rightFinalPosition));
        var rightResult = new ResolvedMovementActor(
            right.FighterId,
            right.Position,
            rightRequestedDelta,
            rightClamp.ToPosition,
            rightClamp.ActualDelta,
            rightClamp.BlockedByWall,
            rightSeparationDelta,
            rightFinalPosition,
            rightFinalDelta,
            ArenaGeometry.GetFacing(rightFinalPosition, leftFinalPosition));

        return new SeparationPairResult(leftResult, rightResult, penetration, rollback);
    }

    public static PairAllocation AllocateRollback(
        int penetration,
        FighterId leftActorId,
        int leftCause,
        FighterId rightActorId,
        int rightCause,
        IReadOnlyList<FighterId> initiativeOrder)
    {
        if (penetration < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(penetration));
        }

        if (leftCause < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leftCause));
        }

        if (rightCause < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rightCause));
        }

        var causeSum = checked((long)leftCause + rightCause);
        if (causeSum < penetration)
        {
            throw new InvalidOperationException(
                "Separation penetration exceeds the inward movement that caused it.");
        }

        return ProportionalAllocator.Allocate(
            penetration,
            leftActorId,
            leftCause,
            rightActorId,
            rightCause,
            initiativeOrder);
    }

    internal static void ValidateParticipants(
        ArenaInterval arena,
        MovementParticipant left,
        MovementParticipant right)
    {
        ProportionalAllocator.ValidateActorPair(left.FighterId, right.FighterId);
        RequireValidParticipant(left, nameof(left));
        RequireValidParticipant(right, nameof(right));
        ArenaGeometry.ValidateOrderedNonOverlappingPair(
            arena,
            left.Position,
            left.CollisionRadius,
            right.Position,
            right.CollisionRadius);
    }

    private static void ValidateFinalPair(
        ArenaInterval arena,
        int leftPosition,
        int leftRadius,
        int rightPosition,
        int rightRadius)
    {
        try
        {
            ArenaGeometry.ValidateOrderedNonOverlappingPair(
                arena,
                leftPosition,
                leftRadius,
                rightPosition,
                rightRadius);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Separation failed to restore a valid ordered pair.",
                exception);
        }
    }

    private static void RequireValidParticipant(MovementParticipant participant, string parameterName)
    {
        if (participant.CollisionRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Collision radius must be positive.");
        }

        if (participant.FrozenSpeed < 0 || (participant.IsActive && participant.FrozenSpeed == 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Frozen speed must be positive for an active participant and non-negative otherwise.");
        }
    }
}
