using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;

namespace Battle.Core.UnitTests.Contracts;

public sealed class ResultAndReplayContractTests
{
    [Fact]
    public void BattleResult_UsesThreeDistinctTerminalStatuses()
    {
        var completed = BattleResult.Completed(
            ContractFixtures.CreateSummary(),
            ContractFixtures.Digest);
        var rejected = BattleResult.Rejected(
            new[]
            {
                new BattleRejectionError(
                    new ReasonCode("UnknownStableId"),
                    "/fighters/0/animal_id",
                    new ExternalId("unknown_animal"),
                    new StableId("battle_rejected_unknown_stable_id"),
                    Array.Empty<BattleRejectionDetail>()),
            });
        var failed = BattleResult.FailedInvariant(
            new BattleInvariantFailure(
                new ReasonCode("TriggerCapExceeded"),
                "Effects",
                7,
                "Trigger cap exceeded."));

        Assert.Equal(BattleResultStatus.Completed, completed.Status);
        Assert.Equal(BattleResultStatus.Rejected, rejected.Status);
        Assert.Equal(BattleResultStatus.FailedInvariant, failed.Status);
        Assert.NotNull(completed.Summary);
        Assert.Single(rejected.RejectionErrors);
        Assert.NotNull(failed.InvariantFailure);
    }

    [Fact]
    public void JournalProfile_ContainsOnlyTechnicalDesignProfiles()
    {
        Assert.Equal(
            new[]
            {
                "StandardReplay",
                "DiagnosticReplay",
                "SummaryOnly",
                "FailureCapture",
            },
            Enum.GetNames<JournalProfile>());
    }

    [Fact]
    public void RejectionError_DefensivelyCopiesTypedDetails()
    {
        var details = new List<BattleRejectionDetail>
        {
            new(
                "expected_namespace",
                BattleRejectionDetailValue.FromString("action")),
        };
        var error = new BattleRejectionError(
            new ReasonCode("UnknownStableId"),
            "/fighters/1/special_action_ids/0",
            new ExternalId("kangaroo_unknown_special"),
            new StableId("battle_rejected_unknown_stable_id"),
            details);

        details.Clear();

        var detail = Assert.Single(error.Details);
        Assert.Equal("expected_namespace", detail.Key);
        Assert.Equal(BattleRejectionDetailKind.String, detail.Value.Kind);
        Assert.Equal("action", detail.Value.StringValue);
    }

    [Fact]
    public void BattleCase_HasValueSemantics()
    {
        var request = ContractFixtures.CreateRequest();
        var first = new BattleCase("case-0001", request, JournalProfile.StandardReplay);
        var second = new BattleCase("case-0001", request, JournalProfile.StandardReplay);

        Assert.Equal(first, second);
        Assert.Equal("case-0001", first.CaseId);
    }
}
