using Battle.Contracts.Events;

namespace Battle.Core.Movement;

internal readonly record struct ArenaInterval
{
    public ArenaInterval(int minimumPosition, int maximumPosition)
    {
        if (minimumPosition >= maximumPosition)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPosition),
                "The arena maximum must be greater than its minimum.");
        }

        MinimumPosition = minimumPosition;
        MaximumPosition = maximumPosition;
    }

    public int MinimumPosition { get; }

    public int MaximumPosition { get; }
}

internal readonly record struct CenterInterval
{
    public CenterInterval(int minimumPosition, int maximumPosition)
    {
        if (minimumPosition > maximumPosition)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPosition),
                "The center interval maximum must not be less than its minimum.");
        }

        MinimumPosition = minimumPosition;
        MaximumPosition = maximumPosition;
    }

    public int MinimumPosition { get; }

    public int MaximumPosition { get; }
}

internal readonly record struct WallClampResult(
    int FromPosition,
    int RequestedDelta,
    int ToPosition,
    int ActualDelta,
    int BlockedByWall)
{
    public bool WasWallClipped => BlockedByWall > 0;
}

internal static class ArenaGeometry
{
    public static CenterInterval GetCenterInterval(ArenaInterval arena, int collisionRadius)
    {
        RequirePositiveRadius(collisionRadius, nameof(collisionRadius));

        var minimum = checked((long)arena.MinimumPosition + collisionRadius);
        var maximum = checked((long)arena.MaximumPosition - collisionRadius);
        if (minimum > maximum)
        {
            throw new ArgumentException(
                "The fighter body does not fit inside the arena.",
                nameof(collisionRadius));
        }

        return new CenterInterval(checked((int)minimum), checked((int)maximum));
    }

    public static int SurfaceGap(
        int firstPosition,
        int firstRadius,
        int secondPosition,
        int secondRadius)
    {
        RequirePositiveRadius(firstRadius, nameof(firstRadius));
        RequirePositiveRadius(secondRadius, nameof(secondRadius));

        var centerDistance = firstPosition >= secondPosition
            ? checked((long)firstPosition - secondPosition)
            : checked((long)secondPosition - firstPosition);
        var radiusSum = checked((long)firstRadius + secondRadius);
        var gap = centerDistance - radiusSum;

        return gap <= 0 ? 0 : checked((int)gap);
    }

    public static int OrderedSurfaceGap(
        int leftPosition,
        int leftRadius,
        int rightPosition,
        int rightRadius)
    {
        RequirePositiveRadius(leftRadius, nameof(leftRadius));
        RequirePositiveRadius(rightRadius, nameof(rightRadius));
        if (leftPosition >= rightPosition)
        {
            throw new ArgumentException("The left center must be strictly left of the right center.");
        }

        var signedCenterDistance = checked((long)rightPosition - leftPosition);
        var radiusSum = checked((long)leftRadius + rightRadius);
        var gap = signedCenterDistance - radiusSum;

        return gap <= 0 ? 0 : checked((int)gap);
    }

    public static void ValidateOrderedNonOverlappingPair(
        ArenaInterval arena,
        int leftPosition,
        int leftRadius,
        int rightPosition,
        int rightRadius)
    {
        ValidateCenter(arena, leftPosition, leftRadius, nameof(leftPosition));
        ValidateCenter(arena, rightPosition, rightRadius, nameof(rightPosition));
        if (leftPosition >= rightPosition)
        {
            throw new ArgumentException("The left center must be strictly left of the right center.");
        }

        var signedCenterDistance = checked((long)rightPosition - leftPosition);
        var radiusSum = checked((long)leftRadius + rightRadius);
        if (signedCenterDistance < radiusSum)
        {
            throw new ArgumentException("The initial fighter bodies must not overlap.");
        }
    }

    public static WallClampResult ClampCenter(
        ArenaInterval arena,
        int fromPosition,
        int collisionRadius,
        int requestedDelta)
    {
        var centerInterval = GetCenterInterval(arena, collisionRadius);
        RequireInInterval(fromPosition, centerInterval, nameof(fromPosition));

        var requestedPosition = checked((long)fromPosition + requestedDelta);
        var toPosition = requestedPosition < centerInterval.MinimumPosition
            ? centerInterval.MinimumPosition
            : requestedPosition > centerInterval.MaximumPosition
                ? centerInterval.MaximumPosition
                : checked((int)requestedPosition);
        var actualDelta = checked((int)((long)toPosition - fromPosition));
        var blockedByWall = checked(Magnitude(requestedDelta) - Magnitude(actualDelta));
        ValidateWallBlock(blockedByWall);

        return new WallClampResult(
            fromPosition,
            requestedDelta,
            toPosition,
            actualDelta,
            checked((int)blockedByWall));
    }

    internal static void ValidateWallBlock(long blockedByWall)
    {
        if (blockedByWall < 0)
        {
            throw new InvalidOperationException("A wall clamp increased the requested movement.");
        }
    }

    public static int GetDirectionalHeadroom(
        ArenaInterval arena,
        int position,
        int collisionRadius,
        MovementDirection direction)
    {
        var centerInterval = GetCenterInterval(arena, collisionRadius);
        RequireInInterval(position, centerInterval, nameof(position));

        var headroom = direction switch
        {
            MovementDirection.Left => checked((long)position - centerInterval.MinimumPosition),
            MovementDirection.Right => checked((long)centerInterval.MaximumPosition - position),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };

        return checked((int)headroom);
    }

    public static Facing GetFacing(int position, int opponentPosition)
    {
        if (position == opponentPosition)
        {
            throw new ArgumentException("Facing cannot be derived from equal fighter positions.");
        }

        return position < opponentPosition ? Facing.Right : Facing.Left;
    }

    internal static long Magnitude(int value) => value >= 0 ? value : -(long)value;

    private static void ValidateCenter(
        ArenaInterval arena,
        int position,
        int collisionRadius,
        string parameterName)
    {
        var interval = GetCenterInterval(arena, collisionRadius);
        RequireInInterval(position, interval, parameterName);
    }

    private static void RequireInInterval(int position, CenterInterval interval, string parameterName)
    {
        if (position < interval.MinimumPosition || position > interval.MaximumPosition)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The fighter center must be inside its radius-aware arena interval.");
        }
    }

    private static void RequirePositiveRadius(int collisionRadius, string parameterName)
    {
        if (collisionRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
