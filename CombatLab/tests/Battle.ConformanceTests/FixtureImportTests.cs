using System.Security.Cryptography;
using System.Text.Json;

namespace Battle.ConformanceTests;

public sealed class FixtureImportTests
{
    private const string ExpectedInputDigest =
        "sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b";

    private const string ExpectedFinalDigest =
        "sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e";

    public static TheoryData<string, string> ImportedFiles => new()
    {
        {
            "schemas/combat-event.schema.json",
            "schemas/replay/v0.1/combat-event.schema.json"
        },
        {
            "schemas/combat-presentation.schema.json",
            "schemas/replay/v0.1/combat-presentation.schema.json"
        },
        {
            "schemas/combat-rejection.schema.json",
            "schemas/replay/v0.1/combat-rejection.schema.json"
        },
        {
            "schemas/combat-replay.schema.json",
            "schemas/replay/v0.1/combat-replay.schema.json"
        },
        {
            "examples/battle-rejected.example.json",
            "fixtures/replay/v0.1/battle-rejected.example.json"
        },
        {
            "examples/presentation-timeline.example.json",
            "fixtures/replay/v0.1/presentation-timeline.example.json"
        },
        {
            "examples/replay-diagnostic.example.json",
            "fixtures/replay/v0.1/replay-diagnostic.example.json"
        },
        {
            "examples/replay-standard.example.json",
            "fixtures/replay/v0.1/replay-standard.example.json"
        },
    };

    [Theory]
    [MemberData(nameof(ImportedFiles))]
    public void VersionedFixture_IsByteForByteCopy(string sourceRelativePath, string targetRelativePath)
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var source = File.ReadAllBytes(Resolve(root, sourceRelativePath));
        var target = File.ReadAllBytes(Resolve(root, targetRelativePath));

        Assert.True(
            source.AsSpan().SequenceEqual(target),
            $"{targetRelativePath} differs from {sourceRelativePath}. " +
            $"source={Hash(source)}, target={Hash(target)}");
    }

    [Fact]
    public void SourceReplayPackage_MatchesItsManifest()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));

        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relativePath = entry.GetProperty("path").GetString();
            Assert.False(string.IsNullOrWhiteSpace(relativePath));

            var bytes = File.ReadAllBytes(Resolve(root, relativePath!));
            Assert.Equal(entry.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Hash(bytes));
        }
    }

    [Fact]
    public void StandardAndDiagnosticFixtures_PreserveCanonicalChain()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        using var standard = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(root, "fixtures", "replay", "v0.1", "replay-standard.example.json")));
        using var diagnostic = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(root, "fixtures", "replay", "v0.1", "replay-diagnostic.example.json")));

        var standardRoot = standard.RootElement;
        var diagnosticRoot = diagnostic.RootElement;
        var standardIntegrity = standardRoot.GetProperty("integrity");
        var diagnosticIntegrity = diagnosticRoot.GetProperty("integrity");
        var standardEvents = standardRoot.GetProperty("events");
        var diagnosticEvents = diagnosticRoot.GetProperty("events");

        Assert.Equal(ExpectedInputDigest, standardIntegrity.GetProperty("input_digest").GetString());
        Assert.Equal(ExpectedFinalDigest, standardIntegrity.GetProperty("final_digest").GetString());
        Assert.Equal(13, standardIntegrity.GetProperty("event_count").GetInt32());
        Assert.Equal(13, standardEvents.GetArrayLength());
        Assert.Equal("BattleStarted", standardEvents[0].GetProperty("event_type").GetString());
        Assert.Equal("BattleEnded", standardEvents[12].GetProperty("event_type").GetString());

        Assert.True(JsonElement.DeepEquals(standardRoot.GetProperty("input"), diagnosticRoot.GetProperty("input")));
        Assert.True(JsonElement.DeepEquals(standardRoot.GetProperty("summary"), diagnosticRoot.GetProperty("summary")));
        Assert.True(JsonElement.DeepEquals(standardRoot.GetProperty("keyframes"), diagnosticRoot.GetProperty("keyframes")));
        Assert.True(JsonElement.DeepEquals(standardEvents, diagnosticEvents));
        Assert.True(JsonElement.DeepEquals(standardIntegrity, diagnosticIntegrity));
    }

    private static string Resolve(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Hash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
