using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Database;
using Lumoin.Veritas.Rdf.Indexing;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// Soak characterising the temporal value-index probe route against the scan baseline over the
/// seam's anchor rows. READ anchors (two engines per row, same data and same registry composition,
/// only <see cref="SparqlEnginePolicy.PreferValueIndexes"/> differs): (1) the point-axis window over
/// a large observation set; (2) the interval-pair overlap window; (3) the interval as-of cover (the
/// ASOF anchor rides the interval shape while the point ASOF recognizer is future work); (4) an
/// equality comparison and (5) an undeclared predicate — the no-regression rows where the recognizer
/// declines and both routes must run the identical scan. WRITE anchors (the mutable database route,
/// registered registry versus none): (6) the pure commit tax over out-of-order appends — the
/// registered registry's per-commit maintenance is invalidation only; (7) the commit-then-probe
/// interleave — every probe after a commit pays the wholesale rebuild, the drop-and-rebuild
/// lifecycle's honest read-side cost; (8) one probe after heavy growth — the rebuild-at-probe cost
/// over the accumulated store, standing in for a compaction-triggering run (the v1 method rebuilds
/// wholesale per generation and has no segment compaction to trigger). Per row: wall time, allocated
/// bytes, and the result rows for both arms with an answer-agreement marker. Line-oriented output
/// for hand-collation; answers must MATCH on every row.
/// </summary>
internal static class ValueIndexSoak
{
    /// <summary>The example-namespace prefix the soak's queries and data share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The XSD dateTime datatype IRI suffix used in query literals.</summary>
    private const string XsdDateTime = "http://www.w3.org/2001/XMLSchema#dateTime";

    /// <summary>The Unix-second epoch of the soak's time axis origin (2020-01-01T00:00:00Z); every timestamp derives from this constant, never from a clock.</summary>
    private const long EpochBaseSeconds = 1_577_836_800;

    /// <summary>The point-axis observation count.</summary>
    private const int PointCount = 500_000;

    /// <summary>The interval-pair occurrence count.</summary>
    private const int IntervalCount = 250_000;

    /// <summary>The multiplicative step of the deterministic Lehmer-style index permutation that makes appends arrive out of axis order.</summary>
    private const long ShuffleStep = 48_271;

    /// <summary>Runs the read anchors, the no-regression rows, and the write anchors.</summary>
    /// <returns>The asynchronous run.</returns>
    public static async Task RunValueIndexSoak()
    {
        //The host stamp: soak numbers are per-machine, so every report line set leads with the
        //machine identity and runtime the numbers were taken on (the hand-collation ledger keys
        //portable and per-machine columns apart by this line).
        Console.WriteLine($"[valueindex] host {Environment.MachineName} | {System.Runtime.InteropServices.RuntimeInformation.OSDescription} | {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} | logical cores {Environment.ProcessorCount} | gc server={System.Runtime.GCSettings.IsServerGC}");
        Console.WriteLine("[valueindex] read rows: scan = PreferValueIndexes off, probe = on (same data, same registry composition); alloc per timed run");

        await RunPointWindowRowAsync().ConfigureAwait(false);
        await RunIntervalOverlapRowAsync().ConfigureAwait(false);
        await RunIntervalAsOfRowAsync().ConfigureAwait(false);
        await RunEqualityDeclineRowAsync().ConfigureAwait(false);
        await RunUndeclaredPredicateRowAsync().ConfigureAwait(false);

        Console.WriteLine("[valueindex] write rows: none = empty registry, reg = temporal registry composed (the flag gates reads only)");

        await RunCommitTaxRowAsync().ConfigureAwait(false);
        await RunCommitProbeInterleaveRowAsync().ConfigureAwait(false);
        await RunRebuildAfterGrowthRowAsync().ConfigureAwait(false);
    }

    /// <summary>Read anchor 1 (S1): a one-sided point window selecting the top 100 of 500k observations — the scan walks and compares every row, the probe binary-searches the sorted axis.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunPointWindowRowAsync()
    {
        List<DataTriple> data = new(PointCount);
        for(int i = 0; i < PointCount; i++)
        {
            long shuffled = (i * ShuffleStep) % PointCount;
            data.Add(new DataTriple(Iri($"s{shuffled:D6}"), Iri("at"), DateTimeLiteral(EpochBaseSeconds + (shuffled * 60))));
        }

        string threshold = TimestampLexical(EpochBaseSeconds + ((PointCount - 100L) * 60));
        string query = $"SELECT ?s ?v WHERE {{ ?s <{Ex}at> ?v FILTER(?v >= \"{threshold}\"^^<{XsdDateTime}>) }}";

        await MeasureReadRowAsync("point window 500k -> 100", data, query).ConfigureAwait(false);
    }

