using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Battle.Config.Compiler;

namespace CombatLab.Runner.Config.Export;

/// <summary>
/// Explicit, reproducible WP-10 migration. This is authoring tooling, never a runtime fallback.
/// Unedited OPC parts, styles, shared strings and existing formula locations are retained.
/// </summary>
public sealed class BalanceWorkbookMigrator
{
    public const string SourceSha256 = "bfd8a1d70ac82d5f830a981be078ebe60772a765553d842f73f1fb6b85d54fe2";
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly DateTimeOffset ZipEpoch = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Owner-approved authoring numbers, NOT defaults for missing compiled DATA.
    private static readonly (string Id, int Min, int Max, string Unit)[] Bounds =
    [
        ("max_health", 1, 10000, "health"), ("max_energy", 0, 10000, "energy"),
        ("energy_regen", 0, 1000, "energy/tick"), ("power", 0, 1000, "rating"),
        ("armor", 0, 2000, "rating"), ("precision", 0, 1000, "rating"),
        ("evasion", 0, 1000, "rating"), ("guard", 0, 1000, "rating"),
        ("guard_break", 0, 1000, "rating"), ("move_speed", 1, 500, "distance/tick"),
        ("action_speed", 1, 500, "rating"), ("initiative", 0, 1000, "rating"),
        ("control_power", 0, 1000, "rating"), ("control_resistance", 0, 1000, "rating"),
        ("mass", 1, 2000, "mass rating"),
    ];
    private static readonly IReadOnlyDictionary<string, int> InterruptStrengths = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["bear_paw_jab"] = 1, ["bear_body_check"] = 2, ["bear_crushing_swipe"] = 3,
        ["bear_earthbreaker"] = 3, ["bear_fury_maul"] = 2, ["bear_rampage_charge"] = 3,
        ["gorilla_hammer_fist"] = 2, ["gorilla_driving_shove"] = 2, ["gorilla_overhead_pound"] = 3,
        ["kangaroo_lead_jab"] = 1, ["kangaroo_push_kick"] = 2, ["kangaroo_flying_kick"] = 2,
        ["kangaroo_tail_counter"] = 2, ["kangaroo_tempo_barrage"] = 1,
    };
    private static readonly (string Rule, string Trigger, string Effect, string Role)[] Rules =
    [
        ("rule_control_fatigue", "ControlEnded", "effect_control_fatigue", "ControlFatigue"),
        ("rule_control_immunity", "FatigueThresholdReached", "effect_control_immunity", "ControlImmunity"),
        ("rule_grab_lockout", "GrabEnded", "effect_grab_lockout", "GrabLockout"),
        ("rule_wakeup_immunity", "WakeupCompleted", "effect_wakeup_immunity", "WakeupImmunity"),
        ("rule_guard_broken", "GuardBreak", "effect_guard_broken", "GuardBreak"),
    ];

    /// <returns>The SHA-256 of the newly created workbook.</returns>
    public string Migrate(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) || File.Exists(destinationPath))
            throw new IOException("Migration creates a new file only; source or existing destination will not be overwritten.");
        var sourceBytes = File.ReadAllBytes(sourcePath);
        if (Hash(sourceBytes) != SourceSha256)
            throw new InvalidDataException("WP-10 migration requires the pinned v0.1 workbook; unexpected source SHA-256.");
        var sourceExport = new BalanceWorkbookExporter().Export(sourcePath);
        RequireValid(sourceExport);
        using var sourceReader = new OpenXmlWorkbookReader(sourcePath);
        var package = ReadPackage(sourceBytes);
        var workbook = Parse(package["xl/workbook.xml"]);
        var relationships = Parse(package["xl/_rels/workbook.xml.rels"]);
        var documents = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var name in new[] { "Global Config", "Actions", "Effects", "Gear", "JSON Map" })
        {
            var part = FindSheetPart(workbook, relationships, name);
            documents.Add(name, Parse(package[part]));
        }
        var map = documents["JSON Map"];
        var mapReader = sourceReader.GetSheet("JSON Map");
        var nextMapRow = mapReader.MaximumRow + 1;
        var sortOrder = Enumerable.Range(6, mapReader.MaximumRow - 5)
            .Select(row => int.Parse(mapReader.GetCell("A" + N(row)).Value, CultureInfo.InvariantCulture)).Max();

        void Map(string ns, string id, string property, string type, string unit, string sheet,
            string sourceCell, string validationCell, object value, string note)
        {
            var row = nextMapRow++;
            var template = map.Root!.Element(S + "sheetData")!.Elements(S + "row").First(x => (string?)x.Attribute("r") == "6");
            var cells = new object[] { ++sortOrder, ns, id, property, type, value, unit, sheet, sourceCell, 1, 1, "OK", note };
            var element = NewRow(row, cells, template);
            Formula(element, "F" + N(row), Direct(sheet, sourceCell), value);
            Formula(element, "L" + N(row), Direct(sheet, validationCell), "OK");
            map.Root.Element(S + "sheetData")!.Add(element);
        }

        var global = documents["Global Config"];
        var globalReader = sourceReader.GetSheet("Global Config");
        foreach (var (key, version) in new[] { ("global.sim.schema_version", "combat.balance/0.2"), ("global.sim.config_version", "v0.2") })
        {
            var row = Enumerable.Range(6, globalReader.MaximumRow - 5).Single(x => globalReader.GetCell("A" + N(x)).Value == key);
            SetValue(Cell(global, "D" + N(row)), version);
            var mapRow = Enumerable.Range(6, mapReader.MaximumRow - 5).Single(x =>
                mapReader.GetCell("B" + N(x)).Value == "global" && mapReader.GetCell("D" + N(x)).Value == key);
            SetValue(Cell(map, "F" + N(mapRow)), version); // retains the existing shared formula
        }

        var nextGlobalRow = globalReader.MaximumRow + 1;
        void Setting(string key, string group, string description, int value, string unit, int min, int max, string note)
        {
            var row = nextGlobalRow++;
            var template = global.Root!.Element(S + "sheetData")!.Elements(S + "row").First(x => (string?)x.Attribute("r") == "8");
            var element = NewRow(row, [key, group, description, value, "int", unit, min, max, "FIXED", note, "OK"], template);
            Formula(element, "K" + N(row), $"IF(OR(E{row}=\"string\",E{row}=\"bool\",AND(D{row}>=G{row},D{row}<=H{row})),\"OK\",\"ERROR\")", "OK");
            global.Root.Element(S + "sheetData")!.Add(element);
            Map("global", "root", key, "int", unit, "Global Config", "D" + N(row), "K" + N(row), value, note);
        }
        foreach (var bound in Bounds)
        {
            Setting("stat." + bound.Id + ".min", "Stats", "Нижняя граница " + bound.Id,
                bound.Min, bound.Unit, bound.Min, bound.Max, "OPEN-WP10-05; OPEN-WP07-13: initialization/runtime clamp.");
            Setting("stat." + bound.Id + ".max", "Stats", "Верхняя граница " + bound.Id,
                bound.Max, bound.Unit, bound.Min, bound.Max, "OPEN-WP10-05; OPEN-WP07-13: initialization/runtime clamp.");
        }
        Setting("global.control.max_trigger_depth", "Control", "Максимальная глубина causal closure", 8, "causal levels", 1, 32, "OPEN-WP10-13; eligible depth overflow is fatal.");
        Setting("global.control.max_triggers_per_tick", "Control", "Лимит admitted trigger nodes за tick", 128, "nodes/tick", 1, 4096, "OPEN-WP10-13; resets each tick.");
        Setting("global.control.max_effect_instances_per_fighter", "Control", "Лимит занятых effect stack groups", 32, "groups/fighter", 1, 128, "OPEN-WP10-13; replacement at cap is legal.");
        Setting("global.control.knockdown_fall_ticks", "Control", "Knockdown: длительность Fall", 2, "ticks", 1, 100, "OPEN-WP10-20; half-open stage boundaries.");
        Setting("global.control.knockdown_grounded_ticks", "Control", "Knockdown: длительность Grounded", 6, "ticks", 1, 100, "OPEN-WP10-20; half-open stage boundaries.");
        Setting("global.control.knockdown_getup_ticks", "Control", "Knockdown: длительность GetUp", 3, "ticks", 1, 100, "OPEN-WP10-20; wakeup in phase 2.");

        void Metadata(string sheet, string ns, int column, string header, string property, string type, Func<int, object> value)
        {
            var doc = documents[sheet];
            var reader = sourceReader.GetSheet(sheet);
            var col = OpenXmlWorkbookReader.ColumnName(column);
            var data = doc.Root!.Element(S + "sheetData")!;
            var headerRow = data.Elements(S + "row").Single(x => (string?)x.Attribute("r") == "5");
            headerRow.Add(NewCell(col + "5", header, 2));
            var validationColumn = Enumerable.Range(1, column - 1).Select(OpenXmlWorkbookReader.ColumnName)
                .Single(c => reader.GetCell(c + "5").Value == "Validation");
            foreach (var row in data.Elements(S + "row").Where(x => int.Parse(x.Attribute("r")!.Value, CultureInfo.InvariantCulture) >= 6))
            {
                var index = int.Parse(row.Attribute("r")!.Value, CultureInfo.InvariantCulture);
                var item = value(index);
                row.Add(NewCell(col + N(index), item, type == "int" ? 22 : 7));
                Map(ns, reader.GetCell("A" + N(index)).Value, property, type, type == "int" ? "integer" : "enum tokens",
                    sheet, col + N(index), validationColumn + N(index), item, "WP-10 approved metadata; source DATA, not runtime inference.");
            }
            doc.Root.Element(S + "cols")!.Add(new XElement(S + "col", new XAttribute("min", column),
                new XAttribute("max", column), new XAttribute("width", type == "int" ? 20 : 30), new XAttribute("customWidth", 1)));
        }
        var actions = sourceReader.GetSheet("Actions");
        string ActionValue(int row, string col) => actions.GetCell(col + N(row)).Value;
        Metadata("Actions", "action", 59, "Hit Interrupt Strength", "hit_interrupt_strength", "int", row => InterruptStrengths.GetValueOrDefault(ActionValue(row, "A")));
        Metadata("Actions", "action", 60, "Hit Interrupt Min Strength", "hit_interrupt_min_strength", "int", row => ActionValue(row, "AF") == "Armored" ? 3 : 1);
        Metadata("Actions", "action", 61, "Hit Interruptible Phases", "hit_interruptible_phases", "string", row =>
            ActionValue(row, "F").Split('|').Contains("movement", StringComparer.Ordinal) ? "Startup|Active" : "Startup");
        Metadata("Actions", "action", 62, "Protected Phases", "protected_phases", "string", row => ActionValue(row, "AF") is "Armored" or "Unstoppable" ? "Startup|Active" : "");
        Metadata("Actions", "action", 63, "Ignored Control Categories", "ignored_control_categories", "string", row => ActionValue(row, "AF") == "Unstoppable" ? "Stun|Knockdown" : "");
        Metadata("Gear", "gear", 15, "Priority", "priority", "int", _ => 0);
        Metadata("Effects", "effect", 23, "Priority", "priority", "int", _ => 0);
        Metadata("Effects", "effect", 24, "Refresh Rule", "refresh_rule", "string", _ => "ResetDuration");
        var effects = sourceReader.GetSheet("Effects");
        Metadata("Effects", "effect", 25, "Semantic Role", "semantic_role", "string", row =>
            Rules.Where(rule => rule.Effect == effects.GetCell("A" + N(row)).Value).Select(rule => rule.Role).SingleOrDefault() ?? "None");

        var ruleSheet = CreateRuleSheet();
        var ruleHeaders = new[] { "Rule ID", "Owner Kind", "Owner ID", "Trigger", "Recipient", "Condition", "Primitive", "Effect ID", "Priority", "Internal Cooldown Ticks", "Max Activations / Tick", "Max Activations / Battle", "Once Per Event", "Status", "Notes RU", "Validation" };
        ruleSheet.Root!.Element(S + "sheetData")!.Add(NewRow(5, ruleHeaders.Cast<object>().ToArray(), null, header: true));
        var properties = new[] { "rule_id", "owner_kind", "owner_id", "trigger", "recipient", "condition", "primitive", "effect_id", "priority", "internal_cooldown_ticks", "max_activations_per_tick", "max_activations_per_battle", "once_per_event" };
        var ruleRow = 6;
        foreach (var rule in Rules)
        {
            var row = ruleRow++;
            object[] values = [rule.Rule, "Global", "global", rule.Trigger, "Self", "LivingTarget", "ApplyEffect", rule.Effect, 0, 0, 1, 999, 1, "FIXED", "OPEN-WP10-08/17..20; global Self is the hook subject.", "OK"];
            var element = NewRow(row, values, null);
            element.Elements(S + "c").Single(x => (string?)x.Attribute("r") == "A" + N(row)).SetAttributeValue("s", 4);
            element.Elements(S + "c").Single(x => (string?)x.Attribute("r") == "M" + N(row)).SetAttributeValue("s", 28);
            element.Elements(S + "c").Single(x => (string?)x.Attribute("r") == "O" + N(row)).SetAttributeValue("s", 10);
            Formula(element, "P" + N(row), BalanceWorkbookExporter.EffectRuleValidationFormula(row), "OK");
            ruleSheet.Root.Element(S + "sheetData")!.Add(element);
            for (var i = 0; i < properties.Length; i++)
                Map("effect_rule", rule.Rule, properties[i], i == 12 ? "bool" : i >= 8 ? "int" : "string",
                    i is >= 8 and < 12 ? "integer" : "id/enum", "Effect Rules", OpenXmlWorkbookReader.ColumnName(i + 1) + N(row), "P" + N(row), values[i], "OPEN-WP10-08; explicit rule binding.");
        }
        UpdateRange(ruleSheet, 16);
        RegisterSheet(package, workbook, relationships, ruleSheet, "Effect Rules");
        foreach (var item in documents)
        {
            UpdateRange(item.Value, item.Key switch { "Actions" => 63, "Effects" => 25, "Gear" => 15, "JSON Map" => 13, _ => 11 });
            package[FindSheetPart(workbook, relationships, item.Key)] = Serialize(item.Value);
        }
        var calc = workbook.Root!.Element(S + "calcPr");
        if (calc is null) { calc = new XElement(S + "calcPr"); workbook.Root.Add(calc); }
        calc.SetAttributeValue("fullCalcOnLoad", 1);
        calc.SetAttributeValue("forceFullCalc", 1);
        package["xl/workbook.xml"] = Serialize(workbook);
        package["xl/_rels/workbook.xml.rels"] = Serialize(relationships);

        var directory = Path.GetDirectoryName(destinationPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".wp10-migration-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
                foreach (var part in package.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    // Stored entries avoid zlib implementation/platform differences in binary reproducibility.
                    var entry = archive.CreateEntry(part.Key, CompressionLevel.NoCompression);
                    entry.LastWriteTime = ZipEpoch;
                    entry.ExternalAttributes = 0;
                    using var stream = entry.Open();
                    stream.Write(part.Value);
                }
            var packageBytes = File.ReadAllBytes(temporary);
            CanonicalXlsxZip.Normalize(packageBytes);
            File.WriteAllBytes(temporary, packageBytes);
            RequireValid(new BalanceWorkbookExporter().Export(temporary));
            if (Hash(File.ReadAllBytes(sourcePath)) != SourceSha256)
                throw new InvalidDataException("Source changed during migration; no output will be published.");
            var hash = Hash(File.ReadAllBytes(temporary));
            File.Move(temporary, destinationPath, overwrite: false);
            return hash;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RequireValid(BalanceWorkbookExportResult export)
    {
        var compilation = new BattleConfigCompiler().Compile(export.CandidateJson);
        if (!export.IsSuccess || export.Issues.Count != 0 || !compilation.IsSuccess || compilation.Issues.Count != 0)
            throw new InvalidDataException("Workbook validation failed: " + string.Join("; ",
                export.Issues.Select(x => x.Code + " " + x.Path + ": " + x.Message)
                    .Concat(compilation.Issues.Select(x => x.Code + " " + x.Path + ": " + x.Message))));
    }
    private static string N(int number) => number.ToString(CultureInfo.InvariantCulture);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Direct(string sheet, string cell) => "'" + sheet.Replace("'", "''", StringComparison.Ordinal) + "'!" + cell;
    private static XDocument Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    private static byte[] Serialize(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, NewLineHandling = NewLineHandling.None }))
            document.Save(writer);
        return stream.ToArray();
    }
    private static Dictionary<string, byte[]> ReadPackage(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (!parts.TryAdd(entry.FullName, buffer.ToArray())) throw new InvalidDataException("Duplicate OPC part.");
        }
        return parts;
    }
    private static string FindSheetPart(XDocument workbook, XDocument relationships, string name)
    {
        var sheet = workbook.Descendants(S + "sheet").Single(x => (string?)x.Attribute("name") == name);
        var relationship = relationships.Descendants(P + "Relationship").Single(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(R + "id"));
        var target = relationship.Attribute("Target")!.Value;
        return target.StartsWith('/') ? target[1..] : "xl/" + target;
    }
    private static XElement Cell(XDocument document, string reference) => document.Descendants(S + "c").Single(x => (string?)x.Attribute("r") == reference);
    private static XElement NewCell(string reference, object value, int style)
    {
        var cell = new XElement(S + "c", new XAttribute("r", reference), new XAttribute("s", style));
        SetValue(cell, value);
        return cell;
    }
    private static void SetValue(XElement cell, object value)
    {
        cell.Elements(S + "v").Remove();
        cell.Elements(S + "is").Remove();
        if (value is string text)
        {
            if (cell.Element(S + "f") is not null)
            {
                cell.SetAttributeValue("t", "str");
                cell.Add(new XElement(S + "v", text));
            }
            else
            {
                cell.SetAttributeValue("t", "inlineStr");
                cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
            }
        }
        else
        {
            cell.SetAttributeValue("t", null);
            cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
        }
    }
    private static XElement NewRow(int row, object[] values, XElement? template, bool header = false)
    {
        var element = new XElement(S + "row", new XAttribute("r", row));
        if (template is not null)
            foreach (var attribute in template.Attributes().Where(x => x.Name != "r" && x.Name != "spans")) element.Add(new XAttribute(attribute));
        else { element.SetAttributeValue("ht", header ? 36 : 30); element.SetAttributeValue("customHeight", 1); }
        for (var index = 0; index < values.Length; index++)
        {
            var col = OpenXmlWorkbookReader.ColumnName(index + 1);
            var inherited = template?.Elements(S + "c").FirstOrDefault(x => OpenXmlWorkbookReader.GetColumnName(x.Attribute("r")!.Value) == col)?.Attribute("s")?.Value;
            var style = inherited is not null ? int.Parse(inherited, CultureInfo.InvariantCulture) : header ? 2 : values[index] is string ? 7 : 22;
            element.Add(NewCell(col + N(row), values[index], style));
        }
        return element;
    }
    private static void Formula(XElement row, string reference, string formula, object value)
    {
        var cell = row.Elements(S + "c").Single(x => (string?)x.Attribute("r") == reference);
        cell.AddFirst(new XElement(S + "f", formula));
        SetValue(cell, value);
        cell.SetAttributeValue("s", 16);
    }
    private static void UpdateRange(XDocument doc, int columns)
    {
        var last = doc.Root!.Element(S + "sheetData")!.Elements(S + "row").Max(x => int.Parse(x.Attribute("r")!.Value, CultureInfo.InvariantCulture));
        var end = OpenXmlWorkbookReader.ColumnName(columns);
        doc.Root.Element(S + "dimension")!.SetAttributeValue("ref", "A1:" + end + N(last));
        foreach (var merge in doc.Descendants(S + "mergeCell"))
        {
            var reference = merge.Attribute("ref")!.Value;
            if (reference.StartsWith("A", StringComparison.Ordinal) && reference.Contains(':'))
            {
                var row = OpenXmlWorkbookReader.GetRowNumber(reference.Split(':')[0]);
                if (row <= 3) merge.SetAttributeValue("ref", "A" + N(row) + ":" + end + N(row));
            }
        }
    }
    private static XDocument CreateRuleSheet()
    {
        var root = new XElement(S + "worksheet", new XAttribute(XNamespace.Xmlns + "r", R));
        root.Add(new XElement(S + "dimension", new XAttribute("ref", "A1:P10")),
            new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("showGridLines", 0),
                new XElement(S + "pane", new XAttribute("ySplit", 5), new XAttribute("topLeftCell", "A6"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            new XElement(S + "sheetFormatPr", new XAttribute("defaultRowHeight", 30)));
        var cols = new XElement(S + "cols");
        for (var column = 1; column <= 16; column++)
            cols.Add(new XElement(S + "col", new XAttribute("min", column), new XAttribute("max", column),
                new XAttribute("width", column is 1 or 8 ? 32 : column == 15 ? 48 : 24), new XAttribute("customWidth", 1)));
        root.Add(cols);
        var data = new XElement(S + "sheetData");
        data.Add(NewRow(1, ["Effect Rules — combat.balance/0.2"], null),
            NewRow(2, ["WP-10: явные bindings. Global/Self = semantic subject события."], null),
            NewRow(3, ["OPEN-WP10-08; CLOSED 2026-10-03. Полные passive/gear triggers — WP-11."], null));
        root.Add(data, new XElement(S + "autoFilter", new XAttribute("ref", "A5:P10")),
            new XElement(S + "mergeCells", new XAttribute("count", 3), Enumerable.Range(1, 3).Select(row =>
                new XElement(S + "mergeCell", new XAttribute("ref", "A" + N(row) + ":P" + N(row))))));
        return new XDocument(root);
    }
    private static void RegisterSheet(Dictionary<string, byte[]> package, XDocument workbook, XDocument relationships, XDocument document, string name)
    {
        var sheets = workbook.Root!.Element(S + "sheets")!;
        var id = sheets.Elements(S + "sheet").Max(x => int.Parse(x.Attribute("sheetId")!.Value, CultureInfo.InvariantCulture)) + 1;
        var part = "xl/worksheets/sheet" + N(id) + ".xml";
        var relationshipId = "rIdWp10EffectRules";
        if (package.ContainsKey(part) || relationships.Descendants(P + "Relationship").Any(x => (string?)x.Attribute("Id") == relationshipId))
            throw new InvalidDataException("New worksheet would collide with an existing OPC part.");
        sheets.Add(new XElement(S + "sheet", new XAttribute("name", name), new XAttribute("sheetId", id), new XAttribute(R + "id", relationshipId)));
        relationships.Root!.Add(new XElement(P + "Relationship", new XAttribute("Id", relationshipId),
            new XAttribute("Type", R.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/sheet" + N(id) + ".xml")));
        var contentTypes = Parse(package["[Content_Types].xml"]);
        contentTypes.Root!.Add(new XElement(C + "Override", new XAttribute("PartName", "/" + part),
            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        package["[Content_Types].xml"] = Serialize(contentTypes);
        // Excel's package metadata must list the new sheet as well as workbook.xml.
        var extendedProperties = Parse(package["docProps/app.xml"]);
        var e = (XNamespace)"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        var v = (XNamespace)"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
        var titles = extendedProperties.Root!.Element(e + "TitlesOfParts")!.Element(v + "vector")!;
        titles.Add(new XElement(v + "lpstr", name));
        titles.SetAttributeValue("size", titles.Elements().Count());
        extendedProperties.Root.Element(e + "HeadingPairs")!.Descendants(v + "i4").Single().Value = N(sheets.Elements(S + "sheet").Count());
        package["docProps/app.xml"] = Serialize(extendedProperties);
        package.Add(part, Serialize(document));
    }
}
