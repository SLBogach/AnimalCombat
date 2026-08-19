using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Core.Decisions;

internal static class DecisionRejectionCodes
{
    internal static ReasonCode ActorNotDecisionReady { get; } = new("ActorNotDecisionReady");
    internal static ReasonCode CategoryUnavailable { get; } = new("CategoryUnavailable");
    internal static ReasonCode ActionNotAllowedByMode { get; } = new("ActionNotAllowedByMode");
    internal static ReasonCode WrongOwner { get; } = new("WrongOwner");
    internal static ReasonCode WrongSlot { get; } = new("WrongSlot");
    internal static ReasonCode ActionNotInLoadout { get; } = new("ActionNotInLoadout");
    internal static ReasonCode CooldownActive { get; } = new("CooldownActive");
    internal static ReasonCode InsufficientEnergy { get; } = new("InsufficientEnergy");
    internal static ReasonCode InsufficientResource { get; } = new("InsufficientResource");
    internal static ReasonCode TargetUnavailable { get; } = new("TargetUnavailable");
    internal static ReasonCode TargetDefeated { get; } = new("TargetDefeated");
    internal static ReasonCode OutOfDecisionRange { get; } = new("OutOfDecisionRange");
    internal static ReasonCode SystemBandUnavailable { get; } = new("SystemBandUnavailable");
    internal static ReasonCode NoMovementHeadroom { get; } = new("NoMovementHeadroom");
    internal static ReasonCode TelegraphNotObserved { get; } = new("TelegraphNotObserved");
    internal static ReasonCode MaxConsecutiveUses { get; } = new("MaxConsecutiveUses");
}

internal static class DecisionCatalogBuilder
{
    internal static IReadOnlyList<DecisionActionProfile> BuildCheckedCatalog(
        IEnumerable<DecisionActionProfile> actions,
        StableId actorAnimalId)
    {
        if (actions is null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        if (string.IsNullOrEmpty(actorAnimalId.Value))
        {
            throw new ArgumentException("An actor animal ID is required.", nameof(actorAnimalId));
        }

        var checkedActions = actions
            .Where(action => action is not null)
            .Where(action =>
                action.Slot == DecisionActionSlot.System ||
                action.OwnerAnimalId == actorAnimalId)
            .OrderBy(action => action.Id)
            .ToArray();
        if (checkedActions.Select(action => action.Id).Distinct().Count() != checkedActions.Length)
        {
            throw new ArgumentException("The decision catalog contains duplicate action IDs.", nameof(actions));
        }

        return new ReadOnlyCollection<DecisionActionProfile>(checkedActions);
    }
}

internal sealed record DecisionAvailabilityContext(
    DecisionBatchSnapshot Snapshot,
    FighterId ActorId,
    DecisionAvailabilitySettings Settings,
    bool RequiredTargetExists = true)
{
    internal DecisionFighterView Actor => Snapshot.Get(ActorId);

    internal DecisionFighterView Opponent => Snapshot.GetOpponent(ActorId);
}

internal sealed class DecisionCandidateEvaluation
{
    private DecisionCandidateEvaluation(
        DecisionActionProfile action,
        bool legal,
        ReasonCode? firstRejectionCode,
        int opportunityDebt)
    {
        Action = action;
        Legal = legal;
        FirstRejectionCode = firstRejectionCode;
        OpportunityDebt = opportunityDebt;
    }

    internal DecisionActionProfile Action { get; }

    internal bool Legal { get; }

    internal ReasonCode? FirstRejectionCode { get; }

    internal int OpportunityDebt { get; }

    internal static DecisionCandidateEvaluation Accept(
        DecisionActionProfile action,
        int opportunityDebt) => new(action, true, null, opportunityDebt);

    internal static DecisionCandidateEvaluation Reject(
        DecisionActionProfile action,
        ReasonCode reason,
        int opportunityDebt) => new(action, false, reason, opportunityDebt);
}

internal static class DecisionAvailabilityEvaluator
{
    internal static DecisionCandidateEvaluation Evaluate(
        DecisionActionProfile action,
        DecisionAvailabilityContext context)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return EvaluateBeforeRepeatCap(action, context);
    }

