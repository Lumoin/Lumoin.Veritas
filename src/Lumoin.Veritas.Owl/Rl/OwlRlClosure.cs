using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// The result of an OWL 2 RL closure: the derived triples (never including
/// the base), whether the rules derived a contradiction, and — when they did
/// — the rule that fired it.
/// </summary>
public sealed class OwlRlResult
{
    /// <summary>The triples the rules derived beyond the base.</summary>
    public IReadOnlyCollection<EncodedTriple> Derived { get; }

    /// <summary>Whether the closure completed without deriving a contradiction.</summary>
    public bool IsConsistent { get; }

    /// <summary>The falsity rule that fired, or <c>null</c> when consistent.</summary>
    public string? InconsistencyRule { get; }

    /// <summary>
    /// Initialises the result.
    /// </summary>
    /// <param name="derived">The derived triples.</param>
    /// <param name="isConsistent">Whether no contradiction was derived.</param>
    /// <param name="inconsistencyRule">The falsity rule that fired, or <c>null</c>.</param>
    public OwlRlResult(IReadOnlyCollection<EncodedTriple> derived, bool isConsistent, string? inconsistencyRule)
    {
        Derived = derived;
        IsConsistent = isConsistent;
        InconsistencyRule = inconsistencyRule;
    }
}

/// <summary>
/// Forward-chaining OWL 2 RL materialization: the OWL 2 RL/RDF rules of
/// <see href="https://www.w3.org/TR/owl2-profiles/#Reasoning_in_OWL_2_RL_and_RDF_Graphs_using_Rules">Profiles §4.3</see>
/// (tables 4–9) evaluated to fixpoint over encoded triples, with the falsity
/// rules surfacing as an inconsistency verdict.
/// </summary>
/// <remarks>
/// <para>
/// The rules operate directly on the RDF representation — class expressions
/// stay graph nodes, schema triples participate as data — which is the form
/// the W3C conformance corpus exercises under both semantics. Equality
/// (<c>eq-*</c>) is materialised by explicit rules here; the union-find
/// canonicalization of the design record is the production-scale
/// optimisation, to be adopted against measured fixpoint workloads.
/// </para>
/// <para>
/// The datatype rules (<c>dt-*</c>) are not evaluated: value-space
/// reasoning composes with the SPARQL datatype machinery at a later stage,
/// and a closure over a graph with ill-typed literals simply derives no
/// contradiction from them.
/// </para>
/// </remarks>
public static class OwlRlClosure
{
    /// <summary>
    /// Computes the RL closure of <paramref name="triples"/>.
    /// </summary>
    /// <param name="triples">The base triples, schema statements included.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="literalsKnownDistinct">
    /// The datatype oracle for the <c>dt-*</c> falsity: returns
    /// <see langword="true"/> when the two terms are literals known to denote
    /// distinct data values, so an <c>owl:sameAs</c> between them is a
    /// contradiction. <see langword="null"/> disables the check — the
    /// term-id-level closure cannot inspect literal values itself.
    /// </param>
    /// <param name="cancellationToken">A token that aborts derivation between rounds.</param>
    /// <returns>The derived triples and the consistency verdict.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static OwlRlResult Compute(
        IEnumerable<EncodedTriple> triples,
        OwlRlTerms terms,
        Func<TermId, TermId, bool>? literalsKnownDistinct = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(terms);

        ClosureContext context = new(triples, terms, literalsKnownDistinct);

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            context.RebuildIndexes();
            context.FireRules();

