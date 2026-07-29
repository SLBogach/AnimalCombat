namespace Battle.Contracts.Versions;

public static class ContractVersions
{
    public static ArtifactVersion Engine { get; } = new("battle.core/0.1.0");

    public static ArtifactVersion BalanceSchema { get; } = new("combat.balance/0.1");

    public static ArtifactVersion Rng { get; } = new("pcg32/1");

    public static ArtifactVersion Ordering { get; } = new("tick-pipeline/1");

    public static ArtifactVersion Replay { get; } = new("combat.replay/0.1");

    public static ArtifactVersion Event { get; } = new("combat.event/0.1");

    public static ArtifactVersion Rejection { get; } = new("combat.rejection/0.1");

    public static ArtifactVersion Presentation { get; } = new("combat.presentation/0.1");
}
