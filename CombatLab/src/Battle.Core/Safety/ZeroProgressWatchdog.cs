using Battle.Core.Engine;

namespace Battle.Core.Safety;

internal sealed class ZeroProgressWatchdog
{
    private readonly int _maximumZeroProgressTicks;

    internal ZeroProgressWatchdog(int maximumZeroProgressTicks)
    {
        if (maximumZeroProgressTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumZeroProgressTicks));
        }

        _maximumZeroProgressTicks = maximumZeroProgressTicks;
    }

    internal int Counter { get; private set; }

    internal void Observe(ProgressStamp before, ProgressStamp after)
    {
        if (before != after)
        {
            Counter = 0;
            return;
        }

        Counter = checked(Counter + 1);
        if (Counter == _maximumZeroProgressTicks)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.ZeroProgress,
                TickPhase.EndTick.ToString(),
                $"No authoritative gameplay progress for {Counter} consecutive ticks.");
        }

        if (Counter > _maximumZeroProgressTicks)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.ZeroProgress,
                TickPhase.EndTick.ToString(),
                "Zero-progress watchdog advanced beyond its configured threshold.");
        }
    }
}
