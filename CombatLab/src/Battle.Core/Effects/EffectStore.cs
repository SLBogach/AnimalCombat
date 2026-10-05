using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Effects;

internal readonly record struct EffectOrigin(StableId OwnerId, StableId? ActionId, StableId RuleId)
{
    internal int CompareTo(EffectOrigin other)
    {
        var comparison = OwnerId.CompareTo(other.OwnerId);
        if (comparison == 0) comparison = StringComparer.Ordinal.Compare(ActionId?.Value ?? "", other.ActionId?.Value ?? "");
        return comparison != 0 ? comparison : RuleId.CompareTo(other.RuleId);
    }
}
internal sealed record ActiveEffect(EffectProfile Profile, int Stacks, int EndExclusiveTick,
    EffectOrigin Origin, EventId LatestApplicationEventId)
{
    internal EffectFrame Frame(int tick) => new(Profile.EffectId, Stacks, System.Math.Max(0, checked(EndExclusiveTick - tick)), Profile.ExpiryBoundary);
}
internal readonly record struct EffectMutation(ActiveEffect? Before, ActiveEffect? After, EffectRemoveReason? RemoveReason)
{
    internal bool IsRemoval => After is null;
}
internal readonly record struct EffectActivationBudget(int Tick, int TickCount, int BattleCount, int NextAllowedTick);

/// <summary>
/// Per-fighter preview store. Clone before a closure; only publish a clone after full group preflight.
/// No journal, RNG, timer event emission or private mutable state is shared across stores.
/// </summary>
internal sealed class EffectStore
{
    private readonly Dictionary<StableId, ActiveEffect> groups;
    private readonly Dictionary<StableId, EffectActivationBudget> budgets;
    private readonly int instanceCap;
    internal EffectStore(int instanceCap)
    {
        if (instanceCap is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(instanceCap));
        this.instanceCap = instanceCap;
        groups = new Dictionary<StableId, ActiveEffect>();
        budgets = new Dictionary<StableId, EffectActivationBudget>();
    }
    private EffectStore(EffectStore source)
    {
        instanceCap = source.instanceCap;
        groups = new Dictionary<StableId, ActiveEffect>(source.groups);
        budgets = new Dictionary<StableId, EffectActivationBudget>(source.budgets);
    }
    internal EffectStore Clone() => new(this);
    internal IReadOnlyList<ActiveEffect> Active => groups.Values.OrderBy(x => x.Profile.Priority).ThenBy(x => x.Profile.EffectId).ToArray();
    internal IReadOnlyList<EffectFrame> Frames(int tick) => groups.Values.OrderBy(x => x.Profile.EffectId).Select(x => x.Frame(tick)).ToArray();
    internal EffectActivationBudget Budget(StableId effectId) => budgets.TryGetValue(effectId, out var value) ? value : default;

    internal bool CanApply(EffectProfile profile, int tick)
    {
        var budget = Budget(profile.EffectId);
        return tick >= budget.NextAllowedTick &&
            (budget.Tick != tick || budget.TickCount < profile.MaxActivationsPerTick) &&
            budget.BattleCount < profile.MaxActivationsPerBattle;
    }

    // Project each public delta in order; the fully charged planned store is published afterwards.
    internal void Project(EffectMutation mutation)
    {
        var profile = (mutation.Before ?? mutation.After)?.Profile ??
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Empty effect mutation.");
        groups.TryGetValue(profile.StackGroup, out var current);
        if (current != mutation.Before)
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Effect mutation precondition changed.");
        if (mutation.After is null) groups.Remove(profile.StackGroup);
        else groups[profile.StackGroup] = mutation.After;
    }

