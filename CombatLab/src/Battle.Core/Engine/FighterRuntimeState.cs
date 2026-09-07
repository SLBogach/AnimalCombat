using Battle.Core.Decisions;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using System.Globalization;

namespace Battle.Core.Engine;

internal sealed class FighterRuntimeState
{
    private readonly Dictionary<StableId, int> _cooldowns = new();
    private readonly Dictionary<StableId, int> _opportunityDebts = new();
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
        int actionSpeed,
        int moveSpeed,
        int collisionRadius)
        : this(
            fighterId,
            side,
            animalId,
            position,
            facing,
            maximumHealth,
            maximumEnergy,
            resourceId,
            resource,
            maximumResource,
            staggerThreshold,
            initiative,
            actionSpeed,
            moveSpeed,
            collisionRadius,
            power: 0,
            armor: 0,
            precision: 0,
            evasion: 0,
            guard: 0,
            guardBreak: 0,
            controlPower: 0,
            controlResistance: 0,
            mass: 1)
    {
    }

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
        int actionSpeed,
        int moveSpeed,
        int collisionRadius,
        int power,
        int armor,
        int precision,
        int evasion,
        int guard,
        int guardBreak,
        int controlPower,
        int controlResistance,
        int mass)
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
        ActionSpeed = actionSpeed;
        MoveSpeed = moveSpeed;
        CollisionRadius = collisionRadius;
        Power = power;
        Armor = armor;
        Precision = precision;
        Evasion = evasion;
        Guard = guard;
        GuardBreak = guardBreak;
        ControlPower = controlPower;
        ControlResistance = controlResistance;
        Mass = mass;
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

    internal int ActionSpeed { get; }

    internal int MoveSpeed { get; }

    internal int CollisionRadius { get; }

    internal int Power { get; }

    internal int Armor { get; }

    internal int Precision { get; }

    internal int Evasion { get; }

    internal int Guard { get; }

    internal int GuardBreak { get; }

    internal int ControlPower { get; }

    internal int ControlResistance { get; }

    internal int Mass { get; }

    internal DecisionId? ActiveDecisionId { get; private set; }

    internal SystemActionDefinition? ActiveSystemAction { get; private set; }

    internal CombatActionDescriptor? ActiveCombatAction { get; private set; }

    internal CommitDirection CommitDirection { get; private set; } = CommitDirection.None;

    internal int? TargetPositionAtCommit { get; private set; }

    internal EventId? LastActionEventId { get; private set; }

    internal EventId? CombatLifecycleEventId { get; private set; }

    internal EventId? MoveStartedEventId { get; private set; }

    internal int? MovementStartPosition { get; private set; }

    internal int? FrozenMoveSpeed { get; private set; }

    internal bool MovementStarted { get; private set; }

    internal bool MovementCompleted { get; private set; }

    internal int DecisionCount { get; private set; }

    internal StableId? LastCommittedActionId { get; private set; }

    internal string? LastCommittedCategory { get; private set; }

    internal int SameActionStreak { get; private set; }

    internal int SameCategoryStreak { get; private set; }

    internal StableId? ObservableActionId { get; private set; }

    internal int? ObservableCommitTick { get; private set; }

    internal bool Emergency { get; private set; }

    internal IReadOnlyDictionary<StableId, int> Cooldowns => _cooldowns;

    internal IReadOnlyDictionary<StableId, int> OpportunityDebts => _opportunityDebts;

    internal IReadOnlyList<EffectFrame> Effects => _effects;

    internal bool IsDecisionReady => State == FighterState.DecisionReady && !ActionId.HasValue;

    internal bool IsActiveMovement =>
        ActiveSystemAction?.IsMovement == true &&
        ActionPhase == global::Battle.Contracts.Events.ActionPhase.Active &&
        !MovementCompleted;

    internal bool IsActiveCombat => ActiveCombatAction is not null;

    internal int CooldownFor(StableId actionId) =>
        _cooldowns.TryGetValue(actionId, out var ticks) ? ticks : 0;

    internal int OpportunityDebtFor(StableId actionId) =>
        _opportunityDebts.TryGetValue(actionId, out var debt) ? debt : 0;

    internal void DecrementCooldowns()
    {
        if (_cooldowns.Count == 0)
        {
            return;
        }

        foreach (var actionId in _cooldowns.Keys.OrderBy(id => id).ToArray())
        {
            var next = _cooldowns[actionId] - 1;
            if (next <= 0)
            {
                _cooldowns.Remove(actionId);
            }
            else
            {
                _cooldowns[actionId] = next;
            }
        }
    }

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

        ActiveCombatAction = null;
        CombatLifecycleEventId = null;

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

    internal void CommitCombatAction(CombatActionDescriptor descriptor)
    {
        if (descriptor is null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        if (!IsDecisionReady)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} cannot commit an action from state {State}.");
        }

        if (descriptor.TargetFighterId == FighterId)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                "A combat action cannot target its own fighter ID.");
        }

        ActionId = descriptor.ActionId;
        ActiveDecisionId = descriptor.DecisionId;
        ActiveSystemAction = null;
        ActiveCombatAction = descriptor;
        CommitDirection = descriptor.CommitDirection;
        TargetPositionAtCommit = descriptor.TargetPositionAtCommit;
        LastActionEventId = null;
        CombatLifecycleEventId = null;
        MoveStartedEventId = null;
        MovementStartPosition = null;
        FrozenMoveSpeed = null;
        MovementStarted = false;
        MovementCompleted = false;
        State = CombatStateForPhase(
            descriptor.ResolutionProfile,
            descriptor.StartupTicks > 0
                ? global::Battle.Contracts.Events.ActionPhase.Startup
                : global::Battle.Contracts.Events.ActionPhase.Active);
        ActionPhase = descriptor.StartupTicks > 0
            ? global::Battle.Contracts.Events.ActionPhase.Startup
            : global::Battle.Contracts.Events.ActionPhase.Active;
        StateTicksRemaining = descriptor.StartupTicks > 0
            ? descriptor.StartupTicks
            : descriptor.ActiveTicks;
        if (descriptor.CooldownTicks > 0)
        {
            _cooldowns[descriptor.ActionId] = descriptor.CooldownTicks;
        }

        if (descriptor.RelativeImpactTicks.Count != 0)
        {
            ObservableActionId = descriptor.ActionId;
            ObservableCommitTick = descriptor.CommitTick;
        }
        else
        {
            ObservableActionId = null;
            ObservableCommitTick = null;
        }
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
        if (ActiveCombatAction is not null)
        {
            return AdvanceCombatLifecycle();
        }

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

    private ActionLifecycleTransition? AdvanceCombatLifecycle()
    {
        var action = ActiveCombatAction!;
        if (!ActionId.HasValue || !ActiveDecisionId.HasValue || !ActionPhase.HasValue ||
            !StateTicksRemaining.HasValue || StateTicksRemaining.Value < 1)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.ActionPhaseEnd.ToString(),
                $"{FighterId} has an inconsistent combat lifecycle.");
        }

        var source = CombatLifecycleEventId;
        switch (ActionPhase.Value)
        {
            case global::Battle.Contracts.Events.ActionPhase.Startup:
                if (StateTicksRemaining.Value > 1)
                {
                    StateTicksRemaining = StateTicksRemaining.Value - 1;
                    return null;
                }

                State = CombatStateForPhase(action.ResolutionProfile, global::Battle.Contracts.Events.ActionPhase.Active);
                ActionPhase = global::Battle.Contracts.Events.ActionPhase.Active;
                StateTicksRemaining = action.ActiveTicks;
                return new ActionLifecycleTransition(
                    action.ActionId,
                    action.DecisionId,
                    global::Battle.Contracts.Events.ActionPhase.Startup,
                    global::Battle.Contracts.Events.ActionPhase.Active,
                    action.ActiveTicks,
                    new ReasonCode("StartupCompleted"),
                    source);

            case global::Battle.Contracts.Events.ActionPhase.Active:
                if (StateTicksRemaining.Value > 1)
                {
                    StateTicksRemaining = StateTicksRemaining.Value - 1;
                    return null;
                }

                if (action.RecoveryTicks > 0)
                {
                    State = CombatStateForPhase(action.ResolutionProfile, global::Battle.Contracts.Events.ActionPhase.Recovery);
                    ActionPhase = global::Battle.Contracts.Events.ActionPhase.Recovery;
                    StateTicksRemaining = action.RecoveryTicks;
                    return new ActionLifecycleTransition(
                        action.ActionId,
                        action.DecisionId,
                        global::Battle.Contracts.Events.ActionPhase.Active,
                        global::Battle.Contracts.Events.ActionPhase.Recovery,
                        action.RecoveryTicks,
                        new ReasonCode("ActiveCompleted"),
                        source);
                }

                ClearAction();
                return new ActionLifecycleTransition(
                    action.ActionId,
                    action.DecisionId,
                    global::Battle.Contracts.Events.ActionPhase.Active,
                    null,
                    0,
                    new ReasonCode("ActiveCompleted"),
                    source);

            case global::Battle.Contracts.Events.ActionPhase.Recovery:
                if (StateTicksRemaining.Value > 1)
                {
                    StateTicksRemaining = StateTicksRemaining.Value - 1;
                    return null;
                }

                ClearAction();
                return new ActionLifecycleTransition(
                    action.ActionId,
                    action.DecisionId,
                    global::Battle.Contracts.Events.ActionPhase.Recovery,
                    null,
                    0,
                    new ReasonCode("RecoveryCompleted"),
                    source);

            default:
                throw new EngineInvariantException(
                    EngineFailureCodes.InvalidStateTransition,
                    TickPhase.ActionPhaseEnd.ToString(),
                    $"{FighterId} is in unsupported combat phase {ActionPhase}.");
        }
    }

    internal void RecordActionEvent(EventId eventId) => LastActionEventId = eventId;

    internal void RecordCombatLifecycleEvent(EventId eventId) =>
        CombatLifecycleEventId = eventId;

    internal void RecordCombatCommit(EventId eventId)
    {
        if (ActiveCombatAction is null)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                "A combat commit event requires an active combat descriptor.");
        }

        CombatLifecycleEventId = eventId;
    }

    internal ResourceMutation? ApplyEnergyCost(int cost)
    {
        if (cost < 0 || cost > Energy)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} cannot pay Energy cost {cost} from {Energy}.");
        }

        if (cost == 0)
        {
            return null;
        }

        var before = Energy;
        Energy = checked(Energy - cost);
        return new ResourceMutation(
            ResourceKind.Energy,
            null,
            before,
            -cost,
            Energy,
            0,
            MaximumEnergy);
    }

    internal ResourceMutation? ApplyUniqueResourceCost(int cost)
    {
        if (cost < 0 || cost > Resource)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} cannot pay resource cost {cost} from {Resource}.");
        }

        if (cost == 0)
        {
            return null;
        }

        var before = Resource;
        Resource = checked(Resource - cost);
        return new ResourceMutation(
            ResourceKind.UniqueResource,
            ResourceId,
            before,
            -cost,
            Resource,
            0,
            MaximumResource);
    }

    internal void RecordCommittedHistory(StableId actionId, string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("An action category is required.", nameof(category));
        }

        int nextActionStreak;
        int nextCategoryStreak;
        try
        {
            nextActionStreak = LastCommittedActionId == actionId
                ? checked(SameActionStreak + 1)
                : 1;
            nextCategoryStreak = StringComparer.Ordinal.Equals(LastCommittedCategory, category)
                ? checked(SameCategoryStreak + 1)
                : 1;
        }
        catch (OverflowException exception)
        {
            throw DecisionArithmeticInvariant("Decision repeat counter overflowed", exception);
        }

        SameActionStreak = nextActionStreak;
        SameCategoryStreak = nextCategoryStreak;
        LastCommittedActionId = actionId;
        LastCommittedCategory = category;
    }

    internal void UpdateOpportunityDebts(
        IEnumerable<StableId> selectedSpecialActionIds,
        IEnumerable<StableId> legalSpecialActionIds,
        StableId chosenActionId)
    {
        if (selectedSpecialActionIds is null)
        {
            throw new ArgumentNullException(nameof(selectedSpecialActionIds));
        }

        if (legalSpecialActionIds is null)
        {
            throw new ArgumentNullException(nameof(legalSpecialActionIds));
        }

        var legal = new HashSet<StableId>(legalSpecialActionIds);
        var updates = new List<KeyValuePair<StableId, int?>>();
        try
        {
            foreach (var actionId in selectedSpecialActionIds.OrderBy(id => id))
            {
                if (actionId == chosenActionId)
                {
                    updates.Add(new KeyValuePair<StableId, int?>(actionId, null));
                }
                else if (legal.Contains(actionId))
                {
                    updates.Add(new KeyValuePair<StableId, int?>(
                        actionId,
                        checked(OpportunityDebtFor(actionId) + 1)));
                }
            }
        }
        catch (OverflowException exception)
        {
            throw DecisionArithmeticInvariant("Decision opportunity counter overflowed", exception);
        }

        foreach (var update in updates)
        {
            if (update.Value.HasValue)
            {
                _opportunityDebts[update.Key] = update.Value.Value;
            }
            else
            {
                _opportunityDebts.Remove(update.Key);
            }
        }
    }

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

    internal HealthMutation ApplyDamage(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        var before = Health;
        Health = System.Math.Max(0, checked(Health - amount));
        return new HealthMutation(before, Health, checked(before - Health));
    }

    internal ResourceMutation? ApplyStagger(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        if (amount == 0 || State == FighterState.Defeated)
        {
            return null;
        }

        var before = Stagger;
        var candidate = checked((long)Stagger + amount);
        Stagger = checked((int)System.Math.Min(candidate, StaggerThreshold));
        return new ResourceMutation(
            ResourceKind.Stagger,
            null,
            before,
            checked(Stagger - before),
            Stagger,
            0,
            StaggerThreshold);
    }

    internal ResourceMutation? ResetStagger()
    {
        if (Stagger == 0)
        {
            return null;
        }

        var before = Stagger;
        Stagger = 0;
        return new ResourceMutation(
            ResourceKind.Stagger,
            null,
            before,
            -before,
            0,
            0,
            StaggerThreshold);
    }

    internal CancelledAction? ApplyHardControl(int durationTicks)
    {
        if (durationTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(durationTicks));
        }

        var cancelled = CaptureCancelledAction();
        ClearAction();
        State = FighterState.Stunned;
        StateTicksRemaining = durationTicks;
        return cancelled;
    }

    internal CancelledAction? CancelCurrentAction()
    {
        var cancelled = CaptureCancelledAction();
        if (cancelled.HasValue)
        {
            ClearAction();
        }

        return cancelled;
    }

    internal FighterStateTransition? AdvanceControlExpiry()
    {
        if (State != FighterState.Stunned)
        {
            return null;
        }

        if (!StateTicksRemaining.HasValue || StateTicksRemaining.Value < 1 || ActionId.HasValue)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Expiry.ToString(),
                $"{FighterId} has an inconsistent hard-control state.");
        }

        if (StateTicksRemaining.Value > 1)
        {
            StateTicksRemaining = StateTicksRemaining.Value - 1;
            return null;
        }

        var old = State;
        State = FighterState.DecisionReady;
        StateTicksRemaining = null;
        return new FighterStateTransition(old, State, null);
    }

    internal CancelledAction? ApplyDefeat()
    {
        if (Health != 0)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Outcome.ToString(),
                "A fighter can be defeated only at zero health.");
        }

        var cancelled = CaptureCancelledAction();
        ClearAction();
        State = FighterState.Defeated;
        return cancelled;
    }

    internal CancelledAction? ApplyGrabbed()
    {
        var cancelled = CaptureCancelledAction();
        ClearAction();
        State = FighterState.Grabbed;
        return cancelled;
    }

    internal void ApplyGrabbing()
    {
        if (ActiveCombatAction is null)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.WallsAndGrabs.ToString(),
                "A grabber requires an active combat action.");
        }

        State = FighterState.Grabbing;
    }

    internal void ClearGrabState()
    {
        if (State == FighterState.Grabbed)
        {
            State = FighterState.DecisionReady;
            StateTicksRemaining = null;
            return;
        }

        if (State != FighterState.Grabbing)
        {
            return;
        }

        State = ActiveCombatAction is null ? FighterState.DecisionReady : ActionPhase switch
        {
            global::Battle.Contracts.Events.ActionPhase.Startup =>
                CombatStateForPhase(ActiveCombatAction.ResolutionProfile, global::Battle.Contracts.Events.ActionPhase.Startup),
            global::Battle.Contracts.Events.ActionPhase.Active =>
                CombatStateForPhase(ActiveCombatAction.ResolutionProfile, global::Battle.Contracts.Events.ActionPhase.Active),
            global::Battle.Contracts.Events.ActionPhase.Recovery =>
                CombatStateForPhase(ActiveCombatAction.ResolutionProfile, global::Battle.Contracts.Events.ActionPhase.Recovery),
            _ => FighterState.DecisionReady,
        };
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

    private CancelledAction? CaptureCancelledAction() => ActionId.HasValue
        ? new CancelledAction(ActionId.Value, ActiveDecisionId, ActionPhase, LastActionEventId ?? CombatLifecycleEventId)
        : null;

    private static FighterState CombatStateForPhase(
        Battle.Core.Resolution.ResolutionActionProfile profile,
        global::Battle.Contracts.Events.ActionPhase phase) => phase switch
        {
            global::Battle.Contracts.Events.ActionPhase.Startup => FighterState.AttackPrepare,
            global::Battle.Contracts.Events.ActionPhase.Active when profile.HasTag("block") => FighterState.Block,
            global::Battle.Contracts.Events.ActionPhase.Active when profile.HasTag("dodge") => FighterState.Dodge,
            global::Battle.Contracts.Events.ActionPhase.Active when profile.HasTag("counter") => FighterState.CounterWindow,
            global::Battle.Contracts.Events.ActionPhase.Active => FighterState.AttackActive,
            global::Battle.Contracts.Events.ActionPhase.Recovery when profile.HasTag("dodge") => FighterState.DodgeRecovery,
            global::Battle.Contracts.Events.ActionPhase.Recovery => FighterState.Recovery,
            _ => throw new ArgumentOutOfRangeException(nameof(phase)),
        };

    private void ClearAction()
    {
        ActionId = null;
        ActionPhase = null;
        StateTicksRemaining = null;
        State = FighterState.DecisionReady;
        ActiveDecisionId = null;
        ActiveSystemAction = null;
        ActiveCombatAction = null;
        CommitDirection = CommitDirection.None;
        TargetPositionAtCommit = null;
        LastActionEventId = null;
        CombatLifecycleEventId = null;
        MoveStartedEventId = null;
        MovementStartPosition = null;
        FrozenMoveSpeed = null;
        MovementStarted = false;
        MovementCompleted = false;
        ObservableActionId = null;
        ObservableCommitTick = null;
    }

    internal DecisionId NextDecisionId()
    {
        var decisionId = PeekNextDecisionId();
        CommitDecisionId(decisionId);
        return decisionId;
    }

    internal DecisionId PeekNextDecisionId()
    {
        int next;
        try
        {
            next = checked(DecisionCount + 1);
        }
        catch (OverflowException exception)
        {
            throw DecisionArithmeticInvariant("Decision counter overflowed", exception);
        }

        var side = FighterId == FighterId.FighterA ? "a" : "b";
        return new DecisionId(
            "dec-fighter_" + side + "-" + next.ToString("D6", CultureInfo.InvariantCulture));
    }

    internal void CommitDecisionId(DecisionId decisionId)
    {
        if (decisionId != PeekNextDecisionId())
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Decisions.ToString(),
                $"{FighterId} received a non-sequential decision ID.");
        }

        try
        {
            DecisionCount = checked(DecisionCount + 1);
        }
        catch (OverflowException exception)
        {
            throw DecisionArithmeticInvariant("Decision counter overflowed", exception);
        }
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

    internal void SetEnergyForTesting(int energy)
    {
        if (energy < 0 || energy > MaximumEnergy)
        {
            throw new ArgumentOutOfRangeException(nameof(energy));
        }

        Energy = energy;
    }

    internal void SetResourceForTesting(int resource)
    {
        if (resource < 0 || resource > MaximumResource)
        {
            throw new ArgumentOutOfRangeException(nameof(resource));
        }

        Resource = resource;
    }

    internal void SetStaggerForTesting(int stagger)
    {
        if (stagger < 0 || stagger > StaggerThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(stagger));
        }

        Stagger = stagger;
    }

    internal void SetPositionForTesting(int position) => Position = position;

    internal void SetCooldownForTesting(StableId actionId, int ticks)
    {
        if (ticks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        if (ticks == 0)
        {
            _cooldowns.Remove(actionId);
        }
        else
        {
            _cooldowns[actionId] = ticks;
        }
    }

    internal void SetOpportunityDebtForTesting(StableId actionId, int debt)
    {
        if (debt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(debt));
        }

        if (debt == 0)
        {
            _opportunityDebts.Remove(actionId);
        }
        else
        {
            _opportunityDebts[actionId] = debt;
        }
    }

    internal void SetEmergency(bool emergency) => Emergency = emergency;

    internal void SetEmergencyForTesting(bool emergency) => SetEmergency(emergency);

    internal void SetDecisionHistoryForTesting(
        int decisionCount,
        StableId? lastActionId,
        string? lastCategory,
        int sameActionStreak,
        int sameCategoryStreak)
    {
        if (decisionCount < 0 || sameActionStreak < 0 || sameCategoryStreak < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(decisionCount));
        }

        DecisionCount = decisionCount;
        LastCommittedActionId = lastActionId;
        LastCommittedCategory = lastCategory;
        SameActionStreak = sameActionStreak;
        SameCategoryStreak = sameCategoryStreak;
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

    private static EngineInvariantException DecisionArithmeticInvariant(
        string message,
        OverflowException exception) => new(
        EngineFailureCodes.DecisionArithmeticOverflow,
        TickPhase.Decisions.ToString(),
        message + ": " + exception.Message);
}

internal readonly record struct HealthMutation(int Before, int After, int ActualLoss);

internal readonly record struct CancelledAction(
    StableId ActionId,
    DecisionId? DecisionId,
    ActionPhase? Phase,
    EventId? SourceEventId);

internal readonly record struct FighterStateTransition(
    FighterState From,
    FighterState To,
    int? DurationTicks);

internal readonly record struct ActionLifecycleTransition(
    StableId ActionId,
    DecisionId DecisionId,
    ActionPhase FromPhase,
    ActionPhase? ToPhase,
    int PhaseTicks,
    ReasonCode ReasonCode,
    EventId? SourceEventId);
