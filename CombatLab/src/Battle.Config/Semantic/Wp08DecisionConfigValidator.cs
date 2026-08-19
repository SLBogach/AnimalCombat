using System.Globalization;
using Battle.Config.Json;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;

namespace Battle.Config.Semantic;

internal static class Wp08DecisionConfigValidator
{
    private const string SettingsPath = "$.settings";
    private const long Int32Maximum = int.MaxValue;
    private const int MaximumHitScheduleEntries = 32;

    private static readonly IReadOnlyDictionary<string, NumericDomain> SettingDomains =
        new Dictionary<string, NumericDomain>(StringComparer.Ordinal)
        {
            ["global.sim.fp_scale"] = new(1, Int32Maximum, ConfigValidationCodes.ZeroDivisor),
            ["global.sim.multiplier_min"] = new(0, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.sim.multiplier_max"] = new(1, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.sim.decision_weight_max"] = new(1, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.ai.repeat_same_action_fp"] = new(0, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.ai.repeat_same_category_fp"] = new(0, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.ai.opportunity_growth_fp"] = new(0, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.ai.opportunity_cap_fp"] = new(1, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.ai.hard_opportunity_misses"] = new(0, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
            ["global.ai.default_perception_delay_ticks"] = new(0, Int32Maximum, ConfigValidationCodes.NumericOutOfRange),
        };

    private static readonly string[] TacticMultiplierFields =
    {
        "approach_fp",
        "block_fp",
        "counter_fp",
        "dodge_fp",
        "grab_fp",
        "heavy_fp",
        "light_fp",
        "low_hpfp",
        "repeat_penalty_fp",
        "resource_generator_fp",
        "resource_spender_fp",
        "retreat_fp",
        "self_wall_fp",
        "signature_fp",
        "target_recovery_fp",
        "target_wall_fp",
    };

    private static readonly HashSet<string> OpponentMovementModes = new(
        new[] { "None", "Approach", "Follow", "Push", "Pull", "Swap" },
        StringComparer.Ordinal);

    private static readonly HashSet<string> SelfMovementModes = new(
        new[] { "None", "Approach", "Retreat", "Adaptive" },
        StringComparer.Ordinal);

    private static readonly HashSet<string> HitSchedulePrimitives = new(
        new[] { "counter", "grab", "throw", "wall" },
        StringComparer.Ordinal);

    public static void Validate(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        ValidateSettingDomains(document.Settings, issues);

        if (!TryGetDecisionSettings(document.Settings, out var settings))
        {
            ValidateTags(document, issues);
            ValidateHitSchedules(document.Catalogs["actions"], issues);
            ValidateTargetProfiles(document.Catalogs["actions"], issues);
            ValidateCatalogSize(document, issues);
            return;
        }

        var relationshipsValid = ValidateGlobalRelationships(settings, issues);
        ValidateActions(document.Catalogs["actions"], settings, issues);
        ValidateTactics(document.Catalogs["tactics"], settings, relationshipsValid, issues);
        ValidatePassiveAndGearMultipliers(document, settings, relationshipsValid, issues);
        ValidateTags(document, issues);
        ValidateHitSchedules(document.Catalogs["actions"], issues);
        ValidateTargetProfiles(document.Catalogs["actions"], issues);
        ValidateCatalogSize(document, issues);
    }

    public static bool OwnsSettingNumericDomain(string key) =>
        SettingDomains.ContainsKey(key);

    public static bool OwnsCatalogNumericDomain(string catalog, string key) =>
        (catalog == "actions" && key is
            "base_weight" or
            "hard_opportunity_misses" or
            "hit_count" or
            "max_consecutive_uses" or
            "opportunity_cap_fp") ||
        (catalog == "tactics" && (key == "perception_delay_ticks" || TacticMultiplierFields.Contains(key, StringComparer.Ordinal))) ||
        (catalog == "passives" && key == "weight_multiplier_fp") ||
        (catalog == "gear" && key == "normalized_value");

    private static void ValidateSettingDomains(
        IReadOnlyDictionary<string, ConfigValue> settings,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var item in SettingDomains)
        {
            if (!TryInteger(settings, item.Key, out var value) || item.Value.Contains(value))
            {
                continue;
            }

            var code = value < item.Value.Minimum
                ? item.Value.BelowMinimumCode
                : ConfigValidationCodes.NumericOutOfRange;
            Add(
                issues,
                code,
                SettingsPath + "." + item.Key,
                $"The value must remain inside [{item.Value.Minimum}, {item.Value.Maximum}].");
        }
    }

