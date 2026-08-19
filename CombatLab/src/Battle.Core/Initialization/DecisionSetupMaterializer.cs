using System.Globalization;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Replay;
using Battle.Core.Decisions;

namespace Battle.Core.Initialization;

internal readonly record struct DecisionSetupIssue(string Code, string Path, string? Entity);

internal static class DecisionSetupMaterializer
{
    internal static DecisionRuntimeSettings? TryCreate(
        BattleRequest request,
        CompiledBattleConfig config,
        int timeLimitTicks,
        ArenaSnapshot arena,
        SystemActionDefinition approach,
        SystemActionDefinition retreat,
        ICollection<DecisionSetupIssue> issues)
    {
        if (request is null || config is null || arena is null || approach is null || retreat is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (issues is null)
        {
            throw new ArgumentNullException(nameof(issues));
        }

        if (timeLimitTicks < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeLimitTicks));
        }

        var fixedPointScale = Setting("global.sim.fp_scale", 1, int.MaxValue);
        var multiplierMinimum = Setting("global.sim.multiplier_min", 0, int.MaxValue);
        var multiplierMaximum = Setting("global.sim.multiplier_max", 1, int.MaxValue);
        var decisionWeightMaximum = Setting("global.sim.decision_weight_max", 1, int.MaxValue);
        var repeatSameAction = Setting("global.ai.repeat_same_action_fp", 0, int.MaxValue);
        var repeatSameCategory = Setting("global.ai.repeat_same_category_fp", 0, int.MaxValue);
        var opportunityGrowth = Setting("global.ai.opportunity_growth_fp", 0, int.MaxValue);
        var opportunityCap = Setting("global.ai.opportunity_cap_fp", 1, int.MaxValue);
        var hardOpportunityMisses = Setting("global.ai.hard_opportunity_misses", 0, int.MaxValue);
        _ = Setting("global.ai.default_perception_delay_ticks", 0, int.MaxValue);
        var wallZoneSize = Setting("global.arena.wall_zone_size", 0, int.MaxValue);
        var speedBaseline = Setting("global.damage.speed_baseline", 0, int.MaxValue);
        var speedSlope = Setting("global.damage.speed_slope", 0, int.MaxValue);
        var speedMinimum = Setting("global.damage.speed_min", 1, int.MaxValue);
        var speedMaximum = Setting("global.damage.speed_max", 1, int.MaxValue);

        var actions = new List<DecisionActionProfile>(config.Actions.Count);
        foreach (var entity in config.Actions.OrderBy(item => item.Id))
        {
            var profile = ReadAction(entity);
            if (profile is not null)
            {
                actions.Add(profile);
            }
        }

        foreach (var action in actions.Where(action =>
                     action.Slot == DecisionActionSlot.System &&
                     action.Id != SystemActionSelector.ApproachId &&
                     action.Id != SystemActionSelector.RetreatId &&
                     action.Id != SystemActionSelector.WaitId))
        {
            Add(
                "InvalidSystemAction",
                "$.actions[" + action.Id.Value + "]",
                action.Id.Value);
        }

        var fighterA = ReadFighterProfile(request.BuildA);
        var fighterB = ReadFighterProfile(request.BuildB);
        if (issues.Count != 0 || !fixedPointScale.HasValue || !multiplierMinimum.HasValue ||
            !multiplierMaximum.HasValue || !decisionWeightMaximum.HasValue ||
            !repeatSameAction.HasValue || !repeatSameCategory.HasValue ||
            !opportunityGrowth.HasValue || !opportunityCap.HasValue ||
            !hardOpportunityMisses.HasValue || !wallZoneSize.HasValue ||
            !speedBaseline.HasValue || !speedSlope.HasValue || !speedMinimum.HasValue ||
            !speedMaximum.HasValue || fighterA is null || fighterB is null)
        {
            return null;
        }

        if (multiplierMinimum.Value > fixedPointScale.Value ||
            multiplierMaximum.Value < fixedPointScale.Value ||
            speedMaximum.Value < speedMinimum.Value)
        {
            Add("InvalidConfigRange", "/config/settings", null);
            return null;
        }

