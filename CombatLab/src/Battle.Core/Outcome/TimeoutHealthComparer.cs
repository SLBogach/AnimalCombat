namespace Battle.Core.Outcome;

/// <summary>
/// Compares remaining-health fractions for timeout resolution without division.
/// </summary>
public static class TimeoutHealthComparer
{
    public static int Compare(
        int leftCurrentHealth,
        int leftMaximumHealth,
        int rightCurrentHealth,
        int rightMaximumHealth)
    {
        ValidateHealth(
            leftCurrentHealth,
            leftMaximumHealth,
            nameof(leftCurrentHealth),
            nameof(leftMaximumHealth));
        ValidateHealth(
            rightCurrentHealth,
            rightMaximumHealth,
            nameof(rightCurrentHealth),
            nameof(rightMaximumHealth));

        var leftCrossProduct = checked((long)leftCurrentHealth * rightMaximumHealth);
        var rightCrossProduct = checked((long)rightCurrentHealth * leftMaximumHealth);

        if (leftCrossProduct < rightCrossProduct)
        {
            return -1;
        }

        return leftCrossProduct > rightCrossProduct ? 1 : 0;
    }

    private static void ValidateHealth(
        int currentHealth,
        int maximumHealth,
        string currentParameterName,
        string maximumParameterName)
    {
        if (maximumHealth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                maximumParameterName,
                maximumHealth,
                "Maximum health must be greater than zero.");
        }

        if (currentHealth < 0 || currentHealth > maximumHealth)
        {
            throw new ArgumentOutOfRangeException(
                currentParameterName,
                currentHealth,
                "Current health must be between zero and maximum health.");
        }
    }
}
