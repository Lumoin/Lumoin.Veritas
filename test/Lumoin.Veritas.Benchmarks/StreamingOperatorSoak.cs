using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak characterising the streaming operator pipeline against the materialising executor over the
/// anchor-row workload set: (1) EXISTS-heavy large-graph per-binding probing; (2) the cheap-inner-pattern
/// <c>FILTER(EXISTS)</c> with no enclosing early exit (the reset-dominated shape); (3) the LIMIT+FILTER
/// selective scan (the filter-aware cap); (4) large-scan streaming throughput with no early exit (the
/// per-row cursor chain against the incumbent tight loop); (5) the flat property-table non-terminating
/// shape (the default-flip criterion's row); (6) Slice-over-OrderBy (the breaker-decline row — the
/// interception declines, so the columns should read near-identical). Per row: wall time, allocated bytes,
/// and the result rows for both modes, with an answer-agreement marker. Line-oriented output for
/// hand-collation; answers must MATCH on every row.
/// </summary>
internal static class StreamingOperatorSoak
{
    /// <summary>The example-namespace prefix the soak's queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>Runs the six anchor rows.</summary>
    /// <returns>The asynchronous run.</returns>
    public static async Task RunStreamingOperatorSoak()
    {
        Console.WriteLine("[streaming] anchor rows: off = materialising executor, on = streaming pipeline; alloc per full run");

        await RunExistsHeavyRowAsync().ConfigureAwait(false);
        await RunCheapInnerExistsRowAsync().ConfigureAwait(false);
        await RunLimitFilterRowAsync().ConfigureAwait(false);
        await RunLargeScanRowAsync().ConfigureAwait(false);
        await RunPropertyTableRowAsync().ConfigureAwait(false);
        await RunSliceOverOrderByRowAsync().ConfigureAwait(false);
    }

    /// <summary>Anchor 1: EXISTS-heavy — M outer rows each probing a selective inner over a large graph (the per-binding reuse + seeding against per-row synthesize/normalize/evaluate).</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunExistsHeavyRowAsync()
    {
        List<DataTriple> data = new(41_000);
        for(int i = 0; i < 40_000; i++)
        {
            data.Add(new DataTriple(Iri($"n{i:D5}"), Iri("edge"), Iri($"n{(i + 1) % 40_000:D5}")));
        }

        for(int i = 0; i < 1_000; i++)
        {
            data.Add(new DataTriple(Iri($"m{i:D4}"), Iri("sel"), Iri($"n{(i * 37) % 40_000:D5}")));
        }

        await MeasureRowAsync("exists-heavy 1k-bindings/40k-graph", data, "SELECT * WHERE { ?m :sel ?x FILTER EXISTS { ?x :edge ?y } }").ConfigureAwait(false);
    }

    /// <summary>Anchor 2: the cheap-inner-pattern <c>FILTER(EXISTS)</c> with no enclosing early exit — the reset overhead dominates, the shape the reuse mandate serves.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunCheapInnerExistsRowAsync()
    {
        List<DataTriple> data = new(5_100);
        for(int i = 0; i < 5_000; i++)
        {
            data.Add(new DataTriple(Iri($"m{i:D4}"), Iri("sel"), Iri($"v{i % 100:D3}")));
        }

        for(int i = 0; i < 100; i++)
        {
            data.Add(new DataTriple(Iri($"v{i:D3}"), Iri("tag"), Iri("t")));
        }

        await MeasureRowAsync("cheap-inner 5k-bindings/tiny-inner", data, "SELECT * WHERE { ?m :sel ?v FILTER EXISTS { ?v :tag ?t } }").ConfigureAwait(false);
    }

