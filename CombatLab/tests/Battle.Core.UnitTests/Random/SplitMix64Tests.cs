using Battle.Core.Random;

namespace Battle.Core.UnitTests.Random;

public sealed class SplitMix64Tests
{
    public static TheoryData<ulong, ulong> GoldenVectors =>
        new()
        {
            { 0UL, 16_294_208_416_658_607_535UL },
            { 1UL, 10_451_216_379_200_822_465UL },
            { ulong.MaxValue, 16_490_336_266_968_443_936UL },
            { 0x4445434953494F4EUL, 10_810_203_803_132_911_224UL },
            { 0x5245534F4C555449UL, 11_308_486_435_902_413_071UL },
        };

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    public void Mix_MatchesGoldenVectors(ulong value, ulong expected)
    {
        Assert.Equal(expected, SplitMix64.Mix(value));
    }
}
