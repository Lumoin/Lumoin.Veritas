using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core.Persistence.Manifest;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The fixed store-name vocabulary a repair publish stamps its healed artifacts and loss records under, shared
/// by the <see cref="GenerationCommitCoordinator"/> that writes them and the collector that reclaims the
/// superseded ones so the naming convention lives in one place. A healed image is named
/// <c>{role}-{generation}</c> (the re-derived or parity-restored artifact under a fresh generation-stamped
/// name); the loss record is named <c>losses-{generation}</c>. The generation stamp is zero-padded to a fixed
/// width so a lexical listing sorts the same way the generations do.
/// </summary>
internal static class HealedArtifactNaming
{
    /// <summary>The zero-padded width the generation stamp is formatted to: 20 digits cover the whole non-negative generation range so a lexical sort matches the numeric order.</summary>
    private const string GenerationFormat = "D20";

    /// <summary>The fixed prefix a loss record's store name carries; a reader lists it to enumerate the loss records the store holds.</summary>
    internal const string LossRecordPrefix = "losses-";

    /// <summary>Builds a healed artifact's fresh, generation-stamped store name from its role.</summary>
    /// <param name="role">The artifact's manifest role.</param>
    /// <param name="generation">The healed generation.</param>
    /// <returns>The store name.</returns>
    internal static string HealedArtifactName(ManifestFileRole role, long generation)
    {
        return role.Name + "-" + generation.ToString(GenerationFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the store name of the loss record co-versioned with a healed generation.</summary>
    /// <param name="generation">The healed generation.</param>
    /// <returns>The loss-record store name.</returns>
    internal static string LossRecordName(long generation)
    {
        return LossRecordPrefix + generation.ToString(GenerationFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The store-name prefixes a repair publish can stamp — one per built-in manifest role (a healed image is
    /// named <c>{role}-{generation}</c>) plus the loss-record prefix — that the superseded-artifact collector
    /// enumerates to reclaim healed leftovers. Every built-in role is listed so a role that becomes re-derivable
    /// later is already covered; a prefix that no healed image ever carries simply lists nothing.
    /// </summary>
    internal static IReadOnlyList<string> CollectiblePrefixes { get; } =
    [
        ManifestFileRole.DataSegment.Name + "-",
        ManifestFileRole.Sidecar.Name + "-",
        ManifestFileRole.Sketch.Name + "-",
        ManifestFileRole.Parity.Name + "-",
        ManifestFileRole.Stats.Name + "-",
        ManifestFileRole.Dictionary.Name + "-",
        ManifestFileRole.NamedGraphSegment.Name + "-",
        LossRecordPrefix,
    ];
}
