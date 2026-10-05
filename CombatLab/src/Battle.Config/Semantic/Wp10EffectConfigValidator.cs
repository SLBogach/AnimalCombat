using System.Globalization;
using Battle.Config.Json;
using Battle.Config.Schema;
using Battle.Contracts.Config;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Config.Semantic;

/// <summary>v0.2-only validation. Never supplies missing workbook values.</summary>
internal static class Wp10EffectConfigValidator
{
    private static readonly HashSet<string> StatTargets = new(StringComparer.Ordinal)
    {
        "Power", "Armor", "Precision", "Evasion", "Guard", "GuardBreak", "MoveSpeed",
        "ActionSpeed", "Initiative", "ControlPower", "ControlResistance", "Mass", "EnergyRegen",
    };
    private static readonly HashSet<string> Multipliers = new(StringComparer.Ordinal)
    {
        "DamageTaken", "DamageDealt", "BlockWeight", "PunishWeight", "WallActionWeight", "HardControlDuration",
    };
    private static readonly HashSet<string> Offsets = new(StringComparer.Ordinal)
    { "BlockChanceOffset", "DodgeChanceOffset", "GrabPriority" };
    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal)
    { "HardControlAllowed", "GrabAllowed", "KnockdownAllowed" };
    private static readonly HashSet<string> DormantTargets = new(StringComparer.Ordinal)
    { "GripGain", "RageGain", "TempoPerUnit", "DecayGraceTicks", "SignatureWeight" };

    public static void Validate(BalanceJsonDocument document, ICollection<ConfigValidationIssue> issues)
    {
        ValidateBounds(document, issues);
        Range(document.Settings, "global.control.max_trigger_depth", 1, 32, issues);
        Range(document.Settings, "global.control.max_triggers_per_tick", 1, 4096, issues);
        Range(document.Settings, "global.control.max_effect_instances_per_fighter", 1, 128, issues);
        foreach (var stage in new[] { "fall", "grounded", "getup" })
            Range(document.Settings, "global.control.knockdown_" + stage + "_ticks", 1, 100, issues);

        var effects = document.Catalogs["effects"].ToDictionary(x => x.Id.Value, StringComparer.Ordinal);
        var actions = document.Catalogs["actions"].Select(x => x.Id.Value).ToHashSet(StringComparer.Ordinal);
        var rules = document.Catalogs["effect_rules"];
        ValidateRules(rules, effects, actions, issues);
        var reachable = ReachableEffects(rules);
        ValidateEffects(document, effects, reachable, issues);
        ValidateGroups(effects.Values, issues);
        foreach (var action in document.Catalogs["actions"]) ValidateInterrupt(action, issues);
        foreach (var gear in document.Catalogs["gear"])
            EntityRange(gear, "priority", int.MinValue, int.MaxValue, ConfigValidationCodes.InvalidEffectGroup,
                "$.gear[" + gear.Id + "]", issues);
    }

    internal static bool OwnsNumericDomain(string catalog, string key) =>
        (catalog is "effects" or "effect_rules" or "gear") && key == "priority" ||
        (catalog is "effects" or "effect_rules") &&
        key is "internal_cooldown_ticks" or "max_activations_per_tick" or "max_activations_per_battle";

    private static void ValidateBounds(BalanceJsonDocument document, ICollection<ConfigValidationIssue> issues)
    {
        foreach (var stat in BalanceSchemaDefinition.BoundedStats)
        {
            var minimumKey = "stat." + stat + ".min";
            var maximumKey = "stat." + stat + ".max";
            if (!Integer(document.Settings, minimumKey, out var minimum) ||
                !Integer(document.Settings, maximumKey, out var maximum)) continue;
            var domainMin = stat is "max_health" or "move_speed" or "action_speed" or "mass" ? 1 : 0;
            var domainMax = stat switch
            {
                "max_health" or "max_energy" => 10000,
                "armor" or "mass" => 2000,
                "move_speed" or "action_speed" => 500,
                _ => 1000,
            };
            if (minimum < domainMin || maximum > domainMax || minimum > maximum)
                Add(issues, ConfigValidationCodes.InvalidStatBounds, "$.settings." + minimumKey,
                    "Bounds are inverted or outside the approved stat domain.");
        }
    }

    private static void ValidateRules(IEnumerable<BalanceJsonEntity> rules,
        IReadOnlyDictionary<string, BalanceJsonEntity> effects, ISet<string> actions,
        ICollection<ConfigValidationIssue> issues)
    {
        foreach (var rule in rules)
        {
            var path = "$.effect_rules[" + rule.Id + "]";
            var kind = Text(rule, "owner_kind");
            var owner = Text(rule, "owner_id");
            var validOwner = kind switch
            {
                "Global" => owner == "global",
                "Action" => actions.Contains(owner),
                "Effect" => effects.ContainsKey(owner),
                _ => false,
            };
            if (!validOwner || !StableId.TryParse(owner, out _))
                Add(issues, ConfigValidationCodes.InvalidEffectRule, path + ".owner_id", "Unknown or incompatible rule owner.");
            if (!effects.ContainsKey(Text(rule, "effect_id")))
                Add(issues, ConfigValidationCodes.InvalidEffectReference, path + ".effect_id", "Unknown rule effect reference.");
            foreach (var key in new[] { "internal_cooldown_ticks", "max_activations_per_tick", "max_activations_per_battle", "priority" })
            {
                var minimum = key == "priority" ? int.MinValue : key == "internal_cooldown_ticks" ? 0 : 1;
                EntityRange(rule, key, minimum, int.MaxValue, ConfigValidationCodes.InvalidEffectRule, path, issues);
            }
            if (rule.Properties.TryGetValue("once_per_event", out var once) &&
                once.Kind == ConfigValueKind.Boolean && !once.AsBoolean())
                Add(issues, ConfigValidationCodes.InvalidEffectRule, path + ".once_per_event", "once_per_event must be true.");
        }
    }

    private static HashSet<string> ReachableEffects(IEnumerable<BalanceJsonEntity> rules)
    {
        var all = rules.ToArray();
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var rule in all)
            {
                var kind = Text(rule, "owner_kind");
                if (Text(rule, "primitive") == "ApplyEffect" &&
                    (kind is "Global" or "Action" || kind == "Effect" && reachable.Contains(Text(rule, "owner_id"))))
                    changed |= reachable.Add(Text(rule, "effect_id"));
            }
        } while (changed);
        return reachable;
    }

    private static void ValidateEffects(BalanceJsonDocument document,
        IReadOnlyDictionary<string, BalanceJsonEntity> effects, ISet<string> reachable,
        ICollection<ConfigValidationIssue> issues)
    {
        var roles = new Dictionary<string, BalanceJsonEntity>(StringComparer.Ordinal);
        foreach (var effect in effects.Values)
        {
            var path = "$.effects[" + effect.Id + "]";
            EntityRange(effect, "duration_ticks", 1, int.MaxValue, ConfigValidationCodes.InvalidDuration, path, issues);
            EntityRange(effect, "stack_cap", 1, 255, ConfigValidationCodes.InvalidEffectGroup, path, issues);
            EntityRange(effect, "priority", int.MinValue, int.MaxValue, ConfigValidationCodes.InvalidEffectGroup, path, issues);
            EntityRange(effect, "internal_cooldown_ticks", 0, int.MaxValue, ConfigValidationCodes.InvalidEffectRule, path, issues);
            EntityRange(effect, "max_activations_per_tick", 1, int.MaxValue, ConfigValidationCodes.InvalidEffectRule, path, issues);
            EntityRange(effect, "max_activations_per_battle", 1, int.MaxValue, ConfigValidationCodes.InvalidEffectRule, path, issues);
            var role = Text(effect, "semantic_role");
            if (role.Length > 0 && role != "None" && !roles.TryAdd(role, effect))
                Add(issues, ConfigValidationCodes.InvalidEffectReference, path + ".semantic_role", "A special role must be unique.");
            ValidateModifier(effect, 1, reachable.Contains(effect.Id.Value), path, issues);
            // v0.1 exports operation2=Override/value2=0 without a target for unused modifier slots.
            // Retain the placeholder bytes/number; it is not an active modifier or a runtime default.
            var hasSecondValue = Integer(effect.Properties, "value2", out var secondValue);
            var secondTarget = Text(effect, "modifier_stat2");
            var unusedPlaceholder = secondTarget.Length == 0 && hasSecondValue && secondValue == 0;
            if (secondTarget.Length > 0 || !unusedPlaceholder && (Text(effect, "operation2").Length > 0 || hasSecondValue))
            {
                if (Text(effect, "modifier_stat2").Length == 0 || Text(effect, "operation2").Length == 0 || !effect.Properties.ContainsKey("value2"))
                    Add(issues, ConfigValidationCodes.InvalidEffectGroup, path + ".modifier_stat2", "The second modifier must be a complete triple.");
                ValidateModifier(effect, 2, reachable.Contains(effect.Id.Value), path, issues);
            }
        }

        foreach (var binding in new[]
        {
            (Role: "ControlFatigue", Setting: "fatigue_decay_ticks"),
            (Role: "ControlImmunity", Setting: "immunity_ticks"),
            (Role: "GrabLockout", Setting: "grab_lockout_ticks"),
            (Role: "WakeupImmunity", Setting: "wakeup_immunity_ticks"),
            (Role: "GuardBreak", Setting: ""),
        })
        {
            if (!roles.TryGetValue(binding.Role, out var effect))
            {
                Add(issues, ConfigValidationCodes.InvalidEffectReference, "$.effects", "Missing required semantic role " + binding.Role + ".");
                continue;
            }
            if (binding.Setting.Length > 0 && Integer(effect.Properties, "duration_ticks", out var duration) &&
                Integer(document.Settings, "global.control." + binding.Setting, out var expected) && duration != expected)
                Add(issues, ConfigValidationCodes.InvalidDuration, "$.effects[" + effect.Id + "].duration_ticks", "Effect and global control durations disagree.");
        }
        if (roles.TryGetValue("ControlFatigue", out var fatigue)) ValidateFatigue(document, fatigue, issues);
    }

    private static void ValidateFatigue(BalanceJsonDocument document, BalanceJsonEntity fatigue,
        ICollection<ConfigValidationIssue> issues)
    {
        var path = "$.effects[" + fatigue.Id + "]";
        var lookup = Text(fatigue, "lookup_profile").Split('|');
        if (!Integer(fatigue.Properties, "stack_cap", out var cap) || lookup.LongLength != cap + 1 ||
            !Integer(document.Settings, "global.sim.fp_scale", out var scale) ||
            lookup.Any(x => !int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > scale) ||
            Text(fatigue, "modifier_stat1") != "HardControlDuration" || Text(fatigue, "operation1") != "Multiply" ||
            Text(fatigue, "stack_policy") != "AddStacks")
            Add(issues, ConfigValidationCodes.InvalidEffectLookup, path + ".lookup_profile", "Fatigue requires cap+1 fixed-point values and an AddStacks duration multiplier.");
        if (Integer(document.Settings, "global.control.fatigue_threshold", out var threshold) && (threshold < 1 || threshold > cap))
            Add(issues, ConfigValidationCodes.InvalidEffectLookup, "$.settings.global.control.fatigue_threshold", "Fatigue threshold must be inside the stack domain.");
    }

    private static void ValidateModifier(BalanceJsonEntity effect, int ordinal, bool reachable,
        string path, ICollection<ConfigValidationIssue> issues)
    {
        var suffix = ordinal.ToString(CultureInfo.InvariantCulture);
        var target = Text(effect, "modifier_stat" + suffix);
        var operation = Text(effect, "operation" + suffix);
        var supported = StatTargets.Contains(target) || Multipliers.Contains(target) || Offsets.Contains(target) || Flags.Contains(target);
        if (!supported && (!DormantTargets.Contains(target) || reachable))
        {
            Add(issues, ConfigValidationCodes.UnsupportedEffectModifierTarget, path + ".modifier_stat" + suffix,
                "The modifier target is not available for WP-10 activation.");
            return;
        }
        var validOperation = StatTargets.Contains(target) ? operation is "Add" or "Multiply" or "Override"
            : Multipliers.Contains(target) ? operation == "Multiply"
            : Offsets.Contains(target) ? operation == "Add"
            : Flags.Contains(target) ? operation == "Override" : true;
        if (!validOperation)
            Add(issues, ConfigValidationCodes.UnsupportedEffectModifierTarget, path + ".operation" + suffix, "Unsupported operation/target pair.");
        if (Flags.Contains(target) && Integer(effect.Properties, "value" + suffix, out var value) && value is not (0 or 1))
            Add(issues, ConfigValidationCodes.UnsupportedEffectModifierTarget, path + ".value" + suffix, "An allow flag must be 0 or 1.");
    }

    private static void ValidateGroups(IEnumerable<BalanceJsonEntity> effects, ICollection<ConfigValidationIssue> issues)
    {
        foreach (var group in effects.GroupBy(x => Text(x, "stack_group"), StringComparer.Ordinal))
        {
            var members = group.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            var first = members[0];
            var policy = Text(first, "stack_policy");
            var compare = Text(first, "compare_key");
            if (group.Key.Length == 0 || policy is "Refresh" or "AddStacks" && members.Length > 1 ||
                policy == "StrongestWins" && compare is not ("Value1" or "DurationTicks" or "StackCount"))
                Add(issues, ConfigValidationCodes.InvalidEffectGroup, "$.effects[" + first.Id + "].stack_group", "Incompatible group membership or comparison domain.");
            foreach (var member in members.Skip(1))
            {
                if (Text(member, "stack_policy") != policy ||
                    policy == "StrongestWins" && Text(member, "compare_key") != compare ||
                    Text(member, "modifier_stat1") != Text(first, "modifier_stat1") ||
                    Text(member, "operation1") != Text(first, "operation1") ||
                    Text(member, "modifier_stat2") != Text(first, "modifier_stat2") ||
                    Text(member, "operation2") != Text(first, "operation2"))
                    Add(issues, ConfigValidationCodes.InvalidEffectGroup, "$.effects[" + member.Id + "].stack_group", "Group policies, modifier targets and compare domains must agree.");
            }
        }
    }

    private static void ValidateInterrupt(BalanceJsonEntity action, ICollection<ConfigValidationIssue> issues)
    {
        var path = "$.actions[" + action.Id + "]";
        EntityRange(action, "hit_interrupt_strength", 0, 3, ConfigValidationCodes.InvalidInterruptProfile, path, issues);
        EntityRange(action, "hit_interrupt_min_strength", 1, 3, ConfigValidationCodes.InvalidInterruptProfile, path, issues);
        ValidateTokens<ActionPhase>(action, "hit_interruptible_phases", path, issues);
        ValidateTokens<ActionPhase>(action, "protected_phases", path, issues);
        ValidateTokens<ControlCategory>(action, "ignored_control_categories", path, issues);
        var categories = Text(action, "ignored_control_categories").Split('|');
        if (categories.Contains("Grab", StringComparer.Ordinal) || categories.Contains("Defeat", StringComparer.Ordinal) ||
            Text(action, "interrupt_profile") != "Unstoppable" && Text(action, "ignored_control_categories").Length > 0)
            Add(issues, ConfigValidationCodes.InvalidInterruptProfile, path + ".ignored_control_categories", "Only Unstoppable can ignore Stun/Knockdown, never Grab/Defeat.");
    }

    private static void ValidateTokens<T>(BalanceJsonEntity entity, string key, string path,
        ICollection<ConfigValidationIssue> issues) where T : struct, Enum
    {
        var text = Text(entity, key);
        if (text.Length == 0) return;
        var tokens = text.Split('|');
        var allowed = Enum.GetNames(typeof(T));
        if (tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length ||
            tokens.Any(x => !allowed.Contains(x, StringComparer.Ordinal)))
            Add(issues, ConfigValidationCodes.InvalidInterruptProfile, path + "." + key, "Unknown or duplicate phase/category token.");
    }

    private static void Range(IReadOnlyDictionary<string, ConfigValue> values, string key,
        int minimum, int maximum, ICollection<ConfigValidationIssue> issues)
    {
        if (Integer(values, key, out var value) && (value < minimum || value > maximum))
            Add(issues, ConfigValidationCodes.NumericOutOfRange, "$.settings." + key, "Value is outside the approved domain.");
    }

    private static void EntityRange(BalanceJsonEntity entity, string key, int minimum, int maximum,
        string code, string path, ICollection<ConfigValidationIssue> issues)
    {
        if (Integer(entity.Properties, key, out var value) && (value < minimum || value > maximum))
            Add(issues, code, path + "." + key, "Value is outside the supported integer domain.");
    }

    private static bool Integer(IReadOnlyDictionary<string, ConfigValue> values, string key, out long value)
    {
        value = default;
        if (!values.TryGetValue(key, out var item) || item.Kind != ConfigValueKind.Integer) return false;
        value = item.AsInteger();
        return true;
    }

    private static string Text(BalanceJsonEntity entity, string key) =>
        entity.Properties.TryGetValue(key, out var value) && value.Kind == ConfigValueKind.String ? value.AsString() : string.Empty;

    private static void Add(ICollection<ConfigValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new ConfigValidationIssue(code, path, message));
}
