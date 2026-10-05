using Battle.Contracts.Effects;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Core.Engine;

namespace Battle.Core.Effects;

internal sealed record EffectHookContext(EffectHook Hook, CombatEventDraft? Source,
    FighterId? ActionOwner, StableId? ActionId, DecisionId? DecisionId, ExternalId? GroupId,
    int Damage, bool NormalHit, IReadOnlyDictionary<FighterId, IReadOnlyList<StableId>> Subscribers)
{
    internal FighterId? MutationOwner { get; init; }
}

/// <summary>Private per-battle state. Definition is shared immutable DATA; every mutation runs in a batch preview.</summary>
internal sealed class EffectRuntime
{
    private readonly EffectRuntimeDefinition definition;
    private readonly Dictionary<FighterId, EffectStore> stores;
    private readonly Dictionary<EventId, EffectHookContext> applications;
    private readonly Dictionary<FighterId, IReadOnlyDictionary<EffectModifierTarget, int>> channels;
    private readonly List<EffectAdmission> diagnostics;
    private EffectTriggerQueue queue;

    internal EffectRuntime(EffectRuntimeDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        stores = new Dictionary<FighterId, EffectStore>
        {
            [FighterId.FighterA] = new(definition.MaximumInstancesPerFighter),
            [FighterId.FighterB] = new(definition.MaximumInstancesPerFighter),
        };
        applications = new Dictionary<EventId, EffectHookContext>();
        channels = new Dictionary<FighterId, IReadOnlyDictionary<EffectModifierTarget, int>>();
        diagnostics = new List<EffectAdmission>();
        queue = new EffectTriggerQueue(definition.MaximumTriggerDepth, definition.MaximumTriggersPerTick);
    }

    private EffectRuntime(EffectRuntime source)
    {
        definition = source.definition;
        stores = source.stores.ToDictionary(x => x.Key, x => x.Value.Clone());
        applications = new Dictionary<EventId, EffectHookContext>(source.applications);
        channels = new Dictionary<FighterId, IReadOnlyDictionary<EffectModifierTarget, int>>(source.channels);
        diagnostics = new List<EffectAdmission>(source.diagnostics);
        queue = source.queue.Clone();
    }

    internal EffectRuntime Clone() => new(this);
    internal int ActiveCount => stores.Values.Sum(x => x.Active.Count);
    internal IReadOnlyList<EffectAdmission> Diagnostics => diagnostics;
    internal EffectStore Store(FighterId fighter) => stores[fighter];
    internal EffectTriggerQueue Queue => queue;
    internal int Channel(FighterId fighter, EffectModifierTarget target) => channels[fighter][target];
    internal EffectRuntimeDefinition Definition => definition;
    internal EffectDecisionView CaptureDecision(BattleState state, FighterId fighter) => new(
        stores[fighter].Modifiers(EffectModifierTarget.BlockWeight)
            .Concat(stores[fighter].Modifiers(EffectModifierTarget.PunishWeight))
            .Concat(stores[fighter].Modifiers(EffectModifierTarget.WallActionWeight)),
        state.CanBeGrabbed(fighter, state.Tick));
    internal ActionInterruptProfile? Interrupt(StableId action) => definition.Interrupts.SingleOrDefault(x => x.ActionId == action);
    internal bool HasRole(EffectSemanticRole role) => definition.Effects.Any(x => x.SemanticRole == role);
    internal int RoleEnd(FighterId fighter, EffectSemanticRole role) => stores[fighter].Active
        .Where(x => x.Profile.SemanticRole == role).Select(x => x.EndExclusiveTick).DefaultIfEmpty(0).Max();

