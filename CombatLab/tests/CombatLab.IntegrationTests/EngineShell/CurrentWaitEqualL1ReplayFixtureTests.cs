using System.Security.Cryptography;
using System.Text.Json;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class CurrentWaitEqualL1ReplayFixtureTests
{
    private const string ExpectedFileSha256 =
        "ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409";
    private const string ExpectedInputDigest =
        "sha256:89f3cf32381147cc18bd5f842060fb73d0730607068dcc72d7fccae8f183f8e2";
    private const string ExpectedFinalDigest =
        "sha256:95670ca45d0f1d9be0b72781871f23a1a44e6a7ed218306b42266c8ca3c6373b";

    [Fact]
    public void WP07_REG_003_Engine020WaitFixtureIsPinnedAndReplayVerifiable()
    {
        var replay = File.ReadAllBytes(FixturePath());

        Assert.Equal(
            ExpectedFileSha256,
            Convert.ToHexString(SHA256.HashData(replay)).ToLowerInvariant());
        using var document = JsonDocument.Parse(replay);
        Assert.Equal(
            "battle.core/0.2.0",
            document.RootElement.GetProperty("engine").GetProperty("engine_version").GetString());

        var verification = new ReplayVerifier(File.ReadAllBytes(SchemaPath())).Verify(replay);

        Assert.True(verification.IsValid, Describe(verification));
        Assert.Empty(verification.Issues);
        Assert.Equal(ExpectedInputDigest, verification.ComputedInputDigest?.ToString());
        Assert.Equal(ExpectedFinalDigest, verification.ComputedFinalDigest?.ToString());
        Assert.Equal(8, verification.EventCount);
    }

    private static string FixturePath() => Path.Combine(
        RepositoryRoot(),
        "fixtures",
        "replay",
        "v0.1",
        "wait-equal-l1.engine-0.2.0.json");

    private static string SchemaPath() => Path.Combine(
        RepositoryRoot(),
        "schemas",
        "replay",
        "v0.1",
        "combat-replay.schema.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CombatLab.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln.");
    }

    private static string Describe(ReplayVerificationResult result) => string.Join(
        Environment.NewLine,
        result.Issues.Select(issue => $"{issue.Severity} {issue.Layer} {issue.Code}: {issue.Message}"));
}
