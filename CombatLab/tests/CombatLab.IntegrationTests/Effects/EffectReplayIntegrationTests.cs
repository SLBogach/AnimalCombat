using Battle.Contracts.Ids;
using Battle.Contracts.Events;
using Battle.Core.Random;
using Battle.Replay.Journal;
using Battle.Replay.Verification;
using CombatLab.IntegrationTests.Resolution;
using static CombatLab.IntegrationTests.Effects.EffectEngineFixture;

namespace CombatLab.IntegrationTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class EffectReplayIntegrationTests
{
    [Theory]
    [InlineData("wait"), InlineData("before"), InlineData("after"), InlineData("stacks"), InlineData("replace")]
    [InlineData("knockdown"), InlineData("control_chain"), InlineData("guard_break"), InlineData("uninterruptible")]
    public void RealProducerEffectAndControlTracesPassComposedSchemaSemanticAndIntegrityValidation(string scenario)
    {
        var source = Source(6); ulong seed = 0;
        switch (scenario)
        {
            case "before": case "after":
                Attack(source)["cooldown_ticks"] = 100;
                Effect(source, "effect_trial", duration: 2, boundary: scenario == "before" ? "ExpireBeforeTick" : "ExpireAfterTick");
                Rule(source, "rule_trial", "effect_trial"); break;
            case "stacks":
                Effect(source, "effect_trial", duration: 3, boundary: "ExpireAfterTick", policy: "AddStacks")["stack_cap"] = 3;
                Rule(source, "rule_trial", "effect_trial", "EndOfTick", battleCap: 10); break;
            case "replace":
                Effect(source, "effect_alpha", duration: 10, policy: "Replace", group: "shared"); Rule(source, "rule_alpha", "effect_alpha");
                Effect(source, "effect_beta", duration: 10, policy: "Replace", group: "shared"); Rule(source, "rule_beta", "effect_beta", "EndOfTick"); break;
            case "knockdown":
                source = Source(50); Attack(source, startup: 10, tags: "knockdown|strike")["cooldown_ticks"] = 100; break;
            case "control_chain":
                source = Source(72); var stun = Attack(source); stun["base_stagger"] = 100; stun["base_stun_ticks"] = 8; stun["cooldown_ticks"] = 12; break;
            case "guard_break":
                source = Source(10); var incoming = Attack(source, active: 2, schedule: "0|1", tags: "guard_break|strike");
                incoming["blockable"] = true; incoming["cooldown_ticks"] = 100; incoming["hit_interrupt_strength"] = 0;
                var block = Attack(source, "kangaroo_flying_kick", active: 8, schedule: "", tags: "block");
                block["block_base_chance_fp"] = 500; block["block_reduction_fp"] = 500;
                while (seed < 100000 && Pcg32Stream.CreateResolution(seed).NextInt(0, 1000, RngOperation.ChanceCheck).Result != 999) seed++;
                Assert.True(seed < 100000); break;
            case "uninterruptible":
                source = Source(3); var a = Attack(source, active: 2, schedule: "0|1"); a["base_stagger"] = 100; a["base_stun_ticks"] = 8;
                a["cooldown_ticks"] = 100; a["action_priority"] = 0;
                var b = Attack(source, "kangaroo_flying_kick", active: 2, schedule: "0|1"); b["cooldown_ticks"] = 100;
                b["action_priority"] = 0; b["interrupt_profile"] = "UninterruptibleImpact";
                Entity(source, "fighters", "animal_id", "bear")["initiative"] = 200;
                Entity(source, "fighters", "animal_id", "kangaroo")["initiative"] = 100; break;
        }
        var run = Run(source, seed); run.Completed();
        var bytes = CanonicalReplayArtifactWriter.Write(run.Journal.Canonical,
            new ReplayArtifactMetadata(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), new ExternalId("wp10-test"), true, scenario));
        var result = new ReplayVerifier(File.ReadAllBytes(Wp09ResolutionEngineFixture.SchemaPath())).Verify(bytes);
        Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(x => x.Code + ":" + x.Path + ":" + x.Message)));
        Assert.False(result.HasWarnings); Assert.Equal(run.Events.Length, result.EventCount);
        Assert.Equal(run.Journal.Canonical.InputDigest, result.ComputedInputDigest);
        Assert.Equal(run.Journal.Canonical.FinalDigest, result.ComputedFinalDigest);
    }
}
