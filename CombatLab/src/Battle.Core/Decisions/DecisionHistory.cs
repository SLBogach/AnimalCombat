using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Decisions;

internal readonly record struct DecisionVarietyResult(
    int MultiplierFixedPoint,
    bool SameAction,
    bool SameCategory);

internal static class DecisionVariety
{
    internal static DecisionVarietyResult Calculate(
        DecisionActionProfile action,
        DecisionRepeatHistory history,
        int fixedPointScale,
        int repeatSameActionFixedPoint,
        int repeatSameCategoryFixedPoint,
        int tacticRepeatPenaltyFixedPoint)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (history is null)
        {
            throw new ArgumentNullException(nameof(history));
        }

        if (fixedPointScale <= 0 || repeatSameActionFixedPoint < 0 ||
            repeatSameCategoryFixedPoint < 0 || tacticRepeatPenaltyFixedPoint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPointScale));
        }

        var sameAction = history.LastActionId == action.Id;
        var sameCategory = history.LastCategory is not null &&
                           StringComparer.Ordinal.Equals(history.LastCategory, action.Category);
        var multiplier = fixedPointScale;
        try
        {
            if (sameAction)
            {
                multiplier = global::Battle.Core.Math.FixedMath.Mul(
                    multiplier,
                    repeatSameActionFixedPoint,
                    fixedPointScale);
            }

            if (sameCategory)
            {
                multiplier = global::Battle.Core.Math.FixedMath.Mul(
                    multiplier,
                    repeatSameCategoryFixedPoint,
                    fixedPointScale);
            }

            if (sameAction || sameCategory)
            {
                multiplier = global::Battle.Core.Math.FixedMath.Mul(
                    multiplier,
                    tacticRepeatPenaltyFixedPoint,
                    fixedPointScale);
            }
        }
        catch (OverflowException exception)
        {
            throw ArithmeticInvariant(exception);
        }

        return new DecisionVarietyResult(multiplier, sameAction, sameCategory);
    }

    internal static bool IsAtRepeatCap(
        DecisionActionProfile action,
        DecisionRepeatHistory history)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (history is null)
        {
            throw new ArgumentNullException(nameof(history));
        }

        return history.LastActionId == action.Id &&
               history.ConsecutiveActionUses >= action.MaximumConsecutiveUses;
    }

    internal static DecisionRepeatHistory AfterCommit(
        DecisionRepeatHistory history,
        StableId actionId,
        string category)
    {
        if (history is null)
        {
            throw new ArgumentNullException(nameof(history));
        }

        if (string.IsNullOrEmpty(actionId.Value))
        {
            throw new ArgumentException("A committed action ID is required.", nameof(actionId));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("A committed category is required.", nameof(category));
        }

        try
        {
            var actionUses = history.LastActionId == actionId
                ? checked(history.ConsecutiveActionUses + 1)
                : 1;
            var categoryUses = StringComparer.Ordinal.Equals(history.LastCategory, category)
                ? checked(history.ConsecutiveCategoryUses + 1)
                : 1;
            return new DecisionRepeatHistory(actionId, category, actionUses, categoryUses);
        }
        catch (OverflowException exception)
        {
            throw ArithmeticInvariant(exception);
        }
    }

    private static EngineInvariantException ArithmeticInvariant(Exception inner) => new(
        DecisionFailureCodes.DecisionArithmeticOverflow,
        "Decisions",
        "Decision variety arithmetic overflowed: " + inner.Message);
}

internal static class DecisionOpportunity
{
    internal static int CalculateMultiplier(
        int debt,
        int actionCapFixedPoint,
        int globalCapFixedPoint,
        int growthFixedPoint,
        int fixedPointScale)
    {
        if (debt < 0 || actionCapFixedPoint < 1 || globalCapFixedPoint < 1 ||
            growthFixedPoint < 0 || fixedPointScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(debt));
        }

        try
        {
            var grown = checked((long)fixedPointScale + checked((long)debt * growthFixedPoint));
            var cap = global::System.Math.Min(actionCapFixedPoint, globalCapFixedPoint);
            return checked((int)global::System.Math.Min(grown, cap));
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.DecisionArithmeticOverflow,
                "Decisions",
                "Decision opportunity arithmetic overflowed: " + exception.Message);
        }
    }

    internal static bool IsHardReady(
        int priorDebt,
        int actionHardOpportunityMisses,
        int globalHardOpportunityMisses)
    {
        if (priorDebt < 0 || actionHardOpportunityMisses < 0 || globalHardOpportunityMisses < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priorDebt));
        }

        if (actionHardOpportunityMisses == 0 || globalHardOpportunityMisses == 0)
        {
            return false;
        }

        var threshold = global::System.Math.Min(
            actionHardOpportunityMisses,
            globalHardOpportunityMisses);
        return priorDebt >= threshold;
    }

    internal static int UpdateDebt(
        DecisionActionSlot slot,
        int priorDebt,
        bool fullyLegal,
        bool selected)
    {
        if (!Enum.IsDefined(typeof(DecisionActionSlot), slot) || priorDebt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        if (slot != DecisionActionSlot.Special)
        {
            return priorDebt;
        }

        if (!fullyLegal)
        {
            return priorDebt;
        }

        if (selected)
        {
            return 0;
        }

        try
        {
            return checked(priorDebt + 1);
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.DecisionArithmeticOverflow,
                "Decisions",
                "Decision opportunity debt overflowed: " + exception.Message);
        }
    }
}
