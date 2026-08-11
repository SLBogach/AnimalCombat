namespace Battle.Config.Manifest;

public sealed class ConfigEntityCounts
{
    public ConfigEntityCounts(
        int fighters,
        int actions,
        int passives,
        int effects,
        int tactics,
        int gear,
        int builds)
    {
        if (fighters < 0 || actions < 0 || passives < 0 || effects < 0 ||
            tactics < 0 || gear < 0 || builds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fighters), "Entity counts must not be negative.");
        }

        Fighters = fighters;
        Actions = actions;
        Passives = passives;
        Effects = effects;
        Tactics = tactics;
        Gear = gear;
        Builds = builds;
    }

    public int Fighters { get; }

    public int Actions { get; }

    public int Passives { get; }

    public int Effects { get; }

    public int Tactics { get; }

    public int Gear { get; }

    public int Builds { get; }
}
