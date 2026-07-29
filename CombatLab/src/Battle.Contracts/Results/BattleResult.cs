using System.Collections.ObjectModel;
using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Results;

public enum BattleResultStatus
{
    Completed,
    Rejected,
    FailedInvariant,
}

public readonly record struct BattleMetric(StableId Name, long Value);

public enum BattleRejectionDetailKind
{
    Null,
    String,
    Integer,
    Boolean,
}

public readonly record struct BattleRejectionDetailValue
{
    private BattleRejectionDetailValue(
        BattleRejectionDetailKind kind,
        string? stringValue,
        long integerValue,
        bool booleanValue)
    {
        Kind = kind;
        StringValue = stringValue;
        IntegerValue = integerValue;
        BooleanValue = booleanValue;
    }

    public BattleRejectionDetailKind Kind { get; }

    public string? StringValue { get; }

    public long IntegerValue { get; }

    public bool BooleanValue { get; }

    public static BattleRejectionDetailValue Null { get; } =
        new(BattleRejectionDetailKind.Null, null, 0, false);

    public static BattleRejectionDetailValue FromString(string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new BattleRejectionDetailValue(
            BattleRejectionDetailKind.String,
            value,
            0,
            false);
    }

    public static BattleRejectionDetailValue FromInteger(long value) =>
        new(BattleRejectionDetailKind.Integer, null, value, false);

    public static BattleRejectionDetailValue FromBoolean(bool value) =>
        new(BattleRejectionDetailKind.Boolean, null, 0, value);
}

public readonly record struct BattleRejectionDetail(
    string Key,
    BattleRejectionDetailValue Value);

public sealed class BattleRejectionError
{
    private readonly ReadOnlyCollection<BattleRejectionDetail> _details;

    public BattleRejectionError(
        ReasonCode code,
        string path,
        ExternalId? entityId,
        StableId messageKey,
        IEnumerable<BattleRejectionDetail> details)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (path.Length > 512)
        {
            throw new ArgumentException("A rejection path cannot exceed 512 characters.", nameof(path));
        }

        if (details is null)
        {
            throw new ArgumentNullException(nameof(details));
        }

        var detailList = new List<BattleRejectionDetail>(details);
        if (detailList.Count > 64 || HasDuplicateKeys(detailList))
        {
            throw new ArgumentException(
                "Rejection details must use unique keys and contain at most 64 entries.",
                nameof(details));
        }

        Code = code;
        Path = path;
        EntityId = entityId;
        MessageKey = messageKey;
        _details = new ReadOnlyCollection<BattleRejectionDetail>(detailList);
    }

    public ReasonCode Code { get; }

    public string Path { get; }

    public ExternalId? EntityId { get; }

    public StableId MessageKey { get; }

    public IReadOnlyList<BattleRejectionDetail> Details => _details;

    private static bool HasDuplicateKeys(IReadOnlyList<BattleRejectionDetail> details)
    {
        for (var left = 0; left < details.Count; left++)
        {
            for (var right = left + 1; right < details.Count; right++)
            {
                if (string.Equals(details[left].Key, details[right].Key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public sealed record BattleInvariantFailure(
    ReasonCode Code,
    string Phase,
    int Tick,
    string Message);

public sealed class BattleResult
{
    private readonly ReadOnlyCollection<BattleMetric> _metrics;
    private readonly ReadOnlyCollection<BattleRejectionError> _rejectionErrors;

    private BattleResult(
        BattleResultStatus status,
        BattleSummary? summary,
        Sha256Digest? finalDigest,
        ExternalId? replayId,
        IEnumerable<BattleMetric> metrics,
        IEnumerable<BattleRejectionError> rejectionErrors,
        BattleInvariantFailure? invariantFailure)
    {
        Status = status;
        Summary = summary;
        FinalDigest = finalDigest;
        ReplayId = replayId;
        _metrics = new ReadOnlyCollection<BattleMetric>(new List<BattleMetric>(metrics));
        _rejectionErrors = new ReadOnlyCollection<BattleRejectionError>(
            new List<BattleRejectionError>(rejectionErrors));
        InvariantFailure = invariantFailure;
    }

    public BattleResultStatus Status { get; }

    public BattleSummary? Summary { get; }

    public Sha256Digest? FinalDigest { get; }

    public ExternalId? ReplayId { get; }

    public IReadOnlyList<BattleMetric> Metrics => _metrics;

    public IReadOnlyList<BattleRejectionError> RejectionErrors => _rejectionErrors;

    public BattleInvariantFailure? InvariantFailure { get; }

    public static BattleResult Completed(
        BattleSummary summary,
        Sha256Digest finalDigest,
        ExternalId? replayId = null,
        IEnumerable<BattleMetric>? metrics = null)
    {
        if (summary is null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        return new BattleResult(
            BattleResultStatus.Completed,
            summary,
            finalDigest,
            replayId,
            metrics ?? Array.Empty<BattleMetric>(),
            Array.Empty<BattleRejectionError>(),
            null);
    }

    public static BattleResult Rejected(IEnumerable<BattleRejectionError> errors)
    {
        if (errors is null)
        {
            throw new ArgumentNullException(nameof(errors));
        }

        var rejectionErrors = new List<BattleRejectionError>(errors);
        if (rejectionErrors.Count == 0)
        {
            throw new ArgumentException("A rejected battle must contain at least one error.", nameof(errors));
        }

        return new BattleResult(
            BattleResultStatus.Rejected,
            null,
            null,
            null,
            Array.Empty<BattleMetric>(),
            rejectionErrors,
            null);
    }

    public static BattleResult FailedInvariant(
        BattleInvariantFailure failure,
        IEnumerable<BattleMetric>? metrics = null)
    {
        if (failure is null)
        {
            throw new ArgumentNullException(nameof(failure));
        }

        return new BattleResult(
            BattleResultStatus.FailedInvariant,
            null,
            null,
            null,
            metrics ?? Array.Empty<BattleMetric>(),
            Array.Empty<BattleRejectionError>(),
            failure);
    }
}
