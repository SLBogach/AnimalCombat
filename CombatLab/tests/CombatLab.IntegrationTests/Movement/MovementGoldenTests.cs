using System.Globalization;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;
using Battle.Replay.CanonicalJson;
using Battle.Replay.Journal;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.Movement;

public sealed class MovementGoldenTests
{
    private static readonly CombatEventType[] ExpectedTypes =
    {
        CombatEventType.BattleStarted,
        CombatEventType.DecisionMade,
        CombatEventType.DecisionMade,
        CombatEventType.ActionCommitted,
        CombatEventType.ActionCommitted,
        CombatEventType.ActionPhaseChanged,
        CombatEventType.ActionPhaseChanged,
        CombatEventType.MoveStarted,
        CombatEventType.MoveStarted,
        CombatEventType.PositionChanged,
        CombatEventType.PositionChanged,
        CombatEventType.MoveEnded,
        CombatEventType.MoveEnded,
        CombatEventType.ActionPhaseChanged,
        CombatEventType.ActionPhaseChanged,
        CombatEventType.TimeoutReached,
        CombatEventType.DrawDeclared,
        CombatEventType.BattleEnded,
    };

    [Fact]
    public void WP07_TRACE_001_ApproachBandL3MatchesExactTrace()
    {
        var run = MovementEngineFixture.Run("approach-band-l3", 4_000, 6_555);

        AssertCommonTrace(
            run,
            "sys_approach",
            650,
            MoveStartKind.Approach,
            CommitDirection.Right,
            CommitDirection.Left,
            4_000,
            6_555,
            4_002,
            6_552,
            "PreferredRangeReached",
            "PreferredRangeReached");
        Assert.Equal(new[] { 4_002, 6_552 }, run.Result.Summary!.FinalFrames.Select(frame => frame.Position));
        AssertPosition(run.Journal.Events[9], 6_555, 6_552, -3, -3, 0);
        AssertPosition(run.Journal.Events[10], 4_000, 4_002, 2, 2, 0);
        AssertMoveEnded(run.Journal.Events[11], 6_555, 6_552, "PreferredRangeReached");
        AssertMoveEnded(run.Journal.Events[12], 4_000, 4_002, "PreferredRangeReached");
        AssertReplayAndRoundTrip(run);
    }

    [Fact]
    public void WP07_TRACE_002_RetreatBandL3MatchesMirroredTrace()
    {
        var run = MovementEngineFixture.Run("retreat-band-l3", 4_000, 6_445);

        AssertCommonTrace(
            run,
            "sys_retreat",
            450,
            MoveStartKind.Retreat,
            CommitDirection.Left,
            CommitDirection.Right,
            4_000,
            6_445,
            3_998,
            6_448,
            "PreferredRangeReached",
            "PreferredRangeReached");
        Assert.Equal(new[] { 3_998, 6_448 }, run.Result.Summary!.FinalFrames.Select(frame => frame.Position));
        AssertPosition(run.Journal.Events[9], 6_445, 6_448, 3, 3, 0);
        AssertPosition(run.Journal.Events[10], 4_000, 3_998, -2, -2, 0);
        AssertMoveEnded(run.Journal.Events[11], 6_445, 6_448, "PreferredRangeReached");
        AssertMoveEnded(run.Journal.Events[12], 4_000, 3_998, "PreferredRangeReached");
        AssertReplayAndRoundTrip(run);
    }

    [Fact]
    public void WP07_TRACE_003_RetreatWallL3RedistributesAndStopsWithoutImpact()
    {
        var run = MovementEngineFixture.Run("retreat-wall-l3", 521, 2_966);

        AssertCommonTrace(
            run,
            "sys_retreat",
            450,
            MoveStartKind.Retreat,
            CommitDirection.Left,
            CommitDirection.Right,
            521,
            2_966,
            520,
            2_970,
            "PreferredRangeReached",
            "WallReached");
        Assert.Equal(new[] { 520, 2_970 }, run.Result.Summary!.FinalFrames.Select(frame => frame.Position));
        AssertPosition(run.Journal.Events[9], 2_966, 2_970, 4, 4, 0);
        AssertPosition(run.Journal.Events[10], 521, 520, -2, -1, 1);
        AssertMoveEnded(run.Journal.Events[11], 2_966, 2_970, "PreferredRangeReached");
        AssertMoveEnded(run.Journal.Events[12], 521, 520, "WallReached");
        Assert.DoesNotContain(run.Journal.Events, item => item.Draft.EventType == CombatEventType.WallImpact);
        Assert.All(run.Result.Summary.FinalFrames, frame => Assert.Equal(0, frame.Stagger));
        AssertReplayAndRoundTrip(run);
    }

