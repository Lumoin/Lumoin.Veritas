using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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
    /// Guards every read of and every mutation to <see cref="TermToId"/> and <see cref="IdToTerm"/>. The
    /// contract is two-fold: parallel update executions mint terms concurrently through
    /// <see cref="GetOrAdd(RdfTerm)"/> (the optimistic-concurrency update path mints into the one shared
    /// dictionary), and the durable dataset journal captures the newly-minted id range atomically against
    /// those mints through <see cref="CaptureBeyond(int)"/>. The kind-specific <c>GetOrAdd</c> overloads and
    /// <see cref="Resolve(TermId)"/> reach the shared state only through the guarded core members, so they
    /// need no lock of their own.
    /// </summary>
    private Lock Mutex { get; } = new();

    /// <summary>
    /// Gets the number of terms in the dictionary.
    /// </summary>
    public int Count
    {
        get
        {
            lock(Mutex)
            {
                return IdToTerm.Count;
            }
        }
    }

    /// <summary>
    /// A stable replication identity for this dictionary's identifier-to-term mapping. Structural reconciliation
    /// transfers term IDENTIFIERS, not terms, so two replicas may exchange a structural sketch only when their
    /// dictionaries share this epoch — otherwise an identifier denotes a different term on each side. Independently
    /// constructed dictionaries carry different epochs; <c>0</c> is the unspecified epoch a non-replicating
    /// dictionary uses, and it reconciles only with another unspecified-epoch dictionary. The replication layer
    /// mints a unique epoch (it owns the entropy seam); the dictionary only carries the value it is given.
    /// </summary>
    public ulong Epoch { get; }

    /// <summary>Creates a dictionary carrying a replication epoch.</summary>
    /// <param name="epoch">The dictionary's replication epoch; <c>0</c> (the default) is the unspecified epoch for a non-replicating dictionary.</param>
    public TermDictionary(ulong epoch = 0)
    {
        Epoch = epoch;
    }

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
        lock(Mutex)
        {
            if(TermToId.TryGetValue(term, out uint existingId))
            {
                return TermId.FromEncoded(existingId);
            }

            IdToTerm.Add(term);
            uint newId = checked((uint)IdToTerm.Count);
            TermToId[term] = newId;

            return TermId.FromEncoded(newId);
        }
    }

    /// <summary>
    /// Looks up the identifier of a term WITHOUT assigning one — the read-path counterpart of
    /// <see cref="GetOrAdd(RdfTerm)"/>, for consumers (a value-index build resolving a declared predicate,
    /// a probe resolving a constant) that must never mint an id as a side effect of reading.
    /// </summary>
    /// <param name="term">The RDF term to look up.</param>
    /// <param name="id">Receives the term's identifier when it is already encoded.</param>
    /// <returns><see langword="true"/> when the term is already in the dictionary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    public bool TryGetId(RdfTerm term, out TermId id)
    {
        ArgumentNullException.ThrowIfNull(term);
        lock(Mutex)
        {
            if(TermToId.TryGetValue(term, out uint existingId))
            {
                id = TermId.FromEncoded(existingId);

                return true;
            }
        }

        id = TermId.None;

        return false;
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
        lock(Mutex)
        {
            return TermToId.TryGetValue(term, out uint id) ? TermId.FromEncoded(id) : TermId.None;
        }
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
        lock(Mutex)
        {
            if(encoded == 0 || encoded > (uint)IdToTerm.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(encoded), encoded,
                    $"Term identifier must be between 1 and {IdToTerm.Count}; '0' is reserved for TermId.None.");
            }

            return IdToTerm[(int)(encoded - 1)];
        }
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
        lock(Mutex)
        {
            return TermToId.ContainsKey(term);
        }
    }

    /// <summary>
    /// Atomically captures the current term count and the terms whose 1-based identifiers lie in
    /// <c>(<paramref name="watermark"/>, Count]</c>, in identifier order. The capture is taken under the
    /// same lock the mint path holds, so a term minted concurrently is either fully included in the returned
    /// range or not yet visible in the returned count — never a torn read. This is the durable dataset
    /// journal's append primitive: each record persists exactly the terms minted since the previous durable
    /// record, so the log is self-contained.
    /// </summary>
    /// <param name="watermark">The exclusive lower bound of the captured identifier range; the count captured by the previous durable record.</param>
    /// <returns>The current term count and the newly-minted terms, where <c>NewTerms[i]</c> has identifier <c><paramref name="watermark"/> + 1 + i</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="watermark"/> is negative or greater than the current count.</exception>
    internal (int Count, RdfTerm[] NewTerms) CaptureBeyond(int watermark)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(watermark);
        lock(Mutex)
        {
            int count = IdToTerm.Count;
            ArgumentOutOfRangeException.ThrowIfGreaterThan(watermark, count);

            int newCount = count - watermark;
            RdfTerm[] newTerms = new RdfTerm[newCount];
            for(int i = 0; i < newCount; i++)
            {
                newTerms[i] = IdToTerm[watermark + i];
            }

            return (count, newTerms);
        }
    }
}
