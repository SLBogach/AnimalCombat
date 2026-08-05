using Battle.Contracts.Ids;

namespace Battle.Core.Engine;

internal sealed class EngineInvariantException : Exception
{
    internal EngineInvariantException(ReasonCode code, string phase, string message)
        : base(message)
    {
        Code = code;
        Phase = phase;
    }

    internal ReasonCode Code { get; }

    internal string Phase { get; }
}

internal static class EngineFailureCodes
{
    internal static ReasonCode EventCapExceeded { get; } = new("EventCapExceeded");

    internal static ReasonCode InvalidStateTransition { get; } = new("InvalidStateTransition");

    internal static ReasonCode NoLegalSystemAction { get; } = new("NoLegalSystemAction");

    internal static ReasonCode TerminalMutation { get; } = new("TerminalMutation");

    internal static ReasonCode TickLimitExceeded { get; } = new("TickLimitExceeded");

    internal static ReasonCode ZeroProgress { get; } = new("ZeroProgress");
}