    [Fact]
    public void WP07_DET_001_ApproachTraceIsIdenticalAcrossOneHundredRunsAndProfiles()
    {
        var baseline = MovementEngineFixture.Run("approach-band-l3", 4_000, 6_555);
        var bodies = baseline.Journal.Events.Select(item => item.CanonicalJson.ToArray()).ToArray();

        for (var repetition = 1; repetition < 100; repetition++)
        {
            var current = MovementEngineFixture.Run("approach-band-l3", 4_000, 6_555);
            Assert.Equal(baseline.Journal.InputDigest, current.Journal.InputDigest);
            Assert.Equal(baseline.Journal.FinalDigest, current.Journal.FinalDigest);
            Assert.Equal(baseline.Result.FinalDigest, current.Result.FinalDigest);
            AssertSummaryEquivalent(baseline.Result.Summary!, current.Result.Summary!);
            Assert.Equal(bodies.Length, current.Journal.Events.Count);
            for (var index = 0; index < bodies.Length; index++)
            {
                Assert.Equal(bodies[index], current.Journal.Events[index].CanonicalJson.ToArray());
            }
        }

    }

    [Fact]
    public void WP07_DET_002_MovementProfilesShareResultAndIntegrityChain()
    {
        var standard = MovementEngineFixture.Run("approach-band-l3", 4_000, 6_555);
        var diagnostic = MovementEngineFixture.Run(
            "approach-band-l3",
            4_000,
            6_555,
            JournalProfile.DiagnosticReplay);
        var summaryOnly = MovementEngineFixture.RunSummaryOnly(
            "approach-band-l3",
            4_000,
            6_555);

        Assert.Equal(BattleResultStatus.Completed, summaryOnly.Result.Status);
        Assert.Equal(standard.Journal.InputDigest, diagnostic.Journal.InputDigest);
        Assert.Equal(standard.Journal.InputDigest, summaryOnly.Journal.InputDigest);
        Assert.Equal(standard.Journal.FinalDigest, diagnostic.Journal.FinalDigest);
        Assert.Equal(standard.Journal.FinalDigest, summaryOnly.Journal.FinalDigest);
        Assert.Equal(standard.Result.FinalDigest, diagnostic.Result.FinalDigest);
        Assert.Equal(standard.Result.FinalDigest, summaryOnly.Result.FinalDigest);
        Assert.Equal(18, summaryOnly.Journal.EventCount);
        Assert.Null(summaryOnly.Result.ReplayId);
        AssertSummaryEquivalent(standard.Result.Summary!, diagnostic.Result.Summary!);
        AssertSummaryEquivalent(standard.Result.Summary!, summaryOnly.Result.Summary!);
        Assert.Equal(
            standard.Journal.Events.Select(item => item.CanonicalJson.ToArray()).ToArray(),
            diagnostic.Journal.Events.Select(item => item.CanonicalJson.ToArray()).ToArray());
    }

    [Fact]
    public void WP07_DET_004_ApproachReplayIsIndependentOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var baseline = MovementEngineFixture.Run("approach-band-l3", 4_000, 6_555);
            var baselineReplay = WriteReplay(baseline);
            var baselineBodies = baseline.Journal.Events
                .Select(item => item.CanonicalJson.ToArray())
                .ToArray();

