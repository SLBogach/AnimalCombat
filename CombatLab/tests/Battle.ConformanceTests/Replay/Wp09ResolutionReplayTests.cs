using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Replay.CanonicalJson;
using Battle.Replay.Integrity;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Replay;

[Trait("WorkPackage", "WP09")]
public sealed class Wp09ResolutionReplayTests
{
    private const string ZeroDigest =
        "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    [Fact]
    [Trait("AcceptanceId", "WP09-EVT-001")]
    [Trait("AcceptanceId", "WP09-EVT-002")]
    public void WP09_EVT_001_ResolutionFixturesAreTypedCanonicalAndLocallyOrdered()
    {
        foreach (var name in ResolutionFixtures())
        {
            var bytes = Read(name);
            Assert.Equal(bytes, CanonicalJson.Canonicalize(bytes));
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var summary = FixtureCombatEventReader.ReadSummary(root.GetProperty("summary"));
            foreach (var item in root.GetProperty("events").EnumerateArray())
            {
                var draft = FixtureCombatEventReader.ReadDraft(item, summary);
                Assert.Equal(item.GetProperty("event_type").GetString(), draft.EventType.ToString());
                Assert.Equal(draft.EventType, draft.Payload.EventType);
            }
        }

        AssertOrder(ReadNode("resolution-basic-l1.engine-0.4.0.json"),
            "AttackHit", "DamageApplied", "StateChanged", "FighterDefeated", "BattleEnded");
        AssertOrder(ReadNode("resolution-wall-grab-l1.engine-0.4.0.json"),
            "GrabStarted", "AttackHit", "DamageApplied", "KnockbackApplied", "WallImpact",
            "DamageApplied", "GrabEnded", "FighterDefeated", "BattleEnded");
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-GRP-008")]
    [Trait("AcceptanceId", "WP09-DMG-009")]
    [Trait("AcceptanceId", "WP09-EVT-003")]
    [Trait("AcceptanceId", "WP09-EVT-004")]
    [Trait("AcceptanceId", "WP09-EVT-006")]
    [Trait("AcceptanceId", "WP09-EVT-008")]
    [Trait("AcceptanceId", "WP09-EVT-009")]
    public void WP09_EVT_003_SemanticTamperingOfFramesCausalityGroupsDamageAndSetsIsRejected()
    {
        AssertResolutionRejected(Mutate("resolution-basic-l1.engine-0.4.0.json", replay =>
        {
            var hit = Event(replay, "AttackHit");
            hit["after"]!["target"]!["health"] = 99;
        }));
        AssertRejected(Mutate("resolution-basic-l1.engine-0.4.0.json", replay =>
        {
            var damage = Event(replay, "DamageApplied");
            damage["source_event_id"] = "evt-0000000008";
            damage["payload"]!["related_event_ids"] = new JsonArray("evt-0000000008");
        }), ReplayVerificationCodes.CausalityInvalid);
        AssertResolutionRejected(Mutate("resolution-wall-grab-l1.engine-0.4.0.json", replay =>
        {
            Event(replay, "GrabEnded")["resolution_group_id"] = null;
        }));
        AssertResolutionRejected(Mutate("resolution-basic-l1.engine-0.4.0.json", replay =>
        {
            Event(replay, "DamageApplied")["payload"]!["breakdown"]!["final"] = 599;
        }));
        AssertResolutionRejected(Mutate("resolution-wall-grab-l1.engine-0.4.0.json", replay =>
        {
            Event(replay, "AttackHit")["payload"]!["attack_tags"] =
                new JsonArray("wall_impact", "grab");
        }));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-EVT-005")]
    [Trait("AcceptanceId", "WP09-EVT-007")]
    [Trait("AcceptanceId", "WP09-EVT-010")]
    public void WP09_EVT_005_RngVersionBoundaryAndFinisherForwardReferenceRulesAreEnforced()
    {
        var malformedRng = Mutate("resolution-basic-l1.engine-0.4.0.json", replay =>
        {
            Event(replay, "AttackHit")["rng"] = new JsonObject
            {
                ["index"] = "0",
                ["normalized_fp"] = 1_000,
                ["operation"] = "ChanceCheck",
                ["range_max_exclusive"] = 1_000,
                ["range_min_inclusive"] = 0,
                ["raw_u32"] = "0",
                ["result"] = 1_000,
                ["stream"] = "Resolution",
            };
        });
        AssertResolutionRejected(malformedRng);

        var invalid040 = Mutate("resolution-basic-l1.engine-0.4.0.json", replay =>
            Event(replay, "DamageApplied")["payload"]!["breakdown"]!["final"] = 599);
        AssertResolutionRejected(invalid040);

        var historicalBoundary = Mutate("resolution-basic-l1.engine-0.4.0.json", replay =>
        {
            replay["engine"]!["engine_version"] = "battle.core/0.3.99";
            foreach (var item in replay["events"]!.AsArray()) item!["engine_version"] = "battle.core/0.3.99";
            Event(replay, "DamageApplied")["payload"]!["breakdown"]!["final"] = 599;
        });
        var historicalResult = ReplayTestFixture.Verify(Serialize(historicalBoundary));
        Assert.DoesNotContain(historicalResult.Issues,
            issue => issue.Code == ReplayVerificationCodes.ResolutionInvalid);

        var source = File.ReadAllText(Path.Combine(
            RepositoryLocator.FindCombatLabRoot(), "src", "Battle.Replay", "Verification", "ReplaySemanticValidator.cs"));
        Assert.Contains("predictedIndex <= index", source);
        Assert.Contains("StringComparer.Ordinal.Equals(markerGroup, predictedGroup)", source);
        Assert.Contains("isLethalEvent", source);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-OUT-008")]
    public void WP09_OUT_008_TerminalEventIsLastAndExactlyMatchesSummary()
    {
        foreach (var name in ResolutionFixtures())
        {
            using var document = JsonDocument.Parse(Read(name));
            var root = document.RootElement;
            var events = root.GetProperty("events");
            var terminal = events[events.GetArrayLength() - 1];
            Assert.Equal("BattleEnded", terminal.GetProperty("event_type").GetString());
            var summary = root.GetProperty("summary");
            var payload = terminal.GetProperty("payload");
            foreach (var property in new[]
                     {
                         "duration_ticks", "end_reason", "end_tick", "final_frames", "outcome",
                         "pivotal_event_ids", "winner_fighter_id",
                     })
            {
                Assert.True(JsonElement.DeepEquals(summary.GetProperty(property), payload.GetProperty(property)),
                    "BattleEnded payload differs from summary at " + property);
            }
            Assert.Equal(events.GetArrayLength(), root.GetProperty("summary").GetProperty("event_count").GetInt32());
        }
    }

    private static JsonObject Mutate(string fixture, Action<JsonObject> mutation)
    {
        var replay = ReadNode(fixture);
        mutation(replay);
        Rehash(replay);
        return replay;
    }

    private static void AssertResolutionRejected(JsonObject replay) =>
        AssertRejected(replay, ReplayVerificationCodes.ResolutionInvalid);

    private static void AssertRejected(JsonObject replay, string code)
    {
        var result = ReplayTestFixture.Verify(Serialize(replay));
        Assert.False(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    private static void AssertOrder(JsonObject replay, params string[] expected)
    {
        var actual = replay["events"]!.AsArray()
            .Select(item => item!["event_type"]!.GetValue<string>()).ToArray();
        var cursor = -1;
        foreach (var type in expected)
        {
            cursor = Array.FindIndex(actual, cursor + 1, value => value == type);
            Assert.True(cursor >= 0, "Missing ordered event " + type);
        }
    }

    private static JsonObject Event(JsonObject replay, string type) => replay["events"]!.AsArray()
        .Select(item => item!.AsObject())
        .First(item => item["event_type"]!.GetValue<string>() == type);

    private static void Rehash(JsonObject replay)
    {
        var input = ReplayIntegrity.ComputeInputDigest(Serialize(replay)).ToString();
        replay["integrity"]!["input_digest"] = input;
        replay["events"]!.AsArray()[0]!["payload"]!["input_digest"] = input;
        var previous = input;
        var events = replay["events"]!.AsArray();
        foreach (var node in events)
        {
            var item = node!.AsObject();
            item["integrity"]!["prev_digest"] = previous;
            item["integrity"]!["event_digest"] = ZeroDigest;
            previous = ReplayIntegrity.ComputeEventDigest(Serialize(item)).ToString();
            item["integrity"]!["event_digest"] = previous;
        }
        replay["integrity"]!["final_digest"] = previous;
        replay["integrity"]!["event_count"] = events.Count;
    }

    private static IEnumerable<string> ResolutionFixtures()
    {
        yield return "resolution-basic-l1.engine-0.4.0.json";
        yield return "resolution-double-ko-l1.engine-0.4.0.json";
        yield return "resolution-wall-grab-l1.engine-0.4.0.json";
    }

    private static JsonObject ReadNode(string name) => JsonNode.Parse(Read(name))!.AsObject();
    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(
        RepositoryLocator.FindCombatLabRoot(), "fixtures", "replay", "v0.1", name));
    private static byte[] Serialize(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(
        new JsonSerializerOptions { WriteIndented = false }));
}
