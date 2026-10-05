using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectTriggerQueueGuardTests
{
    private static EffectQueueCandidate Candidate(int tick = 0, bool endTick = false)
    {
        var id = EventId.FromSequence(1);
        var occurrence = endTick ? EffectOccurrence.EndOfTick(tick, FighterId.FighterA) : EffectOccurrence.Event(id);
        return new EffectQueueCandidate(new EffectHook(endTick ? EffectTrigger.EndOfTick : EffectTrigger.EffectAdded, tick, FighterId.FighterA,
            id, 1, occurrence, occurrence, 0), new EffectRuleProfile(new StableId("rule_a"), EffectOwnerKind.Global,
            new StableId("global"), EffectTrigger.EffectAdded, EffectRecipient.Self, EffectCondition.LivingTarget,
            EffectPrimitive.ApplyEffect, new StableId("effect_a"), 0, 0, 10, 100, true), FighterId.FighterA, FighterId.FighterA, true, true);
    }
    [Theory]
    [InlineData(0, 1)]
    [InlineData(33, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 4097)]
    public void CapacityBoundsAreValidated(int depth, int tickCap) => Assert.Throws<ArgumentOutOfRangeException>(() => new EffectTriggerQueue(depth, tickCap));

    [Theory]
    [InlineData("null-level")]
    [InlineData("null-rule")]
    [InlineData("trigger")]
    [InlineData("tick")]
    [InlineData("depth")]
    [InlineData("sequence-negative")]
    [InlineData("sequence-over")]
    [InlineData("event-mismatch")]
    [InlineData("subject")]
    [InlineData("owner")]
    [InlineData("recipient")]
    [InlineData("real-id")]
    [InlineData("real-tick")]
    [InlineData("real-subject")]
    [InlineData("end-tick")]
    [InlineData("end-subject-missing")]
    [InlineData("end-subject-invalid")]
    [InlineData("end-occurrence-mismatch")]
    [InlineData("real-occurrence-mismatch")]
    [InlineData("real-with-end-occurrence")]
    [InlineData("invalid-root")]
    public void InvalidTypedHooksAndOccurrencesNeverAdmitOrChargeWork(string invalid)
    {
        var queue = new EffectTriggerQueue(32, 4096); var candidate = Candidate(); var hook = candidate.Hook;
        switch (invalid)
        {
            case "null-level": Assert.Throws<ArgumentNullException>(() => queue.AdmitLevel(null!)); return;
            case "null-rule": candidate = candidate with { Rule = null! }; break;
            case "trigger": hook = hook with { Trigger = (EffectTrigger)99 }; break;
            case "tick": hook = hook with { Tick = -1 }; break;
            case "depth": hook = hook with { Depth = -1 }; break;
            case "sequence-negative": hook = hook with { SourceSequence = -1 }; break;
            case "sequence-over": hook = hook with { SourceSequence = EventId.MaximumSequence + 1 }; break;
            case "event-mismatch": hook = hook with { SourceEventId = EventId.FromSequence(2) }; break;
            case "subject": hook = hook with { Subject = (FighterId)99 }; break;
            case "owner": candidate = candidate with { Owner = (FighterId)99 }; break;
            case "recipient": candidate = candidate with { Recipient = (FighterId)99 }; break;
            case "real-id": hook = hook with { Occurrence = new EffectOccurrence(default(EventId), -1, null) }; break;
            case "real-tick": hook = hook with { Occurrence = new EffectOccurrence(hook.SourceEventId, 0, null) }; break;
            case "real-subject": hook = hook with { Occurrence = new EffectOccurrence(hook.SourceEventId, -1, FighterId.FighterA) }; break;
            case "end-tick": hook = hook with { Occurrence = new EffectOccurrence(null, -1, FighterId.FighterA) }; break;
            case "end-subject-missing": hook = hook with { Occurrence = new EffectOccurrence(null, 0, null) }; break;
            case "end-subject-invalid": hook = hook with { Occurrence = EffectOccurrence.EndOfTick(0, (FighterId)99) }; break;
            case "end-occurrence-mismatch": hook = Candidate(endTick: true).Hook with { Occurrence = EffectOccurrence.EndOfTick(1, FighterId.FighterA) }; break;
            case "real-occurrence-mismatch": hook = hook with { Occurrence = EffectOccurrence.Event(EventId.FromSequence(2)) }; break;
            case "real-with-end-occurrence": hook = hook with { Occurrence = EffectOccurrence.EndOfTick(0, FighterId.FighterA) }; break;
            case "invalid-root": hook = hook with { Root = default }; break;
        }
        candidate = candidate with { Hook = hook };
        if (invalid == "null-rule") Assert.Throws<ArgumentException>(() => queue.AdmitLevel([candidate]));
        else Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => queue.AdmitLevel([candidate])).Code);
        Assert.Equal(0, queue.AdmittedThisTick); Assert.Equal(0, queue.PendingCount); Assert.Null(queue.Dequeue());
        Assert.Equal(default, queue.Budget(FighterId.FighterA, new StableId("rule_a")));
    }

    [Fact]
    public void BackwardTickIsRejectedEvenWithAnEmptyQueueAndPreservesBudgets()
    {
        var queue = new EffectTriggerQueue(1, 1); var candidate = Candidate(2);
        Assert.Equal(EffectAdmission.Queued, Assert.Single(queue.AdmitLevel([candidate])).Admission);
        Assert.NotNull(queue.Dequeue()); var budget = queue.Budget(candidate.Owner, candidate.Rule.RuleId);
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, Assert.Throws<EngineInvariantException>(() => queue.AdmitLevel([Candidate(1)])).Code);
        Assert.Equal(budget, queue.Budget(candidate.Owner, candidate.Rule.RuleId)); Assert.Equal(0, queue.PendingCount);
    }
}
