using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public sealed class MoveStartedPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<ReasonCode> _stopConditions;

    public MoveStartedPayload(
        IEnumerable<EventId> relatedEventIds,
        int fromPosition,
        MovementDirection direction,
        int speedPerTick,
        MoveStartKind movementKind,
        IEnumerable<ReasonCode> stopConditions)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(direction, nameof(direction));
        PayloadContract.RequireNonNegative(speedPerTick, nameof(speedPerTick));
        PayloadContract.RequireDefined(movementKind, nameof(movementKind));

        FromPosition = fromPosition;
        Direction = direction;
        SpeedPerTick = speedPerTick;
        MovementKind = movementKind;
        _stopConditions = PayloadContract.Copy(
            stopConditions,
            0,
            16,
            nameof(stopConditions),
            requireUnique: true);
    }

    public override CombatEventType EventType => CombatEventType.MoveStarted;

    public int FromPosition { get; }

    public MovementDirection Direction { get; }

    public int SpeedPerTick { get; }

    public MoveStartKind MovementKind { get; }

    public IReadOnlyList<ReasonCode> StopConditions => _stopConditions;
}

public sealed class PositionChangedPayload : CombatEventPayload
{
    public PositionChangedPayload(
        IEnumerable<EventId> relatedEventIds,
        int fromPosition,
        int toPosition,
        int requestedDelta,
        int actualDelta,
        int blockedByWall,
        PositionChangeKind movementKind)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(blockedByWall, nameof(blockedByWall));
        PayloadContract.RequireDefined(movementKind, nameof(movementKind));

        FromPosition = fromPosition;
        ToPosition = toPosition;
        RequestedDelta = requestedDelta;
        ActualDelta = actualDelta;
        BlockedByWall = blockedByWall;
        MovementKind = movementKind;
    }

    public override CombatEventType EventType => CombatEventType.PositionChanged;

    public int FromPosition { get; }

    public int ToPosition { get; }

    public int RequestedDelta { get; }

    public int ActualDelta { get; }

    public int BlockedByWall { get; }

    public PositionChangeKind MovementKind { get; }
}

public sealed class MoveEndedPayload : CombatEventPayload
{
    public MoveEndedPayload(
        IEnumerable<EventId> relatedEventIds,
        int fromPosition,
        int toPosition,
        ReasonCode stopReason)
        : base(relatedEventIds)
    {
        FromPosition = fromPosition;
        ToPosition = toPosition;
        StopReason = stopReason;
    }

    public override CombatEventType EventType => CombatEventType.MoveEnded;

    public int FromPosition { get; }

    public int ToPosition { get; }

    public ReasonCode StopReason { get; }
}

public sealed class AttackPreparedPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<int> _impactTicks;

    public AttackPreparedPayload(
        IEnumerable<EventId> relatedEventIds,
        int telegraphTick,
        IEnumerable<int> impactTicks,
        bool directionLocked,
        FighterId? targetFighterId)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(telegraphTick, nameof(telegraphTick));
        PayloadContract.RequireKnownFighter(targetFighterId, nameof(targetFighterId));
        var ticks = PayloadContract.Copy(
            impactTicks,
            1,
            32,
            nameof(impactTicks),
            requireUnique: true);
        foreach (var tick in ticks)
        {
            PayloadContract.RequireNonNegative(tick, nameof(impactTicks));
        }

        TelegraphTick = telegraphTick;
        _impactTicks = ticks;
        DirectionLocked = directionLocked;
        TargetFighterId = targetFighterId;
    }

    public override CombatEventType EventType => CombatEventType.AttackPrepared;

    public int TelegraphTick { get; }

    public IReadOnlyList<int> ImpactTicks => _impactTicks;

    public bool DirectionLocked { get; }

    public FighterId? TargetFighterId { get; }
}

