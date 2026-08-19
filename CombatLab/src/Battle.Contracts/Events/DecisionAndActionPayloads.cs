using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public sealed class DecisionMadePayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<StableId> _legalActionIds;
    private readonly ReadOnlyCollection<ModifierTrace> _dominantModifiers;

    public DecisionMadePayload(
        IEnumerable<EventId> relatedEventIds,
        StableId chosenActionId,
        IEnumerable<StableId> legalActionIds,
        int candidateCount,
        int chosenWeight,
        int weightSum,
        DecisionSelectionMode selectionMode,
        IEnumerable<ModifierTrace> dominantModifiers)
        : base(relatedEventIds)
    {
        if (candidateCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        }

        PayloadContract.RequireNonNegative(chosenWeight, nameof(chosenWeight));
        PayloadContract.RequireNonNegative(weightSum, nameof(weightSum));
        PayloadContract.RequireDefined(selectionMode, nameof(selectionMode));

        ChosenActionId = chosenActionId;
        _legalActionIds = PayloadContract.Copy(
            legalActionIds,
            1,
            128,
            nameof(legalActionIds),
            requireUnique: true);
        PayloadContract.RequireStrictlySorted(
            _legalActionIds,
            static (left, right) => left.CompareTo(right),
            nameof(legalActionIds));
        if (candidateCount != _legalActionIds.Count)
        {
            throw new ArgumentException(
                "Candidate count must equal the number of legal action IDs.",
                nameof(candidateCount));
        }

        if (!_legalActionIds.Contains(chosenActionId))
        {
            throw new ArgumentException(
                "The chosen action must belong to the legal action set.",
                nameof(chosenActionId));
        }
        CandidateCount = candidateCount;
        ChosenWeight = chosenWeight;
        WeightSum = weightSum;
        SelectionMode = selectionMode;
        _dominantModifiers = PayloadContract.Copy(
            dominantModifiers,
            0,
            6,
            nameof(dominantModifiers));
        if (_dominantModifiers.Select(item => item.Code).Distinct().Count() !=
            _dominantModifiers.Count)
        {
            throw new ArgumentException(
                "Dominant modifier reason codes must be unique.",
                nameof(dominantModifiers));
        }
    }

    public override CombatEventType EventType => CombatEventType.DecisionMade;

    public StableId ChosenActionId { get; }

    public IReadOnlyList<StableId> LegalActionIds => _legalActionIds;

    public int CandidateCount { get; }

    public int ChosenWeight { get; }

    public int WeightSum { get; }

    public DecisionSelectionMode SelectionMode { get; }

    public IReadOnlyList<ModifierTrace> DominantModifiers => _dominantModifiers;
}

public sealed class ActionCommittedPayload : CombatEventPayload
{
    public ActionCommittedPayload(
        IEnumerable<EventId> relatedEventIds,
        FighterId? targetFighterId,
        int energyCost,
        int resourceCost,
        int startupTicks,
        int activeTicks,
        int recoveryTicks,
        int cooldownTicks,
        CommitDirection commitDirection,
        int? targetPositionAtCommit)
        : base(relatedEventIds)
    {
        PayloadContract.RequireKnownFighter(targetFighterId, nameof(targetFighterId));
        PayloadContract.RequireNonNegative(energyCost, nameof(energyCost));
        PayloadContract.RequireNonNegative(resourceCost, nameof(resourceCost));
        PayloadContract.RequireNonNegative(startupTicks, nameof(startupTicks));
        PayloadContract.RequireNonNegative(activeTicks, nameof(activeTicks));
        PayloadContract.RequireNonNegative(recoveryTicks, nameof(recoveryTicks));
        PayloadContract.RequireNonNegative(cooldownTicks, nameof(cooldownTicks));
        PayloadContract.RequireDefined(commitDirection, nameof(commitDirection));

        TargetFighterId = targetFighterId;
        EnergyCost = energyCost;
        ResourceCost = resourceCost;
        StartupTicks = startupTicks;
        ActiveTicks = activeTicks;
        RecoveryTicks = recoveryTicks;
        CooldownTicks = cooldownTicks;
        CommitDirection = commitDirection;
        TargetPositionAtCommit = targetPositionAtCommit;
    }

    public override CombatEventType EventType => CombatEventType.ActionCommitted;

    public FighterId? TargetFighterId { get; }

    public int EnergyCost { get; }

    public int ResourceCost { get; }

    public int StartupTicks { get; }

    public int ActiveTicks { get; }

    public int RecoveryTicks { get; }

    public int CooldownTicks { get; }

    public CommitDirection CommitDirection { get; }

    public int? TargetPositionAtCommit { get; }
}

public sealed class ActionPhaseChangedPayload : CombatEventPayload
{
    public ActionPhaseChangedPayload(
        IEnumerable<EventId> relatedEventIds,
        ActionPhase? fromPhase,
        ActionPhase? toPhase,
        int phaseTicks)
        : base(relatedEventIds)
    {
        if (fromPhase.HasValue)
        {
            PayloadContract.RequireDefined(fromPhase.Value, nameof(fromPhase));
        }

        if (toPhase.HasValue)
        {
            PayloadContract.RequireDefined(toPhase.Value, nameof(toPhase));
        }

        PayloadContract.RequireNonNegative(phaseTicks, nameof(phaseTicks));

        FromPhase = fromPhase;
        ToPhase = toPhase;
        PhaseTicks = phaseTicks;
    }

    public override CombatEventType EventType => CombatEventType.ActionPhaseChanged;

    public ActionPhase? FromPhase { get; }

    public ActionPhase? ToPhase { get; }

    public int PhaseTicks { get; }
}

public sealed class ActionCancelledPayload : CombatEventPayload
{
    private readonly ReadOnlyCollection<ExternalId> _survivingIntentIds;

    public ActionCancelledPayload(
        IEnumerable<EventId> relatedEventIds,
        ActionPhase? cancelledPhase,
        ReasonCode cancelReason,
        IEnumerable<ExternalId> survivingIntentIds)
        : base(relatedEventIds)
    {
        if (cancelledPhase.HasValue)
        {
            PayloadContract.RequireDefined(cancelledPhase.Value, nameof(cancelledPhase));
        }

        CancelledPhase = cancelledPhase;
        CancelReason = cancelReason;
        _survivingIntentIds = PayloadContract.Copy(
            survivingIntentIds,
            0,
            32,
            nameof(survivingIntentIds),
            requireUnique: true);
    }

    public override CombatEventType EventType => CombatEventType.ActionCancelled;

    public ActionPhase? CancelledPhase { get; }

    public ReasonCode CancelReason { get; }

    public IReadOnlyList<ExternalId> SurvivingIntentIds => _survivingIntentIds;
}