    internal IReadOnlyList<EffectMutation> Apply(EffectProfile incoming, EffectOrigin origin, int tick, EventId applicationEventId)
    {
        if (incoming is null) throw new ArgumentNullException(nameof(incoming));
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (!StableId.TryParse(origin.OwnerId.Value, out _) || !StableId.TryParse(origin.RuleId.Value, out _) ||
            (origin.ActionId.HasValue && !StableId.TryParse(origin.ActionId.Value.Value, out _)) || !EventId.TryParse(applicationEventId.Value, out _))
            throw new ArgumentException("Canonical origin/application identifiers are required.", nameof(origin));
        try
        {
            var budget = Budget(incoming.EffectId);
            var count = budget.Tick == tick ? budget.TickCount : 0;
            if (tick < budget.NextAllowedTick || count >= incoming.MaxActivationsPerTick || budget.BattleCount >= incoming.MaxActivationsPerBattle)
                return Array.Empty<EffectMutation>();
            var end = checked(tick + incoming.DurationTicks);
            groups.TryGetValue(incoming.StackGroup, out var current);
            ActiveEffect next;
            var replacement = false;
            if (current is null)
            {
                if (groups.Count >= instanceCap) throw Failure(EngineFailureCodes.EffectInstanceCapExceeded, "Effect instance cap exceeded.");
                next = new ActiveEffect(incoming, 1, end, origin, applicationEventId);
            }
            else
            {
                RequireCompatible(current.Profile, incoming);
                switch (incoming.StackPolicy)
                {
                    case EffectStackPolicy.Reject: return Array.Empty<EffectMutation>();
                    case EffectStackPolicy.Refresh:
                    case EffectStackPolicy.AddStacks:
                        end = incoming.RefreshRule == EffectRefreshRule.KeepLonger ? System.Math.Max(current.EndExclusiveTick, end) : end;
                        var stacks = incoming.StackPolicy == EffectStackPolicy.AddStacks ? System.Math.Min(checked(current.Stacks + 1), incoming.StackCap) : current.Stacks;
                        if (stacks == current.Stacks && end == current.EndExclusiveTick) return Array.Empty<EffectMutation>();
                        next = current with { Stacks = stacks, EndExclusiveTick = end, Origin = origin, LatestApplicationEventId = applicationEventId };
                        break;
                    case EffectStackPolicy.StrongestWins:
                        var candidate = new ActiveEffect(incoming, 1, end, origin, applicationEventId);
                        var strength = CompareStrength(candidate, current);
                        if (strength < 0 || strength == 0 && CompareIdentity(candidate, current) >= 0) return Array.Empty<EffectMutation>();
                        next = candidate; replacement = true;
                        break;
                    case EffectStackPolicy.Replace:
                        next = new ActiveEffect(incoming, 1, end, origin, applicationEventId); replacement = true;
                        break;
                    default: throw Failure(EngineFailureCodes.EffectInvalidMutation, "Unknown stack policy.");
                }
            }
            if (groups.Values.Any(x => x.Profile.EffectId == incoming.EffectId && x.Profile.StackGroup != incoming.StackGroup))
                throw Failure(EngineFailureCodes.EffectInvalidMutation, "Duplicate public effect identity.");
            // Precompute checked budgets before publishing any mutation to this preview.
            var charged = new EffectActivationBudget(tick, checked(count + 1), checked(budget.BattleCount + 1), checked(tick + incoming.InternalCooldownTicks));
            groups[incoming.StackGroup] = next;
            budgets[incoming.EffectId] = charged;
            return replacement
                ? new[] { new EffectMutation(current, null, EffectRemoveReason.Replaced), new EffectMutation(null, next, null) }
                : new[] { new EffectMutation(current, next, null) };
        }
        catch (OverflowException) { throw Failure(EngineFailureCodes.EffectArithmeticOverflow, "Effect timer/counter arithmetic overflow."); }
    }

    internal IReadOnlyList<EffectMutation> Remove(StableId id, EffectRemoveReason reason)
    {
        if (!Enum.IsDefined(typeof(EffectRemoveReason), reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        var effect = groups.Values.SingleOrDefault(x => x.Profile.EffectId == id);
        if (effect is null) return Array.Empty<EffectMutation>();
        groups.Remove(effect.Profile.StackGroup);
        return new[] { new EffectMutation(effect, null, reason) };
    }
    internal IReadOnlyList<ActiveEffect> Due(int tick, EffectExpiryBoundary boundary)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        if (!Enum.IsDefined(typeof(EffectExpiryBoundary), boundary)) throw new ArgumentOutOfRangeException(nameof(boundary));
        return Active.Where(x => x.Profile.ExpiryBoundary == boundary &&
            (boundary == EffectExpiryBoundary.ExpireBeforeTick ? x.EndExclusiveTick <= tick : (long)x.EndExclusiveTick <= (long)tick + 1)).ToArray();
    }
    internal IEnumerable<EffectStatSource> Modifiers(EffectModifierTarget target)
    {
        foreach (var effect in Active)
            foreach (var modifier in effect.Profile.Modifiers.Where(x => x.Target == target))
                yield return effect.Profile.SemanticRole == EffectSemanticRole.ControlFatigue && target == EffectModifierTarget.HardControlDuration
                    ? new EffectStatSource(EffectModifierLayer.TemporaryEffect, effect.Profile.Priority, effect.Profile.EffectId,
                        new EffectModifier(target, EffectModifierOperation.Multiply, effect.Profile.Lookup[effect.Stacks], modifier.Ordinal), 1)
                    : new EffectStatSource(EffectModifierLayer.TemporaryEffect, effect.Profile.Priority, effect.Profile.EffectId, modifier, effect.Stacks);
    }
    private static void RequireCompatible(EffectProfile current, EffectProfile incoming)
    {
        var single = incoming.StackPolicy is EffectStackPolicy.Refresh or EffectStackPolicy.AddStacks;
        if (current.StackPolicy != incoming.StackPolicy || current.CompareKey != incoming.CompareKey ||
            single && current.EffectId != incoming.EffectId ||
            !current.Modifiers.Select(x => x.Target).SequenceEqual(incoming.Modifiers.Select(x => x.Target)))
            throw Failure(EngineFailureCodes.EffectInvalidMutation, "Incompatible profiles in an occupied stack group.");
    }
    private static int CompareStrength(ActiveEffect incoming, ActiveEffect current) => incoming.Profile.CompareKey switch
    {
        EffectCompareKey.Value1 => incoming.Profile.Modifiers[0].Value.CompareTo(current.Profile.Modifiers[0].Value),
        EffectCompareKey.DurationTicks => incoming.Profile.DurationTicks.CompareTo(current.Profile.DurationTicks),
        EffectCompareKey.StackCount => incoming.Stacks.CompareTo(current.Stacks),
        _ => throw Failure(EngineFailureCodes.EffectInvalidMutation, "Invalid StrongestWins compare domain."),
    };
    private static int CompareIdentity(ActiveEffect incoming, ActiveEffect current)
    {
        var comparison = incoming.Profile.EffectId.CompareTo(current.Profile.EffectId);
        return comparison != 0 ? comparison : incoming.Origin.CompareTo(current.Origin);
    }
    private static EngineInvariantException Failure(ReasonCode code, string message) => new(code, "effects", message);
}
