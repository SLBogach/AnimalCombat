using Battle.Core;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Engine;

public sealed class BattleSetupValidationTests
{
    [Theory]
    [InlineData("engine", "EngineVersionMismatch", "/engine_version")]
    [InlineData("config_hash", "ConfigHashMismatch", "/config_hash")]
    [InlineData("balance_schema", "BalanceSchemaVersionMismatch", "/config/balance_schema_version")]
    [InlineData("config_version", "ConfigVersionMismatch", "/config/config_version")]
    [InlineData("schema_setting", "ConfigVersionMismatch", "/config/settings/global.sim.schema_version")]
    [InlineData("config_setting", "ConfigVersionMismatch", "/config/settings/global.sim.config_version")]
    [InlineData("rng", "ConfigVersionMismatch", "/config/settings/global.sim.rng_version")]
    [InlineData("ordering", "ConfigVersionMismatch", "/config/settings/global.sim.ordering_version")]
    [InlineData("mode", "ModeRulesVersionMismatch", "/mode_rules/version")]
    public void WP06_VAL_001_EveryVersionAndHashMismatchIsRejectedBeforeBegin(
        string mutation,
        string expectedCode,
        string expectedPath)
    {
        var mismatchDigest = new Sha256Digest(
            "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc");
        var request = mutation switch
        {
            "engine" => EngineTestFixture.CreateRequest(
                engineVersion: new ArtifactVersion("battle.core/9.9.9")),
            "config_hash" => EngineTestFixture.CreateRequest(configHash: mismatchDigest),
            "mode" => EngineTestFixture.CreateRequest(
                modeVersion: new ArtifactVersion("mode.rules/9.9")),
            _ => EngineTestFixture.CreateRequest(),
        };
        var config = mutation switch
        {
            "balance_schema" => EngineTestFixture.CreateConfig(
                balanceSchemaVersion: new ArtifactVersion("combat.balance/9.9")),
            "config_version" => EngineTestFixture.CreateConfig(
                configVersion: new ArtifactVersion("v9.9")),
            "schema_setting" => EngineTestFixture.CreateConfig(
                changeSettings: source => ReplaceSetting(
                    source,
                    "global.sim.schema_version",
                    ConfigValue.FromString("combat.balance/9.9"))),
            "config_setting" => EngineTestFixture.CreateConfig(
                changeSettings: source => ReplaceSetting(
                    source,
                    "global.sim.config_version",
                    ConfigValue.FromString("v9.9"))),
            "rng" => EngineTestFixture.CreateConfig(
                changeSettings: source => ReplaceSetting(
                    source,
                    "global.sim.rng_version",
                    ConfigValue.FromString("pcg32/9"))),
            "ordering" => EngineTestFixture.CreateConfig(
                changeSettings: source => ReplaceSetting(
                    source,
                    "global.sim.ordering_version",
                    ConfigValue.FromString("tick-pipeline/9"))),
            _ => EngineTestFixture.CreateConfig(),
        };
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(request, config, journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == expectedCode && error.Path == expectedPath);
    }

