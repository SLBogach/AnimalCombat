using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Decisions;

internal readonly record struct SystemActionCandidate(StableId ActionId, int Weight);

internal readonly record struct SystemActionSelection(
    StableId ActionId,
    int ChosenWeight,
    int WeightSum,
    DecisionSelectionMode SelectionMode,
    ReasonCode ReasonCode);

internal static class SystemActionSelector
{
    internal static StableId ApproachId { get; } = new("sys_approach");

    internal static StableId RetreatId { get; } = new("sys_retreat");

    internal static StableId WaitId { get; } = new("sys_wait");

    internal static StableId ChooseByFixedPriority(IEnumerable<StableId> legalActionIds)
    {
        if (legalActionIds is null)
        {
            throw new ArgumentNullException(nameof(legalActionIds));
        }

        var legal = new HashSet<StableId>(legalActionIds);
        if (legal.Contains(ApproachId))
        {
            return ApproachId;
        }

        if (legal.Contains(RetreatId))
        {
            return RetreatId;
        }

        if (legal.Contains(WaitId))
        {
            return WaitId;
        }

        throw new EngineInvariantException(
            EngineFailureCodes.NoLegalSystemAction,
            TickPhase.Decisions.ToString(),
            "No supported legal system action is available.");
    }

    internal static SystemActionSelection Select(IReadOnlyList<SystemActionCandidate> candidates)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        if (candidates.Count == 0)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.NoLegalSystemAction,
                TickPhase.Decisions.ToString(),
                "No legal system action is available.");
        }

        var weightSum = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Weight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(candidates));
            }

            weightSum = checked(weightSum + candidate.Weight);
        }

        if (candidates.Count == 1)
        {
            var only = candidates[0];
            return new SystemActionSelection(
                only.ActionId,
                only.Weight,
                weightSum,
                DecisionSelectionMode.OnlyLegalAction,
                new ReasonCode("OnlyLegalAction"));
        }

        if (weightSum != 0)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.NoLegalSystemAction,
                TickPhase.Decisions.ToString(),
                "WP-06 does not support weighted selection among multiple system actions.");
        }

        var chosen = ChooseByFixedPriority(candidates.Select(candidate => candidate.ActionId));
        return new SystemActionSelection(
            chosen,
            0,
            0,
            DecisionSelectionMode.ZeroWeightFallback,
            new ReasonCode("ZeroWeightFallback"));
    }
}
