using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
        await foreach(EncodedTriple typeTriple in match(TermId.None, vocabulary.RdfType, TermId.None, cancellationToken).ConfigureAwait(false))
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
        await foreach(EncodedTriple triple in match(TermId.None, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
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

    /// <summary>
    /// Runs RDFS inference to fixpoint over the graph exposed by
    /// <paramref name="match"/>, yielding each derived triple at
    /// most once together with the rule that produced it and the
    /// two W3C-schema premises the rule matched against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the source-aware sibling of
    /// <see cref="InferAsync(RdfsVocabularyIds, StorageDelegates.MatchTriplesAsync, CancellationToken)"/>.
    /// Bare callers continue to use the original; consumers that need
    /// to trace a derived triple back through its premises to
    /// asserted source triples use this method. The triple set
    /// produced is identical; only the carrier shape differs.
    /// </para>
    /// <para>
    /// <b>Antecedent semantics (α-decomposed).</b> Each
    /// <see cref="InferredTriple.Antecedents"/> array holds exactly
    /// the two premises stated by the rule in
    /// <see cref="InferredTriple.Rule"/>. The fused derivation paths
    /// the original implementation collapses into single emission
    /// sites (rdfs2/rdfs3 lifted via subPropertyOf-chain and
    /// subClassOf-chain) are decomposed here into separate
    /// rdfs2/rdfs3 base emissions plus rdfs9 lifts, each carrying
    /// the W3C schema for its own rule. The same applies to
    /// transitive subClassOf and subPropertyOf closure: each
    /// rdfs11/rdfs5 emission carries the two-hop BFS predecessor
    /// pair as its antecedents, so the chain reconstructs
    /// transitively by triple-equality lookup back into the stream.
    /// </para>
    /// </remarks>
    /// <param name="vocabulary">The resolved vocabulary identifiers.</param>
    /// <param name="match">The pattern match delegate over the source graph.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async sequence of <see cref="InferredTriple"/> values.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <c>null</c>.</exception>
    public static IAsyncEnumerable<InferredTriple> InferWithProvenanceAsync(
        RdfsVocabularyIds vocabulary,
        StorageDelegates.MatchTriplesAsync match,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        return InferWithProvenanceCore(vocabulary, match, cancellationToken);
    }

    private static async IAsyncEnumerable<InferredTriple> InferWithProvenanceCore(
        RdfsVocabularyIds vocabulary,
        StorageDelegates.MatchTriplesAsync match,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Phase 1: load TBox relations. Mirror InferCore exactly.
        HashSet<(TermId, TermId)> subClassOfDirect = await LoadRelationAsync(match, vocabulary.RdfsSubClassOf, cancellationToken).ConfigureAwait(false);
        HashSet<(TermId, TermId)> subPropertyOfDirect = await LoadRelationAsync(match, vocabulary.RdfsSubPropertyOf, cancellationToken).ConfigureAwait(false);
        HashSet<(TermId, TermId)> domainAssertions = await LoadRelationAsync(match, vocabulary.RdfsDomain, cancellationToken).ConfigureAwait(false);
        HashSet<(TermId, TermId)> rangeAssertions = await LoadRelationAsync(match, vocabulary.RdfsRange, cancellationToken).ConfigureAwait(false);

        //Phase 2: build closures with BFS predecessor maps. The
        //predecessor map records, per start node, the BFS predecessor
        //of each reached node; the edge (predecessor, predicate,
        //reached) is asserted by BFS construction.
        (Dictionary<TermId, HashSet<TermId>> superClasses,
         Dictionary<TermId, Dictionary<TermId, TermId>> subClassPredecessors)
            = BuildTransitiveClosureWithPredecessors(subClassOfDirect);

        (Dictionary<TermId, HashSet<TermId>> superProperties,
         Dictionary<TermId, Dictionary<TermId, TermId>> subPropertyPredecessors)
            = BuildTransitiveClosureWithPredecessors(subPropertyOfDirect);

        //Phase 3: index domain/range assertions by predicate. Mirror InferCore exactly.
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

        //Dedup is per-call. Sharing a HashSet across calls would
        //cross-contaminate provenance between independent inferences.
        HashSet<EncodedTriple> emitted = [];

        //rdfs11: emit closure of subClassOf beyond what was directly asserted.
        //Antecedents = [(sub, sCO, predecessor), (predecessor, sCO, super)].
        //The first is itself possibly derived by rdfs11 earlier in the
        //stream (when predecessor is reached through more than one hop
        //from sub); the second is always asserted by BFS construction.
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

                TermId predecessor = subClassPredecessors[sub][super];
                EncodedTriple consequent = new(sub, vocabulary.RdfsSubClassOf, super);
                EncodedTriple firstPremise = new(sub, vocabulary.RdfsSubClassOf, predecessor);
                EncodedTriple secondPremise = new(predecessor, vocabulary.RdfsSubClassOf, super);
                if(emitted.Add(consequent))
                {
                    yield return new InferredTriple(
                        consequent,
                        ImmutableArray.Create(firstPremise, secondPremise),
                        InferenceRule.Rdfs11);
                }
            }
        }

        //rdfs5: symmetric to rdfs11 over subPropertyOf.
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

                TermId predecessor = subPropertyPredecessors[sub][super];
                EncodedTriple consequent = new(sub, vocabulary.RdfsSubPropertyOf, super);
                EncodedTriple firstPremise = new(sub, vocabulary.RdfsSubPropertyOf, predecessor);
                EncodedTriple secondPremise = new(predecessor, vocabulary.RdfsSubPropertyOf, super);
                if(emitted.Add(consequent))
                {
                    yield return new InferredTriple(
                        consequent,
                        ImmutableArray.Create(firstPremise, secondPremise),
                        InferenceRule.Rdfs5);
                }
            }
        }

        //rdfs9 sweep over rdf:type triples.
        //Antecedents = [(c1, sCO, super), typeTriple]. The first
        //premise is asserted when (c1, sCO, super) is a direct edge,
        //otherwise derived by the rdfs11 emission above; the consumer
        //finds it in the stream by triple equality.
        await foreach(EncodedTriple typeTriple in match(TermId.None, vocabulary.RdfType, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            if(superClasses.TryGetValue(typeTriple.Object, out HashSet<TermId>? supers))
            {
                foreach(TermId super in supers)
                {
                    if(super == typeTriple.Object)
                    {
                        continue;
                    }

                    EncodedTriple consequent = new(typeTriple.Subject, vocabulary.RdfType, super);
                    EncodedTriple subClassPremise = new(typeTriple.Object, vocabulary.RdfsSubClassOf, super);
                    if(emitted.Add(consequent))
                    {
                        yield return new InferredTriple(
                            consequent,
                            ImmutableArray.Create(subClassPremise, typeTriple),
                            InferenceRule.Rdfs9);
                    }
                }
            }
        }

        //Whole-ABox sweep: rdfs7, rdfs2, rdfs3 — α-decomposed.
        await foreach(EncodedTriple triple in match(TermId.None, TermId.None, TermId.None, cancellationToken).ConfigureAwait(false))
        {
            //rdfs7: (p, sPO, super) and (s, p, o) ⇒ (s, super, o).
            //Antecedents = [(p, sPO, super), triple]. The first
            //premise is asserted when super is a direct super of p,
            //otherwise derived by rdfs5 above.
            if(superProperties.TryGetValue(triple.Predicate, out HashSet<TermId>? supers))
            {
                foreach(TermId super in supers)
                {
                    if(super == triple.Predicate)
                    {
                        continue;
                    }

                    EncodedTriple consequent = new(triple.Subject, super, triple.Object);
                    EncodedTriple subPropertyPremise = new(triple.Predicate, vocabulary.RdfsSubPropertyOf, super);
                    if(emitted.Add(consequent))
                    {
                        yield return new InferredTriple(
                            consequent,
                            ImmutableArray.Create(subPropertyPremise, triple),
                            InferenceRule.Rdfs7);
                    }
                }
            }

            //rdfs2 and rdfs3, α-decomposed. For each predicate in the
            //self-and-supers chain of triple.Predicate, the (s,
            //effectivePredicate, o) premise is either the ABox triple
            //itself (when effective == p) or an rdfs7-derived triple
            //emitted just above. Domain and range emit the rdfs2/rdfs3
            //base consequent first; the rdfs9 class-hierarchy lift then
            //emits its own consequents separately, each with the W3C
            //two-premise rdfs9 schema.
            foreach(TermId effectivePredicate in EnumerateSelfAndSupers(triple.Predicate, superProperties))
            {
                EncodedTriple aboxPremise = effectivePredicate == triple.Predicate
                    ? triple
                    : new EncodedTriple(triple.Subject, effectivePredicate, triple.Object);

                if(domainByPredicate.TryGetValue(effectivePredicate, out List<TermId>? domainClasses))
                {
                    foreach(TermId c in domainClasses)
                    {
                        EncodedTriple domainAssertion = new(effectivePredicate, vocabulary.RdfsDomain, c);
                        EncodedTriple rdfs2Consequent = new(triple.Subject, vocabulary.RdfType, c);
                        if(emitted.Add(rdfs2Consequent))
                        {
                            yield return new InferredTriple(
                                rdfs2Consequent,
                                ImmutableArray.Create(domainAssertion, aboxPremise),
                                InferenceRule.Rdfs2);
                        }

                        //rdfs9 lift via class hierarchy. Each cSuper
                        //emission's antecedents are the W3C rdfs9
                        //premises: the closure-or-asserted subClassOf
                        //edge plus the rdfs2 type consequent above.
                        if(superClasses.TryGetValue(c, out HashSet<TermId>? cSupers))
                        {
                            foreach(TermId cSuper in cSupers)
                            {
                                if(cSuper == c)
                                {
                                    continue;
                                }

                                EncodedTriple liftedConsequent = new(triple.Subject, vocabulary.RdfType, cSuper);
                                EncodedTriple subClassPremise = new(c, vocabulary.RdfsSubClassOf, cSuper);
                                if(emitted.Add(liftedConsequent))
                                {
                                    yield return new InferredTriple(
                                        liftedConsequent,
                                        ImmutableArray.Create(subClassPremise, rdfs2Consequent),
                                        InferenceRule.Rdfs9);
                                }
                            }
                        }
                    }
                }

                if(rangeByPredicate.TryGetValue(effectivePredicate, out List<TermId>? rangeClasses))
                {
                    foreach(TermId c in rangeClasses)
                    {
                        EncodedTriple rangeAssertion = new(effectivePredicate, vocabulary.RdfsRange, c);
                        EncodedTriple rdfs3Consequent = new(triple.Object, vocabulary.RdfType, c);
                        if(emitted.Add(rdfs3Consequent))
                        {
                            yield return new InferredTriple(
                                rdfs3Consequent,
                                ImmutableArray.Create(rangeAssertion, aboxPremise),
                                InferenceRule.Rdfs3);
                        }

                        if(superClasses.TryGetValue(c, out HashSet<TermId>? cSupers))
                        {
                            foreach(TermId cSuper in cSupers)
                            {
                                if(cSuper == c)
                                {
                                    continue;
                                }

                                EncodedTriple liftedConsequent = new(triple.Object, vocabulary.RdfType, cSuper);
                                EncodedTriple subClassPremise = new(c, vocabulary.RdfsSubClassOf, cSuper);
                                if(emitted.Add(liftedConsequent))
                                {
                                    yield return new InferredTriple(
                                        liftedConsequent,
                                        ImmutableArray.Create(subClassPremise, rdfs3Consequent),
                                        InferenceRule.Rdfs9);
                                }
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
        await foreach(EncodedTriple triple in match(TermId.None, predicate, TermId.None, cancellationToken).ConfigureAwait(false))
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

    private static (Dictionary<TermId, HashSet<TermId>> Closure,
                    Dictionary<TermId, Dictionary<TermId, TermId>> Predecessors)
        BuildTransitiveClosureWithPredecessors(HashSet<(TermId, TermId)> directPairs)
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

        //For each starting node, BFS to collect reachable supers and
        //record the immediate predecessor of each reached node along
        //the BFS tree path. The edge (predecessor, predicate, reached)
        //is in directPairs by BFS construction: a node enters the
        //frontier only by being reached from a node that has it as a
        //direct neighbour.
        Dictionary<TermId, HashSet<TermId>> closure = [];
        Dictionary<TermId, Dictionary<TermId, TermId>> predecessors = [];
        foreach(TermId start in adjacency.Keys)
        {
            HashSet<TermId> reachable = [];
            Dictionary<TermId, TermId> predecessorForStart = [];
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
                        predecessorForStart[neighbour] = current;
                        frontier.Enqueue(neighbour);
                    }
                }
            }

            closure[start] = reachable;
            predecessors[start] = predecessorForStart;
        }

        return (closure, predecessors);
    }
}
