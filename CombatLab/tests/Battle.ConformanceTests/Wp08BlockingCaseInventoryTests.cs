using System.Reflection;
using System.Text.RegularExpressions;

namespace Battle.ConformanceTests;

public sealed class Wp08BlockingCaseInventoryTests
{
    private static readonly Regex CaseIdPattern = new(
        @"(?:^|_)WP08_(?<family>[A-Z]+)_(?<number>[0-9]{3})(?:_|$)",
        RegexOptions.CultureInvariant);

    private static readonly string[] TestAssemblyNames =
    {
        "Battle.ConformanceTests",
        "Battle.Core.UnitTests",
        "CombatLab.IntegrationTests",
        "CombatLab.PerformanceTests",
    };

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void BlockingCaseInventory_RequiresExact107TraitedCaseIdsAcrossTestAssemblies()
    {
        var expected = CreateExpectedCaseIds();
        Assert.Equal(107, expected.Count);

        var failures = new List<string>();
        var discovered = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var assembly in LoadTestAssemblies())
        {
            foreach (var type in assembly.GetTypes().OrderBy(item => item.FullName, StringComparer.Ordinal))
            {
                foreach (var method in type
                             .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                             .OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    if (!IsXunitTest(method))
                    {
                        continue;
                    }

                    var location = assembly.GetName().Name + ":" + type.FullName + "." + method.Name;
                    var hasWp08Trait = HasWp08Trait(method) || HasWp08Trait(type);
                    if ((LooksLikeWp08Test(type, method) || HasWp08CategoryTrait(method) || HasWp08CategoryTrait(type)) &&
                        !hasWp08Trait)
                    {
                        failures.Add("WP-08 test is missing Trait(WorkPackage, WP08): " + location);
                    }

                    var match = CaseIdPattern.Match(method.Name);
                    if (!match.Success)
                    {
                        continue;
                    }

                    var caseId = "WP08-" +
                                 match.Groups["family"].Value + "-" +
                                 match.Groups["number"].Value;
                    if (!discovered.TryGetValue(caseId, out var locations))
                    {
                        locations = new List<string>();
                        discovered.Add(caseId, locations);
                    }

                    locations.Add(location);
                }
            }
        }

        foreach (var missing in expected.Except(discovered.Keys, StringComparer.Ordinal))
        {
            failures.Add("Missing blocking case " + missing + ".");
        }

        foreach (var unexpected in discovered.Keys.Except(expected, StringComparer.Ordinal))
        {
            failures.Add("Unexpected WP-08 case ID " + unexpected + ".");
        }

        Assert.True(
            failures.Count == 0,
            "WP-08 blocking inventory is not complete:" + Environment.NewLine +
            string.Join(Environment.NewLine, failures.OrderBy(item => item, StringComparer.Ordinal)));
    }

    private static IReadOnlyList<Assembly> LoadTestAssemblies()
    {
        var current = typeof(Wp08BlockingCaseInventoryTests).Assembly;
        var currentDirectory = new DirectoryInfo(Path.GetDirectoryName(current.Location)!);
        var configuration = currentDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Cannot infer the test build configuration.");
        var combatLabRoot = RepositoryLocator.FindCombatLabRoot();
        var result = new List<Assembly>(TestAssemblyNames.Length);

        foreach (var assemblyName in TestAssemblyNames)
        {
            if (StringComparer.Ordinal.Equals(current.GetName().Name, assemblyName))
            {
                result.Add(current);
                continue;
            }

            var assemblyPath = Path.Combine(
                combatLabRoot,
                "tests",
                assemblyName,
                "bin",
                configuration,
                "net10.0",
                assemblyName + ".dll");
            Assert.True(
                File.Exists(assemblyPath),
                "WP-08 inventory requires built test assembly '" + assemblyPath + "'. Build CombatLab.sln first.");
            result.Add(Assembly.LoadFrom(assemblyPath));
        }

        Assert.Equal(TestAssemblyNames, result.Select(item => item.GetName().Name!));
        return result;
    }

    private static bool IsXunitTest(MethodInfo method) =>
        method.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute");

    private static bool HasWp08Trait(MemberInfo member) =>
        member.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "Xunit.TraitAttribute" &&
            attribute.ConstructorArguments.Count == 2 &&
            StringComparer.Ordinal.Equals(attribute.ConstructorArguments[0].Value as string, "WorkPackage") &&
            StringComparer.Ordinal.Equals(attribute.ConstructorArguments[1].Value as string, "WP08"));

    private static bool HasWp08CategoryTrait(MemberInfo member) =>
        member.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "Xunit.TraitAttribute" &&
            attribute.ConstructorArguments.Count == 2 &&
            StringComparer.Ordinal.Equals(attribute.ConstructorArguments[0].Value as string, "Category") &&
            StringComparer.Ordinal.Equals(attribute.ConstructorArguments[1].Value as string, "WP08"));

    private static bool LooksLikeWp08Test(Type type, MethodInfo method) =>
        method.Name.Contains("WP08", StringComparison.OrdinalIgnoreCase) ||
        type.Name.Contains("Wp08", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> CreateExpectedCaseIds()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        AddRange(result, "CFG", 8);
        AddRange(result, "CAT", 3);
        AddRange(result, "AVL", 9);
        AddRange(result, "WGT", 13);
        AddRange(result, "SEL", 12);
        AddRange(result, "VAR", 5);
        AddRange(result, "OPP", 8);
        AddRange(result, "SNP", 3);
        AddRange(result, "CMT", 8);
        AddRange(result, "LIFE", 4);
        AddRange(result, "CON", 12);
        AddRange(result, "DET", 7);
        AddRange(result, "SAFE", 3);
        AddRange(result, "REG", 6);
        AddRange(result, "ARCH", 6);
        return result;
    }

    private static void AddRange(ISet<string> target, string family, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            Assert.True(target.Add($"WP08-{family}-{index:000}"));
        }
    }
}
