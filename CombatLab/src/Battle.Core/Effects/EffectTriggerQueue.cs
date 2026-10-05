using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Effects;

internal readonly record struct EffectOccurrence(EventId? RealEventId, int Tick, FighterId? Subject)
{
    internal static EffectOccurrence Event(EventId id) => new(id, -1, null);
    internal static EffectOccurrence EndOfTick(int tick, FighterId subject) => new(null, tick, subject);
}
internal readonly record struct EffectHook(EffectTrigger Trigger, int Tick, FighterId Subject, EventId SourceEventId,
    long SourceSequence, EffectOccurrence Occurrence, EffectOccurrence Root, int Depth);
internal readonly record struct EffectQueueCandidate(EffectHook Hook, EffectRuleProfile Rule,
    FighterId Owner, FighterId Recipient, bool Eligible, bool EffectBudgetAvailable);
internal enum EffectAdmission
{
    Queued, ConditionRejected, TriggerCycleSuppressed, OncePerEventSuppressed, RuleBudgetRejected, EffectBudgetRejected,
}
internal readonly record struct EffectRuleBudget(int Tick, int TickCount, int BattleCount, int NextAllowedTick);

/// <summary>Bounded, cloneable per-battle queue state. It admits preview work, never canonical events.</summary>
internal sealed class EffectTriggerQueue
{
    private readonly int maxDepth;
    private readonly int maxPerTick;
    private readonly List<EffectQueueCandidate> pending;
    private readonly Dictionary<(FighterId, StableId), EffectRuleBudget> budgets;
    private readonly HashSet<(FighterId, StableId, EffectOccurrence, FighterId)> once;
    private readonly Dictionary<EffectOccurrence, HashSet<(FighterId, StableId, FighterId)>> visited;
    private readonly Dictionary<EffectOccurrence, int> executingDepths;
    private int currentTick = -1;
    private int admitted;
    internal EffectTriggerQueue(int maxDepth, int maxPerTick)
    {
        if (maxDepth is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        if (maxPerTick is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maxPerTick));
        this.maxDepth = maxDepth; this.maxPerTick = maxPerTick;
        pending = new List<EffectQueueCandidate>();
        budgets = new Dictionary<(FighterId, StableId), EffectRuleBudget>();
        once = new HashSet<(FighterId, StableId, EffectOccurrence, FighterId)>();
        visited = new Dictionary<EffectOccurrence, HashSet<(FighterId, StableId, FighterId)>>();
        executingDepths = new Dictionary<EffectOccurrence, int>();
    }
    private EffectTriggerQueue(EffectTriggerQueue source)
    {
        maxDepth = source.maxDepth; maxPerTick = source.maxPerTick;
        pending = new List<EffectQueueCandidate>(source.pending);
        budgets = new Dictionary<(FighterId, StableId), EffectRuleBudget>(source.budgets);
        once = new HashSet<(FighterId, StableId, EffectOccurrence, FighterId)>(source.once);
        visited = source.visited.ToDictionary(x => x.Key, x => new HashSet<(FighterId, StableId, FighterId)>(x.Value));
        executingDepths = new Dictionary<EffectOccurrence, int>(source.executingDepths);
        currentTick = source.currentTick; admitted = source.admitted;
    }
    internal EffectTriggerQueue Clone() => new(this);
    internal int AdmittedThisTick => admitted;
    internal int PendingCount => pending.Count;
    internal EffectRuleBudget Budget(FighterId owner, StableId rule) => budgets.TryGetValue((owner, rule), out var budget) ? budget : default;

