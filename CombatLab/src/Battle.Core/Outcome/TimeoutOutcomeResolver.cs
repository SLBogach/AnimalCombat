using Battle.Contracts.Ids;
using Battle.Contracts.Results;

namespace Battle.Core.Outcome;

internal readonly record struct TimeoutOutcome(
    BattleOutcome Outcome,
    FighterId? WinnerFighterId,
    BattleEndReason EndReason,
    long LeftCrossProduct,
    long RightCrossProduct);

internal static class TimeoutOutcomeResolver
{
    internal static TimeoutOutcome Resolve(
        int fighterAHealth,
        int fighterAMaximumHealth,
        int fighterBHealth,
        int fighterBMaximumHealth)
    {
        var comparison = TimeoutHealthComparer.Compare(
            fighterAHealth,
            fighterAMaximumHealth,
            fighterBHealth,
            fighterBMaximumHealth);
        var left = checked((long)fighterAHealth * fighterBMaximumHealth);
        var right = checked((long)fighterBHealth * fighterAMaximumHealth);

        return comparison switch
        {
            > 0 => new TimeoutOutcome(
                BattleOutcome.FighterAWin,
                FighterId.FighterA,
                BattleEndReason.TimeoutHealthFraction,
                left,
                right),
            < 0 => new TimeoutOutcome(
                BattleOutcome.FighterBWin,
                FighterId.FighterB,
                BattleEndReason.TimeoutHealthFraction,
                left,
                right),
            _ => new TimeoutOutcome(
                BattleOutcome.Draw,
                null,
                BattleEndReason.TimeoutEqualHealthFraction,
                left,
                right),
        };
    }
}

internal readonly record struct ImmediateOutcome(
    BattleOutcome Outcome,
    FighterId? WinnerFighterId,
    BattleEndReason EndReason);

internal static class ImmediateOutcomeResolver
{
    internal static ImmediateOutcome? Resolve(int fighterAHealth, int fighterBHealth)
    {
        if (fighterAHealth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterAHealth));
        }

        if (fighterBHealth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fighterBHealth));
        }

        if (fighterAHealth == 0 && fighterBHealth == 0)
        {
            return new ImmediateOutcome(BattleOutcome.Draw, null, BattleEndReason.DoubleKO);
        }

        if (fighterAHealth == 0)
        {
            return new ImmediateOutcome(
                BattleOutcome.FighterBWin,
                FighterId.FighterB,
                BattleEndReason.Defeat);
        }

        if (fighterBHealth == 0)
        {
            return new ImmediateOutcome(
                BattleOutcome.FighterAWin,
                FighterId.FighterA,
                BattleEndReason.Defeat);
        }

        return null;
    }
}
