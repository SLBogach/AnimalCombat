using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.ConformanceTests.Replay;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10GoldenManifestTests
{
    [Fact]
    public void AllNineReplayAndConfigFilesMatchManifestHashesAndCanonicalDigests()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var directory = Path.Combine(root, "fixtures/replay/v0.1");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "wp10.engine-0.5.0.manifest.json")));
        var scenarios = manifest.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.Equal(9, scenarios.Length);
        Assert.Equal(9, scenarios.Select(x => x.GetProperty("scenario").GetString()).Distinct(StringComparer.Ordinal).Count());
        foreach (var entry in scenarios)
        {
            var bytes = File.ReadAllBytes(Path.Combine(directory, entry.GetProperty("replay").GetString()!));
            var configBytes = File.ReadAllBytes(Path.Combine(directory, entry.GetProperty("config").GetString()!));
            Assert.Equal(entry.GetProperty("file_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.Equal(entry.GetProperty("config_sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(configBytes)));
            var compilation = new BattleConfigCompiler().Compile(configBytes);
            Assert.True(compilation.IsSuccess); Assert.Equal(configBytes, compilation.GetCanonicalJson());
            using var replay = JsonDocument.Parse(bytes); var document = replay.RootElement;
            Assert.Equal("battle.core/0.5.0", document.GetProperty("engine").GetProperty("engine_version").GetString());
            Assert.Equal(compilation.Config!.Reference.ConfigHash.Value, document.GetProperty("config").GetProperty("config_hash").GetString());
            var integrity = document.GetProperty("integrity");
            Assert.Equal(entry.GetProperty("input_digest").GetString(), integrity.GetProperty("input_digest").GetString());
            Assert.Equal(entry.GetProperty("final_digest").GetString(), integrity.GetProperty("final_digest").GetString());
            Assert.Equal(entry.GetProperty("event_count").GetInt32(), document.GetProperty("events").GetArrayLength());
            Assert.Equal(entry.GetProperty("event_count").GetInt32(), integrity.GetProperty("event_count").GetInt32());
            var verified = ReplayTestFixture.Verify(bytes);
            Assert.True(verified.IsValid, ReplayTestFixture.Describe(verified)); Assert.False(verified.HasWarnings);
        }
    }

    [Theory]
    [InlineData("reopen")]
    [InlineData("cross-tick")]
    public void EffectCausalGroupExceptionDoesNotPermitRealImpactGroupsToReopenOrCrossTicks(string mutation)
    {
        var replay = JsonNode.Parse(ReplayTestFixture.ReadReplay("effects-impact-snapshot-l1.engine-0.5.0.json"))!.AsObject();
        var events = replay["events"]!.AsArray();
        var originalGroup = events.First(x => x!["event_type"]!.GetValue<string>() == "AttackHit")!["resolution_group_id"]!.GetValue<string>();
        var impacts = events.Where(x => x!["tick"]!.GetValue<int>() == 1 && x["event_type"]!.GetValue<string>() == "AttackHit").ToArray();
        impacts[mutation == "reopen" ? 1 : 0]!["resolution_group_id"] = originalGroup;
        Wp10EffectReplayTests.Rehash(replay);
        var verified = ReplayTestFixture.Verify(Wp10EffectReplayTests.Serialize(replay));
        Assert.False(verified.IsValid);
        Assert.DoesNotContain(verified.Issues, x => x.Layer == ReplayVerificationLayer.Integrity && x.Severity == ReplayVerificationSeverity.Error);
        Assert.Contains(verified.Issues, x => x.Message.Contains(mutation == "reopen" ? "cannot reopen" : "remain on one tick", StringComparison.Ordinal));
    }
}
