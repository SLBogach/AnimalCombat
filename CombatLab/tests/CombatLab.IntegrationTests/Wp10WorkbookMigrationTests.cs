using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Battle.Config;
using Battle.Config.Compiler;
using Battle.Config.Manifest;
using CombatLab.Runner.Config;
using CombatLab.Runner.Config.Export;

namespace CombatLab.IntegrationTests;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10WorkbookMigrationTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";

    [Fact]
    public void ZipPlatformMetadataNormalizationIsIdempotentAndRemovesUnixHostDifferences()
    {
        var expected = Read("config/source/Combat_Balance_Workbook_v0.2.xlsx");
        var simulatedUnix = expected.ToArray();
        var end = simulatedUnix.Length - 22;
        var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(simulatedUnix.AsSpan(end + 16, 4)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(simulatedUnix.AsSpan(end + 10, 2));
        for (var index = 0; index < count; index++)
        {
            simulatedUnix[offset + 5] = 3;
            BinaryPrimitives.WriteUInt32LittleEndian(simulatedUnix.AsSpan(offset + 38, 4), 0x81a40000);
            offset += 46 + BinaryPrimitives.ReadUInt16LittleEndian(simulatedUnix.AsSpan(offset + 28, 2)) +
                BinaryPrimitives.ReadUInt16LittleEndian(simulatedUnix.AsSpan(offset + 30, 2)) +
                BinaryPrimitives.ReadUInt16LittleEndian(simulatedUnix.AsSpan(offset + 32, 2));
        }
        Assert.NotEqual(expected, simulatedUnix);
        CanonicalXlsxZip.Normalize(simulatedUnix);
        Assert.Equal(expected, simulatedUnix);
        CanonicalXlsxZip.Normalize(simulatedUnix);
        Assert.Equal(expected, simulatedUnix);
        Assert.Throws<InvalidDataException>(() => CanonicalXlsxZip.Normalize([1, 2, 3]));
        var malformed = expected.ToArray(); malformed[end] = 0;
        Assert.Throws<InvalidDataException>(() => CanonicalXlsxZip.Normalize(malformed));
    }

    [Fact]
    public void NativeMigrationReproducesCommittedWorkbookAndPreservesExistingOpcPartsAndCells()
    {
        InTemp(directory =>
        {
            var destination = Path.Combine(directory, "migrated.xlsx");
            var source = Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.1.xlsx");
            var hash = new BalanceWorkbookMigrator().Migrate(source, destination);
            Assert.Equal(BalanceWorkbookMigrator.SourceSha256, Hash(File.ReadAllBytes(source)));
            Assert.Equal(File.ReadAllBytes(Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.2.xlsx")), File.ReadAllBytes(destination));
            Assert.Equal("3628c7fecafc91622d19086675bfd935529e53ef3622286ce309bbe47d0fc8bf", hash);
            var original = Parts(source);
            var migrated = Parts(destination);
            var changed = new HashSet<string>(StringComparer.Ordinal)
            {
                "[Content_Types].xml", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "docProps/app.xml",
                "xl/worksheets/sheet2.xml", "xl/worksheets/sheet4.xml", "xl/worksheets/sheet6.xml",
                "xl/worksheets/sheet8.xml", "xl/worksheets/sheet13.xml",
            };
            foreach (var part in original)
            {
                Assert.True(migrated.ContainsKey(part.Key), part.Key);
                if (!changed.Contains(part.Key)) Assert.Equal(part.Value, migrated[part.Key]);
                if (!part.Key.StartsWith("xl/worksheets/", StringComparison.Ordinal)) continue;
                var oldCells = Xml(part.Value).Descendants(S + "c");
                var newCells = Xml(migrated[part.Key]).Descendants(S + "c").ToDictionary(x => x.Attribute("r")!.Value);
                foreach (var oldCell in oldCells)
                {
                    var reference = oldCell.Attribute("r")!.Value;
                    var newCell = newCells[reference];
                    Assert.Equal((string?)oldCell.Attribute("s"), (string?)newCell.Attribute("s"));
                    Assert.True(XNode.DeepEquals(oldCell.Element(S + "f"), newCell.Element(S + "f")), part.Key + ":" + reference);
                    var permittedVersionCell = part.Key == "xl/worksheets/sheet2.xml" && reference is "D6" or "D7" ||
                        part.Key == "xl/worksheets/sheet13.xml" && reference is "F6" or "F7";
                    if (!permittedVersionCell) Assert.True(XNode.DeepEquals(oldCell, newCell), part.Key + ":" + reference);
                }
            }
            Assert.Equal(original.Count + 1, migrated.Count);
            Assert.Contains("xl/worksheets/sheet15.xml", migrated.Keys);
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("ru-RU")]
    [InlineData("tr-TR")]
    public void NativeWorkbookBytesDoNotDependOnCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            InTemp(directory =>
            {
                Assert.Equal("3628c7fecafc91622d19086675bfd935529e53ef3622286ce309bbe47d0fc8bf",
                    new BalanceWorkbookMigrator().Migrate(Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.1.xlsx"), Path.Combine(directory, "migrated.xlsx")));
            });
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    [Fact]
    public void MigrationRefusesOverwriteAliasAndUnexpectedSourceWithoutLeavingTemporaryFiles()
    {
        InTemp(directory =>
        {
            var source = Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.1.xlsx");
            var destination = Path.Combine(directory, "existing.xlsx");
            File.WriteAllBytes(destination, [1, 2, 3]);
            var migrator = new BalanceWorkbookMigrator();
            Assert.Throws<IOException>(() => migrator.Migrate(source, destination));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(destination));
            Assert.Throws<IOException>(() => migrator.Migrate(source, source));
            Assert.Throws<InvalidDataException>(() => migrator.Migrate(destination, Path.Combine(directory, "new.xlsx")));
            Assert.Single(Directory.GetFiles(directory));
            Assert.Equal(BalanceWorkbookMigrator.SourceSha256, Hash(File.ReadAllBytes(source)));
        });
    }

    [Fact]
    public void CliReportsUnexpectedSourceAsInputErrorWithoutCreatingDestination()
    {
        InTemp(directory =>
        {
            var source = Path.Combine(directory, "unexpected.xlsx");
            var destination = Path.Combine(directory, "new.xlsx");
            File.WriteAllBytes(source, [1, 2, 3]);
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(ConfigCommand.UsageOrInputExitCode,
                ConfigCommand.Execute(["migrate-config", "--workbook", source, "--output", destination], output, error, Root()));
            Assert.Contains("unexpected source SHA-256", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(destination));
            Assert.Single(Directory.GetFiles(directory));
        });
    }

    [Fact]
    public void SourceMapExportLoaderAndCompiledRulesAgreeForEveryNewCell()
    {
        var workbook = Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.2.xlsx");
        var export = new BalanceWorkbookExporter().Export(workbook);
        Assert.True(export.IsSuccess, string.Join("; ", export.Issues));
        Assert.Empty(export.Issues);
        var compiled = new BattleConfigCompiler().Compile(export.CandidateJson);
        Assert.True(compiled.IsSuccess);
        Assert.Empty(compiled.Issues);
        var loaded = new BattleConfigLoader().Load(Read("config/generated/combat.balance.v0.2.json"), Read("config/generated/combat.balance.v0.2.manifest.json"));
        Assert.True(loaded.IsSuccess, string.Join("; ", loaded.Issues));
        Assert.Equal(compiled.Config!.Reference, loaded.Config!.Reference);
        Assert.Equal(5, loaded.Config.EffectRules.Count);
        Assert.Equal(5, export.EntityCounts["effect_rules"]);
        var parts = Parts(workbook);
        var book = Xml(parts["xl/workbook.xml"]);
        var rels = Xml(parts["xl/_rels/workbook.xml.rels"]);
        var strings = Xml(parts["xl/sharedStrings.xml"]).Descendants(S + "si").Select(x => x.Value).ToArray();
        var sheets = book.Descendants(S + "sheet").ToDictionary(x => x.Attribute("name")!.Value, x =>
        {
            var target = rels.Descendants(P + "Relationship").Single(r => (string?)r.Attribute("Id") == (string?)x.Attribute(R + "id")).Attribute("Target")!.Value;
            return Xml(parts["xl/" + target]).Descendants(S + "c").ToDictionary(c => c.Attribute("r")!.Value);
        });
        string Value(XElement cell) => (string?)cell.Attribute("t") == "s" ? strings[int.Parse(cell.Element(S + "v")!.Value, CultureInfo.InvariantCulture)] :
            cell.Element(S + "is")?.Value ?? cell.Element(S + "v")?.Value ?? "";
        using var json = JsonDocument.Parse(compiled.GetCanonicalJson());
        var map = sheets["JSON Map"];
        var newRows = Xml(parts["xl/worksheets/sheet13.xml"]).Descendants(S + "row")
            .Where(x => int.Parse(x.Attribute("r")!.Value, CultureInfo.InvariantCulture) > 2101).ToArray();
        Assert.Equal(260, newRows.Length); // 36 settings + 120 action + 9 gear + 30 effect + 65 rule fields
        foreach (var row in newRows)
        {
            var n = row.Attribute("r")!.Value;
            var ns = Value(map["B" + n]);
            var id = Value(map["C" + n]);
            var property = Value(map["D" + n]);
            var sheet = Value(map["H" + n]);
            var address = Value(map["I" + n]);
            Assert.Equal("1", Value(map["J" + n]));
            Assert.Equal("1", Value(map["K" + n]));
            Assert.Equal("'" + sheet + "'!" + address, map["F" + n].Element(S + "f")!.Value);
            var source = Value(sheets[sheet][address]);
            var element = ns == "global" ? json.RootElement.GetProperty("settings").GetProperty(property) :
                json.RootElement.GetProperty(ns switch { "action" => "actions", "gear" => "gear", "effect" => "effects", _ => "effect_rules" })
                    .EnumerateArray().Single(x => x.GetProperty(ns == "effect_rule" ? "rule_id" : ns + "_id").GetString() == id).GetProperty(property);
            Assert.Equal(source, element.ValueKind switch { JsonValueKind.True => "1", JsonValueKind.False => "0", JsonValueKind.String => element.GetString(), _ => element.GetRawText() });
        }
    }

    [Fact]
    public void CliV02ExportIsReproducibleAndCannotTouchHistoricalV01Artifacts()
    {
        InTemp(directory =>
        {
            // Isolated solution root prevents schema writing into the actual working tree.
            File.WriteAllBytes(Path.Combine(directory, "CombatLab.sln"), []);
            var source = Path.Combine(Root(), "config/source/Combat_Balance_Workbook_v0.2.xlsx");
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(0, ConfigCommand.Execute(["export-config", "--workbook", source, "--output", directory], output, error, directory));
            Assert.Equal("", error.ToString());
            foreach (var suffix in new[] { ".json", ".map.csv", ".validation.json" })
                Assert.Equal(Read("config/generated/combat.balance.v0.2" + suffix), File.ReadAllBytes(Path.Combine(directory, "combat.balance.v0.2" + suffix)));
            Assert.Equal(Read("schemas/balance/v0.2/combat.balance.schema.json"), File.ReadAllBytes(Path.Combine(directory, "schemas/balance/v0.2/combat.balance.schema.json")));
            var actual = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "combat.balance.v0.2.manifest.json")))!.AsObject();
            var expected = JsonNode.Parse(Read("config/generated/combat.balance.v0.2.manifest.json"))!.AsObject();
            Assert.Equal("0.2.0+wp10", actual["exporter_version"]!.GetValue<string>());
            actual.Remove("generated_utc"); expected.Remove("generated_utc");
            Assert.True(JsonNode.DeepEquals(expected, actual));
            Assert.DoesNotContain(Directory.GetFiles(directory, "*", SearchOption.AllDirectories), path => path.Contains("v0.1", StringComparison.Ordinal));
            Assert.Equal(0, ConfigCommand.Execute(["validate-config", "--config", Path.Combine(directory, "combat.balance.v0.2.json"), "--manifest", Path.Combine(directory, "combat.balance.v0.2.manifest.json")], output, error, directory));
        });
    }

    private static Dictionary<string, byte[]> Parts(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
        });
    }
    private static XDocument Xml(byte[] bytes) { using var stream = new MemoryStream(bytes); return XDocument.Load(stream); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Read(string path) => File.ReadAllBytes(Path.Combine(Root(), path));
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CombatLab.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("CombatLab.sln");
    }
    private static void InTemp(Action<string> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "combatlab-wp10-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { body(directory); } finally { Directory.Delete(directory, recursive: true); }
    }
}