    internal void Recompute(BattleState state, FighterId fighter)
    {
        var source = definition.Fighters.Single(x => x.Fighter == fighter);
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        var computedChannels = new Dictionary<EffectModifierTarget, int>();
        foreach (var target in (EffectModifierTarget[])Enum.GetValues(typeof(EffectModifierTarget)))
        {
            var name = target.ToString();
            var modifiers = stores[fighter].Modifiers(target);
            if (source.BaseStats.TryGetValue(name, out var baseValue))
            {
                var gear = source.Gear.Where(x => x.Stat == name).Select(x => new EffectStatSource(
                    EffectModifierLayer.Gear, x.Priority, x.SourceId,
                    new EffectModifier(target, x.Operation, x.Value, x.Ordinal), 1));
                stats[name] = EffectStatMath.Compute(baseValue, definition.Bounds[name], gear.Concat(modifiers), definition.FixedPointScale);
            }
            else
            {
                var identity = target is EffectModifierTarget.DamageTaken or EffectModifierTarget.DamageDealt or
                    EffectModifierTarget.BlockWeight or EffectModifierTarget.PunishWeight or
                    EffectModifierTarget.WallActionWeight or EffectModifierTarget.HardControlDuration
                    ? definition.FixedPointScale
                    : target is EffectModifierTarget.HardControlAllowed or EffectModifierTarget.GrabAllowed or EffectModifierTarget.KnockdownAllowed ? 1 : 0;
                var bounds = target is EffectModifierTarget.HardControlAllowed or EffectModifierTarget.GrabAllowed or EffectModifierTarget.KnockdownAllowed
                    ? new StatBounds(0, 1) : new StatBounds(int.MinValue, int.MaxValue);
                computedChannels[target] = EffectStatMath.Compute(identity, bounds, modifiers, definition.FixedPointScale);
            }
        }
        state.Get(fighter).SetDerivedStats(stats);
        state.Get(fighter).SetEffectFrames(stores[fighter].Frames(state.Tick));
        channels[fighter] = computedChannels;
    }

    internal void SynchronizeFrames(BattleState state)
    {
        state.FighterA.SynchronizeEffectControlTimer(state.Tick);
        state.FighterB.SynchronizeEffectControlTimer(state.Tick);
        state.FighterA.SetEffectFrames(stores[FighterId.FighterA].Frames(state.Tick));
        state.FighterB.SetEffectFrames(stores[FighterId.FighterB].Frames(state.Tick));
    }

    internal void CloseEvents(BattleState state, CombatEventEmitter emitter, IEnumerable<CombatEventDraft> events)
    {
        var sourceEvents = events.ToArray();
        var byId = sourceEvents.GroupBy(x => x.EventId).ToDictionary(x => x.Key, x => x.First());
        var roots = sourceEvents.SelectMany(draft => HooksFor(state, draft, byId)).ToArray();
        Close(state, emitter, roots);
    }

    internal void EndOfTick(BattleState state, CombatEventEmitter emitter)
    {
        var source = emitter.LastEventId ?? throw Invalid("EndOfTick needs an earlier canonical source.");
        var sequence = emitter.EventCount - 1;
        var subscribers = Subscribers();
        var roots = new[] { FighterId.FighterA, FighterId.FighterB }.Select(subject =>
        {
            var occurrence = EffectOccurrence.EndOfTick(state.Tick, subject);
            return new EffectHookContext(new EffectHook(EffectTrigger.EndOfTick, state.Tick, subject, source,
                sequence, occurrence, occurrence, 0), emitter.LastDraft, null, null, null, null, 0, false, subscribers);
        });
        Close(state, emitter, roots);
    }

