using System.Collections.ObjectModel;
using Battle.Contracts.Ids;
using Battle.Contracts.Versions;

namespace Battle.Contracts.Requests;

public enum NormalizationMode
{
    None,
    NormalizedRating,
}

public sealed class ModeRulesSnapshot
{
    private readonly ReadOnlyCollection<StableId> _allowedAnimalIds;
    private readonly ReadOnlyCollection<StableId> _allowedActionIds;
    private readonly ReadOnlyCollection<StableId> _allowedPassiveIds;
    private readonly ReadOnlyCollection<StableId> _allowedGearIds;
    private readonly ReadOnlyCollection<StableId> _allowedTacticIds;

    public ModeRulesSnapshot(
        StableId id,
        ArtifactVersion version,
        NormalizationMode normalizationMode,
        IEnumerable<StableId> allowedAnimalIds,
        IEnumerable<StableId> allowedActionIds,
        IEnumerable<StableId> allowedPassiveIds,
        IEnumerable<StableId> allowedGearIds,
        IEnumerable<StableId> allowedTacticIds)
    {
        if (string.IsNullOrEmpty(id.Value))
        {
            throw new ArgumentException("A mode rules ID is required.", nameof(id));
        }

        if (normalizationMode is not NormalizationMode.None and
            not NormalizationMode.NormalizedRating)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizationMode));
        }

        Id = id;
        Version = version;
        NormalizationMode = normalizationMode;
        _allowedAnimalIds = CopyAllowlist(allowedAnimalIds, nameof(allowedAnimalIds));
        _allowedActionIds = CopyAllowlist(allowedActionIds, nameof(allowedActionIds));
        _allowedPassiveIds = CopyAllowlist(allowedPassiveIds, nameof(allowedPassiveIds));
        _allowedGearIds = CopyAllowlist(allowedGearIds, nameof(allowedGearIds));
        _allowedTacticIds = CopyAllowlist(allowedTacticIds, nameof(allowedTacticIds));
    }

    public StableId Id { get; }

    public ArtifactVersion Version { get; }

    public NormalizationMode NormalizationMode { get; }

    public IReadOnlyList<StableId> AllowedAnimalIds => _allowedAnimalIds;

    public IReadOnlyList<StableId> AllowedActionIds => _allowedActionIds;

    public IReadOnlyList<StableId> AllowedPassiveIds => _allowedPassiveIds;

    public IReadOnlyList<StableId> AllowedGearIds => _allowedGearIds;

    public IReadOnlyList<StableId> AllowedTacticIds => _allowedTacticIds;

    private static ReadOnlyCollection<StableId> CopyAllowlist(
        IEnumerable<StableId> source,
        string parameterName)
    {
        if (source is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var items = source.ToArray();
        if (items.Length == 0)
        {
            throw new ArgumentException("A mode allowlist cannot be empty.", parameterName);
        }

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Value))
            {
                throw new ArgumentException("A mode allowlist cannot contain a default ID.", parameterName);
            }

            if (StringComparer.Ordinal.Equals(item.Value, "all"))
            {
                throw new ArgumentException(
                    "The sentinel ID 'all' is forbidden in an explicit mode allowlist.",
                    parameterName);
            }
        }

        Array.Sort(items, static (left, right) => left.CompareTo(right));
        for (var index = 1; index < items.Length; index++)
        {
            if (items[index - 1] == items[index])
            {
                throw new ArgumentException("A mode allowlist cannot contain duplicate IDs.", parameterName);
            }
        }

        return new ReadOnlyCollection<StableId>(items);
    }
}
