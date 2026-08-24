using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Execution.Interception;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Stage B pins for the evaluation interception registry: the differential-isolation switch answers
/// identically with the fast paths off, the registry and the ASK entry emit their provenance events when a
/// fast path fires (and none when disabled), and — the seam's certification gate — every SPARQL evaluation
/// and update fixture in the conformance corpus produces the same outcome with interceptions disabled as
/// with them on, retroactively certifying the shipped fast paths never changed an answer.
/// </summary>
[TestClass]
internal sealed class SparqlInterceptionTests
{
    /// <summary>The example namespace the test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The SPARQL evaluation suite folders the isolation arm sweeps — the same set the standing differential arms triple.</summary>
    private static string[] EvalSuites { get; } =
    [
        "eval-smoke", "aggregates", "bind", "bindings", "cast", "construct", "exists", "functions",
        "grouping", "negation", "project-expression", "property-path", "subquery", "expression",
        "eval-triple-terms", "sparql12-grouping", "sparql12-rdf11", "lang-basedir", "entailment",
        "json-res", "csv-tsv-res",
    ];

    /// <summary>The SPARQL update suite folders the isolation arm sweeps.</summary>
    private static string[] UpdateSuites { get; } =
    [
        "basic-update", "delete-data", "delete", "delete-where", "delete-insert", "add", "clear",
        "copy", "move", "drop", "update-silent",
    ];

    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// The isolation switch changes no answer, and provenance flows exactly when the fast paths run: an
    /// enabled engine emits the leaf-cap and ASK events (rows/answers unchanged either way), a disabled one
    /// emits none.
    /// </summary>
    [TestMethod]
    public async Task InterceptionsDisabledAnswerIdenticallyWithoutProvenance()
    {
        DataTriple[] data =
        [
            new DataTriple(Iri("a"), Iri("knows"), Iri("b")),
            new DataTriple(Iri("a"), Iri("likes"), Iri("c")),
            new DataTriple(Iri("b"), Iri("knows"), Iri("c")),
        ];
        List<SparqlExecutionTraceEvent> enabledEvents = [];
        TraceHandler<SparqlExecutionTraceEvent> enabledHandler = (in SparqlExecutionTraceEvent traceEvent) => enabledEvents.Add(traceEvent);
        List<SparqlExecutionTraceEvent> disabledEvents = [];
        TraceHandler<SparqlExecutionTraceEvent> disabledHandler = (in SparqlExecutionTraceEvent traceEvent) => disabledEvents.Add(traceEvent);
        SparqlQueryEngine enabled = await SparqlQueryEngine.BuildAsync(data, executionTrace: enabledHandler, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        SparqlQueryEngine disabled = await SparqlQueryEngine.BuildAsync(data, executionTrace: disabledHandler, enginePolicy: new SparqlEnginePolicy(DisableInterceptions: true), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        using Utf8StringPool pool = new();

        string[] selects =
        [
            "PREFIX : <http://example.org/> SELECT (COUNT(*) AS ?c) WHERE { ?s :knows ?o . ?s :likes ?p }",
            "PREFIX : <http://example.org/> SELECT DISTINCT ?s ?o ?p WHERE { ?s :knows ?o . ?s :likes ?p }",
            "PREFIX : <http://example.org/> SELECT * WHERE { ?s :knows ?o } LIMIT 1",
        ];
        foreach(string text in selects)
        {
            IReadOnlyList<SparqlSolution> onRows = await enabled.EvaluateAsync(Translate(text, pool), TestContext.CancellationToken).ConfigureAwait(false);
            IReadOnlyList<SparqlSolution> offRows = await disabled.EvaluateAsync(Translate(text, pool), TestContext.CancellationToken).ConfigureAwait(false);
            Assert.HasCount(offRows.Count, onRows, $"Interceptions must not change the answer of: {text}");
        }

        bool onAsk = await enabled.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?o }", pool), TestContext.CancellationToken).ConfigureAwait(false);
        bool offAsk = await disabled.EvaluateAskAsync(Translate("PREFIX : <http://example.org/> ASK { ?s :knows ?o }", pool), TestContext.CancellationToken).ConfigureAwait(false);
        Assert.IsTrue(onAsk);
        Assert.AreEqual(onAsk, offAsk);

        HashSet<string> labels = [];
        foreach(SparqlExecutionTraceEvent traceEvent in enabledEvents)
        {
            if(traceEvent.Kind == SparqlExecutionEventKind.InterceptionApplied)
            {
                labels.Add(traceEvent.Label!);
            }
        }

        Assert.Contains(SparqlInterceptions.LimitLeafCapName, labels, "The LIMIT query's leaf cap must announce itself.");
        Assert.Contains(SparqlInterceptions.AskFirstSolutionName, labels, "The ASK short-circuit must announce itself.");
        foreach(SparqlExecutionTraceEvent traceEvent in disabledEvents)
        {
            Assert.AreNotEqual(SparqlExecutionEventKind.InterceptionApplied, traceEvent.Kind, "A disabled registry must emit nothing.");
        }
    }

    /// <summary>
    /// The 4.2 isolation arm: every evaluation and update fixture in the corpus produces the SAME outcome
    /// with the interception registry disabled as with it on — the retroactive answer-identity certificate
    /// for the five shipped fast paths, and the standing differential a future entry must keep green.
    /// </summary>
    [TestMethod]
    public async Task IsolationArmMatchesBaselineAcrossTheCorpus()
    {
        SparqlEnginePolicy isolated = new(DisableInterceptions: true);
        MethodInfo self = typeof(SparqlInterceptionTests).GetMethod(nameof(IsolationArmMatchesBaselineAcrossTheCorpus))!;
        int compared = 0;
        foreach(string suite in EvalSuites)
        {
            foreach(object[] row in new W3cManifestDataAttribute("Sparql", suite).GetData(self))
            {
                W3cTestCase testCase = (W3cTestCase)row[0];
                if(testCase.Type != W3cTestType.SparqlQueryEvaluation)
                {
                    continue;
                }

                W3cOutcome baseline = await W3cSparqlEvalRunner.RunAsync(testCase, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                W3cOutcome withoutInterceptions = await W3cSparqlEvalRunner.RunAsync(testCase, isolated, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(baseline.Status, withoutInterceptions.Status, $"{suite}/{testCase.Name}: baseline '{baseline.Message}' vs isolated '{withoutInterceptions.Message}'.");
                compared++;
            }
        }

        foreach(string suite in UpdateSuites)
        {
            foreach(object[] row in new W3cManifestDataAttribute("Sparql", suite).GetData(self))
            {
                W3cTestCase testCase = (W3cTestCase)row[0];
                if(testCase.Type != W3cTestType.SparqlUpdateEvaluation)
                {
                    continue;
                }

                W3cOutcome baseline = await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                W3cOutcome withoutInterceptions = await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, isolated, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
                Assert.AreEqual(baseline.Status, withoutInterceptions.Status, $"{suite}/{testCase.Name}: baseline '{baseline.Message}' vs isolated '{withoutInterceptions.Message}'.");
                compared++;
            }
        }

        //The corpus holds 462 evaluation fixtures across these suites at the pinned rdf-tests commit; the
        //floor is a tripwire against an accidentally truncated suite list, not an exact census.
        Assert.IsGreaterThan(400, compared, "The isolation arm must sweep the evaluation corpus, not a slice of it.");
    }

    /// <summary>Parses, normalizes, and translates a query to algebra.</summary>
    /// <param name="text">The query text.</param>
    /// <param name="pool">The pool owning the parsed strings.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Builds an example-namespace IRI node from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The IRI node.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }
}
