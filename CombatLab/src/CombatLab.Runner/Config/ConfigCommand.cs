using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Battle.Config;
using Battle.Config.Compiler;
using Battle.Config.Manifest;
using Battle.Config.Schema;
using Battle.Config.Semantic;
using CombatLab.Runner.Config.Export;

namespace CombatLab.Runner.Config;

public static class ConfigCommand
{
    public const int SuccessExitCode = 0;
    public const int UsageOrInputExitCode = 2;
    public const int InvalidConfigExitCode = 20;

    private const string ExporterVersion = "0.1.0+wp04";
    private const string ArtifactBaseName = "combat.balance.v0.1";

    public static int Execute(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        try
        {
            if (!TryNormalizeCommand(arguments, out var command, out var optionArguments))
            {
                WriteUsage(standardError);
                return UsageOrInputExitCode;
            }

            return command switch
            {
                "export" => Export(optionArguments, standardOutput, standardError, workingDirectory),
                "validate" => Validate(optionArguments, standardOutput, standardError, workingDirectory),
                _ => UsageOrInputExitCode,
            };
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            standardError.WriteLine($"Input error: {exception.Message}");
            return UsageOrInputExitCode;
        }
    }

    private static int Export(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        string workingDirectory)
    {
        if (!TryParseOptions(arguments, ["--workbook", "--output"], error, out var options))
        {
            return UsageOrInputExitCode;
        }

        var repositoryRoot = FindRepositoryRoot(workingDirectory);
        var workbookPath = ResolvePath(
            options.GetValueOrDefault("--workbook") ??
                Path.Combine(repositoryRoot, "config", "source", "Combat_Balance_Workbook_v0.1.xlsx"),
            workingDirectory);
        var outputDirectory = ResolvePath(
            options.GetValueOrDefault("--output") ??
                Path.Combine(repositoryRoot, "config", "generated"),
            workingDirectory);

        if (!File.Exists(workbookPath))
        {
            error.WriteLine($"Workbook not found: {workbookPath}");
            return UsageOrInputExitCode;
        }

        var export = new BalanceWorkbookExporter().Export(workbookPath);
        WriteIssues(export.Issues, error);
        if (!export.IsSuccess || export.SourceWorkbookHash is null)
        {
            WriteValidationArtifact(outputDirectory, export.Issues, Array.Empty<ConfigValidationIssue>(), null, export.SourceWorkbookHash?.Value);
            return InvalidConfigExitCode;
        }

        var compilation = new BattleConfigCompiler().Compile(export.CandidateJson);
        WriteIssues(compilation.Issues, error);
        if (!compilation.IsSuccess || compilation.Config is null || compilation.ConfigHash is null)
        {
            WriteValidationArtifact(
                outputDirectory,
                export.Issues,
                compilation.Issues,
                compilation.ConfigHash?.Value,
                export.SourceWorkbookHash.Value.Value);
            return InvalidConfigExitCode;
        }

        var warningCount = export.Issues.Count(issue => issue.Severity == BalanceExportIssueSeverity.Warning) +
            compilation.Issues.Count(issue => issue.Severity == ConfigValidationSeverity.Warning);
        var counts = new ConfigEntityCounts(
            export.EntityCounts["fighters"],
            export.EntityCounts["actions"],
            export.EntityCounts["passives"],
            export.EntityCounts["effects"],
            export.EntityCounts["tactics"],
            export.EntityCounts["gear"],
            export.EntityCounts["builds"]);
        var manifest = ConfigManifest.Create(
            compilation.Config.Reference,
            export.SourceWorkbookHash.Value,
            ExporterVersion,
            DateTimeOffset.UtcNow,
            counts,
            warningCount);

        Directory.CreateDirectory(outputDirectory);
        WriteAtomic(Path.Combine(outputDirectory, ArtifactBaseName + ".json"), compilation.GetCanonicalJson());
        WriteAtomic(Path.Combine(outputDirectory, ArtifactBaseName + ".manifest.json"), ConfigManifestJson.Write(manifest));
        WriteAtomic(
            Path.Combine(repositoryRoot, "schemas", "balance", "v0.1", "combat.balance.schema.json"),
            BalanceSchemaJson.Write());
        WriteAtomic(
            Path.Combine(outputDirectory, ArtifactBaseName + ".map.csv"),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(export.MapCsv));
        WriteValidationArtifact(
            outputDirectory,
            export.Issues,
            compilation.Issues,
            compilation.ConfigHash.Value.Value,
            export.SourceWorkbookHash.Value.Value);

        output.WriteLine($"Exported {ArtifactBaseName} to {outputDirectory}");
        output.WriteLine($"Config hash: {compilation.ConfigHash.Value.Value}");
        return SuccessExitCode;
    }

    private static int Validate(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        string workingDirectory)
    {
        if (!TryParseOptions(arguments, ["--config", "--manifest"], error, out var options))
        {
            return UsageOrInputExitCode;
        }

        var repositoryRoot = FindRepositoryRoot(workingDirectory);
        var generatedDirectory = Path.Combine(repositoryRoot, "config", "generated");
        var configPath = ResolvePath(
            options.GetValueOrDefault("--config") ?? Path.Combine(generatedDirectory, ArtifactBaseName + ".json"),
            workingDirectory);
        var manifestPath = ResolvePath(
            options.GetValueOrDefault("--manifest") ?? Path.Combine(generatedDirectory, ArtifactBaseName + ".manifest.json"),
            workingDirectory);

        if (!File.Exists(configPath) || !File.Exists(manifestPath))
        {
            if (!File.Exists(configPath))
            {
                error.WriteLine($"Config not found: {configPath}");
            }

            if (!File.Exists(manifestPath))
            {
                error.WriteLine($"Manifest not found: {manifestPath}");
            }

            return UsageOrInputExitCode;
        }

        var result = new BattleConfigLoader().Load(
            File.ReadAllBytes(configPath),
            File.ReadAllBytes(manifestPath));
        WriteIssues(result.Issues, error);
        if (!result.IsSuccess || result.Config is null)
        {
            return InvalidConfigExitCode;
        }

        output.WriteLine($"Config is valid: {result.Config.Reference.ConfigHash.Value}");
        return SuccessExitCode;
    }

