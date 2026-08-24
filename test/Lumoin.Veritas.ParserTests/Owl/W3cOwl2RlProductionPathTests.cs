using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Owl;
using Lumoin.Veritas.Owl.Rl;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Xml;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Runs the RL-marked arms of the W3C OWL 2 conformance corpus through the reasoned
/// MUTABLE engine's production maintenance path, the facade peer of
/// <see cref="W3cOwl2RlTests"/>. The open path is a remat by construction, so the
/// shape that actually exercises maintenance splits the premise — schema-shaped and
/// blank-bearing quads open the engine, ground assertion quads commit as maintained
/// applies — and scrambles one committed assertion (retract then re-add) to drive the
/// retract path. It then asserts the consistency/inconsistency verdict through
/// <see cref="VeritasEngine.ReasoningProvenance"/> against the corpus expectation, and
/// for the entailment families EMBEDS the conclusion over the SERVED store's decoded
/// contents — the serving certification: the served store must answer the
/// conclusion's graph embedding exactly as a from-scratch <see cref="OwlRlClosure.Compute"/>
/// closure does, so the maintained serving reproduces the closure. The
/// recompute-based <see cref="OwlRlEntailment.Entails"/> — which reads no serving and
/// decides the refutation/contrapositive forms — is the corpus-expectation comparand.
/// </summary>
/// <remarks>
/// <para>
/// <b>The split.</b> A quad opens the engine when it carries a blank node (so every
/// blank node's whole neighbourhood stays in ONE scope, preserving co-reference the
/// commit path's fresh-scope blanks would break) or is schema/structural (a reserved
/// <c>owl:</c>/<c>rdfs:</c>/<c>rdf:</c> predicate, or an <c>rdf:type</c> into a
/// reserved class). A bounded number of the remaining ground assertion quads — plain
/// user typings and object/data-property assertions, all blank-free — commit through
/// <c>INSERT DATA</c>. Named nodes co-reference globally by IRI across the open and
/// the commit, so moving any blank-free quad is always sound; the net is the full
/// premise, and only the FINAL verdict (base = full premise) is asserted, so the split
/// point is free. A premise with no ground assertion quad opens whole (no maintained
/// commit); those are recorded as comparand-only coverage by the reduced runtime.
/// </para>
/// <para>
/// <b>Gating.</b> The full arm runs always-on for every RL-marked case: measured at
/// implementation the two data-driven methods total ~2.5s over 74 cases, well inside
/// suite-time discipline, so no environment gate or pinned subset is needed. Should the
/// corpus ever grow past that budget, the split arm is the natural gate boundary.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cOwl2RlProductionPathTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The most ground assertion quads a premise commits as maintained applies; the rest open the engine.</summary>
    private const int MaxCommitQuads = 4;

    /// <summary>The reserved vocabulary namespaces whose predicates and typed objects mark a quad schema/structural.</summary>
    private static string[] ReservedNamespaces { get; } =
    [
        "http://www.w3.org/2002/07/owl#",
        "http://www.w3.org/2000/01/rdf-schema#",
        "http://www.w3.org/1999/02/22-rdf-syntax-ns#",
    ];

    /// <summary>The <c>rdf:type</c> IRI.</summary>
    private static Utf8String RdfType { get; } = Utf8Strings.From("http://www.w3.org/1999/02/22-rdf-syntax-ns#type");

    /// <summary>Runs one approved-status, RL-marked OWL 2 test case through the production maintenance path.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous run.</returns>
    [TestMethod]
    [Owl2ManifestData("approved", "all.rdf", Owl2TestRemit.RlMarked)]
    public async Task RunApproved(Owl2TestCase testCase)
    {
        await RunAndAssertAsync(testCase).ConfigureAwait(false);
    }

    /// <summary>Runs one proposed-status, RL-marked OWL 2 test case through the production maintenance path.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous run.</returns>
    [TestMethod]
    [Owl2ManifestData("proposed", "all.rdf", Owl2TestRemit.RlMarked)]
    public async Task RunProposed(Owl2TestCase testCase)
    {
        await RunAndAssertAsync(testCase).ConfigureAwait(false);
    }

    /// <summary>Opens the split premise on a reasoned mutable engine, commits the remainder, scrambles one assertion, and asserts the verdict and served entailment.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous run.</returns>
    private async Task RunAndAssertAsync(Owl2TestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);
        CancellationToken cancellationToken = TestContext.CancellationToken;

        bool isEntailment = testCase.Kinds.Contains("PositiveEntailmentTest") || testCase.Kinds.Contains("NegativeEntailmentTest");
        bool directSemanticsOnly = isEntailment && !testCase.Semantics.Contains("RDF-BASED");

        List<Quad>? maybePremise = LoadQuads(testCase, testCase.RdfXmlPremise, testCase.FunctionalPremise);
        if(maybePremise is not List<Quad> premiseQuads)
        {
            Assert.Fail($"{testCase.Identifier}: the test declares no premise document in a syntax the harness reads.");

            return;
        }

        premiseQuads = Owl2ImportResolver.Expand(testCase, premiseQuads);

        (List<Quad> openQuads, List<Quad> commitQuads) = Split(premiseQuads);

        VeritasEngine database = await VeritasEngine
            .OpenMutableAsync(ToDataTriples(openQuads), cancellationToken: cancellationToken).ConfigureAwait(false);
        await using var scope = database.ConfigureAwait(false);

        //Commit the ground assertions as maintained applies, then scramble one — retract
        //and re-add it — to drive the retract path through the production commit choke point.
        if(commitQuads.Count > 0)
        {
            await database.UpdateAsync(Utf8Strings.From(UpdateText("INSERT DATA", commitQuads)), cancellationToken: cancellationToken).ConfigureAwait(false);

            Quad scramble = commitQuads[0];
            await database.UpdateAsync(Utf8Strings.From(UpdateText("DELETE DATA", [scramble])), cancellationToken: cancellationToken).ConfigureAwait(false);
            await database.UpdateAsync(Utf8Strings.From(UpdateText("INSERT DATA", [scramble])), cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        ReasoningProvenance? provenance = database.ReasoningProvenance;
        Assert.IsNotNull(provenance, $"{testCase.Identifier}: a reasoned mutable engine surfaces the reasoning provenance.");

        TermDictionary dictionary = new();
        List<EncodedTriple> encoded = Encode(premiseQuads, dictionary);
        OwlRlTerms terms = new(dictionary);
        OwlRlDatatypeOracle oracle = OwlRlDatatypeOracles.FromDictionary(dictionary);
        OwlRlResult computeResult = OwlRlClosure.Compute(encoded, terms, oracle, cancellationToken: cancellationToken);

        if(testCase.Kinds.Contains("InconsistencyTest"))
        {
            Assert.IsFalse(computeResult.IsConsistent, $"{testCase.Identifier}: the from-scratch RL closure derives the inconsistency (comparand).");
            Assert.IsFalse(provenance.IsConsistent, $"{testCase.Identifier}: the served generation surfaces the inconsistency.");

            return;
        }

        if(testCase.Kinds.Contains("ConsistencyTest"))
        {
            Assert.IsTrue(computeResult.IsConsistent, $"{testCase.Identifier}: the from-scratch RL closure agrees the premise is consistent (comparand).");
            Assert.IsTrue(provenance.IsConsistent, $"{testCase.Identifier}: the served generation is consistent, but rule {provenance.InconsistencyRule} withdrew the overlay.");
        }

        List<Quad> servedQuads = await ProductionPathServedReader.ReadServedQuadsAsync(database, cancellationToken).ConfigureAwait(false);
        List<Quad> computeClosure = DecodeUnion(premiseQuads, computeResult, dictionary);

        if(testCase.Kinds.Contains("PositiveEntailmentTest")
            && LoadQuads(testCase, testCase.RdfXmlConclusion, testCase.FunctionalConclusion) is List<Quad> conclusionQuads)
        {
            AssertEntailmentFamily(testCase, directSemanticsOnly, conclusionQuads, servedQuads, computeClosure, premiseQuads, dictionary, terms, oracle, positive: true, cancellationToken);
        }

        if(testCase.Kinds.Contains("NegativeEntailmentTest")
            && LoadQuads(testCase, testCase.RdfXmlNonConclusion, testCase.FunctionalNonConclusion) is List<Quad> nonConclusionQuads)
        {
            AssertEntailmentFamily(testCase, directSemanticsOnly, nonConclusionQuads, servedQuads, computeClosure, premiseQuads, dictionary, terms, oracle, positive: false, cancellationToken);
        }
    }

    /// <summary>Asserts one entailment family: the served store answers the conclusion embedding exactly like the from-scratch closure (the serving certification), and the recompute comparand agrees with the corpus expectation.</summary>
    /// <param name="testCase">The test case, for messages.</param>
    /// <param name="directSemanticsOnly">Whether the conclusion's annotations are projected away.</param>
    /// <param name="conclusion">The conclusion or non-conclusion graph.</param>
    /// <param name="servedQuads">The served store's decoded contents.</param>
    /// <param name="computeClosure">The from-scratch RL closure over the premise.</param>
    /// <param name="premiseQuads">The premise graph, for the recompute comparand.</param>
    /// <param name="dictionary">The term dictionary the premise encodes with.</param>
    /// <param name="terms">The resolved RL vocabulary.</param>
    /// <param name="oracle">The datatype oracle.</param>
    /// <param name="positive">Whether this is a positive entailment (the conclusion follows) or negative (it must not).</param>
    /// <param name="cancellationToken">A token that aborts the recompute.</param>
    private static void AssertEntailmentFamily(
        Owl2TestCase testCase,
        bool directSemanticsOnly,
        List<Quad> conclusion,
        List<Quad> servedQuads,
        List<Quad> computeClosure,
        List<Quad> premiseQuads,
        TermDictionary dictionary,
        OwlRlTerms terms,
        OwlRlDatatypeOracle oracle,
        bool positive,
        CancellationToken cancellationToken)
    {
        List<Quad> pattern = directSemanticsOnly ? WithoutAnnotations(conclusion) : conclusion;

        bool embedsServed = OwlGraphEntailment.Embeds(pattern, servedQuads, OwlComprehension.InformativeConditions);
        bool embedsCompute = OwlGraphEntailment.Embeds(pattern, computeClosure, OwlComprehension.InformativeConditions);

        //The served store answers the conclusion's graph embedding exactly like the
        //from-scratch closure — the maintained serving reproduces the closure, positive-embedding
        //and refutation-only (both false) cases alike.
        Assert.AreEqual(
            embedsCompute,
            embedsServed,
            $"{testCase.Identifier}: the served store must answer the conclusion embedding exactly like the from-scratch closure.");

        //The recompute-based entailment — refutation-inclusive, serving-independent — is the corpus
        //expectation comparand.
        bool entailsRecompute = OwlRlEntailment.Entails(
            premiseQuads, pattern, dictionary, terms, oracle, OwlComprehension.InformativeConditions, cancellationToken: cancellationToken);

        if(positive)
        {
            Assert.IsTrue(entailsRecompute, $"{testCase.Identifier}: the conclusion follows from the RL closure (comparand).");
        }
        else
        {
            Assert.IsFalse(entailsRecompute, $"{testCase.Identifier}: the non-conclusion must not follow from the RL closure (comparand).");
        }
    }

    /// <summary>Splits a premise into the quads that open the engine (schema-shaped or blank-bearing) and the bounded ground assertion quads that commit as maintained applies.</summary>
    /// <param name="premise">The premise graph.</param>
    /// <returns>The open quads and the commit quads.</returns>
    private static (List<Quad> Open, List<Quad> Commit) Split(List<Quad> premise)
    {
        List<Quad> open = [];
        List<Quad> commit = [];
        foreach(Quad quad in premise)
        {
            if(commit.Count < MaxCommitQuads && IsGroundAssertion(quad))
            {
                commit.Add(quad);
            }
            else
            {
                open.Add(quad);
            }
        }

        return (open, commit);
    }

    /// <summary>Whether a quad is a blank-free assertion — a user typing or a user object/data-property assertion — safe to commit and scramble.</summary>
    /// <param name="quad">The premise quad.</param>
    /// <returns><see langword="true"/> when the quad is a ground assertion.</returns>
    private static bool IsGroundAssertion(Quad quad)
    {
        if(quad.Subject is BlankNode || quad.Object is BlankNode)
        {
            return false;
        }

        if(quad.Predicate.Iri == RdfType)
        {
            //A user-class typing is an assertion; a typing into a reserved class (owl:Class,
            //owl:ObjectProperty, ...) is a declaration and opens the engine.
            return quad.Object is NamedNode named && !IsReserved(named.Iri);
        }

        //A user-property assertion is ground; a reserved predicate is schema/structural.
        return !IsReserved(quad.Predicate.Iri);
    }

    /// <summary>Whether an IRI is in a reserved vocabulary namespace.</summary>
    /// <param name="iri">The IRI to test.</param>
    /// <returns><see langword="true"/> when the IRI is in the OWL, RDFS, or RDF namespace.</returns>
    private static bool IsReserved(Utf8String iri)
    {
        string text = iri.ToString();
        foreach(string reserved in ReservedNamespaces)
        {
            if(text.StartsWith(reserved, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Renders an <c>INSERT DATA</c>/<c>DELETE DATA</c> operation over blank-free quads.</summary>
    /// <param name="keyword">The operation keyword — <c>INSERT DATA</c> or <c>DELETE DATA</c>.</param>
    /// <param name="quads">The blank-free quads.</param>
    /// <returns>The update text.</returns>
    private static string UpdateText(string keyword, IReadOnlyList<Quad> quads)
    {
        StringBuilder builder = new();
        builder.Append(keyword).Append(" { ");
        foreach(Quad quad in quads)
        {
            builder
                .Append(ProductionPathServedReader.SparqlTerm(quad.Subject)).Append(' ')
                .Append(ProductionPathServedReader.SparqlTerm(quad.Predicate)).Append(' ')
                .Append(ProductionPathServedReader.SparqlTerm(quad.Object)).Append(" . ");
        }

        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>Projects a premise's quads onto the data triples a mutable-engine open takes.</summary>
    /// <param name="quads">The quads.</param>
    /// <returns>The data triples.</returns>
    private static List<DataTriple> ToDataTriples(List<Quad> quads)
    {
        List<DataTriple> triples = new(quads.Count);
        foreach(Quad quad in quads)
        {
            triples.Add(new DataTriple(quad.Subject, quad.Predicate, quad.Object));
        }

        return triples;
    }

    /// <summary>Decodes the base premise united with the closure's derived set into a quad list, for the embedding checker.</summary>
    /// <param name="premise">The base premise quads.</param>
    /// <param name="result">The closure result whose derived set to decode.</param>
    /// <param name="dictionary">The dictionary the derived triples encode with.</param>
    /// <returns>The premise united with the decoded derived set.</returns>
    private static List<Quad> DecodeUnion(List<Quad> premise, OwlRlResult result, TermDictionary dictionary)
    {
        List<Quad> closure = [.. premise];
        foreach(EncodedTriple triple in result.Derived)
        {
            closure.Add(new Quad(
                dictionary.Resolve(triple.Subject.Encoded),
                (NamedNode)dictionary.Resolve(triple.Predicate.Encoded),
                dictionary.Resolve(triple.Object.Encoded),
                Graph: null));
        }

        return closure;
    }

    /// <summary>Loads a document role as quads: RDF/XML parses directly; functional syntax reads into structural form and serialises through the forward RDF mapping. <see langword="null"/> when the role has no readable document.</summary>
    /// <param name="testCase">The test case, for messages.</param>
    /// <param name="rdfXml">The RDF/XML document bytes, or <see langword="null"/>.</param>
    /// <param name="functional">The functional-syntax document text, or <see langword="null"/>.</param>
    /// <returns>The parsed quads, or <see langword="null"/> when neither document is present.</returns>
    private static List<Quad>? LoadQuads(Owl2TestCase testCase, Utf8String? rdfXml, string? functional)
    {
        if(rdfXml is { } xml)
        {
            return ParseDocument(testCase, xml);
        }

        if(functional is string text)
        {
            Lumoin.Veritas.Owl.Structural.OwlOntologyDocument document = Lumoin.Veritas.Owl.Functional.OwlFunctionalSyntaxReader.Read(text);
            Assert.IsFalse(document.Diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as functional syntax; the test cannot be set up.");

            return Lumoin.Veritas.Owl.Structural.OwlStructuralToRdf.ToQuads(document);
        }

        return null;
    }

    /// <summary>Parses an RDF/XML document to quads against the test's base IRI.</summary>
    /// <param name="testCase">The test case, for the base IRI and messages.</param>
    /// <param name="document">The RDF/XML document bytes.</param>
    /// <returns>The parsed quads.</returns>
    private static List<Quad> ParseDocument(Owl2TestCase testCase, Utf8String document)
    {
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [.. RdfXmlReader.Read(document.Memory, diagnostics, baseIri: Utf8Strings.From(testCase.Uri.AbsoluteUri))];
        Assert.IsFalse(diagnostics.HasErrors, $"{testCase.Identifier}: a test document did not parse as RDF/XML; the test cannot be set up.");

        return quads;
    }

    /// <summary>Encodes quads into a dictionary as encoded triples.</summary>
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

    /// <summary>The Direct Semantics' logical projection of a conclusion graph: its annotation assertions carry no logical meaning, so they are set aside.</summary>
    /// <param name="conclusion">The conclusion graph.</param>
    /// <returns>The conclusion with annotation assertions removed.</returns>
    private static List<Quad> WithoutAnnotations(List<Quad> conclusion)
    {
        IReadOnlySet<Utf8String> annotationProperties =
            Lumoin.Veritas.Owl.Structural.OwlRdfMapper.Map(conclusion).DeclaredAnnotationProperties;

        List<Quad> logical = new(conclusion.Count);
        foreach(Quad quad in conclusion)
        {
            if(!annotationProperties.Contains(quad.Predicate.Iri))
            {
                logical.Add(quad);
            }
        }

        return logical;
    }
}
