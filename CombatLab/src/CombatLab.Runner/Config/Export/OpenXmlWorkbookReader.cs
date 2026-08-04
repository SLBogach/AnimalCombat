using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace CombatLab.Runner.Config.Export;

internal sealed class OpenXmlWorkbookReader : IDisposable
{
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace OfficeRelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly XNamespace PackageRelationshipNamespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly FileStream stream;
    private readonly ZipArchive archive;
    private readonly IReadOnlyList<string> sharedStrings;
    private readonly Dictionary<string, string> sheetParts;
    private readonly Dictionary<string, WorkbookSheet> sheets = new(StringComparer.Ordinal);

    public OpenXmlWorkbookReader(string path)
    {
        stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        sharedStrings = ReadSharedStrings();
        sheetParts = ReadSheetParts();
    }

    public WorkbookSheet GetSheet(string name)
    {
        if (sheets.TryGetValue(name, out var existing))
        {
            return existing;
        }

        if (!sheetParts.TryGetValue(name, out var partName))
        {
            throw new InvalidDataException($"Workbook sheet '{name}' does not exist.");
        }

        var document = LoadPart(partName);
        var cells = new Dictionary<string, WorkbookCell>(StringComparer.OrdinalIgnoreCase);
        var maximumRow = 0;

        foreach (var element in document.Descendants(SpreadsheetNamespace + "c"))
        {
            var reference = (string?)element.Attribute("r");
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            reference = NormalizeCellReference(reference);
            maximumRow = Math.Max(maximumRow, GetRowNumber(reference));
            var type = (string?)element.Attribute("t");
            var formula = element.Element(SpreadsheetNamespace + "f")?.Value;
            var rawValue = element.Element(SpreadsheetNamespace + "v")?.Value;
            var value = DecodeValue(element, type, rawValue);
            cells.Add(reference, new WorkbookCell(reference, value, formula));
        }

        var loaded = new WorkbookSheet(name, cells, maximumRow);
        sheets.Add(name, loaded);
        return loaded;
    }

    public void Dispose()
    {
        archive.Dispose();
        stream.Dispose();
    }

    internal static string NormalizeCellReference(string reference)
    {
        var normalized = reference.Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        if (normalized.Length == 0)
        {
            throw new InvalidDataException("A cell reference cannot be empty.");
        }

        var letterCount = 0;
        while (letterCount < normalized.Length && char.IsAsciiLetter(normalized[letterCount]))
        {
            letterCount++;
        }

        if (letterCount == 0 || letterCount == normalized.Length)
        {
            throw new InvalidDataException($"Invalid cell reference '{reference}'.");
        }

        var column = normalized[..letterCount].ToUpperInvariant();
        var rowText = normalized[letterCount..];
        if (!int.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out var row) || row <= 0)
        {
            throw new InvalidDataException($"Invalid cell reference '{reference}'.");
        }

        return column + row.ToString(CultureInfo.InvariantCulture);
    }

    internal static int GetRowNumber(string reference)
    {
        var index = 0;
        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            index++;
        }

        return int.Parse(reference.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    internal static string GetColumnName(string reference)
    {
        var index = 0;
        while (index < reference.Length && char.IsAsciiLetter(reference[index]))
        {
            index++;
        }

        return reference[..index].ToUpperInvariant();
    }

    internal static string ColumnName(int oneBasedIndex)
    {
        if (oneBasedIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(oneBasedIndex));
        }

        Span<char> buffer = stackalloc char[8];
        var position = buffer.Length;
        var remaining = oneBasedIndex;
        while (remaining > 0)
        {
            remaining--;
            buffer[--position] = (char)('A' + (remaining % 26));
            remaining /= 26;
        }

        return new string(buffer[position..]);
    }

    private IReadOnlyList<string> ReadSharedStrings()
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return Array.Empty<string>();
        }

        using var entryStream = entry.Open();
        var document = LoadXml(entryStream);
        return document
            .Descendants(SpreadsheetNamespace + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value)))
            .ToArray();
    }

    private Dictionary<string, string> ReadSheetParts()
    {
        var workbook = LoadPart("xl/workbook.xml");
        var relationships = LoadPart("xl/_rels/workbook.xml.rels");
        var targets = relationships
            .Descendants(PackageRelationshipNamespace + "Relationship")
            .Where(relationship =>
                !string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                relationship => (string?)relationship.Attribute("Id")
                    ?? throw new InvalidDataException("A workbook relationship is missing its Id."),
                relationship => ResolvePartName(
                    "xl/workbook.xml",
                    (string?)relationship.Attribute("Target")
                        ?? throw new InvalidDataException("A workbook relationship is missing its Target.")),
                StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sheet in workbook.Descendants(SpreadsheetNamespace + "sheet"))
        {
            var name = (string?)sheet.Attribute("name")
                ?? throw new InvalidDataException("A workbook sheet is missing its name.");
            var relationshipId = (string?)sheet.Attribute(OfficeRelationshipNamespace + "id")
                ?? throw new InvalidDataException($"Workbook sheet '{name}' is missing its relationship id.");
            if (!targets.TryGetValue(relationshipId, out var target))
            {
                throw new InvalidDataException($"Workbook sheet '{name}' has an unresolved relationship.");
            }

            result.Add(name, target);
        }

        return result;
    }

    private string DecodeValue(XElement cell, string? type, string? rawValue)
    {
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
        {
            return string.Concat(cell.Descendants(SpreadsheetNamespace + "t").Select(text => text.Value));
        }

        if (string.Equals(type, "s", StringComparison.Ordinal))
        {
            if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 0 ||
                index >= sharedStrings.Count)
            {
                throw new InvalidDataException($"Invalid shared-string index '{rawValue}'.");
            }

            return sharedStrings[index];
        }

        return rawValue ?? string.Empty;
    }

    private XDocument LoadPart(string partName)
    {
        var entry = archive.GetEntry(partName)
            ?? throw new InvalidDataException($"Required XLSX part '{partName}' does not exist.");
        using var entryStream = entry.Open();
        return LoadXml(entryStream);
    }

    private static XDocument LoadXml(Stream input)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(input, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string ResolvePartName(string basePart, string target)
    {
        var slashNormalized = target.Replace('\\', '/');
        var combined = slashNormalized.StartsWith("/", StringComparison.Ordinal)
            ? slashNormalized[1..]
            : basePart[..(basePart.LastIndexOf('/') + 1)] + slashNormalized;
        var segments = new Stack<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new InvalidDataException($"XLSX relationship target '{target}' leaves the archive root.");
                }

                segments.Pop();
                continue;
            }

            segments.Push(segment);
        }

        return string.Join('/', segments.Reverse());
    }
}

internal sealed class WorkbookSheet
{
    private readonly IReadOnlyDictionary<string, WorkbookCell> cells;

    public WorkbookSheet(string name, IReadOnlyDictionary<string, WorkbookCell> cells, int maximumRow)
    {
        Name = name;
        this.cells = cells;
        MaximumRow = maximumRow;
    }

    public string Name { get; }

    public int MaximumRow { get; }

    public WorkbookCell GetCell(string reference)
    {
        var normalized = OpenXmlWorkbookReader.NormalizeCellReference(reference);
        return cells.TryGetValue(normalized, out var value)
            ? value
            : new WorkbookCell(normalized, string.Empty, null);
    }
}

internal sealed record WorkbookCell(string Reference, string Value, string? Formula);
