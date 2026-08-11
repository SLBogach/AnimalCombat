using Battle.Core.Decisions;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using System.Globalization;

namespace Battle.Core.Engine;

internal sealed class FighterRuntimeState
{
    private readonly Dictionary<StableId, int> _cooldowns = new();
    private readonly List<EffectFrame> _effects = new();

    internal FighterRuntimeState(
        FighterId fighterId,
        FighterSide side,
        StableId animalId,
        int position,
        Facing facing,
        int maximumHealth,
        int maximumEnergy,
        StableId resourceId,
        int resource,
        int maximumResource,
        int staggerThreshold,
        int initiative,
        int moveSpeed,
        int collisionRadius)
    {
        FighterId = fighterId;
        Side = side;
        AnimalId = animalId;
        Position = position;
        Facing = facing;
        MaximumHealth = maximumHealth;
        Health = maximumHealth;
        MaximumEnergy = maximumEnergy;
        Energy = maximumEnergy;
        ResourceId = resourceId;
        Resource = resource;
        MaximumResource = maximumResource;
        StaggerThreshold = staggerThreshold;
        Initiative = initiative;
        MoveSpeed = moveSpeed;
        CollisionRadius = collisionRadius;
        State = FighterState.DecisionReady;
    }

    internal FighterId FighterId { get; }

    internal FighterSide Side { get; }

    internal StableId AnimalId { get; }

    internal int Position { get; private set; }

    internal Facing Facing { get; private set; }

    internal FighterState State { get; private set; }

    internal int? StateTicksRemaining { get; private set; }

    internal StableId? ActionId { get; private set; }

    internal ActionPhase? ActionPhase { get; private set; }

    internal int Health { get; private set; }

    internal int MaximumHealth { get; }

    internal int Energy { get; private set; }

    internal int MaximumEnergy { get; }

    internal StableId ResourceId { get; }

    internal int Resource { get; private set; }

    internal int MaximumResource { get; }

    internal int Stagger { get; private set; }

    internal int StaggerThreshold { get; }

    internal int Initiative { get; }

    internal int MoveSpeed { get; }

    internal int CollisionRadius { get; }

    internal DecisionId? ActiveDecisionId { get; private set; }

    internal SystemActionDefinition? ActiveSystemAction { get; private set; }

    internal CommitDirection CommitDirection { get; private set; } = CommitDirection.None;

    internal int? TargetPositionAtCommit { get; private set; }

    internal EventId? LastActionEventId { get; private set; }

    internal EventId? MoveStartedEventId { get; private set; }

    internal int? MovementStartPosition { get; private set; }

    internal int? FrozenMoveSpeed { get; private set; }

    internal bool MovementStarted { get; private set; }

    internal bool MovementCompleted { get; private set; }

    internal int DecisionCount { get; private set; }

    internal IReadOnlyDictionary<StableId, int> Cooldowns => _cooldowns;

    internal IReadOnlyList<EffectFrame> Effects => _effects;

    internal bool IsDecisionReady => State == FighterState.DecisionReady && !ActionId.HasValue;

    internal bool IsActiveMovement =>
        ActiveSystemAction?.IsMovement == true &&
        ActionPhase == global::Battle.Contracts.Events.ActionPhase.Active &&
        !MovementCompleted;

