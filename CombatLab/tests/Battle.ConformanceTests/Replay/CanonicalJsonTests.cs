using System.Text;
using System.Text.Json;
using Canonicalizer = Battle.Replay.CanonicalJson.CanonicalJson;
using ReplayDigest = Battle.Replay.Integrity.ReplayIntegrity;

namespace Battle.ConformanceTests.Replay;

public sealed class CanonicalJsonTests
{
    private const string ExpectedInputDigest =
        "sha256:26bd0244bada8360818da1de29c926c09ae1f2e31915654c5e86fd954b2cca5b";

    private const string ExpectedFinalDigest =
        "sha256:bdf470b43b23569fbfbe053772fdc4684b531e48b4a88d6b3b577ad122a1e69e";

    [Fact]
    public void Canonicalize_SortsObjectsPreservesArraysAndIsIdempotent()
    {
        const string source = """
            {
              "z": "x",
              "array": [3, 1, 2],
              "m": null,
              "a": { "b": 2, "a": 1 }
            }
            """;
        const string expected =
            "{\"a\":{\"a\":1,\"b\":2},\"array\":[3,1,2],\"m\":null,\"z\":\"x\"}";

        var canonical = Canonicalizer.Canonicalize(Utf8(source));

        Assert.Equal(expected, Encoding.UTF8.GetString(canonical));
        Assert.True(canonical.AsSpan().SequenceEqual(Canonicalizer.Canonicalize(canonical)));
        Assert.Equal(
            "sha256:370844335fbe7d1051af6252ea285dd28e208bca88aafed3acf321dac83dbbd4",
            Canonicalizer.ComputeDigest(Utf8(source)).ToString());
    }

    [Fact]
    public void Canonicalize_RejectsUtf8ByteOrderMark()
    {
        var json = Utf8("{}");
        var withByteOrderMark = new byte[json.Length + 3];
        withByteOrderMark[0] = 0xef;
        withByteOrderMark[1] = 0xbb;
        withByteOrderMark[2] = 0xbf;
        json.CopyTo(withByteOrderMark.AsSpan(3));

        Assert.Throws<ArgumentException>(() => Canonicalizer.Canonicalize(withByteOrderMark));
    }

    [Theory]
    [InlineData("{\"value\":1,\"value\":2}")]
    [InlineData("{\"значение\":1}")]
    [InlineData("{\"value\":1.5}")]
    [InlineData("{\"value\":1e3}")]
    [InlineData("{\"value\":-0}")]
    public void Canonicalize_RejectsCanonicalSubsetViolations(string source)
    {
        Assert.Throws<ArgumentException>(() => Canonicalizer.Canonicalize(Utf8(source)));
    }

    [Theory]
    [InlineData("{\"value\":1/* comment */}")]
    [InlineData("{\"value\":1,}")]
    public void Canonicalize_RejectsCommentsAndTrailingCommas(string source)
    {
        Assert.ThrowsAny<JsonException>(() => Canonicalizer.Canonicalize(Utf8(source)));
    }

    [Theory]
    [InlineData("replay-standard.example.json")]
    [InlineData("replay-diagnostic.example.json")]
    public void MachineReplay_ReproducesInputAndEveryEventChainDigest(string fixtureName)
    {
        var bytes = File.ReadAllBytes(GetReplayFixturePath(fixtureName));
        using var document = JsonDocument.Parse(bytes);
        var replay = document.RootElement;
        var events = replay.GetProperty("events");
        var integrity = replay.GetProperty("integrity");

        Assert.Equal(ExpectedInputDigest, ReplayDigest.ComputeInputDigest(bytes).ToString());
        Assert.Equal(ExpectedInputDigest, integrity.GetProperty("input_digest").GetString());
        Assert.Equal(13, events.GetArrayLength());
        Assert.Equal(13, integrity.GetProperty("event_count").GetInt32());

        var previousDigest = ExpectedInputDigest;
        for (var sequence = 0; sequence < events.GetArrayLength(); sequence++)
        {
            var combatEvent = events[sequence];
            var eventIntegrity = combatEvent.GetProperty("integrity");

            Assert.Equal(sequence, combatEvent.GetProperty("sequence").GetInt64());
            Assert.Equal($"evt-{sequence:D10}", combatEvent.GetProperty("event_id").GetString());
            Assert.Equal(previousDigest, eventIntegrity.GetProperty("prev_digest").GetString());

            var computedDigest = ReplayDigest.ComputeEventDigest(
                Utf8(combatEvent.GetRawText())).ToString();
            Assert.Equal(eventIntegrity.GetProperty("event_digest").GetString(), computedDigest);
            previousDigest = computedDigest;
        }

        Assert.Equal(ExpectedFinalDigest, previousDigest);
        Assert.Equal(ExpectedFinalDigest, integrity.GetProperty("final_digest").GetString());
    }

    [Fact]
    public void MachineReplay_ReproducesBothKeyframeStateDigests()
    {
        var bytes = File.ReadAllBytes(GetReplayFixturePath("replay-standard.example.json"));
        using var document = JsonDocument.Parse(bytes);
        var keyframes = document.RootElement.GetProperty("keyframes");

        Assert.Equal(2, keyframes.GetArrayLength());
        Assert.Equal(
            "sha256:a5b2642d36dbd5514893cfe3e55b705196a052aa7dc8b24da9dd42255c2639b9",
            ReplayDigest.ComputeKeyframeStateDigest(Utf8(keyframes[0].GetRawText())).ToString());
        Assert.Equal(
            "sha256:4e64855179860635d72aab284128657a6ad6364d1ecc84f721d58bb18c0cbeef",
            ReplayDigest.ComputeKeyframeStateDigest(Utf8(keyframes[1].GetRawText())).ToString());
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string GetReplayFixturePath(string fixtureName) =>
        Path.Combine(
            RepositoryLocator.FindCombatLabRoot(),
            "fixtures",
            "replay",
            "v0.1",
            fixtureName);
}