    /// <summary>Read anchor 2 (S2): the interval-pair overlap window over 250k two-hour intervals — the scan joins both patterns then filters, the probe walks the start-sorted entries and stops past the window.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunIntervalOverlapRowAsync()
    {
        List<DataTriple> data = BuildIntervalData();

        long windowStart = EpochBaseSeconds + (200_000L * 120);
        string lower = TimestampLexical(windowStart);
        string upper = TimestampLexical(windowStart + 600);
        string query = $"SELECT ?o ?s ?e WHERE {{ ?o <{Ex}from> ?s . ?o <{Ex}until> ?e FILTER(?s <= \"{upper}\"^^<{XsdDateTime}> && ?e >= \"{lower}\"^^<{XsdDateTime}>) }}";

        await MeasureReadRowAsync("interval overlap 250k window", data, query).ConfigureAwait(false);
    }

    /// <summary>Read anchor 3 (S6, the ASOF anchor): the interval as-of cover — both window bounds equal one instant, selecting the intervals in effect at it (the point ASOF recognizer is future work per the spec, so ASOF rides the interval shape).</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunIntervalAsOfRowAsync()
    {
        List<DataTriple> data = BuildIntervalData();

        string instant = TimestampLexical(EpochBaseSeconds + (200_000L * 120) + 60);
        string query = $"SELECT ?o ?s ?e WHERE {{ ?o <{Ex}from> ?s . ?o <{Ex}until> ?e FILTER(?s <= \"{instant}\"^^<{XsdDateTime}> && ?e >= \"{instant}\"^^<{XsdDateTime}>) }}";

        await MeasureReadRowAsync("interval as-of 250k cover", data, query).ConfigureAwait(false);
    }

