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
        int initiative)
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

    internal int DecisionCount { get; private set; }

    internal IReadOnlyDictionary<StableId, int> Cooldowns => _cooldowns;

    internal IReadOnlyList<EffectFrame> Effects => _effects;

    internal bool IsDecisionReady => State == FighterState.DecisionReady && !ActionId.HasValue;

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
            ActionId = null;
            ActionPhase = null;
            StateTicksRemaining = null;
            State = FighterState.DecisionReady;
        }
        else
        {
            StateTicksRemaining = remaining;
        }
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
