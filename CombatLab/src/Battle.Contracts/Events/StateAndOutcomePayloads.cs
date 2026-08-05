using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public sealed class ResourceChangedPayload : CombatEventPayload
{
    public ResourceChangedPayload(
        IEnumerable<EventId> relatedEventIds,
        ResourceKind resourceKind,
        StableId? resourceId,
        int before,
        int delta,
        int after,
        int minimum,
        int maximum,
        ResourceClampReason? clampReason)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(resourceKind, nameof(resourceKind));
        if (clampReason.HasValue)
        {
            PayloadContract.RequireDefined(clampReason.Value, nameof(clampReason));
        }

        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        if (after < minimum || after > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(after));
        }

        ResourceKind = resourceKind;
        ResourceId = resourceId;
        Before = before;
        Delta = delta;
        After = after;
        Minimum = minimum;
        Maximum = maximum;
        ClampReason = clampReason;
    }

    public override CombatEventType EventType => CombatEventType.ResourceChanged;

    public ResourceKind ResourceKind { get; }

    public StableId? ResourceId { get; }

    public int Before { get; }

    public int Delta { get; }

    public int After { get; }

    public int Minimum { get; }

    public int Maximum { get; }

    public ResourceClampReason? ClampReason { get; }
}

public sealed class EffectAddedPayload : CombatEventPayload
{
    public EffectAddedPayload(
        IEnumerable<EventId> relatedEventIds,
        int stacksBefore,
        int stacksAfter,
        int durationTicks,
        EffectExpiryBoundary expiryBoundary,
        EffectStackPolicy stackPolicy)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(stacksBefore, nameof(stacksBefore));
        if (stacksAfter < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stacksAfter));
        }

        PayloadContract.RequireNonNegative(durationTicks, nameof(durationTicks));
        PayloadContract.RequireDefined(expiryBoundary, nameof(expiryBoundary));
        PayloadContract.RequireDefined(stackPolicy, nameof(stackPolicy));

        StacksBefore = stacksBefore;
        StacksAfter = stacksAfter;
        DurationTicks = durationTicks;
        ExpiryBoundary = expiryBoundary;
        StackPolicy = stackPolicy;
    }

    public override CombatEventType EventType => CombatEventType.EffectAdded;

    public int StacksBefore { get; }

    public int StacksAfter { get; }

    public int DurationTicks { get; }

    public EffectExpiryBoundary ExpiryBoundary { get; }

    public EffectStackPolicy StackPolicy { get; }
}

public sealed class EffectRemovedPayload : CombatEventPayload
{
    public EffectRemovedPayload(
        IEnumerable<EventId> relatedEventIds,
        int stacksBefore,
        int stacksAfter,
        EffectRemoveReason removeReason)
        : base(relatedEventIds)
    {
        if (stacksBefore < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stacksBefore));
        }

        PayloadContract.RequireNonNegative(stacksAfter, nameof(stacksAfter));
        PayloadContract.RequireDefined(removeReason, nameof(removeReason));

        StacksBefore = stacksBefore;
        StacksAfter = stacksAfter;
        RemoveReason = removeReason;
    }

    public override CombatEventType EventType => CombatEventType.EffectRemoved;

    public int StacksBefore { get; }

    public int StacksAfter { get; }

    public EffectRemoveReason RemoveReason { get; }
}

public sealed class StateChangedPayload : CombatEventPayload
{
    public StateChangedPayload(
        IEnumerable<EventId> relatedEventIds,
        FighterState oldState,
        FighterState newState,
        int? durationTicks,
        int? controlRatioFixedPoint,
        int? fatigueMultiplierFixedPoint,
        ImmunityResult immunityResult)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(oldState, nameof(oldState));
        PayloadContract.RequireDefined(newState, nameof(newState));
        RequireNullableNonNegative(durationTicks, nameof(durationTicks));
        RequireNullableNonNegative(controlRatioFixedPoint, nameof(controlRatioFixedPoint));
        RequireNullableNonNegative(fatigueMultiplierFixedPoint, nameof(fatigueMultiplierFixedPoint));
        PayloadContract.RequireDefined(immunityResult, nameof(immunityResult));

