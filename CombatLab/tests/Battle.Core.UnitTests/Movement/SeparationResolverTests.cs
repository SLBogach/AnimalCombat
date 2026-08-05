using Battle.Contracts.Ids;
using Battle.Core.Movement;

namespace Battle.Core.UnitTests.Movement;

public sealed class SeparationResolverTests
{
    private static readonly ArenaInterval Arena = new(0, 10_000);
    private static readonly FighterId[] AFirst = [FighterId.FighterA, FighterId.FighterB];

    [Fact]
    public void WP07_SEP_001_EqualInwardCausesRollbackEqually()
    {
        var result = SeparationResolver.Resolve(
            Arena,
            Participant(FighterId.FighterA, 4_000),
            100,
            Participant(FighterId.FighterB, 5_100),
            -100,
            AFirst);

        Assert.Equal(100, result.Penetration);
        Assert.Equal(new PairAllocation(50, 50, 100), result.RollbackAllocation);
        Assert.Equal(4_100, result.Left.ProvisionalPosition);
        Assert.Equal(-50, result.Left.SeparationDelta);
        Assert.Equal(4_050, result.Left.FinalPosition);
        Assert.Equal(5_000, result.Right.ProvisionalPosition);
        Assert.Equal(50, result.Right.SeparationDelta);
        Assert.Equal(5_050, result.Right.FinalPosition);
    }

    [Fact]
    public void WP07_SEP_002_StationaryActorReceivesNoRollbackShare()
    {
        var result = SeparationResolver.Resolve(
            Arena,
            Participant(FighterId.FighterA, 4_000),
            200,
            Participant(FighterId.FighterB, 5_100),
            0,
            AFirst);

        Assert.Equal(100, result.Penetration);
        Assert.Equal(-100, result.Left.SeparationDelta);
        Assert.Equal(0, result.Right.SeparationDelta);
        Assert.Equal(4_100, result.Left.FinalPosition);
        Assert.Equal(5_100, result.Right.FinalPosition);
    }

    [Fact]
    public void WP07_SEP_003_OddRollbackTieUsesImmutableInitiativeOrder()
    {
        var result = SeparationResolver.Resolve(
            Arena,
            Participant(FighterId.FighterA, 4_000),
            101,
            Participant(FighterId.FighterB, 5_101),
            -101,
            [FighterId.FighterB, FighterId.FighterA]);

        Assert.Equal(101, result.Penetration);
        Assert.Equal(new PairAllocation(50, 51, 101), result.RollbackAllocation);
        Assert.Equal(-50, result.Left.SeparationDelta);
        Assert.Equal(51, result.Right.SeparationDelta);
        Assert.Equal(1_000, result.Right.FinalPosition - result.Left.FinalPosition);
    }

    [Fact]
    public void WP07_SEP_004_CrossingUsesSignedDistanceAndRestoresOrder()
    {
        var result = SeparationResolver.Resolve(
            Arena,
            Participant(FighterId.FighterA, 4_000),
            2_000,
            Participant(FighterId.FighterB, 5_100),
            -2_000,
            AFirst);

        Assert.Equal(3_900, result.Penetration);
        Assert.Equal(4_050, result.Left.FinalPosition);
        Assert.Equal(5_050, result.Right.FinalPosition);
        Assert.True(result.Left.FinalPosition < result.Right.FinalPosition);
    }

    [Fact]
    public void WP07_SEP_005_ImpossibleCauseDeficitIsAnInvariantFailure()
    {
        Assert.Throws<InvalidOperationException>(() => SeparationResolver.AllocateRollback(
            3,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            AFirst));
    }

    [Fact]
    public void WP07_SEP_006_NonPenetratingMovementNeedsNoCorrection()
    {
        var result = SeparationResolver.Resolve(
            Arena,
            Participant(FighterId.FighterA, 4_000),
            -10,
            Participant(FighterId.FighterB, 5_100),
            10,
            AFirst);

        Assert.Equal(0, result.Penetration);
        Assert.Equal(0, result.Left.SeparationDelta);
        Assert.Equal(0, result.Right.SeparationDelta);
        Assert.Equal(-10, result.Left.FinalDelta);
        Assert.Equal(10, result.Right.FinalDelta);
    }

    private static MovementParticipant Participant(FighterId fighterId, int position) =>
        new(fighterId, position, 500, 2_000);
}
