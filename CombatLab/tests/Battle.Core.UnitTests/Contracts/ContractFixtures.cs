using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Contracts;

internal static class ContractFixtures
{
    public static Sha256Digest Digest { get; } = new($"sha256:{new string('0', 64)}");

    public static FighterBuildSnapshot CreateBuild(FighterSide side)
    {
        var fighterId = side == FighterSide.A ? FighterId.FighterA : FighterId.FighterB;
        var suffix = side == FighterSide.A ? "a" : "b";

        return new FighterBuildSnapshot(
            fighterId,
            side,
            new StableId($"animal_{suffix}"),
            new StableId($"build_{suffix}"),
            new[]
            {
                new StableId($"special_{suffix}_one"),
                new StableId($"special_{suffix}_two"),
            },
            new StableId($"passive_{suffix}"),
            new GearSelection(
                new StableId($"gear_{suffix}_offense"),
                new StableId($"gear_{suffix}_defense"),
                new StableId($"gear_{suffix}_utility")),
            new StableId($"tactic_{suffix}"));
    }

    public static BattleRequest CreateRequest() =>
        new(
            ContractVersions.Engine,
            Digest,
            new ArtifactVersion("mode.rules/0.1"),
            42UL,
            CreateBuild(FighterSide.A),
            CreateBuild(FighterSide.B));

    public static FighterFrame CreateFrame(FighterId fighterId) =>
        new(
            fighterId,
            fighterId == FighterId.FighterA ? -10 : 10,
            fighterId == FighterId.FighterA ? Facing.Right : Facing.Left,
            FighterState.Idle,
            null,
            null,
            null,
            100,
            100,
            0,
            100,
            new ResourceFrame(new StableId("unique_resource"), 0, 100),
            0,
            100,
            Array.Empty<EffectFrame>());

    public static BattleSummary CreateSummary() =>
        new(
            BattleOutcome.FighterAWin,
            FighterId.FighterA,
            BattleEndReason.Defeat,
            12,
            13,
            3,
            new[] { EventId.FromSequence(1) },
            new[]
            {
                CreateFrame(FighterId.FighterA),
                CreateFrame(FighterId.FighterB),
            });
}
