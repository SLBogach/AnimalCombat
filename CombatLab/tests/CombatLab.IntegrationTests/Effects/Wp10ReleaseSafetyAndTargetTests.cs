using System.Diagnostics;
using System.Text.Json.Nodes;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Ports;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.Core;
using Battle.Core.Engine;
using Battle.Replay.Journal;
using CombatLab.Runner.Replays;
using static CombatLab.IntegrationTests.Effects.Wp10GoldenAndDeterminismTests;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10ReleaseSafetyAndTargetTests
{
    [Theory, Trait("AcceptanceId", "WP10-DET-004")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualTargetDependenciesReproduceAllNineGoldenArtifacts(bool forbidFileHashCmdlet)
    {
        var shell = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell/v1.0/powershell.exe") : "pwsh";
        var start = new ProcessStartInfo(shell) { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        var script = Path.Combine(Root, "scripts/verify-wp10-target-determinism.ps1");
        if (forbidFileHashCmdlet)
        {
            // Fail any accidental dependency on the cmdlet even when this host
            // happens to have its module installed. Run the complete gate.
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("function global:Get-FileHash { throw 'Get-FileHash must not be used by the target gate.' }; & '" +
                script.Replace("'", "''", StringComparison.Ordinal) + "' -Configuration '" + Configuration + "'");
        }
        else
        {
            foreach (var argument in new[] { "-File", script, "-Configuration", Configuration }) start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000)) { process.Kill(entireProcessTree: true); Assert.Fail("Target determinism probe timed out."); }
        var stdout = await output; var stderr = await error;
        Assert.True(process.ExitCode == 0, stdout + stderr);
        Assert.Contains("WP10 nine goldens match actual netstandard2.1/net10.0 dependencies", stdout, StringComparison.Ordinal);
    }

    [Fact, Trait("AcceptanceId", "WP10-DET-007")]
    public void SymmetricMirrorPreservesMappedGameplayAndEligibleGrabTieKeepsItsResolutionDraw()
    {
        var source = EffectDemoCatalog.CreateSource(Root, "effects-impact-snapshot");
        source["fighters"]!.AsArray().Single(x => x!["animal_id"]!.GetValue<string>() == "bear")!["initiative"] = 100;
        var config = EffectDemoCatalog.Compile(source); var original = EffectDemoCatalog.Request("effects-impact-snapshot", config);
        var a = original.BuildA; var b = new FighterBuildSnapshot(FighterId.FighterB, FighterSide.B, a.AnimalId, null,
            a.SpecialActionIds, a.PassiveId, a.Gear, a.TacticId);
        BattleRequest Request(Battle.Contracts.Config.CompiledBattleConfig input) => new(new ExternalId("wp10-mirror"), ContractVersions.Engine,
            input.Reference.ConfigHash, original.ModeRules, 0, a, b);
        BattleResult Execute(Battle.Contracts.Config.CompiledBattleConfig input, out CanonicalReplayJournal journal)
        { journal = new CanonicalReplayJournal(new ExternalId("wp10-mirror-replay")); return new CombatEngine().Simulate(Request(input), input, journal); }
        var normal = Execute(config, out var first);
        source["settings"]!["global.arena.start_position_a"] = 4250; source["settings"]!["global.arena.start_position_b"] = 6000;
        var mirrored = Execute(EffectDemoCatalog.Compile(source), out var second);
        Assert.Equal(BattleResultStatus.Completed, normal.Status); Assert.Equal(BattleResultStatus.Completed, mirrored.Status);
        var normalFrames = normal.Summary!.FinalFrames.OrderBy(x => x.FighterId).ToArray(); var mirrorFrames = mirrored.Summary!.FinalFrames.OrderBy(x => x.FighterId).ToArray();
        Assert.Equal(normalFrames[0].Health, mirrorFrames[1].Health); Assert.Equal(normalFrames[1].Health, mirrorFrames[0].Health);
        Assert.Equal(10000 - normalFrames[0].Position, mirrorFrames[1].Position); Assert.Equal(10000 - normalFrames[1].Position, mirrorFrames[0].Position);
        Assert.Equal(first.Events.Count(x => x.Draft.Rng is not null), second.Events.Count(x => x.Draft.Rng is not null));
        var grab = source["actions"]!.AsArray().Single(x => x!["action_id"]!.GetValue<string>() == "bear_earthbreaker")!;
        grab["tags"] = "grab"; grab["hit_schedule"] = "grab:0|throw:1"; grab["hit_count"] = 1; grab["active_ticks"] = 2;
        var tie = Execute(EffectDemoCatalog.Compile(source), out var tied);
        Assert.Equal(BattleResultStatus.Completed, tie.Status);
        var draw = Assert.Single(tied.Events, x => x.Draft.Rng?.Stream == RngStream.Resolution);
        Assert.Equal(0UL, draw.Draft.Rng!.Value.Index); Assert.Equal(CombatEventType.ConflictResolved, draw.Draft.EventType);
    }

    [Theory, Trait("AcceptanceId", "WP10-SAFE-008")]
    [InlineData("cap")]
    [InlineData("depth")]
    [InlineData("watchdog")]
    public void FatalPathsCompleteOnceWithReservedInvalidTerminalAndBoundedCausalCapture(string failure)
    {
        var source = EffectDemoCatalog.CreateSource(Root, failure == "watchdog" ? "wait" : "effects-stack-expiry");
        if (failure == "cap") source["settings"]!["global.sim.max_events_per_battle"] = 16;
        if (failure == "watchdog") { source["settings"]!["battle.time_limit_ticks"] = 6; source["settings"]!["global.sim.max_zero_progress_ticks"] = 1; }
        if (failure == "depth")
        {
            source["settings"]!["global.control.max_trigger_depth"] = 1;
            var profile = source["effects"]!.AsArray().Single(x => x!["effect_id"]!.GetValue<string>() == "effect_demo_after")!;
            var owner = "effect_demo_stacks";
            for (var i = 0; i < 3; i++)
            {
                var child = "effect_depth_" + i; var effect = profile.DeepClone(); effect["effect_id"] = child; effect["stack_group"] = child; source["effects"]!.AsArray().Add(effect);
                var rule = source["effect_rules"]!.AsArray().Single(x => x!["rule_id"]!.GetValue<string>() == "rule_demo_after")!.DeepClone();
                rule["rule_id"] = "rule_depth_" + i; rule["owner_kind"] = "Effect"; rule["owner_id"] = owner; rule["trigger"] = "EffectAdded"; rule["effect_id"] = child;
                source["effect_rules"]!.AsArray().Add(rule); owner = child;
            }
        }
        var config = EffectDemoCatalog.Compile(source); var request = EffectDemoCatalog.Request(failure == "watchdog" ? "wait" : "effects-stack-expiry", config);
        var journal = new CountedCapture();
        var result = new CombatEngine(ContractVersions.Engine, failure == "watchdog" ? new FrozenControlObserver() : null).Simulate(request, config, journal);
        Assert.Equal(BattleResultStatus.FailedInvariant, result.Status); Assert.NotNull(result.InvariantFailure);
        Assert.Equal(1, journal.Begins); Assert.Equal(1, journal.Completes); Assert.InRange(journal.Inner.CapturedDrafts.Count, 1, 32);
        Assert.Equal(BattleEndReason.BattleInvalid, journal.Inner.Summary!.EndReason);
        Assert.Single(journal.Inner.CapturedDrafts, x => x.EventType == CombatEventType.BattleEnded);
        Assert.DoesNotContain(journal.Inner.CapturedDrafts, x => x.EventType == CombatEventType.DrawDeclared);
        Assert.All(journal.Inner.Summary.FinalFrames, x => Assert.Empty(x.Effects)); Assert.Null(result.ReplayId);
    }

    private sealed class CountedCapture : ICombatEventJournal
    {
        internal FailureCaptureEventJournal Inner { get; } = new(new ExternalId("wp10-invalid-tail"), 32);
        internal int Begins, Completes;
        public JournalBeginResult Begin(in CombatJournalStart start) { Begins++; return Inner.Begin(in start); }
        public CombatEventIdentity Append(in CombatEventDraft draft) => Inner.Append(in draft);
        public JournalCompletion Complete(in BattleSummary summary) { Completes++; return Inner.Complete(in summary); }
    }
    // Deliberately impossible internal state, not a valid external gameplay/default setting.
    private sealed class FrozenControlObserver : ITickCoordinatorObserver
    {
        public void OnPhase(BattleState state, TickPhase phase)
        { if (phase == TickPhase.Snapshot) { state.FighterA.ApplyHardControl(5); state.FighterB.ApplyHardControl(5); } }
        public void OnDecisionSnapshot(FighterId fighterId, TickSnapshot snapshot) { }
    }
}