public sealed class ConflictResolvedPayload : CombatEventPayload
{
    public ConflictResolvedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId conflictId,
        ExternalId intentAId,
        ExternalId intentBId,
        StableId categoryA,
        StableId categoryB,
        int priorityA,
        int priorityB,
        ExternalId? winnerIntentId,
        ConflictResolutionResult result,
        ConflictTieBreakMethod tieBreakMethod)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(result, nameof(result));
        PayloadContract.RequireDefined(tieBreakMethod, nameof(tieBreakMethod));

        ConflictId = conflictId;
        IntentAId = intentAId;
        IntentBId = intentBId;
        CategoryA = categoryA;
        CategoryB = categoryB;
        PriorityA = priorityA;
        PriorityB = priorityB;
        WinnerIntentId = winnerIntentId;
        Result = result;
        TieBreakMethod = tieBreakMethod;
    }

    public override CombatEventType EventType => CombatEventType.ConflictResolved;

    public ExternalId ConflictId { get; }

    public ExternalId IntentAId { get; }

    public ExternalId IntentBId { get; }

    public StableId CategoryA { get; }

    public StableId CategoryB { get; }

    public int PriorityA { get; }

    public int PriorityB { get; }

    public ExternalId? WinnerIntentId { get; }

    public ConflictResolutionResult Result { get; }

    public ConflictTieBreakMethod TieBreakMethod { get; }
}

public sealed class AttackHitPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<StableId> _attackTags;

    public AttackHitPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId impactId,
        ExternalId hitGroupId,
        int gap,
        int hitRangeMinimum,
        int hitRangeMaximum,
        MovementDirection hitDirection,
        IEnumerable<StableId> attackTags)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(gap, nameof(gap));
        ValidateRange(hitRangeMinimum, hitRangeMaximum);
        PayloadContract.RequireDefined(hitDirection, nameof(hitDirection));

        ImpactId = impactId;
        HitGroupId = hitGroupId;
        Gap = gap;
        HitRangeMinimum = hitRangeMinimum;
        HitRangeMaximum = hitRangeMaximum;
        HitDirection = hitDirection;
        _attackTags = PayloadContract.Copy(
            attackTags,
            0,
            32,
            nameof(attackTags),
            requireUnique: true);
    }

    public override CombatEventType EventType => CombatEventType.AttackHit;

    public ExternalId ImpactId { get; }

    public ExternalId HitGroupId { get; }

    public int Gap { get; }

    public int HitRangeMinimum { get; }

    public int HitRangeMaximum { get; }

    public MovementDirection HitDirection { get; }

    public IReadOnlyList<StableId> AttackTags => _attackTags;

    internal static void ValidateRange(int minimum, int maximum)
    {
        PayloadContract.RequireNonNegative(minimum, nameof(minimum));
        PayloadContract.RequireNonNegative(maximum, nameof(maximum));
        if (maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }
    }
}

public sealed class AttackMissedPayload : CombatEventPayload
{
    public AttackMissedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId impactId,
        ExternalId hitGroupId,
        AttackMissReason missReason,
        int gap,
        int hitRangeMinimum,
        int hitRangeMaximum)
        : base(relatedEventIds)
    {
        PayloadContract.RequireDefined(missReason, nameof(missReason));
        PayloadContract.RequireNonNegative(gap, nameof(gap));
        AttackHitPayload.ValidateRange(hitRangeMinimum, hitRangeMaximum);

        ImpactId = impactId;
        HitGroupId = hitGroupId;
        MissReason = missReason;
        Gap = gap;
        HitRangeMinimum = hitRangeMinimum;
        HitRangeMaximum = hitRangeMaximum;
    }

    public override CombatEventType EventType => CombatEventType.AttackMissed;

    public ExternalId ImpactId { get; }

    public ExternalId HitGroupId { get; }

    public AttackMissReason MissReason { get; }

    public int Gap { get; }

    public int HitRangeMinimum { get; }

    public int HitRangeMaximum { get; }
}

