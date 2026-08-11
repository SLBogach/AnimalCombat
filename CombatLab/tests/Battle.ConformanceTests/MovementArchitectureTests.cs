using System.Text.RegularExpressions;

namespace Battle.ConformanceTests;

public sealed class MovementArchitectureTests
{
    [Fact]
    public void WP07_ARCH_002_BattleCoreExcludesNondeterministicAndPresentationApis()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var source = ReadSources(Path.Combine(root, "src", "Battle.Core"));
        var forbidden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["floating-point types"] = @"\b(?:float|double|decimal)\b",
            ["nondeterministic RNG"] = @"\bSystem\.Random\b|\bRandom\.Shared\b|\bnew\s+Random\s*\(",
            ["wall clock"] = @"\b(?:DateTime|DateTimeOffset|Stopwatch)\b|Environment\.TickCount|Thread\.Sleep|Task\.Delay",
            ["filesystem or stream I/O"] = @"\bSystem\.IO\b|\b(?:File|Directory|Path|Stream|Console)\s*\.",
            ["Unity API"] = @"\bUnity(?:Engine)?\b",
        };

        foreach (var item in forbidden)
        {
            Assert.False(
                Regex.IsMatch(source, item.Value, RegexOptions.CultureInvariant),
                $"Battle.Core contains forbidden WP-07 {item.Key} usage.");
        }
    }

    [Fact]
    public void WP07_ARCH_004_MovementDomainDoesNotImplementDeferredCombatScope()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var source = ReadSources(Path.Combine(root, "src", "Battle.Core", "Movement"));

        Assert.False(
            Regex.IsMatch(
                source,
                @"\b(?:Damage|Knockback|Grab|Effect|WallImpact|Weighted|Physics|Unity)\b",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            "Battle.Core/Movement contains combat, weighted-selection, effect, physics, or Unity scope deferred past WP-07.");
    }

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
