namespace Battle.ConformanceTests;

public sealed class ReplayArchitectureTests
{
    [Fact]
    public void BattleReplayAssembly_ReferencesOnlyContractsAndSystemAssemblies()
    {
        var unexpected = typeof(global::Battle.Replay.AssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name =>
                !string.Equals(name, "Battle.Contracts", StringComparison.Ordinal) &&
                !string.Equals(name, "netstandard", StringComparison.Ordinal) &&
                !string.Equals(name, "mscorlib", StringComparison.Ordinal) &&
                !name.StartsWith("System", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(unexpected);
    }
}
