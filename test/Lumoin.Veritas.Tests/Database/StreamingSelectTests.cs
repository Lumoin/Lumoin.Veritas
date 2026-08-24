using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Tests.Database;

/// <summary>
/// The streaming SELECT producer (<see cref="VeritasEngine.StreamSelect"/>) must yield exactly the materialized
/// <see cref="VeritasEngine.QueryAsync"/> result — for the truly-incremental shapes (bare/projected BGP, LIMIT,
/// OFFSET) and the fallback shapes (ORDER BY, DISTINCT). This equivalence is the safety net on the streaming
/// fast path: a divergence means the fast path computed a different answer than the conformance-tested path.
/// </summary>
[TestClass]
internal sealed class StreamingSelectTests
{
    /// <summary>The example-namespace prefix the data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The MSTest execution context, for the per-test cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>A named node in the example namespace for a local name.</summary>
    /// <param name="local">The local name.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From(Ex + local));
    }

    /// <summary>Renders a solution sequence canonically: one line per solution, its bindings sorted within the line, so two sequences compare equal exactly when they hold the same solutions in the same order.</summary>
    /// <param name="solutions">The solutions to render.</param>
    /// <returns>The canonical rendering.</returns>
    private static string Render(IReadOnlyList<SparqlSolution> solutions)
    {
        StringBuilder builder = new();
        foreach(SparqlSolution solution in solutions)
        {
            List<string> cells = [];
            foreach(SparqlBinding binding in solution.Bindings)
            {
                cells.Add($"{binding.Variable.Name}={binding.Value}");
            }

            cells.Sort(StringComparer.Ordinal);
            builder.Append(string.Join("|", cells)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Across BGP shapes — bare, projected, LIMIT, OFFSET (streamed) and ORDER BY, DISTINCT (fallback) — the streamed solutions equal the materialized ones.</summary>
    [TestMethod]
    public async Task StreamedSelectMatchesMaterializedAcrossShapes()
    {
        IReadOnlyList<DataTriple> seed =
        [
            new DataTriple(Iri("x"), Iri("p"), Iri("a")),
            new DataTriple(Iri("x"), Iri("p"), Iri("b")),
            new DataTriple(Iri("y"), Iri("p"), Iri("c")),
            new DataTriple(Iri("y"), Iri("q"), Iri("d")),
        ];
        VeritasEngine engine = await VeritasEngine.OpenAsync(seed, cancellationToken: TestContext.CancellationToken).ConfigureAwait(false);
        await using var scope = engine.ConfigureAwait(false);

        string[] queries =
        [
            "SELECT * WHERE { ?s ?p ?o }",
            $"SELECT ?o WHERE {{ <{Ex}x> <{Ex}p> ?o }}",
            $"SELECT ?s ?o WHERE {{ ?s <{Ex}p> ?o }}",
            $"SELECT ?s ?o WHERE {{ ?s <{Ex}p> ?o }} LIMIT 2",
            $"SELECT ?s ?o WHERE {{ ?s <{Ex}p> ?o }} OFFSET 1",
            $"SELECT ?o WHERE {{ ?s <{Ex}p> ?o }} ORDER BY ?o",
            "SELECT DISTINCT ?p WHERE { ?s ?p ?o }",
        ];

        bool sawNonEmpty = false;
        foreach(string query in queries)
        {
            IReadOnlyList<SparqlSolution> materialized = (await engine
                .QueryAsync(Utf8Strings.From(query), cancellationToken: TestContext.CancellationToken)
                .ConfigureAwait(false)).Bindings!.Solutions;

            List<SparqlSolution> streamed = [];
            using(VeritasSelectStream stream = await engine.StreamSelectAsync(Utf8Strings.From(query), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
            {
                await foreach(SparqlSolution solution in stream.Solutions.WithCancellation(TestContext.CancellationToken).ConfigureAwait(false))
                {
                    streamed.Add(solution);
                }
            }

            Assert.AreEqual(Render(materialized), Render(streamed), $"Streamed and materialized results differ for: {query}");
            sawNonEmpty = sawNonEmpty || materialized.Count > 0;
        }

        Assert.IsTrue(sawNonEmpty, "The fixture must produce results so the streaming fast path is actually exercised.");
    }
}
