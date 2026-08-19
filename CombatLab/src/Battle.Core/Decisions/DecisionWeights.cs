using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Decisions;

internal static class DecisionFailureCodes
{
    internal static ReasonCode DecisionArithmeticOverflow { get; } = new("DecisionArithmeticOverflow");
    internal static ReasonCode NoLegalAction { get; } = new("NoLegalAction");
    internal static ReasonCode InvalidDecisionDraw { get; } = new("InvalidDecisionDraw");
}

internal readonly record struct DecisionWeightSettings
{
    internal DecisionWeightSettings(
        int fixedPointScale,
        int multiplierMinimum,
        int multiplierMaximum,
        int decisionWeightMaximum)
    {
        if (fixedPointScale <= 0 || multiplierMinimum < 0 ||
            multiplierMinimum > fixedPointScale || multiplierMaximum < fixedPointScale ||
            decisionWeightMaximum < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPointScale));
        }

        FixedPointScale = fixedPointScale;
        MultiplierMinimum = multiplierMinimum;
        MultiplierMaximum = multiplierMaximum;
        DecisionWeightMaximum = decisionWeightMaximum;
    }

    internal int FixedPointScale { get; }

    internal int MultiplierMinimum { get; }

    internal int MultiplierMaximum { get; }

    internal int DecisionWeightMaximum { get; }
}

internal readonly record struct DecisionStageMultipliers(
    int Tactic,
    int Situation,
    int Synergy,
    int Counter,
    int Variety,
    int Opportunity);

internal sealed class CandidateScore
{
    private static readonly string[] StageCodes =
    {
        "Tactic",
        "Situation",
        "Synergy",
        "Counter",
        "Variety",
        "Opportunity",
    };

    private readonly ReadOnlyCollection<ModifierTrace> _modifiers;

    private CandidateScore(
        StableId actionId,
        DecisionActionSlot slot,
        bool legal,
        ReasonCode? firstRejectionCode,
        int baseWeight,
        IEnumerable<ModifierTrace> modifiers,
        int finalWeight,
        int opportunityDebt,
        bool hardOpportunityReady)
    {
        if (string.IsNullOrEmpty(actionId.Value) || baseWeight < 0 || finalWeight < 0 ||
            opportunityDebt < 0 || !Enum.IsDefined(typeof(DecisionActionSlot), slot))
        {
            throw new ArgumentOutOfRangeException(nameof(actionId));
        }

        if (modifiers is null)
        {
            throw new ArgumentNullException(nameof(modifiers));
        }

        var copy = modifiers.ToArray();
        if (legal)
        {
            if (firstRejectionCode.HasValue || copy.Length != StageCodes.Length)
            {
                throw new ArgumentException("A legal score requires exactly six stages and no rejection.");
            }

            for (var index = 0; index < StageCodes.Length; index++)
            {
                if (!StringComparer.Ordinal.Equals(copy[index].Code.Value, StageCodes[index]))
                {
                    throw new ArgumentException("Decision score stages are out of canonical order.", nameof(modifiers));
                }
            }
        }
        else if (!firstRejectionCode.HasValue || copy.Length != 0 || finalWeight != 0 || hardOpportunityReady)
        {
            throw new ArgumentException("An illegal score must contain one rejection and no weight trace.");
        }

        ActionId = actionId;
        Slot = slot;
        Legal = legal;
        FirstRejectionCode = firstRejectionCode;
        BaseWeight = baseWeight;
        _modifiers = new ReadOnlyCollection<ModifierTrace>(copy);
        FinalWeight = finalWeight;
        OpportunityDebt = opportunityDebt;
        HardOpportunityReady = hardOpportunityReady;
    }

    internal StableId ActionId { get; }

    internal DecisionActionSlot Slot { get; }

    internal bool Legal { get; }

    internal ReasonCode? FirstRejectionCode { get; }

    internal int BaseWeight { get; }

    internal IReadOnlyList<ModifierTrace> Modifiers => _modifiers;

    internal int FinalWeight { get; }

    internal int OpportunityDebt { get; }

    internal bool HardOpportunityReady { get; }