    private static bool ValidateGlobalRelationships(
        DecisionSettings settings,
        ICollection<ConfigValidationIssue> issues)
    {
        var valid =
            settings.MultiplierMinimum <= settings.FixedPointScale &&
            settings.FixedPointScale <= settings.MultiplierMaximum &&
            IsInside(settings.RepeatSameAction, settings.MultiplierMinimum, settings.MultiplierMaximum) &&
            IsInside(settings.RepeatSameCategory, settings.MultiplierMinimum, settings.MultiplierMaximum) &&
            IsInside(settings.OpportunityCap, settings.MultiplierMinimum, settings.MultiplierMaximum);

        if (!valid)
        {
            Add(
                issues,
                ConfigValidationCodes.NumericOutOfRange,
                SettingsPath,
                "Decision multiplier relationships must satisfy min <= fp_scale <= max and keep repeat/opportunity multipliers inside those bounds.");
        }

        return valid;
    }

    private static void ValidateActions(
        IEnumerable<BalanceJsonEntity> actions,
        DecisionSettings settings,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var action in actions)
        {
            var path = "$.actions[" + action.Id + "]";
            ValidateEntityRange(action, "base_weight", 0, settings.DecisionWeightMaximum, path, issues);
            ValidateEntityRange(action, "max_consecutive_uses", 1, Int32Maximum, path, issues);
            ValidateEntityRange(action, "hit_count", 0, MaximumHitScheduleEntries, path, issues);
            ValidateEntityRange(
                action,
                "opportunity_cap_fp",
                settings.FixedPointScale,
                settings.OpportunityCap,
                path,
                issues);

            if (TryEntityInteger(action, "hard_opportunity_misses", out var hardMisses) &&
                (hardMisses < 0 || hardMisses > Int32Maximum ||
                 (hardMisses > 0 && hardMisses > settings.HardOpportunityMisses)))
            {
                Add(
                    issues,
                    ConfigValidationCodes.NumericOutOfRange,
                    path + ".hard_opportunity_misses",
                    "Action hard opportunity misses must be zero or remain inside the global threshold.");
            }
        }
    }

    private static void ValidateTactics(
        IEnumerable<BalanceJsonEntity> tactics,
        DecisionSettings settings,
        bool relationshipsValid,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var tactic in tactics)
        {
            var path = "$.tactics[" + tactic.Id + "]";
            if (relationshipsValid)
            {
                foreach (var field in TacticMultiplierFields)
                {
                    ValidateEntityRange(
                        tactic,
                        field,
                        settings.MultiplierMinimum,
                        settings.MultiplierMaximum,
                        path,
                        issues);
                }
            }

            ValidateEntityRange(tactic, "perception_delay_ticks", 0, Int32Maximum, path, issues);
        }
    }

    private static void ValidatePassiveAndGearMultipliers(
        BalanceJsonDocument document,
        DecisionSettings settings,
        bool relationshipsValid,
        ICollection<ConfigValidationIssue> issues)
    {
        if (!relationshipsValid)
        {
            return;
        }

        foreach (var passive in document.Catalogs["passives"])
        {
            ValidateEntityRange(
                passive,
                "weight_multiplier_fp",
                settings.MultiplierMinimum,
                settings.MultiplierMaximum,
                "$.passives[" + passive.Id + "]",
                issues);
        }

        foreach (var gear in document.Catalogs["gear"])
        {
            ValidateEntityRange(
                gear,
                "normalized_value",
                settings.MultiplierMinimum,
                settings.MultiplierMaximum,
                "$.gear[" + gear.Id + "]",
                issues);
        }
    }

    private static void ValidateTags(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var catalog in document.Catalogs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            foreach (var entity in catalog.Value.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
            {
                if (!TryEntityString(entity, "tags", out var source))
                {
                    continue;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var valid = true;
                foreach (var token in source.Split(new[] { '|' }, StringSplitOptions.None))
                {
                    if (!StableId.TryParse(token, out _) || !seen.Add(token))
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                {
                    Add(
                        issues,
                        ConfigValidationCodes.InvalidTagSet,
                        "$." + catalog.Key + "[" + entity.Id + "].tags",
                        "Tags must be unique canonical lowercase tokens separated by '|'.");
                }
            }
        }
    }

    private static void ValidateHitSchedules(
        IEnumerable<BalanceJsonEntity> actions,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var action in actions)
        {
            if (!TryEntityString(action, "hit_schedule", out var schedule) ||
                !TryEntityInteger(action, "hit_count", out var hitCount) ||
                !TryEntityInteger(action, "active_ticks", out var activeTicks))
            {
                continue;
            }

            if (hitCount < 0 || hitCount > Int32Maximum ||
                activeTicks < 0 || activeTicks > Int32Maximum)
            {
                continue;
            }

            if (!IsValidHitSchedule(schedule, hitCount, activeTicks))
            {
                Add(
                    issues,
                    ConfigValidationCodes.InvalidHitSchedule,
                    "$.actions[" + action.Id + "].hit_schedule",
                    "The hit schedule must be canonical, strictly ordered, inside Active, and match hit_count.");
            }
        }
    }

    private static bool IsValidHitSchedule(string schedule, long hitCount, long activeTicks)
    {
        if (schedule.Length == 0)
        {
            return hitCount == 0;
        }

        if (activeTicks == 0)
        {
            return false;
        }

        var items = schedule.Split(new[] { '|' }, StringSplitOptions.None);
        if (items.Length > MaximumHitScheduleEntries)
        {
            return false;
        }

        var previousTick = -1L;
        var scheduledHits = 0L;
        foreach (var item in items)
        {
            var separator = item.IndexOf(':');
            string tickText;
            var isHit = true;
            if (separator < 0)
            {
                tickText = item;
            }
            else
            {
                if (separator == 0 || separator != item.LastIndexOf(':'))
                {
                    return false;
                }

                var primitive = item[..separator];
                if (!HitSchedulePrimitives.Contains(primitive))
                {
                    return false;
                }

                isHit = primitive != "grab";
                tickText = item[(separator + 1)..];
            }

            if (!TryParseCanonicalNonNegativeInteger(tickText, out var tick) ||
                tick <= previousTick ||
                tick >= activeTicks)
            {
                return false;
            }

            previousTick = tick;
            if (isHit)
            {
                scheduledHits++;
            }
        }

        return scheduledHits == hitCount;
    }

    private static void ValidateTargetProfiles(
        IEnumerable<BalanceJsonEntity> actions,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var action in actions)
        {
            if (!TryEntityString(action, "slot_type", out var slot) ||
                slot == "System" ||
                !TryEntityString(action, "movement_mode", out var movement) ||
                !TryEntityString(action, "hit_schedule", out var schedule) ||
                !TryEntityInteger(action, "hit_count", out var hitCount))
            {
                continue;
            }

            var targetsOpponent = hitCount > 0 || schedule.Length > 0;
            var allowed = targetsOpponent ? OpponentMovementModes : SelfMovementModes;
            if (!allowed.Contains(movement))
            {
                Add(
                    issues,
                    ConfigValidationCodes.AmbiguousTargetProfile,
                    "$.actions[" + action.Id + "].movement_mode",
                    "The inferred target is incompatible with the configured movement mode.");
            }
        }
    }

    private static void ValidateCatalogSize(
        BalanceJsonDocument document,
        ICollection<ConfigValidationIssue> issues)
    {
        var systemCount = document.Catalogs["actions"].Count(action =>
            TryEntityString(action, "slot_type", out var slot) && slot == "System");

        foreach (var fighter in document.Catalogs["fighters"])
        {
            var ownedCount = document.Catalogs["actions"].Count(action =>
                TryEntityString(action, "animal_id", out var owner) && owner == fighter.Id.Value &&
                TryEntityString(action, "slot_type", out var slot) && slot is "Basic" or "Special");

            // This is the full diagnostic checked catalog, not the request-specific
            // legal set. The legal <=128 limit is enforced by Battle.Core after
            // mode/loadout applicability is known; replay diagnostics allow 256.
            if (systemCount + ownedCount > 256)
            {
                Add(
                    issues,
                    ConfigValidationCodes.NumericOutOfRange,
                    "$.actions",
                    $"The diagnostic checked action catalog for '{fighter.Id}' exceeds 256 entries.");
            }
        }
    }

    private static bool TryGetDecisionSettings(
        IReadOnlyDictionary<string, ConfigValue> settings,
        out DecisionSettings result)
    {
        result = default;
        if (!TryValidSetting(settings, "global.sim.fp_scale", out var fixedPointScale) ||
            !TryValidSetting(settings, "global.sim.multiplier_min", out var multiplierMinimum) ||
            !TryValidSetting(settings, "global.sim.multiplier_max", out var multiplierMaximum) ||
            !TryValidSetting(settings, "global.sim.decision_weight_max", out var decisionWeightMaximum) ||
            !TryValidSetting(settings, "global.ai.repeat_same_action_fp", out var repeatSameAction) ||
            !TryValidSetting(settings, "global.ai.repeat_same_category_fp", out var repeatSameCategory) ||
            !TryValidSetting(settings, "global.ai.opportunity_cap_fp", out var opportunityCap) ||
            !TryValidSetting(settings, "global.ai.hard_opportunity_misses", out var hardOpportunityMisses))
        {
            return false;
        }

        result = new DecisionSettings(
            fixedPointScale,
            multiplierMinimum,
            multiplierMaximum,
            decisionWeightMaximum,
            repeatSameAction,
            repeatSameCategory,
            opportunityCap,
            hardOpportunityMisses);
        return true;
    }

    private static bool TryValidSetting(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        out long value)
    {
        if (TryInteger(settings, key, out value) && SettingDomains[key].Contains(value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static void ValidateEntityRange(
        BalanceJsonEntity entity,
        string key,
        long minimum,
        long maximum,
        string path,
        ICollection<ConfigValidationIssue> issues)
    {
        if (TryEntityInteger(entity, key, out var value) && !IsInside(value, minimum, maximum))
        {
            Add(
                issues,
                ConfigValidationCodes.NumericOutOfRange,
                path + "." + key,
                $"The value must remain inside [{minimum}, {maximum}].");
        }
    }

    private static bool TryParseCanonicalNonNegativeInteger(string text, out long value)
    {
        value = default;
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsInside(long value, long minimum, long maximum) =>
        value >= minimum && value <= maximum;

    private static bool TryInteger(
        IReadOnlyDictionary<string, ConfigValue> settings,
        string key,
        out long value)
    {
        if (settings.TryGetValue(key, out var property) && property.Kind == ConfigValueKind.Integer)
        {
            value = property.AsInteger();
            return true;
        }

        value = default;
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

    private readonly struct NumericDomain
    {
        public NumericDomain(long minimum, long maximum, string belowMinimumCode)
        {
            Minimum = minimum;
            Maximum = maximum;
            BelowMinimumCode = belowMinimumCode;
        }

        public long Minimum { get; }

        public long Maximum { get; }

        public string BelowMinimumCode { get; }

        public bool Contains(long value) => value >= Minimum && value <= Maximum;
    }

    private readonly struct DecisionSettings
    {
        public DecisionSettings(
            long fixedPointScale,
            long multiplierMinimum,
            long multiplierMaximum,
            long decisionWeightMaximum,
            long repeatSameAction,
            long repeatSameCategory,
            long opportunityCap,
            long hardOpportunityMisses)
        {
            FixedPointScale = fixedPointScale;
            MultiplierMinimum = multiplierMinimum;
            MultiplierMaximum = multiplierMaximum;
            DecisionWeightMaximum = decisionWeightMaximum;
            RepeatSameAction = repeatSameAction;
            RepeatSameCategory = repeatSameCategory;
            OpportunityCap = opportunityCap;
            HardOpportunityMisses = hardOpportunityMisses;
        }

        public long FixedPointScale { get; }

        public long MultiplierMinimum { get; }

        public long MultiplierMaximum { get; }

        public long DecisionWeightMaximum { get; }

        public long RepeatSameAction { get; }

        public long RepeatSameCategory { get; }

        public long OpportunityCap { get; }

        public long HardOpportunityMisses { get; }
    }
}
