using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Results;

public enum BattleOutcome
{
    FighterAWin,
    FighterBWin,
    Draw,
    Invalid,
}

public enum BattleEndReason
{
    Defeat,
    DoubleKO,
    TimeoutHealthFraction,
    TimeoutEqualHealthFraction,
    BattleInvalid,
}

public sealed class BattleSummary
{
    private readonly ReadOnlyCollection<EventId> _pivotalEventIds;
    private readonly ReadOnlyCollection<FighterFrame> _finalFrames;

    public BattleSummary(
        BattleOutcome outcome,
        FighterId? winnerFighterId,
        BattleEndReason endReason,
        int endTick,
        int durationTicks,
        long eventCount,
        IEnumerable<EventId> pivotalEventIds,
        IEnumerable<FighterFrame> finalFrames)
    {
        if (pivotalEventIds is null)
        {
            throw new ArgumentNullException(nameof(pivotalEventIds));
        }

        if (finalFrames is null)
        {
            throw new ArgumentNullException(nameof(finalFrames));
        }

        if (endTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endTick));
        }

        if (durationTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationTicks));
        }

        if (eventCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(eventCount));
        }

        if (outcome is < BattleOutcome.FighterAWin or > BattleOutcome.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        if (endReason is < BattleEndReason.Defeat or > BattleEndReason.BattleInvalid)
        {
            throw new ArgumentOutOfRangeException(nameof(endReason));
        }

        var expectedWinner = outcome switch
        {
            BattleOutcome.FighterAWin => FighterId.FighterA,
            BattleOutcome.FighterBWin => FighterId.FighterB,
            _ => (FighterId?)null,
        };
        if (winnerFighterId != expectedWinner)
        {
            throw new ArgumentException("Winner fighter ID must agree with the outcome.", nameof(winnerFighterId));
        }

        var pivotalIds = new List<EventId>(pivotalEventIds);
        if (pivotalIds.Count > 8 || HasDuplicates(pivotalIds))
        {
            throw new ArgumentException(
                "Pivotal event IDs must be unique and contain at most eight entries.",
                nameof(pivotalEventIds));
        }

        var frames = new List<FighterFrame>(finalFrames);
        if (frames.Count != 2 ||
            frames[0].FighterId != FighterId.FighterA ||
            frames[1].FighterId != FighterId.FighterB)
        {
            throw new ArgumentException(
                "Final frames must contain fighter A followed by fighter B.",
                nameof(finalFrames));
        }

        Outcome = outcome;
        WinnerFighterId = winnerFighterId;
        EndReason = endReason;
        EndTick = endTick;
        DurationTicks = durationTicks;
        EventCount = eventCount;
        _pivotalEventIds = new ReadOnlyCollection<EventId>(pivotalIds);
        _finalFrames = new ReadOnlyCollection<FighterFrame>(frames);
    }

    public BattleOutcome Outcome { get; }

    public FighterId? WinnerFighterId { get; }

    public BattleEndReason EndReason { get; }

    public int EndTick { get; }

    public int DurationTicks { get; }

    public long EventCount { get; }

    public IReadOnlyList<EventId> PivotalEventIds => _pivotalEventIds;

    public IReadOnlyList<FighterFrame> FinalFrames => _finalFrames;

    private static bool HasDuplicates(IReadOnlyList<EventId> values)
    {
        for (var left = 0; left < values.Count; left++)
        {
            for (var right = left + 1; right < values.Count; right++)
            {
                if (values[left] == values[right])
                {
                    return true;
                }
            }
        }

        return false;
    }
}
