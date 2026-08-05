using System.Collections.ObjectModel;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace CombatLab.Runner.Battles;

public sealed class BattleCaseFactoryResult
{
    private readonly ReadOnlyCollection<BattleRejectionError> _errors;

    internal BattleCaseFactoryResult(
        BattleRequest? request,
        IEnumerable<BattleRejectionError> errors)
    {
        Request = request;
        _errors = new ReadOnlyCollection<BattleRejectionError>(errors.ToList());
    }

    public bool IsSuccess => Request is not null;

    public BattleRequest? Request { get; }

    public IReadOnlyList<BattleRejectionError> Errors => _errors;
}

public static class BattleCaseFactory
{
    private static readonly ReasonCode MissingRequiredValue = new("MissingRequiredValue");
    private static readonly ReasonCode InvalidIdentifier = new("InvalidIdentifier");
    private static readonly ReasonCode InvalidEnumValue = new("InvalidEnumValue");
    private static readonly ReasonCode InvalidItemCount = new("InvalidItemCount");
    private static readonly ReasonCode DuplicateItem = new("DuplicateItem");
    private static readonly ReasonCode SlotMismatch = new("SlotMismatch");
    private static readonly ReasonCode InvalidStructure = new("InvalidStructure");

    private static readonly StableId MissingRequiredMessage = new("raw_request_missing_required");
    private static readonly StableId InvalidIdentifierMessage = new("raw_request_invalid_identifier");
    private static readonly StableId InvalidEnumMessage = new("raw_request_invalid_enum");
    private static readonly StableId InvalidItemCountMessage = new("raw_request_invalid_item_count");
    private static readonly StableId DuplicateItemMessage = new("raw_request_duplicate_item");
    private static readonly StableId SlotMismatchMessage = new("raw_request_slot_mismatch");
    private static readonly StableId InvalidStructureMessage = new("raw_request_invalid_structure");

    public static BattleCaseFactoryResult TryCreate(RawBattleRequest? raw)
    {
        var errors = new List<BattleRejectionError>();
        if (raw is null)
        {
            Add(errors, MissingRequiredValue, "$", MissingRequiredMessage);
            return Failure(errors);
        }

        var battleId = ParseExternalId(raw.BattleId, "$.battle_id", errors);
        var engineVersion = ParseArtifactVersion(raw.EngineVersion, "$.engine_version", errors);
        var configHash = ParseDigest(raw.ConfigHash, "$.config_hash", errors);
        var modeRules = ParseModeRules(raw.ModeRules, errors);
        var buildA = ParseBuild(
            raw.BuildA,
            "$.build_a",
            FighterId.FighterA,
            FighterSide.A,
            errors);
        var buildB = ParseBuild(
            raw.BuildB,
            "$.build_b",
            FighterId.FighterB,
            FighterSide.B,
            errors);

        if (errors.Count > 0)
        {
            return Failure(errors);
        }

        try
        {
            var parsedModeRules = RequireReference(modeRules);
            var strictModeRules = new ModeRulesSnapshot(
                RequireValue(parsedModeRules.Id),
                RequireValue(parsedModeRules.Version),
                RequireValue(parsedModeRules.NormalizationMode),
                RequireReference(parsedModeRules.AllowedAnimalIds),
                RequireReference(parsedModeRules.AllowedActionIds),
                RequireReference(parsedModeRules.AllowedPassiveIds),
                RequireReference(parsedModeRules.AllowedGearIds),
                RequireReference(parsedModeRules.AllowedTacticIds));
            var strictBuildA = CreateBuild(RequireReference(buildA));
            var strictBuildB = CreateBuild(RequireReference(buildB));
            var request = new BattleRequest(
                RequireValue(battleId),
                RequireValue(engineVersion),
                RequireValue(configHash),
                strictModeRules,
                raw.MasterSeed,
                strictBuildA,
                strictBuildB);

            return new BattleCaseFactoryResult(request, Array.Empty<BattleRejectionError>());
        }
        catch (ArgumentException)
        {
            Add(errors, InvalidStructure, "$", InvalidStructureMessage);
            return Failure(errors);
        }
    }