            foreach (var cultureName in new[] { "ru-RU", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                var current = MovementEngineFixture.Run("approach-band-l3", 4_000, 6_555);

                Assert.Equal(baseline.Journal.InputDigest, current.Journal.InputDigest);
                Assert.Equal(baseline.Journal.FinalDigest, current.Journal.FinalDigest);
                Assert.Equal(baselineReplay, WriteReplay(current));
                Assert.Equal(baselineBodies, current.Journal.Events
                    .Select(item => item.CanonicalJson.ToArray())
                    .ToArray());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static void AssertCommonTrace(
        MovementEngineRun run,
        string actionId,
        int actionWeight,
        MoveStartKind movementKind,
        CommitDirection directionA,
        CommitDirection directionB,
        int initialPositionA,
        int initialPositionB,
        int finalPositionA,
        int finalPositionB,
        string stopReasonB,
        string stopReasonA)
    {
        var events = run.Journal.Events;
        var stableActionId = new StableId(actionId);
        var decisionA = new DecisionId("dec-fighter_a-000001");
        var decisionB = new DecisionId("dec-fighter_b-000001");
        var actionState = movementKind == MoveStartKind.Approach
            ? FighterState.Approach
            : FighterState.Retreat;
        var movementDirectionA = directionA == CommitDirection.Right
            ? MovementDirection.Right
            : MovementDirection.Left;
        var movementDirectionB = directionB == CommitDirection.Right
            ? MovementDirection.Right
            : MovementDirection.Left;
        var expectedSources = new string?[]
        {
            null, "evt-0000000000", "evt-0000000000", "evt-0000000001", "evt-0000000002",
            "evt-0000000004", "evt-0000000003", "evt-0000000005", "evt-0000000006",
            "evt-0000000007", "evt-0000000008", "evt-0000000009", "evt-0000000010",
            "evt-0000000011", "evt-0000000012", null, "evt-0000000015", "evt-0000000016",
        };

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        Assert.Equal(ExpectedTypes, events.Select(item => item.Draft.EventType));
        Assert.Equal(
            Enumerable.Range(0, 18).Select(index => EventId.FromSequence(index)),
            events.Select(item => item.Draft.EventId));
        Assert.Equal(
            new[] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 3, 3, 3 },
            events.Select(item => item.Draft.Tick));
        Assert.Equal(expectedSources, events.Select(item => item.Draft.SourceEventId?.Value));
        Assert.Equal(
            new FighterId?[]
            {
                null, FighterId.FighterA, FighterId.FighterB, FighterId.FighterA, FighterId.FighterB,
                FighterId.FighterB, FighterId.FighterA, FighterId.FighterB, FighterId.FighterA,
                FighterId.FighterB, FighterId.FighterA, FighterId.FighterB, FighterId.FighterA,
                FighterId.FighterB, FighterId.FighterA, null, null, null,
            },
            events.Select(item => item.Draft.ActorId));
        Assert.Equal(
            new FighterId?[]
            {
                null, FighterId.FighterB, FighterId.FighterA, FighterId.FighterB, FighterId.FighterA,
                null, null, null, null, null, null, null, null, null, null, null, null, null,
            },
            events.Select(item => item.Draft.TargetId));
        Assert.Equal(
            new string?[]
            {
                null, decisionA.Value, decisionB.Value, decisionA.Value, decisionB.Value,
                decisionB.Value, decisionA.Value, decisionB.Value, decisionA.Value,
                decisionB.Value, decisionA.Value, decisionB.Value, decisionA.Value,
                decisionB.Value, decisionA.Value, null, null, null,
            },
            events.Select(item => item.Draft.DecisionId?.Value));
        Assert.Equal(
            Enumerable.Range(0, 18).Select(index => index is >= 1 and <= 14 ? actionId : null),
            events.Select(item => item.Draft.ActionId?.Value));
        Assert.Equal(
            new[]
            {
                "Initialization", "OnlyLegalAction", "OnlyLegalAction", "ActionSelected", "ActionSelected",
                "StartupCompleted", "StartupCompleted", "MovementStarted", "MovementStarted",
                "VoluntaryMovement", "VoluntaryMovement", stopReasonB, stopReasonA,
                "MovementCompleted", "MovementCompleted", "TimeLimitReached",
                "TimeoutEqualHealthFraction", "TimeoutEqualHealthFraction",
            },
            events.Select(item => Assert.Single(item.Draft.ReasonCodes).Value));
        Assert.All(events, item =>
        {
            Assert.Null(item.Draft.Rng);
            Assert.Null(item.Draft.ResolutionGroupId);
            Assert.Null(item.Draft.EffectId);
        });
        for (var index = 0; index < events.Count; index++)
        {
            var expectedRelated = expectedSources[index] is null
                ? Array.Empty<EventId>()
                : new[] { new EventId(expectedSources[index]!) };
            Assert.Equal(expectedRelated, events[index].Draft.Payload.RelatedEventIds);
        }

        var started = Assert.IsType<BattleStartedPayload>(events[0].Draft.Payload);
        Assert.Equal(run.Journal.InputDigest, started.InputDigest);
        Assert.Equal(new[] { FighterId.FighterB, FighterId.FighterA }, started.InitiativeOrder);
        Assert.Equal(InitiativeTieBreak.StatThenSeededHash, started.InitiativeTieBreak);
        var initialA = started.InitialFrames[0];
        var initialB = started.InitialFrames[1];
        AssertFrame(initialA, initialA, initialPositionA, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, initialB, initialPositionB, FighterState.DecisionReady, null, null, null);

        foreach (var index in new[] { 1, 2 })
        {
            var decision = Assert.IsType<DecisionMadePayload>(events[index].Draft.Payload);
            Assert.Equal(stableActionId, decision.ChosenActionId);
            Assert.Equal(new[] { stableActionId }, decision.LegalActionIds);
            Assert.Equal(1, decision.CandidateCount);
            Assert.Equal(actionWeight, decision.ChosenWeight);
            Assert.Equal(actionWeight, decision.WeightSum);
            Assert.Equal(DecisionSelectionMode.OnlyLegalAction, decision.SelectionMode);
            Assert.Empty(decision.DominantModifiers);
        }

        var committedA = Assert.IsType<ActionCommittedPayload>(events[3].Draft.Payload);
        var committedB = Assert.IsType<ActionCommittedPayload>(events[4].Draft.Payload);
        AssertCommit(committedA, FighterId.FighterB, initialPositionB, directionA);
        AssertCommit(committedB, FighterId.FighterA, initialPositionA, directionB);
        AssertPhase(events[5], ActionPhase.Startup, ActionPhase.Active, 5);
        AssertPhase(events[6], ActionPhase.Startup, ActionPhase.Active, 5);
        AssertPhase(events[13], ActionPhase.Active, ActionPhase.Recovery, 1);
        AssertPhase(events[14], ActionPhase.Active, ActionPhase.Recovery, 1);
        AssertMoveStarted(events[7], initialPositionB, movementDirectionB, 147, movementKind);
        AssertMoveStarted(events[8], initialPositionA, movementDirectionA, 82, movementKind);

        AssertEmptyFrames(events[0].Draft);
        foreach (var index in new[] { 15, 16, 17 })
        {
            AssertEmptyFrames(events[index].Draft);
        }

        AssertFrame(initialA, events[1].Draft.Before.Actor, initialPositionA, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, events[1].Draft.Before.Target, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialA, events[1].Draft.After.Actor, initialPositionA, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, events[1].Draft.After.Target, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, events[2].Draft.Before.Actor, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialA, events[2].Draft.Before.Target, initialPositionA, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, events[2].Draft.After.Actor, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialA, events[2].Draft.After.Target, initialPositionA, FighterState.DecisionReady, null, null, null);

        AssertFrame(initialA, events[3].Draft.Before.Actor, initialPositionA, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, events[3].Draft.Before.Target, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialA, events[3].Draft.After.Actor, initialPositionA, actionState, 1, stableActionId, ActionPhase.Startup);
        AssertFrame(initialB, events[3].Draft.After.Target, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialB, events[4].Draft.Before.Actor, initialPositionB, FighterState.DecisionReady, null, null, null);
        AssertFrame(initialA, events[4].Draft.Before.Target, initialPositionA, actionState, 1, stableActionId, ActionPhase.Startup);
        AssertFrame(initialB, events[4].Draft.After.Actor, initialPositionB, actionState, 1, stableActionId, ActionPhase.Startup);
        AssertFrame(initialA, events[4].Draft.After.Target, initialPositionA, actionState, 1, stableActionId, ActionPhase.Startup);

        AssertActorOnlyTransition(events[5].Draft, initialB, initialPositionB, actionState, 1, ActionPhase.Startup, initialPositionB, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[6].Draft, initialA, initialPositionA, actionState, 1, ActionPhase.Startup, initialPositionA, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[7].Draft, initialB, initialPositionB, actionState, 5, ActionPhase.Active, initialPositionB, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[8].Draft, initialA, initialPositionA, actionState, 5, ActionPhase.Active, initialPositionA, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[9].Draft, initialB, initialPositionB, actionState, 5, ActionPhase.Active, finalPositionB, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[10].Draft, initialA, initialPositionA, actionState, 5, ActionPhase.Active, finalPositionA, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[11].Draft, initialB, finalPositionB, actionState, 5, ActionPhase.Active, finalPositionB, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[12].Draft, initialA, finalPositionA, actionState, 5, ActionPhase.Active, finalPositionA, actionState, 5, ActionPhase.Active, stableActionId);
        AssertActorOnlyTransition(events[13].Draft, initialB, finalPositionB, actionState, 5, ActionPhase.Active, finalPositionB, FighterState.Recovery, 1, ActionPhase.Recovery, stableActionId);
        AssertActorOnlyTransition(events[14].Draft, initialA, finalPositionA, actionState, 5, ActionPhase.Active, finalPositionA, FighterState.Recovery, 1, ActionPhase.Recovery, stableActionId);

        var timeout = Assert.IsType<TimeoutReachedPayload>(events[15].Draft.Payload);
        Assert.Equal(initialA.MaxHealth, timeout.FighterAHealth);
        Assert.Equal(initialA.MaxHealth, timeout.FighterAMaxHealth);
        Assert.Equal(initialB.MaxHealth, timeout.FighterBHealth);
        Assert.Equal(initialB.MaxHealth, timeout.FighterBMaxHealth);
        Assert.Equal((long)initialA.MaxHealth * initialB.MaxHealth, timeout.LeftCrossProduct);
        Assert.Equal((long)initialB.MaxHealth * initialA.MaxHealth, timeout.RightCrossProduct);
        var draw = Assert.IsType<DrawDeclaredPayload>(events[16].Draft.Payload);
        Assert.Equal(DrawReason.TimeoutEqualHealthFraction, draw.DrawReason);
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, draw.ParticipantIds);
        Assert.Null(draw.SimultaneousGroupId);
        var ended = Assert.IsType<BattleEndedPayload>(events[17].Draft.Payload);
        Assert.Equal(BattleOutcome.Draw, ended.Summary.Outcome);
        Assert.Null(ended.Summary.WinnerFighterId);
        Assert.Equal(BattleEndReason.TimeoutEqualHealthFraction, ended.Summary.EndReason);
        Assert.Equal(3, ended.Summary.EndTick);
        Assert.Equal(3, ended.Summary.DurationTicks);
        Assert.Equal(18, ended.Summary.EventCount);

        Assert.Equal(FighterState.Recovery, run.Result.Summary!.FinalFrames[0].State);
        Assert.Equal(FighterState.Recovery, run.Result.Summary.FinalFrames[1].State);
        Assert.Equal(ActionPhase.Recovery, run.Result.Summary.FinalFrames[0].ActionPhase);
        Assert.Equal(1, run.Result.Summary.FinalFrames[0].StateTicksRemaining);
        AssertFrame(initialA, run.Result.Summary.FinalFrames[0], finalPositionA, FighterState.Recovery, 1, stableActionId, ActionPhase.Recovery);
        AssertFrame(initialB, run.Result.Summary.FinalFrames[1], finalPositionB, FighterState.Recovery, 1, stableActionId, ActionPhase.Recovery);
        Assert.Equal(18, run.Result.Summary.EventCount);
    }

