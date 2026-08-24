using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Diagnostic soak for the 3B decision: how much of a query's cost is the row-materialization 3B
/// removes — the BGP flatten (decode every intermediate <c>TermId</c> to an <c>RdfTerm</c> row) plus
/// the per-operator <c>Merge</c> — versus work any representation must do.
/// </summary>
/// <remarks>
/// <para>
/// Per rung it runs two queries over the same fan-out graph: a FULL two-hop join that materialises the
/// whole intermediate (<c>?a knows ?b . ?b knows ?c</c>), and the SAME join with a FILTER pinning
/// <c>?a</c> to one subject so the output is tiny. The current engine evaluates the FILTER after the
/// join, so it decodes the whole intermediate and discards almost all of it — the
/// decode-everything-then-discard shape 3B kills by carrying columns through and decoding only
/// survivors. The gap between the FILTER query's allocation and its tiny output is the size of the
/// 3B win; if the FILTER query allocates close to the FULL query, materialisation dominates.
/// </para>
/// <para>Release, line-oriented output for hand-collation. Allocation via <see cref="GC.GetTotalAllocatedBytes(bool)"/>.</para>
/// </remarks>
internal static class SparqlPipelineSoak
{
    /// <summary>The example namespace the generated data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>Runs the soak ladder.</summary>
    /// <returns>A task that completes when the ladder has run and reported.</returns>
    public static async Task RunSparqlPipelineSoakAsync()
    {
        await RunConfiguration(persons: 2_000, fanOut: 8).ConfigureAwait(false);
        await RunConfiguration(persons: 5_000, fanOut: 10).ConfigureAwait(false);
        await RunConfiguration(persons: 10_000, fanOut: 12).ConfigureAwait(false);
    }

    /// <summary>Generates, measures, and reports one ladder rung.</summary>
    /// <param name="persons">The number of person subjects.</param>
    /// <param name="fanOut">The out-degree of each person's <c>knows</c> edges (also the in-degree on the ring).</param>
    /// <returns>A task that completes when the rung is reported.</returns>
    private static async Task RunConfiguration(int persons, int fanOut)
    {
        List<DataTriple> data = Generate(persons, fanOut);
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data, cancellationToken: CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine($"[pipeline-soak] persons={persons:N0} fanOut={fanOut} triples={data.Count:N0}");

        string fullQuery = $"PREFIX : <{Ex}> SELECT * WHERE {{ ?a :knows ?b . ?b :knows ?c }}";
        string filterQuery = $"PREFIX : <{Ex}> SELECT * WHERE {{ ?a :knows ?b . ?b :knows ?c FILTER(?a = :p0) }}";

        Measurement full = await MeasureAsync(engine, fullQuery).ConfigureAwait(false);
        Measurement filtered = await MeasureAsync(engine, filterQuery).ConfigureAwait(false);

        Console.WriteLine($"[pipeline-soak]   FULL   (materialises all): rows={full.Rows,12:N0}  {full.Milliseconds,9:F1} ms  alloc {full.AllocMiB,9:F1} MiB  {full.BytesPerRow,6:F0} B/row");
        Console.WriteLine($"[pipeline-soak]   FILTER (?a=:p0, tiny out): rows={filtered.Rows,12:N0}  {filtered.Milliseconds,9:F1} ms  alloc {filtered.AllocMiB,9:F1} MiB");
        double intermediate = full.Rows;
        double wastedFraction = intermediate > 0 ? (intermediate - filtered.Rows) / intermediate : 0;
        Console.WriteLine($"[pipeline-soak]   FILTER allocates {filtered.AllocMiB / Math.Max(full.AllocMiB, 0.001) * 100,5:F1}% of FULL for {filtered.Rows * 100.0 / Math.Max(intermediate, 1),6:F3}% of the rows  (decode-then-discard: {wastedFraction * 100,5:F1}% of the intermediate is thrown away)");

        //The columnar island (3B.1): a single-BGP scan whose operators are all columnar (DISTINCT, LIMIT) carries
        //encoded ids through and decodes only the surviving output rows. Allocation should track the OUTPUT, not the
        //scanned input — the opposite of the row engine, which decoded every scanned row into a heap solution first.
        string distinctQuery = $"PREFIX : <{Ex}> SELECT DISTINCT ?city WHERE {{ ?a :livesIn ?city }}";
        string limitQuery = $"PREFIX : <{Ex}> SELECT * WHERE {{ ?a :knows ?b }} LIMIT 5";

        Measurement distinct = await MeasureAsync(engine, distinctQuery).ConfigureAwait(false);
        Measurement limited = await MeasureAsync(engine, limitQuery).ConfigureAwait(false);

        Console.WriteLine($"[pipeline-soak]   DISTINCT ?city (scan {persons:N0} -> {distinct.Rows} out): {distinct.Milliseconds,9:F1} ms  alloc {distinct.AllocMiB,9:F2} MiB  ({(double)distinct.AllocatedBytes / persons,5:F1} B per scanned row)");
        Console.WriteLine($"[pipeline-soak]   LIMIT 5 over :knows (scan {persons * (long)fanOut:N0} -> {limited.Rows} out): {limited.Milliseconds,9:F1} ms  alloc {limited.AllocMiB,9:F2} MiB");
    }

