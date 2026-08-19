using System.Collections.ObjectModel;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;

namespace Battle.Core.Decisions;

internal readonly record struct DecisionTimingSettings
{
    internal DecisionTimingSettings(
        int speedBaseline,
        int speedSlope,
        int speedMinimumFixedPoint,
        int speedMaximumFixedPoint)
    {
        if (speedBaseline < 0 || speedSlope < 0 || speedMinimumFixedPoint < 1 ||
            speedMaximumFixedPoint < speedMinimumFixedPoint)
        {
            throw new ArgumentOutOfRangeException(nameof(speedBaseline));
        }

        SpeedBaseline = speedBaseline;
        SpeedSlope = speedSlope;
        SpeedMinimumFixedPoint = speedMinimumFixedPoint;
        SpeedMaximumFixedPoint = speedMaximumFixedPoint;
    }

    internal int SpeedBaseline { get; }

    internal int SpeedSlope { get; }

    internal int SpeedMinimumFixedPoint { get; }

    internal int SpeedMaximumFixedPoint { get; }
}

internal sealed class DecisionFighterProfile
{
    internal DecisionFighterProfile(
        FighterBuildSnapshot build,
        DecisionBuildView buildView,
        DecisionTacticProfile tactic,
        DecisionTagMultiplierProfile passive,
        DecisionTagMultiplierProfile offenseGear,
        DecisionTagMultiplierProfile defenseGear,
        DecisionTagMultiplierProfile utilityGear,
        int? lowHealthThresholdFixedPoint)
    {
        Build = build ?? throw new ArgumentNullException(nameof(build));
        BuildView = buildView ?? throw new ArgumentNullException(nameof(buildView));
        Tactic = tactic ?? throw new ArgumentNullException(nameof(tactic));
        Passive = passive ?? throw new ArgumentNullException(nameof(passive));
        OffenseGear = offenseGear ?? throw new ArgumentNullException(nameof(offenseGear));
        DefenseGear = defenseGear ?? throw new ArgumentNullException(nameof(defenseGear));
        UtilityGear = utilityGear ?? throw new ArgumentNullException(nameof(utilityGear));
        if (lowHealthThresholdFixedPoint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lowHealthThresholdFixedPoint));
        }

        LowHealthThresholdFixedPoint = lowHealthThresholdFixedPoint;
    }

    internal FighterBuildSnapshot Build { get; }

    internal DecisionBuildView BuildView { get; }

    internal DecisionTacticProfile Tactic { get; }

    internal DecisionTagMultiplierProfile Passive { get; }

    internal DecisionTagMultiplierProfile OffenseGear { get; }

    internal DecisionTagMultiplierProfile DefenseGear { get; }

    internal DecisionTagMultiplierProfile UtilityGear { get; }

    internal int? LowHealthThresholdFixedPoint { get; }
}

internal sealed class DecisionRuntimeSettings
{
    private readonly ReadOnlyCollection<DecisionActionProfile> _actions;

    internal DecisionRuntimeSettings(
        ExternalId battleId,
        ArtifactVersion engineVersion,
        ulong masterSeed,
        Sha256Digest configHash,
        ModeRulesSnapshot modeRules,
        IEnumerable<DecisionActionProfile> actions,
        DecisionFighterProfile fighterA,
        DecisionFighterProfile fighterB,
        DecisionAvailabilitySettings availability,
        DecisionWeightSettings weights,
        DecisionTimingSettings timing,
        int repeatSameActionFixedPoint,
        int repeatSameCategoryFixedPoint,
        int opportunityGrowthFixedPoint,
        int opportunityCapFixedPoint,
        int hardOpportunityMisses,
        int wallZoneSize)
    {
        ModeRules = modeRules ?? throw new ArgumentNullException(nameof(modeRules));
        FighterA = fighterA ?? throw new ArgumentNullException(nameof(fighterA));
        FighterB = fighterB ?? throw new ArgumentNullException(nameof(fighterB));
        Availability = availability ?? throw new ArgumentNullException(nameof(availability));
        if (actions is null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        var actionCopy = actions.OrderBy(action => action.Id).ToArray();
        if (actionCopy.Length == 0 || actionCopy.Select(action => action.Id).Distinct().Count() != actionCopy.Length)
        {
            throw new ArgumentException("The decision action catalog must be non-empty and unique.", nameof(actions));
        }

        if (repeatSameActionFixedPoint < 0 || repeatSameCategoryFixedPoint < 0 ||
            opportunityGrowthFixedPoint < 0 || opportunityCapFixedPoint < 1 ||
            hardOpportunityMisses < 0 || wallZoneSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(repeatSameActionFixedPoint));
        }

        BattleId = battleId;
        EngineVersion = engineVersion;
        MasterSeed = masterSeed;
        ConfigHash = configHash;
        _actions = new ReadOnlyCollection<DecisionActionProfile>(actionCopy);
        Weights = weights;
        Timing = timing;
        RepeatSameActionFixedPoint = repeatSameActionFixedPoint;
        RepeatSameCategoryFixedPoint = repeatSameCategoryFixedPoint;
        OpportunityGrowthFixedPoint = opportunityGrowthFixedPoint;
        OpportunityCapFixedPoint = opportunityCapFixedPoint;
        HardOpportunityMisses = hardOpportunityMisses;
        WallZoneSize = wallZoneSize;
    }

    internal ExternalId BattleId { get; }

    internal ArtifactVersion EngineVersion { get; }

    internal ulong MasterSeed { get; }

    internal Sha256Digest ConfigHash { get; }

    internal ModeRulesSnapshot ModeRules { get; }

    internal IReadOnlyList<DecisionActionProfile> Actions => _actions;

    internal DecisionFighterProfile FighterA { get; }

    internal DecisionFighterProfile FighterB { get; }

    internal DecisionAvailabilitySettings Availability { get; }

    internal DecisionWeightSettings Weights { get; }

    internal DecisionTimingSettings Timing { get; }

    internal int RepeatSameActionFixedPoint { get; }

    internal int RepeatSameCategoryFixedPoint { get; }

    internal int OpportunityGrowthFixedPoint { get; }

    internal int OpportunityCapFixedPoint { get; }

    internal int HardOpportunityMisses { get; }

    internal int WallZoneSize { get; }

    internal DecisionFighterProfile GetFighter(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => FighterA,
        FighterId.FighterB => FighterB,
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    internal DecisionActionProfile GetAction(StableId actionId) =>
        _actions.FirstOrDefault(action => action.Id == actionId) ??
        throw new KeyNotFoundException($"Unknown decision action '{actionId}'.");
}
