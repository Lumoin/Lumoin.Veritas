using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.ParserTests.Conformance;

/// <summary>
/// Parses a W3C conformance manifest (written in Turtle) into a
/// <see cref="W3cManifest"/>.
/// </summary>
/// <remarks>
/// <para>
/// W3C manifests use the test-manifest vocabulary
/// (<c>mf:</c>) plus <c>rdft:</c> for test-type IRIs. Each
/// manifest declares its tests with <c>mf:entries</c> as an
/// RDF list; the loader walks <c>rdf:first</c>/<c>rdf:rest</c>
/// links iteratively. Top-level manifests aggregate suite
/// manifests via <c>mf:include</c> (also an RDF list); the
/// loader follows those references transitively. An include
/// that does not exist on disk is recorded on
/// <see cref="W3cManifest.UnresolvedIncludes"/> rather than
/// failing the load — vendored snapshots commonly point at
/// out-of-scope corpora (the RDF 1.1 cross-reference, for
/// instance).
/// </para>
/// <para>
/// The loader synthesises absolute file paths for
/// <c>mf:action</c> and <c>mf:result</c> by resolving the
/// declared IRI against the absolute file URL of the
/// containing manifest. Manifests that use
/// <c>mf:assumedTestBase</c> for HTTP-style IRIs are not
/// short-circuited; the loader still resolves on-disk file
/// names from the manifest's location.
/// </para>
/// </remarks>
internal static class W3cManifestLoader
{
    private const string MfNamespace = "http://www.w3.org/2001/sw/DataAccess/tests/test-manifest#";
    private const string RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string RdfsNamespace = "http://www.w3.org/2000/01/rdf-schema#";
    private const string ShtNamespace = "http://www.w3.org/ns/shacl-test#";
    private const string QtNamespace = "http://www.w3.org/2001/sw/DataAccess/tests/test-query#";
    private const string UtNamespace = "http://www.w3.org/2009/sparql/tests/test-update#";
    private const string SdNamespace = "http://www.w3.org/ns/sparql-service-description#";

    /// <summary>
    /// Loads a manifest. The load is genuinely synchronous end to end — the
    /// manifest bytes are read whole and parsed by the in-memory Turtle core —
    /// so <see cref="MSTest"/> data sources, whose
    /// <c>ITestDataSource.GetData</c> contract is synchronous at test-discovery
    /// time, consume it without any task machinery.
    /// </summary>
    /// <param name="manifestPath">Absolute path to the manifest file.</param>
    /// <returns>The parsed manifest including all transitively-included tests.</returns>
    public static W3cManifest Load(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);

        Uri rootUri = new(Path.GetFullPath(manifestPath));
        ImmutableArray<W3cTestCase>.Builder tests = ImmutableArray.CreateBuilder<W3cTestCase>();
        ImmutableArray<string>.Builder unresolved = ImmutableArray.CreateBuilder<string>();

        //Iterative breadth-first traversal of mf:include references; the visited set guards against cycles.
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> queue = new();
        queue.Enqueue(rootUri.LocalPath);

        while(queue.Count > 0)
        {
            string current = queue.Dequeue();
            if(!visited.Add(current))
            {
                continue;
            }

            if(!File.Exists(current))
            {
                unresolved.Add(current);
                continue;
            }

            ManifestParse parsed = ParseSingle(current);
            foreach(W3cTestCase test in parsed.Tests)
            {
                tests.Add(test);
            }

            foreach(string include in parsed.Includes)
            {
                queue.Enqueue(include);
            }
        }