    internal static CandidateScore Illegal(DecisionCandidateEvaluation evaluation)
    {
        if (evaluation is null)
        {
            throw new ArgumentNullException(nameof(evaluation));
        }

        if (evaluation.Legal || !evaluation.FirstRejectionCode.HasValue)
        {
            throw new ArgumentException("The evaluation is not illegal.", nameof(evaluation));
        }

        return new CandidateScore(
            evaluation.Action.Id,
            evaluation.Action.Slot,
            false,
            evaluation.FirstRejectionCode,
            evaluation.Action.BaseWeight,
            Array.Empty<ModifierTrace>(),
            0,
            evaluation.OpportunityDebt,
            false);
    }

    internal static CandidateScore LegalScore(
        DecisionCandidateEvaluation evaluation,
        IEnumerable<ModifierTrace> modifiers,
        int finalWeight,
        bool hardOpportunityReady)
    {
        if (evaluation is null)
        {
            throw new ArgumentNullException(nameof(evaluation));
        }

        if (!evaluation.Legal)
        {
            throw new ArgumentException("The evaluation is not legal.", nameof(evaluation));
        }

        return new CandidateScore(
            evaluation.Action.Id,
            evaluation.Action.Slot,
            true,
            null,
            evaluation.Action.BaseWeight,
            modifiers,
            finalWeight,
            evaluation.OpportunityDebt,
            hardOpportunityReady);
    }
}

internal static class DecisionWeightCalculator
{
    private static readonly ReasonCode[] StageCodes =
    {
        new("Tactic"),
        new("Situation"),
        new("Synergy"),
        new("Counter"),
        new("Variety"),
        new("Opportunity"),
    };

    internal static CandidateScore Calculate(
        DecisionCandidateEvaluation evaluation,
        DecisionStageMultipliers multipliers,
        DecisionWeightSettings settings,
        bool hardOpportunityReady = false)
    {
        if (evaluation is null)
        {
            throw new ArgumentNullException(nameof(evaluation));
        }

        if (!evaluation.Legal)
        {
            return CandidateScore.Illegal(evaluation);
        }

        var stageValues = new[]
        {
            multipliers.Tactic,
            multipliers.Situation,
            multipliers.Synergy,
            multipliers.Counter,
            multipliers.Variety,
            multipliers.Opportunity,
        };
        var traces = new ModifierTrace[stageValues.Length];
        var weight = global::Battle.Core.Math.FixedMath.Clamp(
            evaluation.Action.BaseWeight,
            0,
            settings.DecisionWeightMaximum);
        try
        {
            for (var index = 0; index < stageValues.Length; index++)
            {
                var multiplier = global::Battle.Core.Math.FixedMath.Clamp(
                    stageValues[index],
                    settings.MultiplierMinimum,
                    settings.MultiplierMaximum);
                traces[index] = new ModifierTrace(StageCodes[index], multiplier);
                weight = global::Battle.Core.Math.FixedMath.Mul(
                    weight,
                    multiplier,
                    settings.FixedPointScale);
            }
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.DecisionArithmeticOverflow,
                "Decisions",
                "Decision weight arithmetic overflowed: " + exception.Message);
        }

        weight = global::Battle.Core.Math.FixedMath.Clamp(
            weight,
            0,
            settings.DecisionWeightMaximum);
        return CandidateScore.LegalScore(evaluation, traces, weight, hardOpportunityReady);
    }
}

internal static class DecisionMultiplierFolder
{
    internal static int Fold(
        IEnumerable<int> multipliers,
        DecisionWeightSettings settings)
    {
        if (multipliers is null)
        {
            throw new ArgumentNullException(nameof(multipliers));
        }

        var result = settings.FixedPointScale;
        try
        {
            foreach (var value in multipliers)
            {
                result = global::Battle.Core.Math.FixedMath.Mul(
                    result,
                    global::Battle.Core.Math.FixedMath.Clamp(
                        value,
                        settings.MultiplierMinimum,
                        settings.MultiplierMaximum),
                    settings.FixedPointScale);
            }
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                DecisionFailureCodes.DecisionArithmeticOverflow,
                "Decisions",
                "Decision multiplier folding overflowed: " + exception.Message);
        }

        return result;
    }
}

internal static class DecisionTacticMultiplierCalculator
{
    internal static int Calculate(
        DecisionActionProfile action,
        DecisionTacticProfile tactic,
        DecisionWeightSettings settings)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (tactic is null)
        {
            throw new ArgumentNullException(nameof(tactic));
        }

