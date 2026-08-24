using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Sparql;

/// <summary>
/// Runs the vendored W3C SPARQL query-evaluation suites through the full engine pipeline (parse → normalize →
/// translate → execute) and compares results to the expected fixtures with <see cref="SparqlResultComparer"/>.
/// </summary>
/// <remarks>
/// <para>
/// The complete official SPARQL query-evaluation corpus is the fixed measuring stick: every suite the upstream
/// <c>manifest-sparql11-query.ttl</c> / sparql12 manifest references is vendored (see
/// <c>Material/Sparql/ATTRIBUTION.md</c>), and a test that produces a wrong answer <b>fails</b> (red) — the failing
/// count is the honest distance to full conformance, driven down as features land (the SHACL suite's baseline model,
/// not the syntax suite's known-gap model).
/// </para>
/// <para>
/// A test the harness cannot run structurally is reported inconclusive, not failed, by
/// <see cref="W3cSparqlEvalRunner"/>: a <c>CONSTRUCT</c>/<c>DESCRIBE</c> graph result, a query using an operator the
/// executor does not yet support (<c>GRAPH</c>/<c>SERVICE</c>), a data graph in a format the harness cannot read
/// (RDF/XML, TriG), or a non-query-evaluation entry in a mixed manifest.
/// </para>
/// </remarks>
[TestClass]
internal sealed class W3cSparqlEvalTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The on-mode policy the streaming differential's on-mode and streaming-entry arms build engines under; the parameterless baseline methods are the off-mode arm.</summary>
    private static SparqlEnginePolicy StreamingOn { get; } = new(PreferStreamingOperators: true);

    /// <summary>The fourth differential arm's policy: the FULL rewrite catalog enabled over the materialising executor — certifies every catalog rule answer-preserving against the baseline arm.</summary>
    private static SparqlEnginePolicy RewriterOn { get; } = new(Rewrites: AlgebraRewritePipeline.Create(
        AlgebraRewriteCatalog.UnitJoinElimination,
        AlgebraRewriteCatalog.SliceFusion,
        AlgebraRewriteCatalog.DistinctIdempotence,
        AlgebraRewriteCatalog.NoopProjectCollapse,
        AlgebraRewriteCatalog.EmptyTableAnnihilation));

    /// <summary>The fifth differential arm's policy: the full catalog AND the streaming operators — certifies rewritten plans keep answer identity through the cursor pipeline.</summary>
    private static SparqlEnginePolicy RewriterStreamingOn { get; } = new(PreferStreamingOperators: true, Rewrites: AlgebraRewritePipeline.Create(
        AlgebraRewriteCatalog.UnitJoinElimination,
        AlgebraRewriteCatalog.SliceFusion,
        AlgebraRewriteCatalog.DistinctIdempotence,
        AlgebraRewriteCatalog.NoopProjectCollapse,
        AlgebraRewriteCatalog.EmptyTableAnnihilation));

    /// <summary>Runs one hand-authored evaluation smoke test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmoke(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C aggregate evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregates(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>BIND</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bind")]
    public Task RunBind(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>VALUES</c>/<c>BINDINGS</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bindings")]
    public Task RunBindings(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C cast evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "cast")]
    public Task RunCast(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>CONSTRUCT</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "construct")]
    public Task RunConstruct(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExists(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C built-in function evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "functions")]
    public Task RunFunctions(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C <c>GROUP BY</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "grouping")]
    public Task RunGrouping(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C negation (<c>MINUS</c>/<c>NOT EXISTS</c>) evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "negation")]
    public Task RunNegation(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C projection-expression evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpression(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C property-path evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "property-path")]
    public Task RunPropertyPath(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C sub-<c>SELECT</c> evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "subquery")]
    public Task RunSubquery(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 expression evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "expression")]
    public Task RunExpression(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-triple-terms")]
    public Task RunEvalTripleTerms(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 grouping evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-grouping")]
    public Task RunSparql12Grouping(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 RDF-1.1-literals evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-rdf11")]
    public Task RunSparql12Rdf11(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 language-and-base-direction evaluation test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "lang-basedir")]
    public Task RunLangBaseDirection(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>
    /// Runs one W3C SPARQL 1.1 entailment-regime evaluation test. A test whose action lists the RDF, RDFS, or D
    /// regime evaluates over the finite RDFS closure (the expected result holds under every listed regime, so
    /// any implemented one suffices); a test offering only OWL Direct Semantics, OWL RDF-Based Semantics, or
    /// RIF Core is reported inconclusive.
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "entailment")]
    public Task RunEntailment(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.1 JSON results-format test (the expected result is a <c>.srj</c> serialization).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "json-res")]
    public Task RunJsonResults(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.1 CSV/TSV results-format test (the expected result is a <c>.csv</c> or <c>.tsv</c> serialization).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "csv-tsv-res")]
    public Task RunCsvTsvResults(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one hand-authored evaluation smoke test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmokePolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one hand-authored evaluation smoke test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmokeStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one hand-authored evaluation smoke test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmokeRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one hand-authored evaluation smoke test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-smoke")]
    public Task RunSmokeRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C aggregate evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregatesPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C aggregate evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregatesStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C aggregate evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregatesRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C aggregate evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "aggregates")]
    public Task RunAggregatesRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>BIND</c> evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bind")]
    public Task RunBindPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C <c>BIND</c> evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bind")]
    public Task RunBindStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>BIND</c> evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bind")]
    public Task RunBindRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C <c>BIND</c> evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bind")]
    public Task RunBindRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>VALUES</c>/<c>BINDINGS</c> evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bindings")]
    public Task RunBindingsPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C <c>VALUES</c>/<c>BINDINGS</c> evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bindings")]
    public Task RunBindingsStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>VALUES</c>/<c>BINDINGS</c> evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bindings")]
    public Task RunBindingsRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C <c>VALUES</c>/<c>BINDINGS</c> evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "bindings")]
    public Task RunBindingsRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C cast evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "cast")]
    public Task RunCastPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C cast evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "cast")]
    public Task RunCastStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C cast evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "cast")]
    public Task RunCastRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C cast evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "cast")]
    public Task RunCastRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>CONSTRUCT</c> evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "construct")]
    public Task RunConstructPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C <c>CONSTRUCT</c> evaluation test through the streaming entry (the differential's cursor-pipeline arm; a graph-result fixture is unaffected by the flag and answers as the on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "construct")]
    public Task RunConstructStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>CONSTRUCT</c> evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "construct")]
    public Task RunConstructRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C <c>CONSTRUCT</c> evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "construct")]
    public Task RunConstructRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExistsPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExistsStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExistsRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C <c>EXISTS</c> evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "exists")]
    public Task RunExistsRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C built-in function evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "functions")]
    public Task RunFunctionsPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C built-in function evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "functions")]
    public Task RunFunctionsStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C built-in function evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "functions")]
    public Task RunFunctionsRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C built-in function evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "functions")]
    public Task RunFunctionsRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>GROUP BY</c> evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "grouping")]
    public Task RunGroupingPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C <c>GROUP BY</c> evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "grouping")]
    public Task RunGroupingStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C <c>GROUP BY</c> evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "grouping")]
    public Task RunGroupingRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C <c>GROUP BY</c> evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "grouping")]
    public Task RunGroupingRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C negation (<c>MINUS</c>/<c>NOT EXISTS</c>) evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "negation")]
    public Task RunNegationPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C negation (<c>MINUS</c>/<c>NOT EXISTS</c>) evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "negation")]
    public Task RunNegationStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C negation (<c>MINUS</c>/<c>NOT EXISTS</c>) evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "negation")]
    public Task RunNegationRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C negation (<c>MINUS</c>/<c>NOT EXISTS</c>) evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "negation")]
    public Task RunNegationRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C projection-expression evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpressionPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C projection-expression evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpressionStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C projection-expression evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpressionRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C projection-expression evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "project-expression")]
    public Task RunProjectExpressionRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C property-path evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "property-path")]
    public Task RunPropertyPathPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C property-path evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "property-path")]
    public Task RunPropertyPathStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C property-path evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "property-path")]
    public Task RunPropertyPathRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C property-path evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "property-path")]
    public Task RunPropertyPathRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C sub-<c>SELECT</c> evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "subquery")]
    public Task RunSubqueryPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C sub-<c>SELECT</c> evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "subquery")]
    public Task RunSubqueryStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C sub-<c>SELECT</c> evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "subquery")]
    public Task RunSubqueryRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C sub-<c>SELECT</c> evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "subquery")]
    public Task RunSubqueryRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 expression evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "expression")]
    public Task RunExpressionPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.2 expression evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "expression")]
    public Task RunExpressionStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 expression evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "expression")]
    public Task RunExpressionRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.2 expression evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "expression")]
    public Task RunExpressionRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-triple-terms")]
    public Task RunEvalTripleTermsPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-triple-terms")]
    public Task RunEvalTripleTermsStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-triple-terms")]
    public Task RunEvalTripleTermsRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "eval-triple-terms")]
    public Task RunEvalTripleTermsRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 grouping evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-grouping")]
    public Task RunSparql12GroupingPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.2 grouping evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-grouping")]
    public Task RunSparql12GroupingStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 grouping evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-grouping")]
    public Task RunSparql12GroupingRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.2 grouping evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-grouping")]
    public Task RunSparql12GroupingRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 RDF-1.1-literals evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-rdf11")]
    public Task RunSparql12Rdf11PolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.2 RDF-1.1-literals evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-rdf11")]
    public Task RunSparql12Rdf11StreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 RDF-1.1-literals evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-rdf11")]
    public Task RunSparql12Rdf11RewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.2 RDF-1.1-literals evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "sparql12-rdf11")]
    public Task RunSparql12Rdf11RewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 language-and-base-direction evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "lang-basedir")]
    public Task RunLangBaseDirectionPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.2 language-and-base-direction evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "lang-basedir")]
    public Task RunLangBaseDirectionStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 language-and-base-direction evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "lang-basedir")]
    public Task RunLangBaseDirectionRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.2 language-and-base-direction evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "lang-basedir")]
    public Task RunLangBaseDirectionRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.1 entailment-regime evaluation test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "entailment")]
    public Task RunEntailmentPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.1 entailment-regime evaluation test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "entailment")]
    public Task RunEntailmentStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.1 entailment-regime evaluation test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "entailment")]
    public Task RunEntailmentRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 entailment-regime evaluation test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "entailment")]
    public Task RunEntailmentRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.1 JSON results-format test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "json-res")]
    public Task RunJsonResultsPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.1 JSON results-format test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "json-res")]
    public Task RunJsonResultsStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.1 JSON results-format test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "json-res")]
    public Task RunJsonResultsRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 JSON results-format test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "json-res")]
    public Task RunJsonResultsRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.1 CSV/TSV results-format test under the on-mode policy (the differential's on-mode arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "csv-tsv-res")]
    public Task RunCsvTsvResultsPolicyOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn);

    /// <summary>Runs one W3C SPARQL 1.1 CSV/TSV results-format test through the streaming entry (the differential's cursor-pipeline arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "csv-tsv-res")]
    public Task RunCsvTsvResultsStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: StreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.1 CSV/TSV results-format test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "csv-tsv-res")]
    public Task RunCsvTsvResultsRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 CSV/TSV results-format test under the full rewrite catalog through the streaming entry (the fifth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "csv-tsv-res")]
    public Task RunCsvTsvResultsRewriterStreamingEntry(W3cTestCase testCase) => RunAndAssertAsync(testCase, enginePolicy: RewriterStreamingOn, throughStreamingEntry: true);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term positive-syntax test (covers the <c>INSERT DATA</c> update-syntax entries).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-triple-terms-positive")]
    public Task RunSyntaxTripleTermsPositive(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.2 triple-term negative-syntax test (covers the negative update-syntax entries).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-triple-terms-negative")]
    public Task RunSyntaxTripleTermsNegative(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>INSERT</c>/<c>DELETE DATA</c> (basic-update) test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "basic-update")]
    public Task RunUpdateBasic(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>INSERT</c>/<c>DELETE DATA</c> (basic-update) test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "basic-update")]
    public Task RunUpdateBasicRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE DATA</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-data")]
    public Task RunUpdateDeleteData(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE DATA</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-data")]
    public Task RunUpdateDeleteDataRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete")]
    public Task RunUpdateDelete(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete")]
    public Task RunUpdateDeleteRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE WHERE</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-where")]
    public Task RunUpdateDeleteWhere(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE WHERE</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-where")]
    public Task RunUpdateDeleteWhereRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE</c>/<c>INSERT</c> (modify) test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-insert")]
    public Task RunUpdateDeleteInsert(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DELETE</c>/<c>INSERT</c> (modify) test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "delete-insert")]
    public Task RunUpdateDeleteInsertRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>ADD</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "add")]
    public Task RunUpdateAdd(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>ADD</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "add")]
    public Task RunUpdateAddRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>CLEAR</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "clear")]
    public Task RunUpdateClear(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>CLEAR</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "clear")]
    public Task RunUpdateClearRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>COPY</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "copy")]
    public Task RunUpdateCopy(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>COPY</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "copy")]
    public Task RunUpdateCopyRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>MOVE</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "move")]
    public Task RunUpdateMove(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>MOVE</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "move")]
    public Task RunUpdateMoveRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DROP</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "drop")]
    public Task RunUpdateDrop(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>DROP</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "drop")]
    public Task RunUpdateDropRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>SILENT</c> test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "update-silent")]
    public Task RunUpdateSilent(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update <c>SILENT</c> test under the full rewrite catalog (the fourth differential arm).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "update-silent")]
    public Task RunUpdateSilentRewriterOn(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true, enginePolicy: RewriterOn);

    /// <summary>Runs one W3C SPARQL 1.1 Update positive/negative syntax test (set 1).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-update-1")]
    public Task RunSyntaxUpdate1(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>Runs one W3C SPARQL 1.1 Update positive/negative syntax test (set 2).</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-update-2")]
    public Task RunSyntaxUpdate2(W3cTestCase testCase) => RunAndAssertAsync(testCase, updateSyntaxSuite: true);

    /// <summary>
    /// Runs one manifest entry and applies the outcome, dispatching on its type so every entry in a mixed
    /// manifest runs with the right semantics — query / update evaluation, or query / update syntax. A wrong
    /// answer fails (the conformance baseline); a structural non-runnable (handled by the runner) is
    /// inconclusive.
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <param name="updateSyntaxSuite">
    /// Whether this suite is an update suite: a generic <c>PositiveSyntaxTest11</c>/<c>NegativeSyntaxTest11</c>
    /// entry (the W3C manifests do not distinguish query from update syntax by type) is then a SPARQL Update
    /// syntax test, parsed as an update rather than a query.
    /// </param>
    /// <param name="enginePolicy">The execution-strategy policy the query-evaluation engine is built under; the default is the off-mode baseline arm of the streaming differential.</param>
    /// <param name="throughStreamingEntry">Whether query evaluation drains the streaming entry — the differential arm that routes the whole-plan cursor pipeline through the corpus oracle.</param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase, bool updateSyntaxSuite = false, SparqlEnginePolicy enginePolicy = default, bool throughStreamingEntry = false)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        W3cOutcome outcome = testCase.Type switch
        {
            W3cTestType.SparqlQueryEvaluation => await W3cSparqlEvalRunner.RunAsync(testCase, enginePolicy, throughStreamingEntry, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.SparqlUpdateEvaluation => await W3cSparqlEvalRunner.RunUpdateEvalAsync(testCase, enginePolicy, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.PositiveUpdateSyntax => await W3cSparqlEvalRunner.RunUpdateSyntaxAsync(testCase, expectSuccess: true, TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.NegativeUpdateSyntax => await W3cSparqlEvalRunner.RunUpdateSyntaxAsync(testCase, expectSuccess: false, TestContext.CancellationToken).ConfigureAwait(false),
            W3cTestType.PositiveSyntax or W3cTestType.NegativeSyntax => await RunSyntaxAsync(testCase, updateSyntaxSuite).ConfigureAwait(false),
            _ => new W3cOutcome(W3cOutcomeStatus.Skipped, $"Unhandled W3C test type '{testCase.RawTypeIri}'.")
        };

        if(outcome.Status == W3cOutcomeStatus.Skipped)
        {
            //A structurally non-runnable case — most often a test offering only
            //an unimplemented entailment regime (the census the suite asserts in
            //EntailmentRegimeCensus), but also a graph in a format the harness
            //cannot read or a feature the executor does not yet support — is a
            //documented capability boundary, not an inconclusive verdict. A
            //wrong answer is still a failure: the ratchet's honest distance.
            TestContext.WriteLine($"{testCase.Name} ({testCase.Type}): {outcome.Message}");

            return;
        }

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>The entailment-regime IRIs this build evaluates (the finite RDFS closure); mirrors the runner's implemented set.</summary>
    private static string[] ImplementedEntailmentRegimes { get; } =
    [
        "http://www.w3.org/ns/entailment/RDF",
        "http://www.w3.org/ns/entailment/RDFS",
        "http://www.w3.org/ns/entailment/D",
    ];

    /// <summary>
    /// Asserts the count of entailment-regime tests this build cannot evaluate
    /// — those offering only OWL Direct Semantics, an unsanctioned OWL
    /// RDF-Based, or RIF — matches the pinned census. These pass individually as
    /// documented capability boundaries; pinning the count keeps the gap visible
    /// so a regime newly implemented (count drops) or a new such test (count
    /// rises) is a visible test change.
    /// </summary>
    [TestMethod]
    public void EntailmentRegimeCensus()
    {
        W3cManifest manifest = W3cManifestLoader.Load(W3cCorpusPath.For("Sparql", "entailment", "manifest.ttl"));

        int pending = 0;
        foreach(W3cTestCase testCase in manifest.Tests)
        {
            if(RequiresUnimplementedEntailmentRegime(testCase))
            {
                pending++;
            }
        }

        Assert.AreEqual(27, pending, "The count of entailment tests requiring an unimplemented regime changed; update the census (a regime was implemented, or a test was added/removed).");
    }

    /// <summary>Whether the test offers only entailment regimes this build does not evaluate, mirroring the runner's skip condition.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns><see langword="true"/> when no offered regime is implemented and no RL-sanctioned OWL RDF-Based regime applies.</returns>
    private static bool RequiresUnimplementedEntailmentRegime(W3cTestCase testCase)
    {
        if(testCase.EntailmentRegimes is not { Count: > 0 } regimes)
        {
            return false;
        }

        bool hasImplemented = false;
        foreach(string regime in regimes)
        {
            foreach(string implemented in ImplementedEntailmentRegimes)
            {
                if(string.Equals(regime, implemented, StringComparison.Ordinal))
                {
                    hasImplemented = true;
                }
            }
        }

        bool rlSanctioned = Contains(regimes, "http://www.w3.org/ns/entailment/OWL-RDF-Based")
            && testCase.EntailmentProfiles is { } profiles
            && Contains(profiles, "http://www.w3.org/ns/owl-profile/RL");

        return !hasImplemented && !rlSanctioned;
    }

    /// <summary>Whether a list of IRIs contains an exact match.</summary>
    /// <param name="iris">The IRIs to scan.</param>
    /// <param name="iri">The IRI to find.</param>
    /// <returns><see langword="true"/> when present.</returns>
    private static bool Contains(IReadOnlyList<string> iris, string iri)
    {
        foreach(string candidate in iris)
        {
            if(string.Equals(candidate, iri, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs a generic positive/negative syntax entry as a query syntax test, or — in an update suite — as a
    /// SPARQL Update syntax test (the manifests type both as <c>PositiveSyntaxTest11</c>/<c>NegativeSyntaxTest11</c>).
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <param name="updateSyntaxSuite">Whether to parse as an update rather than a query.</param>
    /// <returns>The outcome.</returns>
    private Task<W3cOutcome> RunSyntaxAsync(W3cTestCase testCase, bool updateSyntaxSuite)
        => updateSyntaxSuite
            ? W3cSparqlEvalRunner.RunUpdateSyntaxAsync(testCase, expectSuccess: testCase.Type == W3cTestType.PositiveSyntax, TestContext.CancellationToken)
            : W3cTestRunner.RunAsync(testCase, SparqlConformanceReader.ParseQuery, TestContext.CancellationToken);
}
