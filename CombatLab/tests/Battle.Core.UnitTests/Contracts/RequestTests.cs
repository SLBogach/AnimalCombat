using Battle.Contracts.Ids;
using Battle.Contracts.Requests;

namespace Battle.Core.UnitTests.Contracts;

public sealed class RequestTests
{
    [Fact]
    public void FighterBuildSnapshot_DefensivelyCopiesSpecialActions()
    {
        var specials = new List<StableId>
        {
            new("special_one"),
            new("special_two"),
        };

        var build = new FighterBuildSnapshot(
            FighterId.FighterA,
            FighterSide.A,
            new StableId("bear"),
            null,
            specials,
            new StableId("passive"),
            new GearSelection(
                new StableId("offense"),
                new StableId("defense"),
                new StableId("utility")),
            new StableId("tactic"));

        specials[0] = new StableId("changed");

        Assert.Equal(new StableId("special_one"), build.SpecialActionIds[0]);
        Assert.IsAssignableFrom<IReadOnlyList<StableId>>(build.SpecialActionIds);
    }

    [Fact]
    public void FighterBuildSnapshot_RejectsDuplicateSpecials()
    {
        var duplicate = new StableId("same_special");

        Assert.Throws<ArgumentException>(
            () => new FighterBuildSnapshot(
                FighterId.FighterA,
                FighterSide.A,
                new StableId("bear"),
                null,
                new[] { duplicate, duplicate },
                new StableId("passive"),
                new GearSelection(
                    new StableId("offense"),
                    new StableId("defense"),
                    new StableId("utility")),
                new StableId("tactic")));
    }

    [Fact]
    public void FighterBuildSnapshot_RejectsUnknownEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FighterBuildSnapshot(
                (FighterId)42,
                FighterSide.B,
                new StableId("bear"),
                null,
                new[] { new StableId("special_one"), new StableId("special_two") },
                new StableId("passive"),
                new GearSelection(
                    new StableId("offense"),
                    new StableId("defense"),
                    new StableId("utility")),
                new StableId("tactic")));
    }

    [Fact]
    public void BattleRequest_PreservesTwoExplicitBuildSlots()
    {
        var request = ContractFixtures.CreateRequest();

        Assert.Equal(FighterSide.A, request.BuildA.Side);
        Assert.Equal(FighterSide.B, request.BuildB.Side);
        Assert.Equal(42UL, request.MasterSeed);
    }
}
