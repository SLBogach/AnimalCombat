using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Random;
using Battle.Core.Resolution;
using Battle.Core.Safety;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectControlRuntimeTests
{
    [Fact]
    public void AbsoluteControlTimerProjectionUsesCurrentTickWithoutPerformingExpiryOrRevivingDefeatedFighters()
    {
        var test = Create();
        var a = test.State.FighterA; var b = test.State.FighterB;
        a.SynchronizeEffectControlTimer(2); Assert.Null(a.StateTicksRemaining);
        a.ApplyHardControl(5); a.SynchronizeEffectControlTimer(2); Assert.Equal(5, a.StateTicksRemaining);
        a.BeginStun(0, 5, FighterId.FighterB, b.NextDecisionId());
        a.SynchronizeEffectControlTimer(2); Assert.Equal(3, a.StateTicksRemaining);
        a.SynchronizeEffectControlTimer(6); Assert.Equal(0, a.StateTicksRemaining);
        Assert.Equal(FighterState.Stunned, a.State);
        b.BeginKnockdown(new KnockdownTimeline(0, 1, 3, 7), FighterId.FighterA, a.NextDecisionId());
        b.SynchronizeEffectControlTimer(3); Assert.Equal(4, b.StateTicksRemaining);
        b.SynchronizeEffectControlTimer(7); Assert.Equal(0, b.StateTicksRemaining);
        Assert.Equal(FighterState.KnockedDown, b.State);
        b.ApplyDamage(b.Health); b.ApplyDefeat(); b.SynchronizeEffectControlTimer(9);
        Assert.Equal(FighterState.Defeated, b.State); Assert.Null(b.StateTicksRemaining);
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-001")]
    public void FatigueLookupIsAppliedExactlyOnceInTheActualRuntimeCache()
    {
        var test = Create(); test.Start();
        Assert.Equal(1000, test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.HardControlDuration));
        for (var stack = 1; stack <= 3; stack++)
        {
            Stun(test, 1); ExpireControls(test, stack);
            Assert.Equal(new[] { 1000, 750, 500, 250 }[stack], test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.HardControlDuration));
            Assert.Equal(stack, Role(test, EffectSemanticRole.ControlFatigue).Stacks);
        }
        Assert.Equal(28, Role(test, EffectSemanticRole.ControlImmunity).EndExclusiveTick);
    }

    [Theory, Trait("AcceptanceId", "WP10-CTRL-005")]
    [InlineData("stun"), InlineData("knockdown"), InlineData("grab")]
    public void ImmunityKeepsActionsAndDamageButPreventsNewControlAndResetsStagger(string category)
    {
        var test = Create(initial: EffectSemanticRole.ControlImmunity); test.Start();
        Commit(test, FighterId.FighterB, Action("action_b", defense: "block"), startup: 4);
        var timer = test.State.FighterB.StateTicksRemaining;
        var hp = test.State.FighterB.Health;
        Commit(test, FighterId.FighterA, Action("action_a", stagger: category == "grab" ? 0 : 260,
            tags: category == "knockdown" ? ["strike", "knockdown"] : category == "grab" ? ["grab"] : ["strike"],
            primitive: category == "grab" ? HitPrimitiveKind.Grab : HitPrimitiveKind.Hit));
        Resolve(test);
        Assert.Equal(FighterState.AttackPrepare, test.State.FighterB.State);
        Assert.Equal(new StableId("action_b"), test.State.FighterB.ActionId);
        Assert.Equal(timer, test.State.FighterB.StateTicksRemaining);
        Assert.Equal(0, test.State.FighterB.Stagger);
        Assert.DoesNotContain(test.Journal.Drafts, x => x.EventType == CombatEventType.ActionCancelled);
        var prevented = Assert.Single(test.Journal.Drafts, x => x.Payload is StateChangedPayload { ImmunityResult: ImmunityResult.Prevented });
        var change = Assert.IsType<StateChangedPayload>(prevented.Payload);
        Assert.Equal(change.OldState, change.NewState); Assert.Equal(0, change.DurationTicks);
        if (category != "grab")
        {
            Assert.True(test.State.FighterB.Health < hp);
            Assert.Single(test.Journal.Drafts, x => x.ReasonCodes.Any(r => r.Value == "StaggerReset"));
        }
        else { Assert.Equal(hp, test.State.FighterB.Health); Assert.Null(test.State.ActiveGrab); }
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-006")]
    public void RepeatedProtectedControlDoesNotExtendImmunityAndExpiryRestoresEligibility()
    {
        var test = Create(initial: EffectSemanticRole.ControlImmunity); test.Start();
        Stun(test, 8); test.At(10); Stun(test, 8);
        Assert.Equal(25, Role(test, EffectSemanticRole.ControlImmunity).EndExclusiveTick);
        Assert.Equal(FighterState.DecisionReady, test.State.FighterB.State);
        test.At(24); test.EndTick(); test.At(25);
        Assert.DoesNotContain(test.Runtime.Store(FighterId.FighterB).Active, x => x.Profile.SemanticRole == EffectSemanticRole.ControlImmunity);
        Stun(test, 8);
        Assert.Equal(FighterState.Stunned, test.State.FighterB.State);
        Assert.Equal(8, test.State.FighterB.StateTicksRemaining);
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-007")]
    public void FatigueRefreshesOneSharedLifetimeAndExpiresTheWholeGroup()
    {
        var test = Create(); test.Start(); Stun(test, 1); ExpireControls(test, 1);
        test.At(10); Stun(test, 1); ExpireControls(test, 11);
        Assert.Equal(2, Role(test, EffectSemanticRole.ControlFatigue).Stacks);
        Assert.Equal(111, Role(test, EffectSemanticRole.ControlFatigue).EndExclusiveTick);
        test.At(109); test.EndTick(); Assert.Equal(500, test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.HardControlDuration));
        test.At(110); test.EndTick(); test.At(111);
        Assert.Equal(1000, test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.HardControlDuration));
        Assert.DoesNotContain(test.Runtime.Store(FighterId.FighterB).Active, x => x.Profile.SemanticRole == EffectSemanticRole.ControlFatigue);
    }

    [Fact, Trait("AcceptanceId", "WP10-CTRL-009")]
    public void SameCommitMultiHitDoesNotRefreshStunAndNewCommitCanStartAnotherLifecycle()
    {
        var test = Create(); test.Start();
        Commit(test, FighterId.FighterA, Action("action_a", stagger: 260, hits: 5));
        Resolve(test); Assert.Equal(8, test.State.FighterB.StateTicksRemaining);
        test.At(1); ExpireControls(test, 1); Resolve(test);
        Assert.Equal(7, test.State.FighterB.StateTicksRemaining);
        Assert.Single(test.Journal.Drafts, x => x.Payload is StateChangedPayload { NewState: FighterState.Stunned });
        ExpireControls(test, 8); Assert.Equal(FighterState.DecisionReady, test.State.FighterB.State);
        _ = test.State.FighterA.CancelCurrentAction();
        Commit(test, FighterId.FighterA, Action("action_a", stagger: 260)); Resolve(test);
        Assert.Equal(6, test.State.FighterB.StateTicksRemaining); // fatigue750 after the first real ControlEnded
        Assert.Equal(2, test.Journal.Drafts.Count(x => x.Payload is StateChangedPayload { NewState: FighterState.Stunned }));
        foreach (var protection in new[] { EffectSemanticRole.ControlImmunity, EffectSemanticRole.GrabLockout })
        {
            var fallback = Create(initial: protection); fallback.Start();
            Assert.False(EffectControlSystem.Allows(fallback.State, FighterId.FighterB, ControlCategory.Grab));
            Assert.False(fallback.State.CanBeGrabbed(FighterId.FighterB, 0));
            Assert.Throws<EngineInvariantException>(() => fallback.State.StartGrab(new ExternalId("grab:fallback"),
                FighterId.FighterA, FighterId.FighterB, 0, 12));
            Assert.Null(fallback.State.ActiveGrab);
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-KDN-001")]
    public void KnockdownUsesExactHalfOpenStageBoundariesAndNeverBecomesDecisionReadyEarly()
    {
        var test = Create(); test.Start(); test.At(10);
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "knockdown"])); Resolve(test);
        Assert.Equal(new KnockdownTimeline(10, 12, 18, 21), test.State.FighterB.Knockdown);
        for (var tick = 10; tick <= 21; tick++)
        {
            ExpireControls(test, tick);
            if (tick == 21) break;
            var fighter = test.State.FighterB;
            Assert.False(fighter.IsDecisionReady); Assert.Equal(FighterState.KnockedDown, fighter.State);
            Assert.Null(fighter.ActionId); Assert.Null(fighter.ActionPhase);
            Assert.Equal(21 - tick, fighter.StateTicksRemaining);
            Assert.Equal(tick < 12 ? KnockdownStage.Fall : tick < 18 ? KnockdownStage.Grounded : KnockdownStage.GetUp, fighter.KnockdownPhase);
        }
        Assert.True(test.State.FighterB.IsDecisionReady); Assert.Null(test.State.FighterB.Knockdown);
        Assert.Equal(new[] { "KnockdownGrounded", "KnockdownGetUp", "KnockdownCompleted" }, test.Journal.Drafts
            .Where(x => x.ReasonCodes.Any(r => r.Value is "KnockdownGrounded" or "KnockdownGetUp" or "KnockdownCompleted"))
            .Select(x => x.ReasonCodes.Single().Value));
        Assert.Equal(1, Role(test, EffectSemanticRole.ControlFatigue).Stacks);
        Assert.Equal(46, Role(test, EffectSemanticRole.WakeupImmunity).EndExclusiveTick);
    }

    [Theory, Trait("AcceptanceId", "WP10-KDN-004")]
    [InlineData(10), InlineData(12), InlineData(18)]
    public void OrdinaryHitsMissInEveryKnockdownStageWithoutDefenseDraws(int tick)
    {
        var test = Create(); test.Start();
        _ = test.State.FighterB.BeginKnockdown(new KnockdownTimeline(10, 12, 18, 21), FighterId.FighterA, test.State.FighterA.NextDecisionId());
        test.At(tick); ExpireControls(test, tick);
        var hp = test.State.FighterB.Health; var draws = test.State.Rng.Resolution.NextDrawIndex;
        Commit(test, FighterId.FighterA, Action("action_a")); Resolve(test);
        Assert.Equal(hp, test.State.FighterB.Health); Assert.Equal(draws, test.State.Rng.Resolution.NextDrawIndex);
        Assert.Equal(AttackMissReason.InvalidTarget, Assert.IsType<AttackMissedPayload>(test.Journal.Drafts.Last().Payload).MissReason);
    }

    [Theory, Trait("AcceptanceId", "WP10-KDN-007")]
    [InlineData(EffectSemanticRole.ControlImmunity), InlineData(EffectSemanticRole.WakeupImmunity)]
    [InlineData(EffectSemanticRole.None)]
    public void ControlAndWakeupImmunityPreventKnockdownButNotValidDamage(EffectSemanticRole immunity)
    {
        var test = Create(initial: immunity, kind: immunity == EffectSemanticRole.None ? ActionInterruptKind.Unstoppable : ActionInterruptKind.Cancelable);
        test.Start(); var health = test.State.FighterB.Health;
        if (immunity == EffectSemanticRole.None) Commit(test, FighterId.FighterB, Action("action_b"), startup: 3);
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "knockdown"])); Resolve(test);
        Assert.True(test.State.FighterB.Health < health); Assert.Null(test.State.FighterB.Knockdown);
        Assert.Equal(immunity == EffectSemanticRole.None ? FighterState.AttackPrepare : FighterState.DecisionReady, test.State.FighterB.State);
        Assert.Contains(test.Journal.Drafts, x => x.Payload is StateChangedPayload { ImmunityResult: ImmunityResult.Prevented });
    }

    [Theory, Trait("AcceptanceId", "WP10-INT-002")]
    [InlineData(HitInterruptStrength.Light, false), InlineData(HitInterruptStrength.Medium, false), InlineData(HitInterruptStrength.Heavy, true)]
    public void ArmoredStartupOnlyCancelsAtItsHeavyThresholdAndStillTakesDamage(HitInterruptStrength strength, bool cancelled)
    {
        var test = Create(strength: strength, kind: ActionInterruptKind.Armored); test.Start();
        test.State.FighterB.SetResourceForTesting(10);
        Commit(test, FighterId.FighterB, Action("action_b"), startup: 3, energy: 5, resource: 3, cooldown: 20);
        var hp = test.State.FighterB.Health; var energy = test.State.FighterB.Energy; var resource = test.State.FighterB.Resource;
        Commit(test, FighterId.FighterA, Action("action_a")); Resolve(test);
        Assert.True(test.State.FighterB.Health < hp);
        Assert.Equal(cancelled, !test.State.FighterB.ActionId.HasValue);
        Assert.Equal(energy, test.State.FighterB.Energy); Assert.Equal(resource, test.State.FighterB.Resource);
        Assert.Equal(20, test.State.FighterB.CooldownFor(new StableId("action_b")));
        Assert.Equal(cancelled ? 1 : 0, test.Journal.Drafts.Count(x => x.Payload is ActionCancelledPayload));
    }

    [Theory, Trait("AcceptanceId", "WP10-INT-003")]
    [InlineData(false, 3, false), InlineData(true, 3, false), InlineData(false, 0, true), InlineData(true, 0, true)]
    public void UnstoppableBlocksOnlyConfiguredControlInsideProtectedPhases(bool knockdown, int startup, bool controlled)
    {
        var test = Create(kind: ActionInterruptKind.Unstoppable); test.Start();
        Commit(test, FighterId.FighterB, Action("action_b", defense: "block"), startup: startup);
        var hp = test.State.FighterB.Health;
        Commit(test, FighterId.FighterA, Action("action_a", stagger: 260, tags: knockdown ? ["strike", "knockdown"] : ["strike"], unblockable: true)); Resolve(test);
        Assert.True(test.State.FighterB.Health < hp);
        Assert.Equal(controlled, test.State.FighterB.State is FighterState.Stunned or FighterState.KnockedDown);
        Assert.Equal(controlled ? 1 : 0, test.Journal.Drafts.Count(x => x.Payload is ActionCancelledPayload));
        Assert.True(EffectControlSystem.Allows(test.State, FighterId.FighterB, ControlCategory.Grab));
        Assert.True(EffectControlSystem.Allows(test.State, FighterId.FighterB, ControlCategory.Defeat));
    }

    [Theory, Trait("AcceptanceId", "WP10-INT-008")]
    [InlineData(false), InlineData(true)]
    public void HardControlCancelsExactlyOnceOnlyWhenAllowed(bool immune)
    {
        var test = Create(initial: immune ? EffectSemanticRole.ControlImmunity : EffectSemanticRole.None); test.Start();
        Commit(test, FighterId.FighterB, Action("action_b"), startup: 3);
        Commit(test, FighterId.FighterA, Action("action_a", stagger: 260, hits: 2)); Resolve(test);
        test.At(1); Resolve(test);
        Assert.Equal(immune ? 0 : 1, test.Journal.Drafts.Count(x => x.Payload is ActionCancelledPayload));
        Assert.Equal(immune, test.State.FighterB.ActionId.HasValue);
    }

    [Fact, Trait("AcceptanceId", "WP10-INT-004")]
    public void UnstoppableCannotIgnoreGrabDefeatOrTheDeterministicCounterPolicy()
    {
        foreach (var grab in new[] { false, true })
        {
            var test = Create(kind: ActionInterruptKind.Unstoppable); test.Start();
            Commit(test, FighterId.FighterB, Action("action_b"), startup: 4);
            if (!grab) test.State.FighterB.SetHealthForTesting(1);
            Commit(test, FighterId.FighterA, Action("action_a", tags: grab ? ["grab"] : ["strike"],
                primitive: grab ? HitPrimitiveKind.Grab : HitPrimitiveKind.Hit)); Resolve(test);
            Assert.Equal(grab ? FighterState.Grabbed : FighterState.Defeated, test.State.FighterB.State);
            var cancelled = Assert.Single(test.Journal.Drafts, x => x.Payload is ActionCancelledPayload);
            Assert.Equal(grab ? "Grabbed" : "Defeat", Assert.IsType<ActionCancelledPayload>(cancelled.Payload).CancelReason.Value);
            Assert.Empty(Assert.IsType<ActionCancelledPayload>(cancelled.Payload).SurvivingIntentIds);
        }
        var counter = Create(attackerKind: ActionInterruptKind.Unstoppable); counter.Start();
        Commit(counter, FighterId.FighterA, Action("action_a", hits: 2));
        Commit(counter, FighterId.FighterB, Action("action_b", tags: ["counter"], primitive: HitPrimitiveKind.Counter));
        Resolve(counter);
        Assert.Equal(0UL, counter.State.Rng.Resolution.NextDrawIndex);
        Assert.Single(counter.Journal.Drafts, x => x.EventType == CombatEventType.Countered);
        var cancellation = Assert.Single(counter.Journal.Drafts, x => x.Payload is ActionCancelledPayload);
        Assert.Equal("Countered", Assert.IsType<ActionCancelledPayload>(cancellation.Payload).CancelReason.Value);
        Assert.Null(counter.State.FighterA.ActiveCombatAction);
    }

    [Theory, Trait("AcceptanceId", "WP10-CTRL-011")]
    [InlineData("success_block"), InlineData("no_block"), InlineData("miss"), InlineData("dodge"), InlineData("counter")]
    public void GuardBreakNeverActivatesOutsideFailedEligibleActiveBlock(string scenario)
    {
        var test = Create(seed: SeedFor(0)); test.Start();
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "guard_break"]));
        if (scenario == "success_block") Commit(test, FighterId.FighterB, Action("action_b", defense: "block"));
        if (scenario == "dodge") Commit(test, FighterId.FighterB, Action("action_b", defense: "dodge"));
        if (scenario == "counter") Commit(test, FighterId.FighterB, Action("action_b", primitive: HitPrimitiveKind.Counter, tags: ["counter"]));
        if (scenario == "miss") test.State.FighterB.SetPositionForTesting(test.State.FighterA.Position);
        Resolve(test);
        Assert.DoesNotContain(test.Journal.Drafts, x => x.ReasonCodes.Any(r => r.Value == "GuardBreak"));
        Assert.DoesNotContain(test.Runtime.Store(FighterId.FighterB).Active, x => x.Profile.SemanticRole == EffectSemanticRole.GuardBreak);
        Assert.Equal(100, test.State.FighterB.Guard);
        if (scenario == "success_block") Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.Blocked);
        if (scenario == "dodge") Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.Dodged);
        if (scenario == "counter") Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.Countered);
        if (scenario == "miss") Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.AttackMissed);
    }

    [Fact]
    public void FailedActiveBlockGuardBreakUpdatesEffectiveGuardAfterItsGroup()
    {
        var test = Create(seed: SeedFor(999)); test.Start();
        Commit(test, FighterId.FighterB, Action("action_b", defense: "block"));
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "guard_break"])); Resolve(test);
        var hit = Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.AttackHit);
        var added = Assert.Single(test.Journal.Drafts, x => x.EffectId == new StableId("guard_broken"));
        Assert.True(added.Sequence > test.Journal.Drafts.Single(x => x.EventType == CombatEventType.DamageApplied).Sequence);
        Assert.Equal(hit.EventId, added.SourceEventId);
        Assert.Equal(70, test.State.FighterB.Guard);
        Assert.Equal(650, test.Runtime.Channel(FighterId.FighterB, EffectModifierTarget.BlockWeight));
    }

    [Fact]
    public void WakeupImmunityExpiresAfterTick45AndNeverBlocksStunOrGrab()
    {
        var test = Create(); test.Start();
        _ = test.State.FighterB.BeginKnockdown(new KnockdownTimeline(10, 12, 18, 21), FighterId.FighterA, test.State.FighterA.NextDecisionId());
        ExpireControls(test, 21);
        Assert.False(EffectControlSystem.Allows(test.State, FighterId.FighterB, ControlCategory.Knockdown));
        Assert.True(EffectControlSystem.Allows(test.State, FighterId.FighterB, ControlCategory.Stun));
        Assert.True(test.State.CanBeGrabbed(FighterId.FighterB, 21));
        test.At(44); test.EndTick(); Assert.False(EffectControlSystem.Allows(test.State, FighterId.FighterB, ControlCategory.Knockdown));
        test.At(45); test.EndTick(); test.At(46);
        Assert.True(EffectControlSystem.Allows(test.State, FighterId.FighterB, ControlCategory.Knockdown));
    }

    [Fact]
    public void GroundHitDamagesWithoutRefreshingSameSourceAndLethalPreventsWakeup()
    {
        var test = Create(); test.Start(); test.At(10);
        Commit(test, FighterId.FighterB, Action("action_b"), startup: 20);
        Commit(test, FighterId.FighterA, Action("action_a", hits: 2, tags: ["strike", "knockdown", "ground_hit"])); Resolve(test);
        var hp = test.State.FighterB.Health;
        test.At(11); Resolve(test);
        Assert.Equal(new KnockdownTimeline(10, 12, 18, 21), test.State.FighterB.Knockdown);
        Assert.True(test.State.FighterB.Health < hp); Assert.Single(test.Journal.Drafts, x => x.Payload is ActionCancelledPayload);
        _ = test.State.FighterA.CancelCurrentAction(); test.State.FighterB.SetHealthForTesting(1);
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "ground_hit"])); Resolve(test);
        Assert.Equal(FighterState.Defeated, test.State.FighterB.State); Assert.Null(test.State.FighterB.Knockdown);
        ExpireControls(test, 21); Assert.Equal(FighterState.Defeated, test.State.FighterB.State);
        Assert.DoesNotContain(test.Journal.Drafts, x => x.ReasonCodes.Any(r => r.Value == "KnockdownCompleted"));
    }

    [Fact]
    public void FatigueCrossingCreatesOneImmunityIntervalAndAtCapRefreshDoesNotExtendIt()
    {
        var test = Create(); test.Start();
        for (var tick = 1; tick <= 3; tick++) { Stun(test, 1); ExpireControls(test, tick); }
        Assert.Equal(28, Role(test, EffectSemanticRole.ControlImmunity).EndExclusiveTick);
        // Ending a pre-existing lifecycle is valid even after immunity has been added.
        _ = test.State.FighterB.BeginStun(3, 1, FighterId.FighterA, test.State.FighterA.NextDecisionId());
        ExpireControls(test, 4);
        Assert.Equal(3, Role(test, EffectSemanticRole.ControlFatigue).Stacks);
        Assert.Equal(104, Role(test, EffectSemanticRole.ControlFatigue).EndExclusiveTick);
        Assert.Equal(28, Role(test, EffectSemanticRole.ControlImmunity).EndExclusiveTick);
        Assert.Single(test.Journal.Drafts, x => x.EffectId == new StableId("control_immunity") && x.ActorId == FighterId.FighterB);
    }

    [Fact]
    public void GrabEndedCreatesExactlyOneFatigueAndAnAuthoritativeRoleInterval()
    {
        var test = Create(kind: ActionInterruptKind.Unstoppable); test.Start(); test.At(10);
        Commit(test, FighterId.FighterB, Action("action_b"), startup: 5);
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["grab"], primitive: HitPrimitiveKind.Grab)); Resolve(test);
        Assert.Equal(FighterState.Grabbed, test.State.FighterB.State);
        AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.WallsAndGrabs, (state, emitter, drafts) =>
        {
            _ = ResolutionSystem.EndActiveGrab(state, test.Settings, emitter, GrabEndReason.Release, emitter.LastEventId, preflight: false);
            state.Effects!.CloseEvents(state, emitter, drafts.ToArray());
        });
        Assert.Equal(1, Role(test, EffectSemanticRole.ControlFatigue).Stacks);
        Assert.Equal(30, test.State.GrabLockoutUntil(FighterId.FighterB));
        for (var tick = 10; tick <= 29; tick++)
        { test.At(tick); Assert.False(test.State.CanBeGrabbed(FighterId.FighterB, tick)); test.EndTick(); }
        test.At(30); Assert.True(test.State.CanBeGrabbed(FighterId.FighterB, 30));
        Assert.Equal(0, test.State.GrabLockoutUntil(FighterId.FighterB));
        Assert.Single(test.Journal.Drafts, x => x.Payload is ActionCancelledPayload);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public void UninterruptibleProtectsOnlyAnAlreadyCreatedCurrentGroupIntent(bool protectedIntent)
    {
        var test = Create(kind: protectedIntent ? ActionInterruptKind.UninterruptibleImpact : ActionInterruptKind.Cancelable); test.Start();
        Commit(test, FighterId.FighterA, Action("action_a", stagger: 260, hits: 2));
        Commit(test, FighterId.FighterB, Action("action_b", hits: 2));
        Resolve(test);
        Assert.Equal(protectedIntent ? 2 : 1, test.Journal.Drafts.Count(x => x.Payload is DamageAppliedPayload));
        var cancelled = Assert.Single(test.Journal.Drafts, x => x.Payload is ActionCancelledPayload);
        Assert.Equal(protectedIntent ? 1 : 0, Assert.IsType<ActionCancelledPayload>(cancelled.Payload).SurvivingIntentIds.Count);
        test.At(1); Resolve(test);
        Assert.DoesNotContain(test.Journal.Drafts, x => x.Tick == 1 && x.EventType == CombatEventType.DamageApplied && x.ActorId == FighterId.FighterB);
        Assert.DoesNotContain(ImpactIntentCollector.Collect(test.State), x => x.ActorId == FighterId.FighterB);
    }

    [Fact]
    public void EventCapRollbackRestoresCancellationCostsCooldownTimelineAndSourceLedger()
    {
        var test = Create(maxEvents: 5); test.Start();
        Commit(test, FighterId.FighterB, Action("action_b"), startup: 3, energy: 5, cooldown: 20);
        Commit(test, FighterId.FighterA, Action("action_a", stagger: 260));
        var stamp = ProgressStamp.Capture(test.State); var decision = test.State.FighterA.ActiveDecisionId!.Value;
        var error = Assert.Throws<EngineInvariantException>(() => Resolve(test));
        Assert.Equal(EngineFailureCodes.EventCapExceeded, error.Code); Assert.Equal(stamp, ProgressStamp.Capture(test.State));
        Assert.Equal(20, test.State.FighterB.CooldownFor(new StableId("action_b")));
        Assert.False(test.State.FighterB.HasControlSource(FighterId.FighterA, decision, ControlCategory.Stun));
        Assert.Null(test.State.FighterB.Knockdown); Assert.Single(test.Journal.Drafts);
    }

    [Fact]
    public void CoordinatorOwnsControlTimersInExpiryAndWakesBeforeTheDecisionSnapshot()
    {
        var test = Create(); test.Start();
        _ = test.State.FighterB.BeginKnockdown(new KnockdownTimeline(0, 2, 8, 11), FighterId.FighterA, test.State.FighterA.NextDecisionId());
        test.At(10); _ = test.State.FighterA.BeginStun(10, 2, FighterId.FighterB, test.State.FighterB.NextDecisionId());
        var coordinator = new TickCoordinator(100);
        _ = coordinator.RunActiveTick(test.State, test.Settings, test.Emitter);
        Assert.Equal(FighterState.KnockedDown, test.State.FighterB.State); Assert.Equal(1, test.State.FighterB.StateTicksRemaining);
        _ = coordinator.RunActiveTick(test.State, test.Settings, test.Emitter);
        var wakeup = test.Journal.Drafts.Single(x => x.ReasonCodes.Any(r => r.Value == "KnockdownCompleted"));
        var decision = test.Journal.Drafts.First(x => x.Tick == 11 && x.EventType == CombatEventType.DecisionMade && x.ActorId == FighterId.FighterB);
        Assert.True(wakeup.Sequence < decision.Sequence);
        Assert.Equal(36, Role(test, EffectSemanticRole.WakeupImmunity).EndExclusiveTick);
    }

    private static ActiveEffect Role(EffectRuntimeFixture test, EffectSemanticRole role) =>
        Assert.Single(test.Runtime.Store(FighterId.FighterB).Active, x => x.Profile.SemanticRole == role);

    [Fact]
    public void ControlLifecycleDraftsRepeatExactlyAcrossIndependentBattleStates()
    {
        string[] Transcript()
        {
            var test = Create(); test.Start(); test.At(10);
            Commit(test, FighterId.FighterB, Action("action_b"), startup: 20);
            Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "knockdown"])); Resolve(test);
            for (var tick = 10; tick <= 21; tick++) { ExpireControls(test, tick); test.EndTick(); }
            test.At(45); test.EndTick(); test.Finish();
            Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
            return test.Journal.Drafts.Select(x => System.Text.Json.JsonSerializer.Serialize(new { x.EventId, x.Sequence, x.Tick,
                x.EventType, x.ActorId, x.TargetId, x.ActionId, x.DecisionId, x.ResolutionGroupId, x.SourceEventId, x.EffectId,
                x.ReasonCodes, x.Before, x.After, x.Rng,
                Payload = System.Text.Json.JsonSerializer.Serialize(x.Payload, x.Payload.GetType()) })).ToArray();
        }
        Assert.Equal(Transcript(), Transcript());
    }

    [Theory]
    [InlineData(-1, 1, 2, 3), InlineData(0, 0, 2, 3), InlineData(0, 2, 2, 3), InlineData(0, 2, 3, 3)]
    public void InvalidKnockdownTimelineDoesNotMutateAnAction(int start, int grounded, int getup, int ready)
    {
        var test = Create(); test.Start(); Commit(test, FighterId.FighterB, Action("action_b"), startup: 4);
        var stamp = ProgressStamp.Capture(test.State);
        Assert.Throws<ArgumentException>(() => test.State.FighterB.BeginKnockdown(new KnockdownTimeline(start, grounded, getup, ready),
            FighterId.FighterA, test.State.FighterA.NextDecisionId()));
        Assert.Equal(stamp, ProgressStamp.Capture(test.State));
    }

    [Fact]
    public void SurvivingIntentIdentityMustMatchTheExactCommitNotJustTheActionId()
    {
        var test = Create(kind: ActionInterruptKind.UninterruptibleImpact); test.Start();
        Commit(test, FighterId.FighterB, Action("action_b"));
        var stale = ImpactIntentCollector.Collect(test.State);
        _ = test.State.FighterB.CancelCurrentAction(); Commit(test, FighterId.FighterB, Action("action_b"));
        Assert.Empty(EffectControlSystem.SurvivingIntents(test.State, FighterId.FighterB, stale));
        Assert.Single(EffectControlSystem.SurvivingIntents(test.State, FighterId.FighterB, ImpactIntentCollector.Collect(test.State)));
        test.State.FighterB.SetActiveDecisionIdForTesting(null);
        var error = Assert.Throws<EngineInvariantException>(() => EffectControlSystem.SurvivingIntents(test.State, FighterId.FighterB, stale));
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, error.Code);
    }

    [Fact]
    public void CancellationFallsBackToTheEarlierCanonicalEventAndNeverInventsASource()
    {
        var test = Create(); test.Start(); Commit(test, FighterId.FighterB, Action("action_b"), startup: 3);
        var cause = test.Emitter.LastEventId;
        AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Resolve, (state, emitter, drafts) =>
            _ = EffectControlSystem.Cancel(state, emitter, FighterId.FighterB, "HitInterrupt", null, null));
        Assert.Equal(cause, test.Journal.Drafts.Last().SourceEventId);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public void MissingCanonicalControlCauseFailsAtomicallyBeforeAnyAppend(bool cancellation)
    {
        var test = Create();
        if (cancellation) Commit(test, FighterId.FighterB, Action("action_b"), startup: 3);
        else { _ = test.State.FighterB.BeginKnockdown(new KnockdownTimeline(0, 1, 2, 3), FighterId.FighterA, test.State.FighterA.NextDecisionId()); test.At(3); }
        var stamp = ProgressStamp.Capture(test.State);
        var error = Assert.Throws<EngineInvariantException>(() => AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Expiry,
            (state, emitter, drafts) =>
            {
                if (cancellation) _ = EffectControlSystem.Cancel(state, emitter, FighterId.FighterB, "HardControl", null, null);
                else EffectControlSystem.Expire(state, emitter, FighterId.FighterB);
            }));
        Assert.Equal(EngineFailureCodes.EffectInvalidMutation, error.Code);
        Assert.Equal(stamp, ProgressStamp.Capture(test.State)); Assert.Empty(test.Journal.Drafts);
    }

    [Fact]
    public void KnockdownActionRuleRetainsTheAttackerAsOwnerThroughControlCancellationSources()
    {
        var test = new EffectRuntimeFixture([Effect()], [Rule(trigger: EffectTrigger.Knockdown,
            ownerKind: EffectOwnerKind.Action, owner: "action_a", recipient: EffectRecipient.Opponent)]);
        test.Start(); Commit(test, FighterId.FighterB, Action("action_b"), startup: 3);
        Commit(test, FighterId.FighterA, Action("action_a", tags: ["strike", "knockdown"])); Resolve(test);
        Assert.Empty(test.Runtime.Store(FighterId.FighterA).Active);
        Assert.Single(test.Runtime.Store(FighterId.FighterB).Active);
        var added = Assert.Single(test.Journal.Drafts, x => x.EventType == CombatEventType.EffectAdded);
        Assert.Equal(FighterId.FighterB, added.ActorId); Assert.Equal(FighterId.FighterA, added.TargetId);
        Assert.Equal(new StableId("action_a"), added.ActionId);
    }

    private static EffectRuntimeFixture Create(EffectSemanticRole initial = EffectSemanticRole.None,
        HitInterruptStrength strength = HitInterruptStrength.None, ActionInterruptKind kind = ActionInterruptKind.Cancelable,
        int maxEvents = 200000, ulong seed = 7, ActionInterruptKind attackerKind = ActionInterruptKind.Cancelable)
    {
        EffectProfile Special(string id, int duration, EffectSemanticRole role, EffectModifierTarget target,
            EffectStackPolicy policy = EffectStackPolicy.Reject, int cap = 1) => new(new StableId(id), new StableId(id), duration,
                EffectExpiryBoundary.ExpireAfterTick, policy, cap, role == EffectSemanticRole.ControlFatigue ? EffectCompareKey.StackCount : EffectCompareKey.DurationTicks,
                0, EffectRefreshRule.ResetDuration, role, 0, 1, 999,
                [new EffectModifier(target, role == EffectSemanticRole.ControlFatigue ? EffectModifierOperation.Multiply : EffectModifierOperation.Override,
                    role == EffectSemanticRole.ControlFatigue ? 1000 : 0, 0)], role == EffectSemanticRole.ControlFatigue ? [1000, 750, 500, 250] : []);
        var effects = new[] {
            Special("fatigue", 100, EffectSemanticRole.ControlFatigue, EffectModifierTarget.HardControlDuration, EffectStackPolicy.AddStacks, 3),
            Special("control_immunity", 25, EffectSemanticRole.ControlImmunity, EffectModifierTarget.HardControlAllowed),
            Special("grab_lockout", 20, EffectSemanticRole.GrabLockout, EffectModifierTarget.GrabAllowed),
            Special("wakeup_immunity", 25, EffectSemanticRole.WakeupImmunity, EffectModifierTarget.KnockdownAllowed),
            new EffectProfile(new StableId("guard_broken"), new StableId("guard_broken"), 15, EffectExpiryBoundary.ExpireAfterTick,
                EffectStackPolicy.Refresh, 1, EffectCompareKey.DurationTicks, 0, EffectRefreshRule.ResetDuration, EffectSemanticRole.GuardBreak,
                0, 1, 99, [new EffectModifier(EffectModifierTarget.Guard, EffectModifierOperation.Add, -30, 0),
                    new EffectModifier(EffectModifierTarget.BlockWeight, EffectModifierOperation.Multiply, 650, 1)], []) };
        var rules = new List<EffectRuleProfile> { Rule("end_fatigue", "fatigue", EffectTrigger.ControlEnded),
            Rule("crossing", "control_immunity", EffectTrigger.FatigueThresholdReached),
            Rule("lockout", "grab_lockout", EffectTrigger.GrabEnded), Rule("wakeup", "wakeup_immunity", EffectTrigger.WakeupCompleted),
            Rule("guard_broken", "guard_broken", EffectTrigger.GuardBreak) };
        if (initial != EffectSemanticRole.None) rules.Add(Rule("initial", effects.Single(x => x.SemanticRole == initial).EffectId.Value));
        var interrupts = new[] {
            new ActionInterruptProfile(new StableId("action_a"), attackerKind, strength, HitInterruptStrength.Light, [ActionPhase.Startup], [ActionPhase.Active],
                attackerKind == ActionInterruptKind.Unstoppable ? [ControlCategory.Stun, ControlCategory.Knockdown] : []),
            new ActionInterruptProfile(new StableId("action_b"), kind, HitInterruptStrength.None,
                kind == ActionInterruptKind.Armored ? HitInterruptStrength.Heavy : HitInterruptStrength.Light,
                [ActionPhase.Startup], [ActionPhase.Startup], kind == ActionInterruptKind.Unstoppable ? [ControlCategory.Stun, ControlCategory.Knockdown] : []) };
        return new EffectRuntimeFixture(effects, rules, maximumEvents: maxEvents, seed: seed, interrupts: interrupts);
    }

    private static void Stun(EffectRuntimeFixture test, int duration)
    {
        var decision = test.State.FighterA.NextDecisionId();
        AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Resolve, (state, emitter, drafts) =>
        {
            _ = EffectControlSystem.Stun(state, emitter, FighterId.FighterB, FighterId.FighterA, decision, new StableId("action_a"),
                duration, 1000, state.Effects!.Channel(FighterId.FighterB, EffectModifierTarget.HardControlDuration),
                EffectControlSystem.Allows(state, FighterId.FighterB, ControlCategory.Stun), emitter.LastEventId!.Value, new ExternalId("control:fixture"), []);
            state.Effects.CloseEvents(state, emitter, drafts.ToArray());
        });
    }

    private static void ExpireControls(EffectRuntimeFixture test, int tick)
    {
        test.At(tick);
        AtomicBattleBatch.Execute(test.State, test.Emitter, TickPhase.Expiry, (state, emitter, drafts) =>
        {
            state.Effects!.Expire(state, emitter, EffectExpiryBoundary.ExpireBeforeTick);
            foreach (var fighter in new[] { FighterId.FighterA, FighterId.FighterB }) EffectControlSystem.Expire(state, emitter, fighter);
            state.Effects.CloseEvents(state, emitter, drafts.ToArray());
        });
    }

    private static void Resolve(EffectRuntimeFixture test) => ResolutionSystem.ResolveTick(test.State, test.Settings, test.Emitter,
        ImpactIntentOrderer.BuildGroups(ImpactIntentCollector.Collect(test.State), test.State.Tick));

    private static void Commit(EffectRuntimeFixture test, FighterId fighter, ResolutionActionProfile profile,
        int startup = 0, int energy = 0, int resource = 0, int cooldown = 0)
    {
        var actor = test.State.Get(fighter); var target = test.State.GetOpponent(fighter);
        _ = actor.ApplyEnergyCost(energy); _ = actor.ApplyUniqueResourceCost(resource);
        actor.CommitCombatAction(new CombatActionDescriptor(profile.Id, profile.Category, actor.NextDecisionId(), target.FighterId,
            target.Position, fighter == FighterId.FighterA ? CommitDirection.Right : CommitDirection.Left,
            energy, resource, startup, profile.ActiveTicks, 0, cooldown, profile, false, true, test.State.Tick));
    }

    private static ResolutionActionProfile Action(string id, int stagger = 0, int hits = 1, string[]? tags = null,
        HitPrimitiveKind primitive = HitPrimitiveKind.Hit, string? defense = null, bool unblockable = false) => new(new StableId(id), "Basic", "Fixture",
            ResolutionMovementMode.None, (tags ?? [defense ?? "strike"]).Select(x => new StableId(x)), defense is null
                ? Enumerable.Range(0, hits).Select(i => new HitScheduleEntry(primitive, i, i)) : [],
            hits, defense is null && primitive != HitPrimitiveKind.Grab ? hits : 0, 10, 10, 10, 10, 0, 10000, 100, 0, 10, !unblockable, true, false,
            500, 0, 500, 10, stagger, 8, 0, 0, 0, 0, false, false, 300, 0, 600, "Cancelable");

    private static ulong SeedFor(int draw)
    {
        for (ulong seed = 0; seed < 100000; seed++)
            if (Pcg32Stream.CreateResolution(seed).NextInt(0, 1000, RngOperation.ChanceCheck).Result == draw) return seed;
        throw new InvalidOperationException("No deterministic seed for fixture draw.");
    }
}
