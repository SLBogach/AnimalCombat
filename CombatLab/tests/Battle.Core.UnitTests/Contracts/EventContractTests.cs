using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Contracts;

public sealed class EventContractTests
{
    [Fact]
    public void EventCatalog_MatchesReplaySchemaVocabulary()
    {
        var expected = new[]
        {
            "BattleStarted",
            "DecisionMade",
            "ActionCommitted",
            "ActionPhaseChanged",
            "ActionCancelled",
            "MoveStarted",
            "PositionChanged",
            "MoveEnded",
            "AttackPrepared",
            "ConflictResolved",
            "AttackHit",
            "AttackMissed",
            "Blocked",
            "Dodged",
            "Countered",
            "DamageApplied",
            "ResourceChanged",
            "EffectAdded",
            "EffectRemoved",
            "StateChanged",
            "KnockbackApplied",
            "WallImpact",
            "GrabStarted",
            "GrabEnded",
            "FinisherTriggered",
            "FighterDefeated",
            "TimeoutReached",
            "DrawDeclared",
            "BattleEnded",
        };

        Assert.Equal(expected, Enum.GetNames<CombatEventType>());
    }

    [Fact]
    public void StateAndPhaseCatalogs_MatchReplaySchemaVocabulary()
    {
        Assert.Equal(
            new[]
            {
                "Idle",
                "DecisionReady",
                "Approach",
                "Retreat",
                "AttackPrepare",
                "AttackActive",
                "Recovery",
                "Block",
                "Dodge",
                "DodgeRecovery",
                "CounterWindow",
                "Stunned",
                "KnockedDown",
                "Grabbing",
                "Grabbed",
                "Defeated",
            },
            Enum.GetNames<FighterState>());
        Assert.Equal(
            new[]
            {
                "Startup",
                "Active",
                "Recovery",
                "CancelWindow",
                "CommitLock",
                "Hold",
                "Throw",
                "GetUp",
            },
            Enum.GetNames<ActionPhase>());
    }

    [Fact]
    public void Draft_UsesPayloadTypeAndDefensivelyCopiesReasonCodes()
    {
        var reasons = new List<ReasonCode> { new("Initialization") };
        var draft = CreateDraft(reasons);

        reasons.Clear();

        Assert.Equal(CombatEventType.BattleStarted, draft.EventType);
        Assert.Single(draft.ReasonCodes);
        Assert.IsType<BattleStartedPayload>(draft.Payload);
        Assert.IsType<long>(draft.Sequence);
    }

    [Fact]
    public void Draft_RejectsDuplicateReasonCodesAndMismatchedEventId()
    {
        var duplicate = new ReasonCode("Initialization");

        Assert.Throws<ArgumentException>(() => CreateDraft(new[] { duplicate, duplicate }));
        Assert.Throws<ArgumentException>(
            () => CreateDraft(Array.Empty<ReasonCode>(), EventId.FromSequence(1)));
    }

    [Fact]
    public void FrameContracts_EnforceSchemaBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EffectFrame(
                new StableId("effect"),
                256,
                0,
                EffectExpiryBoundary.ExpireAfterTick));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ResourceFrame(new StableId("resource"), 0, -1));
    }

    [Fact]
    public void BattleStartedPayload_DefensivelyCopiesOrderedCollections()
    {
        var initiative = new List<FighterId> { FighterId.FighterA, FighterId.FighterB };
        var payload = new BattleStartedPayload(
            Array.Empty<EventId>(),
            ContractFixtures.Digest,
            new[]
            {
                ContractFixtures.CreateFrame(FighterId.FighterA),
                ContractFixtures.CreateFrame(FighterId.FighterB),
            },
            initiative,
            InitiativeTieBreak.SeededHash);

        initiative.Reverse();

        Assert.Equal(FighterId.FighterA, payload.InitiativeOrder[0]);
        Assert.Equal(FighterId.FighterB, payload.InitiativeOrder[1]);
    }

    [Fact]
    public void JournalPort_AcceptsDraftAndSummaryByReadonlyReference()
    {
        var journal = new RecordingJournal();
        var draft = CreateDraft(Array.Empty<ReasonCode>());
        var summary = ContractFixtures.CreateSummary();

        var identity = journal.Append(in draft);
        journal.Complete(in summary);

        Assert.Equal(draft.EventId, identity.EventId);
        Assert.Same(summary, journal.Summary);
    }

    private static CombatEventDraft CreateDraft(
        IEnumerable<ReasonCode> reasons,
        EventId? eventId = null) =>
        new(
            ContractVersions.Event,
            ContractVersions.Engine,
            ContractFixtures.Digest,
            new ExternalId("battle-0001"),
            0,
            0L,
            eventId ?? EventId.FromSequence(0),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            reasons,
            null,
            new FramePair(null, null),
            new FramePair(null, null),
            new BattleStartedPayload(
                Array.Empty<EventId>(),
                ContractFixtures.Digest,
                new[]
                {
                    ContractFixtures.CreateFrame(FighterId.FighterA),
                    ContractFixtures.CreateFrame(FighterId.FighterB),
                },
                new[] { FighterId.FighterA, FighterId.FighterB },
                InitiativeTieBreak.StatThenSeededHash));

    private sealed class RecordingJournal : ICombatEventJournal
    {
        public BattleSummary? Summary { get; private set; }

        public CombatEventIdentity Append(in CombatEventDraft draft) =>
            new(draft.EventId, draft.Sequence);

        public void Complete(in BattleSummary summary)
        {
            Summary = summary;
        }
    }
}
