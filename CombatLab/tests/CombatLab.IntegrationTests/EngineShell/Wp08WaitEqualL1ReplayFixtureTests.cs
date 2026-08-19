using System.Security.Cryptography;
using System.Text.Json;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class Wp08WaitEqualL1ReplayFixtureTests
{
    private const string ExpectedFileSha256 =
        "8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd";
    private const string ExpectedInputDigest =
        "sha256:4155833aa33fd60fee5f034dc8f4050afb957682af5141701d6dca463bbc7a08";
    private const string ExpectedFinalDigest =
        "sha256:bcc34972a33aadd5da02f3c5d3996ecd76c0037fbfe5e94e25cdf883ca9177f9";

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void Engine030WaitFixtureIsPinnedAndReplayVerifiable()
    {
        var replay = File.ReadAllBytes(FixturePath());

        Assert.Equal(
            ExpectedFileSha256,
            Convert.ToHexString(SHA256.HashData(replay)).ToLowerInvariant());
        using var document = JsonDocument.Parse(replay);
        Assert.Equal(
            "battle.core/0.3.0",
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
        "wait-equal-l1.engine-0.3.0.json");

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
        result.Issues.Select(issue =>
            $"{issue.Severity} {issue.Layer} {issue.Code}: {issue.Message}"));
}
