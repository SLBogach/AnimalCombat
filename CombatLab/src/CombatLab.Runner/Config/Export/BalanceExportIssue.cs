namespace CombatLab.Runner.Config.Export;

public enum BalanceExportIssueSeverity
{
    Warning = 0,
    Error = 1,
}

public sealed record BalanceExportIssue(
    BalanceExportIssueSeverity Severity,
    string Code,
    string Path,
    string Message);