            if(context.InconsistencyRule is not null || !context.MergePending())
            {
                break;
            }
        }

        return new OwlRlResult(context.Derived, context.InconsistencyRule is null, context.InconsistencyRule);
    }

    //The per-run state: the accumulating triple set, the per-round indexes,
    //and the rule implementations. Indexes rebuild per round — the closure's
    //consumers are conformance-scale documents; the production-scale path
    //runs rule bodies through the BGP engine per the design record.
    private sealed class ClosureContext
    {
        private OwlRlTerms Terms { get; }

        /// <summary>Every triple seen so far — base plus derived.</summary>
        private HashSet<EncodedTriple> All { get; } = [];

        /// <summary>The triples derived beyond the base.</summary>
        public HashSet<EncodedTriple> Derived { get; } = [];

        /// <summary>The current round's newly derived triples, merged after the round.</summary>
        private List<EncodedTriple> Pending { get; } = [];

        /// <summary>The falsity rule that fired, or <c>null</c>.</summary>
        public string? InconsistencyRule { get; private set; }

        /// <summary>Per-predicate (subject, object) pairs.</summary>
        private Dictionary<TermId, List<(TermId Subject, TermId Object)>> ByPredicate { get; } = [];

        /// <summary>(subject, predicate) → objects.</summary>
        private Dictionary<(TermId Subject, TermId Predicate), List<TermId>> BySubjectPredicate { get; } = [];

        /// <summary>Instance → its types.</summary>
        private Dictionary<TermId, HashSet<TermId>> TypesOf { get; } = [];

        /// <summary>Type → its instances.</summary>
        private Dictionary<TermId, List<TermId>> InstancesOf { get; } = [];

        /// <summary>The datatype oracle for literal-distinctness, or <c>null</c>.</summary>
        private Func<TermId, TermId, bool>? LiteralsKnownDistinct { get; }

        public ClosureContext(IEnumerable<EncodedTriple> triples, OwlRlTerms terms, Func<TermId, TermId, bool>? literalsKnownDistinct)
        {
            Terms = terms;
            LiteralsKnownDistinct = literalsKnownDistinct;
            foreach(EncodedTriple triple in triples)
            {
                All.Add(triple);
            }

            //The built-in datatype map enters as axiomatic knowledge — the
            //hierarchy and each datatype's rdfs:Datatype typing. Axiomatic
            //triples are entailed by the empty graph, so they count as
            //derived: entailment checks over the closure see them.
            foreach((TermId sub, TermId super) in terms.DatatypeHierarchy)
            {
                Seed(EncodedTriple.FromEncoded(sub.Encoded, terms.SubClassOf.Encoded, super.Encoded));
                Seed(EncodedTriple.FromEncoded(sub.Encoded, terms.Type.Encoded, terms.RdfsDatatype.Encoded));
                Seed(EncodedTriple.FromEncoded(super.Encoded, terms.Type.Encoded, terms.RdfsDatatype.Encoded));
            }

            void Seed(EncodedTriple triple)
            {
                if(All.Add(triple))
                {
                    Derived.Add(triple);
                }
            }
        }

        /// <summary>Rebuilds the per-round indexes from the accumulated set.</summary>
        public void RebuildIndexes()
        {
            ByPredicate.Clear();
            BySubjectPredicate.Clear();
            TypesOf.Clear();
            InstancesOf.Clear();

            foreach(EncodedTriple triple in All)
            {
                if(!ByPredicate.TryGetValue(triple.Predicate, out List<(TermId, TermId)>? pairs))
                {
                    pairs = [];
                    ByPredicate[triple.Predicate] = pairs;
                }

                pairs.Add((triple.Subject, triple.Object));

                if(!BySubjectPredicate.TryGetValue((triple.Subject, triple.Predicate), out List<TermId>? objects))
                {
                    objects = [];
                    BySubjectPredicate[(triple.Subject, triple.Predicate)] = objects;
                }

                objects.Add(triple.Object);

                if(triple.Predicate == Terms.Type)
                {
                    if(!TypesOf.TryGetValue(triple.Subject, out HashSet<TermId>? types))
                    {
                        types = [];
                        TypesOf[triple.Subject] = types;
                    }

                    types.Add(triple.Object);

                    if(!InstancesOf.TryGetValue(triple.Object, out List<TermId>? instances))
                    {
                        instances = [];
                        InstancesOf[triple.Object] = instances;
                    }

                    instances.Add(triple.Subject);
                }
            }
        }

        /// <summary>Fires every rule family once over the current indexes.</summary>
        public void FireRules()
        {
            FireEquality();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireProperties();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireClasses();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireClassAxioms();
            if(InconsistencyRule is not null)
            {
                return;
            }

            FireSchema();
        }

        /// <summary>Merges the round's pending derivations; <c>false</c> when the round was empty (fixpoint).</summary>
        public bool MergePending()
        {
            bool grew = false;
            foreach(EncodedTriple triple in Pending)
            {
                if(All.Add(triple))
                {
                    Derived.Add(triple);
                    grew = true;
                }
            }

            Pending.Clear();

            return grew;
        }

        //----- eq-* (Table 4) -----

        private void FireEquality()
        {
            List<(TermId Subject, TermId Object)> sameAs = Pairs(Terms.SameAs);

            foreach((TermId x, TermId y) in sameAs)
            {
                //eq-sym.
                Add(y, Terms.SameAs, x);

                //eq-trans.
                foreach(TermId z in ObjectsOf(y, Terms.SameAs))
                {
                    Add(x, Terms.SameAs, z);
                }

                //eq-diff1.
                if(ObjectsOf(x, Terms.DifferentFrom).Contains(y))
                {
                    Inconsistent("eq-diff1");

                    return;
                }

                //dt-* falsity through the datatype oracle: sameAs between
                //literals denoting distinct data values contradicts.
                if(x != y && LiteralsKnownDistinct is not null && LiteralsKnownDistinct(x, y))
                {
                    Inconsistent("dt-diff");

                    return;
                }

                if(x == y)
                {
                    continue;
                }

                //eq-rep-s / eq-rep-p / eq-rep-o: every triple mentioning x
                //holds with y substituted.
                foreach(EncodedTriple triple in All)
                {
                    if(triple.Subject == x)
                    {
                        Add(y, triple.Predicate.Encoded, triple.Object.Encoded);
                    }

                    if(triple.Predicate == x)
                    {
                        Add(triple.Subject.Encoded, y, triple.Object.Encoded);
                    }

                    if(triple.Object == x)
                    {
                        Add(triple.Subject.Encoded, triple.Predicate.Encoded, y);
                    }
                }
            }

            //Difference is symmetric.
            foreach((TermId x, TermId y) in Pairs(Terms.DifferentFrom))
            {
                Add(y, Terms.DifferentFrom, x);
            }

            //eq-diff2 / eq-diff3: two sameAs members of an owl:AllDifferent
            //list contradict.
            foreach((TermId node, TermId type) in Pairs(Terms.Type))
            {
                if(type != Terms.AllDifferent)
                {
                    continue;
                }

                List<TermId>? members = ListOf(node, Terms.Members) ?? ListOf(node, Terms.DistinctMembers);
                if(members is null)
                {
                    continue;
                }

                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        if(members[i] == members[j] || ObjectsOf(members[i], Terms.SameAs).Contains(members[j]))
                        {
                            Inconsistent("eq-diff2");

                            return;
                        }
                    }
                }
            }
        }

        //----- prp-* (Table 5) -----

        private void FireProperties()
        {
            //prp-dom / prp-rng.
            foreach((TermId p, TermId c) in Pairs(Terms.Domain))
            {
                foreach((TermId x, TermId _) in Pairs(p))
                {
                    Add(x, Terms.Type, c);
                }
            }

            foreach((TermId p, TermId c) in Pairs(Terms.Range))
            {
                foreach((TermId _, TermId y) in Pairs(p))
                {
                    Add(y, Terms.Type, c);
                }
            }

            //The characteristic rules key off type statements.
            foreach((TermId p, TermId characteristic) in Pairs(Terms.Type))
            {
                if(characteristic == Terms.FunctionalProperty)
                {
                    //prp-fp.
                    ForEachSamePredicatePair(p, bySubject: true, (first, second) => Add(first, Terms.SameAs, second));
                }
                else if(characteristic == Terms.InverseFunctionalProperty)
                {
                    //prp-ifp.
                    ForEachSamePredicatePair(p, bySubject: false, (first, second) => Add(first, Terms.SameAs, second));
                }
                else if(characteristic == Terms.IrreflexiveProperty)
                {
                    //prp-irp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        if(x == y)
                        {
                            Inconsistent("prp-irp");

                            return;
                        }
                    }
                }
                else if(characteristic == Terms.SymmetricProperty)
                {
                    //prp-symp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        Add(y, p, x);
                    }
                }
                else if(characteristic == Terms.AsymmetricProperty)
                {
                    //prp-asyp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        if(ObjectsOf(y, p).Contains(x))
                        {
                            Inconsistent("prp-asyp");

                            return;
                        }
                    }
                }
                else if(characteristic == Terms.TransitiveProperty)
                {
                    //prp-trp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        foreach(TermId z in ObjectsOf(y, p))
                        {
                            Add(x, p, z);
                        }
                    }
                }
                else if(characteristic == Terms.ReflexiveProperty)
                {
                    //Reflexivity instantiates over the named individuals.
                    if(InstancesOf.TryGetValue(Terms.NamedIndividual, out List<TermId>? individuals))
                    {
                        foreach(TermId x in individuals)
                        {
                            Add(x, p, x);
                        }
                    }
                }
            }

            //prp-spo1.
            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y);
                }
            }

            //prp-spo2: a chain p1 ∘ … ∘ pn ⊑ p.
            foreach((TermId p, TermId listHead) in Pairs(Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is not List<TermId> chain || chain.Count == 0)
                {
                    continue;
                }

                //Walk the chain link by link, extending the frontier of
                //(start, current) pairs.
                List<(TermId Start, TermId Current)> frontier = [];
                foreach((TermId start, TermId next) in Pairs(chain[0]))
                {
                    frontier.Add((start, next));
                }

                for(int i = 1; i < chain.Count && frontier.Count > 0; i++)
                {
                    List<(TermId Start, TermId Current)> extended = [];
                    foreach((TermId start, TermId current) in frontier)
                    {
                        foreach(TermId next in ObjectsOf(current, chain[i]))
                        {
                            extended.Add((start, next));
                        }
                    }

                    frontier = extended;
                }

                foreach((TermId start, TermId end) in frontier)
                {
                    Add(start, p, end);
                }
            }

            //prp-eqp1 / prp-eqp2.
            foreach((TermId p1, TermId p2) in Pairs(Terms.EquivalentProperty))
            {
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y);
                }

                foreach((TermId x, TermId y) in Pairs(p2))
                {
                    Add(x, p1, y);
                }
            }

            //prp-pdw; property disjointness is symmetric.
            foreach((TermId p1, TermId p2) in Pairs(Terms.PropertyDisjointWith))
            {
                Add(p2, Terms.PropertyDisjointWith, p1);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    if(ObjectsOf(x, p2).Contains(y))
                    {
                        Inconsistent("prp-pdw");

                        return;
                    }
                }
            }

            //prp-adp; the reified list also materialises as pairwise
            //owl:propertyDisjointWith statements.
            foreach((TermId node, TermId type) in Pairs(Terms.Type))
            {
                if(type != Terms.AllDisjointProperties || ListOf(node, Terms.Members) is not List<TermId> members)
                {
                    continue;
                }

                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        Add(members[i], Terms.PropertyDisjointWith, members[j]);
                        foreach((TermId x, TermId y) in Pairs(members[i]))
                        {
                            if(ObjectsOf(x, members[j]).Contains(y))
                            {
                                Inconsistent("prp-adp");

                                return;
                            }
                        }
                    }
                }
            }

            //prp-inv1 / prp-inv2.
            foreach((TermId p1, TermId p2) in Pairs(Terms.InverseOf))
            {
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(y, p2, x);
                }

                foreach((TermId x, TermId y) in Pairs(p2))
                {
                    Add(y, p1, x);
                }
            }

            //prp-key: instances of the keyed class sharing a value for every
            //key property are the same individual.
            foreach((TermId c, TermId listHead) in Pairs(Terms.HasKey))
            {
                if(ListOf(listHead) is not List<TermId> keys || keys.Count == 0 || !InstancesOf.TryGetValue(c, out List<TermId>? instances))
                {
                    continue;
                }

                for(int i = 0; i < instances.Count; i++)
                {
                    for(int j = i + 1; j < instances.Count; j++)
                    {
                        bool sharesAll = true;
                        foreach(TermId key in keys)
                        {
                            if(!ShareValue(instances[i], instances[j], key))
                            {
                                sharesAll = false;

                                break;
                            }
                        }

                        if(sharesAll)
                        {
                            Add(instances[i], Terms.SameAs, instances[j]);
                        }
                    }
                }
            }

            //prp-npa1 / prp-npa2.
            foreach((TermId node, TermId type) in Pairs(Terms.Type))
            {
                if(type != Terms.NegativePropertyAssertion)
                {
                    continue;
                }

                TermId? source = SingleObjectOf(node, Terms.SourceIndividual);
                TermId? property = SingleObjectOf(node, Terms.AssertionProperty);
                TermId? target = SingleObjectOf(node, Terms.TargetIndividual) ?? SingleObjectOf(node, Terms.TargetValue);

                if(source is TermId s && property is TermId p && target is TermId t && ObjectsOf(s, p).Contains(t))
                {
                    Inconsistent("prp-npa");

                    return;
                }
            }
        }

        //----- cls-* (Table 6) -----

        private void FireClasses()
        {
            //cls-nothing2.
            if(InstancesOf.ContainsKey(Terms.Nothing))
            {
                Inconsistent("cls-nothing2");

                return;
            }

            //cls-int1 / cls-int2.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                if(InstancesOf.TryGetValue(members[0], out List<TermId>? candidates))
                {
                    foreach(TermId x in candidates)
                    {
                        bool inAll = true;
                        foreach(TermId member in members)
                        {
                            if(!HasType(x, member))
                            {
                                inAll = false;

                                break;
                            }
                        }

                        if(inAll)
                        {
                            Add(x, Terms.Type, c);
                        }
                    }
                }

                if(InstancesOf.TryGetValue(c, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        foreach(TermId member in members)
                        {
                            Add(x, Terms.Type, member);
                        }
                    }
                }
            }

            //cls-uni.
            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is not List<TermId> members)
                {
                    continue;
                }

                foreach(TermId member in members)
                {
                    if(InstancesOf.TryGetValue(member, out List<TermId>? instances))
                    {
                        foreach(TermId x in instances)
                        {
                            Add(x, Terms.Type, c);
                        }
                    }
                }
            }

            //cls-com.
            foreach((TermId c1, TermId c2) in Pairs(Terms.ComplementOf))
            {
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        if(HasType(x, c2))
                        {
                            Inconsistent("cls-com");

                            return;
                        }
                    }
                }
            }

            //The restriction rules key off owl:onProperty.
            foreach((TermId x, TermId p) in Pairs(Terms.OnProperty))
            {
                //cls-svf1 / cls-svf2.
                if(SingleObjectOf(x, Terms.SomeValuesFrom) is TermId someFiller)
                {
                    foreach((TermId u, TermId v) in Pairs(p))
                    {
                        if(someFiller == Terms.Thing || HasType(v, someFiller))
                        {
                            Add(u, Terms.Type, x);
                        }
                    }
                }

                //cls-avf.
                if(SingleObjectOf(x, Terms.AllValuesFrom) is TermId allFiller && InstancesOf.TryGetValue(x, out List<TermId>? avfInstances))
                {
                    foreach(TermId u in avfInstances)
                    {
                        foreach(TermId v in ObjectsOf(u, p))
                        {
                            Add(v, Terms.Type, allFiller);
                        }
                    }
                }

                //cls-hv1 / cls-hv2.
                if(SingleObjectOf(x, Terms.HasValue) is TermId value)
                {
                    if(InstancesOf.TryGetValue(x, out List<TermId>? hvInstances))
                    {
                        foreach(TermId u in hvInstances)
                        {
                            Add(u, p, value);
                        }
                    }

                    foreach((TermId u, TermId v) in Pairs(p))
                    {
                        if(v == value)
                        {
                            Add(u, Terms.Type, x);
                        }
                    }
                }

                //cls-maxc1 / cls-maxc2.
                if(SingleObjectOf(x, Terms.MaxCardinality) is TermId bound)
                {
                    if(Terms.ZeroBounds.Contains(bound) && AnyInstanceHasEdge(x, p))
                    {
                        Inconsistent("cls-maxc1");

                        return;
                    }

                    if(Terms.OneBounds.Contains(bound) && InstancesOf.TryGetValue(x, out List<TermId>? maxcInstances))
                    {
                        foreach(TermId u in maxcInstances)
                        {
                            EquateAllPairs(ObjectsOf(u, p));
                        }
                    }
                }

                //cls-maxqc1–4.
                if(SingleObjectOf(x, Terms.MaxQualifiedCardinality) is TermId qualifiedBound)
                {
                    TermId filler = SingleObjectOf(x, Terms.OnClass) ?? Terms.Thing;

                    if(Terms.ZeroBounds.Contains(qualifiedBound) && InstancesOf.TryGetValue(x, out List<TermId>? maxqcInstances))
                    {
                        foreach(TermId u in maxqcInstances)
                        {
                            foreach(TermId y in ObjectsOf(u, p))
                            {
                                if(filler == Terms.Thing || HasType(y, filler))
                                {
                                    Inconsistent("cls-maxqc1");

                                    return;
                                }
                            }
                        }
                    }

                    if(Terms.OneBounds.Contains(qualifiedBound) && InstancesOf.TryGetValue(x, out List<TermId>? maxqc1Instances))
                    {
                        foreach(TermId u in maxqc1Instances)
                        {
                            List<TermId> qualified = [];
                            foreach(TermId y in ObjectsOf(u, p))
                            {
                                if(filler == Terms.Thing || HasType(y, filler))
                                {
                                    qualified.Add(y);
                                }
                            }

                            EquateAllPairs(qualified);
                        }
                    }
                }
            }

            //cls-oo.
            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    foreach(TermId member in members)
                    {
                        Add(member, Terms.Type, c);
                    }
                }
            }
        }

        //----- cax-* (Table 7) -----

        private void FireClassAxioms()
        {
            //cax-sco.
            foreach((TermId c1, TermId c2) in Pairs(Terms.SubClassOf))
            {
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        Add(x, Terms.Type, c2);
                    }
                }
            }

            //cax-eqc1 / cax-eqc2.
            foreach((TermId c1, TermId c2) in Pairs(Terms.EquivalentClass))
            {
                if(InstancesOf.TryGetValue(c1, out List<TermId>? forward))
                {
                    foreach(TermId x in forward)
                    {
                        Add(x, Terms.Type, c2);
                    }
                }

                if(InstancesOf.TryGetValue(c2, out List<TermId>? backward))
                {
                    foreach(TermId x in backward)
                    {
                        Add(x, Terms.Type, c1);
                    }
                }
            }

            //cax-dw; class disjointness is symmetric.
            foreach((TermId c1, TermId c2) in Pairs(Terms.DisjointWith))
            {
                Add(c2, Terms.DisjointWith, c1);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        if(HasType(x, c2))
                        {
                            Inconsistent("cax-dw");

                            return;
                        }
                    }
                }
            }

            //cax-adc; the reified list also materialises as pairwise
            //owl:disjointWith statements.
            foreach((TermId node, TermId type) in Pairs(Terms.Type))
            {
                if(type != Terms.AllDisjointClasses || ListOf(node, Terms.Members) is not List<TermId> members)
                {
                    continue;
                }

                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        Add(members[i], Terms.DisjointWith, members[j]);
                    }

                    if(!InstancesOf.TryGetValue(members[i], out List<TermId>? instances))
                    {
                        continue;
                    }

                    for(int j = 0; j < members.Count; j++)
                    {
                        if(i == j)
                        {
                            continue;
                        }

                        foreach(TermId x in instances)
                        {
                            if(HasType(x, members[j]))
                            {
                                Inconsistent("cax-adc");

                                return;
                            }
                        }
                    }
                }
            }
        }

        //----- scm-* (Table 9) -----

        private void FireSchema()
        {
            //scm-cls: every declared class is its own sub- and equivalent
            //class, below owl:Thing and above owl:Nothing.
            if(InstancesOf.TryGetValue(Terms.ClassTerm, out List<TermId>? classes))
            {
                foreach(TermId c in classes)
                {
                    Add(c, Terms.SubClassOf, c);
                    Add(c, Terms.EquivalentClass, c);
                    Add(c, Terms.SubClassOf, Terms.Thing);
                    Add(Terms.Nothing, Terms.SubClassOf, c);
                }
            }

            //scm-sco / scm-eqc2.
            foreach((TermId c1, TermId c2) in Pairs(Terms.SubClassOf))
            {
                foreach(TermId c3 in ObjectsOf(c2, Terms.SubClassOf))
                {
                    Add(c1, Terms.SubClassOf, c3);
                }

                if(ObjectsOf(c2, Terms.SubClassOf).Contains(c1))
                {
                    Add(c1, Terms.EquivalentClass, c2);
                }
            }

            //scm-eqc1; equivalence is symmetric.
            foreach((TermId c1, TermId c2) in Pairs(Terms.EquivalentClass))
            {
                Add(c2, Terms.EquivalentClass, c1);
                Add(c1, Terms.SubClassOf, c2);
                Add(c2, Terms.SubClassOf, c1);
            }

            //scm-spo / scm-eqp1 / scm-eqp2.
            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                foreach(TermId p3 in ObjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.SubPropertyOf, p3);
                }

                if(ObjectsOf(p2, Terms.SubPropertyOf).Contains(p1))
                {
                    Add(p1, Terms.EquivalentProperty, p2);
                }
            }

            foreach((TermId p1, TermId p2) in Pairs(Terms.EquivalentProperty))
            {
                Add(p2, Terms.EquivalentProperty, p1);
                Add(p1, Terms.SubPropertyOf, p2);
                Add(p2, Terms.SubPropertyOf, p1);
            }

            //scm-dom1 / scm-dom2.
            foreach((TermId p, TermId c1) in Pairs(Terms.Domain))
            {
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Domain, c2);
                }
            }

            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                foreach(TermId c in ObjectsOf(p2, Terms.Domain))
                {
                    Add(p1, Terms.Domain, c);
                }

                foreach(TermId c in ObjectsOf(p2, Terms.Range))
                {
                    Add(p1, Terms.Range, c);
                }
            }

            //scm-rng1.
            foreach((TermId p, TermId c1) in Pairs(Terms.Range))
            {
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Range, c2);
                }
            }

            //scm-int / scm-uni.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    foreach(TermId member in members)
                    {
                        Add(c, Terms.SubClassOf, member);
                    }
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    foreach(TermId member in members)
                    {
                        Add(member, Terms.SubClassOf, c);
                    }
                }
            }
        }

        //----- Shared helpers -----

        private List<(TermId Subject, TermId Object)> Pairs(TermId predicate)
        {
            return ByPredicate.TryGetValue(predicate, out List<(TermId, TermId)>? pairs) ? pairs : [];
        }

        private List<TermId> ObjectsOf(TermId subject, TermId predicate)
        {
            return BySubjectPredicate.TryGetValue((subject, predicate), out List<TermId>? objects) ? objects : [];
        }

        private TermId? SingleObjectOf(TermId subject, TermId predicate)
        {
            List<TermId> objects = ObjectsOf(subject, predicate);

            return objects.Count > 0 ? objects[0] : null;
        }

        private bool HasType(TermId instance, TermId type)
        {
            return TypesOf.TryGetValue(instance, out HashSet<TermId>? types) && types.Contains(type);
        }

        private bool AnyInstanceHasEdge(TermId restriction, TermId property)
        {
            if(!InstancesOf.TryGetValue(restriction, out List<TermId>? instances))
            {
                return false;
            }

            foreach(TermId u in instances)
            {
                if(ObjectsOf(u, property).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void EquateAllPairs(List<TermId> values)
        {
            for(int i = 0; i < values.Count; i++)
            {
                for(int j = i + 1; j < values.Count; j++)
                {
                    if(values[i] != values[j])
                    {
                        Add(values[i], Terms.SameAs, values[j]);
                    }
                }
            }
        }

        private bool ShareValue(TermId first, TermId second, TermId property)
        {
            List<TermId> firstValues = ObjectsOf(first, property);
            List<TermId> secondValues = ObjectsOf(second, property);
            foreach(TermId value in firstValues)
            {
                if(secondValues.Contains(value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Walks an RDF collection into members; <c>null</c> on malformed or cyclic chains.</summary>
        private List<TermId>? ListOf(TermId head)
        {
            List<TermId> members = [];
            HashSet<TermId> visited = [];
            TermId current = head;

            while(current != Terms.Nil)
            {
                if(!visited.Add(current))
                {
                    return null;
                }

                TermId? first = SingleObjectOf(current, Terms.First);
                TermId? rest = SingleObjectOf(current, Terms.Rest);
                if(first is not TermId firstValue || rest is not TermId restValue)
                {
                    return null;
                }

                members.Add(firstValue);
                current = restValue;
            }

            return members;
        }

        private List<TermId>? ListOf(TermId subject, TermId predicate)
        {
            return SingleObjectOf(subject, predicate) is TermId head ? ListOf(head) : null;
        }

        //Fires the functional/inverse-functional pairing: for every two
        //triples of the property sharing a subject (or object), the other
        //ends equate.
        private void ForEachSamePredicatePair(TermId property, bool bySubject, Action<TermId, TermId> equate)
        {
            Dictionary<TermId, List<TermId>> groups = [];
            foreach((TermId s, TermId o) in Pairs(property))
            {
                TermId key = bySubject ? s : o;
                TermId value = bySubject ? o : s;
                if(!groups.TryGetValue(key, out List<TermId>? values))
                {
                    values = [];
                    groups[key] = values;
                }

                values.Add(value);
            }

            foreach(KeyValuePair<TermId, List<TermId>> group in groups)
            {
                List<TermId> values = group.Value;
                for(int i = 0; i < values.Count; i++)
                {
                    for(int j = i + 1; j < values.Count; j++)
                    {
                        if(values[i] != values[j])
                        {
                            equate(values[i], values[j]);
                        }
                    }
                }
            }
        }

        private void Add(TermId subject, TermId predicate, TermId @object)
        {
            Add(subject.Encoded, predicate.Encoded, @object.Encoded);
        }

        private void Add(TermId subject, uint predicate, uint @object)
        {
            Add(subject.Encoded, predicate, @object);
        }

        private void Add(uint subject, TermId predicate, uint @object)
        {
            Add(subject, predicate.Encoded, @object);
        }

        private void Add(uint subject, uint predicate, TermId @object)
        {
            Add(subject, predicate, @object.Encoded);
        }

        private void Add(uint subject, uint predicate, uint @object)
        {
            EncodedTriple triple = EncodedTriple.FromEncoded(subject, predicate, @object);
            if(!All.Contains(triple))
            {
                Pending.Add(triple);
            }
        }

        private void Inconsistent(string rule)
        {
            InconsistencyRule = rule;
        }
    }
}
