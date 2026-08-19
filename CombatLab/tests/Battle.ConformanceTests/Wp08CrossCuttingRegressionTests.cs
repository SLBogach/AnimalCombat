using System.Security.Cryptography;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests;

public sealed class Wp08CrossCuttingRegressionTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_004_TargetParityGateComparesBothSharedFrameworkOutputsByteForByte()
    {
        var script = Read("scripts", "verify-wp08-target-determinism.ps1");

        Assert.Contains("@(\"netstandard2.1\", \"net10.0\")", script);
        Assert.Contains("Test-ByteArrayEqual", script);
        Assert.Contains("$results[$scenario.Name][\"netstandard2.1\"]", script);
        Assert.Contains("$results[$scenario.Name][\"net10.0\"]", script);
        Assert.Contains("decision-weighted-l1.engine-0.3.0.json", script);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_DET_005_CiMatrixPinsDebugReleaseWindowsLinuxAndReplayParityInEveryJob()
    {
        var workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "..", ".github", "workflows", "combatlab.yml"));

        Assert.Contains("ubuntu-latest", workflow);
        Assert.Contains("windows-latest", workflow);
        Assert.Contains("- Debug", workflow);
        Assert.Contains("- Release", workflow);
        Assert.Contains("Verify WP-08 target determinism", workflow);
        Assert.Contains("verify-wp08-target-determinism.ps1", workflow);

        var targetStepStart = workflow.IndexOf(
            "- name: Verify WP-08 target determinism",
            StringComparison.Ordinal);
        var nextStepStart = workflow.IndexOf(
            "- name: Verify WP-04 generated balance artifacts",
            targetStepStart,
            StringComparison.Ordinal);
        Assert.True(targetStepStart >= 0 && nextStepStart > targetStepStart);
        var targetStep = workflow[targetStepStart..nextStepStart];
        Assert.DoesNotContain("if:", targetStep, StringComparison.Ordinal);
        Assert.Contains("${{ matrix.configuration }}", targetStep, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_REG_001_Wp02AndWp03GoldenVectorAndCoverageGatesRemainWired()
    {
        var fixedMathTests = Read(
            "tests", "Battle.Core.UnitTests", "Math", "FixedMathTests.cs");
        var rngTests = Read(
            "tests", "Battle.Core.UnitTests", "Random", "GameplayRngTests.cs");
        var wp02Gate = Read("scripts", "verify-wp02-coverage.ps1");
        var wp03Gate = Read("scripts", "verify-wp03-coverage.ps1");

        Assert.Contains("Mul_MatchesCanonicalGoldenVectors", fixedMathTests);
        Assert.Contains("Div_MatchesCanonicalGoldenVectors", fixedMathTests);
        Assert.Contains("2_879_411_843U", rngTests);
        Assert.Contains("495_049_527U", rngTests);
        Assert.Contains("Streams_MatchGoldenRawSequencesAndLogicalIndexes", rngTests);
        Assert.Contains("Battle.Core.Math.FixedMath", wp02Gate);
        Assert.Contains("$branchRate -ne 1", wp02Gate);
        Assert.Contains("Battle.Core.Random.Pcg32Stream", wp03Gate);
        Assert.Contains("$branchRate -ne 1", wp03Gate);
        Assert.Contains("$lineRate -ne 1", wp03Gate);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_REG_002_CanonicalGeneratedConfigAndReproducibilityGateRemainPinned()
    {
        Assert.Equal(
            "0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f",
            Hash(PathInRoot("config", "generated", "combat.balance.v0.1.json")));
        Assert.Equal(
            "38db30bd8572f325c6259bc110456944ed18ec42538d584953b6290205be9fae",
            Hash(PathInRoot("config", "generated", "combat.balance.v0.1.map.csv")));
        Assert.Equal(
            "041ff6c70a2e1d9f0cd91b6edeae3879e72782a38f043730ce1ae191c6a74526",
            Hash(PathInRoot(
                "config",
                "generated",
                "combat.balance.v0.1.validation.json")));

        var gate = Read("scripts", "verify-wp04-generated.ps1");
        Assert.Contains("combat.balance.v0.1.json", gate);
        Assert.Contains("combat.balance.v0.1.map.csv", gate);
        Assert.Contains("combat.balance.v0.1.validation.json", gate);
        Assert.Contains("combat.balance.v0.1.manifest.json", gate);
        Assert.Contains("Generated artifact is stale", gate);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_REG_003_Wp05ReplayPackageFixturesRemainBytePinnedAndVerifiable()
    {
        AssertPinnedReplay(
            "replay-standard.example.json",
            "a335bc21640cdd12782efbccbfadf27969dbe15d58d240b0dcb9649c647e8f25",
            expectedEvents: 13);
        AssertPinnedReplay(
            "replay-diagnostic.example.json",
            "e36cbb5bbee509bdd1004e2a43d50df3125fca97fa6366ed77feda13c5831d67",
            expectedEvents: 13);
        Assert.Equal(
            "8e4e43ccfaa929425e2056d957243faca402ff497ea4fa2cc6629302b1a57b5b",
            Hash(FixturePath("battle-rejected.example.json")));
        Assert.Equal(
            "48299058686f6c8d002cfa3a9c21630746b2e1d6379e60d68a8788df856c08ab",
            Hash(FixturePath("presentation-timeline.example.json")));
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_REG_004_HistoricalWaitFixturesAreImmutableAndCurrentWaitIsSeparatelyVersioned()
    {
        AssertPinnedReplay(
            "wait-equal-l1.engine-0.1.0.json",
            "4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292",
            expectedEvents: 8);
        AssertPinnedReplay(
            "wait-equal-l1.engine-0.2.0.json",
            "ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409",
            expectedEvents: 8);

        var gate = Read("scripts", "verify-wp08-target-determinism.ps1");
        Assert.Contains("wait-equal-l1.engine-0.1.0.json", gate);
        Assert.Contains("wait-equal-l1.engine-0.2.0.json", gate);
        Assert.Contains("wait-equal-l1.engine-0.3.0.json", gate);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_REG_005_HistoricalMovementGoldenIsImmutableAndSemanticallyVerifiable()
    {
        AssertPinnedReplay(
            "approach-band-l3.engine-0.2.0.json",
            "7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873",
            expectedEvents: 18);

        var semanticTests = Read(
            "tests", "CombatLab.IntegrationTests", "Movement", "MovementGoldenTests.cs");
        Assert.Contains("ApproachBandL3", semanticTests);
        Assert.Contains("RetreatBandL3", semanticTests);
        Assert.Contains("RetreatWallL3", semanticTests);
    }

    private static void AssertPinnedReplay(
        string fileName,
        string expectedSha256,
        int expectedEvents)
    {
        var bytes = File.ReadAllBytes(FixturePath(fileName));
        Assert.Equal(expectedSha256, Hash(bytes));

        var verification = new ReplayVerifier(
            File.ReadAllBytes(PathInRoot("schemas", "replay", "v0.1", "combat-replay.schema.json")))
            .Verify(bytes);
        Assert.True(
            verification.IsValid,
            string.Join(Environment.NewLine, verification.Issues.Select(
                issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(expectedEvents, verification.EventCount);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(PathInRoot(segments));

    private static string FixturePath(string fileName) =>
        PathInRoot("fixtures", "replay", "v0.1", fileName);

    private static string PathInRoot(params string[] segments) =>
        segments.Aggregate(RepositoryRoot(), Path.Combine);

    private static string Hash(string path) => Hash(File.ReadAllBytes(path));

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string RepositoryRoot() => RepositoryLocator.FindCombatLabRoot();
}
