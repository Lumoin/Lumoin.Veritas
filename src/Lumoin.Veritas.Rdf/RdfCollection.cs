using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// How one RDF collection chain walk ended.
/// </summary>
public enum RdfCollectionOutcome
{
    /// <summary>The chain reached <c>rdf:nil</c> with exactly one <c>rdf:first</c> and <c>rdf:rest</c> read per cell.</summary>
    WellFormed = 0,

    /// <summary>A cell lacked <c>rdf:first</c> or <c>rdf:rest</c>; the members walked before the break are returned.</summary>
    BrokenChain = 1,

    /// <summary>The chain revisited a cell before reaching <c>rdf:nil</c>; the members walked before the revisit are returned.</summary>
    CyclicChain = 2,

    /// <summary>A cell carried several distinct <c>rdf:first</c> or <c>rdf:rest</c> values; the unambiguous prefix is returned.</summary>
    AmbiguousCell = 3,
}

/// <summary>
/// One RDF collection read: the members walked and how the chain ended.
/// </summary>
/// <remarks>
/// A consumer that treats every outcome as membership reproduces the historic
/// truncating read; the outcome makes that choice visible instead of silent.
/// </remarks>
/// <param name="Members">The members walked, in list order — the whole list when <see cref="Outcome"/> is <see cref="RdfCollectionOutcome.WellFormed"/>, otherwise the prefix the chain determined.</param>
/// <param name="Outcome">How the walk ended.</param>
public readonly record struct RdfCollectionRead(IReadOnlyList<TermId> Members, RdfCollectionOutcome Outcome);

