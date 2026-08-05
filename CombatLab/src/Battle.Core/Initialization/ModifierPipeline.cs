using Battle.Contracts.Ids;

namespace Battle.Core.Initialization;

internal enum ModifierLayer
{
    BaseAnimal = 1,
    ModeNormalization = 2,
    Gear = 3,
    PassiveInitialization = 4,
    PermanentEffect = 5,
    TemporaryEffect = 6,
    Clamp = 7,
}

internal enum ModifierOperation
{
    Add,
    Multiply,
    Override,
}

internal readonly record struct StatModifier(
    ModifierLayer Layer,
    int Priority,
    StableId SourceId,
    string Stat,
    ModifierOperation Operation,
    int Value);

internal static class ModifierPipeline
{
    internal static IReadOnlyDictionary<string, int> Apply(
        IReadOnlyDictionary<string, int> baseStats,
        IEnumerable<StatModifier> modifiers,
        int fixedPointScale,
        Action<StatModifier>? applied = null)
    {
        if (baseStats is null)
        {
            throw new ArgumentNullException(nameof(baseStats));
        }

        if (modifiers is null)
        {
            throw new ArgumentNullException(nameof(modifiers));
        }

        if (fixedPointScale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedPointScale));
        }

        var result = new Dictionary<string, int>(baseStats, StringComparer.Ordinal);
        foreach (var modifier in modifiers
                     .OrderBy(item => item.Layer)
                     .ThenBy(item => item.Priority)
                     .ThenBy(item => item.SourceId)
                     .ThenBy(item => item.Stat, StringComparer.Ordinal))
        {
            if (!result.TryGetValue(modifier.Stat, out var current))
            {
                continue;
            }

            result[modifier.Stat] = modifier.Operation switch
            {
                ModifierOperation.Add => checked(current + modifier.Value),
                ModifierOperation.Multiply =>
                    checked((int)((long)current * modifier.Value / fixedPointScale)),
                ModifierOperation.Override => modifier.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(modifiers)),
            };
            applied?.Invoke(modifier);
        }

        return result;
    }
}
