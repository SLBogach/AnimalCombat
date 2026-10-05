using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Results;
using Battle.Replay.Verification;

namespace CombatLab.Runner.Replays;

public static class BattleDemoCommand
{
    public static int Execute(IReadOnlyList<string> args, TextWriter output, TextWriter error, string workingDirectory)
    {
        try
        {
            if (args.Count < 2 || args[0] != "run-demo")
            { error.WriteLine("run-demo <scenario> --output <new-file.json> [--seed <u64>] [--config-output <new-file.json>]"); return 2; }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 2; i < args.Count; i += 2)
            {
                if (i + 1 >= args.Count || args[i] is not ("--output" or "--seed" or "--config-output") || !options.TryAdd(args[i], args[i + 1]))
                    throw new ArgumentException("Unknown, duplicate or missing option value.");
            }
            if (!options.TryGetValue("--output", out var destination)) throw new ArgumentException("--output is required; existing files are never overwritten.");
            var seed = options.TryGetValue("--seed", out var value) ? ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
            var root = FindRoot(workingDirectory); var run = EffectDemoCatalog.Run(root, args[1], seed: seed);
            if (run.Result.Status != BattleResultStatus.Completed)
                throw new InvalidDataException(run.Result.InvariantFailure?.Message ?? string.Join("; ", run.Result.RejectionErrors.Select(x => x.Code + "@" + x.Path)));
            var bytes = EffectDemoCatalog.Write(args[1], run);
            var verification = new ReplayVerifier(File.ReadAllBytes(Path.Combine(root, "schemas/replay/v0.1/combat-replay.schema.json"))).Verify(bytes);
            if (!verification.IsValid) throw new InvalidDataException(string.Join("; ", verification.Issues.Select(x => x.Code + ":" + x.Message)));
            var path = Path.GetFullPath(destination, workingDirectory);
            string? configPath = options.TryGetValue("--config-output", out var config) ? Path.GetFullPath(config, workingDirectory) : null;
            if (File.Exists(path) || configPath is not null && File.Exists(configPath) || StringComparer.OrdinalIgnoreCase.Equals(path, configPath))
                throw new IOException("Output already exists or replay/config destinations coincide.");
            if (!Directory.Exists(Path.GetDirectoryName(path)) || configPath is not null && !Directory.Exists(Path.GetDirectoryName(configPath)))
                throw new DirectoryNotFoundException("Create the output directory before running the demo.");
            // CreateNew only. Paths are explicit caller outputs, never generated/source destinations by default.
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes);
            if (configPath is not null)
            {
                var source = EffectDemoCatalog.CreateSource(root, args[1]);
                var compilation = new Battle.Config.Compiler.BattleConfigCompiler().Compile(System.Text.Encoding.UTF8.GetBytes(source.ToJsonString()));
                using var stream = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write); stream.Write(compilation.GetCanonicalJson());
            }
            output.WriteLine("Replay: " + path); output.WriteLine("Engine: " + run.Request.EngineVersion);
            output.WriteLine("Outcome: " + run.Result.Summary!.Outcome + "; end_tick=" + run.Result.Summary.EndTick.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("Config: " + run.Config.Reference.ConfigHash); output.WriteLine("Final digest: " + run.Journal.FinalDigest);
            return 0;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException or FormatException or UnauthorizedAccessException or JsonException)
        { error.WriteLine(exception.Message); return 2; }
    }

    public static string FindRoot(string workingDirectory)
    {
        var directory = new DirectoryInfo(workingDirectory);
        while (directory is not null)
        { if (File.Exists(Path.Combine(directory.FullName, "CombatLab.sln"))) return directory.FullName; directory = directory.Parent; }
        throw new DirectoryNotFoundException("Run from CombatLab or one of its subdirectories.");
    }
}
