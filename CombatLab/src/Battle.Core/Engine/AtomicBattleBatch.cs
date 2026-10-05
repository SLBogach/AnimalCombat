using Battle.Contracts.Events;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace Battle.Core.Engine;

/// <summary>
/// Execute gameplay and its bounded closure only against isolated state/RNG/drafts.
/// Publish after exact event-cap preflight, including the resulting terminal cleanup reserve.
/// The journal port is trusted to append successfully, as in the existing engine contract.
/// </summary>
internal static class AtomicBattleBatch
{
    internal static void Execute(BattleState state, CombatEventEmitter emitter, TickPhase phase,
        Action<BattleState, CombatEventEmitter, IReadOnlyList<CombatEventDraft>> prepare,
        int additionalReservedEvents = 0)
    {
        if (state is null || emitter is null || prepare is null) throw new ArgumentNullException(nameof(state));
        if (additionalReservedEvents < 0) throw new ArgumentOutOfRangeException(nameof(additionalReservedEvents));
        state.EnsureMutable();
        var preview = state.Clone();
        var journal = new PreviewJournal();
        var previewEmitter = emitter.CreatePreview(journal);
        try
        {
            prepare(preview, previewEmitter, journal.Drafts);
            var cleanup = preview.Effects?.ActiveCount ?? 0;
            var drawReserve = phase == TickPhase.Resolve && preview.FighterA.Health == 0 && preview.FighterB.Health == 0 ? 1 : 0;
            emitter.PreflightNonterminalBatch(checked(journal.Drafts.Count + additionalReservedEvents + drawReserve), phase, cleanup);
            state.CommitPreview(preview);
            emitter.SetTerminalCleanupReserve(cleanup);
            emitter.PublishBatch(journal.Drafts);
        }
        catch (ArithmeticException exception)
        {
            throw new EngineInvariantException(EngineFailureCodes.EffectArithmeticOverflow, phase.ToString(),
                "Atomic effect arithmetic failed: " + exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new EngineInvariantException(EngineFailureCodes.EffectInvalidMutation, phase.ToString(),
                "Atomic effect mutation failed: " + exception.Message);
        }
    }

    private sealed class PreviewJournal : ICombatEventJournal
    {
        private readonly List<CombatEventDraft> drafts = new();
        internal IReadOnlyList<CombatEventDraft> Drafts => drafts;
        public CombatEventIdentity Append(in CombatEventDraft draft)
        {
            if (draft.EventType == CombatEventType.BattleEnded)
                throw new EngineInvariantException(EngineFailureCodes.EffectInvalidMutation, "AtomicBatch", "A gameplay preview cannot terminate the journal.");
            drafts.Add(draft);
            return new CombatEventIdentity(draft.EventId, draft.Sequence);
        }
        public JournalBeginResult Begin(in CombatJournalStart start) => throw new NotSupportedException();
        public JournalCompletion Complete(in BattleSummary summary) => throw new NotSupportedException();
    }
}