    [Fact]
    public void WP06_VAL_002_UnknownOrForbiddenBuildIdsAreRejectedBeforeBegin()
    {
        var allowed = EngineTestFixture.ActionIds()
            .Where(id => id.Value != "bear_earthbreaker")
            .ToArray();
        var request = EngineTestFixture.CreateRequest(allowedActions: allowed);
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(request, EngineTestFixture.CreateConfig(), journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(result.RejectionErrors, error => error.Code.Value == "ForbiddenStableId");
    }

    [Theory]
    [InlineData("animal", "unknown_animal")]
    [InlineData("action", "unknown_action")]
    [InlineData("passive", "unknown_passive")]
    [InlineData("gear", "unknown_gear")]
    [InlineData("tactic", "unknown_tactic")]
    public void WP06_VAL_002_EveryModeAllowlistEntryMustExistInItsCatalog(
        string catalog,
        string unknownValue)
    {
        var unknown = new StableId(unknownValue);
        var request = catalog switch
        {
            "animal" => EngineTestFixture.CreateRequest(
                allowedAnimals: new[] { new StableId("bear"), new StableId("kangaroo"), unknown }),
            "action" => EngineTestFixture.CreateRequest(
                allowedActions: EngineTestFixture.ActionIds().Append(unknown)),
            "passive" => EngineTestFixture.CreateRequest(
                allowedPassives: new[]
                {
                    new StableId("bear_thick_hide"),
                    new StableId("kangaroo_never_still"),
                    unknown,
                }),
            "gear" => EngineTestFixture.CreateRequest(
                allowedGear: EngineTestFixture.GearIds().Append(unknown)),
            "tactic" => EngineTestFixture.CreateRequest(
                allowedTactics: new[]
                {
                    new StableId("tactic_position"),
                    new StableId("tactic_pressure"),
                    unknown,
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(catalog)),
        };
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            request,
            EngineTestFixture.CreateConfig(),
            journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "UnknownStableId" &&
                     error.Path.StartsWith("/mode_rules/allowed_", StringComparison.Ordinal) &&
                     error.EntityId?.Value == unknownValue);
    }

    [Fact]
    public void WP06_VAL_002_ModeAllowlistRejectsAnIdFromTheWrongCatalog()
    {
        var request = EngineTestFixture.CreateRequest(
            allowedActions: EngineTestFixture.ActionIds().Append(new StableId("bear")));
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            request,
            EngineTestFixture.CreateConfig(),
            journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "WrongCatalog" &&
                     error.Path.StartsWith("/mode_rules/allowed_action_ids/", StringComparison.Ordinal) &&
                     error.EntityId?.Value == "bear");
    }

