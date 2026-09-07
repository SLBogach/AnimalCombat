using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Core.Movement;
using Battle.Core.Outcome;
using Battle.Core.Random;

namespace Battle.Core.Resolution;

internal readonly record struct ResolutionTickResult(
    int GroupCount,
    int ImpactCount,
    bool AuthoritativeMutation);

internal static class ResolutionSystem
{
    private static readonly StableId StrikeTag = new("strike");
    private static readonly StableId GrabCategory = new("grab");

    internal static ResolutionTickResult ResolveTick(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter,
        IReadOnlyList<ResolutionGroup> groups)
    {
        if (state is null || settings is null || emitter is null || groups is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var mutations = false;
        var impacts = 0;
        foreach (var group in groups)
        {
            var result = ResolveGroup(state, settings, emitter, group);
            mutations |= result.AuthoritativeMutation;
            impacts = checked(impacts + result.ImpactCount);
            if (state.FighterA.Health == 0 || state.FighterB.Health == 0)
            {
                break;
            }
        }

        return new ResolutionTickResult(groups.Count, impacts, mutations);
    }

    internal static CombatEventIdentity? EndActiveGrab(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter,
        GrabEndReason reason,
        EventId? sourceEventId = null,
        ExternalId? resolutionGroupId = null,
        StableId? throwActionId = null,
        bool preflight = true)
    {
        if (!state.ActiveGrab.HasValue)
        {
            return null;
        }

        if (preflight)
        {
            emitter.PreflightNonterminalBatch(1, TickPhase.WallsAndGrabs);
        }
        var active = state.ActiveGrab.Value;
        var grabber = state.Get(active.GrabberId);
        var grabbed = state.Get(active.GrabbedId);
        var before = new FramePair(grabber.ToFrame(), grabbed.ToFrame());
        state.EndGrab(state.Tick, settings.Resolution.Global.GrabLockoutTicks);
        grabber.ClearGrabState();
        grabbed.ClearGrabState();
        var related = sourceEventId.HasValue ? new[] { sourceEventId.Value } : Array.Empty<EventId>();
        return emitter.Emit(
            state.Tick,
            new GrabEndedPayload(
                related,
                active.GrabId,
                reason,
                throwActionId,
                grabber.Position,
                grabbed.Position),
            active.GrabberId,
            active.GrabbedId,
            actionId: throwActionId,
            resolutionGroupId: resolutionGroupId,
            sourceEventId: sourceEventId,
            reasonCodes: new[] { new ReasonCode(reason.ToString()) },
            before: before,
            after: new FramePair(grabber.ToFrame(), grabbed.ToFrame()));
    }

    private static GroupCommitResult ResolveGroup(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter,
        ResolutionGroup group)
    {
        try
        {
            var preview = state.Rng.Resolution.CreatePreview();
            var plan = BuildPlan(state, settings, group, preview);
            plan.GrabEndReason = PredictGrabEnd(state, plan);
            var outcome = PredictOutcome(state, plan);
            var reservedOutcomeEvents = outcome?.Outcome == BattleOutcome.Draw ? 1 : 0;
            emitter.PreflightNonterminalBatch(
                checked(plan.EventCount + reservedOutcomeEvents),
                TickPhase.Resolve);
            state.Rng.Resolution.CommitPreview(preview);

            var result = CommitPlan(state, settings, emitter, plan);
            EmitDefeats(state, emitter, group.Id, result.LethalSources);
            return result;
        }
        catch (EngineInvariantException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArithmeticException or ArgumentException or InvalidOperationException)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.ResolutionArithmeticOverflow,
                TickPhase.Resolve.ToString(),
                "Resolution plan failed before atomic commit: " + exception.Message);
        }
    }

    private static ResolutionPlan BuildPlan(
        BattleState state,
        RuntimeBattleSettings settings,
        ResolutionGroup group,
        Pcg32Stream preview)
    {
        if (group.Intents.Count == 2 && group.IsTrade)
        {
            return new ResolutionPlan(
                group,
                group.Intents.Select(intent => BuildImpact(state, settings, intent, preview, skipDefense: true)),
                null,
                null,
                0);
        }

        var counter = group.Intents.FirstOrDefault(intent => intent.Entry.Kind == HitPrimitiveKind.Counter);
        var incoming = group.Intents.FirstOrDefault(intent => intent.Class == ResolutionClass.Strike);
        if (counter is not null && incoming is not null &&
            incoming.Action.HasTag("strike") &&
            counter.Action.ResolutionPriority >= incoming.Action.ResolutionPriority)
        {
            var counterPlan = BuildImpact(state, settings, counter, preview, skipDefense: true);
            return new ResolutionPlan(
                group,
                new[] { counterPlan },
                counter,
                incoming,
                1);
        }

        if (incoming is not null)
        {
            return new ResolutionPlan(
                group,
                new[] { BuildImpact(state, settings, incoming, preview, skipDefense: false) },
                null,
                null,
                0);
        }

        var grabs = group.Intents.Where(intent => intent.Entry.Kind == HitPrimitiveKind.Grab).ToArray();
        if (grabs.Length != 0)
        {
            return BuildGrabPlan(state, group, grabs, preview);
        }

        var terminal = group.Intents.Single();
        if (terminal.Entry.Kind is HitPrimitiveKind.Throw or HitPrimitiveKind.Wall)
        {
            if (!state.ActiveGrab.HasValue ||
                state.ActiveGrab.Value.GrabberId != terminal.ActorId ||
                state.ActiveGrab.Value.GrabbedId != terminal.TargetId)
            {
                return new ResolutionPlan(
                    group,
                    new[] { ImpactPlan.Miss(terminal, AttackMissReason.InvalidTarget, "OrphanGrabPrimitive") },
                    null,
                    null,
                    0);
            }

            var impact = BuildImpact(state, settings, terminal, preview, skipDefense: true);
            impact.EndsGrab = true;
            return new ResolutionPlan(group, new[] { impact }, null, null, 0);
        }

        var unmatched = group.Intents.Single();
        return new ResolutionPlan(
            group,
            new[] { ImpactPlan.Miss(unmatched, AttackMissReason.InvalidTarget, "NoMatchingIntent") },
            null,
            null,
            0);
    }

    private static ResolutionPlan BuildGrabPlan(
        BattleState state,
        ResolutionGroup group,
        IReadOnlyList<ImpactIntent> grabs,
        Pcg32Stream preview)
    {
        if (grabs.Count == 1)
        {
            var intent = grabs[0];
            var miss = ValidateImpactGeometry(state, intent);
            if (miss.HasValue || state.ActiveGrab.HasValue || !state.CanBeGrabbed(intent.TargetId, state.Tick))
            {
                return new ResolutionPlan(
                    group,
                    new[] { ImpactPlan.Miss(intent, miss ?? AttackMissReason.InvalidTarget, "GrabUnavailable") },
                    null,
                    null,
                    0);
            }

            return new ResolutionPlan(group, Array.Empty<ImpactPlan>(), null, null, 1)
            {
                GrabWinner = intent,
                GrabPriorityResult = GrabPriorityResult.Uncontested,
            };
        }

        var canonical = grabs.OrderBy(intent => intent.ActorId).ThenBy(intent => intent.Action.Id).ToArray();
        ImpactIntent winner;
        ConflictTieBreakMethod method;
        GrabPriorityResult priorityResult;
        RngProvenance? rng = null;
        if (canonical[0].Action.GrabPriority != canonical[1].Action.GrabPriority)
        {
            winner = canonical.OrderByDescending(intent => intent.Action.GrabPriority).First();
            method = ConflictTieBreakMethod.Priority;
            priorityResult = GrabPriorityResult.Priority;
        }
        else if (canonical[0].Initiative != canonical[1].Initiative)
        {
            winner = canonical.OrderByDescending(intent => intent.Initiative).First();
            method = ConflictTieBreakMethod.Initiative;
            priorityResult = GrabPriorityResult.Initiative;
        }
        else
        {
            rng = preview.NextInt(0, canonical.Length, RngOperation.TieBreak);
            winner = canonical[rng.Value.Result];
            method = ConflictTieBreakMethod.SeededHash;
            priorityResult = GrabPriorityResult.SeededTieBreak;
        }

        var winnerMiss = ValidateImpactGeometry(state, winner);
        var plan = winnerMiss.HasValue || state.ActiveGrab.HasValue || !state.CanBeGrabbed(winner.TargetId, state.Tick)
            ? new ResolutionPlan(
                group,
                new[] { ImpactPlan.Miss(winner, winnerMiss ?? AttackMissReason.InvalidTarget, "GrabUnavailable") },
                null,
                null,
                1)
            : new ResolutionPlan(group, Array.Empty<ImpactPlan>(), null, null, 2)
            {
                GrabWinner = winner,
                GrabPriorityResult = priorityResult,
            };
        plan.Conflict = new GrabConflict(canonical[0], canonical[1], winner, method, rng);
        return plan;
    }

    private static ImpactPlan BuildImpact(
        BattleState state,
        RuntimeBattleSettings settings,
        ImpactIntent intent,
        Pcg32Stream preview,
        bool skipDefense)
    {
        if (state.IsHitGroupConsumed(intent.HitGroupId, intent.TargetId))
        {
            return ImpactPlan.Miss(intent, AttackMissReason.HitGroupConsumed, "HitGroupConsumed", consume: false);
        }

        var miss = ValidateImpactGeometry(state, intent);
        if (miss.HasValue)
        {
            return ImpactPlan.Miss(intent, miss.Value, miss.Value.ToString());
        }

        var target = state.Get(intent.TargetId);
        if (!skipDefense && target.ActiveCombatAction is not null &&
            target.ActionPhase == ActionPhase.Active)
        {
            var defense = target.ActiveCombatAction.ResolutionProfile;
            if (defense.HasTag("dodge") && intent.Action.Dodgeable && !intent.Action.Undodgeable)
            {
                var chance = ResolutionMath.ComputeDodgeChance(
                    settings.Resolution.Global,
                    defense,
                    target.Evasion,
                    state.Get(intent.ActorId).Precision);
                var draw = preview.NextInt(0, settings.Resolution.Global.FixedPointScale, RngOperation.ChanceCheck);
                if (draw.Result < chance)
                {
                    return ImpactPlan.Dodged(intent, defense, chance, draw);
                }

                return BuildDamagePlan(state, settings, intent, draw, "DodgeFailed");
            }

            if (defense.HasTag("block") && intent.Action.Blockable)
            {
                var chance = ResolutionMath.ComputeBlockChance(
                    settings.Resolution.Global,
                    defense,
                    target.Guard,
                    state.Get(intent.ActorId).GuardBreak);
                var draw = preview.NextInt(0, settings.Resolution.Global.FixedPointScale, RngOperation.ChanceCheck);
                if (draw.Result < chance)
                {
                    return BuildBlockedPlan(state, settings, intent, defense, chance, draw);
                }

                return BuildDamagePlan(
                    state,
                    settings,
                    intent,
                    draw,
                    intent.Action.HasTag("guard_break") ? "GuardBreak" : "BlockFailed");
            }
        }

        return BuildDamagePlan(state, settings, intent, null, "Hit");
    }

    private static ImpactPlan BuildBlockedPlan(
        BattleState state,
        RuntimeBattleSettings settings,
        ImpactIntent intent,
        ResolutionActionProfile defense,
        int chance,
        RngProvenance rng)
    {
        var attacker = state.Get(intent.ActorId);
        var target = state.Get(intent.TargetId);
        var normal = ResolutionMath.ComputeDamage(
            settings.Resolution.Global,
            intent.Action,
            attacker.Power,
            target.Armor);
        var damage = ResolutionMath.ApplyBlock(
            settings.Resolution.Global,
            intent.Action,
            defense,
            normal);
        return ImpactPlan.Blocked(
            intent,
            defense,
            chance,
            rng,
            ResolutionMath.ApplyHealth(damage, target.Health));
    }

    private static ImpactPlan BuildDamagePlan(
        BattleState state,
        RuntimeBattleSettings settings,
        ImpactIntent intent,
        RngProvenance? rng,
        string reason)
    {
        var attacker = state.Get(intent.ActorId);
        var target = state.Get(intent.TargetId);
        var damage = ResolutionMath.ComputeDamage(
            settings.Resolution.Global,
            intent.Action,
            attacker.Power,
            target.Armor);
        var main = ResolutionMath.ApplyHealth(damage, target.Health);
        var control = intent.Action.BaseStagger == 0
            ? (ControlComputation?)null
            : ResolutionMath.ComputeControl(
                settings.Resolution.Global,
                intent.Action,
                attacker.ControlPower,
                target.ControlResistance,
                settings.Resolution.Global.FixedPointScale);
        var movementMode = intent.Action.MovementMode switch
        {
            ResolutionMovementMode.Pull => ResolutionMovementMode.Pull,
            ResolutionMovementMode.Swap => ResolutionMovementMode.Swap,
            _ when intent.Action.BaseKnockback > 0 => ResolutionMovementMode.Push,
            _ => ResolutionMovementMode.None,
        };
        var requestedMove = movementMode == ResolutionMovementMode.None
            ? 0
            : ResolutionMath.ComputeRequestedMove(
                settings.Resolution.Global,
                intent.Action,
                target.Mass);
        var forced = ForcedMovementResolver.Resolve(
            new ArenaInterval(settings.Arena.MinimumPosition, settings.Arena.MaximumPosition),
            movementMode,
            intent.ActorId,
            attacker.Position,
            attacker.CollisionRadius,
            intent.TargetId,
            target.Position,
            target.CollisionRadius,
            ToMovementDirection(intent.CommitDirection),
            requestedMove);
        var wallDamageValue = ResolutionMath.ComputeWallDamage(
            settings.Resolution.Global,
            intent.Action,
            forced.BlockedByWall);
        var wallDamage = wallDamageValue == 0
            ? (DamageMutation?)null
            : ResolutionMath.ApplyHealth(
                new DamageComputation(
                    wallDamageValue,
                    wallDamageValue,
                    wallDamageValue,
                    wallDamageValue,
                    wallDamageValue),
                main.HealthAfter);
        var mainControlTriggers = WouldReachStaggerThreshold(target.Stagger, target.StaggerThreshold, control);
        var staggerAfterMain = mainControlTriggers
            ? 0
            : PlannedStagger(target.Stagger, target.StaggerThreshold, control);
        var wallControlTriggers = intent.Action.WallImpact && forced.BlockedByWall > 0 &&
                                  WouldReachStaggerThreshold(staggerAfterMain, target.StaggerThreshold, control);
        return ImpactPlan.Hit(
            intent,
            rng,
            reason,
            main,
            control,
            mainControlTriggers,
            forced,
            wallDamage,
            wallControlTriggers);
    }

    private static int PlannedStagger(int current, int threshold, ControlComputation? control) =>
        !control.HasValue || control.Value.StaggerGain == 0
            ? current
            : checked((int)System.Math.Min((long)threshold, checked((long)current + control.Value.StaggerGain)));

    private static bool WouldReachStaggerThreshold(
        int current,
        int threshold,
        ControlComputation? control) =>
        control.HasValue && control.Value.StaggerGain > 0 &&
        checked((long)current + control.Value.StaggerGain) >= threshold;

    private static AttackMissReason? ValidateImpactGeometry(BattleState state, ImpactIntent intent)
    {
        var actor = state.Get(intent.ActorId);
        var target = state.Get(intent.TargetId);
        if (target.State == FighterState.Defeated || target.Health == 0)
        {
            return AttackMissReason.DefeatedTarget;
        }

        if (actor.ActiveCombatAction is null || actor.ActiveCombatAction.DecisionId != intent.DecisionId)
        {
            return AttackMissReason.InvalidTarget;
        }

        var directionValid = intent.CommitDirection switch
        {
            CommitDirection.Left => target.Position < actor.Position,
            CommitDirection.Right => target.Position > actor.Position,
            _ => false,
        };
        if (!directionValid)
        {
            return AttackMissReason.WrongDirection;
        }

        var gap = ArenaGeometry.SurfaceGap(
            actor.Position,
            actor.CollisionRadius,
            target.Position,
            target.CollisionRadius);
        return gap < intent.Action.HitRangeMinimum || gap > intent.Action.HitRangeMaximum
            ? AttackMissReason.OutOfRange
            : null;
    }

    private static ImmediateOutcome? PredictOutcome(BattleState state, ResolutionPlan plan)
    {
        var healthA = state.FighterA.Health;
        var healthB = state.FighterB.Health;
        foreach (var impact in plan.Impacts)
        {
            var finalHealth = impact.WallDamage?.HealthAfter ?? impact.MainDamage?.HealthAfter;
            if (!finalHealth.HasValue)
            {
                continue;
            }

            if (impact.Intent.TargetId == FighterId.FighterA)
            {
                healthA = finalHealth.Value;
            }
            else
            {
                healthB = finalHealth.Value;
            }
        }

        return ImmediateOutcomeResolver.Resolve(healthA, healthB);
    }

    private static GrabEndReason? PredictGrabEnd(BattleState state, ResolutionPlan plan)
    {
        if (!state.ActiveGrab.HasValue || plan.Impacts.Any(impact => impact.EndsGrab))
        {
            return null;
        }

        var active = state.ActiveGrab.Value;
        var lethalTargets = plan.Impacts
            .Where(impact => (impact.WallDamage?.Lethal ?? impact.MainDamage?.Lethal) == true)
            .Select(impact => impact.Intent.TargetId)
            .ToHashSet();
        if (lethalTargets.Contains(active.GrabberId))
        {
            return GrabEndReason.GrabberDefeated;
        }

        if (lethalTargets.Contains(active.GrabbedId))
        {
            return GrabEndReason.TargetDefeated;
        }

        var controlledTargets = plan.Impacts
            .Where(impact => impact.MainControlTriggers || impact.WallControlTriggers)
            .Select(impact => impact.Intent.TargetId);
        return controlledTargets.Any(id => id == active.GrabberId || id == active.GrabbedId)
            ? GrabEndReason.Interrupted
            : null;
    }

    private static GroupCommitResult CommitPlan(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter,
        ResolutionPlan plan)
    {
        var mutation = false;
        var lethalSources = new Dictionary<FighterId, EventId>();
        if (plan.Conflict is not null)
        {
            EmitConflict(state, emitter, plan.Group.Id, plan.Conflict);
        }

        if (plan.Counter is not null && plan.CancelledIncoming is not null)
        {
            EmitCountered(state, emitter, plan.Group.Id, plan.Counter, plan.CancelledIncoming);
            state.ConsumeHitGroup(plan.CancelledIncoming.HitGroupId, plan.CancelledIncoming.TargetId);
            _ = state.Get(plan.CancelledIncoming.ActorId).CancelCurrentAction();
        }

        if (plan.GrabWinner is not null)
        {
            foreach (var intent in plan.Group.Intents)
            {
                state.ConsumeHitGroup(intent.HitGroupId, intent.TargetId);
            }

            CommitGrabStart(state, settings, emitter, plan.Group.Id, plan.GrabWinner, plan.GrabPriorityResult);
            mutation = true;
        }

        var damageOrdinal = 0;
        foreach (var impact in plan.Impacts)
        {
            var impactResult = CommitImpact(
                state,
                settings,
                emitter,
                plan.Group.Id,
                impact,
                ref damageOrdinal);
            mutation |= impactResult.AuthoritativeMutation;
            if (impactResult.LethalSource.HasValue)
            {
                lethalSources[impact.Intent.TargetId] = impactResult.LethalSource.Value;
            }
        }

        if (plan.GrabEndReason.HasValue && state.ActiveGrab.HasValue)
        {
            var active = state.ActiveGrab.Value;
            var participant = plan.GrabEndReason == GrabEndReason.GrabberDefeated
                ? active.GrabberId
                : active.GrabbedId;
            var source = lethalSources.TryGetValue(participant, out var lethal)
                ? lethal
                : emitter.LastEventId;
            _ = EndActiveGrab(
                state,
                settings,
                emitter,
                plan.GrabEndReason.Value,
                source,
                plan.Group.Id,
                preflight: false);
            mutation = true;
        }

        return new GroupCommitResult(plan.Impacts.Count, mutation, lethalSources);
    }

    private static ImpactCommitResult CommitImpact(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter,
        ExternalId groupId,
        ImpactPlan plan,
        ref int damageOrdinal)
    {
        var intent = plan.Intent;
        var actor = state.Get(intent.ActorId);
        var target = state.Get(intent.TargetId);
        var source = intent.SourceEventId ?? emitter.LastEventId;
        var related = source.HasValue ? new[] { source.Value } : Array.Empty<EventId>();
        var gap = ArenaGeometry.SurfaceGap(actor.Position, actor.CollisionRadius, target.Position, target.CollisionRadius);

        if (plan.ConsumeHitGroup)
        {
            state.ConsumeHitGroup(intent.HitGroupId, intent.TargetId);
        }

        if (plan.Outcome == PlannedImpactOutcome.Miss)
        {
            _ = emitter.Emit(
                state.Tick,
                new AttackMissedPayload(
                    related,
                    intent.ImpactId,
                    intent.HitGroupId,
                    plan.MissReason!.Value,
                    gap,
                    intent.Action.HitRangeMinimum,
                    intent.Action.HitRangeMaximum),
                intent.ActorId,
                intent.TargetId,
                intent.Action.Id,
                decisionId: intent.DecisionId,
                resolutionGroupId: groupId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode(plan.Reason) },
                before: new FramePair(actor.ToFrame(), target.ToFrame()),
                after: new FramePair(actor.ToFrame(), target.ToFrame()));
            return default;
        }

        if (plan.Outcome == PlannedImpactOutcome.Dodged)
        {
            var frame = new FramePair(actor.ToFrame(), target.ToFrame());
            _ = emitter.Emit(
                state.Tick,
                new DodgedPayload(
                    related,
                    intent.ImpactId,
                    plan.DefenseAction!.Id,
                    plan.DefenseChance,
                    target.Position,
                    new[] { intent.IntentId }),
                intent.ActorId,
                intent.TargetId,
                intent.Action.Id,
                decisionId: intent.DecisionId,
                resolutionGroupId: groupId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode("Dodged") },
                rng: plan.Rng,
                before: frame,
                after: frame);
            return default;
        }

        CombatEventIdentity marker;
        var markerFrame = new FramePair(actor.ToFrame(), target.ToFrame());
        if (plan.Outcome == PlannedImpactOutcome.Blocked)
        {
            marker = emitter.Emit(
                state.Tick,
                new BlockedPayload(
                    related,
                    intent.ImpactId,
                    plan.DefenseAction!.Id,
                    plan.DefenseChance,
                    plan.DefenseAction.BlockReductionFixedPoint,
                    guardBreak: false,
                    new[] { intent.IntentId }),
                intent.ActorId,
                intent.TargetId,
                intent.Action.Id,
                decisionId: intent.DecisionId,
                resolutionGroupId: groupId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode("Blocked") },
                rng: plan.Rng,
                before: markerFrame,
                after: markerFrame);
        }
        else
        {
            marker = emitter.Emit(
                state.Tick,
                new AttackHitPayload(
                    related,
                    intent.ImpactId,
                    intent.HitGroupId,
                    gap,
                    intent.Action.HitRangeMinimum,
                    intent.Action.HitRangeMaximum,
                    ToMovementDirection(intent.CommitDirection),
                    intent.Action.Tags),
                intent.ActorId,
                intent.TargetId,
                intent.Action.Id,
                decisionId: intent.DecisionId,
                resolutionGroupId: groupId,
                sourceEventId: source,
                reasonCodes: new[] { new ReasonCode(plan.Reason) },
                rng: plan.Rng,
                before: markerFrame,
                after: markerFrame);
        }

        var mutation = false;
        EventId? lethalSource = null;
        EventId latestSource = marker.EventId;
        if (plan.MainDamage.HasValue)
        {
            var damageEvent = CommitDamage(
                state,
                emitter,
                groupId,
                intent,
                plan.MainDamage.Value,
                marker.EventId,
                wall: false,
                settings.Resolution.Global.DamageCap,
                damageOrdinal++);
            latestSource = damageEvent.EventId;
            mutation |= plan.MainDamage.Value.ActualHealthLoss != 0;
            if (plan.MainDamage.Value.Lethal)
            {
                lethalSource = damageEvent.EventId;
            }
        }

        if (plan.Outcome != PlannedImpactOutcome.Blocked && plan.Control.HasValue)
        {
            latestSource = CommitStagger(
                state,
                emitter,
                groupId,
                intent,
                plan.Control.Value,
                plan.MainControlTriggers,
                settings.Resolution.Global.FixedPointScale,
                latestSource,
                "StaggerGain",
                out var controlMutation);
            mutation |= controlMutation;
        }

        if (plan.Outcome != PlannedImpactOutcome.Blocked && plan.Forced.HasValue &&
            plan.Forced.Value.RequestedMove > 0)
        {
            latestSource = CommitForcedMovement(
                state,
                emitter,
                groupId,
                intent,
                plan.Forced.Value,
                latestSource,
                out var positionMutation);
            mutation |= positionMutation;
        }

        if (plan.Outcome != PlannedImpactOutcome.Blocked && plan.Forced.HasValue &&
            intent.Action.WallImpact && plan.Forced.Value.BlockedByWall > 0)
        {
            var forced = plan.Forced.Value;
            var wallFrame = new FramePair(actor.ToFrame(), target.ToFrame());
            var wall = emitter.Emit(
                state.Tick,
                new WallImpactPayload(
                    new[] { latestSource },
                    forced.Direction,
                    forced.BlockedByWall,
                    thresholdMet: true,
                    plan.WallDamage?.Computation.Final ?? 0,
                    plan.Control?.StaggerGain ?? 0),
                intent.ActorId,
                intent.TargetId,
                intent.Action.Id,
                decisionId: intent.DecisionId,
                resolutionGroupId: groupId,
                sourceEventId: latestSource,
                reasonCodes: new[] { new ReasonCode("WallImpact") },
                before: wallFrame,
                after: wallFrame);
            latestSource = wall.EventId;
            if (plan.WallDamage.HasValue)
            {
                var wallDamage = CommitDamage(
                    state,
                    emitter,
                    groupId,
                    intent,
                    plan.WallDamage.Value,
                    latestSource,
                    wall: true,
                    settings.Resolution.Global.DamageCap,
                    damageOrdinal++);
                latestSource = wallDamage.EventId;
                mutation |= plan.WallDamage.Value.ActualHealthLoss != 0;
                if (plan.WallDamage.Value.Lethal)
                {
                    lethalSource = wallDamage.EventId;
                }
            }

            if (plan.Control.HasValue)
            {
                latestSource = CommitStagger(
                    state,
                    emitter,
                    groupId,
                    intent,
                    plan.Control.Value,
                    plan.WallControlTriggers,
                    settings.Resolution.Global.FixedPointScale,
                    latestSource,
                    "WallStagger",
                    out var wallControlMutation);
                mutation |= wallControlMutation;
            }
        }

        if (plan.EndsGrab && state.ActiveGrab.HasValue)
        {
            var ended = EndActiveGrab(
                state,
                settings,
                emitter,
                GrabEndReason.Throw,
                latestSource,
                groupId,
                intent.Action.Id,
                preflight: false);
            mutation |= ended.HasValue;
        }

        return new ImpactCommitResult(mutation, lethalSource);
    }

    private static CombatEventIdentity CommitDamage(
        BattleState state,
        CombatEventEmitter emitter,
        ExternalId groupId,
        ImpactIntent intent,
        DamageMutation plan,
        EventId source,
        bool wall,
        int damageCap,
        int damageOrdinal)
    {
        var actor = state.Get(intent.ActorId);
        var target = state.Get(intent.TargetId);
        if (target.Health != plan.HealthBefore)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Resolve.ToString(),
                "Damage precondition changed after plan construction.");
        }

        var before = new FramePair(actor.ToFrame(), target.ToFrame());
        var applied = target.ApplyDamage(plan.Computation.Final);
        if (applied.After != plan.HealthAfter)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Resolve.ToString(),
                "Damage commit differs from its immutable plan.");
        }

        var breakdown = new DamageBreakdown(
            plan.Computation.PowerTerm,
            plan.Computation.Raw,
            plan.Computation.AfterArmor,
            plan.Computation.AfterBlock,
            plan.Computation.Final,
            intent.Action.MinimumDamage,
            wall ? intent.Action.WallDamageMaximum : damageCap,
            plan.Overkill);
        return emitter.Emit(
            state.Tick,
            new DamageAppliedPayload(
                new[] { source },
                intent.ImpactId,
                ResolutionIdentifiers.Damage(groupId, damageOrdinal),
                breakdown,
                plan.HealthBefore,
                plan.HealthAfter,
                intent.Action.Tags,
                plan.Lethal),
            intent.ActorId,
            intent.TargetId,
            intent.Action.Id,
            decisionId: intent.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: source,
            reasonCodes: new[] { new ReasonCode(wall ? "WallDamage" : "Damage") },
            before: before,
            after: new FramePair(actor.ToFrame(), target.ToFrame()));
    }

    private static EventId CommitStagger(
        BattleState state,
        CombatEventEmitter emitter,
        ExternalId groupId,
        ImpactIntent intent,
        ControlComputation control,
        bool expectedThreshold,
        int fixedPointScale,
        EventId source,
        string reason,
        out bool mutation)
    {
        var target = state.Get(intent.TargetId);
        var beforeFrame = target.ToFrame();
        var stagger = target.ApplyStagger(control.StaggerGain);
        if (!stagger.HasValue)
        {
            mutation = false;
            return source;
        }

        mutation = stagger.Value.Delta != 0;
        var resource = emitter.Emit(
            state.Tick,
            new ResourceChangedPayload(
                new[] { source },
                ResourceKind.Stagger,
                null,
                stagger.Value.Before,
                stagger.Value.Delta,
                stagger.Value.After,
                0,
                target.StaggerThreshold,
                stagger.Value.After == target.StaggerThreshold ? ResourceClampReason.Maximum : null),
            intent.TargetId,
            actionId: intent.Action.Id,
            decisionId: intent.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: source,
            reasonCodes: new[] { new ReasonCode(reason) },
            before: new FramePair(beforeFrame, null),
            after: new FramePair(target.ToFrame(), null));
        var latest = resource.EventId;
        var thresholdReached = target.Stagger >= target.StaggerThreshold;
        if (thresholdReached != expectedThreshold)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                TickPhase.Resolve.ToString(),
                "Stagger threshold result differs from its immutable resolution plan.");
        }

        if (!thresholdReached)
        {
            return latest;
        }

        var oldState = target.State;
        var beforeState = target.ToFrame();
        _ = target.ApplyHardControl(control.StunTicks);
        var changed = emitter.Emit(
            state.Tick,
            new StateChangedPayload(
                new[] { latest },
                oldState,
                FighterState.Stunned,
                control.StunTicks,
                control.ControlRatioFixedPoint,
                fixedPointScale,
                ImmunityResult.NotChecked),
            intent.TargetId,
            actionId: intent.Action.Id,
            decisionId: intent.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: latest,
            reasonCodes: new[] { new ReasonCode("StaggerThreshold") },
            before: new FramePair(beforeState, null),
            after: new FramePair(target.ToFrame(), null));
        latest = changed.EventId;
        var beforeReset = target.ToFrame();
        var reset = target.ResetStagger()!.Value;
        var resetEvent = emitter.Emit(
            state.Tick,
            new ResourceChangedPayload(
                new[] { latest },
                ResourceKind.Stagger,
                null,
                reset.Before,
                reset.Delta,
                reset.After,
                0,
                target.StaggerThreshold,
                ResourceClampReason.Minimum),
            intent.TargetId,
            actionId: intent.Action.Id,
            decisionId: intent.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: latest,
            reasonCodes: new[] { new ReasonCode("StaggerReset") },
            before: new FramePair(beforeReset, null),
            after: new FramePair(target.ToFrame(), null));
        mutation = true;
        return resetEvent.EventId;
    }

    private static EventId CommitForcedMovement(
        BattleState state,
        CombatEventEmitter emitter,
        ExternalId groupId,
        ImpactIntent intent,
        ForcedMovementResult forced,
        EventId source,
        out bool mutation)
    {
        var actor = state.Get(intent.ActorId);
        var target = state.Get(intent.TargetId);
        var before = new FramePair(actor.ToFrame(), target.ToFrame());
        actor.ApplyPosition(forced.ActorTo);
        target.ApplyPosition(forced.TargetTo);
        if (actor.Position != target.Position)
        {
            actor.SetFacing(ArenaGeometry.GetFacing(actor.Position, target.Position));
            target.SetFacing(ArenaGeometry.GetFacing(target.Position, actor.Position));
        }

        mutation = forced.ActorFrom != forced.ActorTo || forced.TargetFrom != forced.TargetTo;
        var emitted = emitter.Emit(
            state.Tick,
            new KnockbackAppliedPayload(
                new[] { source },
                forced.TargetFrom,
                forced.TargetTo,
                forced.RequestedMove,
                forced.ActualMove,
                forced.BlockedByWall),
            intent.ActorId,
            intent.TargetId,
            intent.Action.Id,
            decisionId: intent.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: source,
            reasonCodes: new[] { new ReasonCode(forced.Kind == PositionChangeKind.Swap ? "Swap" : "ForcedMovement") },
            before: before,
            after: new FramePair(actor.ToFrame(), target.ToFrame()));
        return emitted.EventId;
    }

    private static void EmitConflict(
        BattleState state,
        CombatEventEmitter emitter,
        ExternalId groupId,
        GrabConflict conflict)
    {
        var a = state.Get(conflict.A.ActorId);
        var b = state.Get(conflict.B.ActorId);
        var frame = new FramePair(a.ToFrame(), b.ToFrame());
        var related = Sources(a, b);
        _ = emitter.Emit(
            state.Tick,
            new ConflictResolvedPayload(
                related,
                ResolutionIdentifiers.Conflict(groupId, 0),
                conflict.A.IntentId,
                conflict.B.IntentId,
                GrabCategory,
                GrabCategory,
                conflict.A.Action.GrabPriority,
                conflict.B.Action.GrabPriority,
                conflict.Winner.IntentId,
                conflict.Winner == conflict.A ? ConflictResolutionResult.AWin : ConflictResolutionResult.BWin,
                conflict.Method),
            conflict.A.ActorId,
            conflict.B.ActorId,
            resolutionGroupId: groupId,
            sourceEventId: FirstOrNull(related),
            reasonCodes: new[] { new ReasonCode("GrabConflict") },
            rng: conflict.Rng,
            before: frame,
            after: frame);
    }

    private static void EmitCountered(
        BattleState state,
        CombatEventEmitter emitter,
        ExternalId groupId,
        ImpactIntent counter,
        ImpactIntent incoming)
    {
        var actor = state.Get(counter.ActorId);
        var target = state.Get(counter.TargetId);
        var related = Sources(actor, target);
        var frame = new FramePair(actor.ToFrame(), target.ToFrame());
        _ = emitter.Emit(
            state.Tick,
            new CounteredPayload(
                related,
                incoming.ImpactId,
                counter.Action.Id,
                StrikeTag,
                new[] { incoming.IntentId }),
            counter.ActorId,
            counter.TargetId,
            counter.Action.Id,
            decisionId: counter.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: FirstOrNull(related),
            reasonCodes: new[] { new ReasonCode("Countered") },
            before: frame,
            after: frame);
    }

    private static void CommitGrabStart(
        BattleState state,
        RuntimeBattleSettings settings,
        CombatEventEmitter emitter,
        ExternalId groupId,
        ImpactIntent winner,
        GrabPriorityResult priorityResult)
    {
        var actor = state.Get(winner.ActorId);
        var target = state.Get(winner.TargetId);
        var source = winner.SourceEventId ?? emitter.LastEventId;
        var related = source.HasValue ? new[] { source.Value } : Array.Empty<EventId>();
        var before = new FramePair(actor.ToFrame(), target.ToFrame());
        var grabId = ResolutionIdentifiers.Grab(winner.DecisionId, winner.Entry.Ordinal);
        state.StartGrab(
            grabId,
            winner.ActorId,
            winner.TargetId,
            state.Tick,
            settings.Resolution.Global.MaximumHoldTicks);
        actor.ApplyGrabbing();
        _ = target.ApplyGrabbed();
        _ = emitter.Emit(
            state.Tick,
            new GrabStartedPayload(
                related,
                grabId,
                winner.ActorId,
                winner.TargetId,
                settings.Resolution.Global.MaximumHoldTicks,
                priorityResult),
            winner.ActorId,
            winner.TargetId,
            winner.Action.Id,
            decisionId: winner.DecisionId,
            resolutionGroupId: groupId,
            sourceEventId: source,
            reasonCodes: new[] { new ReasonCode("GrabStarted") },
            before: before,
            after: new FramePair(actor.ToFrame(), target.ToFrame()));
    }

    private static void EmitDefeats(
        BattleState state,
        CombatEventEmitter emitter,
        ExternalId groupId,
        IReadOnlyDictionary<FighterId, EventId> lethalSources)
    {
        foreach (var fighterId in new[] { FighterId.FighterA, FighterId.FighterB })
        {
            var fighter = state.Get(fighterId);
            if (fighter.Health != 0 || fighter.State == FighterState.Defeated)
            {
                continue;
            }

            var lethalSource = lethalSources.TryGetValue(fighterId, out var source)
                ? source
                : emitter.LastEventId ?? throw new InvalidOperationException("A defeat requires a prior group event.");
            var before = fighter.ToFrame();
            var oldState = fighter.State;
            _ = fighter.ApplyDefeat();
            var changed = emitter.Emit(
                state.Tick,
                new StateChangedPayload(
                    new[] { lethalSource },
                    oldState,
                    FighterState.Defeated,
                    null,
                    null,
                    null,
                    ImmunityResult.NotChecked),
                fighterId,
                resolutionGroupId: groupId,
                sourceEventId: lethalSource,
                reasonCodes: new[] { new ReasonCode("HealthDepleted") },
                before: new FramePair(before, null),
                after: new FramePair(fighter.ToFrame(), null));
            var frame = fighter.ToFrame();
            var defeated = emitter.Emit(
                state.Tick,
                new FighterDefeatedPayload(
                    new[] { lethalSource, changed.EventId }.OrderBy(id => id),
                    fighterId,
                    lethalSource,
                    groupId,
                    fighter.Health),
                fighterId,
                resolutionGroupId: groupId,
                sourceEventId: changed.EventId,
                reasonCodes: new[] { new ReasonCode("Defeat") },
                before: new FramePair(frame, null),
                after: new FramePair(frame, null));
            state.RecordPivotalEvent(defeated.EventId, groupId);
        }
    }

    private static EventId[] Sources(params FighterRuntimeState[] fighters) => fighters
        .Select(fighter => fighter.CombatLifecycleEventId)
        .Where(id => id.HasValue)
        .Select(id => id!.Value)
        .Distinct()
        .OrderBy(id => id)
        .ToArray();

    private static EventId? FirstOrNull(IReadOnlyList<EventId> values) =>
        values.Count == 0 ? null : values[0];

    private static MovementDirection ToMovementDirection(CommitDirection direction) => direction switch
    {
        CommitDirection.Left => MovementDirection.Left,
        CommitDirection.Right => MovementDirection.Right,
        _ => throw new InvalidOperationException("A combat impact requires a frozen direction."),
    };

    private sealed class ResolutionPlan
    {
        internal ResolutionPlan(
            ResolutionGroup group,
            IEnumerable<ImpactPlan> impacts,
            ImpactIntent? counter,
            ImpactIntent? cancelledIncoming,
            int additionalEventCount)
        {
            Group = group;
            Impacts = impacts.ToArray();
            Counter = counter;
            CancelledIncoming = cancelledIncoming;
            AdditionalEventCount = additionalEventCount;
        }

        internal ResolutionGroup Group { get; }
        internal IReadOnlyList<ImpactPlan> Impacts { get; }
        internal ImpactIntent? Counter { get; }
        internal ImpactIntent? CancelledIncoming { get; }
        internal int AdditionalEventCount { get; }
        internal ImpactIntent? GrabWinner { get; init; }
        internal GrabPriorityResult GrabPriorityResult { get; init; }
        internal GrabConflict? Conflict { get; set; }
        internal GrabEndReason? GrabEndReason { get; set; }

        internal int EventCount => checked(
            AdditionalEventCount +
            Impacts.Sum(impact => impact.EventCount) +
            (GrabEndReason.HasValue ? 1 : 0) +
            PredictDefeatEventCount());

        private int PredictDefeatEventCount() => Impacts.Count(impact =>
            (impact.WallDamage?.Lethal ?? impact.MainDamage?.Lethal) == true) * 2;
    }

    private enum PlannedImpactOutcome
    {
        Miss,
        Dodged,
        Blocked,
        Hit,
    }

    private sealed class ImpactPlan
    {
        private ImpactPlan(ImpactIntent intent, PlannedImpactOutcome outcome, string reason)
        {
            Intent = intent;
            Outcome = outcome;
            Reason = reason;
        }

        internal ImpactIntent Intent { get; }
        internal PlannedImpactOutcome Outcome { get; }
        internal string Reason { get; }
        internal AttackMissReason? MissReason { get; private init; }
        internal ResolutionActionProfile? DefenseAction { get; private init; }
        internal int DefenseChance { get; private init; }
        internal RngProvenance? Rng { get; private init; }
        internal DamageMutation? MainDamage { get; private init; }
        internal ControlComputation? Control { get; private init; }
        internal bool MainControlTriggers { get; private init; }
        internal ForcedMovementResult? Forced { get; private init; }
        internal DamageMutation? WallDamage { get; private init; }
        internal bool WallControlTriggers { get; private init; }
        internal bool ConsumeHitGroup { get; private init; } = true;
        internal bool EndsGrab { get; set; }

        internal int EventCount
        {
            get
            {
                if (Outcome is PlannedImpactOutcome.Miss or PlannedImpactOutcome.Dodged)
                {
                    return 1;
                }

                var count = 1 + (MainDamage.HasValue ? 1 : 0);
                if (Outcome != PlannedImpactOutcome.Blocked && Control.HasValue && Control.Value.StaggerGain > 0)
                {
                    count += 1;
                    if (MainControlTriggers)
                    {
                        // The exact threshold check is completed against the plan's target state at commit.
                        count += 2;
                    }
                }

                if (Outcome != PlannedImpactOutcome.Blocked && Forced.HasValue && Forced.Value.RequestedMove > 0)
                {
                    count += 1;
                }

                if (Outcome != PlannedImpactOutcome.Blocked && Forced.HasValue &&
                    Intent.Action.WallImpact && Forced.Value.BlockedByWall > 0)
                {
                    count += 1 + (WallDamage.HasValue ? 1 : 0);
                    if (Control.HasValue && Control.Value.StaggerGain > 0)
                    {
                        count += 1 + (WallControlTriggers ? 2 : 0);
                    }
                }

                if (EndsGrab)
                {
                    count += 1;
                }

                return count;
            }
        }

        internal static ImpactPlan Miss(
            ImpactIntent intent,
            AttackMissReason reason,
            string reasonCode,
            bool consume = true) => new(intent, PlannedImpactOutcome.Miss, reasonCode)
            {
                MissReason = reason,
                ConsumeHitGroup = consume,
            };

        internal static ImpactPlan Dodged(
            ImpactIntent intent,
            ResolutionActionProfile defense,
            int chance,
            RngProvenance rng) => new(intent, PlannedImpactOutcome.Dodged, "Dodged")
            {
                DefenseAction = defense,
                DefenseChance = chance,
                Rng = rng,
            };

        internal static ImpactPlan Blocked(
            ImpactIntent intent,
            ResolutionActionProfile defense,
            int chance,
            RngProvenance rng,
            DamageMutation damage) => new(intent, PlannedImpactOutcome.Blocked, "Blocked")
            {
                DefenseAction = defense,
                DefenseChance = chance,
                Rng = rng,
                MainDamage = damage,
            };

        internal static ImpactPlan Hit(
            ImpactIntent intent,
            RngProvenance? rng,
            string reason,
            DamageMutation damage,
            ControlComputation? control,
            bool mainControlTriggers,
            ForcedMovementResult forced,
            DamageMutation? wallDamage,
            bool wallControlTriggers) => new(intent, PlannedImpactOutcome.Hit, reason)
            {
                Rng = rng,
                MainDamage = damage,
                Control = control,
                MainControlTriggers = mainControlTriggers,
                Forced = forced,
                WallDamage = wallDamage,
                WallControlTriggers = wallControlTriggers,
            };
    }

    private sealed record GrabConflict(
        ImpactIntent A,
        ImpactIntent B,
        ImpactIntent Winner,
        ConflictTieBreakMethod Method,
        RngProvenance? Rng);

    private readonly record struct ImpactCommitResult(bool AuthoritativeMutation, EventId? LethalSource);

    private readonly record struct GroupCommitResult(
        int ImpactCount,
        bool AuthoritativeMutation,
        IReadOnlyDictionary<FighterId, EventId> LethalSources);
}
