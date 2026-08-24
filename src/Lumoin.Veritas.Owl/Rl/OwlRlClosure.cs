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

    /// <summary>The ill-formed encodings the closure declined to read — broken or cyclic list chains, and ambiguous mint sources. Empty on well-formed input; a verdict computed beside recorded shapes covers less than the graph asserts.</summary>
    public ImmutableArray<MalformedShape> MalformedShapes { get; }

    /// <summary>
    /// Initialises the result.
    /// </summary>
    /// <param name="derived">The derived triples.</param>
    /// <param name="isConsistent">Whether no contradiction was derived.</param>
    /// <param name="inconsistencyRule">The falsity rule that fired, or <c>null</c>.</param>
    /// <param name="inconsistencyPremises">The triples the falsity rule matched; empty when consistent.</param>
    /// <param name="malformedShapes">The ill-formed encodings the closure declined to read.</param>
    public OwlRlResult(IReadOnlyCollection<EncodedTriple> derived, bool isConsistent, string? inconsistencyRule, ImmutableArray<EncodedTriple> inconsistencyPremises, ImmutableArray<MalformedShape> malformedShapes)
    {
        Derived = derived;
        IsConsistent = isConsistent;
        InconsistencyRule = inconsistencyRule;
        InconsistencyPremises = inconsistencyPremises;
        MalformedShapes = malformedShapes;
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
public static partial class OwlRlClosure
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
    /// <param name="comprehension">How the closure reads the comprehension conditions: <see cref="OwlComprehension.InformativeConditions"/> fires the comprehension completion family, the entailment path's mode; the default keeps the normative rule set alone.</param>
    /// <param name="axiomaticVocabulary">Which axiomatic vocabulary table seeds the closure: <see cref="OwlAxiomaticVocabulary.MetaclassMerged"/> adds the RDF-Based <c>owl:Class</c>/<c>rdfs:Class</c> metaclass merge; the default keeps the shared table alone.</param>
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
        OwlComprehension comprehension = OwlComprehension.None,
        OwlAxiomaticVocabulary axiomaticVocabulary = OwlAxiomaticVocabulary.Shared,
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
            correlationId,
            recordDeltas: true,
            comprehension: comprehension,
            axiomaticVocabulary: axiomaticVocabulary);

        context.BuildIndexes();

        //Round 0 fires the unchanged naive pass over base plus seed — the
        //delta of round 0 is everything, so no delta form is needed there.
        //Every later round restricts each rule to the triples the previous
        //merge accepted, which the semi-naive argument proves equivalent to
        //the naive full re-fire.
        bool first = true;
        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if(first)
            {
                context.FireRules();
                first = false;
            }
            else
            {
                context.FireRulesDelta();
            }

            if(context.InconsistencyRule is not null || !context.MergePending())
            {
                break;
            }
        }

        return new OwlRlResult(context.Derived, context.InconsistencyRule is null, context.InconsistencyRule, context.InconsistencyPremises, context.MalformedShapeSnapshot());
    }

    /// <summary>
    /// Computes the RL closure of <paramref name="triples"/> by the naive
    /// full re-fire evaluation — every rule family runs over the whole
    /// accumulated set each round. This is the differential oracle and
    /// measurement comparand for the semi-naive <see cref="Compute"/>: on
    /// every input the derived sets are equal and the consistency verdict
    /// agrees, so it stays callable for tests and benchmarks.
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
    /// <param name="comprehension">How the closure reads the comprehension conditions, mirroring <see cref="Compute"/> so the oracle certifies the same mode.</param>
    /// <param name="axiomaticVocabulary">Which axiomatic vocabulary table seeds the closure, mirroring <see cref="Compute"/> so the oracle certifies the same mode.</param>
    /// <param name="cancellationToken">A token that aborts derivation between rounds.</param>
    /// <returns>The derived triples and the consistency verdict, with the falsity's premises when inconsistent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> or <paramref name="terms"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    internal static OwlRlResult ComputeNaive(
        IEnumerable<EncodedTriple> triples,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle = default,
        TraceHandler<InferenceTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        OwlComprehension comprehension = OwlComprehension.None,
        OwlAxiomaticVocabulary axiomaticVocabulary = OwlAxiomaticVocabulary.Shared,
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
            correlationId,
            recordDeltas: false,
            comprehension: comprehension,
            axiomaticVocabulary: axiomaticVocabulary);

        context.BuildIndexes();

        while(true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            context.FireRules();

            if(context.InconsistencyRule is not null || !context.MergePending())
            {
                break;
            }
        }

        return new OwlRlResult(context.Derived, context.InconsistencyRule is null, context.InconsistencyRule, context.InconsistencyPremises, context.MalformedShapeSnapshot());
    }

    //The per-run state: the accumulating triple set, the maintained indexes,
    //and the rule implementations. Indexes build once from the base and are
    //maintained incrementally as each round's derivations merge — the closure
    //is monotone add-only, so appending exactly the newly accepted triples
    //keeps every index equal to a from-scratch build over the accumulated
    //set. The production-scale path runs rule bodies through the BGP engine
    //per the design record.
    internal sealed partial class ClosureContext
    {
        private OwlRlTerms Terms { get; }

        /// <summary>Whether merges record the per-round delta bookkeeping that semi-naive firing reads; <c>false</c> for the naive oracle.</summary>
        private bool RecordDeltas { get; }

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

        /// <summary>The ill-formed encodings declined so far, deduplicated — a broken chain read by several axioms and rounds records once.</summary>
        private HashSet<MalformedShape> MalformedShapes { get; } = [];

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

        /// <summary>(object, predicate) → subjects — the reverse of <see cref="BySubjectPredicate"/>, the reverse-join primitive delta firing reaches an edge's subjects with.</summary>
        private Dictionary<(TermId Object, TermId Predicate), List<TermId>> ByObjectPredicate { get; } = [];

        /// <summary>Subject → the distinct predicates it appears under as a subject — the by-term access path over subject == x.</summary>
        private Dictionary<TermId, List<TermId>> PredicatesOfSubject { get; } = [];

        /// <summary>Object → the distinct predicates it appears under as an object — the by-term access path over object == x.</summary>
        private Dictionary<TermId, List<TermId>> PredicatesOfObject { get; } = [];

        /// <summary>The datatype oracle for the <c>dt-*</c> falsities.</summary>
        private OwlRlDatatypeOracle DatatypeOracle { get; }

        public ClosureContext(
            IEnumerable<EncodedTriple> triples,
            OwlRlTerms terms,
            OwlRlDatatypeOracle datatypeOracle,
            TraceHandler<InferenceTraceEvent>? traceHandler,
            TimeProvider? timeProvider,
            Guid correlationId,
            bool recordDeltas,
            bool maintainBase = false,
            OwlComprehension comprehension = OwlComprehension.None,
            OwlAxiomaticVocabulary axiomaticVocabulary = OwlAxiomaticVocabulary.Shared)
        {
            Terms = terms;
            DatatypeOracle = datatypeOracle;
            TraceHandler = traceHandler;
            TimeProvider = timeProvider;
            CorrelationId = correlationId;
            RecordDeltas = recordDeltas;
            Comprehension = comprehension;
            foreach(EncodedTriple triple in triples)
            {
                All.Add(triple);

                //Base membership is recorded only for the maintained engine; the
                //from-scratch Compute / ComputeNaive paths leave the set empty.
                if(maintainBase)
                {
                    Base.Add(triple);
                }
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

            //The axiomatic vocabulary table of the OWL 2 RDF-Based
            //Semantics, restricted to the rows the conformance census
            //demands: the built-in annotation properties typed
            //owl:AnnotationProperty; owl:Thing and owl:Nothing typed
            //owl:Class; owl:imports typed with its domain and range; the
            //property-characteristic classes subsumed under
            //owl:ObjectProperty; and the list accessors functional. The
            //owl:Class/rdfs:Class metaclass merge seeds only under
            //OwlAxiomaticVocabulary.MetaclassMerged — it changes what the
            //shared calculus claims, so it rides the per-semantics mode,
            //never the shared table.
            foreach(TermId annotation in terms.BuiltInAnnotationProperties)
            {
                Seed(EncodedTriple.FromEncoded(annotation.Encoded, terms.Type.Encoded, terms.AnnotationProperty.Encoded));
            }

            Seed(EncodedTriple.FromEncoded(terms.Thing.Encoded, terms.Type.Encoded, terms.ClassTerm.Encoded));
            Seed(EncodedTriple.FromEncoded(terms.Nothing.Encoded, terms.Type.Encoded, terms.ClassTerm.Encoded));
            Seed(EncodedTriple.FromEncoded(terms.Imports.Encoded, terms.Type.Encoded, terms.RdfProperty.Encoded));
            Seed(EncodedTriple.FromEncoded(terms.Imports.Encoded, terms.Domain.Encoded, terms.Ontology.Encoded));
            Seed(EncodedTriple.FromEncoded(terms.Imports.Encoded, terms.Range.Encoded, terms.Ontology.Encoded));
            foreach(TermId characteristic in terms.PropertyCharacteristicClasses)
            {
                Seed(EncodedTriple.FromEncoded(characteristic.Encoded, terms.SubClassOf.Encoded, terms.ObjectPropertyTerm.Encoded));
            }

            Seed(EncodedTriple.FromEncoded(terms.First.Encoded, terms.Type.Encoded, terms.FunctionalProperty.Encoded));
            Seed(EncodedTriple.FromEncoded(terms.Rest.Encoded, terms.Type.Encoded, terms.FunctionalProperty.Encoded));

            //The metaclass merge: both subsumptions and both self-typings;
            //cax-sco then derives the cross-typings. All four rows hold in
            //every RDF-Based interpretation.
            if(axiomaticVocabulary == OwlAxiomaticVocabulary.MetaclassMerged)
            {
                Seed(EncodedTriple.FromEncoded(terms.ClassTerm.Encoded, terms.SubClassOf.Encoded, terms.RdfsClass.Encoded));
                Seed(EncodedTriple.FromEncoded(terms.RdfsClass.Encoded, terms.SubClassOf.Encoded, terms.ClassTerm.Encoded));
                Seed(EncodedTriple.FromEncoded(terms.ClassTerm.Encoded, terms.Type.Encoded, terms.ClassTerm.Encoded));
                Seed(EncodedTriple.FromEncoded(terms.RdfsClass.Encoded, terms.Type.Encoded, terms.RdfsClass.Encoded));
            }

            void Seed(EncodedTriple triple)
            {
                //Every seed triple is recorded as seeded unconditionally, so a
                //seed also present in the base sits in both sets; deletion never
                //propagates through a seed.
                if(maintainBase)
                {
                    Seeded.Add(triple);
                }

                if(All.Add(triple))
                {
                    Derived.Add(triple);
                }
            }
        }

        /// <summary>Builds the indexes from the accumulated set — once, before the first round; <see cref="MergePending"/> maintains them incrementally afterwards.</summary>
        public void BuildIndexes()
        {
            foreach(EncodedTriple triple in All)
            {
                IndexTriple(triple);
            }
        }

        /// <summary>Appends one accepted triple to every index it participates in. Called for each base triple at build time and for each newly merged derivation, so the indexes always equal a from-scratch build over <see cref="All"/> — the closure is monotone add-only and nothing is ever re-keyed or removed.</summary>
        /// <param name="triple">The accepted triple.</param>
        private void IndexTriple(EncodedTriple triple)
        {
            if(!ByPredicate.TryGetValue(triple.Predicate, out List<(TermId, TermId)>? pairs))
            {
                pairs = [];
                ByPredicate[triple.Predicate] = pairs;
            }

            if(RecordDeltas)
            {
                DeltaStartByPredicate.TryAdd(triple.Predicate, pairs.Count);
            }

            pairs.Add((triple.Subject, triple.Object));

            if(!BySubjectPredicate.TryGetValue((triple.Subject, triple.Predicate), out List<TermId>? objects))
            {
                objects = [];
                BySubjectPredicate[(triple.Subject, triple.Predicate)] = objects;

                //A (subject, predicate) list is created on the first triple
                //with that pair, so appending the predicate here keeps the
                //subject's predicate list distinct.
                if(!PredicatesOfSubject.TryGetValue(triple.Subject, out List<TermId>? subjectPredicates))
                {
                    subjectPredicates = [];
                    PredicatesOfSubject[triple.Subject] = subjectPredicates;
                }

                subjectPredicates.Add(triple.Predicate);
            }

            if(RecordDeltas)
            {
                DeltaStartBySubjectPredicate.TryAdd((triple.Subject, triple.Predicate), objects.Count);
            }

            objects.Add(triple.Object);

            if(!ByObjectPredicate.TryGetValue((triple.Object, triple.Predicate), out List<TermId>? subjects))
            {
                subjects = [];
                ByObjectPredicate[(triple.Object, triple.Predicate)] = subjects;

                //Same distinctness discipline on the object side.
                if(!PredicatesOfObject.TryGetValue(triple.Object, out List<TermId>? objectPredicates))
                {
                    objectPredicates = [];
                    PredicatesOfObject[triple.Object] = objectPredicates;
                }

                objectPredicates.Add(triple.Predicate);
            }

            if(RecordDeltas)
            {
                DeltaStartByObjectPredicate.TryAdd((triple.Object, triple.Predicate), subjects.Count);
            }

            subjects.Add(triple.Subject);

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

                if(RecordDeltas)
                {
                    DeltaStartInstancesOf.TryAdd(triple.Object, instances.Count);
                }

                instances.Add(triple.Subject);
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

            if(Comprehension == OwlComprehension.InformativeConditions)
            {
                FireComprehension();
            }
        }

        /// <summary>Merges the round's pending derivations into the accumulated set and the maintained indexes; <c>false</c> when the round was empty (fixpoint). A conclusion two rules derived in one round merges and indexes once — acceptance is keyed on the <see cref="All"/> add.</summary>
        public bool MergePending()
        {
            if(RecordDeltas)
            {
                //The bookkeeping records THIS merge's accepted triples and the
                //tail each index gained, so it is cleared here and rebuilt
                //below; it then survives untouched through the next
                //FireRulesDelta, which reads it.
                MergedThisRound.Clear();
                MergedThisRoundSet.Clear();
                DeltaStartByPredicate.Clear();
                DeltaStartBySubjectPredicate.Clear();
                DeltaStartByObjectPredicate.Clear();
                DeltaStartInstancesOf.Clear();
            }

            bool grew = false;
            foreach(EncodedTriple triple in Pending)
            {
                if(All.Add(triple))
                {
                    RecordAllEntered(triple);

                    if(Derived.Add(triple))
                    {
                        RecordDerivedEntered(triple);
                    }

                    IndexTriple(triple);

                    if(RecordDeltas)
                    {
                        MergedThisRound.Add(triple);
                        MergedThisRoundSet.Add(triple);
                    }

                    grew = true;
                }
            }

            Pending.Clear();

            return grew;
        }

        //eq-* (Table 4).

        private void FireEquality()
        {
            //eq-ref: every term of every statement equals itself. The
            //self-pairs reach the pair loop below on a later round, where
            //the eq-diff1 check turns a reflexive owl:differentFrom into a
            //contradiction and the x == y guard keeps them out of the
            //substitution scan.
            foreach(EncodedTriple triple in All)
            {
                Add(triple.Subject, Terms.SameAs, triple.Subject, EntailmentRules.EqRef, [triple]);
                Add(triple.Predicate, Terms.SameAs, triple.Predicate, EntailmentRules.EqRef, [triple]);
                Add(triple.Object, Terms.SameAs, triple.Object, EntailmentRules.EqRef, [triple]);
            }

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

                //dt falsity through the datatype oracle: sameAs between
                //datatypes of known-disjoint value spaces contradicts the
                //datatype map's distinct denotations.
                if(x != y && DatatypeOracle.DatatypesKnownDisjoint(x, y))
                {
                    Inconsistent(EntailmentRules.DtDisjointIdentity, [same]);

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

                if(CheckAllDifferentNode(node))
                {
                    return;
                }
            }
        }

        /// <summary>Checks one <c>owl:AllDifferent</c> node's members pairwise for a sameAs collision — eq-diff2 / eq-diff3. Every <c>owl:members</c> and <c>owl:distinctMembers</c> list on the node checks: each list triple is an independent distinctness assertion. Returns whether a falsity fired.</summary>
        /// <param name="node">The list node typed <c>owl:AllDifferent</c>.</param>
        /// <returns><c>true</c> when the node made the closure inconsistent.</returns>
        private bool CheckAllDifferentNode(TermId node)
        {
            return CheckAllDifferentMemberLists(node, Terms.Members) || CheckAllDifferentMemberLists(node, Terms.DistinctMembers);
        }

        /// <summary>Checks every member list the node asserts under one list predicate. Returns whether a falsity fired.</summary>
        /// <param name="node">The list node typed <c>owl:AllDifferent</c>.</param>
        /// <param name="predicate">The list predicate — <c>owl:members</c> or <c>owl:distinctMembers</c>.</param>
        /// <returns><c>true</c> when a list made the closure inconsistent.</returns>
        private bool CheckAllDifferentMemberLists(TermId node, TermId predicate)
        {
            foreach(TermId head in ObjectsOf(node, predicate))
            {
                if(ListOf(head) is not List<TermId> members)
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

                            return true;
                        }

                        if(ObjectsOf(members[i], Terms.SameAs).Contains(members[j]))
                        {
                            Inconsistent(EntailmentRules.EqDiff2, [allDifferent, Fact(members[i], Terms.SameAs, members[j])]);

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        //prp-* (Table 5).

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

            //dt-range-intersection (extension beyond the §4.3 tables): two
            //ranges confine the property's values to the intersection of
            //their value spaces, so every datatype-map space containing
            //that intersection is a range too. The oracle owns the
            //interval algebra; unknown pairs answer empty and derive
            //nothing.
            Dictionary<TermId, List<TermId>> rangesByProperty = [];
            foreach((TermId p, TermId d) in Pairs(Terms.Range))
            {
                if(!rangesByProperty.TryGetValue(p, out List<TermId>? datatypes))
                {
                    datatypes = [];
                    rangesByProperty[p] = datatypes;
                }

                datatypes.Add(d);
            }

            foreach(KeyValuePair<TermId, List<TermId>> propertyRanges in rangesByProperty)
            {
                List<TermId> datatypes = propertyRanges.Value;
                for(int i = 0; i < datatypes.Count; i++)
                {
                    for(int j = i + 1; j < datatypes.Count; j++)
                    {
                        if(datatypes[i] == datatypes[j])
                        {
                            continue;
                        }

                        foreach(TermId superset in DatatypeOracle.RangeIntersectionSupersets(datatypes[i], datatypes[j]))
                        {
                            if(superset != datatypes[i] && superset != datatypes[j])
                            {
                                Add(
                                    propertyRanges.Key,
                                    Terms.Range,
                                    superset,
                                    EntailmentRules.DtRangeIntersection,
                                    [Fact(propertyRanges.Key, Terms.Range, datatypes[i]), Fact(propertyRanges.Key, Terms.Range, datatypes[j])]);
                            }
                        }
                    }
                }
            }

            //The characteristic rules key off type statements.
            foreach((TermId p, TermId characteristic) in Pairs(Terms.Type))
            {
                if(FireCharacteristic(p, characteristic))
                {
                    return;
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

                FireChainAxiom(p, listHead, chain);
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
                if(type != Terms.AllDisjointProperties)
                {
                    continue;
                }

                if(FireAllDisjointPropertiesNode(node))
                {
                    return;
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
                FireKeyAxiom(c, listHead);
            }

            //prp-npa1 / prp-npa2: the negative-assertion falsities key off
            //the helper triples alone — the semantics constrains the helper
            //vocabulary unconditionally, so no typing antecedent gates the
            //check. Distinct subjects only: however many helper edges a
            //node repeats, its combinations are checked once per round.
            HashSet<TermId> negativeAssertionNodes = [];
            foreach((TermId node, TermId _) in Pairs(Terms.SourceIndividual))
            {
                if(negativeAssertionNodes.Add(node) && FireNegativePropertyAssertionNode(node))
                {
                    return;
                }
            }
        }

        /// <summary>Fires prp-npa1 / prp-npa2 for one node carrying negative-assertion helper triples — every (source, property, target) combination the helpers state clashes with a matching asserted edge. The typing antecedent is deliberately absent: the semantics constrains the helper vocabulary unconditionally, and the reported premises are exactly the matched triples. Returns whether a falsity fired.</summary>
        /// <param name="node">The node carrying an <c>owl:sourceIndividual</c> edge.</param>
        /// <returns><c>true</c> when a negated edge is asserted and made the closure inconsistent.</returns>
        private bool FireNegativePropertyAssertionNode(TermId node)
        {
            foreach(TermId s in ObjectsOf(node, Terms.SourceIndividual))
            {
                foreach(TermId p in ObjectsOf(node, Terms.AssertionProperty))
                {
                    List<TermId> asserted = ObjectsOf(s, p);
                    if(asserted.Count == 0)
                    {
                        continue;
                    }

                    foreach(TermId t in ObjectsOf(node, Terms.TargetIndividual))
                    {
                        if(asserted.Contains(t))
                        {
                            Inconsistent(EntailmentRules.PrpNpa, [Fact(node, Terms.SourceIndividual, s), Fact(node, Terms.AssertionProperty, p), Fact(node, Terms.TargetIndividual, t), Fact(s, p, t)]);

                            return true;
                        }
                    }

                    foreach(TermId t in ObjectsOf(node, Terms.TargetValue))
                    {
                        if(asserted.Contains(t))
                        {
                            Inconsistent(EntailmentRules.PrpNpa, [Fact(node, Terms.SourceIndividual, s), Fact(node, Terms.AssertionProperty, p), Fact(node, Terms.TargetValue, t), Fact(s, p, t)]);

                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Fires the property-characteristic rule keyed by <paramref name="characteristic"/> for property <paramref name="p"/> over the full indexes. Returns whether a falsity (irp / asyp) fired.</summary>
        /// <param name="p">The property carrying the characteristic typing.</param>
        /// <param name="characteristic">The characteristic class the property is typed with.</param>
        /// <returns><c>true</c> when the characteristic made the closure inconsistent.</returns>
        private bool FireCharacteristic(TermId p, TermId characteristic)
        {
            EncodedTriple typing = Fact(p, Terms.Type, characteristic);

            return characteristic switch
            {
                _ when characteristic == Terms.FunctionalProperty => FireFunctional(),
                _ when characteristic == Terms.InverseFunctionalProperty => FireInverseFunctional(),
                _ when characteristic == Terms.IrreflexiveProperty => FireIrreflexive(),
                _ when characteristic == Terms.SymmetricProperty => FireSymmetric(),
                _ when characteristic == Terms.AsymmetricProperty => FireAsymmetric(),
                _ when characteristic == Terms.TransitiveProperty => FireTransitive(),
                _ when characteristic == Terms.ReflexiveProperty => FireReflexive(),
                _ => false,
            };

            bool FireFunctional()
            {
                //prp-fp.
                foreach((TermId key, TermId first, TermId second) in SamePredicatePairs(p, bySubject: true))
                {
                    Add(first, Terms.SameAs, second, EntailmentRules.PrpFp, [typing, Fact(key, p, first), Fact(key, p, second)]);
                }

                return false;
            }

            bool FireInverseFunctional()
            {
                //prp-ifp.
                foreach((TermId key, TermId first, TermId second) in SamePredicatePairs(p, bySubject: false))
                {
                    Add(first, Terms.SameAs, second, EntailmentRules.PrpIfp, [typing, Fact(first, p, key), Fact(second, p, key)]);
                }

                return false;
            }

            bool FireIrreflexive()
            {
                //prp-irp.
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    if(x == y)
                    {
                        Inconsistent(EntailmentRules.PrpIrp, [typing, Fact(x, p, x)]);

                        return true;
                    }
                }

                return false;
            }

            bool FireSymmetric()
            {
                //prp-symp.
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    Add(y, p, x, EntailmentRules.PrpSymp, [typing, Fact(x, p, y)]);
                }

                return false;
            }

            bool FireAsymmetric()
            {
                //prp-asyp.
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    if(ObjectsOf(y, p).Contains(x))
                    {
                        Inconsistent(EntailmentRules.PrpAsyp, [typing, Fact(x, p, y), Fact(y, p, x)]);

                        return true;
                    }
                }

                return false;
            }

            bool FireTransitive()
            {
                //prp-trp.
                foreach((TermId x, TermId y) in Pairs(p))
                {
                    foreach(TermId z in ObjectsOf(y, p))
                    {
                        Add(x, p, z, EntailmentRules.PrpTrp, [typing, Fact(x, p, y), Fact(y, p, z)]);
                    }
                }

                //trans-chain: transitivity states exactly the chain
                //p ∘ p ⊑ p, so the structure materialises — on
                //deterministic list nodes, the same ones every round,
                //which keeps the fixpoint idempotent.
                TermId head = Terms.TransitivityChainNode(p, 0);
                TermId tail = Terms.TransitivityChainNode(p, 1);
                Add(p, Terms.PropertyChainAxiom, head, EntailmentRules.TransitivityChain, [typing]);
                Add(head, Terms.First, p, EntailmentRules.TransitivityChain, [typing]);
                Add(head, Terms.Rest, tail, EntailmentRules.TransitivityChain, [typing]);
                Add(tail, Terms.First, p, EntailmentRules.TransitivityChain, [typing]);
                Add(tail, Terms.Rest, Terms.Nil, EntailmentRules.TransitivityChain, [typing]);

                return false;
            }

            bool FireReflexive()
            {
                //Reflexivity instantiates over the named individuals.
                if(InstancesOf.TryGetValue(Terms.NamedIndividual, out List<TermId>? individuals))
                {
                    foreach(TermId x in individuals)
                    {
                        Add(x, p, x, EntailmentRules.ReflexiveInstantiation, [typing, Fact(x, Terms.Type, Terms.NamedIndividual)]);
                    }
                }

                return false;
            }
        }

        /// <summary>Fires the full prp-spo2 body for one chain axiom over the full indexes — the chain-trans materialisation when the chain is <c>p ∘ p ⊑ p</c>, then the frontier walk producing every start-to-end conclusion with its complete hop provenance.</summary>
        /// <param name="p">The super-property the chain implies.</param>
        /// <param name="listHead">The chain list's head node.</param>
        /// <param name="chain">The parsed chain properties in positional order.</param>
        private void FireChainAxiom(TermId p, TermId listHead, List<TermId> chain)
        {
            EncodedTriple chainAxiom = Fact(p, Terms.PropertyChainAxiom, listHead);

            //chain-trans: a chain p ∘ p ⊑ p is exactly transitivity,
            //so the typing materialises; the premises carry the full
            //list structure the conclusion reads from.
            if(chain.Count == 2 && chain[0] == p && chain[1] == p)
            {
                List<EncodedTriple> structure = [chainAxiom];
                TermId node = listHead;
                for(int i = 0; i < chain.Count; i++)
                {
                    structure.Add(Fact(node, Terms.First, chain[i]));
                    foreach(TermId rest in ObjectsOf(node, Terms.Rest))
                    {
                        structure.Add(Fact(node, Terms.Rest, rest));
                        node = rest;

                        break;
                    }
                }

                Add(p, Terms.Type, Terms.TransitiveProperty, EntailmentRules.ChainTransitivity, [.. structure]);
            }

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

        /// <summary>Fires prp-adp for one <c>owl:AllDisjointProperties</c> node — the pairwise disjointness materialisation and the shared-edge falsity scan — over the full indexes, once per asserted <c>owl:members</c> list. Returns whether a falsity fired.</summary>
        /// <param name="node">The list node typed <c>owl:AllDisjointProperties</c>.</param>
        /// <returns><c>true</c> when a disjoint pair shared an edge and made the closure inconsistent.</returns>
        private bool FireAllDisjointPropertiesNode(TermId node)
        {
            foreach(TermId head in ObjectsOf(node, Terms.Members))
            {
                if(ListOf(head) is not List<TermId> members)
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

                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Fires prp-key for one <c>owl:hasKey</c> axiom — every pair of the keyed class's instances sharing a value for every key property is equated — over the full instance list.</summary>
        /// <param name="c">The keyed class.</param>
        /// <param name="listHead">The key-property list's head node.</param>
        private void FireKeyAxiom(TermId c, TermId listHead)
        {
            if(ListOf(listHead) is not List<TermId> keys || keys.Count == 0 || !InstancesOf.TryGetValue(c, out List<TermId>? instances))
            {
                return;
            }

            EncodedTriple hasKey = Fact(c, Terms.HasKey, listHead);
            for(int i = 0; i < instances.Count; i++)
            {
                for(int j = i + 1; j < instances.Count; j++)
                {
                    FireKeyPair(hasKey, c, keys, instances[i], instances[j]);
                }
            }
        }

        /// <summary>Equates one pair of a keyed class's instances when they share a value for every key property — the prp-key body for a single instance pair, with the shared-value witnesses assembled in key order.</summary>
        /// <param name="hasKey">The <c>owl:hasKey</c> axiom triple.</param>
        /// <param name="c">The keyed class.</param>
        /// <param name="keys">The key properties in list order.</param>
        /// <param name="first">The first instance of the pair.</param>
        /// <param name="second">The second instance of the pair.</param>
        private void FireKeyPair(EncodedTriple hasKey, TermId c, List<TermId> keys, TermId first, TermId second)
        {
            //The premises accumulate one shared-value witness
            //pair per key property.
            List<EncodedTriple> premises = [hasKey, Fact(first, Terms.Type, c), Fact(second, Terms.Type, c)];
            bool sharesAll = true;
            foreach(TermId key in keys)
            {
                if(!TryGetSharedValue(first, second, key, out TermId value))
                {
                    sharesAll = false;

                    break;
                }

                premises.Add(Fact(first, key, value));
                premises.Add(Fact(second, key, value));
            }

            if(sharesAll)
            {
                Add(first, Terms.SameAs, second, EntailmentRules.PrpKey, [.. premises]);
            }
        }

        //cls-* (Table 6).

        private void FireClasses()
        {
            //cls-nothing2.
            if(InstancesOf.TryGetValue(Terms.Nothing, out List<TermId>? nothings) && nothings.Count > 0)
            {
                Inconsistent(EntailmentRules.ClsNothing2, [Fact(nothings[0], Terms.Type, Terms.Nothing)]);

                return;
            }

            //The rdf:nil structure falsity.
            if(CheckNilStructure())
            {
                return;
            }

            //cls-int1 / cls-int2.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                if(ListOf(listHead) is not List<TermId> members || members.Count == 0)
                {
                    continue;
                }

                FireIntersectionAxiom(c, listHead, members);
            }

            //cls-uni.
            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                if(ListOf(listHead) is not List<TermId> members)
                {
                    continue;
                }

                FireUnionAxiom(c, listHead, members);
            }

            //cls-com; complement is symmetric between class extensions.
            foreach((TermId c1, TermId c2) in Pairs(Terms.ComplementOf))
            {
                EncodedTriple complement = Fact(c1, Terms.ComplementOf, c2);
                Add(c2, Terms.ComplementOf, c1, EntailmentRules.ComplementOfSymmetry, [complement]);
                if(InstancesOf.TryGetValue(c1, out List<TermId>? instances))
                {
                    foreach(TermId x in instances)
                    {
                        if(HasType(x, c2))
                        {
                            Inconsistent(EntailmentRules.ClsCom, [complement, Fact(x, Terms.Type, c1), Fact(x, Terms.Type, c2)]);

                            return;
                        }
                    }
                }
            }

            //The restriction rules key off owl:onProperty.
            foreach((TermId x, TermId p) in Pairs(Terms.OnProperty))
            {
                if(FireRestrictionBody(x, p))
                {
                    return;
                }
            }

            //cls-oo; an enumeration of owl:Thing is the Thing-enumeration
            //falsity instead — the finite sequence cannot exhaust the
            //infinite RDF-Based universe, so the axiom triple alone
            //contradicts at any arity and the list is never read.
            foreach((TermId c, TermId listHead) in Pairs(Terms.OneOf))
            {
                if(c == Terms.Thing)
                {
                    Inconsistent(EntailmentRules.ThingEnumerationClash, [Fact(c, Terms.OneOf, listHead)]);

                    return;
                }

                FireOneOfAxiom(c, listHead);
            }
        }

        /// <summary>Fires cls-int1 / cls-int2 for one <c>owl:intersectionOf</c> axiom over the full instance lists — every instance of every member class becomes an instance of the intersection, and every instance of the intersection an instance of each member.</summary>
        /// <param name="c">The intersection class.</param>
        /// <param name="listHead">The member list's head node.</param>
        /// <param name="members">The parsed member classes in list order.</param>
        private void FireIntersectionAxiom(TermId c, TermId listHead, List<TermId> members)
        {
            EncodedTriple intersection = Fact(c, Terms.IntersectionOf, listHead);

            if(InstancesOf.TryGetValue(members[0], out List<TermId>? candidates))
            {
                foreach(TermId x in candidates)
                {
                    FireIntersectionCandidate(c, intersection, members, x);
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

        /// <summary>Fires cls-int1 for one candidate instance of an intersection's first member — when it has every member type it becomes an instance of the intersection, with a per-member typing witness in list order.</summary>
        /// <param name="c">The intersection class.</param>
        /// <param name="intersection">The <c>owl:intersectionOf</c> axiom triple.</param>
        /// <param name="members">The member classes in list order.</param>
        /// <param name="x">The candidate instance.</param>
        private void FireIntersectionCandidate(TermId c, EncodedTriple intersection, List<TermId> members, TermId x)
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

        /// <summary>Fires cls-uni for one <c>owl:unionOf</c> axiom over the full instance lists — every instance of any member class is an instance of the union.</summary>
        /// <param name="c">The union class.</param>
        /// <param name="listHead">The member list's head node.</param>
        /// <param name="members">The parsed member classes in list order.</param>
        private void FireUnionAxiom(TermId c, TermId listHead, List<TermId> members)
        {
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

        /// <summary>
        /// Fires the rdf:nil structure falsity when the empty collection
        /// carries an <c>rdf:first</c> or <c>rdf:rest</c> edge — an
        /// unconditional condition of the RDF-Based semantics, independent
        /// of any list machinery around the node, so the edge triple alone
        /// is the premise. A fixed-subject check, never a general
        /// list-well-formedness pass. Returns whether a falsity fired.
        /// </summary>
        /// <returns><c>true</c> when an edge on <c>rdf:nil</c> made the closure inconsistent.</returns>
        private bool CheckNilStructure()
        {
            List<TermId> firsts = ObjectsOf(Terms.Nil, Terms.First);
            if(firsts.Count > 0)
            {
                Inconsistent(EntailmentRules.NilStructureClash, [Fact(Terms.Nil, Terms.First, firsts[0])]);

                return true;
            }

            List<TermId> rests = ObjectsOf(Terms.Nil, Terms.Rest);
            if(rests.Count > 0)
            {
                Inconsistent(EntailmentRules.NilStructureClash, [Fact(Terms.Nil, Terms.Rest, rests[0])]);

                return true;
            }

            return false;
        }

        /// <summary>Fires cls-oo for one <c>owl:oneOf</c> axiom — every enumerated member is an instance of the class.</summary>
        /// <param name="c">The enumerated class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireOneOfAxiom(TermId c, TermId listHead)
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

        /// <summary>Fires every restriction rule (svf1/svf2, avf and its dt falsity, hv1/hv2, maxc1/maxc2, maxqc1/maxqc4, and the min-cardinality-1 membership) for one restriction node <paramref name="x"/> on property <paramref name="p"/> over the full indexes. Each field fires once per asserted value — every field triple is an independent fact the rules pattern-match, so a node carrying several fillers or bounds fires every instance. Returns whether a falsity fired.</summary>
        /// <param name="x">The restriction node.</param>
        /// <param name="p">The property the restriction is on.</param>
        /// <returns><c>true</c> when a restriction falsity made the closure inconsistent.</returns>
        private bool FireRestrictionBody(TermId x, TermId p)
        {
            EncodedTriple onProperty = Fact(x, Terms.OnProperty, p);

            //cls-svf1 / cls-svf2.
            foreach(TermId someFiller in ObjectsOf(x, Terms.SomeValuesFrom))
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
            if(InstancesOf.TryGetValue(x, out List<TermId>? avfInstances))
            {
                foreach(TermId allFiller in ObjectsOf(x, Terms.AllValuesFrom))
                {
                    EncodedTriple allValues = Fact(x, Terms.AllValuesFrom, allFiller);
                    foreach(TermId u in avfInstances)
                    {
                        foreach(TermId v in ObjectsOf(u, p))
                        {
                            if(DatatypeOracle.LiteralOutsideDatatype(v, allFiller))
                            {
                                Inconsistent(EntailmentRules.DtNotType, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);

                                return true;
                            }

                            Add(v, Terms.Type, allFiller, EntailmentRules.ClsAvf, [onProperty, allValues, Fact(u, Terms.Type, x), Fact(u, p, v)]);
                        }
                    }
                }
            }

            //cls-hv1 / cls-hv2.
            foreach(TermId value in ObjectsOf(x, Terms.HasValue))
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
            foreach(TermId bound in ObjectsOf(x, Terms.MaxCardinality))
            {
                EncodedTriple maxCardinality = Fact(x, Terms.MaxCardinality, bound);
                if(Terms.ZeroBounds.Contains(bound) && TryFindInstanceEdge(x, p, out TermId edgeSubject, out TermId edgeObject))
                {
                    Inconsistent(EntailmentRules.ClsMaxc1, [onProperty, maxCardinality, Fact(edgeSubject, Terms.Type, x), Fact(edgeSubject, p, edgeObject)]);

                    return true;
                }

                if(Terms.OneBounds.Contains(bound) && InstancesOf.TryGetValue(x, out List<TermId>? maxcInstances))
                {
                    foreach(TermId u in maxcInstances)
                    {
                        EquateAllPairs(ObjectsOf(u, p), EntailmentRules.ClsMaxc2, u, p, [onProperty, maxCardinality, Fact(u, Terms.Type, x)]);
                    }
                }
            }

            //cls-maxqc1–4; the bound and the onClass filler positions pair
            //by the cartesian product, exactly as the rules pattern-match.
            //Every maxqc rule requires the owl:onClass triple — an absent
            //onClass matches no rule, and the unqualified reading belongs
            //to owl:maxCardinality above, never to an invented owl:Thing.
            foreach(TermId qualifiedBound in ObjectsOf(x, Terms.MaxQualifiedCardinality))
            {
                EncodedTriple maxQualified = Fact(x, Terms.MaxQualifiedCardinality, qualifiedBound);
                foreach(TermId filler in ObjectsOf(x, Terms.OnClass))
                {

                    if(Terms.ZeroBounds.Contains(qualifiedBound) && InstancesOf.TryGetValue(x, out List<TermId>? maxqcInstances))
                    {
                        foreach(TermId u in maxqcInstances)
                        {
                            foreach(TermId y in ObjectsOf(u, p))
                            {
                                if(filler == Terms.Thing)
                                {
                                    Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, y)]);

                                    return true;
                                }

                                if(HasType(y, filler))
                                {
                                    Inconsistent(EntailmentRules.ClsMaxqc1, [onProperty, maxQualified, Fact(u, Terms.Type, x), Fact(u, p, y), Fact(y, Terms.Type, filler)]);

                                    return true;
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

            //The min-cardinality-1 membership completion: the restriction
            //conditions determine the extension exactly, so one asserted
            //value places the subject in a min-1 restriction on the
            //property. Bounds above one never conclude membership — two
            //asserted values need not be distinct individuals — and the
            //zero bound stays out as universally true. A node carrying
            //several bounds fires per one-bound: each bound triple is an
            //independent constraint of the graph.
            foreach(TermId minBound in ObjectsOf(x, Terms.MinCardinality))
            {
                if(!Terms.OneBounds.Contains(minBound))
                {
                    continue;
                }

                EncodedTriple minCardinality = Fact(x, Terms.MinCardinality, minBound);
                foreach((TermId u, TermId v) in Pairs(p))
                {
                    Add(u, Terms.Type, x, EntailmentRules.MinCardinalityOneMembership, [onProperty, minCardinality, Fact(u, p, v)]);
                }
            }

            return false;
        }

        //cax-* (Table 7).

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
                if(type != Terms.AllDisjointClasses)
                {
                    continue;
                }

                if(FireAllDisjointClassesNode(node))
                {
                    return;
                }
            }
        }

        /// <summary>Fires cax-adc for one <c>owl:AllDisjointClasses</c> node — the pairwise disjointness materialisation and the shared-instance falsity scan — over the full indexes, once per asserted <c>owl:members</c> list. Returns whether a falsity fired.</summary>
        /// <param name="node">The list node typed <c>owl:AllDisjointClasses</c>.</param>
        /// <returns><c>true</c> when a disjoint pair shared an instance and made the closure inconsistent.</returns>
        private bool FireAllDisjointClassesNode(TermId node)
        {
            foreach(TermId head in ObjectsOf(node, Terms.Members))
            {
                if(ListOf(head) is not List<TermId> members)
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

                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        //scm-* (Table 9).

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

            //scm-op / scm-dp: every declared object or datatype property is
            //its own sub- and equivalent property.
            if(InstancesOf.TryGetValue(Terms.ObjectPropertyTerm, out List<TermId>? objectProperties))
            {
                FireSelfSubsumption(objectProperties, Terms.ObjectPropertyTerm, EntailmentRules.ScmOp);
            }

            if(InstancesOf.TryGetValue(Terms.DatatypePropertyTerm, out List<TermId>? datatypeProperties))
            {
                FireSelfSubsumption(datatypeProperties, Terms.DatatypePropertyTerm, EntailmentRules.ScmDp);
            }

            //scm-sco / scm-eqc2.
            foreach((TermId c1, TermId c2) in Pairs(Terms.SubClassOf))
            {
                EncodedTriple subClass = Fact(c1, Terms.SubClassOf, c2);
                ComposeThroughBridge(this, c1, c2, subClass, Terms.SubClassOf, Terms.SubClassOf, EntailmentRules.ScmSco);

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
                ComposeThroughBridge(this, p1, p2, subProperty, Terms.SubPropertyOf, Terms.SubPropertyOf, EntailmentRules.ScmSpo);

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
                ComposeThroughBridge(this, p, c1, domain, Terms.SubClassOf, Terms.Domain, EntailmentRules.ScmDom1);
            }

            foreach((TermId p1, TermId p2) in Pairs(Terms.SubPropertyOf))
            {
                EncodedTriple subProperty = Fact(p1, Terms.SubPropertyOf, p2);
                ComposeThroughBridge(this, p1, p2, subProperty, Terms.Domain, Terms.Domain, EntailmentRules.ScmDom2);
                ComposeThroughBridge(this, p1, p2, subProperty, Terms.Range, Terms.Range, EntailmentRules.ScmRng2);
            }

            //scm-rng1.
            foreach((TermId p, TermId c1) in Pairs(Terms.Range))
            {
                EncodedTriple range = Fact(p, Terms.Range, c1);
                ComposeThroughBridge(this, p, c1, range, Terms.SubClassOf, Terms.Range, EntailmentRules.ScmRng1);
            }

            //scm-int / scm-uni.
            foreach((TermId c, TermId listHead) in Pairs(Terms.IntersectionOf))
            {
                FireSchemaIntersectionAxiom(c, listHead);
            }

            foreach((TermId c, TermId listHead) in Pairs(Terms.UnionOf))
            {
                FireSchemaUnionAxiom(c, listHead);
            }

            //scm-svf1/svf2, scm-avf1/avf2, scm-hv.
            FireRestrictionComparisons();

            //The RDF-Based-semantics completions of the conformance residue:
            //the inverse-characteristic transfer, the singleton-enumeration
            //characteristics, and the member-subset comparisons of the
            //order-insensitive constructors.
            foreach((TermId p1, TermId p2) in Pairs(Terms.InverseOf))
            {
                FireInverseCharacteristicPair(p1, p2);
            }

            FireSingletonEnumerationCharacteristics();
            FireEnumerationComparisons();

            //The one transitive-join shape the scm-* rules share: the pair's
            //object composes through the bridge predicate, and each reached
            //target concludes under the output predicate with the pair's own
            //triple and the bridge triple as premises.
            static void ComposeThroughBridge(ClosureContext context, TermId subject, TermId via, EncodedTriple premise, TermId bridge, TermId output, string rule)
            {
                foreach(TermId target in context.ObjectsOf(via, bridge))
                {
                    context.Add(subject, output, target, rule, [premise, Fact(via, bridge, target)]);
                }
            }
        }

        /// <summary>Fires scm-op or scm-dp for the given declared properties — each is its own sub- and equivalent property, with its declaration triple as the premise.</summary>
        /// <param name="properties">The properties typed with <paramref name="propertyClass"/>.</param>
        /// <param name="propertyClass">The declaring class — <c>owl:ObjectProperty</c> or <c>owl:DatatypeProperty</c>.</param>
        /// <param name="rule">The rule name the derivations carry.</param>
        private void FireSelfSubsumption(List<TermId> properties, TermId propertyClass, string rule)
        {
            foreach(TermId p in properties)
            {
                EncodedTriple declaration = Fact(p, Terms.Type, propertyClass);
                Add(p, Terms.SubPropertyOf, p, rule, [declaration]);
                Add(p, Terms.EquivalentProperty, p, rule, [declaration]);
            }
        }

        /// <summary>
        /// Fires the Table 9 restriction-comparison rules over the full
        /// indexes: restrictions grouped by <c>owl:onProperty</c> compare
        /// pairwise — on one property along their fillers' subsumption
        /// (scm-svf1 / scm-avf1), and on one shared filler or value along
        /// their properties' subsumption (scm-svf2 / scm-hv, with scm-avf2's
        /// conclusion contravariant: the superproperty's restriction
        /// subsumes under the subproperty's).
        /// </summary>
        private void FireRestrictionComparisons()
        {
            List<(TermId Subject, TermId Object)> onProperty = Pairs(Terms.OnProperty);
            if(onProperty.Count == 0)
            {
                return;
            }

            Dictionary<TermId, List<TermId>> restrictionsByProperty = [];
            foreach((TermId x, TermId p) in onProperty)
            {
                if(!restrictionsByProperty.TryGetValue(p, out List<TermId>? group))
                {
                    group = [];
                    restrictionsByProperty[p] = group;
                }

                group.Add(x);
            }

            //scm-svf1 / scm-avf1: one property, fillers along rdfs:subClassOf.
            foreach(KeyValuePair<TermId, List<TermId>> group in restrictionsByProperty)
            {
                CompareFillersOnOneProperty(this, group.Key, group.Value, Terms.SomeValuesFrom, EntailmentRules.ScmSvf1);
                CompareFillersOnOneProperty(this, group.Key, group.Value, Terms.AllValuesFrom, EntailmentRules.ScmAvf1);
            }

            //scm-svf2 / scm-avf2 / scm-hv: one shared filler or value,
            //properties along rdfs:subPropertyOf.
            foreach((TermId c1, TermId p1) in onProperty)
            {
                foreach(TermId p2 in ObjectsOf(p1, Terms.SubPropertyOf))
                {
                    if(!restrictionsByProperty.TryGetValue(p2, out List<TermId>? candidates))
                    {
                        continue;
                    }

                    CompareSharedFillerAcrossProperties(this, c1, p1, p2, candidates, Terms.SomeValuesFrom, EntailmentRules.ScmSvf2, reverseConclusion: false);
                    CompareSharedFillerAcrossProperties(this, c1, p1, p2, candidates, Terms.AllValuesFrom, EntailmentRules.ScmAvf2, reverseConclusion: true);
                    CompareSharedFillerAcrossProperties(this, c1, p1, p2, candidates, Terms.HasValue, EntailmentRules.ScmHv, reverseConclusion: false);
                }
            }

            //The pairwise comparison on one property: each ordered pair of
            //its restrictions whose fillers stand in rdfs:subClassOf
            //concludes the restrictions' subsumption in the fillers'
            //direction. A restriction carrying several fillers compares
            //per asserted filler pair — each filler triple is an
            //independent fact the comparison pattern-matches.
            static void CompareFillersOnOneProperty(ClosureContext context, TermId property, List<TermId> restrictions, TermId fillerPredicate, string rule)
            {
                for(int i = 0; i < restrictions.Count; i++)
                {
                    foreach(TermId firstFiller in context.ObjectsOf(restrictions[i], fillerPredicate))
                    {
                        for(int j = 0; j < restrictions.Count; j++)
                        {
                            foreach(TermId secondFiller in context.ObjectsOf(restrictions[j], fillerPredicate))
                            {
                                if(context.ObjectsOf(firstFiller, context.Terms.SubClassOf).Contains(secondFiller))
                                {
                                    context.Add(
                                        restrictions[i],
                                        context.Terms.SubClassOf,
                                        restrictions[j],
                                        rule,
                                        [
                                            Fact(restrictions[i], fillerPredicate, firstFiller),
                                            Fact(restrictions[i], context.Terms.OnProperty, property),
                                            Fact(restrictions[j], fillerPredicate, secondFiller),
                                            Fact(restrictions[j], context.Terms.OnProperty, property),
                                            Fact(firstFiller, context.Terms.SubClassOf, secondFiller),
                                        ]);
                                }
                            }
                        }
                    }
                }
            }

            //The comparison across a property subsumption: a candidate on
            //the superproperty sharing the exact filler or value concludes
            //subsumption — toward the candidate normally, toward the subject
            //when the conclusion reverses (the contravariant scm-avf2). A
            //restriction carrying several fillers compares per asserted
            //filler.
            static void CompareSharedFillerAcrossProperties(ClosureContext context, TermId subject, TermId subProperty, TermId superProperty, List<TermId> candidates, TermId fillerPredicate, string rule, bool reverseConclusion)
            {
                foreach(TermId filler in context.ObjectsOf(subject, fillerPredicate))
                {
                    foreach(TermId candidate in candidates)
                    {
                        if(!context.ObjectsOf(candidate, fillerPredicate).Contains(filler))
                        {
                            continue;
                        }

                        TermId subClass = reverseConclusion ? candidate : subject;
                        TermId superClass = reverseConclusion ? subject : candidate;
                        context.Add(
                            subClass,
                            context.Terms.SubClassOf,
                            superClass,
                            rule,
                            [
                                Fact(subject, fillerPredicate, filler),
                                Fact(subject, context.Terms.OnProperty, subProperty),
                                Fact(candidate, fillerPredicate, filler),
                                Fact(candidate, context.Terms.OnProperty, superProperty),
                                Fact(subProperty, context.Terms.SubPropertyOf, superProperty),
                            ]);
                    }
                }
            }
        }

        /// <summary>Fires scm-int for one <c>owl:intersectionOf</c> axiom — the intersection is a subclass of every member.</summary>
        /// <param name="c">The intersection class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireSchemaIntersectionAxiom(TermId c, TermId listHead)
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

        /// <summary>Fires scm-uni for one <c>owl:unionOf</c> axiom — every member is a subclass of the union.</summary>
        /// <param name="c">The union class.</param>
        /// <param name="listHead">The member list's head node.</param>
        private void FireSchemaUnionAxiom(TermId c, TermId listHead)
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

        /// <summary>Fires the inverse-characteristic transfer for one <c>owl:inverseOf</c> pair: a functional end makes the other end inverse functional, and an inverse-functional end makes the other end functional — the kinds exchange across the inverse in both directions.</summary>
        /// <param name="p1">The inverse statement's subject property.</param>
        /// <param name="p2">The inverse statement's object property.</param>
        private void FireInverseCharacteristicPair(TermId p1, TermId p2)
        {
            EncodedTriple inverse = Fact(p1, Terms.InverseOf, p2);
            TransferAcrossInverse(this, inverse, p1, p2);
            TransferAcrossInverse(this, inverse, p2, p1);

            //One end's characteristic typing concludes the exchanged
            //characteristic on the other end.
            static void TransferAcrossInverse(ClosureContext context, EncodedTriple inverse, TermId source, TermId target)
            {
                if(context.HasType(source, context.Terms.FunctionalProperty))
                {
                    context.Add(
                        target,
                        context.Terms.Type,
                        context.Terms.InverseFunctionalProperty,
                        EntailmentRules.InverseCharacteristicTransfer,
                        [inverse, Fact(source, context.Terms.Type, context.Terms.FunctionalProperty)]);
                }

                if(context.HasType(source, context.Terms.InverseFunctionalProperty))
                {
                    context.Add(
                        target,
                        context.Terms.Type,
                        context.Terms.FunctionalProperty,
                        EntailmentRules.InverseCharacteristicTransfer,
                        [inverse, Fact(source, context.Terms.Type, context.Terms.InverseFunctionalProperty)]);
                }
            }
        }

        /// <summary>Fires the inverse-characteristic transfer for one functional or inverse-functional typing: the property's <c>owl:inverseOf</c> partners on either orientation gain the exchanged characteristic. Any other typing derives nothing.</summary>
        /// <param name="p">The property carrying the typing.</param>
        /// <param name="characteristic">The class the property is typed with.</param>
        private void FireInverseCharacteristicTyping(TermId p, TermId characteristic)
        {
            if(characteristic != Terms.FunctionalProperty && characteristic != Terms.InverseFunctionalProperty)
            {
                return;
            }

            TermId concluded = characteristic == Terms.FunctionalProperty ? Terms.InverseFunctionalProperty : Terms.FunctionalProperty;
            EncodedTriple typing = Fact(p, Terms.Type, characteristic);
            foreach(TermId q in ObjectsOf(p, Terms.InverseOf))
            {
                Add(q, Terms.Type, concluded, EntailmentRules.InverseCharacteristicTransfer, [Fact(p, Terms.InverseOf, q), typing]);
            }

            foreach(TermId q in SubjectsOf(p, Terms.InverseOf))
            {
                Add(q, Terms.Type, concluded, EntailmentRules.InverseCharacteristicTransfer, [Fact(q, Terms.InverseOf, p), typing]);
            }
        }

        /// <summary>Fires the singleton-enumeration characteristics over the full indexes: a property whose range is a singleton enumeration is functional, and one whose domain is a singleton enumeration is inverse functional — the enumerated extension holds one individual, so all values (or subjects) coincide on it.</summary>
        private void FireSingletonEnumerationCharacteristics()
        {
            foreach((TermId p, TermId c) in Pairs(Terms.Range))
            {
                FireSingletonEnumerationEdge(p, c, Terms.Range, Terms.FunctionalProperty);
            }

            foreach((TermId p, TermId c) in Pairs(Terms.Domain))
            {
                FireSingletonEnumerationEdge(p, c, Terms.Domain, Terms.InverseFunctionalProperty);
            }
        }

        /// <summary>Fires the singleton-enumeration characteristic for one range or domain edge: every singleton <c>owl:oneOf</c> list of the confining class concludes the characteristic, with the list's two cell triples completing the provenance. The member's identity is never read — only the list's arity matters.</summary>
        /// <param name="p">The confined property.</param>
        /// <param name="c">The range or domain class.</param>
        /// <param name="confiningPredicate">The confining predicate — <c>rdfs:range</c> or <c>rdfs:domain</c>.</param>
        /// <param name="characteristic">The characteristic class the confinement concludes.</param>
        private void FireSingletonEnumerationEdge(TermId p, TermId c, TermId confiningPredicate, TermId characteristic)
        {
            foreach(TermId head in ObjectsOf(c, Terms.OneOf))
            {
                if(ListOf(head) is not List<TermId> members || members.Count != 1)
                {
                    continue;
                }

                Add(
                    p,
                    Terms.Type,
                    characteristic,
                    EntailmentRules.SingletonEnumerationCharacteristic,
                    [
                        Fact(p, confiningPredicate, c),
                        Fact(c, Terms.OneOf, head),
                        Fact(head, Terms.First, members[0]),
                        Fact(head, Terms.Rest, Terms.Nil),
                    ]);
            }
        }

        /// <summary>Fires the member-subset comparisons of the order-insensitive class constructors over the full indexes: two <c>owl:oneOf</c> enumerations, or two <c>owl:unionOf</c> unions, whose member sets stand in subset order stand in subclass order. List order and repetition carry no meaning for either constructor, so the comparison reads the lists as sets; equal sets subsume both ways, which scm-eqc2 closes into the equivalence. The order-sensitive list constructors never compare.</summary>
        private void FireEnumerationComparisons()
        {
            CompareMemberSets(this, Terms.OneOf, EntailmentRules.OneOfMemberSubset);
            CompareMemberSets(this, Terms.UnionOf, EntailmentRules.UnionOfMemberSubset);

            //Each ordered pair of distinct classes under one constructor
            //concludes subsumption when the first's member set is contained
            //in the second's; an empty member set is contained in every
            //other, which is sound — an empty enumeration or union denotes
            //the empty class.
            static void CompareMemberSets(ClosureContext context, TermId constructor, string rule)
            {
                List<(TermId Subject, TermId Object)> axioms = context.Pairs(constructor);
                if(axioms.Count < 2)
                {
                    return;
                }

                List<(TermId Class, TermId ListHead, HashSet<TermId> Members)> parsed = [];
                foreach((TermId c, TermId listHead) in axioms)
                {
                    if(context.ListOf(listHead) is List<TermId> members)
                    {
                        parsed.Add((c, listHead, [.. members]));
                    }
                }

                for(int i = 0; i < parsed.Count; i++)
                {
                    for(int j = 0; j < parsed.Count; j++)
                    {
                        if(i == j || parsed[i].Class == parsed[j].Class)
                        {
                            continue;
                        }

                        if(parsed[i].Members.IsSubsetOf(parsed[j].Members))
                        {
                            context.Add(
                                parsed[i].Class,
                                context.Terms.SubClassOf,
                                parsed[j].Class,
                                rule,
                                [Fact(parsed[i].Class, constructor, parsed[i].ListHead), Fact(parsed[j].Class, constructor, parsed[j].ListHead)]);
                        }
                    }
                }
            }
        }

        //Shared helpers.

        private List<(TermId Subject, TermId Object)> Pairs(TermId predicate)
        {
            return ByPredicate.TryGetValue(predicate, out List<(TermId, TermId)>? pairs) ? pairs : [];
        }

        private List<TermId> ObjectsOf(TermId subject, TermId predicate)
        {
            return BySubjectPredicate.TryGetValue((subject, predicate), out List<TermId>? objects) ? objects : [];
        }

        //The canonical read of a list cell: the minimum object by term
        //identifier — a function of the accumulated set, never of insertion
        //order, so closures over equal content read equal values. Sound for
        //list cells alone: the shared axiomatic table types rdf:first and
        //rdf:rest functional, so a cell's values are entailed equal and any
        //canonical pick reads the same individual. Every other single-valued
        //position fires per asserted value instead of picking.
        private TermId? MinimumObjectOf(TermId subject, TermId predicate)
        {
            List<TermId> objects = ObjectsOf(subject, predicate);
            if(objects.Count == 0)
            {
                return null;
            }

            TermId canonical = objects[0];
            for(int i = 1; i < objects.Count; i++)
            {
                if(objects[i].CompareTo(canonical) < 0)
                {
                    canonical = objects[i];
                }
            }

            return canonical;
        }

        //Records one declined reading, deduplicated across axioms and
        //rounds; the record rides the result so a silent skip never hides
        //that the closure read less than the graph asserts.
        private void RecordMalformedShape(TermId subject, TermId predicate, MalformedShapeKind kind)
        {
            MalformedShapes.Add(new MalformedShape(subject, predicate, kind));
        }

        /// <summary>The declined readings accumulated so far, in a stable snapshot for the result.</summary>
        /// <returns>The recorded shapes.</returns>
        public ImmutableArray<MalformedShape> MalformedShapeSnapshot()
        {
            return [.. MalformedShapes];
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

        /// <summary>Walks an RDF collection into members; <c>null</c> on broken or cyclic chains, with the refusal recorded on the result. A cell carrying several values reads its canonical minimum — the axiomatic list functionality entails a cell's values equal, so the pick is sound and deterministic over equal content.</summary>
        private List<TermId>? ListOf(TermId head)
        {
            List<TermId> members = [];
            HashSet<TermId> visited = [];
            TermId current = head;

            while(current != Terms.Nil)
            {
                if(!visited.Add(current))
                {
                    RecordMalformedShape(current, TermId.None, MalformedShapeKind.CyclicListChain);

                    return null;
                }

                TermId? first = MinimumObjectOf(current, Terms.First);
                TermId? rest = MinimumObjectOf(current, Terms.Rest);
                if(first is not TermId firstValue || rest is not TermId restValue)
                {
                    RecordMalformedShape(current, first is null ? Terms.First : Terms.Rest, MalformedShapeKind.BrokenListChain);

                    return null;
                }

                members.Add(firstValue);
                current = restValue;
            }

            return members;
        }

        /// <summary>
        /// Yields the functional/inverse-functional pairing triples: for every two triples of the
        /// property sharing a subject (when <paramref name="bySubject"/>) or object, the shared key
        /// and the two distinct other ends, so the caller can equate them and name its premises.
        /// </summary>
        /// <param name="property">The property whose triples are paired.</param>
        /// <param name="bySubject">Whether to group by subject (functional) or object (inverse-functional).</param>
        /// <returns>The (shared key, first end, second end) triples.</returns>
        private IEnumerable<(TermId Key, TermId First, TermId Second)> SamePredicatePairs(TermId property, bool bySubject)
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
                            yield return (group.Key, values[i], values[j]);
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

            //Overdelete mode routes every rule conclusion to the deletion sink
            //with its own membership predicate, ahead of the derive-side dedup.
            if(OverdeleteSink)
            {
                MarkOverdeleteCandidate(conclusion);

                return;
            }

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
        /// <exception cref="InvalidOperationException">A reported premise is absent from the reasoned-over graph — premise fidelity is structural, and a fabricated premise is an invariant violation, never a report.</exception>
        private void Inconsistent(string rule, scoped ReadOnlySpan<EncodedTriple> premises)
        {
            //Falsity calls are no-ops while marking: overdelete runs over the
            //intact indexes of a consistent closure, where no falsity holds.
            if(OverdeleteSink)
            {
                return;
            }

            foreach(EncodedTriple premise in premises)
            {
                if(!All.Contains(premise) && !Pending.Contains(premise))
                {
                    throw new InvalidOperationException($"Rule {rule} reported a premise absent from the reasoned-over graph.");
                }
            }

            InconsistencyRule = rule;
            InconsistencyPremises = [.. premises];
        }
    }
}
