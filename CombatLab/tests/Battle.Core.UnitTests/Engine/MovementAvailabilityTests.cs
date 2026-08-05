using Battle.Core.Decisions;
using Battle.Core.Initialization;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;

namespace Battle.Core.UnitTests.Engine;

public sealed class MovementAvailabilityTests
{
    [Theory]
    [InlineData(1_499, "sys_retreat", 450)]
    [InlineData(1_500, "sys_wait", 150)]
    [InlineData(1_550, "sys_wait", 150)]
    [InlineData(1_600, "sys_wait", 150)]
    [InlineData(1_601, "sys_approach", 650)]
    public void WP07_AVL_001_NeutralBandTruthTableReturnsExactlyOneCandidate(
        int gap,
        string expectedAction,
        int expectedWeight)
    {
        var setup = CreateSetup(4_000, checked(4_000 + 520 + 430 + gap));
        var state = setup.State;
        var snapshot = state.CreateSnapshot();

        var candidates = Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            state,
            snapshot,
            FighterId.FighterA,
            setup.Settings);

        var candidate = Assert.Single(candidates);
        Assert.Equal(expectedAction, candidate.ActionId.Value);
        Assert.Equal(expectedWeight, candidate.Weight);
        Assert.Equal(0UL, state.Rng.Decision.NextDrawIndex);
        Assert.Equal(0UL, state.Rng.Resolution.NextDrawIndex);
    }

    [Fact]
    public void WP07_AVL_001_WallPinnedRetreatFallsBackToWait()
    {
        var setup = CreateSetup(520, 2_969);
        var snapshot = setup.State.CreateSnapshot();

        var candidates = Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            setup.State,
            snapshot,
            FighterId.FighterA,
            setup.Settings);

        var candidate = Assert.Single(candidates);
        Assert.Equal("sys_wait", candidate.ActionId.Value);
        Assert.Equal(150, candidate.Weight);
    }

    [Theory]
    [InlineData(1_499, "sys_retreat")]
    [InlineData(1_550, "sys_wait")]
    [InlineData(1_601, "sys_approach")]
    public void WP07_AVL_002_OnlyLegalSelectionUsesNoRng(int gap, string expectedAction)
    {
        var setup = CreateSetup(4_000, checked(4_000 + 520 + 430 + gap));
        var snapshot = setup.State.CreateSnapshot();
        var candidates = Wp07SystemActionAvailability.Instance.GetLegalCandidates(
            setup.State,
            snapshot,
            FighterId.FighterB,
            setup.Settings);

        var selection = SystemActionSelector.Select(candidates);

        Assert.Equal(expectedAction, selection.ActionId.Value);
        Assert.Equal(Battle.Contracts.Events.DecisionSelectionMode.OnlyLegalAction, selection.SelectionMode);
        Assert.Equal(0UL, setup.State.Rng.Decision.NextDrawIndex);
        Assert.Equal(0UL, setup.State.Rng.Resolution.NextDrawIndex);
    }

    private static BattleSetup CreateSetup(int startA, int startB)
    {
        var request = EngineTestFixture.CreateRequest(
            allowedActions: EngineTestFixture.ActionIds().Concat(new[]
            {
                SystemActionSelector.ApproachId,
                SystemActionSelector.RetreatId,
            }));
        var config = EngineTestFixture.CreateConfig(changeSettings: settings => settings.Select(property =>
            property.Name switch
            {
                "global.arena.start_position_a" => new ConfigProperty(
                    property.Name,
                    ConfigValue.FromInteger(startA)),
                "global.arena.start_position_b" => new ConfigProperty(
                    property.Name,
                    ConfigValue.FromInteger(startB)),
                _ => property,
            }));
        var result = BattleSetupFactory.Create(request, config);
        Assert.True(result.IsSuccess, string.Join(",", result.Errors.Select(error => error.Code.Value)));
        return result.Setup!;
    }
}
