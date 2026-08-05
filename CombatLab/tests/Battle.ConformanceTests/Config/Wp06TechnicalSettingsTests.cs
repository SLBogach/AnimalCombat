using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Config.Semantic;

namespace Battle.ConformanceTests.Config;

public sealed class Wp06TechnicalSettingsTests
{
    [Fact]
    public void Compiler_AcceptsValidRequiredEngineSafetySettings()
    {
        var result = Compile(ConfigFixture.ReadConfigObject());

        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Issues.Select(issue => $"{issue.Code} {issue.Path}: {issue.Message}")));
        Assert.NotNull(result.Config);
    }

    [Theory]
    [InlineData("global.sim.max_events_per_battle")]
    [InlineData("global.sim.max_zero_progress_ticks")]
    public void Compiler_RejectsMissingRequiredEngineSafetySetting(string key)
    {
        var candidate = CandidateWithValidSettings();
        candidate["settings"]!.AsObject().Remove(key);

        var result = Compile(candidate);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.MissingRequiredConfigKey &&
                     issue.Path == "$.settings" &&
                     issue.Message.Contains($"'{key}'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("global.sim.max_events_per_battle")]
    [InlineData("global.sim.max_zero_progress_ticks")]
    public void Compiler_RejectsWrongTechnicalSettingType(string key)
    {
        var candidate = CandidateWithValidSettings();
        candidate["settings"]!.AsObject()[key] = "100";

        var result = Compile(candidate);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.InvalidInteger &&
                     issue.Path == "$.settings." + key);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(200_001)]
    public void Compiler_RejectsEventCapOutsideWp06Range(int value)
    {
        var result = CompileWithSettings(value, 100);

        AssertRangeRejection(result, "global.sim.max_events_per_battle");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Compiler_RejectsNonPositiveZeroProgressCap(int value)
    {
        var result = CompileWithSettings(200_000, value);

        AssertRangeRejection(result, "global.sim.max_zero_progress_ticks");
    }

    private static ConfigCompilationResult CompileWithSettings(
        int maximumEvents,
        int maximumZeroProgressTicks)
    {
        var candidate = ConfigFixture.ReadConfigObject();
        var settings = candidate["settings"]!.AsObject();
        settings["global.sim.max_events_per_battle"] = maximumEvents;
        settings["global.sim.max_zero_progress_ticks"] = maximumZeroProgressTicks;
        return Compile(candidate);
    }

    private static JsonObject CandidateWithValidSettings()
        => ConfigFixture.ReadConfigObject();

    private static ConfigCompilationResult Compile(JsonObject candidate) =>
        new BattleConfigCompiler().Compile(Encoding.UTF8.GetBytes(candidate.ToJsonString()));

    private static void AssertRangeRejection(ConfigCompilationResult result, string key)
    {
        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == ConfigValidationCodes.NumericOutOfRange &&
                     issue.Path == "$.settings." + key);
    }
}
