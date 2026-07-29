namespace Battle.Contracts.Ids;

public enum FighterId
{
    FighterA = 0,
    FighterB = 1,
}

public enum FighterSide
{
    A = 0,
    B = 1,
}

public static class FighterIdValues
{
    public static string ToWireValue(this FighterId fighterId) =>
        fighterId switch
        {
            FighterId.FighterA => "fighter_a",
            FighterId.FighterB => "fighter_b",
            _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
        };

    public static FighterId ParseWireValue(string value) =>
        value switch
        {
            "fighter_a" => FighterId.FighterA,
            "fighter_b" => FighterId.FighterB,
            _ => throw new ArgumentException("Unknown fighter ID.", nameof(value)),
        };
}
