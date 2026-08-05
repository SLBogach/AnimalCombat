using System.Text.Json;
using Battle.Contracts.Ids;
using Battle.Replay.CanonicalJson;
using Battle.Replay.Journal;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.EngineShell;

public sealed class WaitEqualL1ReplayArtifactTests
{
    private static readonly ReplayArtifactMetadata Metadata = new(
        new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
        new ExternalId("combat-lab-integration-tests"),
        fixture: true,
        notes: "WP-06 wait_equal_l1 acceptance fixture");

    [Fact]
    public void StandardReplayArtifact_PassesSchemaSemanticAndIntegrityVerification()
    {
        var run = EngineShellFixture.RunCanonical();

        var replay = CanonicalReplayArtifactWriter.Write(run.Journal, Metadata);
        var verification = new ReplayVerifier(File.ReadAllBytes(SchemaPath())).Verify(replay);

        Assert.True(verification.IsValid, Describe(verification));
        Assert.False(verification.HasWarnings);
        Assert.Empty(verification.Issues);
        Assert.Equal(run.Journal.InputDigest, verification.ComputedInputDigest);
        Assert.Equal(run.Journal.FinalDigest, verification.ComputedFinalDigest);
        Assert.Equal(run.Journal.Events.Count, verification.EventCount);
        Assert.Equal(replay, CanonicalJson.Canonicalize(replay));
        Assert.Equal(
            replay,
            CanonicalReplayArtifactWriter.Write(
                EngineShellFixture.RunCanonical().Journal,
                Metadata));

        using var document = JsonDocument.Parse(replay);
        var root = document.RootElement;
        Assert.Equal("standard", root.GetProperty("profile").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("diagnostics").ValueKind);
        Assert.Equal(2, root.GetProperty("keyframes").GetArrayLength());
        Assert.Equal(0, root.GetProperty("keyframes")[0].GetProperty("after_sequence").GetInt32());
        Assert.Equal(
            run.Journal.Events.Count - 1,
            root.GetProperty("keyframes")[1].GetProperty("after_sequence").GetInt32());
        Assert.Equal(
            "2026-07-29T12:00:00.0000000Z",
            root.GetProperty("metadata").GetProperty("created_at_utc").GetString());
    }

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
