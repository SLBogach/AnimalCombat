using System.Security.Cryptography;
using System.Text.Json;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Replay.Journal;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.Decisions;

public sealed class Wp08DecisionWeightedReplayFixtureTests
{
    private const string ExpectedFileSha256 =
        "1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0";
    private const string ExpectedInputDigest =
        "sha256:eaee293a90e5fc432ab1822965b3f632abc803bd79b23ae401a8fc9fd8a2b021";
    private const string ExpectedFinalDigest =
        "sha256:6ed4f34aa845096ee63d125d306fbef64ff469773e14389bfe1152146a007f3f";

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DecisionWeightedFixtureIsPinnedReplayVerifiableAndMatchesTheProbeOracle()
    {
        var replay = File.ReadAllBytes(FixturePath());

        Assert.Equal(
            ExpectedFileSha256,
            Convert.ToHexString(SHA256.HashData(replay)).ToLowerInvariant());
        using var document = JsonDocument.Parse(replay);
        Assert.Equal(
            "battle.core/0.3.0",
            document.RootElement.GetProperty("engine").GetProperty("engine_version").GetString());

        var verification = new ReplayVerifier(
            File.ReadAllBytes(DecisionEngineFixture.SchemaPath())).Verify(replay);

        Assert.True(verification.IsValid, Describe(verification));
        Assert.Empty(verification.Issues);
        Assert.Equal(ExpectedInputDigest, verification.ComputedInputDigest?.ToString());
        Assert.Equal(ExpectedFinalDigest, verification.ComputedFinalDigest?.ToString());
        Assert.Equal(9, verification.EventCount);

        var currentRun = DecisionEngineFixture.Run();
        var regenerated = CanonicalReplayArtifactWriter.Write(
            currentRun.Journal,
            new ReplayArtifactMetadata(
                new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero),
                new ExternalId("combat-lab-wp08-target-probe"),
                fixture: true,
                notes: "WP-08 decision_weighted_l1 target determinism probe"));

        Assert.Equal(replay, regenerated);
    }

    private static string FixturePath() => Path.Combine(
        DecisionEngineFixture.RepositoryRoot(),
        "fixtures",
        "replay",
        "v0.1",
        "decision-weighted-l1.engine-0.3.0.json");

    private static string Describe(ReplayVerificationResult result) => string.Join(
        Environment.NewLine,
        result.Issues.Select(issue =>
            $"{issue.Severity} {issue.Layer} {issue.Code}: {issue.Message}"));
}