        OldState = oldState;
        NewState = newState;
        DurationTicks = durationTicks;
        ControlRatioFixedPoint = controlRatioFixedPoint;
        FatigueMultiplierFixedPoint = fatigueMultiplierFixedPoint;
        ImmunityResult = immunityResult;
    }

    public override CombatEventType EventType => CombatEventType.StateChanged;

    public FighterState OldState { get; }

    public FighterState NewState { get; }

    public int? DurationTicks { get; }

    public int? ControlRatioFixedPoint { get; }

    public int? FatigueMultiplierFixedPoint { get; }

    public ImmunityResult ImmunityResult { get; }

    private static void RequireNullableNonNegative(int? value, string parameterName)
    {
        if (value.HasValue)
        {
            PayloadContract.RequireNonNegative(value.Value, parameterName);
        }
    }
}

public sealed class KnockbackAppliedPayload : CombatEventPayload
{
    public KnockbackAppliedPayload(
        IEnumerable<EventId> relatedEventIds,
        int fromPosition,
        int toPosition,
        int requestedMove,
        int actualMove,
        int blockedByWall)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(requestedMove, nameof(requestedMove));
        PayloadContract.RequireNonNegative(actualMove, nameof(actualMove));
        PayloadContract.RequireNonNegative(blockedByWall, nameof(blockedByWall));

        FromPosition = fromPosition;
        ToPosition = toPosition;
        RequestedMove = requestedMove;
        ActualMove = actualMove;
        BlockedByWall = blockedByWall;
    }

    public override CombatEventType EventType => CombatEventType.KnockbackApplied;

    public int FromPosition { get; }

    public int ToPosition { get; }

    public int RequestedMove { get; }

    public int ActualMove { get; }

    public int BlockedByWall { get; }
}

public sealed class WallImpactPayload : CombatEventPayload
{
    public WallImpactPayload(
        IEnumerable<EventId> relatedEventIds,
        MovementDirection wallSide,
        int blockedDistance,
        bool thresholdMet,
        int wallDamage,
        int wallStagger)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(wallSide, nameof(wallSide));
        PayloadContract.RequireNonNegative(blockedDistance, nameof(blockedDistance));
        PayloadContract.RequireNonNegative(wallDamage, nameof(wallDamage));
        PayloadContract.RequireNonNegative(wallStagger, nameof(wallStagger));

        WallSide = wallSide;
        BlockedDistance = blockedDistance;
        ThresholdMet = thresholdMet;
        WallDamage = wallDamage;
        WallStagger = wallStagger;
    }

    public override CombatEventType EventType => CombatEventType.WallImpact;

    public MovementDirection WallSide { get; }

    public int BlockedDistance { get; }

    public bool ThresholdMet { get; }

    public int WallDamage { get; }

    public int WallStagger { get; }
}

public sealed class GrabStartedPayload : CombatEventPayload
{
    public GrabStartedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId grabId,
        FighterId grabberId,
        FighterId grabbedId,
        int holdMaximumTicks,
        GrabPriorityResult priorityResult)
        : base(relatedEventIds)
    {
        PayloadContract.RequireKnownFighter(grabberId, nameof(grabberId));
        PayloadContract.RequireKnownFighter(grabbedId, nameof(grabbedId));
        if (grabberId == grabbedId)
        {
            throw new ArgumentException("Grabber and grabbed fighter must be distinct.", nameof(grabbedId));
        }

        if (holdMaximumTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(holdMaximumTicks));
        }

        PayloadContract.RequireDefined(priorityResult, nameof(priorityResult));

        GrabId = grabId;
        GrabberId = grabberId;
        GrabbedId = grabbedId;
        HoldMaximumTicks = holdMaximumTicks;
        PriorityResult = priorityResult;
    }

    public override CombatEventType EventType => CombatEventType.GrabStarted;

    public ExternalId GrabId { get; }

    public FighterId GrabberId { get; }

    public FighterId GrabbedId { get; }

    public int HoldMaximumTicks { get; }

    public GrabPriorityResult PriorityResult { get; }
}

public sealed class GrabEndedPayload : CombatEventPayload
{
    public GrabEndedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId grabId,
        GrabEndReason endReason,
        StableId? throwActionId,
        int grabberPosition,
        int grabbedPosition)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(endReason, nameof(endReason));

        GrabId = grabId;
        EndReason = endReason;
        ThrowActionId = throwActionId;
        GrabberPosition = grabberPosition;
        GrabbedPosition = grabbedPosition;
    }

    public override CombatEventType EventType => CombatEventType.GrabEnded;

    public ExternalId GrabId { get; }

    public GrabEndReason EndReason { get; }

    public StableId? ThrowActionId { get; }

    public int GrabberPosition { get; }

    public int GrabbedPosition { get; }
}

