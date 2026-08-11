using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Ports;

public readonly record struct JournalBeginResult(Sha256Digest InputDigest);

public readonly record struct JournalCompletion(
    Sha256Digest FinalDigest,
    ExternalId? PublishedReplayId);
