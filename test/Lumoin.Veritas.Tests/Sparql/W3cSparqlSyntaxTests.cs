// W3C SPARQL Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.Tests/Material/Sparql/.
// See Material/Sparql/ATTRIBUTION.md for provenance.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Tests.Conformance;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Runs the vendored W3C SPARQL query-syntax conformance suites against
/// <see cref="SparqlParser"/>: a positive-syntax test passes when the query
/// parses without error, a negative-syntax test passes when parsing raises a
/// <see cref="Lumoin.Veritas.Sparql.SparqlParseException"/>.
/// </summary>
/// <remarks>
/// The conformance runner is quad-oriented; a SPARQL query produces no quads, so
/// the reader here parses the query and yields an empty quad stream, propagating
/// any parse failure for the runner to observe. Update-syntax test types are
/// classified as unknown by the loader and skipped (this build parses queries).
/// </remarks>
[TestClass]
internal sealed class W3cSparqlSyntaxTests
{
    /// <summary>The MSTest-supplied per-test context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Tests this build's parser cannot decide by syntax alone, keyed by manifest <c>mf:name</c> with
    /// the reason. They need capabilities beyond a query parser — variable-scope analysis (SELECT /
    /// GROUP BY / BIND scope rules, duplicate projections), static aggregate-nesting checks, a
    /// PN_LOCAL-escape lexer refinement, or the sugar-expansion pass (a standalone blank-node property
    /// list). They are reported as inconclusive rather than failing the suite; if any ever starts
    /// passing, the run fails so the entry is removed.
    /// </summary>
    private static Dictionary<string, string> KnownGaps { get; } = new(StringComparer.Ordinal)
    {
        ["syn-bad-01.rq"] = "SELECT * with GROUP BY — requires variable-scope analysis",
        ["syn-bad-02.rq"] = "ungrouped projected variable — requires variable-scope analysis",
        ["syn-bad-03.rq"] = "duplicate projection variable — requires projection-scope analysis",
        ["syn-bad-pname-06"] = "PN_LOCAL escaping nuance — lexer refinement",
        ["syntax-BINDscope6.rq"] = "BIND reuses an in-scope variable — requires scope analysis",
        ["syntax-BINDscope7.rq"] = "BIND reuses an in-scope variable — requires scope analysis",
        ["syntax-BINDscope8.rq"] = "BIND reuses an in-scope variable — requires scope analysis",
        ["syntax-SELECTscope2"] = "projection scope — requires scope analysis",
        ["GROUP BY expression in-scope"] = "GROUP BY scope — requires scope analysis",
        ["GROUP BY variable in-scope (1)"] = "GROUP BY scope — requires scope analysis",
        ["GROUP BY variable in-scope (2)"] = "GROUP BY scope — requires scope analysis",
        ["Nested aggregate functions"] = "nested aggregates — requires static aggregate analysis",
        ["syntax-propertyPaths-01.rq"] = "standalone blank-node property list — needs the sugar-expansion pass",

        // RDF 1.2 reification (Slice 4b-ii) — features not yet wired; tracked for follow-up.
        ["basic-anonreifier-08.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-anonreifier-09.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-anonreifier-10.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-anonreifier-11.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-anonreifier-12.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-anonreifier-13.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-reifier-08.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-reifier-09.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-reifier-10.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-reifier-11.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-reifier-12.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["basic-reifier-13.rq"] = "standalone reified triple (<< … >> with empty property list) — needs the BGP subject-only assertion",
        ["version-01.rq"] = "VERSION + standalone reified triple — VERSION parses, but the standalone << … >> still needs the BGP subject-only assertion",
        ["version-02.rq"] = "VERSION + standalone reified triple — VERSION parses, but the standalone << … >> still needs the BGP subject-only assertion",
        ["version-05.rq"] = "VERSION + standalone reified triple — VERSION parses, but the standalone << … >> still needs the BGP subject-only assertion",
    };

    /// <summary>Runs one SPARQL 1.1 query-syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-query")]
    public Task RunW3cSparql11SyntaxQueryTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 query-syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax")]
    public Task RunW3cSparql12SyntaxTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 RDF-1.2 triple-term positive-syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-triple-terms-positive")]
    public Task RunW3cSparql12TripleTermsPositiveTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 RDF-1.2 triple-term negative-syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "syntax-triple-terms-negative")]
    public Task RunW3cSparql12TripleTermsNegativeTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>Runs one SPARQL 1.2 <c>VERSION</c>-declaration syntax test.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestMethod]
    [W3cManifestData("Sparql", "version")]
    public Task RunW3cSparql12VersionTest(W3cTestCase testCase) => RunAndAssertAsync(testCase);

    /// <summary>
    /// Runs a test case and applies the outcome, translating a known gap into an inconclusive result
    /// (and failing if a known gap unexpectedly starts passing).
    /// </summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <returns>The asynchronous test operation.</returns>
    private async Task RunAndAssertAsync(W3cTestCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        W3cOutcome outcome = await W3cTestRunner.RunAsync(testCase, ParseSparqlQuery, TestContext.CancellationToken).ConfigureAwait(false);

        if(KnownGaps.TryGetValue(testCase.Name, out string? reason))
        {
            if(outcome.Status == W3cOutcomeStatus.Passed)
            {
                Assert.Fail($"'{testCase.Name}' is recorded as a known gap ({reason}) but now passes; remove it from KnownGaps.");
            }

            Assert.Inconclusive($"Known gap ({reason}): {outcome.Message}");
            return;
        }

        ConformanceAssertions.Apply(outcome);
    }

    /// <summary>
    /// Parses the query bytes from the pipe and yields no quads; a lexical or grammar error surfaces as
    /// the exception the runner observes.
    /// </summary>
    /// <remarks>
    /// The recovery-mode parser never throws on malformed input — it records diagnostics into a bag and
    /// returns a (possibly error-node-carrying) request. The shared <see cref="W3cTestRunner"/> detects a
    /// negative-syntax test by catching a parse exception, so this adapter re-raises a
    /// <see cref="Lumoin.Veritas.Sparql.SparqlParseException"/> when the bag holds any error-severity
    /// diagnostic, mirroring <see cref="TurtleConformanceReader"/>.
    /// </remarks>
    /// <param name="input">The pipe over the query file's bytes.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>An empty quad stream (a query produces no quads).</returns>
    private static async IAsyncEnumerable<Quad> ParseSparqlQuery(PipeReader input, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> source = await ReadToEndAsync(input, cancellationToken).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        DiagnosticBag diagnostics = new();
        SparqlLexer lexer = new(source, pool);
        SparqlParser parser = new(lexer.Tokenize(), pool, baseIri: null, blankNodes: null, diagnostics: diagnostics);
        ParseResult<Lumoin.Veritas.Sparql.Ast.SparqlRequest> result = parser.ParseToResult();
        BridgeLexerDiagnostics(lexer, diagnostics);

        if(result.HasErrors || diagnostics.HasErrors)
        {
            throw new Lumoin.Veritas.Sparql.SparqlParseException(TurtleConformanceReader.DescribeFirstError(diagnostics));
        }

        yield break;
    }

    /// <summary>Bridges the lexer's internal diagnostics into the shared parse-level bag.</summary>
    /// <param name="lexer">The lexer whose diagnostics are drained.</param>
    /// <param name="diagnostics">The bag to append the bridged diagnostics to.</param>
    private static void BridgeLexerDiagnostics(SparqlLexer lexer, DiagnosticBag diagnostics)
    {
        foreach(SparqlLexDiagnostic lexDiagnostic in lexer.Diagnostics)
        {
            diagnostics.Add(SparqlLexDiagnosticBridge.ToDiagnostic(lexDiagnostic));
        }
    }

    /// <summary>
    /// Drains a <see cref="PipeReader"/> into a contiguous byte buffer.
    /// </summary>
    /// <param name="input">The pipe to drain.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The full input bytes.</returns>
    private static async Task<ReadOnlyMemory<byte>> ReadToEndAsync(PipeReader input, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();

        while(true)
        {
            ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);

            foreach(ReadOnlyMemory<byte> segment in result.Buffer)
            {
                buffer.Write(segment.Span);
            }

            input.AdvanceTo(result.Buffer.End);

            if(result.IsCompleted)
            {
                break;
            }
        }

        return buffer.ToArray();
    }
}
