namespace CombatLab.Runner.Battles;

public sealed record RawModeRules(
    string? Id,
    string? Version,
    string? NormalizationMode,
    IReadOnlyList<string?>? AllowedAnimalIds,
    IReadOnlyList<string?>? AllowedActionIds,
    IReadOnlyList<string?>? AllowedPassiveIds,
    IReadOnlyList<string?>? AllowedGearIds,
    IReadOnlyList<string?>? AllowedTacticIds);