    internal void Expire(BattleState state, CombatEventEmitter emitter, EffectExpiryBoundary boundary)
    {
        SynchronizeFrames(state);
        // Re-evaluate after every closure: an After D1 consequence is due on this same boundary.
        while (true)
        {
            var due = stores.SelectMany(pair => pair.Value.Due(state.Tick, boundary).Select(effect => (Fighter: pair.Key, Effect: effect)))
                .OrderBy(x => x.Effect.Profile.Priority).ThenBy(x => x.Effect.Profile.EffectId).ThenBy(x => x.Fighter).FirstOrDefault();
            if (due.Effect is null) break;
            var lineage = applications[due.Effect.LatestApplicationEventId];
            var cause = lineage.Source!;
            var occurrence = EffectOccurrence.Event(cause.EventId);
            var root = new EffectHookContext(new EffectHook(EffectTrigger.EffectRemoved, state.Tick, due.Fighter,
                cause.EventId, cause.Sequence, occurrence, occurrence, 0), cause, lineage.ActionOwner, cause.ActionId,
                cause.DecisionId, cause.ResolutionGroupId, 0, false, Subscribers())
            {
                MutationOwner = due.Effect.Origin.OwnerId.Value == "fighter_a" ? FighterId.FighterA : FighterId.FighterB,
            };
            var mutations = stores[due.Fighter].Clone().Remove(due.Effect.Profile.EffectId,
                boundary == EffectExpiryBoundary.ExpireBeforeTick ? EffectRemoveReason.ExpiredBeforeTick : EffectRemoveReason.ExpiredAfterTick);
            var hooks = PublishMutations(state, emitter, due.Fighter, mutations, root)
                .Select(context => context with { Hook = context.Hook with { Depth = 0, Root = context.Hook.Occurrence } });
            Close(state, emitter, hooks);
        }
        PruneApplications();
    }

    internal void Cleanup(BattleState state, CombatEventEmitter emitter, EventId? source)
    {
        SynchronizeFrames(state);
        var active = stores.SelectMany(pair => pair.Value.Active.Select(effect => (Fighter: pair.Key, Effect: effect)))
            .OrderBy(x => x.Effect.Profile.Priority).ThenBy(x => x.Effect.Profile.EffectId).ThenBy(x => x.Fighter).ToArray();
        foreach (var item in active)
        {
            var before = state.Get(item.Fighter).ToFrame();
            foreach (var mutation in stores[item.Fighter].Clone().Remove(item.Effect.Profile.EffectId, EffectRemoveReason.BattleEnded))
                stores[item.Fighter].Project(mutation);
            Recompute(state, item.Fighter);
            _ = emitter.Emit(state.Tick, new EffectRemovedPayload(source.HasValue ? new[] { source.Value } : Array.Empty<EventId>(),
                item.Effect.Stacks, 0, EffectRemoveReason.BattleEnded), actorId: item.Fighter,
                effectId: item.Effect.Profile.EffectId, sourceEventId: source,
                reasonCodes: new[] { new ReasonCode("BattleEnded") }, before: new FramePair(before, null),
                after: new FramePair(state.Get(item.Fighter).ToFrame(), null));
        }
        applications.Clear();
    }