        var values = new List<int>(10);
        AddIf("approach", tactic.ApproachFixedPoint);
        AddIf("block", tactic.BlockFixedPoint);
        AddIf("dodge", tactic.DodgeFixedPoint);
        AddIf("grab", tactic.GrabFixedPoint);
        AddIf("heavy", tactic.HeavyFixedPoint);
        AddIf("light", tactic.LightFixedPoint);
        if (action.Tags.Any(tag =>
                tag.Value.EndsWith("_generator", StringComparison.Ordinal) ||
                StringComparer.Ordinal.Equals(tag.Value, "rhythm")))
        {
            values.Add(tactic.ResourceGeneratorFixedPoint);
        }

        if (action.ResourceCost > 0)
        {
            values.Add(tactic.ResourceSpenderFixedPoint);
        }

        AddIf("retreat", tactic.RetreatFixedPoint);
        AddIf("signature", tactic.SignatureFixedPoint);
        return DecisionMultiplierFolder.Fold(values, settings);

        void AddIf(string tag, int value)
        {
            if (action.HasTag(tag))
            {
                values.Add(value);
            }
        }
    }
}

internal static class DecisionSituationMultiplierCalculator
{
    internal static int Calculate(
        DecisionActionProfile action,
        DecisionFighterView actor,
        DecisionFighterView opponent,
        DecisionTacticProfile tactic,
        DecisionWeightSettings settings,
        int? lowHealthThresholdFixedPoint,
        int arenaMinimum,
        int arenaMaximum,
        int wallZoneSize)
    {
        if (action is null || actor is null || opponent is null || tactic is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (lowHealthThresholdFixedPoint < 0 || wallZoneSize < 0 || arenaMaximum <= arenaMinimum)
        {
            throw new ArgumentOutOfRangeException(nameof(lowHealthThresholdFixedPoint));
        }

        var values = new List<int>(4);
        if (lowHealthThresholdFixedPoint.HasValue &&
            checked((long)actor.Health * settings.FixedPointScale) <=
            checked((long)actor.MaximumHealth * lowHealthThresholdFixedPoint.Value))
        {
            values.Add(tactic.LowHealthFixedPoint);
        }

        if (HasAnyTag(action, "retreat", "dodge", "position", "grab") &&
            IsInWallZone(actor, arenaMinimum, arenaMaximum, wallZoneSize))
        {
            values.Add(tactic.SelfWallFixedPoint);
        }

        if (HasAnyTag(action, "position", "knockback", "wall_impact", "grab") &&
            IsInWallZone(opponent, arenaMinimum, arenaMaximum, wallZoneSize))
        {
            values.Add(tactic.TargetWallFixedPoint);
        }

        if (opponent.State == global::Battle.Contracts.Events.FighterState.Recovery)
        {
            values.Add(tactic.TargetRecoveryFixedPoint);
        }

        return DecisionMultiplierFolder.Fold(values, settings);
    }

    private static bool HasAnyTag(DecisionActionProfile action, params string[] tags) =>
        tags.Any(action.HasTag);

    private static bool IsInWallZone(
        DecisionFighterView fighter,
        int arenaMinimum,
        int arenaMaximum,
        int wallZoneSize)
    {
        var left = (long)fighter.Position - fighter.CollisionRadius - arenaMinimum;
        var right = (long)arenaMaximum - fighter.Position - fighter.CollisionRadius;
        return left <= wallZoneSize || right <= wallZoneSize;
    }
}

internal static class DecisionSynergyMultiplierCalculator
{
    internal static int Calculate(
        DecisionActionProfile action,
        DecisionTagMultiplierProfile passive,
        DecisionTagMultiplierProfile offenseGear,
        DecisionTagMultiplierProfile defenseGear,
        DecisionTagMultiplierProfile utilityGear,
        DecisionWeightSettings settings)
    {
        if (action is null || passive is null || offenseGear is null ||
            defenseGear is null || utilityGear is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        var profiles = new[] { passive, offenseGear, defenseGear, utilityGear };
        return DecisionMultiplierFolder.Fold(
            profiles
                .Where(profile => profile.Tags.Intersect(action.Tags).Any())
                .Select(profile => profile.MultiplierFixedPoint),
            settings);
    }
}

internal static class DecisionCounterMultiplierCalculator
{
    internal static int Calculate(
        DecisionActionProfile action,
        DecisionTacticProfile tactic,
        bool telegraphObserved,
        int fixedPointScale)
    {
        if (action is null || tactic is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (fixedPointScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPointScale));
        }

        return action.HasTag("counter") && telegraphObserved
            ? tactic.CounterFixedPoint
            : fixedPointScale;
    }
}
