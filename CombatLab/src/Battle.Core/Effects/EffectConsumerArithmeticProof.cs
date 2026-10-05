using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Math;
using Battle.Core.Resolution;

namespace Battle.Core.Effects;

/// <summary>Conservative reachable-value proof; no mutation, journal, cache or RNG access.</summary>
internal static class EffectConsumerArithmeticProof
{
    internal static void Validate(EffectRuntimeDefinition definition, RuntimeBattleSettings settings,
        BattleState initialState, ICollection<EffectSetupIssue> issues)
    {
        if (definition.FixedPointScale != settings.FixedPointScale)
        {
            issues.Add(new EffectSetupIssue("EffectScaleMismatch", "/config/settings/global.fp_scale", null));
            return;
        }
        var movementMaximums = new List<int>();
        Guard("/config/arena/arithmetic", null, () =>
        {
            // Every possible center/headroom/gap/delta fits the Engine's Int32 geometry contract.
            _ = checked((int)((long)settings.Arena.MaximumPosition - settings.Arena.MinimumPosition));
            _ = checked(initialState.FighterA.CollisionRadius + initialState.FighterB.CollisionRadius);
        });
        Guard("/config/control/event_reserve", null, () =>
        {
            // Arithmetic bound only, not admission against max_events: each actual closure is
            // still previewed and exact-preflighted, with its resulting active cleanup count.
            var cleanup = checked(2 * definition.MaximumInstancesPerFighter);
            _ = checked(2 * definition.MaximumTriggersPerTick + cleanup + 2);
            _ = checked((long)settings.MaximumEvents + cleanup + 1);
        });
        foreach (var fighter in definition.Fighters)
        {
            var path = "/fighters/" + (fighter.Fighter == FighterId.FighterA ? "0" : "1");
            var opponent = definition.Fighters.Single(x => x.Fighter != fighter.Fighter);
            var ranges = new Dictionary<EffectModifierTarget, StatBounds>();
            var targetRanges = new Dictionary<EffectModifierTarget, StatBounds>();
            try
            {
                foreach (var target in (EffectModifierTarget[])Enum.GetValues(typeof(EffectModifierTarget)))
                {
                    ranges[target] = EffectArithmeticProof.ReachableRange(definition, fighter, target);
                    targetRanges[target] = EffectArithmeticProof.ReachableRange(definition, opponent, target);
                }
            }
            catch (OverflowException) { Risk(path + "/effect_values", null); continue; }
            var profile = settings.Decisions.GetFighter(fighter.Fighter);
            var reachable = settings.Decisions.Actions.Where(action => settings.Decisions.Availability.IsActionAllowed(action.Id) &&
                (action.Slot == DecisionActionSlot.System || action.OwnerAnimalId == profile.BuildView.AnimalId &&
                    (action.Slot != DecisionActionSlot.Special || profile.BuildView.SpecialActionIds.Contains(action.Id)))).ToArray();
            long sum = 0;
            var systemMaximum = 0;
            var combatMovementMaximum = 0;
            Guard(path + "/movement_speed", null, () =>
            {
                // System segment budgets are raw frozen speeds; pair allocation uses Int64.
                _ = checked(ranges[EffectModifierTarget.MoveSpeed].Maximum + targetRanges[EffectModifierTarget.MoveSpeed].Maximum);
            });
            foreach (var action in reachable)
            {
                var actionPath = path + "/actions/" + action.Id;
                Guard(actionPath + "/decision", action.Id.Value, () =>
                {
                    var weight = DecisionUpper(definition, settings.Decisions, profile, action);
                    if (action.Slot == DecisionActionSlot.System) systemMaximum = System.Math.Max(systemMaximum, weight);
                    else sum = checked(sum + weight);
                });
                Guard(actionPath + "/timing", action.Id.Value, () =>
                {
                    if (action.Slot == DecisionActionSlot.System)
                    {
                        // System timing is fixed DATA, not scaled by ActionSpeed (FreezeCommit).
                        var system = settings.GetSystemAction(action.Id);
                        _ = checked(settings.TimeLimitTicks + system.StartupTicks + system.ActiveTicks + system.RecoveryTicks + system.CooldownTicks);
                        return;
                    }
                    var speed = ranges[EffectModifierTarget.ActionSpeed];
                    var startup = Timing(action.StartupBaseTicks, speed.Minimum);
                    var recovery = Timing(action.RecoveryBaseTicks, speed.Minimum);
                    _ = Timing(action.StartupBaseTicks, speed.Maximum);
                    _ = Timing(action.RecoveryBaseTicks, speed.Maximum);
                    _ = checked(settings.TimeLimitTicks + System.Math.Clamp(startup, action.StartupMinimumTicks, action.StartupMaximumTicks) +
                        action.ActiveTicks + System.Math.Clamp(recovery, action.RecoveryMinimumTicks, action.RecoveryMaximumTicks) + action.CooldownTicks);
                });
                var impact = settings.Resolution.GetAction(action.Id);
                Guard(actionPath + "/movement", action.Id.Value, () =>
                {
                    if (impact.MovementMode is ResolutionMovementMode.Approach or ResolutionMovementMode.Retreat or ResolutionMovementMode.Adaptive or ResolutionMovementMode.Follow)
                    {
                        var budget = ResolutionMath.ActiveTickBudget(impact.MoveDistance, impact.ActiveTicks, 0);
                        var width = checked((int)((long)settings.Arena.MaximumPosition - settings.Arena.MinimumPosition));
                        var gapBound = System.Math.Max(width, System.Math.Max(action.PreferredRangeMinimum, impact.HitRangeMaximum));
                        combatMovementMaximum = System.Math.Max(combatMovementMaximum, System.Math.Min(budget, gapBound));
                    }
                });
                Guard(actionPath + "/resolution", action.Id.Value, () =>
                {
                    var global = settings.Resolution.Global;
                    var scale = definition.FixedPointScale;
                    _ = checked(targetRanges[EffectModifierTarget.Armor].Maximum + global.ArmorK);
                    _ = ResolutionMath.ComputeDamage(global, impact, ranges[EffectModifierTarget.Power].Maximum,
                        targetRanges[EffectModifierTarget.Armor].Minimum, damageDealtFixedPoint: ranges[EffectModifierTarget.DamageDealt].Maximum,
                        damageTakenFixedPoint: targetRanges[EffectModifierTarget.DamageTaken].Maximum);
                    _ = checked(global.ControlK + targetRanges[EffectModifierTarget.ControlResistance].Maximum);
                    var control = ResolutionMath.ComputeControl(global, impact, ranges[EffectModifierTarget.ControlPower].Maximum,
                        targetRanges[EffectModifierTarget.ControlResistance].Minimum, targetRanges[EffectModifierTarget.HardControlDuration].Maximum);
                    // Stagger accumulation deliberately uses Int64 before threshold clamp in Engine.
                    _ = checked((long)control.StaggerGain + initialState.Get(opponent.Fighter).StaggerThreshold - 1);
                    _ = checked(settings.TimeLimitTicks + global.StunMaximumTicks + global.MaximumHoldTicks + global.GrabLockoutTicks);
                    _ = EffectControlMath.Knockdown(settings.TimeLimitTicks, definition.KnockdownFallTicks,
                        definition.KnockdownGroundedTicks, definition.KnockdownGetupTicks, control.ControlRatioFixedPoint,
                        targetRanges[EffectModifierTarget.HardControlDuration].Maximum, scale);
                    _ = checked(global.ForceK + targetRanges[EffectModifierTarget.Mass].Maximum);
                    var move = ResolutionMath.ComputeRequestedMove(global, impact, targetRanges[EffectModifierTarget.Mass].Minimum);
                    _ = ResolutionMath.ComputeWallDamage(global, impact, move);
                    if (impact.Schedule.Any(x => x.Kind == HitPrimitiveKind.Grab))
                    {
                        _ = checked(impact.GrabPriority + ranges[EffectModifierTarget.GrabPriority].Minimum);
                        _ = checked(impact.GrabPriority + ranges[EffectModifierTarget.GrabPriority].Maximum);
                    }
                });
            }
            Guard(path + "/decision_weight_sum", null, () => { _ = checked((int)(sum + systemMaximum)); });
            movementMaximums.Add(combatMovementMaximum);

            int Timing(int configured, int speed)
            {
                var timing = settings.Decisions.Timing;
                var candidate = checked((long)settings.FixedPointScale + (long)checked(speed - timing.SpeedBaseline) * timing.SpeedSlope);
                var multiplier = checked((int)System.Math.Clamp(candidate, timing.SpeedMinimumFixedPoint, timing.SpeedMaximumFixedPoint));
                return FixedMath.Div(configured, multiplier, settings.FixedPointScale);
            }
        }
        Guard("/config/arena/combat_move_pair", null, () =>
        {
            _ = checked(movementMaximums.Aggregate(0, (sum, value) => checked(sum + value)));
        });
        void Risk(string path, string? entity) => issues.Add(new EffectSetupIssue("EffectArithmeticOverflowRisk", path, entity));
        void Guard(string path, string? entity, Action proof)
        {
            try { proof(); }
            catch (OverflowException) { Risk(path, entity); }
            catch (EngineInvariantException failure) when (failure.Code == DecisionFailureCodes.DecisionArithmeticOverflow) { Risk(path, entity); }
        }
    }

