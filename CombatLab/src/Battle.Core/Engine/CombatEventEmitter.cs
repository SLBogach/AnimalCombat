using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;

namespace Battle.Core.Engine;

internal sealed class CombatEventEmitter
{
    private readonly BattleRequest _request;
    private readonly CompiledBattleConfig _config;
    private readonly ICombatEventJournal _journal;
    private readonly int _maximumEvents;
    private long _nextSequence;

    internal CombatEventEmitter(
        BattleRequest request,
        CompiledBattleConfig config,
        ICombatEventJournal journal,
        int maximumEvents)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        if (maximumEvents < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEvents));
        }

        _maximumEvents = maximumEvents;
    }

    internal long EventCount => _nextSequence;

    internal bool IsTerminal { get; private set; }

    internal EventId? LastEventId => _nextSequence == 0
        ? null
        : EventId.FromSequence(_nextSequence - 1);

    internal CombatEventIdentity Emit(
        int tick,
        CombatEventPayload payload,
        FighterId? actorId = null,
        FighterId? targetId = null,
        StableId? actionId = null,
        StableId? effectId = null,
        DecisionId? decisionId = null,
        ExternalId? resolutionGroupId = null,
        EventId? sourceEventId = null,
        IEnumerable<ReasonCode>? reasonCodes = null,
        RngProvenance? rng = null,
        FramePair? before = null,
        FramePair? after = null)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (IsTerminal)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.TerminalMutation,
                "EventEmitter",
                "No canonical event may follow BattleEnded.");
        }

        var isTerminal = payload.EventType == CombatEventType.BattleEnded;
        if ((!isTerminal && _nextSequence >= _maximumEvents - 1L) ||
            (isTerminal && _nextSequence >= _maximumEvents))
        {
            throw new EngineInvariantException(
                EngineFailureCodes.EventCapExceeded,
                "EventEmitter",
                $"The event cap of {_maximumEvents} reserves the final slot for BattleEnded.");
        }

        var sequence = _nextSequence;
        var draft = new CombatEventDraft(
            ContractVersions.Event,
            _request.EngineVersion,
            _config.Reference.ConfigHash,
            _request.BattleId,
            tick,
            sequence,
            EventId.FromSequence(sequence),
            sourceEventId,
            actorId,
            targetId,
            actionId,
            effectId,
            decisionId,
            resolutionGroupId,
            reasonCodes ?? Array.Empty<ReasonCode>(),
            rng,
            before ?? new FramePair(null, null),
            after ?? new FramePair(null, null),
            payload);
        var identity = _journal.Append(in draft);
        if (identity.EventId != draft.EventId || identity.Sequence != draft.Sequence)
        {
            throw new EngineInvariantException(
                EngineFailureCodes.InvalidStateTransition,
                "EventEmitter",
                "The journal returned an event identity different from the emitted draft.");
        }

        _nextSequence = checked(_nextSequence + 1);
        IsTerminal = isTerminal;
        return identity;
    }
}
