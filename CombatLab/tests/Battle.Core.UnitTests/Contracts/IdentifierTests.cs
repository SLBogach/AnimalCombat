using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Core.UnitTests.Contracts;

public sealed class IdentifierTests
{
    [Theory]
    [InlineData("bear")]
    [InlineData("bear_heavy_01")]
    [InlineData("a")]
    public void StableId_AcceptsCanonicalAscii(string value)
    {
        var id = new StableId(value);

        Assert.Equal(value, id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bear")]
    [InlineData("1bear")]
    [InlineData("bear-heavy")]
    [InlineData("медведь")]
    public void StableId_RejectsNonCanonicalValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new StableId(value));
    }

    [Fact]
    public void StableId_UsesOrdinalOrdering()
    {
        var uppercaseLikeBoundary = new StableId("z");
        var lower = new StableId("aa");

        Assert.True(uppercaseLikeBoundary.CompareTo(lower) > 0);
    }

    [Theory]
    [InlineData("battle.core/0.1.0")]
    [InlineData("pcg32/1")]
    [InlineData("v0.1")]
    public void ArtifactVersion_AcceptsExternalIdShape(string value)
    {
        Assert.Equal(value, new ArtifactVersion(value).ToString());
    }

    [Fact]
    public void EventId_FormatsFullTenDigitSequence()
    {
        var eventId = EventId.FromSequence(EventId.MaximumSequence);

        Assert.Equal("evt-9999999999", eventId.Value);
    }

    [Theory]
    [InlineData("evt-000000000")]
    [InlineData("evt-00000000000")]
    [InlineData("evt-000000000x")]
    public void EventId_RejectsWrongShape(string value)
    {
        Assert.False(EventId.TryParse(value, out _));
    }

    [Theory]
    [InlineData("dec-fighter_a-000001")]
    [InlineData("dec-fighter_b-999999")]
    public void DecisionId_AcceptsSchemaShape(string value)
    {
        Assert.True(DecisionId.TryParse(value, out var decisionId));
        Assert.Equal(value, decisionId.Value);
    }

    [Fact]
    public void Sha256Digest_RejectsUppercaseHex()
    {
        var uppercaseDigest = $"sha256:{new string('A', 64)}";

        Assert.False(Sha256Digest.TryParse(uppercaseDigest, out _));
    }

    [Theory]
    [InlineData("DamageApplied")]
    [InlineData("UnknownStableId")]
    public void ReasonCode_RemainsAnOpenValidatedValue(string value)
    {
        Assert.True(ReasonCode.TryParse(value, out var code));
        Assert.Equal(value, code.Value);
    }
}
