using System.Text;
using System.Text.Json;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Replay.Journal;

namespace Battle.ConformanceTests.Replay;

public sealed class CanonicalReplayJournalTests
{
    private static readonly Sha256Digest ConfigHash = new(
        "sha256:2222222222222222222222222222222222222222222222222222222222222222");

    private static readonly ExternalId BattleId = new("battle-journal-test-0001");

    private static readonly ExternalId ReplayId = new("replay-journal-test-0001");

    public static TheoryData<CombatEventPayload, string, int> RepresentativePayloadMappings => new()
    {
        {
            new BlockedPayload(
                new[] { EventId.FromSequence(0) },
                new ExternalId("impact-0001"),
                new StableId("guard"),
                321,
                700,
                false,
                Array.Empty<ExternalId>()),
            "chance_fp",
            321
        },
        {
            new AttackHitPayload(
                new[] { EventId.FromSequence(0) },
                new ExternalId("impact-0001"),
                new ExternalId("hit-group-0001"),
                4,
                7,
                15,
                MovementDirection.Right,
                Array.Empty<StableId>()),
            "hit_range_min",
            7
        },
        {
            new DamageAppliedPayload(
                new[] { EventId.FromSequence(0) },
                new ExternalId("impact-0001"),
                new ExternalId("damage-0001"),
                new DamageBreakdown(20, 20, 18, 18, 18, 1, 100, 0),
                900,
                882,
                Array.Empty<StableId>(),
                false),
            "hp_before",
            900
        },
        {
            new StateChangedPayload(
                new[] { EventId.FromSequence(0) },
                FighterState.DecisionReady,
                FighterState.Stunned,
                3,
                850,
                1000,
                ImmunityResult.Allowed),
            "control_ratio_fp",
            850
        },
        {
            new GrabStartedPayload(
                new[] { EventId.FromSequence(0) },
                new ExternalId("grab-0001"),
                FighterId.FighterA,
                FighterId.FighterB,
                12,
                GrabPriorityResult.Uncontested),
            "hold_max_ticks",
            12
        },
    };

    [Fact]
    public void HappyPath_FreezesCanonicalEventsAndCompletesDigestChain()
    {
        var journal = CreateStartedJournal();
        var summary = CreateSummary(eventCount: 2, endTick: 3);
        var endIdentity = journal.Append(
            CreateEndedDraft(sequence: 1, tick: 3, summary, EventId.FromSequence(0)));

        var completion = journal.Complete(in summary);

        Assert.Equal(EventId.FromSequence(1), endIdentity.EventId);
        Assert.Equal(1, endIdentity.Sequence);
        Assert.True(journal.IsCompleted);
        Assert.Equal(journal.FinalDigest, completion.FinalDigest);
        Assert.Equal(ReplayId, completion.PublishedReplayId);
        Assert.Equal(journal.Events[1].EventDigest, journal.FinalDigest);
        Assert.Equal(journal.InputDigest, journal.Events[0].PreviousDigest);
        Assert.Equal(journal.Events[0].EventDigest, journal.Events[1].PreviousDigest);
        Assert.Equal(EventId.FromSequence(0), journal.Events[0].Identity.EventId);
        Assert.Equal(EventId.FromSequence(1), journal.Events[1].Identity.EventId);

        var firstBytes = journal.Events[0].CanonicalJson.ToArray();
        using (var first = JsonDocument.Parse(firstBytes))
        {
            Assert.Equal(0, first.RootElement.GetProperty("sequence").GetInt64());
            Assert.Equal("evt-0000000000", first.RootElement.GetProperty("event_id").GetString());
            Assert.Equal(
                journal.InputDigest!.Value.Value,
                first.RootElement.GetProperty("integrity").GetProperty("prev_digest").GetString());
            Assert.Equal(
                journal.Events[0].EventDigest.Value,
                first.RootElement.GetProperty("integrity").GetProperty("event_digest").GetString());
            var resource = first.RootElement
                .GetProperty("payload")
                .GetProperty("initial_frames")[0]
                .GetProperty("unique_resource");
            Assert.Equal(100, resource.GetProperty("max").GetInt32());
            Assert.False(resource.TryGetProperty("maximum", out _));
        }

        using (var ended = JsonDocument.Parse(journal.Events[1].CanonicalJson))
        {
            var payload = ended.RootElement.GetProperty("payload");
            Assert.Equal("FighterAWin", payload.GetProperty("outcome").GetString());
            Assert.False(payload.TryGetProperty("summary", out _));
            Assert.False(payload.TryGetProperty("event_count", out _));
        }

        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(firstBytes));
        Assert.DoesNotContain('\n', Encoding.UTF8.GetString(firstBytes));

