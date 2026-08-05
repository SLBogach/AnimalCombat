using System.Security.Cryptography;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.Movement;

public sealed class PinnedMovementReplayFixtureTests
{
    private const string ExpectedFileSha256 =
        "7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873";
    private const string ExpectedInputDigest =
        "sha256:dae170bccf84b44e6c0c173692e6198c45ec0e0ae1484bf9c7dd989cad4a0b20";
    private const string ExpectedFinalDigest =
        "sha256:956b15fd915222f8b404823dfab070c6bc2f6e1852309d1ef12dc988954cfe93";

    [Fact]
    public void WP07_CON_007_ApproachBandFixtureIsPinnedAndFullyVerifiable()
    {
        var replay = File.ReadAllBytes(FixturePath());

        Assert.Equal(
            ExpectedFileSha256,
            Convert.ToHexString(SHA256.HashData(replay)).ToLowerInvariant());
        var verification = new ReplayVerifier(
            File.ReadAllBytes(MovementEngineFixture.SchemaPath())).Verify(replay);

        Assert.True(
            verification.IsValid,
            string.Join(Environment.NewLine, verification.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Empty(verification.Issues);
        Assert.Equal(ExpectedInputDigest, verification.ComputedInputDigest?.ToString());
        Assert.Equal(ExpectedFinalDigest, verification.ComputedFinalDigest?.ToString());
        Assert.Equal(18, verification.EventCount);
    }

    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solution = Path.Combine(directory.FullName, "CombatLab.sln");
            if (File.Exists(solution))
            {
                return Path.Combine(
                    directory.FullName,
                    "fixtures",
                    "replay",
                    "v0.1",
                    "approach-band-l3.engine-0.2.0.json");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln.");
    }
}
