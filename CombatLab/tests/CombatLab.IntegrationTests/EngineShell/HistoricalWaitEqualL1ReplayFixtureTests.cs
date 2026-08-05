using System.Security.Cryptography;
using System.Text.Json;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class HistoricalWaitEqualL1ReplayFixtureTests
{
    private const string ExpectedFileSha256 =
        "4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292";
    private const string ExpectedInputDigest =
        "sha256:0edc1dbc1d8d2a09c38debed5626fba5637f7304a38df258342d1d959edc8ba2";
    private const string ExpectedFinalDigest =
        "sha256:d06e3c2153a4fbfc495279cd6fcf7379d6f8d42c059e8756a5003d01acfa9ea6";

    [Fact]
    public void Engine010Fixture_IsImmutableAndReplayVerifiable()
    {
        var replay = File.ReadAllBytes(FixturePath());

        Assert.Equal(
            ExpectedFileSha256,
            Convert.ToHexString(SHA256.HashData(replay)).ToLowerInvariant());

        using var document = JsonDocument.Parse(replay);
        Assert.Equal(
            "battle.core/0.1.0",
            document.RootElement
                .GetProperty("engine")
                .GetProperty("engine_version")
                .GetString());
        Assert.Equal("standard", document.RootElement.GetProperty("profile").GetString());

        var verification = new ReplayVerifier(File.ReadAllBytes(SchemaPath())).Verify(replay);

        Assert.True(verification.IsValid, Describe(verification));
        Assert.False(verification.HasWarnings);
        Assert.Empty(verification.Issues);
        Assert.Equal(ExpectedInputDigest, verification.ComputedInputDigest?.ToString());
        Assert.Equal(ExpectedFinalDigest, verification.ComputedFinalDigest?.ToString());
        Assert.Equal(8, verification.EventCount);
    }

    private static string FixturePath() =>
        Path.Combine(
            RepositoryRoot(),
            "fixtures",
            "replay",
            "v0.1",
            "wait-equal-l1.engine-0.1.0.json");

    private static string SchemaPath() =>
        Path.Combine(
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

        throw new DirectoryNotFoundException(
            "Could not locate CombatLab.sln from the test output directory.");
    }

    private static string Describe(ReplayVerificationResult result) =>
        string.Join(
            Environment.NewLine,
            result.Issues.Select(
                issue =>
                    $"{issue.Severity} {issue.Layer} {issue.Code} {issue.Path}: {issue.Message}"));
}
