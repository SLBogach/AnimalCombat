using System.Collections.ObjectModel;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Resolution;

internal sealed record ImpactIntent(
    ExternalId IntentId,
    ExternalId ImpactId,
    ExternalId HitGroupId,
    FighterId ActorId,
    FighterId TargetId,
    DecisionId DecisionId,
    ResolutionActionProfile Action,
    HitScheduleEntry Entry,
    int Tick,
    int Initiative,
    CommitDirection CommitDirection,
    EventId? SourceEventId)
{
    internal ResolutionClass Class => Action.ClassFor(Entry.Kind);
}

internal sealed class ResolutionGroup
{
    private readonly ReadOnlyCollection<ImpactIntent> _intents;

    internal ResolutionGroup(ExternalId id, IEnumerable<ImpactIntent> intents, bool isTrade)
    {
        Id = id;
        var copy = intents?.ToArray() ?? throw new ArgumentNullException(nameof(intents));
        if (copy.Length is < 1 or > 2 || copy.Select(intent => intent.IntentId).Distinct().Count() != copy.Length)
        {
            throw new ArgumentException("A resolution group must contain one or two unique intents.", nameof(intents));
        }

        if (isTrade && (copy.Length != 2 || copy.Any(intent => intent.Class != ResolutionClass.Strike)))
        {
            throw new ArgumentException("A trade requires two strike intents.", nameof(isTrade));
        }

        _intents = new ReadOnlyCollection<ImpactIntent>(copy);
        IsTrade = isTrade;
    }

    internal ExternalId Id { get; }
    internal IReadOnlyList<ImpactIntent> Intents => _intents;
    internal bool IsTrade { get; }
}

internal static class ImpactIntentCollector
{
    internal static IReadOnlyList<ImpactIntent> Collect(BattleState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var result = new List<ImpactIntent>();
        Collect(state.FighterA);
        Collect(state.FighterB);
        return result;

        void Collect(FighterRuntimeState actor)
        {
            var descriptor = actor.ActiveCombatAction;
            if (descriptor is null || actor.State == global::Battle.Contracts.Events.FighterState.Defeated)
            {
                return;
            }

            if (!descriptor.TargetFighterId.HasValue)
            {
                return;
            }

            foreach (var entry in descriptor.HitSchedule)
            {
                if (descriptor.AbsoluteImpactTick(entry) != state.Tick)
                {
                    continue;
                }

                result.Add(new ImpactIntent(
                    ResolutionIdentifiers.Intent(descriptor.DecisionId, entry.Ordinal),
                    ResolutionIdentifiers.Impact(descriptor.DecisionId, entry.Ordinal),
                    ResolutionIdentifiers.HitGroup(descriptor.DecisionId, entry.Ordinal),
                    actor.FighterId,
                    descriptor.TargetFighterId.Value,
                    descriptor.DecisionId,
                    descriptor.ResolutionProfile,
                    entry,
                    state.Tick,
                    actor.Initiative,
                    actor.CommitDirection,
                    actor.CombatLifecycleEventId));
            }
        }
    }
}

internal static class ImpactIntentOrderer
{
    internal static IReadOnlyList<ImpactIntent> Order(IEnumerable<ImpactIntent> intents)
    {
        if (intents is null)
        {
            throw new ArgumentNullException(nameof(intents));
        }

        return intents
            .OrderBy(intent => intent.Class)
            .ThenByDescending(intent => intent.Action.ActionPriority)
            .ThenByDescending(intent => intent.Initiative)
            .ThenBy(intent => intent.ActorId)
            .ThenBy(intent => intent.Action.Id)
            .ThenBy(intent => intent.Entry.Ordinal)
            .ThenBy(intent => intent.IntentId)
            .ToArray();
    }

    internal static IReadOnlyList<ResolutionGroup> BuildGroups(
        IEnumerable<ImpactIntent> intents,
        int tick)
    {
        var ordered = Order(intents);
        var consumed = new HashSet<ExternalId>();
        var groups = new List<ResolutionGroup>();
        foreach (var intent in ordered)
        {
            if (!consumed.Add(intent.IntentId))
            {
                continue;
            }

            ImpactIntent? partner = null;
            var isTrade = false;
            if (intent.Class == ResolutionClass.Counter)
            {
                partner = ordered.FirstOrDefault(candidate =>
                    !consumed.Contains(candidate.IntentId) &&
                    candidate.Class == ResolutionClass.Strike &&
                    candidate.ActorId == intent.TargetId &&
                    candidate.TargetId == intent.ActorId);
            }
            else if (intent.Class == ResolutionClass.Strike)
            {
                partner = ordered.FirstOrDefault(candidate =>
                    !consumed.Contains(candidate.IntentId) &&
                    candidate.Class == ResolutionClass.Strike &&
                    candidate.ActorId == intent.TargetId &&
                    candidate.TargetId == intent.ActorId &&
                    candidate.Action.ClashPriority == intent.Action.ClashPriority);
                isTrade = partner is not null;
            }
            else if (intent.Class == ResolutionClass.Grab)
            {
                partner = ordered.FirstOrDefault(candidate =>
                    !consumed.Contains(candidate.IntentId) &&
                    candidate.Class == ResolutionClass.Grab &&
                    candidate.Entry.Kind == HitPrimitiveKind.Grab &&
                    intent.Entry.Kind == HitPrimitiveKind.Grab &&
                    candidate.ActorId == intent.TargetId &&
                    candidate.TargetId == intent.ActorId);
            }

            var members = partner is null ? new[] { intent } : new[] { intent, partner };
            if (partner is not null)
            {
                consumed.Add(partner.IntentId);
            }

            groups.Add(new ResolutionGroup(
                ResolutionIdentifiers.ResolutionGroup(tick, groups.Count),
                Order(members),
                isTrade));
        }

        return groups;
    }
}
