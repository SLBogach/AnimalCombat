using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.Movement;

internal enum GapMovementMode
{
    Approach,
    Retreat,
}

internal readonly record struct MovementPairResult(
    GapMovementMode Mode,
    int TargetGap,
    int InitialGap,
    int FinalGap,
    int RequiredBudget,
    int AllocatedBudget,
    int AppliedBudget,
    int RedistributedBudget,
    bool TargetBandReached,
    PairAllocation InitialAllocation,
    ResolvedMovementActor Left,
    ResolvedMovementActor Right);

internal static class MovementPairResolver
{
    public static MovementPairResult Resolve(
        ArenaInterval arena,
        GapMovementMode mode,
        int targetGap,
        MovementParticipant left,
        MovementParticipant right,
        IReadOnlyList<FighterId> initiativeOrder)
    {
        if (mode is not GapMovementMode.Approach and not GapMovementMode.Retreat)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (targetGap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetGap));
        }

        SeparationResolver.ValidateParticipants(arena, left, right);
        var initialGap = ArenaGeometry.OrderedSurfaceGap(
            left.Position,
            left.CollisionRadius,
            right.Position,
            right.CollisionRadius);
        var requiredBudget = GetRequiredBudget(mode, initialGap, targetGap);
        var leftCapacity = left.IsActive ? left.FrozenSpeed : 0;
        var rightCapacity = right.IsActive ? right.FrozenSpeed : 0;
        var initialAllocation = ProportionalAllocator.Allocate(
            requiredBudget,
            left.FighterId,
            leftCapacity,
            right.FighterId,
            rightCapacity,
            initiativeOrder);
        var leftDirection = mode == GapMovementMode.Approach
            ? MovementDirection.Right
            : MovementDirection.Left;
        var rightDirection = mode == GapMovementMode.Approach
            ? MovementDirection.Left
            : MovementDirection.Right;
        var leftInitialRequest = ApplyDirection(initialAllocation.FirstAmount, leftDirection);
        var rightInitialRequest = ApplyDirection(initialAllocation.SecondAmount, rightDirection);
        var leftInitialClamp = ArenaGeometry.ClampCenter(
            arena,
            left.Position,
            left.CollisionRadius,
            leftInitialRequest);
        var rightInitialClamp = ArenaGeometry.ClampCenter(
            arena,
            right.Position,
            right.CollisionRadius,
            rightInitialRequest);
        var initialAppliedBudget = checked(
            ArenaGeometry.Magnitude(leftInitialClamp.ActualDelta)
            + ArenaGeometry.Magnitude(rightInitialClamp.ActualDelta));
        var missingBudget = checked(initialAllocation.AllocatedBudget - (int)initialAppliedBudget);
        var leftExtraCapacity = GetExtraCapacity(
            arena,
            left,
            leftDirection,
            initialAllocation.FirstAmount,
            leftInitialClamp.ActualDelta,
            leftCapacity);
        var rightExtraCapacity = GetExtraCapacity(
            arena,
            right,
            rightDirection,
            initialAllocation.SecondAmount,
            rightInitialClamp.ActualDelta,
            rightCapacity);
        var redistribution = ProportionalAllocator.Allocate(
            missingBudget,
            left.FighterId,
            leftExtraCapacity,
            right.FighterId,
            rightExtraCapacity,
            initiativeOrder);
        var leftRequestMagnitude = checked(initialAllocation.FirstAmount + redistribution.FirstAmount);
        var rightRequestMagnitude = checked(initialAllocation.SecondAmount + redistribution.SecondAmount);
        var leftRequestedDelta = ApplyDirection(leftRequestMagnitude, leftDirection);
        var rightRequestedDelta = ApplyDirection(rightRequestMagnitude, rightDirection);
        var resolved = SeparationResolver.Resolve(
            arena,
            left,
            leftRequestedDelta,
            right,
            rightRequestedDelta,
            initiativeOrder);
        var finalGap = ArenaGeometry.OrderedSurfaceGap(
            resolved.Left.FinalPosition,
            left.CollisionRadius,
            resolved.Right.FinalPosition,
            right.CollisionRadius);
        var appliedBudget = checked(
            (int)(ArenaGeometry.Magnitude(resolved.Left.VoluntaryActualDelta)
                + ArenaGeometry.Magnitude(resolved.Right.VoluntaryActualDelta)));

        ValidateResolution(mode, targetGap, initialGap, finalGap, initialAllocation.AllocatedBudget, appliedBudget);
        var targetBandReached = mode == GapMovementMode.Approach
            ? finalGap <= targetGap
            : finalGap >= targetGap;

        return new MovementPairResult(
            mode,
            targetGap,
            initialGap,
            finalGap,
            requiredBudget,
            initialAllocation.AllocatedBudget,
            appliedBudget,
            redistribution.AllocatedBudget,
            targetBandReached,
            initialAllocation,
            resolved.Left,
            resolved.Right);
    }

    internal static int GetRequiredBudget(GapMovementMode mode, int currentGap, int targetGap) =>
        mode switch
        {
            GapMovementMode.Approach => currentGap > targetGap
                ? checked(currentGap - targetGap)
                : 0,
            GapMovementMode.Retreat => currentGap < targetGap
                ? checked(targetGap - currentGap)
                : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

    internal static int ApplyDirection(int magnitude, MovementDirection direction) =>
        direction switch
        {
            MovementDirection.Left => checked(-magnitude),
            MovementDirection.Right => magnitude,
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };

    internal static int GetExtraCapacity(
        ArenaInterval arena,
        MovementParticipant participant,
        MovementDirection direction,
        int initiallyRequestedMagnitude,
        int initialActualDelta,
        int totalCapacity)
    {
        if (!participant.IsActive || totalCapacity == 0)
        {
            return 0;
        }

        var unusedSpeed = checked(totalCapacity - initiallyRequestedMagnitude);
        var headroom = ArenaGeometry.GetDirectionalHeadroom(
            arena,
            participant.Position,
            participant.CollisionRadius,
            direction);
        var usedHeadroom = checked((int)ArenaGeometry.Magnitude(initialActualDelta));
        var remainingHeadroom = checked(headroom - usedHeadroom);
        if (unusedSpeed < 0 || remainingHeadroom < 0)
        {
            throw new InvalidOperationException("Movement redistribution capacity became negative.");
        }

        return System.Math.Min(unusedSpeed, remainingHeadroom);
    }

    internal static void ValidateResolution(
        GapMovementMode mode,
        int targetGap,
        int initialGap,
        int finalGap,
        int allocatedBudget,
        int appliedBudget)
    {
        if (appliedBudget > allocatedBudget)
        {
            throw new InvalidOperationException("Applied movement exceeded the allocated pair budget.");
        }

        if (mode == GapMovementMode.Approach)
        {
            if (finalGap > initialGap || (initialGap > targetGap && finalGap < targetGap))
            {
                throw new InvalidOperationException("Approach movement crossed its target gap.");
            }
        }
        else if (finalGap < initialGap || (initialGap < targetGap && finalGap > targetGap))
        {
            throw new InvalidOperationException("Retreat movement crossed its target gap.");
        }
    }
}
