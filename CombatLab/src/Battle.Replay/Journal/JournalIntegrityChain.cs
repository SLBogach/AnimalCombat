using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Versions;
using CanonicalJsonWriter = Battle.Replay.CanonicalJson.CanonicalJson;
using IntegrityCalculator = Battle.Replay.Integrity.ReplayIntegrity;

namespace Battle.Replay.Journal;

internal sealed class JournalIntegrityChain
{
    private readonly ExternalId _replayId;
    private CombatJournalStart? _start;
    private byte[]? _inputProjection;
    private Sha256Digest? _inputDigest;
    private Sha256Digest? _lastDigest;

    public JournalIntegrityChain(ExternalId replayId)
    {
        if (string.IsNullOrEmpty(replayId.Value))
        {
            throw new ArgumentException("A replay ID is required.", nameof(replayId));
        }

        _replayId = replayId;
    }

    public bool HasBegun => _start is not null;

    public ExternalId ReplayId => _replayId;

    public CombatJournalStart? Start => _start;

    public Sha256Digest? InputDigest => _inputDigest;

    public ReadOnlyMemory<byte> InputProjection => _inputProjection is null
        ? ReadOnlyMemory<byte>.Empty
        : new ReadOnlyMemory<byte>(_inputProjection.ToArray());

    public Sha256Digest? LastDigest => _lastDigest;

    public JournalBeginResult Begin(CombatJournalStart start)
    {
        if (start is null)
        {
            throw new ArgumentNullException(nameof(start));
        }

        if (HasBegun)
        {
            throw new InvalidOperationException("The combat event journal has already begun.");
        }

        var projection = CombatJournalStartJsonWriter.WriteInputProjection(start, _replayId);
        var inputDigest = CanonicalJsonWriter.HashCanonicalBytes(projection);
        _start = start;
        _inputProjection = projection;
        _inputDigest = inputDigest;
        return new JournalBeginResult(inputDigest);
    }

    public void ValidateDraft(CombatEventDraft draft, long eventCount)
    {
        if (draft is null)
        {
            throw new ArgumentNullException(nameof(draft));
        }

        var start = _start
            ?? throw new InvalidOperationException("Begin must be called before Append.");
        if (draft.EngineVersion != start.EngineVersion ||
            draft.ConfigHash != start.Config.ConfigHash ||
            draft.BattleId != start.BattleId)
        {
            throw new InvalidOperationException(
                "Event engine, config hash and battle identity must match the journal start.");
        }

        if (eventCount == 0)
        {
            var started = draft.Payload as BattleStartedPayload
                ?? throw new InvalidOperationException(
                    "The first canonical event must use BattleStartedPayload.");
            if (started.InputDigest != _inputDigest)
            {
                throw new InvalidOperationException(
                    "BattleStarted input digest must equal the journal input digest.");
            }

            if (!FramesEqual(started.InitialFrames[0], start.FighterA.InitialFrame) ||
                !FramesEqual(started.InitialFrames[1], start.FighterB.InitialFrame))
            {
                throw new InvalidOperationException(
                    "BattleStarted initial frames must equal the journal input frames.");
            }
        }
    }

    public IntegrityAppendResult AppendValidated(CombatEventDraft draft)
    {
        var previousDigest = _lastDigest ?? _inputDigest
            ?? throw new InvalidOperationException("Begin must be called before Append.");
        var projectionJson = EventDraftJsonWriter.Write(draft, previousDigest, null);
        var eventDigest = IntegrityCalculator.ComputeEventDigest(projectionJson);
        var canonicalJson = EventDraftJsonWriter.Write(draft, previousDigest, eventDigest);
        _lastDigest = eventDigest;
        return new IntegrityAppendResult(previousDigest, eventDigest, canonicalJson);
    }

    public Sha256Digest Complete()
    {
        if (!HasBegun)
        {
            throw new InvalidOperationException("Begin must be called before Complete.");
        }

        return _lastDigest
            ?? throw new InvalidOperationException("At least one event is required before completion.");
    }

    private static bool FramesEqual(FighterFrame left, FighterFrame right) =>
        EventDraftJsonWriter.WriteFrame(left)
            .AsSpan()
            .SequenceEqual(EventDraftJsonWriter.WriteFrame(right));
}

internal readonly struct IntegrityAppendResult
{
    internal IntegrityAppendResult(
        Sha256Digest previousDigest,
        Sha256Digest eventDigest,
        byte[] canonicalJson)
    {
        PreviousDigest = previousDigest;
        EventDigest = eventDigest;
        CanonicalJson = canonicalJson;
    }

    internal Sha256Digest PreviousDigest { get; }

    internal Sha256Digest EventDigest { get; }

    internal byte[] CanonicalJson { get; }
}
