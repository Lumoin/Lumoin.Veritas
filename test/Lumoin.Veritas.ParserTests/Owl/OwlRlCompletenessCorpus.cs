using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The shared corpus and traced-closure scaffolding the deletion-coverage and
/// rederivability-coverage sweeps run over: the four randomized battery pools
/// taken as whole bases, the four hand-built adversarial op-0 shapes, and the
/// W3C OWL 2 RL-marked premises, each carried as a base with the resolved
/// vocabulary and datatype oracle it was minted against, plus the traced naive
/// materialization the sweeps read their derivations from.
/// </summary>
/// <remarks>
/// The pool and shape bases mirror the add/retract battery's fixtures so the
/// coverage sweeps and the differential battery exercise the same inputs; the
/// W3C loader mirrors the RL conformance arm's premise-encode path (imports
/// expansion included) so the sweeps reason over the same imports closure.
/// </remarks>
internal static class OwlRlCompletenessCorpus
{
    /// <summary>The <c>xsd:integer</c> datatype IRI the equality pool's numeric literals carry.</summary>
    private const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>
    /// One corpus input: a base to close over together with the vocabulary and
    /// datatype oracle it was minted against, both threaded identically into
    /// the traced closure and the maintained engine.
    /// </summary>
    /// <param name="Identifier">The input's name, carried into a sweep's assertion messages.</param>
    /// <param name="Base">The base triples, schema statements included.</param>
    /// <param name="Terms">The resolved RL vocabulary.</param>
    /// <param name="Oracle">The datatype oracle, or <see langword="default"/> to disable the <c>dt-*</c> falsities.</param>
    internal sealed record CorpusInput(
        string Identifier,
        IReadOnlyList<EncodedTriple> Base,
        OwlRlTerms Terms,
        OwlRlDatatypeOracle Oracle);

    /// <summary>Builds the schema-closure pool as a whole base: subsumptions, an equivalentClass two-cycle, subproperties, domain/range, class declarations and instance edges.</summary>
    /// <returns>The corpus input over the schema pool.</returns>
    internal static CorpusInput SchemaPool()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a0 = OwlRlBatteryHelpers.Mint(dictionary, "sc0");
        TermId a1 = OwlRlBatteryHelpers.Mint(dictionary, "sc1");
        TermId a2 = OwlRlBatteryHelpers.Mint(dictionary, "sc2");
        TermId a3 = OwlRlBatteryHelpers.Mint(dictionary, "sc3");
        TermId a4 = OwlRlBatteryHelpers.Mint(dictionary, "sc4");
        TermId p0 = OwlRlBatteryHelpers.Mint(dictionary, "sp0");
        TermId p1 = OwlRlBatteryHelpers.Mint(dictionary, "sp1");
        TermId i0 = OwlRlBatteryHelpers.Mint(dictionary, "si0");
        TermId i1 = OwlRlBatteryHelpers.Mint(dictionary, "si1");
        TermId i2 = OwlRlBatteryHelpers.Mint(dictionary, "si2");

        IReadOnlyList<EncodedTriple> pool =
        [
            OwlRlBatteryHelpers.Triple(a0, terms.EquivalentClass, a1),
            OwlRlBatteryHelpers.Triple(a1, terms.EquivalentClass, a0),
            OwlRlBatteryHelpers.Triple(a2, terms.SubClassOf, a3),
            OwlRlBatteryHelpers.Triple(a3, terms.SubClassOf, a4),
            OwlRlBatteryHelpers.Triple(a1, terms.SubClassOf, a2),
            OwlRlBatteryHelpers.Triple(p0, terms.SubPropertyOf, p1),
            OwlRlBatteryHelpers.Triple(p1, terms.EquivalentProperty, p0),
            OwlRlBatteryHelpers.Triple(p0, terms.Domain, a0),
            OwlRlBatteryHelpers.Triple(p0, terms.Range, a2),
            OwlRlBatteryHelpers.Triple(a0, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(a3, terms.Type, terms.ClassTerm),
            OwlRlBatteryHelpers.Triple(i0, terms.Type, a0),
            OwlRlBatteryHelpers.Triple(i1, terms.Type, a2),
            OwlRlBatteryHelpers.Triple(i0, p0, i1),
            OwlRlBatteryHelpers.Triple(i2, p1, i0),
            OwlRlBatteryHelpers.Triple(i1, terms.Type, a3),
        ];

        return new CorpusInput("schema-pool", pool, terms, default);
    }

