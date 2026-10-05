using System.Security.Cryptography;
using System.Text.Json;
using Battle.Contracts.Config;
using Battle.Contracts.Ids;
using Battle.Contracts.Requests;
using Battle.Contracts.Versions;
using Battle.Core.UnitTests.Engine;

namespace Battle.Core.UnitTests.Effects;

/// <summary>
/// Test-only reader of committed generated DATA. Malformed compiled snapshots deliberately bypass
/// Battle.Config, so Core's independent pre-start guards cannot accidentally rely on the compiler.
/// </summary>
internal static class EffectSetupFixture
{
    internal static CompiledBattleConfig Config()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CombatLab.sln"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("CombatLab test root not found.");
        var bytes = File.ReadAllBytes(Path.Combine(directory.FullName, "config/generated/combat.balance.v0.2.json"));
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var hash = new Sha256Digest("sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        return new CompiledBattleConfig(new ConfigReference(new ArtifactVersion("combat.balance/0.2"), new ArtifactVersion("v0.2"), hash),
            Properties(root.GetProperty("settings")), Catalog("fighters", "animal_id"), Catalog("actions", "action_id"),
            Catalog("passives", "passive_id"), Catalog("effects", "effect_id"), Catalog("tactics", "tactic_id"),
            Catalog("gear", "gear_id"), Catalog("effect_rules", "rule_id"));

        CompiledConfigEntity[] Catalog(string name, string id) => root.GetProperty(name).EnumerateArray()
            .OrderBy(x => x.GetProperty(id).GetString(), StringComparer.Ordinal).Select((x, index) =>
                new CompiledConfigEntity(new StableId(x.GetProperty(id).GetString()!), index, Properties(x))).ToArray();
    }
    private static ConfigProperty[] Properties(JsonElement entity) => entity.EnumerateObject()
        .OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => new ConfigProperty(x.Name, x.Value.ValueKind switch
        {
            JsonValueKind.String => ConfigValue.FromString(x.Value.GetString()!),
            JsonValueKind.Number => ConfigValue.FromInteger(x.Value.GetInt64()),
            JsonValueKind.True => ConfigValue.FromBoolean(true),
            JsonValueKind.False => ConfigValue.FromBoolean(false),
            _ => throw new InvalidOperationException("Unexpected fixture value kind."),
        })).ToArray();

    internal static BattleRequest Request(CompiledBattleConfig config) => EngineTestFixture.CreateRequest(
        engineVersion: new ArtifactVersion("battle.core/0.5.0"), configHash: config.Reference.ConfigHash);
    internal static CompiledConfigEntity Entity(string id, params (string Name, ConfigValue Value)[] properties) =>
        EngineTestFixture.Entity(id, 0, properties);
    internal static CompiledConfigEntity Patch(CompiledConfigEntity entity, params (string Name, ConfigValue Value)[] properties)
    {
        foreach (var property in properties) entity = EngineTestFixture.WithProperty(entity, property.Name, property.Value);
        return entity;
    }
    internal static CompiledConfigEntity Without(CompiledConfigEntity entity, string property) =>
        new(entity.Id, entity.DenseHandle, entity.Properties.Where(x => x.Name != property));
    internal static CompiledBattleConfig Change(CompiledBattleConfig config,
        IEnumerable<ConfigProperty>? settings = null, IEnumerable<CompiledConfigEntity>? effects = null,
        IEnumerable<CompiledConfigEntity>? rules = null, IEnumerable<CompiledConfigEntity>? actions = null,
        IEnumerable<CompiledConfigEntity>? gear = null, IEnumerable<CompiledConfigEntity>? fighters = null) => new(config.Reference,
            (settings ?? config.Settings).OrderBy(x => x.Name, StringComparer.Ordinal),
            EngineTestFixture.ReindexCatalog(fighters ?? config.Fighters), EngineTestFixture.ReindexCatalog(actions ?? config.Actions), config.Passives,
            EngineTestFixture.ReindexCatalog(effects ?? config.Effects), config.Tactics, EngineTestFixture.ReindexCatalog(gear ?? config.Gear),
            EngineTestFixture.ReindexCatalog(rules ?? config.EffectRules));
    internal static CompiledBattleConfig Setting(CompiledBattleConfig config, string key, ConfigValue value) =>
        Change(config, settings: config.Settings.Where(x => x.Name != key).Append(new ConfigProperty(key, value)));
    internal static CompiledBattleConfig Effect(CompiledBattleConfig config, string id, params (string Name, ConfigValue Value)[] properties) =>
        Change(config, effects: config.Effects.Select(x => x.Id.Value == id ? Patch(x, properties) : x));
    internal static CompiledConfigEntity Rule(string id, string effect, string ownerKind = "Global", string owner = "global", string trigger = "BattleStart") =>
        Entity(id, ("owner_kind", ConfigValue.FromString(ownerKind)), ("owner_id", ConfigValue.FromString(owner)),
            ("trigger", ConfigValue.FromString(trigger)), ("recipient", ConfigValue.FromString("Self")),
            ("condition", ConfigValue.FromString("LivingTarget")), ("primitive", ConfigValue.FromString("ApplyEffect")),
            ("effect_id", ConfigValue.FromString(effect)), ("priority", ConfigValue.FromInteger(0)),
            ("internal_cooldown_ticks", ConfigValue.FromInteger(0)), ("max_activations_per_tick", ConfigValue.FromInteger(1)),
            ("max_activations_per_battle", ConfigValue.FromInteger(999)), ("once_per_event", ConfigValue.FromBoolean(true)));
    internal static CompiledBattleConfig Reachable(CompiledBattleConfig config, string effect, string ownerKind = "Global", string owner = "global") =>
        Change(config, rules: config.EffectRules.Append(Rule("rule_test", effect, ownerKind, owner)));
    internal static CompiledConfigEntity CustomEffect(CompiledBattleConfig config, string id, string group, string target,
        string operation = "Add", int value = 10, string policy = "Refresh", int cap = 1) =>
        Entity(id, Patch(config.Effects.Single(x => x.Id.Value == "effect_exposed"),
            ("effect_id", ConfigValue.FromString(id)), ("stack_group", ConfigValue.FromString(group)),
            ("modifier_stat1", ConfigValue.FromString(target)), ("operation1", ConfigValue.FromString(operation)),
            ("value1", ConfigValue.FromInteger(value)), ("modifier_stat2", ConfigValue.FromString("")),
            ("operation2", ConfigValue.FromString("Override")), ("value2", ConfigValue.FromInteger(0)),
            ("stack_policy", ConfigValue.FromString(policy)), ("stack_cap", ConfigValue.FromInteger(cap))).Properties
            .Select(x => (x.Name, x.Value)).ToArray());
}
