using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Battle.Contracts.Versions;

namespace Battle.ConformanceTests;

public sealed class Wp08ArchitectureGuardsTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_ARCH_001_BattleCoreReferencesOnlyContractsAndTheBcl()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var project = XDocument.Load(
            Path.Combine(root, "src", "Battle.Core", "Battle.Core.csproj"));
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
            .ToArray();

        Assert.Equal(new[] { "Battle.Contracts" }, projectReferences);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.DoesNotContain(
            projectReferences,
            reference => reference is "Battle.Config" or "Battle.Replay" or
                "CombatLab.Runner" or "UnityClient");
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_ARCH_002_GameplaySourceExcludesForbiddenNondeterministicAndIoApis()
    {
        var source = ReadCoreSources();
        var forbidden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["floating point"] = @"\b(?:float|double|decimal)\b",
            ["ambient RNG"] = @"\bSystem\.Random\b|\bRandom\.Shared\b|\bnew\s+Random\s*\(",
            ["ambient identity/hash"] = @"\bGuid\b|\bGetHashCode\s*\(|\bHashCode\s*\.",
            ["wall clock"] = @"\b(?:DateTime|DateTimeOffset|Stopwatch)\b|Environment\.TickCount",
            ["I/O"] = @"\bSystem\.IO\b|\b(?:File|Directory|Console)\s*\.",
            ["Unity"] = @"\bUnity(?:Engine)?\b",
        };

        foreach (var item in forbidden)
        {
            Assert.False(
                Regex.IsMatch(source, item.Value, RegexOptions.CultureInvariant),
                $"Battle.Core contains forbidden WP-08 {item.Key} usage.");
        }
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_ARCH_003_SelectorAndEvaluatorAcceptOnlyImmutableDecisionViews()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var evaluator = File.ReadAllText(
            Path.Combine(root, "src", "Battle.Core", "Decisions", "DecisionEvaluator.cs"));
        var selector = File.ReadAllText(
            Path.Combine(root, "src", "Battle.Core", "Decisions", "DecisionSelector.cs"));
        var snapshot = File.ReadAllText(
            Path.Combine(root, "src", "Battle.Core", "Decisions", "DecisionSnapshot.cs"));

        Assert.Matches(
            @"Evaluate\s*\(\s*DecisionBatchSnapshot\s+snapshot",
            evaluator);
        Assert.DoesNotContain("BattleState", evaluator);
        Assert.DoesNotContain("BattleState", selector);
        Assert.Contains("internal sealed class DecisionBatchSnapshot", snapshot);
        Assert.Contains("private readonly ReadOnlyCollection<FighterId>", snapshot);
        Assert.DoesNotMatch(@"\b(?:public|internal)\s+void\s+Set", snapshot);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_ARCH_004_TwelvePhasePipelineAndVersionPinsRemainExact()
    {
        var phaseSource = File.ReadAllText(
            Path.Combine(
                RepositoryLocator.FindCombatLabRoot(),
                "src", "Battle.Core", "Engine", "TickPhase.cs"));
        var phaseMatches = Regex.Matches(
            phaseSource,
            @"^\s*(?<name>[A-Za-z]+)\s*=\s*(?<value>[0-9]+),?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        Assert.Equal(
            new[]
            {
                "Snapshot=1", "Expiry=2", "Resource=3", "ActionPhaseEnd=4",
                "Decisions=5", "VoluntaryMovement=6", "CollectIntents=7",
                "SortIntents=8", "Resolve=9", "WallsAndGrabs=10",
                "Outcome=11", "EndTick=12",
            },
            phaseMatches.Select(match =>
                match.Groups["name"].Value + "=" + match.Groups["value"].Value));
        Assert.Equal("tick-pipeline/1", ContractVersions.Ordering.ToString());
        Assert.Equal("battle.core/0.3.0", ContractVersions.Engine.ToString());
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_ARCH_005_DecisionEngineContainsNoAnimalOrConcreteCombatActionLiteral()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var decisionSource = ReadSources(
            Path.Combine(root, "src", "Battle.Core", "Decisions")) +
            Environment.NewLine +
            File.ReadAllText(Path.Combine(root, "src", "Battle.Core", "Engine", "TickCoordinator.cs"));
        using var config = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(root, "config", "generated", "combat.balance.v0.1.json")));
        var concreteIds = config.RootElement
            .GetProperty("fighters")
            .EnumerateArray()
            .Select(item => item.GetProperty("animal_id").GetString()!)
            .Concat(config.RootElement
                .GetProperty("actions")
                .EnumerateArray()
                .Select(item => item.GetProperty("action_id").GetString()!)
                .Where(id => !id.StartsWith("sys_", StringComparison.Ordinal)))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        foreach (var id in concreteIds)
        {
            Assert.DoesNotContain("\"" + id + "\"", decisionSource, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_ARCH_006_ResolutionPhasesRemainNoOpAndUnityIsOutsideCombatLabScope()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var coordinator = File.ReadAllText(
            Path.Combine(root, "src", "Battle.Core", "Engine", "TickCoordinator.cs"));
        var noOpResolution = @"
            Observe\(state,\s*TickPhase\.CollectIntents\);\s*
            Observe\(state,\s*TickPhase\.SortIntents\);\s*
            Observe\(state,\s*TickPhase\.Resolve\);\s*
            Observe\(state,\s*TickPhase\.WallsAndGrabs\);\s*
            Observe\(state,\s*TickPhase\.Outcome\);";

        Assert.Matches(
            new Regex(
                noOpResolution,
                RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace),
            coordinator);
        Assert.DoesNotContain(
            "UnityClient",
            File.ReadAllText(Path.Combine(root, "CombatLab.sln")),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(
                @"\bUnity(?:Engine)?\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            ReadCoreSources());
    }

    private static string ReadCoreSources() => ReadSources(
        Path.Combine(RepositoryLocator.FindCombatLabRoot(), "src", "Battle.Core"));

    private static string ReadSources(string directory) =>
        string.Join(
            Environment.NewLine,
            Directory
                .GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Contains(
                        Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains(
                        Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
}
