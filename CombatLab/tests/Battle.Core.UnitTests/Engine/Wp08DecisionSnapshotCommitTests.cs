using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.UnitTests.Engine;

public sealed class Wp08DecisionSnapshotCommitTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SNP_001_PhaseFiveSnapshotSeesRecoveryCompletionFromPhaseFour()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            SelfAction(Wp08EngineTestFixture.BearPrimaryId.Value),
        });
        var harness = Wp08EngineTestFixture.CreateHarness(config);
        Wp08EngineTestFixture.MakeBusy(harness, FighterId.FighterB);
        var actor = harness.State.FighterA;
        var priorDecision = actor.PeekNextDecisionId();
        actor.CommitDecisionId(priorDecision);
        actor.CommitCombatAction(Wp08EngineTestFixture.Descriptor(
            actor,
            harness.State.FighterB,
            priorDecision,
            actionId: "fixture_prior_combat"));
        actor.SetActionPhaseForTesting(ActionPhase.Recovery);
        actor.SetStateForTesting(FighterState.Recovery);
        actor.SetActionTimerForTesting(1);
        actor.SetOpportunityDebtForTesting(Wp08EngineTestFixture.BearPrimaryId, 4);
        harness.Start();
        actor.RecordCombatCommit(harness.StartedEvent.EventId);

        _ = harness.RunTick();

        var lifecycle = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.ActionPhaseChanged &&
            draft.ActorId == FighterId.FighterA);
        var transition = Assert.IsType<ActionPhaseChangedPayload>(lifecycle.Payload);
        Assert.Equal(ActionPhase.Recovery, transition.FromPhase);
        Assert.Null(transition.ToPhase);
        Assert.Equal(FighterState.DecisionReady, Assert.IsType<FighterFrame>(lifecycle.After.Actor).State);

        var decision = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.DecisionMade &&
            draft.ActorId == FighterId.FighterA);
        Assert.True(lifecycle.Sequence < decision.Sequence);
        var phaseFiveActor = Assert.IsType<FighterFrame>(decision.Before.Actor);
        Assert.Equal(FighterState.DecisionReady, phaseFiveActor.State);
        Assert.Null(phaseFiveActor.ActionId);
        Assert.Equal(Wp08EngineTestFixture.BearPrimaryId, decision.ActionId);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SNP_002_CooldownReachingZeroInPhaseThreeIsLegalInSameTick()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            SelfAction(Wp08EngineTestFixture.BearPrimaryId.Value) with { CooldownTicks = 5 },
        });
        var harness = Wp08EngineTestFixture.CreateHarness(config);
        Wp08EngineTestFixture.MakeBusy(harness, FighterId.FighterB);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.State.FighterA.SetCooldownForTesting(Wp08EngineTestFixture.BearPrimaryId, 1);
        harness.Start();

        _ = harness.RunTick();

        var decision = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.DecisionMade &&
            draft.ActorId == FighterId.FighterA);
        var payload = Assert.IsType<DecisionMadePayload>(decision.Payload);
        Assert.Contains(Wp08EngineTestFixture.BearPrimaryId, payload.LegalActionIds);
        Assert.Equal(Wp08EngineTestFixture.BearPrimaryId, payload.ChosenActionId);
        Assert.Equal(5, harness.State.FighterA.CooldownFor(Wp08EngineTestFixture.BearPrimaryId));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SNP_003_BDecisionUsesSharedPrecommitSnapshotAfterACommitMutation()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value) with
            {
                EnergyCost = 25,
                ResourceCost = 25,
            },
        });
        var harness = Wp08EngineTestFixture.CreateHarness(
            config,
            Wp08EngineTestFixture.CreateSymmetricBearRequest());
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterB,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.Start();
        var initialA = harness.State.FighterA.ToFrame();

        _ = harness.RunTick();

        var decisions = harness.Journal.Drafts
            .Where(draft => draft.EventType == CombatEventType.DecisionMade)
            .ToArray();
        Assert.Equal(2, decisions.Length);
        var aPayload = Assert.IsType<DecisionMadePayload>(decisions[0].Payload);
        var bPayload = Assert.IsType<DecisionMadePayload>(decisions[1].Payload);
        Assert.Equal(aPayload.LegalActionIds, bPayload.LegalActionIds);
        Assert.Equal(aPayload.ChosenWeight, bPayload.ChosenWeight);
        Assert.Equal(aPayload.WeightSum, bPayload.WeightSum);
        var bTargetAtDecision = Assert.IsType<FighterFrame>(decisions[1].Before.Target);
        Assert.Equal(initialA.Position, bTargetAtDecision.Position);
        Assert.Equal(initialA.Energy, bTargetAtDecision.Energy);
        Assert.Equal(initialA.UniqueResource, bTargetAtDecision.UniqueResource);
        Assert.Equal(initialA.ActionId, bTargetAtDecision.ActionId);
        Assert.Equal(FighterState.DecisionReady, bTargetAtDecision.State);

        var commitB = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.ActionCommitted &&
            draft.ActorId == FighterId.FighterB);
        var authoritativeTarget = Assert.IsType<FighterFrame>(commitB.Before.Target);
        Assert.Equal(FighterState.AttackPrepare, authoritativeTarget.State);
        Assert.Equal(Wp08EngineTestFixture.BearPrimaryId, authoritativeTarget.ActionId);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_001_DecisionsAndAuthoritativeCommitsRemainABDespiteInitiativeBA()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value),
            OpponentAction(Wp08EngineTestFixture.KangarooPrimaryId.Value),
        });
        var harness = Wp08EngineTestFixture.CreateHarness(config);
        Assert.Equal(FighterId.FighterB, harness.Setup.InitiativeOrder[0]);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterB,
            Wp08EngineTestFixture.KangarooPrimaryId);
        var positionA = harness.State.FighterA.Position;
        var positionB = harness.State.FighterB.Position;
        harness.Start();

        _ = harness.RunTick();

        var batch = harness.Journal.Drafts.Skip(1).Take(4).ToArray();
        Assert.Equal(
            new[]
            {
                CombatEventType.DecisionMade,
                CombatEventType.DecisionMade,
                CombatEventType.ActionCommitted,
                CombatEventType.ActionCommitted,
            },
            batch.Select(draft => draft.EventType));
        Assert.Equal(
            new[]
            {
                FighterId.FighterA,
                FighterId.FighterB,
                FighterId.FighterA,
                FighterId.FighterB,
            },
            batch.Select(draft => draft.ActorId!.Value));
        Assert.Equal(positionB, Assert.IsType<ActionCommittedPayload>(batch[2].Payload).TargetPositionAtCommit);
        Assert.Equal(positionA, Assert.IsType<ActionCommittedPayload>(batch[3].Payload).TargetPositionAtCommit);
        Assert.Equal(FighterState.DecisionReady, Assert.IsType<FighterFrame>(batch[1].Before.Target).State);
        Assert.Equal(FighterState.AttackPrepare, Assert.IsType<FighterFrame>(batch[3].Before.Target).State);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_002_EnergyAndUniqueResourceCostsDeductOnceInCanonicalOrder()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value) with
            {
                EnergyCost = 75,
                ResourceCost = 125,
            },
        });
        var harness = OneActorHarness(config);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId,
            resource: 500);
        harness.Start();

        _ = harness.RunTick();

        var committed = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.ActionCommitted);
        var commitPayload = Assert.IsType<ActionCommittedPayload>(committed.Payload);
        Assert.Equal(75, commitPayload.EnergyCost);
        Assert.Equal(125, commitPayload.ResourceCost);
        var resources = harness.Journal.Drafts
            .Where(draft => draft.EventType == CombatEventType.ResourceChanged)
            .ToArray();
        Assert.Equal(2, resources.Length);
        Assert.Equal(new[] { ResourceKind.Energy, ResourceKind.UniqueResource },
            resources.Select(draft => Assert.IsType<ResourceChangedPayload>(draft.Payload).ResourceKind));
        AssertResource(resources[0], 1_000, -75, 925, committed.EventId, resourceId: null);
        AssertResource(resources[1], 500, -125, 375, committed.EventId, new StableId("rage"));
        Assert.Equal(925, harness.State.FighterA.Energy);
        Assert.Equal(375, harness.State.FighterA.Resource);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_003_ZeroCostsEmitNoResourceChangedButRemainInCommitPayload()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value),
        });
        var harness = OneActorHarness(config);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.Start();

        _ = harness.RunTick();

        var commit = Assert.IsType<ActionCommittedPayload>(Assert.Single(
            harness.Journal.Drafts,
            draft => draft.EventType == CombatEventType.ActionCommitted).Payload);
        Assert.Equal(0, commit.EnergyCost);
        Assert.Equal(0, commit.ResourceCost);
        Assert.DoesNotContain(
            harness.Journal.Drafts,
            draft => draft.EventType == CombatEventType.ResourceChanged);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_004_CooldownStartsAtCommitAndFirstDecrementsOnNextTick()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            SelfAction(Wp08EngineTestFixture.BearPrimaryId.Value) with { CooldownTicks = 2 },
        });
        var harness = OneActorHarness(config);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.Start();

        _ = harness.RunTick();
        Assert.Equal(2, harness.State.FighterA.CooldownFor(Wp08EngineTestFixture.BearPrimaryId));
        _ = harness.RunTick();
        Assert.Equal(1, harness.State.FighterA.CooldownFor(Wp08EngineTestFixture.BearPrimaryId));
        _ = harness.RunTick();
        Assert.Equal(0, harness.State.FighterA.CooldownFor(Wp08EngineTestFixture.BearPrimaryId));
        _ = harness.RunTick();

        var nextDecision = harness.Journal.Drafts
            .Where(draft => draft.EventType == CombatEventType.DecisionMade &&
                            draft.ActorId == FighterId.FighterA)
            .Skip(1)
            .Select(draft => Assert.IsType<DecisionMadePayload>(draft.Payload))
            .Single();
        Assert.Contains(Wp08EngineTestFixture.BearPrimaryId, nextDecision.LegalActionIds);
    }

    [Theory]
    [InlineData(1, 15, 30)]
    [InlineData(100, 10, 20)]
    [InlineData(300, 7, 13)]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_005_ActionSpeedScalesAndClampsFrozenStartupRecovery(
        int actionSpeed,
        int expectedStartup,
        int expectedRecovery)
    {
        var config = Wp08EngineTestFixture.CreateConfig(
            new[]
            {
                SelfAction(Wp08EngineTestFixture.BearPrimaryId.Value) with
                {
                    StartupBaseTicks = 10,
                    StartupMinimumTicks = 7,
                    StartupMaximumTicks = 15,
                    ActiveTicks = 3,
                    RecoveryBaseTicks = 20,
                    RecoveryMinimumTicks = 10,
                    RecoveryMaximumTicks = 30,
                },
            },
            bearActionSpeed: actionSpeed);
        var harness = OneActorHarness(config);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.Start();

        _ = harness.RunTick();

        var commit = Assert.IsType<ActionCommittedPayload>(Assert.Single(
            harness.Journal.Drafts,
            draft => draft.EventType == CombatEventType.ActionCommitted).Payload);
        Assert.Equal(expectedStartup, commit.StartupTicks);
        Assert.Equal(3, commit.ActiveTicks);
        Assert.Equal(expectedRecovery, commit.RecoveryTicks);
        Assert.Equal(expectedStartup, harness.State.FighterA.StateTicksRemaining);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_006_PostCommitResourceLossDoesNotRevalidateCancelOrRefund()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value) with
            {
                EnergyCost = 100,
                ResourceCost = 100,
                StartupBaseTicks = 2,
                StartupMinimumTicks = 2,
                StartupMaximumTicks = 2,
                ActiveTicks = 2,
            },
        });
        var harness = OneActorHarness(config);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId,
            resource: 200);
        harness.Start();
        _ = harness.RunTick();
        var actionId = harness.State.FighterA.ActionId;
        harness.State.FighterA.SetEnergyForTesting(0);
        harness.State.FighterA.SetResourceForTesting(0);

        _ = harness.RunTick();
        _ = harness.RunTick();

        Assert.Equal(actionId, harness.State.FighterA.ActionId);
        Assert.Equal(ActionPhase.Active, harness.State.FighterA.ActionPhase);
        Assert.Equal(0, harness.State.FighterA.Energy);
        Assert.Equal(0, harness.State.FighterA.Resource);
        Assert.DoesNotContain(
            harness.Journal.Drafts,
            draft => draft.EventType == CombatEventType.ActionCancelled);
        Assert.Equal(2, harness.Journal.Drafts.Count(draft =>
            draft.EventType == CombatEventType.ResourceChanged));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_007_OpponentAndSelfTargetsFreezeExactNullAndDirectionSemantics()
    {
        var opponent = RunTargetCase(OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value));
        var opponentDecision = Assert.Single(opponent.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.DecisionMade);
        var opponentCommit = Assert.IsType<ActionCommittedPayload>(Assert.Single(
            opponent.Journal.Drafts,
            draft => draft.EventType == CombatEventType.ActionCommitted).Payload);
        Assert.Equal(FighterId.FighterB, opponentDecision.TargetId);
        Assert.Equal(FighterId.FighterB, Assert.IsType<FighterFrame>(opponentDecision.Before.Target).FighterId);
        Assert.Equal(FighterId.FighterB, opponentCommit.TargetFighterId);
        Assert.Equal(4_500, opponentCommit.TargetPositionAtCommit);
        Assert.Equal(CommitDirection.Right, opponentCommit.CommitDirection);

        var self = RunTargetCase(SelfAction(Wp08EngineTestFixture.BearPrimaryId.Value));
        var selfDecision = Assert.Single(self.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.DecisionMade);
        var selfCommit = Assert.IsType<ActionCommittedPayload>(Assert.Single(
            self.Journal.Drafts,
            draft => draft.EventType == CombatEventType.ActionCommitted).Payload);
        Assert.Null(selfDecision.TargetId);
        Assert.Null(selfDecision.Before.Target);
        Assert.Null(selfCommit.TargetFighterId);
        Assert.Null(selfCommit.TargetPositionAtCommit);
        Assert.Equal(CommitDirection.None, selfCommit.CommitDirection);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_008_HitScheduleEmitsOneTelegraphWithAbsoluteSortedTicks()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            OpponentAction(Wp08EngineTestFixture.BearPrimaryId.Value) with
            {
                StartupBaseTicks = 2,
                StartupMinimumTicks = 2,
                StartupMaximumTicks = 2,
                ActiveTicks = 5,
                HitCount = 2,
                HitSchedule = "0|3",
            },
        });
        var harness = OneActorHarness(config);
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.Start();

        _ = harness.RunTick();

        var prepared = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.AttackPrepared);
        var payload = Assert.IsType<AttackPreparedPayload>(prepared.Payload);
        Assert.Equal(0, payload.TelegraphTick);
        Assert.Equal(new[] { 2, 5 }, payload.ImpactTicks);
        Assert.True(payload.DirectionLocked);
        Assert.Equal(FighterId.FighterB, payload.TargetFighterId);
        var commit = Assert.Single(harness.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.ActionCommitted);
        Assert.Equal(commit.EventId, prepared.SourceEventId);
        Assert.Equal(new[] { commit.EventId }, payload.RelatedEventIds);
    }

    private static Wp08TickHarness OneActorHarness(CompiledBattleConfig config)
    {
        var harness = Wp08EngineTestFixture.CreateHarness(config);
        Wp08EngineTestFixture.MakeBusy(harness, FighterId.FighterB);
        return harness;
    }

    private static Wp08TickHarness RunTargetCase(Wp08ActionSpec action)
    {
        var harness = OneActorHarness(Wp08EngineTestFixture.CreateConfig(new[] { action }));
        Wp08EngineTestFixture.ForceCombat(
            harness,
            FighterId.FighterA,
            Wp08EngineTestFixture.BearPrimaryId);
        harness.Start();
        _ = harness.RunTick();
        return harness;
    }

    private static Wp08ActionSpec OpponentAction(string actionId) => new(actionId);

    private static Wp08ActionSpec SelfAction(string actionId) => new(
        actionId,
        HitCount: 0,
        HitSchedule: string.Empty);

    private static void AssertResource(
        CombatEventDraft draft,
        int before,
        int delta,
        int after,
        EventId source,
        StableId? resourceId)
    {
        var payload = Assert.IsType<ResourceChangedPayload>(draft.Payload);
        Assert.Equal(before, payload.Before);
        Assert.Equal(delta, payload.Delta);
        Assert.Equal(after, payload.After);
        Assert.Equal(resourceId, payload.ResourceId);
        Assert.Equal(source, draft.SourceEventId);
        Assert.Equal(new[] { source }, payload.RelatedEventIds);
        var beforeFrame = Assert.IsType<FighterFrame>(draft.Before.Actor);
        var afterFrame = Assert.IsType<FighterFrame>(draft.After.Actor);
        Assert.Equal(
            before,
            resourceId.HasValue ? beforeFrame.UniqueResource.Value : beforeFrame.Energy);
        Assert.Equal(
            after,
            resourceId.HasValue ? afterFrame.UniqueResource.Value : afterFrame.Energy);
    }
}