    private static void AssertCommit(
        ActionCommittedPayload payload,
        FighterId target,
        int targetPosition,
        CommitDirection direction)
    {
        Assert.Equal(target, payload.TargetFighterId);
        Assert.Equal(targetPosition, payload.TargetPositionAtCommit);
        Assert.Equal(direction, payload.CommitDirection);
        Assert.Equal(0, payload.EnergyCost);
        Assert.Equal(0, payload.ResourceCost);
        Assert.Equal(1, payload.StartupTicks);
        Assert.Equal(5, payload.ActiveTicks);
        Assert.Equal(1, payload.RecoveryTicks);
        Assert.Equal(0, payload.CooldownTicks);
    }

    private static void AssertPhase(
        JournaledCombatEvent item,
        ActionPhase from,
        ActionPhase to,
        int phaseTicks)
    {
        var payload = Assert.IsType<ActionPhaseChangedPayload>(item.Draft.Payload);
        Assert.Equal(from, payload.FromPhase);
        Assert.Equal(to, payload.ToPhase);
        Assert.Equal(phaseTicks, payload.PhaseTicks);
    }

    private static void AssertMoveStarted(
        JournaledCombatEvent item,
        int fromPosition,
        MovementDirection direction,
        int speed,
        MoveStartKind movementKind)
    {
        var payload = Assert.IsType<MoveStartedPayload>(item.Draft.Payload);
        Assert.Equal(fromPosition, payload.FromPosition);
        Assert.Equal(direction, payload.Direction);
        Assert.Equal(speed, payload.SpeedPerTick);
        Assert.Equal(movementKind, payload.MovementKind);
        Assert.Equal(
            new[]
            {
                new ReasonCode("WallReached"),
                new ReasonCode("PreferredRangeReached"),
                new ReasonCode("SegmentExpired"),
            },
            payload.StopConditions);
    }

