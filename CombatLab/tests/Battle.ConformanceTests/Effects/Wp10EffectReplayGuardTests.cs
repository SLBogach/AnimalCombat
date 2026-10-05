using System.Text.Json.Nodes;
using Battle.ConformanceTests.Replay;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10EffectReplayGuardTests
{
    [Theory]
    [InlineData("initial-membership")]
    [InlineData("missing-before")]
    [InlineData("missing-after")]
    [InlineData("defeated-recipient")]
    [InlineData("changed-expiry")]
    [InlineData("refresh-duration")]
    [InlineData("stack-duration")]
    [InlineData("decreased-stack")]
    [InlineData("late-decision")]
    [InlineData("post-cleanup-add")]
    [InlineData("post-cleanup-expiry")]
    [InlineData("post-cleanup-normal")]
    [InlineData("last-replacement")]
    public void MembershipLifetimeAndCleanupGuardsRejectIntegrityValidTampering(string mutation)
    {
        var replay = mutation is "changed-expiry" or "refresh-duration" ? Wp10EffectReplayTests.Refresh("Refresh", 1, 3)
            : mutation == "stack-duration" ? Wp10EffectReplayTests.Refresh("AddStacks", 2, 3)
            : mutation is "decreased-stack" or "post-cleanup-add" or "post-cleanup-expiry" or "post-cleanup-normal" ? Read("effects-stack-expiry")
            : mutation == "late-decision" ? Read("effects-impact-snapshot") : Wp10EffectReplayTests.Basic();
        var events = replay["events"]!.AsArray(); var added = events.First(x => x!["event_type"]!.GetValue<string>() == "EffectAdded")!;
        switch (mutation)
        {
            case "initial-membership": replay["input"]!["fighters"]![0]!["initial_frame"]!["effects"] = added["after"]!["actor"]!["effects"]!.DeepClone(); break;
            case "missing-before": added["before"]!["actor"] = null; break;
            case "missing-after": added["after"]!["actor"] = null; break;
            case "defeated-recipient": added["after"]!["actor"]!["state"] = "Defeated"; break;
            case "changed-expiry":
                events[2]!["payload"]!["expiry_boundary"] = "ExpireAfterTick"; events[2]!["after"]!["actor"]!["effects"]![0]!["expiry_boundary"] = "ExpireAfterTick"; break;
            case "refresh-duration": case "stack-duration": events[2]!["payload"]!["duration_ticks"] = 2; break;
            case "decreased-stack":
                var stacked = events.First(x => x!["event_type"]!.GetValue<string>() == "EffectAdded" && x["payload"]!["stacks_before"]!.GetValue<int>() == 2)!;
                stacked["payload"]!["stacks_after"] = 1;
                stacked["after"]!["actor"]!["effects"]!.AsArray().Single(x => x!["effect_id"]!.GetValue<string>() == stacked["effect_id"]!.GetValue<string>())!["stacks"] = 1; break;
            case "late-decision":
                added["payload"]!["duration_ticks"] = 1;
                added["after"]!["actor"]!["effects"]![0]!["ticks_remaining"] = 1; break;
            case "post-cleanup-add": case "post-cleanup-expiry": case "post-cleanup-normal":
                events.First(x => x!["event_type"]!.GetValue<string>() == "EffectRemoved")!["payload"]!["remove_reason"] = "BattleEnded"; break;
            case "last-replacement":
                var removed = events[2]!.DeepClone(); removed["payload"]!["remove_reason"] = "Replaced";
                removed["event_id"] = events[^1]!["event_id"]!.DeepClone(); removed["sequence"] = events.Count - 1; events[^1] = removed; break;
        }
        Reject(replay);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("old-state")]
    [InlineData("resurrection")]
    [InlineData("zero-knockdown")]
    [InlineData("repeat-knockdown")]
    public void ControlEnvelopeStateAndPositiveKnockdownGuardsRejectTampering(string mutation)
    {
        var replay = Wp10EffectReplayTests.Controls(mutation is "zero-knockdown" or "repeat-knockdown");
        var change = replay["events"]![1]!;
        switch (mutation)
        {
            case "actor": change["actor_id"] = null; break;
            case "before": change["before"]!["actor"] = null; break;
            case "after": change["after"]!["actor"] = null; break;
            case "old-state": change["payload"]!["old_state"] = "Stunned"; break;
            case "resurrection":
                change["before"]!["actor"]!["state"] = "Defeated"; change["payload"]!["old_state"] = "Defeated";
                break;
            case "zero-knockdown": change["payload"]!["duration_ticks"] = 0; break;
            case "repeat-knockdown":
                change = replay["events"]![2]!; change["reason_codes"] = new JsonArray("Knockdown"); change["payload"]!["duration_ticks"] = 0; break;
        }
        if (mutation == "actor")
        {
            Wp10EffectReplayTests.Rehash(replay);
            var result = ReplayTestFixture.Verify(Wp10EffectReplayTests.Serialize(replay));
            Assert.Contains(result.Issues, x => x.Layer == ReplayVerificationLayer.Schema && x.Severity == ReplayVerificationSeverity.Error);
            Assert.Empty(EffectPolicy(replay)); // Defensive policy returns safely; envelope schema owns missing StateChanged actor.
        }
        else Reject(replay);
    }

    [Fact]
    public void DefeatedToDefeatedObservationDoesNotClaimResurrectionInTheEffectPolicy()
    {
        var replay = Wp10EffectReplayTests.Controls(false); var change = replay["events"]![1]!;
        change["before"]!["actor"]!["state"] = "Defeated"; change["after"]!["actor"]!["state"] = "Defeated";
        change["payload"]!["old_state"] = "Defeated"; change["payload"]!["new_state"] = "Defeated";
        Assert.Empty(EffectPolicy(replay)); // This does not assert complete producer/schema/config conformance for a synthetic observation.
    }

    private static IReadOnlyList<ReplayVerificationIssue> EffectPolicy(JsonObject replay)
    {
        using var document = System.Text.Json.JsonDocument.Parse(Wp10EffectReplayTests.Serialize(replay));
        var issues = new List<ReplayVerificationIssue>();
        var type = typeof(ReplayVerifier).Assembly.GetType("Battle.Replay.Verification.EffectReplaySemanticValidator", throwOnError: true)!;
        type.GetMethod("Validate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null,
            [document.RootElement, document.RootElement.GetProperty("events").EnumerateArray().ToArray(), issues]);
        return issues;
    }

    private static JsonObject Read(string scenario) => JsonNode.Parse(ReplayTestFixture.ReadReplay(scenario + "-l1.engine-0.5.0.json"))!.AsObject();
    private static void Reject(JsonObject replay)
    {
        Wp10EffectReplayTests.Rehash(replay);
        var result = ReplayTestFixture.Verify(Wp10EffectReplayTests.Serialize(replay));
        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(result.Issues, x => x.Layer == ReplayVerificationLayer.Semantic && x.Severity == ReplayVerificationSeverity.Error);
        Assert.DoesNotContain(result.Issues, x => x.Layer == ReplayVerificationLayer.Integrity && x.Severity == ReplayVerificationSeverity.Error);
    }
}
