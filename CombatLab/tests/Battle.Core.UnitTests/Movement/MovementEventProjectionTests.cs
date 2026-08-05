using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Movement;
using Battle.Core.UnitTests.Engine;

namespace Battle.Core.UnitTests.Movement;

public sealed class MovementEventProjectionTests
{
    [Fact]
    public void WP07_TRACE_004_InwardOverlapEmitsExactFourPositionSubmutations()
    {
        var actionA = MovementAction("fixture_inward_a");
        var actionB = MovementAction("fixture_inward_b");
        var decisionA = new DecisionId("dec-fighter_a-000001");
        var decisionB = new DecisionId("dec-fighter_b-000001");
        var fighterA = Fighter(FighterId.FighterA, FighterSide.A, 4_000, Facing.Right);
        var fighterB = Fighter(FighterId.FighterB, FighterSide.B, 5_100, Facing.Left);
        fighterA.CommitSystemAction(actionA, decisionA, CommitDirection.Right, fighterB.Position);
        fighterB.CommitSystemAction(actionB, decisionB, CommitDirection.Left, fighterA.Position);

        var config = EngineTestFixture.CreateConfig();
        var request = EngineTestFixture.CreateRequest();
        var journal = new RecordingJournal();
        var emitter = new CombatEventEmitter(request, config, journal, maximumEvents: 200);
        for (var sequence = 0; sequence < 100; sequence++)
        {
            _ = emitter.Emit(
                0,
                new TimeoutReachedPayload(Array.Empty<EventId>(), 1, 1, 1, 1, 1, 1));
        }

        var startedB = EmitMoveStarted(emitter, fighterB, actionB.Id, decisionB, MovementDirection.Left);
        fighterB.MarkMovementStarted(startedB.EventId);
        var startedA = EmitMoveStarted(emitter, fighterA, actionA.Id, decisionA, MovementDirection.Right);
        fighterA.MarkMovementStarted(startedA.EventId);
        Assert.Equal(EventId.FromSequence(100), startedB.EventId);
        Assert.Equal(EventId.FromSequence(101), startedA.EventId);

        var separation = SeparationResolver.Resolve(
            new ArenaInterval(0, 10_000),
            new MovementParticipant(FighterId.FighterA, 4_000, 500, 100),
            100,
            new MovementParticipant(FighterId.FighterB, 5_100, 500, 100),
            -100,
            new[] { FighterId.FighterB, FighterId.FighterA });
        var result = new MovementPairResult(
            GapMovementMode.Approach,
            0,
            100,
            0,
            200,
            200,
            200,
            0,
            true,
            new PairAllocation(100, 100, 200),
            separation.Left,
            separation.Right);
        var state = new BattleState(fighterA, fighterB, masterSeed: 7);

        TickCoordinator.EmitResolvedPositionChanges(
            state,
            emitter,
            result,
            new[] { fighterB, fighterA });

        var drafts = journal.Drafts.Skip(102).ToArray();
        Assert.Equal(4, drafts.Length);
        Assert.Equal(
            new[] { FighterId.FighterB, FighterId.FighterA, FighterId.FighterB, FighterId.FighterA },
            drafts.Select(item => item.ActorId!.Value));
        Assert.Equal(
            new[] { EventId.FromSequence(102), EventId.FromSequence(103), EventId.FromSequence(104), EventId.FromSequence(105) },
            drafts.Select(item => item.EventId));

        AssertPosition(
            drafts[0],
            5_100,
            5_000,
            -100,
            PositionChangeKind.Voluntary,
            EventId.FromSequence(100),
            actionB.Id,
            decisionB,
            new[] { EventId.FromSequence(100) });
        AssertPosition(
            drafts[1],
            4_000,
            4_100,
            100,
            PositionChangeKind.Voluntary,
            EventId.FromSequence(101),
            actionA.Id,
            decisionA,
            new[] { EventId.FromSequence(101) });
        AssertPosition(
            drafts[2],
            5_000,
            5_050,
            50,
            PositionChangeKind.Separation,
            EventId.FromSequence(102),
            null,
            null,
            new[] { EventId.FromSequence(102), EventId.FromSequence(103) });
        AssertPosition(
            drafts[3],
            4_100,
            4_050,
            -50,
            PositionChangeKind.Separation,
            EventId.FromSequence(103),
            null,
            null,
            new[] { EventId.FromSequence(102), EventId.FromSequence(103) });

        Assert.Equal(4_050, fighterA.Position);
        Assert.Equal(5_050, fighterB.Position);
        Assert.Equal(1_000, fighterB.Position - fighterA.Position);
        Assert.Equal(Facing.Right, fighterA.Facing);
        Assert.Equal(Facing.Left, fighterB.Facing);
    }

