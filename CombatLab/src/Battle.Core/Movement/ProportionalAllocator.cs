using Battle.Contracts.Ids;

namespace Battle.Core.Movement;

internal readonly record struct PairAllocation(
    int FirstAmount,
    int SecondAmount,
    int AllocatedBudget);

internal static class ProportionalAllocator
{
    public static PairAllocation Allocate(
        int budget,
        FighterId firstActorId,
        int firstCapacity,
        FighterId secondActorId,
        int secondCapacity,
        IReadOnlyList<FighterId> initiativeOrder)
    {
        if (budget < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        if (firstCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstCapacity));
        }

        if (secondCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondCapacity));
        }

        ValidateActorPair(firstActorId, secondActorId);
        ValidateInitiativeOrder(firstActorId, secondActorId, initiativeOrder);

        var capacitySum = checked((long)firstCapacity + secondCapacity);
        if (budget == 0 || capacitySum == 0)
        {
            return new PairAllocation(0, 0, 0);
        }

        var allocatedBudget = checked((int)System.Math.Min(budget, capacitySum));
        var firstNumerator = checked((long)allocatedBudget * firstCapacity);
        var secondNumerator = checked((long)allocatedBudget * secondCapacity);
        var firstAmount = firstNumerator / capacitySum;
        var secondAmount = secondNumerator / capacitySum;
        var firstRemainder = firstNumerator % capacitySum;
        var secondRemainder = secondNumerator % capacitySum;
        var unassigned = checked((long)allocatedBudget - firstAmount - secondAmount);

        ValidateAllocationPostconditions(
            unassigned,
            firstAmount,
            firstCapacity,
            secondAmount,
            secondCapacity);

        if (unassigned == 1)
        {
            var firstWins = firstRemainder > secondRemainder
                || (firstRemainder == secondRemainder && initiativeOrder[0] == firstActorId);
            if (firstWins)
            {
                firstAmount = checked(firstAmount + 1);
            }
            else
            {
                secondAmount = checked(secondAmount + 1);
            }
        }

        ValidateAllocationPostconditions(
            0,
            firstAmount,
            firstCapacity,
            secondAmount,
            secondCapacity);

        return new PairAllocation(
            checked((int)firstAmount),
            checked((int)secondAmount),
            allocatedBudget);
    }

    internal static void ValidateAllocationPostconditions(
        long unassigned,
        long firstAmount,
        int firstCapacity,
        long secondAmount,
        int secondCapacity)
    {
        if (unassigned > 1)
        {
            throw new InvalidOperationException("Two-actor largest-remainder allocation left too many units.");
        }

        if (firstAmount > firstCapacity || secondAmount > secondCapacity)
        {
            throw new InvalidOperationException("An allocated share exceeded actor capacity.");
        }
    }

    internal static void ValidateActorPair(FighterId firstActorId, FighterId secondActorId)
    {
        RequireKnownFighter(firstActorId, nameof(firstActorId));
        RequireKnownFighter(secondActorId, nameof(secondActorId));
        if (firstActorId == secondActorId)
        {
            throw new ArgumentException("The actor pair must contain two different fighters.");
        }
    }

    private static void ValidateInitiativeOrder(
        FighterId firstActorId,
        FighterId secondActorId,
        IReadOnlyList<FighterId> initiativeOrder)
    {
        if (initiativeOrder is null)
        {
            throw new ArgumentNullException(nameof(initiativeOrder));
        }
        if (initiativeOrder.Count != 2
            || initiativeOrder[0] == initiativeOrder[1]
            || (initiativeOrder[0] != firstActorId && initiativeOrder[0] != secondActorId)
            || (initiativeOrder[1] != firstActorId && initiativeOrder[1] != secondActorId))
        {
            throw new ArgumentException(
                "Initiative order must contain both actors exactly once.",
                nameof(initiativeOrder));
        }
    }

    private static void RequireKnownFighter(FighterId fighterId, string parameterName)
    {
        if (fighterId is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