    private void Close(BattleState state, CombatEventEmitter emitter, IEnumerable<EffectHookContext> roots)
    {
        var contexts = new Dictionary<(FighterId, StableId, EffectOccurrence, FighterId), EffectHookContext>();
        Admit(roots);
        while (queue.Dequeue() is { } node)
        {
            var key = (node.Owner, node.Rule.RuleId, node.Hook.Occurrence, node.Recipient);
            var context = contexts[key]; contexts.Remove(key);
            // A preceding node may have defeated or depleted this recipient's effect budget.
            if (node.Rule.Primitive == EffectPrimitive.ApplyEffect &&
                (state.Get(node.Recipient).Health == 0 || state.Get(node.Recipient).State == FighterState.Defeated)) continue;
            var store = stores[node.Recipient];
            var planned = store.Clone();
            IReadOnlyList<EffectMutation> mutations;
            if (node.Rule.Primitive == EffectPrimitive.ApplyEffect)
            {
                var profile = definition.Effects.Single(x => x.EffectId == node.Rule.EffectId);
                var origin = new EffectOrigin(FighterStableId(node.Owner), context.ActionId, node.Rule.RuleId);
                mutations = planned.Apply(profile, origin, state.Tick, EventId.FromSequence(emitter.EventCount));
                if (mutations.Count == 2)
                {
                    planned = store.Clone();
                    mutations = planned.Apply(profile, origin, state.Tick, EventId.FromSequence(emitter.EventCount + 1));
                }
            }
            else mutations = planned.Remove(node.Rule.EffectId, EffectRemoveReason.Dispelled);
            var hooks = PublishMutations(state, emitter, node.Recipient, mutations, context with { MutationOwner = node.Owner });
            stores[node.Recipient] = planned;
            Admit(hooks);
        }
        PruneApplications();

        void Admit(IEnumerable<EffectHookContext> hooks)
        {
            var pending = new List<(EffectQueueCandidate Candidate, EffectHookContext Context)>();
            foreach (var context in hooks)
            foreach (var rule in definition.Rules.Where(rule => rule.Trigger == context.Hook.Trigger))
            foreach (var owner in Owners(rule, context))
            {
                var recipient = rule.Recipient == EffectRecipient.Self ? owner : state.GetOpponent(owner).FighterId;
                var alive = state.Get(recipient).Health > 0 && state.Get(recipient).State != FighterState.Defeated;
                var eligible = (rule.Primitive == EffectPrimitive.RemoveEffect || alive) && (rule.Condition switch
                {
                    EffectCondition.Always => true,
                    EffectCondition.PositiveDamage => context.Damage > 0,
                    EffectCondition.NormalHit => context.NormalHit,
                    EffectCondition.LivingTarget => alive,
                    _ => false,
                });
                var budget = rule.Primitive == EffectPrimitive.RemoveEffect || stores[recipient].CanApply(
                    definition.Effects.Single(x => x.EffectId == rule.EffectId), state.Tick);
                pending.Add((new EffectQueueCandidate(context.Hook, rule, owner, recipient, eligible, budget), context));
            }
            var contextMap = pending.GroupBy(x => (x.Candidate.Owner, x.Candidate.Rule.RuleId, x.Candidate.Hook.Occurrence, x.Candidate.Recipient))
                .ToDictionary(x => x.Key, x => x.First().Context);
            foreach (var result in queue.AdmitLevel(pending.Select(x => x.Candidate)))
            {
                if (result.Admission == EffectAdmission.Queued)
                {
                    var node = result.Candidate;
                    contexts.Add((node.Owner, node.Rule.RuleId, node.Hook.Occurrence, node.Recipient),
                        contextMap[(node.Owner, node.Rule.RuleId, node.Hook.Occurrence, node.Recipient)]);
                }
                else
                {
                    // Bounded recent capture: later cycle suppression is recorded even after earlier rejections filled the buffer.
                    if (diagnostics.Count == definition.MaximumTriggersPerTick) diagnostics.RemoveAt(0);
                    diagnostics.Add(result.Admission);
                }
            }
        }
    }

