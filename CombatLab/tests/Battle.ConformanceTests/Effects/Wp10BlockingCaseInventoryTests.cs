using System.Reflection;
using System.Text.RegularExpressions;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10BlockingCaseInventoryTests
{
    [Fact, Trait("AcceptanceId", "WP10-BASE-006")]
    public void BlockingMatrixHasExactly132UniqueDiscoverableNonSkippedAcceptanceMethods()
    {
        var families = new Dictionary<string, int> { ["BASE"] = 6, ["DATA"] = 11, ["MOD"] = 14, ["EXP"] = 9, ["STACK"] = 13,
            ["TRG"] = 14, ["CTRL"] = 12, ["KDN"] = 8, ["INT"] = 8, ["EVT"] = 9, ["SAFE"] = 9, ["DET"] = 8, ["REG"] = 7, ["GOLD"] = 4 };
        var expected = families.SelectMany(x => Enumerable.Range(1, x.Value).Select(n => "WP10-" + x.Key + "-" + n.ToString("D3"))).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(132, expected.Count);
        var root = RepositoryLocator.FindCombatLabRoot(); var current = typeof(Wp10BlockingCaseInventoryTests).Assembly;
        var configuration = new DirectoryInfo(Path.GetDirectoryName(current.Location)!).Parent!.Name;
        var discovered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "Battle.ConformanceTests", "Battle.Core.UnitTests", "CombatLab.IntegrationTests", "CombatLab.PerformanceTests" })
        {
            var assembly = name == current.GetName().Name ? current : Assembly.LoadFrom(Path.Combine(root, "tests", name, "bin", configuration, "net10.0", name + ".dll"));
            foreach (var type in assembly.GetTypes()) foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                var tests = method.CustomAttributes.Where(x => x.AttributeType.FullName is "Xunit.FactAttribute" or "Xunit.TheoryAttribute").ToArray();
                if (tests.Length == 0) continue;
                var ids = Traits(method, "AcceptanceId").Where(x => x.StartsWith("WP10-", StringComparison.Ordinal)).ToArray();
                if (ids.Length == 0) continue;
                Assert.Contains("WP10", Traits(type, "WorkPackage").Concat(Traits(method, "WorkPackage")));
                Assert.DoesNotContain(tests.SelectMany(x => x.NamedArguments), x => x.MemberName == "Skip" && x.TypedValue.Value is not null);
                foreach (var id in ids) Assert.True(discovered.TryAdd(id, type.FullName + "." + method.Name), "Duplicate acceptance ID: " + id);
            }
        }
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), discovered.Keys.OrderBy(x => x, StringComparer.Ordinal));
        var plan = File.ReadAllText(Path.Combine(Directory.GetParent(root)!.FullName, "Docs/Combat_Test_Plan_WP-10_v0.1.md"));
        var rows = Regex.Matches(plan, "(?m)^\\| `(?<id>WP10-[A-Z]+-\\d{3})` \\|").Select(x => x.Groups["id"].Value).ToArray();
        Assert.Equal(132, rows.Length); Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), rows.OrderBy(x => x, StringComparer.Ordinal));
    }
    private static IEnumerable<string> Traits(MemberInfo member, string key) => member.CustomAttributes.Where(x => x.AttributeType.FullName == "Xunit.TraitAttribute" &&
        x.ConstructorArguments.Count == 2 && (string?)x.ConstructorArguments[0].Value == key).Select(x => (string)x.ConstructorArguments[1].Value!);
}
