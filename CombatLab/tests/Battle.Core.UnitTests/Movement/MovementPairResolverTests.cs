using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Movement;

namespace Battle.Core.UnitTests.Movement;

public sealed class MovementPairResolverTests
{
    private static readonly ArenaInterval Arena = new(0, 10_000);
    private static readonly FighterId[] AFirst = [FighterId.FighterA, FighterId.FighterB];

    [Fact]
    public void WP07_MOV_001_ApproachStopsExactlyAtOuterTargetGap()
    {
        var result = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            Participant(FighterId.FighterA, 4_000, 520, 82),
            Participant(FighterId.FighterB, 6_555, 430, 147),
            AFirst);

        Assert.Equal(1_605, result.InitialGap);
        Assert.Equal(1_600, result.FinalGap);
        Assert.Equal(5, result.RequiredBudget);
        Assert.Equal(5, result.AllocatedBudget);
        Assert.Equal(5, result.AppliedBudget);
        Assert.Equal(0, result.RedistributedBudget);
        Assert.True(result.TargetBandReached);
        Assert.Equal(2, result.Left.RequestedDelta);
        Assert.Equal(2, result.Left.VoluntaryActualDelta);
        Assert.Equal(4_002, result.Left.FinalPosition);
        Assert.Equal(-3, result.Right.RequestedDelta);
        Assert.Equal(-3, result.Right.VoluntaryActualDelta);
        Assert.Equal(6_552, result.Right.FinalPosition);
        Assert.Equal(Facing.Right, result.Left.Facing);
        Assert.Equal(Facing.Left, result.Right.Facing);
    }

    [Fact]
    public void WP07_MOV_002_RetreatStopsExactlyAtInnerTargetGap()
    {
        var result = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Retreat,
            1_500,
            Participant(FighterId.FighterA, 4_000, 520, 82),
            Participant(FighterId.FighterB, 6_445, 430, 147),
            AFirst);

        Assert.Equal(1_495, result.InitialGap);
        Assert.Equal(1_500, result.FinalGap);
        Assert.True(result.TargetBandReached);
        Assert.Equal(-2, result.Left.RequestedDelta);
        Assert.Equal(-2, result.Left.VoluntaryActualDelta);
        Assert.Equal(3_998, result.Left.FinalPosition);
        Assert.Equal(3, result.Right.RequestedDelta);
        Assert.Equal(3, result.Right.VoluntaryActualDelta);
        Assert.Equal(6_448, result.Right.FinalPosition);
    }

    [Fact]
    public void WP07_MOV_005_WallLossIsRedistributedWithinOtherMoverCapacity()
    {
        var result = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Retreat,
            1_500,
            Participant(FighterId.FighterA, 521, 520, 82),
            Participant(FighterId.FighterB, 2_966, 430, 147),
            AFirst);

        Assert.Equal(1_500, result.FinalGap);
        Assert.Equal(1, result.RedistributedBudget);
        Assert.Equal(-2, result.Left.RequestedDelta);
        Assert.Equal(-1, result.Left.VoluntaryActualDelta);
        Assert.Equal(1, result.Left.BlockedByWall);
        Assert.True(result.Left.WasWallClipped);
        Assert.Equal(520, result.Left.FinalPosition);
        Assert.Equal(4, result.Right.RequestedDelta);
        Assert.Equal(4, result.Right.VoluntaryActualDelta);
        Assert.False(result.Right.WasWallClipped);
        Assert.Equal(2_970, result.Right.FinalPosition);
        Assert.True(result.TargetBandReached);
    }

    [Fact]
    public void WP07_MOV_004_SharedSnapshotResultHasNoHiddenFighterIdOrder()
    {
        var aOnLeft = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            Participant(FighterId.FighterA, 4_000, 520, 82),
            Participant(FighterId.FighterB, 6_555, 430, 147),
            AFirst);
        var bOnLeft = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            Participant(FighterId.FighterB, 4_000, 520, 82),
            Participant(FighterId.FighterA, 6_555, 430, 147),
            [FighterId.FighterB, FighterId.FighterA]);

        Assert.Equal(aOnLeft.Left.RequestedDelta, bOnLeft.Left.RequestedDelta);
        Assert.Equal(aOnLeft.Right.RequestedDelta, bOnLeft.Right.RequestedDelta);
        Assert.Equal(aOnLeft.Left.FinalPosition, bOnLeft.Left.FinalPosition);
        Assert.Equal(aOnLeft.Right.FinalPosition, bOnLeft.Right.FinalPosition);
        Assert.Equal(aOnLeft.FinalGap, bOnLeft.FinalGap);
    }

    [Fact]
    public void WP07_DET_004_SpatialAndIdentityMirrorHasNoHiddenSideAdvantage()
    {
        var original = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            Participant(FighterId.FighterA, 4_000, 520, 82),
            Participant(FighterId.FighterB, 6_555, 430, 147),
            AFirst);
        var mirrored = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            Participant(FighterId.FighterB, 3_445, 430, 147),
            Participant(FighterId.FighterA, 6_000, 520, 82),
            AFirst);

        Assert.Equal(original.Left.FighterId, mirrored.Right.FighterId);
        Assert.Equal(original.Right.FighterId, mirrored.Left.FighterId);
        Assert.Equal(original.Left.RequestedDelta, -mirrored.Right.RequestedDelta);
        Assert.Equal(original.Right.RequestedDelta, -mirrored.Left.RequestedDelta);
        var mirrorAxisSum = Arena.MinimumPosition + Arena.MaximumPosition;
        Assert.Equal(original.Left.FinalPosition, mirrorAxisSum - mirrored.Right.FinalPosition);
        Assert.Equal(original.Right.FinalPosition, mirrorAxisSum - mirrored.Left.FinalPosition);
        Assert.Equal(original.FinalGap, mirrored.FinalGap);
        Assert.Equal(original.TargetBandReached, mirrored.TargetBandReached);
    }

    [Fact]
    public void WP07_MOV_NOOP_AlreadySatisfiedBandProducesStableZeroMovement()
    {
        var result = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            Participant(FighterId.FighterB, 4_000, 520, 147),
            Participant(FighterId.FighterA, 6_445, 430, 82),
            [FighterId.FighterA, FighterId.FighterB]);

        Assert.Equal(0, result.RequiredBudget);
        Assert.Equal(0, result.AllocatedBudget);
        Assert.Equal(0, result.Left.RequestedDelta);
        Assert.Equal(0, result.Right.RequestedDelta);
        Assert.True(result.TargetBandReached);
        Assert.Equal(FighterId.FighterB, result.Left.FighterId);
        Assert.Equal(FighterId.FighterA, result.Right.FighterId);
    }

    private static MovementParticipant Participant(
        FighterId fighterId,
        int position,
        int radius,
        int speed,
        bool isActive = true) =>
        new(fighterId, position, radius, speed, isActive);
}
