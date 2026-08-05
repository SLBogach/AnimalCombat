using System.Globalization;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Random;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core.Initialization;

internal static class BattleSetupFactory
{
    private const string ExpectedConfigVersion = "v0.1";
    private const string TimeLimitKey = "battle.time_limit_ticks";
    private const string MaximumEventsKey = "global.sim.max_events_per_battle";
    private const string MaximumZeroProgressKey = "global.sim.max_zero_progress_ticks";
    private const string FixedPointScaleKey = "global.sim.fp_scale";
    private const string ArenaMinimumKey = "global.arena.min_position";
    private const string ArenaMaximumKey = "global.arena.max_position";
    private const string StartPositionAKey = "global.arena.start_position_a";
    private const string StartPositionBKey = "global.arena.start_position_b";

    internal static BattleSetupResult Create(BattleRequest request, CompiledBattleConfig config)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        var issues = new List<ValidationIssue>();
        ValidateVersions(request, config, issues);
        ValidateMode(request.ModeRules, issues);
        ValidateModeAllowlists(request.ModeRules, config, issues);

        var timeLimit = ReadRequiredSetting(config, TimeLimitKey, 1, int.MaxValue, issues);
        var maximumEvents = ReadRequiredSetting(config, MaximumEventsKey, 4, 200_000, issues);
        var maximumZeroProgress = ReadRequiredSetting(
            config,
            MaximumZeroProgressKey,
            1,
            int.MaxValue,
            issues);
        var fixedPointScale = ReadRequiredSetting(config, FixedPointScaleKey, 1, int.MaxValue, issues);
        var arenaMinimum = ReadRequiredSetting(config, ArenaMinimumKey, int.MinValue, int.MaxValue, issues);
        var arenaMaximum = ReadRequiredSetting(config, ArenaMaximumKey, int.MinValue, int.MaxValue, issues);
        var startPositionA = ReadRequiredSetting(config, StartPositionAKey, int.MinValue, int.MaxValue, issues);
        var startPositionB = ReadRequiredSetting(config, StartPositionBKey, int.MinValue, int.MaxValue, issues);

        ValidateArena(arenaMinimum, arenaMaximum, startPositionA, startPositionB, issues);
        var buildA = ValidateBuild(request.BuildA, request.ModeRules, config, "/fighters/0", issues);
        var buildB = ValidateBuild(request.BuildB, request.ModeRules, config, "/fighters/1", issues);
        var systemApproach = ValidateSystemMovementAction(
            SystemActionSelector.ApproachId,
            "Approach",
            config,
            issues);
        var systemRetreat = ValidateSystemMovementAction(
            SystemActionSelector.RetreatId,
            "Retreat",
            config,
            issues);
        var systemWait = ValidateSystemWait(request.ModeRules, config, issues);
        if (systemApproach is not null && systemRetreat is not null &&
            systemApproach.PreferredRangeMaximum > systemRetreat.PreferredRangeMinimum)
        {
            issues.Add(new ValidationIssue(
                "InvalidNeutralBand",
                "/system_actions/sys_retreat/preferred_range_min",
                systemRetreat.Id.Value));
        }

        if (issues.Count != 0 ||
            !timeLimit.HasValue ||
            !maximumEvents.HasValue ||
            !maximumZeroProgress.HasValue ||
            !fixedPointScale.HasValue ||
            !arenaMinimum.HasValue ||
            !arenaMaximum.HasValue ||
            !startPositionA.HasValue ||
            !startPositionB.HasValue ||
            buildA is null ||
            buildB is null ||
            systemApproach is null ||
            systemRetreat is null ||
            systemWait is null)
        {
            return new BattleSetupResult(null, ToRejectionErrors(issues));
        }

        var fighterA = TryInitializeFighter(
            request.BuildA,
            buildA,
            startPositionA.Value,
            Facing.Right,
            fixedPointScale.Value,
            "/fighters/0",
            issues);
        var fighterB = TryInitializeFighter(
            request.BuildB,
            buildB,
            startPositionB.Value,
            Facing.Left,
            fixedPointScale.Value,
            "/fighters/1",
            issues);
        if (fighterA is null || fighterB is null || issues.Count != 0)
        {
            return new BattleSetupResult(null, ToRejectionErrors(issues));
        }

        ValidateInitialGeometry(
            arenaMinimum.Value,
            arenaMaximum.Value,
            fighterA,
            fighterB,
            issues);
        if (issues.Count != 0)
        {
            return new BattleSetupResult(null, ToRejectionErrors(issues));
        }

        var state = new BattleState(fighterA, fighterB, request.MasterSeed);
        var arena = new ArenaSnapshot(
            new StableId("combat_lab_arena"),
            arenaMinimum.Value,
            arenaMaximum.Value,
            startPositionA.Value,
            startPositionB.Value);
        var initiative = DetermineInitiative(fighterA, fighterB, request.MasterSeed);
        var allowedSystemActionIds = request.ModeRules.AllowedActionIds
            .Where(id => id == SystemActionSelector.ApproachId ||
                         id == SystemActionSelector.RetreatId ||
                         id == SystemActionSelector.WaitId)
            .OrderBy(id => id)
            .ToArray();
        var settings = new RuntimeBattleSettings(
            timeLimit.Value,
            maximumEvents.Value,
            maximumZeroProgress.Value,
            fixedPointScale.Value,
            arena,
            systemApproach,
            systemRetreat,
            systemWait,
            allowedSystemActionIds,
            initiative);