    private static int DecisionUpper(EffectRuntimeDefinition definition, DecisionRuntimeSettings runtime,
        DecisionFighterProfile profile, DecisionActionProfile action)
    {
        var weights = runtime.Weights;
        var scale = weights.FixedPointScale;
        var tactic = DecisionTacticMultiplierCalculator.Calculate(action, profile.Tactic, weights);
        var situation = scale;
        foreach (var factor in new[] { profile.Tactic.LowHealthFixedPoint, profile.Tactic.SelfWallFixedPoint,
                     profile.Tactic.TargetWallFixedPoint, profile.Tactic.TargetRecoveryFixedPoint })
            situation = FixedMath.Mul(situation, System.Math.Max(scale, System.Math.Clamp(factor, weights.MultiplierMinimum, weights.MultiplierMaximum)), scale);
        situation = FixedMath.Mul(situation, EffectArithmeticProof.WeightUpper(definition, action.Tags.Select(x => x.Value)), scale);
        var synergy = DecisionSynergyMultiplierCalculator.Calculate(action, profile.Passive, profile.OffenseGear,
            profile.DefenseGear, profile.UtilityGear, weights);
        var variety = scale;
        foreach (var factor in new[] { runtime.RepeatSameActionFixedPoint, runtime.RepeatSameCategoryFixedPoint, profile.Tactic.RepeatPenaltyFixedPoint })
            variety = FixedMath.Mul(variety, System.Math.Max(scale, factor), scale);
        var counter = action.HasTag("counter") ? System.Math.Max(scale, profile.Tactic.CounterFixedPoint) : scale;
        var opportunity = System.Math.Max(scale, System.Math.Min(runtime.OpportunityCapFixedPoint, action.OpportunityCapFixedPoint));
        var weight = System.Math.Min(action.BaseWeight, weights.DecisionWeightMaximum);
        foreach (var factor in new[] { tactic, situation, synergy, counter, variety, opportunity })
            weight = FixedMath.Mul(weight, System.Math.Clamp(factor, weights.MultiplierMinimum, weights.MultiplierMaximum), scale);
        return System.Math.Min(weight, weights.DecisionWeightMaximum);
    }
}
