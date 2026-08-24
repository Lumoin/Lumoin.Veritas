using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;

namespace Lumoin.Veritas.Core.Indexing;

/// <summary>
/// The result of a value-index census over one graph: how many literal entries carrying a registered
/// axis datatype live under DECLARED predicates (servable by a probe) versus under predicates no
/// registration declares (invisible to every probe). A large undeclared count is the visibility
/// signal for a host whose value annotations are encoded outside the registered axes — the data is
/// served correctly by the scan but never accelerated, and this census makes that state observable
/// rather than silent.
/// </summary>
/// <param name="DeclaredEntryCount">The literal entries carrying a registered datatype under a declared axis predicate.</param>
/// <param name="UndeclaredEntryCount">The literal entries carrying a registered datatype under a predicate no registration declares.</param>
public readonly record struct ValueIndexCensusResult(long DeclaredEntryCount, long UndeclaredEntryCount);

/// <summary>
/// The on-demand value-index census: sweeps one graph counting the literal entries whose datatype
/// matches a registered axis, partitioned by whether their predicate is declared. A diagnostic read —
/// it runs only when asked, holds no state, and adds zero query-path or commit-path work.
/// </summary>
public static class ValueIndexCensus
{
    /// <summary>Computes the census over a graph's triples.</summary>
    /// <param name="store">The graph to sweep.</param>
    /// <param name="dictionary">The shared term dictionary the triples are encoded against.</param>
    /// <param name="registry">The composed registry whose datatypes and declared predicates partition the count.</param>
    /// <returns>The census; zero counts for an empty registry (nothing is registered, so nothing is countable).</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static ValueIndexCensusResult Compute(HypertrieGraphStore store, TermDictionary dictionary, ValueIndexRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(registry);

        if(registry.IsEmpty)
        {
            return new ValueIndexCensusResult(0, 0);
        }

        //Declared predicates compare by term id (no per-triple IRI resolution); a declared predicate the
        //dictionary has not encoded cannot occur in any triple and contributes nothing.
        HashSet<uint> declaredPredicates = [];
        List<Utf8String> registeredDatatypes = [];
        for(int i = 0; i < registry.Registrations.Count; i++)
        {
            ValueIndexRegistration registration = registry.Registrations[i];
            if(dictionary.TryGetId(new NamedNode(registration.Axis.StartPredicateIri), out TermId start))
            {
                declaredPredicates.Add(start.Encoded);
            }

            if(registration.Axis.EndPredicateIri is { } endIri && dictionary.TryGetId(new NamedNode(endIri), out TermId end))
            {
                declaredPredicates.Add(end.Encoded);
            }

            if(!registeredDatatypes.Contains(registration.Method.DatatypeIri))
            {
                registeredDatatypes.Add(registration.Method.DatatypeIri);
            }
        }

        long declaredCount = 0;
        long undeclaredCount = 0;
        foreach(EncodedTriple triple in store.Match(subject: TermId.None, predicate: TermId.None, @object: TermId.None))
        {
            if(dictionary.Resolve(triple.Object) is not Literal literal || !CarriesRegisteredDatatype(literal, registeredDatatypes))
            {
                continue;
            }

            if(declaredPredicates.Contains(triple.Predicate.Encoded))
            {
                declaredCount++;
            }
            else
            {
                undeclaredCount++;
            }
        }

        return new ValueIndexCensusResult(declaredCount, undeclaredCount);
    }

    /// <summary>Whether a literal's datatype IRI matches any registered axis datatype.</summary>
    /// <param name="literal">The literal.</param>
    /// <param name="registeredDatatypes">The registered datatype IRIs.</param>
    /// <returns><see langword="true"/> on a match.</returns>
    private static bool CarriesRegisteredDatatype(Literal literal, List<Utf8String> registeredDatatypes)
    {
        for(int i = 0; i < registeredDatatypes.Count; i++)
        {
            if(literal.Datatype.Iri.Equals(registeredDatatypes[i]))
            {
                return true;
            }
        }

        return false;
    }
}