        var reachableCatalogs = new[] { request.BuildA, request.BuildB }
            .Select(build => new
            {
                Build = build,
                // Exactly one WP-07 System action can be legal for a decision
                // state. The other System entries remain in the checked catalog
                // but must not inflate the legal-set overflow bound.
                Count = 1 + actions.Count(action =>
                    action.Slot != DecisionActionSlot.System &&
                    IsReachableForBuild(action, build)),
            })
            .ToArray();
        foreach (var catalog in reachableCatalogs)
        {
            if (catalog.Count is < 1 or > 128)
            {
                Add("InvalidDecisionCatalog", "/config/actions", catalog.Build.AnimalId.Value);
            }
        }

        foreach (var action in actions.Where(action =>
                     action.HitScheduleTicks.Count != 0 &&
                     (IsReachableForBuild(action, request.BuildA) ||
                      IsReachableForBuild(action, request.BuildB))))
        {
            var latestImpactTick =
                (long)timeLimitTicks - 1L +
                action.StartupMaximumTicks +
                action.HitScheduleTicks[^1];
            if (latestImpactTick > int.MaxValue)
            {
                Add(
                    "DecisionTimingOverflowRisk",
                    "$.actions[" + action.Id.Value + "].hit_schedule",
                    action.Id.Value);
            }
        }

        if (reachableCatalogs.Any(catalog =>
                checked((long)catalog.Count * decisionWeightMaximum.Value) > int.MaxValue))
        {
            Add(
                "DecisionWeightSumOverflowRisk",
                "$.mode_rules.allowed_action_ids",
                null);
        }

        if (issues.Count != 0)
        {
            return null;
        }

        try
        {
            return new DecisionRuntimeSettings(
                request.BattleId,
                request.EngineVersion,
                request.MasterSeed,
                config.Reference.ConfigHash,
                request.ModeRules,
                actions,
                fighterA!,
                fighterB!,
                new DecisionAvailabilitySettings(
                    request.ModeRules.AllowedActionIds,
                    Array.Empty<string>(),
                    arena.MinimumPosition,
                    arena.MaximumPosition,
                    approach.PreferredRangeMaximum,
                    retreat.PreferredRangeMinimum),
                new DecisionWeightSettings(
                    fixedPointScale.Value,
                    multiplierMinimum.Value,
                    multiplierMaximum.Value,
                    decisionWeightMaximum.Value),
                new DecisionTimingSettings(
                    speedBaseline.Value,
                    speedSlope.Value,
                    speedMinimum.Value,
                    speedMaximum.Value),
                repeatSameAction.Value,
                repeatSameCategory.Value,
                opportunityGrowth.Value,
                opportunityCap.Value,
                hardOpportunityMisses.Value,
                wallZoneSize.Value);
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            Add("InvalidDecisionConfig", "/config", null);
            return null;
        }