        return new BattleSetupResult(
            new BattleSetup(state, settings, initiative),
            Array.Empty<BattleRejectionError>());
    }

    private static void ValidateVersions(
        BattleRequest request,
        CompiledBattleConfig config,
        ICollection<ValidationIssue> issues)
    {
        AddMismatch(
            request.EngineVersion == ContractVersions.Engine,
            "EngineVersionMismatch",
            "/engine_version",
            request.EngineVersion.ToString(),
            issues);
        AddMismatch(
            request.ConfigHash == config.Reference.ConfigHash,
            "ConfigHashMismatch",
            "/config_hash",
            request.ConfigHash.ToString(),
            issues);
        AddMismatch(
            config.Reference.BalanceSchemaVersion == ContractVersions.BalanceSchema,
            "BalanceSchemaVersionMismatch",
            "/config/balance_schema_version",
            config.Reference.BalanceSchemaVersion.ToString(),
            issues);
        AddMismatch(
            string.Equals(
                config.Reference.ConfigVersion.ToString(),
                ExpectedConfigVersion,
                StringComparison.Ordinal),
            "ConfigVersionMismatch",
            "/config/config_version",
            config.Reference.ConfigVersion.ToString(),
            issues);
        AddMismatch(
            request.ModeRules.Version == ContractVersions.ModeRules,
            "ModeRulesVersionMismatch",
            "/mode_rules/version",
            request.ModeRules.Version.ToString(),
            issues);

        ValidateVersionSetting(
            config,
            "global.sim.schema_version",
            ContractVersions.BalanceSchema.ToString(),
            config.Reference.BalanceSchemaVersion.ToString(),
            issues);
        ValidateVersionSetting(
            config,
            "global.sim.config_version",
            ExpectedConfigVersion,
            config.Reference.ConfigVersion.ToString(),
            issues);
        ValidateVersionSetting(
            config,
            "global.sim.rng_version",
            ContractVersions.Rng.ToString(),
            ContractVersions.Rng.ToString(),
            issues);
        ValidateVersionSetting(
            config,
            "global.sim.ordering_version",
            ContractVersions.Ordering.ToString(),
            ContractVersions.Ordering.ToString(),
            issues);
    }

    private static void ValidateVersionSetting(
        CompiledBattleConfig config,
        string name,
        string expected,
        string entityValue,
        ICollection<ValidationIssue> issues)
    {
        var path = SettingPath(name);
        if (!config.TryGetSetting(name, out var value))
        {
            issues.Add(new ValidationIssue("MissingRequiredConfigKey", path, null));
            return;
        }

        if (value.Kind != ConfigValueKind.String)
        {
            issues.Add(new ValidationIssue("InvalidConfigValueType", path, null));
            return;
        }

        if (!string.Equals(value.AsString(), expected, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("ConfigVersionMismatch", path, entityValue));
        }
    }

    private static void ValidateMode(ModeRulesSnapshot modeRules, ICollection<ValidationIssue> issues)
    {
        if (modeRules.NormalizationMode != NormalizationMode.None)
        {
            issues.Add(new ValidationIssue(
                "UnsupportedNormalization",
                "/mode_rules/normalization_mode",
                modeRules.Id.Value));
        }
    }

    private static void ValidateModeAllowlists(
        ModeRulesSnapshot modeRules,
        CompiledBattleConfig config,
        ICollection<ValidationIssue> issues)
    {
        for (var index = 0; index < modeRules.AllowedAnimalIds.Count; index++)
        {
            var id = modeRules.AllowedAnimalIds[index];
            var path = AllowlistPath("allowed_animal_ids", index);
            if (!config.TryGetFighter(id, out _))
            {
                AddCatalogIssue(config, id, CatalogKind.Fighter, path, issues);
            }
        }

        for (var index = 0; index < modeRules.AllowedActionIds.Count; index++)
        {
            var id = modeRules.AllowedActionIds[index];
            var path = AllowlistPath("allowed_action_ids", index);
            if (!config.TryGetAction(id, out var action) || action is null)
            {
                AddCatalogIssue(config, id, CatalogKind.Action, path, issues);
                continue;
            }

            ValidateAllowedAction(action, modeRules, config, path, issues);
        }

        for (var index = 0; index < modeRules.AllowedPassiveIds.Count; index++)
        {
            var id = modeRules.AllowedPassiveIds[index];
            var path = AllowlistPath("allowed_passive_ids", index);
            if (!config.TryGetPassive(id, out var passive) || passive is null)
            {
                AddCatalogIssue(config, id, CatalogKind.Passive, path, issues);
                continue;
            }

            ValidateAllowedPassive(passive, modeRules, config, path, issues);
        }

        for (var index = 0; index < modeRules.AllowedGearIds.Count; index++)
        {
            var id = modeRules.AllowedGearIds[index];
            var path = AllowlistPath("allowed_gear_ids", index);
            if (!config.TryGetGear(id, out var gear) || gear is null)
            {
                AddCatalogIssue(config, id, CatalogKind.Gear, path, issues);
                continue;
            }

            ValidateAllowedGear(gear, path, issues);
        }

        for (var index = 0; index < modeRules.AllowedTacticIds.Count; index++)
        {
            var id = modeRules.AllowedTacticIds[index];
            var path = AllowlistPath("allowed_tactic_ids", index);
            if (!config.TryGetTactic(id, out var tactic) || tactic is null)
            {
                AddCatalogIssue(config, id, CatalogKind.Tactic, path, issues);
                continue;
            }

            if (!tactic.Id.Value.StartsWith("tactic_", StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("WrongKind", path, tactic.Id.Value));
            }
        }
    }

    private static void ValidateAllowedAction(
        CompiledConfigEntity action,
        ModeRulesSnapshot modeRules,
        CompiledBattleConfig config,
        string path,
        ICollection<ValidationIssue> issues)
    {
        var hasOwner = TryGetEntityString(action, "animal_id", out var owner);
        var hasSlot = TryGetEntityString(action, "slot_type", out var slot);
        if (!hasOwner)
        {
            issues.Add(new ValidationIssue("WrongOwner", path, action.Id.Value));
        }

        if (!hasSlot)
        {
            issues.Add(new ValidationIssue("WrongSlot", path, action.Id.Value));
        }

        if (!hasOwner || !hasSlot)
        {
            return;
        }

        if (string.Equals(slot, "System", StringComparison.Ordinal))
        {
            if (!string.Equals(owner, "all", StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("WrongOwner", path, action.Id.Value));
            }

            if (!action.Id.Value.StartsWith("sys_", StringComparison.Ordinal))
            {
                issues.Add(new ValidationIssue("WrongKind", path, action.Id.Value));
            }

            return;
        }

        if (!string.Equals(slot, "Basic", StringComparison.Ordinal) &&
            !string.Equals(slot, "Special", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("WrongSlot", path, action.Id.Value));
        }

        if (!StableId.TryParse(owner, out var ownerId) ||
            !config.TryGetFighter(ownerId, out _) ||
            !modeRules.AllowedAnimalIds.Contains(ownerId))
        {
            issues.Add(new ValidationIssue("WrongOwner", path, action.Id.Value));
            return;
        }

        if (!action.Id.Value.StartsWith(owner + "_", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("WrongOwner", path, action.Id.Value));
        }
    }

    private static void ValidateAllowedPassive(
        CompiledConfigEntity passive,
        ModeRulesSnapshot modeRules,
        CompiledBattleConfig config,
        string path,
        ICollection<ValidationIssue> issues)
    {
        if (!TryGetEntityString(passive, "animal_id", out var owner) ||
            !StableId.TryParse(owner, out var ownerId) ||
            !config.TryGetFighter(ownerId, out _) ||
            !modeRules.AllowedAnimalIds.Contains(ownerId) ||
            !passive.Id.Value.StartsWith(owner + "_", StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("WrongOwner", path, passive.Id.Value));
        }
    }

    private static void ValidateAllowedGear(
        CompiledConfigEntity gear,
        string path,
        ICollection<ValidationIssue> issues)
    {
        if (!TryGetEntityString(gear, "slot", out var slot) ||
            (slot != "Offense" && slot != "Defense" && slot != "Utility"))
        {
            issues.Add(new ValidationIssue("WrongSlot", path, gear.Id.Value));
            return;
        }

        var expectedPrefix = "gear_" + slot.ToLowerInvariant() + "_";
        if (!gear.Id.Value.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue("WrongKind", path, gear.Id.Value));
        }
    }

    private static void AddCatalogIssue(
        CompiledBattleConfig config,
        StableId id,
        CatalogKind expected,
        string path,
        ICollection<ValidationIssue> issues)
    {
        var existsInAnotherCatalog =
            (expected != CatalogKind.Fighter && config.TryGetFighter(id, out _)) ||
            (expected != CatalogKind.Action && config.TryGetAction(id, out _)) ||
            (expected != CatalogKind.Passive && config.TryGetPassive(id, out _)) ||
            (expected != CatalogKind.Gear && config.TryGetGear(id, out _)) ||
            (expected != CatalogKind.Tactic && config.TryGetTactic(id, out _)) ||
            config.TryGetEffect(id, out _);
        issues.Add(new ValidationIssue(
            existsInAnotherCatalog ? "WrongCatalog" : "UnknownStableId",
            path,
            id.Value));
    }

    private static bool TryGetEntityString(
        CompiledConfigEntity entity,
        string name,
        out string value)
    {
        if (entity.TryGetProperty(name, out var property) &&
            property.Kind == ConfigValueKind.String)
        {
            value = property.AsString();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string AllowlistPath(string name, int index) =>
        "/mode_rules/" + name + "/" + index.ToString(CultureInfo.InvariantCulture);

    private static void ValidateArena(
        int? minimum,
        int? maximum,
        int? startA,
        int? startB,
        ICollection<ValidationIssue> issues)
    {
        if (!minimum.HasValue || !maximum.HasValue || !startA.HasValue || !startB.HasValue)
        {
            return;
        }

        if (minimum.Value >= maximum.Value)
        {
            issues.Add(new ValidationIssue(
                "InvalidConfigRange",
                SettingPath(ArenaMaximumKey),
                null));
        }

        if (startA.Value < minimum.Value || startA.Value > maximum.Value)
        {
            issues.Add(new ValidationIssue(
                "InvalidConfigRange",
                SettingPath(StartPositionAKey),
                null));
        }

        if (startB.Value < minimum.Value || startB.Value > maximum.Value)
        {
            issues.Add(new ValidationIssue(
                "InvalidConfigRange",
                SettingPath(StartPositionBKey),
                null));
        }

        if (startA.Value >= startB.Value)
        {
            issues.Add(new ValidationIssue(
                "InvalidInitialPositions",
                SettingPath(StartPositionBKey),
                null));
        }
    }

    private static void ValidateInitialGeometry(
        int arenaMinimum,
        int arenaMaximum,
        FighterRuntimeState fighterA,
        FighterRuntimeState fighterB,
        ICollection<ValidationIssue> issues)
    {
        int minimumA;
        int maximumA;
        int minimumB;
        int maximumB;
        int minimumDistance;
        try
        {
            minimumA = checked((int)checked((long)arenaMinimum + fighterA.CollisionRadius));
            maximumA = checked((int)checked((long)arenaMaximum - fighterA.CollisionRadius));
            minimumB = checked((int)checked((long)arenaMinimum + fighterB.CollisionRadius));
            maximumB = checked((int)checked((long)arenaMaximum - fighterB.CollisionRadius));
            minimumDistance = checked((int)checked(
                (long)fighterA.CollisionRadius + fighterB.CollisionRadius));
        }
        catch (OverflowException)
        {
            issues.Add(new ValidationIssue(
                "InvalidConfigRange",
                SettingPath(ArenaMaximumKey),
                null));
            return;
        }

        if (minimumA > maximumA)
        {
            issues.Add(new ValidationIssue("InvalidInitialState", "/fighters/0/position", fighterA.AnimalId.Value));
        }
        else if (fighterA.Position < minimumA || fighterA.Position > maximumA)
        {
            issues.Add(new ValidationIssue("InvalidInitialPositions", "/fighters/0/position", fighterA.AnimalId.Value));
        }

        if (minimumB > maximumB)
        {
            issues.Add(new ValidationIssue("InvalidInitialState", "/fighters/1/position", fighterB.AnimalId.Value));
        }
        else if (fighterB.Position < minimumB || fighterB.Position > maximumB)
        {
            issues.Add(new ValidationIssue("InvalidInitialPositions", "/fighters/1/position", fighterB.AnimalId.Value));
        }

        var signedDistance = checked((long)fighterB.Position - fighterA.Position);
        if (signedDistance < minimumDistance)
        {
            issues.Add(new ValidationIssue(
                "InvalidInitialPositions",
                SettingPath(StartPositionBKey),
                null));
        }
    }

    private static ValidatedBuild? ValidateBuild(
        FighterBuildSnapshot build,
        ModeRulesSnapshot modeRules,
        CompiledBattleConfig config,
        string path,
        ICollection<ValidationIssue> issues)
    {
        var startIssueCount = issues.Count;
        EnsureAllowed(modeRules.AllowedAnimalIds, build.AnimalId, path + "/animal_id", issues);
        if (!config.TryGetFighter(build.AnimalId, out var fighter) || fighter is null)
        {
            issues.Add(new ValidationIssue("UnknownStableId", path + "/animal_id", build.AnimalId.Value));
        }

        for (var index = 0; index < build.SpecialActionIds.Count; index++)
        {
            var actionId = build.SpecialActionIds[index];
            var actionPath = path + "/special_action_ids/" + index.ToString(CultureInfo.InvariantCulture);
            EnsureAllowed(modeRules.AllowedActionIds, actionId, actionPath, issues);
            if (!config.TryGetAction(actionId, out var action) || action is null)
            {
                issues.Add(new ValidationIssue("UnknownStableId", actionPath, actionId.Value));
                continue;
            }

            ValidateEntityString(action, "animal_id", build.AnimalId.Value, actionPath, "WrongOwner", issues);
            ValidateEntityString(action, "slot_type", "Special", actionPath, "WrongSlot", issues);
        }

        EnsureAllowed(modeRules.AllowedPassiveIds, build.PassiveId, path + "/passive_id", issues);
        if (!config.TryGetPassive(build.PassiveId, out var passive) || passive is null)
        {
            issues.Add(new ValidationIssue("UnknownStableId", path + "/passive_id", build.PassiveId.Value));
        }
        else
        {
            ValidateEntityString(
                passive,
                "animal_id",
                build.AnimalId.Value,
                path + "/passive_id",
                "WrongOwner",
                issues);
        }

        var gearEntities = new List<CompiledConfigEntity>(3);
        ValidateGear(build.Gear.Offense, "Offense", path + "/gear/offense", modeRules, config, gearEntities, issues);
        ValidateGear(build.Gear.Defense, "Defense", path + "/gear/defense", modeRules, config, gearEntities, issues);
        ValidateGear(build.Gear.Utility, "Utility", path + "/gear/utility", modeRules, config, gearEntities, issues);

        EnsureAllowed(modeRules.AllowedTacticIds, build.TacticId, path + "/tactic_id", issues);
        if (!config.TryGetTactic(build.TacticId, out _))
        {
            issues.Add(new ValidationIssue("UnknownStableId", path + "/tactic_id", build.TacticId.Value));
        }

        if (fighter is null)
        {
            return null;
        }

        var baseStats = ReadFighterStats(fighter, path + "/animal_id", issues);
        var resourceId = ReadRequiredStableIdProperty(fighter, "resource_id", path + "/animal_id", issues);
        var modifiers = ReadGearModifiers(gearEntities, path + "/gear", issues);

        return issues.Count == startIssueCount && resourceId.HasValue
            ? new ValidatedBuild(baseStats, resourceId.Value, modifiers)
            : null;
    }

    private static void ValidateGear(
        StableId gearId,
        string expectedSlot,
        string path,
        ModeRulesSnapshot modeRules,
        CompiledBattleConfig config,
        ICollection<CompiledConfigEntity> gearEntities,
        ICollection<ValidationIssue> issues)
    {
        EnsureAllowed(modeRules.AllowedGearIds, gearId, path, issues);
        if (!config.TryGetGear(gearId, out var gear) || gear is null)
        {
            issues.Add(new ValidationIssue("UnknownStableId", path, gearId.Value));
            return;
        }

        ValidateEntityString(gear, "slot", expectedSlot, path, "WrongSlot", issues);
        gearEntities.Add(gear);
    }

    private static SystemActionDefinition? ValidateSystemMovementAction(
        StableId actionId,
        string expectedMovementMode,
        CompiledBattleConfig config,
        ICollection<ValidationIssue> issues)
    {
        var path = "/system_actions/" + actionId.Value;
        if (!config.TryGetAction(actionId, out var action) || action is null)
        {
            issues.Add(new ValidationIssue("UnknownStableId", path, actionId.Value));
            return null;
        }

        ValidateEntityString(action, "animal_id", "all", path, "WrongOwner", issues);
        ValidateEntityString(action, "slot_type", "System", path, "WrongSlot", issues);
        ValidateEntityString(action, "category", "Movement", path, "InvalidSystemAction", issues);
        ValidateEntityString(
            action,
            "movement_mode",
            expectedMovementMode,
            path,
            "InvalidSystemAction",
            issues);
        ValidateEntityBoolean(action, "track_target", true, path, issues);
        ValidateEntityBoolean(action, "wall_impact", false, path, issues);
        ValidateEntityBoolean(action, "blockable", false, path, issues);
        ValidateEntityBoolean(action, "dodgeable", false, path, issues);
        ValidateEntityBoolean(action, "undodgeable", false, path, issues);
        ValidateEntityString(action, "hit_schedule", string.Empty, path, "InvalidSystemAction", issues);

        var weight = ReadRequiredEntityInteger(action, "base_weight", 1, int.MaxValue, path, issues);
        var energy = ReadRequiredEntityInteger(action, "energy_cost", 0, 0, path, issues);
        var resource = ReadRequiredEntityInteger(action, "resource_cost", 0, 0, path, issues);
        var startup = ReadRequiredEntityInteger(action, "startup_base_ticks", 0, int.MaxValue, path, issues);
        var startupMinimum = ReadRequiredEntityInteger(action, "startup_min_ticks", 0, int.MaxValue, path, issues);
        var startupMaximum = ReadRequiredEntityInteger(action, "startup_max_ticks", 0, int.MaxValue, path, issues);
        var active = ReadRequiredEntityInteger(action, "active_ticks", 1, int.MaxValue, path, issues);
        var recovery = ReadRequiredEntityInteger(action, "recovery_base_ticks", 0, int.MaxValue, path, issues);
        var recoveryMinimum = ReadRequiredEntityInteger(action, "recovery_min_ticks", 0, int.MaxValue, path, issues);
        var recoveryMaximum = ReadRequiredEntityInteger(action, "recovery_max_ticks", 0, int.MaxValue, path, issues);
        var cooldown = ReadRequiredEntityInteger(action, "cooldown_ticks", 0, 0, path, issues);
        var preferredMinimum = ReadRequiredEntityInteger(action, "preferred_range_min", 0, int.MaxValue, path, issues);
        var preferredMaximum = ReadRequiredEntityInteger(action, "preferred_range_max", 0, int.MaxValue, path, issues);

        foreach (var field in new[]
                 {
                     "base_damage",
                     "base_knockback",
                     "base_stagger",
                     "base_stun_ticks",
                     "block_base_chance_fp",
                     "block_reduction_fp",
                     "chip_min",
                     "clash_priority",
                     "dodge_base_chance_fp",
                     "grab_priority",
                     "hit_count",
                     "hit_range_min",
                     "hit_range_max",
                     "knockback_min",
                     "knockback_max",
                     "min_damage",
                     "move_distance",
                     "power_ratio_fp",
                     "wall_damage_min",
                     "wall_damage_max",
                     "wall_damage_per_unit_fp",
                 })
        {
            _ = ReadRequiredEntityInteger(action, field, 0, 0, path, issues);
        }

        var timingsMatch = startup.HasValue && startupMinimum.HasValue && startupMaximum.HasValue &&
                           startup.Value == startupMinimum.Value && startup.Value == startupMaximum.Value &&
                           recovery.HasValue && recoveryMinimum.HasValue && recoveryMaximum.HasValue &&
                           recovery.Value == recoveryMinimum.Value && recovery.Value == recoveryMaximum.Value;
        if (!timingsMatch)
        {
            issues.Add(new ValidationIssue("InvalidSystemAction", path + "/timings", actionId.Value));
        }

        if (preferredMinimum.HasValue && preferredMaximum.HasValue &&
            preferredMinimum.Value > preferredMaximum.Value)
        {
            issues.Add(new ValidationIssue(
                "InvalidSystemAction",
                path + "/preferred_range_max",
                actionId.Value));
        }

        if (!weight.HasValue || !energy.HasValue || !resource.HasValue ||
            !startup.HasValue || !active.HasValue || !recovery.HasValue || !cooldown.HasValue ||
            !preferredMinimum.HasValue || !preferredMaximum.HasValue || !timingsMatch)
        {
            return null;
        }

        return new SystemActionDefinition(
            actionId,
            weight.Value,
            energy.Value,
            resource.Value,
            startup.Value,
            active.Value,
            recovery.Value,
            cooldown.Value,
            expectedMovementMode == "Approach"
                ? SystemMovementMode.Approach
                : SystemMovementMode.Retreat,
            preferredMinimum.Value,
            preferredMaximum.Value,
            true);
    }

    private static SystemActionDefinition? ValidateSystemWait(
        ModeRulesSnapshot modeRules,
        CompiledBattleConfig config,
        ICollection<ValidationIssue> issues)
    {
        var path = "/system_actions/sys_wait";
        EnsureAllowed(modeRules.AllowedActionIds, SystemActionSelector.WaitId, path, issues);
        if (!config.TryGetAction(SystemActionSelector.WaitId, out var action) || action is null)
        {
            issues.Add(new ValidationIssue("UnknownStableId", path, SystemActionSelector.WaitId.Value));
            return null;
        }

        ValidateEntityString(action, "animal_id", "all", path, "WrongOwner", issues);
        ValidateEntityString(action, "slot_type", "System", path, "WrongSlot", issues);
        ValidateEntityString(action, "category", "Wait", path, "InvalidSystemAction", issues);
        ValidateEntityString(action, "movement_mode", "None", path, "InvalidSystemAction", issues);
        ValidateEntityBoolean(action, "track_target", false, path, issues);
        _ = ReadRequiredEntityInteger(action, "move_distance", 0, 0, path, issues);
        var preferredMinimum = ReadRequiredEntityInteger(
            action,
            "preferred_range_min",
            0,
            int.MaxValue,
            path,
            issues);
        var preferredMaximum = ReadRequiredEntityInteger(
            action,
            "preferred_range_max",
            0,
            int.MaxValue,
            path,
            issues);

        var weight = ReadRequiredEntityInteger(action, "base_weight", 1, int.MaxValue, path, issues);
        var energy = ReadRequiredEntityInteger(action, "energy_cost", 0, 0, path, issues);
        var resource = ReadRequiredEntityInteger(action, "resource_cost", 0, 0, path, issues);
        var startup = ReadRequiredEntityInteger(action, "startup_base_ticks", 0, int.MaxValue, path, issues);
        var active = ReadRequiredEntityInteger(action, "active_ticks", 1, int.MaxValue, path, issues);
        var recovery = ReadRequiredEntityInteger(action, "recovery_base_ticks", 0, int.MaxValue, path, issues);
        var cooldown = ReadRequiredEntityInteger(action, "cooldown_ticks", 0, 0, path, issues);

        return weight.HasValue && energy.HasValue && resource.HasValue && startup.HasValue &&
               active.HasValue && recovery.HasValue && cooldown.HasValue &&
               preferredMinimum.HasValue && preferredMaximum.HasValue
            ? new SystemActionDefinition(
                SystemActionSelector.WaitId,
                weight.Value,
                energy.Value,
                resource.Value,
                startup.Value,
                active.Value,
                recovery.Value,
                cooldown.Value,
                SystemMovementMode.None,
                preferredMinimum.Value,
                preferredMaximum.Value,
                false)
            : null;
    }

    private static IReadOnlyDictionary<string, int> ReadFighterStats(
        CompiledConfigEntity fighter,
        string path,
        ICollection<ValidationIssue> issues)
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in fighter.Properties)
        {
            if (property.Value.Kind != ConfigValueKind.Integer)
            {
                continue;
            }

            var value = property.Value.AsInteger();
            if (value is < int.MinValue or > int.MaxValue)
            {
                issues.Add(new ValidationIssue("InvalidConfigRange", path + "/" + property.Name, fighter.Id.Value));
                continue;
            }

            stats[ToStatName(property.Name)] = (int)value;
        }

        foreach (var required in new[]
                 {
                     "MaxHealth",
                     "MaxEnergy",
                     "MaxResource",
                     "StartResource",
                      "StaggerThreshold",
                      "Initiative",
                      "MoveSpeed",
                      "CollisionRadius",
                  })
        {
            if (!stats.ContainsKey(required))
            {
                issues.Add(new ValidationIssue("MissingRequiredConfigKey", path + "/" + required, fighter.Id.Value));
            }
        }

        foreach (var positive in new[] { "MoveSpeed", "CollisionRadius" })
        {
            if (stats.TryGetValue(positive, out var value) && value < 1)
            {
                issues.Add(new ValidationIssue(
                    "InvalidConfigRange",
                    path + "/" + positive,
                    fighter.Id.Value));
            }
        }

        return stats;
    }

    private static IReadOnlyList<StatModifier> ReadGearModifiers(
        IEnumerable<CompiledConfigEntity> gearEntities,
        string path,
        ICollection<ValidationIssue> issues)
    {
        var modifiers = new List<StatModifier>();
        foreach (var gear in gearEntities.OrderBy(item => item.Id))
        {
            var priority = 0;
            if (gear.TryGetProperty("priority", out var priorityValue))
            {
                if (priorityValue.Kind != ConfigValueKind.Integer ||
                    priorityValue.AsInteger() is < int.MinValue or > int.MaxValue)
                {
                    issues.Add(new ValidationIssue("InvalidConfigValue", path + "/" + gear.Id + "/priority", gear.Id.Value));
                    continue;
                }

                priority = (int)priorityValue.AsInteger();
            }

            ReadGearModifier(gear, priority, 1, path, modifiers, issues);
            ReadGearModifier(gear, priority, 2, path, modifiers, issues);
        }

        return modifiers;
    }

    private static void ReadGearModifier(
        CompiledConfigEntity gear,
        int priority,
        int number,
        string path,
        ICollection<StatModifier> modifiers,
        ICollection<ValidationIssue> issues)
    {
        var statName = "stat" + number.ToString(CultureInfo.InvariantCulture);
        if (!gear.TryGetProperty(statName, out var statValue))
        {
            return;
        }

        var operationName = "operation" + number.ToString(CultureInfo.InvariantCulture);
        var valueName = "value" + number.ToString(CultureInfo.InvariantCulture);
        if (statValue.Kind != ConfigValueKind.String ||
            !gear.TryGetProperty(operationName, out var operationValue) ||
            operationValue.Kind != ConfigValueKind.String ||
            !gear.TryGetProperty(valueName, out var modifierValue) ||
            modifierValue.Kind != ConfigValueKind.Integer ||
            modifierValue.AsInteger() is < int.MinValue or > int.MaxValue ||
            !TryParseOperation(operationValue.AsString(), out var operation))
        {
            issues.Add(new ValidationIssue(
                "InvalidConfigValue",
                path + "/" + gear.Id + "/" + statName,
                gear.Id.Value));
            return;
        }

        modifiers.Add(new StatModifier(
            ModifierLayer.Gear,
            priority,
            gear.Id,
            statValue.AsString(),
            operation,
            (int)modifierValue.AsInteger()));
    }

    private static FighterRuntimeState? TryInitializeFighter(
        FighterBuildSnapshot build,
        ValidatedBuild validated,
        int position,
        Facing facing,
        int fixedPointScale,
        string path,
        ICollection<ValidationIssue> issues)
    {
        IReadOnlyDictionary<string, int> stats;
        try
        {
            stats = ModifierPipeline.Apply(validated.BaseStats, validated.Modifiers, fixedPointScale);
        }
        catch (OverflowException)
        {
            issues.Add(new ValidationIssue("InvalidConfigRange", path + "/derived_stats", build.AnimalId.Value));
            return null;
        }

        var maximumHealth = stats["MaxHealth"];
        var maximumEnergy = stats["MaxEnergy"];
        var maximumResource = stats["MaxResource"];
        var startResource = stats["StartResource"];
        var staggerThreshold = stats["StaggerThreshold"];
        var initiative = stats["Initiative"];
        var moveSpeed = stats["MoveSpeed"];
        var collisionRadius = stats["CollisionRadius"];

        if (maximumHealth < 1 || maximumEnergy < 0 || maximumResource < 0 ||
            startResource < 0 || startResource > maximumResource || staggerThreshold < 1 ||
            moveSpeed < 1 || collisionRadius < 1)
        {
            issues.Add(new ValidationIssue("InvalidInitialState", path, build.AnimalId.Value));
            return null;
        }

        return new FighterRuntimeState(
            build.FighterId,
            build.Side,
            build.AnimalId,
            position,
            facing,
            maximumHealth,
            maximumEnergy,
            validated.ResourceId,
            startResource,
            maximumResource,
            staggerThreshold,
            initiative,
            moveSpeed,
            collisionRadius);
    }

    private static IReadOnlyList<FighterId> DetermineInitiative(
        FighterRuntimeState fighterA,
        FighterRuntimeState fighterB,
        ulong masterSeed)
    {
        if (fighterA.Initiative > fighterB.Initiative)
        {
            return new[] { FighterId.FighterA, FighterId.FighterB };
        }

        if (fighterB.Initiative > fighterA.Initiative)
        {
            return new[] { FighterId.FighterB, FighterId.FighterA };
        }

        var scoreA = SplitMix64.Mix(masterSeed ^ 0x666967687465725fUL);
        var scoreB = SplitMix64.Mix(masterSeed ^ 0x666967687465725eUL);
        return scoreA <= scoreB
            ? new[] { FighterId.FighterA, FighterId.FighterB }
            : new[] { FighterId.FighterB, FighterId.FighterA };
    }

    private static int? ReadRequiredSetting(
        CompiledBattleConfig config,
        string name,
        int minimum,
        int maximum,
        ICollection<ValidationIssue> issues)
    {
        var path = SettingPath(name);
        if (!config.TryGetSetting(name, out var value))
        {
            issues.Add(new ValidationIssue("MissingRequiredConfigKey", path, null));
            return null;
        }

        if (value.Kind != ConfigValueKind.Integer)
        {
            issues.Add(new ValidationIssue("InvalidConfigValueType", path, null));
            return null;
        }

        var integer = value.AsInteger();
        if (integer < minimum || integer > maximum)
        {
            issues.Add(new ValidationIssue("InvalidConfigRange", path, null));
            return null;
        }

        return (int)integer;
    }

    private static int? ReadRequiredEntityInteger(
        CompiledConfigEntity entity,
        string name,
        int minimum,
        int maximum,
        string path,
        ICollection<ValidationIssue> issues)
    {
        var propertyPath = path + "/" + name;
        if (!entity.TryGetProperty(name, out var value))
        {
            issues.Add(new ValidationIssue("MissingRequiredConfigKey", propertyPath, entity.Id.Value));
            return null;
        }

        if (value.Kind != ConfigValueKind.Integer)
        {
            issues.Add(new ValidationIssue("InvalidConfigValueType", propertyPath, entity.Id.Value));
            return null;
        }

        var integer = value.AsInteger();
        if (integer < minimum || integer > maximum)
        {
            issues.Add(new ValidationIssue("InvalidConfigRange", propertyPath, entity.Id.Value));
            return null;
        }

        return (int)integer;
    }

    private static StableId? ReadRequiredStableIdProperty(
        CompiledConfigEntity entity,
        string name,
        string path,
        ICollection<ValidationIssue> issues)
    {
        if (!entity.TryGetProperty(name, out var value) ||
            value.Kind != ConfigValueKind.String ||
            !StableId.TryParse(value.Kind == ConfigValueKind.String ? value.AsString() : null, out var id))
        {
            issues.Add(new ValidationIssue("InvalidConfigValue", path + "/" + name, entity.Id.Value));
            return null;
        }

        return id;
    }

    private static void ValidateEntityString(
        CompiledConfigEntity entity,
        string name,
        string expected,
        string path,
        string code,
        ICollection<ValidationIssue> issues)
    {
        if (!entity.TryGetProperty(name, out var value) ||
            value.Kind != ConfigValueKind.String ||
            !string.Equals(value.AsString(), expected, StringComparison.Ordinal))
        {
            issues.Add(new ValidationIssue(code, path, entity.Id.Value));
        }
    }

    private static void ValidateEntityBoolean(
        CompiledConfigEntity entity,
        string name,
        bool expected,
        string path,
        ICollection<ValidationIssue> issues)
    {
        if (!entity.TryGetProperty(name, out var value) ||
            value.Kind != ConfigValueKind.Boolean ||
            value.AsBoolean() != expected)
        {
            issues.Add(new ValidationIssue(
                "InvalidSystemAction",
                path + "/" + name,
                entity.Id.Value));
        }
    }

    private static void EnsureAllowed(
        IReadOnlyList<StableId> allowlist,
        StableId id,
        string path,
        ICollection<ValidationIssue> issues)
    {
        if (!allowlist.Contains(id))
        {
            issues.Add(new ValidationIssue("ForbiddenStableId", path, id.Value));
        }
    }

    private static void AddMismatch(
        bool matches,
        string code,
        string path,
        string? entity,
        ICollection<ValidationIssue> issues)
    {
        if (!matches)
        {
            issues.Add(new ValidationIssue(code, path, entity));
        }
    }

    private static IReadOnlyList<BattleRejectionError> ToRejectionErrors(
        IEnumerable<ValidationIssue> issues) =>
        issues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Entity, StringComparer.Ordinal)
            .Select(issue => new BattleRejectionError(
                new ReasonCode(issue.Code),
                issue.Path,
                issue.Entity is null ? null : new ExternalId(issue.Entity),
                new StableId("battle_rejected_validation"),
                Array.Empty<BattleRejectionDetail>()))
            .ToArray();

    private static bool TryParseOperation(string value, out ModifierOperation operation)
    {
        operation = value switch
        {
            "Add" => ModifierOperation.Add,
            "Multiply" => ModifierOperation.Multiply,
            "Override" => ModifierOperation.Override,
            _ => default,
        };
        return value is "Add" or "Multiply" or "Override";
    }

    private static string ToStatName(string configName)
    {
        var builder = new System.Text.StringBuilder(configName.Length);
        var upper = true;
        foreach (var character in configName)
        {
            if (character == '_')
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }

        return builder.ToString();
    }

    private static string SettingPath(string name) => "/config/settings/" + name;

    private enum CatalogKind
    {
        Fighter,
        Action,
        Passive,
        Gear,
        Tactic,
    }

    private readonly record struct ValidationIssue(string Code, string Path, string? Entity);

    private sealed record ValidatedBuild(
        IReadOnlyDictionary<string, int> BaseStats,
        StableId ResourceId,
        IReadOnlyList<StatModifier> Modifiers);
}
