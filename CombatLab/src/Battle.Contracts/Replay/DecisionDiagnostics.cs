using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Replay;

public readonly record struct DecisionCooldownSnapshot
{
    public DecisionCooldownSnapshot(StableId actionId, int ticksRemaining)
    {
        if (ticksRemaining < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksRemaining));
        }

        ActionId = actionId;
        TicksRemaining = ticksRemaining;
    }

    public StableId ActionId { get; }

    public int TicksRemaining { get; }
}

public readonly record struct DecisionOpportunitySnapshot
{
    public DecisionOpportunitySnapshot(StableId actionId, int debt)
    {
        if (debt < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(debt));
        }

        ActionId = actionId;
        Debt = debt;
    }

    public StableId ActionId { get; }

    public int Debt { get; }
}

public sealed class DecisionFighterSnapshot
{
    private readonly ReadOnlyCollection<DecisionCooldownSnapshot> _cooldowns;
    private readonly ReadOnlyCollection<DecisionOpportunitySnapshot> _opportunityDebts;

    public DecisionFighterSnapshot(
        FighterFrame publicFrame,
        FighterBuildSnapshot build,
        IEnumerable<DecisionCooldownSnapshot> cooldowns,
        StableId? lastActionId,
        string? lastActionCategory,
        int sameActionStreak,
        int sameCategoryStreak,
        IEnumerable<DecisionOpportunitySnapshot> opportunityDebts,
        StableId? observableActionId,
        int? observableCommitTick,
        bool emergency)
    {
        PublicFrame = publicFrame ?? throw new ArgumentNullException(nameof(publicFrame));
        Build = build ?? throw new ArgumentNullException(nameof(build));
        if (PublicFrame.FighterId != Build.FighterId)
        {
            throw new ArgumentException(
                "The public frame and build must identify the same fighter.",
                nameof(build));
        }

        if (sameActionStreak < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sameActionStreak));
        }

        if (sameCategoryStreak < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sameCategoryStreak));
        }

        if ((lastActionId.HasValue && sameActionStreak == 0) ||
            (!lastActionId.HasValue && sameActionStreak != 0) ||
            ((lastActionCategory is not null) && sameCategoryStreak == 0) ||
            (lastActionCategory is null && sameCategoryStreak != 0))
        {
            throw new ArgumentException("Decision history fields are inconsistent.");
        }

        if (observableCommitTick < 0 ||
            (observableActionId.HasValue != observableCommitTick.HasValue))
        {
            throw new ArgumentException("Observable telegraph fields are inconsistent.");
        }

        _cooldowns = CopySorted(
            cooldowns,
            item => item.ActionId,
            nameof(cooldowns));
        _opportunityDebts = CopySorted(
            opportunityDebts,
            item => item.ActionId,
            nameof(opportunityDebts));
        var selectedSpecialIds = Build.SpecialActionIds
            .OrderBy(item => item)
            .ToArray();
        if (!_opportunityDebts.Select(item => item.ActionId).SequenceEqual(selectedSpecialIds))
        {
            throw new ArgumentException(
                "Opportunity debt entries must cover both selected special actions in ActionId order.",
                nameof(opportunityDebts));
        }
        LastActionId = lastActionId;
        LastActionCategory = lastActionCategory;
        SameActionStreak = sameActionStreak;
        SameCategoryStreak = sameCategoryStreak;
        ObservableActionId = observableActionId;
        ObservableCommitTick = observableCommitTick;
        Emergency = emergency;
    }

    public FighterFrame PublicFrame { get; }

    public FighterBuildSnapshot Build { get; }

    public IReadOnlyList<DecisionCooldownSnapshot> Cooldowns => _cooldowns;

    public StableId? LastActionId { get; }

    public string? LastActionCategory { get; }

    public int SameActionStreak { get; }

    public int SameCategoryStreak { get; }

    public IReadOnlyList<DecisionOpportunitySnapshot> OpportunityDebts => _opportunityDebts;

    public StableId? ObservableActionId { get; }

    public int? ObservableCommitTick { get; }

    public bool Emergency { get; }

    private static ReadOnlyCollection<T> CopySorted<T>(
        IEnumerable<T> source,
        Func<T, StableId> key,
        string parameterName)
    {
        if (source is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var copy = source.ToArray();
        for (var index = 1; index < copy.Length; index++)
        {
            if (key(copy[index - 1]).CompareTo(key(copy[index])) >= 0)
            {
                throw new ArgumentException(
                    "Decision snapshot entries must be in strict ActionId order.",
                    parameterName);
            }
        }

        return new ReadOnlyCollection<T>(copy);
    }
}

public sealed class DecisionBatchSnapshotProjection
{
    private readonly ReadOnlyCollection<FighterId> _initiativeOrder;
    private readonly ReadOnlyCollection<DecisionFighterSnapshot> _fighters;

    public DecisionBatchSnapshotProjection(
        ExternalId battleId,
        ArtifactVersion engineVersion,
        ulong masterSeed,
        Sha256Digest configHash,
        ModeRulesSnapshot modeRules,
        int tick,
        IEnumerable<FighterId> initiativeOrder,
        ulong decisionNextIndex,
        IEnumerable<DecisionFighterSnapshot> fighters)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        ModeRules = modeRules ?? throw new ArgumentNullException(nameof(modeRules));
        var initiative = initiativeOrder?.ToArray() ??
            throw new ArgumentNullException(nameof(initiativeOrder));
        if (initiative.Length != 2 ||
            initiative.Distinct().Count() != 2 ||
            initiative.Any(item => item is not FighterId.FighterA and not FighterId.FighterB))
        {
            throw new ArgumentException(
                "Initiative order must contain both fighters exactly once.",
                nameof(initiativeOrder));
        }