    private IEnumerable<EffectHookContext> PublishMutations(BattleState state, CombatEventEmitter emitter,
        FighterId recipient, IReadOnlyList<EffectMutation> mutations, EffectHookContext cause)
    {
        var children = new List<EffectHookContext>();
        foreach (var mutation in mutations)
        {
            var subscribed = Subscribers(); // Includes the owner immediately before removal.
            var fighter = state.Get(recipient);
            var before = fighter.ToFrame();
            stores[recipient].Project(mutation);
            Recompute(state, recipient);
            var profile = (mutation.After ?? mutation.Before)!.Profile;
            var sourceOwner = cause.MutationOwner ?? cause.Hook.Subject;
            FighterId? target = sourceOwner != recipient ? sourceOwner : null;
            var targetFrame = target.HasValue ? state.Get(target.Value).ToFrame() : null;
            CombatEventPayload payload = mutation.IsRemoval
                ? new EffectRemovedPayload(new[] { cause.Hook.SourceEventId }, mutation.Before!.Stacks, 0, mutation.RemoveReason!.Value)
                : new EffectAddedPayload(new[] { cause.Hook.SourceEventId }, mutation.Before?.Stacks ?? 0,
                    mutation.After!.Stacks, mutation.After.Frame(state.Tick).TicksRemaining, profile.ExpiryBoundary, profile.StackPolicy);
            var identity = emitter.Emit(state.Tick, payload, recipient, target, cause.ActionId, profile.EffectId,
                cause.DecisionId, cause.GroupId, cause.Hook.SourceEventId,
                new[] { new ReasonCode(mutation.IsRemoval ? mutation.RemoveReason!.Value.ToString() : "EffectApplied") },
                before: new FramePair(before, targetFrame), after: new FramePair(fighter.ToFrame(), targetFrame));
            var draft = emitter.LastDraft!;
            var occurrence = EffectOccurrence.Event(identity.EventId);
            var trigger = mutation.IsRemoval ? EffectTrigger.EffectRemoved : EffectTrigger.EffectAdded;
            var child = new EffectHookContext(new EffectHook(trigger, state.Tick, recipient, identity.EventId, identity.Sequence,
                occurrence, cause.Hook.Root, checked(cause.Hook.Depth + 1)), draft, cause.ActionOwner,
                cause.ActionId, cause.DecisionId, cause.GroupId, 0, false, mutation.IsRemoval ? subscribed : Subscribers());
            if (!mutation.IsRemoval) applications[identity.EventId] = child;
            children.Add(child);
            if (profile.SemanticRole == EffectSemanticRole.ControlFatigue && mutation.After is not null &&
                (mutation.Before?.Stacks ?? 0) < definition.FatigueThreshold && mutation.After.Stacks >= definition.FatigueThreshold)
                children.Add(child with { Hook = child.Hook with { Trigger = EffectTrigger.FatigueThresholdReached } });
        }
        return children;
    }

