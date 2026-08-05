using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using CanonicalJsonWriter = Battle.Replay.CanonicalJson.CanonicalJson;

namespace Battle.Replay.Journal;

internal static class EventDraftJsonWriter
{
    public static byte[] Write(
        CombatEventDraft draft,
        Sha256Digest previousDigest,
        Sha256Digest? eventDigest)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schema_version", draft.SchemaVersion.ToString());
            writer.WriteString("engine_version", draft.EngineVersion.ToString());
            writer.WriteString("config_hash", draft.ConfigHash.Value);
            writer.WriteString("battle_id", draft.BattleId.Value);
            writer.WriteNumber("tick", draft.Tick);
            writer.WriteNumber("sequence", draft.Sequence);
            writer.WriteString("event_id", draft.EventId.Value);
            WriteNullableString(writer, "source_event_id", draft.SourceEventId?.Value);
            writer.WriteString("event_type", draft.EventType.ToString());
            WriteNullableFighter(writer, "actor_id", draft.ActorId);
            WriteNullableFighter(writer, "target_id", draft.TargetId);
            WriteNullableString(writer, "action_id", draft.ActionId?.Value);
            WriteNullableString(writer, "effect_id", draft.EffectId?.Value);
            WriteNullableString(writer, "decision_id", draft.DecisionId?.Value);
            WriteNullableString(writer, "resolution_group_id", draft.ResolutionGroupId?.Value);

            writer.WritePropertyName("reason_codes");
            WriteValue(writer, draft.ReasonCodes, typeof(IReadOnlyList<ReasonCode>));

            writer.WritePropertyName("rng");
            if (draft.Rng.HasValue)
            {
                WriteRng(writer, draft.Rng.Value);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WritePropertyName("before");
            WriteValue(writer, draft.Before, typeof(FramePair));
            writer.WritePropertyName("after");
            WriteValue(writer, draft.After, typeof(FramePair));
            writer.WritePropertyName("payload");
            WritePayload(writer, draft.Payload);

            writer.WritePropertyName("integrity");
            writer.WriteStartObject();
            writer.WriteString("prev_digest", previousDigest.Value);
            if (eventDigest.HasValue)
            {
                writer.WriteString("event_digest", eventDigest.Value.Value);
            }
            else
            {
                writer.WriteNull("event_digest");
            }

            writer.WriteEndObject();
            writer.WriteStartObject("extensions");
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        return CanonicalJsonWriter.Canonicalize(buffer.WrittenMemory);
    }

