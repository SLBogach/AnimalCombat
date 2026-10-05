using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Battle.Config.Compiler;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Results;
using Battle.Contracts.Versions;
using Battle.ConformanceTests.Replay;
using Battle.Replay.Journal;

namespace Battle.ConformanceTests.Effects;

[Trait("WorkPackage", "WP10")]
public sealed class Wp10ReleaseConformanceTests
{
    private static string Root => RepositoryLocator.FindCombatLabRoot();
    private static string Configuration => new DirectoryInfo(Path.GetDirectoryName(typeof(Wp10ReleaseConformanceTests).Assembly.Location)!).Parent!.Name;

    [Fact, Trait("AcceptanceId", "WP10-BASE-001"), Trait("AcceptanceId", "WP10-BASE-003")]
    public void CurrentVersionContractsAndCoreDependencyGraphMatchTheApprovedDelta()
    {
        Assert.Equal("battle.core/0.5.0", ContractVersions.Engine.ToString()); Assert.Equal("combat.balance/0.2", ContractVersions.BalanceSchema.ToString());
        Assert.Equal("battle.core/0.4.0", ContractVersions.HistoricalEngine.ToString()); Assert.Equal("combat.balance/0.1", ContractVersions.HistoricalBalanceSchema.ToString());
        Assert.Equal("combat.event/0.1", ContractVersions.Event.ToString()); Assert.Equal("combat.replay/0.1", ContractVersions.Replay.ToString());
        Assert.Equal("pcg32/1", ContractVersions.Rng.ToString()); Assert.Equal("tick-pipeline/1", ContractVersions.Ordering.ToString());
        Assert.Equal("mode.rules/0.1", ContractVersions.ModeRules.ToString()); Assert.Equal("combat.rejection/0.1", ContractVersions.Rejection.ToString());
        Assert.Equal("combat.presentation/0.1", ContractVersions.Presentation.ToString());
        var project = XDocument.Load(Path.Combine(Root, "src/Battle.Core/Battle.Core.csproj"));
        Assert.Equal(new[] { "Battle.Contracts" }, project.Descendants("ProjectReference").Select(x => Path.GetFileNameWithoutExtension(x.Attribute("Include")!.Value.Replace('\\', '/'))));
        Assert.Empty(project.Descendants("PackageReference"));
    }

