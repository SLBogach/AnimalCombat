using System.Globalization;
using System.IO.Compression;
using System.Text;
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
        var sharedFormulaMasters = ReadSharedFormulaMasters(document);
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
            var formula = DecodeFormula(element, reference, sharedFormulaMasters);
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

    private static IReadOnlyDictionary<string, SharedFormulaMaster> ReadSharedFormulaMasters(
        XDocument document)
    {
        var result = new Dictionary<string, SharedFormulaMaster>(StringComparer.Ordinal);
        foreach (var cell in document.Descendants(SpreadsheetNamespace + "c"))
        {
            var formula = cell.Element(SpreadsheetNamespace + "f");
            if (formula is null ||
                !string.Equals((string?)formula.Attribute("t"), "shared", StringComparison.Ordinal) ||
                formula.Value.Length == 0)
            {
                continue;
            }

            var sharedIndex = (string?)formula.Attribute("si");
            var reference = (string?)cell.Attribute("r");
            if (string.IsNullOrWhiteSpace(sharedIndex) || string.IsNullOrWhiteSpace(reference))
            {
                throw new InvalidDataException("A shared-formula master is missing its index or cell reference.");
            }

            var master = new SharedFormulaMaster(
                NormalizeCellReference(reference),
                formula.Value);
            if (!result.TryAdd(sharedIndex, master))
            {
                throw new InvalidDataException($"Shared-formula index '{sharedIndex}' has more than one master.");
            }
        }

        return result;
    }

    private static string? DecodeFormula(
        XElement cell,
        string reference,
        IReadOnlyDictionary<string, SharedFormulaMaster> sharedFormulaMasters)
    {
        var formula = cell.Element(SpreadsheetNamespace + "f");
        if (formula is null ||
            !string.Equals((string?)formula.Attribute("t"), "shared", StringComparison.Ordinal) ||
            formula.Value.Length > 0)
        {
            return formula?.Value;
        }

        var sharedIndex = (string?)formula.Attribute("si");
        if (string.IsNullOrWhiteSpace(sharedIndex) ||
            !sharedFormulaMasters.TryGetValue(sharedIndex, out var master))
        {
            throw new InvalidDataException(
                $"Shared-formula cell '{reference}' has no resolvable master.");
        }

        return TranslateSharedFormula(master.Formula, master.Reference, reference);
    }

    private static string TranslateSharedFormula(
        string formula,
        string masterReference,
        string targetReference)
    {
        var masterColumn = ColumnIndex(GetColumnName(masterReference));
        var targetColumn = ColumnIndex(GetColumnName(targetReference));
        var columnOffset = targetColumn - masterColumn;
        var rowOffset = GetRowNumber(targetReference) - GetRowNumber(masterReference);
        var translated = new StringBuilder(formula.Length);

        for (var index = 0; index < formula.Length;)
        {
            if (formula[index] == '"')
            {
                CopyQuotedToken(formula, translated, ref index, '"');
                continue;
            }

            if (formula[index] == '\'')
            {
                CopyQuotedToken(formula, translated, ref index, '\'');
                continue;
            }

            if (TryTranslateCellReference(
                    formula,
                    ref index,
                    columnOffset,
                    rowOffset,
                    translated))
            {
                continue;
            }

            translated.Append(formula[index]);
            index++;
        }

        return translated.ToString();
    }

    private static void CopyQuotedToken(
        string formula,
        StringBuilder target,
        ref int index,
        char quote)
    {
        target.Append(formula[index++]);
        while (index < formula.Length)
        {
            var current = formula[index++];
            target.Append(current);
            if (current != quote)
            {
                continue;
            }

            if (index < formula.Length && formula[index] == quote)
            {
                target.Append(formula[index++]);
                continue;
            }

            return;
        }
    }

    private static bool TryTranslateCellReference(
        string formula,
        ref int index,
        int columnOffset,
        int rowOffset,
        StringBuilder target)
    {
        var start = index;
        if (start > 0 && IsReferenceIdentifierCharacter(formula[start - 1]))
        {
            return false;
        }

        var cursor = start;
        var absoluteColumn = cursor < formula.Length && formula[cursor] == '$';
        if (absoluteColumn)
        {
            cursor++;
        }

        var columnStart = cursor;
        while (cursor < formula.Length && char.IsAsciiLetter(formula[cursor]))
        {
            cursor++;
        }

        var columnLength = cursor - columnStart;
        if (columnLength is < 1 or > 3)
        {
            return false;
        }

        var absoluteRow = cursor < formula.Length && formula[cursor] == '$';
        if (absoluteRow)
        {
            cursor++;
        }

        var rowStart = cursor;
        while (cursor < formula.Length && char.IsAsciiDigit(formula[cursor]))
        {
            cursor++;
        }

        if (rowStart == cursor ||
            (cursor < formula.Length &&
             (IsReferenceIdentifierCharacter(formula[cursor]) || formula[cursor] == '!')))
        {
            return false;
        }

        var lookahead = cursor;
        while (lookahead < formula.Length && char.IsWhiteSpace(formula[lookahead]))
        {
            lookahead++;
        }

        if (lookahead < formula.Length && formula[lookahead] == '(')
        {
            return false;
        }

        var columnText = formula[columnStart..(columnStart + columnLength)];
        var column = ColumnIndex(columnText);
        if (column > 16_384 ||
            !int.TryParse(
                formula.AsSpan(rowStart, cursor - rowStart),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var row) ||
            row > 1_048_576)
        {
            return false;
        }

        var translatedColumn = absoluteColumn ? column : column + columnOffset;
        var translatedRow = absoluteRow ? row : row + rowOffset;
        if (translatedColumn is < 1 or > 16_384 || translatedRow is < 1 or > 1_048_576)
        {
            throw new InvalidDataException(
                $"Shared formula translation from '{formula}' produced an out-of-range cell reference.");
        }

        if (absoluteColumn)
        {
            target.Append('$');
        }

        target.Append(ColumnName(translatedColumn));
        if (absoluteRow)
        {
            target.Append('$');
        }

        target.Append(translatedRow.ToString(CultureInfo.InvariantCulture));
        index = cursor;
        return true;
    }

    private static bool IsReferenceIdentifierCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '.' or '\\';

    private static int ColumnIndex(string column)
    {
        var result = 0;
        foreach (var character in column)
        {
            result = checked((result * 26) + (char.ToUpperInvariant(character) - 'A' + 1));
        }

        return result;
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

internal sealed record SharedFormulaMaster(string Reference, string Formula);

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
