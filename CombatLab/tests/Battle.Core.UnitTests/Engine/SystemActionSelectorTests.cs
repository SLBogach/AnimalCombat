using Battle.Core;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Initialization;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;

namespace Battle.Core.UnitTests.Engine;

public sealed class SystemActionSelectorTests
{
    public static TheoryData<string[], string> PriorityCases => new()
    {
        { new[] { "sys_approach", "sys_retreat", "sys_wait" }, "sys_approach" },
        { new[] { "sys_retreat", "sys_wait" }, "sys_retreat" },
        { new[] { "sys_approach", "sys_wait" }, "sys_approach" },
        { new[] { "sys_wait" }, "sys_wait" },
    };

    [Theory]
    [MemberData(nameof(PriorityCases))]
    public void WP06_SYS_001_FixedPriorityChoosesApproachThenRetreatThenWait(
        string[] legal,
        string expected)
    {
        var selected = SystemActionSelector.ChooseByFixedPriority(
            legal.Select(id => new StableId(id)));

        Assert.Equal(expected, selected.Value);
    }

    [Fact]
    public void WP06_SYS_002_ZeroWeightFallbackUsesPriorityAndNoRng()
    {
        var selection = SystemActionSelector.Select(
            new[]
            {
                new SystemActionCandidate(SystemActionSelector.WaitId, 0),
                new SystemActionCandidate(SystemActionSelector.RetreatId, 0),
            });

        Assert.Equal(SystemActionSelector.RetreatId, selection.ActionId);
        Assert.Equal(0, selection.ChosenWeight);
        Assert.Equal(0, selection.WeightSum);
        Assert.Equal(DecisionSelectionMode.ZeroWeightFallback, selection.SelectionMode);
        Assert.Equal("ZeroWeightFallback", selection.ReasonCode.Value);
    }

    [Fact]
    public void WP06_SYS_003_OnlyWaitUsesConfiguredWeightWithoutRng()
    {
        var selection = SystemActionSelector.Select(
            new[] { new SystemActionCandidate(SystemActionSelector.WaitId, 150) });

        Assert.Equal(SystemActionSelector.WaitId, selection.ActionId);
        Assert.Equal(150, selection.ChosenWeight);
        Assert.Equal(150, selection.WeightSum);
        Assert.Equal(DecisionSelectionMode.OnlyLegalAction, selection.SelectionMode);
    }

    [Fact]
    public void WP06_SYS_004_EmptyLegalListIsAnInvariantFailure()
    {
        var failure = Assert.Throws<EngineInvariantException>(
            () => SystemActionSelector.Select(Array.Empty<SystemActionCandidate>()));

        Assert.Equal("NoLegalSystemAction", failure.Code.Value);
    }

    [Fact]
    public void WP06_SYS_004_EngineDoesNotWeakenAnEmptyAvailabilityResult()
    {
        var journal = new RecordingJournal();

        var result = new CombatEngine(
                NullTickCoordinatorObserver.Instance,
                EmptySystemActionAvailability.Instance)
            .Simulate(
                EngineTestFixture.CreateRequest(),
                EngineTestFixture.CreateConfig(),
                journal);

        Assert.Equal(Battle.Contracts.Results.BattleResultStatus.FailedInvariant, result.Status);
        Assert.Equal("NoLegalSystemAction", result.InvariantFailure!.Code.Value);
        Assert.Null(result.Summary);
        Assert.Equal(1, journal.BeginCount);
        Assert.Equal(1, journal.CompleteCount);
        Assert.Equal(
            new[] { CombatEventType.BattleStarted, CombatEventType.BattleEnded },
            journal.Drafts.Select(draft => draft.EventType));
        var ended = Assert.IsType<BattleEndedPayload>(journal.Drafts[^1].Payload);
        Assert.Equal(Battle.Contracts.Results.BattleOutcome.Invalid, ended.Summary.Outcome);
    }

    private sealed class EmptySystemActionAvailability : ISystemActionAvailability
    {
        internal static EmptySystemActionAvailability Instance { get; } = new();

        public IReadOnlyList<SystemActionCandidate> GetLegalCandidates(
            BattleState state,
            TickSnapshot snapshot,
            FighterId actorId,
            RuntimeBattleSettings settings) =>
            Array.Empty<SystemActionCandidate>();
    }
}