    /// <summary>Builds the property-characteristic pool as a whole base: characteristics some of which drive inconsistency, named individuals and edges.</summary>
    /// <returns>The corpus input over the characteristic pool.</returns>
    internal static CorpusInput CharacteristicPool()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId cp0 = OwlRlBatteryHelpers.Mint(dictionary, "cp0");
        TermId cp1 = OwlRlBatteryHelpers.Mint(dictionary, "cp1");
        TermId cp2 = OwlRlBatteryHelpers.Mint(dictionary, "cp2");
        TermId n0 = OwlRlBatteryHelpers.Mint(dictionary, "cn0");
        TermId n1 = OwlRlBatteryHelpers.Mint(dictionary, "cn1");
        TermId n2 = OwlRlBatteryHelpers.Mint(dictionary, "cn2");
        TermId n3 = OwlRlBatteryHelpers.Mint(dictionary, "cn3");
        TermId n4 = OwlRlBatteryHelpers.Mint(dictionary, "cn4");

        IReadOnlyList<EncodedTriple> pool =
        [
            OwlRlBatteryHelpers.Triple(cp0, terms.Type, terms.TransitiveProperty),
            OwlRlBatteryHelpers.Triple(cp0, terms.Type, terms.IrreflexiveProperty),
            OwlRlBatteryHelpers.Triple(cp1, terms.Type, terms.SymmetricProperty),
            OwlRlBatteryHelpers.Triple(cp1, terms.Type, terms.AsymmetricProperty),
            OwlRlBatteryHelpers.Triple(cp2, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(cp2, terms.Type, terms.InverseFunctionalProperty),
            OwlRlBatteryHelpers.Triple(n0, terms.Type, terms.NamedIndividual),
            OwlRlBatteryHelpers.Triple(n2, terms.Type, terms.NamedIndividual),
            OwlRlBatteryHelpers.Triple(n0, cp0, n1),
            OwlRlBatteryHelpers.Triple(n1, cp0, n2),
            OwlRlBatteryHelpers.Triple(n2, cp0, n0),
            OwlRlBatteryHelpers.Triple(n0, cp1, n3),
            OwlRlBatteryHelpers.Triple(n2, cp2, n3),
            OwlRlBatteryHelpers.Triple(n2, cp2, n4),
            OwlRlBatteryHelpers.Triple(n1, cp0, n1),
        ];

        return new CorpusInput("characteristic-pool", pool, terms, default);
    }

    /// <summary>Builds the inverse/chain pool as a whole base: inverses, equivalences, subproperties, a 2-link chain and the transitivity chain with hand-built lists, and edges.</summary>
    /// <returns>The corpus input over the inverse/chain pool.</returns>
    internal static CorpusInput InverseChainPool()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId ip0 = OwlRlBatteryHelpers.Mint(dictionary, "ip0");
        TermId ip1 = OwlRlBatteryHelpers.Mint(dictionary, "ip1");
        TermId ip2 = OwlRlBatteryHelpers.Mint(dictionary, "ip2");
        TermId ip3 = OwlRlBatteryHelpers.Mint(dictionary, "ip3");
        TermId ip5 = OwlRlBatteryHelpers.Mint(dictionary, "ip5");
        TermId m0 = OwlRlBatteryHelpers.Mint(dictionary, "im0");
        TermId m1 = OwlRlBatteryHelpers.Mint(dictionary, "im1");
        TermId m2 = OwlRlBatteryHelpers.Mint(dictionary, "im2");
        TermId m3 = OwlRlBatteryHelpers.Mint(dictionary, "im3");

        List<EncodedTriple> pool =
        [
            OwlRlBatteryHelpers.Triple(ip0, terms.InverseOf, ip1),
            OwlRlBatteryHelpers.Triple(ip0, terms.EquivalentProperty, ip2),
            OwlRlBatteryHelpers.Triple(ip3, terms.SubPropertyOf, ip0),
            OwlRlBatteryHelpers.Triple(m0, ip0, m1),
            OwlRlBatteryHelpers.Triple(m1, ip1, m2),
            OwlRlBatteryHelpers.Triple(m0, ip5, m1),
            OwlRlBatteryHelpers.Triple(m1, ip5, m2),
            OwlRlBatteryHelpers.Triple(m2, ip5, m3),
            OwlRlBatteryHelpers.Triple(m0, ip3, m2),
        ];

