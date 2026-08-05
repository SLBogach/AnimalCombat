using System.Text;
using System.Text.Json;
using Canonicalizer = Battle.Replay.CanonicalJson.CanonicalJson;
using Battle.Replay.Integrity;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Replay;

public sealed class ReplayVerifierTests
{
    private const string ExpectedInputDigest =
        "sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b";

    private const string ExpectedFinalDigest =
        "sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e";

    [Theory]
    [InlineData("replay-standard.example.json")]
    [InlineData("replay-diagnostic.example.json")]
    public void MachineReplayFixture_PassesAllVerificationLayers(string fixtureName)
    {
        var result = ReplayTestFixture.Verify(ReplayTestFixture.ReadReplay(fixtureName));

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.False(result.HasWarnings);
        Assert.Empty(result.Issues);
        Assert.Equal(ExpectedInputDigest, result.ComputedInputDigest?.ToString());
        Assert.Equal(ExpectedFinalDigest, result.ComputedFinalDigest?.ToString());
        Assert.Equal(13, result.EventCount);
    }

    [Fact]
    public void MachineKeyframes_MatchTheirExactStateDigestVectors()
    {
        using var document = JsonDocument.Parse(
            ReplayTestFixture.ReadReplay("replay-standard.example.json"));
        var keyframes = document.RootElement.GetProperty("keyframes");

        Assert.Equal(
            "sha256:a5b2642d36dbd5514893cfe3e55b705196a052aa7dc8b24da9dd42255c2639b9",
            ComputeKeyframeDigest(keyframes[0]));
        Assert.Equal(
            "sha256:4e64855179860635d72aab284128657a6ad6364d1ecc84f721d58bb18c0cbeef",
            ComputeKeyframeDigest(keyframes[1]));
    }

    [Fact]
    public void MachineReplay_CanonicalRoundTripPreservesVerificationResult()
    {
        var source = ReplayTestFixture.ReadReplay("replay-standard.example.json");
        var canonical = Canonicalizer.Canonicalize(source);

        var result = ReplayTestFixture.Verify(canonical);

        Assert.True(result.IsValid, ReplayTestFixture.Describe(result));
        Assert.Equal(ExpectedInputDigest, result.ComputedInputDigest?.ToString());
        Assert.Equal(ExpectedFinalDigest, result.ComputedFinalDigest?.ToString());
        Assert.DoesNotContain((byte)'\n', canonical);
        Assert.True(canonical.AsSpan().SequenceEqual(Canonicalizer.Canonicalize(canonical)));
    }

    private static string ComputeKeyframeDigest(JsonElement keyframe) =>
        ReplayIntegrity.ComputeKeyframeStateDigest(
            Encoding.UTF8.GetBytes(keyframe.GetRawText())).ToString();
}

internal static class ReplayTestFixture
{
    private static readonly string Root =
        global::Battle.ConformanceTests.RepositoryLocator.FindCombatLabRoot();

    public static byte[] ReadReplay(string fixtureName) =>
        File.ReadAllBytes(
            Path.Combine(Root, "fixtures", "replay", "v0.1", fixtureName));

    public static ReplayVerificationResult Verify(ReadOnlyMemory<byte> replayBytes)
    {
        var schemaBytes = File.ReadAllBytes(
            Path.Combine(Root, "schemas", "replay", "v0.1", "combat-replay.schema.json"));
        return new ReplayVerifier(schemaBytes).Verify(replayBytes);
    }

    public static string Describe(ReplayVerificationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Issues.Select(
                issue =>
                    $"{issue.Severity} {issue.Layer} {issue.Code} {issue.Path}: {issue.Message}"));
}
