using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Core.Math;

namespace Battle.Core.Effects;

/// <summary>
/// Bounded, conservative interval proof for modifier folds, lifetimes and control stages.
/// It covers all reachable groups/stack counts, including subsets, without enumerating combinations.
/// Mutually exclusive profiles contribute one per-group envelope, never their summed strengths.
/// Consumer-specific damage/decision/movement proof is separate and runs in the versioned setup before Begin.
/// </summary>
internal static class EffectArithmeticProof
{
    internal static void Validate(EffectRuntimeDefinition definition, ICollection<EffectSetupIssue> issues)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));
        if (issues is null) throw new ArgumentNullException(nameof(issues));
        foreach (var effect in definition.Effects)
        {
            Timer(effect.DurationTicks, "/config/effects/" + effect.EffectId + "/duration_ticks", effect.EffectId.Value);
            Timer(effect.InternalCooldownTicks, "/config/effects/" + effect.EffectId + "/internal_cooldown_ticks", effect.EffectId.Value);
        }
        foreach (var rule in definition.Rules)
            Timer(rule.InternalCooldownTicks, "/config/effect_rules/" + rule.RuleId + "/internal_cooldown_ticks", rule.RuleId.Value);
        foreach (var fighter in definition.Fighters)
        {
            foreach (var stat in fighter.BaseStats)
            {
                try
                {
                    var sources = fighter.Gear.Where(x => x.Stat == stat.Key).ToArray();
                    var additive = 0;
                    var product = definition.FixedPointScale;
                    foreach (var source in sources)
                    {
                        if (source.Operation == EffectModifierOperation.Add) additive = checked(additive + source.Value);
                        else if (source.Operation == EffectModifierOperation.Multiply) product = FixedMath.Mul(product, source.Value, definition.FixedPointScale);
                    }
                    Prove(stat.Key, stat.Value, additive, product, definition);
                }
                catch (OverflowException)
                {
                    issues.Add(new EffectSetupIssue("EffectArithmeticOverflowRisk", "/fighters/" +
                        (fighter.Fighter == Battle.Contracts.Ids.FighterId.FighterA ? "0" : "1") + "/stats/" + stat.Key, null));
                }
            }
        }
        foreach (var target in Enum.GetValues(typeof(EffectModifierTarget)).Cast<EffectModifierTarget>())
        {
            if (definition.Bounds.ContainsKey(target.ToString())) continue;
            try
            {
                var neutral = target is EffectModifierTarget.BlockChanceOffset or EffectModifierTarget.DodgeChanceOffset or EffectModifierTarget.GrabPriority
                    ? 0 : target is EffectModifierTarget.HardControlAllowed or EffectModifierTarget.GrabAllowed or EffectModifierTarget.KnockdownAllowed
                    ? 1 : definition.FixedPointScale;
                Prove(target.ToString(), neutral, 0, definition.FixedPointScale, definition);
            }
            catch (OverflowException) { issues.Add(new EffectSetupIssue("EffectArithmeticOverflowRisk", "/config/channels/" + target, null)); }
        }
        try
        {
            var ratio = EffectControlMath.ControlRatio(definition.ControlK,
                definition.Bounds["ControlPower"].Maximum, definition.Bounds["ControlResistance"].Minimum, definition.FixedPointScale);
            var durationUpper = ProductUpper(EffectModifierTarget.HardControlDuration.ToString(), definition.FixedPointScale, definition);
            _ = EffectControlMath.Knockdown(definition.TimeLimitTicks, definition.KnockdownFallTicks,
                definition.KnockdownGroundedTicks, definition.KnockdownGetupTicks, ratio,
                checked((int)durationUpper), definition.FixedPointScale);
            _ = checked(definition.ControlK + definition.Bounds["ControlResistance"].Maximum);
        }
        catch (OverflowException) { issues.Add(new EffectSetupIssue("EffectArithmeticOverflowRisk", "/config/control/knockdown", null)); }

        void Timer(int duration, string path, string entity)
        {
            if ((long)definition.TimeLimitTicks + duration > int.MaxValue)
                issues.Add(new EffectSetupIssue("EffectArithmeticOverflowRisk", path, entity));
        }
    }

    private static (long Lower, long Upper, long Magnitude) Prove(string target, int baseValue, int staticAdd, int staticProduct, EffectRuntimeDefinition definition)
    {
        long minimum = staticAdd;
        long maximum = staticAdd;
        foreach (var group in definition.Effects.GroupBy(x => x.StackGroup))
        {
            long groupMin = 0;
            long groupMax = 0;
            foreach (var profile in group)
            {
                long negative = 0;
                long positive = 0;
                foreach (var modifier in profile.Modifiers.Where(x => x.Target.ToString() == target && x.Operation == EffectModifierOperation.Add))
                {
                    var stacks = profile.StackPolicy == EffectStackPolicy.AddStacks ? profile.StackCap : 1;
                    var scaled = checked(modifier.Value * stacks);
                    if (scaled < 0) negative = checked(negative + scaled);
                    else positive = checked(positive + scaled);
                }
                groupMin = System.Math.Min(groupMin, negative);
                groupMax = System.Math.Max(groupMax, positive);
            }
            minimum = checked(minimum + groupMin);
            maximum = checked(maximum + groupMax);
        }
        RequireInt32(minimum); RequireInt32(maximum);
        var lower = checked((long)baseValue + minimum);
        var upper = checked((long)baseValue + maximum);
        RequireInt32(lower); RequireInt32(upper);
        var product = ProductUpper(target, System.Math.Abs((long)staticProduct), definition, staticProduct < 0);
        var absolute = System.Math.Max(System.Math.Abs(lower), System.Math.Abs(upper));
        // Ceil of the absolute result also bounds signed mathematical floor, including negative values.
        var result = CeilingMultiply(absolute, product, definition.FixedPointScale);
        if (result > int.MaxValue) throw new OverflowException("A pre-clamp derived value may exceed Int32.");
        return (lower, upper, result);
    }

    internal static StatBounds ReachableRange(EffectRuntimeDefinition definition, EffectFighterDefinition fighter, EffectModifierTarget target)
    {
        var name = target.ToString();
        var isStat = fighter.BaseStats.TryGetValue(name, out var baseValue);
        if (!isStat) baseValue = target is EffectModifierTarget.BlockChanceOffset or EffectModifierTarget.DodgeChanceOffset or EffectModifierTarget.GrabPriority
            ? 0 : target is EffectModifierTarget.HardControlAllowed or EffectModifierTarget.GrabAllowed or EffectModifierTarget.KnockdownAllowed
            ? 1 : definition.FixedPointScale;
        var bounds = isStat ? definition.Bounds[name] : new StatBounds(int.MinValue, int.MaxValue);
        var gear = fighter.Gear.Where(x => x.Stat == name).Select(x => new EffectStatSource(
            EffectModifierLayer.Gear, x.Priority, x.SourceId, new EffectModifier(target, x.Operation, x.Value, x.Ordinal), 1)).ToArray();
        var modifiers = definition.Effects.SelectMany(x => x.Modifiers).Where(x => x.Target == target).ToArray();
        if (modifiers.Length == 0)
        {
            var exact = EffectStatMath.Compute(baseValue, bounds, gear, definition.FixedPointScale);
            return new StatBounds(exact, exact);
        }
        var additive = 0;
        var product = definition.FixedPointScale;
        foreach (var source in gear)
        {
            if (source.Modifier.Operation == EffectModifierOperation.Add) additive = checked(additive + source.Modifier.Value);
            else if (source.Modifier.Operation == EffectModifierOperation.Multiply)
                product = FixedMath.Mul(product, source.Modifier.Value, definition.FixedPointScale);
        }
        var proof = Prove(name, baseValue, additive, product, definition);
        var unchangedProduct = product == definition.FixedPointScale && !modifiers.Any(x => x.Operation == EffectModifierOperation.Multiply);
        var minimum = unchangedProduct ? proof.Lower : -proof.Magnitude;
        var maximum = unchangedProduct ? proof.Upper : proof.Magnitude;
        foreach (var value in gear.Select(x => x.Modifier).Concat(modifiers).Where(x => x.Operation == EffectModifierOperation.Override).Select(x => x.Value))
        {
            minimum = System.Math.Min(minimum, value);
            maximum = System.Math.Max(maximum, value);
        }
        // Channel Multiply domains are nonnegative; signed Add channels retain their lower bound.
        if (!isStat && target is not EffectModifierTarget.BlockChanceOffset and not EffectModifierTarget.DodgeChanceOffset and not EffectModifierTarget.GrabPriority)
            minimum = System.Math.Max(0, minimum);
        return new StatBounds(bounds.Clamp(checked((int)minimum)), bounds.Clamp(checked((int)maximum)));
    }

    internal static int WeightUpper(EffectRuntimeDefinition definition, IEnumerable<string> tags)
    {
        var present = tags.ToArray();
        var targets = new List<string>();
        if (present.Contains("block", StringComparer.Ordinal)) targets.Add(nameof(EffectModifierTarget.BlockWeight));
        if (present.Contains("punish", StringComparer.Ordinal)) targets.Add(nameof(EffectModifierTarget.PunishWeight));
        if (present.Contains("wall_impact", StringComparer.Ordinal)) targets.Add(nameof(EffectModifierTarget.WallActionWeight));
        return checked((int)ProductUpper("", definition.FixedPointScale, definition, mergedTargets: targets));
    }

    private static long ProductUpper(string target, long initial, EffectRuntimeDefinition definition, bool signed = false, IReadOnlyCollection<string>? mergedTargets = null)
    {
        if (initial == 0) return 0;
        var factors = new List<long>();
        bool Matches(EffectModifier modifier) => mergedTargets is null ? modifier.Target.ToString() == target : mergedTargets.Contains(modifier.Target.ToString());
        signed |= definition.Effects.SelectMany(x => x.Modifiers).Any(x => Matches(x) &&
            x.Operation == EffectModifierOperation.Multiply && x.Value < 0);
        foreach (var group in definition.Effects.GroupBy(x => x.StackGroup))
        {
            var profiles = group.ToArray();
            var ordinals = profiles.SelectMany(x => x.Modifiers.Where(m => Matches(m) && m.Operation == EffectModifierOperation.Multiply)
                .Select(m => m.Ordinal)).Distinct().OrderBy(x => x);
            foreach (var ordinal in ordinals)
            {
                var factor = (long)definition.FixedPointScale;
                var repetitions = 1;
                foreach (var profile in profiles)
                {
                    var matching = profile.Modifiers.Where(x => x.Ordinal == ordinal && Matches(x) && x.Operation == EffectModifierOperation.Multiply).ToArray();
                    if (matching.Length == 0) continue;
                    if (profile.SemanticRole == EffectSemanticRole.ControlFatigue)
                        factor = System.Math.Max(factor, profile.Lookup.Max());
                    else
                    {
                        factor = System.Math.Max(factor, System.Math.Abs((long)matching[0].Value));
                        if (profile.StackPolicy == EffectStackPolicy.AddStacks) repetitions = System.Math.Max(repetitions, profile.StackCap);
                    }
                }
                if (factor > definition.FixedPointScale)
                    for (var index = 0; index < repetitions; index++) factors.Add(factor);
            }
        }
        // Positive floor folds are bounded by the exact real product, irrespective of interleaving.
        // For signed folds use x*f/scale+1: ascending factors maximize this affine error envelope.
        // Integer ceiling of that envelope is conservative, not an alternate gameplay rounding rule.
        var bound = initial;
        foreach (var factor in factors.OrderBy(x => x))
        {
            bound = CeilingMultiply(bound, factor, definition.FixedPointScale);
            if (signed) bound = checked(bound + 1);
            if (bound > int.MaxValue) throw new OverflowException("A fixed-point fold may exceed Int32.");
        }
        return bound;
    }

    private static long CeilingMultiply(long left, long right, int scale)
    {
        var numerator = checked(left * right);
        var quotient = numerator / scale;
        return numerator % scale == 0 ? quotient : checked(quotient + 1);
    }
    private static void RequireInt32(long value)
    {
        if (value < int.MinValue || value > int.MaxValue) throw new OverflowException("An additive fold may exceed Int32.");
    }
}
