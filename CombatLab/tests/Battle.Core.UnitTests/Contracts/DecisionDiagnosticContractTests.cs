using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Contracts;

public sealed class DecisionDiagnosticContractTests
{
    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_010_DiagnosticDtosDefensivelyCopyCanonicalCollections()
    {
        var cooldowns = new List<DecisionCooldownSnapshot>
        {
            new(new StableId("action_a"), 1),
            new(new StableId("action_b"), 2),
        };
        var debts = new List<DecisionOpportunitySnapshot>
        {
            new(new StableId("special_a_one"), 10),
            new(new StableId("special_a_two"), 20),
        };
        var fighterA = CreateFighterSnapshot(
            FighterId.FighterA,
            cooldowns,
            debts);

        var modifiers = CreateModifiers().ToList();
        var chosen = new DecisionCandidateTrace(
            new StableId("action_a"),
            legal: true,
            firstRejectionCode: null,
            baseWeight: 1_000,
            modifiers,
            finalWeight: 750);
        var rejected = new DecisionCandidateTrace(
            new StableId("action_b"),
            legal: false,
            new ReasonCode("CooldownActive"),
            baseWeight: 900,
            Array.Empty<ModifierTrace>(),
            finalWeight: 0);
        var candidates = new List<DecisionCandidateTrace> { chosen, rejected };
        var trace = new DecisionTrace(
            new DecisionId("dec-fighter_a-000001"),
            tick: 3,
            sequence: 1,
            FighterId.FighterA,
            ContractFixtures.Digest,
            candidates);

        var initiative = new List<FighterId> { FighterId.FighterB, FighterId.FighterA };
        var fighters = new List<DecisionFighterSnapshot>
        {
            fighterA,
            CreateFighterSnapshot(FighterId.FighterB),
        };
        var projection = new DecisionBatchSnapshotProjection(
            new ExternalId("battle-contract-0001"),
            ContractVersions.Engine,
            masterSeed: 42,
            ContractFixtures.Digest,
            ContractFixtures.CreateModeRules(),
            tick: 3,
            initiative,
            decisionNextIndex: 7,
            fighters);

        cooldowns.Clear();
        debts.Clear();
        modifiers.Clear();
        candidates.Clear();
        initiative.Reverse();
        fighters.Clear();

        Assert.Equal(new[] { "action_a", "action_b" }, fighterA.Cooldowns.Select(item => item.ActionId.Value));
        Assert.Equal(
            new[] { "special_a_one", "special_a_two" },
            fighterA.OpportunityDebts.Select(item => item.ActionId.Value));
        Assert.Equal(6, chosen.Modifiers.Count);
        Assert.Equal(new[] { "action_a", "action_b" }, trace.Candidates.Select(item => item.ActionId.Value));
        Assert.Equal(new[] { FighterId.FighterB, FighterId.FighterA }, projection.InitiativeOrder);
        Assert.Equal(new[] { FighterId.FighterA, FighterId.FighterB }, projection.Fighters.Select(item => item.PublicFrame.FighterId));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_009_DiagnosticDtosRejectNonCanonicalOrderAndInconsistentShapes()
    {
        Assert.Throws<ArgumentException>(
            () => CreateFighterSnapshot(
                FighterId.FighterA,
                new[]
                {
                    new DecisionCooldownSnapshot(new StableId("action_b"), 1),
                    new DecisionCooldownSnapshot(new StableId("action_a"), 1),
                }));
        Assert.Throws<ArgumentException>(
            () => CreateFighterSnapshot(
                FighterId.FighterA,
                opportunityDebts: new[]
                {
                    new DecisionOpportunitySnapshot(new StableId("special_a_one"), 1),
                    new DecisionOpportunitySnapshot(new StableId("special_a_one"), 2),
                }));
        Assert.Throws<ArgumentException>(
            () => CreateFighterSnapshot(
                FighterId.FighterA,
                opportunityDebts: Array.Empty<DecisionOpportunitySnapshot>()));
        Assert.Throws<ArgumentException>(
            () => new DecisionFighterSnapshot(
                ContractFixtures.CreateFrame(FighterId.FighterA),
                ContractFixtures.CreateBuild(FighterSide.A),
                Array.Empty<DecisionCooldownSnapshot>(),
                lastActionId: new StableId("action_a"),
                lastActionCategory: null,
                sameActionStreak: 0,
                sameCategoryStreak: 0,
                Array.Empty<DecisionOpportunitySnapshot>(),
                observableActionId: null,
                observableCommitTick: null,
                emergency: false));

        var wrongStages = CreateModifiers().ToArray();
        wrongStages[0] = new ModifierTrace(new ReasonCode("Situation"), 1_000);
        Assert.Throws<ArgumentException>(
            () => new DecisionCandidateTrace(
                new StableId("action_a"),
                legal: true,
                firstRejectionCode: null,
                baseWeight: 1,
                wrongStages,
                finalWeight: 1));
        Assert.Throws<ArgumentException>(
            () => new DecisionCandidateTrace(
                new StableId("action_a"),
                legal: false,
                firstRejectionCode: null,
                baseWeight: 1,
                Array.Empty<ModifierTrace>(),
                finalWeight: 0));

        var actionA = CreateLegalCandidate("action_a");
        var actionB = CreateLegalCandidate("action_b");
        Assert.Throws<ArgumentException>(
            () => new DecisionTrace(
                new DecisionId("dec-fighter_a-000001"),
                0,
                1,
                FighterId.FighterA,
                ContractFixtures.Digest,
                new[] { actionB, actionA }));
        Assert.Throws<ArgumentException>(
            () => new DecisionTrace(
                new DecisionId("dec-fighter_a-000001"),
                0,
                1,
                FighterId.FighterA,
                ContractFixtures.Digest,
                Array.Empty<DecisionCandidateTrace>()));
        Assert.Throws<ArgumentException>(
            () => new DecisionTrace(
                new DecisionId("dec-fighter_a-000001"),
                0,
                1,
                FighterId.FighterA,
                ContractFixtures.Digest,
                new DecisionCandidateTrace[] { null! }));

        Assert.Throws<ArgumentException>(
            () => new DecisionBatchSnapshotProjection(
                new ExternalId("battle-contract-0001"),
                ContractVersions.Engine,
                42,
                ContractFixtures.Digest,
                ContractFixtures.CreateModeRules(),
                0,
                new[] { FighterId.FighterA, FighterId.FighterA },
                0,
                new[]
                {
                    CreateFighterSnapshot(FighterId.FighterA),
                    CreateFighterSnapshot(FighterId.FighterB),
                }));
        Assert.Throws<ArgumentException>(
            () => new DecisionBatchSnapshotProjection(
                new ExternalId("battle-contract-0001"),
                ContractVersions.Engine,
                42,
                ContractFixtures.Digest,
                ContractFixtures.CreateModeRules(),
                0,
                new[] { FighterId.FighterA, FighterId.FighterB },
                0,
                new[]
                {
                    CreateFighterSnapshot(FighterId.FighterB),
                    CreateFighterSnapshot(FighterId.FighterA),
                }));
        Assert.Throws<ArgumentException>(
            () => new DecisionBatchSnapshotProjection(
                new ExternalId("battle-contract-0001"),
                ContractVersions.Engine,
                42,
                ContractFixtures.Digest,
                ContractFixtures.CreateModeRules(),
                0,
                new[] { FighterId.FighterA, FighterId.FighterB },
                0,
                new DecisionFighterSnapshot[]
                {
                    null!,
                    CreateFighterSnapshot(FighterId.FighterB),
                }));
    }

    [Fact]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_010_DiagnosticScalarBoundsAreEnforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DecisionCooldownSnapshot(new StableId("action_a"), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DecisionOpportunitySnapshot(new StableId("action_a"), -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DecisionBatchSnapshotProjection(
                new ExternalId("battle-contract-0001"),
                ContractVersions.Engine,
                42,
                ContractFixtures.Digest,
                ContractFixtures.CreateModeRules(),
                -1,
                new[] { FighterId.FighterA, FighterId.FighterB },
                0,
                new[]
                {
                    CreateFighterSnapshot(FighterId.FighterA),
                    CreateFighterSnapshot(FighterId.FighterB),
                }));
    }

    private static DecisionFighterSnapshot CreateFighterSnapshot(
        FighterId fighterId,
        IEnumerable<DecisionCooldownSnapshot>? cooldowns = null,
        IEnumerable<DecisionOpportunitySnapshot>? opportunityDebts = null)
    {
        var side = fighterId == FighterId.FighterA
            ? FighterSide.A
            : FighterSide.B;
        var suffix = fighterId == FighterId.FighterA ? "a" : "b";
        return new DecisionFighterSnapshot(
            ContractFixtures.CreateFrame(fighterId),
            ContractFixtures.CreateBuild(side),
            cooldowns ?? Array.Empty<DecisionCooldownSnapshot>(),
            lastActionId: new StableId("action_" + suffix),
            lastActionCategory: "Basic",
            sameActionStreak: 1,
            sameCategoryStreak: 1,
            opportunityDebts ?? new[]
            {
                new DecisionOpportunitySnapshot(new StableId("special_" + suffix + "_one"), 0),
                new DecisionOpportunitySnapshot(new StableId("special_" + suffix + "_two"), 0),
            },
            observableActionId: new StableId("action_" + suffix),
            observableCommitTick: 2,
            emergency: fighterId == FighterId.FighterB);
    }

    private static DecisionCandidateTrace CreateLegalCandidate(string actionId) =>
        new(
            new StableId(actionId),
            legal: true,
            firstRejectionCode: null,
            baseWeight: 1_000,
            CreateModifiers(),
            finalWeight: 1_000);

    private static IEnumerable<ModifierTrace> CreateModifiers()
    {
        yield return new ModifierTrace(new ReasonCode("Tactic"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Situation"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Synergy"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Counter"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Variety"), 1_000);
        yield return new ModifierTrace(new ReasonCode("Opportunity"), 1_000);
    }
}
