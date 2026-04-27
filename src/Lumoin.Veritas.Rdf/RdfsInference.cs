using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// Forward-chaining RDFS inference, producing derived triples from the RDFS
/// entailment rules until fixpoint.
/// </summary>
/// <remarks>
/// <para>
/// Implements the four most commonly used RDFS entailment rules from
/// <see href="https://www.w3.org/TR/rdf12-schema/#ch_entailment">RDF 1.2 Schema §8</see>:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Rule</term>
///     <description>Entailment</description>
///   </listheader>
///   <item>
///     <term>rdfs9</term>
///     <description><c>(?c1 rdfs:subClassOf ?c2) &#8743; (?x rdf:type ?c1)</c> &#8658; <c>(?x rdf:type ?c2)</c></description>
///   </item>
///   <item>
///     <term>rdfs7</term>
///     <description><c>(?p1 rdfs:subPropertyOf ?p2) &#8743; (?s ?p1 ?o)</c> &#8658; <c>(?s ?p2 ?o)</c></description>
///   </item>
///   <item>
///     <term>rdfs2</term>
///     <description><c>(?p rdfs:domain ?c) &#8743; (?s ?p ?o)</c> &#8658; <c>(?s rdf:type ?c)</c></description>
///   </item>
///   <item>
///     <term>rdfs3</term>
///     <description><c>(?p rdfs:range ?c) &#8743; (?s ?p ?o)</c> &#8658; <c>(?o rdf:type ?c)</c></description>
///   </item>
/// </list>
/// <para>
/// Also computes the transitive closure of <c>rdfs:subClassOf</c> and
/// <c>rdfs:subPropertyOf</c> (rules <c>rdfs11</c> and <c>rdfs5</c>), which are
/// needed for the rules above to be complete in a single pass.
/// </para>
/// <para>
/// Axiomatic triples (<c>rdf:type rdfs:subPropertyOf rdfs:member</c> and similar
/// vocabulary-describing axioms) are not emitted. Domain/range inferences
/// involving reflexive <c>rdfs:subClassOf</c> cycles are deduplicated.
/// </para>
/// <para>
/// The TBox (schema) triples needed for inference — the subclass, subproperty,
/// domain, and range declarations — are loaded into memory up front. The ABox
/// (instance data) is traversed through the same <see cref="StorageDelegates.MatchTriplesAsync"/>
/// delegate. Derived triples are emitted as a stream; callers can accumulate
/// them, insert them into another store, or validate them further.
/// </para>
/// </remarks>
public static class RdfsInference
{
    /// <summary>
    /// Runs RDFS inference to fixpoint over the graph exposed by
    /// <paramref name="match"/>, yielding each derived triple at most once.
    /// </summary>
    /// <remarks>
    /// Derived triples that already exist as asserted triples in the source
    /// graph are <em>not</em> suppressed — consumers that want only genuinely
    /// new triples should filter the output against the source graph.
    /// </remarks>
    /// <param name="vocabulary">The resolved vocabulary identifiers.</param>
    /// <param name="match">The pattern match delegate over the source graph.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of inferred <see cref="EncodedTriple"/>s.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<EncodedTriple> InferAsync(
        RdfsVocabularyIds vocabulary,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        return InferCore(vocabulary, match, cancellationToken);
    }

    private static async IAsyncEnumerable<EncodedTriple> InferCore(
        RdfsVocabularyIds vocabulary,
        StorageDelegates.MatchTriplesAsync match,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Load TBox relations. Each is a binary relation over class/property identifiers.
        //The pairs carry whatever subject/object terms appear in the source graph; we
        //type them as TermId because no kind validation is performed at load time.
        HashSet<(TermId, TermId)> subClassOfDirect = await LoadRelationAsync(match, vocabulary.RdfsSubClassOf, cancellationToken).ConfigureAwait(false);
        HashSet<(TermId, TermId)> subPropertyOfDirect = await LoadRelationAsync(match, vocabulary.RdfsSubPropertyOf, cancellationToken).ConfigureAwait(false);
        HashSet<(TermId, TermId)> domainAssertions = await LoadRelationAsync(match, vocabulary.RdfsDomain, cancellationToken).ConfigureAwait(false);
        HashSet<(TermId, TermId)> rangeAssertions = await LoadRelationAsync(match, vocabulary.RdfsRange, cancellationToken).ConfigureAwait(false);

        //Close subClassOf and subPropertyOf under transitivity and reflexivity on observed terms.
        Dictionary<TermId, HashSet<TermId>> superClasses = BuildTransitiveClosure(subClassOfDirect);
        Dictionary<TermId, HashSet<TermId>> superProperties = BuildTransitiveClosure(subPropertyOfDirect);

        //Track every emitted triple so we only emit each once across all rules.
        HashSet<EncodedTriple> emitted = [];

        //rdfs11 and rdfs5: emit closure of subClassOf/subPropertyOf beyond what was asserted.
        foreach((TermId sub, HashSet<TermId> supers) in superClasses)
        {
            foreach(TermId super in supers)
            {
                if(sub == super)
                {
                    continue;
                }

                if(subClassOfDirect.Contains((sub, super)))
                {
                    continue;
                }

                EncodedTriple triple = new(sub, vocabulary.RdfsSubClassOf, super);
                if(emitted.Add(triple))
                {
                    yield return triple;
                }
            }
        }

        foreach((TermId sub, HashSet<TermId> supers) in superProperties)
        {
            foreach(TermId super in supers)
            {
                if(sub == super)
                {
                    continue;
                }

                if(subPropertyOfDirect.Contains((sub, super)))
                {
                    continue;
                }

                EncodedTriple triple = new(sub, vocabulary.RdfsSubPropertyOf, super);
                if(emitted.Add(triple))
                {
                    yield return triple;
                }
            }
        }

        //Index domain/range assertions by predicate for O(1) lookup during instance sweep.
        Dictionary<TermId, List<TermId>> domainByPredicate = [];
        foreach((TermId pred, TermId cls) in domainAssertions)
        {
            if(!domainByPredicate.TryGetValue(pred, out List<TermId>? classes))
            {
                classes = [];
                domainByPredicate[pred] = classes;
            }

            classes.Add(cls);
        }

        Dictionary<TermId, List<TermId>> rangeByPredicate = [];
        foreach((TermId pred, TermId cls) in rangeAssertions)
        {
            if(!rangeByPredicate.TryGetValue(pred, out List<TermId>? classes))
            {
                classes = [];
                rangeByPredicate[pred] = classes;
            }

            classes.Add(cls);
        }

        //rdfs7: (?p1 rdfs:subPropertyOf ?p2) and (?s ?p1 ?o) implies (?s ?p2 ?o).
        //rdfs2: (?p rdfs:domain ?c) and (?s ?p ?o) implies (?s rdf:type ?c).
        //rdfs3: (?p rdfs:range ?c) and (?s ?p ?o) implies (?o rdf:type ?c).
        //rdfs9: (?c1 rdfs:subClassOf ?c2) and (?x rdf:type ?c1) implies (?x rdf:type ?c2).
        //
        //All instance-level rules fire by streaming the ABox once. Domain and range
        //are applied to every triple, regardless of predicate. Subproperty and subclass
        //expansion use the pre-computed closures.

        //Emit subclass type inferences (rdfs9) by streaming rdf:type assertions.
        await foreach(EncodedTriple typeTriple in match(null, vocabulary.RdfType, null, cancellationToken).ConfigureAwait(false))
        {
            if(superClasses.TryGetValue(typeTriple.Object, out HashSet<TermId>? supers))
            {
                foreach(TermId super in supers)
                {
                    if(super == typeTriple.Object)
                    {
                        continue;
                    }

                    EncodedTriple derived = new(typeTriple.Subject, vocabulary.RdfType, super);
                    if(emitted.Add(derived))
                    {
                        yield return derived;
                    }
                }
            }
        }

        //Stream the whole graph once to apply rdfs7, rdfs2, rdfs3. Use a match with
        //all three positions unbound. For each triple, consult the pre-computed maps.
        await foreach(EncodedTriple triple in match(null, null, null, cancellationToken).ConfigureAwait(false))
        {
            //rdfs7: emit (s, super, o) for every super-property of p beyond p itself.
            if(superProperties.TryGetValue(triple.Predicate, out HashSet<TermId>? supers))
            {
                foreach(TermId super in supers)
                {
                    if(super == triple.Predicate)
                    {
                        continue;
                    }

                    EncodedTriple derived = new(triple.Subject, super, triple.Object);
                    if(emitted.Add(derived))
                    {
                        yield return derived;
                    }
                }
            }

            //rdfs2: if p has a domain c, s is typed c. Also apply for every super-property of p that has a domain.
            foreach(TermId effectivePredicate in EnumerateSelfAndSupers(triple.Predicate, superProperties))
            {
                if(domainByPredicate.TryGetValue(effectivePredicate, out List<TermId>? domainClasses))
                {
                    foreach(TermId c in domainClasses)
                    {
                        foreach(TermId cSuper in EnumerateSelfAndSupers(c, superClasses))
                        {
                            EncodedTriple derived = new(triple.Subject, vocabulary.RdfType, cSuper);
                            if(emitted.Add(derived))
                            {
                                yield return derived;
                            }
                        }
                    }
                }

                //rdfs3: same for range applied to the object.
                if(rangeByPredicate.TryGetValue(effectivePredicate, out List<TermId>? rangeClasses))
                {
                    foreach(TermId c in rangeClasses)
                    {
                        foreach(TermId cSuper in EnumerateSelfAndSupers(c, superClasses))
                        {
                            EncodedTriple derived = new(triple.Object, vocabulary.RdfType, cSuper);
                            if(emitted.Add(derived))
                            {
                                yield return derived;
                            }
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<TermId> EnumerateSelfAndSupers(TermId node, Dictionary<TermId, HashSet<TermId>> closure)
    {
        yield return node;
        if(closure.TryGetValue(node, out HashSet<TermId>? supers))
        {
            foreach(TermId super in supers)
            {
                if(super != node)
                {
                    yield return super;
                }
            }
        }
    }

    private static async ValueTask<HashSet<(TermId, TermId)>> LoadRelationAsync(
        StorageDelegates.MatchTriplesAsync match,
        IriId predicate,
        CancellationToken cancellationToken)
    {
        HashSet<(TermId, TermId)> pairs = [];
        await foreach(EncodedTriple triple in match(null, predicate, null, cancellationToken).ConfigureAwait(false))
        {
            pairs.Add((triple.Subject, triple.Object));
        }

        return pairs;
    }

    private static Dictionary<TermId, HashSet<TermId>> BuildTransitiveClosure(HashSet<(TermId, TermId)> directPairs)
    {
        //Build adjacency list.
        Dictionary<TermId, HashSet<TermId>> adjacency = [];
        foreach((TermId sub, TermId super) in directPairs)
        {
            if(!adjacency.TryGetValue(sub, out HashSet<TermId>? supers))
            {
                supers = [];
                adjacency[sub] = supers;
            }

            supers.Add(super);
        }

        //For each starting node, do a BFS to collect all reachable supers.
        Dictionary<TermId, HashSet<TermId>> closure = [];
        foreach(TermId start in adjacency.Keys)
        {
            HashSet<TermId> reachable = [];
            Queue<TermId> frontier = new();
            frontier.Enqueue(start);
            HashSet<TermId> visited = [start];

            while(frontier.Count > 0)
            {
                TermId current = frontier.Dequeue();
                if(!adjacency.TryGetValue(current, out HashSet<TermId>? neighbours))
                {
                    continue;
                }

                foreach(TermId neighbour in neighbours)
                {
                    if(visited.Add(neighbour))
                    {
                        reachable.Add(neighbour);
                        frontier.Enqueue(neighbour);
                    }
                }
            }

            closure[start] = reachable;
        }

        return closure;
    }
}
