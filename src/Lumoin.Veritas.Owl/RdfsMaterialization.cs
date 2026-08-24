using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// Forward-chaining RDFS materialization: derives the rdfs2
/// (domain), rdfs3 (range), rdfs5/rdfs7 (subproperty transitivity
/// and inheritance), and rdfs9/rdfs11 (subclass transitivity and
/// type inheritance) consequences of a triple set, to fixpoint —
/// plus, when the regime vocabulary terms are supplied, the
/// finite axiomatic rules (rdf1, axiomatic class/property typing,
/// rdfs6, rdfs8, rdfs10, rdfs12, rdfs13).
/// </summary>
/// <remarks>
/// <para>
/// <b>One closure-driven pass per round.</b> The TBox closures in
/// <see cref="RdfsSchema"/> fold whole rule chains into single
/// lookups — a triple's effective domain typings already account
/// for superproperties and superclasses — so the first round
/// derives everything except consequences that change the schema
/// itself. Rounds repeat until no new triple appears; a round that
/// derived new schema statements (for example through a property
/// declared <c>rdfs:subPropertyOf rdfs:subClassOf</c>) re-extracts
/// the schema and reprocesses every triple, otherwise the next
/// round processes only the previous round's delta.
/// </para>
/// <para>
/// <b>Scope.</b> The base profile — only the five schema terms
/// supplied — derives the schema-driven rules and nothing else.
/// Supplying the regime terms (<c>rdf:Property</c>,
/// <c>rdfs:Class</c>, …) additionally derives the finite axiomatic
/// rules per SPARQL 1.1 entailment-regime semantics: rdf1 typing
/// of predicates, axiomatic class/property typing of schema-
/// statement subjects and objects, and the reflexivity and
/// subsumption rules rdfs6/rdfs8/rdfs10/rdfs12/rdfs13 keyed off
/// those typings. The everything-is-a-resource rules (rdfs4a/4b)
/// are deliberately not materialised: their conclusions grow with
/// the instance data (two per triple), require literal-kind
/// knowledge this term-id-level reasoner does not have, and no
/// finite-answer regime query observes them except by naming
/// <c>rdfs:Resource</c> directly.
/// </para>
/// <para>
/// <b>Provenance.</b> The derived set never includes base triples,
/// so committing it through an <c>EditSession</c> makes the journal
/// entry's additions exactly the inferred knowledge of the run —
/// which <see cref="MaterializeAndCommitAsync"/> does — and each
/// derivation step is announced as an
/// <see cref="InferenceTraceEvent"/> when a trace handler is
/// supplied, sharing the run's correlation id.
/// </para>
/// </remarks>
public static class RdfsMaterialization
{
    /// <summary>
    /// Derives the RDFS consequences of <paramref name="triples"/>
    /// to fixpoint and returns the derived triples — the base
    /// triples are never included in the result.
    /// </summary>
    /// <param name="triples">The base triples, schema statements included.</param>
    /// <param name="terms">The resolved vocabulary term identifiers.</param>
    /// <param name="traceHandler">Optional handler receiving one <see cref="InferenceTraceEvent"/> per derivation.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Required when <paramref name="traceHandler"/> is non-<c>null</c>; ignored otherwise.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts derivation between triples.</param>
    /// <returns>The set of triples entailed but not present in the base.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static IReadOnlyCollection<EncodedTriple> MaterializeToFixpoint(
        IEnumerable<EncodedTriple> triples,
        RdfsVocabularyTerms terms,
        TraceHandler<InferenceTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);

        if(traceHandler is not null && timeProvider is null)
        {
            throw new ArgumentException("A time provider must be supplied when a trace handler is configured.", nameof(timeProvider));
        }

        MaterializationContext context = new()
        {
            All = [.. triples],
            Terms = terms,
            TraceHandler = traceHandler,
            TimeProvider = timeProvider,
            CorrelationId = correlationId,
        };

        HashSet<EncodedTriple> derived = [];
        RdfsSchema schema = RdfsSchema.Extract(context.All, terms);

        //The axiomatic rules derive from bare statements (rdf1 types
        //every predicate), so an empty schema only short-circuits the
        //base profile.
        if(schema.IsEmpty && !HasAxiomaticRules(terms))
        {
            return derived;
        }

        //The round's input is materialised to an array because the
        //accumulating set mutates while the round runs.
        EncodedTriple[] worklist = [.. context.All];

        while(true)
        {
            context.Delta.Clear();
            context.SchemaTouched = false;

            for(int i = 0; i < worklist.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DeriveInto(worklist[i], schema, context);
            }

            if(context.Delta.Count == 0)
            {
                return derived;
            }

            for(int i = 0; i < context.Delta.Count; i++)
            {
                derived.Add(context.Delta[i]);
            }

            if(context.SchemaTouched)
            {
                //New schema statements change the closures; rebuild
                //and reprocess everything against the wider schema.
                schema = RdfsSchema.Extract(context.All, terms);
                worklist = [.. context.All];
            }
            else
            {
                worklist = [.. context.Delta];
            }
        }
    }

    /// <summary>
    /// Derives the RDFS consequences of <paramref name="store"/>'s
    /// triples and commits them through an
    /// <see cref="EditSession"/>, so the journal entry's additions
    /// are exactly the inferred knowledge of this run. A run that
    /// derives nothing returns the store unchanged with no commit.
    /// </summary>
    /// <param name="store">The store to materialize over; its snapshot is the base the session branches from.</param>
    /// <param name="terms">The resolved vocabulary term identifiers.</param>
    /// <param name="traceHandler">Optional handler receiving one <see cref="InferenceTraceEvent"/> per derivation.</param>
    /// <param name="timeProvider">Clock used to stamp Ticks on emitted trace events. Required when <paramref name="traceHandler"/> is non-<c>null</c>; ignored otherwise.</param>
    /// <param name="correlationId">Correlation id stamped on emitted trace events.</param>
    /// <param name="cancellationToken">A token that aborts derivation and the commit.</param>
    /// <returns>The store over the post-commit snapshot (the input store when nothing was derived) and the number of derived triples.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A trace handler is supplied without a time provider.</exception>
    /// <exception cref="EditSessionConcurrencyException">Another session committed against the store's journal first.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<(HypertrieGraphStore Store, int DerivedCount)> MaterializeAndCommitAsync(
        HypertrieGraphStore store,
        RdfsVocabularyTerms terms,
        TraceHandler<InferenceTraceEvent>? traceHandler = null,
        TimeProvider? timeProvider = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        IReadOnlyCollection<EncodedTriple> derived = MaterializeToFixpoint(
            store.Match(TermId.None, TermId.None, TermId.None),
            terms,
            traceHandler,
            timeProvider,
            correlationId,
            cancellationToken);

        if(derived.Count == 0)
        {
            return (store, 0);
        }

        EditSession session = await store.Snapshot.Store.OpenEditSessionAsync(store.Snapshot, cancellationToken).ConfigureAwait(false);

        await using(session.ConfigureAwait(false))
        {
            session.AddRange(derived);
            HypertrieSnapshot committed = await session.CommitAsync(cancellationToken).ConfigureAwait(false);

            return (HypertrieGraphStore.FromSnapshot(committed), derived.Count);
        }
    }

    //Derives the immediate consequences of one triple through the
    //precomputed closures, adding each previously-unseen result to
    //the accumulating set and the round delta.
    private static void DeriveInto(
        in EncodedTriple triple,
        RdfsSchema schema,
        MaterializationContext context)
    {
        RdfsVocabularyTerms terms = schema.Terms;
        TermId subject = triple.Subject;
        TermId predicate = triple.Predicate;
        TermId @object = triple.Object;

        //rdfs7 through the subproperty closure: the triple holds
        //under every strict superproperty. A superproperty that is
        //rdf:type itself feeds the subclass expansion below.
        IReadOnlyList<TermId> superProperties = schema.SuperPropertiesOf(predicate);

        for(int i = 0; i < superProperties.Count; i++)
        {
            TermId super = superProperties[i];

            Emit(EntailmentRules.Rdfs7, in triple, EncodedTriple.FromEncoded(subject.Encoded, super.Encoded, @object.Encoded), context);

            if(super == terms.Type)
            {
                EmitTypeExpansion(in triple, subject, @object, schema, context);
            }
        }

        //rdfs2/rdfs3 through the effective typings: domain types
        //the subject, range types the object. The closures already
        //include the superclass expansions.
        IReadOnlyList<TermId> domainTypes = schema.DomainTypesOf(predicate);

        for(int i = 0; i < domainTypes.Count; i++)
        {
            Emit(EntailmentRules.Rdfs2, in triple, EncodedTriple.FromEncoded(subject.Encoded, terms.Type.Encoded, domainTypes[i].Encoded), context);
        }

        IReadOnlyList<TermId> rangeTypes = schema.RangeTypesOf(predicate);

        for(int i = 0; i < rangeTypes.Count; i++)
        {
            Emit(EntailmentRules.Rdfs3, in triple, EncodedTriple.FromEncoded(@object.Encoded, terms.Type.Encoded, rangeTypes[i].Encoded), context);
        }

        //rdf1: any statement types its predicate as an rdf:Property.
        if(terms.Property != TermId.None)
        {
            Emit(EntailmentRules.Rdf1, in triple, EncodedTriple.FromEncoded(predicate.Encoded, terms.Type.Encoded, terms.Property.Encoded), context);
        }

        //rdfs9 on type statements; rdfs11/rdfs5 close the schema
        //statements themselves. The axiomatic typings mirror the
        //domains and ranges the RDFS axiomatic schema declares for
        //the vocabulary properties.
        if(predicate == terms.Type)
        {
            EmitTypeExpansion(in triple, subject, @object, schema, context);

            if(terms.Class != TermId.None)
            {
                Emit(EntailmentRules.AxiomaticTyping, in triple, EncodedTriple.FromEncoded(@object.Encoded, terms.Type.Encoded, terms.Class.Encoded), context);
            }

            EmitVocabularyTypeConsequences(in triple, subject, @object, terms, context);
        }
        else if(predicate == terms.SubClassOf)
        {
            IReadOnlyList<TermId> superClasses = schema.SuperClassesOf(@object);

            for(int i = 0; i < superClasses.Count; i++)
            {
                Emit(EntailmentRules.Rdfs11, in triple, EncodedTriple.FromEncoded(subject.Encoded, terms.SubClassOf.Encoded, superClasses[i].Encoded), context);
            }

            EmitAxiomaticTyping(in triple, subject, terms.Class, terms, context);
            EmitAxiomaticTyping(in triple, @object, terms.Class, terms, context);
        }
        else if(predicate == terms.SubPropertyOf)
        {
            IReadOnlyList<TermId> superOfTarget = schema.SuperPropertiesOf(@object);

            for(int i = 0; i < superOfTarget.Count; i++)
            {
                Emit(EntailmentRules.Rdfs5, in triple, EncodedTriple.FromEncoded(subject.Encoded, terms.SubPropertyOf.Encoded, superOfTarget[i].Encoded), context);
            }

            EmitAxiomaticTyping(in triple, subject, terms.Property, terms, context);
            EmitAxiomaticTyping(in triple, @object, terms.Property, terms, context);
        }
        else if(predicate == terms.Domain || predicate == terms.Range)
        {
            EmitAxiomaticTyping(in triple, subject, terms.Property, terms, context);
            EmitAxiomaticTyping(in triple, @object, terms.Class, terms, context);
        }
    }

    //The reflexivity and subsumption rules keyed off a vocabulary
    //typing: rdfs6 on rdf:Property, rdfs8 and rdfs10 on rdfs:Class,
    //rdfs12 on rdfs:ContainerMembershipProperty, rdfs13 on
    //rdfs:Datatype. Each fires only when its terms are supplied.
    private static void EmitVocabularyTypeConsequences(
        in EncodedTriple premise,
        TermId instance,
        TermId @class,
        RdfsVocabularyTerms terms,
        MaterializationContext context)
    {
        if(@class == terms.Property && terms.Property != TermId.None)
        {
            Emit(EntailmentRules.Rdfs6, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.SubPropertyOf.Encoded, instance.Encoded), context);
        }
        else if(@class == terms.Class && terms.Class != TermId.None)
        {
            Emit(EntailmentRules.Rdfs10, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.SubClassOf.Encoded, instance.Encoded), context);

            if(terms.Resource != TermId.None)
            {
                Emit(EntailmentRules.Rdfs8, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.SubClassOf.Encoded, terms.Resource.Encoded), context);
            }
        }
        else if(@class == terms.ContainerMembershipProperty && terms.ContainerMembershipProperty != TermId.None && terms.Member != TermId.None)
        {
            Emit(EntailmentRules.Rdfs12, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.SubPropertyOf.Encoded, terms.Member.Encoded), context);
        }
        else if(@class == terms.Datatype && terms.Datatype != TermId.None && terms.Literal != TermId.None)
        {
            Emit(EntailmentRules.Rdfs13, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.SubClassOf.Encoded, terms.Literal.Encoded), context);
        }
    }

    //Types a schema-statement participant with the class the RDFS
    //axiomatic schema gives it; a None class (term not supplied)
    //disables the typing.
    private static void EmitAxiomaticTyping(
        in EncodedTriple premise,
        TermId instance,
        TermId @class,
        RdfsVocabularyTerms terms,
        MaterializationContext context)
    {
        if(@class != TermId.None)
        {
            Emit(EntailmentRules.AxiomaticTyping, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.Type.Encoded, @class.Encoded), context);
        }
    }

    //Whether any axiomatic rule is enabled — the rules that derive
    //from bare statements rather than from extracted schema.
    private static bool HasAxiomaticRules(in RdfsVocabularyTerms terms)
    {
        return terms.Property != TermId.None
            || terms.Class != TermId.None
            || terms.ContainerMembershipProperty != TermId.None
            || terms.Datatype != TermId.None;
    }

    //rdfs9 through the subclass closure: an instance of a class is
    //an instance of every strict superclass.
    private static void EmitTypeExpansion(
        in EncodedTriple premise,
        TermId instance,
        TermId @class,
        RdfsSchema schema,
        MaterializationContext context)
    {
        RdfsVocabularyTerms terms = schema.Terms;
        IReadOnlyList<TermId> superClasses = schema.SuperClassesOf(@class);

        for(int i = 0; i < superClasses.Count; i++)
        {
            Emit(EntailmentRules.Rdfs9, in premise, EncodedTriple.FromEncoded(instance.Encoded, terms.Type.Encoded, superClasses[i].Encoded), context);
        }
    }

    private static void Emit(
        string rule,
        in EncodedTriple premise,
        EncodedTriple candidate,
        MaterializationContext context)
    {
        if(!context.All.Add(candidate))
        {
            return;
        }

        context.Delta.Add(candidate);

        TermId predicate = candidate.Predicate;
        RdfsVocabularyTerms terms = context.Terms;

        if(predicate == terms.SubClassOf || predicate == terms.SubPropertyOf || predicate == terms.Domain || predicate == terms.Range)
        {
            context.SchemaTouched = true;
        }

        if(context.TraceHandler is not null)
        {
            InferenceTraceEvent evt = new(
                ++context.TraceSequence,
                context.TimeProvider!.GetUtcNow().UtcTicks,
                context.CorrelationId,
                rule,
                [premise],
                candidate);

            context.TraceHandler(in evt);
        }
    }

    //The per-run mutable state one materialization threads through
    //its derivation calls: the accumulating set, the round delta,
    //and the trace wiring.
    private sealed class MaterializationContext
    {
        /// <summary>Every triple seen so far — base plus derived.</summary>
        public required HashSet<EncodedTriple> All { get; init; }

        /// <summary>The current round's newly derived triples.</summary>
        public List<EncodedTriple> Delta { get; } = [];

        /// <summary>The vocabulary term identifiers, for schema-touch detection.</summary>
        public required RdfsVocabularyTerms Terms { get; init; }

        /// <summary>The trace handler, or <c>null</c> for no tracing.</summary>
        public TraceHandler<InferenceTraceEvent>? TraceHandler { get; init; }

        /// <summary>Clock for trace timestamps; non-<c>null</c> whenever <see cref="TraceHandler"/> is.</summary>
        public TimeProvider? TimeProvider { get; init; }

        /// <summary>The run's correlation id.</summary>
        public Guid CorrelationId { get; init; }

        /// <summary>Whether the current round derived a schema statement, forcing re-extraction.</summary>
        public bool SchemaTouched;

        /// <summary>The trace stream's sequence counter.</summary>
        public long TraceSequence;
    }
}