    /// <summary>No-regression row 1: an equality comparison keeps record semantics, the recognizer declines on BOTH arms, and the two routes must run the identical scan — the alloc columns must match.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunEqualityDeclineRowAsync()
    {
        List<DataTriple> data = new(100_000);
        for(int i = 0; i < 100_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("at"), DateTimeLiteral(EpochBaseSeconds + (i * 60L))));
        }

        string constant = TimestampLexical(EpochBaseSeconds + (50_000L * 60));
        string query = $"SELECT ?s WHERE {{ ?s <{Ex}at> ?v FILTER(?v = \"{constant}\"^^<{XsdDateTime}>) }}";

        await MeasureReadRowAsync("equality decline 100k (no-regr.)", data, query).ConfigureAwait(false);
    }

    /// <summary>No-regression row 2: the comparison predicate is not a registered axis, the recognizer declines on BOTH arms, and the routes must be identical — the alloc columns must match.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunUndeclaredPredicateRowAsync()
    {
        List<DataTriple> data = new(100_000);
        for(int i = 0; i < 100_000; i++)
        {
            data.Add(new DataTriple(Iri($"s{i:D6}"), Iri("other"), DateTimeLiteral(EpochBaseSeconds + (i * 60L))));
        }

        string threshold = TimestampLexical(EpochBaseSeconds + (99_900L * 60));
        string query = $"SELECT ?s WHERE {{ ?s <{Ex}other> ?v FILTER(?v >= \"{threshold}\"^^<{XsdDateTime}>) }}";

        await MeasureReadRowAsync("undeclared pred 100k (no-regr.)", data, query).ConfigureAwait(false);
    }

    /// <summary>Write anchor 1: the pure commit tax — 200 commits of 500 out-of-order appends each, with the temporal registry composed versus none; no query runs, so the registered arm pays exactly the per-commit invalidation.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunCommitTaxRowAsync()
    {
        SoakSample none = await RunCommitBatchesAsync(withRegistry: false, commitCount: 200, triplesPerCommit: 500, probeEachCommit: false).ConfigureAwait(false);
        SoakSample registered = await RunCommitBatchesAsync(withRegistry: true, commitCount: 200, triplesPerCommit: 500, probeEachCommit: false).ConfigureAwait(false);

        Console.WriteLine($"[valueindex] {"commit tax 200x500 out-of-order",-38} | none {none.Milliseconds,9:F1} ms {none.AllocCell,13} | reg {registered.Milliseconds,9:F1} ms {registered.AllocCell,13} | per-commit {(registered.Milliseconds - none.Milliseconds) / 200,7:F3} ms");
    }

    /// <summary>Write anchor 2: the commit-then-probe interleave — 50 cycles of one 500-triple commit followed by one probed point query; every probe after a commit pays the wholesale rebuild, the drop-and-rebuild lifecycle's honest cost, against the same interleave on the scan route.</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunCommitProbeInterleaveRowAsync()
    {
        SoakSample scan = await RunCommitBatchesAsync(withRegistry: false, commitCount: 50, triplesPerCommit: 500, probeEachCommit: true).ConfigureAwait(false);
        SoakSample probe = await RunCommitBatchesAsync(withRegistry: true, commitCount: 50, triplesPerCommit: 500, probeEachCommit: true).ConfigureAwait(false);

        Console.WriteLine($"[valueindex] {"commit+probe interleave 50x500",-38} | scan {scan.Milliseconds,9:F1} ms {scan.AllocCell,13} | probe {probe.Milliseconds,9:F1} ms {probe.AllocCell,13} | per-cycle {(probe.Milliseconds - scan.Milliseconds) / 50,7:F3} ms");
    }

    /// <summary>Write anchor 3: one probe after heavy growth — 100 commits of 1000 triples, then a single probed query whose rebuild covers the whole accumulated store (the wholesale-rebuild analogue of a compaction-triggering run; the v1 method has no segment compaction).</summary>
    /// <returns>The asynchronous run.</returns>
    private static async Task RunRebuildAfterGrowthRowAsync()
    {
        string threshold = TimestampLexical(EpochBaseSeconds + ((100L * 1000 * 60) - (100 * 60)));
        Utf8String probe = Utf8Strings.From($"SELECT ?s WHERE {{ ?s <{Ex}at> ?v FILTER(?v >= \"{threshold}\"^^<{XsdDateTime}>) }}");

        VeritasEngine scanEngine = await OpenMutableAsync(withRegistry: false).ConfigureAwait(false);
        await using(scanEngine.ConfigureAwait(false))
        {
            await AppendCommitsAsync(scanEngine, commitCount: 100, triplesPerCommit: 1000, startIndex: 0).ConfigureAwait(false);
            (SoakSample scan, int scanRows) = await TimeQueryAsync(scanEngine, probe).ConfigureAwait(false);

            VeritasEngine probeEngine = await OpenMutableAsync(withRegistry: true).ConfigureAwait(false);
            await using(probeEngine.ConfigureAwait(false))
            {
                await AppendCommitsAsync(probeEngine, commitCount: 100, triplesPerCommit: 1000, startIndex: 0).ConfigureAwait(false);
                (SoakSample rebuilt, int probeRows) = await TimeQueryAsync(probeEngine, probe).ConfigureAwait(false);

                string agreement = scanRows == probeRows ? "MATCH" : "MISMATCH";
                Console.WriteLine($"[valueindex] {"rebuild-at-probe after 100k growth",-38} | scan {scan.Milliseconds,9:F1} ms {scan.AllocCell,13} | probe {rebuilt.Milliseconds,9:F1} ms {rebuilt.AllocCell,13} | rows {probeRows,8:N0} {agreement}");
            }
        }
    }

    /// <summary>Builds the shared 250k interval dataset: two-hour intervals whose starts step 120 s apart, appended in the deterministic out-of-order permutation.</summary>
    /// <returns>The interval triples.</returns>
    private static List<DataTriple> BuildIntervalData()
    {
        List<DataTriple> data = new(IntervalCount * 2);
        for(int i = 0; i < IntervalCount; i++)
        {
            long shuffled = (i * ShuffleStep) % IntervalCount;
            long start = EpochBaseSeconds + (shuffled * 120);
            data.Add(new DataTriple(Iri($"o{shuffled:D6}"), Iri("from"), DateTimeLiteral(start)));
            data.Add(new DataTriple(Iri($"o{shuffled:D6}"), Iri("until"), DateTimeLiteral(start + 7200)));
        }

        return data;
    }

    /// <summary>Measures one read row: builds the scan and probe engines over the same data and the same registry composition, runs the query warmed on each, and prints wall/alloc/rows with the agreement marker.</summary>
    /// <param name="label">The row label.</param>
    /// <param name="data">The data graph.</param>
    /// <param name="query">The query text (absolute IRIs, no prefixes).</param>
    /// <returns>The asynchronous run.</returns>
    private static async Task MeasureReadRowAsync(string label, List<DataTriple> data, string query)
    {
        SparqlQueryEngine scanEngine = await SparqlQueryEngine.BuildAsync(
            data,
            enginePolicy: new SparqlEnginePolicy(PreferValueIndexes: false),
            valueIndexes: ComposedRegistry()).ConfigureAwait(false);
        SparqlQueryEngine probeEngine = await SparqlQueryEngine.BuildAsync(
            data,
            enginePolicy: new SparqlEnginePolicy(PreferValueIndexes: true),
            valueIndexes: ComposedRegistry()).ConfigureAwait(false);

        using Utf8StringPool scanPool = new();
        AlgebraOperator scanAlgebra = Translate(query, scanPool);
        (SoakSample scan, int scanRows) = await TimeEvaluateAsync(scanEngine, scanAlgebra).ConfigureAwait(false);

        using Utf8StringPool probePool = new();
        AlgebraOperator probeAlgebra = Translate(query, probePool);
        (SoakSample probe, int probeRows) = await TimeEvaluateAsync(probeEngine, probeAlgebra).ConfigureAwait(false);

        string agreement = scanRows == probeRows ? "MATCH" : "MISMATCH";
        Console.WriteLine($"[valueindex] {label,-38} | scan {scan.Milliseconds,9:F1} ms {scan.AllocCell,13} | probe {probe.Milliseconds,9:F1} ms {probe.AllocCell,13} | rows {probeRows,8:N0} {agreement}");
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

    /// <summary>Runs one write-arm batch sequence on a fresh mutable database: <paramref name="commitCount"/> commits of <paramref name="triplesPerCommit"/> out-of-order appends, optionally probing after each commit, timing the whole sequence.</summary>
    /// <param name="withRegistry">Whether the temporal registry is composed (the flag follows it: probes prefer the index exactly when the registry is present).</param>
    /// <param name="commitCount">The number of commits.</param>
    /// <param name="triplesPerCommit">The appended triples per commit.</param>
    /// <param name="probeEachCommit">Whether each commit is followed by one probed point query.</param>
    /// <returns>Wall milliseconds and allocated bytes over the whole sequence.</returns>
    private static async Task<SoakSample> RunCommitBatchesAsync(bool withRegistry, int commitCount, int triplesPerCommit, bool probeEachCommit)
    {
        VeritasEngine engine = await OpenMutableAsync(withRegistry).ConfigureAwait(false);
        await using(engine.ConfigureAwait(false))
        {
            string threshold = TimestampLexical(EpochBaseSeconds);
            Utf8String probe = Utf8Strings.From($"SELECT ?s WHERE {{ ?s <{Ex}at> ?v FILTER(?v >= \"{threshold}\"^^<{XsdDateTime}>) }}");

            SoakWindow window = SoakWindow.Open();
            for(int commit = 0; commit < commitCount; commit++)
            {
                await AppendCommitsAsync(engine, commitCount: 1, triplesPerCommit, startIndex: commit * triplesPerCommit).ConfigureAwait(false);
                if(probeEachCommit)
                {
                    _ = await engine.QueryAsync(probe).ConfigureAwait(false);
                }
            }

            return window.Close();
        }
    }

    /// <summary>Appends commits of out-of-order point observations through the SPARQL Update route.</summary>
    /// <param name="engine">The mutable database.</param>
    /// <param name="commitCount">The number of commits.</param>
    /// <param name="triplesPerCommit">The appended triples per commit.</param>
    /// <param name="startIndex">The global observation index the first appended triple takes.</param>
    /// <returns>The asynchronous run.</returns>
    private static async Task AppendCommitsAsync(VeritasEngine engine, int commitCount, int triplesPerCommit, int startIndex)
    {
        for(int commit = 0; commit < commitCount; commit++)
        {
            StringBuilder insert = new(triplesPerCommit * 96);
            insert.Append("INSERT DATA { ");
            for(int i = 0; i < triplesPerCommit; i++)
            {
                long global = startIndex + ((long)commit * triplesPerCommit) + i;
                long shuffled = (global * ShuffleStep) % 1_000_000;
                insert.Append(CultureInfo.InvariantCulture, $"<{Ex}s{global:D7}> <{Ex}at> \"{TimestampLexical(EpochBaseSeconds + (shuffled * 60))}\"^^<{XsdDateTime}> . ");
            }

            insert.Append('}');
            await engine.UpdateAsync(Utf8Strings.From(insert.ToString())).ConfigureAwait(false);
        }
    }

    /// <summary>Times one query on the database facade through the shared window (no warm run — the probe arm's first post-commit query IS the measured rebuild).</summary>
    /// <param name="engine">The database.</param>
    /// <param name="query">The query text.</param>
    /// <returns>The sample and the row count.</returns>
    private static async Task<(SoakSample Sample, int Rows)> TimeQueryAsync(VeritasEngine engine, Utf8String query)
    {
        SoakWindow window = SoakWindow.Open();
        VeritasQueryResult result = await engine.QueryAsync(query).ConfigureAwait(false);
        SoakSample sample = window.Close();

        return (sample, result.Bindings!.Solutions.Count);
    }

    /// <summary>Opens a fresh empty mutable database, with the temporal registry composed and the probe flag on when <paramref name="withRegistry"/> is set.</summary>
    /// <param name="withRegistry">Whether the temporal registry is composed.</param>
    /// <returns>The database.</returns>
    private static async ValueTask<VeritasEngine> OpenMutableAsync(bool withRegistry)
    {
        VeritasEngineOptions options = withRegistry
            ? new VeritasEngineOptions { ValueIndexes = ComposedRegistry(), SparqlExecution = new SparqlEnginePolicy(PreferValueIndexes: true) }
            : new VeritasEngineOptions();

        return await VeritasEngine.OpenMutableAsync([], options).ConfigureAwait(false);
    }

    /// <summary>Composes a fresh registry — the point axis over <c>:at</c> and the interval pair over <c>:from</c>/<c>:until</c>, both UTC. Fresh per engine so no built method state is shared across arms.</summary>
    /// <returns>The registry.</returns>
    private static ValueIndexRegistry ComposedRegistry()
    {
        Utf8String at = Utf8Strings.From(Ex + "at");
        Utf8String from = Utf8Strings.From(Ex + "from");
        Utf8String until = Utf8Strings.From(Ex + "until");
        ValueAxisDeclaration pointAxis = ValueAxisDeclaration.PointAxis(at);
        ValueAxisDeclaration intervalAxis = ValueAxisDeclaration.IntervalPair(from, until);

        return new ValueIndexRegistryBuilder()
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, pointAxis, TimeSpan.Zero),
                pointAxis,
                new EmptySource(),
                selfTestCases: []))
            .Add(new ValueIndexRegistration(
                new TemporalIntervalAccessMethod(Vocabulary.Xsd.DateTime, intervalAxis, TimeSpan.Zero),
                intervalAxis,
                new EmptySource(),
                selfTestCases: []))
            .Build();
    }

    /// <summary>Formats a Unix-second instant as an <c>xsd:dateTime</c> UTC lexical form; pure arithmetic from the fixed epoch constant, never a clock read.</summary>
    /// <param name="unixSeconds">The instant in Unix seconds.</param>
    /// <returns>The lexical form.</returns>
    private static string TimestampLexical(long unixSeconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Builds an <c>xsd:dateTime</c> literal at a Unix-second instant.</summary>
    /// <param name="unixSeconds">The instant in Unix seconds.</param>
    /// <returns>The literal term.</returns>
    private static Literal DateTimeLiteral(long unixSeconds)
    {
        return new Literal(Utf8Strings.From(TimestampLexical(unixSeconds)), new NamedNode(Vocabulary.Xsd.DateTime));
    }

    /// <summary>Builds an example-namespace IRI term from a local name.</summary>
    /// <param name="localName">The local name appended to the example prefix.</param>
    /// <returns>The named-node term.</returns>
    private static NamedNode Iri(string localName)
    {
        return new NamedNode(Utf8Strings.From(Ex + localName));
    }

    /// <summary>Parses, normalizes, and translates a query to its algebra.</summary>
    /// <param name="text">The query text (absolute IRIs, no prefixes).</param>
    /// <param name="pool">The pool owning the parsed strings.</param>
    /// <returns>The translated algebra.</returns>
    private static AlgebraOperator Translate(string text, Utf8StringPool pool)
    {
        SparqlLexer lexer = new(Encoding.UTF8.GetBytes(text), pool);
        SparqlParser parser = new(lexer.Tokenize(), pool);
        SparqlQuery query = (SparqlQuery)new SparqlNormalizer(pool).Normalize(parser.ParseRequest());

        return SparqlTranslator.Translate(query);
    }

    /// <summary>An empty registrant sample corpus (the method's semantics are certified by its own battery; the soak measures routes).</summary>
    private sealed class EmptySource: ValueSegmentSource
    {
        /// <summary>Enumerates nothing.</summary>
        /// <param name="predicateIri">The requested predicate.</param>
        /// <returns>No entries.</returns>
        public override IEnumerable<ValueSegmentEntry> EnumerateDeclared(Utf8String predicateIri)
        {
            return [];
        }
    }
}
