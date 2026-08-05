using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Replay;

public sealed class ArenaSnapshot
{
    public ArenaSnapshot(
        StableId arenaId,
        int minimumPosition,
        int maximumPosition,
        int startPositionA,
        int startPositionB)
    {
        if (string.IsNullOrEmpty(arenaId.Value))
        {
            throw new ArgumentException("An arena ID is required.", nameof(arenaId));
        }

        if (minimumPosition >= maximumPosition)
        {
            throw new ArgumentException(
                "The arena minimum position must be less than its maximum position.",
                nameof(maximumPosition));
        }

        if (startPositionA < minimumPosition || startPositionA > maximumPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(startPositionA));
        }

        if (startPositionB < minimumPosition || startPositionB > maximumPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(startPositionB));
        }

        ArenaId = arenaId;
        MinimumPosition = minimumPosition;
        MaximumPosition = maximumPosition;
        StartPositionA = startPositionA;
        StartPositionB = startPositionB;
    }

    public StableId ArenaId { get; }

    public int MinimumPosition { get; }

    public int MaximumPosition { get; }

    public int StartPositionA { get; }

    public int StartPositionB { get; }
}

public sealed class BattleInputSnapshot
{
    public BattleInputSnapshot(
        ulong masterSeed,
        StableId modeRulesId,
        ArenaSnapshot arena)
    {
        if (string.IsNullOrEmpty(modeRulesId.Value))
        {
            throw new ArgumentException("A mode rules ID is required.", nameof(modeRulesId));
        }

        MasterSeed = masterSeed;
        ModeRulesId = modeRulesId;
        Arena = arena ?? throw new ArgumentNullException(nameof(arena));
    }

    public ulong MasterSeed { get; }

    public StableId ModeRulesId { get; }

    public ArenaSnapshot Arena { get; }
}

public sealed class CombatJournalFighterStart
{
    public CombatJournalFighterStart(
        FighterBuildSnapshot build,
        FighterFrame initialFrame)
    {
        Build = build ?? throw new ArgumentNullException(nameof(build));
        InitialFrame = initialFrame ?? throw new ArgumentNullException(nameof(initialFrame));

        if (Build.FighterId != InitialFrame.FighterId)
        {
            throw new ArgumentException(
                "The build and initial frame must describe the same fighter.",
                nameof(initialFrame));
        }
    }

    public FighterBuildSnapshot Build { get; }

    public FighterFrame InitialFrame { get; }
}

public sealed class CombatJournalStart
{
    public CombatJournalStart(
        ExternalId battleId,
        ArtifactVersion engineVersion,
        ArtifactVersion rngVersion,
        ArtifactVersion orderingVersion,
        ConfigReference config,
        BattleInputSnapshot input,
        CombatJournalFighterStart fighterA,
        CombatJournalFighterStart fighterB)
    {
        if (string.IsNullOrEmpty(battleId.Value))
        {
            throw new ArgumentException("A battle ID is required.", nameof(battleId));
        }

        Input = input ?? throw new ArgumentNullException(nameof(input));
        FighterA = fighterA ?? throw new ArgumentNullException(nameof(fighterA));
        FighterB = fighterB ?? throw new ArgumentNullException(nameof(fighterB));

        if (FighterA.Build.FighterId != FighterId.FighterA ||
            FighterA.Build.Side != FighterSide.A ||
            FighterA.InitialFrame.FighterId != FighterId.FighterA)
        {
            throw new ArgumentException("Fighter A start must occupy side A.", nameof(fighterA));
        }

        if (FighterB.Build.FighterId != FighterId.FighterB ||
            FighterB.Build.Side != FighterSide.B ||
            FighterB.InitialFrame.FighterId != FighterId.FighterB)
        {
            throw new ArgumentException("Fighter B start must occupy side B.", nameof(fighterB));
        }

        BattleId = battleId;
        EngineVersion = engineVersion;
        RngVersion = rngVersion;
        OrderingVersion = orderingVersion;
        Config = config;
    }

    public ExternalId BattleId { get; }

    public ArtifactVersion EngineVersion { get; }

    public ArtifactVersion RngVersion { get; }

    public ArtifactVersion OrderingVersion { get; }

    public ConfigReference Config { get; }

    public BattleInputSnapshot Input { get; }

    public CombatJournalFighterStart FighterA { get; }

    public CombatJournalFighterStart FighterB { get; }
}
