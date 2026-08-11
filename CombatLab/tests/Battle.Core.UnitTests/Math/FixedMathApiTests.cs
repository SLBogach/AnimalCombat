using System.Reflection;
using Battle.Core.Math;

namespace Battle.Core.UnitTests.Math;

public sealed class FixedMathApiTests
{
    [Fact]
    public void PublicApi_MatchesTechnicalDesign()
    {
        AssertMethod(nameof(FixedMath.Mul), typeof(int), typeof(int), typeof(int), typeof(int));
        AssertMethod(nameof(FixedMath.Div), typeof(int), typeof(int), typeof(int), typeof(int));
        AssertMethod(
            nameof(FixedMath.FloorDiv),
            typeof(int),
            typeof(long),
            typeof(long));
        AssertMethod(
            nameof(FixedMath.Clamp),
            typeof(int),
            typeof(int),
            typeof(int),
            typeof(int));
        AssertMethod(
            nameof(FixedMath.ProductSorted),
            typeof(int),
            typeof(ReadOnlySpan<Modifier>),
            typeof(int));
    }

    [Fact]
    public void CorePublicApi_DoesNotExposeFloatingPointGameplayTypes()
    {
        var forbiddenTypes = new HashSet<Type>
        {
            typeof(float),
            typeof(double),
            typeof(decimal),
        };
        var publicTypes = typeof(FixedMath)
            .Assembly
            .GetExportedTypes();

        foreach (var type in publicTypes)
        {
            foreach (var property in type.GetProperties(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(property.PropertyType, forbiddenTypes);
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.Public |
                         BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(method.ReturnType, forbiddenTypes);
                Assert.DoesNotContain(
                    method.GetParameters(),
                    parameter => forbiddenTypes.Contains(parameter.ParameterType));
            }
        }
    }

    private static void AssertMethod(
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = typeof(FixedMath).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static,
            parameterTypes);

        Assert.NotNull(method);
        Assert.Equal(returnType, method.ReturnType);
    }
}
