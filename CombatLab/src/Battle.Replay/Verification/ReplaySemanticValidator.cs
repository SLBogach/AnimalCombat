using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Battle.Replay.Verification;

internal static class ReplaySemanticValidator
{
    public static void Validate(
        JsonElement replay,
        ICollection<ReplayVerificationIssue> issues)
    {
        var events = replay.GetProperty("events").EnumerateArray().ToArray();
        if (events.Length == 0)
        {
            return;
        }

        ValidateLifecycle(events, issues);
        ValidateInput(replay, events, issues);
        ValidateEvents(replay, events, issues);
        ValidateSummary(replay, events, issues);
        ValidateKeyframes(replay, events, issues);
    }

    private static void ValidateLifecycle(
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        if (!HasStringValue(events[0], "event_type", "BattleStarted") ||
            events[0].GetProperty("sequence").GetInt64() != 0)
        {
            AddError(
                issues,
                ReplayVerificationCodes.FirstEventInvalid,
                "$/events/0",
                "The first canonical event must be BattleStarted with sequence 0.");
        }

        var finalIndex = events.Count - 1;
        if (!HasStringValue(events[finalIndex], "event_type", "BattleEnded"))
        {
            AddError(
                issues,
                ReplayVerificationCodes.LastEventInvalid,
                $"$/events/{finalIndex}",
                "The final canonical event must be BattleEnded.");
        }
    }

