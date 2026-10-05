using Battle.Core.Math;

namespace Battle.Core.Effects;

internal enum KnockdownStage { Fall, Grounded, GetUp, Completed }

internal readonly record struct KnockdownTimeline(int StartTick, int GroundedTick, int GetupTick, int ReadyTick)
{
    internal KnockdownStage StageAt(int tick)
    {
        if (tick < StartTick) throw new ArgumentOutOfRangeException(nameof(tick));
        return tick < GroundedTick ? KnockdownStage.Fall : tick < GetupTick ? KnockdownStage.Grounded
            : tick < ReadyTick ? KnockdownStage.GetUp : KnockdownStage.Completed;
    }
    internal int RemainingAt(int tick)
    {
        if (tick < StartTick) throw new ArgumentOutOfRangeException(nameof(tick));
        return tick >= ReadyTick ? 0 : checked(ReadyTick - tick);
    }
}

/// <summary>DATA-driven duration math only. State/action/journal transitions are Engine work.</summary>
internal static class EffectControlMath
{
    internal static int ControlRatio(int controlK, int power, int resistance, int scale)
    {
        if (controlK < 1 || power < 0 || resistance < 0 || scale < 1)
            throw new ArgumentOutOfRangeException(nameof(controlK));
        return FixedMath.Div(checked(controlK + power), checked(controlK + resistance), scale);
    }
    internal static int StunTicks(int baseTicks, int ratio, int fatigue, int scale, int minimum, int maximum)
    {
        ValidateFactors(baseTicks, ratio, fatigue, scale);
        if (minimum < 1 || maximum < minimum) throw new ArgumentOutOfRangeException(nameof(minimum));
        return FixedMath.Clamp(FixedMath.Mul(FixedMath.Mul(baseTicks, ratio, scale), fatigue, scale), minimum, maximum);
    }
    internal static KnockdownTimeline Knockdown(int tick, int fall, int grounded, int getup,
        int ratio, int fatigue, int scale)
    {
        if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
        ValidateFactors(fall, ratio, fatigue, scale);
        ValidateFactors(grounded, ratio, fatigue, scale);
        ValidateFactors(getup, ratio, fatigue, scale);
        if (fall < 1 || grounded < 1 || getup < 1) throw new ArgumentOutOfRangeException(nameof(fall));
        int Stage(int duration) => System.Math.Max(1, FixedMath.Mul(FixedMath.Mul(duration, ratio, scale), fatigue, scale));
        var groundedTick = checked(tick + Stage(fall));
        var getupTick = checked(groundedTick + Stage(grounded));
        return new KnockdownTimeline(tick, groundedTick, getupTick, checked(getupTick + Stage(getup)));
    }
    private static void ValidateFactors(int value, int ratio, int fatigue, int scale)
    {
        if (value < 0 || ratio < 0 || fatigue < 0 || scale < 1) throw new ArgumentOutOfRangeException(nameof(value));
    }
}
