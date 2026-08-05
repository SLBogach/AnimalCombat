using Battle.Core.Decisions;
using Battle.Core.Initialization;
using Battle.Core.Movement;
using Battle.Core.UnitTests.Engine;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.UnitTests.Movement;

public sealed class MovementCoverageGuardTests
{
    private static readonly ArenaInterval Arena = new(0, 10_000);
    private static readonly FighterId[] AFirst = [FighterId.FighterA, FighterId.FighterB];

    [Fact]
    public void WP07_GEO_003_OrderedBoundsDirectionAndWallPostconditionsCoverEveryGuard()
    {
        Assert.Equal(0, ArenaGeometry.OrderedSurfaceGap(100, 50, 225, 75));
        Assert.Equal(275, ArenaGeometry.OrderedSurfaceGap(100, 50, 500, 75));
        Assert.Throws<ArgumentException>(() => ArenaGeometry.OrderedSurfaceGap(500, 50, 100, 75));
        Assert.Throws<ArgumentException>(() => ArenaGeometry.ValidateOrderedNonOverlappingPair(
            new ArenaInterval(0, 1_000),
            300,
            100,
            450,
            100));
        ArenaGeometry.ValidateOrderedNonOverlappingPair(
            new ArenaInterval(0, 1_000),
            300,
            100,
            500,
            100);
        Assert.Throws<ArgumentOutOfRangeException>(() => ArenaGeometry.ClampCenter(
            new ArenaInterval(0, 1_000),
            99,
            100,
            1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArenaGeometry.ClampCenter(
            new ArenaInterval(0, 1_000),
            901,
            100,
            -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArenaGeometry.GetDirectionalHeadroom(
            new ArenaInterval(0, 1_000),
            500,
            100,
            (MovementDirection)99));
        ArenaGeometry.ValidateWallBlock(0);
        Assert.Throws<InvalidOperationException>(() => ArenaGeometry.ValidateWallBlock(-1));
    }

    [Fact]
    public void WP07_MOV_003_AllocatorCoversZeroTieIdentityAndPostconditionGuards()
    {
        Assert.Equal(
            new PairAllocation(0, 0, 0),
            ProportionalAllocator.Allocate(0, FighterId.FighterA, 1, FighterId.FighterB, 1, AFirst));
        Assert.Equal(
            new PairAllocation(0, 0, 0),
            ProportionalAllocator.Allocate(1, FighterId.FighterA, 0, FighterId.FighterB, 0, AFirst));
        Assert.Equal(
            new PairAllocation(1, 0, 1),
            ProportionalAllocator.Allocate(1, FighterId.FighterA, 1, FighterId.FighterB, 1, AFirst));
        Assert.Equal(
            new PairAllocation(0, 1, 1),
            ProportionalAllocator.Allocate(1, FighterId.FighterA, 1, FighterId.FighterB, 2, AFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            -1,
            AFirst));
        Assert.Throws<ArgumentException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterA,
            1,
            AFirst));
        Assert.Throws<ArgumentNullException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            null!));
        Assert.Throws<ArgumentException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            [FighterId.FighterA]));
        Assert.Throws<ArgumentException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            [FighterId.FighterB, FighterId.FighterB]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProportionalAllocator.Allocate(
            1,
            (FighterId)99,
            1,
            FighterId.FighterB,
            1,
            AFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            (FighterId)99,
            1,
            AFirst));

        ProportionalAllocator.ValidateAllocationPostconditions(1, 1, 1, 1, 1);
        Assert.Throws<InvalidOperationException>(() =>
            ProportionalAllocator.ValidateAllocationPostconditions(2, 0, 1, 0, 1));
        Assert.Throws<InvalidOperationException>(() =>
            ProportionalAllocator.ValidateAllocationPostconditions(0, 2, 1, 0, 1));
        Assert.Throws<InvalidOperationException>(() =>
            ProportionalAllocator.ValidateAllocationPostconditions(0, 0, 1, 2, 1));
    }

    [Fact]
    public void WP07_MOV_001_PairHelperGuardsAndInactiveActorsAreDeterministic()
    {
        Assert.Equal(5, MovementPairResolver.GetRequiredBudget(GapMovementMode.Approach, 1_605, 1_600));
        Assert.Equal(0, MovementPairResolver.GetRequiredBudget(GapMovementMode.Approach, 1_600, 1_600));
        Assert.Equal(5, MovementPairResolver.GetRequiredBudget(GapMovementMode.Retreat, 1_495, 1_500));
        Assert.Equal(0, MovementPairResolver.GetRequiredBudget(GapMovementMode.Retreat, 1_500, 1_500));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementPairResolver.GetRequiredBudget(
            (GapMovementMode)99,
            1_500,
            1_500));
        Assert.Equal(-5, MovementPairResolver.ApplyDirection(5, MovementDirection.Left));
        Assert.Equal(5, MovementPairResolver.ApplyDirection(5, MovementDirection.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementPairResolver.ApplyDirection(5, (MovementDirection)99));

        var leftInactive = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            new MovementParticipant(FighterId.FighterA, 4_000, 520, 0, false),
            new MovementParticipant(FighterId.FighterB, 6_555, 430, 147),
            AFirst);
        Assert.Equal(0, leftInactive.Left.FinalDelta);
        var rightInactive = MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            1_600,
            new MovementParticipant(FighterId.FighterA, 4_000, 520, 82),
            new MovementParticipant(FighterId.FighterB, 6_555, 430, 0, false),
            AFirst);
        Assert.Equal(0, rightInactive.Right.FinalDelta);
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementPairResolver.Resolve(
            Arena,
            (GapMovementMode)(-1),
            1_600,
            new MovementParticipant(FighterId.FighterA, 4_000, 520, 82),
            new MovementParticipant(FighterId.FighterB, 6_555, 430, 147),
            AFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementPairResolver.Resolve(
            Arena,
            (GapMovementMode)99,
            1_600,
            new MovementParticipant(FighterId.FighterA, 4_000, 520, 82),
            new MovementParticipant(FighterId.FighterB, 6_555, 430, 147),
            AFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => MovementPairResolver.Resolve(
            Arena,
            GapMovementMode.Approach,
            -1,
            new MovementParticipant(FighterId.FighterA, 4_000, 520, 82),
            new MovementParticipant(FighterId.FighterB, 6_555, 430, 147),
            AFirst));
    }

    [Fact]
    public void WP07_MOV_005_RedistributionAndResolutionPostconditionsFailClosed()
    {
        var inactive = new MovementParticipant(FighterId.FighterA, 4_000, 520, 0, false);
        var activeZero = new MovementParticipant(FighterId.FighterA, 4_000, 520, 0);
        Assert.Equal(0, MovementPairResolver.GetExtraCapacity(
            Arena,
            inactive,
            MovementDirection.Right,
            0,
            0,
            0));
        Assert.Equal(0, MovementPairResolver.GetExtraCapacity(
            Arena,
            activeZero,
            MovementDirection.Right,
            0,
            0,
            0));
        var active = new MovementParticipant(FighterId.FighterA, 4_000, 520, 10);
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.GetExtraCapacity(
            Arena,
            active,
            MovementDirection.Right,
            11,
            0,
            10));
        var wall = new MovementParticipant(FighterId.FighterA, 520, 520, 10);
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.GetExtraCapacity(
            Arena,
            wall,
            MovementDirection.Left,
            0,
            -1,
            10));

        MovementPairResolver.ValidateResolution(GapMovementMode.Approach, 1_600, 1_605, 1_600, 5, 5);
        MovementPairResolver.ValidateResolution(GapMovementMode.Retreat, 1_500, 1_495, 1_500, 5, 5);
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.ValidateResolution(
            GapMovementMode.Approach, 1_600, 1_605, 1_600, 4, 5));
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.ValidateResolution(
            GapMovementMode.Approach, 1_600, 1_605, 1_606, 5, 5));
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.ValidateResolution(
            GapMovementMode.Approach, 1_600, 1_605, 1_599, 5, 5));
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.ValidateResolution(
            GapMovementMode.Retreat, 1_500, 1_495, 1_494, 5, 5));
        Assert.Throws<InvalidOperationException>(() => MovementPairResolver.ValidateResolution(
            GapMovementMode.Retreat, 1_500, 1_495, 1_501, 5, 5));
    }

    [Fact]
    public void WP07_SEP_004_SeparationRejectsEveryInvalidCauseAndParticipantShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeparationResolver.AllocateRollback(
            -1, FighterId.FighterA, 0, FighterId.FighterB, 0, AFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeparationResolver.AllocateRollback(
            0, FighterId.FighterA, -1, FighterId.FighterB, 0, AFirst));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeparationResolver.AllocateRollback(
            0, FighterId.FighterA, 0, FighterId.FighterB, -1, AFirst));

        AssertInvalidParticipant(new MovementParticipant(FighterId.FighterA, 4_000, 0, 1));
        AssertInvalidParticipant(new MovementParticipant(FighterId.FighterA, 4_000, 500, -1));
        AssertInvalidParticipant(new MovementParticipant(FighterId.FighterA, 4_000, 500, 0));
        var inactive = new MovementParticipant(FighterId.FighterA, 4_000, 500, 0, false);
        var result = SeparationResolver.Resolve(
            Arena,
            inactive,
            0,
            new MovementParticipant(FighterId.FighterB, 5_100, 500, 1),
            0,
            AFirst);
        Assert.Equal(0, result.Penetration);
    }

    [Fact]
    public void WP07_AVL_001_AvailabilityRejectsNullsAndKeepsWaitAsTheFailClosedFallback()
    {
        var setup = EngineTestFixture.CreateSetup();
        var snapshot = setup.State.CreateSnapshot();
        Assert.Throws<ArgumentNullException>(() => Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            null!, snapshot, FighterId.FighterA, setup.Settings));
        Assert.Throws<ArgumentNullException>(() => Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            setup.State, null!, FighterId.FighterA, setup.Settings));
        Assert.Throws<ArgumentNullException>(() => Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            setup.State, snapshot, FighterId.FighterA, null!));

        var noAllowedSystemActions = setup.Settings with
        {
            AllowedSystemActionIds = Array.Empty<StableId>(),
        };
        var candidate = Assert.Single(Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            setup.State,
            snapshot,
            FighterId.FighterA,
            noAllowedSystemActions));
        Assert.Equal("sys_wait", candidate.ActionId.Value);
    }

    private static void AssertInvalidParticipant(MovementParticipant participant)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeparationResolver.Resolve(
            Arena,
            participant,
            0,
            new MovementParticipant(FighterId.FighterB, 5_100, 500, 1),
            0,
            AFirst));
    }
}