    internal IReadOnlyList<(EffectQueueCandidate Candidate, EffectAdmission Admission)> AdmitLevel(IEnumerable<EffectQueueCandidate> candidates)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));
        var ordered = candidates.OrderBy(x => x.Hook.Depth).ThenBy(x => x.Hook.SourceSequence)
            .ThenBy(x => x.Rule.Priority).ThenBy(x => x.Rule.RuleId).ThenBy(x => x.Owner).ThenBy(x => x.Recipient).ToArray();
        var results = new List<(EffectQueueCandidate, EffectAdmission)>();
        foreach (var candidate in ordered) results.Add((candidate, Admit(candidate)));
        return results;
    }
    private EffectAdmission Admit(EffectQueueCandidate candidate)
    {
        var hook = candidate.Hook;
        var rule = candidate.Rule ?? throw new ArgumentException("A materialized rule is required.", nameof(candidate));
        ValidateHook(hook);
        RequireFighter(candidate.Owner); RequireFighter(candidate.Recipient);
        if (hook.Tick < currentTick) throw Failure(EngineFailureCodes.EffectInvalidMutation, "Trigger ticks cannot move backwards.");
        if (hook.Tick != currentTick)
        {
            if (pending.Count != 0) throw Failure(EngineFailureCodes.EffectInvalidMutation, "Pending triggers cannot leak into the next tick.");
            currentTick = hook.Tick; admitted = 0;
            visited.Clear();
            executingDepths.Clear();
        }
        if (!candidate.Eligible) return EffectAdmission.ConditionRejected;
        var rootKey = (candidate.Owner, rule.RuleId, candidate.Recipient);
        if (visited.TryGetValue(hook.Root, out var nodes) && nodes.Contains(rootKey)) return EffectAdmission.TriggerCycleSuppressed;
        var onceKey = (candidate.Owner, rule.RuleId, hook.Occurrence, candidate.Recipient);
        if (once.Contains(onceKey)) return EffectAdmission.OncePerEventSuppressed;
        var budget = Budget(candidate.Owner, rule.RuleId);
        var tickCount = budget.Tick == hook.Tick ? budget.TickCount : 0;
        if (hook.Tick < budget.NextAllowedTick || tickCount >= rule.MaxActivationsPerTick || budget.BattleCount >= rule.MaxActivationsPerBattle)
            return EffectAdmission.RuleBudgetRejected;
        if (!candidate.EffectBudgetAvailable) return EffectAdmission.EffectBudgetRejected;
        if (executingDepths.TryGetValue(hook.Root, out var executingDepth) && hook.Depth < executingDepth)
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Causal work cannot return to an earlier executing level.");
        if (hook.Depth > maxDepth) throw Failure(EngineFailureCodes.EffectTriggerDepthExceeded, "Trigger depth exceeded.");
        if (admitted >= maxPerTick) throw Failure(EngineFailureCodes.EffectTriggerTickCapExceeded, "Trigger tick cap exceeded.");
        EffectRuleBudget charged;
        try { charged = new EffectRuleBudget(hook.Tick, checked(tickCount + 1), checked(budget.BattleCount + 1), checked(hook.Tick + rule.InternalCooldownTicks)); }
        catch (OverflowException) { throw Failure(EngineFailureCodes.EffectArithmeticOverflow, "Rule timer/counter overflow."); }
        if (nodes is null) { nodes = new HashSet<(FighterId, StableId, FighterId)>(); visited.Add(hook.Root, nodes); }
        nodes.Add(rootKey); once.Add(onceKey); budgets[(candidate.Owner, rule.RuleId)] = charged;
        admitted++; pending.Add(candidate);
        return EffectAdmission.Queued;
    }
    internal EffectQueueCandidate? Dequeue()
    {
        if (pending.Count == 0) return null;
        pending.Sort(Compare);
        var next = pending[0]; pending.RemoveAt(0); executingDepths[next.Hook.Root] = next.Hook.Depth;
        return next;
    }
    private static int Compare(EffectQueueCandidate left, EffectQueueCandidate right)
    {
        var value = left.Hook.Depth.CompareTo(right.Hook.Depth);
        if (value == 0) value = left.Hook.SourceSequence.CompareTo(right.Hook.SourceSequence);
        if (value == 0) value = left.Rule.Priority.CompareTo(right.Rule.Priority);
        if (value == 0) value = left.Rule.RuleId.CompareTo(right.Rule.RuleId);
        if (value == 0) value = left.Owner.CompareTo(right.Owner);
        return value != 0 ? value : left.Recipient.CompareTo(right.Recipient);
    }
    private static void ValidateHook(EffectHook hook)
    {
        if (!Enum.IsDefined(typeof(EffectTrigger), hook.Trigger) || hook.Tick < 0 || hook.Depth < 0 ||
            hook.SourceSequence < 0 || hook.SourceSequence > EventId.MaximumSequence ||
            hook.SourceEventId != EventId.FromSequence(hook.SourceSequence))
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Invalid typed trigger hook/source.");
        RequireFighter(hook.Subject);
        ValidateOccurrence(hook.Occurrence); ValidateOccurrence(hook.Root);
        if (hook.Trigger == EffectTrigger.EndOfTick && hook.Occurrence != EffectOccurrence.EndOfTick(hook.Tick, hook.Subject) ||
            hook.Trigger != EffectTrigger.EndOfTick && hook.Occurrence.RealEventId != hook.SourceEventId)
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Hook occurrence does not identify its source.");
    }
    private static void ValidateOccurrence(EffectOccurrence occurrence)
    {
        if (occurrence.RealEventId.HasValue)
        {
            if (!EventId.TryParse(occurrence.RealEventId.Value.Value, out _) || occurrence.Tick != -1 || occurrence.Subject.HasValue)
                throw Failure(EngineFailureCodes.EffectInvalidMutation, "Invalid real event occurrence.");
        }
        else
        {
            if (occurrence.Tick < 0 || !occurrence.Subject.HasValue)
                throw Failure(EngineFailureCodes.EffectInvalidMutation, "Invalid EndOfTick occurrence.");
            RequireFighter(occurrence.Subject.Value);
        }
    }
    private static void RequireFighter(FighterId fighter)
    {
        if (fighter is not FighterId.FighterA and not FighterId.FighterB)
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Invalid fighter identity.");
    }
    private static EngineInvariantException Failure(ReasonCode code, string message) => new(code, "effects", message);
}