    private static void ValidateInput(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var input = replay.GetProperty("input");
        var masterSeedText = input.GetProperty("master_seed").GetString()!;
        if (!BigInteger.TryParse(
                masterSeedText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var masterSeed) ||
            masterSeed > ulong.MaxValue)
        {
            AddError(
                issues,
                ReplayVerificationCodes.IdentityMismatch,
                "$/input/master_seed",
                "master_seed is outside the UInt64 range.");
        }

        var fighters = input.GetProperty("fighters").EnumerateArray().ToArray();
        if (fighters.Length != 2 ||
            !HasStringValue(fighters[0], "fighter_id", "fighter_a") ||
            !HasStringValue(fighters[0], "side", "A") ||
            !HasStringValue(fighters[1], "fighter_id", "fighter_b") ||
            !HasStringValue(fighters[1], "side", "B"))
        {
            AddError(
                issues,
                ReplayVerificationCodes.IdentityMismatch,
                "$/input/fighters",
                "BattleInput must contain fighter_a/A followed by fighter_b/B.");
        }

        if (fighters.Length == 2 && HasStringValue(events[0], "event_type", "BattleStarted"))
        {
            var initialFrames = events[0].GetProperty("payload").GetProperty("initial_frames");
            if (!JsonElement.DeepEquals(fighters[0].GetProperty("initial_frame"), initialFrames[0]) ||
                !JsonElement.DeepEquals(fighters[1].GetProperty("initial_frame"), initialFrames[1]))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.FrameMismatch,
                    "$/events/0/payload/initial_frames",
                    "BattleStarted initial frames must equal the immutable BattleInput frames.");
            }
        }
    }

    private static void ValidateEvents(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var expectedBattleId = replay.GetProperty("battle_id").GetString()!;
        var expectedEngineVersion = replay.GetProperty("engine").GetProperty("engine_version").GetString()!;
        var expectedConfigHash = replay.GetProperty("config").GetProperty("config_hash").GetString()!;
        var priorEventIds = new HashSet<string>(StringComparer.Ordinal);
        var eventIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextRngIndex = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        BigInteger? previousTick = null;

        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index];
            var path = $"$/events/{index}";
            var eventId = combatEvent.GetProperty("event_id").GetString()!;
            eventIndices[eventId] = index;

            var sequence = ReadInteger(combatEvent.GetProperty("sequence"));
            if (sequence != index)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.SequenceInvalid,
                    path + "/sequence",
                    $"Expected contiguous sequence {index.ToString(CultureInfo.InvariantCulture)}.");
            }

            var expectedEventId = "evt-" + index.ToString("D10", CultureInfo.InvariantCulture);
            if (!StringComparer.Ordinal.Equals(eventId, expectedEventId))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.EventIdInvalid,
                    path + "/event_id",
                    $"Expected event_id '{expectedEventId}'.");
            }

            var tick = ReadInteger(combatEvent.GetProperty("tick"));
            if (previousTick.HasValue && tick < previousTick.Value)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.TickOrderInvalid,
                    path + "/tick",
                    "Canonical event ticks must be nondecreasing.");
            }

            previousTick = tick;

            ValidateHeaderIdentity(
                combatEvent,
                path,
                expectedBattleId,
                expectedEngineVersion,
                expectedConfigHash,
                issues);
            ValidateRolesAndFrames(combatEvent, path, issues);
            ValidateBackwardReferences(combatEvent, path, priorEventIds, issues);
            ValidateRng(combatEvent, path, nextRngIndex, issues);

            priorEventIds.Add(eventId);
        }

        ValidateFinisherReferences(events, eventIndices, issues);
    }

    private static void ValidateHeaderIdentity(
        JsonElement combatEvent,
        string path,
        string expectedBattleId,
        string expectedEngineVersion,
        string expectedConfigHash,
        ICollection<ReplayVerificationIssue> issues)
    {
        if (!HasStringValue(combatEvent, "battle_id", expectedBattleId))
        {
            AddError(
                issues,
                ReplayVerificationCodes.IdentityMismatch,
                path + "/battle_id",
                "Event battle_id must equal replay.battle_id.");
        }

        if (!HasStringValue(combatEvent, "engine_version", expectedEngineVersion))
        {
            AddError(
                issues,
                ReplayVerificationCodes.IdentityMismatch,
                path + "/engine_version",
                "Event engine_version must equal replay.engine.engine_version.");
        }

        if (!HasStringValue(combatEvent, "config_hash", expectedConfigHash))
        {
            AddError(
                issues,
                ReplayVerificationCodes.IdentityMismatch,
                path + "/config_hash",
                "Event config_hash must equal replay.config.config_hash.");
        }
    }

    private static void ValidateRolesAndFrames(
        JsonElement combatEvent,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        var eventType = combatEvent.GetProperty("event_type").GetString()!;
        var actor = GetNullableString(combatEvent.GetProperty("actor_id"));
        var target = GetNullableString(combatEvent.GetProperty("target_id"));
        var role = GetRoleRule(eventType);

        var roleIsValid = role switch
        {
            EventRoleRule.None => actor is null && target is null,
            EventRoleRule.ActorOnly => actor is not null && target is null,
            EventRoleRule.ActorAndTarget => actor is not null && target is not null && actor != target,
            EventRoleRule.ActorWithOptionalTarget => actor is not null && (target is null || actor != target),
            _ => false,
        };

        if (!roleIsValid)
        {
            AddError(
                issues,
                ReplayVerificationCodes.RoleMismatch,
                path,
                $"Actor/target roles are invalid for event_type '{eventType}'.");
        }

        ValidateFramePair(combatEvent.GetProperty("before"), actor, target, path + "/before", issues);
        ValidateFramePair(combatEvent.GetProperty("after"), actor, target, path + "/after", issues);

        if (eventType == "FighterDefeated" &&
            actor is not null &&
            !HasStringValue(combatEvent.GetProperty("payload"), "defeated_fighter_id", actor))
        {
            AddError(
                issues,
                ReplayVerificationCodes.RoleMismatch,
                path + "/payload/defeated_fighter_id",
                "FighterDefeated payload must identify the actor as the defeated fighter.");
        }
    }

    private static void ValidateFramePair(
        JsonElement framePair,
        string? actor,
        string? target,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        ValidateFrameRole(framePair.GetProperty("actor"), actor, path + "/actor", issues);
        ValidateFrameRole(framePair.GetProperty("target"), target, path + "/target", issues);
    }

    private static void ValidateFrameRole(
        JsonElement frame,
        string? fighterId,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        var valid = fighterId is null
            ? frame.ValueKind == JsonValueKind.Null
            : frame.ValueKind == JsonValueKind.Object && HasStringValue(frame, "fighter_id", fighterId);

        if (!valid)
        {
            AddError(
                issues,
                ReplayVerificationCodes.FrameMismatch,
                path,
                "Frame nullability and fighter_id must agree with the event role.");
        }
    }

    private static void ValidateBackwardReferences(
        JsonElement combatEvent,
        string path,
        ISet<string> priorEventIds,
        ICollection<ReplayVerificationIssue> issues)
    {
        var source = GetNullableString(combatEvent.GetProperty("source_event_id"));
        if (source is not null && !priorEventIds.Contains(source))
        {
            AddError(
                issues,
                ReplayVerificationCodes.CausalityInvalid,
                path + "/source_event_id",
                "source_event_id must reference an earlier canonical event.");
        }

        var related = combatEvent.GetProperty("payload").GetProperty("related_event_ids");
        string? previous = null;
        var relatedIndex = 0;
        foreach (var item in related.EnumerateArray())
        {
            var relatedId = item.GetString()!;
            if (!priorEventIds.Contains(relatedId))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.CausalityInvalid,
                    path + "/payload/related_event_ids/" + relatedIndex.ToString(CultureInfo.InvariantCulture),
                    "related_event_ids may contain only earlier canonical events.");
            }

            if (previous is not null && StringComparer.Ordinal.Compare(previous, relatedId) >= 0)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.CausalityInvalid,
                    path + "/payload/related_event_ids",
                    "related_event_ids must be strictly sorted by ordinal event_id.");
                break;
            }

            previous = relatedId;
            relatedIndex++;
        }

        if (HasStringValue(combatEvent, "event_type", "FighterDefeated"))
        {
            var lethalSource = GetNullableString(
                combatEvent.GetProperty("payload").GetProperty("lethal_source_event_id"));
            if (lethalSource is not null && !priorEventIds.Contains(lethalSource))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.CausalityInvalid,
                    path + "/payload/lethal_source_event_id",
                    "lethal_source_event_id must reference an earlier event.");
            }
        }
    }

    private static void ValidateRng(
        JsonElement combatEvent,
        string path,
        IDictionary<string, BigInteger> nextRngIndex,
        ICollection<ReplayVerificationIssue> issues)
    {
        var rng = combatEvent.GetProperty("rng");
        if (rng.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var stream = rng.GetProperty("stream").GetString()!;
        var indexText = rng.GetProperty("index").GetString()!;
        if (!BigInteger.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index > ulong.MaxValue)
        {
            AddError(
                issues,
                ReplayVerificationCodes.RngSequenceInvalid,
                path + "/rng/index",
                "RNG index is outside the supported unsigned integer range.");
            return;
        }

        var rawText = rng.GetProperty("raw_u32").GetString()!;
        if (!BigInteger.TryParse(rawText, NumberStyles.None, CultureInfo.InvariantCulture, out var raw) ||
            raw > uint.MaxValue)
        {
            AddError(
                issues,
                ReplayVerificationCodes.RngSequenceInvalid,
                path + "/rng/raw_u32",
                "raw_u32 is outside the UInt32 range.");
        }

        if (!nextRngIndex.TryGetValue(stream, out var expected))
        {
            expected = BigInteger.Zero;
        }

        if (index != expected)
        {
            AddError(
                issues,
                ReplayVerificationCodes.RngSequenceInvalid,
                path + "/rng/index",
                $"RNG stream '{stream}' expected index {expected.ToString(CultureInfo.InvariantCulture)}.");
        }

        nextRngIndex[stream] = expected + BigInteger.One;
    }

    private static void ValidateFinisherReferences(
        IReadOnlyList<JsonElement> events,
        IReadOnlyDictionary<string, int> eventIndices,
        ICollection<ReplayVerificationIssue> issues)
    {
        for (var index = 0; index < events.Count; index++)
        {
            var marker = events[index];
            if (!HasStringValue(marker, "event_type", "FinisherTriggered"))
            {
                continue;
            }

            var path = $"$/events/{index}/payload/predicted_lethal_event_id";
            var predictedId = marker.GetProperty("payload").GetProperty("predicted_lethal_event_id").GetString()!;
            if (!eventIndices.TryGetValue(predictedId, out var predictedIndex) || predictedIndex <= index)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.CausalityInvalid,
                    path,
                    "The finisher prediction must resolve to a later canonical event.");
                continue;
            }

            var predicted = events[predictedIndex];
            var markerGroup = GetNullableString(marker.GetProperty("resolution_group_id"));
            var predictedGroup = GetNullableString(predicted.GetProperty("resolution_group_id"));
            var predictedType = predicted.GetProperty("event_type").GetString();
            var isLethalEvent = predictedType == "FighterDefeated" ||
                                (predictedType == "DamageApplied" &&
                                 predicted.GetProperty("payload").GetProperty("lethal").GetBoolean());

            if (markerGroup is null ||
                !StringComparer.Ordinal.Equals(markerGroup, predictedGroup) ||
                !isLethalEvent)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.CausalityInvalid,
                    path,
                    "The finisher prediction must target a later lethal event in the same resolution group.");
            }
        }
    }

    private static void ValidateSummary(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var summary = replay.GetProperty("summary");
        var finalEvent = events[^1];
        var expectedEventCount = events.Count;

        if (ReadInteger(summary.GetProperty("event_count")) != expectedEventCount)
        {
            AddError(
                issues,
                ReplayVerificationCodes.SummaryMismatch,
                "$/summary/event_count",
                "summary.event_count must equal the canonical event count.");
        }

        if (HasStringValue(finalEvent, "event_type", "BattleEnded"))
        {
            var endedPayload = finalEvent.GetProperty("payload");
            foreach (var member in new[]
                     {
                         "outcome",
                         "winner_fighter_id",
                         "end_reason",
                         "end_tick",
                         "duration_ticks",
                         "pivotal_event_ids",
                         "final_frames",
                     })
            {
                if (!JsonElement.DeepEquals(summary.GetProperty(member), endedPayload.GetProperty(member)))
                {
                    AddError(
                        issues,
                        ReplayVerificationCodes.SummaryMismatch,
                        "$/summary/" + member,
                        $"Summary member '{member}' must equal BattleEnded.payload.{member}.");
                }
            }
        }

        var outcome = summary.GetProperty("outcome").GetString()!;
        var winner = GetNullableString(summary.GetProperty("winner_fighter_id"));
        var expectedWinner = outcome switch
        {
            "FighterAWin" => "fighter_a",
            "FighterBWin" => "fighter_b",
            _ => null,
        };
        if (!StringComparer.Ordinal.Equals(winner, expectedWinner))
        {
            AddError(
                issues,
                ReplayVerificationCodes.SummaryMismatch,
                "$/summary/winner_fighter_id",
                "winner_fighter_id must agree with outcome.");
        }

        if (!JsonElement.DeepEquals(summary.GetProperty("end_tick"), summary.GetProperty("duration_ticks")) ||
            (HasStringValue(finalEvent, "event_type", "BattleEnded") &&
             ReadInteger(summary.GetProperty("end_tick")) != ReadInteger(finalEvent.GetProperty("tick"))))
        {
            AddError(
                issues,
                ReplayVerificationCodes.SummaryMismatch,
                "$/summary/end_tick",
                "For v0.1 end_tick and duration_ticks must equal the BattleEnded tick.");
        }

        var eventIds = new HashSet<string>(
            events.Select(item => item.GetProperty("event_id").GetString()!),
            StringComparer.Ordinal);
        foreach (var pivotal in summary.GetProperty("pivotal_event_ids").EnumerateArray())
        {
            if (!eventIds.Contains(pivotal.GetString()!))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.SummaryMismatch,
                    "$/summary/pivotal_event_ids",
                    "Every pivotal event ID must exist in the canonical event log.");
            }
        }
    }

    private static void ValidateKeyframes(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var keyframes = replay.GetProperty("keyframes").EnumerateArray().ToArray();
        if (keyframes.Length < 2)
        {
            AddError(
                issues,
                ReplayVerificationCodes.KeyframeMismatch,
                "$/keyframes",
                "A complete replay requires distinct start and end keyframes.");
            return;
        }

        BigInteger? previousSequence = null;
        for (var index = 0; index < keyframes.Length; index++)
        {
            var keyframe = keyframes[index];
            var path = $"$/keyframes/{index}";
            var sequence = ReadInteger(keyframe.GetProperty("after_sequence"));
            if (sequence < 0 || sequence >= events.Count)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.KeyframeMismatch,
                    path + "/after_sequence",
                    "Keyframe after_sequence must identify an existing event.");
                continue;
            }

            if (previousSequence.HasValue && sequence <= previousSequence.Value)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.KeyframeMismatch,
                    path + "/after_sequence",
                    "Keyframes must be strictly ordered and unique by after_sequence.");
            }

            previousSequence = sequence;
            var eventIndex = (int)sequence;
            if (ReadInteger(keyframe.GetProperty("tick")) != ReadInteger(events[eventIndex].GetProperty("tick")))
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.KeyframeMismatch,
                    path + "/tick",
                    "Keyframe tick must equal the tick of after_sequence.");
            }
        }

        var start = keyframes[0];
        if (ReadInteger(start.GetProperty("tick")) != 0 ||
            ReadInteger(start.GetProperty("after_sequence")) != 0 ||
            (HasStringValue(events[0], "event_type", "BattleStarted") &&
             !JsonElement.DeepEquals(
                 start.GetProperty("fighters"),
                 events[0].GetProperty("payload").GetProperty("initial_frames"))))
        {
            AddError(
                issues,
                ReplayVerificationCodes.KeyframeMismatch,
                "$/keyframes/0",
                "Start keyframe must represent state immediately after BattleStarted.");
        }

        var end = keyframes[^1];
        if (ReadInteger(end.GetProperty("after_sequence")) != events.Count - 1 ||
            !JsonElement.DeepEquals(end.GetProperty("fighters"), replay.GetProperty("summary").GetProperty("final_frames")))
        {
            AddError(
                issues,
                ReplayVerificationCodes.KeyframeMismatch,
                $"$/keyframes/{keyframes.Length - 1}",
                "End keyframe must identify BattleEnded and contain summary.final_frames.");
        }
    }

    private static EventRoleRule GetRoleRule(string eventType) => eventType switch
    {
        "BattleStarted" or "TimeoutReached" or "DrawDeclared" or "BattleEnded" => EventRoleRule.None,
        "FighterDefeated" or "MoveStarted" or "PositionChanged" or "MoveEnded" or
            "ResourceChanged" or "StateChanged" => EventRoleRule.ActorOnly,
        "DecisionMade" or "KnockbackApplied" or "WallImpact" or "ConflictResolved" or
            "AttackHit" or "Blocked" or "Dodged" or "Countered" or "DamageApplied" or
            "GrabStarted" or "GrabEnded" or "FinisherTriggered" => EventRoleRule.ActorAndTarget,
        "ActionCommitted" or "AttackPrepared" or "ActionPhaseChanged" or "ActionCancelled" or
            "AttackMissed" or "EffectAdded" or "EffectRemoved" => EventRoleRule.ActorWithOptionalTarget,
        _ => throw new InvalidOperationException($"Unsupported canonical event type '{eventType}'."),
    };

    private static BigInteger ReadInteger(JsonElement value) =>
        BigInteger.Parse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static bool HasStringValue(JsonElement value, string propertyName, string expected) =>
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        StringComparer.Ordinal.Equals(property.GetString(), expected);

    private static string? GetNullableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetString();

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

    private enum EventRoleRule
    {
        None,
        ActorOnly,
        ActorAndTarget,
        ActorWithOptionalTarget,
    }
}
