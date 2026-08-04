using System.IO.Compression;
using System.Xml.Linq;
using CombatLab.Runner.Config.Export;

namespace CombatLab.IntegrationTests;

public sealed class BalanceWorkbookExporterTests
{
    private static readonly XNamespace SpreadsheetNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void CurrentWorkbookPassesRecalculatedValidationPolicies()
    {
        var result = new BalanceWorkbookExporter().Export(SourceWorkbookPath());

        Assert.True(result.IsSuccess, string.Join(Environment.NewLine, result.Issues.Select(FormatIssue)));
        Assert.DoesNotContain(result.Issues, issue => issue.Code is "source.validation_stale" or "source.validation_formula");
    }

    [Theory]
    [InlineData("xl/worksheets/sheet2.xml", "D10", "-1", "Global Config!K10")]
    [InlineData("xl/worksheets/sheet9.xml", "G6", "missing_gear", "Test Builds!N6")]
    [InlineData("xl/worksheets/sheet10.xml", "N6", "missing_build", "Expected Matchups!S6")]
    public void StaleCachedValidationIsRejected(
        string worksheetPart,
        string changedCell,
        string changedValue,
        string expectedValidationPath)
    {
        var temporaryWorkbook = Path.Combine(
            Path.GetTempPath(),
            "combat-lab-stale-validation-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            CopyWithReadSharing(SourceWorkbookPath(), temporaryWorkbook);
            ChangeCellWithoutUpdatingFormulaCache(
                temporaryWorkbook,
                worksheetPart,
                changedCell,
                changedValue);

            var result = new BalanceWorkbookExporter().Export(temporaryWorkbook);

            Assert.False(result.IsSuccess);
            var issue = Assert.Single(result.Issues, item => item.Code == "source.validation_stale");
            Assert.Equal(expectedValidationPath, issue.Path);
            Assert.Contains("recalculation returned 'ERROR'", issue.Message, StringComparison.Ordinal);
            Assert.Contains(result.Issues, item =>
                item.Code == "source.validation_error" && item.Path == expectedValidationPath);
        }
        finally
        {
            File.Delete(temporaryWorkbook);
        }
    }

    private static void ChangeCellWithoutUpdatingFormulaCache(
        string workbookPath,
        string worksheetPart,
        string cellReference,
        string changedValue)
    {
        using var archive = ZipFile.Open(workbookPath, ZipArchiveMode.Update);
        var entry = archive.GetEntry(worksheetPart)
            ?? throw new InvalidDataException($"Workbook part '{worksheetPart}' was not found.");
        XDocument document;
        using (var input = entry.Open())
        {
            document = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        }

        var changedCell = document
            .Descendants(SpreadsheetNamespace + "c")
            .Single(cell => string.Equals((string?)cell.Attribute("r"), cellReference, StringComparison.Ordinal));
        var value = changedCell.Element(SpreadsheetNamespace + "v")
            ?? throw new InvalidDataException($"Cell {cellReference} has no stored value.");
        value.Value = changedValue;

        entry.Delete();
        var replacement = archive.CreateEntry(worksheetPart, CompressionLevel.Optimal);
        using var output = replacement.Open();
        document.Save(output, SaveOptions.DisableFormatting);
    }

    private static void CopyWithReadSharing(string source, string destination)
    {
        using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static string SourceWorkbookPath() =>
        Path.Combine(RepositoryRoot(), "config", "source", "Combat_Balance_Workbook_v0.1.xlsx");

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

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln from the test output directory.");
    }

    private static string FormatIssue(BalanceExportIssue issue) =>
        $"{issue.Severity} {issue.Code} {issue.Path}: {issue.Message}";
}