    private static CombatEventIdentity EmitMoveStarted(
        CombatEventEmitter emitter,
        FighterRuntimeState fighter,
        StableId actionId,
        DecisionId decisionId,
        MovementDirection direction)
    {
        var frame = fighter.ToFrame();
        return emitter.Emit(
            0,
            new MoveStartedPayload(
                Array.Empty<EventId>(),
                fighter.Position,
                direction,
                100,
                MoveStartKind.Approach,
                new[]
                {
                    new ReasonCode("WallReached"),
                    new ReasonCode("PreferredRangeReached"),
                    new ReasonCode("SegmentExpired"),
                }),
            actorId: fighter.FighterId,
            actionId: actionId,
            decisionId: decisionId,
            reasonCodes: new[] { new ReasonCode("MovementStarted") },
            before: new FramePair(frame, null),
            after: new FramePair(frame, null));
    }

    private static void AssertPosition(
        CombatEventDraft draft,
        int from,
        int to,
        int delta,
        PositionChangeKind kind,
        EventId source,
        StableId? actionId,
        DecisionId? decisionId,
        IReadOnlyList<EventId> related)
    {
        var payload = Assert.IsType<PositionChangedPayload>(draft.Payload);
        Assert.Equal(from, payload.FromPosition);
        Assert.Equal(to, payload.ToPosition);
        Assert.Equal(delta, payload.RequestedDelta);
        Assert.Equal(delta, payload.ActualDelta);
        Assert.Equal(0, payload.BlockedByWall);
        Assert.Equal(kind, payload.MovementKind);
        Assert.Equal(source, draft.SourceEventId);
        Assert.Equal(related, payload.RelatedEventIds);
        Assert.Equal(actionId, draft.ActionId);
        Assert.Equal(decisionId, draft.DecisionId);
        Assert.Null(draft.TargetId);
        Assert.Null(draft.EffectId);
        Assert.Null(draft.Before.Target);
        Assert.Null(draft.After.Target);
        Assert.Null(draft.Rng);
        Assert.Null(draft.ResolutionGroupId);
        var before = Assert.IsType<FighterFrame>(draft.Before.Actor);
        var after = Assert.IsType<FighterFrame>(draft.After.Actor);
        Assert.Equal(from, before.Position);
        Assert.Equal(to, after.Position);
        Assert.Equal(before.FighterId, after.FighterId);
        Assert.Equal(before.Facing, after.Facing);
        Assert.Equal(before.State, after.State);
        Assert.Equal(before.StateTicksRemaining, after.StateTicksRemaining);
        Assert.Equal(before.ActionId, after.ActionId);
        Assert.Equal(before.ActionPhase, after.ActionPhase);
        Assert.Equal(before.Health, after.Health);
        Assert.Equal(before.MaxHealth, after.MaxHealth);
        Assert.Equal(before.Energy, after.Energy);
        Assert.Equal(before.MaxEnergy, after.MaxEnergy);
        Assert.Equal(before.UniqueResource, after.UniqueResource);
        Assert.Equal(before.Stagger, after.Stagger);
        Assert.Equal(before.StaggerThreshold, after.StaggerThreshold);
        Assert.Equal(before.Effects, after.Effects);
        Assert.Equal(
            kind == PositionChangeKind.Voluntary ? "VoluntaryMovement" : "SeparationCorrection",
            Assert.Single(draft.ReasonCodes).Value);
    }

    private static FighterRuntimeState Fighter(
        FighterId fighterId,
        FighterSide side,
        int position,
        Facing facing) =>
        new(
            fighterId,
            side,
            new StableId(fighterId == FighterId.FighterA ? "fixture_a" : "fixture_b"),
            position,
            facing,
            maximumHealth: 100,
            maximumEnergy: 100,
            resourceId: new StableId("fixture_resource"),
            resource: 0,
            maximumResource: 100,
            staggerThreshold: 100,
            initiative: fighterId == FighterId.FighterB ? 2 : 1,
            moveSpeed: 100,
            collisionRadius: 500);

    private static SystemActionDefinition MovementAction(string id) =>
        new(
            new StableId(id),
            Weight: 1,
            EnergyCost: 0,
            ResourceCost: 0,
            StartupTicks: 0,
            ActiveTicks: 5,
            RecoveryTicks: 0,
            CooldownTicks: 0,
            SystemMovementMode.Approach,
            PreferredRangeMinimum: 0,
            PreferredRangeMaximum: 1_600,
            TrackTarget: true);
}
