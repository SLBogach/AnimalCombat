using System.Collections.ObjectModel;
using Battle.Contracts.Effects;
using Battle.Contracts.Ids;

namespace Battle.Core.Effects;

internal readonly record struct EffectSetupIssue(string Code, string Path, string? Entity);
internal readonly record struct InitialStatSource(string Stat, int Priority, StableId SourceId,
    int Ordinal, EffectModifierOperation Operation, int Value);

internal sealed class EffectFighterDefinition
{
    internal EffectFighterDefinition(FighterId fighter, IDictionary<string, int> baseStats,
        IEnumerable<InitialStatSource> gear, IDictionary<string, int> initialStats,
        IEnumerable<StableId> selectedActions)
    {
        Fighter = fighter;
        BaseStats = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(baseStats, StringComparer.Ordinal));
        Gear = Array.AsReadOnly(gear.OrderBy(x => x.Priority).ThenBy(x => x.SourceId).ThenBy(x => x.Ordinal).ToArray());
        InitialStats = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(initialStats, StringComparer.Ordinal));
        SelectedActions = Array.AsReadOnly(selectedActions.Distinct().OrderBy(x => x).ToArray());
    }
    internal FighterId Fighter { get; }
    internal IReadOnlyDictionary<string, int> BaseStats { get; }
    internal IReadOnlyList<InitialStatSource> Gear { get; }
    internal IReadOnlyDictionary<string, int> InitialStats { get; }
    internal IReadOnlyList<StableId> SelectedActions { get; }
}

/// <summary>Immutable pre-start snapshot. No mutable store, queue, cache or RNG belongs here.</summary>
internal sealed class EffectRuntimeDefinition
{
    internal EffectRuntimeDefinition(int scale, int timeLimit, int depth, int triggers, int instances,
        int fatigueThreshold, int fall, int grounded, int getup, int stunMinimum, int stunMaximum,
        int controlK, IDictionary<string, StatBounds> bounds, IEnumerable<EffectProfile> effects,
        IEnumerable<EffectRuleProfile> rules, IEnumerable<ActionInterruptProfile> interrupts,
        IEnumerable<EffectFighterDefinition> fighters)
    {
        FixedPointScale = scale; TimeLimitTicks = timeLimit; MaximumTriggerDepth = depth;
        MaximumTriggersPerTick = triggers; MaximumInstancesPerFighter = instances;
        FatigueThreshold = fatigueThreshold; KnockdownFallTicks = fall;
        KnockdownGroundedTicks = grounded; KnockdownGetupTicks = getup;
        StunMinimumTicks = stunMinimum; StunMaximumTicks = stunMaximum; ControlK = controlK;
        Bounds = new ReadOnlyDictionary<string, StatBounds>(new Dictionary<string, StatBounds>(bounds, StringComparer.Ordinal));
        Effects = Array.AsReadOnly(effects.OrderBy(x => x.EffectId).ToArray());
        Rules = Array.AsReadOnly(rules.OrderBy(x => x.RuleId).ToArray());
        Interrupts = Array.AsReadOnly(interrupts.OrderBy(x => x.ActionId).ToArray());
        Fighters = Array.AsReadOnly(fighters.OrderBy(x => x.Fighter).ToArray());
    }
    internal int FixedPointScale { get; }
    internal int TimeLimitTicks { get; }
    internal int MaximumTriggerDepth { get; }
    internal int MaximumTriggersPerTick { get; }
    internal int MaximumInstancesPerFighter { get; }
    internal int FatigueThreshold { get; }
    internal int KnockdownFallTicks { get; }
    internal int KnockdownGroundedTicks { get; }
    internal int KnockdownGetupTicks { get; }
    internal int StunMinimumTicks { get; }
    internal int StunMaximumTicks { get; }
    internal int ControlK { get; }
    internal IReadOnlyDictionary<string, StatBounds> Bounds { get; }
    internal IReadOnlyList<EffectProfile> Effects { get; }
    internal IReadOnlyList<EffectRuleProfile> Rules { get; }
    internal IReadOnlyList<ActionInterruptProfile> Interrupts { get; }
    internal IReadOnlyList<EffectFighterDefinition> Fighters { get; }
}
