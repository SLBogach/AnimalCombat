using Battle.Contracts.Effects;
using Battle.Contracts.Ids;
using Battle.Core.Effects;

namespace Battle.Core.UnitTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectStatMathTests
{
    private static EffectStatSource Source(string id, int value, EffectModifierOperation operation,
        int priority = 0, int stacks = 1, EffectModifierLayer layer = EffectModifierLayer.TemporaryEffect, int ordinal = 0,
        EffectModifierTarget target = EffectModifierTarget.Guard) =>
        new(layer, priority, new StableId(id), new EffectModifier(target, operation, value, ordinal), stacks);

    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-001")]
    public void MixedAddAndMultiplyAggregatesBeforeProductUnderEveryInsertionPermutation()
    {
        var sources = new[] { Source("gear", 10, EffectModifierOperation.Add, layer: EffectModifierLayer.Gear),
            Source("debuff", -30, EffectModifierOperation.Add), Source("buff", 1500, EffectModifierOperation.Multiply) };
        foreach (var permutation in Permutations(sources)) Assert.Equal(120, EffectStatMath.Compute(100, new StatBounds(0, 1000), permutation, 1000));
    }
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-002")]
    public void MathematicalFloorAndOverflowAreNotConcealedByClampOrOverride()
    {
        Assert.Equal(-2, EffectStatMath.Compute(-3, new StatBounds(-100, 100), [Source("mul", 500, EffectModifierOperation.Multiply)], 1000));
        Assert.Throws<OverflowException>(() => EffectStatMath.Compute(int.MaxValue, new StatBounds(0, 1), [Source("add", 1, EffectModifierOperation.Add)], 1000));
        Assert.Throws<OverflowException>(() => EffectStatMath.Compute(int.MaxValue, new StatBounds(0, 1),
            [Source("mul", int.MaxValue, EffectModifierOperation.Multiply), Source("override", 0, EffectModifierOperation.Override)], 1000));
        Assert.Throws<OverflowException>(() => EffectStatMath.Compute(0, new StatBounds(0, 1000), [Source("add", int.MaxValue, EffectModifierOperation.Add, stacks: 2)], 1000));
        Assert.Throws<OverflowException>(() => EffectStatMath.Compute(0, new StatBounds(0, 1000),
            [Source("a", int.MaxValue, EffectModifierOperation.Add), Source("b", 1, EffectModifierOperation.Add)], 1000));
    }
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-003")]
    public void ProductFloorsEachCanonicalFoldInsteadOfReversingRegistrationOrder()
    {
        var sources = new[] { Source("a", 1001, EffectModifierOperation.Multiply), Source("b", 1001, EffectModifierOperation.Multiply), Source("c", 1501, EffectModifierOperation.Multiply) };
        foreach (var permutation in Permutations(sources)) Assert.Equal(1504, EffectStatMath.Compute(1000, new StatBounds(0, 2000), permutation, 1000));
        Assert.Equal(1503, EffectStatMath.Compute(1000, new StatBounds(0, 2000),
            [sources[2] with { Priority = -1 }, sources[0], sources[1]], 1000));
    }
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-004")]
    public void LastCanonicalOverrideWinsOnceThenClamps()
    {
        var sources = new[] { Source("a", 100, EffectModifierOperation.Add), Source("b", 1500, EffectModifierOperation.Multiply),
            Source("override_first", 30, EffectModifierOperation.Override), Source("override_last", 7, EffectModifierOperation.Override, stacks: 3) };
        Assert.Equal(7, EffectStatMath.Compute(100, new StatBounds(0, 1000), sources.Reverse(), 1000));
        Assert.Equal(0, EffectStatMath.Compute(100, new StatBounds(0, 1000), sources.Append(Source("z", -5, EffectModifierOperation.Override)), 1000));
        // Layer precedes priority/ID, and modifier ordinal is the final tie-breaker.
        Assert.Equal(8, EffectStatMath.Compute(1, new StatBounds(0, 1000),
            [Source("same", 9, EffectModifierOperation.Override, ordinal: 0), Source("same", 8, EffectModifierOperation.Override, ordinal: 1),
             Source("z", 3, EffectModifierOperation.Override, priority: 100, layer: EffectModifierLayer.Gear)], 1000));
    }
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-005")]
    public void ExplicitSpeedBoundsClampOnlyAfterCheckedMathAndPermitEquality()
    {
        var bounds = new StatBounds(1, 500);
        var gear = Source("gear", 12, EffectModifierOperation.Add, layer: EffectModifierLayer.Gear, target: EffectModifierTarget.MoveSpeed);
        Assert.Equal(147, EffectStatMath.Compute(135, bounds, [gear], 1000));
        Assert.Equal(294, EffectStatMath.Compute(135, bounds, [gear, Source("speed", 2000, EffectModifierOperation.Multiply, target: EffectModifierTarget.MoveSpeed)], 1000));
        Assert.Equal(500, EffectStatMath.Compute(135, bounds, [gear, Source("speed", 4000, EffectModifierOperation.Multiply, target: EffectModifierTarget.MoveSpeed)], 1000));
        Assert.Equal(1, EffectStatMath.Compute(0, bounds, [], 1000));
        Assert.Equal(1, EffectStatMath.Compute(1, bounds, [], 1000));
        Assert.Equal(500, EffectStatMath.Compute(500, bounds, [], 1000));
    }
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-006")]
    public void RecomputeUsesImmutableBaseAndRemovingEffectRestoresExactGearValue()
    {
        var gear = Source("gear", 18, EffectModifierOperation.Add, layer: EffectModifierLayer.Gear, target: EffectModifierTarget.Armor);
        var effect = Source("effect", 1150, EffectModifierOperation.Multiply, target: EffectModifierTarget.Armor);
        for (var iteration = 0; iteration < 100; iteration++)
            Assert.Equal(135, EffectStatMath.Compute(100, new StatBounds(0, 2000), [gear, effect], 1000));
        Assert.Equal(118, EffectStatMath.Compute(100, new StatBounds(0, 2000), [gear], 1000));
    }
    [Fact]
    [Trait("AcceptanceId", "WP10-MOD-007")]
    public void StacksScaleAddsRepeatProductsAndApplyOverrideOnlyOnce()
    {
        var bounds = new StatBounds(0, 1000);
        Assert.Equal(124, EffectStatMath.Compute(100, bounds, [Source("precision", 8, EffectModifierOperation.Add, stacks: 3, target: EffectModifierTarget.Precision)], 1000));
        Assert.Equal(115, EffectStatMath.Compute(100, bounds, [Source("speed", 5, EffectModifierOperation.Add, stacks: 3, target: EffectModifierTarget.ActionSpeed)], 1000));
        Assert.Equal(172, EffectStatMath.Compute(100, bounds, [Source("mul", 1200, EffectModifierOperation.Multiply, stacks: 3)], 1000));
        Assert.Equal(7, EffectStatMath.Compute(100, bounds, [Source("override", 7, EffectModifierOperation.Override, stacks: 3)], 1000));
    }

