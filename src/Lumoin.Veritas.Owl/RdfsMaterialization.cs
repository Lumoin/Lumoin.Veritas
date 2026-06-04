using System;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Owl;

/// <summary>
/// Forward-chaining RDFS materialization: derives the rdfs2
/// (domain), rdfs3 (range), rdfs5/rdfs7 (subproperty transitivity
/// and inheritance), and rdfs9/rdfs11 (subclass transitivity and
/// type inheritance) consequences of a triple set, to fixpoint.
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
/// <b>Scope.</b> The axiomatic-triple and reflexivity rules
/// (rdfs4a/4b, rdfs6, rdfs8, rdfs10, rdfs12, rdfs13) and literal
/// generalisation (rdfs1) are not derived here; they enter with
/// the W3C entailment-regime conformance wiring, which fixes their
/// finite-answer semantics.
/// </para>
/// <para>
/// <b>Provenance.</b> The returned set contains only the derived
/// triples — never base triples — so a caller commits it through
/// its own <c>EditSession</c> and the journal entry's additions
/// list is exactly the inferred knowledge of this run.
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
    /// <param name="cancellationToken">A token that aborts derivation between triples.</param>
    /// <returns>The set of triples entailed but not present in the base.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static IReadOnlyCollection<EncodedTriple> MaterializeToFixpoint(
        IEnumerable<EncodedTriple> triples,
        RdfsVocabularyTerms terms,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triples);

        HashSet<EncodedTriple> all = [.. triples];
        HashSet<EncodedTriple> derived = [];
        RdfsSchema schema = RdfsSchema.Extract(all, terms);

        if(schema.IsEmpty)
        {
            return derived;
        }

        //The round's input is materialised to an array because the
        //accumulating set mutates while the round runs.
        EncodedTriple[] worklist = [.. all];
        List<EncodedTriple> delta = [];

        while(true)
        {
            delta.Clear();

            bool schemaTouched = false;

            for(int i = 0; i < worklist.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DeriveInto(worklist[i], schema, all, delta, ref schemaTouched);
            }

            if(delta.Count == 0)
            {
                return derived;
            }

            for(int i = 0; i < delta.Count; i++)
            {
                derived.Add(delta[i]);
            }

            if(schemaTouched)
            {
                //New schema statements change the closures; rebuild
                //and reprocess everything against the wider schema.
                schema = RdfsSchema.Extract(all, terms);
                worklist = [.. all];
            }
            else
            {
                worklist = [.. delta];
            }
        }
    }

    //Derives the immediate consequences of one triple through the
    //precomputed closures, adding each previously-unseen result to
    //the accumulating set and the round delta.
    private static void DeriveInto(
        in EncodedTriple triple,
        RdfsSchema schema,
        HashSet<EncodedTriple> all,
        List<EncodedTriple> delta,
        ref bool schemaTouched)
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

            Emit(EncodedTriple.FromEncoded(subject.Encoded, super.Encoded, @object.Encoded), terms, all, delta, ref schemaTouched);

            if(super == terms.Type)
            {
                EmitTypeExpansion(subject, @object, schema, all, delta, ref schemaTouched);
            }
        }

        //rdfs2/rdfs3 through the effective typings: domain types
        //the subject, range types the object. The closures already
        //include the superclass expansions.
        IReadOnlyList<TermId> domainTypes = schema.DomainTypesOf(predicate);

        for(int i = 0; i < domainTypes.Count; i++)
        {
            Emit(EncodedTriple.FromEncoded(subject.Encoded, terms.Type.Encoded, domainTypes[i].Encoded), terms, all, delta, ref schemaTouched);
        }

        IReadOnlyList<TermId> rangeTypes = schema.RangeTypesOf(predicate);

        for(int i = 0; i < rangeTypes.Count; i++)
        {
            Emit(EncodedTriple.FromEncoded(@object.Encoded, terms.Type.Encoded, rangeTypes[i].Encoded), terms, all, delta, ref schemaTouched);
        }

        //rdfs9 on type statements; rdfs11/rdfs5 close the schema
        //statements themselves.
        if(predicate == terms.Type)
        {
            EmitTypeExpansion(subject, @object, schema, all, delta, ref schemaTouched);
        }
        else if(predicate == terms.SubClassOf)
        {
            IReadOnlyList<TermId> superClasses = schema.SuperClassesOf(@object);

            for(int i = 0; i < superClasses.Count; i++)
            {
                Emit(EncodedTriple.FromEncoded(subject.Encoded, terms.SubClassOf.Encoded, superClasses[i].Encoded), terms, all, delta, ref schemaTouched);
            }
        }
        else if(predicate == terms.SubPropertyOf)
        {
            IReadOnlyList<TermId> superOfTarget = schema.SuperPropertiesOf(@object);

            for(int i = 0; i < superOfTarget.Count; i++)
            {
                Emit(EncodedTriple.FromEncoded(subject.Encoded, terms.SubPropertyOf.Encoded, superOfTarget[i].Encoded), terms, all, delta, ref schemaTouched);
            }
        }
    }

    //rdfs9 through the subclass closure: an instance of a class is
    //an instance of every strict superclass.
    private static void EmitTypeExpansion(
        TermId instance,
        TermId @class,
        RdfsSchema schema,
        HashSet<EncodedTriple> all,
        List<EncodedTriple> delta,
        ref bool schemaTouched)
    {
        RdfsVocabularyTerms terms = schema.Terms;
        IReadOnlyList<TermId> superClasses = schema.SuperClassesOf(@class);

        for(int i = 0; i < superClasses.Count; i++)
        {
            Emit(EncodedTriple.FromEncoded(instance.Encoded, terms.Type.Encoded, superClasses[i].Encoded), terms, all, delta, ref schemaTouched);
        }
    }

    private static void Emit(
        EncodedTriple candidate,
        RdfsVocabularyTerms terms,
        HashSet<EncodedTriple> all,
        List<EncodedTriple> delta,
        ref bool schemaTouched)
    {
        if(!all.Add(candidate))
        {
            return;
        }

        delta.Add(candidate);

        TermId predicate = candidate.Predicate;

        if(predicate == terms.SubClassOf || predicate == terms.SubPropertyOf || predicate == terms.Domain || predicate == terms.Range)
        {
            schemaTouched = true;
        }
    }
}
