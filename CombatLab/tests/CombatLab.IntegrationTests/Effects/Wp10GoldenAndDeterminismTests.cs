using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Replay.Journal;
using Battle.Replay.Verification;
using CombatLab.IntegrationTests.Resolution;
using CombatLab.Runner.Replays;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10GoldenAndDeterminismTests
{
    internal static string Root => Wp09ResolutionEngineFixture.Root();
    internal static IEnumerable<CombatEventDraft> Events(EffectDemoRun run) => run.Journal.Events.Select(x => x.Draft);
    internal static EffectDemoRun Run(string name, JournalProfile profile = JournalProfile.StandardReplay, JsonObject? source = null)
    {
        var run = EffectDemoCatalog.Run(Root, name, profile, source: source);
        Assert.Equal(BattleResultStatus.Completed, run.Result.Status); return run;
    }
    internal static void Golden(string name, EffectDemoRun run)
    {
        var bytes = EffectDemoCatalog.Write(name, run);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Root, "fixtures/replay/v0.1", EffectDemoCatalog.FixtureName(name))), bytes);
        var verification = new ReplayVerifier(File.ReadAllBytes(Wp09ResolutionEngineFixture.SchemaPath())).Verify(bytes);
        Assert.True(verification.IsValid, string.Join(";", verification.Issues.Select(x => x.Code + ":" + x.Message))); Assert.False(verification.HasWarnings);
    }

    [Fact, Trait("AcceptanceId", "WP10-GOLD-001")]
    public void StackExpiryGoldenPinsCountsRemainingBothBoundariesAndRestoration()
    {
        var run = Run("effects-stack-expiry"); Golden("effects-stack-expiry", run);
        var added = Events(run).Where(x => x.ActorId == FighterId.FighterA && x.EffectId?.Value == "effect_demo_stacks" && x.Payload is EffectAddedPayload).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 3 }, added.Select(x => ((EffectAddedPayload)x.Payload).StacksAfter));
        Assert.Equal(new[] { 0, 1, 2, 3 }, added.Select(x => x.Tick));
        var before = Events(run).Single(x => x.ActorId == FighterId.FighterA && x.EffectId?.Value == "effect_demo_stacks" && x.Payload is EffectRemovedPayload);
        Assert.Equal(6, before.Tick); Assert.Equal(EffectRemoveReason.ExpiredBeforeTick, ((EffectRemovedPayload)before.Payload).RemoveReason);
        Assert.Equal(added[^1].EventId, before.SourceEventId); Assert.Equal(0, before.Before.Actor!.Effects.Single(x => x.EffectId.Value == "effect_demo_stacks").TicksRemaining);
        var after = Events(run).Single(x => x.ActorId == FighterId.FighterA && x.EffectId?.Value == "effect_demo_after" && x.Payload is EffectRemovedPayload);
        Assert.Equal(2, after.Tick); Assert.Equal(EffectRemoveReason.ExpiredAfterTick, ((EffectRemovedPayload)after.Payload).RemoveReason);
        Assert.Equal(1, after.Before.Actor!.Effects.Single(x => x.EffectId.Value == "effect_demo_after").TicksRemaining);
        Assert.All(run.Result.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
    }

    [Fact, Trait("AcceptanceId", "WP10-GOLD-002")]
    public void ImpactSnapshotGoldenHasOldTradeArmorThenUpdatedNextGroupDamage()
    {
        var run = Run("effects-impact-snapshot"); Golden("effects-impact-snapshot", run);
        var damage = Events(run).Where(x => x.Payload is DamageAppliedPayload).ToArray();
        Assert.Equal(new[] { 66, 66, 50, 50 }, damage.Select(x => ((DamageAppliedPayload)x.Payload).Breakdown.Final));
        Assert.Equal(damage[0].ResolutionGroupId, damage[1].ResolutionGroupId);
        var added = Events(run).Where(x => x.EffectId?.Value == "effect_demo_armor" && x.Payload is EffectAddedPayload).ToArray();
        Assert.Equal(2, added.Length); Assert.All(added, x => { Assert.True(x.Sequence > damage[1].Sequence); Assert.Null(x.Rng); });
        Assert.Equal(new[] { 0, 0, 1, 1 }, damage.Select(x => x.Tick));
    }

    [Fact, Trait("AcceptanceId", "WP10-GOLD-003")]
    public void ControlChainGoldenHasOneFatiguePerExitCrossingOnlyImmunityAndBoundedRecontrol()
    {
        var run = Run("effects-control-chain"); Golden("effects-control-chain", run);
        var fatigue = Events(run).Where(x => x.EffectId?.Value == "effect_control_fatigue" && x.Payload is EffectAddedPayload).ToArray();
        Assert.Equal(new[] { 1, 2, 3, 3 }, fatigue.Take(4).Select(x => ((EffectAddedPayload)x.Payload).StacksAfter));
        var immunity = Events(run).Single(x => x.EffectId?.Value == "effect_control_immunity" && x.Payload is EffectAddedPayload);
        Assert.Equal(fatigue[2].EventId, immunity.SourceEventId);
        var removed = Events(run).Single(x => x.EffectId?.Value == "effect_control_immunity" && x.Payload is EffectRemovedPayload);
        Assert.Equal(immunity.Tick + 24, removed.Tick); Assert.True(fatigue[3].Tick > removed.Tick);
        Assert.Contains(Events(run), x => x.Tick > immunity.Tick && x.Tick < removed.Tick && x.Payload is StateChangedPayload { ImmunityResult: ImmunityResult.Prevented });
        Assert.Equal(4, fatigue.Length);
    }

    [Fact, Trait("AcceptanceId", "WP10-GOLD-004")]
    public void KnockdownGoldenPinsThrowIllegalHitGroundHitStagesWakeupLockoutAndCleanup()
    {
        var run = Run("effects-knockdown"); Golden("effects-knockdown", run);
        Assert.Contains(Events(run), x => x.Tick == 9 && x.EventType == CombatEventType.GrabStarted);
        Assert.Contains(Events(run), x => x.Tick == 10 && x.Payload is GrabEndedPayload { EndReason: GrabEndReason.Throw });
        Assert.Contains(Events(run), x => x.Tick == 11 && x.Payload is AttackMissedPayload { MissReason: AttackMissReason.InvalidTarget });
        Assert.Contains(Events(run), x => x.Tick == 12 && x.ActionId?.Value == "bear_paw_jab" && x.Payload is DamageAppliedPayload);
        var stages = Events(run).Where(x => x.Payload is StateChangedPayload { OldState: FighterState.KnockedDown }).ToArray();
        Assert.Equal(new[] { 12, 18, 21 }, stages.Select(x => x.Tick));
        Assert.DoesNotContain(Events(run), x => x.ActorId == FighterId.FighterB && x.EventType == CombatEventType.DecisionMade && x.Tick is >= 10 and < 21);
        Assert.Contains(Events(run), x => x.Tick == 29 && x.EffectId?.Value == "effect_grab_lockout" && x.Payload is EffectRemovedPayload);
        Assert.Contains(Events(run), x => x.Tick == 45 && x.EffectId?.Value == "effect_wakeup_immunity" && x.Payload is EffectRemovedPayload);
        Assert.All(run.Result.Summary!.FinalFrames, x => Assert.Empty(x.Effects));
    }

    [Fact, Trait("AcceptanceId", "WP10-DET-001")]
    public void FourEffectGoldensRepeatOneHundredTimesWithIdenticalWholeArtifacts()
    {
        foreach (var name in EffectDemoCatalog.Names.Where(x => x.StartsWith("effects-", StringComparison.Ordinal)))
        {
            var expected = EffectDemoCatalog.Write(name, Run(name));
            for (var repeat = 0; repeat < 100; repeat++) Assert.Equal(expected, EffectDemoCatalog.Write(name, Run(name)));
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-DET-002")]
    public void TenFreshProcessesPerEffectGoldenProduceIdenticalFixedMetadataBytes()
    {
        var directory = Directory.CreateTempSubdirectory("combatlab-wp10-process-");
        try
        {
            foreach (var name in EffectDemoCatalog.Names.Where(x => x.StartsWith("effects-", StringComparison.Ordinal)))
                for (var repeat = 0; repeat < 10; repeat++)
                {
                    var output = Path.Combine(directory.FullName, name + repeat + ".json");
                    Dotnet("src/CombatLab.Cli/bin/" + Configuration + "/net10.0/CombatLab.Cli.dll", "run-demo", name, "--output", output);
                    Assert.Equal(File.ReadAllBytes(Path.Combine(Root, "fixtures/replay/v0.1", EffectDemoCatalog.FixtureName(name))), File.ReadAllBytes(output));
                }
        }
        finally { directory.Delete(true); }
    }

    [Fact, Trait("AcceptanceId", "WP10-DET-003")]
    public void JournalProfilesKeepCanonicalChainAndSummaryWithoutDiagnosticGameplayDrift()
    {
        foreach (var name in EffectDemoCatalog.Names)
        {
            var standard = Run(name); var diagnostic = Run(name, JournalProfile.DiagnosticReplay);
            var summaryJournal = new SummaryOnlyEventJournal(new ExternalId("replay-wp10-" + name));
            var summary = new CombatEngine().Simulate(standard.Request, standard.Config, summaryJournal);
            Assert.Equal(BattleResultStatus.Completed, summary.Status);
            Assert.Equal(standard.Journal.InputDigest, diagnostic.Journal.InputDigest); Assert.Equal(standard.Journal.FinalDigest, diagnostic.Journal.FinalDigest);
            Assert.Equal(standard.Journal.FinalDigest, summaryJournal.FinalDigest);
            Assert.Equal(standard.Result.Summary!.Outcome, summary.Summary!.Outcome);
            Assert.Equal(standard.Result.Summary.EndTick, summary.Summary.EndTick);
            Assert.Equal(JsonSerializer.Serialize(standard.Result.Summary.FinalFrames), JsonSerializer.Serialize(summary.Summary.FinalFrames));
            Assert.Equal(standard.Result.Metrics, summary.Metrics);
            Assert.NotEmpty(diagnostic.Journal.DecisionTraces); Assert.Empty(standard.Journal.DecisionTraces); Assert.Null(summary.ReplayId);
            foreach (var stream in Enum.GetValues<RngStream>())
                Assert.Equal(Events(standard).Count(x => x.Rng?.Stream == stream), summaryJournal.RngDrawCounts[stream]);
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-DET-005"), Trait("AcceptanceId", "WP10-REG-004")]
    public void EachCiConfigurationAndOsMustMatchAllNineCommittedGoldens()
    {
        Assert.Contains(Configuration, new[] { "Debug", "Release" });
        foreach (var name in EffectDemoCatalog.Names) Golden(name, Run(name));
        Assert.Equal(BattleEndReason.Defeat, Run("resolution-basic").Result.Summary!.EndReason);
        Assert.Equal(BattleEndReason.DoubleKO, Run("resolution-double-ko").Result.Summary!.EndReason);
        var wall = Run("resolution-wall-grab"); Assert.Contains(Events(wall), x => x.EventType == CombatEventType.WallImpact);
        Assert.Contains(Events(wall), x => x.Payload is GrabEndedPayload);
    }

    [Fact, Trait("AcceptanceId", "WP10-DET-006")]
    public void CulturesAndCatalogRuleInsertionPermutationsCannotChangeTheArtifacts()
    {
        var saved = CultureInfo.CurrentCulture; var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var culture in new[] { "en-US", "ru-RU", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                foreach (var name in EffectDemoCatalog.Names)
                {
                    var source = EffectDemoCatalog.CreateSource(Root, name);
                    foreach (var property in source.ToArray())
                        if (property.Value is JsonArray array) source[property.Key] = new JsonArray(array.Reverse().Select(x => x!.DeepClone()).ToArray());
                    Golden(name, Run(name, source: source));
                }
            }
        }
        finally { CultureInfo.CurrentCulture = saved; CultureInfo.CurrentUICulture = savedUi; }
    }

    [Fact, Trait("AcceptanceId", "WP10-REG-003")]
    public void NoEffectWaitHasAnExactTimeoutBoundaryAndDefeatDoubleKoPrecedence()
    {
        var run = Run("wait"); Assert.Equal(1, run.Result.Summary!.EndTick);
        Assert.Equal(BattleEndReason.TimeoutEqualHealthFraction, run.Result.Summary.EndReason);
        Assert.DoesNotContain(Events(run), x => x.Tick >= 1 && x.EventType == CombatEventType.DecisionMade);
        Assert.DoesNotContain(Events(run), x => x.EventType is CombatEventType.EffectAdded or CombatEventType.EffectRemoved);
        foreach (var name in new[] { "resolution-basic", "resolution-double-ko" })
            Assert.DoesNotContain(Events(Run(name)), x => x.EventType == CombatEventType.TimeoutReached);
    }

    internal static string Configuration => new DirectoryInfo(Path.GetDirectoryName(typeof(Wp10GoldenAndDeterminismTests).Assembly.Location)!).Parent!.Name;
    internal static void Dotnet(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { process.Kill(entireProcessTree: true); Assert.Fail("WP10 child process timed out."); }
        Assert.True(process.ExitCode == 0, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
}
