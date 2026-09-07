namespace Battle.Core.Resolution;

internal readonly record struct DamageComputation(
    int PowerTerm,
    int Raw,
    int AfterArmor,
    int AfterBlock,
    int Final);

internal readonly record struct DamageMutation(
    DamageComputation Computation,
    int HealthBefore,
    int HealthAfter,
    int ActualHealthLoss,
    int Overkill,
    bool Lethal);

internal readonly record struct ControlComputation(
    int ControlRatioFixedPoint,
    int StaggerGain,
    int StunTicks);

internal static class ResolutionMath
{
    internal static int MultiplyFixedPoint(int value, int multiplier, int scale)
    {
        RequireNonNegative(value, nameof(value));
        RequireNonNegative(multiplier, nameof(multiplier));
        if (scale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        return checked((int)(checked((long)value * multiplier) / scale));
    }

    internal static int DivideFixedPoint(int numerator, int denominator, int scale)
    {
        RequireNonNegative(numerator, nameof(numerator));
        if (scale < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        var safeDenominator = System.Math.Max(denominator, 1);
        return checked((int)(checked((long)numerator * scale) / safeDenominator));
    }

    internal static DamageComputation ComputeDamage(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile action,
        int effectivePower,
        int effectiveArmor,
        bool blocked = false)
    {
        settings.Validate();
        RequireNonNegative(effectivePower, nameof(effectivePower));
        RequireNonNegative(effectiveArmor, nameof(effectiveArmor));

        var powerTerm = checked(action.BaseDamage +
            MultiplyFixedPoint(effectivePower, action.PowerRatioFixedPoint, settings.FixedPointScale));
        var raw = MultiplyFixedPoint(powerTerm, settings.FixedPointScale, settings.FixedPointScale);
        var armorDenominator = checked(effectiveArmor + settings.ArmorK);
        var armorRatio = DivideFixedPoint(effectiveArmor, armorDenominator, settings.FixedPointScale);
        var afterArmor = MultiplyFixedPoint(raw, checked(settings.FixedPointScale - armorRatio), settings.FixedPointScale);
        var normal = System.Math.Max(
            action.MinimumDamage,
            System.Math.Clamp(afterArmor, settings.DamageFloor, settings.DamageCap));
        if (!blocked)
        {
            return new DamageComputation(powerTerm, raw, afterArmor, afterArmor, normal);
        }

        var reduced = MultiplyFixedPoint(
            normal,
            checked(settings.FixedPointScale - action.BlockReductionFixedPoint),
            settings.FixedPointScale);
        var blockedDamage = System.Math.Max(action.ChipMinimum, reduced);
        return new DamageComputation(powerTerm, raw, afterArmor, blockedDamage, blockedDamage);
    }

    internal static DamageMutation ApplyHealth(DamageComputation computation, int healthBefore)
    {
        RequireNonNegative(healthBefore, nameof(healthBefore));
        var healthAfter = System.Math.Max(0, checked(healthBefore - computation.Final));
        var actual = checked(healthBefore - healthAfter);
        var overkill = checked(computation.Final - actual);
        return new DamageMutation(computation, healthBefore, healthAfter, actual, overkill, healthAfter == 0);
    }

    internal static DamageComputation ApplyBlock(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile incoming,
        ResolutionActionProfile defense,
        DamageComputation normal)
    {
        settings.Validate();
        var reduced = MultiplyFixedPoint(
            normal.Final,
            checked(settings.FixedPointScale - defense.BlockReductionFixedPoint),
            settings.FixedPointScale);
        var blockedDamage = System.Math.Max(incoming.ChipMinimum, reduced);
        return new DamageComputation(
            normal.PowerTerm,
            normal.Raw,
            normal.AfterArmor,
            blockedDamage,
            blockedDamage);
    }

    internal static int ComputeBlockChance(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile defense,
        int guard,
        int attackerGuardBreak)
    {
        RequireNonNegative(guard, nameof(guard));
        RequireNonNegative(attackerGuardBreak, nameof(attackerGuardBreak));
        var candidate = checked((long)defense.BlockBaseChanceFixedPoint +
            checked((long)(guard - attackerGuardBreak) * settings.BlockSlope));
        return checked((int)System.Math.Clamp(candidate, settings.BlockMinimum, settings.BlockMaximum));
    }

    internal static int ComputeDodgeChance(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile defense,
        int evasion,
        int attackerPrecision)
    {
        RequireNonNegative(evasion, nameof(evasion));
        RequireNonNegative(attackerPrecision, nameof(attackerPrecision));
        var candidate = checked((long)defense.DodgeBaseChanceFixedPoint +
            checked((long)(evasion - attackerPrecision) * settings.DodgeSlope));
        return checked((int)System.Math.Clamp(candidate, settings.DodgeMinimum, settings.DodgeMaximum));
    }

    internal static ControlComputation ComputeControl(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile action,
        int attackerControlPower,
        int defenderControlResistance,
        int fatigueMultiplierFixedPoint)
    {
        RequireNonNegative(attackerControlPower, nameof(attackerControlPower));
        RequireNonNegative(defenderControlResistance, nameof(defenderControlResistance));
        RequireNonNegative(fatigueMultiplierFixedPoint, nameof(fatigueMultiplierFixedPoint));
        var ratio = DivideFixedPoint(
            checked(settings.ControlK + attackerControlPower),
            checked(settings.ControlK + defenderControlResistance),
            settings.FixedPointScale);
        var stagger = MultiplyFixedPoint(action.BaseStagger, ratio, settings.FixedPointScale);
        var stun = MultiplyFixedPoint(action.BaseStunTicks, ratio, settings.FixedPointScale);
        stun = MultiplyFixedPoint(stun, fatigueMultiplierFixedPoint, settings.FixedPointScale);
        stun = System.Math.Clamp(stun, settings.StunMinimumTicks, settings.StunMaximumTicks);
        return new ControlComputation(ratio, stagger, stun);
    }

    internal static int ComputeForceRatio(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile action,
        int defenderMass)
    {
        RequireNonNegative(defenderMass, nameof(defenderMass));
        return DivideFixedPoint(
            checked(settings.ForceK + action.BaseKnockback),
            checked(settings.ForceK + defenderMass),
            settings.FixedPointScale);
    }

    internal static int ComputeRequestedMove(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile action,
        int defenderMass)
    {
        var ratio = ComputeForceRatio(settings, action, defenderMass);
        return System.Math.Clamp(
            MultiplyFixedPoint(action.BaseKnockback, ratio, settings.FixedPointScale),
            action.KnockbackMinimum,
            action.KnockbackMaximum);
    }

    internal static int ComputeWallDamage(
        ResolutionGlobalSettings settings,
        ResolutionActionProfile action,
        int blockedByWall)
    {
        RequireNonNegative(blockedByWall, nameof(blockedByWall));
        if (blockedByWall == 0 || !action.WallImpact)
        {
            return 0;
        }

        return System.Math.Clamp(
            MultiplyFixedPoint(blockedByWall, action.WallDamagePerUnitFixedPoint, settings.FixedPointScale),
            action.WallDamageMinimum,
            action.WallDamageMaximum);
    }

    internal static int ActiveTickBudget(int moveDistance, int activeTicks, int activeTickIndex)
    {
        RequireNonNegative(moveDistance, nameof(moveDistance));
        if (activeTicks < 1 || activeTickIndex < 0 || activeTickIndex >= activeTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(activeTickIndex));
        }

        var quotient = moveDistance / activeTicks;
        var remainder = moveDistance % activeTicks;
        return checked(quotient + (activeTickIndex < remainder ? 1 : 0));
    }

    private static void RequireNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