        firstBytes[0] = (byte)'[';
        using var unchanged = JsonDocument.Parse(journal.Events[0].CanonicalJson);
        Assert.Equal(JsonValueKind.Object, unchanged.RootElement.ValueKind);
    }

    [Fact]
    public void Append_RejectsWrongFirstEvent()
    {
        var journal = CreateBegunCanonicalJournal();
        var summary = CreateSummary(eventCount: 2, endTick: 0);

        Assert.Throws<InvalidOperationException>(
            () => journal.Append(CreateEndedDraft(0, 0, summary, null)));
    }

    [Fact]
    public void Append_RejectsSequenceGap()
    {
        var journal = CreateStartedJournal();
        var summary = CreateSummary(eventCount: 3, endTick: 1);

        Assert.Throws<InvalidOperationException>(
            () => journal.Append(
                CreateEndedDraft(2, 1, summary, EventId.FromSequence(0))));
    }

    [Fact]
    public void Draft_RejectsMismatchedEventIdBeforeAppend()
    {
        Assert.Throws<ArgumentException>(
            () => CreateDraft(
                sequence: 1,
                tick: 1,
                payload: CreateMoveStartedPayload(),
                actorId: FighterId.FighterA,
                targetId: null,
                eventId: EventId.FromSequence(2)));
    }

    [Fact]
    public void Append_RejectsDecreasingTick()
    {
        var journal = CreateStartedJournal();
        journal.Append(
            CreateDraft(
                1,
                2,
                CreateMoveStartedPayload(),
                FighterId.FighterA,
                null,
                sourceEventId: EventId.FromSequence(0)));
        var summary = CreateSummary(eventCount: 3, endTick: 1);

        Assert.Throws<InvalidOperationException>(
            () => journal.Append(
                CreateEndedDraft(2, 1, summary, EventId.FromSequence(1))));
    }

    [Fact]
    public void Append_RejectsChangedBattleIdentity()
    {
        var journal = CreateStartedJournal();
        var draft = CreateDraft(
            1,
            1,
            CreateMoveStartedPayload(),
            FighterId.FighterA,
            null,
            sourceEventId: EventId.FromSequence(0),
            battleId: new ExternalId("battle-other"));

        Assert.Throws<InvalidOperationException>(() => journal.Append(in draft));
    }

    [Fact]
    public void Append_RejectsForwardSourceAndRelatedReferences()
    {
        var sourceJournal = CreateStartedJournal();
        var forwardSource = CreateDraft(
            1,
            1,
            CreateMoveStartedPayload(),
            FighterId.FighterA,
            null,
            sourceEventId: EventId.FromSequence(2));
        Assert.Throws<InvalidOperationException>(() => sourceJournal.Append(in forwardSource));

        var relatedJournal = CreateStartedJournal();
        var forwardRelated = CreateDraft(
            1,
            1,
            CreateMoveStartedPayload(EventId.FromSequence(2)),
            FighterId.FighterA,
            null,
            sourceEventId: EventId.FromSequence(0));
        Assert.Throws<InvalidOperationException>(() => relatedJournal.Append(in forwardRelated));
    }

    [Fact]
    public void Append_RejectsRoleAndFrameMismatch()
    {
        var missingActorJournal = CreateStartedJournal();
        var missingActor = CreateDraft(
            1,
            1,
            CreateMoveStartedPayload(),
            null,
            null,
            sourceEventId: EventId.FromSequence(0));
        Assert.Throws<InvalidOperationException>(() => missingActorJournal.Append(in missingActor));

        var wrongFrameJournal = CreateStartedJournal();
        var wrongFrame = CreateDraft(
            1,
            1,
            CreateMoveStartedPayload(),
            FighterId.FighterA,
            null,
            sourceEventId: EventId.FromSequence(0),
            before: new FramePair(CreateFrame(FighterId.FighterB), null),
            after: new FramePair(CreateFrame(FighterId.FighterB), null));
        Assert.Throws<InvalidOperationException>(() => wrongFrameJournal.Append(in wrongFrame));
    }

    [Fact]
    public void Journal_RejectsAppendAfterBattleEndedOrCompletion()
    {
        var endedJournal = CreateStartedJournal();
        var summary = CreateSummary(eventCount: 2, endTick: 1);
        endedJournal.Append(CreateEndedDraft(1, 1, summary, EventId.FromSequence(0)));
        var afterEnd = CreateDraft(
            2,
            2,
            CreateMoveStartedPayload(),
            FighterId.FighterA,
            null,
            sourceEventId: EventId.FromSequence(1));

        Assert.Throws<InvalidOperationException>(() => endedJournal.Append(in afterEnd));

        endedJournal.Complete(in summary);
        Assert.Throws<InvalidOperationException>(() => endedJournal.Append(in afterEnd));
        Assert.Throws<InvalidOperationException>(() => endedJournal.Complete(in summary));
    }

    [Fact]
    public void Complete_RejectsSummaryDifferentFromBattleEndedPayload()
    {
        var journal = CreateStartedJournal();
        var endedSummary = CreateSummary(eventCount: 2, endTick: 1);
        journal.Append(CreateEndedDraft(1, 1, endedSummary, EventId.FromSequence(0)));
        var differentSummary = CreateSummary(
            eventCount: 2,
            endTick: 1,
            outcome: BattleOutcome.Draw,
            endReason: BattleEndReason.TimeoutEqualHealthFraction);

        Assert.Throws<InvalidOperationException>(() => journal.Complete(in differentSummary));
        Assert.False(journal.IsCompleted);
    }

    [Fact]
    public void Complete_RejectsUnresolvedFinisherPrediction()
    {
        var journal = CreateStartedJournal();
        var groupId = new ExternalId("resolution-0001");
        var finisher = new FinisherTriggeredPayload(
            new[] { EventId.FromSequence(0) },
            EventId.FromSequence(2),
            FinisherMarkerKind.PredictedLethalImpact);
        journal.Append(
            CreateDraft(
                1,
                1,
                finisher,
                FighterId.FighterA,
                FighterId.FighterB,
                sourceEventId: EventId.FromSequence(0),
                resolutionGroupId: groupId));

        var summary = CreateSummary(eventCount: 3, endTick: 1);
        journal.Append(
            CreateEndedDraft(
                2,
                1,
                summary,
                EventId.FromSequence(1),
                resolutionGroupId: groupId));

        Assert.Throws<InvalidOperationException>(() => journal.Complete(in summary));
        Assert.False(journal.IsCompleted);
    }

    [Theory]
    [MemberData(nameof(RepresentativePayloadMappings))]
    public void Append_UsesNormativeWireNamesForRepresentativePayloads(
        CombatEventPayload payload,
        string wireName,
        int expectedValue)
    {
        var journal = CreateStartedJournal();
        var actorOnly = payload.EventType == CombatEventType.StateChanged;
        var actorId = FighterId.FighterA;
        var targetId = actorOnly ? (FighterId?)null : FighterId.FighterB;
        var frames = CreateFrames(actorId, targetId);
        var draft = CreateDraft(
            1,
            1,
            payload,
            actorId,
            targetId,
            sourceEventId: EventId.FromSequence(0),
            before: frames,
            after: frames);

        journal.Append(in draft);

        using var document = JsonDocument.Parse(journal.Events[1].CanonicalJson);
        Assert.Equal(
            expectedValue,
            document.RootElement.GetProperty("payload").GetProperty(wireName).GetInt32());
    }

    [Fact]
    public void Append_WritesRngUIntValuesAsCanonicalDecimalStrings()
    {
        var journal = CreateStartedJournal();
        var payload = new DecisionMadePayload(
            new[] { EventId.FromSequence(0) },
            new StableId("sys_wait"),
            new[] { new StableId("sys_wait") },
            1,
            100,
            100,
            DecisionSelectionMode.WeightedRng,
            Array.Empty<ModifierTrace>());
        var rng = new RngProvenance(
            RngStream.Decision,
            4,
            RngOperation.NextInt,
            0,
            100,
            uint.MaxValue,
            42,
            420);
        var frames = CreateFrames(FighterId.FighterA, FighterId.FighterB);
        var draft = CreateDraft(
            1,
            1,
            payload,
            FighterId.FighterA,
            FighterId.FighterB,
            sourceEventId: EventId.FromSequence(0),
            before: frames,
            after: frames,
            rng: rng);

        journal.Append(in draft);

        using var document = JsonDocument.Parse(journal.Events[1].CanonicalJson);
        var rngJson = document.RootElement.GetProperty("rng");
        Assert.Equal("4", rngJson.GetProperty("index").GetString());
        Assert.Equal("4294967295", rngJson.GetProperty("raw_u32").GetString());
        Assert.Equal(100, rngJson.GetProperty("range_max_exclusive").GetInt32());
        Assert.Equal(8, rngJson.EnumerateObject().Count());
    }

    [Fact]
    public void StandardAndDiagnosticJournals_ProduceTheSameCanonicalChain()
    {
        var standard = new CanonicalReplayJournal(ReplayId, JournalProfile.StandardReplay);
        var diagnostic = new CanonicalReplayJournal(ReplayId, JournalProfile.DiagnosticReplay);
        var start = CreateJournalStart();
        var standardBegin = standard.Begin(in start);
        var diagnosticBegin = diagnostic.Begin(in start);
        var started = CreateStartedDraft(standardBegin.InputDigest);
        var summary = CreateSummary(eventCount: 2, endTick: 1);
        var ended = CreateEndedDraft(1, 1, summary, EventId.FromSequence(0));

        standard.Append(in started);
        diagnostic.Append(in started);
        standard.Append(in ended);
        diagnostic.Append(in ended);
        standard.Complete(in summary);
        diagnostic.Complete(in summary);

        Assert.Equal(JournalProfile.StandardReplay, standard.Profile);
        Assert.Equal(JournalProfile.DiagnosticReplay, diagnostic.Profile);
        Assert.Equal(standardBegin, diagnosticBegin);
        Assert.Equal(standard.FinalDigest, diagnostic.FinalDigest);
        Assert.Equal(
            standard.Events.Select(item => item.EventDigest),
            diagnostic.Events.Select(item => item.EventDigest));
    }

    [Fact]
    public void AllJournalProfiles_ProduceTheSameStreamingIntegrityChain()
    {
        ICombatEventJournal[] journals =
        {
            new CanonicalReplayJournal(ReplayId, JournalProfile.StandardReplay),
            new CanonicalReplayJournal(ReplayId, JournalProfile.DiagnosticReplay),
            new SummaryOnlyEventJournal(ReplayId),
            new FailureCaptureEventJournal(ReplayId, capacity: 4),
        };
        var start = CreateJournalStart();
        var beginReceipts = new JournalBeginResult[journals.Length];
        for (var index = 0; index < journals.Length; index++)
        {
            beginReceipts[index] = journals[index].Begin(in start);
        }
        var started = CreateStartedDraft(beginReceipts[0].InputDigest);
        var summary = CreateSummary(eventCount: 2, endTick: 1);
        var ended = CreateEndedDraft(1, 1, summary, EventId.FromSequence(0));

        foreach (var journal in journals)
        {
            journal.Append(in started);
            journal.Append(in ended);
        }

        var completions = new JournalCompletion[journals.Length];
        for (var index = 0; index < journals.Length; index++)
        {
            completions[index] = journals[index].Complete(in summary);
        }

        Assert.All(beginReceipts, receipt => Assert.Equal(beginReceipts[0], receipt));
        Assert.All(
            completions,
            completion => Assert.Equal(completions[0].FinalDigest, completion.FinalDigest));
        Assert.Equal(ReplayId, completions[0].PublishedReplayId);
        Assert.Equal(ReplayId, completions[1].PublishedReplayId);
        Assert.Null(completions[2].PublishedReplayId);
        Assert.Null(completions[3].PublishedReplayId);
    }

    [Fact]
    public void JournalLifecycle_RequiresExactlyOneBeginBeforeAppend()
    {
        var journal = new CanonicalReplayJournal(ReplayId);
        var start = CreateJournalStart();
        var unpreparedStarted = CreateStartedDraft(ConfigHash);

        Assert.Throws<InvalidOperationException>(() => journal.Append(in unpreparedStarted));

        var begin = journal.Begin(in start);
        Assert.Throws<InvalidOperationException>(() => journal.Begin(in start));

        var started = CreateStartedDraft(begin.InputDigest);
        journal.Append(in started);
        Assert.Throws<InvalidOperationException>(() => journal.Begin(in start));
    }

    [Fact]
    public void SummaryOnly_CountsDraftsAndRngWithoutPublishingReplay()
    {
        var journal = new SummaryOnlyEventJournal(ReplayId);
        var start = CreateJournalStart();
        var begin = journal.Begin(in start);
        var started = CreateStartedDraft(begin.InputDigest);
        journal.Append(in started);

        var frames = CreateFrames(FighterId.FighterA, FighterId.FighterB);
        var decision = CreateDraft(
            1,
            1,
            new DecisionMadePayload(
                new[] { EventId.FromSequence(0) },
                new StableId("sys_wait"),
                new[] { new StableId("sys_wait") },
                1,
                1,
                1,
                DecisionSelectionMode.WeightedRng,
                Array.Empty<ModifierTrace>()),
            FighterId.FighterA,
            FighterId.FighterB,
            sourceEventId: EventId.FromSequence(0),
            before: frames,
            after: frames,
            rng: new RngProvenance(
                RngStream.Decision,
                0,
                RngOperation.NextInt,
                0,
                1,
                0,
                0,
                0));
        journal.Append(in decision);

        var summary = CreateSummary(eventCount: 3, endTick: 1);
        var ended = CreateEndedDraft(2, 1, summary, EventId.FromSequence(1));
        journal.Append(in ended);
        var completion = journal.Complete(in summary);

        Assert.Equal(JournalProfile.SummaryOnly, journal.Profile);
        Assert.False(journal.PublishesReplay);
        Assert.True(journal.IsCompleted);
        Assert.Equal(3, journal.EventCount);
        Assert.Equal(1, journal.EventTypeCounts[CombatEventType.DecisionMade]);
        Assert.Equal(1, journal.RngDrawCounts[RngStream.Decision]);
        Assert.Same(summary, journal.Summary);
        Assert.Equal(journal.FinalDigest, completion.FinalDigest);
        Assert.Null(completion.PublishedReplayId);
    }

    [Fact]
    public void FailureCapture_KeepsOnlyBoundedDiagnosticTail()
    {
        var journal = new FailureCaptureEventJournal(ReplayId, capacity: 2);
        var start = CreateJournalStart();
        var begin = journal.Begin(in start);
        var started = CreateStartedDraft(begin.InputDigest);
        journal.Append(in started);
        var movement = CreateDraft(
            1,
            1,
            CreateMoveStartedPayload(EventId.FromSequence(0)),
            FighterId.FighterA,
            null,
            sourceEventId: EventId.FromSequence(0));
        journal.Append(in movement);
        var summary = CreateSummary(eventCount: 3, endTick: 2);
        var ended = CreateEndedDraft(2, 2, summary, EventId.FromSequence(1));
        journal.Append(in ended);
        var completion = journal.Complete(in summary);

        Assert.Equal(JournalProfile.FailureCapture, journal.Profile);
        Assert.False(journal.PublishesReplay);
        Assert.Equal(3, journal.EventCount);
        Assert.Equal(
            new[] { EventId.FromSequence(1), EventId.FromSequence(2) },
            journal.CapturedDrafts.Select(draft => draft.EventId));
        Assert.Equal(journal.FinalDigest, completion.FinalDigest);
        Assert.Null(completion.PublishedReplayId);
    }

    private static CanonicalReplayJournal CreateStartedJournal()
    {
        var journal = CreateBegunCanonicalJournal();
        var started = CreateStartedDraft(journal.InputDigest!.Value);
        var identity = journal.Append(in started);
        Assert.Equal(new CombatEventIdentity(EventId.FromSequence(0), 0), identity);
        return journal;
    }

    private static CanonicalReplayJournal CreateBegunCanonicalJournal()
    {
        var journal = new CanonicalReplayJournal(ReplayId);
        var start = CreateJournalStart();
        var begin = journal.Begin(in start);
        Assert.Equal(journal.InputDigest, begin.InputDigest);
        return journal;
    }

    private static CombatEventDraft CreateStartedDraft(Sha256Digest inputDigest)
    {
        var frameA = CreateFrame(FighterId.FighterA);
        var frameB = CreateFrame(FighterId.FighterB);
        return CreateDraft(
            0,
            0,
            new BattleStartedPayload(
                Array.Empty<EventId>(),
                inputDigest,
                new[] { frameA, frameB },
                new[] { FighterId.FighterA, FighterId.FighterB },
                InitiativeTieBreak.StatThenSeededHash),
            null,
            null,
            before: new FramePair(null, null),
            after: new FramePair(null, null));
    }

    private static CombatJournalStart CreateJournalStart() =>
        new(
            BattleId,
            ContractVersions.Engine,
            ContractVersions.Rng,
            ContractVersions.Ordering,
            new ConfigReference(
                ContractVersions.BalanceSchema,
                new ArtifactVersion("v0.1"),
                ConfigHash),
            new BattleInputSnapshot(
                42UL,
                new StableId("mode_open_v01"),
                new ArenaSnapshot(new StableId("combat_lab_arena"), 0, 10_000, 100, 200)),
            new CombatJournalFighterStart(
                CreateBuild(FighterSide.A),
                CreateFrame(FighterId.FighterA)),
            new CombatJournalFighterStart(
                CreateBuild(FighterSide.B),
                CreateFrame(FighterId.FighterB)));

    private static FighterBuildSnapshot CreateBuild(FighterSide side)
    {
        var fighterId = side == FighterSide.A ? FighterId.FighterA : FighterId.FighterB;
        var suffix = side == FighterSide.A ? "a" : "b";
        return new FighterBuildSnapshot(
            fighterId,
            side,
            new StableId("animal_" + suffix),
            new StableId("build_" + suffix),
            new[]
            {
                new StableId("special_" + suffix + "_one"),
                new StableId("special_" + suffix + "_two"),
            },
            new StableId("passive_" + suffix),
            new GearSelection(
                new StableId("gear_" + suffix + "_offense"),
                new StableId("gear_" + suffix + "_defense"),
                new StableId("gear_" + suffix + "_utility")),
            new StableId("tactic_" + suffix));
    }

    private static CombatEventDraft CreateEndedDraft(
        long sequence,
        int tick,
        BattleSummary summary,
        EventId? sourceEventId,
        ExternalId? resolutionGroupId = null) =>
        CreateDraft(
            sequence,
            tick,
            new BattleEndedPayload(
                sourceEventId.HasValue ? new[] { sourceEventId.Value } : Array.Empty<EventId>(),
                summary),
            null,
            null,
            sourceEventId: sourceEventId,
            resolutionGroupId: resolutionGroupId,
            before: new FramePair(null, null),
            after: new FramePair(null, null));

    private static CombatEventDraft CreateDraft(
        long sequence,
        int tick,
        CombatEventPayload payload,
        FighterId? actorId,
        FighterId? targetId,
        EventId? sourceEventId = null,
        EventId? eventId = null,
        ExternalId? battleId = null,
        ExternalId? resolutionGroupId = null,
        FramePair? before = null,
        FramePair? after = null,
        RngProvenance? rng = null)
    {
        var frames = CreateFrames(actorId, targetId);
        return new CombatEventDraft(
            ContractVersions.Event,
            ContractVersions.Engine,
            ConfigHash,
            battleId ?? BattleId,
            tick,
            sequence,
            eventId ?? EventId.FromSequence(sequence),
            sourceEventId,
            actorId,
            targetId,
            null,
            null,
            null,
            resolutionGroupId,
            Array.Empty<ReasonCode>(),
            rng,
            before ?? frames,
            after ?? frames,
            payload);
    }

    private static FramePair CreateFrames(FighterId? actorId, FighterId? targetId) =>
        new(
            actorId.HasValue ? CreateFrame(actorId.Value) : null,
            targetId.HasValue ? CreateFrame(targetId.Value) : null);

    private static MoveStartedPayload CreateMoveStartedPayload(params EventId[] relatedEventIds) =>
        new(
            relatedEventIds,
            100,
            MovementDirection.Right,
            10,
            MoveStartKind.Approach,
            Array.Empty<ReasonCode>());

    private static FighterFrame CreateFrame(FighterId fighterId) =>
        new(
            fighterId,
            fighterId == FighterId.FighterA ? 100 : 200,
            fighterId == FighterId.FighterA ? Facing.Right : Facing.Left,
            FighterState.DecisionReady,
            null,
            null,
            null,
            100,
            100,
            50,
            100,
            new ResourceFrame(
                new StableId(fighterId == FighterId.FighterA ? "rage" : "tempo"),
                0,
                100),
            0,
            100,
            Array.Empty<EffectFrame>());

    private static BattleSummary CreateSummary(
        long eventCount,
        int endTick,
        BattleOutcome outcome = BattleOutcome.FighterAWin,
        BattleEndReason endReason = BattleEndReason.Defeat)
    {
        var winner = outcome switch
        {
            BattleOutcome.FighterAWin => FighterId.FighterA,
            BattleOutcome.FighterBWin => FighterId.FighterB,
            _ => (FighterId?)null,
        };

        return new BattleSummary(
            outcome,
            winner,
            endReason,
            endTick,
            endTick,
            eventCount,
            Array.Empty<EventId>(),
            new[]
            {
                CreateFrame(FighterId.FighterA),
                CreateFrame(FighterId.FighterB),
            });
    }
}
