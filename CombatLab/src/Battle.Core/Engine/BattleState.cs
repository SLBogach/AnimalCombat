using Battle.Core.Random;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Core.Engine;

internal sealed class BattleState
{
    private long _nextSnapshotIdentity;
    private readonly HashSet<string> _consumedHitGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<FighterId, int> _grabLockoutUntil = new();
    private readonly List<EventId> _pivotalEventIds = new();
    private ActiveGrabRuntime? _activeGrab;

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

    internal ExternalId? ActiveGrabId => _activeGrab?.GrabId;

    internal ActiveGrabRuntime? ActiveGrab => _activeGrab;

    internal StableId? ActiveControlId { get; private set; }

    internal BattleOutcome? Outcome { get; private set; }

    internal FighterId? WinnerFighterId { get; private set; }

    internal BattleEndReason? EndReason { get; private set; }

    internal IReadOnlyList<EventId> PivotalEventIds => _pivotalEventIds;

    internal ExternalId? TerminalResolutionGroupId { get; private set; }

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

    internal void RecordPivotalEvent(EventId eventId, ExternalId? resolutionGroupId)
    {
        EnsureMutable();
        if (_pivotalEventIds.Contains(eventId))
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Outcome.ToString(),
                "A pivotal event cannot be recorded twice.");
        }

        _pivotalEventIds.Add(eventId);
        if (resolutionGroupId.HasValue)
        {
            TerminalResolutionGroupId = resolutionGroupId;
        }
    }

    internal bool IsHitGroupConsumed(ExternalId hitGroupId, FighterId targetId) =>
        _consumedHitGroups.Contains(ConsumptionKey(hitGroupId, targetId));

    internal void ConsumeHitGroup(ExternalId hitGroupId, FighterId targetId)
    {
        EnsureMutable();
        if (!_consumedHitGroups.Add(ConsumptionKey(hitGroupId, targetId)))
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Resolve.ToString(),
                $"Hit group '{hitGroupId}' was consumed twice for {targetId}.");
        }
    }

    internal bool CanBeGrabbed(FighterId fighterId, int tick) =>
        !_grabLockoutUntil.TryGetValue(fighterId, out var until) || tick >= until;

    internal void StartGrab(
        ExternalId grabId,
        FighterId grabberId,
        FighterId grabbedId,
        int startTick,
        int maximumHoldTicks)
    {
        EnsureMutable();
        if (_activeGrab.HasValue || grabberId == grabbedId || maximumHoldTicks < 1 ||
            !CanBeGrabbed(grabbedId, startTick))
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.WallsAndGrabs.ToString(),
                "The requested grab cannot start from the current state.");
        }

        _activeGrab = new ActiveGrabRuntime(
            grabId,
            grabberId,
            grabbedId,
            startTick,
            checked(startTick + maximumHoldTicks));
    }

    internal ActiveGrabRuntime EndGrab(int releaseTick, int lockoutTicks)
    {
        EnsureMutable();
        if (!_activeGrab.HasValue || releaseTick < _activeGrab.Value.StartTick || lockoutTicks < 0)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.WallsAndGrabs.ToString(),
                "The requested grab cannot end from the current state.");
        }

        var ended = _activeGrab.Value;
        _activeGrab = null;
        _grabLockoutUntil[ended.GrabbedId] = checked(releaseTick + lockoutTicks);
        return ended;
    }

    internal int GrabLockoutUntil(FighterId fighterId) =>
        _grabLockoutUntil.TryGetValue(fighterId, out var until) ? until : 0;

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

    private static string ConsumptionKey(ExternalId hitGroupId, FighterId targetId) =>
        hitGroupId.Value + "\u001f" + ((int)targetId).ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal readonly record struct ActiveGrabRuntime(
    ExternalId GrabId,
    FighterId GrabberId,
    FighterId GrabbedId,
    int StartTick,
    int EndExclusiveTick);
