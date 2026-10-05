using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;
using Battle.Core.Effects;
using Battle.Core.Engine;
using Battle.Core.Resolution;
using Battle.Core.UnitTests.Decisions;
using static Battle.Core.UnitTests.Effects.EffectRuntimeFixture;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectDecisionConsumerTests
{
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-011")]
    public void WeightOracleAndGrabMassControlConsumersReadTheirFrozenBoundaries()
    {
        foreach (var (tag, target, value, expected) in new[] {
            ("block", EffectModifierTarget.BlockWeight, 650, 65),
            ("punish", EffectModifierTarget.PunishWeight, 1400, 140),
            ("wall_impact", EffectModifierTarget.WallActionWeight, 1250, 125) })
        {
            var inputs = new EffectDecisionView([Source("effect_a", target, value)], true);
            Assert.Equal(expected, Score(tag, inputs).FinalWeight);
            Assert.Equal(100, Score("light", inputs).FinalWeight);
        }
        CheckFrozenGrabPriority();
        CheckResolutionMassAndResistance();
    }

    [Fact]
    public void EffectMultiplierAppearsOnlyOnceInSituationTrace()
    {
        var score = Score("block", new EffectDecisionView([Source("effect_a", EffectModifierTarget.BlockWeight, 650)], true));
        Assert.Equal(new[] { "Tactic", "Situation", "Synergy", "Counter", "Variety", "Opportunity" }, score.Modifiers.Select(x => x.Code.Value));
        Assert.Equal(new[] { 1000, 650, 1000, 1000, 1000, 1000 }, score.Modifiers.Select(x => x.MultiplierFixedPoint));
    }

    [Theory]
    [InlineData("blocking"), InlineData("blocked"), InlineData("wall"), InlineData("punishing")]
    public void TagsMatchExactlyAndOrdinally(string tag)
    {
        var inputs = new EffectDecisionView([Source("effect_a", EffectModifierTarget.BlockWeight, 650),
            Source("effect_b", EffectModifierTarget.PunishWeight, 1400), Source("effect_c", EffectModifierTarget.WallActionWeight, 1250)], true);
        Assert.Equal(100, Score(tag, inputs).FinalWeight);
    }

    [Fact]
    public void MultipleChannelsMergeInCanonicalSourceOrderBeforeFloor()
    {
        // floor(floor(1000*650/1000)*1401/1000)*1251/1000 = 1138.
        // Reversing the canonical multiplier sequence gives 1137 instead.
        var sources = new[] { Source("a", EffectModifierTarget.BlockWeight, 650),
            Source("b", EffectModifierTarget.PunishWeight, 1401), Source("c", EffectModifierTarget.WallActionWeight, 1251) };
        var action = DecisionTestFixture.Action(tags: ["block", "punish", "wall_impact"]);
        var canonical = new EffectDecisionView(sources, true).Situation(action, 1000, 1000);
        Assert.Equal(1138, canonical);
        Assert.Equal(canonical, new EffectDecisionView(sources.Reverse(), true).Situation(action, 1000, 1000));
    }

    [Fact]
    public void SourceArrayAndRuntimeChangesCannotAlterCapturedDecisionInputs()
    {
        var sources = new[] { Source("a", EffectModifierTarget.BlockWeight, 650) };
        var copy = new EffectDecisionView(sources, true);
        sources[0] = Source("a", EffectModifierTarget.BlockWeight, 1400);
        Assert.Equal(65, Score("block", copy).FinalWeight);
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.BlockWeight, value: 650,
            operation: EffectModifierOperation.Multiply, cap: 1)], [Rule()]);
        test.Start();
        var frozenA = test.Runtime.CaptureDecision(test.State, FighterId.FighterA);
        var frozenB = test.Runtime.CaptureDecision(test.State, FighterId.FighterB);
        _ = test.Runtime.Store(FighterId.FighterA).Remove(new StableId("effect_a"), EffectRemoveReason.ExpiredBeforeTick);
        test.Runtime.Recompute(test.State, FighterId.FighterA);
        Assert.Equal(65, Score("block", frozenA).FinalWeight);
        Assert.Equal(65, Score("block", frozenB).FinalWeight);
        Assert.Equal(100, Score("block", test.Runtime.CaptureDecision(test.State, FighterId.FighterA)).FinalWeight);
    }

    [Theory]
    [InlineData(true), InlineData(false)]
    public void GrabAvailabilityUsesFrozenOpponentProtectionWithoutRandomness(bool allowed)
    {
        var grab = DecisionTestFixture.Action(tags: ["grab"]);
        var target = View(FighterId.FighterB, new EffectDecisionView([], allowed));
        var context = DecisionTestFixture.Context([grab], DecisionTestFixture.Snapshot(fighterB: target));
        var evaluation = DecisionAvailabilityEvaluator.Evaluate(grab, context);
        Assert.Equal(allowed, evaluation.Legal);
        Assert.Equal(allowed ? null : DecisionRejectionCodes.TargetUnavailable, evaluation.FirstRejectionCode);
    }

    [Fact]
    public void SituationOverflowIsTypedAndNoClampHidesIt()
    {
        var inputs = new EffectDecisionView([Source("a", EffectModifierTarget.BlockWeight, int.MaxValue),
            Source("b", EffectModifierTarget.PunishWeight, int.MaxValue)], true);
        var error = Assert.Throws<EngineInvariantException>(() => inputs.Situation(
            DecisionTestFixture.Action(tags: ["block", "punish"]), 1000, 1000));
        Assert.Equal(EngineFailureCodes.EffectArithmeticOverflow, error.Code);
    }

    [Fact]
    public void UnregisteredWeightTargetIsNotConsumedAndMissingSourcesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new EffectDecisionView(null!, true));
        var inputs = new EffectDecisionView([Source("a", EffectModifierTarget.DamageTaken, 500)], true);
        Assert.Equal(1000, inputs.Situation(DecisionTestFixture.Action(tags: ["block"]), 1000, 1000));
        Assert.Throws<ArgumentException>(() => DecisionTestFixture.Action(tags: ["Block"]));
        Assert.Throws<EngineInvariantException>(() => new EffectDecisionView([Source("a", EffectModifierTarget.BlockWeight, 2000)], true)
            .Situation(DecisionTestFixture.Action(tags: ["block"]), int.MaxValue, 1000));
    }

    [Fact]
    public void GrabPriorityOverflowFailsDuringCollectionWithoutEventsOrDraws()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.GrabPriority, value: int.MaxValue, cap: 1)], [Rule()]);
        test.Start(); Commit(test, FighterId.FighterA, Action("action_a", primitive: HitPrimitiveKind.Grab));
        var count = test.Journal.Drafts.Count;
        var error = Assert.Throws<EngineInvariantException>(() => ImpactIntentCollector.Collect(test.State));
        Assert.Equal(EngineFailureCodes.EffectArithmeticOverflow, error.Code);
        Assert.Equal("CollectIntents", error.Phase);
        Assert.Equal(count, test.Journal.Drafts.Count);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
    }

    private static void CheckFrozenGrabPriority()
    {
        var test = new EffectRuntimeFixture([Effect(target: EffectModifierTarget.GrabPriority, value: 100, cap: 1)], [Rule()]);
        test.Start();
        Commit(test, FighterId.FighterA, Action("action_a", primitive: HitPrimitiveKind.Grab, grabPriority: 10));
        Commit(test, FighterId.FighterB, Action("action_b", primitive: HitPrimitiveKind.Grab, grabPriority: 20));
        var intents = ImpactIntentCollector.Collect(test.State);
        Assert.Equal(new[] { 110, 120 }, intents.Select(x => x.EffectiveGrabPriority));
        _ = test.Runtime.Store(FighterId.FighterB).Remove(new StableId("effect_a"), EffectRemoveReason.ExpiredBeforeTick);
        test.Runtime.Recompute(test.State, FighterId.FighterB);
        ResolutionSystem.ResolveTick(test.State, test.Settings, test.Emitter, ImpactIntentOrderer.BuildGroups(intents, 0));
        var conflict = Assert.IsType<ConflictResolvedPayload>(Assert.Single(test.Journal.Drafts,
            x => x.EventType == CombatEventType.ConflictResolved).Payload);
        Assert.Equal(110, conflict.PriorityA); Assert.Equal(120, conflict.PriorityB);
        Assert.Equal(ConflictResolutionResult.BWin, conflict.Result);
        Assert.Equal(ConflictTieBreakMethod.Priority, conflict.TieBreakMethod);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
    }

    private static void CheckResolutionMassAndResistance()
    {
        var effects = new[] { Effect("mass", target: EffectModifierTarget.Mass, value: 100, cap: 1),
            Effect("resistance", target: EffectModifierTarget.ControlResistance, value: 100, cap: 1) };
        var test = new EffectRuntimeFixture(effects, [Rule("mass", "mass", EffectTrigger.DamageTaken),
            Rule("resistance", "resistance", EffectTrigger.DamageTaken)]);
        test.Start();
        var action = Action("action_a", stagger: 100, knockback: 100);
        Commit(test, FighterId.FighterA, action);
        var intents = ImpactIntentCollector.Collect(test.State);
        test.Pulse(0); // Changes B after intent capture, before the resolution-group freeze.
        Assert.Equal(200, test.State.FighterB.Mass); Assert.Equal(200, test.State.FighterB.ControlResistance);
        var position = test.State.FighterB.Position;
        var expected = ResolutionMath.ComputeControl(test.Settings.Resolution.Global, action, 100, 200, 1000);
        var move = ResolutionMath.ComputeRequestedMove(test.Settings.Resolution.Global, action, 200);
        ResolutionSystem.ResolveTick(test.State, test.Settings, test.Emitter, ImpactIntentOrderer.BuildGroups(intents, 0));
        Assert.Equal(expected.StaggerGain, test.State.FighterB.Stagger);
        Assert.Equal(position + move, test.State.FighterB.Position);
        Assert.Equal(0UL, test.State.Rng.Resolution.NextDrawIndex);
    }

    private static EffectStatSource Source(string id, EffectModifierTarget target, int value) => new(
        EffectModifierLayer.TemporaryEffect, 0, new StableId(id), new EffectModifier(target, EffectModifierOperation.Multiply, value, 0), 1);

    private static DecisionFighterView View(FighterId fighter, EffectDecisionView inputs) => new(fighter, DecisionTestFixture.Build(),
        fighter == FighterId.FighterA ? 4000 : 5540, fighter == FighterId.FighterA ? Facing.Right : Facing.Left,
        FighterState.DecisionReady, null, 520, 1000, 1000, 1000, 1000, new StableId("rage"), 0, 1000, 100, 5, effectInputs: inputs);

    private static CandidateScore Score(string tag, EffectDecisionView inputs)
    {
        var action = DecisionTestFixture.Action(tags: [tag], baseWeight: 100);
        var actor = View(FighterId.FighterA, inputs);
        var opponent = DecisionTestFixture.Fighter(FighterId.FighterB, 5540);
        var situation = DecisionSituationMultiplierCalculator.Calculate(action, actor, opponent, DecisionTestFixture.NeutralTactic,
            DecisionTestFixture.WeightSettings, null, 0, 10000, 0);
        return DecisionWeightCalculator.Calculate(DecisionCandidateEvaluation.Accept(action, 0),
            new DecisionStageMultipliers(1000, situation, 1000, 1000, 1000, 1000), DecisionTestFixture.WeightSettings);
    }

    private static void Commit(EffectRuntimeFixture test, FighterId fighter, ResolutionActionProfile profile)
    {
        var actor = test.State.Get(fighter); var target = test.State.GetOpponent(fighter);
        actor.CommitCombatAction(new CombatActionDescriptor(profile.Id, profile.Category, actor.NextDecisionId(), target.FighterId,
            target.Position, fighter == FighterId.FighterA ? CommitDirection.Right : CommitDirection.Left,
            0, 0, 0, profile.ActiveTicks, 0, 0, profile, false, true, test.State.Tick));
    }

    private static ResolutionActionProfile Action(string id, HitPrimitiveKind primitive = HitPrimitiveKind.Hit,
        int grabPriority = 10, int stagger = 0, int knockback = 0) => new(new StableId(id), "Basic", "Fixture",
        ResolutionMovementMode.None, [new StableId(primitive == HitPrimitiveKind.Grab ? "grab" : "strike")],
        [new HitScheduleEntry(primitive, 0, 0)], 1, primitive == HitPrimitiveKind.Grab ? 0 : 1,
        10, 10, 10, grabPriority, 0, 10000, 100, 0, 10, false, false, false, 500, 0, 500, 10,
        stagger, 8, knockback, 0, 1000, 0, false, false, 300, 0, 600, "Cancelable");
}
