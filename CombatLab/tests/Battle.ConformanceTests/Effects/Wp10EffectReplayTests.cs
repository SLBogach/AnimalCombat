using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.ConformanceTests.Replay;
using Battle.Replay.Integrity;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Effects;

/// <summary>In-memory public witnesses, not new golden artifacts or private-config proof.</summary>
[Trait("WorkPackage", "WP10")]
public sealed class Wp10EffectReplayTests
{
    [Theory, Trait("AcceptanceId", "WP10-EVT-001")]
    [InlineData("missing-effect")]
    [InlineData("missing-actor")]
    [InlineData("wrong-actor-frame")]
    [InlineData("self-target")]
    [InlineData("mutated-source-target")]
    public void EffectEnvelopeAndAffectedActorRolesAreRequired(string mutation)
    {
        var replay = Basic(); var added = Events(replay)[1]!;
        switch (mutation)
        {
            case "missing-effect": added["effect_id"] = null; break;
            case "missing-actor": added["actor_id"] = null; break;
            case "wrong-actor-frame": added["after"]!["actor"]!["fighter_id"] = "fighter_b"; break;
            case "self-target": added["target_id"] = "fighter_a"; break;
            case "mutated-source-target":
                added["target_id"] = "fighter_b";
                added["before"]!["target"] = Initial(replay, 1).DeepClone();
                added["after"]!["target"] = Initial(replay, 1).DeepClone();
                added["after"]!["target"]!["energy"] = 999; break;
        }
        Reject(replay);
    }

    [Theory, Trait("AcceptanceId", "WP10-EVT-002")]
    [InlineData("forward-source")]
    [InlineData("missing-related")]
    [InlineData("duplicate-related")]
    [InlineData("rng")]
    [InlineData("action")]
    [InlineData("decision")]
    [InlineData("group")]
    public void EffectCausalityIsEarlierUniqueDeterministicAndInheritedOnlyWhereApplicable(string mutation)
    {
        var replay = Basic(); var added = Events(replay)[1]!;
        switch (mutation)
        {
            case "forward-source": added["source_event_id"] = Id(2); break;
            case "missing-related": added["payload"]!["related_event_ids"] = new JsonArray(); break;
            case "duplicate-related": added["payload"]!["related_event_ids"] = new JsonArray(Id(0), Id(0)); break;
            case "rng":
                added["rng"] = JsonNode.Parse(ReplayTestFixture.ReadReplay("decision-weighted-l1.engine-0.4.0.json"))!["events"]!
                    .AsArray().First(x => x!["rng"] is not null)!["rng"]!.DeepClone(); break;
            case "action": added["action_id"] = "bear_earthbreaker"; break;
            case "decision": added["decision_id"] = "dec-fighter_a-999999"; break;
            case "group": added["resolution_group_id"] = "resolution:unrelated"; break;
        }
        Reject(replay);
    }