public sealed class FinisherTriggeredPayload : CombatEventPayload
{
    public FinisherTriggeredPayload(
        IEnumerable<EventId> relatedEventIds,
        EventId predictedLethalEventId,
        FinisherMarkerKind markerKind,
        FinisherConfidence confidence = FinisherConfidence.GuaranteedByCurrentIntent)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(markerKind, nameof(markerKind));
        PayloadContract.RequireDefined(confidence, nameof(confidence));

        PredictedLethalEventId = predictedLethalEventId;
        MarkerKind = markerKind;
        Confidence = confidence;
    }

    public override CombatEventType EventType => CombatEventType.FinisherTriggered;

    public EventId PredictedLethalEventId { get; }

    public FinisherMarkerKind MarkerKind { get; }

    public FinisherConfidence Confidence { get; }
}

public sealed class FighterDefeatedPayload : CombatEventPayload
{
    public FighterDefeatedPayload(
        IEnumerable<EventId> relatedEventIds,
        FighterId defeatedFighterId,
        EventId? lethalSourceEventId,
        ExternalId? simultaneousGroupId,
        int finalHealth)
        : base(relatedEventIds)
    {
        PayloadContract.RequireKnownFighter(defeatedFighterId, nameof(defeatedFighterId));
        PayloadContract.RequireNonNegative(finalHealth, nameof(finalHealth));

        DefeatedFighterId = defeatedFighterId;
        LethalSourceEventId = lethalSourceEventId;
        SimultaneousGroupId = simultaneousGroupId;
        FinalHealth = finalHealth;
    }

    public override CombatEventType EventType => CombatEventType.FighterDefeated;

    public FighterId DefeatedFighterId { get; }

    public EventId? LethalSourceEventId { get; }

    public ExternalId? SimultaneousGroupId { get; }

    public int FinalHealth { get; }
}

public sealed class TimeoutReachedPayload : CombatEventPayload
{
    public TimeoutReachedPayload(
        IEnumerable<EventId> relatedEventIds,
        int fighterAHealth,
        int fighterAMaxHealth,
        int fighterBHealth,
        int fighterBMaxHealth,
        long leftCrossProduct,
        long rightCrossProduct)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(fighterAHealth, nameof(fighterAHealth));
        if (fighterAMaxHealth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterAMaxHealth));
        }

        PayloadContract.RequireNonNegative(fighterBHealth, nameof(fighterBHealth));
        if (fighterBMaxHealth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterBMaxHealth));
        }

        PayloadContract.RequireNonNegative(leftCrossProduct, nameof(leftCrossProduct));
        PayloadContract.RequireNonNegative(rightCrossProduct, nameof(rightCrossProduct));

        FighterAHealth = fighterAHealth;
        FighterAMaxHealth = fighterAMaxHealth;
        FighterBHealth = fighterBHealth;
        FighterBMaxHealth = fighterBMaxHealth;
        LeftCrossProduct = leftCrossProduct;
        RightCrossProduct = rightCrossProduct;
    }

    public override CombatEventType EventType => CombatEventType.TimeoutReached;

    public int FighterAHealth { get; }

    public int FighterAMaxHealth { get; }

    public int FighterBHealth { get; }

    public int FighterBMaxHealth { get; }

    public long LeftCrossProduct { get; }

    public long RightCrossProduct { get; }
}

public sealed class DrawDeclaredPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<FighterId> _participantIds;

    public DrawDeclaredPayload(
        IEnumerable<EventId> relatedEventIds,
        DrawReason drawReason,
        IEnumerable<FighterId> participantIds,
        ExternalId? simultaneousGroupId)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(drawReason, nameof(drawReason));
        var participants = PayloadContract.Copy(
            participantIds,
            2,
            2,
            nameof(participantIds),
            requireUnique: true);
        if (participants[0] != FighterId.FighterA || participants[1] != FighterId.FighterB)
        {
            throw new ArgumentException(
                "Participants must contain fighter A followed by fighter B.",
                nameof(participantIds));
        }

        DrawReason = drawReason;
        _participantIds = participants;
        SimultaneousGroupId = simultaneousGroupId;
    }

    public override CombatEventType EventType => CombatEventType.DrawDeclared;

    public DrawReason DrawReason { get; }

    public IReadOnlyList<FighterId> ParticipantIds => _participantIds;

    public ExternalId? SimultaneousGroupId { get; }
}
