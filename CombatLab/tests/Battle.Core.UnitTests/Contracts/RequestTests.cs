using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;

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
        Assert.Equal(new ExternalId("battle-contract-0001"), request.BattleId);
        Assert.Equal(new StableId("mode_open_v01"), request.ModeRules.Id);
        Assert.Equal(42UL, request.MasterSeed);
    }

    [Fact]
    public void ModeRulesSnapshot_DefensivelyCopiesAndSortsExplicitAllowlists()
    {
        var animals = new List<StableId>
        {
            new("zebra"),
            new("bear"),
        };
        var snapshot = new ModeRulesSnapshot(
            new StableId("mode_open_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            animals,
            new[] { new StableId("sys_wait") },
            new[] { new StableId("passive") },
            new[] { new StableId("gear") },
            new[] { new StableId("tactic") });

        animals[0] = new StableId("changed");

        Assert.Equal(
            new[] { new StableId("bear"), new StableId("zebra") },
            snapshot.AllowedAnimalIds);
        Assert.Equal(ContractVersions.ModeRules, snapshot.Version);
        Assert.Equal(NormalizationMode.None, snapshot.NormalizationMode);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("all")]
    [InlineData("empty")]
    public void ModeRulesSnapshot_RejectsNonExplicitAllowlist(string caseName)
    {
        var animals = caseName switch
        {
            "duplicate" => new[] { new StableId("bear"), new StableId("bear") },
            "all" => new[] { new StableId("all") },
            _ => Array.Empty<StableId>(),
        };

        Assert.Throws<ArgumentException>(
            () => new ModeRulesSnapshot(
                new StableId("mode_open_v01"),
                ContractVersions.ModeRules,
                NormalizationMode.None,
                animals,
                new[] { new StableId("sys_wait") },
                new[] { new StableId("passive") },
                new[] { new StableId("gear") },
                new[] { new StableId("tactic") }));
    }

    [Fact]
    public void ModeRulesSnapshot_ExposesFutureNormalizationWithoutApplyingIt()
    {
        var snapshot = new ModeRulesSnapshot(
            new StableId("mode_normalized_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.NormalizedRating,
            new[] { new StableId("bear") },
            new[] { new StableId("sys_wait") },
            new[] { new StableId("passive") },
            new[] { new StableId("gear") },
            new[] { new StableId("tactic") });

        Assert.Equal(NormalizationMode.NormalizedRating, snapshot.NormalizationMode);
    }
}