    private static ParsedModeRules? ParseModeRules(
        RawModeRules? raw,
        ICollection<BattleRejectionError> errors)
    {
        const string path = "$.mode_rules";
        if (raw is null)
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        var id = ParseStableId(raw.Id, path + ".id", errors);
        var version = ParseArtifactVersion(raw.Version, path + ".version", errors);
        var normalizationMode = ParseNormalizationMode(
            raw.NormalizationMode,
            path + ".normalization_mode",
            errors);
        var animals = ParseStableIdList(raw.AllowedAnimalIds, path + ".allowed_animal_ids", errors, null);
        var actions = ParseStableIdList(raw.AllowedActionIds, path + ".allowed_action_ids", errors, null);
        var passives = ParseStableIdList(raw.AllowedPassiveIds, path + ".allowed_passive_ids", errors, null);
        var gear = ParseStableIdList(raw.AllowedGearIds, path + ".allowed_gear_ids", errors, null);
        var tactics = ParseStableIdList(raw.AllowedTacticIds, path + ".allowed_tactic_ids", errors, null);

        return new ParsedModeRules(
            id,
            version,
            normalizationMode,
            animals,
            actions,
            passives,
            gear,
            tactics);
    }

    private static ParsedFighterBuild? ParseBuild(
        RawFighterBuild? raw,
        string path,
        FighterId expectedFighterId,
        FighterSide expectedSide,
        ICollection<BattleRejectionError> errors)
    {
        if (raw is null)
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        var fighterId = ParseFighterId(raw.FighterId, path + ".fighter_id", errors);
        var side = ParseSide(raw.Side, path + ".side", errors);
        if (fighterId.HasValue && fighterId.Value != expectedFighterId)
        {
            Add(errors, SlotMismatch, path + ".fighter_id", SlotMismatchMessage);
        }

        if (side.HasValue && side.Value != expectedSide)
        {
            Add(errors, SlotMismatch, path + ".side", SlotMismatchMessage);
        }

        var animalId = ParseStableId(raw.AnimalId, path + ".animal_id", errors);
        var buildId = ParseOptionalStableId(raw.BuildId, path + ".build_id", errors);
        var specials = ParseStableIdList(
            raw.SpecialActionIds,
            path + ".special_action_ids",
            errors,
            expectedCount: 2);
        var passiveId = ParseStableId(raw.PassiveId, path + ".passive_id", errors);
        var offenseGearId = ParseStableId(raw.OffenseGearId, path + ".gear.offense", errors);
        var defenseGearId = ParseStableId(raw.DefenseGearId, path + ".gear.defense", errors);
        var utilityGearId = ParseStableId(raw.UtilityGearId, path + ".gear.utility", errors);
        var tacticId = ParseStableId(raw.TacticId, path + ".tactic_id", errors);

        return new ParsedFighterBuild(
            fighterId,
            side,
            animalId,
            buildId,
            specials,
            passiveId,
            offenseGearId,
            defenseGearId,
            utilityGearId,
            tacticId);
    }

    private static FighterBuildSnapshot CreateBuild(ParsedFighterBuild build) =>
        new(
            RequireValue(build.FighterId),
            RequireValue(build.Side),
            RequireValue(build.AnimalId),
            build.BuildId,
            RequireReference(build.SpecialActionIds),
            RequireValue(build.PassiveId),
            new GearSelection(
                RequireValue(build.OffenseGearId),
                RequireValue(build.DefenseGearId),
                RequireValue(build.UtilityGearId)),
            RequireValue(build.TacticId));

    private static ExternalId? ParseExternalId(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        if (!ExternalId.TryParse(value, out var parsed))
        {
            Add(errors, InvalidIdentifier, path, InvalidIdentifierMessage);
            return null;
        }

        return parsed;
    }

    private static ArtifactVersion? ParseArtifactVersion(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        var parsed = ParseExternalId(value, path, errors);
        return parsed.HasValue ? new ArtifactVersion(parsed.Value.Value) : null;
    }

    private static Sha256Digest? ParseDigest(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        if (!Sha256Digest.TryParse(value, out var parsed))
        {
            Add(errors, InvalidIdentifier, path, InvalidIdentifierMessage);
            return null;
        }

        return parsed;
    }

    private static StableId? ParseStableId(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        if (!StableId.TryParse(value, out var parsed))
        {
            Add(errors, InvalidIdentifier, path, InvalidIdentifierMessage);
            return null;
        }

        return parsed;
    }