    private static void AssertEmptyFrames(CombatEventDraft draft)
    {
        Assert.Null(draft.Before.Actor);
        Assert.Null(draft.Before.Target);
        Assert.Null(draft.After.Actor);
        Assert.Null(draft.After.Target);
    }

    private static void AssertActorOnlyTransition(
        CombatEventDraft draft,
        FighterFrame template,
        int beforePosition,
        FighterState beforeState,
        int? beforeTicks,
        ActionPhase? beforePhase,
        int afterPosition,
        FighterState afterState,
        int? afterTicks,
        ActionPhase? afterPhase,
        StableId actionId)
    {
        AssertFrame(
            template,
            draft.Before.Actor,
            beforePosition,
            beforeState,
            beforeTicks,
            actionId,
            beforePhase);
        AssertFrame(
            template,
            draft.After.Actor,
            afterPosition,
            afterState,
            afterTicks,
            actionId,
            afterPhase);
        Assert.Null(draft.Before.Target);
        Assert.Null(draft.After.Target);
    }

    private static void AssertFrame(
        FighterFrame template,
        FighterFrame? actual,
        int position,
        FighterState state,
        int? stateTicks,
        StableId? actionId,
        ActionPhase? actionPhase)
    {
        Assert.NotNull(actual);
        Assert.Equal(template.FighterId, actual.FighterId);
        Assert.Equal(position, actual.Position);
        Assert.Equal(template.Facing, actual.Facing);
        Assert.Equal(state, actual.State);
        Assert.Equal(stateTicks, actual.StateTicksRemaining);
        Assert.Equal(actionId, actual.ActionId);
        Assert.Equal(actionPhase, actual.ActionPhase);
        Assert.Equal(template.Health, actual.Health);
        Assert.Equal(template.MaxHealth, actual.MaxHealth);
        Assert.Equal(template.Energy, actual.Energy);
        Assert.Equal(template.MaxEnergy, actual.MaxEnergy);
        Assert.Equal(template.UniqueResource, actual.UniqueResource);
        Assert.Equal(template.Stagger, actual.Stagger);
        Assert.Equal(template.StaggerThreshold, actual.StaggerThreshold);
        Assert.Equal(template.Effects, actual.Effects);
    }

