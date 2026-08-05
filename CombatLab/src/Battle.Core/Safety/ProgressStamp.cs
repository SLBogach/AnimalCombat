using Battle.Core.Engine;
using System.Globalization;

namespace Battle.Core.Safety;

internal readonly record struct ProgressStamp(
    int FighterAPosition,
    int FighterBPosition,
    int FighterAHealth,
    int FighterBHealth,
    int FighterAEnergy,
    int FighterBEnergy,
    int FighterAResource,
    int FighterBResource,
    int FighterAStagger,
    int FighterBStagger,
    int FighterAState,
    int FighterBState,
    string? FighterAAction,
    string? FighterBAction,
    int FighterAActionPhase,
    int FighterBActionPhase,
    int FighterATimer,
    int FighterBTimer,
    string FighterACooldowns,
    string FighterBCooldowns,
    string FighterAEffects,
    string FighterBEffects,
    string? ActiveGrabId,
    string? ActiveControlId,
    int Outcome,
    int Winner,
    int EndReason)
{
    internal static ProgressStamp Capture(BattleState state)
    {
        if (state is null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        var fighterA = state.FighterA;
        var fighterB = state.FighterB;
        return new ProgressStamp(
            fighterA.Position,
            fighterB.Position,
            fighterA.Health,
            fighterB.Health,
            fighterA.Energy,
            fighterB.Energy,
            fighterA.Resource,
            fighterB.Resource,
            fighterA.Stagger,
            fighterB.Stagger,
            (int)fighterA.State,
            (int)fighterB.State,
            fighterA.ActionId?.Value,
            fighterB.ActionId?.Value,
            fighterA.ActionPhase.HasValue ? (int)fighterA.ActionPhase.Value : -1,
            fighterB.ActionPhase.HasValue ? (int)fighterB.ActionPhase.Value : -1,
            fighterA.StateTicksRemaining ?? -1,
            fighterB.StateTicksRemaining ?? -1,
            CooldownStamp(fighterA),
            CooldownStamp(fighterB),
            EffectStamp(fighterA),
            EffectStamp(fighterB),
            state.ActiveGrabId?.Value,
            state.ActiveControlId?.Value,
            state.Outcome.HasValue ? (int)state.Outcome.Value : -1,
            state.WinnerFighterId.HasValue ? (int)state.WinnerFighterId.Value : -1,
            state.EndReason.HasValue ? (int)state.EndReason.Value : -1);
    }

    private static string CooldownStamp(FighterRuntimeState fighter) =>
        string.Join(
            "\u001e",
            fighter.Cooldowns
                .OrderBy(item => item.Key.Value, StringComparer.Ordinal)
                .Select(item =>
                    item.Key.Value + ":" + item.Value.ToString(CultureInfo.InvariantCulture)));

    private static string EffectStamp(FighterRuntimeState fighter) =>
        string.Join(
            "\u001e",
            fighter.Effects
                .OrderBy(item => item.EffectId.Value, StringComparer.Ordinal)
                .Select(item => string.Join(
                    ":",
                    item.EffectId.Value,
                    item.Stacks.ToString(CultureInfo.InvariantCulture),
                    item.TicksRemaining.ToString(CultureInfo.InvariantCulture),
                    ((int)item.ExpiryBoundary).ToString(CultureInfo.InvariantCulture))));
}
