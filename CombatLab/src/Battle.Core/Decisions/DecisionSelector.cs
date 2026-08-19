using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Decisions;

internal interface IDecisionDrawSource
{
    ulong NextDrawIndex { get; }

    RngProvenance NextInt(int minimumInclusive, int maximumExclusive);
}

internal sealed class DecisionSelection
{
    private readonly ReadOnlyCollection<StableId> _legalActionIds;

    internal DecisionSelection(
        StableId actionId,
        int chosenWeight,
        int weightSum,
        DecisionSelectionMode selectionMode,
        ReasonCode reasonCode,
        IEnumerable<StableId> legalActionIds,
        RngProvenance? rng)
    {
        if (string.IsNullOrEmpty(actionId.Value) || chosenWeight < 0 || weightSum < 0 ||
            !Enum.IsDefined(typeof(DecisionSelectionMode), selectionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(actionId));
        }

        if (legalActionIds is null)
        {
            throw new ArgumentNullException(nameof(legalActionIds));
        }

        var legal = legalActionIds.ToArray();
        if (legal.Length == 0 || legal.Distinct().Count() != legal.Length ||
            !legal.Contains(actionId))
        {
            throw new ArgumentException("A selection requires a unique legal set containing the chosen action.");
        }

        for (var index = 1; index < legal.Length; index++)
        {
            if (legal[index - 1].CompareTo(legal[index]) >= 0)
            {
                throw new ArgumentException("Legal action IDs must use ordinal order.", nameof(legalActionIds));
            }
        }

        ActionId = actionId;
        ChosenWeight = chosenWeight;
        WeightSum = weightSum;
        SelectionMode = selectionMode;
        ReasonCode = reasonCode;
        _legalActionIds = new ReadOnlyCollection<StableId>(legal);
        Rng = rng;
    }

    internal StableId ActionId { get; }

    internal int ChosenWeight { get; }

    internal int WeightSum { get; }

    internal DecisionSelectionMode SelectionMode { get; }

    internal ReasonCode ReasonCode { get; }

    internal IReadOnlyList<StableId> LegalActionIds => _legalActionIds;

    internal RngProvenance? Rng { get; }
}

internal static class DecisionSelector
{
    private static readonly ReasonCode OnlyLegalReason = new("OnlyLegalAction");
    private static readonly ReasonCode ZeroFallbackReason = new("ZeroWeightFallback");
    private static readonly ReasonCode HardOpportunityReason = new("HardOpportunity");
    private static readonly ReasonCode WeightedReason = new("WeightedRng");

    internal static DecisionSelection Select(
        IEnumerable<CandidateScore> candidates,
        bool emergency,
        IDecisionDrawSource? drawSource)
    {
        if (candidates is null)
        {
            throw new ArgumentNullException(nameof(candidates));
        }

        var all = candidates.ToArray();
        if (all.Any(candidate => candidate is null) ||
            all.Select(candidate => candidate.ActionId).Distinct().Count() != all.Length)
        {
            throw new ArgumentException("Decision candidates must be non-null and unique.", nameof(candidates));
        }

        var legal = all
            .Where(candidate => candidate.Legal)
            .OrderBy(candidate => candidate.ActionId)
            .ToArray();
        if (legal.Length == 0)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.NoLegalAction,
                "Decisions",
                "No fully legal decision candidate is available.");
        }

        var weightSum = SumWeights(legal);
        if (legal.Length == 1)
        {
            return Create(
                legal[0],
                weightSum,
                DecisionSelectionMode.OnlyLegalAction,
                OnlyLegalReason,
                legal,
                null);
        }

        if (!emergency)
        {
            var hard = legal
                .Where(candidate => candidate.HardOpportunityReady)
                .OrderByDescending(candidate => candidate.OpportunityDebt)
                .ThenByDescending(candidate => candidate.FinalWeight)
                .ThenBy(candidate => candidate.ActionId)
                .FirstOrDefault();
            if (hard is not null)
            {
                return Create(
                    hard,
                    weightSum,
                    DecisionSelectionMode.HardOpportunity,
                    HardOpportunityReason,
                    legal,
                    null);
            }
        }

        if (weightSum == 0)
        {
            var systemIds = legal
                .Where(candidate => candidate.Slot == DecisionActionSlot.System)
                .Select(candidate => candidate.ActionId)
                .ToArray();
            var chosenId = SystemActionSelector.ChooseByFixedPriority(systemIds);
            var chosen = legal.Single(candidate => candidate.ActionId == chosenId);
            return Create(
                chosen,
                0,
                DecisionSelectionMode.ZeroWeightFallback,
                ZeroFallbackReason,
                legal,
                null);
        }

        if (drawSource is null)
        {
            throw new ArgumentNullException(nameof(drawSource));
        }

        var expectedIndex = drawSource.NextDrawIndex;
        var rng = drawSource.NextInt(0, weightSum);
        ValidateDraw(rng, weightSum, expectedIndex, drawSource.NextDrawIndex);
        var cumulativeEnd = 0;
        CandidateScore weightedChosen;
        try
        {
            weightedChosen = legal.First(candidate =>
            {
                cumulativeEnd = checked(cumulativeEnd + candidate.FinalWeight);
                return cumulativeEnd > rng.Result;
            });
        }
        catch (InvalidOperationException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.InvalidDecisionDraw,
                "Decisions",
                "The bounded Decision draw did not map to a positive interval: " + exception.Message);
        }

        return Create(
            weightedChosen,
            weightSum,
            DecisionSelectionMode.WeightedRng,
            WeightedReason,
            legal,
            rng);
    }

    private static int SumWeights(IEnumerable<CandidateScore> legal)
    {
        try
        {
            var sum = 0;
            foreach (var candidate in legal)
            {
                sum = checked(sum + candidate.FinalWeight);
            }

            return sum;
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.DecisionArithmeticOverflow,
                "Decisions",
                "Decision weight sum overflowed: " + exception.Message);
        }
    }

    private static void ValidateDraw(
        RngProvenance rng,
        int weightSum,
        ulong expectedIndex,
        ulong nextIndex)
    {
        var bound = checked((uint)weightSum);
        var threshold = unchecked(0U - bound) % bound;
        var offset = rng.RawValue % bound;
        var expectedResult = checked((int)offset);
        var expectedNormalized = checked((int)((long)offset * 1_000 / bound));
        ulong expectedNextIndex;
        try
        {
            expectedNextIndex = checked(expectedIndex + 1);
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.InvalidDecisionDraw,
                "Decisions",
                "The injected Decision draw index overflowed: " + exception.Message);
        }

        if (rng.Stream != RngStream.Decision || rng.Operation != RngOperation.NextInt ||
            rng.RangeMinimumInclusive != 0 || rng.RangeMaximumExclusive != weightSum ||
            rng.RawValue < threshold || rng.Result != expectedResult ||
            rng.NormalizedFixedPoint != expectedNormalized || rng.Index != expectedIndex ||
            nextIndex != expectedNextIndex)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.InvalidDecisionDraw,
                "Decisions",
                "The injected Decision draw has invalid provenance.");
        }
    }

    private static DecisionSelection Create(
        CandidateScore chosen,
        int weightSum,
        DecisionSelectionMode mode,
        ReasonCode reason,
        IEnumerable<CandidateScore> legal,
        RngProvenance? rng) => new(
            chosen.ActionId,
            chosen.FinalWeight,
            weightSum,
            mode,
            reason,
            legal.Select(candidate => candidate.ActionId),
            rng);
}
