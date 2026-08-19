using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal static class DecisionEvaluator
{
    internal static IReadOnlyList<CandidateScore> Evaluate(
        DecisionBatchSnapshot snapshot,
        FighterId actorId,
        DecisionRuntimeSettings runtime)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (runtime is null)
        {
            throw new ArgumentNullException(nameof(runtime));
        }

        var fighterProfile = runtime.GetFighter(actorId);
        var checkedCatalog = DecisionCatalogBuilder.BuildCheckedCatalog(
            runtime.Actions,
            fighterProfile.BuildView.AnimalId);
        var context = new DecisionAvailabilityContext(snapshot, actorId, runtime.Availability);
        var evaluations = DecisionAvailabilityEvaluator.EvaluateCatalog(checkedCatalog, context);
        var scores = evaluations.Select(evaluation => Score(evaluation, context, fighterProfile, runtime)).ToArray();
        return new ReadOnlyCollection<CandidateScore>(scores);
    }

    private static CandidateScore Score(
        DecisionCandidateEvaluation evaluation,
        DecisionAvailabilityContext context,
        DecisionFighterProfile fighterProfile,
        DecisionRuntimeSettings runtime)
    {
        if (!evaluation.Legal)
        {
            return CandidateScore.Illegal(evaluation);
        }

        var action = evaluation.Action;
        var actor = context.Actor;
        var opponent = context.Opponent;
        var variety = DecisionVariety.Calculate(
            action,
            actor.History,
            runtime.Weights.FixedPointScale,
            runtime.RepeatSameActionFixedPoint,
            runtime.RepeatSameCategoryFixedPoint,
            fighterProfile.Tactic.RepeatPenaltyFixedPoint);
        var opportunity = DecisionOpportunity.CalculateMultiplier(
            evaluation.OpportunityDebt,
            action.OpportunityCapFixedPoint,
            runtime.OpportunityCapFixedPoint,
            runtime.OpportunityGrowthFixedPoint,
            runtime.Weights.FixedPointScale);
        var hardReady = action.Slot == DecisionActionSlot.Special && DecisionOpportunity.IsHardReady(
            evaluation.OpportunityDebt,
            action.HardOpportunityMisses,
            runtime.HardOpportunityMisses);
        var telegraphObserved = opponent.Telegraph is not null &&
            opponent.Telegraph.CommitTick <= context.Snapshot.Tick &&
            context.Snapshot.Tick - opponent.Telegraph.CommitTick >= actor.PerceptionDelayTicks;

        var multipliers = new DecisionStageMultipliers(
            DecisionTacticMultiplierCalculator.Calculate(action, fighterProfile.Tactic, runtime.Weights),
            DecisionSituationMultiplierCalculator.Calculate(
                action,
                actor,
                opponent,
                fighterProfile.Tactic,
                runtime.Weights,
                fighterProfile.LowHealthThresholdFixedPoint,
                runtime.Availability.ArenaMinimum,
                runtime.Availability.ArenaMaximum,
                runtime.WallZoneSize),
            DecisionSynergyMultiplierCalculator.Calculate(
                action,
                fighterProfile.Passive,
                fighterProfile.OffenseGear,
                fighterProfile.DefenseGear,
                fighterProfile.UtilityGear,
                runtime.Weights),
            DecisionCounterMultiplierCalculator.Calculate(
                action,
                fighterProfile.Tactic,
                telegraphObserved,
                runtime.Weights.FixedPointScale),
            variety.MultiplierFixedPoint,
            opportunity);
        return DecisionWeightCalculator.Calculate(evaluation, multipliers, runtime.Weights, hardReady);
    }
}
