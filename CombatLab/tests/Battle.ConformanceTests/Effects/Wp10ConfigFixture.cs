using System.Text;
using System.Text.Json.Nodes;
using Battle.ConformanceTests.Config;

namespace Battle.ConformanceTests.Effects;

/// <summary>Synthetic v0.2 DATA for boundary tests, not a substitute for the source workbook.</summary>
internal static class Wp10ConfigFixture
{
    public static readonly (string Stat, int Minimum, int Maximum)[] Bounds =
    {
        ("max_health", 1, 10000), ("max_energy", 0, 10000), ("energy_regen", 0, 1000),
        ("power", 0, 1000), ("armor", 0, 2000), ("precision", 0, 1000), ("evasion", 0, 1000),
        ("guard", 0, 1000), ("guard_break", 0, 1000), ("move_speed", 1, 500),
        ("action_speed", 1, 500), ("initiative", 0, 1000), ("control_power", 0, 1000),
        ("control_resistance", 0, 1000), ("mass", 1, 2000),
    };

    public static JsonObject Create()
    {
        var root = ConfigFixture.ReadConfigObject();
        var settings = root["settings"]!.AsObject();
        settings["global.sim.schema_version"] = "combat.balance/0.2";
        settings["global.sim.config_version"] = "v0.2";
        foreach (var item in Bounds)
        {
            settings["stat." + item.Stat + ".min"] = item.Minimum;
            settings["stat." + item.Stat + ".max"] = item.Maximum;
        }
        settings["global.control.max_trigger_depth"] = 8;
        settings["global.control.max_triggers_per_tick"] = 128;
        settings["global.control.max_effect_instances_per_fighter"] = 32;
        settings["global.control.knockdown_fall_ticks"] = 2;
        settings["global.control.knockdown_grounded_ticks"] = 6;
        settings["global.control.knockdown_getup_ticks"] = 3;
        foreach (var action in root["actions"]!.AsArray().Select(x => x!.AsObject()))
        {
            action["hit_interrupt_strength"] = 0;
            action["hit_interrupt_min_strength"] = action["interrupt_profile"]!.GetValue<string>() == "Armored" ? 3 : 1;
            action["hit_interruptible_phases"] = "Startup";
            action["protected_phases"] = action["interrupt_profile"]!.GetValue<string>() is "Armored" or "Unstoppable" ? "Startup|Active" : "";
            action["ignored_control_categories"] = action["interrupt_profile"]!.GetValue<string>() == "Unstoppable" ? "Stun|Knockdown" : "";
        }
        foreach (var gear in root["gear"]!.AsArray().Select(x => x!.AsObject())) gear["priority"] = 0;
        foreach (var effect in root["effects"]!.AsArray().Select(x => x!.AsObject()))
        {
            effect["priority"] = 0;
            effect["refresh_rule"] = "ResetDuration";
            effect["semantic_role"] = effect["effect_id"]!.GetValue<string>() switch
            {
                "effect_control_fatigue" => "ControlFatigue", "effect_control_immunity" => "ControlImmunity",
                "effect_grab_lockout" => "GrabLockout", "effect_wakeup_immunity" => "WakeupImmunity",
                "effect_guard_broken" => "GuardBreak", _ => "None",
            };
        }
        root["effect_rules"] = new JsonArray(
            Rule("rule_control_fatigue", "ControlEnded", "effect_control_fatigue"),
            Rule("rule_control_immunity", "FatigueThresholdReached", "effect_control_immunity"),
            Rule("rule_grab_lockout", "GrabEnded", "effect_grab_lockout"),
            Rule("rule_wakeup_immunity", "WakeupCompleted", "effect_wakeup_immunity"),
            Rule("rule_guard_broken", "GuardBreak", "effect_guard_broken"));
        return root;
    }

    public static JsonObject Rule(string id, string trigger, string effectId) => new()
    {
        ["rule_id"] = id, ["owner_kind"] = "Global", ["owner_id"] = "global",
        ["trigger"] = trigger, ["recipient"] = "Self", ["condition"] = "LivingTarget",
        ["primitive"] = "ApplyEffect", ["effect_id"] = effectId, ["priority"] = 0,
        ["internal_cooldown_ticks"] = 0, ["max_activations_per_tick"] = 1,
        ["max_activations_per_battle"] = 999, ["once_per_event"] = true,
    };

    public static JsonObject Effect(JsonObject root, string id) =>
        ConfigFixture.Entity(root, "effects", "effect_id", id);
    public static byte[] Bytes(JsonObject root) => Encoding.UTF8.GetBytes(root.ToJsonString());
}
