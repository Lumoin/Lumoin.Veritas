using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Algebra.Rewriting;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak characterising the algebra rewrite catalog against the un-rewritten baseline over the anchor-row
/// set: (1) R2 nested-slice fusion over a large scan — the fused window becomes leaf-cappable where the
/// nested one drains its whole inner window; (2) R5 empty-join annihilation — the dead scan is never
/// opened; (3) R4 no-op projection collapse — expected a structural wash, measured to document it; (4) the
/// flat property-table star and (5) the large full scan — the no-regression rows where every rule declines
/// and only the pass overhead shows; (6) a bare-BGP ASK — the smallest plan's worst-case pass overhead.
/// Per row: wall time, allocated bytes, and the result rows for catalog-off (the Empty pipeline) versus
/// catalog-on (all five rules), same engine, per-call override — with an answer-agreement marker.
/// Line-oriented output for hand-collation; answers must MATCH on every row.
/// </summary>
internal static class RewriterSoak
{
    /// <summary>The example-namespace prefix the soak's queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The full catalog in order — the measured pipeline.</summary>
    private static AlgebraRewritePipeline FullCatalog { get; } = AlgebraRewritePipeline.Create(
        AlgebraRewriteCatalog.UnitJoinElimination,
        AlgebraRewriteCatalog.SliceFusion,
        AlgebraRewriteCatalog.DistinctIdempotence,
        AlgebraRewriteCatalog.NoopProjectCollapse,
        AlgebraRewriteCatalog.EmptyTableAnnihilation);

    /// <summary>Runs the six anchor rows.</summary>
    /// <returns>The asynchronous run.</returns>
    public static async Task RunRewriterSoak()
    {
        Console.WriteLine("[rewriter] anchor rows: off = Empty pipeline, on = full catalog (per-call override, same engine); alloc per full run");

        await RunNestedSliceRowAsync().ConfigureAwait(false);
        await RunEmptyJoinRowAsync().ConfigureAwait(false);
        await RunProjectCollapseRowAsync().ConfigureAwait(false);
        await RunPropertyTableRowAsync().ConfigureAwait(false);
        await RunLargeScanRowAsync().ConfigureAwait(false);
        await RunBareAskRowAsync().ConfigureAwait(false);
    }

