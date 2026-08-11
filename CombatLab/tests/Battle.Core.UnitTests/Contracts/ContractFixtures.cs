using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
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
            new ExternalId("battle-contract-0001"),
            ContractVersions.Engine,
            Digest,
            CreateModeRules(),
            42UL,
            CreateBuild(FighterSide.A),
            CreateBuild(FighterSide.B));

    public static ModeRulesSnapshot CreateModeRules() =>
        new(
            new StableId("mode_open_v01"),
            ContractVersions.ModeRules,
            NormalizationMode.None,
            new[] { new StableId("animal_b"), new StableId("animal_a") },
            new[]
            {
                new StableId("special_b_two"),
                new StableId("special_a_two"),
                new StableId("sys_wait"),
                new StableId("special_b_one"),
                new StableId("special_a_one"),
            },
            new[] { new StableId("passive_b"), new StableId("passive_a") },
            new[]
            {
                new StableId("gear_b_utility"),
                new StableId("gear_a_utility"),
                new StableId("gear_b_offense"),
                new StableId("gear_a_offense"),
                new StableId("gear_b_defense"),
                new StableId("gear_a_defense"),
            },
            new[] { new StableId("tactic_b"), new StableId("tactic_a") });

    public static CombatJournalStart CreateJournalStart() =>
        new(
            new ExternalId("battle-contract-0001"),
            ContractVersions.Engine,
            ContractVersions.Rng,
            ContractVersions.Ordering,
            new ConfigReference(
                ContractVersions.BalanceSchema,
                new ArtifactVersion("v0.1"),
                Digest),
            new BattleInputSnapshot(
                42UL,
                new StableId("mode_open_v01"),
                new ArenaSnapshot(new StableId("arena"), -100, 100, -10, 10)),
            new CombatJournalFighterStart(
                CreateBuild(FighterSide.A),
                CreateFrame(FighterId.FighterA)),
            new CombatJournalFighterStart(
                CreateBuild(FighterSide.B),
                CreateFrame(FighterId.FighterB)));

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