    [Theory, Trait("AcceptanceId", "WP10-EVT-003")]
    [InlineData("before-stack")]
    [InlineData("after-stack")]
    [InlineData("new-two-stacks")]
    [InlineData("duration")]
    [InlineData("boundary")]
    [InlineData("duplicate")]
    [InlineData("unrelated-stat")]
    [InlineData("absent-added")]
    public void AddedPayloadAndFrameDeltaMustDescribeOneRealMutation(string mutation)
    {
        var replay = Basic(); var added = Events(replay)[1]!; var payload = added["payload"]!;
        var entries = added["after"]!["actor"]!["effects"]!.AsArray();
        switch (mutation)
        {
            case "before-stack": payload["stacks_before"] = 1; break;
            case "after-stack": payload["stacks_after"] = 2; break;
            case "new-two-stacks": payload["stacks_after"] = 2; entries[0]!["stacks"] = 2; break;
            case "duration": payload["duration_ticks"] = 3; break;
            case "boundary": payload["expiry_boundary"] = "ExpireAfterTick"; break;
            case "duplicate": entries.Add(entries[0]!.DeepClone()); break;
            case "unrelated-stat": added["after"]!["actor"]!["energy"] = 999; break;
            case "absent-added": entries.Clear(); break;
        }
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Theory, Trait("AcceptanceId", "WP10-EVT-004")]
    [InlineData("absent-profile")]
    [InlineData("before-stack")]
    [InlineData("after-stack")]
    [InlineData("still-present")]
    [InlineData("wrong-boundary")]
    [InlineData("replacement-without-add")]
    public void RemovalMustMatchTheExistingOccupantAndReason(string mutation)
    {
        var replay = Basic(); var removed = Events(replay)[2]!;
        switch (mutation)
        {
            case "absent-profile": removed["effect_id"] = "effect_unknown"; break;
            case "before-stack": removed["payload"]!["stacks_before"] = 2; break;
            case "after-stack": removed["payload"]!["stacks_after"] = 1; break;
            case "still-present": removed["after"]!["actor"] = removed["before"]!["actor"]!.DeepClone(); break;
            case "wrong-boundary": removed["payload"]!["remove_reason"] = "ExpiredAfterTick"; break;
            case "replacement-without-add": removed["payload"]!["remove_reason"] = "Replaced"; break;
        }
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Theory, Trait("AcceptanceId", "WP10-EVT-005")]
    [InlineData(false)]
    [InlineData(true)]
    public void BeforeAndAfterExpiryUseTheirExactPublicBoundaries(bool after)
    {
        var replay = Basic(after); var removed = Events(replay)[2]!;
        removed["tick"] = after ? 0 : 1;
        removed["before"]!["actor"]!["effects"]![0]!["ticks_remaining"] = after ? 2 : 1;
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Fact]
    public void APostExpiryDecisionCannotReintroduceStaleMembership()
    {
        var replay = Basic(); var events = Events(replay); var end = events[^1]!.DeepClone(); events.RemoveAt(events.Count - 1);
        var historical = Read("wait-equal-l1.engine-0.4.0.json");
        foreach (var type in new[] { "DecisionMade", "ActionCommitted" })
        {
            var item = Events(historical).First(x => x!["event_type"]!.GetValue<string>() == type &&
                x["actor_id"]!.GetValue<string>() == "fighter_a")!.DeepClone();
            item["engine_version"] = "battle.core/0.5.0"; item["tick"] = 2; item["sequence"] = events.Count;
            item["config_hash"] = replay["config"]!["config_hash"]!.DeepClone();
            item["event_id"] = Id(events.Count); item["source_event_id"] = type == "DecisionMade" ? Id(0) : Id(3);
            item["payload"]!["related_event_ids"] = new JsonArray(type == "DecisionMade" ? Id(0) : Id(3));
            events.Add(item);
        }
        end["event_id"] = Id(events.Count); end["sequence"] = events.Count; events.Add(end);
        replay["summary"]!["event_count"] = events.Count;
        replay["summary"]!["final_frames"]![0] = events[4]!["after"]!["actor"]!.DeepClone();
        end["payload"]!["final_frames"] = replay["summary"]!["final_frames"]!.DeepClone();
        replay["keyframes"]![1]!["after_sequence"] = events.Count - 1;
        replay["keyframes"]![1]!["fighters"] = replay["summary"]!["final_frames"]!.DeepClone();
        Valid(replay);
        events[3]!["before"]!["actor"]!["effects"] = new JsonArray(Entry(1, 0, "ExpireBeforeTick"));
        events[3]!["after"]!["actor"]!["effects"] = new JsonArray(Entry(1, 0, "ExpireBeforeTick"));
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Fact]
    public void ExpiryUsesTheLatestSuccessfulApplicationRatherThanAnOlderRefreshCause()
    {
        var replay = Refresh("Refresh", 1, 3); Valid(replay);
        Events(replay)[3]!["source_event_id"] = Id(1);
        Events(replay)[3]!["payload"]!["related_event_ids"] = new JsonArray(Id(1));
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Fact]
    public void ReplacementPairKeepsTheSameCauseAndPolicy()
    {
        var replay = Replacement(); Valid(replay);
        Events(replay)[3]!["source_event_id"] = Id(1);
        Events(replay)[3]!["payload"]!["related_event_ids"] = new JsonArray(Id(1));
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
        replay = Replacement(); Events(replay)[3]!["payload"]!["stack_policy"] = "Reject";
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Theory]
    [InlineData("Refresh", 1, 3, true)]
    [InlineData("AddStacks", 2, 3, true)]
    [InlineData("AddStacks", 2, 1, true)]
    [InlineData("AddStacks", 1, 3, true)]
    [InlineData("Refresh", 2, 3, false)]
    [InlineData("Refresh", 1, 1, false)]
    [InlineData("AddStacks", 3, 3, false)]
    [InlineData("AddStacks", 1, 1, false)]
    [InlineData("Reject", 1, 3, false)]
    [InlineData("Replace", 1, 3, false)]
    [InlineData("StrongestWins", 1, 3, false)]
    public void OccupiedProfileAllowsOnlyPublicRefreshOrSingleStackDelta(string policy, int stacks, int remaining, bool valid)
    {
        var replay = Refresh(policy, stacks, remaining);
        if (valid) Valid(replay); else Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Theory, Trait("AcceptanceId", "WP10-EVT-006")]
    [InlineData("state")]
    [InlineData("action")]
    [InlineData("duration")]
    [InlineData("null-duration")]
    [InlineData("immunity")]
    public void PreventedControlIsAZeroDurationIdenticalFrameObservation(string mutation)
    {
        var replay = Controls(false); var change = Events(replay)[1]!;
        switch (mutation)
        {
            case "state": change["after"]!["actor"]!["state"] = "Stunned"; change["payload"]!["new_state"] = "Stunned"; break;
            case "action": change["after"]!["actor"]!["action_id"] = "sys_wait"; break;
            case "duration": change["payload"]!["duration_ticks"] = 1; break;
            case "null-duration": change["payload"]!["duration_ticks"] = null; break;
            case "immunity": change["after"]!["actor"]!["effects"] = new JsonArray(Entry(1, 25, "ExpireBeforeTick")); break;
        }
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Theory]
    [InlineData("action-in-knockdown")]
    [InlineData("wrong-countdown")]
    [InlineData("skipped-grounded")]
    [InlineData("early-ready")]
    [InlineData("extended-stage")]
    public void KnockdownFramesFollowAnActionlessSingleReadyBoundary(string mutation)
    {
        var replay = Controls(true); var events = Events(replay);
        switch (mutation)
        {
            case "action-in-knockdown": events[1]!["after"]!["actor"]!["action_id"] = "sys_wait"; break;
            case "wrong-countdown": events[2]!["before"]!["actor"]!["state_ticks_remaining"] = 5; break;
            case "skipped-grounded": events[2]!["reason_codes"] = new JsonArray("KnockdownGetUp"); break;
            case "early-ready": events[4]!["tick"] = 5; break;
            case "extended-stage": events[2]!["payload"]!["duration_ticks"] = 5; break;
        }
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Fact, Trait("AcceptanceId", "WP10-EVT-007")]
    public void AdvisoryKeyframeCanBeDiscardedWhileAuthoritativeMembershipAndFinalFramesCannot()
    {
        var replay = Basic(); var keyframes = replay["keyframes"]!.AsArray();
        var advisory = keyframes[0]!.DeepClone(); advisory["after_sequence"] = 1;
        advisory["fighters"]![0]!["effects"] = new JsonArray(Entry(1, 2, "ExpireBeforeTick"));
        keyframes.Insert(1, advisory); Valid(replay);
        advisory["fighters"]![0]!["effects"]![0]!["ticks_remaining"] = 9;
        Rehash(replay); var result = ReplayTestFixture.Verify(Serialize(replay));
        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(result.Issues, x => x.Code == ReplayVerificationCodes.KeyframeMismatch && x.Severity == ReplayVerificationSeverity.Warning);
        // No stale snapshot can be propagated into canonical after/summary frames.
        replay = Basic(); Events(replay)[2]!["after"]!["actor"]!["effects"] = new JsonArray(Entry(1, 0, "ExpireBeforeTick"));
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
        replay = Basic(); replay["summary"]!["final_frames"]![0]!["effects"] = new JsonArray(Entry(1, 0, "ExpireBeforeTick"));
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    [Fact, Trait("AcceptanceId", "WP10-EVT-008")]
    public void Engine05ComposesDecisionAndResolutionValidationWithoutRetrofittingHistoricalEffectPolicy()
    {
        var decisions = Read("decision-weighted-l1.engine-0.4.0.json"); Version(decisions, "battle.core/0.5.0"); Valid(decisions);
        var selected = Events(decisions).First(x => x!["event_type"]!.GetValue<string>() == "DecisionMade")!;
        selected["payload"]!["candidate_count"] = 3;
        Reject(decisions, ReplayVerificationCodes.DecisionCandidateInvalid);
        var resolution = Read("resolution-basic-l1.engine-0.4.0.json"); Version(resolution, "battle.core/0.5.0"); Valid(resolution);
        Events(resolution).First(x => x!["event_type"]!.GetValue<string>() == "DamageApplied")!["payload"]!["breakdown"]!["final"] = 599;
        Reject(resolution, ReplayVerificationCodes.ResolutionInvalid);
        foreach (var version in new[] { "0.1.0", "0.2.0", "0.3.0", "0.4.0" })
            Valid(Read("wait-equal-l1.engine-" + version + ".json"));
        var opaque = Basic(); Version(opaque, "battle.core/0.4.0"); Events(opaque)[1]!["effect_id"] = null;
        Rehash(opaque); var result = ReplayTestFixture.Verify(Serialize(opaque));
        Assert.DoesNotContain(result.Issues, x => x.Code == ReplayVerificationCodes.EffectInvalid);
    }

    [Theory]
    [InlineData("battle.core/0.5.7", true)]
    [InlineData("battle.core/0.5.", false)]
    [InlineData("battle.core/0.5.01", false)]
    [InlineData("battle.core/0.5.x", false)]
    public void EffectVersionPolicyRequiresACanonicalNumericPatch(string version, bool enabled)
    {
        var replay = Basic(); Version(replay, version); Events(replay)[1]!["effect_id"] = null;
        Rehash(replay); var result = ReplayTestFixture.Verify(Serialize(replay));
        Assert.Equal(enabled, result.Issues.Any(x => x.Code == ReplayVerificationCodes.EffectInvalid));
    }

    [Theory]
    [InlineData("balance_schema_version", "combat.balance/0.1")]
    [InlineData("config_version", "v0.1")]
    public void PublicEngine05MetadataCannotPretendThatLegacyBalanceWasImplicitlyMigrated(string field, string value)
    {
        var replay = Basic(); replay["config"]![field] = value;
        Reject(replay, ReplayVerificationCodes.EffectInvalid);
    }

    internal static JsonObject Basic(bool after = false)
    {
        var replay = Skeleton(); var boundary = after ? "ExpireAfterTick" : "ExpireBeforeTick";
        var tick = after ? 1 : 2; var events = Events(replay);
        events.Add(Mutation(replay, "EffectAdded", 1, 0, "effect_trial", 0, 1, 2, boundary, "Refresh", 0));
        events.Add(Mutation(replay, "EffectRemoved", 2, tick, "effect_trial", 1, 0, after ? 1 : 0, boundary,
            after ? "ExpiredAfterTick" : "ExpiredBeforeTick", 1));
        Finish(replay, tick); Valid(replay); return replay;
    }

    internal static JsonObject Replacement()
    {
        var replay = Skeleton(); var events = Events(replay);
        events.Add(Mutation(replay, "EffectAdded", 1, 0, "effect_trial", 0, 1, 2, "ExpireBeforeTick", "Replace", 0));
        events.Add(Mutation(replay, "EffectRemoved", 2, 0, "effect_trial", 1, 0, 2, "ExpireBeforeTick", "Replaced", 0));
        events.Add(Mutation(replay, "EffectAdded", 3, 0, "effect_beta", 0, 1, 5, "ExpireBeforeTick", "Replace", 0));
        events.Add(Mutation(replay, "EffectRemoved", 4, 5, "effect_beta", 1, 0, 0, "ExpireBeforeTick", "ExpiredBeforeTick", 3));
        Finish(replay, 5); return replay;
    }

    internal static JsonObject Refresh(string policy, int stacks, int remaining)
    {
        var replay = Skeleton(); var events = Events(replay);
        events.Add(Mutation(replay, "EffectAdded", 1, 0, "effect_trial", 0, 1, 2, "ExpireBeforeTick", "Refresh", 0));
        var refresh = Mutation(replay, "EffectAdded", 2, 1, "effect_trial", 1, stacks, remaining, "ExpireBeforeTick", policy, 1);
        refresh["before"]!["actor"]!["effects"]![0]!["ticks_remaining"] = 1; events.Add(refresh);
        events.Add(Mutation(replay, "EffectRemoved", 3, 1 + remaining, "effect_trial", stacks, 0, 0,
            "ExpireBeforeTick", "ExpiredBeforeTick", 2));
        Finish(replay, 1 + remaining); return replay;
    }

    internal static JsonObject Controls(bool knockdown)
    {
        var replay = Skeleton(); var events = Events(replay);
        var steps = knockdown
            ? new[] { (0, "DecisionReady", "KnockedDown", (int?)6, "Knockdown"), (2, "KnockedDown", "KnockedDown", (int?)4, "KnockdownGrounded"),
                (4, "KnockedDown", "KnockedDown", (int?)2, "KnockdownGetUp"), (6, "KnockedDown", "DecisionReady", (int?)null, "KnockdownCompleted") }
            : new[] { (0, "DecisionReady", "DecisionReady", (int?)0, "ControlPrevented") };
        foreach (var (tick, oldState, newState, duration, reason) in steps)
        {
            var change = Events(replay)[0]!.DeepClone().AsObject(); change["event_type"] = "StateChanged";
            change["event_id"] = Id(events.Count); change["sequence"] = events.Count; change["tick"] = tick;
            change["actor_id"] = "fighter_b"; change["source_event_id"] = Id(0); change["reason_codes"] = new JsonArray(reason);
            var before = Initial(replay, 1).DeepClone(); before["state"] = oldState;
            before["state_ticks_remaining"] = oldState == "KnockedDown" ? 6 - tick : null;
            var afterFrame = Initial(replay, 1).DeepClone(); afterFrame["state"] = newState;
            afterFrame["state_ticks_remaining"] = newState == "KnockedDown" ? 6 - tick : null;
            change["before"]!["actor"] = before; change["after"]!["actor"] = afterFrame;
            change["payload"] = new JsonObject { ["related_event_ids"] = new JsonArray(Id(0)), ["old_state"] = oldState,
                ["new_state"] = newState, ["duration_ticks"] = duration, ["control_ratio_fp"] = null,
                ["fatigue_multiplier_fp"] = null, ["immunity_result"] = knockdown ? "Allowed" : "Prevented" };
            events.Add(change);
        }
        Finish(replay, knockdown ? 6 : 2); Valid(replay); return replay;
    }

    private static JsonObject Skeleton()
    {
        var replay = Read("wait-equal-l1.engine-0.4.0.json"); Version(replay, "battle.core/0.5.0");
        var started = Events(replay)[0]!.DeepClone(); replay["events"] = new JsonArray(started);
        return replay;
    }

    private static JsonObject Mutation(JsonObject replay, string type, int sequence, int tick, string effect,
        int beforeStacks, int afterStacks, int remaining, string boundary, string policyOrReason, int source)
    {
        var item = Events(replay)[0]!.DeepClone().AsObject(); item["event_type"] = type;
        item["event_id"] = Id(sequence); item["sequence"] = sequence; item["tick"] = tick;
        item["actor_id"] = "fighter_a"; item["effect_id"] = effect; item["source_event_id"] = Id(source);
        item["reason_codes"] = new JsonArray(type == "EffectAdded" ? "EffectApplied" : policyOrReason);
        var before = Initial(replay, 0).DeepClone(); var after = Initial(replay, 0).DeepClone();
        before["effects"] = beforeStacks == 0 ? new JsonArray() : new JsonArray(Entry(beforeStacks, remaining, boundary, effect));
        after["effects"] = afterStacks == 0 ? new JsonArray() : new JsonArray(Entry(afterStacks, remaining, boundary, effect));
        item["before"]!["actor"] = before; item["after"]!["actor"] = after;
        var payload = new JsonObject { ["related_event_ids"] = new JsonArray(Id(source)), ["stacks_before"] = beforeStacks, ["stacks_after"] = afterStacks };
        if (type == "EffectAdded") { payload["duration_ticks"] = remaining; payload["expiry_boundary"] = boundary; payload["stack_policy"] = policyOrReason; }
        else payload["remove_reason"] = policyOrReason;
        item["payload"] = payload; return item;
    }

    private static void Finish(JsonObject replay, int tick)
    {
        var final = replay["input"]!["fighters"]!.AsArray().Select(x => x!["initial_frame"]!.DeepClone()).ToArray();
        var summary = replay["summary"]!; summary["duration_ticks"] = tick; summary["end_tick"] = tick;
        summary["final_frames"] = new JsonArray(final); summary["pivotal_event_ids"] = new JsonArray();
        var ended = Read("wait-equal-l1.engine-0.4.0.json")["events"]!.AsArray().Last()!.DeepClone();
        var events = Events(replay); ended["event_id"] = Id(events.Count); ended["sequence"] = events.Count;
        ended["tick"] = tick; ended["source_event_id"] = null; ended["engine_version"] = "battle.core/0.5.0";
        ended["config_hash"] = replay["config"]!["config_hash"]!.DeepClone();
        var payload = summary.DeepClone().AsObject(); payload.Remove("event_count"); payload["related_event_ids"] = new JsonArray();
        ended["payload"] = payload; events.Add(ended); summary["event_count"] = events.Count;
        var keyframes = replay["keyframes"]!.AsArray(); keyframes[1]!["after_sequence"] = events.Count - 1;
        keyframes[1]!["tick"] = tick; keyframes[1]!["fighters"] = summary["final_frames"]!.DeepClone();
        Rehash(replay);
    }

    internal static void Rehash(JsonObject replay)
    {
        var input = ReplayIntegrity.ComputeInputDigest(Serialize(replay)).ToString(); replay["integrity"]!["input_digest"] = input;
        Events(replay)[0]!["payload"]!["input_digest"] = input; var previous = input;
        foreach (var item in Events(replay))
        {
            item!["integrity"]!["prev_digest"] = previous;
            previous = ReplayIntegrity.ComputeEventDigest(Serialize(item)).ToString(); item["integrity"]!["event_digest"] = previous;
        }
        replay["integrity"]!["final_digest"] = previous; replay["integrity"]!["event_count"] = Events(replay).Count;
        foreach (var frame in replay["keyframes"]!.AsArray())
            frame!["state_digest"] = ReplayIntegrity.ComputeKeyframeStateDigest(Serialize(frame)).ToString();
    }

    private static void Version(JsonObject replay, string version)
    {
        replay["engine"]!["engine_version"] = version;
        if (version.StartsWith("battle.core/0.5.", StringComparison.Ordinal))
        {
            replay["config"]!["balance_schema_version"] = "combat.balance/0.2";
            replay["config"]!["config_version"] = "v0.2";
            replay["config"]!["config_hash"] = "sha256:5361ec68359de06a1f4ff458893ad4d537825c873ffb0252e272a5b429b4e0c4";
        }
        foreach (var item in Events(replay))
        { item!["engine_version"] = version; item["config_hash"] = replay["config"]!["config_hash"]!.DeepClone(); }
    }
    private static JsonObject Entry(int stacks, int remaining, string boundary, string id = "effect_trial") => new()
    { ["effect_id"] = id, ["stacks"] = stacks, ["ticks_remaining"] = remaining, ["expiry_boundary"] = boundary };
    private static JsonNode Initial(JsonObject replay, int index) => replay["input"]!["fighters"]![index]!["initial_frame"]!;
    private static JsonArray Events(JsonObject replay) => replay["events"]!.AsArray();
    private static string Id(int sequence) => "evt-" + sequence.ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
    private static JsonObject Read(string name) => JsonNode.Parse(ReplayTestFixture.ReadReplay(name))!.AsObject();
    internal static byte[] Serialize(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
    private static void Valid(JsonObject replay)
    {
        Rehash(replay); var result = ReplayTestFixture.Verify(Serialize(replay));
        Assert.True(result.IsValid, ReplayTestFixture.Describe(result)); Assert.False(result.HasWarnings, ReplayTestFixture.Describe(result));
    }
    private static void Reject(JsonObject replay, string? code = null)
    {
        Rehash(replay); var result = ReplayTestFixture.Verify(Serialize(replay));
        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.DoesNotContain(result.Issues, x => x.Layer == ReplayVerificationLayer.Integrity && x.Severity == ReplayVerificationSeverity.Error);
        if (code is not null) Assert.Contains(result.Issues, x => x.Code == code);
    }
}
