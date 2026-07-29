using Battle.Contracts.Ids;

namespace Battle.Contracts.Versions;

public readonly record struct ArtifactVersion : IComparable<ArtifactVersion>
{
    public ArtifactVersion(string value)
    {
        Value = new ExternalId(value);
    }

    public ExternalId Value { get; }

    public int CompareTo(ArtifactVersion other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
