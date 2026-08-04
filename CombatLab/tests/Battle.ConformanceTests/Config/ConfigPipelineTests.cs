using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Contracts.Config;

namespace Battle.ConformanceTests.Config;

public sealed class ConfigPipelineTests
{
    [Fact]
    public void GeneratedArtifacts_CompileAndLoadWithoutIssues()
    {
        var configBytes = ConfigFixture.ReadConfigBytes();
        var compilation = new BattleConfigCompiler().Compile(configBytes);

        Assert.True(compilation.IsSuccess, Describe(compilation.Issues));
        Assert.Empty(compilation.Issues);
        Assert.Equal(ConfigFixture.ReadExpectedHash(), compilation.ConfigHash?.ToString());

        var compiled = Assert.IsType<CompiledBattleConfig>(compilation.Config);
        Assert.Equal("combat.balance/0.1", compiled.Reference.BalanceSchemaVersion.ToString());
        Assert.Equal("v0.1", compiled.Reference.ConfigVersion.ToString());
        Assert.Equal(3, compiled.Fighters.Count);
        Assert.Equal(24, compiled.Actions.Count);
        Assert.Equal(6, compiled.Passives.Count);
        Assert.Equal(10, compiled.Effects.Count);
        Assert.Equal(4, compiled.Tactics.Count);
        Assert.Equal(9, compiled.Gear.Count);

        var loaded = new global::Battle.Config.BattleConfigLoader().Load(
            configBytes,
            ConfigFixture.ReadManifestBytes());

        Assert.True(loaded.IsSuccess, Describe(loaded.Issues));
        Assert.Empty(loaded.Issues);
        Assert.Equal(compiled.Reference, Assert.IsType<CompiledBattleConfig>(loaded.Config).Reference);
    }

