namespace CombatLab.Runner.Battles;

public sealed record RawFighterBuild(
    string? FighterId,
    string? Side,
    string? AnimalId,
    string? BuildId,
    IReadOnlyList<string?>? SpecialActionIds,
    string? PassiveId,
    string? OffenseGearId,
    string? DefenseGearId,
    string? UtilityGearId,
    string? TacticId);
