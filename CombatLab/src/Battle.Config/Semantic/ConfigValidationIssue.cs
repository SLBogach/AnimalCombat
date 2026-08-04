namespace Battle.Config.Semantic;

public sealed class ConfigValidationIssue
{
    public ConfigValidationIssue(
        string code,
        string path,
        string message,
        ConfigValidationSeverity severity = ConfigValidationSeverity.Error)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A validation code is required.", nameof(code));
        }

        Code = code;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Severity = severity;
    }

    public string Code { get; }

    public string Path { get; }

    public string Message { get; }

    public ConfigValidationSeverity Severity { get; }
}