public sealed class BlockedPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<ExternalId> _cancelledIntentIds;

    public BlockedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId impactId,
        StableId defenseActionId,
        int chanceFixedPoint,
        int damageReductionFixedPoint,
        bool guardBreak,
        IEnumerable<ExternalId> cancelledIntentIds)
        : base(relatedEventIds)
    {
        PayloadContract.RequireFixedPoint(chanceFixedPoint, nameof(chanceFixedPoint));
        PayloadContract.RequireFixedPoint(damageReductionFixedPoint, nameof(damageReductionFixedPoint));

        ImpactId = impactId;
        DefenseActionId = defenseActionId;
        ChanceFixedPoint = chanceFixedPoint;
        DamageReductionFixedPoint = damageReductionFixedPoint;
        GuardBreak = guardBreak;
        _cancelledIntentIds = PayloadContract.Copy(
            cancelledIntentIds,
            0,
            32,
            nameof(cancelledIntentIds),
            requireUnique: true);
    }

    public override CombatEventType EventType => CombatEventType.Blocked;

    public ExternalId ImpactId { get; }

    public StableId DefenseActionId { get; }

    public int ChanceFixedPoint { get; }

    public int DamageReductionFixedPoint { get; }

    public bool GuardBreak { get; }

    public IReadOnlyList<ExternalId> CancelledIntentIds => _cancelledIntentIds;
}

public sealed class DodgedPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<ExternalId> _cancelledIntentIds;

    public DodgedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId impactId,
        StableId defenseActionId,
        int chanceFixedPoint,
        int exitPosition,
        IEnumerable<ExternalId> cancelledIntentIds)
        : base(relatedEventIds)
    {
        PayloadContract.RequireFixedPoint(chanceFixedPoint, nameof(chanceFixedPoint));

        ImpactId = impactId;
        DefenseActionId = defenseActionId;
        ChanceFixedPoint = chanceFixedPoint;
        ExitPosition = exitPosition;
        _cancelledIntentIds = PayloadContract.Copy(
            cancelledIntentIds,
            0,
            32,
            nameof(cancelledIntentIds),
            requireUnique: true);
    }

    public override CombatEventType EventType => CombatEventType.Dodged;

    public ExternalId ImpactId { get; }

    public StableId DefenseActionId { get; }

    public int ChanceFixedPoint { get; }

    public int ExitPosition { get; }

    public IReadOnlyList<ExternalId> CancelledIntentIds => _cancelledIntentIds;
}

public sealed class CounteredPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<ExternalId> _cancelledIntentIds;

    public CounteredPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId impactId,
        StableId counterActionId,
        StableId matchedTag,
        IEnumerable<ExternalId> cancelledIntentIds)
        : base(relatedEventIds)
    {
        ImpactId = impactId;
        CounterActionId = counterActionId;
        MatchedTag = matchedTag;
        _cancelledIntentIds = PayloadContract.Copy(
            cancelledIntentIds,
            0,
            32,
            nameof(cancelledIntentIds),
            requireUnique: true);
    }

    public override CombatEventType EventType => CombatEventType.Countered;

    public ExternalId ImpactId { get; }

    public StableId CounterActionId { get; }

    public StableId MatchedTag { get; }

    public IReadOnlyList<ExternalId> CancelledIntentIds => _cancelledIntentIds;
}

public sealed class DamageAppliedPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<StableId> _damageTags;

    public DamageAppliedPayload(
        IEnumerable<EventId> relatedEventIds,
        ExternalId impactId,
        ExternalId damageId,
        DamageBreakdown breakdown,
        int healthBefore,
        int healthAfter,
        IEnumerable<StableId> damageTags,
        bool lethal)
        : base(relatedEventIds)
    {
        PayloadContract.RequireNonNegative(healthBefore, nameof(healthBefore));
        PayloadContract.RequireNonNegative(healthAfter, nameof(healthAfter));

        ImpactId = impactId;
        DamageId = damageId;
        Breakdown = breakdown;
        HealthBefore = healthBefore;
        HealthAfter = healthAfter;
        _damageTags = PayloadContract.Copy(
            damageTags,
            0,
            32,
            nameof(damageTags),
            requireUnique: true);
        Lethal = lethal;
    }

    public override CombatEventType EventType => CombatEventType.DamageApplied;

    public ExternalId ImpactId { get; }

    public ExternalId DamageId { get; }

    public DamageBreakdown Breakdown { get; }

    public int HealthBefore { get; }

    public int HealthAfter { get; }

    public IReadOnlyList<StableId> DamageTags => _damageTags;

    public bool Lethal { get; }
}
