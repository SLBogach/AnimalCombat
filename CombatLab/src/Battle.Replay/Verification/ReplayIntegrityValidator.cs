using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Versions;
using IntegrityCalculator = Battle.Replay.Integrity.ReplayIntegrity;

namespace Battle.Replay.Verification;

internal static class ReplayIntegrityValidator
{
    public static void Validate(
        JsonElement replay,
        ICollection<ReplayVerificationIssue> issues,
        out Sha256Digest computedInputDigest,
        out Sha256Digest computedFinalDigest)
    {
        computedInputDigest = IntegrityCalculator.ComputeInputDigest(replay);
        var integrity = replay.GetProperty("integrity");
        var storedInputDigest = ParseDigest(integrity.GetProperty("input_digest"));
        if (storedInputDigest != computedInputDigest)
        {
            AddError(
                issues,
                ReplayVerificationCodes.InputDigestMismatch,
                "$/integrity/input_digest",
                $"Stored input digest does not match computed digest '{computedInputDigest}'.");
        }

        var events = replay.GetProperty("events").EnumerateArray().ToArray();
        if (events[0].GetProperty("event_type").GetString() == "BattleStarted")
        {
            var startedInputDigest = ParseDigest(
                events[0].GetProperty("payload").GetProperty("input_digest"));
            if (startedInputDigest != computedInputDigest)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.InputDigestMismatch,
                    "$/events/0/payload/input_digest",
                    "BattleStarted input_digest must equal the computed replay input digest.");
            }
        }

        var previousDigest = computedInputDigest;
        for (var index = 0; index < events.Length; index++)
        {
            var eventIntegrity = events[index].GetProperty("integrity");
            var storedPrevious = ParseDigest(eventIntegrity.GetProperty("prev_digest"));
            if (storedPrevious != previousDigest)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.PreviousDigestMismatch,
                    $"$/events/{index}/integrity/prev_digest",
                    $"Expected previous digest '{previousDigest}'.");
            }

            var computedEventDigest = IntegrityCalculator.ComputeEventDigest(events[index]);
            var storedEventDigest = ParseDigest(eventIntegrity.GetProperty("event_digest"));
            if (storedEventDigest != computedEventDigest)
            {
                AddError(
                    issues,
                    ReplayVerificationCodes.EventDigestMismatch,
                    $"$/events/{index}/integrity/event_digest",
                    $"Stored event digest does not match computed digest '{computedEventDigest}'.");
            }

            previousDigest = computedEventDigest;
        }

        computedFinalDigest = previousDigest;
        var storedFinalDigest = ParseDigest(integrity.GetProperty("final_digest"));
        if (storedFinalDigest != computedFinalDigest)
        {
            AddError(
                issues,
                ReplayVerificationCodes.FinalDigestMismatch,
                "$/integrity/final_digest",
                $"Stored final digest does not match computed digest '{computedFinalDigest}'.");
        }

        var storedEventCount = integrity.GetProperty("event_count").GetInt64();
        if (storedEventCount != events.LongLength)
        {
            AddError(
                issues,
                ReplayVerificationCodes.EventCountMismatch,
                "$/integrity/event_count",
                $"Expected event_count {events.LongLength.ToString(CultureInfo.InvariantCulture)}.");
        }

        ValidateKeyframeDigests(replay.GetProperty("keyframes"), issues);
    }

    private static void ValidateKeyframeDigests(
        JsonElement keyframes,
        ICollection<ReplayVerificationIssue> issues)
    {
        var index = 0;
        foreach (var keyframe in keyframes.EnumerateArray())
        {
            var computed = IntegrityCalculator.ComputeKeyframeStateDigest(keyframe);
            var stored = ParseDigest(keyframe.GetProperty("state_digest"));
            if (stored != computed)
            {
                issues.Add(
                    new ReplayVerificationIssue(
                        ReplayVerificationLayer.Integrity,
                        ReplayVerificationSeverity.Warning,
                        ReplayVerificationCodes.KeyframeMismatch,
                        $"$/keyframes/{index}/state_digest",
                        "Keyframe state digest is invalid. Discard this keyframe and replay authoritative events instead."));
            }

            index++;
        }
    }

    private static Sha256Digest ParseDigest(JsonElement value) =>
        new(value.GetString()!);

    private static void AddError(
        ICollection<ReplayVerificationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(
            new ReplayVerificationIssue(
                ReplayVerificationLayer.Integrity,
                ReplayVerificationSeverity.Error,
                code,
                path,
                message));
}
