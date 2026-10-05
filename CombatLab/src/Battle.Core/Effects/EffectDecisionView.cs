using Battle.Contracts.Effects;
using Battle.Core.Decisions;
using Battle.Core.Engine;
using Battle.Core.Math;

namespace Battle.Core.Effects;

/// <summary>Immutable phase-5 inputs shared by all evaluations in a decision batch.</summary>
internal sealed class EffectDecisionView
{
    private readonly EffectStatSource[] sources;

    internal EffectDecisionView(IEnumerable<EffectStatSource> sources, bool grabAllowed)
    {
        this.sources = sources?.ToArray() ?? throw new ArgumentNullException(nameof(sources));
        GrabAllowed = grabAllowed;
    }

    internal bool GrabAllowed { get; }

    internal int Situation(DecisionActionProfile action, int baseline, int scale)
    {
        // Merge matching channels before folding: cross-channel canonical order matters to floor.
        var matching = sources.Where(x => Matches(action, x.Modifier.Target)).Select(x => x with
        {
            Modifier = new EffectModifier(EffectModifierTarget.BlockWeight,
                x.Modifier.Operation, x.Modifier.Value, x.Modifier.Ordinal),
        }).ToArray();
        if (matching.Length == 0) return baseline;
        try
        {
            var factor = EffectStatMath.Compute(scale, new StatBounds(0, int.MaxValue), matching, scale);
            return FixedMath.Mul(baseline, factor, scale);
        }
        catch (OverflowException exception)
        {
            throw new EngineInvariantException(EngineFailureCodes.EffectArithmeticOverflow,
                TickPhase.Decisions.ToString(), "Effect Situation arithmetic overflowed: " + exception.Message);
        }
    }

    private static bool Matches(DecisionActionProfile action, EffectModifierTarget target) => target switch
    {
        EffectModifierTarget.BlockWeight => action.HasTag("block"),
        EffectModifierTarget.PunishWeight => action.HasTag("punish"),
        EffectModifierTarget.WallActionWeight => action.HasTag("wall_impact"),
        _ => false,
    };
}
