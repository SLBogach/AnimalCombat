using Battle.Contracts.Events;
using Battle.Core.Movement;

namespace Battle.Core.UnitTests.Movement;

public sealed class ArenaGeometryTests
{
    [Fact]
    public void WP07_GEO_001_SurfaceGapIsRadiusAwareAndSymmetric()
    {
        var forward = ArenaGeometry.SurfaceGap(100, 50, 500, 75);
        var reverse = ArenaGeometry.SurfaceGap(500, 75, 100, 50);

        Assert.Equal(275, forward);
        Assert.Equal(forward, reverse);
        Assert.Equal(0, ArenaGeometry.SurfaceGap(100, 50, 225, 75));
        Assert.Equal(0, ArenaGeometry.SurfaceGap(100, 100, 150, 100));
    }

    [Fact]
    public void WP07_GEO_002_WallClampUsesInclusiveRadiusAwareBounds()
    {
        var arena = new ArenaInterval(0, 1_000);

        var left = ArenaGeometry.ClampCenter(arena, 150, 100, -75);
        var right = ArenaGeometry.ClampCenter(arena, 850, 100, 75);

        Assert.Equal(new WallClampResult(150, -75, 100, -50, 25), left);
        Assert.True(left.WasWallClipped);
        Assert.Equal(new WallClampResult(850, 75, 900, 50, 25), right);
        Assert.True(right.WasWallClipped);
        Assert.Equal(0, ArenaGeometry.GetDirectionalHeadroom(arena, 100, 100, MovementDirection.Left));
        Assert.Equal(800, ArenaGeometry.GetDirectionalHeadroom(arena, 100, 100, MovementDirection.Right));
    }

    [Fact]
    public void WP07_GEO_003_CenterBoundsAndFacingAreDerivedWithoutFloatingPoint()
    {
        var arena = new ArenaInterval(-1_000, 1_000);

        Assert.Equal(new CenterInterval(-900, 900), ArenaGeometry.GetCenterInterval(arena, 100));
        Assert.Equal(Facing.Right, ArenaGeometry.GetFacing(-10, 10));
        Assert.Equal(Facing.Left, ArenaGeometry.GetFacing(10, -10));
        Assert.Throws<ArgumentException>(() => ArenaGeometry.GetFacing(10, 10));
    }

    [Fact]
    public void WP07_GEO_004_InvalidOrUnrepresentableGeometryFailsChecked()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArenaInterval(10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ArenaGeometry.GetCenterInterval(new ArenaInterval(0, 100), 0));
        Assert.Throws<ArgumentException>(
            () => ArenaGeometry.GetCenterInterval(new ArenaInterval(0, 100), 51));
        Assert.Throws<OverflowException>(
            () => ArenaGeometry.SurfaceGap(int.MinValue, 1, int.MaxValue, 1));
        Assert.Throws<ArgumentException>(
            () => ArenaGeometry.ValidateOrderedNonOverlappingPair(
                new ArenaInterval(0, 1_000),
                500,
                100,
                400,
                100));
    }
}
