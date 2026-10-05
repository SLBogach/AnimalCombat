using Battle.Contracts.Config;

namespace Battle.Config.Schema;

internal sealed class BalanceSchemaDefinition
{
    internal static readonly string[] BoundedStats =
    {
        "max_health", "max_energy", "energy_regen", "power", "armor", "precision", "evasion",
        "guard", "guard_break", "move_speed", "action_speed", "initiative", "control_power",
        "control_resistance", "mass",
    };

    public static readonly BalanceSchemaDefinition V01 = new(
        BalanceV01Schema.SchemaVersion, "v0.1", BalanceV01Schema.Settings, BalanceV01Schema.Catalogs);
    public static readonly BalanceSchemaDefinition V02 = CreateV02();

    private BalanceSchemaDefinition(string version, string configVersion,
        CatalogSchema settings, IReadOnlyDictionary<string, CatalogSchema> catalogs)
    {
        Version = version;
        ConfigVersion = configVersion;
        Settings = settings;
        Catalogs = catalogs;
        RootMembers = catalogs.Keys.Append("settings").OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    public string Version { get; }
    public string ConfigVersion { get; }
    public CatalogSchema Settings { get; }
    public IReadOnlyDictionary<string, CatalogSchema> Catalogs { get; }
    public IReadOnlyList<string> RootMembers { get; }

    public static BalanceSchemaDefinition? Find(string version) => version switch
    {
        "combat.balance/0.1" => V01,
        "combat.balance/0.2" => V02,
        _ => null,
    };

    private static BalanceSchemaDefinition CreateV02()
    {
        // Keep v0.1 metadata untouched: extending the schema must not change its exported bytes.
        var catalogs = new Dictionary<string, CatalogSchema>(BalanceV01Schema.Catalogs, StringComparer.Ordinal)
        {
            ["actions"] = Extend(BalanceV01Schema.Catalogs["actions"],
                "hit_interrupt_strength|hit_interrupt_min_strength", string.Empty,
                "hit_interruptible_phases|protected_phases|ignored_control_categories"),
            ["effects"] = Extend(BalanceV01Schema.Catalogs["effects"], "priority", string.Empty,
                "refresh_rule|semantic_role",
                ("refresh_rule", "ResetDuration|KeepLonger"),
                ("semantic_role", "None|ControlFatigue|ControlImmunity|GrabLockout|WakeupImmunity|GuardBreak")),
            ["gear"] = Extend(BalanceV01Schema.Catalogs["gear"], "priority", string.Empty, string.Empty),
            ["effect_rules"] = new CatalogSchema("rule_id",
                "priority|internal_cooldown_ticks|max_activations_per_tick|max_activations_per_battle",
                "once_per_event", "rule_id|owner_kind|owner_id|trigger|recipient|condition|primitive|effect_id",
                string.Empty, string.Empty, string.Empty,
                ("owner_kind", "Global|Action|Effect"), ("recipient", "Self|Opponent"),
                ("condition", "Always|PositiveDamage|NormalHit|LivingTarget"),
                ("primitive", "ApplyEffect|RemoveEffect"),
                ("trigger", "BattleStart|DamageTaken|DamageDealt|Blocked|Dodged|GuardBreak|ControlEnded|FatigueThresholdReached|GrabEnded|Knockdown|WakeupCompleted|EffectAdded|EffectRemoved|EndOfTick")),
        };
        var bounds = string.Join("|", BoundedStats.SelectMany(x => new[] { "stat." + x + ".min", "stat." + x + ".max" }));
        var settings = Extend(BalanceV01Schema.Settings,
            "global.control.max_trigger_depth|global.control.max_triggers_per_tick|global.control.max_effect_instances_per_fighter|global.control.knockdown_fall_ticks|global.control.knockdown_grounded_ticks|global.control.knockdown_getup_ticks|" + bounds,
            string.Empty, string.Empty);
        return new BalanceSchemaDefinition("combat.balance/0.2", "v0.2", settings, catalogs);
    }

    private static CatalogSchema Extend(CatalogSchema original, string integers, string booleans,
        string strings, params (string Field, string Values)[] enums)
    {
        string Names(ConfigValueKind kind, bool required, string extras = "") => string.Join("|",
            original.Fields.Where(x => x.Value.Kind == kind && x.Value.Required == required)
                .Select(x => x.Key).Concat(extras.Split('|').Where(x => x.Length > 0)));
        var inheritedEnums = original.Fields.Where(x => x.Value.EnumValues.Count > 0)
            .Select(x => (x.Key, string.Join("|", x.Value.EnumValues)));
        return new CatalogSchema(original.IdProperty,
            Names(ConfigValueKind.Integer, true, integers), Names(ConfigValueKind.Boolean, true, booleans),
            Names(ConfigValueKind.String, true, strings), Names(ConfigValueKind.Integer, false),
            Names(ConfigValueKind.Boolean, false), Names(ConfigValueKind.String, false),
            inheritedEnums.Concat(enums).ToArray());
    }
}
