using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Results;
using Battle.Replay.CanonicalJson;
using Battle.Replay.Verification;

namespace CombatLab.IntegrationTests.Decisions;

public sealed class DecisionWeightedGoldenTests
{
    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DecisionWeightedL1_MatchesTheApprovedGoldenTrace()
    {
        var run = DecisionEngineFixture.Run();

        Assert.Equal(BattleResultStatus.Completed, run.Result.Status);
        Assert.Empty(run.Result.RejectionErrors);
        Assert.Null(run.Result.InvariantFailure);
        Assert.Equal(DecisionEngineFixture.ReplayId, run.Result.ReplayId);
        Assert.Equal(
            "sha256:26c53cf464539e2ebf1eb37f90d73715adb0842e29e6b7a9eeaede8336d49227",
            run.Journal.Start!.Config.ConfigHash.Value);
        Assert.Equal(
            "sha256:eaee293a90e5fc432ab1822965b3f632abc803bd79b23ae401a8fc9fd8a2b021",
            run.Journal.InputDigest!.Value.Value);
        Assert.Equal(
            "sha256:6ed4f34aa845096ee63d125d306fbef64ff469773e14389bfe1152146a007f3f",
            run.Journal.FinalDigest!.Value.Value);

        var events = run.Journal.Events;
        Assert.Equal(
            new[]
            {
                CombatEventType.BattleStarted,
                CombatEventType.DecisionMade,
                CombatEventType.DecisionMade,
                CombatEventType.ActionCommitted,
                CombatEventType.ActionCommitted,
                CombatEventType.AttackPrepared,
                CombatEventType.TimeoutReached,
                CombatEventType.DrawDeclared,
                CombatEventType.BattleEnded,
            },
            events.Select(item => item.Draft.EventType));
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 1, 1, 1 }, events.Select(item => item.Draft.Tick));
        Assert.Equal(
            Enumerable.Range(0, 9).Select(index => (long)index),
            events.Select(item => item.Draft.Sequence));

        AssertDecision(
            events[1].Draft,
            FighterId.FighterA,
            DecisionEngineFixture.RetreatId,
            337,
            0,
            2_879_411_843,
            1_400,
            941,
            750);
        AssertDecision(
            events[2].Draft,
            FighterId.FighterB,
            DecisionEngineFixture.PawJabId,
            1_150,
            1,
            495_049_527,
            461,
            310,
            1_150);

        var commitA = Assert.IsType<ActionCommittedPayload>(events[3].Draft.Payload);
        Assert.Equal(FighterId.FighterA, events[3].Draft.ActorId);
        Assert.Equal(FighterId.FighterB, commitA.TargetFighterId);
        Assert.Equal(CommitDirection.Left, commitA.CommitDirection);
        Assert.Equal(5_540, commitA.TargetPositionAtCommit);
        Assert.Equal((1, 5, 1), (commitA.StartupTicks, commitA.ActiveTicks, commitA.RecoveryTicks));

        var commitB = Assert.IsType<ActionCommittedPayload>(events[4].Draft.Payload);
        Assert.Equal(FighterId.FighterB, events[4].Draft.ActorId);
        Assert.Equal(FighterId.FighterA, commitB.TargetFighterId);
        Assert.Equal(CommitDirection.Left, commitB.CommitDirection);
        Assert.Equal(4_000, commitB.TargetPositionAtCommit);
        Assert.Equal((3, 1, 5), (commitB.StartupTicks, commitB.ActiveTicks, commitB.RecoveryTicks));
        Assert.Equal(FighterState.Retreat, events[4].Draft.Before.Target!.State);

        var telegraph = Assert.IsType<AttackPreparedPayload>(events[5].Draft.Payload);
        Assert.Equal(0, telegraph.TelegraphTick);
        Assert.Equal(new[] { 3 }, telegraph.ImpactTicks);
        Assert.True(telegraph.DirectionLocked);
        Assert.Equal(FighterId.FighterA, telegraph.TargetFighterId);
        Assert.Equal(events[4].Draft.EventId, events[5].Draft.SourceEventId);
        Assert.All(events.Skip(3), item => Assert.Null(item.Draft.Rng));

        var summary = Assert.IsType<BattleSummary>(run.Result.Summary);
        Assert.Equal(BattleOutcome.Draw, summary.Outcome);
        Assert.Equal(BattleEndReason.TimeoutEqualHealthFraction, summary.EndReason);
        Assert.Equal((1, 1, 9), (summary.EndTick, summary.DurationTicks, summary.EventCount));
        Assert.Equal(
            new[] { new EventId("evt-0000000006"), new EventId("evt-0000000007") },
            summary.PivotalEventIds);
        AssertFinalFrame(
            summary.FinalFrames[0],
            FighterId.FighterA,
            DecisionEngineFixture.RetreatId,
            FighterState.Retreat,
            1,
            ActionPhase.Startup,
            4_000);
        AssertFinalFrame(
            summary.FinalFrames[1],
            FighterId.FighterB,
            DecisionEngineFixture.PawJabId,
            FighterState.AttackPrepare,
            3,
            ActionPhase.Startup,
            5_540);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void WP08_CON_011_SummaryOnlyPreservesDecisionDigestAndSummaryWithoutReplayPublication()
    {
        var standard = DecisionEngineFixture.Run();
        var summaryOnly = DecisionEngineFixture.RunSummaryOnly();

        Assert.Equal(BattleResultStatus.Completed, summaryOnly.Result.Status);
        Assert.Equal(standard.Journal.InputDigest, summaryOnly.Journal.InputDigest);
        Assert.Equal(standard.Journal.FinalDigest, summaryOnly.Journal.FinalDigest);
        Assert.Equal(9, summaryOnly.Journal.EventCount);
        Assert.Null(summaryOnly.Result.ReplayId);
        Assert.Null(summaryOnly.Result.InvariantFailure);
        AssertSummaryEquivalent(standard.Result.Summary!, summaryOnly.Result.Summary!);
    }

    [Fact]
    [Trait("Category", "WP08")]
    [Trait("WorkPackage", "WP08")]
    public void DiagnosticOverlayDoesNotAlterCanonicalDecisionEventsOrIntegrity()
    {
        var standard = DecisionEngineFixture.Run();
        var diagnostic = DecisionEngineFixture.Run(JournalProfile.DiagnosticReplay);

        Assert.Equal(standard.Journal.InputDigest, diagnostic.Journal.InputDigest);
        Assert.Equal(standard.Journal.FinalDigest, diagnostic.Journal.FinalDigest);
        Assert.Equal(
            standard.Journal.Events.Select(item => item.CanonicalJson.ToArray()),
            diagnostic.Journal.Events.Select(item => item.CanonicalJson.ToArray()));
        Assert.Equal(2, diagnostic.Journal.DecisionTraces.Count);
        Assert.Equal(
            diagnostic.Journal.DecisionTraces[0].SnapshotDigest,
            diagnostic.Journal.DecisionTraces[1].SnapshotDigest);
        AssertSummaryEquivalent(standard.Result.Summary!, diagnostic.Result.Summary!);

        var replay = DecisionEngineFixture.WriteReplay(diagnostic);
        Assert.Equal(replay, CanonicalJson.Canonicalize(replay));
        var verification = new ReplayVerifier(
            File.ReadAllBytes(DecisionEngineFixture.SchemaPath())).Verify(replay);
        Assert.True(
            verification.IsValid,
            string.Join(Environment.NewLine, verification.Issues.Select(issue =>
                $"{issue.Code}: {issue.Message}")));
    }

    private static void AssertDecision(
        CombatEventDraft draft,
        FighterId actor,
        StableId chosen,
        int chosenWeight,
        ulong index,
        uint raw,
        int result,
        int normalized,
        int dominantMultiplier)
    {
        Assert.Equal(actor, draft.ActorId);
        var payload = Assert.IsType<DecisionMadePayload>(draft.Payload);
        Assert.Equal(chosen, payload.ChosenActionId);
        Assert.Equal(
            new[] { DecisionEngineFixture.PawJabId, DecisionEngineFixture.RetreatId },
            payload.LegalActionIds);
        Assert.Equal(2, payload.CandidateCount);
        Assert.Equal(chosenWeight, payload.ChosenWeight);
        Assert.Equal(1_487, payload.WeightSum);
        Assert.Equal(DecisionSelectionMode.WeightedRng, payload.SelectionMode);
        var dominant = Assert.Single(payload.DominantModifiers);
        Assert.Equal("Tactic", dominant.Code.Value);
        Assert.Equal(dominantMultiplier, dominant.MultiplierFixedPoint);
        Assert.Equal(new[] { "WeightedRng", "Tactic" }, draft.ReasonCodes.Select(code => code.Value));

        Assert.True(draft.Rng.HasValue);
        var rng = draft.Rng.Value;
        Assert.Equal(RngStream.Decision, rng.Stream);
        Assert.Equal(RngOperation.NextInt, rng.Operation);
        Assert.Equal(index, rng.Index);
        Assert.Equal(0, rng.RangeMinimumInclusive);
        Assert.Equal(1_487, rng.RangeMaximumExclusive);
        Assert.Equal(raw, rng.RawValue);
        Assert.Equal(result, rng.Result);
        Assert.Equal(normalized, rng.NormalizedFixedPoint);
    }

    private static void AssertFinalFrame(
        FighterFrame frame,
        FighterId fighterId,
        StableId actionId,
        FighterState state,
        int ticks,
        ActionPhase phase,
        int position)
    {
        Assert.Equal(fighterId, frame.FighterId);
        Assert.Equal(actionId, frame.ActionId);
        Assert.Equal(state, frame.State);
        Assert.Equal(ticks, frame.StateTicksRemaining);
        Assert.Equal(phase, frame.ActionPhase);
        Assert.Equal(position, frame.Position);
        Assert.Equal(frame.MaxHealth, frame.Health);
        Assert.Equal(1_000, frame.Energy);
        Assert.Equal(0, frame.UniqueResource.Value);
    }

    private static void AssertSummaryEquivalent(BattleSummary expected, BattleSummary actual)
    {
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.WinnerFighterId, actual.WinnerFighterId);
        Assert.Equal(expected.EndReason, actual.EndReason);
        Assert.Equal(expected.EndTick, actual.EndTick);
        Assert.Equal(expected.DurationTicks, actual.DurationTicks);
        Assert.Equal(expected.EventCount, actual.EventCount);
        Assert.Equal(expected.PivotalEventIds, actual.PivotalEventIds);
        Assert.Equal(expected.FinalFrames.Count, actual.FinalFrames.Count);
        for (var index = 0; index < expected.FinalFrames.Count; index++)
        {
            var left = expected.FinalFrames[index];
            var right = actual.FinalFrames[index];
            Assert.Equal(left.FighterId, right.FighterId);
            Assert.Equal(left.Position, right.Position);
            Assert.Equal(left.Facing, right.Facing);
            Assert.Equal(left.State, right.State);
            Assert.Equal(left.StateTicksRemaining, right.StateTicksRemaining);
            Assert.Equal(left.ActionId, right.ActionId);
            Assert.Equal(left.ActionPhase, right.ActionPhase);
            Assert.Equal(left.Health, right.Health);
            Assert.Equal(left.MaxHealth, right.MaxHealth);
            Assert.Equal(left.Energy, right.Energy);
            Assert.Equal(left.MaxEnergy, right.MaxEnergy);
            Assert.Equal(left.UniqueResource, right.UniqueResource);
            Assert.Equal(left.Stagger, right.Stagger);
            Assert.Equal(left.StaggerThreshold, right.StaggerThreshold);
            Assert.Equal(left.Effects, right.Effects);
        }
    }
}
