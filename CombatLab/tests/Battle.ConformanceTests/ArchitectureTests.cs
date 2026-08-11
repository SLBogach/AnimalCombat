using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Battle.Contracts.Ids;

namespace Battle.ConformanceTests;

public sealed class ArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Battle.Contracts"] = Array.Empty<string>(),
            ["Battle.Core"] = new[] { "Battle.Contracts" },
            ["Battle.Config"] = new[] { "Battle.Contracts" },
            ["Battle.Replay"] = new[] { "Battle.Contracts" },
            ["CombatLab.Runner"] =
                new[] { "Battle.Config", "Battle.Contracts", "Battle.Core", "Battle.Replay" },
            ["CombatLab.Cli"] = new[] { "CombatLab.Runner" },
        };

    [Fact]
    public void ContractsAssembly_HasNoReverseCombatDependencies()
    {
        var combatReferences = typeof(StableId)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && (
                name.StartsWith("Battle.", StringComparison.Ordinal) ||
                name.StartsWith("CombatLab.", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(combatReferences);
    }

    [Fact]
    public void ContractsProject_IsBclOnly()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var project = XDocument.Load(FindProject(root, "src", "Battle.Contracts"));

        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact]
    public void ProductionProjectGraph_MatchesTechnicalDesign()
    {
        var root = RepositoryLocator.FindCombatLabRoot();

        foreach (var expectedProject in ExpectedProjectReferences)
        {
            var projectPath = FindProject(root, "src", expectedProject.Key);
            var document = XDocument.Load(projectPath);
            var actualReferences = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFileNameWithoutExtension(path!.Replace('\\', '/')))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedReferences = expectedProject.Value
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                expectedReferences.SequenceEqual(actualReferences, StringComparer.Ordinal),
                $"{expectedProject.Key}: expected [{string.Join(", ", expectedReferences)}], " +
                $"actual [{string.Join(", ", actualReferences)}].");
        }
    }

    [Fact]
    public void SharedProjects_UseRequiredCompileMatrix()
    {
        var root = RepositoryLocator.FindCombatLabRoot();

        foreach (var projectName in new[]
                 {
                     "Battle.Contracts",
                     "Battle.Core",
                     "Battle.Config",
                     "Battle.Replay",
                 })
        {
            var projectPath = FindProject(root, "src", projectName);
            var document = XDocument.Load(projectPath);
            var targetFrameworks = document
                .Descendants("TargetFrameworks")
                .Single()
                .Value;

            Assert.Equal("netstandard2.1;net10.0", targetFrameworks);
        }
    }

    [Fact]
    public void JournalPort_ExposesExactMethodShape()
    {
        var journalType = typeof(Battle.Contracts.Ports.ICombatEventJournal);
        var begin = journalType.GetMethod("Begin", BindingFlags.Instance | BindingFlags.Public);
        var append = journalType.GetMethod("Append", BindingFlags.Instance | BindingFlags.Public);
        var complete = journalType.GetMethod("Complete", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(begin);
        Assert.Equal(typeof(Battle.Contracts.Ports.JournalBeginResult), begin.ReturnType);
        Assert.Equal(
            typeof(Battle.Contracts.Replay.CombatJournalStart).MakeByRefType(),
            Assert.Single(begin.GetParameters()).ParameterType);

        Assert.NotNull(append);
        Assert.Equal(typeof(Battle.Contracts.Events.CombatEventIdentity), append.ReturnType);
        Assert.Equal(
            typeof(Battle.Contracts.Events.CombatEventDraft).MakeByRefType(),
            Assert.Single(append.GetParameters()).ParameterType);

        Assert.NotNull(complete);
        Assert.Equal(typeof(Battle.Contracts.Ports.JournalCompletion), complete.ReturnType);
        Assert.Equal(
            typeof(Battle.Contracts.Results.BattleSummary).MakeByRefType(),
            Assert.Single(complete.GetParameters()).ParameterType);
    }

    [Fact]
    public void RepositoryBuildPolicy_IsPinnedAndStrict()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        using var globalJson = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "global.json")));
        var sdk = globalJson.RootElement.GetProperty("sdk");
        Assert.Equal("10.0.302", sdk.GetProperty("version").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        var buildProps = XDocument.Load(Path.Combine(root, "Directory.Build.props"));
        Assert.Equal("enable", buildProps.Descendants("Nullable").Single().Value);
        Assert.Equal("true", buildProps.Descendants("TreatWarningsAsErrors").Single().Value);
        Assert.Equal("true", buildProps.Descendants("Deterministic").Single().Value);
        Assert.Equal("true", buildProps.Descendants("CheckForOverflowUnderflow").Single().Value);
        Assert.Equal("true", buildProps.Descendants("RestorePackagesWithLockFile").Single().Value);
    }

    private static string FindProject(string root, string category, string projectName) =>
        Path.Combine(root, category, projectName, $"{projectName}.csproj");
}