    private static bool TryNormalizeCommand(
        IReadOnlyList<string> arguments,
        out string command,
        out IReadOnlyList<string> options)
    {
        command = string.Empty;
        options = Array.Empty<string>();
        if (arguments.Count == 0)
        {
            return false;
        }

        if (string.Equals(arguments[0], "export-config", StringComparison.Ordinal))
        {
            command = "export";
            options = arguments.Skip(1).ToArray();
            return true;
        }

        if (string.Equals(arguments[0], "validate-config", StringComparison.Ordinal))
        {
            command = "validate";
            options = arguments.Skip(1).ToArray();
            return true;
        }

        if (arguments.Count >= 2 && string.Equals(arguments[0], "config", StringComparison.Ordinal))
        {
            if (string.Equals(arguments[1], "export", StringComparison.Ordinal) ||
                string.Equals(arguments[1], "validate", StringComparison.Ordinal))
            {
                command = arguments[1];
                options = arguments.Skip(2).ToArray();
                return true;
            }
        }

        return false;
    }

    private static bool TryParseOptions(
        IReadOnlyList<string> arguments,
        IReadOnlyCollection<string> allowed,
        TextWriter error,
        out Dictionary<string, string> options)
    {
        options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index += 2)
        {
            var name = arguments[index];
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                error.WriteLine($"Unknown option: {name}");
                return false;
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error.WriteLine($"Option {name} requires a value.");
                return false;
            }

            if (!options.TryAdd(name, arguments[index + 1]))
            {
                error.WriteLine($"Option {name} was specified more than once.");
                return false;
            }
        }

        return true;
    }

    private static string FindRepositoryRoot(string workingDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(workingDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CombatLab.sln")))
            {
                return directory.FullName;
            }

            var child = Path.Combine(directory.FullName, "CombatLab");
            if (File.Exists(Path.Combine(child, "CombatLab.sln")))
            {
                return child;
            }

            directory = directory.Parent;
        }

        throw new ArgumentException("Could not locate CombatLab.sln from the current directory.", nameof(workingDirectory));
    }

    private static string ResolvePath(string path, string workingDirectory) =>
        Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(workingDirectory, path));

    private static void WriteValidationArtifact(
        string outputDirectory,
        IReadOnlyList<BalanceExportIssue> exportIssues,
        IReadOnlyList<ConfigValidationIssue> compilationIssues,
        string? configHash,
        string? sourceHash)
    {
        Directory.CreateDirectory(outputDirectory);
        var bytes = WriteValidationJson(exportIssues, compilationIssues, configHash, sourceHash);
        WriteAtomic(Path.Combine(outputDirectory, ArtifactBaseName + ".validation.json"), bytes);
    }

    private static byte[] WriteValidationJson(
        IReadOnlyList<BalanceExportIssue> exportIssues,
        IReadOnlyList<ConfigValidationIssue> compilationIssues,
        string? configHash,
        string? sourceHash)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Default,
            Indented = true,
            NewLine = "\n",
        });

        var errorCount = exportIssues.Count(issue => issue.Severity == BalanceExportIssueSeverity.Error) +
            compilationIssues.Count(issue => issue.Severity == ConfigValidationSeverity.Error);
        var warningCount = exportIssues.Count(issue => issue.Severity == BalanceExportIssueSeverity.Warning) +
            compilationIssues.Count(issue => issue.Severity == ConfigValidationSeverity.Warning);

        writer.WriteStartObject();
        if (configHash is not null)
        {
            writer.WriteString("config_hash", configHash);
        }

        writer.WriteNumber("error_count", errorCount);
        writer.WritePropertyName("issues");
        writer.WriteStartArray();
        foreach (var issue in exportIssues
            .Select(issue => new PrintableIssue(issue.Code, issue.Path, issue.Message, issue.Severity.ToString()))
            .Concat(compilationIssues.Select(issue =>
                new PrintableIssue(issue.Code, issue.Path, issue.Message, issue.Severity.ToString())))
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("code", issue.Code);
            writer.WriteString("message", issue.Message);
            writer.WriteString("path", issue.Path);
            writer.WriteString("severity", issue.Severity);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (sourceHash is not null)
        {
            writer.WriteString("source_workbook_sha256", sourceHash);
        }

        writer.WriteNumber("warning_count", warningCount);
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteIssues(IEnumerable<BalanceExportIssue> issues, TextWriter writer)
    {
        foreach (var issue in issues)
        {
            writer.WriteLine($"{issue.Severity.ToString().ToUpperInvariant()} {issue.Code} {issue.Path}: {issue.Message}");
        }
    }

    private static void WriteIssues(IEnumerable<ConfigValidationIssue> issues, TextWriter writer)
    {
        foreach (var issue in issues)
        {
            writer.WriteLine($"{issue.Severity.ToString().ToUpperInvariant()} {issue.Code} {issue.Path}: {issue.Message}");
        }
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Output path must have a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  combatlab export-config [--workbook <path>] [--output <directory>]");
        writer.WriteLine("  combatlab validate-config [--config <path>] [--manifest <path>]");
        writer.WriteLine("Aliases: config export, config validate");
    }

    private sealed record PrintableIssue(string Code, string Path, string Message, string Severity);
}
