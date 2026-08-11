using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Battle.ConformanceTests.Config;

internal static class ConfigFixture
{
    private const string ConfigRelativePath = "config/generated/combat.balance.v0.1.json";
    private const string ManifestRelativePath = "config/generated/combat.balance.v0.1.manifest.json";

    public static byte[] ReadConfigBytes() => File.ReadAllBytes(Resolve(ConfigRelativePath));

    public static byte[] ReadManifestBytes() => File.ReadAllBytes(Resolve(ManifestRelativePath));

    public static string ReadExpectedHash()
    {
        using var manifest = JsonDocument.Parse(ReadManifestBytes());
        return manifest.RootElement.GetProperty("config_hash").GetString()!;
    }

    public static JsonObject ReadConfigObject() =>
        JsonNode.Parse(ReadConfigBytes())?.AsObject()
        ?? throw new InvalidDataException("The generated balance fixture must be a JSON object.");

    public static byte[] Mutate(Action<JsonObject> mutation)
    {
        var root = ReadConfigObject();
        mutation(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    public static JsonObject Entity(
        JsonObject root,
        string catalog,
        string idProperty,
        string id)
    {
        var array = root[catalog]?.AsArray()
            ?? throw new InvalidDataException($"Catalog '{catalog}' is missing.");
        return array
            .Select(item => item?.AsObject())
            .Single(item => string.Equals(
                item?[idProperty]?.GetValue<string>(),
                id,
                StringComparison.Ordinal))!;
    }

    public static JsonObject FirstEntity(JsonObject root, string catalog) =>
        root[catalog]?.AsArray()[0]?.AsObject()
        ?? throw new InvalidDataException($"Catalog '{catalog}' is empty.");

    public static byte[] WithDuplicateRootMember()
    {
        var text = Encoding.UTF8.GetString(ReadConfigBytes());
        const string prefix = "{\"actions\":";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The generated config no longer starts with the canonical actions member.");
        }

        return Encoding.UTF8.GetBytes(
            "{\"actions\":[],\"actions\":" + text[prefix.Length..]);
    }

    public static byte[] WithInvalidUtf8()
    {
        var source = ReadConfigBytes();
        var result = new byte[source.Length + 1];
        source.CopyTo(result, 0);
        result[^1] = 0xff;
        return result;
    }

    private static string Resolve(string relativePath)
    {
        var root = RepositoryLocator.FindCombatLabRoot();
        return Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
