using Battle.Contracts.Ids;
using Battle.Core.Movement;

namespace Battle.Core.UnitTests.Movement;

public sealed class ProportionalAllocatorTests
{
    [Fact]
    public void WP07_MOV_003_GoldenFiveUnitBudgetUsesLargestRemainder()
    {
        var result = ProportionalAllocator.Allocate(
            5,
            FighterId.FighterA,
            82,
            FighterId.FighterB,
            147,
            [FighterId.FighterA, FighterId.FighterB]);

        Assert.Equal(new PairAllocation(2, 3, 5), result);
    }

    [Fact]
    public void WP07_MOV_003_ExactRemainderTieUsesOnlyExplicitInitiativeOrder()
    {
        var bFirst = ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            [FighterId.FighterB, FighterId.FighterA]);
        var aFirstWithReversedArguments = ProportionalAllocator.Allocate(
            1,
            FighterId.FighterB,
            1,
            FighterId.FighterA,
            1,
            [FighterId.FighterA, FighterId.FighterB]);

        Assert.Equal(new PairAllocation(0, 1, 1), bFirst);
        Assert.Equal(new PairAllocation(0, 1, 1), aFirstWithReversedArguments);
    }

    [Fact]
    public void WP07_ALLOC_001_AllocationIsCappedByCombinedCapacity()
    {
        var result = ProportionalAllocator.Allocate(
            100,
            FighterId.FighterA,
            2,
            FighterId.FighterB,
            3,
            [FighterId.FighterA, FighterId.FighterB]);

        Assert.Equal(new PairAllocation(2, 3, 5), result);
    }

    [Fact]
    public void WP07_ALLOC_002_InvalidBudgetCapacityOrInitiativeIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProportionalAllocator.Allocate(
            -1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            [FighterId.FighterA, FighterId.FighterB]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            -1,
            FighterId.FighterB,
            1,
            [FighterId.FighterA, FighterId.FighterB]));
        Assert.Throws<ArgumentException>(() => ProportionalAllocator.Allocate(
            1,
            FighterId.FighterA,
            1,
            FighterId.FighterB,
            1,
            [FighterId.FighterA, FighterId.FighterA]));
    }
}
