using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Battle.Replay.Verification;

/// <summary>
/// Verifies movement facts that are represented by the replay itself. Arena/body
/// geometry is intentionally left to the engine because collision radii and
/// derived wall bounds are not part of the event wire contract.
/// </summary>
internal static class MovementReplaySemanticValidator
{
    private static readonly string[] Wp07StopConditions =
    {
        "WallReached",
        "PreferredRangeReached",
        "SegmentExpired",
    };

    public static void Validate(
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var priorEvents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var activeSegments = new Dictionary<string, MovementSegment>(StringComparer.Ordinal);
        var lastVoluntaryByActor = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < events.Count; index++)
        {
            var combatEvent = events[index];
            var eventType = combatEvent.GetProperty("event_type").GetString()!;
            var path = $"$/events/{index}";

            switch (eventType)
            {
                case "MoveStarted":
                    ValidateMoveStarted(
                        combatEvent,
                        path,
                        priorEvents,
                        activeSegments,
                        issues);
                    break;

                case "PositionChanged":
                    ValidatePositionChanged(
                        combatEvent,
                        path,
                        priorEvents,
                        activeSegments,
                        lastVoluntaryByActor,
                        issues);
                    break;

                case "MoveEnded":
                    ValidateMoveEnded(
                        combatEvent,
                        path,
                        activeSegments,
                        issues);
                    break;
            }

            priorEvents[combatEvent.GetProperty("event_id").GetString()!] = combatEvent;
        }
    }

    private static void ValidateMoveStarted(
        JsonElement combatEvent,
        string path,
        IReadOnlyDictionary<string, JsonElement> priorEvents,
        IDictionary<string, MovementSegment> activeSegments,
        ICollection<ReplayVerificationIssue> issues)
    {
        ValidateNullMovementEnvelope(combatEvent, path, issues);
        ValidateExactReasonCodes(combatEvent, path, issues, "MovementStarted");

        var actor = GetNullableString(combatEvent.GetProperty("actor_id"));
        if (actor is null)
        {
            return;
        }

        var payload = combatEvent.GetProperty("payload");
        var movementKind = payload.GetProperty("movement_kind").GetString()!;
        var source = GetNullableString(combatEvent.GetProperty("source_event_id"));
        var action = GetNullableString(combatEvent.GetProperty("action_id"));
        var decision = GetNullableString(combatEvent.GetProperty("decision_id"));
        var fromPosition = ReadInteger(payload.GetProperty("from_position"));
        var stopConditions = payload.GetProperty("stop_conditions")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

        if (activeSegments.ContainsKey(actor))
        {
            AddError(
                issues,
                path,
                "A mover cannot start a second segment before MoveEnded.");
            return;
        }

        if (movementKind is "Approach" or "Retreat")
        {
            var expectedAction = movementKind == "Approach" ? "sys_approach" : "sys_retreat";
            if (!StringComparer.Ordinal.Equals(action, expectedAction) || decision is null)
            {
                AddError(
                    issues,
                    path,
                    $"WP-07 {movementKind} movement must preserve its committed action and decision IDs.");
            }

            if (!stopConditions.SequenceEqual(Wp07StopConditions, StringComparer.Ordinal))
            {
                AddError(
                    issues,
                    path + "/payload/stop_conditions",
                    "WP-07 stop conditions must be WallReached, PreferredRangeReached, SegmentExpired in that order.");
            }
        }

        if (!TryGetPriorEvent(source, priorEvents, out var sourceEvent) ||
            !HasStringValue(sourceEvent, "event_type", "ActionPhaseChanged") ||
            !StringComparer.Ordinal.Equals(
                actor,
                GetNullableString(sourceEvent.GetProperty("actor_id"))) ||
            !StringComparer.Ordinal.Equals(
                action,
                GetNullableString(sourceEvent.GetProperty("action_id"))) ||
            !StringComparer.Ordinal.Equals(
                decision,
                GetNullableString(sourceEvent.GetProperty("decision_id"))) ||
            !HasStringValue(sourceEvent.GetProperty("payload"), "from_phase", "Startup") ||
            !HasStringValue(sourceEvent.GetProperty("payload"), "to_phase", "Active"))
        {
            AddError(
                issues,
                path + "/source_event_id",
                "MoveStarted source must be the actor's matching Startup-to-Active event.");
        }

        ValidateExactRelatedEvents(combatEvent, path, issues, source);
        ValidateMarkerFrames(combatEvent, path, fromPosition, issues);

        activeSegments[actor] = new MovementSegment(
            combatEvent.GetProperty("event_id").GetString()!,
            action,
            decision,
            movementKind,
            payload.GetProperty("direction").GetString()!,
            fromPosition,
            stopConditions);
    }

    private static void ValidatePositionChanged(
        JsonElement combatEvent,
        string path,
        IReadOnlyDictionary<string, JsonElement> priorEvents,
        IDictionary<string, MovementSegment> activeSegments,
        IDictionary<string, string> lastVoluntaryByActor,
        ICollection<ReplayVerificationIssue> issues)
    {
        ValidateNullMovementEnvelope(combatEvent, path, issues);

        var actor = GetNullableString(combatEvent.GetProperty("actor_id"));
        var payload = combatEvent.GetProperty("payload");
        var movementKind = payload.GetProperty("movement_kind").GetString()!;
        var fromPosition = ReadInteger(payload.GetProperty("from_position"));
        var toPosition = ReadInteger(payload.GetProperty("to_position"));
        var requestedDelta = ReadInteger(payload.GetProperty("requested_delta"));
        var actualDelta = ReadInteger(payload.GetProperty("actual_delta"));
        var blockedByWall = ReadInteger(payload.GetProperty("blocked_by_wall"));

        if (actualDelta != toPosition - fromPosition)
        {
            AddError(
                issues,
                path + "/payload/actual_delta",
                "PositionChanged actual_delta must equal to_position - from_position.");
        }

        ValidatePositionFrames(combatEvent, path, fromPosition, toPosition, issues);

        switch (movementKind)
        {
            case "Voluntary":
                ValidateVoluntaryPositionChanged(
                    combatEvent,
                    path,
                    actor,
                    fromPosition,
                    toPosition,
                    requestedDelta,
                    actualDelta,
                    blockedByWall,
                    activeSegments,
                    lastVoluntaryByActor,
                    issues);
                break;

            case "Separation":
                ValidateSeparationPositionChanged(
                    combatEvent,
                    path,
                    actor,
                    fromPosition,
                    toPosition,
                    requestedDelta,
                    actualDelta,
                    blockedByWall,
                    priorEvents,
                    activeSegments,
                    lastVoluntaryByActor,
                    issues);
                break;
        }
    }

    private static void ValidateVoluntaryPositionChanged(
        JsonElement combatEvent,
        string path,
        string? actor,
        BigInteger fromPosition,
        BigInteger toPosition,
        BigInteger requestedDelta,
        BigInteger actualDelta,
        BigInteger blockedByWall,
        IDictionary<string, MovementSegment> activeSegments,
        IDictionary<string, string> lastVoluntaryByActor,
        ICollection<ReplayVerificationIssue> issues)
    {
        ValidateExactReasonCodes(combatEvent, path, issues, "VoluntaryMovement");

        var action = GetNullableString(combatEvent.GetProperty("action_id"));
        var decision = GetNullableString(combatEvent.GetProperty("decision_id"));
        if (action is null || decision is null)
        {
            AddError(
                issues,
                path,
                "Voluntary movement must preserve non-null action_id and decision_id.");
        }

        var absoluteRequested = BigInteger.Abs(requestedDelta);
        var absoluteActual = BigInteger.Abs(actualDelta);
        if (absoluteActual > absoluteRequested ||
            (actualDelta != BigInteger.Zero && requestedDelta.Sign != actualDelta.Sign) ||
            blockedByWall != absoluteRequested - absoluteActual)
        {
            AddError(
                issues,
                path + "/payload",
                "Voluntary movement delta and blocked_by_wall values are inconsistent.");
        }

        if (actor is null || !activeSegments.TryGetValue(actor, out var segment))
        {
            AddError(
                issues,
                path + "/source_event_id",
                "Voluntary PositionChanged requires an active MoveStarted segment for the actor.");
            return;
        }

        var source = GetNullableString(combatEvent.GetProperty("source_event_id"));
        if (!StringComparer.Ordinal.Equals(source, segment.StartEventId))
        {
            AddError(
                issues,
                path + "/source_event_id",
                "Voluntary PositionChanged source must be the actor's MoveStarted event.");
        }

        ValidateExactRelatedEvents(combatEvent, path, issues, source);

        if (!StringComparer.Ordinal.Equals(action, segment.ActionId) ||
            !StringComparer.Ordinal.Equals(decision, segment.DecisionId))
        {
            AddError(
                issues,
                path,
                "Voluntary PositionChanged must preserve the segment action and decision IDs.");
        }

        if (fromPosition != segment.CurrentPosition)
        {
            AddError(
                issues,
                path + "/payload/from_position",
                "PositionChanged must continue from the actor's latest segment position.");
        }

        if (requestedDelta != BigInteger.Zero &&
            ((segment.Direction == "Right" && requestedDelta.Sign < 0) ||
             (segment.Direction == "Left" && requestedDelta.Sign > 0)))
        {
            AddError(
                issues,
                path + "/payload/requested_delta",
                "Voluntary requested_delta must agree with the frozen MoveStarted direction.");
        }

        segment.CurrentPosition = toPosition;
        segment.LastMovementEventId = combatEvent.GetProperty("event_id").GetString()!;
        segment.WallClipped |= blockedByWall > BigInteger.Zero;
        lastVoluntaryByActor[actor] = segment.LastMovementEventId;
    }

    private static void ValidateSeparationPositionChanged(
        JsonElement combatEvent,
        string path,
        string? actor,
        BigInteger fromPosition,
        BigInteger toPosition,
        BigInteger requestedDelta,
        BigInteger actualDelta,
        BigInteger blockedByWall,
        IReadOnlyDictionary<string, JsonElement> priorEvents,
        IDictionary<string, MovementSegment> activeSegments,
        IDictionary<string, string> lastVoluntaryByActor,
        ICollection<ReplayVerificationIssue> issues)
    {
        ValidateExactReasonCodes(combatEvent, path, issues, "SeparationCorrection");

        if (GetNullableString(combatEvent.GetProperty("action_id")) is not null ||
            GetNullableString(combatEvent.GetProperty("decision_id")) is not null ||
            blockedByWall != BigInteger.Zero ||
            requestedDelta != actualDelta)
        {
            AddError(
                issues,
                path,
                "Separation must have null action/decision IDs, zero wall clip, and requested_delta equal to actual_delta.");
        }

        var source = GetNullableString(combatEvent.GetProperty("source_event_id"));
        if (actor is null ||
            !lastVoluntaryByActor.TryGetValue(actor, out var expectedSource) ||
            !StringComparer.Ordinal.Equals(source, expectedSource) ||
            !TryGetPriorEvent(source, priorEvents, out var sourceEvent) ||
            !IsVoluntaryPositionChanged(sourceEvent) ||
            !StringComparer.Ordinal.Equals(
                actor,
                GetNullableString(sourceEvent.GetProperty("actor_id"))))
        {
            AddError(
                issues,
                path + "/source_event_id",
                "Separation source must be the actor's latest voluntary PositionChanged event.");
        }

        ValidateSeparationRelatedEvents(
            combatEvent,
            path,
            priorEvents,
            lastVoluntaryByActor,
            issues);

        if (actor is not null && activeSegments.TryGetValue(actor, out var segment))
        {
            if (fromPosition != segment.CurrentPosition)
            {
                AddError(
                    issues,
                    path + "/payload/from_position",
                    "Separation must continue from the actor's provisional segment position.");
            }

            segment.CurrentPosition = toPosition;
            segment.LastMovementEventId = combatEvent.GetProperty("event_id").GetString()!;
        }
    }

    private static void ValidateMoveEnded(
        JsonElement combatEvent,
        string path,
        IDictionary<string, MovementSegment> activeSegments,
        ICollection<ReplayVerificationIssue> issues)
    {
        ValidateNullMovementEnvelope(combatEvent, path, issues);

        var actor = GetNullableString(combatEvent.GetProperty("actor_id"));
        if (actor is null || !activeSegments.TryGetValue(actor, out var segment))
        {
            AddError(
                issues,
                path + "/source_event_id",
                "MoveEnded requires an active MoveStarted segment for the actor.");
            return;
        }

        var payload = combatEvent.GetProperty("payload");
        var source = GetNullableString(combatEvent.GetProperty("source_event_id"));
        var stopReason = payload.GetProperty("stop_reason").GetString()!;
        var fromPosition = ReadInteger(payload.GetProperty("from_position"));
        var toPosition = ReadInteger(payload.GetProperty("to_position"));

        ValidateExactReasonCodes(combatEvent, path, issues, stopReason);
        ValidateExactRelatedEvents(combatEvent, path, issues, source);

        if (!StringComparer.Ordinal.Equals(source, segment.LastMovementEventId))
        {
            AddError(
                issues,
                path + "/source_event_id",
                "MoveEnded source must be the actor's latest movement event, or MoveStarted when no delta was emitted.");
        }

        if (!StringComparer.Ordinal.Equals(
                GetNullableString(combatEvent.GetProperty("action_id")),
                segment.ActionId) ||
            !StringComparer.Ordinal.Equals(
                GetNullableString(combatEvent.GetProperty("decision_id")),
                segment.DecisionId))
        {
            AddError(
                issues,
                path,
                "MoveEnded must preserve the segment action and decision IDs.");
        }

        if (!segment.StopConditions.Contains(stopReason, StringComparer.Ordinal))
        {
            AddError(
                issues,
                path + "/payload/stop_reason",
                "MoveEnded stop_reason must be declared by MoveStarted.stop_conditions.");
        }

        if ((segment.WallClipped && stopReason != "WallReached") ||
            (!segment.WallClipped && stopReason == "WallReached"))
        {
            AddError(
                issues,
                path + "/payload/stop_reason",
                "WallReached must agree with an observable wall-clipped movement request in the segment.");
        }

        var beforeActor = combatEvent.GetProperty("before").GetProperty("actor");
        if (stopReason == "SegmentExpired" &&
            (!beforeActor.TryGetProperty("state_ticks_remaining", out var ticksRemaining) ||
             ticksRemaining.ValueKind != JsonValueKind.Number ||
             ReadInteger(ticksRemaining) != BigInteger.One))
        {
            AddError(
                issues,
                path + "/payload/stop_reason",
                "SegmentExpired is valid only on the final active tick with state_ticks_remaining equal to 1.");
        }

        if (fromPosition != segment.StartPosition || toPosition != segment.CurrentPosition)
        {
            AddError(
                issues,
                path + "/payload",
                "MoveEnded positions must span the segment start and latest actor position.");
        }

        ValidateMarkerFrames(combatEvent, path, segment.CurrentPosition, issues);
        activeSegments.Remove(actor);
    }

    private static void ValidateNullMovementEnvelope(
        JsonElement combatEvent,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        if (combatEvent.GetProperty("effect_id").ValueKind != JsonValueKind.Null ||
            combatEvent.GetProperty("resolution_group_id").ValueKind != JsonValueKind.Null ||
            combatEvent.GetProperty("rng").ValueKind != JsonValueKind.Null)
        {
            AddError(
                issues,
                path,
                "WP-07 movement events must not carry effect, resolution-group, or RNG provenance.");
        }
    }

    private static void ValidatePositionFrames(
        JsonElement combatEvent,
        string path,
        BigInteger fromPosition,
        BigInteger toPosition,
        ICollection<ReplayVerificationIssue> issues)
    {
        var before = combatEvent.GetProperty("before").GetProperty("actor");
        var after = combatEvent.GetProperty("after").GetProperty("actor");
        if (!TryReadFramePosition(before, out var beforePosition) ||
            !TryReadFramePosition(after, out var afterPosition) ||
            beforePosition != fromPosition ||
            afterPosition != toPosition ||
            !FramesEqualExceptPosition(before, after))
        {
            AddError(
                issues,
                path + "/before",
                "PositionChanged frames must project exactly the from/to position submutation with stable facing and state.");
        }
    }

    private static void ValidateMarkerFrames(
        JsonElement combatEvent,
        string path,
        BigInteger position,
        ICollection<ReplayVerificationIssue> issues)
    {
        var before = combatEvent.GetProperty("before").GetProperty("actor");
        var after = combatEvent.GetProperty("after").GetProperty("actor");
        if (!TryReadFramePosition(before, out var beforePosition) ||
            !TryReadFramePosition(after, out var afterPosition) ||
            beforePosition != position ||
            afterPosition != position ||
            !JsonElement.DeepEquals(before, after))
        {
            AddError(
                issues,
                path + "/before",
                "Movement marker frames must be identical and agree with the payload position.");
        }
    }

    private static void ValidateSeparationRelatedEvents(
        JsonElement combatEvent,
        string path,
        IReadOnlyDictionary<string, JsonElement> priorEvents,
        IDictionary<string, string> lastVoluntaryByActor,
        ICollection<ReplayVerificationIssue> issues)
    {
        var tick = ReadInteger(combatEvent.GetProperty("tick"));
        var expected = lastVoluntaryByActor.Values
            .Where(id => priorEvents.TryGetValue(id, out var relatedEvent) &&
                         IsVoluntaryPositionChanged(relatedEvent) &&
                         ReadInteger(relatedEvent.GetProperty("tick")) == tick)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var actual = combatEvent.GetProperty("payload")
            .GetProperty("related_event_ids")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            AddError(
                issues,
                path + "/payload/related_event_ids",
                "Separation related IDs must exactly list the latest same-tick voluntary movement events in ordinal order.");
        }
    }

    private static void ValidateExactRelatedEvents(
        JsonElement combatEvent,
        string path,
        ICollection<ReplayVerificationIssue> issues,
        params string?[] expected)
    {
        var actual = combatEvent.GetProperty("payload")
            .GetProperty("related_event_ids")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        if (expected.Any(item => item is null) ||
            !actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            AddError(
                issues,
                path + "/payload/related_event_ids",
                "The movement event's related IDs must exactly equal its canonical causal sources.");
        }
    }

    private static void ValidateExactReasonCodes(
        JsonElement combatEvent,
        string path,
        ICollection<ReplayVerificationIssue> issues,
        params string[] expected)
    {
        var actual = combatEvent.GetProperty("reason_codes")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            AddError(
                issues,
                path + "/reason_codes",
                "Movement reason_codes do not match the canonical event meaning.");
        }
    }

    private static bool FramesEqualExceptPosition(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != JsonValueKind.Object || right.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in left.EnumerateObject())
        {
            if (property.NameEquals("position"))
            {
                continue;
            }

            if (!right.TryGetProperty(property.Name, out var rightValue) ||
                !JsonElement.DeepEquals(property.Value, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryReadFramePosition(JsonElement frame, out BigInteger position)
    {
        if (frame.ValueKind != JsonValueKind.Object ||
            !frame.TryGetProperty("position", out var value))
        {
            position = BigInteger.Zero;
            return false;
        }

        position = ReadInteger(value);
        return true;
    }

    private static bool TryGetPriorEvent(
        string? eventId,
        IReadOnlyDictionary<string, JsonElement> priorEvents,
        out JsonElement combatEvent)
    {
        if (eventId is not null && priorEvents.TryGetValue(eventId, out combatEvent))
        {
            return true;
        }

        combatEvent = default;
        return false;
    }

    private static bool IsVoluntaryPositionChanged(JsonElement combatEvent) =>
        HasStringValue(combatEvent, "event_type", "PositionChanged") &&
        HasStringValue(combatEvent.GetProperty("payload"), "movement_kind", "Voluntary");

    private static BigInteger ReadInteger(JsonElement value) =>
        BigInteger.Parse(
            value.GetRawText(),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);

    private static bool HasStringValue(
        JsonElement value,
        string propertyName,
        string expected) =>
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        StringComparer.Ordinal.Equals(property.GetString(), expected);

    private static string? GetNullableString(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ? null : value.GetString();

    private static void AddError(
        ICollection<ReplayVerificationIssue> issues,
        string path,
        string message) =>
        issues.Add(
            new ReplayVerificationIssue(
                ReplayVerificationLayer.Semantic,
                ReplayVerificationSeverity.Error,
                ReplayVerificationCodes.MovementInvalid,
                path,
                message));

    private sealed class MovementSegment
    {
        public MovementSegment(
            string startEventId,
            string? actionId,
            string? decisionId,
            string movementKind,
            string direction,
            BigInteger startPosition,
            IReadOnlyList<string> stopConditions)
        {
            StartEventId = startEventId;
            LastMovementEventId = startEventId;
            ActionId = actionId;
            DecisionId = decisionId;
            MovementKind = movementKind;
            Direction = direction;
            StartPosition = startPosition;
            CurrentPosition = startPosition;
            StopConditions = stopConditions;
        }

        public string StartEventId { get; }

        public string LastMovementEventId { get; set; }

        public string? ActionId { get; }

        public string? DecisionId { get; }

        public string MovementKind { get; }

        public string Direction { get; }

        public BigInteger StartPosition { get; }

        public BigInteger CurrentPosition { get; set; }

        public IReadOnlyList<string> StopConditions { get; }

        public bool WallClipped { get; set; }
    }
}
