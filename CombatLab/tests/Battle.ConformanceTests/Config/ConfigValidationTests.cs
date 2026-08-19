using System.Text;
using System.Text.Json.Nodes;
using Battle.Config.Compiler;
using Battle.Config.Semantic;

namespace Battle.ConformanceTests.Config;

public sealed class ConfigValidationTests
{
    [Fact]
    public void Loader_RejectsManifestHashMismatch()
    {
        var manifest = JsonNode.Parse(ConfigFixture.ReadManifestBytes())!.AsObject();
        manifest["config_hash"] = "sha256:" + new string('0', 64);

        var result = new global::Battle.Config.BattleConfigLoader().Load(
            ConfigFixture.ReadConfigBytes(),
            Encoding.UTF8.GetBytes(manifest.ToJsonString()));

        AssertLoadRejected(result, ConfigValidationCodes.ConfigHashMismatch);
    }

    [Fact]
    public void Loader_RejectsNoncanonicalRuntimeBytes()
    {
        var canonical = ConfigFixture.ReadConfigBytes();
        var noncanonical = new byte[canonical.Length + 1];
        canonical.CopyTo(noncanonical, 0);
        noncanonical[^1] = (byte)' ';

        var result = new global::Battle.Config.BattleConfigLoader().Load(
            noncanonical,
            ConfigFixture.ReadManifestBytes());

        AssertLoadRejected(result, ConfigValidationCodes.ConfigNotCanonical);
    }

    [Fact]
    public void Compiler_RejectsDuplicateMember()
    {
        AssertCompileRejected(
            ConfigFixture.WithDuplicateRootMember(),
            ConfigValidationCodes.DuplicateJsonMember);
    }

    [Fact]
    public void Compiler_RejectsInvalidUtf8()
    {
        AssertCompileRejected(
            ConfigFixture.WithInvalidUtf8(),
            ConfigValidationCodes.InvalidUtf8);
    }

    [Theory]
    [InlineData("unknown_member", ConfigValidationCodes.UnknownJsonMember)]
    [InlineData("missing_required", ConfigValidationCodes.MissingRequiredConfigKey)]
    [InlineData("float", ConfigValidationCodes.InvalidInteger)]
    [InlineData("numeric_string", ConfigValidationCodes.InvalidInteger)]
    [InlineData("enum_case", ConfigValidationCodes.InvalidEnumValue)]
    public void Compiler_RejectsStrictJsonContractViolations(
        string mutation,
        string expectedCode)
    {
        var candidate = ConfigFixture.Mutate(root => ApplyStrictMutation(root, mutation));

        AssertCompileRejected(candidate, expectedCode);
    }

    [Theory]
    [InlineData("broken_reference", ConfigValidationCodes.UnknownStableId)]
    [InlineData("wrong_owner", ConfigValidationCodes.WrongOwner)]
    [InlineData("wrong_slot", ConfigValidationCodes.WrongSlot)]
    [InlineData("zero_divisor", ConfigValidationCodes.ZeroDivisor)]
    [InlineData("arena_bounds", ConfigValidationCodes.InvalidArenaBounds)]
    [InlineData("duration", ConfigValidationCodes.InvalidDuration)]
    [InlineData("range", ConfigValidationCodes.NumericOutOfRange)]
    [InlineData("overflow", ConfigValidationCodes.ArithmeticOverflowRisk)]
    [InlineData("conflict_matrix", ConfigValidationCodes.InvalidConflictMatrix)]
    public void Compiler_RejectsSemanticViolations(
        string mutation,
        string expectedCode)
    {
        var candidate = ConfigFixture.Mutate(root => ApplySemanticMutation(root, mutation));

        AssertCompileRejected(candidate, expectedCode);
    }

    private static void ApplyStrictMutation(JsonObject root, string mutation)
    {
        var settings = root["settings"]!.AsObject();
        switch (mutation)
        {
            case "unknown_member":
                settings["global.sim.hidden_default"] = 1;
                break;
            case "missing_required":
                settings.Remove("global.sim.tick_ms");
                break;
            case "float":
                settings["global.sim.tick_ms"] = 100.5;
                break;
            case "numeric_string":
                settings["global.sim.tick_ms"] = "100";
                break;
            case "enum_case":
                ConfigFixture.Entity(root, "actions", "action_id", "bear_paw_jab")["slot_type"] = "basic";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown strict mutation.");
        }
    }

    private static void ApplySemanticMutation(JsonObject root, string mutation)
    {
        var settings = root["settings"]!.AsObject();
        switch (mutation)
        {
            case "broken_reference":
                ConfigFixture.Entity(root, "passives", "passive_id", "bear_thick_hide")["effect_id"] =
                    "effect_missing";
                break;
            case "wrong_owner":
                ConfigFixture.Entity(root, "actions", "action_id", "bear_paw_jab")["animal_id"] = "kangaroo";
                break;
            case "wrong_slot":
                ConfigFixture.Entity(
                    root,
                    "gear",
                    "gear_id",
                    "gear_defense_reinforced_hide")["slot"] = "Offense";
                break;
            case "zero_divisor":
                settings["global.sim.fp_scale"] = 0;
                break;
            case "arena_bounds":
                settings["global.arena.min_position"] = 10_000;
                settings["global.arena.max_position"] = 10_000;
                break;
            case "duration":
                ConfigFixture.Entity(root, "actions", "action_id", "bear_paw_jab")["active_ticks"] = -1;
                break;
            case "range":
                ConfigFixture.Entity(root, "actions", "action_id", "bear_paw_jab")["hit_range_min"] = 1_051;
                break;
            case "overflow":
                settings["global.arena.wall_zone_size"] = long.MaxValue;
                break;
            case "conflict_matrix":
                ConfigFixture.Entity(
                    root,
                    "effects",
                    "effect_id",
                    "effect_bear_thick_hide")["stack_group"] = "control_fatigue";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown semantic mutation.");
        }
    }

    private static void AssertCompileRejected(byte[] candidate, string expectedCode)
    {
        var result = new BattleConfigCompiler().Compile(candidate);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Config);
        Assert.Null(result.ConfigHash);
        Assert.Empty(result.GetCanonicalJson());
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }

    private static void AssertLoadRejected(
        global::Battle.Config.ConfigLoadResult result,
        string expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Null(result.Config);
        Assert.Contains(result.Issues, issue => issue.Code == expectedCode);
    }
}