        int? Setting(string name, int minimum, int maximum)
        {
            var path = "/config/settings/" + name;
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

        bool IsReachableForBuild(DecisionActionProfile action, FighterBuildSnapshot build)
        {
            if (!request.ModeRules.AllowedActionIds.Contains(action.Id))
            {
                return false;
            }

            if (action.Slot == DecisionActionSlot.System)
            {
                return true;
            }

            if (action.OwnerAnimalId != build.AnimalId)
            {
                return false;
            }

            return action.Slot == DecisionActionSlot.Basic ||
                   action.Slot == DecisionActionSlot.Special &&
                   build.SpecialActionIds.Contains(action.Id);
        }

        DecisionActionProfile? ReadAction(CompiledConfigEntity entity)
        {
            var path = "/config/actions/" + entity.Id.Value;
            var ownerText = Text(entity, "animal_id", path);
            var slotText = Text(entity, "slot_type", path);
            var category = Text(entity, "category", path);
            var movementText = Text(entity, "movement_mode", path);
            var tagsText = Text(entity, "tags", path);
            var hitScheduleText = Text(entity, "hit_schedule", path);
            var baseWeight = Integer(entity, "base_weight", 0, int.MaxValue, path);
            var energyCost = Integer(entity, "energy_cost", 0, int.MaxValue, path);
            var resourceCost = Integer(entity, "resource_cost", 0, int.MaxValue, path);
            var cooldown = Integer(entity, "cooldown_ticks", 0, int.MaxValue, path);
            var maximumConsecutive = Integer(entity, "max_consecutive_uses", 1, int.MaxValue, path);
            var hardMisses = Integer(entity, "hard_opportunity_misses", 0, int.MaxValue, path);
            var actionOpportunityCap = Integer(entity, "opportunity_cap_fp", 1, int.MaxValue, path);
            var startupBase = Integer(entity, "startup_base_ticks", 0, int.MaxValue, path);
            var startupMinimum = Integer(entity, "startup_min_ticks", 0, int.MaxValue, path);
            var startupMaximum = Integer(entity, "startup_max_ticks", 0, int.MaxValue, path);
            var active = Integer(entity, "active_ticks", 1, int.MaxValue, path);
            var recoveryBase = Integer(entity, "recovery_base_ticks", 0, int.MaxValue, path);
            var recoveryMinimum = Integer(entity, "recovery_min_ticks", 0, int.MaxValue, path);
            var recoveryMaximum = Integer(entity, "recovery_max_ticks", 0, int.MaxValue, path);
            var preferredMinimum = Integer(entity, "preferred_range_min", 0, int.MaxValue, path);
            var preferredMaximum = Integer(entity, "preferred_range_max", 0, int.MaxValue, path);
            var hitMinimum = Integer(entity, "hit_range_min", 0, int.MaxValue, path);
            var hitMaximum = Integer(entity, "hit_range_max", 0, int.MaxValue, path);
            var hitCount = Integer(entity, "hit_count", 0, 32, path);
            var trackTarget = Boolean(entity, "track_target", path);
            if (new object?[]
                {
                    ownerText, slotText, category, movementText, tagsText, hitScheduleText,
                    baseWeight, energyCost, resourceCost, cooldown, maximumConsecutive, hardMisses,
                    actionOpportunityCap, startupBase, startupMinimum, startupMaximum, active,
                    recoveryBase, recoveryMinimum, recoveryMaximum, preferredMinimum, preferredMaximum,
                    hitMinimum, hitMaximum, hitCount, trackTarget,
                }.Any(value => value is null))
            {
                return null;
            }

            if (!TrySlot(slotText!, out var slot) || !TryMovement(movementText!, out var movement))
            {
                Add("InvalidConfigValue", path, entity.Id.Value);
                return null;
            }

            var schedule = ParseSchedule(hitScheduleText!, hitCount!.Value, active!.Value, path, entity.Id.Value);
            var tags = ParseTags(tagsText!, path, entity.Id.Value);
            if (schedule is null || tags is null)
            {
                return null;
            }

            var target = slot == DecisionActionSlot.System || hitCount.Value > 0 || schedule.Count != 0
                ? DecisionTargetKind.Opponent
                : DecisionTargetKind.Self;
            if (!IsCompatible(slot, target, movement))
            {
                Add("AmbiguousTargetProfile", path + "/movement_mode", entity.Id.Value);
                return null;
            }

            try
            {
                return new DecisionActionProfile(
                    entity.Id,
                    slot == DecisionActionSlot.System ? null : new StableId(ownerText!),
                    slot,
                    category!,
                    movement,
                    target,
                    tags,
                    baseWeight!.Value,
                    energyCost!.Value,
                    resourceCost!.Value,
                    cooldown!.Value,
                    maximumConsecutive!.Value,
                    hardMisses!.Value,
                    actionOpportunityCap!.Value,
                    startupBase!.Value,
                    startupMinimum!.Value,
                    startupMaximum!.Value,
                    active.Value,
                    recoveryBase!.Value,
                    recoveryMinimum!.Value,
                    recoveryMaximum!.Value,
                    preferredMinimum!.Value,
                    preferredMaximum!.Value,
                    hitMinimum!.Value,
                    hitMaximum!.Value,
                    schedule,
                    trackTarget!.Value);
            }
            catch (ArgumentException)
            {
                Add("InvalidDecisionAction", path, entity.Id.Value);
                return null;
            }
        }

        DecisionFighterProfile? ReadFighterProfile(FighterBuildSnapshot build)
        {
            if (!config.TryGetTactic(build.TacticId, out var tacticEntity) || tacticEntity is null ||
                !config.TryGetPassive(build.PassiveId, out var passiveEntity) || passiveEntity is null ||
                !config.TryGetGear(build.Gear.Offense, out var offenseEntity) || offenseEntity is null ||
                !config.TryGetGear(build.Gear.Defense, out var defenseEntity) || defenseEntity is null ||
                !config.TryGetGear(build.Gear.Utility, out var utilityEntity) || utilityEntity is null)
            {
                Add("MissingCatalogEntry", "/fighters/" + build.FighterId, build.AnimalId.Value);
                return null;
            }

            var tactic = ReadTactic(tacticEntity);
            var passive = ReadTagMultiplier(passiveEntity, "weight_multiplier_fp", "/config/passives/");
            var offense = ReadTagMultiplier(offenseEntity, "normalized_value", "/config/gear/");
            var defense = ReadTagMultiplier(defenseEntity, "normalized_value", "/config/gear/");
            var utility = ReadTagMultiplier(utilityEntity, "normalized_value", "/config/gear/");
            if (tactic is null || passive is null || offense is null || defense is null || utility is null)
            {
                return null;
            }

            int? lowHealth = null;
            var lowHealthKey = "fighter." + build.AnimalId.Value + ".low_health_threshold_fp";
            if (config.TryGetSetting(lowHealthKey, out var lowHealthValue))
            {
                if (lowHealthValue.Kind != ConfigValueKind.Integer || lowHealthValue.AsInteger() is < 0 or > int.MaxValue)
                {
                    Add("InvalidConfigRange", "/config/settings/" + lowHealthKey, build.AnimalId.Value);
                    return null;
                }

                lowHealth = (int)lowHealthValue.AsInteger();
            }

            return new DecisionFighterProfile(
                build,
                new DecisionBuildView(
                    build.AnimalId,
                    build.SpecialActionIds,
                    build.PassiveId,
                    build.Gear.Offense,
                    build.Gear.Defense,
                    build.Gear.Utility,
                    build.TacticId),
                tactic,
                passive,
                offense,
                defense,
                utility,
                lowHealth);
        }

        DecisionTacticProfile? ReadTactic(CompiledConfigEntity entity)
        {
            var path = "/config/tactics/" + entity.Id.Value;
            var names = new[]
            {
                "approach_fp", "block_fp", "dodge_fp", "grab_fp", "heavy_fp", "light_fp",
                "resource_generator_fp", "resource_spender_fp", "retreat_fp", "signature_fp",
                "counter_fp", "low_hpfp", "self_wall_fp", "target_wall_fp", "target_recovery_fp",
                "repeat_penalty_fp", "perception_delay_ticks",
            };
            var values = names.Select(name => Integer(entity, name, 0, int.MaxValue, path)).ToArray();
            return values.Any(value => !value.HasValue)
                ? null
                : new DecisionTacticProfile(
                    values[0]!.Value, values[1]!.Value, values[2]!.Value, values[3]!.Value,
                    values[4]!.Value, values[5]!.Value, values[6]!.Value, values[7]!.Value,
                    values[8]!.Value, values[9]!.Value, values[10]!.Value, values[11]!.Value,
                    values[12]!.Value, values[13]!.Value, values[14]!.Value, values[15]!.Value,
                    values[16]!.Value);
        }

        DecisionTagMultiplierProfile? ReadTagMultiplier(
            CompiledConfigEntity entity,
            string multiplierName,
            string pathPrefix)
        {
            var path = pathPrefix + entity.Id.Value;
            var tagsText = Text(entity, "tags", path);
            var multiplier = Integer(entity, multiplierName, 0, int.MaxValue, path);
            var tags = tagsText is null ? null : ParseTags(tagsText, path, entity.Id.Value);
            return tags is null || !multiplier.HasValue
                ? null
                : new DecisionTagMultiplierProfile(tags, multiplier.Value);
        }

        string? Text(CompiledConfigEntity entity, string name, string path)
        {
            if (!entity.TryGetProperty(name, out var value))
            {
                Add("MissingRequiredConfigKey", path + "/" + name, entity.Id.Value);
                return null;
            }

            if (value.Kind != ConfigValueKind.String)
            {
                Add("InvalidConfigValueType", path + "/" + name, entity.Id.Value);
                return null;
            }

            return value.AsString();
        }

        int? Integer(CompiledConfigEntity entity, string name, int minimum, int maximum, string path)
        {
            if (!entity.TryGetProperty(name, out var value))
            {
                Add("MissingRequiredConfigKey", path + "/" + name, entity.Id.Value);
                return null;
            }

            if (value.Kind != ConfigValueKind.Integer)
            {
                Add("InvalidConfigValueType", path + "/" + name, entity.Id.Value);
                return null;
            }

            var integer = value.AsInteger();
            if (integer < minimum || integer > maximum)
            {
                Add("InvalidConfigRange", path + "/" + name, entity.Id.Value);
                return null;
            }

            return (int)integer;
        }

        bool? Boolean(CompiledConfigEntity entity, string name, string path)
        {
            if (!entity.TryGetProperty(name, out var value))
            {
                Add("MissingRequiredConfigKey", path + "/" + name, entity.Id.Value);
                return null;
            }

            if (value.Kind != ConfigValueKind.Boolean)
            {
                Add("InvalidConfigValueType", path + "/" + name, entity.Id.Value);
                return null;
            }

            return value.AsBoolean();
        }

        IReadOnlyList<StableId>? ParseTags(string text, string path, string entity)
        {
            var values = text.Length == 0
                ? Array.Empty<string>()
                : text.Split('|');
            var tags = new List<StableId>(values.Length);
            foreach (var value in values)
            {
                if (!StableId.TryParse(value, out var tag) || tags.Contains(tag))
                {
                    Add("InvalidTagSet", path + "/tags", entity);
                    return null;
                }

                tags.Add(tag);
            }

            return tags.OrderBy(tag => tag).ToArray();
        }

        IReadOnlyList<int>? ParseSchedule(string text, int hitCount, int activeTicks, string path, string entity)
        {
            var parts = text.Length == 0 ? Array.Empty<string>() : text.Split('|');
            if (parts.Length > DecisionActionProfile.MaximumHitScheduleEntries)
            {
                Add("InvalidHitSchedule", path + "/hit_schedule", entity);
                return null;
            }

            var ticks = new List<int>(parts.Length);
            var scheduledHits = 0;
            foreach (var part in parts)
            {
                var separator = part.IndexOf(':');
                var tickText = separator < 0 ? part : part[(separator + 1)..];
                var primitive = separator < 0 ? null : part[..separator];
                if ((separator >= 0 &&
                     (separator == 0 || separator != part.LastIndexOf(':') ||
                      primitive is not ("counter" or "grab" or "throw" or "wall"))) ||
                    !int.TryParse(tickText, NumberStyles.None, CultureInfo.InvariantCulture, out var tick) ||
                    tick < 0 || tick >= activeTicks || (ticks.Count != 0 && ticks[^1] >= tick))
                {
                    Add("InvalidHitSchedule", path + "/hit_schedule", entity);
                    return null;
                }

                ticks.Add(tick);
                if (primitive != "grab")
                {
                    scheduledHits++;
                }
            }

            if (scheduledHits != hitCount)
            {
                Add("InvalidHitSchedule", path + "/hit_schedule", entity);
                return null;
            }

            return ticks;
        }

        void Add(string code, string path, string? entity) =>
            issues.Add(new DecisionSetupIssue(code, path, entity));
    }

