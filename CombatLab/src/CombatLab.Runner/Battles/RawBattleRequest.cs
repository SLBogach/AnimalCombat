namespace CombatLab.Runner.Battles;

public sealed record RawBattleRequest(
    string? BattleId,
    string? EngineVersion,
    string? ConfigHash,
    RawModeRules? ModeRules,
    ulong MasterSeed,
    RawFighterBuild? BuildA,
    RawFighterBuild? BuildB);