        return new W3cManifest(rootUri, tests.ToImmutable(), unresolved.ToImmutable());
    }

    private static ManifestParse ParseSingle(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Uri manifestUri = new(Path.GetFullPath(path));

        //The manifest's own file URL is its retrieval base: its relative IRIs (#manifest, sibling
        //action/result files, ../-style includes) resolve to absolute file URIs, which the path
        //helpers below map straight back to disk. The base is harness bookkeeping and never reaches
        //test output, so a file:// base is the right choice here.
        List<Quad> quads = [];
        DiagnosticBag diagnostics = new();
        foreach(Quad q in TurtleReader.Read(
            bytes,
            TurtleSyntax.Turtle,
            diagnostics,
            pool: null,
            baseIri: manifestUri.AbsoluteUri))
        {
            quads.Add(q);
        }

        //A manifest is trusted infrastructure; a malformed one should fail loudly rather than silently
        //yield a partial graph. The reader no longer throws on malformed input, so the bag is checked.
        if(diagnostics.HasErrors)
        {
            throw new InvalidOperationException($"Failed to parse manifest or test file '{path}': {TurtleConformanceReader.DescribeFirstError(diagnostics)}");
        }

        Dictionary<string, List<Quad>> bySubject = IndexBySubject(quads);

        //Locate the manifest subject — the term that has rdf:type mf:Manifest.
        string? manifestSubject = FindManifestSubject(bySubject);
        if(manifestSubject is null)
        {
            return new ManifestParse([], []);
        }

        ImmutableArray<W3cTestCase> tests = ImmutableArray<W3cTestCase>.Empty;
        ImmutableArray<string> includes = ImmutableArray<string>.Empty;

        if(bySubject.TryGetValue(manifestSubject, out List<Quad>? manifestProperties))
        {
            List<string> entrySubjects = ResolveListOrDirect(FindObjects(manifestProperties, MfNamespace + "entries"), bySubject);
            tests = BuildTests(entrySubjects, bySubject, manifestUri, manifestUri.LocalPath);

            List<string> rawIncludes = ResolveListOrDirect(FindObjects(manifestProperties, MfNamespace + "include"), bySubject);
            includes = ResolveIncludesToPaths(rawIncludes, manifestUri);
        }

        return new ManifestParse(tests, includes);
    }

    private static Dictionary<string, List<Quad>> IndexBySubject(List<Quad> quads)
    {
        Dictionary<string, List<Quad>> result = new(StringComparer.Ordinal);
        for(int i = 0; i < quads.Count; i++)
        {
            string key = TermKey(quads[i].Subject);
            if(!result.TryGetValue(key, out List<Quad>? bucket))
            {
                bucket = [];
                result[key] = bucket;
            }

            bucket.Add(quads[i]);
        }

        return result;
    }

    private static string? FindManifestSubject(Dictionary<string, List<Quad>> bySubject)
    {
        foreach(KeyValuePair<string, List<Quad>> entry in bySubject)
        {
            for(int i = 0; i < entry.Value.Count; i++)
            {
                Quad q = entry.Value[i];
                if(q.Predicate.Iri.ToString() == RdfNamespace + "type"
                    && q.Object is NamedNode obj
                    && obj.Iri.ToString() == MfNamespace + "Manifest")
                {
                    return entry.Key;
                }
            }
        }

        return null;
    }

    private static string? FindObject(List<Quad> properties, string predicateIri)
    {
        for(int i = 0; i < properties.Count; i++)
        {
            if(properties[i].Predicate.Iri.ToString() == predicateIri)
            {
                return TermKey(properties[i].Object);
            }
        }

        return null;
    }

    private static List<string> FindObjects(List<Quad> properties, string predicateIri)
    {
        List<string> result = [];
        for(int i = 0; i < properties.Count; i++)
        {
            if(properties[i].Predicate.Iri.ToString() == predicateIri)
            {
                result.Add(TermKey(properties[i].Object));
            }
        }

        return result;
    }

    private static List<string> WalkRdfList(string head, Dictionary<string, List<Quad>> bySubject)
    {
        //RDF lists chain rdf:first / rdf:rest until rdf:nil. Iterative traversal guards against cycles.
        List<string> result = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        string current = head;
        string nil = "i:" + RdfNamespace + "nil";
        while(current != nil)
        {
            if(!visited.Add(current))
            {
                break;
            }

            if(!bySubject.TryGetValue(current, out List<Quad>? nodeProperties))
            {
                break;
            }

            string? first = FindObject(nodeProperties, RdfNamespace + "first");
            string? rest = FindObject(nodeProperties, RdfNamespace + "rest");
            if(first is null || rest is null)
            {
                break;
            }

            result.Add(first);
            current = rest;
        }

        return result;
    }

    /// <summary>
    /// Resolves the objects of a manifest list-or-direct predicate. Each raw
    /// object is either an RDF-list head — walked via
    /// <c>rdf:first</c>/<c>rdf:rest</c> — or a direct reference.
    /// </summary>
    /// <remarks>
    /// The two forms appear interchangeably for <c>mf:entries</c> and
    /// <c>mf:include</c> across the vendored corpora: some manifests state a
    /// single RDF list, others repeat the predicate once per member. Probing
    /// each raw object for an <c>rdf:first</c> edge and walking or taking it
    /// as-is handles both uniformly.
    /// </remarks>
    /// <param name="rawObjects">The objects of the predicate, one per assertion.</param>
    /// <param name="bySubject">The per-subject quad index.</param>
    /// <returns>The flattened list of referenced subject keys.</returns>
    private static List<string> ResolveListOrDirect(List<string> rawObjects, Dictionary<string, List<Quad>> bySubject)
    {
        List<string> result = [];
        foreach(string raw in rawObjects)
        {
            if(bySubject.TryGetValue(raw, out List<Quad>? properties) && FindObject(properties, RdfNamespace + "first") is not null)
            {
                result.AddRange(WalkRdfList(raw, bySubject));
            }
            else
            {
                result.Add(raw);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the test cases for one manifest's entry subjects, dispatching
    /// SHACL <c>sht:Validate</c> entries to <see cref="BuildShaclTest"/> and
    /// the syntax, evaluation, and canonicalization entries to the
    /// sibling-file resolution path.
    /// </summary>
    /// <param name="entrySubjects">The entry subject keys declared by the manifest.</param>
    /// <param name="bySubject">The per-subject quad index.</param>
    /// <param name="manifestUri">The manifest's absolute file URI, the base for resolving references.</param>
    /// <param name="originManifest">The manifest's local path, recorded on each test for sub-suite grouping.</param>
    /// <returns>The built test cases.</returns>
    private static ImmutableArray<W3cTestCase> BuildTests(
        List<string> entrySubjects,
        Dictionary<string, List<Quad>> bySubject,
        Uri manifestUri,
        string originManifest)
    {
        ImmutableArray<W3cTestCase>.Builder builder = ImmutableArray.CreateBuilder<W3cTestCase>(entrySubjects.Count);

        for(int i = 0; i < entrySubjects.Count; i++)
        {
            string entry = entrySubjects[i];
            if(!bySubject.TryGetValue(entry, out List<Quad>? properties))
            {
                continue;
            }

            string typeIri = FindObjects(properties, RdfNamespace + "type") switch
            {
                { Count: > 0 } list => StripIriPrefix(list[0]),
                _ => string.Empty
            };

            //SHACL manifests name their entries with rdfs:label; the RDF/Turtle
            //suites use mf:name. Prefer mf:name and fall back to rdfs:label so
            //both corpora yield a populated name.
            string name = ResolveStringLiteral(properties, MfNamespace + "name")
                ?? ResolveStringLiteral(properties, RdfsNamespace + "label")
                ?? string.Empty;
            string comment = ResolveStringLiteral(properties, RdfsNamespace + "comment") ?? string.Empty;
            W3cTestType type = ClassifyTestType(typeIri);

            if(type == W3cTestType.ShaclValidate)
            {
                W3cTestCase? shaclTest = BuildShaclTest(entry, properties, bySubject, manifestUri, typeIri, name, comment, originManifest);
                if(shaclTest is not null)
                {
                    builder.Add(shaclTest);
                }

                continue;
            }

            if(type == W3cTestType.SparqlQueryEvaluation)
            {
                W3cTestCase? evalTest = BuildSparqlEvalTest(entry, properties, bySubject, manifestUri, typeIri, name, comment, originManifest);
                if(evalTest is not null)
                {
                    builder.Add(evalTest);
                }

                continue;
            }

            if(type == W3cTestType.SparqlUpdateEvaluation)
            {
                W3cTestCase? updateTest = BuildSparqlUpdateTest(entry, properties, bySubject, manifestUri, typeIri, name, comment, originManifest);
                if(updateTest is not null)
                {
                    builder.Add(updateTest);
                }

                continue;
            }

            string? actionTerm = FindObject(properties, MfNamespace + "action");
            string? resultTerm = FindObject(properties, MfNamespace + "result");

            if(actionTerm is null)
            {
                continue;
            }

            string? inputPath = ResolveSiblingPath(actionTerm, manifestUri);
            if(inputPath is null)
            {
                continue;
            }

            string? expectedPath = resultTerm is null ? null : ResolveSiblingPath(resultTerm, manifestUri);

            Uri testUri = TryBuildAbsoluteUri(entry) ?? new Uri(manifestUri, "#" + entry);
            W3cTestCase testCase = new(testUri, type, typeIri, name, comment, inputPath, expectedPath, originManifest);
            builder.Add(testCase);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Builds one SHACL <c>sht:Validate</c> test case from its blank-node
    /// action and inline expected report.
    /// </summary>
    /// <remarks>
    /// The action is a blank node carrying <c>sht:dataGraph</c> and
    /// <c>sht:shapesGraph</c> — frequently the empty IRI <c>&lt;&gt;</c>, which
    /// resolves to the leaf file's own URL so both graphs are the test file
    /// itself. The result is a blank node naming the inline expected
    /// <c>sh:ValidationReport</c>; its term key is preserved on
    /// <see cref="W3cTestCase.ExpectedReportTerm"/> for the runner to extract
    /// the rooted report subgraph, rather than resolved to a sibling path.
    /// Returns <c>null</c> when the action or its graph references are absent
    /// or unresolvable.
    /// </remarks>
    /// <param name="entry">The test entry subject key.</param>
    /// <param name="properties">The entry subject's quads.</param>
    /// <param name="bySubject">The per-subject quad index.</param>
    /// <param name="manifestUri">The manifest's absolute file URI.</param>
    /// <param name="typeIri">The raw <c>rdf:type</c> IRI.</param>
    /// <param name="name">The test name.</param>
    /// <param name="comment">The test comment.</param>
    /// <param name="originManifest">The declaring manifest's local path.</param>
    /// <returns>The SHACL test case, or <c>null</c> when it cannot be built.</returns>
    private static W3cTestCase? BuildShaclTest(
        string entry,
        List<Quad> properties,
        Dictionary<string, List<Quad>> bySubject,
        Uri manifestUri,
        string typeIri,
        string name,
        string comment,
        string originManifest)
    {
        string? actionTerm = FindObject(properties, MfNamespace + "action");
        if(actionTerm is null || !bySubject.TryGetValue(actionTerm, out List<Quad>? actionProperties))
        {
            return null;
        }

        string? dataGraphTerm = FindObject(actionProperties, ShtNamespace + "dataGraph");
        string? shapesGraphTerm = FindObject(actionProperties, ShtNamespace + "shapesGraph");
        if(dataGraphTerm is null || shapesGraphTerm is null)
        {
            return null;
        }

        string? dataPath = ResolveSiblingPath(dataGraphTerm, manifestUri);
        string? shapesPath = ResolveSiblingPath(shapesGraphTerm, manifestUri);
        if(dataPath is null || shapesPath is null)
        {
            return null;
        }

        string? expectedReportTerm = FindObject(properties, MfNamespace + "result");
        Uri testUri = TryBuildAbsoluteUri(entry) ?? new Uri(manifestUri, "#" + entry);

        return new W3cTestCase(
            testUri,
            W3cTestType.ShaclValidate,
            typeIri,
            name,
            comment,
            InputPath: dataPath,
            ExpectedPath: null,
            OriginManifest: originManifest,
            ShapesGraphPath: shapesPath,
            ExpectedReportTerm: expectedReportTerm);
    }

    /// <summary>
    /// Builds one SPARQL <c>mf:QueryEvaluationTest</c> case from its action and result. The action is a blank node
    /// carrying <c>qt:query</c> (the query file) and an optional <c>qt:data</c> (the default-graph data file); the
    /// <c>mf:result</c> is the expected-result sibling file. Returns <c>null</c> when the query reference is absent
    /// or unresolvable.
    /// </summary>
    /// <remarks>
    /// A test with <c>qt:graphData</c> (named-graph data) carries each named-graph file through
    /// <see cref="W3cTestCase.GraphDataPaths"/>; the runner loads each as a named graph keyed by its own file IRI,
    /// which is the IRI a query's <c>GRAPH &lt;file&gt;</c> (or projected <c>?g</c>) resolves to.
    /// </remarks>
    /// <param name="entry">The test entry subject key.</param>
    /// <param name="properties">The entry subject's quads.</param>
    /// <param name="bySubject">The per-subject quad index.</param>
    /// <param name="manifestUri">The manifest's absolute file URI.</param>
    /// <param name="typeIri">The raw <c>rdf:type</c> IRI.</param>
    /// <param name="name">The test name.</param>
    /// <param name="comment">The test comment.</param>
    /// <param name="originManifest">The declaring manifest's local path.</param>
    /// <returns>The SPARQL evaluation test case, or <c>null</c> when it cannot be built.</returns>
    private static W3cTestCase? BuildSparqlEvalTest(
        string entry,
        List<Quad> properties,
        Dictionary<string, List<Quad>> bySubject,
        Uri manifestUri,
        string typeIri,
        string name,
        string comment,
        string originManifest)
    {
        string? actionTerm = FindObject(properties, MfNamespace + "action");
        if(actionTerm is null)
        {
            return null;
        }

        //The action is normally a blank node holding qt:query (+ qt:data); some manifests point mf:action straight
        //at the query file. Resolve the query from the action node when it has one, else treat the action as the query.
        string? queryTerm = bySubject.TryGetValue(actionTerm, out List<Quad>? actionProperties)
            ? FindObject(actionProperties, QtNamespace + "query")
            : null;
        string? dataTerm = actionProperties is null ? null : FindObject(actionProperties, QtNamespace + "data");

        string? queryPath = ResolveSiblingPath(queryTerm ?? actionTerm, manifestUri);
        if(queryPath is null)
        {
            return null;
        }

        string? dataPath = dataTerm is null ? null : ResolveSiblingPath(dataTerm, manifestUri);
        string? resultTerm = FindObject(properties, MfNamespace + "result");
        string? expectedPath = resultTerm is null ? null : ResolveSiblingPath(resultTerm, manifestUri);

        //Each qt:graphData is one named graph; its file resolves like qt:data, and the runner keys it by its own
        //file IRI (the IRI a query's GRAPH <file> resolves to).
        List<string> graphDataPaths = [];
        List<string> entailmentRegimes = [];
        List<string> entailmentProfiles = [];
        if(actionProperties is not null)
        {
            foreach(string graphDataTerm in FindObjects(actionProperties, QtNamespace + "graphData"))
            {
                if(ResolveSiblingPath(graphDataTerm, manifestUri) is string graphDataPath)
                {
                    graphDataPaths.Add(graphDataPath);
                }
            }

            //sd:entailmentRegime is a single regime IRI or an RDF list of them; the expected result holds under
            //each listed regime, so the runner may evaluate under any one it implements.
            foreach(string regimeTerm in ResolveListOrDirect(FindObjects(actionProperties, SdNamespace + "entailmentRegime"), bySubject))
            {
                entailmentRegimes.Add(StripIriPrefix(regimeTerm));
            }

            //sd:EntailmentProfile lists the OWL 2 profiles whose reasoners the test sanctions — the regimes
            //specification conditions the OWL RDF-Based regime on a profile, so a pr:RL-annotated test is
            //answerable by the RL rules calculus.
            foreach(string profileTerm in ResolveListOrDirect(FindObjects(actionProperties, SdNamespace + "EntailmentProfile"), bySubject))
            {
                entailmentProfiles.Add(StripIriPrefix(profileTerm));
            }
        }

        Uri testUri = TryBuildAbsoluteUri(entry) ?? new Uri(manifestUri, "#" + entry);

        return new W3cTestCase(
            testUri,
            W3cTestType.SparqlQueryEvaluation,
            typeIri,
            name,
            comment,
            InputPath: queryPath,
            ExpectedPath: expectedPath,
            OriginManifest: originManifest,
            QueryDataPath: dataPath,
            GraphDataPaths: graphDataPaths,
            EntailmentRegimes: entailmentRegimes.Count > 0 ? entailmentRegimes : null,
            EntailmentProfiles: entailmentProfiles.Count > 0 ? entailmentProfiles : null);
    }

    /// <summary>
    /// Builds a SPARQL Update evaluation test from an <c>mf:UpdateEvaluationTest</c> entry: the action's
    /// <c>ut:request</c> is the update file, its <c>ut:data</c> / <c>ut:graphData</c> the initial dataset, and the
    /// result's <c>ut:data</c> the expected resulting dataset (a TriG file carrying the whole default + named result).
    /// </summary>
    /// <param name="entry">The test entry subject.</param>
    /// <param name="properties">The entry's properties.</param>
    /// <param name="bySubject">The subject-to-properties index.</param>
    /// <param name="manifestUri">The manifest URI for sibling-path resolution.</param>
    /// <param name="typeIri">The raw test-type IRI.</param>
    /// <param name="name">The test name.</param>
    /// <param name="comment">The test comment.</param>
    /// <param name="originManifest">The origin manifest path.</param>
    /// <returns>The test case, or <see langword="null"/> when it lacks an executable request.</returns>
    private static W3cTestCase? BuildSparqlUpdateTest(
        string entry,
        List<Quad> properties,
        Dictionary<string, List<Quad>> bySubject,
        Uri manifestUri,
        string typeIri,
        string name,
        string comment,
        string originManifest)
    {
        string? actionTerm = FindObject(properties, MfNamespace + "action");
        if(actionTerm is null || !bySubject.TryGetValue(actionTerm, out List<Quad>? actionProperties))
        {
            return null;
        }

        string? requestTerm = FindObject(actionProperties, UtNamespace + "request");
        string? requestPath = requestTerm is null ? null : ResolveSiblingPath(requestTerm, manifestUri);
        if(requestPath is null)
        {
            return null;
        }

        string? dataTerm = FindObject(actionProperties, UtNamespace + "data");
        string? dataPath = dataTerm is null ? null : ResolveSiblingPath(dataTerm, manifestUri);
        List<(string GraphName, string Path)> inputGraphs = ParseUpdateGraphData(actionProperties, bySubject, manifestUri);

        //The result node carries the expected resulting dataset: ut:data (default graph) and labelled ut:graphData
        //(named graphs). Some manifests point mf:result straight at a file (the whole-dataset TriG case).
        List<Quad>? resultProperties = null;
        string? resultTerm = FindObject(properties, MfNamespace + "result");
        string? expectedDataTerm;
        if(resultTerm is not null && bySubject.TryGetValue(resultTerm, out resultProperties))
        {
            expectedDataTerm = FindObject(resultProperties, UtNamespace + "data");
        }
        else
        {
            //A property-less result node is either a direct file IRI (the whole-dataset case) or an empty blank node
            //`[]` (the expected dataset is empty); a blank node must NOT be resolved as a file path.
            expectedDataTerm = resultTerm is not null && resultTerm.StartsWith("i:", StringComparison.Ordinal) ? resultTerm : null;
        }

        string? expectedPath = expectedDataTerm is null ? null : ResolveSiblingPath(expectedDataTerm, manifestUri);
        List<(string GraphName, string Path)> expectedGraphs = ParseUpdateGraphData(resultProperties, bySubject, manifestUri);

        Uri testUri = TryBuildAbsoluteUri(entry) ?? new Uri(manifestUri, "#" + entry);

        return new W3cTestCase(
            testUri,
            W3cTestType.SparqlUpdateEvaluation,
            typeIri,
            name,
            comment,
            InputPath: requestPath,
            ExpectedPath: expectedPath,
            OriginManifest: originManifest,
            QueryDataPath: dataPath,
            GraphDataPaths: null,
            UpdateInputGraphs: inputGraphs,
            UpdateExpectedGraphs: expectedGraphs);
    }

    /// <summary>Parses a node's <c>ut:graphData [ ut:graph &lt;file&gt; ; rdfs:label "name" ]</c> entries into (graph-name, file) pairs; the graph name is the <c>rdfs:label</c> literal, not the file IRI.</summary>
    /// <param name="nodeProperties">The action or result node's properties, or <see langword="null"/>.</param>
    /// <param name="bySubject">The subject-to-properties index.</param>
    /// <param name="manifestUri">The manifest URI for sibling-path resolution.</param>
    /// <returns>The named-graph (name, file) pairs.</returns>
    private static List<(string GraphName, string Path)> ParseUpdateGraphData(List<Quad>? nodeProperties, Dictionary<string, List<Quad>> bySubject, Uri manifestUri)
    {
        List<(string GraphName, string Path)> graphs = [];
        if(nodeProperties is null)
        {
            return graphs;
        }

        foreach(string graphDataTerm in FindObjects(nodeProperties, UtNamespace + "graphData"))
        {
            if(!bySubject.TryGetValue(graphDataTerm, out List<Quad>? graphDataProperties))
            {
                continue;
            }

            string? graphTerm = FindObject(graphDataProperties, UtNamespace + "graph");
            string? label = ResolveStringLiteral(graphDataProperties, RdfsNamespace + "label");
            string? path = graphTerm is null ? null : ResolveSiblingPath(graphTerm, manifestUri);
            if(path is not null && label is not null)
            {
                graphs.Add((label, path));
            }
        }

        return graphs;
    }

    private static ImmutableArray<string> ResolveIncludesToPaths(List<string> rawIncludes, Uri manifestUri)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(rawIncludes.Count);
        for(int i = 0; i < rawIncludes.Count; i++)
        {
            string? resolved = ResolveSiblingPath(rawIncludes[i], manifestUri);
            if(resolved is not null)
            {
                builder.Add(resolved);
            }
        }

        return builder.ToImmutable();
    }

    private static string? ResolveSiblingPath(string termKey, Uri manifestUri)
    {
        string iri = StripIriPrefix(termKey);
        if(iri.Length == 0)
        {
            return null;
        }

        if(Uri.TryCreate(iri, UriKind.Absolute, out Uri? absolute) && absolute.IsFile)
        {
            return absolute.LocalPath;
        }

        if(Uri.TryCreate(manifestUri, iri, out Uri? combined) && combined.IsFile)
        {
            return combined.LocalPath;
        }

        //IRI is a real HTTP IRI; fall back to "same directory as manifest, take last segment as file name."
        string fileName = LastPathSegment(iri);
        string? directory = Path.GetDirectoryName(manifestUri.LocalPath);
        if(directory is null || fileName.Length == 0)
        {
            return null;
        }

        return Path.Combine(directory, fileName);
    }

    private static string LastPathSegment(string iri)
    {
        int hash = iri.IndexOf('#', StringComparison.Ordinal);
        string withoutFragment = hash >= 0 ? iri[..hash] : iri;
        int slash = withoutFragment.LastIndexOf('/');
        return slash >= 0 ? withoutFragment[(slash + 1)..] : withoutFragment;
    }

    private static Uri? TryBuildAbsoluteUri(string termKey)
    {
        string iri = StripIriPrefix(termKey);
        return Uri.TryCreate(iri, UriKind.Absolute, out Uri? absolute) ? absolute : null;
    }

    private static string? ResolveStringLiteral(List<Quad> properties, string predicateIri)
    {
        for(int i = 0; i < properties.Count; i++)
        {
            if(properties[i].Predicate.Iri.ToString() == predicateIri && properties[i].Object is Literal literal)
            {
                return literal.Value.ToString();
            }
        }

        return null;
    }

    private static W3cTestType ClassifyTestType(string typeIri)
    {
        //All evaluation variants — TestTurtleEval, TestTrigEval, etc. — share an "Eval" suffix and produce an expected file.
        //Negative evaluations share a "NegativeEval" suffix. Canonicalisation tests share a PositiveC14N suffix
        //(TestNTriplesPositiveC14N, TestNQuadsPositiveC14N). SHACL validation tests are an exact IRI (sht:Validate),
        //matched before the suffix arms.
        //
        //Syntax tests are matched by substring rather than suffix because the SPARQL suites name them
        //mf:PositiveSyntaxTest11 / mf:NegativeSyntaxTest (a "Test"/"Test11" suffix), while the RDF suites use a
        //...PositiveSyntax suffix; both contain the substring. The Update-syntax markers (PositiveUpdateSyntaxTest)
        //deliberately do NOT contain "PositiveSyntax"/"NegativeSyntax", so they fall through to Unknown and are
        //skipped — this build parses queries, not update requests.
        if(typeIri == ShtNamespace + "Validate")
        {
            return W3cTestType.ShaclValidate;
        }

        //SPARQL evaluation tests are an exact marker; matched before the generic "...Eval" / syntax arms so the
        //"...EvaluationTest" suffix is not mistaken for a syntax or RDF-eval test. A CSVResultFormatTest is the same
        //shape (qt:query + mf:result), run as a query-evaluation whose expected result is a .csv serialization.
        if(typeIri == MfNamespace + "QueryEvaluationTest" || typeIri == MfNamespace + "CSVResultFormatTest")
        {
            return W3cTestType.SparqlQueryEvaluation;
        }

        //Update-syntax/-evaluation markers are matched before the generic syntax/Eval arms: "PositiveUpdateSyntax"
        //does not contain "PositiveSyntax", but "UpdateEvaluationTest" ends with "Eval"-adjacent text, so the
        //update arms must win. The update suite parses (and, for eval, executes) update requests.
        if(typeIri.Contains("PositiveUpdateSyntax", StringComparison.Ordinal))
        {
            return W3cTestType.PositiveUpdateSyntax;
        }

        if(typeIri.Contains("NegativeUpdateSyntax", StringComparison.Ordinal))
        {
            return W3cTestType.NegativeUpdateSyntax;
        }

        if(typeIri == MfNamespace + "UpdateEvaluationTest")
        {
            return W3cTestType.SparqlUpdateEvaluation;
        }

        if(typeIri.Contains("PositiveSyntax", StringComparison.Ordinal))
        {
            return W3cTestType.PositiveSyntax;
        }

        if(typeIri.Contains("NegativeSyntax", StringComparison.Ordinal))
        {
            return W3cTestType.NegativeSyntax;
        }

        if(typeIri.EndsWith("PositiveC14N", StringComparison.Ordinal))
        {
            return W3cTestType.PositiveC14N;
        }

        if(typeIri.EndsWith("NegativeEval", StringComparison.Ordinal))
        {
            return W3cTestType.NegativeEvaluation;
        }

        if(typeIri.EndsWith("Eval", StringComparison.Ordinal))
        {
            return W3cTestType.Evaluation;
        }

        return W3cTestType.Unknown;
    }

    private static string StripIriPrefix(string termKey)
    {
        //TermKey wraps IRIs as "i:<iri>" and blank nodes as "b:<label>". Manifests use IRIs almost everywhere.
        if(termKey.StartsWith("i:", StringComparison.Ordinal))
        {
            return termKey[2..];
        }

        return termKey;
    }

    private static string TermKey(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => "i:" + named.Iri.ToString(),
            BlankNode blank => "b:" + blank.Label.ToString(),
            Literal literal => "l:" + literal.Value.ToString() + ":" + (literal.Language?.ToString() ?? string.Empty) + ":" + literal.Datatype.Iri.ToString(),
            TripleTerm tt => "t:" + TermKey(tt.Subject) + "|" + tt.Predicate.Iri.ToString() + "|" + TermKey(tt.Object),
            _ => "x:" + term.ToString()
        };
    }

    private readonly record struct ManifestParse(
        ImmutableArray<W3cTestCase> Tests,
        ImmutableArray<string> Includes);
}
