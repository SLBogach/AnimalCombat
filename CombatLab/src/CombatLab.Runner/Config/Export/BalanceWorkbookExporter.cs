using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Battle.Contracts.Versions;

namespace CombatLab.Runner.Config.Export;

public sealed class BalanceWorkbookExporter
{
    private const int JsonMapHeaderRow = 5;
    private const int JsonMapFirstDataRow = 6;

    private static readonly string[] MapHeaders =
    [
        "Sort Order",
        "Namespace",
        "Object ID",
        "Property Path",
        "Value Type",
        "Live Value",
        "Unit",
        "Source Sheet",
        "Source Cell",
        "Required",
        "Include Runtime",
        "Validation",
        "Notes RU",
    ];

    private static readonly IReadOnlyDictionary<string, string> CatalogNames =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "actions",
            ["effect"] = "effects",
            ["fighter"] = "fighters",
            ["gear"] = "gear",
            ["passive"] = "passives",
            ["tactic"] = "tactics",
        });

    private static readonly IReadOnlyDictionary<string, string> IdProperties =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = "action_id",
            ["effect"] = "effect_id",
            ["fighter"] = "animal_id",
            ["gear"] = "gear_id",
            ["passive"] = "passive_id",
            ["tactic"] = "tactic_id",
        });

    public BalanceWorkbookExportResult Export(string workbookPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);

        var issues = new List<BalanceExportIssue>();
        Sha256Digest? workbookHash = null;
        try
        {
            workbookHash = ComputeWorkbookHash(workbookPath);
            using var workbook = new OpenXmlWorkbookReader(workbookPath);
            return Export(workbook, workbookHash.Value, issues);
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            UnauthorizedAccessException or
            System.Xml.XmlException)
        {
            issues.Add(new BalanceExportIssue(
                BalanceExportIssueSeverity.Error,
                "workbook.unreadable",
                workbookPath,
                exception.Message));
            return new BalanceWorkbookExportResult(
                Array.Empty<byte>(),
                string.Empty,
                workbookHash,
                EmptyEntityCounts(),
                issues);
        }
    }

    private static BalanceWorkbookExportResult Export(
        OpenXmlWorkbookReader workbook,
        Sha256Digest workbookHash,
        List<BalanceExportIssue> issues)
    {
        var mapSheet = workbook.GetSheet("JSON Map");
        ValidateHeaders(mapSheet, issues);
        var rows = ReadMapRows(mapSheet, issues);
        ValidateSortOrders(rows, issues);
        ValidateFormulaTargetsAndSourceResults(workbook, mapSheet, rows, issues);

        var settings = new SortedDictionary<string, JsonScalar>(StringComparer.Ordinal);
        var catalogs = CatalogNames.Keys.ToDictionary(
            namespaceName => namespaceName,
            _ => new Dictionary<string, Dictionary<string, JsonScalar>>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        PopulateRuntimeModel(workbook, rows, settings, catalogs, issues);
        var counts = CountEntities(rows, catalogs);
        var json = WriteCandidateJson(settings, catalogs, issues);
        var mapCsv = WriteMapCsv(rows);

        return new BalanceWorkbookExportResult(json, mapCsv, workbookHash, counts, issues);
    }

    private static void ValidateHeaders(WorkbookSheet sheet, List<BalanceExportIssue> issues)
    {
        for (var index = 0; index < MapHeaders.Length; index++)
        {
            var reference = OpenXmlWorkbookReader.ColumnName(index + 1) +
                JsonMapHeaderRow.ToString(CultureInfo.InvariantCulture);
            var actual = sheet.GetCell(reference).Value;
            if (!string.Equals(actual, MapHeaders[index], StringComparison.Ordinal))
            {
                issues.Add(Error(
                    "map.header",
                    $"JSON Map!{reference}",
                    $"Expected header '{MapHeaders[index]}', but found '{actual}'."));
            }
        }
    }

    private static List<MapRow> ReadMapRows(WorkbookSheet sheet, List<BalanceExportIssue> issues)
    {
        var rows = new List<MapRow>();
        for (var rowNumber = JsonMapFirstDataRow; rowNumber <= sheet.MaximumRow; rowNumber++)
        {
            var values = new string[MapHeaders.Length];
            var hasValue = false;
            for (var index = 0; index < MapHeaders.Length; index++)
            {
                values[index] = sheet.GetCell(
                    OpenXmlWorkbookReader.ColumnName(index + 1) +
                    rowNumber.ToString(CultureInfo.InvariantCulture)).Value;
                hasValue |= values[index].Length > 0;
            }

            if (!hasValue)
            {
                continue;
            }

            var path = $"JSON Map!A{rowNumber}";
            if (!int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sortOrder) ||
                sortOrder <= 0)
            {
                issues.Add(Error("map.sort_order", path, $"Invalid positive Sort Order '{values[0]}'."));
                continue;
            }

            if (!TryReadFlag(values[9], out var required))
            {
                issues.Add(Error("map.required", $"JSON Map!J{rowNumber}", "Required must be 0 or 1."));
                continue;
            }

            if (!TryReadFlag(values[10], out var includeRuntime))
            {
                issues.Add(Error("map.include_runtime", $"JSON Map!K{rowNumber}", "Include Runtime must be 0 or 1."));
                continue;
            }

            var namespaceName = values[1].Trim();
            var objectId = values[2].Trim();
            var propertyPath = values[3].Trim();
            var valueType = values[4].Trim();
            var sourceSheet = values[7].Trim();
            var sourceCellText = values[8].Trim();
            if (namespaceName.Length == 0 ||
                objectId.Length == 0 ||
                propertyPath.Length == 0 ||
                valueType.Length == 0 ||
                sourceSheet.Length == 0 ||
                sourceCellText.Length == 0)
            {
                issues.Add(Error(
                    "map.required_metadata",
                    path,
                    "Namespace, Object ID, Property Path, Value Type, Source Sheet, and Source Cell are required."));
                continue;
            }

            string sourceCell;
            try
            {
                sourceCell = OpenXmlWorkbookReader.NormalizeCellReference(sourceCellText);
            }
            catch (InvalidDataException exception)
            {
                issues.Add(Error("map.source_cell", $"JSON Map!I{rowNumber}", exception.Message));
                continue;
            }

            rows.Add(new MapRow(
                rowNumber,
                sortOrder,
                namespaceName,
                objectId,
                propertyPath,
                valueType,
                values[6],
                sourceSheet,
                sourceCell,
                required,
                includeRuntime,
                values[12]));
        }

        if (rows.Count == 0)
        {
            issues.Add(Error("map.empty", "JSON Map", "JSON Map contains no valid data rows."));
        }

        return rows;
    }

    private static void ValidateSortOrders(IReadOnlyCollection<MapRow> rows, List<BalanceExportIssue> issues)
    {
        var seen = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            if (seen.TryGetValue(row.SortOrder, out var previousRow))
            {
                issues.Add(Error(
                    "map.duplicate_sort_order",
                    $"JSON Map!A{row.RowNumber}",
                    $"Sort Order {row.SortOrder} is already used by row {previousRow}."));
            }
            else
            {
                seen.Add(row.SortOrder, row.RowNumber);
            }
        }

        var expected = 1;
        foreach (var row in rows.OrderBy(row => row.SortOrder))
        {
            if (row.SortOrder != expected)
            {
                issues.Add(Error(
                    "map.sort_order_gap",
                    $"JSON Map!A{row.RowNumber}",
                    $"Expected Sort Order {expected}, but found {row.SortOrder}."));
                expected = row.SortOrder;
            }

            expected++;
        }
    }

    private static void ValidateFormulaTargetsAndSourceResults(
        OpenXmlWorkbookReader workbook,
        WorkbookSheet mapSheet,
        IReadOnlyList<MapRow> rows,
        List<BalanceExportIssue> issues)
    {
        var validationColumns = new Dictionary<string, string>(StringComparer.Ordinal);
        var validatedSourceRows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            WorkbookSheet sourceSheet;
            try
            {
                sourceSheet = workbook.GetSheet(row.SourceSheet);
            }
            catch (InvalidDataException exception)
            {
                issues.Add(Error(
                    "map.source_sheet",
                    $"JSON Map!H{row.RowNumber}",
                    exception.Message));
                continue;
            }

            VerifyDirectFormula(
                mapSheet.GetCell($"F{row.RowNumber}").Formula,
                row.SourceSheet,
                row.SourceCell,
                $"JSON Map!F{row.RowNumber}",
                "map.live_formula",
                issues);

            if (!validationColumns.TryGetValue(row.SourceSheet, out var validationColumn))
            {
                validationColumn = FindValidationColumn(sourceSheet);
                validationColumns.Add(row.SourceSheet, validationColumn);
            }

            if (validationColumn.Length == 0)
            {
                issues.Add(Error(
                    "map.validation_column",
                    row.SourceSheet,
                    "The source sheet does not contain a 'Validation' header in row 5."));
                continue;
            }

            var validationCellReference = validationColumn +
                OpenXmlWorkbookReader.GetRowNumber(row.SourceCell).ToString(CultureInfo.InvariantCulture);
            VerifyDirectFormula(
                mapSheet.GetCell($"L{row.RowNumber}").Formula,
                row.SourceSheet,
                validationCellReference,
                $"JSON Map!L{row.RowNumber}",
                "map.validation_formula",
                issues);

            var validationKey = row.SourceSheet + "!" + validationCellReference;
            if (!validatedSourceRows.Add(validationKey))
            {
                continue;
            }

            var validationCell = sourceSheet.GetCell(validationCellReference);
            var cachedValidation = validationCell.Value.Trim();
            if (row.IncludeRuntime ||
                row.SourceSheet is "Test Builds" or "Expected Matchups")
            {
                ValidateRecalculatedSourceResult(
                    workbook,
                    sourceSheet,
                    row.SourceSheet,
                    OpenXmlWorkbookReader.GetRowNumber(row.SourceCell),
                    validationCell,
                    cachedValidation,
                    issues);
            }
            else if (string.Equals(cachedValidation, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Error(
                    "source.validation_error",
                    validationKey,
                    "Workbook validation returned ERROR."));
            }
            else if (!string.Equals(cachedValidation, "OK", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(Error(
                    "source.validation_result",
                    validationKey,
                    $"Validation result must be OK or ERROR, but found '{cachedValidation}'."));
            }
        }
    }

    private static void ValidateRecalculatedSourceResult(
        OpenXmlWorkbookReader workbook,
        WorkbookSheet sheet,
        string sheetName,
        int rowNumber,
        WorkbookCell validationCell,
        string cachedValidation,
        List<BalanceExportIssue> issues)
    {
        if (!TryRecalculateSourceValidation(
                workbook,
                sheet,
                sheetName,
                rowNumber,
                out var isValid,
                out var expectedFormula))
        {
            issues.Add(Error(
                "source.validation_policy",
                $"{sheetName}!{validationCell.Reference}",
                $"No safe validation policy is registered for export source sheet '{sheetName}'."));
            return;
        }

        if (!FormulaEquals(validationCell.Formula, expectedFormula))
        {
            issues.Add(Error(
                "source.validation_formula",
                $"{sheetName}!{validationCell.Reference}",
                $"Validation formula does not match the supported policy. Expected '{expectedFormula}', but found '{validationCell.Formula ?? string.Empty}'."));
        }

        var recalculatedValidation = isValid ? "OK" : "ERROR";
        if (!string.Equals(cachedValidation, recalculatedValidation, StringComparison.Ordinal))
        {
            issues.Add(Error(
                "source.validation_stale",
                $"{sheetName}!{validationCell.Reference}",
                $"Cached validation is '{cachedValidation}', but recalculation returned '{recalculatedValidation}'."));
        }

        if (!isValid)
        {
            issues.Add(Error(
                "source.validation_error",
                $"{sheetName}!{validationCell.Reference}",
                "Recalculated workbook validation returned ERROR."));
        }
    }

    private static bool TryRecalculateSourceValidation(
        OpenXmlWorkbookReader workbook,
        WorkbookSheet sheet,
        string sheetName,
        int rowNumber,
        out bool isValid,
        out string expectedFormula)
    {
        var row = rowNumber.ToString(CultureInfo.InvariantCulture);
        switch (sheetName)
        {
            case "Global Config":
                isValid = CellEquals(sheet, "E", rowNumber, "string") ||
                    CellEquals(sheet, "E", rowNumber, "bool") ||
                    (GreaterThanOrEqual(sheet, "D", rowNumber, "G", rowNumber) &&
                     LessThanOrEqual(sheet, "D", rowNumber, "H", rowNumber));
                expectedFormula = $"IF(OR(E{row}=\"string\",E{row}=\"bool\",AND(D{row}>=G{row},D{row}<=H{row})),\"OK\",\"ERROR\")";
                return true;
            case "Fighter Stats":
                isValid = IsNonEmpty(sheet, "A", rowNumber) &&
                    GreaterThan(sheet, "D", rowNumber, 0) &&
                    GreaterThan(sheet, "Q", rowNumber, 0) &&
                    GreaterThanOrEqual(sheet, "R", rowNumber, 0) &&
                    GreaterThanOrEqual(sheet, "T", rowNumber, 0) &&
                    LessThanOrEqual(sheet, "T", rowNumber, "U", rowNumber) &&
                    GreaterThan(sheet, "V", rowNumber, 0);
                expectedFormula = $"IF(AND(A{row}<>\"\",D{row}>0,Q{row}>0,R{row}>=0,T{row}>=0,T{row}<=U{row},V{row}>0),\"OK\",\"ERROR\")";
                return true;
            case "Actions":
                isValid = IsNonEmpty(sheet, "A", rowNumber) &&
                    LessThanOrEqual(sheet, "H", rowNumber, "I", rowNumber) &&
                    LessThanOrEqual(sheet, "J", rowNumber, "K", rowNumber) &&
                    LessThanOrEqual(sheet, "S", rowNumber, "R", rowNumber) &&
                    LessThanOrEqual(sheet, "R", rowNumber, "T", rowNumber) &&
                    LessThanOrEqual(sheet, "Y", rowNumber, "X", rowNumber) &&
                    LessThanOrEqual(sheet, "X", rowNumber, "Z", rowNumber) &&
                    LessThanOrEqual(sheet, "AT", rowNumber, "AS", rowNumber) &&
                    LessThanOrEqual(sheet, "AS", rowNumber, "AU", rowNumber) &&
                    LessThanOrEqual(sheet, "AW", rowNumber, "AX", rowNumber) &&
                    GreaterThanOrEqual(sheet, "AY", rowNumber, 1) &&
                    GreaterThanOrEqual(sheet, "L", rowNumber, 0) &&
                    GreaterThanOrEqual(sheet, "M", rowNumber, 0);
                expectedFormula = $"IF(AND(A{row}<>\"\",H{row}<=I{row},J{row}<=K{row},S{row}<=R{row},R{row}<=T{row},Y{row}<=X{row},X{row}<=Z{row},AT{row}<=AS{row},AS{row}<=AU{row},AW{row}<=AX{row},AY{row}>=1,L{row}>=0,M{row}>=0),\"OK\",\"ERROR\")";
                return true;
            case "Passives":
                isValid = IsNonEmpty(sheet, "A", rowNumber) &&
                    IsNonEmpty(sheet, "B", rowNumber) &&
                    GreaterThanOrEqual(sheet, "P", rowNumber, 1) &&
                    GreaterThanOrEqual(sheet, "Q", rowNumber, 1) &&
                    GreaterThanOrEqual(sheet, "O", rowNumber, 1) &&
                    GreaterThanOrEqual(sheet, "S", rowNumber, 250) &&
                    LessThanOrEqual(sheet, "S", rowNumber, 3000);
                expectedFormula = $"IF(AND(A{row}<>\"\",B{row}<>\"\",P{row}>=1,Q{row}>=1,O{row}>=1,S{row}>=250,S{row}<=3000),\"OK\",\"ERROR\")";
                return true;
            case "Effects":
                isValid = IsNonEmpty(sheet, "A", rowNumber) &&
                    GreaterThanOrEqual(sheet, "C", rowNumber, 0) &&
                    GreaterThanOrEqual(sheet, "G", rowNumber, 1) &&
                    GreaterThanOrEqual(sheet, "P", rowNumber, 1) &&
                    GreaterThanOrEqual(sheet, "Q", rowNumber, 1);
                expectedFormula = $"IF(AND(A{row}<>\"\",C{row}>=0,G{row}>=1,P{row}>=1,Q{row}>=1),\"OK\",\"ERROR\")";
                return true;
            case "Tactics":
                isValid = IsNonEmpty(sheet, "A", rowNumber) &&
                    IsInside(sheet, "C", rowNumber, 250, 3000) &&
                    IsInside(sheet, "D", rowNumber, 250, 3000) &&
                    IsInside(sheet, "K", rowNumber, 250, 3000) &&
                    GreaterThanOrEqual(sheet, "R", rowNumber, 1) &&
                    IsInside(sheet, "S", rowNumber, 250, 1000);
                expectedFormula = $"IF(AND(A{row}<>\"\",C{row}>=250,C{row}<=3000,D{row}>=250,D{row}<=3000,K{row}>=250,K{row}<=3000,R{row}>=1,S{row}>=250,S{row}<=1000),\"OK\",\"ERROR\")";
                return true;
            case "Gear":
                isValid = IsNonEmpty(sheet, "A", rowNumber) &&
                    (CellEquals(sheet, "C", rowNumber, "Offense") ||
                     CellEquals(sheet, "C", rowNumber, "Defense") ||
                     CellEquals(sheet, "C", rowNumber, "Utility")) &&
                    IsNonEmpty(sheet, "D", rowNumber) &&
                    GreaterThan(sheet, "K", rowNumber, 0);
                expectedFormula = $"IF(AND(A{row}<>\"\",OR(C{row}=\"Offense\",C{row}=\"Defense\",C{row}=\"Utility\"),D{row}<>\"\",K{row}>0),\"OK\",\"ERROR\")";
                return true;
            case "Test Builds":
                var actions = workbook.GetSheet("Actions");
                var passives = workbook.GetSheet("Passives");
                var gear = workbook.GetSheet("Gear");
                var tactics = workbook.GetSheet("Tactics");
                var animalId = CellValue(sheet, "C", rowNumber);
                var special1 = CellValue(sheet, "D", rowNumber);
                var special2 = CellValue(sheet, "E", rowNumber);
                var passiveId = CellValue(sheet, "F", rowNumber);
                var offenseGear = CellValue(sheet, "G", rowNumber);
                var defenseGear = CellValue(sheet, "H", rowNumber);
                var utilityGear = CellValue(sheet, "I", rowNumber);
                var tacticId = CellValue(sheet, "J", rowNumber);
                isValid = !string.Equals(special1, special2, StringComparison.Ordinal) &&
                    CountMatches(6, 29, candidateRow =>
                        CellEquals(actions, "A", candidateRow, special1) &&
                        CellEquals(actions, "B", candidateRow, animalId) &&
                        CellEquals(actions, "D", candidateRow, "Special")) == 1 &&
                    CountMatches(6, 29, candidateRow =>
                        CellEquals(actions, "A", candidateRow, special2) &&
                        CellEquals(actions, "B", candidateRow, animalId) &&
                        CellEquals(actions, "D", candidateRow, "Special")) == 1 &&
                    CountMatches(6, 11, candidateRow =>
                        CellEquals(passives, "A", candidateRow, passiveId) &&
                        CellEquals(passives, "B", candidateRow, animalId)) == 1 &&
                    CountMatches(6, 14, candidateRow =>
                        CellEquals(gear, "A", candidateRow, offenseGear) &&
                        CellEquals(gear, "C", candidateRow, "Offense")) == 1 &&
                    CountMatches(6, 14, candidateRow =>
                        CellEquals(gear, "A", candidateRow, defenseGear) &&
                        CellEquals(gear, "C", candidateRow, "Defense")) == 1 &&
                    CountMatches(6, 14, candidateRow =>
                        CellEquals(gear, "A", candidateRow, utilityGear) &&
                        CellEquals(gear, "C", candidateRow, "Utility")) == 1 &&
                    CountMatches(6, 9, candidateRow =>
                        CellEquals(tactics, "A", candidateRow, tacticId)) == 1;
                expectedFormula = $"IF(AND(D{row}<>E{row},COUNTIFS('Actions'!$A$6:$A$29,D{row},'Actions'!$B$6:$B$29,C{row},'Actions'!$D$6:$D$29,\"Special\")=1,COUNTIFS('Actions'!$A$6:$A$29,E{row},'Actions'!$B$6:$B$29,C{row},'Actions'!$D$6:$D$29,\"Special\")=1,COUNTIFS('Passives'!$A$6:$A$11,F{row},'Passives'!$B$6:$B$11,C{row})=1,COUNTIFS('Gear'!$A$6:$A$14,G{row},'Gear'!$C$6:$C$14,\"Offense\")=1,COUNTIFS('Gear'!$A$6:$A$14,H{row},'Gear'!$C$6:$C$14,\"Defense\")=1,COUNTIFS('Gear'!$A$6:$A$14,I{row},'Gear'!$C$6:$C$14,\"Utility\")=1,COUNTIF('Tactics'!$A$6:$A$9,J{row})=1),\"OK\",\"ERROR\")";
                return true;
            case "Expected Matchups":
                var builds = workbook.GetSheet("Test Builds");
                var buildA = CellValue(sheet, "N", rowNumber);
                var buildB = CellValue(sheet, "O", rowNumber);
                isValid = GreaterThanOrEqual(sheet, "D", rowNumber, "E", rowNumber) &&
                    LessThanOrEqual(sheet, "D", rowNumber, "F", rowNumber) &&
                    LessThanOrEqual(sheet, "E", rowNumber, "F", rowNumber) &&
                    LessThanOrEqual(sheet, "G", rowNumber, 0.02m) &&
                    LessThanOrEqual(sheet, "H", rowNumber, 0.02m) &&
                    LessThan(sheet, "I", rowNumber, "J", rowNumber) &&
                    LessThanOrEqual(sheet, "K", rowNumber, "L", rowNumber) &&
                    CountMatches(6, 17, candidateRow =>
                        CellEquals(builds, "A", candidateRow, buildA)) == 1 &&
                    CountMatches(6, 17, candidateRow =>
                        CellEquals(builds, "A", candidateRow, buildB)) == 1;
                expectedFormula = $"IF(AND(D{row}>=E{row},D{row}<=F{row},E{row}<=F{row},G{row}<=0.02,H{row}<=0.02,I{row}<J{row},K{row}<=L{row},COUNTIF('Test Builds'!$A$6:$A$17,N{row})=1,COUNTIF('Test Builds'!$A$6:$A$17,O{row})=1),\"OK\",\"ERROR\")";
                return true;
            default:
                isValid = false;
                expectedFormula = string.Empty;
                return false;
        }
    }

    private static bool FormulaEquals(string? actual, string expected)
    {
        if (actual is null)
        {
            return false;
        }

        static string Normalize(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.StartsWith("=", StringComparison.Ordinal))
            {
                trimmed = trimmed[1..];
            }

            return trimmed;
        }

        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.Ordinal);
    }

    private static bool CellEquals(
        WorkbookSheet sheet,
        string column,
        int rowNumber,
        string expected) =>
        string.Equals(CellValue(sheet, column, rowNumber), expected, StringComparison.Ordinal);

    private static string CellValue(WorkbookSheet sheet, string column, int rowNumber) =>
        sheet.GetCell(column + rowNumber.ToString(CultureInfo.InvariantCulture)).Value;

    private static bool IsNonEmpty(WorkbookSheet sheet, string column, int rowNumber) =>
        CellValue(sheet, column, rowNumber).Length > 0;

    private static int CountMatches(
        int firstRow,
        int lastRow,
        Func<int, bool> predicate)
    {
        var count = 0;
        for (var row = firstRow; row <= lastRow; row++)
        {
            if (predicate(row))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsInside(
        WorkbookSheet sheet,
        string column,
        int rowNumber,
        decimal minimum,
        decimal maximum) =>
        GreaterThanOrEqual(sheet, column, rowNumber, minimum) &&
        LessThanOrEqual(sheet, column, rowNumber, maximum);

    private static bool GreaterThan(
        WorkbookSheet sheet,
        string column,
        int rowNumber,
        decimal boundary) =>
        TryReadExcelNumber(sheet, column, rowNumber, out var value) && value > boundary;

    private static bool GreaterThanOrEqual(
        WorkbookSheet sheet,
        string column,
        int rowNumber,
        decimal boundary) =>
        TryReadExcelNumber(sheet, column, rowNumber, out var value) && value >= boundary;

    private static bool LessThanOrEqual(
        WorkbookSheet sheet,
        string column,
        int rowNumber,
        decimal boundary) =>
        TryReadExcelNumber(sheet, column, rowNumber, out var value) && value <= boundary;

    private static bool GreaterThanOrEqual(
        WorkbookSheet sheet,
        string leftColumn,
        int leftRow,
        string rightColumn,
        int rightRow) =>
        TryReadExcelNumber(sheet, leftColumn, leftRow, out var left) &&
        TryReadExcelNumber(sheet, rightColumn, rightRow, out var right) &&
        left >= right;

    private static bool LessThanOrEqual(
        WorkbookSheet sheet,
        string leftColumn,
        int leftRow,
        string rightColumn,
        int rightRow) =>
        TryReadExcelNumber(sheet, leftColumn, leftRow, out var left) &&
        TryReadExcelNumber(sheet, rightColumn, rightRow, out var right) &&
        left <= right;

    private static bool LessThan(
        WorkbookSheet sheet,
        string leftColumn,
        int leftRow,
        string rightColumn,
        int rightRow) =>
        TryReadExcelNumber(sheet, leftColumn, leftRow, out var left) &&
        TryReadExcelNumber(sheet, rightColumn, rightRow, out var right) &&
        left < right;

    private static bool TryReadExcelNumber(
        WorkbookSheet sheet,
        string column,
        int rowNumber,
        out decimal value)
    {
        var raw = CellValue(sheet, column, rowNumber);
        if (raw.Length == 0)
        {
            value = 0;
            return true;
        }

        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void PopulateRuntimeModel(
        OpenXmlWorkbookReader workbook,
        IEnumerable<MapRow> mapRows,
        SortedDictionary<string, JsonScalar> settings,
        Dictionary<string, Dictionary<string, Dictionary<string, JsonScalar>>> catalogs,
        List<BalanceExportIssue> issues)
    {
        foreach (var row in mapRows.Where(row => row.IncludeRuntime).OrderBy(row => row.SortOrder))
        {
            if (row.NamespaceName == "global")
            {
                if (!string.Equals(row.ObjectId, "root", StringComparison.Ordinal))
                {
                    issues.Add(Error(
                        "map.global_object_id",
                        $"JSON Map!C{row.RowNumber}",
                        "Runtime global settings must use Object ID 'root'."));
                    continue;
                }
            }
            else if (!CatalogNames.ContainsKey(row.NamespaceName))
            {
                issues.Add(Error(
                    "map.runtime_namespace",
                    $"JSON Map!B{row.RowNumber}",
                    $"Unsupported runtime namespace '{row.NamespaceName}'."));
                continue;
            }

            WorkbookSheet sourceSheet;
            try
            {
                sourceSheet = workbook.GetSheet(row.SourceSheet);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            var rawValue = sourceSheet.GetCell(row.SourceCell).Value;
            if (!TryConvertValue(row, rawValue, issues, out var converted))
            {
                continue;
            }

            if (converted is null)
            {
                continue;
            }

            if (row.NamespaceName == "global")
            {
                if (!settings.TryAdd(row.PropertyPath, converted.Value))
                {
                    issues.Add(Error(
                        "map.duplicate_property",
                        $"JSON Map!D{row.RowNumber}",
                        $"Duplicate settings property '{row.PropertyPath}'."));
                }

                continue;
            }

            var entities = catalogs[row.NamespaceName];
            if (!entities.TryGetValue(row.ObjectId, out var properties))
            {
                properties = new Dictionary<string, JsonScalar>(StringComparer.Ordinal);
                entities.Add(row.ObjectId, properties);
            }

            if (!properties.TryAdd(row.PropertyPath, converted.Value))
            {
                issues.Add(Error(
                    "map.duplicate_property",
                    $"JSON Map!D{row.RowNumber}",
                    $"Duplicate property '{row.PropertyPath}' for '{row.NamespaceName}/{row.ObjectId}'."));
            }
        }
    }

    private static byte[] WriteCandidateJson(
        SortedDictionary<string, JsonScalar> settings,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, JsonScalar>>> catalogs,
        List<BalanceExportIssue> issues)
    {
        var output = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        });

        writer.WriteStartObject();
        foreach (var catalogName in CatalogNames
            .Where(pair => pair.Key != "tactic")
            .OrderBy(pair => pair.Value, StringComparer.Ordinal))
        {
            writer.WritePropertyName(catalogName.Value);
            writer.WriteStartArray();
            var idProperty = IdProperties[catalogName.Key];
            var entities = catalogs[catalogName.Key];
            foreach (var entity in entities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!entity.Value.TryGetValue(idProperty, out var id) || id.Kind != JsonScalarKind.String)
                {
                    issues.Add(Error(
                        "map.missing_entity_id",
                        $"{catalogName.Key}/{entity.Key}",
                        $"Entity is missing string property '{idProperty}'."));
                }
                else if (!string.Equals(id.StringValue, entity.Key, StringComparison.Ordinal))
                {
                    issues.Add(Error(
                        "map.entity_id_mismatch",
                        $"{catalogName.Key}/{entity.Key}/{idProperty}",
                        $"ID property '{id.StringValue}' does not match Object ID '{entity.Key}'."));
                }

                writer.WriteStartObject();
                foreach (var property in entity.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteScalar(writer, property.Value);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WritePropertyName("settings");
        writer.WriteStartObject();
        foreach (var setting in settings)
        {
            writer.WritePropertyName(setting.Key);
            WriteScalar(writer, setting.Value);
        }

        writer.WriteEndObject();

        writer.WritePropertyName("tactics");
        writer.WriteStartArray();
        var tactics = catalogs["tactic"];
        foreach (var entity in tactics.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var idProperty = IdProperties["tactic"];
            if (!entity.Value.TryGetValue(idProperty, out var id) ||
                id.Kind != JsonScalarKind.String ||
                !string.Equals(id.StringValue, entity.Key, StringComparison.Ordinal))
            {
                issues.Add(Error(
                    "map.entity_id_mismatch",
                    $"tactic/{entity.Key}/{idProperty}",
                    $"ID property must be the string '{entity.Key}'."));
            }

            writer.WriteStartObject();
            foreach (var property in entity.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Key);
                WriteScalar(writer, property.Value);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return output.WrittenSpan.ToArray();
    }

    private static IReadOnlyDictionary<string, int> CountEntities(
        IReadOnlyList<MapRow> rows,
        IReadOnlyDictionary<string, Dictionary<string, Dictionary<string, JsonScalar>>> catalogs)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["actions"] = catalogs["action"].Count,
            ["builds"] = rows
                .Where(row => row.NamespaceName == "test_build")
                .Select(row => row.ObjectId)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            ["effects"] = catalogs["effect"].Count,
            ["fighters"] = catalogs["fighter"].Count,
            ["gear"] = catalogs["gear"].Count,
            ["passives"] = catalogs["passive"].Count,
            ["tactics"] = catalogs["tactic"].Count,
        };
        return result;
    }

    private static IReadOnlyDictionary<string, int> EmptyEntityCounts() =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["actions"] = 0,
            ["builds"] = 0,
            ["effects"] = 0,
            ["fighters"] = 0,
            ["gear"] = 0,
            ["passives"] = 0,
            ["tactics"] = 0,
        };

    private static bool TryConvertValue(
        MapRow row,
        string rawValue,
        List<BalanceExportIssue> issues,
        out JsonScalar? result)
    {
        result = null;
        if (rawValue.Length == 0)
        {
            if (!row.Required)
            {
                return true;
            }

            if (row.ValueType == "string")
            {
                result = JsonScalar.FromString(string.Empty);
                return true;
            }

            issues.Add(Error(
                "source.required_blank",
                $"{row.SourceSheet}!{row.SourceCell}",
                $"Required {row.ValueType} property '{row.PropertyPath}' is blank."));
            return false;
        }

        switch (row.ValueType)
        {
            case "string":
                result = JsonScalar.FromString(rawValue);
                return true;
            case "int":
            case "fp":
                if (!long.TryParse(rawValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
                {
                    issues.Add(Error(
                        "source.integer",
                        $"{row.SourceSheet}!{row.SourceCell}",
                        $"Property '{row.PropertyPath}' must be an integer, but found '{rawValue}'."));
                    return false;
                }

                result = JsonScalar.FromInteger(integer);
                return true;
            case "bool":
                if (!TryReadFlag(rawValue, out var boolean))
                {
                    issues.Add(Error(
                        "source.boolean",
                        $"{row.SourceSheet}!{row.SourceCell}",
                        $"Property '{row.PropertyPath}' must be 0 or 1, but found '{rawValue}'."));
                    return false;
                }

                result = JsonScalar.FromBoolean(boolean);
                return true;
            default:
                issues.Add(Error(
                    "map.value_type",
                    $"JSON Map!E{row.RowNumber}",
                    $"Unsupported runtime Value Type '{row.ValueType}'."));
                return false;
        }
    }

    private static string FindValidationColumn(WorkbookSheet sheet)
    {
        const int maximumColumnsToInspect = 256;
        for (var column = 1; column <= maximumColumnsToInspect; column++)
        {
            var columnName = OpenXmlWorkbookReader.ColumnName(column);
            if (string.Equals(
                sheet.GetCell(columnName + JsonMapHeaderRow.ToString(CultureInfo.InvariantCulture)).Value,
                "Validation",
                StringComparison.Ordinal))
            {
                return columnName;
            }
        }

        return string.Empty;
    }

    private static void VerifyDirectFormula(
        string? formula,
        string expectedSheet,
        string expectedCell,
        string issuePath,
        string issueCode,
        List<BalanceExportIssue> issues)
    {
        if (!TryParseDirectReference(formula, out var actualSheet, out var actualCell) ||
            !string.Equals(actualSheet, expectedSheet, StringComparison.Ordinal) ||
            !string.Equals(actualCell, expectedCell, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error(
                issueCode,
                issuePath,
                $"Formula must directly reference '{expectedSheet}'!{expectedCell}, but found '{formula ?? string.Empty}'."));
        }
    }

    private static bool TryParseDirectReference(string? formula, out string sheet, out string cell)
    {
        sheet = string.Empty;
        cell = string.Empty;
        if (string.IsNullOrWhiteSpace(formula))
        {
            return false;
        }

        var value = formula.Trim();
        if (value[0] == '=')
        {
            value = value[1..];
        }

        var bang = value.LastIndexOf('!');
        if (bang <= 0 || bang == value.Length - 1 || value.IndexOf('!', StringComparison.Ordinal) != bang)
        {
            return false;
        }

        var sheetToken = value[..bang];
        if (sheetToken.StartsWith('\'') && sheetToken.EndsWith('\'') && sheetToken.Length >= 2)
        {
            sheetToken = sheetToken[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }
        else if (sheetToken.Contains('\'') || sheetToken.Contains('[') || sheetToken.Contains(']'))
        {
            return false;
        }

        try
        {
            cell = OpenXmlWorkbookReader.NormalizeCellReference(value[(bang + 1)..]);
        }
        catch (InvalidDataException)
        {
            return false;
        }

        sheet = sheetToken;
        return true;
    }

    private static bool TryReadFlag(string raw, out bool value)
    {
        switch (raw.Trim())
        {
            case "0":
                value = false;
                return true;
            case "1":
                value = true;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static void WriteScalar(Utf8JsonWriter writer, JsonScalar scalar)
    {
        switch (scalar.Kind)
        {
            case JsonScalarKind.String:
                writer.WriteStringValue(scalar.StringValue);
                break;
            case JsonScalarKind.Integer:
                writer.WriteNumberValue(scalar.IntegerValue);
                break;
            case JsonScalarKind.Boolean:
                writer.WriteBooleanValue(scalar.BooleanValue);
                break;
            default:
                throw new InvalidOperationException($"Unknown JSON scalar kind '{scalar.Kind}'.");
        }
    }

    private static string WriteMapCsv(IEnumerable<MapRow> rows)
    {
        var builder = new StringBuilder();
        builder.Append("sort_order,namespace,object_id,property_path,value_type,unit,source_sheet,source_cell,required,include_runtime,notes_ru\n");
        foreach (var row in rows.OrderBy(row => row.SortOrder))
        {
            AppendCsvRow(
                builder,
                row.SortOrder.ToString(CultureInfo.InvariantCulture),
                row.NamespaceName,
                row.ObjectId,
                row.PropertyPath,
                row.ValueType,
                row.Unit,
                row.SourceSheet,
                row.SourceCell,
                row.Required ? "1" : "0",
                row.IncludeRuntime ? "1" : "0",
                row.Notes);
        }

        return builder.ToString();
    }

    private static void AppendCsvRow(StringBuilder builder, params string[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            var value = values[index];
            if (value.IndexOfAny([',', '"', '\r', '\n']) >= 0)
            {
                builder.Append('"');
                builder.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
                builder.Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }

        builder.Append('\n');
    }

    private static Sha256Digest ComputeWorkbookHash(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return new Sha256Digest("sha256:" + Convert.ToHexString(hash).ToLowerInvariant());
    }

    private static BalanceExportIssue Error(string code, string path, string message) =>
        new(BalanceExportIssueSeverity.Error, code, path, message);

    private sealed record MapRow(
        int RowNumber,
        int SortOrder,
        string NamespaceName,
        string ObjectId,
        string PropertyPath,
        string ValueType,
        string Unit,
        string SourceSheet,
        string SourceCell,
        bool Required,
        bool IncludeRuntime,
        string Notes);

    private enum JsonScalarKind
    {
        String,
        Integer,
        Boolean,
    }

    private readonly record struct JsonScalar(
        JsonScalarKind Kind,
        string StringValue,
        long IntegerValue,
        bool BooleanValue)
    {
        public static JsonScalar FromString(string value) =>
            new(JsonScalarKind.String, value, 0, false);

        public static JsonScalar FromInteger(long value) =>
            new(JsonScalarKind.Integer, string.Empty, value, false);

        public static JsonScalar FromBoolean(bool value) =>
            new(JsonScalarKind.Boolean, string.Empty, 0, value);
    }
}