        var fighterCopy = fighters?.ToArray() ?? throw new ArgumentNullException(nameof(fighters));
        if (fighterCopy.Length != 2 ||
            fighterCopy.Any(item => item is null) ||
            fighterCopy[0].PublicFrame.FighterId != FighterId.FighterA ||
            fighterCopy[1].PublicFrame.FighterId != FighterId.FighterB)
        {
            throw new ArgumentException(
                "Decision fighters must contain fighter A followed by fighter B.",
                nameof(fighters));
        }

        BattleId = battleId;
        EngineVersion = engineVersion;
        MasterSeed = masterSeed;
        ConfigHash = configHash;
        Tick = tick;
        DecisionNextIndex = decisionNextIndex;
        _initiativeOrder = new ReadOnlyCollection<FighterId>(initiative);
        _fighters = new ReadOnlyCollection<DecisionFighterSnapshot>(fighterCopy);
    }

    public ExternalId BattleId { get; }

    public ArtifactVersion EngineVersion { get; }

    public ulong MasterSeed { get; }

    public Sha256Digest ConfigHash { get; }

    public ModeRulesSnapshot ModeRules { get; }

    public int Tick { get; }

    public IReadOnlyList<FighterId> InitiativeOrder => _initiativeOrder;

    public ulong DecisionNextIndex { get; }

    public IReadOnlyList<DecisionFighterSnapshot> Fighters => _fighters;
}

public sealed class DecisionCandidateTrace
{
    private static readonly string[] StageCodes =
    {
        "Tactic",
        "Situation",
        "Synergy",
        "Counter",
        "Variety",
        "Opportunity",
    };

    private readonly ReadOnlyCollection<ModifierTrace> _modifiers;

    public DecisionCandidateTrace(
        StableId actionId,
        bool legal,
        ReasonCode? firstRejectionCode,
        int baseWeight,
        IEnumerable<ModifierTrace> modifiers,
        int finalWeight)
    {
        if (baseWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseWeight));
        }

        if (finalWeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(finalWeight));
        }

        if (legal == firstRejectionCode.HasValue)
        {
            throw new ArgumentException(
                "A legal candidate has no rejection; an illegal candidate has exactly one.",
                nameof(firstRejectionCode));
        }

        var copy = modifiers?.ToArray() ?? throw new ArgumentNullException(nameof(modifiers));
        if (legal)
        {
            if (copy.Length != StageCodes.Length)
            {
                throw new ArgumentException(
                    "A legal diagnostic candidate must contain all six folded stages.",
                    nameof(modifiers));
            }

            for (var index = 0; index < StageCodes.Length; index++)
            {
                if (!StringComparer.Ordinal.Equals(copy[index].Code.Value, StageCodes[index]))
                {
                    throw new ArgumentException(
                        "Diagnostic stages are not in canonical order.",
                        nameof(modifiers));
                }
            }
        }
        else if (copy.Length != 0 || finalWeight != 0)
        {
            throw new ArgumentException(
                "An illegal diagnostic candidate has no modifiers and zero final weight.",
                nameof(modifiers));
        }

        ActionId = actionId;
        Legal = legal;
        FirstRejectionCode = firstRejectionCode;
        BaseWeight = baseWeight;
        _modifiers = new ReadOnlyCollection<ModifierTrace>(copy);
        FinalWeight = finalWeight;
    }

    public StableId ActionId { get; }

    public bool Legal { get; }

    public ReasonCode? FirstRejectionCode { get; }

    public int BaseWeight { get; }

    public IReadOnlyList<ModifierTrace> Modifiers => _modifiers;

    public int FinalWeight { get; }
}

public sealed class DecisionTrace
{
    private readonly ReadOnlyCollection<DecisionCandidateTrace> _candidates;

    public DecisionTrace(
        DecisionId decisionId,
        int tick,
        long sequence,
        FighterId actorId,
        Sha256Digest snapshotDigest,
        IEnumerable<DecisionCandidateTrace> candidates)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        if (sequence is < 0 or > EventId.MaximumSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (actorId is not FighterId.FighterA and not FighterId.FighterB)
        {
            throw new ArgumentOutOfRangeException(nameof(actorId));
        }

        var copy = candidates?.ToArray() ?? throw new ArgumentNullException(nameof(candidates));
        if (copy.Length is < 1 or > 256 || copy.Any(item => item is null))
        {
            throw new ArgumentException(
                "A decision trace must contain between one and 256 checked candidates.",
                nameof(candidates));
        }

        for (var index = 1; index < copy.Length; index++)
        {
            if (copy[index - 1].ActionId.CompareTo(copy[index].ActionId) >= 0)
            {
                throw new ArgumentException(
                    "Decision trace candidates must be in strict ActionId order.",
                    nameof(candidates));
            }
        }

        DecisionId = decisionId;
        Tick = tick;
        Sequence = sequence;
        ActorId = actorId;
        SnapshotDigest = snapshotDigest;
        _candidates = new ReadOnlyCollection<DecisionCandidateTrace>(copy);
    }

    public DecisionId DecisionId { get; }

    public int Tick { get; }

    public long Sequence { get; }

    public FighterId ActorId { get; }

    public Sha256Digest SnapshotDigest { get; }

    public IReadOnlyList<DecisionCandidateTrace> Candidates => _candidates;
}
