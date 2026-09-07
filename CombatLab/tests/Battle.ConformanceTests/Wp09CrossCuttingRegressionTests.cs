using System.Security.Cryptography;
using Battle.Contracts.Versions;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests;

[Trait("WorkPackage", "WP09")]
public sealed class Wp09CrossCuttingRegressionTests
{
    [Fact]
    [Trait("AcceptanceId", "WP09-CFG-007")]
    [Trait("AcceptanceId", "WP09-REG-004")]
    public void WP09_CFG_007_CanonicalBalanceAndGeneratedArtifactsRemainPinned()
    {
        Assert.Equal("0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f",
            Hash(PathInRoot("config", "generated", "combat.balance.v0.1.json")));
        Assert.Equal("38db30bd8572f325c6259bc110456944ed18ec42538d584953b6290205be9fae",
            Hash(PathInRoot("config", "generated", "combat.balance.v0.1.map.csv")));
        Assert.Equal("041ff6c70a2e1d9f0cd91b6edeae3879e72782a38f043730ce1ae191c6a74526",
            Hash(PathInRoot("config", "generated", "combat.balance.v0.1.validation.json")));
        Assert.Contains("Generated artifact is stale", Read("scripts", "verify-wp04-generated.ps1"));
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-CFG-008")]
    [Trait("AcceptanceId", "WP09-SCH-005")]
    public void WP09_CFG_008_RuntimeUsesMaterializedTypedProfilesAndSchedules()
    {
        var factory = Read("src", "Battle.Core", "Initialization", "BattleSetupFactory.cs");
        var descriptor = Read("src", "Battle.Core", "Engine", "CombatActionDescriptor.cs");
        var resolution = Read("src", "Battle.Core", "Resolution", "ResolutionSystem.cs");

        Assert.Contains("ResolutionSetupMaterializer.TryCreate", factory);
        Assert.Contains("ResolutionActionProfile ResolutionProfile", descriptor);
        Assert.Contains("IReadOnlyList<HitScheduleEntry>", descriptor);
        Assert.DoesNotContain("CompiledBattleConfig", resolution);
        Assert.DoesNotContain("TryGetProperty", resolution);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-REG-001")]
    [Trait("AcceptanceId", "WP09-REG-002")]
    public void WP09_REG_001_ProjectGraphAndContractVersionsMatchPlan()
    {
        Assert.Equal("battle.core/0.4.0", ContractVersions.Engine.ToString());
        Assert.Equal("combat.event/0.1", ContractVersions.Event.ToString());
        Assert.Equal("combat.replay/0.1", ContractVersions.Replay.ToString());
        Assert.Equal("combat.balance/0.1", ContractVersions.BalanceSchema.ToString());

        var coreProject = Read("src", "Battle.Core", "Battle.Core.csproj");
        Assert.Contains("Battle.Contracts", coreProject);
        Assert.DoesNotContain("Battle.Config", coreProject);
        Assert.DoesNotContain("Battle.Replay", coreProject);
        Assert.DoesNotContain("UnityClient", coreProject);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-REG-003")]
    public void WP09_REG_003_HistoricalReplayFixturesRemainBytePinnedAndVerifiable()
    {
        AssertReplay("wait-equal-l1.engine-0.1.0.json",
            "4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292", 8);
        AssertReplay("wait-equal-l1.engine-0.2.0.json",
            "ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409", 8);
        AssertReplay("approach-band-l3.engine-0.2.0.json",
            "7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873", 18);
        AssertReplay("wait-equal-l1.engine-0.3.0.json",
            "8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd", 8);
        AssertReplay("decision-weighted-l1.engine-0.3.0.json",
            "1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0", 9);
    }

    [Fact]
    public void CurrentEngine040FixturesArePinnedAndReplayVerifiable()
    {
        AssertReplay("wait-equal-l1.engine-0.4.0.json",
            "732b442056083d6dc2a3d2439219116199ec707bd5854ce4c365629cdfcd6c08", 8);
        AssertReplay("decision-weighted-l1.engine-0.4.0.json",
            "ffbed61a784e72b20f43dc6b3d5f89c4954d2e65714ab747dc6c42c6e76d815b", 9);
        AssertReplay("resolution-basic-l1.engine-0.4.0.json",
            "c56685b7b9fae47abd1b0cb503b9d2b46cfda4a890c73cc82226810626f70c99", 11);
        AssertReplay("resolution-double-ko-l1.engine-0.4.0.json",
            "1bee7887f1603c0f95e2f48950a54549ff17dd34edb67dd85d51415f39bb188f", 17);
        AssertReplay("resolution-wall-grab-l1.engine-0.4.0.json",
            "25ee1cbe58c0fa1df40076ca69c79ba2dde0e00b427d4c4b7017a8a65e28843c", 18);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-DET-005")]
    [Trait("AcceptanceId", "WP09-DET-006")]
    [Trait("AcceptanceId", "WP09-DET-007")]
    public void WP09_DET_005_TargetAndCiGatesCoverFrameworkConfigurationAndOperatingSystemParity()
    {
        var gate = Read("scripts", "verify-wp09-target-determinism.ps1");
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), "..", ".github", "workflows", "combatlab.yml"));
        Assert.Contains("netstandard2.1", gate);
        Assert.Contains("net10.0", gate);
        Assert.Contains("resolution-basic-l1.engine-0.4.0.json", gate);
        Assert.Contains("resolution-double-ko-l1.engine-0.4.0.json", gate);
        Assert.Contains("resolution-wall-grab-l1.engine-0.4.0.json", gate);
        Assert.Contains("ubuntu-latest", workflow);
        Assert.Contains("windows-latest", workflow);
        Assert.Contains("- Debug", workflow);
        Assert.Contains("- Release", workflow);
        Assert.Contains("Verify WP-09 target determinism", workflow);
    }

    [Fact]
    [Trait("AcceptanceId", "WP09-REG-006")]
    public void WP09_REG_006_CoverageGatePinsCriticalResolutionCodeAndUnityIsOutsideWorkflowScope()
    {
        var gate = Read("scripts", "verify-wp09-coverage.ps1");
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot(), "..", ".github", "workflows", "combatlab.yml"));
        Assert.Contains("Battle.Core.Resolution.ResolutionMath", gate);
        Assert.Contains("Battle.Core.Resolution.ResolutionSystem", gate);
        Assert.Contains("Battle.Replay.Verification.ResolutionReplaySemanticValidator", gate);
        Assert.Contains("0.85", gate);
        Assert.Contains("Enforce WP-09 coverage", workflow);
        Assert.DoesNotContain("UnityClient/**", workflow);
    }

    private static void AssertReplay(string name, string sha256, int eventCount)
    {
        var path = PathInRoot("fixtures", "replay", "v0.1", name);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(sha256, Hash(bytes));
        var result = new ReplayVerifier(File.ReadAllBytes(
            PathInRoot("schemas", "replay", "v0.1", "combat-replay.schema.json"))).Verify(bytes);
        Assert.True(result.IsValid, string.Join(Environment.NewLine,
            result.Issues.Select(issue => issue.Code + ": " + issue.Message)));
        Assert.Equal(eventCount, result.EventCount);
    }

    private static string Read(params string[] segments) => File.ReadAllText(PathInRoot(segments));
    private static string PathInRoot(params string[] segments) => segments.Aggregate(RepositoryRoot(), Path.Combine);
    private static string Hash(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string RepositoryRoot() => RepositoryLocator.FindCombatLabRoot();
}
