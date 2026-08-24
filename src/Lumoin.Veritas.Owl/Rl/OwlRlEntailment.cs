using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;

namespace Lumoin.Veritas.Owl.Rl;

/// <summary>
/// RL-closure-backed entailment: embedding first, refutation through the
/// closure for the contrapositive forms the forward rules cannot reach.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two engines, one question.</b> The RL rules are complete for
/// assertional consequences (Profiles §4.3, Theorem PR1), so a conclusion
/// the rules can derive embeds into the materialized closure — the fast
/// path. What the rules cannot derive is the contrapositive family:
/// <c>differentFrom</c> from functionality plus distinctness, complement
/// membership from disjointness, pairwise distinctness from disjoint
/// properties. Those are refutation questions, and the closure is its own
/// refutation engine: assert the conclusion atom's semantic negation into
/// the premise and re-run — an inconsistency verdict proves the atom holds
/// in every model.
/// </para>
/// <para>
/// <b>The recognized duals.</b> A ground <c>a differentFrom b</c> negates
/// to <c>a sameAs b</c>. A complement-membership block
/// (<c>x rdf:type _:c</c>, <c>_:c owl:complementOf G</c>) negates to
/// <c>x rdf:type G</c>. An <c>owl:AllDifferent</c> block over named
/// members reduces to its pairwise <c>differentFrom</c> atoms. The block
/// node itself is comprehension territory — the structure is granted the
/// way <see cref="OwlComprehension.InformativeConditions"/> grants
/// pure-existence scaffolds — while refutation proves the semantic
/// content; everything left after the blocks reduce embeds normally.
/// A block reduces only when its blanks are confined to the block and
/// its reducing memberships: a mention anywhere else in the conclusion
/// is a joint existential the reduction would split across independent
/// bindings, so such a block stays residual and embeds whole. Each
/// atom's direct-embed probe carries its block context, so a
/// membership's blank only ever binds a genuine complement class in the
/// closure, never a free typing.
/// </para>
/// <para>
/// <b>The comprehension mode.</b> Under
/// <see cref="OwlComprehension.InformativeConditions"/> the check grants
/// the informative comprehension conditions in two halves: the embedding
/// strips the conclusion's pure-existence scaffolds at check time, and the
/// contentful scaffolds — the expression structures the conclusion also
/// makes claims about — are minted into the reasoned-over premise by
/// <see cref="OwlComprehensionScaffolds"/>, with the closure's
/// comprehension completion family deriving the claims the granted
/// structure carries. The mode belongs to this surface alone: the
/// consistency verdicts and every other closure consumer read the
/// normative rule set, so an entailment this mode settles can rest on a
/// refutation the normative consistency surface does not reproduce — that
/// asymmetry is the mode's contract, not a defect. A minted structure can
/// never make a satisfiable premise clash; a clash unique to the augmented
/// run refuses the minting and the check degrades to the un-minted
/// premise.
/// </para>
/// <para>
/// <b>The value-identity bridge.</b> Under
/// <see cref="OwlComprehension.InformativeConditions"/> an embedding
/// failure additionally consults the oracle's literal value equality: a
/// closure literal and a conclusion literal known to denote one data
/// value are the <c>dt-eq</c> direction of the datatype rules, so their
/// sameAs holds in every interpretation and seeds the re-reasoned
/// premise, letting the equality rules derive the conclusion's own
/// spelling. A clash unique to the bridged run refuses the bridges and
/// the check continues on the pre-bridge state.
/// </para>
/// <para>
/// <b>Cost shape.</b> Each refutation atom re-runs the closure over the
/// premise plus one assertion. The conformance corpus is small; the
/// production path shares whatever incremental closure the semi-naive
/// rework brings, through this same surface.
/// </para>
/// </remarks>
public static class OwlRlEntailment
{
    /// <summary>
    /// Whether the premise entails the conclusion under the RL rules,
    /// refutation included.
    /// </summary>
    /// <param name="premise">The premise graph (base triples, not a closure).</param>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <param name="dictionary">The term dictionary the graphs encode with.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="datatypeOracle">The datatype oracle for the <c>dt-*</c> falsities.</param>
    /// <param name="comprehension">How the embedding reads the comprehension conditions.</param>
    /// <param name="axiomaticVocabulary">Which axiomatic vocabulary table seeds every closure the check runs.</param>
    /// <param name="cancellationToken">A token that aborts closure runs.</param>
    /// <returns><see langword="true"/> when the conclusion follows.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="terms"/> was resolved through a dictionary other than <paramref name="dictionary"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static bool Entails(
        IReadOnlyList<Quad> premise,
        IReadOnlyList<Quad> conclusion,
        TermDictionary dictionary,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle = default,
        OwlComprehension comprehension = OwlComprehension.None,
        OwlAxiomaticVocabulary axiomaticVocabulary = OwlAxiomaticVocabulary.Shared,
        CancellationToken cancellationToken = default)
    {
        return TryEntail(premise, conclusion, dictionary, terms, out _, datatypeOracle, comprehension, axiomaticVocabulary, cancellationToken);
    }

    /// <summary>
    /// Whether the premise entails the conclusion under the RL rules,
    /// refutation included, reporting the conclusion triples the check
    /// could not settle.
    /// </summary>
    /// <remarks>
    /// The remainder is stated over the comprehension-stripped conclusion
    /// and names the failing path's triples: the straight embedding's
    /// unembedded remainder when the conclusion carries no refutation
    /// atom, the residual graph's remainder when the non-atom part fails
    /// to embed, or the asserted triples of every refutation atom that
    /// neither embeds nor refutes. Every atom is evaluated, so the
    /// remainder names the complete unproven set. The remainder is empty
    /// exactly when the entailment holds.
    /// </remarks>
    /// <param name="premise">The premise graph (base triples, not a closure).</param>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <param name="dictionary">The term dictionary the graphs encode with.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="unsettled">The conclusion triples the check could not settle; empty exactly when the conclusion follows.</param>
    /// <param name="datatypeOracle">The datatype oracle for the <c>dt-*</c> falsities.</param>
    /// <param name="comprehension">How the embedding reads the comprehension conditions.</param>
    /// <param name="axiomaticVocabulary">Which axiomatic vocabulary table seeds every closure the check runs — the premise closure, the minting-refusal re-run, and each refutation re-run alike.</param>
    /// <param name="cancellationToken">A token that aborts closure runs.</param>
    /// <returns><see langword="true"/> when the conclusion follows.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="terms"/> was resolved through a dictionary other than <paramref name="dictionary"/> — identifiers from independent dictionaries collide by construction, so the pairing is rejected rather than answered wrongly.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static bool TryEntail(
        IReadOnlyList<Quad> premise,
        IReadOnlyList<Quad> conclusion,
        TermDictionary dictionary,
        OwlRlTerms terms,
        out IReadOnlyList<Quad> unsettled,
        OwlRlDatatypeOracle datatypeOracle = default,
        OwlComprehension comprehension = OwlComprehension.None,
        OwlAxiomaticVocabulary axiomaticVocabulary = OwlAxiomaticVocabulary.Shared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(premise);
        ArgumentNullException.ThrowIfNull(conclusion);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(terms);

        if(!ReferenceEquals(terms.Dictionary, dictionary))
        {
            throw new ArgumentException("The vocabulary must be resolved through the same dictionary the graphs encode with.", nameof(terms));
        }

        //Under the informative conditions the conclusion's contentful
        //expression scaffolds are granted: their grammar copies join the
        //reasoned-over premise, and the closure runs with the comprehension
        //completion family on. The one reasoned list feeds the closure, the
        //embedding target, and every refutation re-run alike.
        IReadOnlyList<Quad> reasoned = premise;
        bool mintedIn = false;
        if(comprehension == OwlComprehension.InformativeConditions)
        {
            List<Quad> minted = OwlComprehensionScaffolds.MintContentful(conclusion, premise);
            if(minted.Count > 0)
            {
                reasoned = [.. premise, .. minted];
                mintedIn = true;
            }
        }

        OwlRlResult result = OwlRlClosure.Compute(Encode(reasoned, dictionary), terms, datatypeOracle, comprehension: comprehension, axiomaticVocabulary: axiomaticVocabulary, cancellationToken: cancellationToken);
        if(!result.IsConsistent && mintedIn)
        {
            //Granted structure never makes a satisfiable premise clash, and
            //this pins it: a clash unique to the augmented run refuses the
            //minting — the check degrades to the un-minted premise rather
            //than proving everything.
            OwlRlResult unminted = OwlRlClosure.Compute(Encode(premise, dictionary), terms, datatypeOracle, comprehension: comprehension, axiomaticVocabulary: axiomaticVocabulary, cancellationToken: cancellationToken);
            if(unminted.IsConsistent)
            {
                reasoned = premise;
                result = unminted;
            }
        }

        if(!result.IsConsistent)
        {
            //An inconsistent premise entails everything.
            unsettled = [];

            return true;
        }

        List<Quad> closure = DecodeUnion(reasoned, result, dictionary);
        if(OwlGraphEntailment.TryEmbed(conclusion, closure, comprehension, out IReadOnlyList<Quad> unembedded))
        {
            unsettled = [];

            return true;
        }

        //The value-identity bridge — the dt-eq direction at the entailment
        //surface: a sameAs between two literals the oracle knows denote one
        //value holds in every interpretation, so seeding the pairs the
        //conclusion needs preserves the model set exactly and lets the
        //equality rules derive the conclusion's own spelling inside the
        //closure. Only the completion-granting mode bridges. A clash unique
        //to the bridged run refuses the bridges: the check continues on the
        //pre-bridge reasoned list and closure — minted scaffolding
        //included — rather than proving everything.
        if(comprehension == OwlComprehension.InformativeConditions && datatypeOracle.LiteralsKnownEqual is not null)
        {
            List<Quad> bridges = CollectValueIdentityBridges(conclusion, closure, dictionary, datatypeOracle);
            if(bridges.Count > 0)
            {
                List<Quad> bridged = [.. reasoned, .. bridges];
                OwlRlResult bridgedResult = OwlRlClosure.Compute(Encode(bridged, dictionary), terms, datatypeOracle, comprehension: comprehension, axiomaticVocabulary: axiomaticVocabulary, cancellationToken: cancellationToken);
                if(bridgedResult.IsConsistent)
                {
                    reasoned = bridged;
                    closure = DecodeUnion(reasoned, bridgedResult, dictionary);
                    if(OwlGraphEntailment.TryEmbed(conclusion, closure, comprehension, out unembedded))
                    {
                        unsettled = [];

                        return true;
                    }
                }
            }
        }

        //The forward rules did not reach the conclusion; reduce it into
        //refutation atoms plus a residual graph and prove each part.
        (List<Quad> residual, List<RefutationAtom> atoms) = Reduce(conclusion);
        if(atoms.Count == 0)
        {
            unsettled = unembedded;

            return false;
        }

        if(!OwlGraphEntailment.TryEmbed(residual, closure, comprehension, out IReadOnlyList<Quad> residualRemainder))
        {
            unsettled = residualRemainder;

            return false;
        }

        List<Quad> unproven = [];
        foreach(RefutationAtom atom in atoms)
        {
            if(OwlGraphEntailment.Embeds(atom.Probe, closure, comprehension))
            {
                continue;
            }

            List<Quad> negated = [.. reasoned, atom.Negation];
            OwlRlResult refutation = OwlRlClosure.Compute(Encode(negated, dictionary), terms, datatypeOracle, comprehension: comprehension, axiomaticVocabulary: axiomaticVocabulary, cancellationToken: cancellationToken);
            if(refutation.IsConsistent)
            {
                unproven.Add(atom.Asserted);
            }
        }

        unsettled = unproven;

        return unproven.Count == 0;
    }

    /// <summary>
    /// Collects the value-identity bridges an embedding failure motivates:
    /// for each literal object of the conclusion and each term-distinct
    /// literal object of the closure the oracle knows denote one value, one
    /// sameAs quad from the closure's literal onto the conclusion's. The
    /// conclusion literal mints into the dictionary here so the oracle can
    /// be consulted; a mint without a statement never reaches the closure.
    /// </summary>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <param name="closure">The computed closure graph.</param>
    /// <param name="dictionary">The term dictionary the graphs encode with.</param>
    /// <param name="datatypeOracle">The datatype oracle answering value equality.</param>
    /// <returns>The bridge quads; empty when no pair is known equal.</returns>
    private static List<Quad> CollectValueIdentityBridges(IReadOnlyList<Quad> conclusion, List<Quad> closure, TermDictionary dictionary, OwlRlDatatypeOracle datatypeOracle)
    {
        List<Quad> bridges = [];
        HashSet<Literal> conclusionLiterals = [];
        foreach(Quad quad in conclusion)
        {
            if(quad.Object is Literal literal)
            {
                conclusionLiterals.Add(literal);
            }
        }

        if(conclusionLiterals.Count == 0)
        {
            return bridges;
        }

        HashSet<Literal> closureLiterals = [];
        foreach(Quad quad in closure)
        {
            if(quad.Object is Literal literal)
            {
                closureLiterals.Add(literal);
            }
        }

        foreach(Literal target in conclusionLiterals)
        {
            TermId targetId = dictionary.GetOrAdd(target);
            foreach(Literal source in closureLiterals)
            {
                if(source.Equals(target))
                {
                    continue;
                }

                if(datatypeOracle.LiteralsKnownEqual(dictionary.GetOrAdd(source), targetId))
                {
                    bridges.Add(new Quad(source, new NamedNode(OwlVocabulary.SameAs), target, Graph: null));
                }
            }
        }

        return bridges;
    }

    /// <summary>One refutation obligation: the conclusion triple it stands for and the semantic negation whose inconsistency proves it.</summary>
    /// <param name="Asserted">The conclusion triple the atom stands for, reported when the atom stays unproven.</param>
    /// <param name="Negation">The semantic negation asserted into the premise for the refutation run.</param>
    /// <param name="Probe">The quads whose joint embedding into the closure proves the atom directly: the asserted triple together with its block context, so a blank object only ever binds a genuine witness.</param>
    private readonly record struct RefutationAtom(Quad Asserted, Quad Negation, IReadOnlyList<Quad> Probe);

    /// <summary>
    /// Reduces the conclusion into refutation atoms and the residual
    /// graph: ground <c>differentFrom</c> triples, complement-membership
    /// blocks, and <c>owl:AllDifferent</c> blocks over named members
    /// convert; anything unrecognized stays residual for the embedding.
    /// A block converts only when its blanks are confined to the block
    /// and its reducing memberships — a blank mentioned elsewhere keeps
    /// the whole block residual, preserving the conclusion's joint
    /// existential.
    /// </summary>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <returns>The residual graph and the atoms.</returns>
    private static (List<Quad> Residual, List<RefutationAtom> Atoms) Reduce(IReadOnlyList<Quad> conclusion)
    {
        Dictionary<Utf8String, List<Quad>> bySubject = [];
        Dictionary<Utf8String, List<Quad>> occurrences = [];
        foreach(Quad quad in conclusion)
        {
            if(quad.Subject is BlankNode subject)
            {
                Append(bySubject, subject.Label, quad);
                Append(occurrences, subject.Label, quad);
            }

            if(quad.Object is BlankNode @object)
            {
                Append(occurrences, @object.Label, quad);
            }
        }

        HashSet<Quad> reduced = [];
        List<RefutationAtom> atoms = [];

        foreach(Quad quad in conclusion)
        {
            //Ground differentFrom: the sameAs dual.
            if(quad.Predicate.Iri.Equals(OwlVocabulary.DifferentFrom) && quad.Subject is NamedNode a && quad.Object is NamedNode b)
            {
                atoms.Add(new RefutationAtom(quad, new Quad(a, new NamedNode(OwlVocabulary.SameAs), b, Graph: null), [quad]));
                reduced.Add(quad);

                continue;
            }

            //Complement membership: x type _:c with _:c complementOf G.
            //The block node is granted; the membership refutes through
            //x type G. The probe pairs the membership with the block's
            //complementOf triple so the blank never matches free.
            if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && quad.Subject is NamedNode member
                && quad.Object is BlankNode complementNode
                && TryReadComplementBlock(complementNode.Label, bySubject, out NamedNode? complemented, out Quad? complementTriple, out List<Quad>? blockTriples)
                && ComplementBlankIsConfined(complementNode.Label, occurrences, blockTriples))
            {
                atoms.Add(new RefutationAtom(quad, new Quad(member, new NamedNode(Vocabulary.Rdf.Type), complemented, Graph: null), [quad, complementTriple]));
                reduced.Add(quad);
                foreach(Quad blockTriple in blockTriples)
                {
                    reduced.Add(blockTriple);
                }

                continue;
            }

            //AllDifferent over named members: the block reduces to its
            //pairwise distinctness atoms.
            if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && quad.Object is NamedNode typed
                && typed.Iri.Equals(OwlVocabulary.AllDifferent)
                && quad.Subject is BlankNode allDifferentNode
                && TryReadAllDifferentBlock(allDifferentNode.Label, bySubject, out List<NamedNode>? members, out List<Quad>? memberTriples)
                && BlockBlanksAreConfined(memberTriples, occurrences))
            {
                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        Quad asserted = new(members[i], new NamedNode(OwlVocabulary.DifferentFrom), members[j], Graph: null);
                        atoms.Add(new RefutationAtom(asserted, new Quad(members[i], new NamedNode(OwlVocabulary.SameAs), members[j], Graph: null), [asserted]));
                    }
                }

                reduced.Add(quad);
                foreach(Quad memberTriple in memberTriples)
                {
                    reduced.Add(memberTriple);
                }
            }
        }

        List<Quad> residual = [];
        foreach(Quad quad in conclusion)
        {
            if(!reduced.Contains(quad))
            {
                residual.Add(quad);
            }
        }

        return (residual, atoms);
    }

    /// <summary>
    /// Reads a complement block: the blank's triples must be exactly the
    /// <c>owl:complementOf</c> onto a named class plus an optional
    /// <c>owl:Class</c> typing.
    /// </summary>
    /// <param name="label">The blank's label.</param>
    /// <param name="bySubject">Blank-subject triple index.</param>
    /// <param name="complemented">The complemented named class.</param>
    /// <param name="complementTriple">The block's <c>owl:complementOf</c> triple.</param>
    /// <param name="blockTriples">The block's triples for removal.</param>
    /// <returns><see langword="true"/> for a well-formed complement block.</returns>
    private static bool TryReadComplementBlock(
        Utf8String label,
        Dictionary<Utf8String, List<Quad>> bySubject,
        [NotNullWhen(true)] out NamedNode? complemented,
        [NotNullWhen(true)] out Quad? complementTriple,
        [NotNullWhen(true)] out List<Quad>? blockTriples)
    {
        complemented = null;
        complementTriple = null;
        blockTriples = null;

        if(!bySubject.TryGetValue(label, out List<Quad>? triples))
        {
            return false;
        }

        List<Quad> block = [];
        foreach(Quad quad in triples)
        {
            Utf8String predicate = quad.Predicate.Iri;
            if(predicate.Equals(OwlVocabulary.ComplementOf) && quad.Object is NamedNode named)
            {
                if(complemented is not null)
                {
                    return false;
                }

                complemented = named;
                complementTriple = quad;
            }
            else if(!(predicate.Equals(Vocabulary.Rdf.Type) && quad.Object is NamedNode typing && typing.Iri.Equals(OwlVocabulary.ClassTerm)))
            {
                return false;
            }

            block.Add(quad);
        }

        blockTriples = block;

        return complemented is not null;
    }

    /// <summary>
    /// Whether every conclusion mention of a complement block's blank is
    /// accounted for by the reduction: a mention is either one of the
    /// block's own triples or a membership that reduces to an atom — a
    /// named subject typed by the blank. Any other mention is a joint
    /// existential the reduction would split, so the block stays
    /// residual.
    /// </summary>
    /// <param name="label">The block blank's label.</param>
    /// <param name="occurrences">All-position blank occurrence index over the conclusion.</param>
    /// <param name="blockTriples">The block's triples.</param>
    /// <returns><see langword="true"/> when the blank is confined.</returns>
    private static bool ComplementBlankIsConfined(Utf8String label, Dictionary<Utf8String, List<Quad>> occurrences, List<Quad> blockTriples)
    {
        HashSet<Quad> block = [.. blockTriples];
        foreach(Quad mention in occurrences[label])
        {
            if(block.Contains(mention))
            {
                continue;
            }

            bool reducingMembership = mention.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && mention.Subject is NamedNode
                && mention.Object is BlankNode typed
                && typed.Label.Equals(label);
            if(!reducingMembership)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether every blank an <c>owl:AllDifferent</c> block touches — the
    /// block node and its list cells — is mentioned nowhere in the
    /// conclusion outside the block's own triples. An outside mention is
    /// a joint existential the reduction would split, so the block stays
    /// residual.
    /// </summary>
    /// <param name="blockTriples">The block's triples.</param>
    /// <param name="occurrences">All-position blank occurrence index over the conclusion.</param>
    /// <returns><see langword="true"/> when every block blank is confined.</returns>
    private static bool BlockBlanksAreConfined(List<Quad> blockTriples, Dictionary<Utf8String, List<Quad>> occurrences)
    {
        HashSet<Quad> block = [.. blockTriples];
        foreach(Quad quad in blockTriples)
        {
            if(quad.Subject is BlankNode subject && !MentionsStayWithin(subject.Label, occurrences, block))
            {
                return false;
            }

            if(quad.Object is BlankNode @object && !MentionsStayWithin(@object.Label, occurrences, block))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every conclusion mention of the label is one of the block's triples.</summary>
    /// <param name="label">The blank label.</param>
    /// <param name="occurrences">All-position blank occurrence index over the conclusion.</param>
    /// <param name="block">The block's triples.</param>
    /// <returns><see langword="true"/> when no mention escapes the block.</returns>
    private static bool MentionsStayWithin(Utf8String label, Dictionary<Utf8String, List<Quad>> occurrences, HashSet<Quad> block)
    {
        foreach(Quad mention in occurrences[label])
        {
            if(!block.Contains(mention))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads an <c>owl:AllDifferent</c> block: the node's members list
    /// (under <c>owl:members</c> or <c>owl:distinctMembers</c>) must be a
    /// well-formed RDF list of named individuals.
    /// </summary>
    /// <param name="label">The block node's label.</param>
    /// <param name="bySubject">Blank-subject triple index.</param>
    /// <param name="members">The named members in list order.</param>
    /// <param name="blockTriples">The node and list triples for removal.</param>
    /// <returns><see langword="true"/> for a well-formed block.</returns>
    private static bool TryReadAllDifferentBlock(
        Utf8String label,
        Dictionary<Utf8String, List<Quad>> bySubject,
        [NotNullWhen(true)] out List<NamedNode>? members,
        [NotNullWhen(true)] out List<Quad>? blockTriples)
    {
        members = null;
        blockTriples = null;

        if(!bySubject.TryGetValue(label, out List<Quad>? triples))
        {
            return false;
        }

        List<Quad> block = [];
        Utf8String? listHead = null;
        foreach(Quad quad in triples)
        {
            Utf8String predicate = quad.Predicate.Iri;
            bool isMembers = predicate.Equals(OwlVocabulary.Members) || predicate.Equals(OwlVocabulary.DistinctMembers);
            bool isTyping = predicate.Equals(Vocabulary.Rdf.Type) && quad.Object is NamedNode typing && typing.Iri.Equals(OwlVocabulary.AllDifferent);
            if(isMembers)
            {
                if(listHead is not null || quad.Object is not BlankNode head)
                {
                    return false;
                }

                listHead = head.Label;
            }
            else if(!isTyping)
            {
                return false;
            }

            block.Add(quad);
        }

        if(listHead is null)
        {
            return false;
        }

        //Walk the list: blanks chained by rest, named members under first,
        //terminated at nil. Counters rather than null checks keep the
        //exactly-one discipline visible.
        List<NamedNode> collected = [];
        Utf8String current = listHead.Value;
        while(true)
        {
            if(!bySubject.TryGetValue(current, out List<Quad>? cellTriples))
            {
                return false;
            }

            int firstCount = 0;
            int restCount = 0;
            NamedNode? first = null;
            Utf8String nextCell = default;
            bool terminated = false;
            foreach(Quad quad in cellTriples)
            {
                Utf8String predicate = quad.Predicate.Iri;
                if(predicate.Equals(RdfVocabulary.Rdf.First))
                {
                    firstCount++;
                    if(firstCount > 1 || quad.Object is not NamedNode named)
                    {
                        return false;
                    }

                    first = named;
                }
                else if(predicate.Equals(RdfVocabulary.Rdf.Rest))
                {
                    restCount++;
                    if(restCount > 1)
                    {
                        return false;
                    }

                    if(quad.Object is BlankNode next)
                    {
                        nextCell = next.Label;
                    }
                    else if(quad.Object is NamedNode named && named.Iri.Equals(RdfVocabulary.Rdf.Nil))
                    {
                        terminated = true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if(!(predicate.Equals(Vocabulary.Rdf.Type) && quad.Object is NamedNode typing && typing.Iri.Equals(RdfVocabulary.Rdf.List)))
                {
                    return false;
                }

                block.Add(quad);
            }

            if(firstCount != 1 || restCount != 1)
            {
                return false;
            }

            collected.Add(first!);
            if(terminated)
            {
                break;
            }

            current = nextCell;
        }

        members = collected;
        blockTriples = block;

        return members.Count >= 2;
    }

    /// <summary>Encodes a quad graph through the dictionary for the closure.</summary>
    /// <param name="quads">The graph to encode.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <returns>The encoded triples.</returns>
    private static List<EncodedTriple> Encode(IReadOnlyList<Quad> quads, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = new(quads.Count);
        foreach(Quad quad in quads)
        {
            encoded.Add(EncodedTriple.FromEncoded(
                dictionary.GetOrAdd(quad.Subject).Encoded,
                dictionary.GetOrAdd(quad.Predicate).Encoded,
                dictionary.GetOrAdd(quad.Object).Encoded));
        }

        return encoded;
    }

    /// <summary>Decodes the closure back to quads: the premise plus every derivation with a named predicate.</summary>
    /// <param name="premise">The premise graph.</param>
    /// <param name="result">The closure result.</param>
    /// <param name="dictionary">The dictionary the triples encode with.</param>
    /// <returns>The closure graph.</returns>
    private static List<Quad> DecodeUnion(IReadOnlyList<Quad> premise, OwlRlResult result, TermDictionary dictionary)
    {
        List<Quad> closure = [.. premise];
        foreach(EncodedTriple triple in result.Derived)
        {
            if(dictionary.Resolve(triple.Predicate) is NamedNode predicate)
            {
                closure.Add(new Quad(dictionary.Resolve(triple.Subject), predicate, dictionary.Resolve(triple.Object), Graph: null));
            }
        }

        return closure;
    }

    /// <summary>Appends a quad to a label's index list, creating it on first contact.</summary>
    /// <param name="index">The index.</param>
    /// <param name="label">The blank label.</param>
    /// <param name="quad">The quad to record.</param>
    private static void Append(Dictionary<Utf8String, List<Quad>> index, Utf8String label, Quad quad)
    {
        if(!index.TryGetValue(label, out List<Quad>? list))
        {
            list = [];
            index[label] = list;
        }

        list.Add(quad);
    }

}
