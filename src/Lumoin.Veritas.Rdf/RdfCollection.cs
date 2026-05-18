using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

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
/// This type does not enforce that the collection is well-formed (exactly one
/// <c>rdf:first</c> and <c>rdf:rest</c> per cell, terminating at <c>rdf:nil</c>).
/// The first encountered <c>rdf:first</c> and <c>rdf:rest</c> at each cell are
/// used, and traversal stops at <c>rdf:nil</c> or when a cell has no
/// <c>rdf:rest</c>. A visited set prevents infinite loops on malformed graphs.
/// </para>
/// </remarks>
public static class RdfCollection
{
    /// <summary>
    /// Enumerates the members of the RDF collection whose head is
    /// <paramref name="head"/>.
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
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of member identifiers in list order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<TermId> EnumerateAsync(
        TermId head,
        IriId firstId,
        IriId restId,
        IriId nilId,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        return EnumerateCore(head, firstId, restId, nilId, match, cancellationToken);
    }

    /// <summary>
    /// Materialises the RDF collection starting at <paramref name="head"/> into
    /// a list.
    /// </summary>
    /// <param name="head">The encoded identifier of the list head.</param>
    /// <param name="firstId">The encoded identifier of <c>rdf:first</c>.</param>
    /// <param name="restId">The encoded identifier of <c>rdf:rest</c>.</param>
    /// <param name="nilId">The encoded identifier of <c>rdf:nil</c>.</param>
    /// <param name="match">The pattern match delegate over the graph.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of member identifiers in list order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static async ValueTask<IReadOnlyList<TermId>> ToListAsync(
        TermId head,
        IriId firstId,
        IriId restId,
        IriId nilId,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);

        List<TermId> results = [];
        await foreach(TermId member in EnumerateAsync(
            head, firstId, restId, nilId, match, cancellationToken).ConfigureAwait(false))
        {
            results.Add(member);
        }

        return results;
    }

    /// <summary>
    /// Detects whether <paramref name="value"/> identifies a SHACL list
    /// (per the W3C RDF Schema 1.2 §5.2 collection vocabulary) and, if
    /// so, materialises its members. Returns <c>null</c> when the value
    /// is not a SHACL list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A value qualifies as a SHACL list iff it is <c>rdf:nil</c> (the
    /// empty list, returning <see cref="Array.Empty{T}"/>-equivalent
    /// empty list) or has at least one outgoing <c>rdf:first</c> triple
    /// (returning the materialised member sequence). Values lacking
    /// <c>rdf:first</c> and not equal to <c>rdf:nil</c> return
    /// <c>null</c>, signalling "not a SHACL list."
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
    /// The list of member identifiers if <paramref name="value"/> is a
    /// SHACL list (possibly empty for <c>rdf:nil</c>); <c>null</c>
    /// otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static async ValueTask<IReadOnlyList<TermId>?> TryGetMembersAsync(
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
            return [];
        }

        bool hasFirst = await HasOutgoingPredicateAsync(
            value, firstId, match, cancellationToken).ConfigureAwait(false);

        if(!hasFirst)
        {
            return null;
        }

        return await ToListAsync(value, firstId, restId, nilId, match, cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<TermId> EnumerateCore(
        TermId head,
        IriId firstId,
        IriId restId,
        IriId nilId,
        StorageDelegates.MatchTriplesAsync match,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TermId nilTerm = nilId.Value;

        HashSet<TermId> visited = [];
        TermId cursor = head;

        while(cursor != nilTerm && visited.Add(cursor))
        {
            cancellationToken.ThrowIfCancellationRequested();

            TermId? member = null;
            await foreach(EncodedTriple triple in match(cursor, firstId, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                member = triple.Object;

                break;
            }

            if(member is null)
            {
                yield break;
            }

            yield return member.Value;

            TermId? next = null;
            await foreach(EncodedTriple triple in match(cursor, restId, TermId.None, cancellationToken).ConfigureAwait(false))
            {
                next = triple.Object;

                break;
            }

            if(next is null)
            {
                yield break;
            }

            cursor = next.Value;
        }
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
