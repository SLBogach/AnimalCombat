using Battle.Core;
using Battle.Core.Engine;
using Battle.Core.Safety;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Core.UnitTests.Engine;

public sealed class SafetyAndTerminalTests
{
    [Fact]
    public void WP06_SAFE_001_EventCapPreservesTerminalSlotAndReturnsFailedInvariant()
    {
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(maximumEvents: 4),
            journal);

        Assert.Equal(BattleResultStatus.FailedInvariant, result.Status);
        Assert.Equal("EventCapExceeded", result.InvariantFailure!.Code.Value);
        Assert.Equal(2, journal.Drafts.Count);
        Assert.Equal(CombatEventType.BattleEnded, journal.Drafts[^1].EventType);
        Assert.Equal(BattleOutcome.Invalid, ((BattleEndedPayload)journal.Drafts[^1].Payload).Summary.Outcome);
        Assert.Equal(1, journal.CompleteCount);
        Assert.Null(result.Summary);
    }

    [Fact]
    public void WP06_SAFE_002_WatchdogFailsExactlyAtThresholdAndResetsOnChange()
    {
        var watchdog = new ZeroProgressWatchdog(2);
        var stamp = default(ProgressStamp);

        watchdog.Observe(stamp, stamp);
        Assert.Equal(1, watchdog.Counter);
        var changed = stamp with { FighterAHealth = 1 };
        watchdog.Observe(stamp, changed);
        Assert.Equal(0, watchdog.Counter);
        watchdog.Observe(stamp, stamp);
        var failure = Assert.Throws<EngineInvariantException>(() => watchdog.Observe(stamp, stamp));

        Assert.Equal("ZeroProgress", failure.Code.Value);
        Assert.Equal(2, watchdog.Counter);
    }

    [Fact]
    public void WP06_SAFE_003_SystemWaitLifecycleMakesProgressAcrossMultipleTicks()
    {
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(
                timeLimit: 8,
                maximumZeroProgressTicks: 1),
            journal);

        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Null(result.InvariantFailure);
        Assert.Equal(8, result.Summary!.EndTick);
        Assert.Equal(BattleEndReason.TimeoutEqualHealthFraction, result.Summary.EndReason);
        Assert.Equal(1, journal.CompleteCount);
    }

    [Fact]
    public void WP06_SAFE_002_TickSequenceAndRngOnlyChangesAreNotProgress()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var setup = EngineTestFixture.CreateSetup();
        var state = setup.State;
        var before = ProgressStamp.Capture(state);
        var journal = new RecordingJournal();
        var emitter = new CombatEventEmitter(request, config, journal, maximumEvents: 4);

        _ = emitter.Emit(
            0,
            new BattleStartedPayload(
                Array.Empty<EventId>(),
                EngineTestFixture.InputDigest,
                state.FinalFrames(),
                new[] { FighterId.FighterA, FighterId.FighterB },
                InitiativeTieBreak.StatThenSeededHash));
        state.AdvanceTick();
        _ = state.FighterA.NextDecisionId();
        _ = state.Rng.Decision.NextInt(0, 2, RngOperation.NextInt);
        var after = ProgressStamp.Capture(state);

        Assert.Equal(before, after);
        var watchdog = new ZeroProgressWatchdog(1);
        var failure = Assert.Throws<EngineInvariantException>(() =>
            watchdog.Observe(before, after));
        Assert.Equal("ZeroProgress", failure.Code.Value);
    }

    [Fact]
    public void WP06_SAFE_002_OutcomeMutationIsAuthoritativeProgress()
    {
        var state = EngineTestFixture.CreateSetup().State;
        var before = ProgressStamp.Capture(state);
        state.RecordOutcome(
            BattleOutcome.Draw,
            null,
            BattleEndReason.TimeoutEqualHealthFraction);
        var after = ProgressStamp.Capture(state);
        var watchdog = new ZeroProgressWatchdog(1);

        watchdog.Observe(before, after);

        Assert.NotEqual(before, after);
        Assert.Equal(0, watchdog.Counter);
    }

    [Fact]
    public void WP07_SAFE_001_PositionMutationResetsWatchdogButZeroDeltaEventDoesNot()
    {
        var state = EngineTestFixture.CreateSetup().State;
        var initial = ProgressStamp.Capture(state);
        var movementWatchdog = new ZeroProgressWatchdog(2);
        movementWatchdog.Observe(initial, initial);

        state.FighterA.ApplyPosition(checked(state.FighterA.Position + 1));
        var moved = ProgressStamp.Capture(state);
        movementWatchdog.Observe(initial, moved);

        Assert.NotEqual(initial, moved);
        Assert.Equal(0, movementWatchdog.Counter);

        var frame = state.FighterA.ToFrame();
        var emitter = new CombatEventEmitter(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(),
            new RecordingJournal(),
            maximumEvents: 4);
        _ = emitter.Emit(
            0,
            new PositionChangedPayload(
                Array.Empty<EventId>(),
                frame.Position,
                frame.Position,
                0,
                0,
                0,
                PositionChangeKind.Voluntary),
            actorId: FighterId.FighterA,
            before: new FramePair(frame, null),
            after: new FramePair(frame, null));
        var afterMarker = ProgressStamp.Capture(state);

        Assert.Equal(moved, afterMarker);
        var markerWatchdog = new ZeroProgressWatchdog(1);
        var failure = Assert.Throws<EngineInvariantException>(() =>
            markerWatchdog.Observe(moved, afterMarker));
        Assert.Equal("ZeroProgress", failure.Code.Value);
    }

    [Fact]
    public void WP06_TERM_001_EmitterRejectsCanonicalEventsAfterBattleEnded()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var journal = new RecordingJournal();
        var setup = EngineTestFixture.CreateSetup();
        var start = new Battle.Contracts.Replay.CombatJournalStart(
            request.BattleId,
            request.EngineVersion,
            Battle.Contracts.Versions.ContractVersions.Rng,
            Battle.Contracts.Versions.ContractVersions.Ordering,
            config.Reference,
            new Battle.Contracts.Replay.BattleInputSnapshot(request.MasterSeed, request.ModeRules.Id, setup.Settings.Arena),
            new Battle.Contracts.Replay.CombatJournalFighterStart(request.BuildA, setup.State.FighterA.ToFrame()),
            new Battle.Contracts.Replay.CombatJournalFighterStart(request.BuildB, setup.State.FighterB.ToFrame()));
        _ = journal.Begin(in start);
        var emitter = new CombatEventEmitter(request, config, journal, 4);
        var frames = setup.State.FinalFrames();
        var started = new BattleStartedPayload(
            Array.Empty<EventId>(),
            EngineTestFixture.InputDigest,
            frames,
            new[] { FighterId.FighterA, FighterId.FighterB },
            InitiativeTieBreak.StatThenSeededHash);
        _ = emitter.Emit(0, started);
        var summary = new BattleSummary(
            BattleOutcome.Draw,
            null,
            BattleEndReason.TimeoutEqualHealthFraction,
            0,
            0,
            2,
            Array.Empty<EventId>(),
            frames);
        _ = emitter.Emit(0, new BattleEndedPayload(summary));

        var failure = Assert.Throws<EngineInvariantException>(() =>
            emitter.Emit(1, new TimeoutReachedPayload(
                Array.Empty<EventId>(),
                1,
                1,
                1,
                1,
                1,
                1)));

        Assert.Equal("TerminalMutation", failure.Code.Value);
        Assert.Equal(2, journal.Drafts.Count);
    }
}