    [Fact]
    public void Compiler_ProducesExactRepeatableCanonicalBytesHashAndDenseHandles()
    {
        var expectedCanonical = ConfigFixture.ReadConfigBytes();
        var candidate = ConfigFixture.Mutate(root =>
        {
            ReverseCatalog(root, "actions");
            ReverseCatalog(root, "fighters");

            var actions = root["actions"]!.AsArray();
            var original = actions[0]!.AsObject();
            var reversed = new JsonObject();
            foreach (var property in original.Reverse())
            {
                reversed.Add(property.Key, property.Value?.DeepClone());
            }

            actions[0] = reversed;
        });

        var compiler = new BattleConfigCompiler();
        var first = compiler.Compile(candidate);
        var second = compiler.Compile(candidate);

        Assert.True(first.IsSuccess, Describe(first.Issues));
        Assert.True(second.IsSuccess, Describe(second.Issues));
        Assert.True(expectedCanonical.AsSpan().SequenceEqual(first.GetCanonicalJson()));
        Assert.True(first.GetCanonicalJson().AsSpan().SequenceEqual(second.GetCanonicalJson()));
        Assert.Equal(ConfigFixture.ReadExpectedHash(), first.ConfigHash?.ToString());
        Assert.Equal(first.ConfigHash, second.ConfigHash);

        var config = Assert.IsType<CompiledBattleConfig>(first.Config);
        Assert.Equal(
            config.Settings.Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal),
            config.Settings.Select(item => item.Name));
        AssertSortedDense(config.Fighters);
        AssertSortedDense(config.Actions);
        AssertSortedDense(config.Passives);
        AssertSortedDense(config.Effects);
        AssertSortedDense(config.Tactics);
        AssertSortedDense(config.Gear);
    }

    [Fact]
    public void CompilationResultAndSnapshot_AreImmutableDefensiveCopies()
    {
        var expectedCanonical = ConfigFixture.ReadConfigBytes();
        var compilation = new BattleConfigCompiler().Compile(expectedCanonical);
        var config = Assert.IsType<CompiledBattleConfig>(compilation.Config);

        var callerCopy = compilation.GetCanonicalJson();
        callerCopy[0] ^= 0xff;

        Assert.True(expectedCanonical.AsSpan().SequenceEqual(compilation.GetCanonicalJson()));

        var actions = Assert.IsAssignableFrom<IList<CompiledConfigEntity>>(config.Actions);
        var properties = Assert.IsAssignableFrom<IList<ConfigProperty>>(config.Actions[0].Properties);
        Assert.True(actions.IsReadOnly);
        Assert.True(properties.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => actions.Clear());
        Assert.Throws<NotSupportedException>(() => properties.Clear());
    }

    [Fact]
    public void Compiler_IsIndependentOfCurrentCulture()
    {
        var source = ConfigFixture.ReadConfigBytes();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            var russian = new BattleConfigCompiler().Compile(source);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = new BattleConfigCompiler().Compile(source);

            Assert.True(russian.IsSuccess, Describe(russian.Issues));
            Assert.True(turkish.IsSuccess, Describe(turkish.Issues));
            Assert.True(russian.GetCanonicalJson().AsSpan().SequenceEqual(turkish.GetCanonicalJson()));
            Assert.Equal(russian.ConfigHash, turkish.ConfigHash);
            Assert.Equal(ConfigFixture.ReadExpectedHash(), russian.ConfigHash?.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Loader_CachesCompiledSnapshotByConfigHash()
    {
        var configBytes = ConfigFixture.ReadConfigBytes();
        var manifestBytes = ConfigFixture.ReadManifestBytes();
        var first = new global::Battle.Config.BattleConfigLoader().Load(configBytes, manifestBytes);
        var second = new global::Battle.Config.BattleConfigLoader().Load(configBytes, manifestBytes);

        Assert.True(first.IsSuccess, Describe(first.Issues));
        Assert.True(second.IsSuccess, Describe(second.Issues));
        Assert.Same(
            Assert.IsType<CompiledBattleConfig>(first.Config),
            Assert.IsType<CompiledBattleConfig>(second.Config));
    }

    [Fact]
    public void CompiledSnapshot_SupportsConcurrentDeterministicReads()
    {
        var compilation = new BattleConfigCompiler().Compile(ConfigFixture.ReadConfigBytes());
        var config = Assert.IsType<CompiledBattleConfig>(compilation.Config);
        var expectedAction = config.Actions[7];
        var expectedFighter = config.Fighters[1];
        var failures = new ConcurrentQueue<string>();

        Parallel.For(0, 1_024, index =>
        {
            if (!config.TryGetAction(expectedAction.Id, out var action) ||
                !ReferenceEquals(expectedAction, action))
            {
                failures.Enqueue($"action:{index}");
            }

            if (!config.TryGetFighter(expectedFighter.Id, out var fighter) ||
                !ReferenceEquals(expectedFighter, fighter))
            {
                failures.Enqueue($"fighter:{index}");
            }

            if (!config.TryGetSetting("global.sim.fp_scale", out var scale) ||
                scale.AsInteger() != 1_000)
            {
                failures.Enqueue($"setting:{index}");
            }
        });

        Assert.Empty(failures);
    }

    private static void ReverseCatalog(JsonObject root, string name)
    {
        var original = root[name]!.AsArray();
        root[name] = new JsonArray(
            original.Reverse().Select(item => item?.DeepClone()).ToArray());
    }

    private static void AssertSortedDense(IReadOnlyList<CompiledConfigEntity> entities)
    {
        Assert.Equal(
            entities.Select(item => item.Id.Value).OrderBy(item => item, StringComparer.Ordinal),
            entities.Select(item => item.Id.Value));
        Assert.Equal(Enumerable.Range(0, entities.Count), entities.Select(item => item.DenseHandle));

        foreach (var entity in entities)
        {
            Assert.Equal(
                entity.Properties.Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal),
                entity.Properties.Select(item => item.Name));
        }
    }

    private static string Describe(IEnumerable<global::Battle.Config.Semantic.ConfigValidationIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}"));
}