    internal static byte[] WriteSummary(BattleSummary summary, bool includeEventCount)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSummaryObject(writer, summary, includeEventCount);
            writer.Flush();
        }

        return CanonicalJsonWriter.Canonicalize(buffer.WrittenMemory);
    }

    private static void WritePayload(Utf8JsonWriter writer, CombatEventPayload payload)
    {
        writer.WriteStartObject();
        var values = new List<WireProperty>
        {
            new("related_event_ids", payload.RelatedEventIds, typeof(IReadOnlyList<EventId>)),
        };

        if (payload is BattleEndedPayload ended)
        {
            AddSummaryProperties(values, ended.Summary, includeEventCount: false);
        }
        else
        {
            foreach (var property in payload.GetType().GetProperties(
                         BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (property.Name == nameof(ICombatEventPayload.EventType) ||
                    property.GetMethod is null)
                {
                    continue;
                }

                values.Add(
                    new WireProperty(
                        GetWireName(property.DeclaringType!, property.Name),
                        property.GetValue(payload),
                        property.PropertyType));
            }
        }

        WriteProperties(writer, values);
        writer.WriteEndObject();
    }

    private static void WriteSummaryObject(
        Utf8JsonWriter writer,
        BattleSummary summary,
        bool includeEventCount)
    {
        writer.WriteStartObject();
        var values = new List<WireProperty>();
        AddSummaryProperties(values, summary, includeEventCount);
        WriteProperties(writer, values);
        writer.WriteEndObject();
    }

    private static void AddSummaryProperties(
        ICollection<WireProperty> values,
        BattleSummary summary,
        bool includeEventCount)
    {
        values.Add(new WireProperty("outcome", summary.Outcome, typeof(BattleOutcome)));
        values.Add(new WireProperty("winner_fighter_id", summary.WinnerFighterId, typeof(FighterId?)));
        values.Add(new WireProperty("end_reason", summary.EndReason, typeof(BattleEndReason)));
        values.Add(new WireProperty("end_tick", summary.EndTick, typeof(int)));
        values.Add(new WireProperty("duration_ticks", summary.DurationTicks, typeof(int)));
        if (includeEventCount)
        {
            values.Add(new WireProperty("event_count", summary.EventCount, typeof(long)));
        }

        values.Add(
            new WireProperty(
                "pivotal_event_ids",
                summary.PivotalEventIds,
                typeof(IReadOnlyList<EventId>)));
        values.Add(
            new WireProperty(
                "final_frames",
                summary.FinalFrames,
                typeof(IReadOnlyList<FighterFrame>)));
    }

    private static void WriteProperties(
        Utf8JsonWriter writer,
        IEnumerable<WireProperty> values)
    {
        var sorted = values.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        for (var index = 1; index < sorted.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(sorted[index - 1].Name, sorted[index].Name))
            {
                throw new InvalidOperationException(
                    $"Typed payload maps more than one property to '{sorted[index].Name}'.");
            }
        }

        foreach (var property in sorted)
        {
            writer.WritePropertyName(property.Name);
            WriteValue(writer, property.Value, property.DeclaredType);
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, Type declaredType)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                return;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                return;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                return;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                return;
            case int integer:
                writer.WriteNumberValue(integer);
                return;
            case long longValue:
                writer.WriteNumberValue(longValue);
                return;
            case uint unsignedInteger:
                writer.WriteStringValue(unsignedInteger.ToString(CultureInfo.InvariantCulture));
                return;
            case ulong unsignedLong:
                writer.WriteStringValue(unsignedLong.ToString(CultureInfo.InvariantCulture));
                return;
            case StableId stableId:
                writer.WriteStringValue(stableId.Value);
                return;
            case ExternalId externalId:
                writer.WriteStringValue(externalId.Value);
                return;
            case EventId eventId:
                writer.WriteStringValue(eventId.Value);
                return;
            case DecisionId decisionId:
                writer.WriteStringValue(decisionId.Value);
                return;
            case ReasonCode reasonCode:
                writer.WriteStringValue(reasonCode.Value);
                return;
            case Sha256Digest digest:
                writer.WriteStringValue(digest.Value);
                return;
            case FighterId fighterId:
                writer.WriteStringValue(FighterIdText(fighterId));
                return;
            case RngProvenance rng:
                WriteRng(writer, rng);
                return;
            case Enum enumValue:
                writer.WriteStringValue(enumValue.ToString());
                return;
            case IDictionary:
                throw new InvalidOperationException(
                    $"Dictionary serialization is forbidden for canonical type '{declaredType.FullName}'.");
            case IEnumerable sequence:
                writer.WriteStartArray();
                var itemType = GetEnumerableItemType(declaredType) ?? typeof(object);
                foreach (var item in sequence)
                {
                    WriteValue(writer, item, itemType);
                }

                writer.WriteEndArray();
                return;
            case float:
            case double:
            case decimal:
                throw new InvalidOperationException("Floating-point values are forbidden in canonical replay JSON.");
        }

        WriteReflectedObject(writer, value);
    }

    private static void WriteReflectedObject(Utf8JsonWriter writer, object value)
    {
        var type = value.GetType();
        var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.GetMethod is not null &&
                property.GetIndexParameters().Length == 0 &&
                property.Name != nameof(ICombatEventPayload.EventType))
            .Select(property =>
                new WireProperty(
                    GetWireName(type, property.Name),
                    property.GetValue(value),
                    property.PropertyType));

        writer.WriteStartObject();
        WriteProperties(writer, properties);
        writer.WriteEndObject();
    }

    private static void WriteRng(Utf8JsonWriter writer, RngProvenance rng)
    {
        writer.WriteStartObject();
        writer.WriteString("index", rng.Index.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("normalized_fp", rng.NormalizedFixedPoint);
        writer.WriteString("operation", rng.Operation.ToString());
        writer.WriteNumber("range_max_exclusive", rng.RangeMaximumExclusive);
        writer.WriteNumber("range_min_inclusive", rng.RangeMinimumInclusive);
        writer.WriteString("raw_u32", rng.RawValue.ToString(CultureInfo.InvariantCulture));
        writer.WriteNumber("result", rng.Result);
        writer.WriteString("stream", rng.Stream.ToString());
        writer.WriteEndObject();
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableFighter(
        Utf8JsonWriter writer,
        string propertyName,
        FighterId? fighterId)
    {
        if (fighterId.HasValue)
        {
            writer.WriteString(propertyName, FighterIdText(fighterId.Value));
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static string FighterIdText(FighterId fighterId) => fighterId switch
    {
        FighterId.FighterA => "fighter_a",
        FighterId.FighterB => "fighter_b",
        _ => throw new ArgumentOutOfRangeException(nameof(fighterId)),
    };

    private static string GetWireName(Type declaringType, string propertyName)
    {
        if (declaringType == typeof(ResourceFrame) && propertyName == nameof(ResourceFrame.Maximum))
        {
            return "max";
        }

        return propertyName switch
        {
            nameof(ModifierTrace.MultiplierFixedPoint) => "multiplier_fp",
            nameof(AttackHitPayload.HitRangeMinimum) => "hit_range_min",
            nameof(AttackHitPayload.HitRangeMaximum) => "hit_range_max",
            nameof(BlockedPayload.ChanceFixedPoint) => "chance_fp",
            nameof(BlockedPayload.DamageReductionFixedPoint) => "damage_reduction_fp",
            nameof(DamageAppliedPayload.HealthBefore) => "hp_before",
            nameof(DamageAppliedPayload.HealthAfter) => "hp_after",
            nameof(StateChangedPayload.ControlRatioFixedPoint) => "control_ratio_fp",
            nameof(StateChangedPayload.FatigueMultiplierFixedPoint) => "fatigue_multiplier_fp",
            nameof(GrabStartedPayload.HoldMaximumTicks) => "hold_max_ticks",
            _ => ToSnakeCase(propertyName),
        };
    }

    private static string ToSnakeCase(string value)
    {
        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsUpper(character) && index > 0)
            {
                var previous = value[index - 1];
                var nextIsLower = index + 1 < value.Length && char.IsLower(value[index + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) ||
                    (char.IsUpper(previous) && nextIsLower))
                {
                    characters.Add('_');
                }
            }

            characters.Add(char.ToLowerInvariant(character));
        }

        return new string(characters.ToArray());
    }

    private static Type? GetEnumerableItemType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        var enumerable = type.GetInterfaces()
            .Append(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0];
    }

    private sealed class WireProperty
    {
        public WireProperty(string name, object? value, Type declaredType)
        {
            Name = name;
            Value = value;
            DeclaredType = declaredType;
        }

        public string Name { get; }

        public object? Value { get; }

        public Type DeclaredType { get; }
    }
}
