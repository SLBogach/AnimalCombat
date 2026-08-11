using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace CombatLab.IntegrationTests.Movement;

public sealed class MovementLifecycleAndSafetyTests
{
    [Fact]
    public void WP07_LIFE_003_SegmentExpiresAfterExactlyFiveActiveMovementTicks()
    {
        var run = MovementEngineFixture.Run(
            "approach-segment-expiry",
            2_000,
            8_000,
            timeLimit: 7);

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        var started = run.Journal.Events.Where(item => item.Draft.EventType == CombatEventType.MoveStarted).ToArray();
        var positions = run.Journal.Events.Where(item => item.Draft.EventType == CombatEventType.PositionChanged).ToArray();
        var ended = run.Journal.Events.Where(item => item.Draft.EventType == CombatEventType.MoveEnded).ToArray();
        Assert.Equal(2, started.Length);
        Assert.Equal(10, positions.Length);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, positions.Select(item => item.Draft.Tick).Distinct());
        Assert.Equal(2, ended.Length);
        Assert.All(
            ended,
            item => Assert.Equal(
                "SegmentExpired",
                Assert.IsType<MoveEndedPayload>(item.Draft.Payload).StopReason.Value));
        Assert.All(ended, item => Assert.Equal(5, item.Draft.Tick));
        Assert.DoesNotContain(
            run.Journal.Events,
            item => item.Draft.EventType == CombatEventType.MoveStarted && item.Draft.Tick != 1);
    }

    [Fact]
    public void WP07_LIFE_005_RecoveryExpiryPrecedesTheNextDecisionInPhaseFive()
    {
        var run = MovementEngineFixture.Run(
            "approach-recovery-expiry",
            4_000,
            6_555,
            timeLimit: 4);
        var tickThree = run.Journal.Events.Where(item => item.Draft.Tick == 3).ToArray();

        Assert.Equal(
            new[]
            {
                CombatEventType.ActionPhaseChanged,
                CombatEventType.ActionPhaseChanged,
                CombatEventType.DecisionMade,
                CombatEventType.DecisionMade,
                CombatEventType.ActionCommitted,
                CombatEventType.ActionCommitted,
            },
            tickThree.Take(6).Select(item => item.Draft.EventType));
        var transitions = tickThree.Take(2).ToArray();
        Assert.Equal(
            new[] { FighterId.FighterB, FighterId.FighterA },
            transitions.Select(item => item.Draft.ActorId!.Value));
        Assert.Equal(
            new[] { EventId.FromSequence(15), EventId.FromSequence(16) },
            transitions.Select(item => item.Draft.EventId));
        Assert.Equal(
            new[] { EventId.FromSequence(13), EventId.FromSequence(14) },
            transitions.Select(item => item.Draft.SourceEventId!.Value));
        Assert.Equal(
            new[]
            {
                new DecisionId("dec-fighter_b-000001"),
                new DecisionId("dec-fighter_a-000001"),
            },
            transitions.Select(item => item.Draft.DecisionId!.Value));
        Assert.All(transitions, item => Assert.Equal(new StableId("sys_approach"), item.Draft.ActionId));

        var expectedPositions = new[] { 6_552, 4_002 };
        for (var index = 0; index < transitions.Length; index++)
        {
            var item = transitions[index];
            var payload = Assert.IsType<ActionPhaseChangedPayload>(item.Draft.Payload);
            Assert.Equal(ActionPhase.Recovery, payload.FromPhase);
            Assert.Null(payload.ToPhase);
            Assert.Equal(0, payload.PhaseTicks);
            Assert.Equal(new[] { item.Draft.SourceEventId!.Value }, payload.RelatedEventIds);
            Assert.Equal("RecoveryCompleted", Assert.Single(item.Draft.ReasonCodes).Value);
            Assert.Null(item.Draft.TargetId);
            Assert.Null(item.Draft.Before.Target);
            Assert.Null(item.Draft.After.Target);
            Assert.Null(item.Draft.Rng);
            Assert.Null(item.Draft.ResolutionGroupId);

            var before = Assert.IsType<FighterFrame>(item.Draft.Before.Actor);
            var after = Assert.IsType<FighterFrame>(item.Draft.After.Actor);
            Assert.Equal(expectedPositions[index], before.Position);
            Assert.Equal(before.Position, after.Position);
            Assert.Equal(before.Facing, after.Facing);
            Assert.Equal(FighterState.Recovery, before.State);
            Assert.Equal(1, before.StateTicksRemaining);
            Assert.Equal(new StableId("sys_approach"), before.ActionId);
            Assert.Equal(ActionPhase.Recovery, before.ActionPhase);
            Assert.Equal(FighterState.DecisionReady, after.State);
            Assert.Null(after.StateTicksRemaining);
            Assert.Null(after.ActionId);
            Assert.Null(after.ActionPhase);
            Assert.Equal(before.Health, after.Health);
            Assert.Equal(before.MaxHealth, after.MaxHealth);
            Assert.Equal(before.Energy, after.Energy);
            Assert.Equal(before.MaxEnergy, after.MaxEnergy);
            Assert.Equal(before.UniqueResource, after.UniqueResource);
            Assert.Equal(before.Stagger, after.Stagger);
            Assert.Equal(before.StaggerThreshold, after.StaggerThreshold);
            Assert.Equal(before.Effects, after.Effects);
        }
    }

    [Fact]
    public void WP07_SAFE_002_EventCapStillReservesTheTerminalSlotForMovementTrace()
    {
        var run = MovementEngineFixture.Run(
            "approach-event-cap",
            4_000,
            6_555,
            maximumEvents: 12);

        Assert.Equal(BattleResultStatus.FailedInvariant, run.Result.Status);
        Assert.Equal("EventCapExceeded", run.Result.InvariantFailure?.Code.Value);
        Assert.Equal(12, run.Journal.Events.Count);
        Assert.Equal(CombatEventType.BattleEnded, run.Journal.Events[^1].Draft.EventType);
        Assert.DoesNotContain(
            run.Journal.Events.Take(run.Journal.Events.Count - 1),
            item => item.Draft.EventType == CombatEventType.BattleEnded);
    }
}