    private static StableId? ParseOptionalStableId(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (!StableId.TryParse(value, out var parsed))
        {
            Add(errors, InvalidIdentifier, path, InvalidIdentifierMessage);
            return null;
        }

        return parsed;
    }

    private static StableId[]? ParseStableIdList(
        IReadOnlyList<string?>? values,
        string path,
        ICollection<BattleRejectionError> errors,
        int? expectedCount)
    {
        if (values is null)
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        if ((expectedCount.HasValue && values.Count != expectedCount.Value) ||
            (!expectedCount.HasValue && values.Count == 0))
        {
            Add(errors, InvalidItemCount, path, InvalidItemCountMessage);
        }

        var parsed = new List<StableId>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var itemPath = path + "[" + index + "]";
            if (StringComparer.Ordinal.Equals(values[index], "all"))
            {
                Add(errors, InvalidIdentifier, itemPath, InvalidIdentifierMessage);
                continue;
            }

            var item = ParseStableId(values[index], itemPath, errors);
            if (item.HasValue)
            {
                parsed.Add(item.Value);
            }
        }

        if (parsed.Count != parsed.Distinct().Count())
        {
            Add(errors, DuplicateItem, path, DuplicateItemMessage);
        }

        return parsed.ToArray();
    }

    private static FighterId? ParseFighterId(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        var parsed = value switch
        {
            "fighter_a" => FighterId.FighterA,
            "fighter_b" => FighterId.FighterB,
            _ => (FighterId?)null,
        };
        if (!parsed.HasValue)
        {
            Add(errors, InvalidIdentifier, path, InvalidIdentifierMessage);
        }

        return parsed;
    }

    private static FighterSide? ParseSide(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        var parsed = value switch
        {
            "A" => FighterSide.A,
            "B" => FighterSide.B,
            _ => (FighterSide?)null,
        };
        if (!parsed.HasValue)
        {
            Add(errors, InvalidEnumValue, path, InvalidEnumMessage);
        }

        return parsed;
    }

    private static NormalizationMode? ParseNormalizationMode(
        string? value,
        string path,
        ICollection<BattleRejectionError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            Add(errors, MissingRequiredValue, path, MissingRequiredMessage);
            return null;
        }

        var parsed = value switch
        {
            "None" => NormalizationMode.None,
            "NormalizedRating" => NormalizationMode.NormalizedRating,
            _ => (NormalizationMode?)null,
        };
        if (!parsed.HasValue)
        {
            Add(errors, InvalidEnumValue, path, InvalidEnumMessage);
        }

        return parsed;
    }

    private static BattleCaseFactoryResult Failure(IEnumerable<BattleRejectionError> errors) =>
        new(
            null,
            errors
                .OrderBy(error => error.Path, StringComparer.Ordinal)
                .ThenBy(error => error.Code.Value, StringComparer.Ordinal)
                .ThenBy(error => error.MessageKey.Value, StringComparer.Ordinal));

    private static void Add(
        ICollection<BattleRejectionError> errors,
        ReasonCode code,
        string path,
        StableId messageKey) =>
        errors.Add(
            new BattleRejectionError(
                code,
                path,
                null,
                messageKey,
                Array.Empty<BattleRejectionDetail>()));

    private static T RequireValue<T>(T? value)
        where T : struct =>
        value ?? throw new InvalidOperationException("Validated raw value is unexpectedly missing.");

    private static T RequireReference<T>(T? value)
        where T : class =>
        value ?? throw new InvalidOperationException("Validated raw value is unexpectedly missing.");

    private sealed record ParsedModeRules(
        StableId? Id,
        ArtifactVersion? Version,
        NormalizationMode? NormalizationMode,
        StableId[]? AllowedAnimalIds,
        StableId[]? AllowedActionIds,
        StableId[]? AllowedPassiveIds,
        StableId[]? AllowedGearIds,
        StableId[]? AllowedTacticIds);

    private sealed record ParsedFighterBuild(
        FighterId? FighterId,
        FighterSide? Side,
        StableId? AnimalId,
        StableId? BuildId,
        StableId[]? SpecialActionIds,
        StableId? PassiveId,
        StableId? OffenseGearId,
        StableId? DefenseGearId,
        StableId? UtilityGearId,
        StableId? TacticId);
}
