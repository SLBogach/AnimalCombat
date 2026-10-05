using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectTriggerQueueFoundationTests
{
    private static EffectRuleProfile Rule(string id, int priority = 0, int cooldown = 0, int tickCap = 999, int battleCap = 999) =>
        new(new StableId(id), EffectOwnerKind.Global, new StableId("global"), EffectTrigger.EffectAdded,
            EffectRecipient.Self, EffectCondition.LivingTarget, EffectPrimitive.ApplyEffect, new StableId("effect_a"), priority, cooldown, tickCap, battleCap, true);
    private static EffectQueueCandidate Candidate(string rule = "rule_a", int tick = 10, long source = 1, int depth = 0,
        long root = 1, FighterId owner = FighterId.FighterA, FighterId recipient = FighterId.FighterA,
        int priority = 0, bool eligible = true, bool effectReady = true, bool endOfTick = false)
    {
        var occurrence = endOfTick ? EffectOccurrence.EndOfTick(tick, owner) : EffectOccurrence.Event(EventId.FromSequence(source));
        var rootOccurrence = endOfTick ? occurrence : EffectOccurrence.Event(EventId.FromSequence(root));
        var hook = new EffectHook(endOfTick ? EffectTrigger.EndOfTick : EffectTrigger.EffectAdded, tick, owner,
            EventId.FromSequence(source), source, occurrence, rootOccurrence, depth);
        return new EffectQueueCandidate(hook, Rule(rule, priority), owner, recipient, eligible, effectReady);
    }
    private static EffectAdmission Admit(EffectTriggerQueue queue, EffectQueueCandidate candidate) => Assert.Single(queue.AdmitLevel([candidate])).Admission;
    private static void Drain(EffectTriggerQueue queue) { while (queue.Dequeue().HasValue) { } }

    [Fact]
    public void CausalLevelsAndSequencePriorityIdOwnerRecipientHaveCanonicalOrder()
    {
        var entries = new[]
        {
            Candidate("rule_z", source: 3), Candidate("rule_b", source: 2, priority: 1),
            Candidate("rule_a", source: 2, priority: 1), Candidate("rule_first", source: 2, priority: -1),
            Candidate("rule_child", source: 1, depth: 1), Candidate("rule_same", source: 2, owner: FighterId.FighterB),
            Candidate("rule_same", source: 2, owner: FighterId.FighterA, recipient: FighterId.FighterB), Candidate("rule_same", source: 2),
        };
        string[] Run(IEnumerable<EffectQueueCandidate> items)
        {
            var queue = new EffectTriggerQueue(8, 128);
            Assert.All(queue.AdmitLevel(items), x => Assert.Equal(EffectAdmission.Queued, x.Admission));
            var actual = new List<string>(); EffectQueueCandidate? item;
            while ((item = queue.Dequeue()).HasValue) actual.Add(item.Value.Rule.RuleId.Value + ":" + item.Value.Owner + ":" + item.Value.Recipient);
            return actual.ToArray();
        }
        var first = Run(entries);
        Assert.Equal(first, Run(entries.Reverse()));
        Assert.Equal(new[] { "rule_first", "rule_same", "rule_same", "rule_same", "rule_a", "rule_b", "rule_z", "rule_child" }, first.Select(x => x.Split(':')[0]));
    }
    [Fact]
    public void SemanticCyclesSuppressBeforeDepthAndDoNotConsumeBudget()
    {
        var queue = new EffectTriggerQueue(8, 128);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate()));
        Drain(queue);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate("rule_b", source: 2, depth: 1)));
        Drain(queue);
        var before = queue.Budget(FighterId.FighterA, new StableId("rule_a"));
        Assert.Equal(EffectAdmission.TriggerCycleSuppressed, Admit(queue, Candidate(source: 3, depth: 9)));
        Assert.Equal(before, queue.Budget(FighterId.FighterA, new StableId("rule_a")));
        Assert.Equal(2, queue.AdmittedThisTick); Assert.Equal(0, queue.PendingCount);
    }
    [Fact]
    public void AcyclicDepthEightIsLegalNineFailsInPreviewAndDoesNotAffectOriginal()
    {
        var authoritative = new EffectTriggerQueue(8, 128);
        var preview = authoritative.Clone();
        for (var depth = 0; depth <= 8; depth++)
        {
            Assert.Equal(EffectAdmission.Queued, Admit(preview, Candidate("rule_" + depth, source: depth + 1, depth: depth)));
            Drain(preview);
        }
        var failure = Assert.Throws<EngineInvariantException>(() => Admit(preview, Candidate("rule_over", source: 10, depth: 9)));
        Assert.Equal(EngineFailureCodes.EffectTriggerDepthExceeded, failure.Code);
        Assert.Equal(9, preview.AdmittedThisTick); Assert.Equal(0, authoritative.AdmittedThisTick);
        Assert.Equal(default, authoritative.Budget(FighterId.FighterA, new StableId("rule_0")));
    }
    [Fact]
    public void OneHundredTwentyEightAdmissionsLegalNextFatalAndNewTickResets()
    {
        var queue = new EffectTriggerQueue(8, 128);
        for (var index = 1; index <= 128; index++)
            Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate("rule_" + index, source: index, root: index)));
        Assert.Equal(128, queue.PendingCount); Assert.Equal(128, queue.AdmittedThisTick);
        Assert.Equal(EffectAdmission.ConditionRejected, Admit(queue, Candidate("ineligible", source: 129, root: 129, eligible: false)));
        var failure = Assert.Throws<EngineInvariantException>(() => Admit(queue, Candidate("eligible", source: 130, root: 130)));
        Assert.Equal(EngineFailureCodes.EffectTriggerTickCapExceeded, failure.Code);
        Drain(queue);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate(tick: 11, source: 131, root: 131)));
        Assert.Equal(1, queue.AdmittedThisTick);
    }
    [Fact]
    public void SameOccurrenceCannotRunAgainUnderDifferentRootButNewSourceMayRun()
    {
        var queue = new EffectTriggerQueue(8, 128);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate())); Drain(queue);
        Assert.Equal(EffectAdmission.OncePerEventSuppressed, Admit(queue, Candidate(root: 2)));
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate(source: 3, root: 3)));
        Assert.Equal(2, queue.AdmittedThisTick);
    }
    [Fact]
    public void QuietConsecutiveEndOfTickOccurrencesDifferEvenWhenSourceEventIsUnchanged()
    {
        var queue = new EffectTriggerQueue(8, 128);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate(tick: 10, endOfTick: true))); Drain(queue);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate(tick: 11, endOfTick: true))); Drain(queue);
        Assert.Equal(2, queue.Budget(FighterId.FighterA, new StableId("rule_a")).BattleCount);
    }
    [Fact]
    public void GlobalRuleBudgetsArePerSubjectRatherThanSharedBetweenFighters()
    {
        var queue = new EffectTriggerQueue(8, 128);
        var rule = Rule("rule_a", tickCap: 1);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate() with { Rule = rule }));
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate(source: 2, root: 2, owner: FighterId.FighterB, recipient: FighterId.FighterB) with { Rule = rule }));
        Assert.Equal(1, queue.Budget(FighterId.FighterA, rule.RuleId).TickCount);
        Assert.Equal(1, queue.Budget(FighterId.FighterB, rule.RuleId).TickCount);
    }
    [Fact]
    public void EligibilityRuleAndEffectBudgetsAreCheckedBeforeAdmissionAndNoopStillChargesRule()
    {
        var queue = new EffectTriggerQueue(8, 128);
        var rule = Rule("rule_a", cooldown: 5, tickCap: 1, battleCap: 2);
        Assert.Equal(EffectAdmission.ConditionRejected, Admit(queue, Candidate(eligible: false)));
        Assert.Equal(EffectAdmission.EffectBudgetRejected, Admit(queue, Candidate(effectReady: false)));
        Assert.Equal(0, queue.AdmittedThisTick);
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate() with { Rule = rule })); Drain(queue);
        for (var tick = 10; tick < 15; tick++)
            Assert.Equal(EffectAdmission.RuleBudgetRejected, Admit(queue, Candidate(tick: tick, source: tick, root: tick) with { Rule = rule }));
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate(tick: 15, source: 15, root: 15) with { Rule = rule })); Drain(queue);
        Assert.Equal(EffectAdmission.RuleBudgetRejected, Admit(queue, Candidate(tick: 20, source: 20, root: 20) with { Rule = rule }));
        Assert.Equal(2, queue.Budget(FighterId.FighterA, rule.RuleId).BattleCount);
    }
    [Fact]
    public void CorruptCounterTimerIsTypedAndNoPartialAdmissionOccurs()
    {
        var queue = new EffectTriggerQueue(8, 128);
        var failure = Assert.Throws<EngineInvariantException>(() => Admit(queue, Candidate() with { Rule = Rule("rule_a", cooldown: int.MaxValue) }));
        Assert.Equal(EngineFailureCodes.EffectArithmeticOverflow, failure.Code);
        Assert.Equal(0, queue.AdmittedThisTick); Assert.Equal(0, queue.PendingCount);
        Assert.Equal(default, queue.Budget(FighterId.FighterA, new StableId("rule_a")));
    }
    [Fact]
    public void NoPendingWorkMayLeakAcrossTickOrReturnToEarlierCausalLevel()
    {
        var queue = new EffectTriggerQueue(8, 128);
        Admit(queue, Candidate(depth: 1));
        Assert.Throws<EngineInvariantException>(() => Admit(queue, Candidate(tick: 11, source: 2, root: 2)));
        Drain(queue);
        Assert.Throws<EngineInvariantException>(() => Admit(queue, Candidate("rule_b", source: 2, root: 1, depth: 0)));
        Assert.Equal(EffectAdmission.Queued, Admit(queue, Candidate("new_root", source: 3, root: 3, depth: 0)));
    }
}
