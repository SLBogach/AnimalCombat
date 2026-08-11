using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using CombatLab.Runner.Battles;

namespace CombatLab.IntegrationTests.RawRequests;

public sealed class BattleCaseFactoryTests
{
    [Fact]
    public void TryCreate_AccumulatesMissingAndMalformedIdentifiersWithoutThrowing()
    {
        var raw = ValidRequest() with
        {
            BattleId = null,
            EngineVersion = "bad version",
            ConfigHash = "sha256:not-a-digest",
            ModeRules = ValidModeRules() with { Id = "Invalid-Mode" },
            BuildA = ValidBuildA() with
            {
                AnimalId = null,
                PassiveId = "Bad-Passive",
            },
        };

        var exception = Record.Exception(() => BattleCaseFactory.TryCreate(raw));
        var result = BattleCaseFactory.TryCreate(raw);

        Assert.Null(exception);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Request);
        Assert.Contains(result.Errors, error => error.Path == "$.battle_id" && error.Code.Value == "MissingRequiredValue");
        Assert.Contains(result.Errors, error => error.Path == "$.engine_version" && error.Code.Value == "InvalidIdentifier");
        Assert.Contains(result.Errors, error => error.Path == "$.config_hash" && error.Code.Value == "InvalidIdentifier");
        Assert.Contains(result.Errors, error => error.Path == "$.mode_rules.id" && error.Code.Value == "InvalidIdentifier");
        Assert.Contains(result.Errors, error => error.Path == "$.build_a.animal_id" && error.Code.Value == "MissingRequiredValue");
        Assert.Contains(result.Errors, error => error.Path == "$.build_a.passive_id" && error.Code.Value == "InvalidIdentifier");
        AssertDeterministicallySorted(result);
    }

    [Fact]
    public void TryCreate_RejectsRequestSlotAndFighterSideMismatches()
    {
        var raw = ValidRequest() with
        {
            BuildA = ValidBuildA() with
            {
                FighterId = "fighter_b",
                Side = "B",
            },
            BuildB = ValidBuildB() with
            {
                FighterId = "fighter_b",
                Side = "A",
            },
        };

        var result = BattleCaseFactory.TryCreate(raw);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            new[]
            {
                "$.build_a.fighter_id",
                "$.build_a.side",
                "$.build_b.side",
            },
            result.Errors.Select(error => error.Path));
        Assert.All(result.Errors, error => Assert.Equal("SlotMismatch", error.Code.Value));
    }

    [Fact]
    public void TryCreate_AccumulatesSpecialCountAndDuplicateErrors()
    {
        var raw = ValidRequest() with
        {
            BuildA = ValidBuildA() with
            {
                SpecialActionIds = new[]
                {
                    "bear_earthbreaker",
                    "bear_earthbreaker",
                    "bear_rampage_charge",
                },
            },
        };

        var result = BattleCaseFactory.TryCreate(raw);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("DuplicateItem", result.Errors[0].Code.Value);
        Assert.Equal("InvalidItemCount", result.Errors[1].Code.Value);
        Assert.All(result.Errors, error => Assert.Equal("$.build_a.special_action_ids", error.Path));
    }

    [Fact]
    public void TryCreate_CreatesStrictRequestOnlyWhenRawShapeIsValid()
    {
        var result = BattleCaseFactory.TryCreate(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Errors);
        var request = Assert.IsType<BattleRequest>(result.Request);
        Assert.Equal(new ExternalId("battle-wp06-raw-success"), request.BattleId);
        Assert.Equal(42UL, request.MasterSeed);
        Assert.Equal(FighterId.FighterA, request.BuildA.FighterId);
        Assert.Equal(FighterSide.B, request.BuildB.Side);
        Assert.Equal(new StableId("engine_shell_wait_v01"), request.ModeRules.Id);
        Assert.Equal(NormalizationMode.None, request.ModeRules.NormalizationMode);
        Assert.Equal(
            new[] { "bear", "kangaroo" },
            request.ModeRules.AllowedAnimalIds.Select(id => id.Value));
        Assert.Equal(
            new[] { "sys_wait", "zz_test_action" },
            request.ModeRules.AllowedActionIds.Select(id => id.Value));
    }

    [Fact]
    public void TryCreate_NullRequestReturnsARejectionInsteadOfThrowing()
    {
        var exception = Record.Exception(() => BattleCaseFactory.TryCreate(null));
        var result = BattleCaseFactory.TryCreate(null);

        Assert.Null(exception);
        Assert.False(result.IsSuccess);
        var error = Assert.Single(result.Errors);
        Assert.Equal("$", error.Path);
        Assert.Equal("MissingRequiredValue", error.Code.Value);
    }

    private static void AssertDeterministicallySorted(BattleCaseFactoryResult result)
    {
        var actual = result.Errors
            .Select(error => (error.Path, Code: error.Code.Value, MessageKey: error.MessageKey.Value))
            .ToArray();
        var expected = actual
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)
            .ThenBy(error => error.MessageKey, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static RawBattleRequest ValidRequest() =>
        new(
            "battle-wp06-raw-success",
            "engine/0.1",
            "sha256:" + new string('1', 64),
            ValidModeRules(),
            42,
            ValidBuildA(),
            ValidBuildB());

    private static RawModeRules ValidModeRules() =>
        new(
            "engine_shell_wait_v01",
            "mode.rules/0.1",
            "None",
            new[] { "kangaroo", "bear" },
            new[] { "zz_test_action", "sys_wait" },
            new[] { "kangaroo_never_still", "bear_thick_hide" },
            new[]
            {
                "gear_utility_sprint_soles",
                "gear_offense_power_wraps",
                "gear_defense_reinforced_hide",
            },
            new[] { "tactic_position", "tactic_pressure" });

    private static RawFighterBuild ValidBuildA() =>
        new(
            "fighter_a",
            "A",
            "bear",
            "build_bear",
            new[] { "bear_earthbreaker", "bear_rampage_charge" },
            "bear_thick_hide",
            "gear_offense_power_wraps",
            "gear_defense_reinforced_hide",
            "gear_utility_sprint_soles",
            "tactic_pressure");

    private static RawFighterBuild ValidBuildB() =>
        new(
            "fighter_b",
            "B",
            "kangaroo",
            "build_kangaroo",
            new[] { "kangaroo_flying_kick", "kangaroo_tail_counter" },
            "kangaroo_never_still",
            "gear_offense_precision_lens",
            "gear_defense_reinforced_hide",
            "gear_utility_sprint_soles",
            "tactic_position");
}