        OwlRlBatteryHelpers.AddChainAxiom(pool, dictionary, terms, ip5, [ip5, ip5], "ppchain");
        OwlRlBatteryHelpers.AddChainAxiom(pool, dictionary, terms, ip3, [ip0, ip1], "twolink");

        return new CorpusInput("inverse-chain-pool", pool, terms, default);
    }

    /// <summary>Builds the equality-churn pool as a whole base: sameAs chains and a clique with per-entity data, differentFrom, a functional-property merge and numeric literals under the dictionary oracle.</summary>
    /// <returns>The corpus input over the equality pool, with the dictionary datatype oracle.</returns>
    internal static CorpusInput EqualityPool()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId e0 = OwlRlBatteryHelpers.Mint(dictionary, "eq0");
        TermId e1 = OwlRlBatteryHelpers.Mint(dictionary, "eq1");
        TermId e2 = OwlRlBatteryHelpers.Mint(dictionary, "eq2");
        TermId e3 = OwlRlBatteryHelpers.Mint(dictionary, "eq3");
        TermId e4 = OwlRlBatteryHelpers.Mint(dictionary, "eq4");
        TermId ep0 = OwlRlBatteryHelpers.Mint(dictionary, "ep0");
        TermId ep1 = OwlRlBatteryHelpers.Mint(dictionary, "ep1");
        TermId d0 = OwlRlBatteryHelpers.Mint(dictionary, "ed0");
        TermId d1 = OwlRlBatteryHelpers.Mint(dictionary, "ed1");
        TermId one = OwlRlBatteryHelpers.Literal(dictionary, "1", XsdInteger);
        TermId two = OwlRlBatteryHelpers.Literal(dictionary, "2", XsdInteger);

        IReadOnlyList<EncodedTriple> pool =
        [
            OwlRlBatteryHelpers.Triple(e0, terms.SameAs, e1),
            OwlRlBatteryHelpers.Triple(e1, terms.SameAs, e2),
            OwlRlBatteryHelpers.Triple(e3, terms.SameAs, e4),
            OwlRlBatteryHelpers.Triple(e0, ep0, d0),
            OwlRlBatteryHelpers.Triple(e1, ep1, d1),
            OwlRlBatteryHelpers.Triple(e2, ep0, d0),
            OwlRlBatteryHelpers.Triple(e0, terms.DifferentFrom, e2),
            OwlRlBatteryHelpers.Triple(ep0, terms.Type, terms.FunctionalProperty),
            OwlRlBatteryHelpers.Triple(e3, ep0, d0),
            OwlRlBatteryHelpers.Triple(e3, ep0, d1),
            OwlRlBatteryHelpers.Triple(one, terms.SameAs, e4),
            OwlRlBatteryHelpers.Triple(e4, terms.SameAs, two),
        ];

        return new CorpusInput("equality-pool", pool, terms, OwlRlDatatypeOracles.FromDictionary(dictionary));
    }

    /// <summary>Builds the CyclicOrphan op-0 base: a transitive property over a two-cycle with external support.</summary>
    /// <returns>The corpus input over the CyclicOrphan base.</returns>
    internal static CorpusInput CyclicOrphan()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId p = OwlRlBatteryHelpers.Mint(dictionary, "p");
        TermId a = OwlRlBatteryHelpers.Mint(dictionary, "a");
        TermId b = OwlRlBatteryHelpers.Mint(dictionary, "b");
        TermId s = OwlRlBatteryHelpers.Mint(dictionary, "s");

        IReadOnlyList<EncodedTriple> shape =
        [
            OwlRlBatteryHelpers.Triple(p, terms.Type, terms.TransitiveProperty),
            OwlRlBatteryHelpers.Triple(a, p, b),
            OwlRlBatteryHelpers.Triple(b, p, a),
            OwlRlBatteryHelpers.Triple(s, p, a),
        ];

        return new CorpusInput("cyclic-orphan", shape, terms, default);
    }

    /// <summary>Builds the AlternateDerivation op-0 base: a typing with two independent cax-sco derivations.</summary>
    /// <returns>The corpus input over the AlternateDerivation base.</returns>
    internal static CorpusInput AlternateDerivation()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classA = OwlRlBatteryHelpers.Mint(dictionary, "A");
        TermId classB = OwlRlBatteryHelpers.Mint(dictionary, "B");
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        IReadOnlyList<EncodedTriple> shape =
        [
            OwlRlBatteryHelpers.Triple(classA, terms.SubClassOf, classC),
            OwlRlBatteryHelpers.Triple(classB, terms.SubClassOf, classC),
            OwlRlBatteryHelpers.Triple(x, terms.Type, classA),
            OwlRlBatteryHelpers.Triple(x, terms.Type, classB),
        ];

        return new CorpusInput("alternate-derivation", shape, terms, default);
    }

    /// <summary>Builds the SameAsUnMerge op-0 base: two two-member cliques bridged into one four-member congruence class with per-entity data.</summary>
    /// <returns>The corpus input over the SameAsUnMerge base.</returns>
    internal static CorpusInput SameAsUnMerge()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId a1 = OwlRlBatteryHelpers.Mint(dictionary, "a1");
        TermId a2 = OwlRlBatteryHelpers.Mint(dictionary, "a2");
        TermId b1 = OwlRlBatteryHelpers.Mint(dictionary, "b1");
        TermId b2 = OwlRlBatteryHelpers.Mint(dictionary, "b2");
        TermId prop = OwlRlBatteryHelpers.Mint(dictionary, "P");
        TermId u = OwlRlBatteryHelpers.Mint(dictionary, "u");
        TermId w = OwlRlBatteryHelpers.Mint(dictionary, "w");

        IReadOnlyList<EncodedTriple> shape =
        [
            OwlRlBatteryHelpers.Triple(a1, terms.SameAs, a2),
            OwlRlBatteryHelpers.Triple(b1, terms.SameAs, b2),
            OwlRlBatteryHelpers.Triple(a2, terms.SameAs, b1),
            OwlRlBatteryHelpers.Triple(a1, prop, u),
            OwlRlBatteryHelpers.Triple(b2, prop, w),
        ];

        return new CorpusInput("same-as-unmerge", shape, terms, default);
    }

    /// <summary>Builds the FalsityRetract consistent base — the post-first-retract shape where the disjointness clash is gone: a class typed into one of two disjoint classes with a subsumption.</summary>
    /// <returns>The corpus input over the consistent FalsityRetract base.</returns>
    internal static CorpusInput FalsityRetractConsistent()
    {
        TermDictionary dictionary = new();
        OwlRlTerms terms = new(dictionary);
        TermId classC = OwlRlBatteryHelpers.Mint(dictionary, "C");
        TermId classD = OwlRlBatteryHelpers.Mint(dictionary, "D");
        TermId classE = OwlRlBatteryHelpers.Mint(dictionary, "E");
        TermId x = OwlRlBatteryHelpers.Mint(dictionary, "x");

        IReadOnlyList<EncodedTriple> shape =
        [
            OwlRlBatteryHelpers.Triple(classC, terms.DisjointWith, classD),
            OwlRlBatteryHelpers.Triple(classC, terms.SubClassOf, classE),
            OwlRlBatteryHelpers.Triple(x, terms.Type, classC),
        ];

        return new CorpusInput("falsity-retract-consistent", shape, terms, default);
    }

    /// <summary>Loads one W3C OWL 2 RL-marked test case's premise as a corpus input over its imports closure, through the same encode path the RL conformance arm uses.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The corpus input over the premise's imports closure, or <see langword="null"/> when the test declares no premise in a readable syntax.</returns>
    internal static CorpusInput? LoadW3c(Owl2TestCase testCase)
    {
        if(LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise) is not List<Quad> premiseQuads)
        {
            return null;
        }

        //The reasoned-over unit is the premise's imports closure: its
        //owl:imports resolve against the test's supplied ontologies.
        List<Quad> expanded = Owl2ImportResolver.Expand(testCase, premiseQuads);

        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = Encode(expanded, dictionary);
        OwlRlTerms terms = new(dictionary);

        return new CorpusInput(testCase.Identifier, encoded, terms, OwlRlDatatypeOracles.FromDictionary(dictionary));
    }

    /// <summary>Runs the naive materialization over an input's base with tracing enabled, capturing every derivation the sweep probes.</summary>
    /// <param name="input">The corpus input.</param>
    /// <param name="cancellationToken">A token that aborts derivation between rounds.</param>
    /// <returns>Whether the base is consistent, and the derivations traced (empty when inconsistent).</returns>
    internal static (bool Consistent, List<InferenceTraceEvent> Events) TraceClosure(CorpusInput input, CancellationToken cancellationToken)
    {
        List<InferenceTraceEvent> events = [];
        OwlRlResult result = OwlRlClosure.ComputeNaive(
            input.Base,
            input.Terms,
            input.Oracle,
            traceHandler: (in InferenceTraceEvent evt) => events.Add(evt),
            timeProvider: VeritasClock.System,
            cancellationToken: cancellationToken);

        return (result.IsConsistent, events);
    }

    /// <summary>The axiomatic datatype-hierarchy seed set an input's closure carries independent of its base — the facts entailed by the empty graph under the input's oracle.</summary>
    /// <param name="input">The corpus input, for its vocabulary and oracle.</param>
    /// <param name="cancellationToken">A token that aborts derivation between rounds.</param>
    /// <returns>The seeded facts.</returns>
    internal static HashSet<EncodedTriple> SeededSet(CorpusInput input, CancellationToken cancellationToken)
    {
        return [.. OwlRlClosure.Compute([], input.Terms, input.Oracle, cancellationToken: cancellationToken).Derived];
    }

    /// <summary>Formats a triple as its three encoded term identifiers for an assertion message.</summary>
    /// <param name="triple">The triple.</param>
    /// <returns>The parenthesized encoded subject, predicate and object.</returns>
    internal static string Describe(EncodedTriple triple)
    {
        return $"({triple.Subject.Encoded} {triple.Predicate.Encoded} {triple.Object.Encoded})";
    }

    /// <summary>Loads a document role as quads: RDF/XML parses directly; functional syntax reads into structural form and serialises through the forward RDF mapping. Null when the role has no readable document.</summary>
    /// <param name="testCase">The test case the document belongs to.</param>
    /// <param name="rdfXml">The RDF/XML document bytes, or <see langword="null"/>.</param>
    /// <param name="functional">The functional-syntax document, or <see langword="null"/>.</param>
    /// <returns>The parsed quads, or <see langword="null"/> when neither syntax is present.</returns>
    private static List<Quad>? LoadQuads(Owl2TestCase testCase, Utf8String? rdfXml, string? functional)
    {
        if(rdfXml is { } xml)
        {
            return ParseDocument(testCase, xml);
        }

        if(functional is string text)
        {
            Lumoin.Veritas.Owl.Structural.OwlOntologyDocument document = Lumoin.Veritas.Owl.Functional.OwlFunctionalSyntaxReader.Read(text);
            Assert.IsFalse(document.Diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as functional syntax; the sweep cannot be set up.");

            return Lumoin.Veritas.Owl.Structural.OwlStructuralToRdf.ToQuads(document);
        }

        return null;
    }

    /// <summary>Parses an RDF/XML document into quads against the test's base IRI.</summary>
    /// <param name="testCase">The test case the document belongs to.</param>
    /// <param name="document">The RDF/XML document bytes.</param>
    /// <returns>The parsed quads.</returns>
    private static List<Quad> ParseDocument(Owl2TestCase testCase, Utf8String document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(document.Memory, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri))];
        Assert.IsFalse(diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as RDF/XML; the sweep cannot be set up.");

        return quads;
    }

    /// <summary>Encodes quads into triples, minting terms into a fresh dictionary.</summary>
    /// <param name="quads">The quads to encode.</param>
    /// <param name="dictionary">The dictionary the terms enter.</param>
    /// <returns>The encoded triples.</returns>
    private static List<EncodedTriple> Encode(List<Quad> quads, TermDictionary dictionary)
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
}