    /// <summary>Anchor 3: the LIMIT+FILTER selective scan — the filter-aware cap the leaf row cap cannot serve.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunLimitFilterRowAsync()
    {
        List<DataTriple> data = new(200_000);
        for(int i = 0; i < 200_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("p"), Iri(i % 100 == 0 ? "rare" : "common")));
        }

        await MeasureRowAsync("limit+filter 200k-scan/limit-5", data, "SELECT * WHERE { ?s :p ?v FILTER(?v = :rare) } LIMIT 5").ConfigureAwait(false);
    }

    /// <summary>Anchor 4: large-scan streaming throughput with NO early exit — the per-row cursor chain against the incumbent streaming entry's tight loop (consumer 2's SSE/paging use).</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunLargeScanRowAsync()
    {
        List<DataTriple> data = new(500_000);
        for(int i = 0; i < 500_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("p"), Iri($"o{i:D6}")));
        }

        await MeasureStreamingEntryRowAsync("large-scan 500k full drain", data, "SELECT * WHERE { ?s :p ?o }").ConfigureAwait(false);
    }

    /// <summary>Anchor 5 (the default-flip criterion): the flat property-table non-terminating shape — a three-arm star fully drained, on vs off.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunPropertyTableRowAsync()
    {
        List<DataTriple> data = new(150_000);
        for(int i = 0; i < 50_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D5}"), Iri("p1"), Iri($"a{i:D5}")));
            data.Add(new DataTriple(Iri($"s{i:D5}"), Iri("p2"), Iri($"b{i:D5}")));
            data.Add(new DataTriple(Iri($"s{i:D5}"), Iri("p3"), Iri($"c{i:D5}")));
        }

        await MeasureRowAsync("property-table 50k-rows full drain", data, "SELECT * WHERE { ?s :p1 ?a . ?s :p2 ?b . ?s :p3 ?c }").ConfigureAwait(false);
    }

    /// <summary>Anchor 6: Slice-over-OrderBy — the breaker-decline row; the interception declines, so on-mode should track off-mode.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunSliceOverOrderByRowAsync()
    {
        List<DataTriple> data = new(100_000);
        for(int i = 0; i < 100_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{(i * 7919) % 100_000:D6}"), Iri("p"), Iri($"o{i:D6}")));
        }

        await MeasureRowAsync("slice-over-orderby 100k/limit-10", data, "SELECT * WHERE { ?s :p ?o } ORDER BY ?s LIMIT 10").ConfigureAwait(false);
    }

    /// <summary>Measures one anchor row through <c>EvaluateAsync</c> under both modes (warm once, then timed), printing wall/alloc/rows with the agreement marker.</summary>
    /// <param name="label">The row label.</param>
    /// <param name="data">The data graph.</param>
    /// <param name="query">The query (without the shared prefix).</param>
    /// <returns>The asynchronous run.</returns>
    private static async Task MeasureRowAsync(string label, List<DataTriple> data, string query)
    {
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: new SparqlEnginePolicy(PreferStreamingOperators: true)).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(query, pool);

        (SoakSample offSample, int offRows) = await TimeEvaluateAsync(off, algebra).ConfigureAwait(false);
        (SoakSample onSample, int onRows) = await TimeEvaluateAsync(on, algebra).ConfigureAwait(false);

        string agreement = offRows == onRows ? "MATCH" : "MISMATCH";
        Console.WriteLine($"[streaming] {label,-36} | off {offSample.Milliseconds,9:F1} ms {offSample.AllocCell,13} | on {onSample.Milliseconds,9:F1} ms {onSample.AllocCell,13} | rows {onRows,8:N0} {agreement}");
    }

    /// <summary>Measures one anchor row through the STREAMING ENTRY under both modes (the peel path off, the pipeline on), printing wall/alloc/rows with the agreement marker.</summary>
    /// <param name="label">The row label.</param>
    /// <param name="data">The data graph.</param>
    /// <param name="query">The query (without the shared prefix).</param>
    /// <returns>The asynchronous run.</returns>
    private static async Task MeasureStreamingEntryRowAsync(string label, List<DataTriple> data, string query)
    {
        SparqlQueryEngine off = await SparqlQueryEngine.BuildAsync(data).ConfigureAwait(false);
        SparqlQueryEngine on = await SparqlQueryEngine.BuildAsync(data, enginePolicy: new SparqlEnginePolicy(PreferStreamingOperators: true)).ConfigureAwait(false);

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate(query, pool);

        (SoakSample offSample, int offRows) = await TimeStreamingEntryAsync(off, algebra).ConfigureAwait(false);
        (SoakSample onSample, int onRows) = await TimeStreamingEntryAsync(on, algebra).ConfigureAwait(false);

        string agreement = offRows == onRows ? "MATCH" : "MISMATCH";
        Console.WriteLine($"[streaming] {label,-36} | off {offSample.Milliseconds,9:F1} ms {offSample.AllocCell,13} | on {onSample.Milliseconds,9:F1} ms {onSample.AllocCell,13} | rows {onRows,8:N0} {agreement}");
    }

    /// <summary>Times one warmed <c>EvaluateAsync</c> run through the shared self-validating window.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="algebra">The translated plan.</param>
    /// <returns>The sample and the row count.</returns>
    private static async Task<(SoakSample Sample, int Rows)> TimeEvaluateAsync(SparqlQueryEngine engine, AlgebraOperator algebra)
    {
        _ = await engine.EvaluateAsync(algebra).ConfigureAwait(false);

        SoakWindow window = SoakWindow.Open();
        IReadOnlyList<SparqlSolution> rows = await engine.EvaluateAsync(algebra).ConfigureAwait(false);

        return (window.Close(), rows.Count);
    }

    /// <summary>Times one warmed streaming-entry drain through the shared self-validating window.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="algebra">The translated plan.</param>
    /// <returns>The sample and the row count.</returns>
    private static async Task<(SoakSample Sample, int Rows)> TimeStreamingEntryAsync(SparqlQueryEngine engine, AlgebraOperator algebra)
    {
        int warm = 0;
        await foreach(SparqlSolution _ in engine.EvaluateStreamingAsync(algebra).ConfigureAwait(false))
        {
            warm++;
        }

        SoakWindow window = SoakWindow.Open();
        int rows = 0;
        await foreach(SparqlSolution _ in engine.EvaluateStreamingAsync(algebra).ConfigureAwait(false))
        {
            rows++;
        }

        return (window.Close(), rows);
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Parses, normalizes, and translates a query (the shared example prefix prepended) to its algebra.</summary>
    /// <param name="text">The query text without the prefix.</param>
    /// <param name="pool">The pool the parse interns into.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes("PREFIX : <http://example.org/> " + text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }
}
