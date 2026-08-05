using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Replay.Journal;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Replay;

public sealed class MovementReplaySemanticTests
{
    private static readonly Sha256Digest ConfigHash = new(
        "sha256:7777777777777777777777777777777777777777777777777777777777777777");

    private static readonly ExternalId BattleId = new("battle-movement-replay-0001");

    private static readonly ExternalId ReplayId = new("replay-movement-replay-0001");

    private static readonly StableId ApproachActionId = new("sys_approach");

    private static readonly DecisionId DecisionId = new("dec-fighter_a-000001");

    [Fact]
    public void MovementReplay_PassesSchemaSemanticAndIntegrityVerification()
    {
        var result = ReplayTestFixture.Verify(CreateValidMovementReplay());

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Empty(result.Issues);
        Assert.Equal(9, result.EventCount);
    }

    [Fact]
    public void MovementPayloads_KeepTheExistingExactWireShape()
    {
        using var document = JsonDocument.Parse(CreateValidMovementReplay());
        var events = document.RootElement.GetProperty("events");

        Assert.Equal(
            new[]
            {
                "direction",
                "from_position",
                "movement_kind",
                "related_event_ids",
                "speed_per_tick",
                "stop_conditions",
            },
            events[4].GetProperty("payload").EnumerateObject().Select(item => item.Name));
        Assert.Equal("Approach", events[4].GetProperty("payload").GetProperty("movement_kind").GetString());

        Assert.Equal(
            new[]
            {
                "actual_delta",
                "blocked_by_wall",
                "from_position",
                "movement_kind",
                "related_event_ids",
                "requested_delta",
                "to_position",
            },
            events[5].GetProperty("payload").EnumerateObject().Select(item => item.Name));
        Assert.Equal("Voluntary", events[5].GetProperty("payload").GetProperty("movement_kind").GetString());
        Assert.Equal("Separation", events[6].GetProperty("payload").GetProperty("movement_kind").GetString());

        Assert.Equal(
            new[]
            {
                "from_position",
                "related_event_ids",
                "stop_reason",
                "to_position",
            },
            events[7].GetProperty("payload").EnumerateObject().Select(item => item.Name));
    }

