using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Rdf;
using Lumoin.Veritas.Shacl;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Shacl.Validation.Evaluators;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Dispatches a SHACL <see cref="W3cTestType.ShaclValidate"/> test case:
/// validates the data graph against the shapes graph and compares the produced
/// report to the manifest's inline expected <c>sh:ValidationReport</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="W3cTestRunner"/>, which is reader-delegate driven for the
/// syntax, evaluation, and canonicalization suites, a SHACL test loads shapes
/// and runs the validator, so it has its own runner. The data and shapes
/// graphs are frequently the same file (the leaf declares its data, shapes,
/// manifest entry, and expected report together); the leaf is parsed once and
/// reused when both point at it.
/// </para>
/// <para>
/// <b>Comparison.</b> The produced report is serialized
/// (<see cref="ValidationReportSerializer"/>) and compared to the expected
/// report under blank-node isomorphism, after <c>sh:resultMessage</c> is
/// stripped from both sides — message text is advisory and the W3C suite does
/// not require it to match. The expected report is the rooted subgraph of the
/// unique <c>sh:ValidationReport</c> node in the test file, collected by an
/// iterative walk over its blank-node structure.
/// </para>
/// <para>
/// <b>Honesty.</b> A genuine validator/serializer disagreement is
/// <see cref="W3cOutcomeStatus.Failed"/>; <see cref="W3cOutcomeStatus.Skipped"/>
/// is reserved for tests the harness cannot run structurally (a missing shapes
/// graph or expected report). A validator that throws is reported Failed, never
/// silently skipped.
/// </para>
/// </remarks>
internal static class W3cShaclRunner
{
    /// <summary>
    /// Runs one SHACL validation test case.
    /// </summary>
    /// <param name="testCase">The SHACL test case to run.</param>
    /// <param name="cancellationToken">A token to cancel parsing and validation.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="testCase"/> is <c>null</c>.</exception>
    private const string ManifestResultPredicate = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#result";

    private const string ShaclTestFailure = "http://www.w3.org/ns/shacl-test#Failure";