    [Fact]
    public void WP06_VAL_002_ModeAllowlistValidatesOwnershipSlotAndKindForEveryEntry()
    {
        var wrongKindTactic = new StableId("strategy_control");
        var request = EngineTestFixture.CreateRequest(
            allowedTactics: new[]
            {
                new StableId("tactic_position"),
                new StableId("tactic_pressure"),
                wrongKindTactic,
            });
        var config = EngineTestFixture.CreateConfig(
            changeActions: source => EngineTestFixture.ReindexCatalog(source.Select(action =>
                action.Id.Value switch
                {
                    "sys_wait" => EngineTestFixture.WithProperty(
                        action,
                        "animal_id",
                        ConfigValue.FromString("bear")),
                    "bear_earthbreaker" => EngineTestFixture.WithProperty(
                        action,
                        "slot_type",
                        ConfigValue.FromString("Mystery")),
                    _ => action,
                })),
            changePassives: source => EngineTestFixture.ReindexCatalog(source.Select(passive =>
                passive.Id.Value == "bear_thick_hide"
                    ? EngineTestFixture.WithProperty(
                        passive,
                        "animal_id",
                        ConfigValue.FromString("kangaroo"))
                    : passive)),
            changeTactics: source => EngineTestFixture.ReindexCatalog(source.Append(
                EngineTestFixture.Entity(wrongKindTactic.Value, 0))),
            changeGear: source => EngineTestFixture.ReindexCatalog(source.Select(gear =>
                gear.Id.Value == "gear_offense_power_wraps"
                    ? EngineTestFixture.WithProperty(
                        gear,
                        "slot",
                        ConfigValue.FromString("Utility"))
                    : gear)));
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(request, config, journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "WrongOwner" &&
                     error.Path.StartsWith("/mode_rules/allowed_action_ids/", StringComparison.Ordinal));
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "WrongSlot" &&
                     error.Path.StartsWith("/mode_rules/allowed_action_ids/", StringComparison.Ordinal));
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "WrongOwner" &&
                     error.Path.StartsWith("/mode_rules/allowed_passive_ids/", StringComparison.Ordinal));
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "WrongKind" &&
                     error.Path.StartsWith("/mode_rules/allowed_gear_ids/", StringComparison.Ordinal));
        Assert.Contains(
            result.RejectionErrors,
            error => error.Code.Value == "WrongKind" &&
                     error.Path.StartsWith("/mode_rules/allowed_tactic_ids/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("global.sim.max_events_per_battle", "missing")]
    [InlineData("global.sim.max_events_per_battle", "wrong_type")]
    [InlineData("global.sim.max_events_per_battle", "out_of_range")]
    [InlineData("global.sim.max_zero_progress_ticks", "missing")]
    [InlineData("global.sim.max_zero_progress_ticks", "wrong_type")]
    [InlineData("global.sim.max_zero_progress_ticks", "out_of_range")]
    public void WP06_VAL_003_TechnicalSettingErrorsAreRejectedWithoutDefaults(
        string setting,
        string mutation)
    {
        var config = EngineTestFixture.CreateConfig(changeSettings: source =>
        {
            var items = source.ToList();
            var index = items.FindIndex(item => item.Name == setting);
            if (mutation == "missing")
            {
                items.RemoveAt(index);
            }
            else
            {
                items[index] = new ConfigProperty(
                    setting,
                    mutation == "wrong_type"
                        ? ConfigValue.FromString("200000")
                        : ConfigValue.FromInteger(
                            setting == "global.sim.max_events_per_battle" ? 3 : 0));
            }

            return items;
        });
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(EngineTestFixture.CreateRequest(), config, journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(
            result.RejectionErrors,
            error => error.Path.EndsWith(setting, StringComparison.Ordinal));
    }

    [Fact]
    public void WP06_VAL_004_UnsupportedNormalizationIsRejectedBeforeBegin()
    {
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(
            EngineTestFixture.CreateRequest(NormalizationMode.NormalizedRating),
            EngineTestFixture.CreateConfig(),
            journal);

        AssertRejectedBeforeBegin(result, journal);
        Assert.Contains(result.RejectionErrors, error => error.Code.Value == "UnsupportedNormalization");
    }

    [Fact]
    public void WP06_VAL_RejectionErrorsAreSortedByPathCodeAndMessageKey()
    {
        var request = EngineTestFixture.CreateRequest(
            engineVersion: new ArtifactVersion("battle.core/9.9.9"),
            configHash: new Sha256Digest(
                "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"),
            modeVersion: new ArtifactVersion("mode.rules/9.9"),
            allowedActions: EngineTestFixture.ActionIds().Append(new StableId("bear")));
        var config = EngineTestFixture.CreateConfig(changeSettings: source =>
            source.Where(property => property.Name != "global.sim.max_zero_progress_ticks"));
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(request, config, journal);

        AssertRejectedBeforeBegin(result, journal);
        var actual = result.RejectionErrors
            .Select(error => (error.Path, Code: error.Code.Value, MessageKey: error.MessageKey.Value))
            .ToArray();
        var expected = actual
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)
            .ThenBy(error => error.MessageKey, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WP06_RAW_002_NullTypedApiArgumentsRemainProgrammingErrors()
    {
        var engine = new CombatEngine();
        var request = EngineTestFixture.CreateRequest();
        var config = EngineTestFixture.CreateConfig();
        var journal = new RecordingJournal();

        Assert.Throws<ArgumentNullException>(() => engine.Simulate(null!, config, journal));
        Assert.Throws<ArgumentNullException>(() => engine.Simulate(request, null!, journal));
        Assert.Throws<ArgumentNullException>(() => engine.Simulate(request, config, null!));
    }

    [Fact]
    public void WP06_INIT_002_InvalidFighterPrerequisiteDoesNotPartiallyInitializeOrBegin()
    {
        var config = EngineTestFixture.CreateConfig(changeFighters: fighters =>
        {
            var items = fighters.ToArray();
            items[0] = EngineTestFixture.Entity(
                "bear",
                0,
                ("initiative", ConfigValue.FromInteger(85)),
                ("max_energy", ConfigValue.FromInteger(1_000)),
                ("max_resource", ConfigValue.FromInteger(1_000)),
                ("resource_id", ConfigValue.FromString("rage")),
                ("stagger_threshold", ConfigValue.FromInteger(260)),
                ("start_resource", ConfigValue.FromInteger(0)));
            return items;
        });
        var journal = new RecordingJournal();

        var result = new CombatEngine().Simulate(EngineTestFixture.CreateRequest(), config, journal);

        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Equal(0, journal.BeginCount);
        Assert.Empty(journal.Drafts);
    }

    private static IEnumerable<ConfigProperty> ReplaceSetting(
        IEnumerable<ConfigProperty> source,
        string name,
        ConfigValue value) =>
        source.Select(property => property.Name == name
            ? new ConfigProperty(name, value)
            : property);

    private static void AssertRejectedBeforeBegin(
        BattleResult result,
        RecordingJournal journal)
    {
        Assert.Equal(BattleResultStatus.Rejected, result.Status);
        Assert.Equal(0, journal.BeginCount);
        Assert.Empty(journal.Drafts);
        Assert.Equal(0, journal.CompleteCount);
    }
}
