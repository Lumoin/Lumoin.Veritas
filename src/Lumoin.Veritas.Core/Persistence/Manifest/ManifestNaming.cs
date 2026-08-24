using System;
using System.Globalization;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// The fixed artifact-name vocabulary the manifest commit and recovery share within a
/// <see cref="Lumoin.Veritas.Core.Persistence.PersistenceStore"/>: the live CURRENT pointer, its
/// transient staging name, the generation-stamped manifest images, and the retained CURRENT copies.
/// The generation stamp is zero-padded to a fixed width so a lexical listing of the store sorts the
/// same way the generations do, and so a prefix list cleanly separates the retained CURRENT copies
/// from the live pointer and from the manifests.
/// </summary>
internal static class ManifestNaming
{
    /// <summary>The name of the single live CURRENT pointer — the one artifact an atomic publish makes live.</summary>
    internal const string CurrentPointerName = "current";

    /// <summary>The transient name a CURRENT pointer is staged under before the atomic rename to <see cref="CurrentPointerName"/>.</summary>
    internal const string CurrentStagingName = "current.staging";

    /// <summary>The prefix the retained per-generation CURRENT copies share; the live pointer and the staging name do not begin with it.</summary>
    internal const string RetainedCurrentPrefix = "current-";

    /// <summary>The prefix the generation-stamped manifest images share.</summary>
    internal const string ManifestPrefix = "manifest-";

    /// <summary>The zero-padded width the generation stamp is formatted to: 20 digits covers the whole non-negative <see cref="long"/> range so a lexical sort matches the numeric order.</summary>
    private const string GenerationFormat = "D20";

    /// <summary>Builds the name of the manifest image for a generation.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The manifest artifact name.</returns>
    internal static string ManifestName(long generation)
    {
        return ManifestPrefix + generation.ToString(GenerationFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the name of the retained CURRENT copy for a generation.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The retained CURRENT artifact name.</returns>
    internal static string RetainedCurrentName(long generation)
    {
        return RetainedCurrentPrefix + generation.ToString(GenerationFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Parses the generation a prefixed artifact name stamps, rejecting any name that does not carry the prefix followed by a non-negative integer.</summary>
    /// <param name="artifactName">The artifact name to parse.</param>
    /// <param name="prefix">The expected prefix (<see cref="ManifestPrefix"/> or <see cref="RetainedCurrentPrefix"/>).</param>
    /// <param name="generation">The parsed generation when the name matches; 0 otherwise.</param>
    /// <returns><see langword="true"/> when the name is the prefix followed by a parseable non-negative generation.</returns>
    internal static bool TryParseGeneration(string artifactName, string prefix, out long generation)
    {
        generation = 0;
        if(artifactName is null || !artifactName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits = artifactName.AsSpan(prefix.Length);

        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out generation);
    }
}
