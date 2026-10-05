using System.Globalization;
using Battle.Contracts.Config;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Core.Math;

namespace Battle.Core.Effects;

/// <summary>Pre-start validation of compiled values, never a JSON/XLSX reader or runtime fallback.</summary>
internal static class EffectSetupMaterializer
{
    internal static EffectRuntimeDefinition? TryCreate(BattleRequest request, CompiledBattleConfig config,
        ICollection<EffectSetupIssue> issues)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (issues is null) throw new ArgumentNullException(nameof(issues));
        var reader = new Reader(config);
        var result = reader.Create(request);
        foreach (var issue in reader.Issues.Distinct().OrderBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Entity, StringComparer.Ordinal)) issues.Add(issue);
        return result;
    }

    // The registry owns names/domains, not missing numeric DATA defaults.
    internal static IEnumerable<(string Runtime, string Data, int Minimum, int Maximum)> StatDomains()
    {
        yield return ("MaxHealth", "max_health", 1, 10000);
        yield return ("MaxEnergy", "max_energy", 0, 10000);
        yield return ("EnergyRegen", "energy_regen", 0, 1000);
        yield return ("Power", "power", 0, 1000);
        yield return ("Armor", "armor", 0, 2000);
        yield return ("Precision", "precision", 0, 1000);
        yield return ("Evasion", "evasion", 0, 1000);
        yield return ("Guard", "guard", 0, 1000);
        yield return ("GuardBreak", "guard_break", 0, 1000);
        yield return ("MoveSpeed", "move_speed", 1, 500);
        yield return ("ActionSpeed", "action_speed", 1, 500);
        yield return ("Initiative", "initiative", 0, 1000);
        yield return ("ControlPower", "control_power", 0, 1000);
        yield return ("ControlResistance", "control_resistance", 0, 1000);
        yield return ("Mass", "mass", 1, 2000);
    }

    private sealed record CatalogEffect(CompiledConfigEntity Entity, EffectStackPolicy Policy,
        StableId Group, EffectCompareKey? CompareKey, IReadOnlyList<(string Target, EffectModifierOperation Operation)> Domains,
        EffectProfile? Profile, EffectSemanticRole Role, int Duration, int StackCap, IReadOnlyList<int> Lookup);

    private sealed class Reader
    {
        private readonly CompiledBattleConfig config;
        internal Reader(CompiledBattleConfig config) => this.config = config;
        internal List<EffectSetupIssue> Issues { get; } = new();

        internal EffectRuntimeDefinition? Create(BattleRequest request)
        {
            if (config.Reference.BalanceSchemaVersion.ToString() != "combat.balance/0.2" ||
                config.Reference.ConfigVersion.ToString() != "v0.2")
                Add("UnsupportedBalanceSchema", "/config/reference", null);
            SettingText("global.sim.schema_version", "combat.balance/0.2", "UnsupportedBalanceSchema");
            SettingText("global.sim.config_version", "v0.2", "UnsupportedConfigVersion");
            if (request.ConfigHash != config.Reference.ConfigHash)
                Add("ConfigHashMismatch", "/config_hash", null);
            if (request.ModeRules.NormalizationMode != NormalizationMode.None)
                Add("UnsupportedNormalizationMode", "/mode_rules/normalization_mode", null);
            var scale = Setting("global.sim.fp_scale", 1, int.MaxValue);
            var time = Setting("battle.time_limit_ticks", 1, 999999);
            var depth = Setting("global.control.max_trigger_depth", 1, 32);
            var triggers = Setting("global.control.max_triggers_per_tick", 1, 4096);
            var instances = Setting("global.control.max_effect_instances_per_fighter", 1, 128);
            var threshold = Setting("global.control.fatigue_threshold", 1, 255);
            var fall = Setting("global.control.knockdown_fall_ticks", 1, 100);
            var grounded = Setting("global.control.knockdown_grounded_ticks", 1, 100);
            var getup = Setting("global.control.knockdown_getup_ticks", 1, 100);
            var stunMin = Setting("global.control.stun_min_ticks", 1, int.MaxValue);
            var stunMax = Setting("global.control.stun_max_ticks", 1, int.MaxValue);
            var controlK = Setting("global.control.control_k", 1, int.MaxValue);
            _ = Setting("global.control.force_k", 1, int.MaxValue);
            _ = Setting("global.damage.armor_k", 1, int.MaxValue);
            if (stunMin > stunMax) Add("InvalidControlDuration", "/config/settings/global.control.stun_min_ticks", null);
            var bounds = ReadBounds();
            var fighters = new List<EffectFighterDefinition>();
            foreach (var build in new[] { request.BuildA, request.BuildB })
            {
                var fighter = ReadFighter(build, request.ModeRules, bounds, scale);
                if (fighter is not null) fighters.Add(fighter);
            }
            var selected = fighters.SelectMany(x => x.SelectedActions).ToHashSet();
            var allRules = ReadRules();
            var reachable = ReachableEffects(allRules, selected);
            var catalog = config.Effects.Select(x => ReadEffect(x, reachable.Contains(x.Id), scale)).Where(x => x is not null)
                .Select(x => x!).ToArray();
            ValidateGroups(catalog);
            ValidateRoles(catalog, threshold, scale);
            var rules = allRules.Where(x => x.OwnerKind == EffectOwnerKind.Global ||
                x.OwnerKind == EffectOwnerKind.Action && selected.Contains(x.OwnerId) ||
                x.OwnerKind == EffectOwnerKind.Effect && reachable.Contains(x.OwnerId)).ToArray();
            var effects = catalog.Where(x => reachable.Contains(x.Entity.Id) && x.Profile is not null)
                .Select(x => x.Profile!).ToArray();
            var interrupts = config.Actions.Select(ReadInterrupt).Where(x => x is not null).Select(x => x!).ToArray();
            if (Issues.Count != 0) return null;
            var definition = new EffectRuntimeDefinition(scale, time, depth, triggers, instances, threshold,
                fall, grounded, getup, stunMin, stunMax, controlK, bounds, effects, rules, interrupts, fighters);
            EffectArithmeticProof.Validate(definition, Issues);
            return Issues.Count == 0 ? definition : null;
        }

        private Dictionary<string, StatBounds> ReadBounds()
        {
            var result = new Dictionary<string, StatBounds>(StringComparer.Ordinal);
            foreach (var stat in StatDomains())
            {
                var path = "/config/settings/stat." + stat.Data;
                var before = Issues.Count;
                var min = Setting("stat." + stat.Data + ".min", stat.Minimum, stat.Maximum, "MissingStatBounds", "InvalidStatBounds");
                var max = Setting("stat." + stat.Data + ".max", stat.Minimum, stat.Maximum, "MissingStatBounds", "InvalidStatBounds");
                if (min > max) Add("InvalidStatBounds", path + ".min", null);
                if (Issues.Count == before) result.Add(stat.Runtime, new StatBounds(min, max));
            }
            return result;
        }

        private EffectFighterDefinition? ReadFighter(FighterBuildSnapshot build, ModeRulesSnapshot mode,
            IReadOnlyDictionary<string, StatBounds> bounds, int scale)
        {
            var before = Issues.Count;
            var path = "/fighters/" + (build.FighterId == FighterId.FighterA ? "0" : "1");
            if (!mode.AllowedAnimalIds.Contains(build.AnimalId) || !config.TryGetFighter(build.AnimalId, out var animal) || animal is null)
            {
                Add("InvalidEffectReference", path + "/animal_id", build.AnimalId.Value);
                return null;
            }
            var baseStats = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var stat in StatDomains())
                baseStats.Add(stat.Runtime, Integer(animal, stat.Data, stat.Minimum, int.MaxValue, "InvalidStatBounds", "/config/fighters/" + animal.Id));
            var actions = new List<StableId>();
            foreach (var entity in config.Actions)
            {
                if (!mode.AllowedActionIds.Contains(entity.Id)) continue;
                var slot = Text(entity, "slot_type", "InvalidEffectReference", "/config/actions/" + entity.Id);
                var owner = Text(entity, "animal_id", "InvalidEffectReference", "/config/actions/" + entity.Id);
                if (slot == "System" || owner == build.AnimalId.Value &&
                    (slot == "Basic" || slot == "Special" && build.SpecialActionIds.Contains(entity.Id))) actions.Add(entity.Id);
            }
            foreach (var id in build.SpecialActionIds)
                if (!actions.Contains(id)) Add("InvalidEffectReference", path + "/special_action_ids", id.Value);
            var gear = new List<InitialStatSource>();
            foreach (var selection in new[] { (Id: build.Gear.Offense, Slot: "Offense"),
                (Id: build.Gear.Defense, Slot: "Defense"), (Id: build.Gear.Utility, Slot: "Utility") })
            {
                if (!mode.AllowedGearIds.Contains(selection.Id) || !config.TryGetGear(selection.Id, out var entity) || entity is null)
                {
                    Add("InvalidEffectReference", path + "/gear/" + selection.Slot, selection.Id.Value);
                    continue;
                }
                var gearPath = "/config/gear/" + entity.Id;
                if (Text(entity, "slot", "InvalidEffectReference", gearPath) != selection.Slot)
                    Add("WrongSlot", path + "/gear/" + selection.Slot, entity.Id.Value);
                var priority = Integer(entity, "priority", int.MinValue, int.MaxValue, "InvalidEffectGroup", gearPath);
                for (var ordinal = 1; ordinal <= 2; ordinal++)
                {
                    var suffix = ordinal.ToString(CultureInfo.InvariantCulture);
                    var stat = Text(entity, "stat" + suffix, "UnsupportedEffectModifierTarget", gearPath, optional: ordinal == 2);
                    if (stat.Length == 0 && ordinal == 2)
                    {
                        if (entity.TryGetProperty("value2", out var unused) && (unused.Kind != ConfigValueKind.Integer || unused.AsInteger() != 0))
                            Add("UnsupportedEffectModifierTarget", gearPath + "/stat2", entity.Id.Value);
                        if (entity.TryGetProperty("operation2", out _)) _ = EnumValue<EffectModifierOperation>(entity, "operation2", "UnsupportedEffectModifierTarget", gearPath);
                        continue;
                    }
                    var operation = EnumValue<EffectModifierOperation>(entity, "operation" + suffix, "UnsupportedEffectModifierTarget", gearPath);
                    var value = Integer(entity, "value" + suffix, int.MinValue, int.MaxValue, "EffectArithmeticOverflowRisk", gearPath);
                    if (!bounds.ContainsKey(stat)) Add("UnsupportedEffectModifierTarget", gearPath + "/stat" + suffix, entity.Id.Value);
                    if (operation.HasValue) gear.Add(new InitialStatSource(stat, priority, entity.Id, ordinal, operation.Value, value));
                }
            }
            var initial = new Dictionary<string, int>(StringComparer.Ordinal);
            if (Issues.Count != before || scale < 1 || bounds.Count != StatDomains().Count()) return null;
            foreach (var stat in baseStats)
            {
                try { initial.Add(stat.Key, InitialValue(stat.Value, bounds[stat.Key], gear.Where(x => x.Stat == stat.Key), scale)); }
                catch (OverflowException) { Add("EffectArithmeticOverflowRisk", path + "/stats/" + stat.Key, build.AnimalId.Value); }
            }
            return Issues.Count == before ? new EffectFighterDefinition(build.FighterId, baseStats, gear, initial, actions) : null;
        }

        private static int InitialValue(int value, StatBounds bounds, IEnumerable<InitialStatSource> sources, int scale)
        {
            var additive = 0; var product = scale; int? overridden = null;
            foreach (var source in sources.OrderBy(x => x.Priority).ThenBy(x => x.SourceId).ThenBy(x => x.Ordinal))
            {
                switch (source.Operation)
                {
                    case EffectModifierOperation.Add: additive = checked(additive + source.Value); break;
                    case EffectModifierOperation.Multiply: product = FixedMath.Mul(product, source.Value, scale); break;
                    case EffectModifierOperation.Override: overridden = source.Value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(sources));
                }
            }
            var derived = FixedMath.Mul(checked(value + additive), product, scale);
            return bounds.Clamp(overridden ?? derived);
        }

        private EffectRuleProfile[] ReadRules()
        {
            var result = new List<EffectRuleProfile>();
            foreach (var entity in config.EffectRules)
            {
                var path = "/config/effect_rules/" + entity.Id;
                var before = Issues.Count;
                var kind = EnumValue<EffectOwnerKind>(entity, "owner_kind", "InvalidEffectRule", path);
                var owner = Id(entity, "owner_id", "InvalidEffectRule", path);
                var trigger = EnumValue<EffectTrigger>(entity, "trigger", "InvalidEffectRule", path);
                var recipient = EnumValue<EffectRecipient>(entity, "recipient", "InvalidEffectRule", path);
                var condition = EnumValue<EffectCondition>(entity, "condition", "InvalidEffectRule", path);
                var primitive = EnumValue<EffectPrimitive>(entity, "primitive", "InvalidEffectRule", path);
                var effect = Id(entity, "effect_id", "InvalidEffectReference", path);
                var priority = Integer(entity, "priority", int.MinValue, int.MaxValue, "InvalidEffectRule", path);
                var cooldown = Integer(entity, "internal_cooldown_ticks", 0, int.MaxValue, "InvalidEffectRule", path);
                var tickCap = Integer(entity, "max_activations_per_tick", 1, int.MaxValue, "InvalidEffectRule", path);
                var battleCap = Integer(entity, "max_activations_per_battle", 1, int.MaxValue, "InvalidEffectRule", path);
                if (!entity.TryGetProperty("once_per_event", out var once) || once.Kind != ConfigValueKind.Boolean || !once.AsBoolean())
                    Add("InvalidEffectRule", path + "/once_per_event", entity.Id.Value);
                if (!config.TryGetEffect(effect, out _)) Add("InvalidEffectReference", path + "/effect_id", entity.Id.Value);
                var validOwner = kind switch
                {
                    EffectOwnerKind.Global => owner.Value == "global",
                    EffectOwnerKind.Action => config.TryGetAction(owner, out _),
                    EffectOwnerKind.Effect => config.TryGetEffect(owner, out _),
                    _ => false,
                };
                if (!validOwner) Add("InvalidEffectRule", path + "/owner_id", entity.Id.Value);
                if (Issues.Count == before) result.Add(new EffectRuleProfile(entity.Id, kind!.Value, owner, trigger!.Value,
                    recipient!.Value, condition!.Value, primitive!.Value, effect, priority, cooldown, tickCap, battleCap, true));
            }
            return result.ToArray();
        }

        private static HashSet<StableId> ReachableEffects(IEnumerable<EffectRuleProfile> rules, ISet<StableId> selected)
        {
            var reachable = new HashSet<StableId>();
            bool changed;
            do
            {
                changed = false;
                foreach (var rule in rules)
                    if (rule.Primitive == EffectPrimitive.ApplyEffect && (rule.OwnerKind == EffectOwnerKind.Global ||
                        rule.OwnerKind == EffectOwnerKind.Action && selected.Contains(rule.OwnerId) ||
                        rule.OwnerKind == EffectOwnerKind.Effect && reachable.Contains(rule.OwnerId)))
                        changed |= reachable.Add(rule.EffectId);
            } while (changed);
            return reachable;
        }

        private CatalogEffect? ReadEffect(CompiledConfigEntity entity, bool reachable, int scale)
        {
            var path = "/config/effects/" + entity.Id;
            var before = Issues.Count;
            var duration = Integer(entity, "duration_ticks", 1, int.MaxValue, "InvalidEffectGroup", path);
            var boundary = EnumValue<EffectExpiryBoundary>(entity, "expiry_boundary", "InvalidEffectGroup", path);
            var policy = EnumValue<EffectStackPolicy>(entity, "stack_policy", "InvalidEffectGroup", path);
            var cap = Integer(entity, "stack_cap", 1, 255, "InvalidEffectGroup", path);
            var group = Id(entity, "stack_group", "InvalidEffectGroup", path);
            var compare = EnumValue<EffectCompareKey>(entity, "compare_key", "InvalidEffectGroup", path, optional: true);
            if (policy == EffectStackPolicy.StrongestWins && !compare.HasValue) Add("InvalidEffectGroup", path + "/compare_key", entity.Id.Value);
            var priority = Integer(entity, "priority", int.MinValue, int.MaxValue, "InvalidEffectGroup", path);
            var refresh = EnumValue<EffectRefreshRule>(entity, "refresh_rule", "InvalidEffectGroup", path);
            var role = EnumValue<EffectSemanticRole>(entity, "semantic_role", "InvalidEffectReference", path);
            var cooldown = Integer(entity, "internal_cooldown_ticks", 0, int.MaxValue, "InvalidEffectRule", path);
            var tickCap = Integer(entity, "max_activations_per_tick", 1, int.MaxValue, "InvalidEffectRule", path);
            var battleCap = Integer(entity, "max_activations_per_battle", 1, int.MaxValue, "InvalidEffectRule", path);
            var modifiers = new List<EffectModifier>();
            var domains = new List<(string Target, EffectModifierOperation Operation)>();
            var executable = true;
            for (var ordinal = 1; ordinal <= 2; ordinal++)
            {
                var suffix = ordinal.ToString(CultureInfo.InvariantCulture);
                var targetText = Text(entity, "modifier_stat" + suffix, "UnsupportedEffectModifierTarget", path, optional: ordinal == 2);
                if (targetText.Length == 0 && ordinal == 2)
                {
                    if (entity.TryGetProperty("value2", out var unused) && (unused.Kind != ConfigValueKind.Integer || unused.AsInteger() != 0))
                        Add("UnsupportedEffectModifierTarget", path + "/modifier_stat2", entity.Id.Value);
                    if (entity.TryGetProperty("operation2", out _)) _ = EnumValue<EffectModifierOperation>(entity, "operation2", "UnsupportedEffectModifierTarget", path);
                    continue;
                }
                var operation = EnumValue<EffectModifierOperation>(entity, "operation" + suffix, "UnsupportedEffectModifierTarget", path);
                var flag = targetText is "HardControlAllowed" or "GrabAllowed" or "KnockdownAllowed";
                var value = Integer(entity, "value" + suffix, flag ? 0 : int.MinValue, flag ? 1 : int.MaxValue,
                    flag ? "UnsupportedEffectModifierTarget" : "EffectArithmeticOverflowRisk", path);
                var supported = Enum.GetNames(typeof(EffectModifierTarget)).Contains(targetText, StringComparer.Ordinal);
                if (!supported)
                {
                    executable = false;
                    var dormant = targetText is "GripGain" or "RageGain" or "TempoPerUnit" or "DecayGraceTicks" or "SignatureWeight";
                    if (!dormant || reachable) Add("UnsupportedEffectModifierTarget", path + "/modifier_stat" + suffix, entity.Id.Value);
                }
                if (!operation.HasValue) continue;
                domains.Add((targetText, operation.Value));
                if (!supported) continue;
                var target = Enum.Parse<EffectModifierTarget>(targetText);
                try { modifiers.Add(new EffectModifier(target, operation.Value, value, ordinal)); }
                catch (ArgumentException) { Add("UnsupportedEffectModifierTarget", path + "/operation" + suffix, entity.Id.Value); }
            }
            var lookup = new List<int>();
            var lookupText = Text(entity, "lookup_profile", "InvalidEffectLookup", path, optional: role != EffectSemanticRole.ControlFatigue);
            if (lookupText.Length != 0)
                foreach (var token in lookupText.Split('|'))
                {
                    if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > scale)
                        Add("InvalidEffectLookup", path + "/lookup_profile", entity.Id.Value);
                    else lookup.Add(value);
                }
            if (role == EffectSemanticRole.ControlFatigue && (lookup.Count != cap + 1 || policy != EffectStackPolicy.AddStacks ||
                modifiers.Count != 1 || modifiers[0].Target != EffectModifierTarget.HardControlDuration || modifiers[0].Operation != EffectModifierOperation.Multiply))
                Add("InvalidEffectLookup", path + "/lookup_profile", entity.Id.Value);
            if (Issues.Count != before) return null;
            EffectProfile? profile = null;
            if (executable)
                profile = new EffectProfile(entity.Id, group, duration, boundary!.Value, policy!.Value, cap, compare,
                    priority, refresh!.Value, role!.Value, cooldown, tickCap, battleCap, modifiers, lookup);
            return new CatalogEffect(entity, policy!.Value, group, compare, domains, profile, role!.Value, duration, cap, lookup);
        }

        private void ValidateGroups(IEnumerable<CatalogEffect> catalog)
        {
            foreach (var group in catalog.GroupBy(x => x.Group).OrderBy(x => x.Key))
            {
                var entries = group.OrderBy(x => x.Entity.Id).ToArray();
                var first = entries[0];
                if (entries.Length > 1 && first.Policy is EffectStackPolicy.Refresh or EffectStackPolicy.AddStacks)
                    Add("InvalidEffectGroup", "/config/effects/" + first.Entity.Id + "/stack_group", first.Entity.Id.Value);
                foreach (var entry in entries.Skip(1))
                    if (entry.Policy != first.Policy || first.Policy == EffectStackPolicy.StrongestWins && entry.CompareKey != first.CompareKey || !entry.Domains.SequenceEqual(first.Domains))
                        Add("InvalidEffectGroup", "/config/effects/" + entry.Entity.Id + "/stack_group", entry.Entity.Id.Value);
            }
        }

        private void ValidateRoles(IEnumerable<CatalogEffect> catalog, int threshold, int scale)
        {
            foreach (var binding in new[] { (Role: EffectSemanticRole.ControlFatigue, Setting: "fatigue_decay_ticks"),
                (Role: EffectSemanticRole.ControlImmunity, Setting: "immunity_ticks"),
                (Role: EffectSemanticRole.GrabLockout, Setting: "grab_lockout_ticks"),
                (Role: EffectSemanticRole.WakeupImmunity, Setting: "wakeup_immunity_ticks"),
                (Role: EffectSemanticRole.GuardBreak, Setting: "") })
            {
                var entries = catalog.Where(x => x.Role == binding.Role).ToArray();
                if (entries.Length != 1)
                {
                    Add("InvalidEffectReference", "/config/effects/semantic_role/" + binding.Role, null);
                    continue;
                }
                var effect = entries[0];
                if (binding.Setting.Length > 0 && effect.Duration != Setting("global.control." + binding.Setting, 1, int.MaxValue))
                    Add("InvalidControlDuration", "/config/effects/" + effect.Entity.Id + "/duration_ticks", effect.Entity.Id.Value);
                if (binding.Role == EffectSemanticRole.ControlFatigue && (threshold > effect.StackCap || effect.Lookup.Count != effect.StackCap + 1 || scale < 1))
                    Add("InvalidEffectLookup", "/config/effects/" + effect.Entity.Id + "/lookup_profile", effect.Entity.Id.Value);
            }
        }

        private ActionInterruptProfile? ReadInterrupt(CompiledConfigEntity entity)
        {
            var before = Issues.Count;
            var path = "/config/actions/" + entity.Id;
            var kind = EnumValue<ActionInterruptKind>(entity, "interrupt_profile", "InvalidInterruptProfile", path);
            var incoming = Integer(entity, "hit_interrupt_strength", 0, 3, "InvalidInterruptProfile", path);
            var threshold = Integer(entity, "hit_interrupt_min_strength", 1, 3, "InvalidInterruptProfile", path);
            var phases = Tokens<ActionPhase>(entity, "hit_interruptible_phases", path);
            var protectedPhases = Tokens<ActionPhase>(entity, "protected_phases", path);
            var ignored = Tokens<ControlCategory>(entity, "ignored_control_categories", path);
            if (ignored.Any(x => x is ControlCategory.Grab or ControlCategory.Defeat) || kind != ActionInterruptKind.Unstoppable && ignored.Length != 0)
                Add("InvalidInterruptProfile", path + "/ignored_control_categories", entity.Id.Value);
            return Issues.Count == before ? new ActionInterruptProfile(entity.Id, kind!.Value, (HitInterruptStrength)incoming,
                (HitInterruptStrength)threshold, phases, protectedPhases, ignored) : null;
        }

        private T[] Tokens<T>(CompiledConfigEntity entity, string key, string path) where T : struct, Enum
        {
            var text = Text(entity, key, "InvalidInterruptProfile", path, allowEmpty: true);
            if (text.Length == 0) return Array.Empty<T>();
            var names = text.Split('|');
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(x => !Enum.GetNames(typeof(T)).Contains(x, StringComparer.Ordinal)))
            {
                Add("InvalidInterruptProfile", path + "/" + key, entity.Id.Value);
                return Array.Empty<T>();
            }
            return names.Select(Enum.Parse<T>).OrderBy(x => x).ToArray();
        }

        private T? EnumValue<T>(CompiledConfigEntity entity, string key, string code, string path, bool optional = false) where T : struct, Enum
        {
            var text = Text(entity, key, code, path, optional);
            if (optional && text.Length == 0) return null;
            if (Enum.GetNames(typeof(T)).Contains(text, StringComparer.Ordinal)) return Enum.Parse<T>(text);
            Add(code, path + "/" + key, entity.Id.Value);
            return null;
        }
        private StableId Id(CompiledConfigEntity entity, string key, string code, string path)
        {
            var text = Text(entity, key, code, path);
            if (StableId.TryParse(text, out var id)) return id;
            Add(code, path + "/" + key, entity.Id.Value);
            return default;
        }
        private int Setting(string key, int min, int max, string missing = "MissingEffectSetting", string invalid = "InvalidEffectSetting")
        {
            var path = "/config/settings/" + key;
            if (!config.TryGetSetting(key, out var value)) { Add(missing, path, null); return 0; }
            if (value.Kind != ConfigValueKind.Integer || value.AsInteger() < min || value.AsInteger() > max)
            { Add(invalid, path, null); return 0; }
            return checked((int)value.AsInteger());
        }
        private void SettingText(string key, string expected, string code)
        {
            if (!config.TryGetSetting(key, out var value) || value.Kind != ConfigValueKind.String || value.AsString() != expected)
                Add(code, "/config/settings/" + key, null);
        }
        private int Integer(CompiledConfigEntity entity, string key, int min, int max, string code, string path)
        {
            if (!entity.TryGetProperty(key, out var value) || value.Kind != ConfigValueKind.Integer || value.AsInteger() < min || value.AsInteger() > max)
            { Add(code, path + "/" + key, entity.Id.Value); return 0; }
            return checked((int)value.AsInteger());
        }
        private string Text(CompiledConfigEntity entity, string key, string code, string path, bool optional = false, bool allowEmpty = false)
        {
            if (!entity.TryGetProperty(key, out var value))
            {
                if (!optional) Add(code, path + "/" + key, entity.Id.Value);
                return string.Empty;
            }
            if (value.Kind == ConfigValueKind.String && (allowEmpty || optional || value.AsString().Length > 0)) return value.AsString();
            Add(code, path + "/" + key, entity.Id.Value);
            return string.Empty;
        }
        private void Add(string code, string path, string? entity) => Issues.Add(new EffectSetupIssue(code, path, entity));
    }
}
