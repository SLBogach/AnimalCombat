using Battle.Contracts.Events;
using Battle.Contracts.Versions;

namespace Battle.Replay.Journal;

public sealed class JournaledCombatEvent
{
    private readonly byte[] _canonicalJson;

    internal JournaledCombatEvent(
        CombatEventDraft draft,
        Sha256Digest previousDigest,
        Sha256Digest eventDigest,
        byte[] canonicalJson)
    {
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        PreviousDigest = previousDigest;
        EventDigest = eventDigest;
        _canonicalJson = canonicalJson?.ToArray() ?? throw new ArgumentNullException(nameof(canonicalJson));
    }

    public CombatEventIdentity Identity => new(Draft.EventId, Draft.Sequence);

    public CombatEventDraft Draft { get; }

    public Sha256Digest PreviousDigest { get; }

    public Sha256Digest EventDigest { get; }

    public ReadOnlyMemory<byte> CanonicalJson => new(_canonicalJson.ToArray());
}
