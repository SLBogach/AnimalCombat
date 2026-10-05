using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Math;

namespace Battle.Core.Effects;

internal enum EffectModifierLayer
{
    BaseAnimal, ModeNormalization, Gear, PassiveInitialization, PermanentEffect, TemporaryEffect,
}

internal readonly record struct EffectStatSource(
    EffectModifierLayer Layer, int Priority, StableId SourceId, EffectModifier Modifier, int Stacks);

/// <summary>Pure derived value computation; starts from immutable base on every invocation.</summary>
internal static class EffectStatMath
{
    internal static int Compute(int baseValue, StatBounds bounds, IEnumerable<EffectStatSource> sources, int scale)
    {
        if (sources is null) throw new ArgumentNullException(nameof(sources));
        if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        var ordered = sources.OrderBy(x => x.Layer).ThenBy(x => x.Priority).ThenBy(x => x.SourceId)
            .ThenBy(x => x.Modifier.Ordinal).ToArray();
        var keys = new HashSet<(EffectModifierLayer, int, StableId, int)>();
        var additive = 0;
        var product = scale;
        int? overridden = null;
        EffectModifierTarget? target = null;
        foreach (var source in ordered)
        {
            if (!Enum.IsDefined(typeof(EffectModifierLayer), source.Layer) ||
                !StableId.TryParse(source.SourceId.Value, out _) || source.Stacks is < 1 or > 255)
                throw new ArgumentException("A validated source/layer/stack count is required.", nameof(sources));
            if (!keys.Add((source.Layer, source.Priority, source.SourceId, source.Modifier.Ordinal)))
                throw new ArgumentException("Canonical modifier ordering keys must be unique.", nameof(sources));
            if (target.HasValue && target.Value != source.Modifier.Target)
                throw new ArgumentException("Each computation accepts only one stat/channel target.", nameof(sources));
            target = source.Modifier.Target;
            switch (source.Modifier.Operation)
            {
                case EffectModifierOperation.Add:
                    additive = checked(additive + checked(source.Modifier.Value * source.Stacks));
                    break;
                case EffectModifierOperation.Multiply:
                    for (var stack = 0; stack < source.Stacks; stack++) product = FixedMath.Mul(product, source.Modifier.Value, scale);
                    break;
                case EffectModifierOperation.Override:
                    overridden = source.Modifier.Value;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(sources));
            }
        }
        // Deliberately compute before Override/clamp: neither may conceal invalid arithmetic.
        var derived = FixedMath.Mul(checked(baseValue + additive), product, scale);
        return bounds.Clamp(overridden ?? derived);
    }
}
