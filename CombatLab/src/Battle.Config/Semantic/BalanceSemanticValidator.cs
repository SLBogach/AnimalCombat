using Battle.Config.Json;
using Battle.Config.Schema;
using Battle.Contracts.Config;

namespace Battle.Config.Semantic;

internal static class BalanceSemanticValidator
{
    private const long TechnicalMagnitudeLimit = 1_000_000_000;

    public static void Validate(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        ValidateVersions(document, issues);
        ValidateSettings(document.Settings, issues);
        ValidateCatalogs(document, issues);
    }

    private static void ValidateVersions(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryString(document.Settings, BalanceV01Schema.SchemaVersionSetting, out var schemaVersion) &&
            !StringComparer.Ordinal.Equals(schemaVersion, BalanceV01Schema.SchemaVersion))
        {
            Add(
                issues,
                ConfigValidationCodes.UnknownSchemaVersion,
                "$.settings." + BalanceV01Schema.SchemaVersionSetting,
                $"Schema '{schemaVersion}' is not supported.");
        }

        if (TryString(document.Settings, BalanceV01Schema.ConfigVersionSetting, out var configVersion) &&
            !StringComparer.Ordinal.Equals(configVersion, "v0.1"))
        {
            Add(
                issues,
                ConfigValidationCodes.InvalidEnumValue,
                "$.settings." + BalanceV01Schema.ConfigVersionSetting,
                $"Config version '{configVersion}' is not the v0.1 workbook contract.");
        }
    }

    private static void ValidateSettings(
        IReadOnlyDictionary<string, ConfigValue> settings,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var item in settings)
        {
            if (item.Value.Kind == ConfigValueKind.Integer &&
                IsMagnitudeTooLarge(item.Value.AsInteger()))
            {
                Add(
                    issues,
                    ConfigValidationCodes.ArithmeticOverflowRisk,
                    "$.settings." + item.Key,
                    $"Value {item.Value.AsInteger()} exceeds the technical magnitude budget.");
            }
        }

        if (TryInteger(settings, "global.arena.min_position", out var arenaMin) &&
            TryInteger(settings, "global.arena.max_position", out var arenaMax))
        {
            if (arenaMin >= arenaMax)
            {
                Add(issues, ConfigValidationCodes.InvalidArenaBounds, "$.settings", "Arena minimum must be less than arena maximum.");
            }

            ValidateInside(settings, "global.arena.start_position_a", arenaMin, arenaMax, issues);
            ValidateInside(settings, "global.arena.start_position_b", arenaMin, arenaMax, issues);
        }

        ValidatePositive(settings, "global.sim.fp_scale", ConfigValidationCodes.ZeroDivisor, issues);
        ValidatePositive(settings, "global.control.control_k", ConfigValidationCodes.ZeroDivisor, issues);
        ValidatePositive(settings, "global.control.force_k", ConfigValidationCodes.ZeroDivisor, issues);
        ValidatePositive(settings, "global.damage.armor_k", ConfigValidationCodes.ZeroDivisor, issues);
        ValidatePositive(settings, "global.sim.tick_ms", ConfigValidationCodes.NumericOutOfRange, issues);
        ValidatePositive(settings, "battle.time_limit_ticks", ConfigValidationCodes.InvalidDuration, issues);
        ValidateSettingRange(
            settings,
            BalanceV01Schema.MaximumEventsPerBattleSetting,
            4,
            200_000,
            issues);
        ValidateSettingRange(
            settings,
            BalanceV01Schema.MaximumZeroProgressTicksSetting,
            1,
            int.MaxValue,
            issues);

        ValidateOrdered(settings, "global.sim.probability_min", "global.sim.probability_max", issues);
        ValidateOrdered(settings, "global.sim.multiplier_min", "global.sim.multiplier_max", issues);
        ValidateOrdered(settings, "global.damage.block_min", "global.damage.block_max", issues);
        ValidateOrdered(settings, "global.damage.dodge_min", "global.damage.dodge_max", issues);
        ValidateOrdered(settings, "global.damage.speed_min", "global.damage.speed_max", issues);
        ValidateOrdered(settings, "global.control.stun_min_ticks", "global.control.stun_max_ticks", issues);

        if (TryInteger(settings, "global.sim.probability_min", out var probabilityMin) &&
            TryInteger(settings, "global.sim.probability_max", out var probabilityMax) &&
            (probabilityMin < 0 || probabilityMax > 1000))
        {
            Add(issues, ConfigValidationCodes.NumericOutOfRange, "$.settings", "Probability bounds must remain inside [0, 1000].");
        }

        if (TryInteger(settings, "global.sim.decision_weight_max", out var weightMax) &&
            TryInteger(settings, "global.sim.multiplier_max", out var multiplierMax))
        {
            try
            {
                _ = checked(weightMax * multiplierMax);
            }
            catch (OverflowException)
            {
                Add(issues, ConfigValidationCodes.ArithmeticOverflowRisk, "$.settings", "Decision weight multiplication can overflow Int64.");
            }
        }
    }

    private static void ValidateCatalogs(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        var fighterIds = document.Catalogs["fighters"]
            .Select(item => item.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var effectIds = document.Catalogs["effects"]
            .Select(item => item.Id.Value)
            .ToHashSet(StringComparer.Ordinal);

        ValidateGlobalStableIds(document, issues);
        ValidateNumericMagnitude(document, issues);
        ValidateEffectConflicts(document.Catalogs["effects"], issues);

        foreach (var action in document.Catalogs["actions"])
        {
            var path = "$.actions[" + action.Id + "]";
            if (!TryEntityString(action, "animal_id", out var owner) ||
                !TryEntityString(action, "slot_type", out var slot))
            {
                continue;
            }

            if (owner != "all" && !fighterIds.Contains(owner))
            {
                Add(issues, ConfigValidationCodes.UnknownStableId, path + ".animal_id", $"Unknown fighter '{owner}'.");
            }

            if (slot == "System")
            {
                if (owner != "all" || !action.Id.Value.StartsWith("sys_", StringComparison.Ordinal))
                {
                    Add(issues, ConfigValidationCodes.WrongOwner, path, "System actions must be owned by 'all' and use the sys_ prefix.");
                }
            }
            else if (owner == "all" || !action.Id.Value.StartsWith(owner + "_", StringComparison.Ordinal))
            {
                Add(issues, ConfigValidationCodes.WrongOwner, path, "A non-system action must use its fighter owner and prefix.");
            }

            ValidateRange(action, "hit_range_min", "hit_range_max", path, issues);
            ValidateRange(action, "preferred_range_min", "preferred_range_max", path, issues);
            ValidateBounded(action, "startup_min_ticks", "startup_base_ticks", "startup_max_ticks", path, issues);
            ValidateBounded(action, "recovery_min_ticks", "recovery_base_ticks", "recovery_max_ticks", path, issues);
            ValidateBounded(action, "knockback_min", "base_knockback", "knockback_max", path, issues);
            ValidateRange(action, "wall_damage_min", "wall_damage_max", path, issues);
            ValidateNonNegative(action, "active_ticks", path, issues);
            ValidateNonNegative(action, "cooldown_ticks", path, issues);
            ValidateNonNegative(action, "energy_cost", path, issues);
            ValidateNonNegative(action, "resource_cost", path, issues);
        }

        foreach (var passive in document.Catalogs["passives"])
        {
            var path = "$.passives[" + passive.Id + "]";
            if (TryEntityString(passive, "animal_id", out var owner))
            {
                if (!fighterIds.Contains(owner))
                {
                    Add(issues, ConfigValidationCodes.UnknownStableId, path + ".animal_id", $"Unknown fighter '{owner}'.");
                }
                else if (!passive.Id.Value.StartsWith(owner + "_", StringComparison.Ordinal))
                {
                    Add(issues, ConfigValidationCodes.WrongOwner, path, "A passive ID must use its fighter owner prefix.");
                }
            }

            if (TryEntityString(passive, "effect_id", out var effectId) &&
                effectId.Length > 0 &&
                !effectIds.Contains(effectId))
            {
                Add(issues, ConfigValidationCodes.UnknownStableId, path + ".effect_id", $"Unknown effect '{effectId}'.");
            }

            ValidateNonNegative(passive, "duration_ticks", path, issues);
            ValidateNonNegative(passive, "internal_cooldown_ticks", path, issues);
            ValidatePositive(passive, "stack_cap", path, issues);
        }

        foreach (var effect in document.Catalogs["effects"])
        {
            var path = "$.effects[" + effect.Id + "]";
            ValidateNonNegative(effect, "duration_ticks", path, issues);
            ValidateNonNegative(effect, "internal_cooldown_ticks", path, issues);
            ValidatePositive(effect, "stack_cap", path, issues);
        }

        foreach (var gear in document.Catalogs["gear"])
        {
            var path = "$.gear[" + gear.Id + "]";
            if (TryEntityString(gear, "slot", out var slot) &&
                !gear.Id.Value.StartsWith("gear_" + slot.ToLowerInvariant() + "_", StringComparison.Ordinal))
            {
                Add(issues, ConfigValidationCodes.WrongSlot, path, "A gear ID must contain its canonical slot prefix.");
            }
        }
    }

    private static void ValidateNumericMagnitude(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var catalog in document.Catalogs)
        {
            foreach (var entity in catalog.Value)
            {
                foreach (var property in entity.Properties)
                {
                    if (property.Value.Kind == ConfigValueKind.Integer &&
                        IsMagnitudeTooLarge(property.Value.AsInteger()))
                    {
                        Add(
                            issues,
                            ConfigValidationCodes.ArithmeticOverflowRisk,
                            "$." + catalog.Key + "[" + entity.Id + "]." + property.Key,
                            $"Value {property.Value.AsInteger()} exceeds the technical magnitude budget.");
                    }
                }
            }
        }
    }

    private static void ValidateEffectConflicts(
        IEnumerable<BalanceJsonEntity> effects,
        ICollection<ConfigValidationIssue> issues)
    {
        var policiesByGroup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var effect in effects.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            var path = "$.effects[" + effect.Id + "]";
            if (!TryEntityString(effect, "stack_group", out var group) || group.Length == 0 ||
                !TryEntityString(effect, "stack_policy", out var policy) || policy.Length == 0)
            {
                Add(
                    issues,
                    ConfigValidationCodes.InvalidConflictMatrix,
                    path,
                    "Every effect must define a non-empty stack group and stack policy.");
                continue;
            }

            if (policiesByGroup.TryGetValue(group, out var establishedPolicy) &&
                !StringComparer.Ordinal.Equals(establishedPolicy, policy))
            {
                Add(
                    issues,
                    ConfigValidationCodes.InvalidConflictMatrix,
                    path + ".stack_policy",
                    $"Effects in stack group '{group}' must use one conflict policy.");
            }
            else
            {
                policiesByGroup[group] = policy;
            }
        }
    }

    private static void ValidateGlobalStableIds(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var catalog in document.Catalogs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var entity in catalog.Value.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                if (!seen.Add(entity.Id.Value))
                {
                    Add(
                        issues,
                        ConfigValidationCodes.DuplicateStableId,
                        "$." + catalog.Key + "[" + entity.Id + "]",
                        $"Stable ID '{entity.Id}' is already used by another config entity.");
                }
            }
        }
    }

    private static bool IsMagnitudeTooLarge(long value) =>
        value > TechnicalMagnitudeLimit || value < -TechnicalMagnitudeLimit;

    private static void ValidateInside(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        long minimum,
        long maximum,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryInteger(settings, key, out var value) && (value < minimum || value > maximum))
        {
            Add(issues, ConfigValidationCodes.InvalidArenaBounds, "$.settings." + key, "The arena position is outside the arena bounds.");
        }
    }

    private static void ValidatePositive(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        string code,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryInteger(settings, key, out var value) && value <= 0)
        {
            Add(issues, code, "$.settings." + key, "The value must be greater than zero.");
        }
    }

    private static void ValidateOrdered(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string minimumKey,
        string maximumKey,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryInteger(settings, minimumKey, out var minimum) &&
            TryInteger(settings, maximumKey, out var maximum) &&
            minimum > maximum)
        {
            Add(issues, ConfigValidationCodes.NumericOutOfRange, "$.settings", $"'{minimumKey}' must not exceed '{maximumKey}'.");
        }
    }

    private static void ValidateSettingRange(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        long minimum,
        long maximum,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryInteger(settings, key, out var value) && (value < minimum || value > maximum))
        {
            Add(
                issues,
                ConfigValidationCodes.NumericOutOfRange,
                "$.settings." + key,
                $"The value must remain inside [{minimum}, {maximum}].");
        }
    }

    private static void ValidateRange(
        BalanceJsonEntity entity,
        string minimumKey,
        string maximumKey,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryEntityInteger(entity, minimumKey, out var minimum) &&
            TryEntityInteger(entity, maximumKey, out var maximum) &&
            minimum > maximum)
        {
            Add(issues, ConfigValidationCodes.NumericOutOfRange, path, $"'{minimumKey}' must not exceed '{maximumKey}'.");
        }
    }

    private static void ValidateBounded(
        BalanceJsonEntity entity,
        string minimumKey,
        string valueKey,
        string maximumKey,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryEntityInteger(entity, minimumKey, out var minimum) &&
            TryEntityInteger(entity, valueKey, out var value) &&
            TryEntityInteger(entity, maximumKey, out var maximum) &&
            (minimum > value || value > maximum))
        {
            Add(
                issues,
                ConfigValidationCodes.NumericOutOfRange,
                path,
                $"'{valueKey}' must remain inside [{minimumKey}, {maximumKey}].");
        }
    }

    private static void ValidateNonNegative(
        BalanceJsonEntity entity,
        string key,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryEntityInteger(entity, key, out var value) && value < 0)
        {
            Add(issues, ConfigValidationCodes.InvalidDuration, path + "." + key, "The value must not be negative.");
        }
    }

    private static void ValidatePositive(
        BalanceJsonEntity entity,
        string key,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryEntityInteger(entity, key, out var value) && value <= 0)
        {
            Add(issues, ConfigValidationCodes.NumericOutOfRange, path + "." + key, "The value must be greater than zero.");
        }
    }

    private static bool TryInteger(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        out long value)
    {
        if (settings.TryGetValue(key, out var configValue) && configValue.Kind == ConfigValueKind.Integer)
        {
            value = configValue.AsInteger();
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryString(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        out string value)
    {
        if (settings.TryGetValue(key, out var configValue) && configValue.Kind == ConfigValueKind.String)
        {
            value = configValue.AsString();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryEntityInteger(BalanceJsonEntity entity, string key, out long value)
    {
        if (entity.Properties.TryGetValue(key, out var property) && property.Kind == ConfigValueKind.Integer)
        {
            value = property.AsInteger();
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryEntityString(BalanceJsonEntity entity, string key, out string value)
    {
        if (entity.Properties.TryGetValue(key, out var property) && property.Kind == ConfigValueKind.String)
        {
            value = property.AsString();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void Add(
        ICollection<ConfigValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new ConfigValidationIssue(code, path, message));
}
