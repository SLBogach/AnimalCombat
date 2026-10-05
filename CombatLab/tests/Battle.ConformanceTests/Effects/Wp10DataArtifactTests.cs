using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Battle.Config;
using Battle.Config.Compiler;
using Battle.Config.Schema;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10DataArtifactTests
{
    [Fact]
    [Trait("AcceptanceId", "WP10-DATA-003")]
    public void PhysicalSourceMapHasAllApprovedDataAndCompiledCatalogAgrees()
    {
        var lines = Encoding.UTF8.GetString(Read("config/generated/combat.balance.v0.2.map.csv")).Split('\n');
        var added = lines.Skip(2097).Where(x => x.Length != 0).Select(x => x.Split(',')).ToArray();
        Assert.Equal(260, added.Length);
        Assert.Equal(36, added.Count(x => x[1] == "global"));
        Assert.Equal(120, added.Count(x => x[1] == "action"));
        Assert.Equal(9, added.Count(x => x[1] == "gear"));
        Assert.Equal(30, added.Count(x => x[1] == "effect"));
        Assert.Equal(65, added.Count(x => x[1] == "effect_rule"));
        using var archive = ZipFile.OpenRead(Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.2.xlsx"));
        var s = (XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var mapStream = archive.GetEntry("xl/worksheets/sheet13.xml")!.Open();
        var mapCells = XDocument.Load(mapStream).Descendants(s + "c").ToDictionary(x => x.Attribute("r")!.Value);
        foreach (var item in added)
        {
            Assert.Equal("1", item[8]);
            Assert.Equal("1", item[9]);
            var row = int.Parse(item[0], System.Globalization.CultureInfo.InvariantCulture) + 5;
            Assert.Equal("'" + item[6] + "'!" + item[7], mapCells["F" + row].Element(s + "f")!.Value);
            Assert.StartsWith("'" + item[6] + "'!", mapCells["L" + row].Element(s + "f")!.Value, StringComparison.Ordinal);
        }
        var loaded = new BattleConfigLoader().Load(Read("config/generated/combat.balance.v0.2.json"), Read("config/generated/combat.balance.v0.2.manifest.json"));
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Issues.Select(x => x.Message)));
        Assert.Empty(loaded.Issues);
        Assert.Equal(5, loaded.Config!.EffectRules.Count);
        Assert.Equal("combat.balance/0.2", loaded.Config.Reference.BalanceSchemaVersion.ToString());
        foreach (var bound in Wp10ConfigFixture.Bounds)
        {
            Assert.True(loaded.Config.TryGetSetting("stat." + bound.Stat + ".min", out var minimum));
            Assert.True(loaded.Config.TryGetSetting("stat." + bound.Stat + ".max", out var maximum));
            Assert.Equal(bound.Minimum, minimum.AsInteger());
            Assert.Equal(bound.Maximum, maximum.AsInteger());
        }
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-DATA-009")]
    public void CanonicalV02DataSchemaManifestAndValidationArePinnedAndAgree()
    {
        var pins = new[]
        {
            ("config/source/Combat_Balance_Workbook_v0.2.xlsx", "3628c7fecafc91622d19086675bfd935529e53ef3622286ce309bbe47d0fc8bf"),
            ("config/generated/combat.balance.v0.2.json", "5361ec68359de06a1f4ff458893ad4d537825c873ffb0252e272a5b429b4e0c4"),
            ("config/generated/combat.balance.v0.2.map.csv", "218a495e35f5df16a6430921bafc457bd77bc619fabb8949914a5557b02f5b66"),
            ("config/generated/combat.balance.v0.2.validation.json", "146b96da958ec881bdfdfe5e9ddcdf47c7d591f5c83e951621b20036eb59f101"),
            ("schemas/balance/v0.2/combat.balance.schema.json", "068b1886e548f45d817cb9c8e6a88a6754c876036af934d49b2cd903bbed53ee"),
        };
        foreach (var pin in pins) Assert.Equal(pin.Item2, Hash(Read(pin.Item1)));
        Assert.Equal(Read("schemas/balance/v0.2/combat.balance.schema.json"), BalanceSchemaJson.Write("combat.balance/0.2"));
        var compiled = new BattleConfigCompiler().Compile(Read("config/generated/combat.balance.v0.2.json"));
        Assert.True(compiled.IsSuccess);
        Assert.Empty(compiled.Issues);
        Assert.Equal(Read("config/generated/combat.balance.v0.2.json"), compiled.GetCanonicalJson());
        using var manifest = JsonDocument.Parse(Read("config/generated/combat.balance.v0.2.manifest.json"));
        using var validation = JsonDocument.Parse(Read("config/generated/combat.balance.v0.2.validation.json"));
        Assert.Equal(compiled.ConfigHash!.Value.Value, manifest.RootElement.GetProperty("config_hash").GetString());
        Assert.Equal(compiled.ConfigHash.Value.Value, validation.RootElement.GetProperty("config_hash").GetString());
        Assert.Equal("sha256:" + pins[0].Item2, manifest.RootElement.GetProperty("source_workbook_sha256").GetString());
        Assert.Equal("sha256:" + pins[0].Item2, validation.RootElement.GetProperty("source_workbook_sha256").GetString());
        Assert.Equal(5, manifest.RootElement.GetProperty("entity_counts").GetProperty("effect_rules").GetInt32());
        Assert.Equal(0, validation.RootElement.GetProperty("error_count").GetInt32());
        Assert.Equal(0, validation.RootElement.GetProperty("warning_count").GetInt32());
        Assert.Empty(validation.RootElement.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    [Trait("AcceptanceId", "WP10-REG-007")]
    public void MigrationRetainsEveryExistingProfileFieldAndStableIdWithoutRebalance()
    {
        using var previous = JsonDocument.Parse(Read("config/generated/combat.balance.v0.1.json"));
        using var current = JsonDocument.Parse(Read("config/generated/combat.balance.v0.2.json"));
        foreach (var catalog in new[] { "fighters", "gear", "actions", "passives", "effects", "tactics" })
        {
            var oldEntries = previous.RootElement.GetProperty(catalog).EnumerateArray().ToArray();
            var newEntries = current.RootElement.GetProperty(catalog).EnumerateArray().ToArray();
            Assert.Equal(oldEntries.Length, newEntries.Length);
            for (var index = 0; index < oldEntries.Length; index++)
                foreach (var property in oldEntries[index].EnumerateObject())
                    Assert.Equal(property.Value.GetRawText(), newEntries[index].GetProperty(property.Name).GetRawText());
        }
        foreach (var property in previous.RootElement.GetProperty("settings").EnumerateObject())
            if (property.Name is not "global.sim.schema_version" and not "global.sim.config_version")
                Assert.Equal(property.Value.GetRawText(), current.RootElement.GetProperty("settings").GetProperty(property.Name).GetRawText());
        var strengths = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["bear_paw_jab"] = 1, ["bear_body_check"] = 2, ["bear_crushing_swipe"] = 3,
            ["bear_earthbreaker"] = 3, ["bear_fury_maul"] = 2, ["bear_rampage_charge"] = 3,
            ["gorilla_hammer_fist"] = 2, ["gorilla_driving_shove"] = 2, ["gorilla_overhead_pound"] = 3,
            ["kangaroo_lead_jab"] = 1, ["kangaroo_push_kick"] = 2, ["kangaroo_flying_kick"] = 2,
            ["kangaroo_tail_counter"] = 2, ["kangaroo_tempo_barrage"] = 1,
        };
        foreach (var action in current.RootElement.GetProperty("actions").EnumerateArray())
        {
            var id = action.GetProperty("action_id").GetString()!;
            Assert.Equal(strengths.GetValueOrDefault(id), action.GetProperty("hit_interrupt_strength").GetInt32());
            var profile = action.GetProperty("interrupt_profile").GetString();
            Assert.Equal(profile == "Armored" ? 3 : 1, action.GetProperty("hit_interrupt_min_strength").GetInt32());
            Assert.Equal(profile == "Unstoppable" ? "Stun|Knockdown" : "", action.GetProperty("ignored_control_categories").GetString());
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Read(string path) => File.ReadAllBytes(Path.Combine(Root(), path));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CombatLab.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CombatLab.sln");
    }
}
