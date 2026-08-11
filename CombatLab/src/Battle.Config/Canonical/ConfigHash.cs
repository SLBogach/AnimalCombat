using System.Security.Cryptography;
using Battle.Contracts.Versions;

namespace Battle.Config.Canonical;

public static class ConfigHash
{
    public static Sha256Digest Compute(ReadOnlyMemory<byte> canonicalJson)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(canonicalJson.ToArray());
        var characters = new char[hash.Length * 2];
        const string digits = "0123456789abcdef";
        for (var index = 0; index < hash.Length; index++)
        {
            characters[index * 2] = digits[hash[index] >> 4];
            characters[(index * 2) + 1] = digits[hash[index] & 0x0f];
        }

        return new Sha256Digest("sha256:" + new string(characters));
    }
}
