namespace Battle.Contracts.Effects;

public enum EffectOwnerKind { Global, Action, Effect }
public enum EffectRecipient { Self, Opponent }
public enum EffectCondition { Always, PositiveDamage, NormalHit, LivingTarget }
public enum EffectPrimitive { ApplyEffect, RemoveEffect }
public enum EffectRefreshRule { ResetDuration, KeepLonger }
public enum EffectCompareKey { Value1, DurationTicks, StackCount }
public enum EffectSemanticRole { None, ControlFatigue, ControlImmunity, GrabLockout, WakeupImmunity, GuardBreak }

public enum EffectTrigger
{
    BattleStart, DamageTaken, DamageDealt, Blocked, Dodged, GuardBreak,
    ControlEnded, FatigueThresholdReached, GrabEnded, Knockdown,
    WakeupCompleted, EffectAdded, EffectRemoved, EndOfTick,
}

public enum EffectModifierOperation { Add, Multiply, Override }

/// <summary>Closed WP-10 domain. Resource-kit channels are intentionally absent.</summary>
public enum EffectModifierTarget
{
    Power, Armor, Precision, Evasion, Guard, GuardBreak, MoveSpeed, ActionSpeed,
    Initiative, ControlPower, ControlResistance, Mass, EnergyRegen,
    DamageTaken, DamageDealt, BlockChanceOffset, DodgeChanceOffset,
    BlockWeight, PunishWeight, WallActionWeight, GrabPriority,
    HardControlDuration, HardControlAllowed, GrabAllowed, KnockdownAllowed,
}

public enum HitInterruptStrength { None, Light, Medium, Heavy }
public enum ControlCategory { Stun, Knockdown, Grab, Defeat }
public enum ActionInterruptKind { Cancelable, Committed, Armored, Unstoppable, UninterruptibleImpact }
