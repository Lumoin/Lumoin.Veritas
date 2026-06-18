using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.ParserTests.Conformance;

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

        //An RDF graph is a set of triples, so the comparison is over the set of canonical
        //statement lines: a triple repeated in one side but present once in the other still
        //denotes the same graph. Canonicalization assigns deterministic blank-node labels, so
        //identical triples produce identical canonical lines on both sides.
        string leftCanonical = RdfCanonicalizer.Canonicalize(left, SHA256.HashData);
        string rightCanonical = RdfCanonicalizer.Canonicalize(right, SHA256.HashData);
        return CanonicalLineSet(leftCanonical).SetEquals(CanonicalLineSet(rightCanonical));
    }

    private static HashSet<string> CanonicalLineSet(string canonical)
    {
        HashSet<string> lines = new(StringComparer.Ordinal);
        foreach(string line in canonical.Split('\n'))
        {
            if(line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}