    internal static IReadOnlyList<DecisionCandidateEvaluation> EvaluateCatalog(
        IEnumerable<DecisionActionProfile> actions,
        DecisionAvailabilityContext context)
    {
        if (actions is null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var baseEvaluations = actions
            .OrderBy(action => action.Id)
            .Select(action => EvaluateBeforeRepeatCap(action, context))
            .ToArray();

        var baseLegal = baseEvaluations.Where(item => item.Legal).ToArray();
        var capped = baseLegal
            .Where(item => DecisionVariety.IsAtRepeatCap(item.Action, context.Actor.History))
            .Select(item => item.Action.Id)
            .ToHashSet();
        if (capped.Count == 0 || capped.Count == baseLegal.Length)
        {
            return new ReadOnlyCollection<DecisionCandidateEvaluation>(baseEvaluations);
        }

        var final = baseEvaluations
            .Select(item => item.Legal && capped.Contains(item.Action.Id)
                ? DecisionCandidateEvaluation.Reject(
                    item.Action,
                    DecisionRejectionCodes.MaxConsecutiveUses,
                    item.OpportunityDebt)
                : item)
            .ToArray();
        return new ReadOnlyCollection<DecisionCandidateEvaluation>(final);
    }

    private static DecisionCandidateEvaluation EvaluateBeforeRepeatCap(
        DecisionActionProfile action,
        DecisionAvailabilityContext context)
    {
        var actor = context.Actor;
        var opponent = context.Opponent;
        var debt = actor.OpportunityDebtFor(action.Id);

        if (!actor.IsDecisionReady)
        {
            return Reject(DecisionRejectionCodes.ActorNotDecisionReady);
        }

        if (!context.Settings.IsCategoryPermitted(action.Category))
        {
            return Reject(DecisionRejectionCodes.CategoryUnavailable);
        }

        if (!context.Settings.IsActionAllowed(action.Id))
        {
            return Reject(DecisionRejectionCodes.ActionNotAllowedByMode);
        }

        if (action.Slot != DecisionActionSlot.System &&
            action.OwnerAnimalId!.Value != actor.Build.AnimalId)
        {
            return Reject(DecisionRejectionCodes.WrongOwner);
        }

        var selectedAsSpecial = actor.Build.SpecialActionIds.Contains(action.Id);
        if (action.Slot != DecisionActionSlot.Special && selectedAsSpecial)
        {
            return Reject(DecisionRejectionCodes.WrongSlot);
        }

        if (action.Slot == DecisionActionSlot.Special && !selectedAsSpecial)
        {
            return Reject(DecisionRejectionCodes.ActionNotInLoadout);
        }

        if (actor.CooldownFor(action.Id) != 0)
        {
            return Reject(DecisionRejectionCodes.CooldownActive);
        }

        if (actor.Energy < action.EnergyCost)
        {
            return Reject(DecisionRejectionCodes.InsufficientEnergy);
        }

        if (actor.Resource < action.ResourceCost)
        {
            return Reject(DecisionRejectionCodes.InsufficientResource);
        }

        if (action.TargetKind == DecisionTargetKind.Opponent)
        {
            if (!context.RequiredTargetExists)
            {
                return Reject(DecisionRejectionCodes.TargetUnavailable);
            }

            if (opponent.State == global::Battle.Contracts.Events.FighterState.Defeated)
            {
                return Reject(DecisionRejectionCodes.TargetDefeated);
            }
        }

        var surfaceGap = SurfaceGap(actor, opponent);
        if (!IsInDecisionRange(action, context.Settings, actor, opponent, surfaceGap))
        {
            return Reject(action.Slot == DecisionActionSlot.System
                ? DecisionRejectionCodes.SystemBandUnavailable
                : DecisionRejectionCodes.OutOfDecisionRange);
        }

        var direction = RequiredDirection(action, actor, opponent, surfaceGap);
        if (direction != 0 && DirectionalHeadroom(actor, context.Settings, direction) == 0)
        {
            return Reject(DecisionRejectionCodes.NoMovementHeadroom);
        }

        if (action.HasTag("counter") && !IsTelegraphObserved(context.Snapshot.Tick, actor, opponent))
        {
            return Reject(DecisionRejectionCodes.TelegraphNotObserved);
        }

        return DecisionCandidateEvaluation.Accept(action, debt);

        DecisionCandidateEvaluation Reject(ReasonCode reason) =>
            DecisionCandidateEvaluation.Reject(action, reason, debt);
    }

    private static long SurfaceGap(DecisionFighterView actor, DecisionFighterView opponent)
    {
        var centerDistance = global::System.Math.Abs((long)actor.Position - opponent.Position);
        var gap = centerDistance - actor.CollisionRadius - opponent.CollisionRadius;
        return gap <= 0 ? 0 : gap;
    }

    private static bool IsInDecisionRange(
        DecisionActionProfile action,
        DecisionAvailabilitySettings settings,
        DecisionFighterView actor,
        DecisionFighterView opponent,
        long gap)
    {
        if (action.Slot == DecisionActionSlot.System)
        {
            return action.MovementMode switch
            {
                DecisionMovementMode.Retreat => gap < settings.SystemNeutralMinimum,
                DecisionMovementMode.None =>
                    gap >= settings.SystemNeutralMinimum && gap <= settings.SystemNeutralMaximum ||
                    gap < settings.SystemNeutralMinimum &&
                    DirectionalHeadroom(
                         actor,
                         settings,
                         actor.Position < opponent.Position ? -1 : 1) == 0,
                DecisionMovementMode.Approach => gap > settings.SystemNeutralMaximum,
                _ => false,
            };
        }

        if (action.TargetKind == DecisionTargetKind.Self)
        {
            return action.MovementMode switch
            {
                DecisionMovementMode.None or DecisionMovementMode.Retreat => true,
                DecisionMovementMode.Approach => gap > action.PreferredRangeMaximum,
                DecisionMovementMode.Adaptive =>
                    gap < action.PreferredRangeMinimum || gap > action.PreferredRangeMaximum,
                _ => false,
            };
        }

        return action.MovementMode is DecisionMovementMode.Approach or DecisionMovementMode.Follow
            ? gap >= action.PreferredRangeMinimum && gap <= action.PreferredRangeMaximum
            : gap >= action.HitRangeMinimum && gap <= action.HitRangeMaximum;
    }

    private static int RequiredDirection(
        DecisionActionProfile action,
        DecisionFighterView actor,
        DecisionFighterView opponent,
        long gap)
    {
        var toward = actor.Position < opponent.Position ? 1 : -1;
        return action.MovementMode switch
        {
            DecisionMovementMode.Approach or DecisionMovementMode.Follow or DecisionMovementMode.Push => toward,
            DecisionMovementMode.Retreat => -toward,
            DecisionMovementMode.Adaptive => gap > action.PreferredRangeMaximum ? toward : -toward,
            _ => 0,
        };
    }

    private static long DirectionalHeadroom(
        DecisionFighterView actor,
        DecisionAvailabilitySettings settings,
        int direction)
    {
        var headroom = direction < 0
            ? (long)actor.Position - actor.CollisionRadius - settings.ArenaMinimum
            : (long)settings.ArenaMaximum - actor.Position - actor.CollisionRadius;
        return headroom <= 0 ? 0 : headroom;
    }

    private static bool IsTelegraphObserved(
        int tick,
        DecisionFighterView actor,
        DecisionFighterView opponent)
    {
        if (opponent.Telegraph is null || opponent.Telegraph.CommitTick > tick)
        {
            return false;
        }

        return tick - opponent.Telegraph.CommitTick >= actor.PerceptionDelayTicks;
    }
}