/// <summary>
/// Traversal operations over RDF collections — the linked-list structure built
/// from <c>rdf:first</c> and <c>rdf:rest</c> terminating at <c>rdf:nil</c>.
/// </summary>
/// <remarks>
/// <para>
/// RDF collections are the idiomatic encoding of ordered sequences in RDF graphs
/// and appear in many contexts: <c>owl:intersectionOf</c>, <c>owl:unionOf</c>,
/// <c>owl:propertyChainAxiom</c>, <c>sh:or</c>, <c>sh:and</c>, <c>sh:in</c>, and
/// SPARQL property path alternatives serialized as collections.
/// </para>
/// <para>
/// Defined in <see href="https://www.w3.org/TR/rdf12-schema/#ch_collectionvocab">RDF 1.2 Schema §5.2</see>.
/// The list starts at a head blank node which has an <c>rdf:first</c> triple
/// pointing to the first member and an <c>rdf:rest</c> triple pointing either
/// to the next cell or to <c>rdf:nil</c>.
/// </para>
/// <para>
/// The traversal is a function of the graph and reports how it ended: a cell
/// carrying several distinct <c>rdf:first</c> or <c>rdf:rest</c> values, a
/// broken chain, and a cycle are distinct <see cref="RdfCollectionOutcome"/>
/// values rather than silent truncation. A visited set bounds every walk on
/// malformed graphs.
/// </para>
/// </remarks>
public static class RdfCollection
{
    /// <summary>
    /// Walks the RDF collection whose head is <paramref name="head"/> into
    /// its members, reporting how the chain ended.
    /// </summary>
    /// <remarks>
    /// The head identifier must already be resolved against the relevant
    /// <see cref="TermDictionary"/>. Pass the resolved identifiers of the
    /// <c>rdf:first</c>, <c>rdf:rest</c>, and <c>rdf:nil</c> terms as the
    /// other arguments.
    /// </remarks>
    /// <param name="head">The encoded identifier of the list head.</param>
    /// <param name="firstId">The encoded identifier of <c>rdf:first</c>.</param>
    /// <param name="restId">The encoded identifier of <c>rdf:rest</c>.</param>
    /// <param name="nilId">The encoded identifier of <c>rdf:nil</c>.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The members walked and the chain outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static ValueTask<RdfCollectionRead> ReadAsync(
        TermId head,
        IriId firstId,
        IriId restId,
        IriId nilId,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);

        return ReadCore(head, firstId, restId, nilId, match, cancellationToken);
    }

    /// <summary>
    /// Detects whether <paramref name="value"/> identifies a SHACL list
    /// (per the W3C RDF Schema 1.2 §5.2 collection vocabulary) and, if
    /// so, walks its members. Returns <c>null</c> when the value is not
    /// a SHACL list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value qualifies as a SHACL list iff it is <c>rdf:nil</c> (the
    /// empty list, returning an empty well-formed read) or has at least
    /// one outgoing <c>rdf:first</c> triple (returning the walked
    /// members with the chain outcome). Values lacking <c>rdf:first</c>
    /// and not equal to <c>rdf:nil</c> return <c>null</c>, signalling
    /// "not a SHACL list."
    /// </para>
    /// <para>
    /// This helper exists to centralise the SHACL list-interpretation
    /// rule in one place. Multiple SHACL evaluators
    /// (<c>sh:minListLength</c>, <c>sh:maxListLength</c>,
    /// <c>sh:uniqueMembers</c>, <c>sh:memberShape</c>) previously
    /// duplicated the detect-then-walk pattern; consolidating the
    /// rule prevents drift between them.
    /// </para>
    /// </remarks>
    /// <param name="value">The value to inspect.</param>
    /// <param name="firstId">The encoded identifier of <c>rdf:first</c>.</param>
    /// <param name="restId">The encoded identifier of <c>rdf:rest</c>.</param>
    /// <param name="nilId">The encoded identifier of <c>rdf:nil</c>.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The collection read if <paramref name="value"/> is a SHACL list
    /// (empty and well-formed for <c>rdf:nil</c>); <c>null</c> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static async ValueTask<RdfCollectionRead?> TryReadAsync(
        TermId value,
        IriId firstId,
        IriId restId,
        IriId nilId,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);

        if(value.Equals(nilId.Value))
        {
            return new RdfCollectionRead([], RdfCollectionOutcome.WellFormed);
        }

        bool hasFirst = await HasOutgoingPredicateAsync(
            value, firstId, match, cancellationToken).ConfigureAwait(false);

        if(!hasFirst)
        {
            return null;
        }

        return await ReadCore(value, firstId, restId, nilId, match, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<RdfCollectionRead> ReadCore(
        TermId head,
        IriId firstId,
        IriId restId,
        IriId nilId,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        TermId nilTerm = nilId.Value;

        List<TermId> members = [];
        HashSet<TermId> visited = [];
        TermId cursor = head;

        while(cursor != nilTerm)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(!visited.Add(cursor))
            {
                return new RdfCollectionRead(members, RdfCollectionOutcome.CyclicChain);
            }

            (TermId? member, bool memberAmbiguous) = await SoleObjectAsync(
                cursor, firstId, match, cancellationToken).ConfigureAwait(false);

            if(memberAmbiguous)
            {
                return new RdfCollectionRead(members, RdfCollectionOutcome.AmbiguousCell);
            }

            if(member is null)
            {
                return new RdfCollectionRead(members, RdfCollectionOutcome.BrokenChain);
            }

            members.Add(member.Value);

            (TermId? next, bool nextAmbiguous) = await SoleObjectAsync(
                cursor, restId, match, cancellationToken).ConfigureAwait(false);

            if(nextAmbiguous)
            {
                return new RdfCollectionRead(members, RdfCollectionOutcome.AmbiguousCell);
            }

            if(next is null)
            {
                return new RdfCollectionRead(members, RdfCollectionOutcome.BrokenChain);
            }

            cursor = next.Value;
        }

        return new RdfCollectionRead(members, RdfCollectionOutcome.WellFormed);
    }

    //Reads the cell's sole object under the predicate: the value when
    //exactly one distinct object is asserted, no value when none is, and
    //the ambiguity flag when several distinct objects are.
    private static async ValueTask<(TermId? Value, bool Ambiguous)> SoleObjectAsync(
        TermId subject,
        IriId predicate,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        TermId? found = null;
        await foreach(EncodedTriple triple in match(subject, predicate, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            if(found is null)
            {
                found = triple.Object;
            }
            else if(found.Value != triple.Object)
            {
                return (found, true);
            }
        }

        return (found, false);
    }

    //Probes whether subject has at least one outgoing triple with
    //the given predicate. The storage layer returns a bounded
    //enumerable; we exit on the first match.
    private static async Task<bool> HasOutgoingPredicateAsync(
        TermId subject,
        IriId predicate,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken)
    {
        await foreach(EncodedTriple _ in match(subject, predicate, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }
}
