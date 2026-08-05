using System.Collections.ObjectModel;
using Battle.Contracts.Versions;

namespace Battle.Replay.Verification;

public enum ReplayVerificationLayer
{
    Syntax,
    Schema,
    Semantic,
    Integrity,
}

public enum ReplayVerificationSeverity
{
    Warning,
    Error,
}

public sealed class ReplayVerificationIssue
{
    public ReplayVerificationIssue(
        ReplayVerificationLayer layer,
        ReplayVerificationSeverity severity,
        string code,
        string path,
        string message)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("A verification issue code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A verification issue path is required.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("A verification issue message is required.", nameof(message));
        }

        Layer = layer;
        Severity = severity;
        Code = code;
        Path = path;
        Message = message;
    }

    public ReplayVerificationLayer Layer { get; }

    public ReplayVerificationSeverity Severity { get; }

    public string Code { get; }

    public string Path { get; }

    public string Message { get; }
}

public sealed class ReplayVerificationResult
{
    private readonly ReadOnlyCollection<ReplayVerificationIssue> _issues;

    internal ReplayVerificationResult(
        IEnumerable<ReplayVerificationIssue> issues,
        Sha256Digest? computedInputDigest,
        Sha256Digest? computedFinalDigest,
        long eventCount)
    {
        if (issues is null)
        {
            throw new ArgumentNullException(nameof(issues));
        }

        _issues = new ReadOnlyCollection<ReplayVerificationIssue>(issues.ToList());
        ComputedInputDigest = computedInputDigest;
        ComputedFinalDigest = computedFinalDigest;
        EventCount = eventCount;
    }

    public bool IsValid => _issues.All(issue => issue.Severity != ReplayVerificationSeverity.Error);

    public bool HasWarnings => _issues.Any(issue => issue.Severity == ReplayVerificationSeverity.Warning);

    public IReadOnlyList<ReplayVerificationIssue> Issues => _issues;

    public Sha256Digest? ComputedInputDigest { get; }

    public Sha256Digest? ComputedFinalDigest { get; }

    public long EventCount { get; }
}

public static class ReplayVerificationCodes
{
    public const int ReplayInvalidExitCode = 40;

    public const string SchemaViolation = "schema.violation";
    public const string FirstEventInvalid = "semantic.first_event";
    public const string LastEventInvalid = "semantic.last_event";
    public const string SequenceInvalid = "semantic.sequence";
    public const string EventIdInvalid = "semantic.event_id";
    public const string TickOrderInvalid = "semantic.tick_order";
    public const string IdentityMismatch = "semantic.identity";
    public const string RoleMismatch = "semantic.role";
    public const string FrameMismatch = "semantic.frame";
    public const string CausalityInvalid = "semantic.causality";
    public const string RngSequenceInvalid = "semantic.rng_sequence";
    public const string SummaryMismatch = "semantic.summary";
    public const string KeyframeMismatch = "semantic.keyframe";
    public const string InputDigestMismatch = "integrity.input_digest";
    public const string PreviousDigestMismatch = "integrity.prev_digest";
    public const string EventDigestMismatch = "integrity.event_digest";
    public const string FinalDigestMismatch = "integrity.final_digest";
    public const string EventCountMismatch = "integrity.event_count";
}