    /// <summary>Parses, translates, and evaluates a query, reporting rows, wall-clock, and allocation; warmed once before timing.</summary>
    /// <param name="engine">The engine to evaluate against.</param>
    /// <param name="queryText">The SPARQL query text.</param>
    /// <returns>The measurement.</returns>
    private static async Task<Measurement> MeasureAsync(SparqlQueryEngine engine, string queryText)
    {
        AlgebraOperator algebra = Translate(queryText);

        //Warm once so the timed run measures steady-state evaluation, not first-call JIT.
        _ = await engine.EvaluateAsync(algebra, CancellationToken.None).ConfigureAwait(false);

        long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
        long start = Stopwatch.GetTimestamp();
        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, CancellationToken.None).ConfigureAwait(false);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(start);
        long alloc = GC.GetTotalAllocatedBytes(precise: true) - allocBefore;

        return new Measurement(solutions.Count, elapsed.TotalMilliseconds, alloc);
    }

    /// <summary>Parses, normalizes, and translates a query string to algebra (fresh pool per call).</summary>
    /// <param name="text">The SPARQL query text.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text)
    {
        using Utf8StringPool pool = new();
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>Generates a ring fan-out graph: each person <c>knows</c> the next <paramref name="fanOut"/> persons, and <c>livesIn</c> one of a few cities.</summary>
    /// <param name="persons">The number of persons.</param>
    /// <param name="fanOut">The out-degree of the <c>knows</c> edges.</param>
    /// <returns>The data triples.</returns>
    private static List<DataTriple> Generate(int persons, int fanOut)
    {
        NamedNode knows = Iri("knows");
        NamedNode livesIn = Iri("livesIn");
        int cityCount = Math.Max(1, persons / 100);
        List<DataTriple> data = new(persons * (fanOut + 1));
        NamedNode[] person = new NamedNode[persons];
        for(int i = 0; i < persons; i++)
        {
            person[i] = Iri("p" + i);
        }

        for(int i = 0; i < persons; i++)
        {
            for(int j = 1; j <= fanOut; j++)
            {
                data.Add(new DataTriple(person[i], knows, person[(i + j) % persons]));
            }

            data.Add(new DataTriple(person[i], livesIn, Iri("city" + (i % cityCount))));
        }

        return data;
    }

    /// <summary>Builds an example-namespace named node from a local name.</summary>
    /// <param name="local">The local name appended to the example prefix.</param>
    /// <returns>The named node.</returns>
    private static NamedNode Iri(string local)
    {
        return new NamedNode(Utf8Strings.From(Ex + local));
    }

    /// <summary>One query measurement: output rows, wall-clock, and bytes allocated.</summary>
    /// <param name="Rows">The output-solution count.</param>
    /// <param name="Milliseconds">The timed-run wall-clock in milliseconds.</param>
    /// <param name="AllocatedBytes">The bytes allocated during the timed run.</param>
    private readonly record struct Measurement(int Rows, double Milliseconds, long AllocatedBytes)
    {
        /// <summary>The bytes allocated, in mebibytes.</summary>
        public double AllocMiB => AllocatedBytes / (1024.0 * 1024.0);

        /// <summary>The bytes allocated per output row, or zero when there are no rows.</summary>
        public double BytesPerRow => Rows > 0 ? (double)AllocatedBytes / Rows : 0;
    }
}
