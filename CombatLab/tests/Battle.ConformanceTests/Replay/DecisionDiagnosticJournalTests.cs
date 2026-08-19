using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Replay.Journal;

namespace Battle.ConformanceTests.Replay;

public sealed class DecisionDiagnosticJournalTests
{
    private static readonly Sha256Digest ConfigHash = new(
        "sha256:3333333333333333333333333333333333333333333333333333333333333333");

    private static readonly ExternalId BattleId = new("battle-wp08-diagnostic-test");

    private static readonly ExternalId ReplayId = new("replay-wp08-diagnostic-test");

    private static readonly ReplayArtifactMetadata Metadata = new(
        new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
        new ExternalId("combat-lab-conformance-tests"),
        fixture: false,
        notes: "WP-08 diagnostic parity vector");

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_010_StandardAndDiagnosticArtifactsPreserveCanonicalGameplayParity()
    {
        var standard = CreateStartedJournal(JournalProfile.StandardReplay);
        var diagnostic = CreateStartedJournal(JournalProfile.DiagnosticReplay);
        var snapshotDigest = diagnostic.ComputeSnapshotDigest(CreateSnapshot());
        var decision = CreateDecisionDraft();

        standard.Append(in decision);
        diagnostic.Append(in decision);
        diagnostic.AppendDecisionTrace(CreateTrace(snapshotDigest));

        var committed = CreateCommittedDraft();
        standard.Append(in committed);
        diagnostic.Append(in committed);
        var summary = CreateSummary();
        var ended = CreateEndedDraft(summary);
        standard.Append(in ended);
        diagnostic.Append(in ended);
        standard.Complete(in summary);
        diagnostic.Complete(in summary);

        var standardBytes = CanonicalReplayArtifactWriter.Write(standard, Metadata);
        var diagnosticBytes = CanonicalReplayArtifactWriter.Write(diagnostic, Metadata);
        using var standardDocument = JsonDocument.Parse(standardBytes);
        using var diagnosticDocument = JsonDocument.Parse(diagnosticBytes);
        var standardRoot = standardDocument.RootElement;
        var diagnosticRoot = diagnosticDocument.RootElement;

        Assert.Equal("standard", standardRoot.GetProperty("profile").GetString());
        Assert.Equal(JsonValueKind.Null, standardRoot.GetProperty("diagnostics").ValueKind);
        Assert.Equal("diagnostic", diagnosticRoot.GetProperty("profile").GetString());
        Assert.Single(
            diagnosticRoot.GetProperty("diagnostics").GetProperty("decisions").EnumerateArray());

        Assert.True(JsonElement.DeepEquals(
            standardRoot.GetProperty("input"),
            diagnosticRoot.GetProperty("input")));
        Assert.True(JsonElement.DeepEquals(
            standardRoot.GetProperty("summary"),
            diagnosticRoot.GetProperty("summary")));
        Assert.True(JsonElement.DeepEquals(
            standardRoot.GetProperty("keyframes"),
            diagnosticRoot.GetProperty("keyframes")));
        Assert.Equal(
            standardRoot.GetProperty("events").EnumerateArray().Select(item => item.GetRawText()),
            diagnosticRoot.GetProperty("events").EnumerateArray().Select(item => item.GetRawText()));
        Assert.Equal(
            standardRoot.GetProperty("integrity").GetProperty("input_digest").GetString(),
            diagnosticRoot.GetProperty("integrity").GetProperty("input_digest").GetString());
        Assert.Equal(
            standardRoot.GetProperty("integrity").GetProperty("final_digest").GetString(),
            diagnosticRoot.GetProperty("integrity").GetProperty("final_digest").GetString());
        Assert.Equal(standard.FinalDigest, diagnostic.FinalDigest);

        var standardVerification = ReplayTestFixture.Verify(standardBytes);
        var diagnosticVerification = ReplayTestFixture.Verify(diagnosticBytes);
        Assert.True(standardVerification.IsValid, ReplayTestFixture.Describe(standardVerification));
        Assert.True(diagnosticVerification.IsValid, ReplayTestFixture.Describe(diagnosticVerification));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_010_SnapshotDigestMatchesPinnedDomainSeparatedCanonicalVector()
    {
        var journal = CreateStartedJournal(JournalProfile.DiagnosticReplay);
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        Sha256Digest russianDigest;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            russianDigest = journal.ComputeSnapshotDigest(CreateSnapshot());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        var permutedMode = CreateModeRules(reverseInputs: true);
        var permutedDigest = journal.ComputeSnapshotDigest(CreateSnapshot(permutedMode));

        Assert.Equal(
            "sha256:04e542a2c972370bc6c7ec7d03a5d594b8a4b43e78b4802194511248699b4873",
            russianDigest.Value);
        Assert.Equal(russianDigest, permutedDigest);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_009_DiagnosticJournalRequiresComputedSnapshotAndCompleteOrderedTraceSet()
    {
        var journal = CreateStartedJournal(JournalProfile.DiagnosticReplay);
        var decision = CreateDecisionDraft();
        journal.Append(in decision);

        var unownedDigest = new Sha256Digest(
            "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
        Assert.Throws<InvalidOperationException>(
            () => journal.AppendDecisionTrace(CreateTrace(unownedDigest)));

        var ownedDigest = journal.ComputeSnapshotDigest(CreateSnapshot());
        Assert.Throws<InvalidOperationException>(
            () => journal.AppendDecisionTrace(CreateTrace(ownedDigest, finalWeight: 999)));
        var committed = CreateCommittedDraft();
        journal.Append(in committed);
        var summary = CreateSummary();
        var ended = CreateEndedDraft(summary);
        journal.Append(in ended);

        Assert.Throws<InvalidOperationException>(() => journal.Complete(in summary));
        Assert.False(journal.IsCompleted);

        journal.AppendDecisionTrace(CreateTrace(ownedDigest));
        var completion = journal.Complete(in summary);

        Assert.Equal(journal.FinalDigest, completion.FinalDigest);
        Assert.Single(journal.DecisionTraces);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_008_StandardJournalRejectsDiagnosticSnapshotAndTraceApis()
    {
        var journal = CreateStartedJournal(JournalProfile.StandardReplay);
        var decision = CreateDecisionDraft();
        journal.Append(in decision);

        Assert.False(journal.IsEnabled);
        Assert.Throws<InvalidOperationException>(
            () => journal.ComputeSnapshotDigest(CreateSnapshot()));
        Assert.Throws<InvalidOperationException>(
            () => journal.AppendDecisionTrace(
                CreateTrace(
                    new Sha256Digest(
                        "sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"))));
        Assert.Empty(journal.DecisionTraces);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_010_SnapshotProducerRejectsForeignJournalIdentity()
    {
        var journal = CreateStartedJournal(JournalProfile.DiagnosticReplay);
        var foreign = CreateSnapshot(battleId: new ExternalId("battle-foreign"));

        Assert.Throws<InvalidOperationException>(() => journal.ComputeSnapshotDigest(foreign));
    }

    private static CanonicalReplayJournal CreateStartedJournal(JournalProfile profile)
    {
        var journal = new CanonicalReplayJournal(ReplayId, profile);
        var start = CreateJournalStart();
        var begin = journal.Begin(in start);
        var started = CreateStartedDraft(begin.InputDigest);
        journal.Append(in started);
        return journal;
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
                42,
                new StableId("mode_open_v01"),
                new ArenaSnapshot(new StableId("combat_lab_arena"), 0, 10_000, 100, 200)),
            new CombatJournalFighterStart(
                CreateBuild(FighterSide.A),
                CreateFrame(FighterId.FighterA)),
            new CombatJournalFighterStart(
                CreateBuild(FighterSide.B),
                CreateFrame(FighterId.FighterB)));

    private static CombatEventDraft CreateStartedDraft(Sha256Digest inputDigest) =>
        new(
            ContractVersions.Event,
            ContractVersions.Engine,
            ConfigHash,
            BattleId,
            tick: 0,
            sequence: 0,
            EventId.FromSequence(0),
            sourceEventId: null,
            actorId: null,
            targetId: null,
            actionId: null,
            effectId: null,
            decisionId: null,
            resolutionGroupId: null,
            reasonCodes: new[] { new ReasonCode("Initialization") },
            rng: null,
            before: new FramePair(null, null),
            after: new FramePair(null, null),
            payload: new BattleStartedPayload(
                Array.Empty<EventId>(),
                inputDigest,
                new[] { CreateFrame(FighterId.FighterA), CreateFrame(FighterId.FighterB) },
                new[] { FighterId.FighterA, FighterId.FighterB },
                InitiativeTieBreak.StatThenSeededHash));

    private static CombatEventDraft CreateDecisionDraft()
    {
        var frames = new FramePair(
            CreateFrame(FighterId.FighterA),
            CreateFrame(FighterId.FighterB));
        return new CombatEventDraft(
            ContractVersions.Event,
            ContractVersions.Engine,
            ConfigHash,
            BattleId,
            tick: 1,
            sequence: 1,
            EventId.FromSequence(1),
            EventId.FromSequence(0),
            FighterId.FighterA,
            FighterId.FighterB,
            new StableId("action_a"),
            effectId: null,
            new DecisionId("dec-fighter_a-000001"),
            resolutionGroupId: null,
            reasonCodes: new[] { new ReasonCode("OnlyLegalAction") },
            rng: null,
            before: frames,
            after: frames,
            payload: new DecisionMadePayload(
                new[] { EventId.FromSequence(0) },
                new StableId("action_a"),
                new[] { new StableId("action_a") },
                candidateCount: 1,
                chosenWeight: 1_000,
                weightSum: 1_000,
                DecisionSelectionMode.OnlyLegalAction,
                Array.Empty<ModifierTrace>()));
    }

    private static CombatEventDraft CreateCommittedDraft()
    {
        var before = new FramePair(
            CreateFrame(FighterId.FighterA),
            CreateFrame(FighterId.FighterB));
        var after = new FramePair(
            CreateFrame(
                FighterId.FighterA,
                new StableId("action_a"),
                ActionPhase.Startup,
                FighterState.AttackPrepare,
                stateTicksRemaining: 1),
            CreateFrame(FighterId.FighterB));
        return new CombatEventDraft(
            ContractVersions.Event,
            ContractVersions.Engine,
            ConfigHash,
            BattleId,
            tick: 1,
            sequence: 2,
            EventId.FromSequence(2),
            EventId.FromSequence(1),
            FighterId.FighterA,
            FighterId.FighterB,
            new StableId("action_a"),
            effectId: null,
            new DecisionId("dec-fighter_a-000001"),
            resolutionGroupId: null,
            reasonCodes: new[] { new ReasonCode("ActionSelected") },
            rng: null,
            before: before,
            after: after,
            payload: new ActionCommittedPayload(
                new[] { EventId.FromSequence(1) },
                FighterId.FighterB,
                energyCost: 0,
                resourceCost: 0,
                startupTicks: 1,
                activeTicks: 1,
                recoveryTicks: 1,
                cooldownTicks: 0,
                CommitDirection.Right,
                targetPositionAtCommit: 200));
    }

    private static CombatEventDraft CreateEndedDraft(BattleSummary summary) =>
        new(
            ContractVersions.Event,
            ContractVersions.Engine,
            ConfigHash,
            BattleId,
            tick: 1,
            sequence: 3,
            EventId.FromSequence(3),
            EventId.FromSequence(2),
            actorId: null,
            targetId: null,
            actionId: null,
            effectId: null,
            decisionId: null,
            resolutionGroupId: null,
            reasonCodes: new[] { new ReasonCode("BattleComplete") },
            rng: null,
            before: new FramePair(null, null),
            after: new FramePair(null, null),
            payload: new BattleEndedPayload(new[] { EventId.FromSequence(2) }, summary));

    private static BattleSummary CreateSummary() =>
        new(
            BattleOutcome.Draw,
            winnerFighterId: null,
            BattleEndReason.TimeoutEqualHealthFraction,
            endTick: 1,
            durationTicks: 1,
            eventCount: 4,
            pivotalEventIds: Array.Empty<EventId>(),
            finalFrames: new[]
            {
                CreateFrame(
                    FighterId.FighterA,
                    new StableId("action_a"),
                    ActionPhase.Startup,
                    FighterState.AttackPrepare,
                    stateTicksRemaining: 1),
                CreateFrame(FighterId.FighterB),
            });

    private static DecisionTrace CreateTrace(
        Sha256Digest snapshotDigest,
        int finalWeight = 1_000) =>
        new(
            new DecisionId("dec-fighter_a-000001"),
            tick: 1,
            sequence: 1,
            FighterId.FighterA,
            snapshotDigest,
            new[]
            {
                new DecisionCandidateTrace(
                    new StableId("action_a"),
                    legal: true,
                    firstRejectionCode: null,
                    baseWeight: 1_000,
                    CreateModifiers(),
                    finalWeight),
            });

    private static DecisionBatchSnapshotProjection CreateSnapshot(
        ModeRulesSnapshot? modeRules = null,
        ExternalId? battleId = null) =>
        new(
            battleId ?? BattleId,
            ContractVersions.Engine,
            masterSeed: 42,
            ConfigHash,
            modeRules ?? CreateModeRules(reverseInputs: false),
            tick: 1,
            new[] { FighterId.FighterA, FighterId.FighterB },
            decisionNextIndex: 0,
            new[]
            {
                CreateDecisionFighter(FighterId.FighterA),
                CreateDecisionFighter(FighterId.FighterB),
            });

    private static DecisionFighterSnapshot CreateDecisionFighter(FighterId fighterId)
    {
        var side = fighterId == FighterId.FighterA ? FighterSide.A : FighterSide.B;
        var suffix = fighterId == FighterId.FighterA ? "a" : "b";
        return new DecisionFighterSnapshot(
            CreateFrame(fighterId),
            CreateBuild(side),
            new[] { new DecisionCooldownSnapshot(new StableId("action_" + suffix), 2) },
            lastActionId: null,
            lastActionCategory: null,
            sameActionStreak: 0,
            sameCategoryStreak: 0,
            new[]
            {
                new DecisionOpportunitySnapshot(new StableId("special_" + suffix + "_one"), 250),
                new DecisionOpportunitySnapshot(new StableId("special_" + suffix + "_two"), 500),
            },
            observableActionId: null,
            observableCommitTick: null,
            emergency: fighterId == FighterId.FighterB);
    }

    private static ModeRulesSnapshot CreateModeRules(bool reverseInputs)
    {
        var animals = new[] { new StableId("animal_a"), new StableId("animal_b") };
        var actions = new[]
        {
            new StableId("action_a"),
            new StableId("action_b"),
            new StableId("special_a_one"),
            new StableId("special_a_two"),
            new StableId("special_b_one"),
            new StableId("special_b_two"),
        };
        var passives = new[] { new StableId("passive_a"), new StableId("passive_b") };
        var gear = new[]
        {
            new StableId("gear_a_defense"),
            new StableId("gear_a_offense"),
            new StableId("gear_a_utility"),
            new StableId("gear_b_defense"),
            new StableId("gear_b_offense"),
            new StableId("gear_b_utility"),
        };
        var tactics = new[] { new StableId("tactic_a"), new StableId("tactic_b") };
        if (reverseInputs)
        {
            Array.Reverse(animals);
            Array.Reverse(actions);
            Array.Reverse(passives);
            Array.Reverse(gear);
            Array.Reverse(tactics);
        }

        return new ModeRulesSnapshot(
            new StableId("mode_open_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            animals,
            actions,
            passives,
            gear,
            tactics);
    }

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
        StableId? actionId = null,
        ActionPhase? actionPhase = null,
        FighterState state = FighterState.DecisionReady,
        int? stateTicksRemaining = null) =>
        new(
            fighterId,
            fighterId == FighterId.FighterA ? 100 : 200,
            fighterId == FighterId.FighterA ? Facing.Right : Facing.Left,
            state,
            stateTicksRemaining,
            actionId,
            actionPhase,
            health: 100,
            maxHealth: 100,
            energy: 50,
            maxEnergy: 100,
            new ResourceFrame(new StableId("resource_" + (fighterId == FighterId.FighterA ? "a" : "b")), 0, 100),
            stagger: 0,
            staggerThreshold: 100,
            Array.Empty<EffectFrame>());

    private static IEnumerable<ModifierTrace> CreateModifiers()
    {
        yield return new ModifierTrace(new ReasonCode("Tactic"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Situation"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Synergy"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Counter"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Variety"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Opportunity"), 1_000);
    }
}
