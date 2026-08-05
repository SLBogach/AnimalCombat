using System.Globalization;
using System.Text.Json;
using Battle.Contracts.Config;
using Battle.Contracts.Events;
using Battle.Contracts.Ids;
using Battle.Contracts.Replay;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;
using Battle.Replay.Journal;

namespace Battle.ConformanceTests.Replay;

public sealed class CombatJournalStartFixtureTests
{
    private const string ExpectedInputDigest =
        "sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b";
    private const string ExpectedFinalDigest =
        "sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e";

    [Theory]
    [InlineData("replay-standard.example.json")]
    [InlineData("replay-diagnostic.example.json")]
    public void TypedJournalStart_ReproducesNormativeInputVector(string fixtureName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(GetReplayFixturePath(fixtureName)));
        var root = document.RootElement;
        var start = ReadStart(root);
        var journal = new CanonicalReplayJournal(
            new ExternalId(root.GetProperty("replay_id").GetString()!));

        var receipt = journal.Begin(in start);

        Assert.Equal(ExpectedInputDigest, receipt.InputDigest.Value);
        Assert.Equal(receipt.InputDigest, journal.InputDigest);
        Assert.Equal(start, journal.Start);
    }

    [Fact]
    public void WP06_CON_002_TypedLifecycleReproducesNormativeFinalVector()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(GetReplayFixturePath("replay-standard.example.json")));
        var root = document.RootElement;
        var start = ReadStart(root);
        var summary = FixtureCombatEventReader.ReadSummary(root.GetProperty("summary"));
        var journal = new CanonicalReplayJournal(
            new ExternalId(root.GetProperty("replay_id").GetString()!));

        var begin = journal.Begin(in start);
        foreach (var eventElement in root.GetProperty("events").EnumerateArray())
        {
            var draft = FixtureCombatEventReader.ReadDraft(eventElement, summary);
            _ = journal.Append(in draft);
        }

        var completion = journal.Complete(in summary);

        Assert.Equal(ExpectedInputDigest, begin.InputDigest.Value);
        Assert.Equal(ExpectedFinalDigest, completion.FinalDigest.Value);
        Assert.Equal(ExpectedFinalDigest, journal.FinalDigest!.Value.Value);
        Assert.Equal(13, journal.Events.Count);
    }

    private static CombatJournalStart ReadStart(JsonElement replay)
    {
        var engine = replay.GetProperty("engine");
        var config = replay.GetProperty("config");
        var input = replay.GetProperty("input");
        var arena = input.GetProperty("arena");
        var fighters = input.GetProperty("fighters");

        return new CombatJournalStart(
            new ExternalId(replay.GetProperty("battle_id").GetString()!),
            new ArtifactVersion(engine.GetProperty("engine_version").GetString()!),
            new ArtifactVersion(engine.GetProperty("rng_version").GetString()!),
            new ArtifactVersion(engine.GetProperty("ordering_version").GetString()!),
            new ConfigReference(
                new ArtifactVersion(config.GetProperty("balance_schema_version").GetString()!),
                new ArtifactVersion(config.GetProperty("config_version").GetString()!),
                new Sha256Digest(config.GetProperty("config_hash").GetString()!)),
            new BattleInputSnapshot(
                ulong.Parse(
                    input.GetProperty("master_seed").GetString()!,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture),
                new StableId(input.GetProperty("mode_rules_id").GetString()!),
                new ArenaSnapshot(
                    new StableId(arena.GetProperty("arena_id").GetString()!),
                    arena.GetProperty("min_position").GetInt32(),
                    arena.GetProperty("max_position").GetInt32(),
                    arena.GetProperty("start_position_a").GetInt32(),
                    arena.GetProperty("start_position_b").GetInt32())),
            ReadFighterStart(fighters[0]),
            ReadFighterStart(fighters[1]));
    }

    private static CombatJournalFighterStart ReadFighterStart(JsonElement fighter)
    {
        var fighterId = fighter.GetProperty("fighter_id").GetString() switch
        {
            "fighter_a" => FighterId.FighterA,
            "fighter_b" => FighterId.FighterB,
            _ => throw new InvalidOperationException("Unknown fixture fighter ID."),
        };
        var side = Enum.Parse<FighterSide>(fighter.GetProperty("side").GetString()!);
        var specialIds = fighter.GetProperty("special_action_ids")
            .EnumerateArray()
            .Select(item => new StableId(item.GetString()!));
        var gear = fighter.GetProperty("gear");
        var buildIdElement = fighter.GetProperty("build_id");
        var build = new FighterBuildSnapshot(
            fighterId,
            side,
            new StableId(fighter.GetProperty("animal_id").GetString()!),
            buildIdElement.ValueKind == JsonValueKind.Null
                ? null
                : new StableId(buildIdElement.GetString()!),
            specialIds,
            new StableId(fighter.GetProperty("passive_id").GetString()!),
            new GearSelection(
                new StableId(gear.GetProperty("offense").GetString()!),
                new StableId(gear.GetProperty("defense").GetString()!),
                new StableId(gear.GetProperty("utility").GetString()!)),
            new StableId(fighter.GetProperty("tactic_id").GetString()!));

        return new CombatJournalFighterStart(
            build,
            ReadFrame(fighter.GetProperty("initial_frame"), fighterId));
    }

    private static FighterFrame ReadFrame(JsonElement frame, FighterId fighterId)
    {
        var actionId = frame.GetProperty("action_id");
        var actionPhase = frame.GetProperty("action_phase");
        var stateTicks = frame.GetProperty("state_ticks_remaining");
        var resource = frame.GetProperty("unique_resource");
        var effects = frame.GetProperty("effects")
            .EnumerateArray()
            .Select(effect => new EffectFrame(
                new StableId(effect.GetProperty("effect_id").GetString()!),
                effect.GetProperty("stacks").GetInt32(),
                effect.GetProperty("ticks_remaining").GetInt32(),
                Enum.Parse<EffectExpiryBoundary>(
                    effect.GetProperty("expiry_boundary").GetString()!)));

        return new FighterFrame(
            fighterId,
            frame.GetProperty("position").GetInt32(),
            Enum.Parse<Facing>(frame.GetProperty("facing").GetString()!),
            Enum.Parse<FighterState>(frame.GetProperty("state").GetString()!),
            stateTicks.ValueKind == JsonValueKind.Null ? null : stateTicks.GetInt32(),
            actionId.ValueKind == JsonValueKind.Null
                ? null
                : new StableId(actionId.GetString()!),
            actionPhase.ValueKind == JsonValueKind.Null
                ? null
                : Enum.Parse<ActionPhase>(actionPhase.GetString()!),
            frame.GetProperty("health").GetInt32(),
            frame.GetProperty("max_health").GetInt32(),
            frame.GetProperty("energy").GetInt32(),
            frame.GetProperty("max_energy").GetInt32(),
            new ResourceFrame(
                new StableId(resource.GetProperty("resource_id").GetString()!),
                resource.GetProperty("value").GetInt32(),
                resource.GetProperty("max").GetInt32()),
            frame.GetProperty("stagger").GetInt32(),
            frame.GetProperty("stagger_threshold").GetInt32(),
            effects);
    }

    private static string GetReplayFixturePath(string fixtureName) =>
        Path.Combine(
            RepositoryLocator.FindCombatLabRoot(),
            "fixtures",
            "replay",
            "v0.1",
            fixtureName);
}
