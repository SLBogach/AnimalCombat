using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;
using Battle.Core.Resolution;

namespace Battle.Core.Engine;

internal sealed class CombatActionDescriptor
{
    private readonly int[] _relativeImpactTicks;

    internal CombatActionDescriptor(
        StableId actionId,
        string category,
        DecisionId decisionId,
        FighterId? targetFighterId,
        int? targetPositionAtCommit,
        CommitDirection commitDirection,
        int energyCost,
        int resourceCost,
        int startupTicks,
        int activeTicks,
        int recoveryTicks,
        int cooldownTicks,
        IEnumerable<int> relativeImpactTicks,
        bool trackTarget,
        int commitTick)
        : this(
            actionId,
            category,
            decisionId,
            targetFighterId,
            targetPositionAtCommit,
            commitDirection,
            energyCost,
            resourceCost,
            startupTicks,
            activeTicks,
            recoveryTicks,
            cooldownTicks,
            CreateLegacyResolutionProfile(actionId, category, activeTicks, relativeImpactTicks, trackTarget),
            trackTarget,
            movesTowardTarget: false,
            commitTick)
    {
    }

    internal CombatActionDescriptor(
        StableId actionId,
        string category,
        DecisionId decisionId,
        FighterId? targetFighterId,
        int? targetPositionAtCommit,
        CommitDirection commitDirection,
        int energyCost,
        int resourceCost,
        int startupTicks,
        int activeTicks,
        int recoveryTicks,
        int cooldownTicks,
        ResolutionActionProfile resolutionProfile,
        bool trackTarget,
        bool movesTowardTarget,
        int commitTick)
    {
        if (string.IsNullOrEmpty(category))
        {
            throw new ArgumentException("An action category is required.", nameof(category));
        }

        if (targetFighterId.HasValue &&
            targetFighterId.Value is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFighterId));
        }

        if (targetFighterId.HasValue != targetPositionAtCommit.HasValue)
        {
            throw new ArgumentException("Target identity and position must have the same nullability.");
        }

        if (targetFighterId.HasValue && commitDirection == CommitDirection.None)
        {
            throw new ArgumentException("An opponent-targeting action requires a frozen direction.");
        }

        if (!targetFighterId.HasValue && targetPositionAtCommit.HasValue)
        {
            throw new ArgumentException("A self action cannot carry a target position.");
        }

        if (energyCost < 0 || resourceCost < 0 || startupTicks < 0 ||
            activeTicks < 1 || recoveryTicks < 0 || cooldownTicks < 0 || commitTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeTicks));
        }

        ResolutionProfile = resolutionProfile ?? throw new ArgumentNullException(nameof(resolutionProfile));
        if (ResolutionProfile.Id != actionId || ResolutionProfile.ActiveTicks != activeTicks)
        {
            throw new ArgumentException("Resolution profile identity/timing must match the committed action.", nameof(resolutionProfile));
        }

        var schedule = ResolutionProfile.Schedule.Select(entry => entry.RelativeTick).ToArray();
        if (schedule.Length > DecisionActionProfile.MaximumHitScheduleEntries)
        {
            throw new ArgumentException(
                $"An impact schedule cannot contain more than {DecisionActionProfile.MaximumHitScheduleEntries} entries.",
                nameof(resolutionProfile));
        }

        for (var index = 0; index < schedule.Length; index++)
        {
            if (schedule[index] < 0 || schedule[index] >= activeTicks ||
                (index != 0 && schedule[index - 1] >= schedule[index]))
            {
                throw new ArgumentException(
                    "Impact ticks must be unique, sorted and inside the active phase.",
                    nameof(resolutionProfile));
            }
        }

        ActionId = actionId;
        Category = category;
        DecisionId = decisionId;
        TargetFighterId = targetFighterId;
        TargetPositionAtCommit = targetPositionAtCommit;
        CommitDirection = commitDirection;
        EnergyCost = energyCost;
        ResourceCost = resourceCost;
        StartupTicks = startupTicks;
        ActiveTicks = activeTicks;
        RecoveryTicks = recoveryTicks;
        CooldownTicks = cooldownTicks;
        _relativeImpactTicks = schedule;
        TrackTarget = trackTarget;
        MovesTowardTarget = movesTowardTarget;
        CommitTick = commitTick;
    }

    internal StableId ActionId { get; }

    internal string Category { get; }

    internal DecisionId DecisionId { get; }

    internal FighterId? TargetFighterId { get; }

    internal int? TargetPositionAtCommit { get; }

    internal CommitDirection CommitDirection { get; }

    internal int EnergyCost { get; }

    internal int ResourceCost { get; }

    internal int StartupTicks { get; }

    internal int ActiveTicks { get; }

    internal int RecoveryTicks { get; }

    internal int CooldownTicks { get; }

    internal ResolutionActionProfile ResolutionProfile { get; }

    internal IReadOnlyList<int> RelativeImpactTicks => _relativeImpactTicks;

    internal IReadOnlyList<HitScheduleEntry> HitSchedule => ResolutionProfile.Schedule;

    internal bool TrackTarget { get; }

    internal bool MovesTowardTarget { get; }

    internal int CommitTick { get; }

    internal IReadOnlyList<int> AbsoluteImpactTicks()
    {
        try
        {
            return _relativeImpactTicks
                .Select(relative => checked(CommitTick + StartupTicks + relative))
                .ToArray();
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.DecisionArithmeticOverflow,
                TickPhase.Decisions.ToString(),
                "Combat impact timing overflowed: " + exception.Message);
        }
    }

    internal int AbsoluteImpactTick(HitScheduleEntry entry)
    {
        if (entry.Ordinal >= HitSchedule.Count || HitSchedule[entry.Ordinal] != entry)
        {
            throw new ArgumentException("The schedule entry does not belong to this action.", nameof(entry));
        }

        try
        {
            return checked(CommitTick + StartupTicks + entry.RelativeTick);
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.DecisionArithmeticOverflow,
                TickPhase.CollectIntents.ToString(),
                "Combat impact timing overflowed: " + exception.Message);
        }
    }

    private static ResolutionActionProfile CreateLegacyResolutionProfile(
        StableId actionId,
        string category,
        int activeTicks,
        IEnumerable<int> relativeImpactTicks,
        bool trackTarget)
    {
        var ticks = relativeImpactTicks?.ToArray() ??
            throw new ArgumentNullException(nameof(relativeImpactTicks));
        if (ticks.Length > ResolutionActionProfile.MaximumScheduleEntries)
        {
            throw new ArgumentException(
                $"An impact schedule cannot contain more than {ResolutionActionProfile.MaximumScheduleEntries} entries.",
                nameof(relativeImpactTicks));
        }

        var schedule = ticks.Select(
                (tick, ordinal) => new HitScheduleEntry(HitPrimitiveKind.Hit, tick, ordinal))
            .ToArray();
        return new ResolutionActionProfile(
            actionId,
            slotType: "LegacyFixture",
            category,
            ResolutionMovementMode.None,
            schedule.Length == 0 ? Array.Empty<StableId>() : new[] { new StableId("strike") },
            schedule,
            activeTicks,
            hitCount: schedule.Length,
            actionPriority: 0,
            resolutionPriority: 0,
            clashPriority: 0,
            grabPriority: 0,
            hitRangeMinimum: 0,
            hitRangeMaximum: int.MaxValue,
            baseDamage: 0,
            powerRatioFixedPoint: 0,
            minimumDamage: 0,
            blockable: false,
            dodgeable: false,
            undodgeable: true,
            blockBaseChanceFixedPoint: 0,
            blockReductionFixedPoint: 0,
            dodgeBaseChanceFixedPoint: 0,
            chipMinimum: 0,
            baseStagger: 0,
            baseStunTicks: 0,
            baseKnockback: 0,
            knockbackMinimum: 0,
            knockbackMaximum: 0,
            moveDistance: 0,
            trackTarget,
            wallImpact: false,
            wallDamagePerUnitFixedPoint: 0,
            wallDamageMinimum: 0,
            wallDamageMaximum: 0,
            interruptProfile: "None");
    }
}

internal readonly record struct ResourceMutation(
    ResourceKind Kind,
    StableId? ResourceId,
    int Before,
    int Delta,
    int After,
    int Minimum,
    int Maximum);