    private static bool TrySlot(string value, out DecisionActionSlot slot)
    {
        slot = value switch
        {
            "System" => DecisionActionSlot.System,
            "Basic" => DecisionActionSlot.Basic,
            "Special" => DecisionActionSlot.Special,
            _ => default,
        };
        return value is "System" or "Basic" or "Special";
    }

    private static bool TryMovement(string value, out DecisionMovementMode movement)
    {
        movement = value switch
        {
            "None" => DecisionMovementMode.None,
            "Approach" => DecisionMovementMode.Approach,
            "Retreat" => DecisionMovementMode.Retreat,
            "Adaptive" => DecisionMovementMode.Adaptive,
            "Follow" => DecisionMovementMode.Follow,
            "Push" => DecisionMovementMode.Push,
            "Pull" => DecisionMovementMode.Pull,
            "Swap" => DecisionMovementMode.Swap,
            _ => default,
        };
        return value is "None" or "Approach" or "Retreat" or "Adaptive" or "Follow" or "Push" or "Pull" or "Swap";
    }

    private static bool IsCompatible(
        DecisionActionSlot slot,
        DecisionTargetKind target,
        DecisionMovementMode movement) => slot == DecisionActionSlot.System
        ? movement is DecisionMovementMode.None or DecisionMovementMode.Approach or DecisionMovementMode.Retreat
        : target switch
    {
        DecisionTargetKind.Opponent => movement is DecisionMovementMode.None or DecisionMovementMode.Approach or
            DecisionMovementMode.Follow or DecisionMovementMode.Push or DecisionMovementMode.Pull or DecisionMovementMode.Swap,
        DecisionTargetKind.Self => movement is DecisionMovementMode.None or DecisionMovementMode.Approach or
            DecisionMovementMode.Retreat or DecisionMovementMode.Adaptive,
        _ => false,
    };
}
