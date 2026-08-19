using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Battle.Replay.Verification;

/// <summary>
/// Config-free decision/commit validation introduced by battle.core/0.3.x.
/// Older engine artifacts deliberately retain their historical v0.1 semantics.
/// </summary>
internal static class DecisionReplaySemanticValidator
{
    private const string CompatibleEnginePrefix = "battle.core/0.3.";
    private const int RngNormalizedScale = 1_000;

    private static readonly string[] StageCodes =
    {
        "Tactic",
        "Situation",
        "Synergy",
        "Counter",
        "Variety",
        "Opportunity",
    };

    public static void Validate(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var engineVersion = replay.GetProperty("engine").GetProperty("engine_version").GetString()!;
        if (!IsCompatibleEngineVersion(engineVersion))
        {
            return;
        }

        try
        {
            ValidateCompatibleReplay(replay, events, issues);
        }
        catch (Exception exception) when (exception is
                   ArithmeticException or FormatException or InvalidOperationException or
                   ArgumentException or KeyNotFoundException)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                "$/events",
                "WP-08 decision semantics contain an invalid numeric or multiplicity relation.");
        }
    }

    private static void ValidateCompatibleReplay(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {

        var decisions = new List<DecisionRecord>();
        var expectedDecisionIndex = BigInteger.Zero;
        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index];
            if (!HasStringValue(combatEvent, "event_type", "DecisionMade"))
            {
                continue;
            }

            var record = new DecisionRecord(combatEvent, index);
            decisions.Add(record);
            ValidateDecision(record, ref expectedDecisionIndex, issues);
        }

        ValidateDecisionBatches(decisions, issues);
        ValidateCommitsAndDerivedEvents(events, decisions, issues);
        ValidateDiagnostics(replay, decisions, issues);
    }

    private static void ValidateDecision(
        DecisionRecord decision,
        ref BigInteger expectedDecisionIndex,
        ICollection<ReplayVerificationIssue> issues)
    {
        var combatEvent = decision.Event;
        var payload = combatEvent.GetProperty("payload");
        var path = decision.Path;
        var legalActionIds = payload
            .GetProperty("legal_action_ids")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        var candidateCount = payload.GetProperty("candidate_count").GetInt32();
        var chosenActionId = payload.GetProperty("chosen_action_id").GetString()!;
        var chosenWeight = payload.GetProperty("chosen_weight").GetInt32();
        var weightSum = payload.GetProperty("weight_sum").GetInt32();
        var selectionMode = payload.GetProperty("selection_mode").GetString()!;

        if (!IsStrictOrdinal(legalActionIds))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionOrderingInvalid,
                path + "/payload/legal_action_ids",
                "Decision legal_action_ids must be unique and strictly ordinal.");
        }

        if (candidateCount != legalActionIds.Length ||
            !legalActionIds.Contains(chosenActionId, StringComparer.Ordinal) ||
            chosenWeight > weightSum)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCandidateInvalid,
                path + "/payload",
                "Decision candidate count, chosen membership and weights are inconsistent.");
        }

        if (!HasNullableStringValue(combatEvent, "action_id", chosenActionId) ||
            combatEvent.GetProperty("actor_id").ValueKind == JsonValueKind.Null ||
            combatEvent.GetProperty("decision_id").ValueKind == JsonValueKind.Null ||
            combatEvent.GetProperty("resolution_group_id").ValueKind != JsonValueKind.Null)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCandidateInvalid,
                path,
                "Decision envelope identity must match the chosen action and remain resolution-free.");
        }

        if (!JsonElement.DeepEquals(
                combatEvent.GetProperty("before"),
                combatEvent.GetProperty("after")))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                path + "/after",
                "DecisionMade is a marker and cannot mutate its frame pair.");
        }

        var sourceEventId = GetNullableString(combatEvent.GetProperty("source_event_id"));
        var relatedEventIds = payload.GetProperty("related_event_ids");
        var relatedIsValid = sourceEventId is null
            ? relatedEventIds.GetArrayLength() == 0
            : HasExactRelatedEvent(payload, sourceEventId);
        if (!relatedIsValid)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCausalityInvalid,
                path + "/payload/related_event_ids",
                "DecisionMade related_event_ids must exactly mirror its causal source.");
        }

        ValidateDominantModifiersAndReasons(combatEvent, payload, selectionMode, path, issues);

        var rng = combatEvent.GetProperty("rng");
        switch (selectionMode)
        {
            case "WeightedRng":
                if (candidateCount < 2 || weightSum <= 0 || chosenWeight <= 0 ||
                    rng.ValueKind == JsonValueKind.Null)
                {
                    AddModeError(path, issues, "WeightedRng requires at least two candidates, positive weights and one RNG draw.");
                }
                else
                {
                    ValidateWeightedRng(rng, weightSum, expectedDecisionIndex, path, issues);
                }

                expectedDecisionIndex += BigInteger.One;
                break;

            case "OnlyLegalAction":
                if (candidateCount != 1 || legalActionIds.Length != 1 ||
                    chosenWeight != weightSum || rng.ValueKind != JsonValueKind.Null)
                {
                    AddModeError(path, issues, "OnlyLegalAction requires exactly one candidate, equal chosen/sum weights and no RNG.");
                }

                break;

            case "ZeroWeightFallback":
                if (candidateCount < 2 || chosenWeight != 0 || weightSum != 0 ||
                    rng.ValueKind != JsonValueKind.Null ||
                    !StringComparer.Ordinal.Equals(
                        chosenActionId,
                        ChooseSystemFallback(legalActionIds)))
                {
                    AddModeError(path, issues, "ZeroWeightFallback requires a zero sum, no RNG and the fixed legal system priority.");
                }

                break;

            case "HardOpportunity":
                if (candidateCount < 2 || rng.ValueKind != JsonValueKind.Null)
                {
                    AddModeError(path, issues, "HardOpportunity requires multiple legal candidates and no RNG draw.");
                }

                break;

            default:
                AddModeError(path, issues, "Decision selection_mode is not a supported WP-08 mode.");
                break;
        }
    }

    private static void ValidateDominantModifiersAndReasons(
        JsonElement combatEvent,
        JsonElement payload,
        string selectionMode,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        var modifiers = payload.GetProperty("dominant_modifiers").EnumerateArray().ToArray();
        var reasons = combatEvent
            .GetProperty("reason_codes")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        var valid = reasons.Length == modifiers.Length + 1 &&
                    reasons.Length > 0 &&
                    StringComparer.Ordinal.Equals(reasons[0], selectionMode);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < modifiers.Length; index++)
        {
            var modifier = modifiers[index];
            var code = modifier.GetProperty("code").GetString()!;
            valid &= Array.IndexOf(StageCodes, code) >= 0 &&
                     seen.Add(code);
            valid &= index + 1 < reasons.Length &&
                     StringComparer.Ordinal.Equals(reasons[index + 1], code);
        }

        if (!valid || modifiers.Length > StageCodes.Length)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionOrderingInvalid,
                path + "/payload/dominant_modifiers",
                "Selection reason and dominant modifiers must use deterministic WP-08 order.");
        }
    }

    private static void ValidateWeightedRng(
        JsonElement rng,
        int weightSum,
        BigInteger expectedIndex,
        string eventPath,
        ICollection<ReplayVerificationIssue> issues)
    {
        var path = eventPath + "/rng";
        var indexText = rng.GetProperty("index").GetString()!;
        var rawText = rng.GetProperty("raw_u32").GetString()!;
        var minimum = rng.GetProperty("range_min_inclusive").GetInt32();
        var maximum = rng.GetProperty("range_max_exclusive").GetInt32();
        var result = rng.GetProperty("result").GetInt32();
        var normalized = rng.GetProperty("normalized_fp").GetInt32();
        var raw = BigInteger.Zero;
        var valid = HasStringValue(rng, "stream", "Decision") &&
                    HasStringValue(rng, "operation", "NextInt") &&
                    minimum == 0 && maximum == weightSum &&
                    BigInteger.TryParse(
                        indexText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var index) &&
                    index == expectedIndex &&
                    BigInteger.TryParse(
                        rawText,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out raw) &&
                    raw >= BigInteger.Zero && raw <= uint.MaxValue;

        if (valid)
        {
            var bound = new BigInteger(maximum - minimum);
            var threshold = (BigInteger.One << 32) % bound;
            var offset = raw % bound;
            valid = raw >= threshold &&
                    result == minimum + (int)offset &&
                    normalized == (int)(offset * RngNormalizedScale / bound);
        }

        if (!valid)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionRngInvalid,
                path,
                "Weighted decision RNG provenance is inconsistent with PCG32 bounded NextInt semantics.");
        }
    }

    private static void ValidateDecisionBatches(
        IReadOnlyList<DecisionRecord> decisions,
        ICollection<ReplayVerificationIssue> issues)
    {
        foreach (var tickGroup in decisions.GroupBy(item => item.Tick))
        {
            var sources = tickGroup
                .Select(item => GetNullableString(item.Event.GetProperty("source_event_id")))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var actors = tickGroup.Select(item => item.ActorId).ToArray();
            var valid = sources.Length == 1 && actors.Distinct(StringComparer.Ordinal).Count() == actors.Length;
            if (actors.Length == 2)
            {
                valid &= StringComparer.Ordinal.Equals(actors[0], "fighter_a") &&
                         StringComparer.Ordinal.Equals(actors[1], "fighter_b");
            }

            if (!valid)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionOrderingInvalid,
                    tickGroup.First().Path,
                    "One phase-5 decision batch must share its causal root and retain fighter A/B order.");
            }
        }
    }

    private static void ValidateCommitsAndDerivedEvents(
        IReadOnlyList<JsonElement> events,
        IReadOnlyList<DecisionRecord> decisions,
        ICollection<ReplayVerificationIssue> issues)
    {
        var decisionsByEventId = new Dictionary<string, DecisionRecord>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            if (!decisionsByEventId.TryAdd(decision.EventId, decision))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionCausalityInvalid,
                    decision.Path + "/event_id",
                    "DecisionMade event IDs must be unique before commit causality is resolved.");
            }
        }

        var commitsByEventId = new Dictionary<string, CommitRecord>(StringComparer.Ordinal);
        var commitByDecisionId = new Dictionary<string, CommitRecord>(StringComparer.Ordinal);

        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index];
            if (!HasStringValue(combatEvent, "event_type", "ActionCommitted"))
            {
                continue;
            }

            var sourceEventId = GetNullableString(combatEvent.GetProperty("source_event_id"));
            var path = EventPath(index);
            if (sourceEventId is null ||
                !decisionsByEventId.TryGetValue(sourceEventId, out var decision))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionCausalityInvalid,
                    path + "/source_event_id",
                    "ActionCommitted must be sourced by its DecisionMade event.");
                continue;
            }

            var commit = new CommitRecord(combatEvent, index, decision);
            ValidateCommit(commit, issues);
            commitsByEventId[commit.EventId] = commit;
            if (!commitByDecisionId.TryAdd(decision.DecisionId, commit))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionCausalityInvalid,
                    path,
                    "A DecisionMade event cannot have more than one ActionCommitted child.");
            }
        }

        foreach (var decision in decisions)
        {
            if (!commitByDecisionId.ContainsKey(decision.DecisionId))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionCausalityInvalid,
                    decision.Path,
                    "Every compatible battle.core/0.3.x DecisionMade event requires exactly one commit.");
            }
        }

        var resourceEvents = new Dictionary<string, List<ResourceRecord>>(StringComparer.Ordinal);
        var attackEvents = new List<DerivedRecord>();
        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index];
            var eventType = combatEvent.GetProperty("event_type").GetString()!;
            if (eventType is not "ResourceChanged" and not "AttackPrepared")
            {
                continue;
            }

            var sourceEventId = GetNullableString(combatEvent.GetProperty("source_event_id"));
            if (sourceEventId is null || !commitsByEventId.TryGetValue(sourceEventId, out var commit))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionCausalityInvalid,
                    EventPath(index) + "/source_event_id",
                    eventType + " must be sourced by its ActionCommitted event.");
                continue;
            }

            if (eventType == "ResourceChanged")
            {
                var resource = new ResourceRecord(combatEvent, index, commit);
                ValidateResource(resource, issues);
                if (!resourceEvents.TryGetValue(commit.EventId, out var items))
                {
                    items = new List<ResourceRecord>();
                    resourceEvents.Add(commit.EventId, items);
                }

                items.Add(resource);
            }
            else
            {
                var attack = new DerivedRecord(combatEvent, index, commit);
                attackEvents.Add(attack);
            }
        }

        foreach (var commit in commitsByEventId.Values)
        {
            resourceEvents.TryGetValue(commit.EventId, out var items);
            ValidateRequiredCosts(commit, items ?? new List<ResourceRecord>(), issues);
        }

        var commits = commitsByEventId.Values.ToArray();
        var duplicateAttackSources = attackEvents
            .GroupBy(item => item.Commit.EventId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);
        foreach (var duplicate in duplicateAttackSources)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCausalityInvalid,
                duplicate.Skip(1).First().Path,
                "An ActionCommitted event cannot have more than one AttackPrepared child.");
        }

        foreach (var attack in attackEvents)
        {
            ValidateAttackPrepared(attack, issues);
        }

        ValidatePhaseFiveFrameChains(decisions, commits, resourceEvents, attackEvents, issues);
        ValidateCombatLifecycles(events, commits, issues);
        ValidateBatchEventOrder(decisions, commitsByEventId.Values.ToArray(), resourceEvents, attackEvents, issues);
    }

    private static void ValidateCommit(
        CommitRecord commit,
        ICollection<ReplayVerificationIssue> issues)
    {
        var combatEvent = commit.Event;
        var decisionEvent = commit.Decision.Event;
        var payload = combatEvent.GetProperty("payload");
        var path = commit.Path;
        var targetId = GetNullableString(combatEvent.GetProperty("target_id"));
        var decisionTargetId = GetNullableString(decisionEvent.GetProperty("target_id"));
        var payloadTargetId = GetNullableString(payload.GetProperty("target_fighter_id"));
        var targetPosition = payload.GetProperty("target_position_at_commit");
        var actionId = GetNullableString(combatEvent.GetProperty("action_id"));
        var startupTicks = payload.GetProperty("startup_ticks").GetInt32();
        var activeTicks = payload.GetProperty("active_ticks").GetInt32();
        var recoveryTicks = payload.GetProperty("recovery_ticks").GetInt32();
        var cooldownTicks = payload.GetProperty("cooldown_ticks").GetInt32();
        var commitDirection = payload.GetProperty("commit_direction").GetString()!;
        var reasons = combatEvent.GetProperty("reason_codes").EnumerateArray().ToArray();
        var validIdentity = SameNullableString(combatEvent, decisionEvent, "actor_id") &&
                            SameNullableString(combatEvent, decisionEvent, "action_id") &&
                            SameNullableString(combatEvent, decisionEvent, "decision_id") &&
                            StringComparer.Ordinal.Equals(targetId, decisionTargetId) &&
                            StringComparer.Ordinal.Equals(targetId, payloadTargetId) &&
                            combatEvent.GetProperty("tick").GetInt32() == commit.Decision.Tick &&
                            combatEvent.GetProperty("rng").ValueKind == JsonValueKind.Null &&
                            combatEvent.GetProperty("resolution_group_id").ValueKind == JsonValueKind.Null &&
                            reasons.Length == 1 && reasons[0].GetString() == "ActionSelected";

        if (!HasExactRelatedEvent(payload, commit.Decision.EventId))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCausalityInvalid,
                path + "/payload/related_event_ids",
                "ActionCommitted related_event_ids must contain exactly its DecisionMade source event.");
        }

        if (targetId is null)
        {
            validIdentity &= targetPosition.ValueKind == JsonValueKind.Null &&
                             combatEvent.GetProperty("before").GetProperty("target").ValueKind == JsonValueKind.Null &&
                             combatEvent.GetProperty("after").GetProperty("target").ValueKind == JsonValueKind.Null;
        }
        else
        {
            var decisionTarget = decisionEvent.GetProperty("after").GetProperty("target");
            var commitBeforeTarget = combatEvent.GetProperty("before").GetProperty("target");
            var commitAfterTarget = combatEvent.GetProperty("after").GetProperty("target");
            validIdentity &= targetPosition.ValueKind == JsonValueKind.Number &&
                             decisionTarget.ValueKind == JsonValueKind.Object &&
                             commitBeforeTarget.ValueKind == JsonValueKind.Object &&
                             commitAfterTarget.ValueKind == JsonValueKind.Object &&
                             HasStringValue(decisionTarget, "fighter_id", targetId) &&
                             HasStringValue(commitBeforeTarget, "fighter_id", targetId) &&
                             HasStringValue(commitAfterTarget, "fighter_id", targetId) &&
                             targetPosition.GetInt32() == decisionTarget.GetProperty("position").GetInt32() &&
                             commitBeforeTarget.GetProperty("position").GetInt32() == targetPosition.GetInt32() &&
                             JsonElement.DeepEquals(commitBeforeTarget, commitAfterTarget);
        }

        validIdentity &= IsCommitDirectionConsistent(
            actionId,
            commitDirection,
            decisionEvent.GetProperty("after").GetProperty("actor"),
            decisionEvent.GetProperty("after").GetProperty("target"));

        if (IsSystemAction(actionId) &&
            (payload.GetProperty("energy_cost").GetInt32() != 0 ||
             payload.GetProperty("resource_cost").GetInt32() != 0 ||
             cooldownTicks != 0))
        {
            validIdentity = false;
        }

        if (!validIdentity)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                path,
                "ActionCommitted identity, target, timing root or payload is inconsistent with its frozen decision.");
        }

        var decisionActorFrame = decisionEvent.GetProperty("after").GetProperty("actor");
        var commitBeforeActor = combatEvent.GetProperty("before").GetProperty("actor");
        var commitAfterActor = combatEvent.GetProperty("after").GetProperty("actor");
        var expectedPhase = startupTicks > 0 ? "Startup" : "Active";
        var expectedTimer = startupTicks > 0 ? startupTicks : activeTicks;
        var expectedState = actionId switch
        {
            "sys_approach" => "Approach",
            "sys_retreat" => "Retreat",
            "sys_wait" => "Idle",
            _ => startupTicks > 0 ? "AttackPrepare" : "AttackActive",
        };
        if (!JsonElement.DeepEquals(decisionActorFrame, commitBeforeActor) ||
            commitAfterActor.ValueKind != JsonValueKind.Object ||
            activeTicks < 1 ||
            !HasNullableStringValue(commitAfterActor, "action_id", actionId) ||
            !HasStringValue(commitAfterActor, "action_phase", expectedPhase) ||
            !HasStringValue(commitAfterActor, "state", expectedState) ||
            commitAfterActor.GetProperty("state_ticks_remaining").ValueKind != JsonValueKind.Number ||
            commitAfterActor.GetProperty("state_ticks_remaining").GetInt32() != expectedTimer ||
            !SameFighterFrameExcept(
                commitBeforeActor,
                commitAfterActor,
                "state",
                "state_ticks_remaining",
                "action_id",
                "action_phase"))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                path + "/after",
                "ActionCommitted actor frames must start at the decision view and expose the committed action.");
        }

        _ = recoveryTicks;
    }

    private static void ValidateResource(
        ResourceRecord resource,
        ICollection<ReplayVerificationIssue> issues)
    {
        var combatEvent = resource.Event;
        var payload = combatEvent.GetProperty("payload");
        var commit = resource.Commit;
        var kind = payload.GetProperty("resource_kind").GetString()!;
        var expectedCost = kind switch
        {
            "Energy" => commit.EnergyCost,
            "UniqueResource" => commit.ResourceCost,
            _ => -1,
        };
        var before = payload.GetProperty("before").GetInt32();
        var delta = payload.GetProperty("delta").GetInt32();
        var after = payload.GetProperty("after").GetInt32();
        var minimum = payload.GetProperty("minimum").GetInt32();
        var maximum = payload.GetProperty("maximum").GetInt32();
        var reasons = combatEvent.GetProperty("reason_codes").EnumerateArray().ToArray();
        var valid = expectedCost > 0 && delta == -expectedCost &&
                     (long)before + delta == after &&
                     minimum == 0 && before >= minimum && before <= maximum &&
                     after >= minimum && after <= maximum &&
                     payload.GetProperty("clamp_reason").ValueKind == JsonValueKind.Null &&
                     SameNullableString(combatEvent, commit.Event, "actor_id") &&
                     SameNullableString(combatEvent, commit.Event, "action_id") &&
                     SameNullableString(combatEvent, commit.Event, "decision_id") &&
                     combatEvent.GetProperty("tick").GetInt32() == commit.Decision.Tick &&
                     combatEvent.GetProperty("target_id").ValueKind == JsonValueKind.Null &&
                     combatEvent.GetProperty("rng").ValueKind == JsonValueKind.Null &&
                     combatEvent.GetProperty("resolution_group_id").ValueKind == JsonValueKind.Null &&
                     reasons.Length == 1 && reasons[0].GetString() == "ActionCost" &&
                     combatEvent.GetProperty("before").GetProperty("target").ValueKind == JsonValueKind.Null &&
                     combatEvent.GetProperty("after").GetProperty("target").ValueKind == JsonValueKind.Null;

        if (!HasExactRelatedEvent(payload, commit.EventId))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCausalityInvalid,
                resource.Path + "/payload/related_event_ids",
                "ResourceChanged related_event_ids must contain exactly its ActionCommitted source event.");
        }

        var beforeFrame = combatEvent.GetProperty("before").GetProperty("actor");
        var afterFrame = combatEvent.GetProperty("after").GetProperty("actor");
        if (kind == "Energy")
        {
            valid &= payload.GetProperty("resource_id").ValueKind == JsonValueKind.Null &&
                      beforeFrame.GetProperty("energy").GetInt32() == before &&
                      afterFrame.GetProperty("energy").GetInt32() == after &&
                      maximum == beforeFrame.GetProperty("max_energy").GetInt32() &&
                      SameFighterFrameExcept(beforeFrame, afterFrame, "energy");
        }
        else if (kind == "UniqueResource")
        {
            var beforeResource = beforeFrame.GetProperty("unique_resource");
            var afterResource = afterFrame.GetProperty("unique_resource");
            var resourceId = GetNullableString(payload.GetProperty("resource_id"));
            valid &= resourceId is not null &&
                      HasStringValue(beforeResource, "resource_id", resourceId) &&
                      HasStringValue(afterResource, "resource_id", resourceId) &&
                      beforeResource.GetProperty("value").GetInt32() == before &&
                      afterResource.GetProperty("value").GetInt32() == after &&
                      beforeResource.GetProperty("max").GetInt32() == maximum &&
                      afterResource.GetProperty("max").GetInt32() == maximum &&
                      SameFighterFrameExcept(beforeFrame, afterFrame, "unique_resource");
        }

        if (!valid)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                resource.Path,
                "Commit cost ResourceChanged must be an exact, single sourced deduction.");
        }
    }

    private static void ValidateRequiredCosts(
        CommitRecord commit,
        IReadOnlyList<ResourceRecord> resources,
        ICollection<ReplayVerificationIssue> issues)
    {
        var energyCount = resources.Count(item =>
            HasStringValue(item.Event.GetProperty("payload"), "resource_kind", "Energy"));
        var uniqueCount = resources.Count(item =>
            HasStringValue(item.Event.GetProperty("payload"), "resource_kind", "UniqueResource"));
        if (energyCount != (commit.EnergyCost > 0 ? 1 : 0) ||
            uniqueCount != (commit.ResourceCost > 0 ? 1 : 0))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                commit.Path + "/payload",
                "Positive commit costs require one matching event; zero costs require none.");
        }
    }

    private static void ValidateAttackPrepared(
        DerivedRecord attack,
        ICollection<ReplayVerificationIssue> issues)
    {
        var combatEvent = attack.Event;
        var commit = attack.Commit;
        var payload = combatEvent.GetProperty("payload");
        var commitPayload = commit.Event.GetProperty("payload");
        var targetId = GetNullableString(combatEvent.GetProperty("target_id"));
        var payloadTargetId = GetNullableString(payload.GetProperty("target_fighter_id"));
        var commitTargetId = GetNullableString(commit.Event.GetProperty("target_id"));
        var commitTick = commit.Event.GetProperty("tick").GetInt32();
        var startupTicks = commitPayload.GetProperty("startup_ticks").GetInt32();
        var activeTicks = commitPayload.GetProperty("active_ticks").GetInt32();
        var activeStart = (long)commitTick + startupTicks;
        var activeEndExclusive = activeStart + activeTicks;
        var impacts = payload.GetProperty("impact_ticks").EnumerateArray()
            .Select(item => item.GetInt32())
            .ToArray();
        var reasons = combatEvent.GetProperty("reason_codes").EnumerateArray().ToArray();
        var valid = SameNullableString(combatEvent, commit.Event, "actor_id") &&
                     SameNullableString(combatEvent, commit.Event, "action_id") &&
                     SameNullableString(combatEvent, commit.Event, "decision_id") &&
                     StringComparer.Ordinal.Equals(targetId, commitTargetId) &&
                     StringComparer.Ordinal.Equals(targetId, payloadTargetId) &&
                     combatEvent.GetProperty("tick").GetInt32() == commitTick &&
                     payload.GetProperty("telegraph_tick").GetInt32() == commitTick &&
                      payload.GetProperty("direction_locked").GetBoolean() &&
                     !IsSystemAction(GetNullableString(combatEvent.GetProperty("action_id"))) &&
                     combatEvent.GetProperty("rng").ValueKind == JsonValueKind.Null &&
                     combatEvent.GetProperty("resolution_group_id").ValueKind == JsonValueKind.Null &&
                     reasons.Length == 1 && reasons[0].GetString() == "AttackPrepared" &&
                     activeTicks > 0 && activeStart <= int.MaxValue &&
                     IsStrictAscending(impacts) &&
                     impacts.All(tick => (long)tick >= activeStart && (long)tick < activeEndExclusive) &&
                     JsonElement.DeepEquals(
                         combatEvent.GetProperty("before"),
                         combatEvent.GetProperty("after"));

        var attackTargetFrame = combatEvent.GetProperty("before").GetProperty("target");
        var frozenTargetPosition = commitPayload.GetProperty("target_position_at_commit");
        if (targetId is null)
        {
            valid &= attackTargetFrame.ValueKind == JsonValueKind.Null &&
                     frozenTargetPosition.ValueKind == JsonValueKind.Null;
        }
        else
        {
            valid &= attackTargetFrame.ValueKind == JsonValueKind.Object &&
                     frozenTargetPosition.ValueKind == JsonValueKind.Number &&
                     HasStringValue(attackTargetFrame, "fighter_id", targetId) &&
                     attackTargetFrame.GetProperty("position").GetInt32() ==
                     frozenTargetPosition.GetInt32();
        }

        if (!HasExactRelatedEvent(payload, commit.EventId))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCausalityInvalid,
                attack.Path + "/payload/related_event_ids",
                "AttackPrepared related_event_ids must contain exactly its ActionCommitted source event.");
        }

        if (!valid)
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCommitInvalid,
                attack.Path,
                "AttackPrepared must preserve commit identity, frozen target, telegraph timing and marker frames.");
        }
    }

    private static void ValidatePhaseFiveFrameChains(
        IReadOnlyList<DecisionRecord> decisions,
        IReadOnlyList<CommitRecord> commits,
        IReadOnlyDictionary<string, List<ResourceRecord>> resourceEvents,
        IReadOnlyList<DerivedRecord> attackEvents,
        ICollection<ReplayVerificationIssue> issues)
    {
        foreach (var tick in decisions.Select(item => item.Tick).Distinct())
        {
            var frames = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var decision in decisions.Where(item => item.Tick == tick))
            {
                SeedFrame(
                    decision.ActorId,
                    decision.Event.GetProperty("after").GetProperty("actor"),
                    decision.Path + "/after/actor");

                var targetId = GetNullableString(decision.Event.GetProperty("target_id"));
                if (targetId is not null)
                {
                    SeedFrame(
                        targetId,
                        decision.Event.GetProperty("after").GetProperty("target"),
                        decision.Path + "/after/target");
                }
            }

            var chain = new List<(int Index, JsonElement Event, bool MutatesActor, string Path)>();
            chain.AddRange(commits.Where(item => item.Decision.Tick == tick).Select(item =>
                (item.Index, item.Event, true, item.Path)));
            chain.AddRange(resourceEvents.Values.SelectMany(items => items)
                .Where(item => item.Commit.Decision.Tick == tick)
                .Select(item => (item.Index, item.Event, true, item.Path)));
            chain.AddRange(attackEvents.Where(item => item.Commit.Decision.Tick == tick).Select(item =>
                (item.Index, item.Event, false, item.Path)));

            foreach (var item in chain.OrderBy(item => item.Index))
            {
                var actorId = GetNullableString(item.Event.GetProperty("actor_id"));
                var targetId = GetNullableString(item.Event.GetProperty("target_id"));
                var before = item.Event.GetProperty("before");
                var after = item.Event.GetProperty("after");
                var actorFrame = default(JsonElement);
                var valid = actorId is not null &&
                            frames.TryGetValue(actorId, out actorFrame) &&
                            JsonElement.DeepEquals(actorFrame, before.GetProperty("actor"));

                if (targetId is null)
                {
                    valid &= before.GetProperty("target").ValueKind == JsonValueKind.Null &&
                             after.GetProperty("target").ValueKind == JsonValueKind.Null;
                }
                else
                {
                    valid &= frames.TryGetValue(targetId, out var targetFrame) &&
                             JsonElement.DeepEquals(targetFrame, before.GetProperty("target")) &&
                             JsonElement.DeepEquals(targetFrame, after.GetProperty("target"));
                }

                if (!item.MutatesActor)
                {
                    valid &= actorId is not null &&
                             JsonElement.DeepEquals(actorFrame, after.GetProperty("actor"));
                }

                if (!valid)
                {
                    AddError(
                        issues,
                        ReplayVerificationCodes.DecisionCommitInvalid,
                        item.Path + "/before",
                        "Decision, commit, cost and telegraph frames must form one authoritative phase-5 chain.");
                }

                if (item.MutatesActor && actorId is not null &&
                    after.GetProperty("actor").ValueKind == JsonValueKind.Object)
                {
                    frames[actorId] = after.GetProperty("actor");
                }
            }

            void SeedFrame(string fighterId, JsonElement frame, string path)
            {
                if (frame.ValueKind != JsonValueKind.Object ||
                    !HasStringValue(frame, "fighter_id", fighterId) ||
                    (frames.TryGetValue(fighterId, out var existing) &&
                     !JsonElement.DeepEquals(existing, frame)))
                {
                    AddError(
                        issues,
                        ReplayVerificationCodes.DecisionCommitInvalid,
                        path,
                        "All decisions in one batch must project the same immutable fighter frames.");
                    return;
                }

                frames[fighterId] = frame;
            }
        }
    }

    private static void ValidateCombatLifecycles(
        IReadOnlyList<JsonElement> events,
        IReadOnlyList<CommitRecord> commits,
        ICollection<ReplayVerificationIssue> issues)
    {
        var lifecycleEvents = events
            .Select((combatEvent, index) => (Event: combatEvent, Index: index))
            .Where(item => HasStringValue(item.Event, "event_type", "ActionPhaseChanged"))
            .ToArray();
        var ownedLifecycleIndices = new HashSet<int>();
        var terminal = events
            .Select((combatEvent, index) => (Event: combatEvent, Index: index))
            .LastOrDefault(item => HasStringValue(item.Event, "event_type", "BattleEnded"));
        int? terminalTick = terminal.Event.ValueKind == JsonValueKind.Object
            ? terminal.Event.GetProperty("tick").GetInt32()
            : null;
        var terminalIsInvalid = terminal.Event.ValueKind == JsonValueKind.Object &&
                                HasStringValue(
                                    terminal.Event.GetProperty("payload"),
                                    "end_reason",
                                    "BattleInvalid");

        foreach (var commit in commits)
        {
            var actionId = GetNullableString(commit.Event.GetProperty("action_id"));
            var decisionId = GetNullableString(commit.Event.GetProperty("decision_id"));
            var actorId = GetNullableString(commit.Event.GetProperty("actor_id"));
            var lifecycle = lifecycleEvents
                .Where(item =>
                    HasNullableStringValue(item.Event, "actor_id", actorId) &&
                    HasNullableStringValue(item.Event, "action_id", actionId) &&
                    HasNullableStringValue(item.Event, "decision_id", decisionId))
                .ToArray();
            foreach (var item in lifecycle)
            {
                ownedLifecycleIndices.Add(item.Index);
            }

            if (IsSystemAction(actionId))
            {
                if (actionId == "sys_wait")
                {
                    foreach (var item in lifecycle)
                    {
                        AddError(
                            issues,
                            ReplayVerificationCodes.DecisionCommitInvalid,
                            EventPath(item.Index),
                            "sys_wait has no canonical ActionPhaseChanged lifecycle events.");
                    }
                }

                continue;
            }

            var payload = commit.Event.GetProperty("payload");
            var startupTicks = payload.GetProperty("startup_ticks").GetInt32();
            var activeTicks = payload.GetProperty("active_ticks").GetInt32();
            var recoveryTicks = payload.GetProperty("recovery_ticks").GetInt32();
            var expectedPhase = startupTicks > 0 ? "Startup" : "Active";
            var expectedTick = (long)commit.Decision.Tick +
                               (startupTicks > 0 ? startupTicks : activeTicks);
            var expectedSource = commit.EventId;

            foreach (var item in lifecycle)
            {
                var combatEvent = item.Event;
                var eventPayload = combatEvent.GetProperty("payload");
                var fromPhase = GetNullableString(eventPayload.GetProperty("from_phase"));
                var toPhase = GetNullableString(eventPayload.GetProperty("to_phase"));
                var phaseTicks = eventPayload.GetProperty("phase_ticks").GetInt32();
                var path = EventPath(item.Index);
                var reasons = combatEvent.GetProperty("reason_codes").EnumerateArray().ToArray();
                var expectedToPhase = expectedPhase switch
                {
                    "Startup" => "Active",
                    "Active" when recoveryTicks > 0 => "Recovery",
                    "Active" => null,
                    "Recovery" => null,
                    _ => "__invalid__",
                };
                var expectedPhaseTicks = expectedPhase switch
                {
                    "Startup" => activeTicks,
                    "Active" when recoveryTicks > 0 => recoveryTicks,
                    _ => 0,
                };
                var expectedReason = expectedPhase switch
                {
                    "Startup" => "StartupCompleted",
                    "Active" => "ActiveCompleted",
                    "Recovery" => "RecoveryCompleted",
                    _ => "__invalid__",
                };
                var beforeActor = combatEvent.GetProperty("before").GetProperty("actor");
                var afterActor = combatEvent.GetProperty("after").GetProperty("actor");
                var valid = expectedTick <= int.MaxValue &&
                            combatEvent.GetProperty("tick").GetInt32() == expectedTick &&
                            HasNullableStringValue(combatEvent, "source_event_id", expectedSource) &&
                            HasExactRelatedEvent(eventPayload, expectedSource) &&
                            StringComparer.Ordinal.Equals(fromPhase, expectedPhase) &&
                            StringComparer.Ordinal.Equals(toPhase, expectedToPhase) &&
                            phaseTicks == expectedPhaseTicks &&
                            reasons.Length == 1 && reasons[0].GetString() == expectedReason &&
                            combatEvent.GetProperty("target_id").ValueKind == JsonValueKind.Null &&
                            combatEvent.GetProperty("rng").ValueKind == JsonValueKind.Null &&
                            combatEvent.GetProperty("resolution_group_id").ValueKind == JsonValueKind.Null &&
                            combatEvent.GetProperty("before").GetProperty("target").ValueKind == JsonValueKind.Null &&
                            combatEvent.GetProperty("after").GetProperty("target").ValueKind == JsonValueKind.Null &&
                            beforeActor.ValueKind == JsonValueKind.Object &&
                            afterActor.ValueKind == JsonValueKind.Object &&
                            HasNullableStringValue(beforeActor, "action_id", actionId) &&
                            HasNullableStringValue(beforeActor, "action_phase", expectedPhase) &&
                            beforeActor.GetProperty("state_ticks_remaining").ValueKind == JsonValueKind.Number &&
                            beforeActor.GetProperty("state_ticks_remaining").GetInt32() == 1 &&
                            SameFighterFrameExcept(
                                beforeActor,
                                afterActor,
                                "state",
                                "state_ticks_remaining",
                                "action_id",
                                "action_phase");

                if (expectedToPhase is null)
                {
                    valid &= afterActor.GetProperty("action_id").ValueKind == JsonValueKind.Null &&
                             afterActor.GetProperty("action_phase").ValueKind == JsonValueKind.Null &&
                             afterActor.GetProperty("state_ticks_remaining").ValueKind == JsonValueKind.Null &&
                             HasStringValue(afterActor, "state", "DecisionReady");
                }
                else
                {
                    var expectedState = expectedToPhase == "Active" ? "AttackActive" : "Recovery";
                    valid &= HasNullableStringValue(afterActor, "action_id", actionId) &&
                             HasStringValue(afterActor, "action_phase", expectedToPhase) &&
                             HasStringValue(afterActor, "state", expectedState) &&
                             afterActor.GetProperty("state_ticks_remaining").ValueKind == JsonValueKind.Number &&
                             afterActor.GetProperty("state_ticks_remaining").GetInt32() == expectedPhaseTicks;
                }

                if (!valid)
                {
                    AddError(
                        issues,
                        ReplayVerificationCodes.DecisionCommitInvalid,
                        path,
                        "Generic combat ActionPhaseChanged must preserve its exact timing, identity, frames and source chain.");
                }

                expectedSource = combatEvent.GetProperty("event_id").GetString()!;
                expectedPhase = expectedToPhase ?? "__complete__";
                expectedTick = (long)combatEvent.GetProperty("tick").GetInt32() + expectedPhaseTicks;
            }

            var cutoffTick = terminalTick;
            var cutoffIsInclusive = !terminalIsInvalid;
            foreach (var combatEvent in events)
            {
                var isCancellation = HasStringValue(combatEvent, "event_type", "ActionCancelled") &&
                                     HasNullableStringValue(combatEvent, "actor_id", actorId) &&
                                     HasNullableStringValue(combatEvent, "action_id", actionId) &&
                                     HasNullableStringValue(combatEvent, "decision_id", decisionId);
                var isDefeat = HasStringValue(combatEvent, "event_type", "FighterDefeated") &&
                               HasNullableStringValue(combatEvent, "actor_id", actorId);
                if (!isCancellation && !isDefeat)
                {
                    continue;
                }

                var eventTick = combatEvent.GetProperty("tick").GetInt32();
                if (!cutoffTick.HasValue || eventTick < cutoffTick.Value ||
                    (eventTick == cutoffTick.Value && !cutoffIsInclusive))
                {
                    cutoffTick = eventTick;
                    cutoffIsInclusive = true;
                }
            }

            var transitionIsDue = cutoffTick.HasValue &&
                                  (expectedTick < cutoffTick.Value ||
                                   (cutoffIsInclusive && expectedTick == cutoffTick.Value));
            if (expectedPhase != "__complete__" && transitionIsDue)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.DecisionCommitInvalid,
                    commit.Path,
                    "A due generic combat lifecycle transition is missing before the terminal boundary.");
            }
        }

        foreach (var orphan in lifecycleEvents.Where(item =>
                     !ownedLifecycleIndices.Contains(item.Index)))
        {
            AddError(
                issues,
                ReplayVerificationCodes.DecisionCausalityInvalid,
                EventPath(orphan.Index),
                "ActionPhaseChanged must belong to one ActionCommitted actor/action/decision lifecycle.");
        }
    }

    private static void ValidateBatchEventOrder(
        IReadOnlyList<DecisionRecord> decisions,
        IReadOnlyList<CommitRecord> commits,
        IReadOnlyDictionary<string, List<ResourceRecord>> resourceEvents,
        IReadOnlyList<DerivedRecord> attackEvents,
        ICollection<ReplayVerificationIssue> issues)
    {
        foreach (var tick in decisions.Select(item => item.Tick).Distinct())
        {
            var ordered = new List<(int Index, int Stage, int Actor, int Detail, string Path)>();
            ordered.AddRange(decisions.Where(item => item.Tick == tick).Select(item =>
                (item.Index, 0, ActorOrder(item.ActorId), 0, item.Path)));
            ordered.AddRange(commits.Where(item => item.Decision.Tick == tick).Select(item =>
                (item.Index, 1, ActorOrder(item.ActorId), 0, item.Path)));
            foreach (var pair in resourceEvents)
            {
                ordered.AddRange(pair.Value.Where(item => item.Commit.Decision.Tick == tick).Select(item =>
                    (item.Index,
                     2,
                     ActorOrder(item.Commit.ActorId),
                     HasStringValue(item.Event.GetProperty("payload"), "resource_kind", "Energy") ? 0 : 1,
                     item.Path)));
            }

            ordered.AddRange(attackEvents.Where(item => item.Commit.Decision.Tick == tick).Select(item =>
                (item.Index, 3, ActorOrder(item.Commit.ActorId), 0, item.Path)));

            var bySequence = ordered.OrderBy(item => item.Index).ToArray();
            for (var index = 1; index < bySequence.Length; index++)
            {
                var previous = bySequence[index - 1];
                var current = bySequence[index];
                if (current.Stage < previous.Stage ||
                    (current.Stage == previous.Stage && current.Actor < previous.Actor) ||
                    (current.Stage == previous.Stage && current.Actor == previous.Actor &&
                     current.Detail < previous.Detail))
                {
                    AddError(
                        issues,
                        ReplayVerificationCodes.DecisionOrderingInvalid,
                        current.Path,
                        "Phase-5 events must be Decisions A/B, Commits A/B, costs A/B and telegraphs A/B.");
                    break;
                }
            }
        }
    }

    private static void ValidateDiagnostics(
        JsonElement replay,
        IReadOnlyList<DecisionRecord> decisions,
        ICollection<ReplayVerificationIssue> issues)
    {
        var profile = replay.GetProperty("profile").GetString()!;
        var diagnostics = replay.GetProperty("diagnostics");
        if (profile == "standard")
        {
            if (diagnostics.ValueKind != JsonValueKind.Null)
            {
                AddDiagnosticError("$/diagnostics", issues, "StandardReplay cannot publish diagnostic overlays.");
            }

            return;
        }

        if (profile != "diagnostic" || diagnostics.ValueKind != JsonValueKind.Object)
        {
            AddDiagnosticError("$/diagnostics", issues, "DiagnosticReplay requires a diagnostics object.");
            return;
        }

        var traces = diagnostics.GetProperty("decisions").EnumerateArray().ToArray();
        if (traces.Length != decisions.Count)
        {
            AddDiagnosticError(
                "$/diagnostics/decisions",
                issues,
                "DiagnosticReplay requires exactly one trace per canonical DecisionMade event.");
        }

        var alignedCount = Math.Min(traces.Length, decisions.Count);
        for (var index = 0; index < alignedCount; index++)
        {
            if (!HasStringValue(traces[index], "decision_id", decisions[index].DecisionId))
            {
                AddDiagnosticError(
                    $"$/diagnostics/decisions/{index}/decision_id",
                    issues,
                    "Decision diagnostics must retain canonical DecisionMade sequence order.");
            }
        }

        var traceByDecision = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var canonicalDecisionIds = decisions
            .Select(item => item.DecisionId)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < traces.Length; index++)
        {
            var trace = traces[index];
            var decisionId = trace.GetProperty("decision_id").GetString()!;
            if (!traceByDecision.TryAdd(decisionId, trace))
            {
                AddDiagnosticError(
                    $"$/diagnostics/decisions/{index}/decision_id",
                    issues,
                    "Decision diagnostic IDs must be unique.");
            }

            if (!canonicalDecisionIds.Contains(decisionId))
            {
                AddDiagnosticError(
                    $"$/diagnostics/decisions/{index}/decision_id",
                    issues,
                    "A decision diagnostic must reference one canonical DecisionMade event.");
            }
        }

        var digestByBatch = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < decisions.Count; index++)
        {
            var decision = decisions[index];
            if (!traceByDecision.TryGetValue(decision.DecisionId, out var trace))
            {
                AddDiagnosticError(
                    "$/diagnostics/decisions",
                    issues,
                    "DiagnosticReplay is missing a trace for a canonical DecisionMade event.");
                continue;
            }

            var tracePath = $"$/diagnostics/decisions/{Array.IndexOf(traces, trace)}";
            var digest = trace.GetProperty("snapshot_digest").GetString()!;
            var validIdentity = trace.GetProperty("tick").GetInt32() == decision.Tick &&
                                trace.GetProperty("sequence").GetInt64() == decision.Sequence &&
                                HasStringValue(trace, "actor_id", decision.ActorId) &&
                                IsSha256Digest(digest);
            if (!validIdentity)
            {
                AddDiagnosticError(tracePath, issues, "Decision trace identity or snapshot digest is invalid.");
            }

            var batchKey = decision.Tick.ToString(CultureInfo.InvariantCulture) + "\u001f" +
                           (GetNullableString(decision.Event.GetProperty("source_event_id")) ?? string.Empty);
            if (digestByBatch.TryGetValue(batchKey, out var expectedDigest) &&
                !StringComparer.Ordinal.Equals(expectedDigest, digest))
            {
                AddDiagnosticError(
                    tracePath + "/snapshot_digest",
                    issues,
                    "Both decisions in one immutable batch require the same snapshot digest.");
            }
            else
            {
                digestByBatch[batchKey] = digest;
            }

            ValidateDiagnosticCandidates(trace, tracePath, decision, issues);
        }
    }

    private static void ValidateDiagnosticCandidates(
        JsonElement trace,
        string tracePath,
        DecisionRecord decision,
        ICollection<ReplayVerificationIssue> issues)
    {
        var candidates = trace.GetProperty("candidates").EnumerateArray().ToArray();
        var actionIds = candidates.Select(item => item.GetProperty("action_id").GetString()!).ToArray();
        var publicLegal = decision.Event.GetProperty("payload").GetProperty("legal_action_ids")
            .EnumerateArray().Select(item => item.GetString()!).ToArray();
        var diagnosticLegal = new List<string>();
        var valid = IsStrictOrdinal(actionIds);
        long finalSum = 0;

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            var candidatePath = tracePath + "/candidates/" + index.ToString(CultureInfo.InvariantCulture);
            var legal = candidate.GetProperty("legal").GetBoolean();
            var rejection = candidate.GetProperty("first_rejection_code");
            var modifiers = candidate.GetProperty("modifiers").EnumerateArray().ToArray();
            var finalWeight = candidate.GetProperty("final_weight").GetInt32();
            if (legal)
            {
                diagnosticLegal.Add(actionIds[index]);
                finalSum += finalWeight;
                valid &= rejection.ValueKind == JsonValueKind.Null &&
                         modifiers.Length == StageCodes.Length &&
                         HasExactStageOrder(modifiers);
            }
            else
            {
                valid &= rejection.ValueKind == JsonValueKind.String &&
                         modifiers.Length == 0 && finalWeight == 0;
            }

            if (!legal && !valid)
            {
                _ = candidatePath;
            }
        }

        var payload = decision.Event.GetProperty("payload");
        valid &= diagnosticLegal.SequenceEqual(publicLegal, StringComparer.Ordinal) &&
                 finalSum == payload.GetProperty("weight_sum").GetInt32();
        var chosenActionId = payload.GetProperty("chosen_action_id").GetString()!;
        var chosen = candidates.Where(item =>
                HasStringValue(item, "action_id", chosenActionId))
            .Take(2)
            .ToArray();
        valid &= chosen.Length == 1 &&
                 chosen[0].GetProperty("legal").GetBoolean() &&
                 chosen[0].GetProperty("final_weight").GetInt32() ==
                 payload.GetProperty("chosen_weight").GetInt32();

        if (chosen.Length == 1)
        {
            var chosenModifiers = chosen[0].GetProperty("modifiers").EnumerateArray().ToArray();
            foreach (var publicModifier in payload.GetProperty("dominant_modifiers").EnumerateArray())
            {
                var code = publicModifier.GetProperty("code").GetString()!;
                var matchingStages = chosenModifiers.Where(item =>
                        HasStringValue(item, "code", code))
                    .Take(2)
                    .ToArray();
                valid &= matchingStages.Length == 1 &&
                         matchingStages[0].GetProperty("multiplier_fp").GetInt32() ==
                         publicModifier.GetProperty("multiplier_fp").GetInt32();
            }
        }

        if (valid && HasStringValue(payload, "selection_mode", "WeightedRng"))
        {
            var draw = decision.Event.GetProperty("rng").GetProperty("result").GetInt32();
            long cumulative = 0;
            string? intervalChoice = null;
            foreach (var candidate in candidates.Where(item => item.GetProperty("legal").GetBoolean()))
            {
                cumulative += candidate.GetProperty("final_weight").GetInt32();
                if (intervalChoice is null && draw < cumulative)
                {
                    intervalChoice = candidate.GetProperty("action_id").GetString();
                }
            }

            valid &= StringComparer.Ordinal.Equals(intervalChoice, chosenActionId);
        }

        if (!valid)
        {
            AddDiagnosticError(
                tracePath + "/candidates",
                issues,
                "Decision trace candidates must exactly explain public legal IDs, weights and selection.");
        }
    }

    private static string? ChooseSystemFallback(IEnumerable<string> legalActionIds)
    {
        var legal = new HashSet<string>(legalActionIds, StringComparer.Ordinal);
        foreach (var actionId in new[] { "sys_approach", "sys_retreat", "sys_wait" })
        {
            if (legal.Contains(actionId))
            {
                return actionId;
            }
        }

        return null;
    }

    private static bool HasExactStageOrder(IReadOnlyList<JsonElement> modifiers)
    {
        for (var index = 0; index < StageCodes.Length; index++)
        {
            if (!HasStringValue(modifiers[index], "code", StageCodes[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasExactRelatedEvent(JsonElement payload, string eventId)
    {
        var related = payload.GetProperty("related_event_ids");
        return related.GetArrayLength() == 1 &&
               StringComparer.Ordinal.Equals(related[0].GetString(), eventId);
    }

    private static bool IsStrictOrdinal(IReadOnlyList<string> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (StringComparer.Ordinal.Compare(values[index - 1], values[index]) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsStrictAscending(IReadOnlyList<int> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index - 1] >= values[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSha256Digest(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 7; index < value.Length; index++)
        {
            if (value[index] is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCompatibleEngineVersion(string value)
    {
        if (!value.StartsWith(CompatibleEnginePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var patch = value.AsSpan(CompatibleEnginePrefix.Length);
        if (patch.IsEmpty || (patch.Length > 1 && patch[0] == '0'))
        {
            return false;
        }

        foreach (var character in patch)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSystemAction(string? actionId) => actionId is
        "sys_approach" or "sys_retreat" or "sys_wait";

    private static bool IsCommitDirectionConsistent(
        string? actionId,
        string commitDirection,
        JsonElement actorFrame,
        JsonElement targetFrame)
    {
        if (actionId is null || actorFrame.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (targetFrame.ValueKind == JsonValueKind.Null)
        {
            return !IsSystemAction(actionId);
        }

        if (targetFrame.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var actorPosition = actorFrame.GetProperty("position").GetInt32();
        var targetPosition = targetFrame.GetProperty("position").GetInt32();
        if (actorPosition == targetPosition)
        {
            return false;
        }

        var toward = targetPosition > actorPosition ? "Right" : "Left";
        var away = toward == "Right" ? "Left" : "Right";
        return actionId switch
        {
            "sys_approach" => commitDirection == toward,
            "sys_retreat" => commitDirection == away,
            "sys_wait" => commitDirection == "None",
            _ => commitDirection is "Left" or "Right",
        };
    }

    private static bool SameFighterFrameExcept(
        JsonElement before,
        JsonElement after,
        params string[] excludedProperties)
    {
        if (before.ValueKind != JsonValueKind.Object || after.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var excluded = new HashSet<string>(excludedProperties, StringComparer.Ordinal);
        var beforeCount = 0;
        var afterCount = 0;
        foreach (var property in before.EnumerateObject())
        {
            beforeCount++;
            if (!excluded.Contains(property.Name) &&
                (!after.TryGetProperty(property.Name, out var afterValue) ||
                 !JsonElement.DeepEquals(property.Value, afterValue)))
            {
                return false;
            }
        }

        foreach (var _ in after.EnumerateObject())
        {
            afterCount++;
        }

        return beforeCount == afterCount;
    }

    private static int ActorOrder(string actorId) => actorId switch
    {
        "fighter_a" => 0,
        "fighter_b" => 1,
        _ => 2,
    };

    private static bool SameNullableString(JsonElement left, JsonElement right, string propertyName) =>
        StringComparer.Ordinal.Equals(
            GetNullableString(left.GetProperty(propertyName)),
            GetNullableString(right.GetProperty(propertyName)));

    private static bool HasNullableStringValue(
        JsonElement value,
        string propertyName,
        string? expected) =>
        StringComparer.Ordinal.Equals(
            GetNullableString(value.GetProperty(propertyName)),
            expected);

    private static string? GetNullableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static bool HasStringValue(JsonElement value, string propertyName, string expected) =>
        StringComparer.Ordinal.Equals(value.GetProperty(propertyName).GetString(), expected);

    private static string EventPath(int index) =>
        "$/events/" + index.ToString(CultureInfo.InvariantCulture);

    private static void AddModeError(
        string eventPath,
        ICollection<ReplayVerificationIssue> issues,
        string message) =>
        AddError(
            issues,
            ReplayVerificationCodes.DecisionModeInvalid,
            eventPath + "/payload/selection_mode",
            message);

    private static void AddDiagnosticError(
        string path,
        ICollection<ReplayVerificationIssue> issues,
        string message) =>
        AddError(
            issues,
            ReplayVerificationCodes.DecisionDiagnosticInvalid,
            path,
            message);

    private static void AddError(
        ICollection<ReplayVerificationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(
            new ReplayVerificationIssue(
                ReplayVerificationLayer.Semantic,
                ReplayVerificationSeverity.Error,
                code,
                path,
                message));

    private sealed class DecisionRecord
    {
        internal DecisionRecord(JsonElement combatEvent, int index)
        {
            Event = combatEvent;
            Index = index;
        }

        internal JsonElement Event { get; }

        internal int Index { get; }

        internal string Path => EventPath(Index);

        internal string EventId => Event.GetProperty("event_id").GetString()!;

        internal string DecisionId => Event.GetProperty("decision_id").GetString()!;

        internal string ActorId => Event.GetProperty("actor_id").GetString()!;

        internal int Tick => Event.GetProperty("tick").GetInt32();

        internal long Sequence => Event.GetProperty("sequence").GetInt64();
    }

    private sealed class CommitRecord
    {
        internal CommitRecord(JsonElement combatEvent, int index, DecisionRecord decision)
        {
            Event = combatEvent;
            Index = index;
            Decision = decision;
        }

        internal JsonElement Event { get; }

        internal int Index { get; }

        internal DecisionRecord Decision { get; }

        internal string Path => EventPath(Index);

        internal string EventId => Event.GetProperty("event_id").GetString()!;

        internal string ActorId => Event.GetProperty("actor_id").GetString()!;

        internal int EnergyCost => Event.GetProperty("payload").GetProperty("energy_cost").GetInt32();

        internal int ResourceCost => Event.GetProperty("payload").GetProperty("resource_cost").GetInt32();
    }

    private class DerivedRecord
    {
        internal DerivedRecord(JsonElement combatEvent, int index, CommitRecord commit)
        {
            Event = combatEvent;
            Index = index;
            Commit = commit;
        }

        internal JsonElement Event { get; }

        internal int Index { get; }

        internal CommitRecord Commit { get; }

        internal string Path => EventPath(Index);
    }

    private sealed class ResourceRecord : DerivedRecord
    {
        internal ResourceRecord(JsonElement combatEvent, int index, CommitRecord commit)
            : base(combatEvent, index, commit)
        {
        }
    }
}
