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
    /// <param name="cancellationToken">A token that aborts closure runs.</param>
    /// <returns><see langword="true"/> when the conclusion follows.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static bool Entails(
        IReadOnlyList<Quad> premise,
        IReadOnlyList<Quad> conclusion,
        TermDictionary dictionary,
        OwlRlTerms terms,
        OwlRlDatatypeOracle datatypeOracle = default,
        OwlComprehension comprehension = OwlComprehension.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(premise);
        ArgumentNullException.ThrowIfNull(conclusion);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(terms);

        OwlRlResult result = OwlRlClosure.Compute(Encode(premise, dictionary), terms, datatypeOracle, cancellationToken: cancellationToken);
        if(!result.IsConsistent)
        {
            //An inconsistent premise entails everything.
            return true;
        }

        List<Quad> closure = DecodeUnion(premise, result, dictionary);
        if(OwlGraphEntailment.Embeds(conclusion, closure, comprehension))
        {
            return true;
        }

        //The forward rules did not reach the conclusion; reduce it into
        //refutation atoms plus a residual graph and prove each part.
        (List<Quad> residual, List<RefutationAtom> atoms) = Reduce(conclusion);
        if(atoms.Count == 0)
        {
            return false;
        }

        if(!OwlGraphEntailment.Embeds(residual, closure, comprehension))
        {
            return false;
        }

        foreach(RefutationAtom atom in atoms)
        {
            if(OwlGraphEntailment.Embeds([atom.Asserted], closure, comprehension))
            {
                continue;
            }

            List<Quad> negated = [.. premise, atom.Negation];
            OwlRlResult refutation = OwlRlClosure.Compute(Encode(negated, dictionary), terms, datatypeOracle, cancellationToken: cancellationToken);
            if(refutation.IsConsistent)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One refutation obligation: the conclusion triple it stands for and the semantic negation whose inconsistency proves it.</summary>
    /// <param name="Asserted">The conclusion triple the atom stands for, for the direct-embed fast path.</param>
    /// <param name="Negation">The semantic negation asserted into the premise for the refutation run.</param>
    private readonly record struct RefutationAtom(Quad Asserted, Quad Negation);

    /// <summary>
    /// Reduces the conclusion into refutation atoms and the residual
    /// graph: ground <c>differentFrom</c> triples, complement-membership
    /// blocks, and <c>owl:AllDifferent</c> blocks over named members
    /// convert; anything unrecognized stays residual for the embedding.
    /// </summary>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <returns>The residual graph and the atoms.</returns>
    private static (List<Quad> Residual, List<RefutationAtom> Atoms) Reduce(IReadOnlyList<Quad> conclusion)
    {
        Dictionary<Utf8String, List<Quad>> bySubject = [];
        foreach(Quad quad in conclusion)
        {
            if(quad.Subject is BlankNode subject)
            {
                Append(bySubject, subject.Label, quad);
            }
        }

        HashSet<Quad> reduced = [];
        List<RefutationAtom> atoms = [];

        foreach(Quad quad in conclusion)
        {
            //Ground differentFrom: the sameAs dual.
            if(quad.Predicate.Iri.Equals(OwlVocabulary.DifferentFrom) && quad.Subject is NamedNode a && quad.Object is NamedNode b)
            {
                atoms.Add(new RefutationAtom(quad, new Quad(a, new NamedNode(OwlVocabulary.SameAs), b, Graph: null)));
                reduced.Add(quad);

                continue;
            }

            //Complement membership: x type _:c with _:c complementOf G.
            //The block node is granted; the membership refutes through
            //x type G.
            if(quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
                && quad.Subject is NamedNode member
                && quad.Object is BlankNode complementNode
                && TryReadComplementBlock(complementNode.Label, bySubject, out NamedNode? complemented, out List<Quad>? blockTriples))
            {
                atoms.Add(new RefutationAtom(quad, new Quad(member, new NamedNode(Vocabulary.Rdf.Type), complemented, Graph: null)));
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
                && TryReadAllDifferentBlock(allDifferentNode.Label, bySubject, out List<NamedNode>? members, out List<Quad>? memberTriples))
            {
                for(int i = 0; i < members.Count; i++)
                {
                    for(int j = i + 1; j < members.Count; j++)
                    {
                        Quad asserted = new(members[i], new NamedNode(OwlVocabulary.DifferentFrom), members[j], Graph: null);
                        atoms.Add(new RefutationAtom(asserted, new Quad(members[i], new NamedNode(OwlVocabulary.SameAs), members[j], Graph: null)));
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
    /// <param name="blockTriples">The block's triples for removal.</param>
    /// <returns><see langword="true"/> for a well-formed complement block.</returns>
    private static bool TryReadComplementBlock(
        Utf8String label,
        Dictionary<Utf8String, List<Quad>> bySubject,
        [NotNullWhen(true)] out NamedNode? complemented,
        [NotNullWhen(true)] out List<Quad>? blockTriples)
    {
        complemented = null;
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
