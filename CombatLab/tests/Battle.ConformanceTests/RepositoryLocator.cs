namespace Battle.ConformanceTests;

internal static class RepositoryLocator
{
    public static string FindCombatLabRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CombatLab.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate CombatLab.sln from the test output directory.");
    }
}