    /// <summary>Anchor 1 (R2): nested slices over a 200k scan — off drains the inner 100k window, on fuses to a 5-row window the leaf cap serves.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunNestedSliceRowAsync()
    {
        List<DataTriple> data = new(200_000);
        for(int i = 0; i < 200_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("p"), Iri($"o{i:D6}")));
        }

        Bgp bgp = new([new TriplePattern(default, Var("s"), Var("p"), Var("o"))]);
        Slice nested = new(new Slice(bgp, Offset: 0, Limit: 100_000), Offset: 0, Limit: 5);

        await MeasureRowAsync("r2 nested-slice 200k/inner-100k/outer-5", data, nested).ConfigureAwait(false);
    }

    /// <summary>Anchor 2 (R5): a join with a zero-row inline table over a 200k scan — off drains the scan into a dead join, on never opens it.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunEmptyJoinRowAsync()
    {
        List<DataTriple> data = new(200_000);
        for(int i = 0; i < 200_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("p"), Iri($"o{i:D6}")));
        }

        Bgp bgp = new([new TriplePattern(default, Var("s"), Var("p"), Var("o"))]);
        Join deadJoin = new(new Table(new ValuesClause(default, [new SparqlVariable(Utf8Strings.From("x"))], [])), bgp);

        await MeasureRowAsync("r5 empty-join 200k dead scan", data, deadJoin).ConfigureAwait(false);
    }

    /// <summary>Anchor 3 (R4): a no-op projection under a join — expected a structural wash (the collapse buys shape, not work); measured to document it.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunProjectCollapseRowAsync()
    {
        List<DataTriple> data = new(100_000);
        for(int i = 0; i < 50_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D5}"), Iri("p1"), Iri($"a{i % 500:D3}")));
            data.Add(new DataTriple(Iri($"a{i % 500:D3}"), Iri("p2"), Iri("t")));
        }

        Bgp left = new([new TriplePattern(default, Var("s"), Var("q"), Var("a"))]);
        Bgp right = new([new TriplePattern(default, Var("a"), Var("r"), Var("t"))]);
        Join joined = new(new Project(left, [new SparqlVariable(Utf8Strings.From("s")), new SparqlVariable(Utf8Strings.From("q")), new SparqlVariable(Utf8Strings.From("a"))]), right);

        await MeasureRowAsync("r4 noop-project join 100k (wash)", data, joined).ConfigureAwait(false);
    }

    /// <summary>Anchor 4 (no-regression): the flat property-table star — every rule declines; only the pass overhead can show.</summary>
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

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :p1 ?a . ?s :p2 ?b . ?s :p3 ?c }", pool);

        await MeasureRowAsync("property-table 50k star (no-regression)", data, algebra).ConfigureAwait(false);
    }

    /// <summary>Anchor 5 (no-regression): the 500k full scan — the large-plan pass overhead against a long drain.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunLargeScanRowAsync()
    {
        List<DataTriple> data = new(500_000);
        for(int i = 0; i < 500_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("p"), Iri($"o{i:D6}")));
        }

        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("SELECT * WHERE { ?s :p ?o }", pool);

        await MeasureRowAsync("large-scan 500k full drain (no-regr.)", data, algebra).ConfigureAwait(false);
    }

    /// <summary>Anchor 6: a bare-BGP ASK — the smallest plan, so the whole difference is the pass over one node against a near-zero evaluation.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunBareAskRowAsync()
    {
        List<DataTriple> data = new(1_000);
        for(int i = 0; i < 1_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D4}"), Iri("p"), Iri($"o{i:D4}")));
        }

        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data).ConfigureAwait(false);
        using Utf8StringPool pool = new();
        AlgebraOperator algebra = Translate("ASK { ?s :p ?o }", pool);

        (SoakSample off, bool offAnswer) = await TimeAskAsync(engine, algebra, AlgebraRewritePipeline.Empty).ConfigureAwait(false);
        (SoakSample on, bool onAnswer) = await TimeAskAsync(engine, algebra, FullCatalog).ConfigureAwait(false);

        string agreement = offAnswer == onAnswer ? "MATCH" : "MISMATCH";
        //Byte precision: this row's banked signal (the bare pass overhead) is sub-kilobyte.
        Console.WriteLine($"[rewriter] {"ask bare-bgp 1k (pass overhead)",-38} | off {off.Milliseconds,9:F3} ms {off.AllocCellBytes,13} | on {on.Milliseconds,9:F3} ms {on.AllocCellBytes,13} | ask {onAnswer,5} {agreement}");
    }

    /// <summary>Measures one anchor row through <c>EvaluateAsync</c> under both pipelines on ONE engine (warm once per pipeline, then timed), printing wall/alloc/rows with the agreement marker.</summary>
    /// <param name="label">The row label.</param>
    /// <param name="data">The data graph.</param>
    /// <param name="algebra">The plan (hand-built or translated).</param>
    /// <returns>The asynchronous run.</returns>
    private static async Task MeasureRowAsync(string label, List<DataTriple> data, AlgebraOperator algebra)
    {
        SparqlQueryEngine engine = await SparqlQueryEngine.BuildAsync(data).ConfigureAwait(false);

        (SoakSample off, int offRows) = await TimeEvaluateAsync(engine, algebra, AlgebraRewritePipeline.Empty).ConfigureAwait(false);
        (SoakSample on, int onRows) = await TimeEvaluateAsync(engine, algebra, FullCatalog).ConfigureAwait(false);

        string agreement = offRows == onRows ? "MATCH" : "MISMATCH";
        Console.WriteLine($"[rewriter] {label,-38} | off {off.Milliseconds,9:F1} ms {off.AllocCell,13} | on {on.Milliseconds,9:F1} ms {on.AllocCell,13} | rows {onRows,8:N0} {agreement}");
    }

    /// <summary>Times one warmed <c>EvaluateAsync</c> run under a per-call pipeline through the shared self-validating window.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="algebra">The translated plan.</param>
    /// <param name="rewrites">The per-call pipeline.</param>
    /// <returns>The sample and the row count.</returns>
    private static async Task<(SoakSample Sample, int Rows)> TimeEvaluateAsync(SparqlQueryEngine engine, AlgebraOperator algebra, AlgebraRewritePipeline rewrites)
    {
        _ = await engine.EvaluateAsync(algebra, rewrites).ConfigureAwait(false);

        SoakWindow window = SoakWindow.Open();
        IReadOnlyList<SparqlSolution> rows = await engine.EvaluateAsync(algebra, rewrites).ConfigureAwait(false);

        return (window.Close(), rows.Count);
    }

    /// <summary>Times one warmed <c>EvaluateAskAsync</c> run under a per-call pipeline through the shared self-validating window.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="algebra">The translated plan.</param>
    /// <param name="rewrites">The per-call pipeline.</param>
    /// <returns>The sample and the answer.</returns>
    private static async Task<(SoakSample Sample, bool Answer)> TimeAskAsync(SparqlQueryEngine engine, AlgebraOperator algebra, AlgebraRewritePipeline rewrites)
    {
        _ = await engine.EvaluateAskAsync(algebra, rewrites).ConfigureAwait(false);

        SoakWindow window = SoakWindow.Open();
        bool answer = await engine.EvaluateAskAsync(algebra, rewrites).ConfigureAwait(false);

        return (window.Close(), answer);
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Builds a variable term with the given name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The variable term.</returns>
    private static VariableTerm Var(string name)
    {
        return new VariableTerm(default, new SparqlVariable(Utf8Strings.From(name)));
    }

    /// <summary>Parses, normalizes, and translates a query (the shared example prefix prepended) to its algebra.</summary>
    /// <param name="text">The query text without the prefix.</param>
    /// <param name="pool">The pool owning the parsed strings.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes($"PREFIX : <{Ex}> {text}"), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }
}
