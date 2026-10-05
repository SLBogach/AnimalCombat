using System.Reflection;
using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectRuntimeGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeadOrDefeatedRecipientCannotReceiveEndTickApplications(bool defeatedState)
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.EndOfTick)]); test.Start();
        if (defeatedState) test.State.FighterB.SetStateForTesting(FighterState.Defeated); else test.State.FighterB.SetHealthForTesting(0);
        test.EndTick(); Assert.Single(test.State.FighterA.Effects); Assert.Empty(test.State.FighterB.Effects);
        Assert.Contains(EffectAdmission.ConditionRejected, test.Runtime.Diagnostics);
    }

    [Fact]
    public void RemovalRemainsAllowedForAnAlreadyDefeatedRecipient()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(), Rule("rule_remove", trigger: EffectTrigger.EndOfTick, primitive: EffectPrimitive.RemoveEffect)]);
        test.Start(); test.State.FighterB.SetStateForTesting(FighterState.Defeated); test.EndTick();
        Assert.Equal(0, test.Runtime.ActiveCount); Assert.Empty(test.State.FighterA.Effects); Assert.Empty(test.State.FighterB.Effects);
    }

    [Fact]
    public void RemovalStillHonorsAnExplicitLivingTargetCondition()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(), Rule("rule_remove", trigger: EffectTrigger.EndOfTick,
            primitive: EffectPrimitive.RemoveEffect, condition: EffectCondition.LivingTarget)]);
        test.Start(); test.State.FighterB.SetStateForTesting(FighterState.Defeated); test.EndTick();
        Assert.Empty(test.State.FighterA.Effects); Assert.Single(test.State.FighterB.Effects);
        Assert.Contains(EffectAdmission.ConditionRejected, test.Runtime.Diagnostics);
    }

    [Fact]
    public void CorruptedUnknownConditionCannotActivateOrChargeEffectBudgets()
    {
        var rule = Rule();
        typeof(EffectRuleProfile).GetField("<Condition>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(rule, (EffectCondition)99);
        var test = new EffectRuntimeFixture([Effect()], [rule]); test.Start();
        Assert.Equal(0, test.Runtime.ActiveCount); Assert.All(test.Runtime.Diagnostics, x => Assert.Equal(EffectAdmission.ConditionRejected, x));
        Assert.Equal(default, test.Runtime.Store(FighterId.FighterA).Budget(new StableId("effect_a")));
    }

    [Fact]
    public void MutationWithoutExplicitOwnerUsesItsSemanticSubject()
    {
        // Exercise the defensive private fallback; all ordinary production paths pass an explicit MutationOwner.
        var profile = Effect(); var test = new EffectRuntimeFixture([profile], []); test.Start();
        var source = test.Journal.Drafts[0]; var occurrence = EffectOccurrence.Event(source.EventId);
        var context = new EffectHookContext(new EffectHook(EffectTrigger.BattleStart, 0, FighterId.FighterA, source.EventId,
            source.Sequence, occurrence, occurrence, 0), source, null, null, null, null, 0, false,
            new Dictionary<FighterId, IReadOnlyList<StableId>> { [FighterId.FighterA] = [], [FighterId.FighterB] = [] });
        var mutation = test.Runtime.Store(FighterId.FighterA).Clone().Apply(profile,
            new EffectOrigin(new StableId("fighter_a"), null, new StableId("rule_a")), 0, EventId.FromSequence(1));
        var method = typeof(EffectRuntime).GetMethod("PublishMutations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var children = (IEnumerable<EffectHookContext>)method.Invoke(test.Runtime, [test.State, test.Emitter, FighterId.FighterA, mutation, context])!;
        Assert.Single(children); Assert.Equal(1, test.Runtime.ActiveCount);
        var added = test.Journal.Drafts[^1]; Assert.Equal(FighterId.FighterA, added.ActorId); Assert.Null(added.TargetId);
        Assert.Equal(source.EventId, added.SourceEventId); Assert.Null(added.Rng);
    }

    [Theory]
    [InlineData("not-impact")]
    [InlineData("action")]
    [InlineData("decision")]
    [InlineData("action-null")]
    [InlineData("decision-null")]
    [InlineData("decision-unowned")]
    [InlineData("both-decisions-null")]
    [InlineData("hit")]
    public void ControlEndedActionOwnerMustComeFromMatchingImpactLineage(string mismatch)
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.ControlEnded, ownerKind: EffectOwnerKind.Action, owner: "action_a")]); test.Start();
        var source = test.Journal.Drafts[0]; var before = new FramePair(test.State.FighterA.ToFrame(), test.State.FighterB.ToFrame());
        if (mismatch != "not-impact")
        {
            CombatEventPayload payload = mismatch == "hit"
                ? new AttackHitPayload([source.EventId], new ExternalId("impact:guard"), new ExternalId("hit:guard"), 0, 0, 10000, MovementDirection.Right, [])
                : new DamageAppliedPayload([source.EventId], new ExternalId("impact:guard"), new ExternalId("damage:guard"), new DamageBreakdown(0, 0, 0, 0, 0, 0, 600, 0), 100, 100, [], false);
            test.Emitter.Emit(0, payload, FighterId.FighterA, FighterId.FighterB,
                mismatch == "action-null" ? null : new StableId(mismatch == "action" ? "action_b" : "action_a"),
                decisionId: mismatch is "decision-null" or "both-decisions-null" ? null : new DecisionId("dec-fighter_a-000001"), sourceEventId: source.EventId,
                before: before, after: before);
            source = test.Emitter.LastDraft!;
        }
        test.Emitter.Emit(0, new StateChangedPayload([source.EventId], FighterState.Stunned, FighterState.DecisionReady, 0, null, null, ImmunityResult.Allowed),
            actorId: FighterId.FighterB, actionId: new StableId("action_a"),
            decisionId: mismatch is "decision-unowned" or "both-decisions-null" ? null : new DecisionId(mismatch == "decision" ? "dec-fighter_a-000002" : "dec-fighter_a-000001"),
            sourceEventId: source.EventId, before: new FramePair(test.State.FighterB.ToFrame(), null), after: new FramePair(test.State.FighterB.ToFrame(), null));
        test.Runtime.CloseEvents(test.State, test.Emitter, test.Journal.Drafts.ToArray());
        var matched = mismatch is "both-decisions-null" or "hit";
        Assert.Equal(matched ? 1 : 0, test.Runtime.ActiveCount);
        if (matched) Assert.Single(test.State.FighterA.Effects); else Assert.Empty(test.State.FighterA.Effects);
        Assert.Empty(test.State.FighterB.Effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalHitConditionNeedsAnEarlierAttackHitNotJustPositiveDamage(bool hit)
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.DamageTaken, condition: EffectCondition.NormalHit)]); test.Start();
        var source = test.Journal.Drafts[0]; var frames = new FramePair(test.State.FighterA.ToFrame(), test.State.FighterB.ToFrame());
        if (hit)
        {
            test.Emitter.Emit(0, new AttackHitPayload([source.EventId], new ExternalId("impact:normal"), new ExternalId("hit:normal"),
                0, 0, 10000, MovementDirection.Right, []), FighterId.FighterA, FighterId.FighterB, before: frames, after: frames);
            source = test.Emitter.LastDraft!;
        }
        var health = test.State.FighterB.Health;
        test.Emitter.Emit(0, new DamageAppliedPayload([source.EventId], new ExternalId("impact:normal"), new ExternalId("damage:normal"),
            new DamageBreakdown(1, 1, 1, 1, 1, 0, 600, 0), health, health - 1, [], false), FighterId.FighterA, FighterId.FighterB,
            sourceEventId: source.EventId, before: frames, after: frames);
        test.Runtime.CloseEvents(test.State, test.Emitter, test.Journal.Drafts.ToArray());
        Assert.Equal(hit ? 1 : 0, test.Runtime.ActiveCount); Assert.Empty(test.State.FighterA.Effects);
        if (hit) Assert.Single(test.State.FighterB.Effects); else Assert.Empty(test.State.FighterB.Effects);
    }
}
