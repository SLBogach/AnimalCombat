namespace Battle.Contracts.Events;

public enum FighterState
{
    Idle,
    DecisionReady,
    Approach,
    Retreat,
    AttackPrepare,
    AttackActive,
    Recovery,
    Block,
    Dodge,
    DodgeRecovery,
    CounterWindow,
    Stunned,
    KnockedDown,
    Grabbing,
    Grabbed,
    Defeated,
}

public enum ActionPhase
{
    Startup,
    Active,
    Recovery,
    CancelWindow,
    CommitLock,
    Hold,
    Throw,
    GetUp,
}

public enum Facing
{
    Left,
    Right,
}

public enum EffectExpiryBoundary
{
    ExpireBeforeTick,
    ExpireAfterTick,
}

public enum RngStream
{
    Decision,
    Resolution,
}

public enum RngOperation
{
    NextInt,
    TieBreak,
    ChanceCheck,
}
