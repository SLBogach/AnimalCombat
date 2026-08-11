using Battle.Core.Random;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Core.Engine;

internal sealed class BattleState
{
    private long _nextSnapshotIdentity;

    internal BattleState(
        FighterRuntimeState fighterA,
        FighterRuntimeState fighterB,
        ulong masterSeed)
    {
        FighterA = fighterA ?? throw new ArgumentNullException(nameof(fighterA));
        FighterB = fighterB ?? throw new ArgumentNullException(nameof(fighterB));
        Rng = new GameplayRng(masterSeed);
    }

    internal int Tick { get; private set; }

    internal FighterRuntimeState FighterA { get; }

    internal FighterRuntimeState FighterB { get; }

    internal GameplayRng Rng { get; }

    internal bool IsTerminal { get; private set; }

    internal ExternalId? ActiveGrabId { get; private set; }

    internal StableId? ActiveControlId { get; private set; }

    internal BattleOutcome? Outcome { get; private set; }

    internal FighterId? WinnerFighterId { get; private set; }

    internal BattleEndReason? EndReason { get; private set; }

    internal TickSnapshot CreateSnapshot()
    {
        EnsureMutable();
        var identity = _nextSnapshotIdentity;
        _nextSnapshotIdentity = checked(_nextSnapshotIdentity + 1);
        return new TickSnapshot(identity, Tick, FighterA.ToFrame(), FighterB.ToFrame());
    }

    internal FighterRuntimeState Get(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterA,
        FighterId.FighterB => FighterB,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    internal FighterRuntimeState GetOpponent(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterB,
        FighterId.FighterB => FighterA,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    internal IReadOnlyList<FighterFrame> FinalFrames() =>
        new[] { FighterA.ToFrame(), FighterB.ToFrame() };

    internal void AdvanceTick()
    {
        EnsureMutable();
        Tick = checked(Tick + 1);
    }

    internal void RecordOutcome(
        BattleOutcome outcome,
        FighterId? winnerFighterId,
        BattleEndReason endReason)
    {
        EnsureMutable();
        Outcome = outcome;
        WinnerFighterId = winnerFighterId;
        EndReason = endReason;
    }

    internal void MarkTerminal()
    {
        EnsureMutable();
        IsTerminal = true;
    }

    internal void EnsureMutable()
    {
        if (IsTerminal)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.TerminalMutation,
                "TerminalGuard",
                "Battle state cannot be mutated after BattleEnded.");
        }
    }
}
