using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Replay.Journal;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class WaitEqualL1GoldenTests
{
    [Fact]
    public void StandardReplay_MatchesExactWaitEqualL1Oracle()
    {
        var run = EngineShellFixture.RunCanonical();

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        Assert.Empty(run.Result.RejectionErrors);
        Assert.Null(run.Result.InvariantFailure);
        Assert.True(run.Journal.IsCompleted);
        Assert.Equal(EngineShellFixture.ReplayId, run.Result.ReplayId);
        Assert.Equal(run.Journal.FinalDigest, run.Result.FinalDigest);
        Assert.Equal(
            "sha256:89f3cf32381147cc18bd5f842060fb73d0730607068dcc72d7fccae8f183f8e2",
            run.Journal.InputDigest!.Value.Value);
        Assert.Equal(
            "sha256:95670ca45d0f1d9be0b72781871f23a1a44e6a7ed218306b42266c8ca3c6373b",
            run.Journal.FinalDigest!.Value.Value);
        var start = run.Journal.Start
            ?? throw new InvalidOperationException("Completed journal did not retain its start receipt.");
        Assert.Equal(EngineShellFixture.BattleId, start.BattleId);
        Assert.Equal(2_026_072_901UL, start.Input.MasterSeed);
        Assert.Equal(new StableId("engine_shell_wait_v01"), start.Input.ModeRulesId);
        Assert.Equal(0, start.Input.Arena.MinimumPosition);
        Assert.Equal(10_000, start.Input.Arena.MaximumPosition);
        Assert.Equal(2_000, start.Input.Arena.StartPositionA);
        Assert.Equal(8_000, start.Input.Arena.StartPositionB);

        var events = run.Journal.Events;
        Assert.Equal(8, events.Count);
        Assert.Equal(
            new[]
            {
                CombatEventType.BattleStarted,
                CombatEventType.DecisionMade,
                CombatEventType.DecisionMade,
                CombatEventType.ActionCommitted,
                CombatEventType.ActionCommitted,
                CombatEventType.TimeoutReached,
                CombatEventType.DrawDeclared,
                CombatEventType.BattleEnded,
            },
            events.Select(item => item.Draft.EventType));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 1, 1, 1 }, events.Select(item => item.Draft.Tick));
        Assert.Equal(Enumerable.Range(0, 8).Select(value => (long)value), events.Select(item => item.Draft.Sequence));
        Assert.Equal(
            Enumerable.Range(0, 8).Select(value => $"evt-{value:0000000000}"),
            events.Select(item => item.Draft.EventId.Value));
        Assert.Equal(
            new FighterId?[]
            {
                null,
                FighterId.FighterA,
                FighterId.FighterB,
                FighterId.FighterA,
                FighterId.FighterB,
                null,
                null,
                null,
            },
            events.Select(item => item.Draft.ActorId));
        Assert.Equal(
            new FighterId?[]
            {
                null,
                FighterId.FighterB,
                FighterId.FighterA,
                FighterId.FighterB,
                FighterId.FighterA,
                null,
                null,
                null,
            },
            events.Select(item => item.Draft.TargetId));
        Assert.Equal(
            new string?[]
            {
                null,
                "evt-0000000000",
                "evt-0000000000",
                "evt-0000000001",
                "evt-0000000002",
                null,
                "evt-0000000005",
                "evt-0000000006",
            },
            events.Select(item => item.Draft.SourceEventId?.Value));
        Assert.Equal(
            new string?[]
            {
                null,
                "dec-fighter_a-000001",
                "dec-fighter_b-000001",
                "dec-fighter_a-000001",
                "dec-fighter_b-000001",
                null,
                null,
                null,
            },
            events.Select(item => item.Draft.DecisionId?.Value));
        Assert.All(events, item => Assert.Null(item.Draft.Rng));

        Assert.Equal(
            new[]
            {
                "Initialization",
                "OnlyLegalAction",
                "OnlyLegalAction",
                "ActionSelected",
                "ActionSelected",
                "TimeLimitReached",
                "TimeoutEqualHealthFraction",
                "TimeoutEqualHealthFraction",
            },
            events.Select(item => Assert.Single(item.Draft.ReasonCodes).Value));

        AssertStarted(events[0], run);
        AssertDecision(events[1], FighterId.FighterA, FighterId.FighterB);
        AssertDecision(events[2], FighterId.FighterB, FighterId.FighterA);
        AssertCommit(events[3], FighterId.FighterB, 8_000);
        AssertCommit(events[4], FighterId.FighterA, 2_000);
        AssertTimeout(events[5]);
        AssertDraw(events[6]);
        AssertEnded(events[7], run);
    }

    private static void AssertStarted(JournaledCombatEvent item, CanonicalEngineRun run)
    {
        var draft = item.Draft;
        var payload = Assert.IsType<BattleStartedPayload>(draft.Payload);
        Assert.True(run.Journal.InputDigest.HasValue);
        Assert.Equal(run.Journal.InputDigest.Value, payload.InputDigest);
        Assert.Equal(
            new[] { FighterId.FighterB, FighterId.FighterA },
            payload.InitiativeOrder);
        Assert.Equal(InitiativeTieBreak.StatThenSeededHash, payload.InitiativeTieBreak);
        AssertInitialFrame(payload.InitialFrames[0], FighterId.FighterA, 2_000, Facing.Right, 1_650, "rage", 260);
        AssertInitialFrame(payload.InitialFrames[1], FighterId.FighterB, 8_000, Facing.Left, 1_150, "tempo", 180);
        Assert.Null(draft.Before.Actor);
        Assert.Null(draft.Before.Target);
        Assert.Null(draft.After.Actor);
        Assert.Null(draft.After.Target);
    }

    private static void AssertDecision(
        JournaledCombatEvent item,
        FighterId actor,
        FighterId target)
    {
        var draft = item.Draft;
        var payload = Assert.IsType<DecisionMadePayload>(draft.Payload);
        Assert.Equal(EngineShellFixture.WaitActionId, draft.ActionId);
        Assert.Equal(EngineShellFixture.WaitActionId, payload.ChosenActionId);
        Assert.Equal(new[] { EngineShellFixture.WaitActionId }, payload.LegalActionIds);
        Assert.Equal(1, payload.CandidateCount);
        Assert.Equal(150, payload.ChosenWeight);
        Assert.Equal(150, payload.WeightSum);
        Assert.Equal(DecisionSelectionMode.OnlyLegalAction, payload.SelectionMode);
        Assert.Empty(payload.DominantModifiers);
        Assert.Equal(actor, draft.Before.Actor?.FighterId);
        Assert.Equal(target, draft.Before.Target?.FighterId);
        Assert.Same(draft.Before.Actor, draft.After.Actor);
        Assert.Same(draft.Before.Target, draft.After.Target);
        Assert.Equal(FighterState.DecisionReady, draft.Before.Actor?.State);
    }

    private static void AssertCommit(
        JournaledCombatEvent item,
        FighterId target,
        int targetPosition)
    {
        var draft = item.Draft;
        var payload = Assert.IsType<ActionCommittedPayload>(draft.Payload);
        Assert.Equal(EngineShellFixture.WaitActionId, draft.ActionId);
        Assert.Equal(target, payload.TargetFighterId);
        Assert.Equal(0, payload.EnergyCost);
        Assert.Equal(0, payload.ResourceCost);
        Assert.Equal(0, payload.StartupTicks);
        Assert.Equal(3, payload.ActiveTicks);
        Assert.Equal(0, payload.RecoveryTicks);
        Assert.Equal(0, payload.CooldownTicks);
        Assert.Equal(CommitDirection.None, payload.CommitDirection);
        Assert.Equal(targetPosition, payload.TargetPositionAtCommit);
        Assert.Equal(FighterState.DecisionReady, draft.Before.Actor?.State);
        Assert.Equal(FighterState.Idle, draft.After.Actor?.State);
        Assert.Equal(EngineShellFixture.WaitActionId, draft.After.Actor?.ActionId);
        Assert.Equal(ActionPhase.Active, draft.After.Actor?.ActionPhase);
        Assert.Equal(3, draft.After.Actor?.StateTicksRemaining);
    }

    private static void AssertTimeout(JournaledCombatEvent item)
    {
        var payload = Assert.IsType<TimeoutReachedPayload>(item.Draft.Payload);
        Assert.Equal(1_650, payload.FighterAHealth);
        Assert.Equal(1_650, payload.FighterAMaxHealth);
        Assert.Equal(1_150, payload.FighterBHealth);
        Assert.Equal(1_150, payload.FighterBMaxHealth);
        Assert.Equal(1_897_500, payload.LeftCrossProduct);
        Assert.Equal(1_897_500, payload.RightCrossProduct);
        Assert.Null(item.Draft.Before.Actor);
        Assert.Null(item.Draft.After.Actor);
    }

    private static void AssertDraw(JournaledCombatEvent item)
    {
        var payload = Assert.IsType<DrawDeclaredPayload>(item.Draft.Payload);
        Assert.Equal(DrawReason.TimeoutEqualHealthFraction, payload.DrawReason);
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, payload.ParticipantIds);
        Assert.Null(payload.SimultaneousGroupId);
    }

    private static void AssertEnded(JournaledCombatEvent item, CanonicalEngineRun run)
    {
        var payload = Assert.IsType<BattleEndedPayload>(item.Draft.Payload);
        var summary = Assert.IsType<BattleSummary>(run.Result.Summary);
        Assert.Same(summary, payload.Summary);
        Assert.Same(summary, run.Journal.Summary);
        Assert.Equal(BattleOutcome.Draw, summary.Outcome);
        Assert.Null(summary.WinnerFighterId);
        Assert.Equal(BattleEndReason.TimeoutEqualHealthFraction, summary.EndReason);
        Assert.Equal(1, summary.EndTick);
        Assert.Equal(1, summary.DurationTicks);
        Assert.Equal(8, summary.EventCount);
        Assert.Equal(
            new[] { new EventId("evt-0000000005"), new EventId("evt-0000000006") },
            summary.PivotalEventIds);
        AssertFinalFrame(summary.FinalFrames[0], FighterId.FighterA, 2_000, Facing.Right, 1_650, "rage", 260);
        AssertFinalFrame(summary.FinalFrames[1], FighterId.FighterB, 8_000, Facing.Left, 1_150, "tempo", 180);
    }

    private static void AssertInitialFrame(
        FighterFrame frame,
        FighterId fighterId,
        int position,
        Facing facing,
        int health,
        string resourceId,
        int staggerThreshold)
    {
        AssertFrameCommon(frame, fighterId, position, facing, health, resourceId, staggerThreshold);
        Assert.Equal(FighterState.DecisionReady, frame.State);
        Assert.Null(frame.StateTicksRemaining);
        Assert.Null(frame.ActionId);
        Assert.Null(frame.ActionPhase);
    }

    private static void AssertFinalFrame(
        FighterFrame frame,
        FighterId fighterId,
        int position,
        Facing facing,
        int health,
        string resourceId,
        int staggerThreshold)
    {
        AssertFrameCommon(frame, fighterId, position, facing, health, resourceId, staggerThreshold);
        Assert.Equal(FighterState.Idle, frame.State);
        Assert.Equal(3, frame.StateTicksRemaining);
        Assert.Equal(EngineShellFixture.WaitActionId, frame.ActionId);
        Assert.Equal(ActionPhase.Active, frame.ActionPhase);
    }

    private static void AssertFrameCommon(
        FighterFrame frame,
        FighterId fighterId,
        int position,
        Facing facing,
        int health,
        string resourceId,
        int staggerThreshold)
    {
        Assert.Equal(fighterId, frame.FighterId);
        Assert.Equal(position, frame.Position);
        Assert.Equal(facing, frame.Facing);
        Assert.Equal(health, frame.Health);
        Assert.Equal(health, frame.MaxHealth);
        Assert.Equal(1_000, frame.Energy);
        Assert.Equal(1_000, frame.MaxEnergy);
        Assert.Equal(new StableId(resourceId), frame.UniqueResource.ResourceId);
        Assert.Equal(0, frame.UniqueResource.Value);
        Assert.Equal(1_000, frame.UniqueResource.Maximum);
        Assert.Equal(0, frame.Stagger);
        Assert.Equal(staggerThreshold, frame.StaggerThreshold);
        Assert.Empty(frame.Effects);
    }
}
