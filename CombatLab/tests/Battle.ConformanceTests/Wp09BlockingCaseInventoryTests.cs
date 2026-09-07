using System.Reflection;

namespace Battle.ConformanceTests;

[Trait("WorkPackage", "WP09")]
public sealed class Wp09BlockingCaseInventoryTests
{
    private static readonly string[] TestAssemblyNames =
    {
        "Battle.ConformanceTests",
        "Battle.Core.UnitTests",
        "CombatLab.IntegrationTests",
        "CombatLab.PerformanceTests",
    };

    [Fact]
    [Trait("AcceptanceId", "WP09-REG-005")]
    public void WP09_REG_005_BlockingInventoryHasExactly128UniqueDiscoverableCases()
    {
        var expected = CreateExpectedCaseIds();
        Assert.Equal(128, expected.Count);
        var discovered = new Dictionary<string, string>(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var assembly in LoadTestAssemblies())
        {
            foreach (var type in assembly.GetTypes().OrderBy(item => item.FullName, StringComparer.Ordinal))
            {
                foreach (var method in type.GetMethods(
                             BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.Instance | BindingFlags.Static))
                {
                    if (!IsXunitTest(method)) continue;
                    var acceptanceIds = Traits(method, "AcceptanceId")
                        .Where(value => value.StartsWith("WP09-", StringComparison.Ordinal))
                        .ToArray();
                    if (acceptanceIds.Length == 0) continue;

                    var location = assembly.GetName().Name + ":" + type.FullName + "." + method.Name;
                    if (!HasWp09WorkPackage(type, method))
                    {
                        failures.Add("WP-09 acceptance test is missing WorkPackage=WP09: " + location);
                    }

                    foreach (var id in acceptanceIds)
                    {
                        if (!discovered.TryAdd(id, location))
                        {
                            failures.Add("Duplicate blocking case " + id + ": " + discovered[id] + " and " + location);
                        }
                    }
                }
            }
        }

        failures.AddRange(expected.Except(discovered.Keys, StringComparer.Ordinal)
            .Select(id => "Missing blocking case " + id));
        failures.AddRange(discovered.Keys.Except(expected, StringComparer.Ordinal)
            .Select(id => "Unexpected blocking case " + id));

        Assert.True(failures.Count == 0,
            "WP-09 blocking inventory is incomplete:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures.OrderBy(item => item, StringComparer.Ordinal)));
        Assert.Equal(128, discovered.Count);
    }

    private static IReadOnlyList<Assembly> LoadTestAssemblies()
    {
        var current = typeof(Wp09BlockingCaseInventoryTests).Assembly;
        var configuration = new DirectoryInfo(Path.GetDirectoryName(current.Location)!).Parent?.Name
            ?? throw new InvalidOperationException("Cannot infer test configuration.");
        var root = RepositoryLocator.FindCombatLabRoot();
        return TestAssemblyNames.Select(name =>
        {
            if (StringComparer.Ordinal.Equals(current.GetName().Name, name)) return current;
            var path = Path.Combine(root, "tests", name, "bin", configuration, "net10.0", name + ".dll");
            Assert.True(File.Exists(path), "Build the solution before running WP-09 inventory: " + path);
            return Assembly.LoadFrom(path);
        }).ToArray();
    }

    private static bool IsXunitTest(MethodInfo method) => method.CustomAttributes.Any(attribute =>
        attribute.AttributeType.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute");

    private static bool HasWp09WorkPackage(Type type, MethodInfo method) =>
        Traits(type, "WorkPackage").Concat(Traits(method, "WorkPackage"))
            .Any(value => StringComparer.Ordinal.Equals(value, "WP09"));

    private static IEnumerable<string> Traits(MemberInfo member, string name) =>
        member.CustomAttributes
            .Where(attribute => attribute.AttributeType.FullName == "Xunit.TraitAttribute" &&
                                attribute.ConstructorArguments.Count == 2 &&
                                StringComparer.Ordinal.Equals(attribute.ConstructorArguments[0].Value, name))
            .Select(attribute => (string)attribute.ConstructorArguments[1].Value!);

    private static HashSet<string> CreateExpectedCaseIds()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        Add("CFG", 8); Add("SCH", 6); Add("GRP", 8); Add("GEO", 6);
        Add("DEF", 12); Add("DMG", 10); Add("CTL", 8); Add("MOV", 6);
        Add("FRC", 8); Add("GRB", 8); Add("OUT", 8); Add("EVT", 10);
        Add("SAFE", 8); Add("INT", 8); Add("DET", 8); Add("REG", 6);
        return result;

        void Add(string family, int count)
        {
            for (var number = 1; number <= count; number++)
            {
                Assert.True(result.Add($"WP09-{family}-{number:000}"));
            }
        }
    }
}