    private static void AssertPosition(
        JournaledCombatEvent item,
        int from,
        int to,
        int requested,
        int actual,
        int wall)
    {
        var payload = Assert.IsType<PositionChangedPayload>(item.Draft.Payload);
        Assert.Equal(from, payload.FromPosition);
        Assert.Equal(to, payload.ToPosition);
        Assert.Equal(requested, payload.RequestedDelta);
        Assert.Equal(actual, payload.ActualDelta);
        Assert.Equal(wall, payload.BlockedByWall);
        Assert.Equal(PositionChangeKind.Voluntary, payload.MovementKind);
        Assert.Null(item.Draft.TargetId);
        Assert.Null(item.Draft.Before.Target);
        Assert.Null(item.Draft.After.Target);
    }

    private static void AssertMoveEnded(
        JournaledCombatEvent item,
        int from,
        int to,
        string reason)
    {
        var payload = Assert.IsType<MoveEndedPayload>(item.Draft.Payload);
        Assert.Equal(from, payload.FromPosition);
        Assert.Equal(to, payload.ToPosition);
        Assert.Equal(reason, payload.StopReason.Value);
        Assert.Equal(reason, Assert.Single(item.Draft.ReasonCodes).Value);
    }

    private static void AssertReplayAndRoundTrip(MovementEngineRun run)
    {
        var replay = WriteReplay(run);
        var verification = new ReplayVerifier(File.ReadAllBytes(MovementEngineFixture.SchemaPath())).Verify(replay);

        Assert.True(
            verification.IsValid,
            string.Join(Environment.NewLine, verification.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Empty(verification.Issues);
        Assert.Equal(replay, CanonicalJson.Canonicalize(replay));
    }

    private static byte[] WriteReplay(MovementEngineRun run) =>
        CanonicalReplayArtifactWriter.Write(
            run.Journal,
            new ReplayArtifactMetadata(
                new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
                new ExternalId("combat-lab-wp07-tests"),
                true,
                "WP-07 movement fixture"));

    private static void AssertSummaryEquivalent(BattleSummary expected, BattleSummary actual)
    {
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.WinnerFighterId, actual.WinnerFighterId);
        Assert.Equal(expected.EndReason, actual.EndReason);
        Assert.Equal(expected.EndTick, actual.EndTick);
        Assert.Equal(expected.DurationTicks, actual.DurationTicks);
        Assert.Equal(expected.EventCount, actual.EventCount);
        Assert.Equal(expected.PivotalEventIds, actual.PivotalEventIds);
        Assert.Equal(expected.FinalFrames.Count, actual.FinalFrames.Count);
        for (var index = 0; index < expected.FinalFrames.Count; index++)
        {
            var expectedFrame = expected.FinalFrames[index];
            var actualFrame = actual.FinalFrames[index];
            Assert.Equal(expectedFrame.FighterId, actualFrame.FighterId);
            Assert.Equal(expectedFrame.Position, actualFrame.Position);
            Assert.Equal(expectedFrame.Facing, actualFrame.Facing);
            Assert.Equal(expectedFrame.State, actualFrame.State);
            Assert.Equal(expectedFrame.StateTicksRemaining, actualFrame.StateTicksRemaining);
            Assert.Equal(expectedFrame.ActionId, actualFrame.ActionId);
            Assert.Equal(expectedFrame.ActionPhase, actualFrame.ActionPhase);
            Assert.Equal(expectedFrame.Health, actualFrame.Health);
            Assert.Equal(expectedFrame.MaxHealth, actualFrame.MaxHealth);
            Assert.Equal(expectedFrame.Energy, actualFrame.Energy);
            Assert.Equal(expectedFrame.MaxEnergy, actualFrame.MaxEnergy);
            Assert.Equal(expectedFrame.UniqueResource, actualFrame.UniqueResource);
            Assert.Equal(expectedFrame.Stagger, actualFrame.Stagger);
            Assert.Equal(expectedFrame.StaggerThreshold, actualFrame.StaggerThreshold);
            Assert.Equal(expectedFrame.Effects, actualFrame.Effects);
        }
    }
}