    /// <summary>Whether the manifest declares <c>mf:result sht:Failure</c> — the test expects SHACL processing to fail rather than report.</summary>
    /// <param name="manifestQuads">The manifest (leaf) quads.</param>
    /// <returns><see langword="true"/> when a <c>sht:Failure</c> result is declared.</returns>
    private static bool ExpectsFailure(List<Quad> manifestQuads)
    {
        foreach(Quad quad in manifestQuads)
        {
            if(string.Equals(quad.Predicate.Iri.ToString(), ManifestResultPredicate, StringComparison.Ordinal)
                && quad.Object is NamedNode result
                && string.Equals(result.Iri.ToString(), ShaclTestFailure, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<W3cOutcome> RunAsync(W3cTestCase testCase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        if(testCase.ShapesGraphPath is null)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "SHACL test declares no shapes graph.");
        }

        if(!File.Exists(testCase.InputPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Data graph file not found: {testCase.InputPath}");
        }

        if(!File.Exists(testCase.ShapesGraphPath))
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Shapes graph file not found: {testCase.ShapesGraphPath}");
        }

        List<Quad> dataQuads;
        List<Quad> shapesQuads;
        List<Quad> manifestQuads;
        try
        {
            dataQuads = await ParseGraphAsync(testCase.InputPath, cancellationToken).ConfigureAwait(false);
            shapesQuads = string.Equals(testCase.InputPath, testCase.ShapesGraphPath, StringComparison.Ordinal)
                ? dataQuads
                : await ParseGraphAsync(testCase.ShapesGraphPath, cancellationToken).ConfigureAwait(false);

            //The expected report lives in the manifest (leaf) file, which is the
            //same file as the data graph only when sht:dataGraph is the empty IRI
            //<>. When the data and shapes graphs are separate files, the report
            //must still be read from the manifest.
            manifestQuads = ResolveManifestQuads(testCase, dataQuads, shapesQuads)
                ?? await ParseGraphAsync(testCase.OriginManifest, cancellationToken).ConfigureAwait(false);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Failed to parse graphs: {ex.Message}");
        }

        TermDictionary dictionary = new();
        InMemoryGraphStore dataStore = BuildStore(dataQuads, dictionary);
        InMemoryGraphStore shapesStore = ReferenceEquals(dataQuads, shapesQuads)
            ? dataStore
            : BuildStore(shapesQuads, dictionary);

        //A test whose mf:result is sht:Failure expects SHACL processing itself to fail (e.g. a SHACL-SPARQL
        //constraint using a construct §5.2.1 forbids under pre-binding), not to produce a validation report.
        bool expectsFailure = ExpectsFailure(manifestQuads);

        ValidationReport report;
        try
        {
            ShapeRegistry registry = await ShapeLoader.LoadAsync(
                shapesStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All, cancellationToken: cancellationToken).ConfigureAwait(false);

            //Expose the shapes graph as the named graph a SPARQL constraint's $shapesGraph designates (SHACL-SPARQL
            //§5.2.1). The graph name is the shapes file's document IRI — the value sht:shapesGraph <> resolves to —
            //so a query's GRAPH <shapes-file> and the pre-bound $shapesGraph key the same named graph.
            RdfTerm shapesGraphIri = new NamedNode(Utf8Strings.From(new Uri(Path.GetFullPath(testCase.ShapesGraphPath)).AbsoluteUri));
            report = await ShaclValidator.ValidateAsync(
                registry, dataStore.AsMatchOps(), dictionary, ShaclBuiltInEvaluators.All, TimeProvider.System,
                shapesGraphMatchOps: shapesStore.AsMatchOps(), shapesGraphIri: shapesGraphIri, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch(ShaclSparqlPreBindingException ex)
        {
            return expectsFailure
                ? new W3cOutcome(W3cOutcomeStatus.Passed, $"Validation failed as expected (sht:Failure): {ex.Message}")
                : new W3cOutcome(W3cOutcomeStatus.Failed, $"Validation threw {ex.GetType().Name}: {ex.Message}");
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, $"Validation threw {ex.GetType().Name}: {ex.Message}");
        }

        if(expectsFailure)
        {
            return new W3cOutcome(W3cOutcomeStatus.Failed, "Expected SHACL processing failure (sht:Failure) but validation produced a report.");
        }

        List<Quad> expected = StripMessages(ExtractReportSubgraph(manifestQuads));
        if(expected.Count == 0)
        {
            return new W3cOutcome(W3cOutcomeStatus.Skipped, "Test file declares no expected sh:ValidationReport.");
        }

        List<Quad> actual = StripMessages(ValidationReportSerializer.Serialize(report, dictionary, includeMessages: false));

        if(QuadSetIsomorphism.AreIsomorphic(actual, expected))
        {
            return new W3cOutcome(W3cOutcomeStatus.Passed, $"Report matches (conforms={report.Conforms}, {report.Results.Length} result(s)).");
        }

        return new W3cOutcome(
            W3cOutcomeStatus.Failed,
            $"Report mismatch: produced conforms={report.Conforms} with {report.Results.Length} result(s) ({actual.Count} quads) vs expected {expected.Count} quads.");
    }

    /// <summary>
    /// Parses a Turtle graph file into quads, resolving relative IRIs against
    /// the file's own URL as base.
    /// </summary>
    /// <param name="path">The absolute path to the Turtle file.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The parsed quads.</returns>
    private static async Task<List<Quad>> ParseGraphAsync(string path, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        Uri baseUri = new(Path.GetFullPath(path));

        List<Quad> quads = [];
        DiagnosticBag diagnostics = new();
        await foreach(Quad quad in TurtleReader.ReadAsync(
            bytes, TurtleSyntax.Turtle, diagnostics, pool: null, baseIri: baseUri.AbsoluteUri, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        //A SHACL shapes/data graph is trusted input; preserve the prior loud-failure semantics by
        //re-raising once the never-throwing reader reports an error (rather than using a partial graph).
        if(diagnostics.HasErrors)
        {
            throw new TurtleParseException(TurtleConformanceReader.DescribeFirstError(diagnostics));
        }

        return quads;
    }

    /// <summary>
    /// Encodes a graph's quads into an <see cref="InMemoryGraphStore"/> over the
    /// shared dictionary.
    /// </summary>
    /// <param name="quads">The quads to encode.</param>
    /// <param name="dictionary">The shared term dictionary.</param>
    /// <returns>The built store.</returns>
    private static InMemoryGraphStore BuildStore(List<Quad> quads, TermDictionary dictionary)
    {
        List<EncodedTriple> triples = new(quads.Count);
        foreach(Quad quad in quads)
        {
            triples.Add(quad.Encode(dictionary).AsTriple());
        }

        return InMemoryGraphStore.Build(triples);
    }

    /// <summary>
    /// Returns the already-parsed quads of the manifest file when it coincides
    /// with the data or shapes graph (the common <c>&lt;&gt;</c> case), or
    /// <c>null</c> when the manifest is a distinct file that must be parsed
    /// separately to recover the expected report.
    /// </summary>
    /// <param name="testCase">The test case carrying the graph and manifest paths.</param>
    /// <param name="dataQuads">The parsed data graph.</param>
    /// <param name="shapesQuads">The parsed shapes graph.</param>
    /// <returns>The reused quads, or <c>null</c> when a separate parse is needed.</returns>
    private static List<Quad>? ResolveManifestQuads(W3cTestCase testCase, List<Quad> dataQuads, List<Quad> shapesQuads)
    {
        if(testCase.OriginManifest.Length == 0 || string.Equals(testCase.OriginManifest, testCase.InputPath, StringComparison.Ordinal))
        {
            return dataQuads;
        }

        if(string.Equals(testCase.OriginManifest, testCase.ShapesGraphPath, StringComparison.Ordinal))
        {
            return shapesQuads;
        }

        return null;
    }

    /// <summary>
    /// Collects the rooted subgraph of the test file's expected
    /// <c>sh:ValidationReport</c>: every quad reachable from a
    /// <c>sh:ValidationReport</c> node by following blank-node objects
    /// (results, property-path structures, list cells). Named-node objects are
    /// report leaves and are not traversed into, so the surrounding data and
    /// shapes triples are excluded.
    /// </summary>
    /// <param name="allQuads">All quads parsed from the test file.</param>
    /// <returns>The expected report subgraph.</returns>
    private static List<Quad> ExtractReportSubgraph(List<Quad> allQuads)
    {
        Dictionary<RdfTerm, List<Quad>> bySubject = [];
        foreach(Quad quad in allQuads)
        {
            if(!bySubject.TryGetValue(quad.Subject, out List<Quad>? owned))
            {
                owned = [];
                bySubject[quad.Subject] = owned;
            }

            owned.Add(quad);
        }

        HashSet<RdfTerm> reached = [];
        Queue<RdfTerm> frontier = new();
        foreach(Quad quad in allQuads)
        {
            if(IsType(quad, ShaclResultsVocabulary.ValidationReport) && reached.Add(quad.Subject))
            {
                frontier.Enqueue(quad.Subject);
            }
        }

        List<Quad> subgraph = [];
        while(frontier.Count > 0)
        {
            RdfTerm subject = frontier.Dequeue();
            if(!bySubject.TryGetValue(subject, out List<Quad>? owned))
            {
                continue;
            }

            foreach(Quad quad in owned)
            {
                subgraph.Add(quad);

                //Follow report-structural blank nodes (sh:result, sh:resultPath path nodes, RDF list cells) but
                //NOT the data/shape nodes a result merely references (sh:focusNode/sh:value/sh:sourceShape/
                //sh:sourceConstraint): those nodes' own triples live in the data/shapes graph, not the report, so
                //traversing a blank focus/value/shape would pull data triples (e.g. its rdf:type) into the
                //"expected report" and wrongly fail the structural comparison.
                if(quad.Object is BlankNode blank && !IsDataReference(quad.Predicate) && reached.Add(blank))
                {
                    frontier.Enqueue(blank);
                }
            }
        }

        return subgraph;
    }

    /// <summary>
    /// Returns whether a predicate links a result to a data/shape node it only references (not report structure):
    /// <c>sh:focusNode</c>, <c>sh:value</c>, <c>sh:sourceShape</c>, <c>sh:sourceConstraint</c>. The report-subgraph
    /// walk does not descend into the objects of these, so a blank focus/value/shape contributes only its
    /// reference, not its own (data-graph) triples.
    /// </summary>
    /// <param name="predicate">The predicate to classify.</param>
    /// <returns><c>true</c> when the predicate is a data/shape reference.</returns>
    private static bool IsDataReference(NamedNode predicate)
    {
        return predicate.Iri.Equals(ShaclResultsVocabulary.FocusNode)
            || predicate.Iri.Equals(ShaclResultsVocabulary.Value)
            || predicate.Iri.Equals(ShaclResultsVocabulary.SourceShape)
            || predicate.Iri.Equals(ShaclResultsVocabulary.SourceConstraint);
    }

    /// <summary>
    /// Returns the quads with <c>sh:resultMessage</c> triples removed.
    /// </summary>
    /// <param name="quads">The quads to filter.</param>
    /// <returns>A new list without message triples.</returns>
    private static List<Quad> StripMessages(List<Quad> quads)
    {
        List<Quad> result = new(quads.Count);
        foreach(Quad quad in quads)
        {
            if(!quad.Predicate.Iri.Equals(ShaclResultsVocabulary.ResultMessage))
            {
                result.Add(quad);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> when the quad asserts <c>rdf:type</c> with the given
    /// class IRI as object.
    /// </summary>
    /// <param name="quad">The quad to test.</param>
    /// <param name="typeIri">The class IRI to match.</param>
    /// <returns><c>true</c> on an <c>rdf:type</c> assertion of the class.</returns>
    private static bool IsType(Quad quad, Utf8String typeIri)
    {
        return quad.Predicate.Iri.Equals(Vocabulary.Rdf.Type)
            && quad.Object is NamedNode named
            && named.Iri.Equals(typeIri);
    }
}
