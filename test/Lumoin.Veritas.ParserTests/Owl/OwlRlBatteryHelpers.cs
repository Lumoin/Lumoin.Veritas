using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl.Rl;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The shared term-minting and RDF-collection helpers the OWL 2 RL
/// differential batteries build their inputs from: minting IRIs, blank nodes
/// and typed literals into a <see cref="TermDictionary"/>, assembling encoded
/// triples, and building <c>rdf:first</c>/<c>rdf:rest</c> collections for the
/// list-bearing axioms (property chains, intersections, disjointness lists).
/// </summary>
/// <remarks>
/// These mirror the private helpers of the semi-naive Phase-0 battery
/// (<c>OwlRlSemiNaiveDifferentialTests</c>) as an accessible, shared home so
/// the add/retract battery reuses them rather than reinventing them; the
/// Phase-0 battery keeps its own copies untouched.
/// </remarks>
internal static class OwlRlBatteryHelpers
{
    /// <summary>The example namespace every minted term shares.</summary>
    public const string Example = "http://example.org/";

    /// <summary>Mints an IRI in the example namespace.</summary>
    /// <param name="dictionary">The dictionary the term enters.</param>
    /// <param name="local">The local name.</param>
    /// <returns>The minted identifier.</returns>
    public static TermId Mint(TermDictionary dictionary, string local)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(Example + local)));
    }

    /// <summary>Mints an IRI by its full string.</summary>
    /// <param name="dictionary">The dictionary the term enters.</param>
    /// <param name="iri">The full IRI.</param>
    /// <returns>The minted identifier.</returns>
    public static TermId Named(TermDictionary dictionary, string iri)
    {
        return dictionary.GetOrAdd(new NamedNode(Utf8Strings.From(iri)));
    }

    /// <summary>Mints a blank node by label.</summary>
    /// <param name="dictionary">The dictionary the node enters.</param>
    /// <param name="label">The blank-node label.</param>
    /// <returns>The minted identifier.</returns>
    public static TermId Blank(TermDictionary dictionary, string label)
    {
        return dictionary.GetOrAdd(new BlankNode(Utf8Strings.From(label)));
    }

    /// <summary>Mints a typed literal.</summary>
    /// <param name="dictionary">The dictionary the literal enters.</param>
    /// <param name="lexical">The literal's lexical form.</param>
    /// <param name="datatype">The datatype IRI.</param>
    /// <returns>The minted identifier.</returns>
    public static TermId Literal(TermDictionary dictionary, string lexical, string datatype)
    {
        return dictionary.GetOrAdd((RdfTerm)new Literal(Utf8Strings.From(lexical), new NamedNode(Utf8Strings.From(datatype))));
    }

    /// <summary>An encoded triple from three term identifiers.</summary>
    /// <param name="subject">The subject identifier.</param>
    /// <param name="predicate">The predicate identifier.</param>
    /// <param name="object">The object identifier.</param>
    /// <returns>The encoded triple.</returns>
    public static EncodedTriple Triple(TermId subject, TermId predicate, TermId @object)
    {
        return EncodedTriple.FromEncoded(subject.Encoded, predicate.Encoded, @object.Encoded);
    }

    /// <summary>Builds an RDF collection over the given members and returns its head node.</summary>
    /// <param name="triples">The triple list the list structure appends to.</param>
    /// <param name="dictionary">The dictionary the list nodes mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="members">The list members, in order.</param>
    /// <param name="label">A unique label distinguishing this list's blank nodes.</param>
    /// <returns>The head node of the collection.</returns>
    public static TermId AddList(List<EncodedTriple> triples, TermDictionary dictionary, OwlRlTerms terms, TermId[] members, string label)
    {
        TermId head = terms.Nil;
        for(int i = members.Length - 1; i >= 0; i--)
        {
            TermId node = Blank(dictionary, $"list-{label}-{i}");
            triples.Add(Triple(node, terms.First, members[i]));
            triples.Add(Triple(node, terms.Rest, head));
            head = node;
        }

        return head;
    }

    /// <summary>Adds a propertyChainAxiom(super, list) with a hand-built rdf:first/rest list over the given properties.</summary>
    /// <param name="triples">The triple list the axiom and its list structure append to.</param>
    /// <param name="dictionary">The dictionary the list nodes mint through.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="super">The chain's super-property.</param>
    /// <param name="chain">The chain's ordered property members.</param>
    /// <param name="label">A unique label distinguishing this list's blank nodes.</param>
    public static void AddChainAxiom(List<EncodedTriple> triples, TermDictionary dictionary, OwlRlTerms terms, TermId super, TermId[] chain, string label)
    {
        TermId head = AddList(triples, dictionary, terms, chain, label);
        triples.Add(Triple(super, terms.PropertyChainAxiom, head));
    }
}
