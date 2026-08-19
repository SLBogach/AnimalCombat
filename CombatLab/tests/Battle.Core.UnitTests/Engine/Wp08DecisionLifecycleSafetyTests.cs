using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Safety;

namespace Battle.Core.UnitTests.Engine;

public sealed class Wp08DecisionLifecycleSafetyTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_LIFE_001_GenericCombatLifecyclePreservesExactCausalChainIncludingZeroRecovery()
    {
        var normal = LifecycleHarness();
        var committed = Wp08EngineTestFixture.SeedCommittedCombat(
            normal,
            FighterId.FighterA,
            startupTicks: 1,
            activeTicks: 1,
            recoveryTicks: 1,
            actionId: "fixture_lifecycle_normal");

        _ = normal.RunTick();
        _ = normal.RunTick();
        _ = normal.RunTick();

        var transitions = normal.Journal.Drafts
            .Where(draft => draft.EventType == CombatEventType.ActionPhaseChanged &&
                            draft.ActionId == new StableId("fixture_lifecycle_normal"))
            .ToArray();
        Assert.Equal(3, transitions.Length);
        AssertTransition(
            transitions[0],
            ActionPhase.Startup,
            ActionPhase.Active,
            1,
            "StartupCompleted",
            committed.EventId);
        AssertTransition(
            transitions[1],
            ActionPhase.Active,
            ActionPhase.Recovery,
            1,
            "ActiveCompleted",
            transitions[0].EventId);
        AssertTransition(
            transitions[2],
            ActionPhase.Recovery,
            null,
            0,
            "RecoveryCompleted",
            transitions[1].EventId);
        Assert.Equal(FighterState.DecisionReady, Assert.IsType<FighterFrame>(transitions[2].After.Actor).State);
        Assert.Null(Assert.IsType<FighterFrame>(transitions[2].After.Actor).ActionId);

        var zeroRecovery = LifecycleHarness();
        var zeroCommit = Wp08EngineTestFixture.SeedCommittedCombat(
            zeroRecovery,
            FighterId.FighterA,
            startupTicks: 0,
            activeTicks: 1,
            recoveryTicks: 0,
            actionId: "fixture_lifecycle_zero_recovery");

        _ = zeroRecovery.RunTick();

        var direct = Assert.Single(zeroRecovery.Journal.Drafts, draft =>
            draft.EventType == CombatEventType.ActionPhaseChanged &&
            draft.ActionId == new StableId("fixture_lifecycle_zero_recovery"));
        AssertTransition(
            direct,
            ActionPhase.Active,
            null,
            0,
            "ActiveCompleted",
            zeroCommit.EventId);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_LIFE_002_ActiveCombatBeforeWp09MutatesOnlyItsPhaseTimer()
    {
        var harness = LifecycleHarness();
        _ = Wp08EngineTestFixture.SeedCommittedCombat(
            harness,
            FighterId.FighterA,
            startupTicks: 0,
            activeTicks: 2,
            recoveryTicks: 1,
            actionId: "fixture_active_no_resolution");
        var actorBefore = harness.State.FighterA.ToFrame();
        var targetBefore = harness.State.FighterB.ToFrame();
        var resolutionIndex = harness.State.Rng.Resolution.NextDrawIndex;
        var eventCount = harness.Emitter.EventCount;

        _ = harness.RunTick();

        var actorAfter = harness.State.FighterA.ToFrame();
        var targetAfter = harness.State.FighterB.ToFrame();
        Assert.Equal(actorBefore.Health, actorAfter.Health);
        Assert.Equal(actorBefore.Position, actorAfter.Position);
        Assert.Equal(actorBefore.Stagger, actorAfter.Stagger);
        Assert.Equal(actorBefore.Effects, actorAfter.Effects);
        Assert.Equal(actorBefore.ActionId, actorAfter.ActionId);
        Assert.Equal(ActionPhase.Active, actorAfter.ActionPhase);
        Assert.Equal(1, actorAfter.StateTicksRemaining);
        Assert.Equal(targetBefore.Health, targetAfter.Health);
        Assert.Equal(targetBefore.Position, targetAfter.Position);
        Assert.Equal(targetBefore.Stagger, targetAfter.Stagger);
        Assert.Equal(targetBefore.Effects, targetAfter.Effects);
        Assert.Equal(resolutionIndex, harness.State.Rng.Resolution.NextDrawIndex);
        Assert.Equal(eventCount, harness.Emitter.EventCount);
        Assert.DoesNotContain(harness.Journal.Drafts, draft =>
            draft.EventType is CombatEventType.DamageApplied or
                CombatEventType.StateChanged or
                CombatEventType.EffectAdded or
                CombatEventType.PositionChanged or
                CombatEventType.ConflictResolved);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_LIFE_004_TerminalDuringActionRetainsFrozenFinalFrameAndEmitsNoCleanupAfterEnd()
    {
        var journal = new RecordingJournal();
        var result = new global::Battle.Core.CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            EngineTestFixture.CreateConfig(timeLimit: 1),
            journal);

        Assert.Equal(BattleResultStatus.Completed, result.Status);
        var ended = Assert.IsType<BattleEndedPayload>(journal.Drafts[^1].Payload);
        Assert.Equal(CombatEventType.BattleEnded, journal.Drafts[^1].EventType);
        Assert.Equal(journal.Drafts.Count - 1, journal.Drafts[^1].Sequence);
        Assert.All(result.Summary!.FinalFrames, frame =>
        {
            Assert.NotNull(frame.ActionId);
            Assert.NotNull(frame.ActionPhase);
            Assert.NotNull(frame.StateTicksRemaining);
        });
        Assert.Equal(
            result.Summary.FinalFrames.Select(FrameSignature),
            ended.Summary.FinalFrames.Select(FrameSignature));
        Assert.DoesNotContain(
            journal.Drafts.SkipWhile(draft => draft.EventType != CombatEventType.BattleEnded).Skip(1),
            draft => draft.EventType == CombatEventType.ActionPhaseChanged);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_001_DecisionBatchPreflightIsAtomicOneSlotBelowAndExactAtBoundary()
    {
        var specs = new[]
        {
            SelfNoEventAction(Wp08EngineTestFixture.BearPrimaryId.Value),
            SelfNoEventAction(Wp08EngineTestFixture.BearSecondaryId.Value),
            SelfNoEventAction(Wp08EngineTestFixture.KangarooPrimaryId.Value),
            SelfNoEventAction(Wp08EngineTestFixture.KangarooSecondaryId.Value),
        };
        var config = Wp08EngineTestFixture.CreateConfig(specs);
        var below = Wp08EngineTestFixture.CreateHarness(config, emitterMaximumEvents: 5);
        var initialA = FrameSignature(below.State.FighterA.ToFrame());
        var initialB = FrameSignature(below.State.FighterB.ToFrame());
        below.Start();

        var failure = Assert.Throws<EngineInvariantException>(() => below.RunTick());

        Assert.Equal(EngineFailureCodes.EventCapExceeded, failure.Code);
        Assert.Equal("Decisions", failure.Phase);
        Assert.Equal(1, below.Emitter.EventCount);
        Assert.Equal(0UL, below.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(0, below.State.FighterA.DecisionCount);
        Assert.Equal(0, below.State.FighterB.DecisionCount);
        Assert.Null(below.State.FighterA.ActionId);
        Assert.Null(below.State.FighterB.ActionId);
        Assert.Null(below.State.FighterA.LastCommittedActionId);
        Assert.Null(below.State.FighterB.LastCommittedActionId);
        Assert.Equal(initialA, FrameSignature(below.State.FighterA.ToFrame()));
        Assert.Equal(initialB, FrameSignature(below.State.FighterB.ToFrame()));
        _ = below.EndInvalid(EngineFailureCodes.EventCapExceeded);
        Assert.Equal(2, below.Emitter.EventCount);
        Assert.Equal(CombatEventType.BattleEnded, below.Journal.Drafts[^1].EventType);

        var exact = Wp08EngineTestFixture.CreateHarness(config, emitterMaximumEvents: 6);
        exact.Start();
        _ = exact.RunTick();

        Assert.Equal(5, exact.Emitter.EventCount);
        Assert.Equal(2UL, exact.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(1, exact.State.FighterA.DecisionCount);
        Assert.Equal(1, exact.State.FighterB.DecisionCount);
        Assert.NotNull(exact.State.FighterA.ActionId);
        Assert.NotNull(exact.State.FighterB.ActionId);
        Assert.Equal(
            new[]
            {
                CombatEventType.BattleStarted,
                CombatEventType.DecisionMade,
                CombatEventType.DecisionMade,
                CombatEventType.ActionCommitted,
                CombatEventType.ActionCommitted,
            },
            exact.Journal.Drafts.Select(draft => draft.EventType));
        _ = exact.EndInvalid(EngineFailureCodes.EventCapExceeded);
        Assert.Equal(6, exact.Emitter.EventCount);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_001_EventEmitterPreflightRejectsOversizedAndPostTerminalBatches()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var setup = EngineTestFixture.CreateSetup();
        var emitter = new CombatEventEmitter(request, config, new RecordingJournal(), maximumEvents: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            emitter.PreflightNonterminalBatch(-1));

        var capacityFailure = Assert.Throws<EngineInvariantException>(() =>
            emitter.PreflightNonterminalBatch(4));

        Assert.Equal(EngineFailureCodes.EventCapExceeded, capacityFailure.Code);
        Assert.Equal(TickPhase.Decisions.ToString(), capacityFailure.Phase);
        Assert.Equal(0, emitter.EventCount);

        _ = emitter.Emit(0, new BattleEndedPayload(new BattleSummary(
            BattleOutcome.Draw,
            null,
            BattleEndReason.TimeoutEqualHealthFraction,
            0,
            0,
            2,
            Array.Empty<EventId>(),
            setup.State.FinalFrames())));

        var terminalFailure = Assert.Throws<EngineInvariantException>(() =>
            emitter.PreflightNonterminalBatch(0));

        Assert.Equal(EngineFailureCodes.TerminalMutation, terminalFailure.Code);
        Assert.Equal("EventEmitter", terminalFailure.Phase);
        Assert.Equal(1, emitter.EventCount);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_001_EventEmitterRejectsBothNonterminalAndTerminalCapOverflow()
    {
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var setup = EngineTestFixture.CreateSetup();
        var nonterminalEmitter = new CombatEventEmitter(
            request,
            config,
            new RecordingJournal(),
            maximumEvents: 4);
        var started = new BattleStartedPayload(
            Array.Empty<EventId>(),
            EngineTestFixture.InputDigest,
            setup.State.FinalFrames(),
            setup.InitiativeOrder,
            InitiativeTieBreak.StatThenSeededHash);
        _ = nonterminalEmitter.Emit(0, started);
        _ = nonterminalEmitter.Emit(0, started);
        _ = nonterminalEmitter.Emit(0, started);

        var nonterminalFailure = Assert.Throws<EngineInvariantException>(() =>
            nonterminalEmitter.Emit(0, started));

        Assert.Equal(EngineFailureCodes.EventCapExceeded, nonterminalFailure.Code);
        Assert.Equal("EventEmitter", nonterminalFailure.Phase);
        Assert.Equal(3, nonterminalEmitter.EventCount);

        var terminalEmitter = new CombatEventEmitter(
            request,
            config,
            new RecordingJournal(),
            maximumEvents: 4);
        var sequenceField = typeof(CombatEventEmitter).GetField(
            "_nextSequence",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(sequenceField);
        sequenceField.SetValue(terminalEmitter, 4L);

        var terminalFailure = Assert.Throws<EngineInvariantException>(() =>
            terminalEmitter.Emit(0, new BattleEndedPayload(new BattleSummary(
                BattleOutcome.Draw,
                null,
                BattleEndReason.TimeoutEqualHealthFraction,
                0,
                0,
                4,
                Array.Empty<EventId>(),
                setup.State.FinalFrames()))));

        Assert.Equal(EngineFailureCodes.EventCapExceeded, terminalFailure.Code);
        Assert.Equal("EventEmitter", terminalFailure.Phase);
        Assert.Equal(4, terminalEmitter.EventCount);
        Assert.False(terminalEmitter.IsTerminal);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_002_ZeroProgressWatchdogRetainsThresholdAndResetSemantics()
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

        Assert.Equal(EngineFailureCodes.ZeroProgress, failure.Code);
        Assert.Equal(2, watchdog.Counter);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_003_DecisionArithmeticRiskIsRejectedBeforeJournalBegin()
    {
        var config = Wp08EngineTestFixture.CreateConfig(changeSettings: settings =>
            settings
                .Where(property => property.Name != "global.sim.decision_weight_max")
                .Append(new ConfigProperty(
                    "global.sim.decision_weight_max",
                    ConfigValue.FromInteger(int.MaxValue)))
                .OrderBy(property => property.Name, StringComparer.Ordinal));
        var journal = new RecordingJournal();

        var result = new global::Battle.Core.CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            config,
            journal);

        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Contains(result.RejectionErrors, error =>
            error.Code.Value == "DecisionWeightSumOverflowRisk" &&
            error.Path == "$.mode_rules.allowed_action_ids");
        Assert.Equal(0, journal.BeginCount);
        Assert.Empty(journal.Drafts);
        Assert.Equal(0, journal.CompleteCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_003_ReachableImpactTimingOverflowRiskIsRejectedBeforeJournalBegin()
    {
        var config = Wp08EngineTestFixture.CreateConfig(
            new[]
            {
                new Wp08ActionSpec(
                    Wp08EngineTestFixture.BearPrimaryId.Value,
                    StartupBaseTicks: 2,
                    StartupMinimumTicks: 2,
                    StartupMaximumTicks: 2),
            },
            timeLimit: int.MaxValue);
        var journal = new RecordingJournal();

        var result = new global::Battle.Core.CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            config,
            journal);

        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Contains(result.RejectionErrors, error =>
            error.Code.Value == "DecisionTimingOverflowRisk" &&
            error.Path.EndsWith(".hit_schedule", StringComparison.Ordinal));
        Assert.Equal(0, journal.BeginCount);
        Assert.Empty(journal.Drafts);
        Assert.Equal(0, journal.CompleteCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_003_ThirtyThreeGrabEntriesRejectBeforeJournalBegin()
    {
        var schedule = string.Join(
            "|",
            Enumerable.Range(0, 33).Select(tick => "grab:" + tick));
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            new Wp08ActionSpec(
                Wp08EngineTestFixture.BearPrimaryId.Value,
                ActiveTicks: 33,
                HitCount: 0,
                HitSchedule: schedule),
        });
        var journal = new RecordingJournal();

        var result = new global::Battle.Core.CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            config,
            journal);

        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Contains(result.RejectionErrors, error =>
            error.Code.Value == "InvalidHitSchedule" &&
            error.Path == "/config/actions/bear_earthbreaker/hit_schedule");
        Assert.Equal(0, journal.BeginCount);
        Assert.Empty(journal.Drafts);
        Assert.Equal(0, journal.CompleteCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_003_ThirtyThreeImpactsRejectBeforeJournalBegin()
    {
        var config = Wp08EngineTestFixture.CreateConfig(new[]
        {
            new Wp08ActionSpec(
                Wp08EngineTestFixture.BearPrimaryId.Value,
                ActiveTicks: 33,
                HitCount: 33,
                HitSchedule: string.Join("|", Enumerable.Range(0, 33))),
        });
        var journal = new RecordingJournal();

        var result = new global::Battle.Core.CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(),
            config,
            journal);

        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Contains(result.RejectionErrors, error =>
            error.Code.Value == "InvalidConfigRange" &&
            error.Path == "/config/actions/bear_earthbreaker/hit_count");
        Assert.Equal(0, journal.BeginCount);
        Assert.Empty(journal.Drafts);
        Assert.Equal(0, journal.CompleteCount);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void CombatActionDescriptorRejectsMoreThanThirtyTwoImpactTicksBeforeMutation()
    {
        var harness = Wp08EngineTestFixture.CreateHarness(Wp08EngineTestFixture.CreateConfig());
        var actor = harness.State.FighterA;
        var actorBefore = FrameSignature(actor.ToFrame());
        var decisionIndex = harness.State.Rng.Decision.NextDrawIndex;
        var resolutionIndex = harness.State.Rng.Resolution.NextDrawIndex;

        var failure = Assert.Throws<ArgumentException>(() => Wp08EngineTestFixture.Descriptor(
            actor,
            harness.State.FighterB,
            actor.PeekNextDecisionId(),
            activeTicks: 33,
            relativeImpactTicks: Enumerable.Range(0, 33)));

        Assert.Equal("relativeImpactTicks", failure.ParamName);
        Assert.Equal(actorBefore, FrameSignature(actor.ToFrame()));
        Assert.Equal(0, actor.DecisionCount);
        Assert.Equal(decisionIndex, harness.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(resolutionIndex, harness.State.Rng.Resolution.NextDrawIndex);
        Assert.Empty(harness.Journal.Drafts);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_SAFE_003_CorruptedRuntimeCountersFailTypedAndDoNotPartiallyMutate()
    {
        var harness = Wp08EngineTestFixture.CreateHarness(Wp08EngineTestFixture.CreateConfig());
        var fighter = harness.State.FighterA;
        var repeatedAction = new StableId("fixture_repeated_action");
        fighter.SetDecisionHistoryForTesting(
            int.MaxValue,
            repeatedAction,
            "FixtureCategory",
            int.MaxValue,
            int.MaxValue);

        AssertDecisionOverflow(() => fighter.PeekNextDecisionId());
        AssertDecisionOverflow(() => fighter.RecordCommittedHistory(repeatedAction, "FixtureCategory"));
        Assert.Equal(int.MaxValue, fighter.SameActionStreak);
        Assert.Equal(int.MaxValue, fighter.SameCategoryStreak);
        Assert.Equal(repeatedAction, fighter.LastCommittedActionId);
        Assert.Equal("FixtureCategory", fighter.LastCommittedCategory);

        var chosen = new StableId("a_chosen_special");
        var overflowed = new StableId("b_overflowed_special");
        fighter.SetOpportunityDebtForTesting(chosen, 7);
        fighter.SetOpportunityDebtForTesting(overflowed, int.MaxValue);
        AssertDecisionOverflow(() => fighter.UpdateOpportunityDebts(
            new[] { chosen, overflowed },
            new[] { chosen, overflowed },
            chosen));
        Assert.Equal(7, fighter.OpportunityDebtFor(chosen));
        Assert.Equal(int.MaxValue, fighter.OpportunityDebtFor(overflowed));

        var history = new DecisionRepeatHistory(
            repeatedAction,
            "FixtureCategory",
            int.MaxValue,
            int.MaxValue);
        AssertDecisionOverflow(() => DecisionVariety.AfterCommit(
            history,
            repeatedAction,
            "FixtureCategory"));
        AssertDecisionOverflow(() => DecisionOpportunity.UpdateDebt(
            DecisionActionSlot.Special,
            int.MaxValue,
            fullyLegal: true,
            selected: false));
        AssertDecisionOverflow(() => SystemActionSelector.Select(new[]
        {
            new SystemActionCandidate(SystemActionSelector.WaitId, int.MaxValue),
            new SystemActionCandidate(SystemActionSelector.RetreatId, 1),
        }));

        var descriptor = Wp08EngineTestFixture.Descriptor(
            fighter,
            harness.State.FighterB,
            new DecisionId("dec-fighter_a-000001"),
            startupTicks: 1,
            relativeImpactTicks: new[] { 0 },
            commitTick: int.MaxValue);
        AssertDecisionOverflow(() => descriptor.AbsoluteImpactTicks());
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CMT_008_NoHitCombatActionDoesNotPublishAnObservableTelegraph()
    {
        var harness = Wp08EngineTestFixture.CreateHarness(Wp08EngineTestFixture.CreateConfig());
        var actor = harness.State.FighterA;
        var decisionId = actor.PeekNextDecisionId();
        var descriptor = Wp08EngineTestFixture.Descriptor(
            actor,
            target: null,
            decisionId,
            actionId: "fixture_no_hit_action",
            relativeImpactTicks: Array.Empty<int>());

        actor.CommitCombatAction(descriptor);

        Assert.Null(actor.ObservableActionId);
        Assert.Null(actor.ObservableCommitTick);
    }

    private static Wp08TickHarness LifecycleHarness()
    {
        var harness = Wp08EngineTestFixture.CreateHarness(
            Wp08EngineTestFixture.CreateConfig(timeLimit: 10));
        Wp08EngineTestFixture.MakeBusy(harness, FighterId.FighterB);
        harness.Start();
        return harness;
    }

    private static Wp08ActionSpec SelfNoEventAction(string actionId) => new(
        actionId,
        EnergyCost: 0,
        ResourceCost: 0,
        CooldownTicks: 0,
        HitCount: 0,
        HitSchedule: string.Empty);

    private static void AssertTransition(
        CombatEventDraft draft,
        ActionPhase from,
        ActionPhase? to,
        int ticks,
        string reason,
        EventId source)
    {
        var payload = Assert.IsType<ActionPhaseChangedPayload>(draft.Payload);
        Assert.Equal(from, payload.FromPhase);
        Assert.Equal(to, payload.ToPhase);
        Assert.Equal(ticks, payload.PhaseTicks);
        Assert.Equal(source, draft.SourceEventId);
        Assert.Equal(new[] { source }, payload.RelatedEventIds);
        Assert.Equal(reason, Assert.Single(draft.ReasonCodes).Value);
        Assert.Null(draft.TargetId);
        Assert.Null(draft.Before.Target);
        Assert.Null(draft.After.Target);
        Assert.Null(draft.Rng);
        Assert.Null(draft.ResolutionGroupId);
    }

    private static void AssertDecisionOverflow(Action action)
    {
        var failure = Assert.Throws<EngineInvariantException>(action);
        Assert.Equal(EngineFailureCodes.DecisionArithmeticOverflow, failure.Code);
        Assert.Equal(TickPhase.Decisions.ToString(), failure.Phase);
    }

    private static object FrameSignature(FighterFrame frame) => new
    {
        frame.FighterId,
        frame.Position,
        frame.Facing,
        frame.State,
        frame.StateTicksRemaining,
        frame.ActionId,
        frame.ActionPhase,
        frame.Health,
        frame.MaxHealth,
        frame.Energy,
        frame.MaxEnergy,
        frame.UniqueResource,
        frame.Stagger,
        frame.StaggerThreshold,
        Effects = frame.Effects.ToArray(),
    };
}