    private IEnumerable<EffectHookContext> HooksFor(BattleState state, CombatEventDraft draft,
        IReadOnlyDictionary<EventId, CombatEventDraft> sourceEvents)
    {
        var subscribers = Subscribers();
        if (draft.EventType == CombatEventType.BattleStarted)
        {
            yield return Root(EffectTrigger.BattleStart, FighterId.FighterA);
            yield return Root(EffectTrigger.BattleStart, FighterId.FighterB);
        }
        if (draft.Payload is DamageAppliedPayload damage && draft.ActorId.HasValue && draft.TargetId.HasValue)
        {
            var amount = checked(damage.HealthBefore - damage.HealthAfter);
            var normal = !draft.ReasonCodes.Any(x => x.Value == "WallDamage") && draft.SourceEventId.HasValue &&
                sourceEvents.TryGetValue(draft.SourceEventId.Value, out var hit) && hit.EventType == CombatEventType.AttackHit;
            yield return Root(EffectTrigger.DamageDealt, draft.ActorId.Value) with { Damage = amount, NormalHit = normal };
            yield return Root(EffectTrigger.DamageTaken, draft.TargetId.Value) with { Damage = amount, NormalHit = normal };
        }
        if (draft.EventType is CombatEventType.Blocked or CombatEventType.Dodged && draft.TargetId.HasValue)
            yield return Root(draft.EventType == CombatEventType.Blocked ? EffectTrigger.Blocked : EffectTrigger.Dodged, draft.TargetId.Value);
        if (draft.EventType == CombatEventType.AttackHit && draft.TargetId.HasValue && draft.ReasonCodes.Any(x => x.Value == "GuardBreak"))
            yield return Root(EffectTrigger.GuardBreak, draft.TargetId.Value);
        if (draft.Payload is StateChangedPayload change && draft.ActorId.HasValue && state.Get(draft.ActorId.Value).Health > 0)
        {
            if (change.OldState is FighterState.Stunned or FighterState.KnockedDown && change.NewState == FighterState.DecisionReady)
                yield return Root(EffectTrigger.ControlEnded, draft.ActorId.Value);
            if (change.OldState == FighterState.KnockedDown && change.NewState == FighterState.DecisionReady)
                yield return Root(EffectTrigger.WakeupCompleted, draft.ActorId.Value);
            if (change.OldState != FighterState.KnockedDown && change.NewState == FighterState.KnockedDown)
                yield return Root(EffectTrigger.Knockdown, draft.ActorId.Value);
        }
        if (draft.EventType == CombatEventType.GrabEnded && draft.TargetId.HasValue && state.Get(draft.TargetId.Value).Health > 0)
        {
            yield return Root(EffectTrigger.GrabEnded, draft.TargetId.Value);
            yield return Root(EffectTrigger.ControlEnded, draft.TargetId.Value);
        }
        EffectHookContext Root(EffectTrigger trigger, FighterId subject)
        {
            var occurrence = EffectOccurrence.Event(draft.EventId);
            var actionOwner = draft.ActorId;
            if (draft.Payload is StateChangedPayload && draft.ActionId.HasValue)
            {
                // StateChanged.actor is the controlled victim, not the originating action owner.
                actionOwner = null;
                var actionId = draft.ActionId.Value;
                var cause = draft;
                for (var remaining = sourceEvents.Count; remaining > 0 && cause.SourceEventId.HasValue &&
                    sourceEvents.TryGetValue(cause.SourceEventId.Value, out cause); remaining--)
                {
                    if (cause.Payload is not (AttackHitPayload or DamageAppliedPayload) ||
                        !cause.ActionId.HasValue || cause.ActionId.Value != actionId || cause.DecisionId != draft.DecisionId) continue;
                    actionOwner = cause.ActorId;
                    break;
                }
            }
            return new EffectHookContext(new EffectHook(trigger, state.Tick, subject, draft.EventId, draft.Sequence,
                occurrence, occurrence, 0), draft, actionOwner, draft.ActionId, draft.DecisionId,
                draft.ResolutionGroupId, 0, draft.EventType == CombatEventType.AttackHit, subscribers);
        }
    }

    private IEnumerable<FighterId> Owners(EffectRuleProfile rule, EffectHookContext context)
    {
        if (rule.OwnerKind == EffectOwnerKind.Global) yield return context.Hook.Subject;
        else if (rule.OwnerKind == EffectOwnerKind.Action && context.ActionOwner.HasValue && context.ActionId == rule.OwnerId &&
            definition.Fighters.Single(x => x.Fighter == context.ActionOwner.Value).SelectedActions.Contains(rule.OwnerId))
            yield return context.ActionOwner.Value;
        else if (rule.OwnerKind == EffectOwnerKind.Effect)
            foreach (var fighter in new[] { FighterId.FighterA, FighterId.FighterB })
                if (context.Subscribers[fighter].Contains(rule.OwnerId)) yield return fighter;
    }

    private IReadOnlyDictionary<FighterId, IReadOnlyList<StableId>> Subscribers() => stores.ToDictionary(x => x.Key,
        x => (IReadOnlyList<StableId>)x.Value.Active.Select(effect => effect.Profile.EffectId).ToArray());
    private void PruneApplications()
    {
        var active = new HashSet<EventId>(stores.Values.SelectMany(x => x.Active).Select(x => x.LatestApplicationEventId));
        foreach (var id in applications.Keys.Where(id => !active.Contains(id)).ToArray()) applications.Remove(id);
    }
    private static StableId FighterStableId(FighterId fighter) => new(fighter == FighterId.FighterA ? "fighter_a" : "fighter_b");
    private static EngineInvariantException Invalid(string message) => new(EngineFailureCodes.EffectInvalidMutation, "effects", message);
}