    internal void CommitSystemWait(StableId actionId, int activeTicks)
    {
        if (!IsDecisionReady)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} cannot commit an action from state {State}.");
        }

        if (activeTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(activeTicks));
        }

        ActionId = actionId;
        ActionPhase = global::Battle.Contracts.Events.ActionPhase.Active;
        State = FighterState.Idle;
        StateTicksRemaining = activeTicks;
    }

    internal void CommitSystemAction(
        SystemActionDefinition action,
        DecisionId decisionId,
        CommitDirection direction,
        int targetPositionAtCommit)
    {
        if (action is null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (!action.IsMovement)
        {
            CommitSystemWait(action.Id, action.ActiveTicks);
            ActiveDecisionId = decisionId;
            ActiveSystemAction = action;
            CommitDirection = CommitDirection.None;
            TargetPositionAtCommit = targetPositionAtCommit;
            LastActionEventId = null;
            MoveStartedEventId = null;
            MovementStartPosition = null;
            FrozenMoveSpeed = null;
            MovementStarted = false;
            MovementCompleted = false;
            return;
        }

        if (!IsDecisionReady)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} cannot commit an action from state {State}.");
        }

        if (direction == CommitDirection.None || action.ActiveTicks < 1 || MoveSpeed < 1)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} received an invalid movement descriptor.");
        }

        ActionId = action.Id;
        ActiveDecisionId = decisionId;
        ActiveSystemAction = action;
        CommitDirection = direction;
        TargetPositionAtCommit = targetPositionAtCommit;
        LastActionEventId = null;
        MoveStartedEventId = null;
        MovementStartPosition = null;
        FrozenMoveSpeed = null;
        MovementStarted = false;
        MovementCompleted = false;
        State = action.MovementMode == SystemMovementMode.Approach
            ? FighterState.Approach
            : FighterState.Retreat;
        ActionPhase = action.StartupTicks == 0
            ? global::Battle.Contracts.Events.ActionPhase.Active
            : global::Battle.Contracts.Events.ActionPhase.Startup;
        StateTicksRemaining = action.StartupTicks == 0
            ? action.ActiveTicks
            : action.StartupTicks;
    }

    internal void AdvanceActionLifecycle()
    {
        if (!ActionId.HasValue && !StateTicksRemaining.HasValue)
        {
            return;
        }

        if (!ActionId.HasValue || !StateTicksRemaining.HasValue)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.ActionPhaseEnd.ToString(),
                $"{FighterId} has an inconsistent action identity/timer pair.");
        }

        if (StateTicksRemaining.Value < 1)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.ActionPhaseEnd.ToString(),
                $"{FighterId} has a non-positive action timer.");
        }

        var remaining = StateTicksRemaining.Value - 1;
        if (remaining == 0)
        {
            ClearAction();
        }
        else
        {
            StateTicksRemaining = remaining;
        }
    }

    internal ActionLifecycleTransition? AdvanceMovementLifecycle()
    {
        if (ActiveSystemAction?.IsMovement != true)
        {
            AdvanceActionLifecycle();
            return null;
        }

        if (!ActionId.HasValue || !ActiveDecisionId.HasValue || !ActionPhase.HasValue ||
            !StateTicksRemaining.HasValue || StateTicksRemaining.Value < 1)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.ActionPhaseEnd.ToString(),
                $"{FighterId} has an inconsistent movement lifecycle.");
        }

        var actionId = ActionId.Value;
        var decisionId = ActiveDecisionId.Value;
        var sourceEventId = LastActionEventId;
        switch (ActionPhase.Value)
        {
            case global::Battle.Contracts.Events.ActionPhase.Startup:
                if (StateTicksRemaining.Value > 1)
                {
                    StateTicksRemaining = StateTicksRemaining.Value - 1;
                    return null;
                }

                ActionPhase = global::Battle.Contracts.Events.ActionPhase.Active;
                StateTicksRemaining = ActiveSystemAction.ActiveTicks;
                return new ActionLifecycleTransition(
                    actionId,
                    decisionId,
                    global::Battle.Contracts.Events.ActionPhase.Startup,
                    global::Battle.Contracts.Events.ActionPhase.Active,
                    ActiveSystemAction.ActiveTicks,
                    new ReasonCode("StartupCompleted"),
                    sourceEventId);

            case global::Battle.Contracts.Events.ActionPhase.Active:
                if (!MovementCompleted)
                {
                    if (StateTicksRemaining.Value > 1)
                    {
                        StateTicksRemaining = StateTicksRemaining.Value - 1;
                    }

                    return null;
                }

                if (ActiveSystemAction.RecoveryTicks > 0)
                {
                    State = FighterState.Recovery;
                    ActionPhase = global::Battle.Contracts.Events.ActionPhase.Recovery;
                    StateTicksRemaining = ActiveSystemAction.RecoveryTicks;
                    return new ActionLifecycleTransition(
                        actionId,
                        decisionId,
                        global::Battle.Contracts.Events.ActionPhase.Active,
                        global::Battle.Contracts.Events.ActionPhase.Recovery,
                        ActiveSystemAction.RecoveryTicks,
                        new ReasonCode("MovementCompleted"),
                        sourceEventId);
                }

                ClearAction();
                return new ActionLifecycleTransition(
                    actionId,
                    decisionId,
                    global::Battle.Contracts.Events.ActionPhase.Active,
                    null,
                    0,
                    new ReasonCode("MovementCompleted"),
                    sourceEventId);

            case global::Battle.Contracts.Events.ActionPhase.Recovery:
                if (StateTicksRemaining.Value > 1)
                {
                    StateTicksRemaining = StateTicksRemaining.Value - 1;
                    return null;
                }

                ClearAction();
                return new ActionLifecycleTransition(
                    actionId,
                    decisionId,
                    global::Battle.Contracts.Events.ActionPhase.Recovery,
                    null,
                    0,
                    new ReasonCode("RecoveryCompleted"),
                    sourceEventId);

            default:
                throw new EngineInvariantException(
                    EngineFailureCodes.InvalidStateTransition,
                    TickPhase.ActionPhaseEnd.ToString(),
                    $"{FighterId} is in unsupported movement phase {ActionPhase}.");
        }
    }

    internal void RecordActionEvent(EventId eventId) => LastActionEventId = eventId;

    internal void MarkMovementStarted(EventId eventId)
    {
        if (!IsActiveMovement || MovementStarted)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.VoluntaryMovement.ToString(),
                $"{FighterId} cannot start its movement segment.");
        }

        MovementStarted = true;
        MoveStartedEventId = eventId;
        MovementStartPosition = Position;
        FrozenMoveSpeed = MoveSpeed;
        LastActionEventId = eventId;
    }

    internal void ApplyPosition(int position)
    {
        Position = position;
    }

    internal void SetFacing(Facing facing)
    {
        Facing = facing;
    }

    internal void CompleteMovement(EventId eventId)
    {
        if (!IsActiveMovement || !MovementStarted)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.VoluntaryMovement.ToString(),
                $"{FighterId} cannot complete its movement segment.");
        }

        MovementCompleted = true;
        LastActionEventId = eventId;
    }

    private void ClearAction()
    {
        ActionId = null;
        ActionPhase = null;
        StateTicksRemaining = null;
        State = FighterState.DecisionReady;
        ActiveDecisionId = null;
        ActiveSystemAction = null;
        CommitDirection = CommitDirection.None;
        TargetPositionAtCommit = null;
        LastActionEventId = null;
        MoveStartedEventId = null;
        MovementStartPosition = null;
        FrozenMoveSpeed = null;
        MovementStarted = false;
        MovementCompleted = false;
    }

    internal DecisionId NextDecisionId()
    {
        DecisionCount = checked(DecisionCount + 1);
        var side = FighterId == FighterId.FighterA ? "a" : "b";
        return new DecisionId(
            "dec-fighter_" + side + "-" + DecisionCount.ToString("D6", CultureInfo.InvariantCulture));
    }

    internal void SetHealthForTesting(int health)
    {
        if (health < 0 || health > MaximumHealth)
        {
            throw new ArgumentOutOfRangeException(nameof(health));
        }

        Health = health;
    }

    internal void SetActionTimerForTesting(int? ticks)
    {
        StateTicksRemaining = ticks;
    }

    internal void SetActionIdForTesting(StableId? actionId)
    {
        ActionId = actionId;
    }

    internal void SetActionPhaseForTesting(ActionPhase? actionPhase)
    {
        ActionPhase = actionPhase;
    }

    internal void SetActiveDecisionIdForTesting(DecisionId? decisionId)
    {
        ActiveDecisionId = decisionId;
    }

    internal void SetActiveSystemActionForTesting(SystemActionDefinition? action)
    {
        ActiveSystemAction = action;
    }

    internal void SetMovementCompletedForTesting(bool completed)
    {
        MovementCompleted = completed;
    }

    internal void SetStateForTesting(FighterState state)
    {
        State = state;
    }

    internal FighterFrame ToFrame() => new(
        FighterId,
        Position,
        Facing,
        State,
        StateTicksRemaining,
        ActionId,
        ActionPhase,
        Health,
        MaximumHealth,
        Energy,
        MaximumEnergy,
        new ResourceFrame(ResourceId, Resource, MaximumResource),
        Stagger,
        StaggerThreshold,
        _effects);
}

internal readonly record struct ActionLifecycleTransition(
    StableId ActionId,
    DecisionId DecisionId,
    ActionPhase FromPhase,
    ActionPhase? ToPhase,
    int PhaseTicks,
    ReasonCode ReasonCode,
    EventId? SourceEventId);
