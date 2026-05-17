using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core;

/// <summary>
/// A bidirectional mapping between <see cref="RdfTerm"/> instances and
/// <see cref="TermId"/> handles.
/// </summary>
/// <remarks>
/// <para>
/// All graph storage and query operations work with encoded triples rather than
/// full RDF terms. This dictionary provides the encoding/decoding layer. Terms
/// are assigned sequential identifiers starting from <c>1</c>; identifier
/// <c>0</c> is reserved as the <see cref="TermId.None"/> sentinel.
/// </para>
/// <para>
/// <b>Typed overloads.</b> When the caller already knows the kind of term it is
/// inserting, prefer the kind-specific overloads (e.g.
/// <see cref="GetOrAdd(NamedNode)"/>) to obtain a narrowed
/// <see cref="IriId"/> / <see cref="BlankNodeId"/> / <see cref="LiteralId"/>
/// / <see cref="TripleTermId"/> directly. This avoids re-resolving the term
/// just to validate its kind, as the generic
/// <see cref="GetOrAdd(RdfTerm)"/> would require downstream.
/// </para>
/// <para>
/// The dictionary does not own the <see cref="Utf8String"/> memory inside the
/// terms. That memory is managed by the <see cref="Utf8StringPool"/> from which
/// the terms were created.
/// </para>
/// </remarks>
[DebuggerDisplay("TermDictionary Count={Count}")]
public sealed class TermDictionary
{
    /// <summary>
    /// Forward mapping from RDF terms to their encoded identifiers. Stored
    /// values are 1-based external identifiers, equal to the
    /// <see cref="TermId.Encoded"/> handle returned to callers.
    /// </summary>
    private Dictionary<RdfTerm, uint> TermToId { get; } = [];

    /// <summary>
    /// Reverse mapping from encoded identifiers to RDF terms. Indexed
    /// internally as 0-based; the external 1-based identifier is
    /// computed by adding one to the index. Decoupling the internal
    /// list index from the external identifier keeps the
    /// <see cref="TermId.None"/> sentinel reserved at external
    /// identifier <c>0</c> without wasting a list slot.
    /// </summary>
    private List<RdfTerm> IdToTerm { get; } = [];

    /// <summary>
    /// Gets the number of terms in the dictionary.
    /// </summary>
    public int Count => IdToTerm.Count;

    /// <summary>
    /// Returns the identifier for the given term, assigning a new one if the term
    /// has not been seen before.
    /// </summary>
    /// <param name="term">The RDF term to encode.</param>
    /// <returns>The <see cref="TermId"/> for <paramref name="term"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    public TermId GetOrAdd(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        if(TermToId.TryGetValue(term, out uint existingId))
        {
            return TermId.FromEncoded(existingId);
        }

        IdToTerm.Add(term);
        uint newId = (uint)IdToTerm.Count;
        TermToId[term] = newId;

        return TermId.FromEncoded(newId);
    }

    /// <summary>
    /// Returns an <see cref="IriId"/> for the given named node, assigning a new
    /// identifier if necessary. The returned handle is narrowed by construction
    /// and does not require subsequent validation.
    /// </summary>
    /// <param name="iri">The named node (IRI term) to encode.</param>
    /// <returns>An <see cref="IriId"/> for <paramref name="iri"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="iri"/> is <c>null</c>.</exception>
    public IriId GetOrAdd(NamedNode iri)
    {
        ArgumentNullException.ThrowIfNull(iri);
        return IriId.FromUnchecked(GetOrAdd((RdfTerm)iri));
    }

    /// <summary>
    /// Returns a <see cref="BlankNodeId"/> for the given blank node, assigning a new
    /// identifier if necessary. The returned handle is narrowed by construction
    /// and does not require subsequent validation.
    /// </summary>
    /// <param name="blank">The blank node to encode.</param>
    /// <returns>A <see cref="BlankNodeId"/> for <paramref name="blank"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blank"/> is <c>null</c>.</exception>
    public BlankNodeId GetOrAdd(BlankNode blank)
    {
        ArgumentNullException.ThrowIfNull(blank);
        return BlankNodeId.FromUnchecked(GetOrAdd((RdfTerm)blank));
    }

    /// <summary>
    /// Returns a <see cref="LiteralId"/> for the given literal, assigning a new
    /// identifier if necessary. The returned handle is narrowed by construction.
    /// </summary>
    /// <param name="literal">The literal to encode.</param>
    /// <returns>A <see cref="LiteralId"/> for <paramref name="literal"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="literal"/> is <c>null</c>.</exception>
    public LiteralId GetOrAdd(Literal literal)
    {
        ArgumentNullException.ThrowIfNull(literal);
        return LiteralId.FromUnchecked(GetOrAdd((RdfTerm)literal));
    }

    /// <summary>
    /// Returns a <see cref="TripleTermId"/> for the given triple term (RDF 1.2),
    /// assigning a new identifier if necessary.
    /// </summary>
    /// <param name="tripleTerm">The triple term to encode.</param>
    /// <returns>A <see cref="TripleTermId"/> for <paramref name="tripleTerm"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tripleTerm"/> is <c>null</c>.</exception>
    public TripleTermId GetOrAdd(TripleTerm tripleTerm)
    {
        ArgumentNullException.ThrowIfNull(tripleTerm);
        return TripleTermId.FromUnchecked(GetOrAdd((RdfTerm)tripleTerm));
    }

    /// <summary>
    /// Returns the identifier for the given term, or <see cref="TermId.None"/>
    /// if the term is not in the dictionary.
    /// </summary>
    /// <param name="term">The RDF term to look up.</param>
    /// <returns>The identifier, or <see cref="TermId.None"/> if not found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    public TermId GetIdOrDefault(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return TermToId.TryGetValue(term, out uint id) ? TermId.FromEncoded(id) : TermId.None;
    }

    /// <summary>
    /// Resolves an identifier back to the original <see cref="RdfTerm"/>.
    /// </summary>
    /// <param name="id">The identifier to resolve.</param>
    /// <returns>The RDF term corresponding to <paramref name="id"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The encoded value is <c>0</c> (the <see cref="TermId.None"/> sentinel)
    /// or greater than <see cref="Count"/>.
    /// </exception>
    public RdfTerm Resolve(TermId id)
    {
        return Resolve(id.Encoded);
    }

    /// <summary>
    /// Resolves a raw encoded identifier back to the original <see cref="RdfTerm"/>.
    /// </summary>
    /// <param name="encoded">The raw encoded identifier to resolve.</param>
    /// <returns>The RDF term corresponding to <paramref name="encoded"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="encoded"/> is <c>0</c> or greater than <see cref="Count"/>.
    /// </exception>
    /// <remarks>
    /// Use this overload when working against a raw-<see cref="uint"/> storage
    /// layer. Prefer <see cref="Resolve(TermId)"/> within the typed pipeline.
    /// </remarks>
    public RdfTerm Resolve(uint encoded)
    {
        if(encoded == 0 || encoded > (uint)IdToTerm.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(encoded), encoded,
                $"Term identifier must be between 1 and {IdToTerm.Count}; '0' is reserved for TermId.None.");
        }

        return IdToTerm[(int)(encoded - 1)];
    }

    /// <summary>
    /// Determines whether the dictionary contains the given term.
    /// </summary>
    /// <param name="term">The term to check.</param>
    /// <returns><c>true</c> if the term has been assigned an identifier; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    public bool Contains(RdfTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return TermToId.ContainsKey(term);
    }
}
