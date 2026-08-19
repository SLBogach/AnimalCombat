using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Replay.Integrity;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Replay;

public sealed class DecisionReplaySemanticTests
{
    private const string Engine030 = "battle.core/0.3.0";
    private const string SnapshotDigest =
        "sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string ZeroDigest = SnapshotDigest;

    [Theory]
    [InlineData("WeightedRng")]
    [InlineData("OnlyLegalAction")]
    [InlineData("ZeroWeightFallback")]
    [InlineData("HardOpportunity")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_001_AllSelectionModesAreSemanticallyValid(string selectionMode)
    {
        var replay = CreateValidStandard030(selectionMode);

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("chosen")]
    [InlineData("count-list")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_002_CandidateCountListAndChosenTamperIsRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var decision = Decision(replay, "fighter_a");
        var payload = decision["payload"]!;
        switch (mutation)
        {
            case "count":
                payload["candidate_count"] = 3;
                break;
            case "chosen":
                payload["chosen_action_id"] = "sys_retreat";
                decision["action_id"] = "sys_retreat";
                break;
            case "count-list":
                payload["legal_action_ids"] = new JsonArray("bear_paw_jab");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionCandidateInvalid);
    }

    [Theory]
    [InlineData("weighted-null-rng")]
    [InlineData("only-multiple")]
    [InlineData("zero-positive")]
    [InlineData("hard-rng")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_003_ModeWeightAndRngNullabilityTamperIsRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var decisionA = Decision(replay, "fighter_a");
        var decisionB = Decision(replay, "fighter_b");
        switch (mutation)
        {
            case "weighted-null-rng":
                decisionA["rng"] = null;
                break;
            case "only-multiple":
                decisionB["payload"]!["legal_action_ids"] =
                    new JsonArray("bear_paw_jab", "sys_wait");
                decisionB["payload"]!["candidate_count"] = 2;
                break;
            case "zero-positive":
                SetDecisionBMode(replay, "ZeroWeightFallback");
                decisionB["payload"]!["weight_sum"] = 1;
                break;
            case "hard-rng":
                SetDecisionBMode(replay, "HardOpportunity");
                decisionB["rng"] = decisionA["rng"]!.DeepClone();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionModeInvalid);
    }

    [Theory]
    [InlineData("raw")]
    [InlineData("result")]
    [InlineData("normalized")]
    [InlineData("range")]
    [InlineData("index")]
    [InlineData("stream")]
    [InlineData("operation")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_004_WeightedRngProvenanceTamperIsRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var rng = Decision(replay, "fighter_a")["rng"]!;
        switch (mutation)
        {
            case "raw":
                rng["raw_u32"] = "701";
                break;
            case "result":
                rng["result"] = 701;
                break;
            case "normalized":
                rng["normalized_fp"] = 609;
                break;
            case "range":
                rng["range_max_exclusive"] = 1_151;
                break;
            case "index":
                rng["index"] = "1";
                break;
            case "stream":
                rng["stream"] = "Resolution";
                break;
            case "operation":
                rng["operation"] = "TieBreak";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionRngInvalid);
    }

    [Theory]
    [InlineData("legal-ids")]
    [InlineData("modifiers")]
    [InlineData("reasons")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_005_UnsortedDecisionExplainabilityIsRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var decision = Decision(replay, "fighter_a");
        switch (mutation)
        {
            case "legal-ids":
                decision["payload"]!["legal_action_ids"] =
                    new JsonArray("sys_wait", "bear_paw_jab");
                break;
            case "modifiers":
                var modifiers = decision["payload"]!["dominant_modifiers"]!.AsArray();
                var firstModifier = modifiers[0]!.DeepClone();
                var secondModifier = modifiers[1]!.DeepClone();
                modifiers[0] = secondModifier;
                modifiers[1] = firstModifier;
                break;
            case "reasons":
                decision["reason_codes"] =
                    new JsonArray("WeightedRng", "Situation", "Tactic");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionOrderingInvalid);
    }

    [Theory]
    [InlineData("commit-target")]
    [InlineData("commit-position")]
    [InlineData("commit-frame")]
    [InlineData("telegraph-target")]
    [InlineData("telegraph-tick")]
    [InlineData("impact-before-startup")]
    [InlineData("impact-after-active")]
    [InlineData("telegraph-frame")]
    [InlineData("commit-target-frame-chain")]
    [InlineData("commit-phase-timer")]
    [InlineData("commit-direction")]
    [InlineData("system-cooldown")]
    [InlineData("attack-frame-chain")]
    [InlineData("startup-outside-int32")]
    [InlineData("tick-startup-overflow")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_006_CommitAndTelegraphContradictionsAreRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var commit = Event(replay, "ActionCommitted", "fighter_a");
        var telegraph = Event(replay, "AttackPrepared", "fighter_a");
        switch (mutation)
        {
            case "commit-target":
                commit["payload"]!["target_fighter_id"] = "fighter_a";
                break;
            case "commit-position":
                commit["payload"]!["target_position_at_commit"] = 3_001;
                break;
            case "commit-frame":
                commit["after"]!["actor"]!["action_id"] = "sys_wait";
                break;
            case "telegraph-target":
                telegraph["payload"]!["target_fighter_id"] = "fighter_a";
                break;
            case "telegraph-tick":
                telegraph["payload"]!["telegraph_tick"] = 1;
                break;
            case "impact-before-startup":
                telegraph["payload"]!["impact_ticks"] = new JsonArray(2);
                break;
            case "impact-after-active":
                telegraph["payload"]!["impact_ticks"] = new JsonArray(4);
                break;
            case "telegraph-frame":
                var position = telegraph["after"]!["actor"]!["position"]!.GetValue<int>();
                telegraph["after"]!["actor"]!["position"] = position + 1;
                break;
            case "commit-target-frame-chain":
                foreach (var side in new[] { "before", "after" })
                {
                    var health = commit[side]!["target"]!["health"]!.GetValue<int>();
                    commit[side]!["target"]!["health"] = health - 1;
                }

                break;
            case "commit-phase-timer":
                commit["after"]!["actor"]!["state_ticks_remaining"] = 2;
                break;
            case "commit-direction":
                commit["payload"]!["commit_direction"] = "None";
                break;
            case "system-cooldown":
                Event(replay, "ActionCommitted", "fighter_b")["payload"]!["cooldown_ticks"] = 1;
                break;
            case "attack-frame-chain":
                foreach (var side in new[] { "before", "after" })
                {
                    var health = telegraph[side]!["actor"]!["health"]!.GetValue<int>();
                    telegraph[side]!["actor"]!["health"] = health - 1;
                }

                break;
            case "startup-outside-int32":
                commit["payload"]!["startup_ticks"] = 2_147_483_648L;
                break;
            case "tick-startup-overflow":
                Decision(replay, "fighter_a")["tick"] = int.MaxValue;
                commit["tick"] = int.MaxValue;
                commit["payload"]!["startup_ticks"] = 1;
                telegraph["tick"] = int.MaxValue;
                telegraph["payload"]!["telegraph_tick"] = int.MaxValue;
                telegraph["payload"]!["impact_ticks"] = new JsonArray(int.MaxValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionCommitInvalid);
    }

    [Theory]
    [InlineData("chain")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    [InlineData("resource-id")]
    [InlineData("side-effect")]
    [InlineData("reason")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_006_CostRelationsAreExactAndSchemaValidTamperIsRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var cost = ReplaceAttackWithEnergyCost(replay);
        var baseline = ReplayTestFixture.Verify(Serialize(replay));
        Assert.True(baseline.IsValid, ReplayTestFixture.Describe(baseline));

        switch (mutation)
        {
            case "chain":
                cost["payload"]!["before"] = 999;
                cost["payload"]!["after"] = 989;
                cost["before"]!["actor"]!["energy"] = 999;
                cost["after"]!["actor"]!["energy"] = 989;
                break;
            case "minimum":
                cost["payload"]!["minimum"] = 1;
                break;
            case "maximum":
                cost["payload"]!["maximum"] = 999;
                break;
            case "resource-id":
                cost["payload"]!["resource_id"] = "rage";
                break;
            case "side-effect":
                cost["after"]!["actor"]!["health"] = 1_649;
                break;
            case "reason":
                cost["reason_codes"] = new JsonArray("ActionSelected");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionCommitInvalid);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("reason")]
    [InlineData("tick")]
    [InlineData("before-timer")]
    [InlineData("after-phase")]
    [InlineData("missing-lifecycle")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_006_CombatLifecycleTimingAndSourceChainAreExact(string mutation)
    {
        var replay = CreateValidStandard030();
        var lifecycle = Event(replay, "ActionPhaseChanged", "fighter_a");
        switch (mutation)
        {
            case "source":
                var attackId = Event(replay, "AttackPrepared", "fighter_a")["event_id"]!
                    .GetValue<string>();
                lifecycle["source_event_id"] = attackId;
                lifecycle["payload"]!["related_event_ids"] = new JsonArray(attackId);
                break;
            case "reason":
                lifecycle["reason_codes"] = new JsonArray("ActiveCompleted");
                break;
            case "tick":
                lifecycle["tick"] = 4;
                break;
            case "before-timer":
                lifecycle["before"]!["actor"]!["state_ticks_remaining"] = 2;
                break;
            case "after-phase":
                lifecycle["after"]!["actor"]!["action_phase"] = "Recovery";
                lifecycle["after"]!["actor"]!["state"] = "Recovery";
                break;
            case "missing-lifecycle":
                ReplaceLifecycleWithStateChanged(lifecycle);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionCommitInvalid);
    }

    [Theory]
    [InlineData("commit-source")]
    [InlineData("commit-related")]
    [InlineData("telegraph-source")]
    [InlineData("telegraph-related")]
    [InlineData("lifecycle-orphan")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_007_DecisionCommitDerivedCausalityTamperIsRejected(string mutation)
    {
        var replay = CreateValidStandard030();
        var commit = Event(replay, "ActionCommitted", "fighter_a");
        var telegraph = Event(replay, "AttackPrepared", "fighter_a");
        switch (mutation)
        {
            case "commit-source":
                commit["source_event_id"] =
                    Decision(replay, "fighter_b")["event_id"]!.GetValue<string>();
                break;
            case "commit-related":
                commit["payload"]!["related_event_ids"] = new JsonArray();
                break;
            case "telegraph-source":
                telegraph["source_event_id"] =
                    Decision(replay, "fighter_a")["event_id"]!.GetValue<string>();
                break;
            case "telegraph-related":
                telegraph["payload"]!["related_event_ids"] = new JsonArray();
                break;
            case "lifecycle-orphan":
                Event(replay, "ActionPhaseChanged", "fighter_a")["decision_id"] =
                    "dec-fighter_a-999999";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionCausalityInvalid);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("decision-id")]
    [InlineData("trace-order")]
    [InlineData("identity")]
    [InlineData("snapshot")]
    [InlineData("legal-set")]
    [InlineData("stage-order")]
    [InlineData("duplicate-chosen")]
    [InlineData("public-dominant-code")]
    [InlineData("public-dominant-multiplier")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_009_DiagnosticTraceTamperIsRejected(string mutation)
    {
        var replay = CreateValidDiagnostic030();
        var traces = replay["diagnostics"]!["decisions"]!.AsArray();
        switch (mutation)
        {
            case "missing":
                traces.RemoveAt(1);
                break;
            case "extra":
                traces.Add(traces[0]!.DeepClone());
                break;
            case "decision-id":
                traces[0]!["decision_id"] = "dec-fighter_a-999999";
                break;
            case "trace-order":
                var firstTrace = traces[0]!.DeepClone();
                var secondTrace = traces[1]!.DeepClone();
                traces[0] = secondTrace;
                traces[1] = firstTrace;
                break;
            case "identity":
                traces[0]!["sequence"] = 99;
                break;
            case "snapshot":
                traces[1]!["snapshot_digest"] =
                    "sha256:1111111111111111111111111111111111111111111111111111111111111111";
                break;
            case "legal-set":
                traces[0]!["candidates"]!.AsArray()[1]!["legal"] = false;
                traces[0]!["candidates"]!.AsArray()[1]!["first_rejection_code"] =
                    "CooldownActive";
                traces[0]!["candidates"]!.AsArray()[1]!["modifiers"] = new JsonArray();
                traces[0]!["candidates"]!.AsArray()[1]!["final_weight"] = 0;
                break;
            case "stage-order":
                var modifiers = traces[0]!["candidates"]!.AsArray()[1]!["modifiers"]!.AsArray();
                var firstModifier = modifiers[0]!.DeepClone();
                var secondModifier = modifiers[1]!.DeepClone();
                modifiers[0] = secondModifier;
                modifiers[1] = firstModifier;
                break;
            case "duplicate-chosen":
                var candidates = traces[0]!["candidates"]!.AsArray();
                candidates.Add(candidates[1]!.DeepClone());
                break;
            case "public-dominant-code":
                Decision(replay, "fighter_a")["payload"]!["dominant_modifiers"]![0]!["code"] =
                    "Synergy";
                Decision(replay, "fighter_a")["reason_codes"]![1] = "Synergy";
                break;
            case "public-dominant-multiplier":
                Decision(replay, "fighter_a")["payload"]!["dominant_modifiers"]![0]!["multiplier_fp"] =
                    1_251;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        AssertRejected(replay, ReplayVerificationCodes.DecisionDiagnosticInvalid);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_008_StandardReplayWithDiagnosticsIsRejectedBySchema()
    {
        var replay = CreateValidDiagnostic030();
        replay["profile"] = "standard";

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Layer == ReplayVerificationLayer.Schema &&
                issue.Severity == ReplayVerificationSeverity.Error &&
                issue.Code == ReplayVerificationCodes.SchemaViolation &&
                issue.Path == "$/diagnostics");
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_012_HistoricalEngine010SemanticsRemainUnchanged()
    {
        var result = ReplayTestFixture.Verify(
            ReplayTestFixture.ReadReplay("replay-standard.example.json"));

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_012_HistoricalDecisionTargetRoleIsNotBroadenedByEngine030()
    {
        var replay = JsonNode.Parse(
            ReplayTestFixture.ReadReplay("replay-standard.example.json"))!.AsObject();
        var decision = Decision(replay, "fighter_a");
        decision["target_id"] = null;
        decision["before"]!["target"] = null;
        decision["after"]!["target"] = null;
        Rehash(replay);

        AssertRejected(replay, ReplayVerificationCodes.RoleMismatch);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_012_CompatibleEnginePatchUsesWp08DecisionSemantics()
    {
        var replay = CreateValidStandard030();
        SetEngineVersion(replay, "battle.core/0.3.1");

        Decision(replay, "fighter_a")["payload"]!["candidate_count"] = 3;
        Rehash(replay);

        AssertRejected(replay, ReplayVerificationCodes.DecisionCandidateInvalid);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_012_CompatibleEnginePatchUsesWp08OptionalDecisionTargetRole()
    {
        var replay = CreateValidStandard030();
        SetEngineVersion(replay, "battle.core/0.3.1");
        ConvertDecisionBToSelfCombat(replay);
        Rehash(replay);

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Empty(result.Issues);
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_012_CompatibleEnginePatchRequiresActorOnlyCombatLifecycleRole()
    {
        var replay = CreateValidStandard030();
        SetEngineVersion(replay, "battle.core/0.3.1");
        var lifecycle = Event(replay, "ActionPhaseChanged", "fighter_a");
        lifecycle["target_id"] = "fighter_b";
        lifecycle["before"]!["target"] =
            Event(replay, "AttackPrepared", "fighter_a")["after"]!["target"]!.DeepClone();
        lifecycle["after"]!["target"] = lifecycle["before"]!["target"]!.DeepClone();
        Rehash(replay);

        AssertRejected(replay, ReplayVerificationCodes.RoleMismatch);
    }

    [Theory]
    [InlineData("action")]
    [InlineData("actor")]
    [InlineData("decision")]
    [InlineData("resolution-group")]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DecisionEnvelopeRejectsEachIdentityViolation(string mutation)
    {
        var replay = CreateValidStandard030();
        var decision = Decision(replay, "fighter_a");
        switch (mutation)
        {
            case "action":
                decision["action_id"] = "sys_wait";
                break;
            case "actor":
                decision["actor_id"] = null;
                break;
            case "decision":
                decision["decision_id"] = null;
                break;
            case "resolution-group":
                decision["resolution_group_id"] = "resolution-coverage-probe";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Rehash(replay);
        var issues = ValidateDecisionSemantics(replay);

        Assert.Contains(
            issues,
            issue => issue.Code == ReplayVerificationCodes.DecisionCandidateInvalid);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DecisionMarkerRejectsFrameMutation()
    {
        var replay = CreateValidStandard030();
        Decision(replay, "fighter_a")["after"]!["actor"]!["energy"] = 999;

        Rehash(replay);
        var issues = ValidateDecisionSemantics(replay);

        Assert.Contains(
            issues,
            issue => issue.Code == ReplayVerificationCodes.DecisionCommitInvalid);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void SourceFreeDecisionRequiresEmptyRelatedEvents()
    {
        var replay = CreateValidStandard030();
        var decision = Decision(replay, "fighter_a");
        decision["source_event_id"] = null;
        decision["payload"]!["related_event_ids"] = new JsonArray();

        Rehash(replay);
        var issues = ValidateDecisionSemantics(replay);

        Assert.DoesNotContain(
            issues,
            issue => issue.Code == ReplayVerificationCodes.DecisionCausalityInvalid);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DecisionRejectsRelatedEventsThatDoNotMirrorSource()
    {
        var replay = CreateValidStandard030();
        Decision(replay, "fighter_a")["payload"]!["related_event_ids"] = new JsonArray();

        Rehash(replay);
        var issues = ValidateDecisionSemantics(replay);

        Assert.Contains(
            issues,
            issue => issue.Code == ReplayVerificationCodes.DecisionCausalityInvalid);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DecisionRejectsUnknownSelectionModeSemantically()
    {
        var replay = CreateValidStandard030();
        var decision = Decision(replay, "fighter_a");
        decision["payload"]!["selection_mode"] = "UnsupportedMode";
        decision["reason_codes"]![0] = "UnsupportedMode";

        Rehash(replay);
        var issues = ValidateDecisionSemantics(replay);

        Assert.Contains(
            issues,
            issue => issue.Code == ReplayVerificationCodes.DecisionModeInvalid);
    }

    private static JsonObject CreateValidStandard030(string? selectionMode = null)
    {
        var replay = JsonNode.Parse(
            ReplayTestFixture.ReadReplay("replay-standard.example.json"))!.AsObject();
        replay["engine"]!["engine_version"] = Engine030;
        foreach (var combatEvent in replay["events"]!.AsArray())
        {
            combatEvent!["engine_version"] = Engine030;
        }

        MoveAttackAfterBothCommits(replay);
        NormalizeDecisionExplainability(replay);
        NormalizeCommitCausality(replay);
        if (selectionMode is "ZeroWeightFallback" or "HardOpportunity")
        {
            SetDecisionBMode(replay, selectionMode);
        }

        NormalizeEngine030FramesAndLifecycle(replay);
        Rehash(replay);
        return replay;
    }

    private static JsonObject CreateValidDiagnostic030()
    {
        var replay = CreateValidStandard030();
        replay["profile"] = "diagnostic";
        var decisionA = Decision(replay, "fighter_a");
        var decisionB = Decision(replay, "fighter_b");
        var chosenA = Candidate("bear_paw_jab", true, null, 1_000, 1_000);
        chosenA["modifiers"] = new JsonArray(
            Modifier("Tactic", 1_250),
            Modifier("Situation", 1_150),
            Modifier("Synergy", 1_000),
            Modifier("Counter", 1_000),
            Modifier("Variety", 1_000),
            Modifier("Opportunity", 1_000));
        replay["diagnostics"] = new JsonObject
        {
            ["decisions"] = new JsonArray(
                CreateTrace(
                    decisionA,
                    new JsonArray(
                        Candidate("bear_crushing_swipe", false, "CooldownActive", 720, 0),
                        chosenA,
                        Candidate("sys_wait", true, null, 150, 150))),
                CreateTrace(
                    decisionB,
                    new JsonArray(Candidate("sys_wait", true, null, 150, 150)))),
            ["warnings"] = new JsonArray(),
        };
        Rehash(replay);
        return replay;
    }

    private static JsonObject CreateTrace(JsonObject decision, JsonArray candidates) => new()
    {
        ["decision_id"] = decision["decision_id"]!.GetValue<string>(),
        ["tick"] = decision["tick"]!.GetValue<int>(),
        ["sequence"] = decision["sequence"]!.GetValue<int>(),
        ["actor_id"] = decision["actor_id"]!.GetValue<string>(),
        ["snapshot_digest"] = SnapshotDigest,
        ["candidates"] = candidates,
    };

    private static JsonObject Candidate(
        string actionId,
        bool legal,
        string? rejection,
        int baseWeight,
        int finalWeight) => new()
    {
        ["action_id"] = actionId,
        ["legal"] = legal,
        ["first_rejection_code"] = rejection,
        ["base_weight"] = baseWeight,
        ["modifiers"] = legal ? SixStageModifiers() : new JsonArray(),
        ["final_weight"] = finalWeight,
    };

    private static JsonArray SixStageModifiers() => new(
        Modifier("Tactic", 1_000),
        Modifier("Situation", 1_000),
        Modifier("Synergy", 1_000),
        Modifier("Counter", 1_000),
        Modifier("Variety", 1_000),
        Modifier("Opportunity", 1_000));

    private static JsonObject Modifier(string code, int multiplier) => new()
    {
        ["code"] = code,
        ["multiplier_fp"] = multiplier,
    };

    private static void MoveAttackAfterBothCommits(JsonObject replay)
    {
        var events = replay["events"]!.AsArray();
        var attack = events[4]!.DeepClone();
        events.RemoveAt(4);
        events.Insert(5, attack);
        ReindexEventIds(replay);
    }

    private static void ReindexEventIds(JsonObject replay)
    {
        var events = replay["events"]!.AsArray();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < events.Count; index++)
        {
            replacements.Add(
                events[index]!["event_id"]!.GetValue<string>(),
                EventId(index));
        }

        ReplaceEventIdStrings(replay, replacements);
        for (var index = 0; index < events.Count; index++)
        {
            events[index]!["sequence"] = index;
            events[index]!["event_id"] = EventId(index);
        }
    }

    private static void ReplaceEventIdStrings(
        JsonNode node,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    replacements.TryGetValue(text, out var replacement))
                {
                    obj[property.Key] = replacement;
                }
                else if (property.Value is not null)
                {
                    ReplaceEventIdStrings(property.Value, replacements);
                }
            }

            return;
        }

        if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    replacements.TryGetValue(text, out var replacement))
                {
                    array[index] = replacement;
                }
                else if (array[index] is not null)
                {
                    ReplaceEventIdStrings(array[index]!, replacements);
                }
            }
        }
    }

    private static void NormalizeDecisionExplainability(JsonObject replay)
    {
        var decisionA = Decision(replay, "fighter_a");
        decisionA["payload"]!["related_event_ids"] =
            new JsonArray(decisionA["source_event_id"]!.GetValue<string>());
        decisionA["payload"]!["dominant_modifiers"] = new JsonArray(
            Modifier("Tactic", 1_250),
            Modifier("Situation", 1_150));
        decisionA["reason_codes"] = new JsonArray("WeightedRng", "Tactic", "Situation");
        decisionA["rng"]!["raw_u32"] = "700";
        decisionA["rng"]!["result"] = 700;
        decisionA["rng"]!["normalized_fp"] = 608;

        var decisionB = Decision(replay, "fighter_b");
        decisionB["payload"]!["related_event_ids"] =
            new JsonArray(decisionB["source_event_id"]!.GetValue<string>());
        decisionB["reason_codes"] = new JsonArray("OnlyLegalAction");
    }

    private static void NormalizeCommitCausality(JsonObject replay)
    {
        foreach (var actor in new[] { "fighter_a", "fighter_b" })
        {
            var commit = Event(replay, "ActionCommitted", actor);
            commit["payload"]!["related_event_ids"] =
                new JsonArray(commit["source_event_id"]!.GetValue<string>());
            commit["reason_codes"] = new JsonArray("ActionSelected");
        }

        var attack = Event(replay, "AttackPrepared", "fighter_a");
        attack["payload"]!["related_event_ids"] =
            new JsonArray(attack["source_event_id"]!.GetValue<string>());
        attack["reason_codes"] = new JsonArray("AttackPrepared");
    }

    private static void SetDecisionBMode(JsonObject replay, string selectionMode)
    {
        var decision = Decision(replay, "fighter_b");
        var commit = Event(replay, "ActionCommitted", "fighter_b");
        var payload = decision["payload"]!;
        decision["rng"] = null;
        payload["selection_mode"] = selectionMode;
        decision["reason_codes"] = new JsonArray(selectionMode);
        payload["dominant_modifiers"] = new JsonArray();

        if (selectionMode == "ZeroWeightFallback")
        {
            payload["chosen_action_id"] = "sys_retreat";
            payload["legal_action_ids"] = new JsonArray("sys_retreat", "sys_wait");
            payload["candidate_count"] = 2;
            payload["chosen_weight"] = 0;
            payload["weight_sum"] = 0;
            decision["action_id"] = "sys_retreat";
            commit["action_id"] = "sys_retreat";
            commit["after"]!["actor"]!["action_id"] = "sys_retreat";
        }
        else
        {
            payload["legal_action_ids"] = new JsonArray("bear_paw_jab", "sys_wait");
            payload["candidate_count"] = 2;
            payload["chosen_weight"] = 150;
            payload["weight_sum"] = 1_150;
        }
    }

    private static void NormalizeEngine030FramesAndLifecycle(JsonObject replay)
    {
        var commitA = Event(replay, "ActionCommitted", "fighter_a");
        var commitB = Event(replay, "ActionCommitted", "fighter_b");
        var attack = Event(replay, "AttackPrepared", "fighter_a");
        var commitBAction = commitB["action_id"]!.GetValue<string>();
        var commitBActor = commitB["after"]!["actor"]!;
        if (commitBAction == "sys_retreat")
        {
            commitB["payload"]!["startup_ticks"] = 1;
            commitB["payload"]!["active_ticks"] = 5;
            commitB["payload"]!["recovery_ticks"] = 1;
            commitB["payload"]!["commit_direction"] = "Right";
            commitBActor["state"] = "Retreat";
            commitBActor["state_ticks_remaining"] = 1;
            commitBActor["action_phase"] = "Startup";
        }
        else
        {
            commitB["payload"]!["startup_ticks"] = 0;
            commitB["payload"]!["active_ticks"] = 5;
            commitB["payload"]!["recovery_ticks"] = 0;
            commitB["payload"]!["commit_direction"] = "None";
            commitBActor["state"] = "Idle";
            commitBActor["state_ticks_remaining"] = 5;
            commitBActor["action_phase"] = "Active";
        }

        commitB["payload"]!["energy_cost"] = 0;
        commitB["payload"]!["resource_cost"] = 0;
        commitB["payload"]!["cooldown_ticks"] = 0;
        attack["before"]!["actor"] = commitA["after"]!["actor"]!.DeepClone();
        attack["after"]!["actor"] = commitA["after"]!["actor"]!.DeepClone();
        attack["before"]!["target"] = commitBActor.DeepClone();
        attack["after"]!["target"] = commitBActor.DeepClone();

        var lifecycle = Event(replay, "ActionPhaseChanged", "fighter_a");
        lifecycle["source_event_id"] = commitA["event_id"]!.GetValue<string>();
        lifecycle["target_id"] = null;
        lifecycle["reason_codes"] = new JsonArray("StartupCompleted");
        lifecycle["before"]!["actor"] = commitA["after"]!["actor"]!.DeepClone();
        lifecycle["before"]!["actor"]!["state_ticks_remaining"] = 1;
        lifecycle["before"]!["target"] = null;
        lifecycle["after"]!["actor"] = lifecycle["before"]!["actor"]!.DeepClone();
        lifecycle["after"]!["actor"]!["state"] = "AttackActive";
        lifecycle["after"]!["actor"]!["state_ticks_remaining"] = 1;
        lifecycle["after"]!["actor"]!["action_phase"] = "Active";
        lifecycle["after"]!["target"] = null;
        lifecycle["payload"]!["related_event_ids"] =
            new JsonArray(commitA["event_id"]!.GetValue<string>());
        lifecycle["payload"]!["from_phase"] = "Startup";
        lifecycle["payload"]!["to_phase"] = "Active";
        lifecycle["payload"]!["phase_ticks"] = 1;
    }

    private static JsonObject ReplaceAttackWithEnergyCost(JsonObject replay)
    {
        var commit = Event(replay, "ActionCommitted", "fighter_a");
        var cost = Event(replay, "AttackPrepared", "fighter_a");
        commit["payload"]!["energy_cost"] = 10;
        cost["event_type"] = "ResourceChanged";
        cost["source_event_id"] = commit["event_id"]!.GetValue<string>();
        cost["target_id"] = null;
        cost["reason_codes"] = new JsonArray("ActionCost");
        cost["rng"] = null;
        cost["resolution_group_id"] = null;
        cost["before"]!["actor"] = commit["after"]!["actor"]!.DeepClone();
        cost["before"]!["target"] = null;
        cost["after"]!["actor"] = commit["after"]!["actor"]!.DeepClone();
        cost["after"]!["actor"]!["energy"] = 990;
        cost["after"]!["target"] = null;
        cost["payload"] = new JsonObject
        {
            ["related_event_ids"] = new JsonArray(commit["event_id"]!.GetValue<string>()),
            ["resource_kind"] = "Energy",
            ["resource_id"] = null,
            ["before"] = 1_000,
            ["delta"] = -10,
            ["after"] = 990,
            ["minimum"] = 0,
            ["maximum"] = 1_000,
            ["clamp_reason"] = null,
        };

        var lifecycle = Event(replay, "ActionPhaseChanged", "fighter_a");
        lifecycle["before"]!["actor"]!["energy"] = 990;
        lifecycle["after"]!["actor"]!["energy"] = 990;
        Rehash(replay);
        return cost;
    }

    private static void ReplaceLifecycleWithStateChanged(JsonObject lifecycle)
    {
        lifecycle["event_type"] = "StateChanged";
        lifecycle["payload"] = new JsonObject
        {
            ["related_event_ids"] = new JsonArray(
                lifecycle["source_event_id"]!.GetValue<string>()),
            ["old_state"] = "AttackPrepare",
            ["new_state"] = "AttackActive",
            ["duration_ticks"] = 1,
            ["control_ratio_fp"] = null,
            ["fatigue_multiplier_fp"] = null,
            ["immunity_result"] = "NotChecked",
        };
    }

    private static void ConvertDecisionBToSelfCombat(JsonObject replay)
    {
        var decision = Decision(replay, "fighter_b");
        var commit = Event(replay, "ActionCommitted", "fighter_b");
        const string actionId = "bear_paw_jab";
        decision["target_id"] = null;
        decision["before"]!["target"] = null;
        decision["after"]!["target"] = null;
        decision["action_id"] = actionId;
        decision["payload"]!["chosen_action_id"] = actionId;
        decision["payload"]!["legal_action_ids"] = new JsonArray(actionId);
        decision["payload"]!["candidate_count"] = 1;

        commit["target_id"] = null;
        commit["before"]!["target"] = null;
        commit["after"]!["target"] = null;
        commit["action_id"] = actionId;
        commit["after"]!["actor"]!["action_id"] = actionId;
        commit["after"]!["actor"]!["state"] = "AttackActive";
        commit["after"]!["actor"]!["state_ticks_remaining"] = 5;
        commit["after"]!["actor"]!["action_phase"] = "Active";
        commit["payload"]!["target_fighter_id"] = null;
        commit["payload"]!["target_position_at_commit"] = null;

        var attack = Event(replay, "AttackPrepared", "fighter_a");
        attack["before"]!["target"] = commit["after"]!["actor"]!.DeepClone();
        attack["after"]!["target"] = commit["after"]!["actor"]!.DeepClone();
    }

    private static void SetEngineVersion(JsonObject replay, string version)
    {
        replay["engine"]!["engine_version"] = version;
        foreach (var combatEvent in replay["events"]!.AsArray())
        {
            combatEvent!["engine_version"] = version;
        }
    }

    private static void Rehash(JsonObject replay)
    {
        var inputDigest = ReplayIntegrity.ComputeInputDigest(Serialize(replay)).ToString();
        replay["integrity"]!["input_digest"] = inputDigest;
        replay["events"]!.AsArray()[0]!["payload"]!["input_digest"] = inputDigest;

        var previousDigest = inputDigest;
        var events = replay["events"]!.AsArray();
        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index]!.AsObject();
            combatEvent["integrity"]!["prev_digest"] = previousDigest;
            combatEvent["integrity"]!["event_digest"] = ZeroDigest;
            previousDigest = ReplayIntegrity.ComputeEventDigest(Serialize(combatEvent)).ToString();
            combatEvent["integrity"]!["event_digest"] = previousDigest;
        }

        replay["integrity"]!["final_digest"] = previousDigest;
        replay["integrity"]!["event_count"] = events.Count;
    }

    private static void AssertRejected(JsonObject replay, string expectedCode)
    {
        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Layer == ReplayVerificationLayer.Semantic &&
                issue.Severity == ReplayVerificationSeverity.Error &&
                issue.Code == expectedCode);
    }

    private static IReadOnlyList<ReplayVerificationIssue> ValidateDecisionSemantics(
        JsonObject replay)
    {
        using var document = JsonDocument.Parse(Serialize(replay));
        var root = document.RootElement.Clone();
        var events = root
            .GetProperty("events")
            .EnumerateArray()
            .Select(item => item.Clone())
            .ToArray();
        var issues = new List<ReplayVerificationIssue>();
        var validator = typeof(ReplayIntegrity).Assembly.GetType(
            "Battle.Replay.Verification.DecisionReplaySemanticValidator",
            throwOnError: true)!;
        var validate = validator.GetMethod(
            "Validate",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
        validate.Invoke(null, new object[] { root, events, issues });
        return issues;
    }

    private static JsonObject Decision(JsonObject replay, string actor) =>
        Event(replay, "DecisionMade", actor);

    private static JsonObject Event(JsonObject replay, string eventType, string actor) =>
        replay["events"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item =>
                item["event_type"]!.GetValue<string>() == eventType &&
                item["actor_id"]!.GetValue<string>() == actor);

    private static string EventId(int sequence) =>
        "evt-" + sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] Serialize(JsonNode node) =>
        Encoding.UTF8.GetBytes(
            node.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = false,
                }));
}
