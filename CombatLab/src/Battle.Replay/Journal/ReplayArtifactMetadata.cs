using Battle.Contracts.Ids;

namespace Battle.Replay.Journal;

/// <summary>
/// Caller-owned, non-gameplay metadata written into a published replay artifact.
/// </summary>
public sealed class ReplayArtifactMetadata
{
    public ReplayArtifactMetadata(
        DateTimeOffset createdAtUtc,
        ExternalId producer,
        bool fixture,
        string? notes)
    {
        if (string.IsNullOrEmpty(producer.Value))
        {
            throw new ArgumentException("A replay producer ID is required.", nameof(producer));
        }

        if (notes is { Length: > 4096 })
        {
            throw new ArgumentOutOfRangeException(
                nameof(notes),
                "Replay notes must not exceed 4096 characters.");
        }

        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Producer = producer;
        Fixture = fixture;
        Notes = notes;
    }

    public DateTimeOffset CreatedAtUtc { get; }

    public ExternalId Producer { get; }

    public bool Fixture { get; }

    public string? Notes { get; }
}
