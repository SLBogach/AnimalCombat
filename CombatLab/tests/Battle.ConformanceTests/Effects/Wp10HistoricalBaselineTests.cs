using System.Security.Cryptography;
using System.Text.Json;
using Battle.Config.Schema;
using Battle.Replay.Verification;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10HistoricalBaselineTests
{
    [Fact]
    [Trait("AcceptanceId", "WP10-REG-001")]
    public void Engine010Through040AreHistoricalBytePinnedAndReplayVerifiableBeforeBump()
    {
        var cases = new[]
        {
            ("wait-equal-l1.engine-0.1.0.json", "4d35559d0cd879c627328b490cb7bd99e946ef45ceb537bac1c753c8e517f292", 8),
            ("wait-equal-l1.engine-0.2.0.json", "ee56e6186506b3b962c52d6f0ca3f6a22597b94b362226e7252a9f53938f2409", 8),
            ("approach-band-l3.engine-0.2.0.json", "7117b582cab17a110fd10b2c08caae923c764b036018b1a4a18ec7d5d26c4873", 18),
            ("wait-equal-l1.engine-0.3.0.json", "8793101a52a2d261ba29e03453bff97298c8cefb16f81e76a76fb357ad684bdd", 8),
            ("decision-weighted-l1.engine-0.3.0.json", "1e2ea3f87bab119b1db687556d7835b2791089b095d202285c7e7f037e331eb0", 9),
            ("wait-equal-l1.engine-0.4.0.json", "732b442056083d6dc2a3d2439219116199ec707bd5854ce4c365629cdfcd6c08", 8),
            ("decision-weighted-l1.engine-0.4.0.json", "ffbed61a784e72b20f43dc6b3d5f89c4954d2e65714ab747dc6c42c6e76d815b", 9),
            ("resolution-basic-l1.engine-0.4.0.json", "c56685b7b9fae47abd1b0cb503b9d2b46cfda4a890c73cc82226810626f70c99", 11),
            ("resolution-double-ko-l1.engine-0.4.0.json", "1bee7887f1603c0f95e2f48950a54549ff17dd34edb67dd85d51415f39bb188f", 17),
            ("resolution-wall-grab-l1.engine-0.4.0.json", "25ee1cbe58c0fa1df40076ca69c79ba2dde0e00b427d4c4b7017a8a65e28843c", 18),
        };
        var verifier = new ReplayVerifier(Read("schemas/replay/v0.1/combat-replay.schema.json"));
        foreach (var (name, hash, count) in cases)
        {
            var bytes = Read("fixtures/replay/v0.1/" + name);
            Assert.Equal(hash, Hash(bytes));
            var verification = verifier.Verify(bytes);
            Assert.True(verification.IsValid, name + ": " + string.Join(Environment.NewLine,
                verification.Issues.Select(x => x.Code + " " + x.Message)));
            Assert.Equal(count, verification.EventCount);
        }
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-REG-002")]
    public void V01SourceSchemaGeneratedAndMachinePackageRemainBytePinned()
    {
        var pins = new[]
        {
            ("config/source/Combat_Balance_Workbook_v0.1.xlsx", "bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2"),
            ("config/generated/combat.balance.v0.1.json", "0e7ef9d85f4062308799c0da6969cefc2ab2239b1b0f8ff4534447f66e37976f"),
            ("config/generated/combat.balance.v0.1.map.csv", "38db30bd8572f325c6259bc110456944ed18ec42538d584953b6290205be9fae"),
            ("config/generated/combat.balance.v0.1.manifest.json", "444f57998473cf165cdc14b8ac6c323d4f0b2c28e6f275a7e4854978693b891f"),
            ("config/generated/combat.balance.v0.1.validation.json", "041ff6c70a2e1d9f0cd91b6edeae3879e72782a38f043730ce1ae191c6a74526"),
            // Pin the existing LF Git blob, not a Windows CRLF checkout of it.
            ("schemas/balance/v0.1/combat.balance.schema.json", "fd7c3c1d5b52807126e260dd71150a36ef2e68fb18c3dc332dad5c1e17eb40f0"),
        };
        foreach (var (path, hash) in pins) Assert.Equal(hash, Hash(Read(path)));
        Assert.Equal(Read("schemas/balance/v0.1/combat.balance.schema.json"), BalanceSchemaJson.Write());
        using var manifest = JsonDocument.Parse(Read("manifest.json"));
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var bytes = Read(entry.GetProperty("path").GetString()!);
            Assert.Equal(entry.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Hash(bytes));
        }
    }

    private static byte[] Read(string relativePath) => File.ReadAllBytes(Path.Combine(
        RepositoryLocator.FindCombatLabRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
