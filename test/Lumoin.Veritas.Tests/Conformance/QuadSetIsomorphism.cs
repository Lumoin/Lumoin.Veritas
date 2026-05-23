using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Tests.Conformance;

/// <summary>
/// Compares two RDF quad sets for blank-node-aware equivalence
/// by running both through RDFC-1.0 canonicalisation and
/// comparing the canonical N-Quads byte-for-byte.
/// </summary>
/// <remarks>
/// <para>
/// W3C evaluation tests treat two datasets as equivalent when
/// they differ only in the choice of blank-node identifiers
/// — the well-known graph-isomorphism problem reduced under
/// the canonicalisation algorithm RDFC-1.0 specifies. The
/// project's canonicaliser implements that algorithm; this
/// helper is a thin wrapper that lifts it to a boolean
/// comparison.
/// </para>
/// </remarks>
internal static class QuadSetIsomorphism
{
    /// <summary>
    /// Returns <c>true</c> when the two quad sets are isomorphic
    /// under blank-node relabelling.
    /// </summary>
    /// <param name="left">The first quad set.</param>
    /// <param name="right">The second quad set.</param>
    /// <returns><c>true</c> when both sets canonicalise to the same N-Quads bytes.</returns>
    public static bool AreIsomorphic(IReadOnlyList<Quad> left, IReadOnlyList<Quad> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if(left.Count != right.Count)
        {
            return false;
        }

        string leftCanonical = RdfCanonicalizer.Canonicalize(left, SHA256.HashData);
        string rightCanonical = RdfCanonicalizer.Canonicalize(right, SHA256.HashData);
        return string.Equals(leftCanonical, rightCanonical, StringComparison.Ordinal);
    }
}