    [Fact, Trait("AcceptanceId", "WP10-BASE-004")]
    public void PublicEffectFramesHaveBoundedStacksCountdownCapacityAndOrdinalMembership()
    {
        foreach (var boundary in new[] { EffectExpiryBoundary.ExpireBeforeTick, EffectExpiryBoundary.ExpireAfterTick })
        {
            Assert.Equal(255, new EffectFrame(new StableId("effect_test"), 255, 0, boundary).Stacks);
            foreach (var stacks in new[] { 0, 256 }) Assert.Throws<ArgumentOutOfRangeException>(() => new EffectFrame(new StableId("effect_test"), stacks, 1, boundary));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectFrame(new StableId("effect_test"), 1, -1, EffectExpiryBoundary.ExpireBeforeTick));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectFrame(new StableId("effect_test"), 1, 1, (EffectExpiryBoundary)99));
        var frames = Enumerable.Range(0, 128).Select(x => new EffectFrame(new StableId("effect_" + x.ToString("D3")), 1, 1, EffectExpiryBoundary.ExpireBeforeTick)).ToArray();
        Assert.Equal(128, Frame(frames).Effects.Count); Assert.Throws<ArgumentException>(() => Frame(frames.Concat(new[] { frames[0] })));
        foreach (var duplicate in new[] { false, true })
        {
            var replay = Wp10EffectReplayTests.Basic(); var entries = replay["events"]![1]!["after"]!["actor"]!["effects"]!.AsArray();
            var entry = entries[0]!.DeepClone(); if (!duplicate) entry["effect_id"] = "effect_a"; entries.Add(entry);
            Wp10EffectReplayTests.Rehash(replay); var result = ReplayTestFixture.Verify(Wp10EffectReplayTests.Serialize(replay));
            Assert.False(result.IsValid); Assert.Contains(result.Issues, x => x.Message.Contains("strictly ordinal", StringComparison.Ordinal));
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-BASE-005")]
    public void EffectsRuntimeContainsNoAmbientIoFloatingGameplayOrMutableStaticFields()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root, "src/Battle.Core/Effects"), "*.cs"))
        {
            var code = Regex.Replace(File.ReadAllText(file), "//[^\\r\\n]*|/\\*[\\s\\S]*?\\*/|\"(?:\\\\.|[^\"\\\\])*\"", "");
            Assert.DoesNotMatch(@"\b(float|double|decimal|DateTime|DateTimeOffset|Random|Guid|Stopwatch|File|Directory|JsonDocument|JsonNode|OpenXml)\b", code);
            Assert.DoesNotMatch(@"(?m)^\s*(?:public|private|internal|protected)\s+static\s+(?:readonly\s+)?[\w<>?, ]+\s+\w+\s*[=;]", code);
        }
    }

    [Fact, Trait("AcceptanceId", "WP10-DATA-008")]
    public void LegacyConfigStillCompilesButPublicEngine05RejectsItBeforeBegin()
    {
        var legacy = new BattleConfigCompiler().Compile(File.ReadAllBytes(Path.Combine(Root, "config/generated/combat.balance.v0.1.json")));
        Assert.True(legacy.IsSuccess); Assert.Equal("combat.balance/0.1", legacy.Config!.Reference.BalanceSchemaVersion.ToString());
        var a = Build(FighterId.FighterA, FighterSide.A, "bear", "bear_earthbreaker", "bear_rampage_charge", "bear_thick_hide", "tactic_pressure");
        var b = Build(FighterId.FighterB, FighterSide.B, "kangaroo", "kangaroo_flying_kick", "kangaroo_tail_counter", "kangaroo_never_still", "tactic_position");
        var mode = new ModeRulesSnapshot(new StableId("wp10_version_conformance"), ContractVersions.ModeRules, NormalizationMode.None,
            new[] { a.AnimalId, b.AnimalId }, a.SpecialActionIds.Concat(b.SpecialActionIds).Concat(new[] { new StableId("sys_wait") }),
            new[] { a.PassiveId, b.PassiveId }, new[] { a.Gear.Offense, a.Gear.Defense, a.Gear.Utility }, new[] { a.TacticId, b.TacticId });
        var request = new BattleRequest(new ExternalId("wp10-legacy-rejection"), ContractVersions.Engine, legacy.Config.Reference.ConfigHash, mode, 0, a, b);
        var assembly = Assembly.LoadFrom(Path.Combine(Root, "src/Battle.Core/bin", Configuration, "net10.0/Battle.Core.dll"));
        var type = assembly.GetType("Battle.Core.CombatEngine", throwOnError: true)!;
        var journal = new CanonicalReplayJournal(new ExternalId("wp10-rejected"));
        var result = (BattleResult)type.GetMethod("Simulate")!.Invoke(Activator.CreateInstance(type), new object[] { request, legacy.Config, journal })!;
        Assert.Equal(BattleResultStatus.Rejected, result.Status); Assert.Null(journal.Start); Assert.Empty(journal.Events);
        Assert.Contains(result.RejectionErrors, x => x.Code.Value == "BalanceSchemaVersionMismatch");
    }

    [Fact, Trait("AcceptanceId", "WP10-EVT-009")]
    public void PinnedConfigAwareImpactOracleRecomputesStatsDamageAndIndependentQueueBudgets()
    {
        var stem = "effects-impact-snapshot-l1.engine-0.5.0";
        var bytes = File.ReadAllBytes(Path.Combine(Root, "fixtures/replay/v0.1", stem + ".config.json"));
        var config = new BattleConfigCompiler().Compile(bytes); Assert.True(config.IsSuccess); Assert.Equal(bytes, config.GetCanonicalJson());
        using var source = JsonDocument.Parse(bytes); using var replay = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "fixtures/replay/v0.1", stem + ".json")));
        Assert.Equal(config.Config!.Reference.ConfigHash.ToString(), replay.RootElement.GetProperty("config").GetProperty("config_hash").GetString());
        var rule = source.RootElement.GetProperty("effect_rules").EnumerateArray().Single(x => x.GetProperty("rule_id").GetString() == "rule_demo_armor");
        Assert.Equal("DamageTaken", rule.GetProperty("trigger").GetString()); Assert.Equal("Self", rule.GetProperty("recipient").GetString());
        Assert.Equal(1, rule.GetProperty("max_activations_per_tick").GetInt32()); Assert.Equal(1, rule.GetProperty("max_activations_per_battle").GetInt32());
        var effect = source.RootElement.GetProperty("effects").EnumerateArray().Single(x => x.GetProperty("effect_id").GetString() == "effect_demo_armor");
        Assert.Equal("Armor", effect.GetProperty("modifier_stat1").GetString()); Assert.Equal(100, effect.GetProperty("value1").GetInt32());
        var armorK = source.RootElement.GetProperty("settings").GetProperty("global.damage.armor_k").GetInt32();
        var initial = 100 * (1000 - 100 * 1000 / (100 + armorK)) / 1000;
        var updated = 100 * (1000 - 200 * 1000 / (200 + armorK)) / 1000;
        var events = replay.RootElement.GetProperty("events").EnumerateArray().ToArray();
        var damage = events.Where(x => x.GetProperty("event_type").GetString() == "DamageApplied").ToArray();
        Assert.Equal(new[] { initial, initial, updated, updated }, damage.Select(x => x.GetProperty("payload").GetProperty("breakdown").GetProperty("final").GetInt32()));
        var added = events.Where(x => x.GetProperty("event_type").GetString() == "EffectAdded" && x.GetProperty("effect_id").GetString() == "effect_demo_armor").ToArray();
        Assert.Equal(new[] { "fighter_a", "fighter_b" }, added.Select(x => x.GetProperty("actor_id").GetString()).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(added, x => { Assert.True(x.GetProperty("sequence").GetInt64() > damage[1].GetProperty("sequence").GetInt64()); Assert.Equal(JsonValueKind.Null, x.GetProperty("rng").ValueKind); });
        // Re-run exactly the pinned input with the public producer, without deriving private stats from public frames.
        var request = ReplayRequest(replay.RootElement, config.Config); var journal = new CanonicalReplayJournal(new ExternalId(replay.RootElement.GetProperty("replay_id").GetString()!));
        var assembly = Assembly.LoadFrom(Path.Combine(Root, "src/Battle.Core/bin", Configuration, "net10.0/Battle.Core.dll")); var type = assembly.GetType("Battle.Core.CombatEngine")!;
        var result = (BattleResult)type.GetMethod("Simulate")!.Invoke(Activator.CreateInstance(type), new object[] { request, config.Config, journal })!;
        Assert.Equal(BattleResultStatus.Completed, result.Status);
        Assert.Equal(replay.RootElement.GetProperty("integrity").GetProperty("final_digest").GetString(), journal.FinalDigest!.Value.ToString());
    }

    [Fact, Trait("AcceptanceId", "WP10-REG-005")]
    public void LegacyCoverageAndFourCiConfigurationsRemainRequiredWithoutThresholdWeakening()
    {
        foreach (var wp in new[] { "02", "03", "06", "07", "08", "09" })
        { var script = File.ReadAllText(Path.Combine(Root, "scripts/verify-wp" + wp + "-coverage.ps1")); Assert.Contains("100%", script); }
        var workflow = File.ReadAllText(Path.Combine(Directory.GetParent(Root)!.FullName, ".github/workflows/combatlab.yml"));
        Assert.Contains("ubuntu-latest", workflow); Assert.Contains("windows-latest", workflow); Assert.Contains("Debug", workflow); Assert.Contains("Release", workflow);
        Assert.Contains("dotnet test", workflow); Assert.DoesNotContain("continue-on-error: true", workflow);
    }

    [Fact, Trait("AcceptanceId", "WP10-REG-006")]
    public void UnityBundled04CopiesStayPinnedAndNoPassiveResourceOwnerRulesAreAutoEnabled()
    {
        var assets = Path.Combine(Directory.GetParent(Root)!.FullName, "UnityClient/AnimalCombat/Assets");
        foreach (var name in new[] { "resolution-basic", "resolution-double-ko", "resolution-wall-grab" })
        {
            var expected = File.ReadAllBytes(Path.Combine(Root, "fixtures/replay/v0.1", name + "-l1.engine-0.4.0.json"));
            var copies = Directory.EnumerateFiles(assets, name + "-l1.engine-0.4.0.json", SearchOption.AllDirectories).ToArray();
            Assert.NotEmpty(copies); Assert.All(copies, x => Assert.Equal(expected, File.ReadAllBytes(x)));
        }
        using var config = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Root, "config/generated/combat.balance.v0.2.json")));
        Assert.All(config.RootElement.GetProperty("effect_rules").EnumerateArray(), x =>
        { Assert.Contains(x.GetProperty("owner_kind").GetString(), new[] { "Global", "Effect" }); Assert.DoesNotContain("GripGain", x.ToString()); });
    }

    private static BattleRequest ReplayRequest(JsonElement replay, Battle.Contracts.Config.CompiledBattleConfig config)
    {
        var input = replay.GetProperty("input"); var fighters = input.GetProperty("fighters").EnumerateArray().ToArray();
        FighterBuildSnapshot Read(JsonElement x) => new(Enum.Parse<FighterId>(x.GetProperty("fighter_id").GetString() == "fighter_a" ? "FighterA" : "FighterB"),
            Enum.Parse<FighterSide>(x.GetProperty("side").GetString()!), new StableId(x.GetProperty("animal_id").GetString()!), null,
            x.GetProperty("special_action_ids").EnumerateArray().Select(y => new StableId(y.GetString()!)), new StableId(x.GetProperty("passive_id").GetString()!),
            new GearSelection(new StableId(x.GetProperty("gear").GetProperty("offense").GetString()!), new StableId(x.GetProperty("gear").GetProperty("defense").GetString()!),
                new StableId(x.GetProperty("gear").GetProperty("utility").GetString()!)), new StableId(x.GetProperty("tactic_id").GetString()!));
        var a = Read(fighters[0]); var b = Read(fighters[1]);
        var mode = new ModeRulesSnapshot(new StableId(input.GetProperty("mode_rules_id").GetString()!), ContractVersions.ModeRules, NormalizationMode.None,
            new[] { a.AnimalId, b.AnimalId }, a.SpecialActionIds.Concat(b.SpecialActionIds).Concat(new[] { new StableId("sys_wait"), new StableId("sys_approach"), new StableId("sys_retreat"), new StableId("bear_paw_jab") }),
            new[] { a.PassiveId, b.PassiveId }, new[] { a.Gear.Offense, a.Gear.Defense, a.Gear.Utility }, new[] { a.TacticId, b.TacticId });
        return new BattleRequest(new ExternalId(replay.GetProperty("battle_id").GetString()!), ContractVersions.Engine, config.Reference.ConfigHash, mode,
            ulong.Parse(input.GetProperty("master_seed").GetString()!, System.Globalization.CultureInfo.InvariantCulture), a, b);
    }
    private static FighterBuildSnapshot Build(FighterId id, FighterSide side, string animal, string first, string second, string passive, string tactic) =>
        new(id, side, new StableId(animal), null, new[] { new StableId(first), new StableId(second) }, new StableId(passive),
            new GearSelection(new StableId("gear_offense_power_wraps"), new StableId("gear_defense_reinforced_hide"), new StableId("gear_utility_sprint_soles")), new StableId(tactic));
    private static FighterFrame Frame(IEnumerable<EffectFrame> effects) => new(FighterId.FighterA, 0, Facing.Right, FighterState.DecisionReady, null, null, null,
        100, 100, 0, 0, new ResourceFrame(new StableId("rage"), 0, 0), 0, 1, effects);
}
