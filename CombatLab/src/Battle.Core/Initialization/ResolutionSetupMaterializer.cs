using System.Globalization;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Core.Resolution;

namespace Battle.Core.Initialization;

internal readonly record struct ResolutionSetupIssue(string Code, string Path, string? Entity);

internal static class ResolutionSetupMaterializer
{
    internal static ResolutionRuntimeSettings? TryCreate(
        CompiledBattleConfig config,
        int timeLimitTicks,
        ICollection<ResolutionSetupIssue> issues)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (issues is null)
        {
            throw new ArgumentNullException(nameof(issues));
        }

        if (timeLimitTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeLimitTicks));
        }

        var global = ReadGlobal();
        var actions = new List<ResolutionActionProfile>(config.Actions.Count);
        foreach (var entity in config.Actions.OrderBy(item => item.Id))
        {
            var action = ReadAction(entity, global?.FixedPointScale);
            if (action is not null)
            {
                actions.Add(action);
            }
        }

        if (timeLimitTicks > 999_999)
        {
            Add("ResolutionCounterOverflowRisk", "$.settings[battle.time_limit_ticks]", null);
        }

        if (global is null || issues.Count != 0)
        {
            return null;
        }

        try
        {
            return new ResolutionRuntimeSettings(global, actions);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            Add("InvalidResolutionConfig", "$.resolution", null);
            return null;
        }

        ResolutionGlobalSettings? ReadGlobal()
        {
            var fixedPointScale = Setting("global.sim.fp_scale", 1, int.MaxValue);
            var armorK = Setting("global.damage.armor_k", 1, int.MaxValue);
            var damageFloor = Setting("global.damage.floor", 0, int.MaxValue);
            var damageCap = Setting("global.damage.cap", 0, int.MaxValue);
            var blockSlope = Setting("global.damage.block_slope", 0, int.MaxValue);
            var blockMinimum = Setting("global.damage.block_min", 0, int.MaxValue);
            var blockMaximum = Setting("global.damage.block_max", 0, int.MaxValue);
            var dodgeSlope = Setting("global.damage.dodge_slope", 0, int.MaxValue);
            var dodgeMinimum = Setting("global.damage.dodge_min", 0, int.MaxValue);
            var dodgeMaximum = Setting("global.damage.dodge_max", 0, int.MaxValue);
            var controlK = Setting("global.control.control_k", 1, int.MaxValue);
            var forceK = Setting("global.control.force_k", 1, int.MaxValue);
            var stunMinimum = Setting("global.control.stun_min_ticks", 1, int.MaxValue);
            var stunMaximum = Setting("global.control.stun_max_ticks", 1, int.MaxValue);
            var maximumHold = Setting("global.control.max_hold_ticks", 1, int.MaxValue);
            var grabLockout = Setting("global.control.grab_lockout_ticks", 0, int.MaxValue);
            var values = new int?[]
            {
                fixedPointScale, armorK, damageFloor, damageCap, blockSlope, blockMinimum,
                blockMaximum, dodgeSlope, dodgeMinimum, dodgeMaximum, controlK, forceK,
                stunMinimum, stunMaximum, maximumHold, grabLockout,
            };
            if (values.Any(value => !value.HasValue))
            {
                return null;
            }

            try
            {
                var result = new ResolutionGlobalSettings(
                    fixedPointScale!.Value,
                    armorK!.Value,
                    damageFloor!.Value,
                    damageCap!.Value,
                    blockSlope!.Value,
                    blockMinimum!.Value,
                    blockMaximum!.Value,
                    dodgeSlope!.Value,
                    dodgeMinimum!.Value,
                    dodgeMaximum!.Value,
                    controlK!.Value,
                    forceK!.Value,
                    stunMinimum!.Value,
                    stunMaximum!.Value,
                    maximumHold!.Value,
                    grabLockout!.Value);
                result.Validate();
                return result;
            }
            catch (ArgumentException)
            {
                Add("InvalidResolutionConfigRange", "$.settings", null);
                return null;
            }
        }

        ResolutionActionProfile? ReadAction(CompiledConfigEntity entity, int? fixedPointScale)
        {
            var path = "$.actions[" + entity.Id.Value + "]";
            var slotType = Text(entity, "slot_type", path);
            var category = Text(entity, "category", path);
            var movementText = Text(entity, "movement_mode", path);
            var tagsText = Text(entity, "tags", path);
            var scheduleText = Text(entity, "hit_schedule", path);
            var interruptProfile = Text(entity, "interrupt_profile", path);
            var activeTicks = Integer(entity, "active_ticks", 1, int.MaxValue, path);
            var startupMaximum = Integer(entity, "startup_max_ticks", 0, int.MaxValue, path);
            var hitCount = Integer(entity, "hit_count", 0, 32, path);
            var actionPriority = Integer(entity, "action_priority", 0, int.MaxValue, path);
            var resolutionPriority = Integer(entity, "resolution_priority", 0, int.MaxValue, path);
            var clashPriority = Integer(entity, "clash_priority", 0, int.MaxValue, path);
            var grabPriority = Integer(entity, "grab_priority", 0, int.MaxValue, path);
            var hitRangeMinimum = Integer(entity, "hit_range_min", 0, int.MaxValue, path);
            var hitRangeMaximum = Integer(entity, "hit_range_max", 0, int.MaxValue, path);
            var baseDamage = Integer(entity, "base_damage", 0, int.MaxValue, path);
            var powerRatio = Integer(entity, "power_ratio_fp", 0, int.MaxValue, path);
            var minimumDamage = Integer(entity, "min_damage", 0, int.MaxValue, path);
            var blockable = Boolean(entity, "blockable", path);
            var dodgeable = Boolean(entity, "dodgeable", path);
            var undodgeable = Boolean(entity, "undodgeable", path);
            var blockBase = Integer(entity, "block_base_chance_fp", 0, int.MaxValue, path);
            var blockReduction = Integer(entity, "block_reduction_fp", 0, int.MaxValue, path);
            var dodgeBase = Integer(entity, "dodge_base_chance_fp", 0, int.MaxValue, path);
            var chipMinimum = Integer(entity, "chip_min", 0, int.MaxValue, path);
            var baseStagger = Integer(entity, "base_stagger", 0, int.MaxValue, path);
            var baseStun = Integer(entity, "base_stun_ticks", 0, int.MaxValue, path);
            var baseKnockback = Integer(entity, "base_knockback", 0, int.MaxValue, path);
            var knockbackMinimum = Integer(entity, "knockback_min", 0, int.MaxValue, path);
            var knockbackMaximum = Integer(entity, "knockback_max", 0, int.MaxValue, path);
            var moveDistance = Integer(entity, "move_distance", 0, int.MaxValue, path);
            var trackTarget = Boolean(entity, "track_target", path);
            var wallImpact = Boolean(entity, "wall_impact", path);
            var wallDamagePerUnit = Integer(entity, "wall_damage_per_unit_fp", 0, int.MaxValue, path);
            var wallDamageMinimum = Integer(entity, "wall_damage_min", 0, int.MaxValue, path);
            var wallDamageMaximum = Integer(entity, "wall_damage_max", 0, int.MaxValue, path);

            var required = new object?[]
            {
                slotType, category, movementText, tagsText, scheduleText, interruptProfile,
                activeTicks, startupMaximum, hitCount, actionPriority, resolutionPriority,
                clashPriority, grabPriority, hitRangeMinimum, hitRangeMaximum, baseDamage,
                powerRatio, minimumDamage, blockable, dodgeable, undodgeable, blockBase,
                blockReduction, dodgeBase, chipMinimum, baseStagger, baseStun, baseKnockback,
                knockbackMinimum, knockbackMaximum, moveDistance, trackTarget, wallImpact,
                wallDamagePerUnit, wallDamageMinimum, wallDamageMaximum,
            };
            if (required.Any(value => value is null) || !TryMovement(movementText!, out var movement))
            {
                if (movementText is not null && !TryMovement(movementText, out _))
                {
                    Add("InvalidResolutionMovementMode", path + ".movement_mode", entity.Id.Value);
                }

                return null;
            }

            var tags = ParseTags(tagsText!, path, entity.Id.Value);
            var schedule = ParseSchedule(scheduleText!, activeTicks!.Value, path, entity.Id.Value);
            if (tags is null || schedule is null)
            {
                return null;
            }

            ValidateConsistency(
                entity.Id,
                slotType!,
                tags,
                schedule,
                hitCount!.Value,
                fixedPointScale!.Value,
                baseDamage!.Value,
                powerRatio!.Value,
                minimumDamage!.Value,
                blockBase!.Value,
                blockReduction!.Value,
                dodgeBase!.Value,
                baseStagger!.Value,
                baseStun!.Value,
                baseKnockback!.Value,
                knockbackMinimum!.Value,
                knockbackMaximum!.Value,
                moveDistance!.Value,
                wallImpact!.Value,
                wallDamagePerUnit!.Value,
                wallDamageMinimum!.Value,
                wallDamageMaximum!.Value,
                path);

            var latestRelative = schedule.Count == 0 ? 0 : schedule[^1].RelativeTick;
            var latestAbsolute = checked((long)timeLimitTicks - 1L + startupMaximum!.Value + latestRelative);
            if (latestAbsolute > int.MaxValue)
            {
                Add("ResolutionTimingOverflowRisk", path + ".hit_schedule", entity.Id.Value);
            }

            if (issues.Any(issue => StringComparer.Ordinal.Equals(issue.Entity, entity.Id.Value)))
            {
                return null;
            }

            try
            {
                return new ResolutionActionProfile(
                    entity.Id,
                    slotType!,
                    category!,
                    movement,
                    tags,
                    schedule,
                    activeTicks.Value,
                    hitCount.Value,
                    actionPriority!.Value,
                    resolutionPriority!.Value,
                    clashPriority!.Value,
                    grabPriority!.Value,
                    hitRangeMinimum!.Value,
                    hitRangeMaximum!.Value,
                    baseDamage.Value,
                    powerRatio.Value,
                    minimumDamage.Value,
                    blockable!.Value,
                    dodgeable!.Value,
                    undodgeable!.Value,
                    blockBase.Value,
                    blockReduction.Value,
                    dodgeBase.Value,
                    chipMinimum!.Value,
                    baseStagger.Value,
                    baseStun.Value,
                    baseKnockback.Value,
                    knockbackMinimum.Value,
                    knockbackMaximum.Value,
                    moveDistance.Value,
                    trackTarget!.Value,
                    wallImpact.Value,
                    wallDamagePerUnit.Value,
                    wallDamageMinimum.Value,
                    wallDamageMaximum.Value,
                    interruptProfile!);
            }
            catch (ArgumentException)
            {
                Add("InvalidResolutionProfile", path, entity.Id.Value);
                return null;
            }
        }

        void ValidateConsistency(
            StableId actionId,
            string slotType,
            IReadOnlyList<StableId> tags,
            IReadOnlyList<HitScheduleEntry> schedule,
            int hitCount,
            int fixedPointScale,
            int baseDamage,
            int powerRatio,
            int minimumDamage,
            int blockBase,
            int blockReduction,
            int dodgeBase,
            int baseStagger,
            int baseStun,
            int baseKnockback,
            int knockbackMinimum,
            int knockbackMaximum,
            int moveDistance,
            bool wallImpact,
            int wallDamagePerUnit,
            int wallDamageMinimum,
            int wallDamageMaximum,
            string path)
        {
            var tagValues = tags.Select(tag => tag.Value).ToHashSet(StringComparer.Ordinal);
            var damageEntries = schedule.Count(entry => entry.IsDamageCapable);
            var hasCounter = tagValues.Contains("counter");
            var hasGrab = tagValues.Contains("grab");
            var hasBlock = tagValues.Contains("block");
            var hasDodge = tagValues.Contains("dodge");
            var hasWall = tagValues.Contains("wall_impact");
            var valid = hitCount == damageEntries &&
                        blockBase <= fixedPointScale && blockReduction <= fixedPointScale &&
                        dodgeBase <= fixedPointScale && knockbackMaximum >= knockbackMinimum &&
                        wallDamageMaximum >= wallDamageMinimum;

            valid &= !hasCounter ||
                     schedule.Count == 1 && schedule[0].Kind == HitPrimitiveKind.Counter && hitCount == 1;
            valid &= !schedule.Any(entry => entry.Kind == HitPrimitiveKind.Counter) || hasCounter;
            valid &= !hasGrab || schedule.Any(entry => entry.Kind == HitPrimitiveKind.Grab);
            valid &= !schedule.Any(entry => entry.Kind is HitPrimitiveKind.Grab or HitPrimitiveKind.Throw or HitPrimitiveKind.Wall) || hasGrab;
            var firstGrab = schedule.ToList().FindIndex(entry => entry.Kind == HitPrimitiveKind.Grab);
            valid &= schedule
                .Where(entry => entry.Kind is HitPrimitiveKind.Throw or HitPrimitiveKind.Wall)
                .All(entry => firstGrab >= 0 && entry.Ordinal > firstGrab);
            valid &= !hasBlock || blockBase > 0 && blockReduction > 0 && damageEntries == 0;
            valid &= !hasDodge || dodgeBase > 0 && damageEntries == 0;
            valid &= wallImpact == hasWall;
            valid &= wallImpact
                ? wallDamagePerUnit > 0 && wallDamageMaximum >= wallDamageMinimum
                : wallDamagePerUnit == 0 && wallDamageMinimum == 0 && wallDamageMaximum == 0;

            if (slotType == "System")
            {
                valid &= schedule.Count == 0 && hitCount == 0 && baseDamage == 0 &&
                         powerRatio == 0 && minimumDamage == 0 && blockBase == 0 &&
                         blockReduction == 0 && dodgeBase == 0 && baseStagger == 0 &&
                         baseStun == 0 && baseKnockback == 0 && knockbackMinimum == 0 &&
                         knockbackMaximum == 0 && moveDistance == 0 && !wallImpact;
            }

            if (damageEntries > 0)
            {
                valid &= baseDamage > 0 || powerRatio > 0 || minimumDamage > 0;
            }

            if (!valid)
            {
                Add("InvalidResolutionProfile", path, actionId.Value);
            }
        }

        IReadOnlyList<StableId>? ParseTags(string text, string path, string entity)
        {
            var parts = text.Length == 0 ? Array.Empty<string>() : text.Split('|');
            var tags = new List<StableId>(parts.Length);
            foreach (var part in parts)
            {
                if (!StableId.TryParse(part, out var tag) || tags.Contains(tag))
                {
                    Add("InvalidResolutionTags", path + ".tags", entity);
                    return null;
                }

                tags.Add(tag);
            }

            return tags.OrderBy(tag => tag).ToArray();
        }

        IReadOnlyList<HitScheduleEntry>? ParseSchedule(
            string text,
            int activeTicks,
            string path,
            string entity)
        {
            var parts = text.Length == 0 ? Array.Empty<string>() : text.Split('|');
            if (parts.Length > ResolutionActionProfile.MaximumScheduleEntries)
            {
                Add("InvalidHitSchedule", path + ".hit_schedule", entity);
                return null;
            }

            var entries = new List<HitScheduleEntry>(parts.Length);
            for (var ordinal = 0; ordinal < parts.Length; ordinal++)
            {
                var part = parts[ordinal];
                var separator = part.IndexOf(':');
                var prefix = separator < 0 ? null : part[..separator];
                var tickText = separator < 0 ? part : part[(separator + 1)..];
                if (part.Length == 0 || tickText.Length == 0 ||
                    separator != part.LastIndexOf(':') ||
                    !TryKind(prefix, out var kind) ||
                    !int.TryParse(tickText, NumberStyles.None, CultureInfo.InvariantCulture, out var tick) ||
                    tick < 0 || tick >= activeTicks ||
                    (entries.Count > 0 && entries[^1].RelativeTick >= tick))
                {
                    Add("InvalidHitSchedule", path + ".hit_schedule", entity);
                    return null;
                }

                entries.Add(new HitScheduleEntry(kind, tick, ordinal));
            }

            return entries;
        }

        int? Setting(string name, int minimum, int maximum)
        {
            var path = "$.settings[" + name + "]";
            if (!config.TryGetSetting(name, out var value))
            {
                Add("MissingRequiredConfigKey", path, null);
                return null;
            }

            if (value.Kind != ConfigValueKind.Integer)
            {
                Add("InvalidConfigValueType", path, null);
                return null;
            }

            var integer = value.AsInteger();
            if (integer < minimum || integer > maximum)
            {
                Add("InvalidConfigRange", path, null);
                return null;
            }

            return (int)integer;
        }

        string? Text(CompiledConfigEntity entity, string name, string path)
        {
            if (!entity.TryGetProperty(name, out var value))
            {
                Add("MissingRequiredConfigKey", path + "." + name, entity.Id.Value);
                return null;
            }

            if (value.Kind != ConfigValueKind.String)
            {
                Add("InvalidConfigValueType", path + "." + name, entity.Id.Value);
                return null;
            }

            return value.AsString();
        }

        int? Integer(CompiledConfigEntity entity, string name, int minimum, int maximum, string path)
        {
            if (!entity.TryGetProperty(name, out var value))
            {
                Add("MissingRequiredConfigKey", path + "." + name, entity.Id.Value);
                return null;
            }

            if (value.Kind != ConfigValueKind.Integer)
            {
                Add("InvalidConfigValueType", path + "." + name, entity.Id.Value);
                return null;
            }

            var integer = value.AsInteger();
            if (integer < minimum || integer > maximum)
            {
                Add("InvalidConfigRange", path + "." + name, entity.Id.Value);
                return null;
            }

            return (int)integer;
        }

        bool? Boolean(CompiledConfigEntity entity, string name, string path)
        {
            if (!entity.TryGetProperty(name, out var value))
            {
                Add("MissingRequiredConfigKey", path + "." + name, entity.Id.Value);
                return null;
            }

            if (value.Kind != ConfigValueKind.Boolean)
            {
                Add("InvalidConfigValueType", path + "." + name, entity.Id.Value);
                return null;
            }

            return value.AsBoolean();
        }

        void Add(string code, string path, string? entity) =>
            issues.Add(new ResolutionSetupIssue(code, path, entity));
    }

    private static bool TryKind(string? prefix, out HitPrimitiveKind kind)
    {
        kind = prefix switch
        {
            null => HitPrimitiveKind.Hit,
            "counter" => HitPrimitiveKind.Counter,
            "grab" => HitPrimitiveKind.Grab,
            "throw" => HitPrimitiveKind.Throw,
            "wall" => HitPrimitiveKind.Wall,
            _ => default,
        };
        return prefix is null or "counter" or "grab" or "throw" or "wall";
    }

    private static bool TryMovement(string value, out ResolutionMovementMode movement)
    {
        movement = value switch
        {
            "None" => ResolutionMovementMode.None,
            "Approach" => ResolutionMovementMode.Approach,
            "Retreat" => ResolutionMovementMode.Retreat,
            "Adaptive" => ResolutionMovementMode.Adaptive,
            "Follow" => ResolutionMovementMode.Follow,
            "Push" => ResolutionMovementMode.Push,
            "Pull" => ResolutionMovementMode.Pull,
            "Swap" => ResolutionMovementMode.Swap,
            _ => default,
        };
        return value is "None" or "Approach" or "Retreat" or "Adaptive" or "Follow" or "Push" or "Pull" or "Swap";
    }
}