    [Theory]
    [InlineData("delta")]
    [InlineData("frame")]
    [InlineData("wall")]
    [InlineData("source")]
    [InlineData("reason")]
    [InlineData("stop-order")]
    [InlineData("voluntary-action")]
    [InlineData("separation-action")]
    [InlineData("separation-rng")]
    [InlineData("related-extra")]
    [InlineData("related-missing")]
    [InlineData("separation-related-extra")]
    [InlineData("segment-expired-too-early")]
    public void SchemaValidMovementTamper_IsRejectedBySemanticVerification(string mutation)
    {
        var replay = JsonNode.Parse(CreateValidMovementReplay())!.AsObject();
        ApplyMovementMutation(replay, mutation);

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Layer == ReplayVerificationLayer.Semantic &&
                issue.Code == ReplayVerificationCodes.MovementInvalid);
    }

    [Fact]
    public void MovementTarget_IsRejectedByActorOnlyWireRule()
    {
        var replay = JsonNode.Parse(CreateValidMovementReplay())!.AsObject();
        Event(replay, 4)["target_id"] = "fighter_b";

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(
            result.Issues,
            issue => issue.Code == ReplayVerificationCodes.SchemaViolation);
    }

    private static byte[] CreateValidMovementReplay()
    {
        var journal = new CanonicalReplayJournal(ReplayId);
        var initialA = CreateFrame(FighterId.FighterA, 1000);
        var initialB = CreateFrame(FighterId.FighterB, 5000);
        var start = CreateJournalStart(initialA, initialB);
        var begin = journal.Begin(in start);

        Append(
            journal,
            0,
            0,
            new BattleStartedPayload(
                Array.Empty<EventId>(),
                begin.InputDigest,
                new[] { initialA, initialB },
                new[] { FighterId.FighterB, FighterId.FighterA },
                InitiativeTieBreak.StatThenSeededHash),
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ReasonCode>(),
            new FramePair(null, null),
            new FramePair(null, null));

        var decisionFrames = new FramePair(initialA, initialB);
        Append(
            journal,
            1,
            0,
            new DecisionMadePayload(
                new[] { EventId.FromSequence(0) },
                ApproachActionId,
                new[] { ApproachActionId },
                1,
                650,
                650,
                DecisionSelectionMode.OnlyLegalAction,
                Array.Empty<ModifierTrace>()),
            FighterId.FighterA,
            FighterId.FighterB,
            EventId.FromSequence(0),
            ApproachActionId,
            DecisionId,
            new[] { new ReasonCode("OnlyLegalAction") },
            decisionFrames,
            decisionFrames);

        var startupA = CreateFrame(
            FighterId.FighterA,
            1000,
            FighterState.Approach,
            ApproachActionId,
            ActionPhase.Startup,
            1);
        Append(
            journal,
            2,
            0,
            new ActionCommittedPayload(
                new[] { EventId.FromSequence(1) },
                FighterId.FighterB,
                0,
                0,
                1,
                5,
                1,
                0,
                CommitDirection.Right,
                5000),
            FighterId.FighterA,
            FighterId.FighterB,
            EventId.FromSequence(1),
            ApproachActionId,
            DecisionId,
            new[] { new ReasonCode("ActionSelected") },
            decisionFrames,
            new FramePair(startupA, initialB));

        var activeAt1000 = CreateFrame(
            FighterId.FighterA,
            1000,
            FighterState.Approach,
            ApproachActionId,
            ActionPhase.Active,
            5);
        Append(
            journal,
            3,
            1,
            new ActionPhaseChangedPayload(
                new[] { EventId.FromSequence(2) },
                ActionPhase.Startup,
                ActionPhase.Active,
                5),
            FighterId.FighterA,
            null,
            EventId.FromSequence(2),
            ApproachActionId,
            DecisionId,
            new[] { new ReasonCode("StartupCompleted") },
            new FramePair(startupA, null),
            new FramePair(activeAt1000, null));

        Append(
            journal,
            4,
            1,
            new MoveStartedPayload(
                new[] { EventId.FromSequence(3) },
                1000,
                MovementDirection.Right,
                100,
                MoveStartKind.Approach,
                new[]
                {
                    new ReasonCode("WallReached"),
                    new ReasonCode("PreferredRangeReached"),
                    new ReasonCode("SegmentExpired"),
                }),
            FighterId.FighterA,
            null,
            EventId.FromSequence(3),
            ApproachActionId,
            DecisionId,
            new[] { new ReasonCode("MovementStarted") },
            new FramePair(activeAt1000, null),
            new FramePair(activeAt1000, null));

        var activeAt1100 = CreateFrame(
            FighterId.FighterA,
            1100,
            FighterState.Approach,
            ApproachActionId,
            ActionPhase.Active,
            5);
        Append(
            journal,
            5,
            1,
            new PositionChangedPayload(
                new[] { EventId.FromSequence(4) },
                1000,
                1100,
                100,
                100,
                0,
                PositionChangeKind.Voluntary),
            FighterId.FighterA,
            null,
            EventId.FromSequence(4),
            ApproachActionId,
            DecisionId,
            new[] { new ReasonCode("VoluntaryMovement") },
            new FramePair(activeAt1000, null),
            new FramePair(activeAt1100, null));

        var activeAt1050 = CreateFrame(
            FighterId.FighterA,
            1050,
            FighterState.Approach,
            ApproachActionId,
            ActionPhase.Active,
            5);
        Append(
            journal,
            6,
            1,
            new PositionChangedPayload(
                new[] { EventId.FromSequence(5) },
                1100,
                1050,
                -50,
                -50,
                0,
                PositionChangeKind.Separation),
            FighterId.FighterA,
            null,
            EventId.FromSequence(5),
            null,
            null,
            new[] { new ReasonCode("SeparationCorrection") },
            new FramePair(activeAt1100, null),
            new FramePair(activeAt1050, null));

        Append(
            journal,
            7,
            1,
            new MoveEndedPayload(
                new[] { EventId.FromSequence(6) },
                1000,
                1050,
                new ReasonCode("PreferredRangeReached")),
            FighterId.FighterA,
            null,
            EventId.FromSequence(6),
            ApproachActionId,
            DecisionId,
            new[] { new ReasonCode("PreferredRangeReached") },
            new FramePair(activeAt1050, null),
            new FramePair(activeAt1050, null));

        var summary = new BattleSummary(
            BattleOutcome.FighterAWin,
            FighterId.FighterA,
            BattleEndReason.Defeat,
            2,
            2,
            9,
            Array.Empty<EventId>(),
            new[] { activeAt1050, initialB });
        Append(
            journal,
            8,
            2,
            new BattleEndedPayload(new[] { EventId.FromSequence(7) }, summary),
            null,
            null,
            EventId.FromSequence(7),
            null,
            null,
            Array.Empty<ReasonCode>(),
            new FramePair(null, null),
            new FramePair(null, null));

        journal.Complete(in summary);
        return CanonicalReplayArtifactWriter.Write(
            journal,
            new ReplayArtifactMetadata(
                new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero),
                new ExternalId("movement-semantic-tests"),
                true,
                null));
    }

    private static void Append(
        CanonicalReplayJournal journal,
        long sequence,
        int tick,
        CombatEventPayload payload,
        FighterId? actorId,
        FighterId? targetId,
        EventId? sourceEventId,
        StableId? actionId,
        DecisionId? decisionId,
        IEnumerable<ReasonCode> reasonCodes,
        FramePair before,
        FramePair after)
    {
        var draft = new CombatEventDraft(
            ContractVersions.Event,
            ContractVersions.Engine,
            ConfigHash,
            BattleId,
            tick,
            sequence,
            EventId.FromSequence(sequence),
            sourceEventId,
            actorId,
            targetId,
            actionId,
            null,
            decisionId,
            null,
            reasonCodes,
            null,
            before,
            after,
            payload);
        journal.Append(in draft);
    }

    private static CombatJournalStart CreateJournalStart(
        FighterFrame initialA,
        FighterFrame initialB) =>
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
                7UL,
                new StableId("movement_semantic_v01"),
                new ArenaSnapshot(new StableId("combat_lab_arena"), 0, 10_000, 1000, 5000)),
            new CombatJournalFighterStart(CreateBuild(FighterSide.A), initialA),
            new CombatJournalFighterStart(CreateBuild(FighterSide.B), initialB));

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

    private static FighterFrame CreateFrame(
        FighterId fighterId,
        int position,
        FighterState state = FighterState.DecisionReady,
        StableId? actionId = null,
        ActionPhase? actionPhase = null,
        int? stateTicksRemaining = null) =>
        new(
            fighterId,
            position,
            fighterId == FighterId.FighterA ? Facing.Right : Facing.Left,
            state,
            stateTicksRemaining,
            actionId,
            actionPhase,
            100,
            100,
            100,
            100,
            new ResourceFrame(
                new StableId(fighterId == FighterId.FighterA ? "rage" : "tempo"),
                0,
                100),
            0,
            100,
            Array.Empty<EffectFrame>());

    private static void ApplyMovementMutation(JsonObject replay, string mutation)
    {
        switch (mutation)
        {
            case "delta":
                Event(replay, 5)["payload"]!["actual_delta"] = 99;
                break;

            case "frame":
                Event(replay, 5)["after"]!["actor"]!["position"] = 1099;
                break;

            case "wall":
                Event(replay, 5)["payload"]!["blocked_by_wall"] = 1;
                break;

            case "source":
                Event(replay, 5)["source_event_id"] = "evt-0000000003";
                Event(replay, 5)["payload"]!["related_event_ids"] =
                    new JsonArray("evt-0000000003");
                break;

            case "reason":
                Event(replay, 7)["payload"]!["stop_reason"] = "NotDeclared";
                Event(replay, 7)["reason_codes"] = new JsonArray("NotDeclared");
                break;

            case "stop-order":
                Event(replay, 4)["payload"]!["stop_conditions"] =
                    new JsonArray(
                        "PreferredRangeReached",
                        "WallReached",
                        "SegmentExpired");
                break;

            case "voluntary-action":
                Event(replay, 5)["action_id"] = null;
                break;

            case "separation-action":
                Event(replay, 6)["action_id"] = "sys_approach";
                break;

            case "separation-rng":
                Event(replay, 6)["rng"] = new JsonObject
                {
                    ["index"] = "0",
                    ["normalized_fp"] = 0,
                    ["operation"] = "NextInt",
                    ["range_max_exclusive"] = 2,
                    ["range_min_inclusive"] = 0,
                    ["raw_u32"] = "0",
                    ["result"] = 0,
                    ["stream"] = "Resolution",
                };
                break;

            case "related-extra":
                Event(replay, 5)["payload"]!["related_event_ids"] =
                    new JsonArray("evt-0000000003", "evt-0000000004");
                break;

            case "related-missing":
                Event(replay, 7)["payload"]!["related_event_ids"] = new JsonArray();
                break;

            case "separation-related-extra":
                Event(replay, 6)["payload"]!["related_event_ids"] =
                    new JsonArray("evt-0000000004", "evt-0000000005");
                break;

            case "segment-expired-too-early":
                Event(replay, 7)["payload"]!["stop_reason"] = "SegmentExpired";
                Event(replay, 7)["reason_codes"] = new JsonArray("SegmentExpired");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static JsonObject Event(JsonObject replay, int index) =>
        replay["events"]!.AsArray()[index]!.AsObject();

    private static byte[] Serialize(JsonObject replay) =>
        Encoding.UTF8.GetBytes(
            replay.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = false,
                }));
}
