using System.Collections.ObjectModel;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Events;

public readonly record struct ResourceFrame
{
    public ResourceFrame(StableId resourceId, int value, int maximum)
    {
        if (maximum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        ResourceId = resourceId;
        Value = value;
        Maximum = maximum;
    }

    public StableId ResourceId { get; }

    public int Value { get; }

    public int Maximum { get; }
}

public readonly record struct EffectFrame
{
    public EffectFrame(
        StableId effectId,
        int stacks,
        int ticksRemaining,
        EffectExpiryBoundary expiryBoundary)
    {
        if (stacks is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(stacks));
        }

        if (ticksRemaining < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksRemaining));
        }

        if (expiryBoundary is not EffectExpiryBoundary.ExpireBeforeTick and
            not EffectExpiryBoundary.ExpireAfterTick)
        {
            throw new ArgumentOutOfRangeException(nameof(expiryBoundary));
        }

        EffectId = effectId;
        Stacks = stacks;
        TicksRemaining = ticksRemaining;
        ExpiryBoundary = expiryBoundary;
    }

    public StableId EffectId { get; }

    public int Stacks { get; }

    public int TicksRemaining { get; }

    public EffectExpiryBoundary ExpiryBoundary { get; }
}

public sealed class FighterFrame
{
    private readonly ReadOnlyCollection<EffectFrame> _effects;

    public FighterFrame(
        FighterId fighterId,
        int position,
        Facing facing,
        FighterState state,
        int? stateTicksRemaining,
        StableId? actionId,
        ActionPhase? actionPhase,
        int health,
        int maxHealth,
        int energy,
        int maxEnergy,
        ResourceFrame uniqueResource,
        int stagger,
        int staggerThreshold,
        IEnumerable<EffectFrame> effects)
    {
        if (effects is null)
        {
            throw new ArgumentNullException(nameof(effects));
        }

        if (fighterId is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterId));
        }

        if (facing is not Facing.Left and not Facing.Right)
        {
            throw new ArgumentOutOfRangeException(nameof(facing));
        }

        if (state is < FighterState.Idle or > FighterState.Defeated)
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        if (actionPhase.HasValue &&
            (actionPhase.Value is < global::Battle.Contracts.Events.ActionPhase.Startup
                or > global::Battle.Contracts.Events.ActionPhase.GetUp))
        {
            throw new ArgumentOutOfRangeException(nameof(actionPhase));
        }

        if (stateTicksRemaining < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stateTicksRemaining));
        }

        if (health < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(health));
        }

        if (maxHealth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHealth));
        }

        if (maxEnergy < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEnergy));
        }

        if (stagger < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stagger));
        }

        if (staggerThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(staggerThreshold));
        }

        var effectFrames = new List<EffectFrame>(effects);
        if (effectFrames.Count > 128)
        {
            throw new ArgumentException("A fighter frame can contain at most 128 effects.", nameof(effects));
        }

        FighterId = fighterId;
        Position = position;
        Facing = facing;
        State = state;
        StateTicksRemaining = stateTicksRemaining;
        ActionId = actionId;
        ActionPhase = actionPhase;
        Health = health;
        MaxHealth = maxHealth;
        Energy = energy;
        MaxEnergy = maxEnergy;
        UniqueResource = uniqueResource;
        Stagger = stagger;
        StaggerThreshold = staggerThreshold;
        _effects = new ReadOnlyCollection<EffectFrame>(effectFrames);
    }

    public FighterId FighterId { get; }

    public int Position { get; }

    public Facing Facing { get; }

    public FighterState State { get; }

    public int? StateTicksRemaining { get; }

    public StableId? ActionId { get; }

    public ActionPhase? ActionPhase { get; }

    public int Health { get; }

    public int MaxHealth { get; }

    public int Energy { get; }

    public int MaxEnergy { get; }

    public ResourceFrame UniqueResource { get; }

    public int Stagger { get; }

    public int StaggerThreshold { get; }

    public IReadOnlyList<EffectFrame> Effects => _effects;
}

public sealed record FramePair(FighterFrame? Actor, FighterFrame? Target);
