using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;

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

        var schedule = relativeImpactTicks?.ToArray() ??
            throw new ArgumentNullException(nameof(relativeImpactTicks));
        if (schedule.Length > DecisionActionProfile.MaximumHitScheduleEntries)
        {
            throw new ArgumentException(
                $"An impact schedule cannot contain more than {DecisionActionProfile.MaximumHitScheduleEntries} entries.",
                nameof(relativeImpactTicks));
        }

        for (var index = 0; index < schedule.Length; index++)
        {
            if (schedule[index] < 0 || schedule[index] >= activeTicks ||
                (index != 0 && schedule[index - 1] >= schedule[index]))
            {
                throw new ArgumentException(
                    "Impact ticks must be unique, sorted and inside the active phase.",
                    nameof(relativeImpactTicks));
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

    internal IReadOnlyList<int> RelativeImpactTicks => _relativeImpactTicks;

    internal bool TrackTarget { get; }

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
}

internal readonly record struct ResourceMutation(
    ResourceKind Kind,
    StableId? ResourceId,
    int Before,
    int Delta,
    int After,
    int Minimum,
    int Maximum);
