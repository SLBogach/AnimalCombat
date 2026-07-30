namespace Battle.ConformanceTests;

public sealed class RandomArchitectureTests
{
    [Fact]
    public void GameplayRandomSources_ExcludeNondeterministicApisAndPresentationStream()
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        var randomDirectory = Path.Combine(root, "src", "Battle.Core", "Random");
        var source = string.Join(
            Environment.NewLine,
            Directory
                .GetFiles(randomDirectory, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        var forbiddenTokens = new[]
        {
            "System.Random",
            "Random.Shared",
            "Guid.",
            "GetHashCode(",
            "HashCode.",
            "new HashCode",
            "Presentation",
        };

        foreach (var forbiddenToken in forbiddenTokens)
        {
            Assert.DoesNotContain(forbiddenToken, source, StringComparison.Ordinal);
        }
    }
}
