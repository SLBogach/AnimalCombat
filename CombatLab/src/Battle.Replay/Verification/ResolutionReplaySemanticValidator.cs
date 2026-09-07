using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Battle.Replay.Verification;

/// <summary>
/// Config-free semantic checks for resolution traces introduced by battle.core/0.4.x.
/// Historical engines are deliberately not reinterpreted by these rules.
/// </summary>
internal static class ResolutionReplaySemanticValidator
{
    internal static void Validate(
        JsonElement replay,
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var engine = replay.GetProperty("engine").GetProperty("engine_version").GetString()!;
        if (!IsCompatible(engine))
        {
            return;
        }

        try
        {
            ValidateGroups(events, issues);
            ValidateResolutionEvents(events, issues);
        }
        catch (Exception exception) when (exception is
                   ArithmeticException or FormatException or InvalidOperationException or
                   ArgumentException or KeyNotFoundException)
        {
            Add(issues, "$/events", "Malformed resolution semantics: " + exception.Message);
        }
    }

    private static void ValidateGroups(
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var closed = new HashSet<string>(StringComparer.Ordinal);
        string? current = null;
        BigInteger? currentTick = null;
        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            var groupElement = item.GetProperty("resolution_group_id");
            var group = groupElement.ValueKind == JsonValueKind.Null ? null : groupElement.GetString();
            if (!StringComparer.Ordinal.Equals(group, current))
            {
                if (current is not null)
                {
                    closed.Add(current);
                }

                current = group;
                currentTick = group is null ? null : ReadInteger(item.GetProperty("tick"));
                if (group is not null && closed.Contains(group))
                {
                    Add(issues, $"$/events/{index}/resolution_group_id", "A closed resolution group cannot reopen.");
                }
            }
            else if (group is not null && ReadInteger(item.GetProperty("tick")) != currentTick)
            {
                Add(issues, $"$/events/{index}/tick", "One resolution group must remain on one tick.");
            }
        }
    }

    private static void ValidateResolutionEvents(
        IReadOnlyList<JsonElement> events,
        ICollection<ReplayVerificationIssue> issues)
    {
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var eventIndexById = events.Select((item, index) => (item, index)).ToDictionary(
            pair => pair.item.GetProperty("event_id").GetString()!,
            pair => pair.index,
            StringComparer.Ordinal);
        var lastDamageByGroup = events
            .Select((item, index) => (Item: item, Index: index))
            .Where(pair => IsType(pair.Item, "DamageApplied") &&
                           pair.Item.GetProperty("resolution_group_id").ValueKind == JsonValueKind.String)
            .GroupBy(pair => pair.Item.GetProperty("resolution_group_id").GetString()!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(pair => pair.Index), StringComparer.Ordinal);

        for (var index = 0; index < events.Count; index++)
        {
            var item = events[index];
            var type = item.GetProperty("event_type").GetString()!;
            var path = $"$/events/{index}";
            ValidateResolutionRng(item, path, issues);

            if (item.GetProperty("resolution_group_id").ValueKind == JsonValueKind.String)
            {
                var source = item.GetProperty("source_event_id");
                var related = item.GetProperty("payload").GetProperty("related_event_ids")
                    .EnumerateArray().Select(value => value.GetString()!).ToArray();
                if (source.ValueKind != JsonValueKind.String ||
                    !eventIndexById.TryGetValue(source.GetString()!, out var sourceIndex) ||
                    sourceIndex >= index || !related.Contains(source.GetString()!, StringComparer.Ordinal))
                {
                    Add(issues, path + "/source_event_id",
                        "A resolution event source must be one earlier primary cause included in related_event_ids.");
                }
            }

            if (type is "AttackHit" or "AttackMissed")
            {
                var payload = item.GetProperty("payload");
                var target = item.GetProperty("target_id").GetString()!;
                var key = payload.GetProperty("hit_group_id").GetString()! + "\u001f" + target;
                var isConsumedMiss = type == "AttackMissed" &&
                                     HasString(payload, "miss_reason", "HitGroupConsumed");
                if (!consumed.Add(key) && !isConsumedMiss)
                {
                    Add(issues, path + "/payload/hit_group_id", "A hit group may be consumed once per target.");
                }
            }

            if (type is "AttackHit" or "Blocked" or "WallImpact")
            {
                ValidateMarkerFrames(item, path, issues);
            }

            if (type == "DamageApplied")
            {
                ValidateDamage(item, path, issues);
                ValidateSortedStrings(item.GetProperty("payload").GetProperty("damage_tags"), path + "/payload/damage_tags", issues);
            }
            else if (type == "AttackHit")
            {
                ValidateSortedStrings(item.GetProperty("payload").GetProperty("attack_tags"), path + "/payload/attack_tags", issues);
            }
            else if (type is "Countered" or "Dodged" or "Blocked")
            {
                ValidateSortedStrings(item.GetProperty("payload").GetProperty("cancelled_intent_ids"), path + "/payload/cancelled_intent_ids", issues);
            }

            if (type == "FighterDefeated" && item.GetProperty("resolution_group_id").ValueKind == JsonValueKind.String)
            {
                var group = item.GetProperty("resolution_group_id").GetString()!;
                if (lastDamageByGroup.TryGetValue(group, out var lastDamage) && index <= lastDamage)
                {
                    Add(issues, path, "Defeat cannot be emitted before all damage in its resolution group.");
                }
            }
        }
    }

    private static void ValidateDamage(
        JsonElement item,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        var payload = item.GetProperty("payload");
        var breakdown = payload.GetProperty("breakdown");
        var before = ReadInteger(payload.GetProperty("hp_before"));
        var after = ReadInteger(payload.GetProperty("hp_after"));
        var final = ReadInteger(breakdown.GetProperty("final"));
        var overkill = ReadInteger(breakdown.GetProperty("overkill"));
        var lethal = payload.GetProperty("lethal").GetBoolean();
        var targetBefore = ReadInteger(item.GetProperty("before").GetProperty("target").GetProperty("health"));
        var targetAfter = ReadInteger(item.GetProperty("after").GetProperty("target").GetProperty("health"));
        if (before != targetBefore || after != targetAfter || final != before - after + overkill ||
            lethal != (after == BigInteger.Zero) || after < 0 || after > before || overkill < 0)
        {
            Add(issues, path + "/payload", "Damage arithmetic, lethal flag and target frames must agree.");
        }
    }

    private static void ValidateMarkerFrames(
        JsonElement item,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        var before = item.GetProperty("before").GetProperty("target");
        var after = item.GetProperty("after").GetProperty("target");
        if (before.ValueKind == JsonValueKind.Object && after.ValueKind == JsonValueKind.Object &&
            ReadInteger(before.GetProperty("health")) != ReadInteger(after.GetProperty("health")))
        {
            Add(issues, path, "A resolution marker cannot mutate health.");
        }
    }

    private static void ValidateResolutionRng(
        JsonElement item,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        var rng = item.GetProperty("rng");
        if (rng.ValueKind == JsonValueKind.Null || !HasString(item, "resolution_group_id"))
        {
            return;
        }

        var operation = rng.GetProperty("operation").GetString();
        var minimum = ReadInteger(rng.GetProperty("range_min_inclusive"));
        var maximum = ReadInteger(rng.GetProperty("range_max_exclusive"));
        var result = ReadInteger(rng.GetProperty("result"));
        var normalized = ReadInteger(rng.GetProperty("normalized_fp"));
        var rawText = rng.GetProperty("raw_u32").GetString();
        var rawIsValid = BigInteger.TryParse(
            rawText,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var raw) && raw >= 0 && raw <= uint.MaxValue;
        var bound = maximum - minimum;
        var offset = rawIsValid && bound > 0 ? raw % bound : -1;
        if (!HasString(rng, "stream", "Resolution") || operation is not ("ChanceCheck" or "TieBreak") ||
            minimum != 0 || maximum <= minimum || result < minimum || result >= maximum || !rawIsValid ||
            result != minimum + offset || normalized != offset * 1_000 / bound)
        {
            Add(issues, path + "/rng",
                "Resolution RNG provenance has an invalid stream, operation, raw/result mapping or normalization.");
        }
    }

    private static void ValidateSortedStrings(
        JsonElement values,
        string path,
        ICollection<ReplayVerificationIssue> issues)
    {
        string? previous = null;
        foreach (var value in values.EnumerateArray())
        {
            var current = value.GetString()!;
            if (previous is not null && StringComparer.Ordinal.Compare(previous, current) >= 0)
            {
                Add(issues, path, "Canonical identifier sets must be strictly ordinal sorted and unique.");
                return;
            }

            previous = current;
        }
    }

    private static bool IsCompatible(string value)
    {
        const string prefix = "battle.core/0.4.";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var patch = value.AsSpan(prefix.Length);
        if (patch.IsEmpty || (patch.Length > 1 && patch[0] == '0'))
        {
            return false;
        }

        foreach (var character in patch)
        {
            if (character is < '0' or > '9') return false;
        }

        return true;
    }

    private static bool IsType(JsonElement item, string expected) =>
        HasString(item, "event_type", expected);

    private static bool HasString(JsonElement item, string name, string? expected = null) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        (expected is null || StringComparer.Ordinal.Equals(value.GetString(), expected));

    private static BigInteger ReadInteger(JsonElement value) =>
        BigInteger.Parse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static void Add(
        ICollection<ReplayVerificationIssue> issues,
        string path,
        string message) => issues.Add(new ReplayVerificationIssue(
        ReplayVerificationLayer.Semantic,
        ReplayVerificationSeverity.Error,
        ReplayVerificationCodes.ResolutionInvalid,
        path,
        message));
}