    [Fact]
    public void InvalidSourcesDuplicateKeysAndScaleAreRejectedWithoutNumericFallback()
    {
        var source = Source("source", 1, EffectModifierOperation.Add);
        var bounds = new StatBounds(0, 1000);
        Assert.Throws<ArgumentNullException>(() => EffectStatMath.Compute(0, bounds, null!, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectStatMath.Compute(0, bounds, [], 0));
        Assert.Throws<ArgumentException>(() => EffectStatMath.Compute(0, bounds, [source, source], 1000));
        Assert.Throws<ArgumentException>(() => EffectStatMath.Compute(0, bounds,
            [source, Source("other", 1, EffectModifierOperation.Add, target: EffectModifierTarget.Armor)], 1000));
        foreach (var invalid in new[] { source with { Stacks = 0 }, source with { Stacks = 256 }, source with { SourceId = default }, source with { Layer = (EffectModifierLayer)999 } })
            Assert.Throws<ArgumentException>(() => EffectStatMath.Compute(0, bounds, [invalid], 1000));
        // Controlled internal corruption, distinct from validated external DATA.
        object boxed = source.Modifier;
        typeof(EffectModifier).GetField("<Operation>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(boxed, (EffectModifierOperation)999);
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectStatMath.Compute(0, bounds,
            [source with { Modifier = (EffectModifier)boxed }], 1000));
    }
    private static IEnumerable<EffectStatSource[]> Permutations(EffectStatSource[] items)
    {
        for (var first = 0; first < 3; first++) for (var second = 0; second < 3; second++) for (var third = 0; third < 3; third++)
            if (first != second && second != third && first != third) yield return [items[first], items[second], items[third]];
    }
}
