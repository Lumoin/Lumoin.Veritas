using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
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

    /// <summary>The triples the falsity rule matched — why the closure is inconsistent; empty when consistent.</summary>
    public ImmutableArray<EncodedTriple> InconsistencyPremises { get; }

    /// <summary>
    /// Initialises the result.
    /// </summary>
    /// <param name="derived">The derived triples.</param>
    /// <param name="isConsistent">Whether no contradiction was derived.</param>
    /// <param name="inconsistencyRule">The falsity rule that fired, or <c>null</c>.</param>
    /// <param name="inconsistencyPremises">The triples the falsity rule matched; empty when consistent.</param>
    public OwlRlResult(IReadOnlyCollection<EncodedTriple> derived, bool isConsistent, string? inconsistencyRule, ImmutableArray<EncodedTriple> inconsistencyPremises)
    {
        Derived = derived;
        IsConsistent = isConsistent;
        InconsistencyRule = inconsistencyRule;
        InconsistencyPremises = inconsistencyPremises;
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
/// The datatype (<c>dt-*</c>) falsities fire through the
/// <see cref="OwlRlDatatypeOracle"/>: literal distinctness under
/// <c>owl:sameAs</c>, and value-space membership under range and
/// universal-restriction typing. The oracle owns the value semantics; the
/// closure itself never inspects a literal.
/// </para>
/// </remarks>
public static class OwlRlClosure
{
    /// <summary>
    /// Computes the RL closure of <paramref name="triples"/>.
    /// </summary>
    /// <param name="triples">The base triples, schema statements included.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">
    /// The datatype oracle for the <c>dt-*</c> falsities — literal
    /// distinctness under <c>owl:sameAs</c> and value-space membership
    /// under range and universal-restriction typing. The term-id-level
    /// closure cannot inspect literal values itself;
    /// <see cref="OwlRlDatatypeOracle.None"/> disables both checks.
    /// </param>
    /// <param name="traceHandler">Optional handler receiving one <see cref="InferenceTraceEvent"/> per derivation, premises included.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Required when <paramref name="traceHandler"/> is non-<c>null</c>; ignored otherwise.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts derivation between rounds.</param>
    /// <returns>The derived triples and the consistency verdict, with the falsity's premises when inconsistent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static OwlRlResult Compute(
        IEnumerable<EncodedTriple> triples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle = default,
        TraceHandler<InferenceTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);
        ArgumentNullException.ThrowIfNull(terms);

        if(traceHandler is not null && timeProvider is null)
        {
            throw new ArgumentException("A time provider must be supplied when a trace handler is configured.", nameof(timeProvider));
        }

        ClosureContext context = new(
            triples,
            terms,
            datatypeOracle.LiteralsKnownDistinct is null ? OwlRlDatatypeOracle.None : datatypeOracle,
            traceHandler,
            timeProvider,
            correlationId);

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

        return new OwlRlResult(context.Derived, context.InconsistencyRule is null, context.InconsistencyRule, context.InconsistencyPremises);
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

        /// <summary>The triples the falsity rule matched; empty while consistent.</summary>
        public ImmutableArray<EncodedTriple> InconsistencyPremises { get; private set; } = [];

        /// <summary>The trace handler, or <c>null</c> for no tracing.</summary>
        private TraceHandler<InferenceTraceEvent>? TraceHandler { get; }

        /// <summary>Clock for trace timestamps; non-<c>null</c> whenever <see cref="TraceHandler"/> is.</summary>
        private TimeProvider? TimeProvider { get; }

        /// <summary>The run's correlation id.</summary>
        private Guid CorrelationId { get; }

        /// <summary>Monotonic sequence for emitted trace events.</summary>
        private long TraceSequence { get; set; }

        /// <summary>Per-predicate (subject, object) pairs.</summary>
        private Dictionary<TermId, List<(TermId Subject, TermId Object)>> ByPredicate { get; } = [];

        /// <summary>(subject, predicate) → objects.</summary>
        private Dictionary<(TermId Subject, TermId Predicate), List<TermId>> BySubjectPredicate { get; } = [];

        /// <summary>Instance → its types.</summary>
        private Dictionary<TermId, HashSet<TermId>> TypesOf { get; } = [];

        /// <summary>Type → its instances.</summary>
        private Dictionary<TermId, List<TermId>> InstancesOf { get; } = [];

        /// <summary>The datatype oracle for the <c>dt-*</c> falsities.</summary>
        private OwlRlDatatypeOracle DatatypeOracle { get; }

        public ClosureContext(
            IEnumerable<EncodedTriple> triples,
            OwlRlTerms terms,
            OwlRlDatatypeOracle datatypeOracle,
            TraceHandler<InferenceTraceEvent>? traceHandler,
            TimeProvider? timeProvider,
            Guid correlationId)
        {
            Terms = terms;
            DatatypeOracle = datatypeOracle;
            TraceHandler = traceHandler;
            TimeProvider = timeProvider;
            CorrelationId = correlationId;
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
                EncodedTriple same = Fact(x, Terms.SameAs, y);

                //eq-sym.
                Add(y, Terms.SameAs, x, EntailmentRules.EqSym, [same]);

                //eq-trans.
                foreach(TermId z in ObjectsOf(y, Terms.SameAs))
                {
                    Add(x, Terms.SameAs, z, EntailmentRules.EqTrans, [same, Fact(y, Terms.SameAs, z)]);
                }

                //eq-diff1.
                if(ObjectsOf(x, Terms.DifferentFrom).Contains(y))
                {
                    Inconsistent(EntailmentRules.EqDiff1, [same, Fact(x, Terms.DifferentFrom, y)]);

                    return;
                }

                //dt-* falsity through the datatype oracle: sameAs between
                //literals denoting distinct data values contradicts.
                if(x != y && DatatypeOracle.LiteralsKnownDistinct(x, y))
                {
                    Inconsistent(EntailmentRules.DtDiff, [same]);

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
                        Add(y, triple.Predicate, triple.Object, EntailmentRules.EqRepS, [same, triple]);
                    }

                    if(triple.Predicate == x)
                    {
                        Add(triple.Subject, y, triple.Object, EntailmentRules.EqRepP, [same, triple]);
                    }

                    if(triple.Object == x)
                    {
                        Add(triple.Subject, triple.Predicate, y, EntailmentRules.EqRepO, [same, triple]);
                    }
                }
            }

            //Difference is symmetric.
            foreach((TermId x, TermId y) in Pairs(Terms.DifferentFrom))
            {
                Add(y, Terms.DifferentFrom, x, EntailmentRules.DifferentFromSymmetry, [Fact(x, Terms.DifferentFrom, y)]);
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

                EncodedTriple allDifferent = Fact(node, Terms.Type, Terms.AllDifferent);
                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        if(members[i] == members[j])
                        {
                            Inconsistent(EntailmentRules.EqDiff2, [allDifferent]);

                            return;
                        }

                        if(ObjectsOf(members[i], Terms.SameAs).Contains(members[j]))
                        {
                            Inconsistent(EntailmentRules.EqDiff2, [allDifferent, Fact(members[i], Terms.SameAs, members[j])]);

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
                EncodedTriple domain = Fact(p, Terms.Domain, c);
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    Add(x, Terms.Type, c, EntailmentRules.PrpDom, [domain, Fact(x, p, y)]);
                }
            }

            foreach((TermId p, TermId c) in Pairs(Terms.Range))
            {
                EncodedTriple range = Fact(p, Terms.Range, c);
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    //dt-not-type: a literal value outside the range
                    //datatype's value space contradicts.
                    if(DatatypeOracle.LiteralOutsideDatatype(y, c))
                    {
                        Inconsistent(EntailmentRules.DtNotType, [range, Fact(x, p, y)]);

                        return;
                    }

                    Add(y, Terms.Type, c, EntailmentRules.PrpRng, [range, Fact(x, p, y)]);
                }
            }

            //The characteristic rules key off type statements.
            foreach((TermId p, TermId characteristic) in Pairs(Terms.Type))
            {
                EncodedTriple typing = Fact(p, Terms.Type, characteristic);

                if(characteristic == Terms.FunctionalProperty)
                {
                    //prp-fp.
                    ForEachSamePredicatePair(p, bySubject: true, (key, first, second) =>
                        Add(first, Terms.SameAs, second, EntailmentRules.PrpFp, [typing, Fact(key, p, first), Fact(key, p, second)]));
                }
                else if(characteristic == Terms.InverseFunctionalProperty)
                {
                    //prp-ifp.
                    ForEachSamePredicatePair(p, bySubject: false, (key, first, second) =>
                        Add(first, Terms.SameAs, second, EntailmentRules.PrpIfp, [typing, Fact(first, p, key), Fact(second, p, key)]));
                }
                else if(characteristic == Terms.IrreflexiveProperty)
                {
                    //prp-irp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        if(x == y)
                        {
                            Inconsistent(EntailmentRules.PrpIrp, [typing, Fact(x, p, x)]);

                            return;
                        }
                    }
                }
                else if(characteristic == Terms.SymmetricProperty)
                {
                    //prp-symp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        Add(y, p, x, EntailmentRules.PrpSymp, [typing, Fact(x, p, y)]);
                    }
                }
                else if(characteristic == Terms.AsymmetricProperty)
                {
                    //prp-asyp.
                    foreach((TermId x, TermId y) in Pairs(p))
                    {
                        if(ObjectsOf(y, p).Contains(x))
                        {
                            Inconsistent(EntailmentRules.PrpAsyp, [typing, Fact(x, p, y), Fact(y, p, x)]);

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
                            Add(x, p, z, EntailmentRules.PrpTrp, [typing, Fact(x, p, y), Fact(y, p, z)]);
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
                            Add(x, p, x, EntailmentRules.ReflexiveInstantiation, [typing, Fact(x, Terms.Type, Terms.NamedIndividual)]);
                        }
                    }
                }
            }

            //prp-spo1.
            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y, EntailmentRules.PrpSpo1, [subProperty, Fact(x, p1, y)]);
                }
            }

            //prp-spo2: a chain p1 ∘ … ∘ pn ⊑ p.
            foreach((TermId p, TermId listHead) in Pairs(Terms.PropertyChainAxiom))
            {
                if(ListOf(listHead) is not List<TermId> chain || chain.Count == 0)
                {
                    continue;
                }

                EncodedTriple chainAxiom = Fact(p, Terms.PropertyChainAxiom, listHead);

                //Walk the chain link by link, extending the frontier of
                //(start, current) pairs; each pair carries the hop triples
                //it traversed, so the conclusion's provenance is complete.
                List<(TermId Start, TermId Current, List<EncodedTriple> Hops)> frontier = [];
                foreach((TermId start, TermId next) in Pairs(chain[0]))
                {
                    frontier.Add((start, next, [Fact(start, chain[0], next)]));
                }

                for(int i = 1; i < chain.Count && frontier.Count > 0; i++)
                {
                    List<(TermId Start, TermId Current, List<EncodedTriple> Hops)> extended = [];
                    foreach((TermId start, TermId current, List<EncodedTriple> hops) in frontier)
                    {
                        foreach(TermId next in ObjectsOf(current, chain[i]))
                        {
                            extended.Add((start, next, [.. hops, Fact(current, chain[i], next)]));
                        }
                    }

                    frontier = extended;
                }

                foreach((TermId start, TermId end, List<EncodedTriple> hops) in frontier)
                {
                    Add(start, p, end, EntailmentRules.PrpSpo2, [chainAxiom, .. hops]);
                }
            }

            //prp-eqp1 / prp-eqp2.
            foreach((TermId p1, TermId p2) in Pairs(Terms.EquivalentProperty))
            {
                EncodedTriple equivalent = Fact(p1, Terms.EquivalentProperty, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(x, p2, y, EntailmentRules.PrpEqp1, [equivalent, Fact(x, p1, y)]);
                }

                foreach((TermId x, TermId y) in Pairs(p2))
                {
                    Add(x, p1, y, EntailmentRules.PrpEqp2, [equivalent, Fact(x, p2, y)]);
                }
            }

            //prp-pdw; property disjointness is symmetric.
            foreach((TermId p1, TermId p2) in Pairs(Terms.PropertyDisjointWith))
            {
                EncodedTriple disjoint = Fact(p1, Terms.PropertyDisjointWith, p2);
                Add(p2, Terms.PropertyDisjointWith, p1, EntailmentRules.PrpPdw, [disjoint]);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    if(ObjectsOf(x, p2).Contains(y))
                    {
                        Inconsistent(EntailmentRules.PrpPdw, [disjoint, Fact(x, p1, y), Fact(x, p2, y)]);

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

                EncodedTriple allDisjoint = Fact(node, Terms.Type, Terms.AllDisjointProperties);
                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        Add(members[i], Terms.PropertyDisjointWith, members[j], EntailmentRules.PrpAdp, [allDisjoint]);
                        foreach((TermId x, TermId y) in Pairs(members[i]))
                        {
                            if(ObjectsOf(x, members[j]).Contains(y))
                            {
                                Inconsistent(EntailmentRules.PrpAdp, [allDisjoint, Fact(x, members[i], y), Fact(x, members[j], y)]);

                                return;
                            }
                        }
                    }
                }
            }

            //prp-inv1 / prp-inv2.
            foreach((TermId p1, TermId p2) in Pairs(Terms.InverseOf))
            {
                EncodedTriple inverse = Fact(p1, Terms.InverseOf, p2);
                foreach((TermId x, TermId y) in Pairs(p1))
                {
                    Add(y, p2, x, EntailmentRules.PrpInv1, [inverse, Fact(x, p1, y)]);
                }

                foreach((TermId x, TermId y) in Pairs(p2))
                {
                    Add(y, p1, x, EntailmentRules.PrpInv2, [inverse, Fact(x, p2, y)]);
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

                EncodedTriple hasKey = Fact(c, Terms.HasKey, listHead);
                for(int i = 0; i < instances.Count; i++)
                {
                    for(int j = i + 1; j < instances.Count; j++)
                    {
                        //The premises accumulate one shared-value witness
                        //pair per key property.
                        List<EncodedTriple> premises = [hasKey, Fact(instances[i], Terms.Type, c), Fact(instances[j], Terms.Type, c)];
                        bool sharesAll = true;
                        foreach(TermId key in keys)
                        {
                            if(!TryGetSharedValue(instances[i], instances[j], key, out TermId value))
                            {
                                sharesAll = false;

                                break;
                            }

                            premises.Add(Fact(instances[i], key, value));
                            premises.Add(Fact(instances[j], key, value));
                        }

                        if(sharesAll)
                        {
                            Add(instances[i], Terms.SameAs, instances[j], EntailmentRules.PrpKey, [.. premises]);
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
                    Inconsistent(EntailmentRules.PrpNpa, [Fact(node, Terms.Type, Terms.NegativePropertyAssertion), Fact(s, p, t)]);

                    return;
                }
            }
        }

        //----- cls-* (Table 6) -----

        private void FireClasses()
        {
            //cls-nothing2.
            if(InstancesOf.TryGetValue(Terms.Nothing, out List<TermId>? nothings) && nothings.Count > 0)
            {
                Inconsistent(EntailmentRules.ClsNothing2, [Fact(nothings[0], Terms.Type, Terms.Nothing)]);

                return;
            }

            //cls-int1 / cls-int2.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                EncodedTriple intersection = Fact(c, Terms.IntersectionOf, listHead);

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
                            List<EncodedTriple> premises = [intersection];
                            foreach(TermId member in members)
                            {
                                premises.Add(Fact(x, Terms.Type, member));
                            }

                            Add(x, Terms.Type, c, EntailmentRules.ClsInt1, [.. premises]);
                        }
                    }
                }

                if(InstancesOf.TryGetValue(c, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        foreach(TermId member in members)
                        {
                            Add(x, Terms.Type, member, EntailmentRules.ClsInt2, [intersection, Fact(x, Terms.Type, c)]);
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

                EncodedTriple union = Fact(c, Terms.UnionOf, listHead);
                foreach(TermId member in members)
                {
                    if(InstancesOf.TryGetValue(member, out List<TermId>? instances))
                    {
                        foreach(TermId x in instances)
                        {
                            Add(x, Terms.Type, c, EntailmentRules.ClsUni, [union, Fact(x, Terms.Type, member)]);
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
                            Inconsistent(EntailmentRules.ClsCom, [Fact(c1, Terms.ComplementOf, c2), Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c2)]);

                            return;
                        }
                    }
                }
            }

            //The restriction rules key off owl:onProperty.
            foreach((TermId x, TermId p) in Pairs(Terms.OnProperty))
            {
                EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);

                //cls-svf1 / cls-svf2.
                if(SingleObjectOf(x, Terms.SomeValuesFrom) is TermId someFiller)
                {
                    EncodedTriple someValues = Fact(x, Terms.SomeValuesFrom, someFiller);
                    foreach((TermId u, TermId v) in Pairs(p))
                    {
                        if(someFiller == Terms.Thing)
                        {
                            Add(u, Terms.Type, x, EntailmentRules.ClsSvf2, [onProperty, someValues, Fact(u, p, v)]);
                        }
                        else if(HasType(v, someFiller))
                        {
                            Add(u, Terms.Type, x, EntailmentRules.ClsSvf1, [onProperty, someValues, Fact(u, p, v), Fact(v, Terms.Type, someFiller)]);
                        }
                    }
                }

                //cls-avf.
                if(SingleObjectOf(x, Terms.AllValuesFrom) is TermId allFiller && InstancesOf.TryGetValue(x, out List<TermId>? avfInstances))
                {
                    EncodedTriple allValues = Fact(x, Terms.AllValuesFrom, allFiller);
                    foreach(TermId u in avfInstances)
                    {
                        foreach(TermId v in ObjectsOf(u, p))
                        {
                            if(DatatypeOracle.LiteralOutsideDatatype(v, allFiller))
                            {
                                Inconsistent(EntailmentRules.DtNotType, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);

                                return;
                            }

                            Add(v, Terms.Type, allFiller, EntailmentRules.ClsAvf, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);
                        }
                    }
                }

                //cls-hv1 / cls-hv2.
                if(SingleObjectOf(x, Terms.HasValue) is TermId value)
                {
                    EncodedTriple hasValue = Fact(x, Terms.HasValue, value);
                    if(InstancesOf.TryGetValue(x, out List<TermId>? hvInstances))
                    {
                        foreach(TermId u in hvInstances)
                        {
                            Add(u, p, value, EntailmentRules.ClsHv1, [onProperty, hasValue, Fact(u, Terms.Type, x)]);
                        }
                    }

                    foreach((TermId u, TermId v) in Pairs(p))
                    {
                        if(v == value)
                        {
                            Add(u, Terms.Type, x, EntailmentRules.ClsHv2, [onProperty, hasValue, Fact(u, p, value)]);
                        }
                    }
                }

                //cls-maxc1 / cls-maxc2.
                if(SingleObjectOf(x, Terms.MaxCardinality) is TermId bound)
                {
                    EncodedTriple maxCardinality = Fact(x, Terms.MaxCardinality, bound);
                    if(Terms.ZeroBounds.Contains(bound) && TryFindInstanceEdge(x, p, out TermId edgeSubject, out TermId edgeObject))
                    {
                        Inconsistent(EntailmentRules.ClsMaxc1, [onProperty, maxCardinality, Fact(edgeSubject, Terms.Type, x), Fact(edgeSubject, p, edgeObject)]);

                        return;
                    }

                    if(Terms.OneBounds.Contains(bound) && InstancesOf.TryGetValue(x, out List<TermId>? maxcInstances))
                    {
                        foreach(TermId u in maxcInstances)
                        {
                            EquateAllPairs(ObjectsOf(u, p), EntailmentRules.ClsMaxc2, u, p, [onProperty, maxCardinality, Fact(u, Terms.Type, x)]);
                        }
                    }
                }

                //cls-maxqc1–4.
                if(SingleObjectOf(x, Terms.MaxQualifiedCardinality) is TermId qualifiedBound)
                {
                    EncodedTriple maxQualified = Fact(x, Terms.MaxQualifiedCardinality, qualifiedBound);
                    TermId filler = SingleObjectOf(x, Terms.OnClass) ?? Terms.Thing;

                    if(Terms.ZeroBounds.Contains(qualifiedBound) && InstancesOf.TryGetValue(x, out List<TermId>? maxqcInstances))
                    {
                        foreach(TermId u in maxqcInstances)
                        {
                            foreach(TermId y in ObjectsOf(u, p))
                            {
                                if(filler == Terms.Thing)
                                {
                                    Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, y)]);

                                    return;
                                }

                                if(HasType(y, filler))
                                {
                                    Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, y), Fact(y, Terms.Type, filler)]);

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

                            EquateAllPairs(qualified, EntailmentRules.ClsMaxqc4, u, p, [onProperty, maxQualified, Fact(u, Terms.Type, x)]);
                        }
                    }
                }
            }

            //cls-oo.
            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    EncodedTriple oneOf = Fact(c, Terms.OneOf, listHead);
                    foreach(TermId member in members)
                    {
                        Add(member, Terms.Type, c, EntailmentRules.ClsOo, [oneOf]);
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
                    EncodedTriple subClass = Fact(c1, Terms.SubClassOf, c2);
                    foreach(TermId x in instances)
                    {
                        Add(x, Terms.Type, c2, EntailmentRules.CaxSco, [subClass, Fact(x, Terms.Type, c1)]);
                    }
                }
            }

            //cax-eqc1 / cax-eqc2.
            foreach((TermId c1, TermId c2) in Pairs(Terms.EquivalentClass))
            {
                EncodedTriple equivalent = Fact(c1, Terms.EquivalentClass, c2);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? forward))
                {
                    foreach(TermId x in forward)
                    {
                        Add(x, Terms.Type, c2, EntailmentRules.CaxEqc1, [equivalent, Fact(x, Terms.Type, c1)]);
                    }
                }

                if(InstancesOf.TryGetValue(c2, out List<TermId>? backward))
                {
                    foreach(TermId x in backward)
                    {
                        Add(x, Terms.Type, c1, EntailmentRules.CaxEqc2, [equivalent, Fact(x, Terms.Type, c2)]);
                    }
                }
            }

            //cax-dw; class disjointness is symmetric.
            foreach((TermId c1, TermId c2) in Pairs(Terms.DisjointWith))
            {
                EncodedTriple disjoint = Fact(c1, Terms.DisjointWith, c2);
                Add(c2, Terms.DisjointWith, c1, EntailmentRules.CaxDw, [disjoint]);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        if(HasType(x, c2))
                        {
                            Inconsistent(EntailmentRules.CaxDw, [disjoint, Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c2)]);

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

                EncodedTriple allDisjoint = Fact(node, Terms.Type, Terms.AllDisjointClasses);
                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        Add(members[i], Terms.DisjointWith, members[j], EntailmentRules.CaxAdc, [allDisjoint]);
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
                                Inconsistent(EntailmentRules.CaxAdc, [allDisjoint, Fact(x, Terms.Type, members[i]), Fact(x, Terms.Type, members[j])]);

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
                    EncodedTriple declaration = Fact(c, Terms.Type, Terms.ClassTerm);
                    Add(c, Terms.SubClassOf, c, EntailmentRules.ScmCls, [declaration]);
                    Add(c, Terms.EquivalentClass, c, EntailmentRules.ScmCls, [declaration]);
                    Add(c, Terms.SubClassOf, Terms.Thing, EntailmentRules.ScmCls, [declaration]);
                    Add(Terms.Nothing, Terms.SubClassOf, c, EntailmentRules.ScmCls, [declaration]);
                }
            }

            //scm-sco / scm-eqc2.
            foreach((TermId c1, TermId c2) in Pairs(Terms.SubClassOf))
            {
                EncodedTriple subClass = Fact(c1, Terms.SubClassOf, c2);
                foreach(TermId c3 in ObjectsOf(c2, Terms.SubClassOf))
                {
                    Add(c1, Terms.SubClassOf, c3, EntailmentRules.ScmSco, [subClass, Fact(c2, Terms.SubClassOf, c3)]);
                }

                if(ObjectsOf(c2, Terms.SubClassOf).Contains(c1))
                {
                    Add(c1, Terms.EquivalentClass, c2, EntailmentRules.ScmEqc2, [subClass, Fact(c2, Terms.SubClassOf, c1)]);
                }
            }

            //scm-eqc1; equivalence is symmetric.
            foreach((TermId c1, TermId c2) in Pairs(Terms.EquivalentClass))
            {
                EncodedTriple equivalent = Fact(c1, Terms.EquivalentClass, c2);
                Add(c2, Terms.EquivalentClass, c1, EntailmentRules.ScmEqc1, [equivalent]);
                Add(c1, Terms.SubClassOf, c2, EntailmentRules.ScmEqc1, [equivalent]);
                Add(c2, Terms.SubClassOf, c1, EntailmentRules.ScmEqc1, [equivalent]);
            }

            //scm-spo / scm-eqp1 / scm-eqp2.
            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach(TermId p3 in ObjectsOf(p2, Terms.SubPropertyOf))
                {
                    Add(p1, Terms.SubPropertyOf, p3, EntailmentRules.ScmSpo, [subProperty, Fact(p2, Terms.SubPropertyOf, p3)]);
                }

                if(ObjectsOf(p2, Terms.SubPropertyOf).Contains(p1))
                {
                    Add(p1, Terms.EquivalentProperty, p2, EntailmentRules.ScmEqp2, [subProperty, Fact(p2, Terms.SubPropertyOf, p1)]);
                }
            }

            foreach((TermId p1, TermId p2) in Pairs(Terms.EquivalentProperty))
            {
                EncodedTriple equivalent = Fact(p1, Terms.EquivalentProperty, p2);
                Add(p2, Terms.EquivalentProperty, p1, EntailmentRules.ScmEqp1, [equivalent]);
                Add(p1, Terms.SubPropertyOf, p2, EntailmentRules.ScmEqp1, [equivalent]);
                Add(p2, Terms.SubPropertyOf, p1, EntailmentRules.ScmEqp1, [equivalent]);
            }

            //scm-dom1 / scm-dom2.
            foreach((TermId p, TermId c1) in Pairs(Terms.Domain))
            {
                EncodedTriple domain = Fact(p, Terms.Domain, c1);
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Domain, c2, EntailmentRules.ScmDom1, [domain, Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                foreach(TermId c in ObjectsOf(p2, Terms.Domain))
                {
                    Add(p1, Terms.Domain, c, EntailmentRules.ScmDom2, [subProperty, Fact(p2, Terms.Domain, c)]);
                }

                foreach(TermId c in ObjectsOf(p2, Terms.Range))
                {
                    Add(p1, Terms.Range, c, EntailmentRules.ScmRng2, [subProperty, Fact(p2, Terms.Range, c)]);
                }
            }

            //scm-rng1.
            foreach((TermId p, TermId c1) in Pairs(Terms.Range))
            {
                EncodedTriple range = Fact(p, Terms.Range, c1);
                foreach(TermId c2 in ObjectsOf(c1, Terms.SubClassOf))
                {
                    Add(p, Terms.Range, c2, EntailmentRules.ScmRng1, [range, Fact(c1, Terms.SubClassOf, c2)]);
                }
            }

            //scm-int / scm-uni.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    EncodedTriple intersection = Fact(c, Terms.IntersectionOf, listHead);
                    foreach(TermId member in members)
                    {
                        Add(c, Terms.SubClassOf, member, EntailmentRules.ScmInt, [intersection]);
                    }
                }
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is List<TermId> members)
                {
                    EncodedTriple union = Fact(c, Terms.UnionOf, listHead);
                    foreach(TermId member in members)
                    {
                        Add(member, Terms.SubClassOf, c, EntailmentRules.ScmUni, [union]);
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

        //Finds one instance of the restriction with an edge of the
        //property — the witness pair a max-0 falsity reports.
        private bool TryFindInstanceEdge(TermId restriction, TermId property, out TermId subject, out TermId @object)
        {
            if(InstancesOf.TryGetValue(restriction, out List<TermId>? instances))
            {
                foreach(TermId u in instances)
                {
                    List<TermId> objects = ObjectsOf(u, property);
                    if(objects.Count > 0)
                    {
                        subject = u;
                        @object = objects[0];

                        return true;
                    }
                }
            }

            subject = default;
            @object = default;

            return false;
        }

        //Equates every pair of values an instance reaches over the
        //property — the max-1 cardinality conclusion. The shared premises
        //carry the restriction; the per-pair edges complete them.
        private void EquateAllPairs(List<TermId> values, string rule, TermId instance, TermId property, scoped ReadOnlySpan<EncodedTriple> shared)
        {
            for(int i = 0; i < values.Count; i++)
            {
                for(int j = i + 1; j < values.Count; j++)
                {
                    if(values[i] != values[j])
                    {
                        Add(values[i], Terms.SameAs, values[j], rule, [.. shared, Fact(instance, property, values[i]), Fact(instance, property, values[j])]);
                    }
                }
            }
        }

        //Finds one value both individuals reach over the property — the
        //shared-key witness prp-key reports.
        private bool TryGetSharedValue(TermId first, TermId second, TermId property, out TermId value)
        {
            List<TermId> firstValues = ObjectsOf(first, property);
            List<TermId> secondValues = ObjectsOf(second, property);
            foreach(TermId candidate in firstValues)
            {
                if(secondValues.Contains(candidate))
                {
                    value = candidate;

                    return true;
                }
            }

            value = default;

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
        //ends equate; the callback receives the shared key so it can name
        //its premises.
        private void ForEachSamePredicatePair(TermId property, bool bySubject, Action<TermId, TermId, TermId> equate)
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
                            equate(group.Key, values[i], values[j]);
                        }
                    }
                }
            }
        }

        /// <summary>Builds the encoded triple of three term ids — the shape every premise and conclusion takes.</summary>
        /// <param name="subject">The subject term.</param>
        /// <param name="predicate">The predicate term.</param>
        /// <param name="object">The object term.</param>
        /// <returns>The encoded triple.</returns>
        private static EncodedTriple Fact(TermId subject, TermId predicate, TermId @object)
        {
            return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
        }

        /// <summary>
        /// Records one derivation: the rule, the premises it matched, and
        /// the concluded triple. The derivation record is the API — the
        /// trace handler observes it; a conclusion already present derives
        /// nothing and emits nothing.
        /// </summary>
        /// <param name="subject">The conclusion's subject.</param>
        /// <param name="predicate">The conclusion's predicate.</param>
        /// <param name="object">The conclusion's object.</param>
        /// <param name="rule">The rule that fired — a name from <see cref="EntailmentRules"/>.</param>
        /// <param name="premises">The triples the rule matched.</param>
        private void Add(TermId subject, TermId predicate, TermId @object, string rule, scoped ReadOnlySpan<EncodedTriple> premises)
        {
            EncodedTriple conclusion = Fact(subject, predicate, @object);
            if(All.Contains(conclusion))
            {
                return;
            }

            Pending.Add(conclusion);

            if(TraceHandler is { } handler)
            {
                InferenceTraceEvent evt = new(
                    ++TraceSequence,
                    TimeProvider!.GetUtcNow().UtcTicks,
                    CorrelationId,
                    rule,
                    [.. premises],
                    conclusion);

                handler(in evt);
            }
        }

        /// <summary>Records the falsity that fired and the triples it matched.</summary>
        /// <param name="rule">The falsity rule — a name from <see cref="EntailmentRules"/>.</param>
        /// <param name="premises">The triples the falsity matched.</param>
        private void Inconsistent(string rule, scoped ReadOnlySpan<EncodedTriple> premises)
        {
            InconsistencyRule = rule;
            InconsistencyPremises = [.. premises];
        }
    }
}
