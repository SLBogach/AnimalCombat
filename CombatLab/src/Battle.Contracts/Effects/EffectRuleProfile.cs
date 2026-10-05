using Battle.Contracts.Ids;

namespace Battle.Contracts.Effects;

/// <summary>Immutable, pre-materialized rule; contains no expressions or runtime parsing.</summary>
public sealed class EffectRuleProfile
{
    public EffectRuleProfile(
        StableId ruleId, EffectOwnerKind ownerKind, StableId ownerId,
        EffectTrigger trigger, EffectRecipient recipient, EffectCondition condition,
        EffectPrimitive primitive, StableId effectId, int priority,
        int internalCooldownTicks, int maxActivationsPerTick, int maxActivationsPerBattle,
        bool oncePerEvent)
    {
        RequireId(ruleId, nameof(ruleId));
        RequireId(ownerId, nameof(ownerId));
        RequireId(effectId, nameof(effectId));
        RequireDefined(ownerKind, nameof(ownerKind));
        RequireDefined(trigger, nameof(trigger));
        RequireDefined(recipient, nameof(recipient));
        RequireDefined(condition, nameof(condition));
        RequireDefined(primitive, nameof(primitive));
        if ((ownerKind == EffectOwnerKind.Global) != (ownerId.Value == "global"))
            throw new ArgumentException("Only a Global owner uses the literal global ID.", nameof(ownerId));
        if (internalCooldownTicks < 0) throw new ArgumentOutOfRangeException(nameof(internalCooldownTicks));
        if (maxActivationsPerTick < 1) throw new ArgumentOutOfRangeException(nameof(maxActivationsPerTick));
        if (maxActivationsPerBattle < 1) throw new ArgumentOutOfRangeException(nameof(maxActivationsPerBattle));
        if (!oncePerEvent) throw new ArgumentException("WP-10 requires once_per_event=true.", nameof(oncePerEvent));

        RuleId = ruleId;
        OwnerKind = ownerKind;
        OwnerId = ownerId;
        Trigger = trigger;
        Recipient = recipient;
        Condition = condition;
        Primitive = primitive;
        EffectId = effectId;
        Priority = priority;
        InternalCooldownTicks = internalCooldownTicks;
        MaxActivationsPerTick = maxActivationsPerTick;
        MaxActivationsPerBattle = maxActivationsPerBattle;
        OncePerEvent = oncePerEvent;
    }

    public StableId RuleId { get; }
    public EffectOwnerKind OwnerKind { get; }
    public StableId OwnerId { get; }
    public EffectTrigger Trigger { get; }
    public EffectRecipient Recipient { get; }
    public EffectCondition Condition { get; }
    public EffectPrimitive Primitive { get; }
    public StableId EffectId { get; }
    public int Priority { get; }
    public int InternalCooldownTicks { get; }
    public int MaxActivationsPerTick { get; }
    public int MaxActivationsPerBattle { get; }
    public bool OncePerEvent { get; }

    private static void RequireId(StableId value, string name)
    {
        if (!StableId.TryParse(value.Value, out _)) throw new ArgumentException("A canonical Stable ID is required.", name);
    }

    private static void RequireDefined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name);
    }
}
