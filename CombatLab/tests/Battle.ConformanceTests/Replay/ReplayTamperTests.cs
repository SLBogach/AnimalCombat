using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Replay;

public sealed class ReplayTamperTests
{
    private const string ZeroDigest =
        "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    [Theory]
    [InlineData("input", ReplayVerificationCodes.InputDigestMismatch)]
    [InlineData("event-payload", ReplayVerificationCodes.EventDigestMismatch)]
    [InlineData("previous-digest", ReplayVerificationCodes.PreviousDigestMismatch)]
    [InlineData("event-digest", ReplayVerificationCodes.EventDigestMismatch)]
    [InlineData("final-digest", ReplayVerificationCodes.FinalDigestMismatch)]
    [InlineData("delete-event", ReplayVerificationCodes.SequenceInvalid)]
    [InlineData("swap-events", ReplayVerificationCodes.SequenceInvalid)]
    [InlineData("sequence", ReplayVerificationCodes.SequenceInvalid)]
    [InlineData("duplicate-sequence", ReplayVerificationCodes.SequenceInvalid)]
    [InlineData("tick", ReplayVerificationCodes.TickOrderInvalid)]
    [InlineData("float", "json.non_integer_number")]
    [InlineData("event-id", ReplayVerificationCodes.EventIdInvalid)]
    [InlineData("source-forward", ReplayVerificationCodes.CausalityInvalid)]
    [InlineData("related-forward", ReplayVerificationCodes.CausalityInvalid)]
    [InlineData("unknown-event-type", ReplayVerificationCodes.SchemaViolation)]
    [InlineData("unknown-member", ReplayVerificationCodes.SchemaViolation)]
    [InlineData("missing-required-null", ReplayVerificationCodes.SchemaViolation)]
    [InlineData("u64-number", ReplayVerificationCodes.SchemaViolation)]
    [InlineData("u64-leading-zero", ReplayVerificationCodes.SchemaViolation)]
    [InlineData("standard-diagnostics", ReplayVerificationCodes.SchemaViolation)]
    public void TamperedReplay_IsRejectedWithStableDiagnostic(
        string mutation,
        string expectedCode)
    {
        var replay = ReadStandardReplayNode();
        ApplyMutation(replay, mutation);

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(
            result.Issues,
            issue =>
                issue.Severity == ReplayVerificationSeverity.Error &&
                issue.Code == expectedCode);
    }

    [Fact]
    public void InvalidKeyframeStateDigest_IsWarningWithSafeEventReplayFallback()
    {
        var replay = ReadStandardReplayNode();
        replay["keyframes"]!.AsArray()[0]!["state_digest"] = ZeroDigest;

        var result = ReplayTestFixture.Verify(Serialize(replay));

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.True(result.HasWarnings);
        var warning = Assert.Single(result.Issues);
        Assert.Equal(ReplayVerificationSeverity.Warning, warning.Severity);
        Assert.Equal(ReplayVerificationLayer.Integrity, warning.Layer);
        Assert.Equal(ReplayVerificationCodes.KeyframeMismatch, warning.Code);
        Assert.Equal("$/keyframes/0/state_digest", warning.Path);
        Assert.Contains("Discard this keyframe", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateJsonMember_IsRejectedBeforeSchemaValidation()
    {
        var canonical = global::Battle.Replay.CanonicalJson.CanonicalJson.Canonicalize(
            ReplayTestFixture.ReadReplay("replay-standard.example.json"));
        var json = Encoding.UTF8.GetString(canonical);
        json = json.Replace(
            "\"profile\":\"standard\"",
            "\"profile\":\"standard\",\"profile\":\"standard\"",
            StringComparison.Ordinal);

        var result = ReplayTestFixture.Verify(Encoding.UTF8.GetBytes(json));

        Assert.False(result.IsValid);
        Assert.Equal("json.duplicate_member", Assert.Single(result.Issues).Code);
        Assert.Equal(ReplayVerificationLayer.Syntax, result.Issues[0].Layer);
    }

    private static JsonObject ReadStandardReplayNode() =>
        JsonNode.Parse(
            ReplayTestFixture.ReadReplay("replay-standard.example.json"))!.AsObject();

    private static void ApplyMutation(JsonObject replay, string mutation)
    {
        var events = replay["events"]!.AsArray();
        switch (mutation)
        {
            case "input":
                replay["input"]!["master_seed"] = "2026072902";
                break;

            case "event-payload":
                Event(events, 9)["payload"]!["hp_after"] = 1;
                break;

            case "previous-digest":
                Event(events, 4)["integrity"]!["prev_digest"] = ZeroDigest;
                break;

            case "event-digest":
                Event(events, 4)["integrity"]!["event_digest"] = ZeroDigest;
                break;

            case "final-digest":
                replay["integrity"]!["final_digest"] = ZeroDigest;
                break;

            case "delete-event":
                events.RemoveAt(5);
                break;

            case "swap-events":
                var left = events[3]!.DeepClone();
                var right = events[4]!.DeepClone();
                events[3] = right;
                events[4] = left;
                break;

            case "sequence":
                Event(events, 5)["sequence"] = 99;
                break;

            case "duplicate-sequence":
                Event(events, 5)["sequence"] = 4;
                break;

            case "tick":
                Event(events, 7)["tick"] = 2;
                break;

            case "float":
                Event(events, 7)["tick"] = 2.5;
                break;

            case "event-id":
                Event(events, 5)["event_id"] = "evt-0000000099";
                break;

            case "source-forward":
                Event(events, 3)["source_event_id"] = "evt-0000000009";
                break;

            case "related-forward":
                Event(events, 8)["payload"]!["related_event_ids"] =
                    new JsonArray("evt-0000000009");
                break;

            case "unknown-event-type":
                Event(events, 8)["event_type"] = "UnknownEvent";
                break;

            case "unknown-member":
                Event(events, 8)["unexpected_member"] = true;
                break;

            case "missing-required-null":
                Event(events, 0).Remove("actor_id");
                break;

            case "u64-number":
                replay["input"]!["master_seed"] = 2026072901;
                break;

            case "u64-leading-zero":
                replay["input"]!["master_seed"] = "02026072901";
                break;

            case "standard-diagnostics":
                var diagnostic = JsonNode.Parse(
                    ReplayTestFixture.ReadReplay("replay-diagnostic.example.json"))!.AsObject();
                replay["diagnostics"] = diagnostic["diagnostics"]!.DeepClone();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }
    }

    private static JsonObject Event(JsonArray events, int index) =>
        events[index]!.AsObject();

    private static byte[] Serialize(JsonObject replay) =>
        Encoding.UTF8.GetBytes(
            replay.ToJsonString(
                new JsonSerializerOptions
                {
                    WriteIndented = false,
                }));
}
