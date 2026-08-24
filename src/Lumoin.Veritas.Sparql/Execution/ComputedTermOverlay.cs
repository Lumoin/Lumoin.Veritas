using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// A query-scoped extension of the data <see cref="TermDictionary"/>'s encoded-id space, holding the terms a
/// query computes (a <c>BIND</c>/<c>Extend</c> result, say) that are not in the data graph — so a
/// <see cref="SolutionTable"/> column stays a uniform <c>uint</c> of encoded ids without ever interning the
/// query's ephemera into the shared dictionary.
/// </summary>
/// <remarks>
/// <para>
/// The top id bit (<see cref="OverlayBit"/>) is reserved for overlay ids; data-dictionary ids occupy the lower
/// range (a data dictionary reaching 2³¹ terms is beyond any in-memory, let alone mobile/browser, budget). Encoding
/// is <b>canonical-reuse-first</b>: a computed term that already exists in the data gets its data id (no mutation,
/// and the column remains joinable/filterable against data columns by id); only a genuinely new term is interned
/// here, deduped by term equality so the same computed term always gets the same id within the evaluation. One
/// overlay instance is shared across an evaluation, so two sibling <c>BIND</c>s that later join never collide on an
/// overlay id.
/// </para>
/// <para>
/// As with the columnar term-(in)equality filter, id equality is RDF-term equality (<c>sameTerm</c>); the columnar
/// fast paths that key on these ids are sound on that basis, and the value-semantics cases fall back to the row
/// path exactly as elsewhere. The overlay is single-writer during one evaluation, then read-only, like the rest of
/// the columnar machinery.
/// </para>
/// </remarks>
internal sealed class ComputedTermOverlay
{
    /// <summary>The reserved high bit marking an overlay id; data-dictionary ids never set it.</summary>
    private const uint OverlayBit = 0x8000_0000u;

    private readonly TermDictionary dictionary;

    private readonly Dictionary<RdfTerm, uint> interned = [];

    private readonly List<RdfTerm> terms = [];

    /// <summary>Constructs an overlay extending the given data dictionary.</summary>
    /// <param name="dictionary">The data dictionary whose ids the overlay's lower range coincides with.</param>
    public ComputedTermOverlay(TermDictionary dictionary)
    {
        this.dictionary = dictionary;
    }

    /// <summary>Whether an encoded id denotes an overlay (query-computed) term rather than a data-dictionary term.</summary>
    /// <param name="encoded">The encoded id.</param>
    /// <returns><see langword="true"/> when the id is an overlay id.</returns>
    public static bool IsOverlay(uint encoded)
    {
        return (encoded & OverlayBit) != 0;
    }

    /// <summary>
    /// Encodes a computed term to an id: the data id when the term is already in the dictionary (canonical reuse,
    /// no mutation), otherwise a deduped overlay id.
    /// </summary>
    /// <param name="term">The computed term.</param>
    /// <returns>The encoded id.</returns>
    public uint Encode(RdfTerm term)
    {
        TermId dataId = dictionary.GetIdOrDefault(term);
        if(!dataId.IsNone)
        {
            return dataId.Encoded;
        }

        if(interned.TryGetValue(term, out uint existing))
        {
            return existing;
        }

        //The overlay id carries the reserved bit set, so it is never zero (unbound) nor a data id.
        uint id = OverlayBit | (uint)terms.Count;
        terms.Add(term);
        interned[term] = id;

        return id;
    }

    /// <summary>Resolves an overlay id (one for which <see cref="IsOverlay"/> is <see langword="true"/>) to its computed term.</summary>
    /// <param name="encoded">The overlay id.</param>
    /// <returns>The computed term.</returns>
    public RdfTerm Resolve(uint encoded)
    {
        return terms[(int)(encoded & ~OverlayBit)];
    }
}
