using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace CombatLab.IntegrationTests.Movement;

public sealed class Wp08MovementLifecycleRegressionTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_LIFE_003_Wp07MovementLifecycleOracleRemainsUnchanged()
    {
        var run = MovementEngineFixture.Run("wp08-life-003", 4_000, 6_555);
        var events = run.Journal.Events;

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        Assert.Equal(
            new[]
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
            },
            events.Select(item => item.Draft.EventType));

        foreach (var index in new[] { 13, 14 })
        {
            var draft = events[index].Draft;
            var payload = Assert.IsType<ActionPhaseChangedPayload>(draft.Payload);
            Assert.Equal(ActionPhase.Active, payload.FromPhase);
            Assert.Equal(ActionPhase.Recovery, payload.ToPhase);
            Assert.Equal(1, payload.PhaseTicks);
            Assert.Equal("MovementCompleted", Assert.Single(draft.ReasonCodes).Value);
            var expectedMoveEnded = draft.ActorId == FighterId.FighterB
                ? events[11].Draft
                : events[12].Draft;
            Assert.Equal(expectedMoveEnded.EventId, draft.SourceEventId);
            Assert.Equal(new[] { expectedMoveEnded.EventId }, payload.RelatedEventIds);
            Assert.Null(draft.TargetId);
            Assert.Null(draft.Before.Target);
            Assert.Null(draft.After.Target);
            Assert.Null(draft.Rng);
            Assert.Null(draft.ResolutionGroupId);
        }
    }
}
