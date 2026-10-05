using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Contracts.Effects;

/// <summary>Frozen DATA filters; ordinary hits and hard control are separate policies.</summary>
public sealed class ActionInterruptProfile
{
    public ActionInterruptProfile(StableId actionId, ActionInterruptKind kind,
        HitInterruptStrength incomingStrength, HitInterruptStrength minimumStrength,
        IEnumerable<ActionPhase> interruptiblePhases, IEnumerable<ActionPhase> protectedPhases,
        IEnumerable<ControlCategory> ignoredCategories)
    {
        if (!StableId.TryParse(actionId.Value, out _)) throw new ArgumentException("A stable action ID is required.", nameof(actionId));
        RequireDefined(kind, nameof(kind));
        RequireDefined(incomingStrength, nameof(incomingStrength));
        RequireDefined(minimumStrength, nameof(minimumStrength));
        if (minimumStrength == HitInterruptStrength.None) throw new ArgumentOutOfRangeException(nameof(minimumStrength));
        var phases = Copy(interruptiblePhases, nameof(interruptiblePhases));
        var protections = Copy(protectedPhases, nameof(protectedPhases));
        var ignored = Copy(ignoredCategories, nameof(ignoredCategories));
        if (ignored.Any(x => x is ControlCategory.Grab or ControlCategory.Defeat) ||
            kind != ActionInterruptKind.Unstoppable && ignored.Length != 0)
            throw new ArgumentException("Only Unstoppable may ignore Stun/Knockdown; never Grab/Defeat.", nameof(ignoredCategories));
        ActionId = actionId;
        Kind = kind;
        IncomingStrength = incomingStrength;
        MinimumStrength = minimumStrength;
        InterruptiblePhases = Array.AsReadOnly(phases);
        ProtectedPhases = Array.AsReadOnly(protections);
        IgnoredCategories = Array.AsReadOnly(ignored);
    }

    public StableId ActionId { get; }
    public ActionInterruptKind Kind { get; }
    public HitInterruptStrength IncomingStrength { get; }
    public HitInterruptStrength MinimumStrength { get; }
    public IReadOnlyList<ActionPhase> InterruptiblePhases { get; }
    public IReadOnlyList<ActionPhase> ProtectedPhases { get; }
    public IReadOnlyList<ControlCategory> IgnoredCategories { get; }

    public bool AllowsHitInterrupt(ActionPhase phase, HitInterruptStrength strength)
    {
        RequireDefined(phase, nameof(phase));
        RequireDefined(strength, nameof(strength));
        if (strength == HitInterruptStrength.None || !InterruptiblePhases.Contains(phase)) return false;
        // Armored's threshold applies only inside its explicit protection phases.
        return strength >= (Kind == ActionInterruptKind.Armored && !ProtectedPhases.Contains(phase)
            ? HitInterruptStrength.Light : MinimumStrength);
    }

    public bool AllowsControl(ActionPhase phase, ControlCategory category)
    {
        RequireDefined(phase, nameof(phase));
        RequireDefined(category, nameof(category));
        return !(Kind == ActionInterruptKind.Unstoppable && ProtectedPhases.Contains(phase) &&
            IgnoredCategories.Contains(category));
    }

    private static T[] Copy<T>(IEnumerable<T> source, string name) where T : struct, Enum
    {
        if (source is null) throw new ArgumentNullException(name);
        var values = source.ToArray();
        foreach (var value in values) RequireDefined(value, name);
        if (values.Distinct().Count() != values.Length) throw new ArgumentException("Duplicate filter entries.", name);
        Array.Sort(values);
        return values;
    }

    private static void RequireDefined<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)) throw new ArgumentOutOfRangeException(name);
    }
}
