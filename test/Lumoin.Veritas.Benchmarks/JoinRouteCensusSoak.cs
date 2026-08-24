using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Hypertrie.Tracing;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Benchmarks;

/// <summary>
/// The differential all-routes census: every eligible route over every fixture,
/// emitted as machine-readable rows rather than prose. For each fixture the
/// census drains the four rendezvous cells under their own policies, the four
/// batch-level Free Join cells at both depths and both trie build modes with the
/// relations built by the harness, and the four factorised cells where the shape
/// admits them; it gates every cell on identical row counts and prints one
/// fixture line, one cost line per measured cell, and one per-relation line under
/// every directly built cell. Every number is formatted invariantly, every line
/// is validated against its own token count and closed vocabularies before it is
/// printed, and a line that fails validation is published under a poison marker
/// rather than repaired or dropped. The census reads engine state and never
/// writes it: no routing, policy, selector, or default changes.
/// </summary>
internal static class JoinRouteCensusSoak
{
    /// <summary>The drain backstop: a cell stops at this row count and reports the cap, so a shape that explodes past its bound is recorded rather than hung on.</summary>
    private const long DrainRowCap = 20_000_000;

    /// <summary>The per-cell repetition budget in milliseconds: repetitions stop once the cell has spent this much, whichever comes first with the repetition target.</summary>
    private const double CellBudgetMilliseconds = 20_000;

    /// <summary>The token count of a fixture line.</summary>
    private const int FixtureTokens = 18;

    /// <summary>The token count of a cost line.</summary>
    private const int RowTokens = 19;

    /// <summary>The token count of a per-relation line.</summary>
    private const int PatternTokens = 10;

    /// <summary>The not-applicable and not-measured marker; a measured zero prints as a zero.</summary>
    private const string Absent = "-";

    /// <summary>The shape vocabulary.</summary>
    private static string[] ShapeVocabulary { get; } = ["star", "chain", "triangle", "lollipop", "sat1", "sat2", "sat4", "cycle4", "disjoint2", "starchain"];

    /// <summary>The join-tree vocabulary.</summary>
    private static string[] GyoVocabulary { get; } = ["tree", "none"];

    /// <summary>The route vocabulary: the engine kinds a rendezvous can select, the two harness labels, and the marker a cell that never ran carries.</summary>
    private static string[] RouteVocabulary { get; } = ["hypertrie", "columnar", "batched", "freejoin", "selfindex", "factorized", Absent];

    /// <summary>The provenance vocabulary.</summary>
    private static string[] ViaVocabulary { get; } = ["rendezvous", "direct", Absent];

    /// <summary>The selection-reason vocabulary.</summary>
    private static string[] ReasonVocabulary { get; } = ["SystemOfRecord", "ViewReused", "ViewBuilt", "SnapshotSuperseded", "RotationIncompatible", "ViewBuilding", Absent];

    /// <summary>The depth token for a join-cover-depth trie build.</summary>
    private const string CoverDepthToken = "cover";

    /// <summary>The depth token for a full-depth trie build.</summary>
    private const string FullDepthToken = "full";

    /// <summary>The build token for an eagerly built trie.</summary>
    private const string EagerBuildToken = "eager";

    /// <summary>The build token for a lazily built trie.</summary>
    private const string LazyBuildToken = "lazy";

    /// <summary>The depth-policy vocabulary.</summary>
    private static string[] DepthVocabulary { get; } = [CoverDepthToken, FullDepthToken, Absent];

    /// <summary>The trie-build vocabulary.</summary>
    private static string[] BuildVocabulary { get; } = [EagerBuildToken, LazyBuildToken, Absent];

    /// <summary>The row-count agreement vocabulary.</summary>
    private static string[] MatchVocabulary { get; } = ["MATCH", "MISMATCH", Absent];

    /// <summary>The cell-key vocabulary.</summary>
    private static string[] CellVocabulary { get; } =
    [
        "rv-default", "rv-leapfrog", "rv-freejoin", "rv-freejoinlazy",
        "fj-cover-eager", "fj-cover-lazy", "fj-full-eager", "fj-full-lazy",
        "fx-cover-eager", "fx-cover-lazy", "fx-full-eager", "fx-full-lazy"
    ];

    /// <summary>The note token a shape the factorised order declines carries; the cell never attempts a drain.</summary>
    private const string DeclinedShapeNote = "declined-shape";

    /// <summary>The note token a fixture refused by the exact-predictor cap carries; no cell of it runs.</summary>
    private const string SkippedOutsizeNote = "skipped-outsize";

    /// <summary>The note token a cell that reached the drain backstop carries; its agreement verdict is retracted.</summary>
    private const string DrainCappedNote = "drain-capped";

    /// <summary>The note token a fixture measured at reduced smoke scale carries.</summary>
    private const string SmokeScaleNote = "smoke-scale";

    /// <summary>The [fjrow] field index of the agreement verdict.</summary>
    private const int MatchFieldIndex = 15;

    /// <summary>The [fjrow] field index of the note.</summary>
    private const int NoteFieldIndex = 16;

    /// <summary>The note vocabulary: each token has one producing condition.</summary>
    private static string[] NoteVocabulary { get; } = [DeclinedShapeNote, SkippedOutsizeNote, DrainCappedNote, SmokeScaleNote, Absent];

    /// <summary>The rendezvous cell keys, in drain order; the first is the fixture's row-count reference.</summary>
    private static string[] RendezvousCells { get; } = ["rv-default", "rv-leapfrog", "rv-freejoin", "rv-freejoinlazy"];

    /// <summary>The directly driven Free Join cell keys, in drain order.</summary>
    private static string[] DirectCells { get; } = ["fj-cover-eager", "fj-cover-lazy", "fj-full-eager", "fj-full-lazy"];

    /// <summary>The factorised cell keys, in drain order.</summary>
    private static string[] FactorizedCells { get; } = ["fx-cover-eager", "fx-cover-lazy", "fx-full-eager", "fx-full-lazy"];

    /// <summary>
    /// Runs the census over the ladder; <c>--quick</c> anywhere in the arguments
    /// runs the smoke-scale protocol.
    /// </summary>
    /// <param name="args">The census arguments.</param>
    /// <returns>The census run.</returns>
    public static async Task RunJoinRouteCensusAsync(string[] args)
    {
        bool quick = Array.IndexOf(args, "--quick") >= 0;

        Console.WriteLine("[fjcensus] v1 fields fix id shape gyo comps pats prim triples nodes edges alpha maxdeg hthr hfrac pred genms idxms");
        Console.WriteLine("[fjcensus] v1 fields row id cell route via reason depth build reps buildms drivems totalms rows retvals nodeents leaves match note");
        Console.WriteLine("[fjcensus] v1 fields pat id cell pat cols depth retvals nodeents leaves");

        using VeritasMemoryPool<uint> pool = new();

        foreach(CensusFixture fixture in JoinRouteCensusFixtures.Ladder(quick))
        {
            await RunFixtureAsync(fixture, pool, CancellationToken.None).ConfigureAwait(false);

            pool.TrimExcess();
        }
    }

    /// <summary>
    /// Runs one fixture: the size guards, the fixture line, then the rendezvous,
    /// directly driven, and factorised cells. The cell lines are held until the
    /// fixture's drain-cap verdict is known, because a capped drain retracts the
    /// whole fixture's row-count comparison rather than one cell's.
    /// </summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="pool">The census run's buffer pool.</param>
    /// <param name="cancellationToken">The token the drains observe.</param>
    /// <returns>The fixture run.</returns>
    private static async Task RunFixtureAsync(CensusFixture fixture, VeritasMemoryPool<uint> pool, CancellationToken cancellationToken)
    {
        BasicGraphPattern query = fixture.BuildQuery(new VariableRegistry());
        string gyo = GyoToken(query);
        string components = Count(ComponentCount(query));
        string patterns = Count(query.Patterns.Count);

        if(fixture.Refused)
        {
            EmitFixture(fixture, gyo, components, patterns, Absent, Absent, Absent, Absent, Absent, Absent);
            EmitLine(RowTokens,
                "[fjrow]", "row",
                [
                    fixture.Id, "rv-default", Absent, Absent, Absent, Absent, Absent, Absent,
                    Absent, Absent, Absent, Absent, Absent, Absent, Absent, Absent, SkippedOutsizeNote
                ]);

            return;
        }

        long generateStart = Stopwatch.GetTimestamp();
        List<EncodedTriple> triples = fixture.Generate();
        double generateMilliseconds = Stopwatch.GetElapsedTime(generateStart).TotalMilliseconds;

        (long realisedTriples, long maxDegree, long heavyThreshold, double heavyFraction) = PrimaryDegreeStatistics(triples, fixture.PrimaryPredicate);

        long indexStart = Stopwatch.GetTimestamp();
        ColumnarTripleIndex index = ColumnarTripleIndex.Build(triples);
        double indexMilliseconds = Stopwatch.GetElapsedTime(indexStart).TotalMilliseconds;

        HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(triples, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);

        EmitFixture(
            fixture, gyo, components, patterns,
            Count(realisedTriples), Count(maxDegree), Count(heavyThreshold), Fraction(heavyFraction),
            Milliseconds(generateMilliseconds), Milliseconds(indexMilliseconds));

        List<CensusLine> lines = [];
        long reference = -1;
        bool capped = false;

        //The magnitude probe carries one route from each provenance rather than
        //the full cell set: it exists to put the largest size the stand protocol
        //exercises under the format and arithmetic, and a two-cell differential
        //does that while leaving the smoke a smoke.
        int rendezvousCells = fixture.SmokeScale ? 1 : RendezvousCells.Length;
        int directCells = fixture.SmokeScale ? 1 : DirectCells.Length;

        for(int cell = 0; cell < rendezvousCells; cell++)
        {
            QueryEnginePolicy policy = PolicyOf(RendezvousCells[cell]);
            RendezvousCell measured = await MeasureRendezvousCellAsync(store, index, policy, query, pool, fixture.TimedRepetitions, cancellationToken).ConfigureAwait(false);

            capped |= measured.Capped;
            if(reference < 0 && !measured.Capped)
            {
                reference = measured.Rows;
            }

            AppendRendezvousRow(lines, fixture, RendezvousCells[cell], measured, reference);
        }

        for(int cell = 0; cell < directCells; cell++)
        {
            bool joinCover = cell < 2;
            FreeJoinTrieBuild trieBuild = (cell % 2) == 0 ? FreeJoinTrieBuild.Eager : FreeJoinTrieBuild.Lazy;
            DirectCell measured = MeasureFreeJoinCell(index, query, joinCover, trieBuild, fixture.TimedRepetitions);

            capped |= measured.Capped;
            if(reference < 0 && !measured.Capped)
            {
                reference = measured.Rows;
            }

            AppendDirectRow(lines, fixture, DirectCells[cell], "freejoin", joinCover ? CoverDepthToken : FullDepthToken, trieBuild, measured, reference);
        }

        if(fixture.SmokeScale)
        {
            FlushLines(lines, capped);

            return;
        }

        if(!FreeJoinPipeline.TryPlanFactorizedOrder(index, query, out IReadOnlyList<Variable>? factorizedOrder))
        {
            AppendDeclinedRow(lines, fixture, FactorizedCells[0]);
        }
        else
        {
            for(int cell = 0; cell < FactorizedCells.Length; cell++)
            {
                bool joinCover = cell < 2;
                FreeJoinTrieBuild trieBuild = (cell % 2) == 0 ? FreeJoinTrieBuild.Eager : FreeJoinTrieBuild.Lazy;
                DirectCell measured = MeasureFactorizedCell(index, query, factorizedOrder, joinCover, trieBuild, fixture.TimedRepetitions, pool);

                capped |= measured.Capped;
                if(reference < 0 && !measured.Capped)
                {
                    reference = measured.Rows;
                }

                AppendDirectRow(lines, fixture, FactorizedCells[cell], "factorized", joinCover ? CoverDepthToken : FullDepthToken, trieBuild, measured, reference);
            }
        }

        FlushLines(lines, capped);
    }

    /// <summary>Emits the fixture line.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="gyo">The join-tree token.</param>
    /// <param name="components">The connected-component count.</param>
    /// <param name="patterns">The pattern count.</param>
    /// <param name="triples">The realised triple count, or the absent marker.</param>
    /// <param name="maxDegree">The realised maximum out-degree, or the absent marker.</param>
    /// <param name="heavyThreshold">The realised heavy threshold, or the absent marker.</param>
    /// <param name="heavyFraction">The realised heavy edge fraction, or the absent marker.</param>
    /// <param name="generateMilliseconds">The generation milliseconds, or the absent marker.</param>
    /// <param name="indexMilliseconds">The index build milliseconds, or the absent marker.</param>
    private static void EmitFixture(
        CensusFixture fixture,
        string gyo,
        string components,
        string patterns,
        string triples,
        string maxDegree,
        string heavyThreshold,
        string heavyFraction,
        string generateMilliseconds,
        string indexMilliseconds)
    {
        EmitLine(FixtureTokens,
            "[fjfix]", "fix",
            [
                fixture.Id,
                fixture.Shape,
                gyo,
                components,
                patterns,
                Count(fixture.PrimaryPredicate),
                triples,
                Count(fixture.NodeCount),
                Count(fixture.EdgeTarget),
                Exponent(fixture.ProfileExponent),
                maxDegree,
                heavyThreshold,
                heavyFraction,
                Count(fixture.Predictor),
                generateMilliseconds,
                indexMilliseconds
            ]);
    }

    /// <summary>Appends one rendezvous cell's cost line.</summary>
    /// <param name="lines">The fixture's held lines.</param>
    /// <param name="fixture">The fixture.</param>
    /// <param name="cell">The cell key.</param>
    /// <param name="measured">The measurement.</param>
    /// <param name="reference">The fixture's reference row count, or negative when none was established.</param>
    private static void AppendRendezvousRow(List<CensusLine> lines, CensusFixture fixture, string cell, RendezvousCell measured, long reference)
    {
        bool measuredRows = !measured.Capped;
        lines.Add(new CensusLine(RowTokens, "[fjrow]", "row",
            [
                fixture.Id,
                cell,
                measured.EngineObserved ? RouteToken(measured.Engine) : Absent,
                "rendezvous",
                measured.EngineObserved ? ReasonToken(measured.Reason) : Absent,
                Absent,
                Absent,
                measuredRows ? Count(measured.Repetitions) : Absent,
                Absent,
                Absent,
                measuredRows ? Milliseconds(measured.TotalMilliseconds) : Absent,
                Count(measured.Rows),
                Absent,
                Absent,
                Absent,
                measuredRows ? MatchToken(measured.Rows, reference) : Absent,
                NoteToken(fixture, measured.Capped)
            ]));
    }

    /// <summary>Appends one directly driven cell's cost line and its per-relation lines.</summary>
    /// <param name="lines">The fixture's held lines.</param>
    /// <param name="fixture">The fixture.</param>
    /// <param name="cell">The cell key.</param>
    /// <param name="route">The harness route label.</param>
    /// <param name="depth">The cell's depth policy.</param>
    /// <param name="trieBuild">The cell's trie build mode.</param>
    /// <param name="measured">The measurement.</param>
    /// <param name="reference">The fixture's reference row count, or negative when none was established.</param>
    private static void AppendDirectRow(List<CensusLine> lines, CensusFixture fixture, string cell, string route, string depth, FreeJoinTrieBuild trieBuild, DirectCell measured, long reference)
    {
        long retainedValues = 0;
        long nodeEntries = 0;
        long leaves = 0;

        for(int pattern = 0; pattern < measured.Relations.Count; pattern++)
        {
            GeneralizedHashTrie relation = measured.Relations[pattern];
            retainedValues += relation.RetainedValueCount;
            nodeEntries += relation.RetainedNodeEntryCount;
            leaves += relation.LeafCount;
        }

        bool measuredRows = !measured.Capped;
        lines.Add(new CensusLine(RowTokens, "[fjrow]", "row",
            [
                fixture.Id,
                cell,
                route,
                "direct",
                Absent,
                depth,
                BuildToken(trieBuild),
                measuredRows ? Count(measured.Repetitions) : Absent,
                measuredRows ? Milliseconds(measured.BuildMilliseconds) : Absent,
                measuredRows ? Milliseconds(measured.DriveMilliseconds) : Absent,
                measuredRows ? Milliseconds(measured.TotalMilliseconds) : Absent,
                Count(measured.Rows),
                Count(retainedValues),
                Count(nodeEntries),
                Count(leaves),
                measuredRows ? MatchToken(measured.Rows, reference) : Absent,
                NoteToken(fixture, measured.Capped)
            ]));

        for(int pattern = 0; pattern < measured.Relations.Count; pattern++)
        {
            GeneralizedHashTrie relation = measured.Relations[pattern];
            lines.Add(new CensusLine(PatternTokens, "[fjpat]", "pat",
                [
                    fixture.Id,
                    cell,
                    Count(pattern),
                    Count(relation.Schema.Count),
                    Count(relation.TrieColumns.Length),
                    Count(relation.RetainedValueCount),
                    Count(relation.RetainedNodeEntryCount),
                    Count(relation.LeafCount)
                ]));
        }
    }

    /// <summary>Appends the single line a shape the factorised order declines carries. The cell's identity columns (depth, build, one repetition) stay populated so the row names which cell declined; every measured column is absent.</summary>
    /// <param name="lines">The fixture's held lines.</param>
    /// <param name="fixture">The fixture.</param>
    /// <param name="cell">The cell key the declined row is published under.</param>
    private static void AppendDeclinedRow(List<CensusLine> lines, CensusFixture fixture, string cell)
    {
        lines.Add(new CensusLine(RowTokens, "[fjrow]", "row",
            [
                fixture.Id, cell, "factorized", "direct", Absent, CoverDepthToken, EagerBuildToken, Count(1),
                Absent, Absent, Absent, Absent, Absent, Absent, Absent, Absent, DeclinedShapeNote
            ]));
    }

    /// <summary>
    /// Prints the fixture's held lines. A fixture any of whose cells reached the
    /// drain backstop retracts every measured row's agreement verdict: a capped
    /// drain is evidence that the shape explodes, never evidence for a cost
    /// comparison. A declined-shape row keeps its note — it never attempted a
    /// drain, so retracting it would claim a measurement that never ran.
    /// </summary>
    /// <param name="lines">The fixture's held lines.</param>
    /// <param name="capped">Whether any cell reached the drain backstop.</param>
    private static void FlushLines(List<CensusLine> lines, bool capped)
    {
        foreach(CensusLine line in lines)
        {
            if(capped && line.ExpectedTokens == RowTokens && line.Fields[NoteFieldIndex] is not DeclinedShapeNote)
            {
                line.Fields[MatchFieldIndex] = Absent;
                line.Fields[NoteFieldIndex] = DrainCappedNote;
            }

            EmitLine(line.ExpectedTokens, line.Prefix, line.Kind, line.Fields);
        }
    }

    /// <summary>
    /// Drains one rendezvous cell. The fixture's already-built index is handed
    /// over as the rendezvous's view, so every cell reads the one index the
    /// fixture line's build cost accounts for rather than materialising a fourth
    /// copy of it. The trace handler rides the untimed warm drain only, so no
    /// published number carries the trace seam's per-event cost.
    /// </summary>
    /// <param name="store">The system of record.</param>
    /// <param name="view">The fixture's columnar index, handed over as the rendezvous's view.</param>
    /// <param name="policy">The engine policy the rendezvous routes by.</param>
    /// <param name="query">The query.</param>
    /// <param name="pool">The census run's buffer pool.</param>
    /// <param name="repetitionTarget">The timed repetition target.</param>
    /// <param name="cancellationToken">The token the drains observe.</param>
    /// <returns>The measurement.</returns>
    private static async Task<RendezvousCell> MeasureRendezvousCellAsync(
        HypertrieGraphStore store,
        ColumnarTripleIndex view,
        QueryEnginePolicy policy,
        BasicGraphPattern query,
        VeritasMemoryPool<uint> pool,
        int repetitionTarget,
        CancellationToken cancellationToken)
    {
        QueryEngineRendezvous rendezvous = new(store, policy, initialView: view, factorizedArenaPool: pool);
        EngineSelectionProbe probe = new();

        (long rows, bool capped) = await DrainAsync(rendezvous, query, probe.Handle, cancellationToken).ConfigureAwait(false);
        if(capped)
        {
            return new RendezvousCell(0, 0, rows, true, probe.Engine, probe.Reason, probe.Observed);
        }

        int repetitions = 0;
        double best = 0;
        double spent = 0;
        while(repetitions < repetitionTarget && (repetitions == 0 || spent < CellBudgetMilliseconds))
        {
            long start = Stopwatch.GetTimestamp();
            (long drained, bool drainCapped) = await DrainAsync(rendezvous, query, null, cancellationToken).ConfigureAwait(false);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            if(drainCapped)
            {
                return new RendezvousCell(repetitions, best, drained, true, probe.Engine, probe.Reason, probe.Observed);
            }

            if(repetitions == 0 || elapsed < best)
            {
                best = elapsed;
            }

            rows = drained;
            repetitions++;
            spent += elapsed;
        }

        return new RendezvousCell(repetitions, best, rows, false, probe.Engine, probe.Reason, probe.Observed);
    }

    /// <summary>Drains the query through the rendezvous, counting solutions and stopping at the drain backstop.</summary>
    /// <param name="rendezvous">The rendezvous.</param>
    /// <param name="query">The query.</param>
    /// <param name="traceHandler">The trace handler, or <see langword="null"/> for a timed repetition.</param>
    /// <param name="cancellationToken">The token the drain observes.</param>
    /// <returns>The drained row count and whether the backstop stopped it.</returns>
    private static async Task<(long Rows, bool Capped)> DrainAsync(QueryEngineRendezvous rendezvous, BasicGraphPattern query, TraceHandler<QueryTraceEvent>? traceHandler, CancellationToken cancellationToken)
    {
        long rows = 0;
        await foreach(Solution solution in rendezvous.QueryAsync(query, TimeProvider.System, traceHandler: traceHandler, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            rows++;

            if(rows >= DrainRowCap)
            {
                return (rows, true);
            }
        }

        return (rows, false);
    }

    /// <summary>
    /// Measures one directly driven Free Join cell: the relations are built by
    /// the harness at the requested depth over the global order, so full depth
    /// stays measurable although no route reaches it, and the build and drive
    /// phases are timed apart.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The query.</param>
    /// <param name="joinCover">Whether relations build at their join-cover depths; otherwise full depth.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <param name="repetitionTarget">The timed repetition target.</param>
    /// <returns>The measurement.</returns>
    private static DirectCell MeasureFreeJoinCell(ColumnarTripleIndex index, BasicGraphPattern query, bool joinCover, FreeJoinTrieBuild trieBuild, int repetitionTarget)
    {
        IReadOnlyList<Variable> order = ColumnarRotationPlanner.TryPlanGlobalOrder(index.OrderSetMode, query)!;

        List<GeneralizedHashTrie> relations = BuildRelations(index, query, order, joinCover, trieBuild);
        (long warmRows, bool warmCapped) = DriveFlat(relations, order);
        if(warmCapped)
        {
            return new DirectCell(0, 0, 0, 0, warmRows, true, relations);
        }

        int repetitions = 0;
        double bestBuild = 0;
        double bestDrive = 0;
        double bestTotal = 0;
        double spent = 0;
        long rows = warmRows;

        while(repetitions < repetitionTarget && (repetitions == 0 || spent < CellBudgetMilliseconds))
        {
            long buildStart = Stopwatch.GetTimestamp();
            relations = BuildRelations(index, query, order, joinCover, trieBuild);
            double buildMilliseconds = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;

            long driveStart = Stopwatch.GetTimestamp();
            (long drained, bool capped) = DriveFlat(relations, order);
            double driveMilliseconds = Stopwatch.GetElapsedTime(driveStart).TotalMilliseconds;

            if(capped)
            {
                return new DirectCell(repetitions, bestBuild, bestDrive, bestTotal, drained, true, relations);
            }

            double total = buildMilliseconds + driveMilliseconds;
            if(repetitions == 0 || total < bestTotal)
            {
                bestBuild = buildMilliseconds;
                bestDrive = driveMilliseconds;
                bestTotal = total;
            }

            rows = drained;
            repetitions++;
            spent += total;
        }

        return new DirectCell(repetitions, bestBuild, bestDrive, bestTotal, rows, false, relations);
    }

    /// <summary>
    /// Measures one factorised cell: the relations build over the factorised
    /// key-first order, the emit runs into a fresh arena rented from the census
    /// run's pool, and the batch's flat row count is what the agreement gate
    /// compares.
    /// </summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The query.</param>
    /// <param name="order">The factorised key-first variable order.</param>
    /// <param name="joinCover">Whether relations build at their join-cover depths; otherwise full depth.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <param name="repetitionTarget">The timed repetition target.</param>
    /// <param name="pool">The census run's buffer pool.</param>
    /// <returns>The measurement.</returns>
    private static DirectCell MeasureFactorizedCell(
        ColumnarTripleIndex index,
        BasicGraphPattern query,
        IReadOnlyList<Variable> order,
        bool joinCover,
        FreeJoinTrieBuild trieBuild,
        int repetitionTarget,
        VeritasMemoryPool<uint> pool)
    {
        List<GeneralizedHashTrie> relations = BuildRelations(index, query, order, joinCover, trieBuild);
        EmitFactorized(relations, order, pool);

        int repetitions = 0;
        double bestBuild = 0;
        double bestDrive = 0;
        double bestTotal = 0;
        double spent = 0;
        long rows = 0;

        while(repetitions < repetitionTarget && (repetitions == 0 || spent < CellBudgetMilliseconds))
        {
            long buildStart = Stopwatch.GetTimestamp();
            relations = BuildRelations(index, query, order, joinCover, trieBuild);
            double buildMilliseconds = Stopwatch.GetElapsedTime(buildStart).TotalMilliseconds;

            long emitStart = Stopwatch.GetTimestamp();
            long flatRows = EmitFactorized(relations, order, pool);
            double emitMilliseconds = Stopwatch.GetElapsedTime(emitStart).TotalMilliseconds;

            double total = buildMilliseconds + emitMilliseconds;
            if(repetitions == 0 || total < bestTotal)
            {
                bestBuild = buildMilliseconds;
                bestDrive = emitMilliseconds;
                bestTotal = total;
            }

            rows = flatRows;
            repetitions++;
            spent += total;
        }

        return new DirectCell(repetitions, bestBuild, bestDrive, bestTotal, rows, false, relations);
    }

    /// <summary>Builds one trie per pattern at the requested depth over the given order — the census never obtains its split cells through the pipeline, so full depth stays measurable.</summary>
    /// <param name="index">The columnar index the relations scan.</param>
    /// <param name="query">The query.</param>
    /// <param name="order">The variable order the trie levels follow.</param>
    /// <param name="joinCover">Whether relations build at their join-cover depths; otherwise full depth.</param>
    /// <param name="trieBuild">How the relations' tries materialise their maps.</param>
    /// <returns>The relations, parallel to the query's patterns.</returns>
    private static List<GeneralizedHashTrie> BuildRelations(ColumnarTripleIndex index, BasicGraphPattern query, IReadOnlyList<Variable> order, bool joinCover, FreeJoinTrieBuild trieBuild)
    {
        Dictionary<Variable, int> orderIndex = new(order.Count);
        for(int position = 0; position < order.Count; position++)
        {
            orderIndex[order[position]] = position;
        }

        HashSet<Variable> joinVariables = FreeJoinPipeline.JoinVariablesOf(index, query);

        List<GeneralizedHashTrie> relations = new(query.Patterns.Count);
        foreach(TriplePattern pattern in query.Patterns)
        {
            IReadOnlyList<Variable> scanSchema = ColumnarBatchScan.ScanSchemaOf(index, pattern);
            int[] columns = FreeJoinPipeline.OrderedColumns(scanSchema, orderIndex);
            int depth = joinCover ? FreeJoinPipeline.JoinCoverDepth(scanSchema, columns, joinVariables) : columns.Length;
            relations.Add(GeneralizedHashTrie.Build(scanSchema, ColumnarBatchScan.Scan(index, pattern), columns[..depth], columns[depth..], trieBuild));
        }

        return relations;
    }

    /// <summary>Drives the flat generic join, counting rows and stopping at the drain backstop.</summary>
    /// <param name="relations">The relations.</param>
    /// <param name="order">The global descent order.</param>
    /// <returns>The drained row count and whether the backstop stopped it.</returns>
    private static (long Rows, bool Capped) DriveFlat(List<GeneralizedHashTrie> relations, IReadOnlyList<Variable> order)
    {
        long rows = 0;
        foreach(SolutionBatch batch in FreeJoinExecutor.Execute(relations, order))
        {
            rows += batch.Count;

            if(rows >= DrainRowCap)
            {
                return (rows, true);
            }
        }

        return (rows, false);
    }

    /// <summary>Runs the factorised emit into a fresh arena and reads the flat row count the batch stands for before the arena is returned.</summary>
    /// <param name="relations">The relations.</param>
    /// <param name="order">The factorised key-first variable order.</param>
    /// <param name="pool">The census run's buffer pool.</param>
    /// <returns>The flat row count, or negative when the executor declined the relations.</returns>
    private static long EmitFactorized(List<GeneralizedHashTrie> relations, IReadOnlyList<Variable> order, VeritasMemoryPool<uint> pool)
    {
        using FactorizedArena arena = new(pool);
        FactorizedBatch? batch = FreeJoinExecutor.ExecuteFactorized(relations, order, arena);

        return batch is null ? -1 : batch.FlatRowCount;
    }

    /// <summary>The policy one rendezvous cell drains under.</summary>
    /// <param name="cell">The cell key.</param>
    /// <returns>The policy.</returns>
    private static QueryEnginePolicy PolicyOf(string cell)
    {
        return cell switch
        {
            "rv-leapfrog" => QueryEnginePolicy.Default with { PreferBatchedForAcyclic = false },
            "rv-freejoin" => QueryEnginePolicy.Default with { PreferFreeJoin = true },
            "rv-freejoinlazy" => QueryEnginePolicy.Default with { PreferFreeJoin = true, FreeJoinTrieBuild = FreeJoinTrieBuild.Lazy },
            _ => QueryEnginePolicy.Default
        };
    }

    /// <summary>The route token for an engine the rendezvous selected.</summary>
    /// <param name="engine">The selected engine.</param>
    /// <returns>The token.</returns>
    private static string RouteToken(QueryEngineKind engine)
    {
        return engine switch
        {
            QueryEngineKind.Hypertrie => "hypertrie",
            QueryEngineKind.Columnar => "columnar",
            QueryEngineKind.ColumnarBatched => "batched",
            QueryEngineKind.FreeJoin => "freejoin",
            QueryEngineKind.SelfIndex => "selfindex",
            _ => Absent
        };
    }

    /// <summary>The reason token for a selection reason.</summary>
    /// <param name="reason">The selection reason.</param>
    /// <returns>The token.</returns>
    private static string ReasonToken(EngineSelectionReason reason)
    {
        return reason switch
        {
            EngineSelectionReason.SystemOfRecord => "SystemOfRecord",
            EngineSelectionReason.ViewReused => "ViewReused",
            EngineSelectionReason.ViewBuilt => "ViewBuilt",
            EngineSelectionReason.SnapshotSuperseded => "SnapshotSuperseded",
            EngineSelectionReason.RotationIncompatible => "RotationIncompatible",
            EngineSelectionReason.ViewBuilding => "ViewBuilding",
            _ => Absent
        };
    }

    /// <summary>The build token for a trie build mode.</summary>
    /// <param name="trieBuild">The build mode.</param>
    /// <returns>The token.</returns>
    private static string BuildToken(FreeJoinTrieBuild trieBuild)
    {
        return trieBuild == FreeJoinTrieBuild.Lazy ? LazyBuildToken : EagerBuildToken;
    }

    /// <summary>The agreement token for a cell's row count against the fixture's reference.</summary>
    /// <param name="rows">The cell's drained row count.</param>
    /// <param name="reference">The fixture's reference row count, or negative when none was established.</param>
    /// <returns>The token.</returns>
    private static string MatchToken(long rows, long reference)
    {
        if(reference < 0)
        {
            return Absent;
        }

        return rows == reference ? "MATCH" : "MISMATCH";
    }

    /// <summary>The note token for a measured cell.</summary>
    /// <param name="fixture">The fixture.</param>
    /// <param name="capped">Whether this cell reached the drain backstop.</param>
    /// <returns>The token.</returns>
    private static string NoteToken(CensusFixture fixture, bool capped)
    {
        if(capped)
        {
            return DrainCappedNote;
        }

        return fixture.SmokeScale ? SmokeScaleNote : Absent;
    }

    /// <summary>The join-tree token: whether the GYO reduction yields a join tree for the query's variable sets.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The token.</returns>
    private static string GyoToken(BasicGraphPattern query)
    {
        List<IReadOnlyCollection<Variable>> edges = new(query.Patterns.Count);
        foreach(TriplePattern pattern in query.Patterns)
        {
            edges.Add(new HashSet<Variable>(pattern.Variables()));
        }

        return GyoJoinTree.TryBuild(edges) is null ? "none" : "tree";
    }

    /// <summary>The number of connected components the query's patterns form over their shared variables, by an iterative union-find.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The component count.</returns>
    private static int ComponentCount(BasicGraphPattern query)
    {
        int count = query.Patterns.Count;
        int[] parent = new int[count];
        for(int pattern = 0; pattern < count; pattern++)
        {
            parent[pattern] = pattern;
        }

        Dictionary<Variable, int> owner = [];
        for(int pattern = 0; pattern < count; pattern++)
        {
            foreach(Variable variable in query.Patterns[pattern].Variables())
            {
                if(owner.TryGetValue(variable, out int other))
                {
                    Merge(parent, pattern, other);
                }
                else
                {
                    owner[variable] = pattern;
                }
            }
        }

        HashSet<int> roots = [];
        for(int pattern = 0; pattern < count; pattern++)
        {
            roots.Add(RootOf(parent, pattern));
        }

        return roots.Count;
    }

    /// <summary>The representative of a set, found by an iterative walk that compresses the path behind it.</summary>
    /// <param name="parent">The union-find parents.</param>
    /// <param name="element">The element.</param>
    /// <returns>The representative.</returns>
    private static int RootOf(int[] parent, int element)
    {
        int root = element;
        while(parent[root] != root)
        {
            root = parent[root];
        }

        int walk = element;
        while(parent[walk] != root)
        {
            int next = parent[walk];
            parent[walk] = root;
            walk = next;
        }

        return root;
    }

    /// <summary>Merges the sets of two elements.</summary>
    /// <param name="parent">The union-find parents.</param>
    /// <param name="left">The first element.</param>
    /// <param name="right">The second element.</param>
    private static void Merge(int[] parent, int left, int right)
    {
        int leftRoot = RootOf(parent, left);
        int rightRoot = RootOf(parent, right);
        if(leftRoot != rightRoot)
        {
            parent[rightRoot] = leftRoot;
        }
    }

    /// <summary>
    /// The realised statistics the fixture line reports, computed from the
    /// emitted triple list rather than from the intended degree sequence: the
    /// total triple count, the maximum out-degree on the primary predicate, the
    /// heavy threshold (the smallest one-based rank at least as large as the
    /// degree at that rank), and the fraction of primary-predicate edges leaving
    /// a heavy node.
    /// </summary>
    /// <param name="triples">The emitted triples.</param>
    /// <param name="primaryPredicate">The predicate the degree statistics are measured on.</param>
    /// <returns>The realised statistics.</returns>
    private static (long Triples, long MaxDegree, long HeavyThreshold, double HeavyFraction) PrimaryDegreeStatistics(List<EncodedTriple> triples, uint primaryPredicate)
    {
        Dictionary<uint, int> outDegrees = [];
        long primaryEdges = 0;

        foreach(EncodedTriple triple in triples)
        {
            if(triple.Predicate.Encoded != primaryPredicate)
            {
                continue;
            }

            primaryEdges++;
            uint subject = triple.Subject.Encoded;
            outDegrees[subject] = outDegrees.TryGetValue(subject, out int existing) ? existing + 1 : 1;
        }

        if(primaryEdges == 0)
        {
            return (triples.Count, 0, 0, 0.0);
        }

        int[] degrees = new int[outDegrees.Count];
        outDegrees.Values.CopyTo(degrees, 0);
        Array.Sort(degrees);
        Array.Reverse(degrees);

        long threshold = degrees.Length;
        for(int rank = 1; rank <= degrees.Length; rank++)
        {
            if(rank >= degrees[rank - 1])
            {
                threshold = rank;

                break;
            }
        }

        long heavyEdges = 0;
        for(int rank = 0; rank < threshold; rank++)
        {
            heavyEdges += degrees[rank];
        }

        return (triples.Count, degrees[0], threshold, (double)heavyEdges / primaryEdges);
    }

    /// <summary>Formats a count: an integer with no group separators, invariantly.</summary>
    /// <param name="value">The count.</param>
    /// <returns>The token.</returns>
    private static string Count(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a millisecond duration to one decimal, invariantly.</summary>
    /// <param name="value">The duration.</param>
    /// <returns>The token.</returns>
    private static string Milliseconds(double value)
    {
        return value.ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a fraction to three decimals, invariantly.</summary>
    /// <param name="value">The fraction.</param>
    /// <returns>The token.</returns>
    private static string Fraction(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a profile exponent to two decimals, invariantly.</summary>
    /// <param name="value">The exponent.</param>
    /// <returns>The token.</returns>
    private static string Exponent(int value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Validates one line against its token count, its field character set, and
    /// every closed vocabulary it carries, then prints it. A violation prints the
    /// poison marker carrying the malformed line as opaque diagnostic text and
    /// the run continues: one poisoned row beats a resit, and the certification
    /// reading counts poison markers.
    /// </summary>
    /// <param name="expectedTokens">The line kind's token count.</param>
    /// <param name="prefix">The line kind's prefix token.</param>
    /// <param name="kind">The line kind's name, as the header lines spell it.</param>
    /// <param name="fields">The line's fields, in order.</param>
    private static void EmitLine(int expectedTokens, string prefix, string kind, string[] fields)
    {
        string composed = Compose(prefix, fields);
        int actualTokens = composed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        if(actualTokens != expectedTokens || !FieldsAreValid(fields) || !VocabulariesHold(kind, fields))
        {
            Console.WriteLine($"[fjcensus] v1 POISONED {kind} {Count(expectedTokens)} {Count(actualTokens)} {composed}");

            return;
        }

        Console.WriteLine(composed);
    }

    /// <summary>Composes the padded line: fields are separated by whitespace runs, which a parser collapses and a reader reads as columns.</summary>
    /// <param name="prefix">The line kind's prefix token.</param>
    /// <param name="fields">The line's fields, in order.</param>
    /// <returns>The composed line.</returns>
    private static string Compose(string prefix, string[] fields)
    {
        StringBuilder builder = new();
        builder.Append(prefix).Append(" v1");

        foreach(string field in fields)
        {
            builder.Append(' ').Append(field.Length == 0 ? "(empty)" : field);
        }

        return builder.ToString();
    }

    /// <summary>Whether every field is non-empty and holds only characters a console copy and a markdown fence carry through unchanged.</summary>
    /// <param name="fields">The line's fields.</param>
    /// <returns><see langword="true"/> when every field is well formed.</returns>
    private static bool FieldsAreValid(string[] fields)
    {
        foreach(string field in fields)
        {
            if(field.Length == 0)
            {
                return false;
            }

            foreach(char character in field)
            {
                bool allowed = character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '+' or '-';
                if(!allowed)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether every closed-vocabulary field of the line holds a literal from its own vocabulary.</summary>
    /// <param name="kind">The line kind's name.</param>
    /// <param name="fields">The line's fields.</param>
    /// <returns><see langword="true"/> when every closed vocabulary holds.</returns>
    private static bool VocabulariesHold(string kind, string[] fields)
    {
        return kind switch
        {
            "fix" => Holds(ShapeVocabulary, fields[1]) && Holds(GyoVocabulary, fields[2]),
            "row" => Holds(CellVocabulary, fields[1])
                && Holds(RouteVocabulary, fields[2])
                && Holds(ViaVocabulary, fields[3])
                && Holds(ReasonVocabulary, fields[4])
                && Holds(DepthVocabulary, fields[5])
                && Holds(BuildVocabulary, fields[6])
                && Holds(MatchVocabulary, fields[15])
                && Holds(NoteVocabulary, fields[16]),
            _ => Holds(CellVocabulary, fields[1])
        };
    }

    /// <summary>Whether a field holds a literal from a vocabulary.</summary>
    /// <param name="vocabulary">The vocabulary.</param>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the field is a literal of the vocabulary.</returns>
    private static bool Holds(string[] vocabulary, string field)
    {
        foreach(string literal in vocabulary)
        {
            if(string.Equals(literal, field, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One census line held until its fixture's drain-cap verdict is known.</summary>
    private sealed class CensusLine
    {
        /// <summary>Constructs a held line.</summary>
        /// <param name="expectedTokens">The line kind's token count.</param>
        /// <param name="prefix">The line kind's prefix token.</param>
        /// <param name="kind">The line kind's name.</param>
        /// <param name="fields">The line's fields, in order.</param>
        internal CensusLine(int expectedTokens, string prefix, string kind, string[] fields)
        {
            ExpectedTokens = expectedTokens;
            Prefix = prefix;
            Kind = kind;
            Fields = fields;
        }

        /// <summary>The line kind's token count.</summary>
        internal int ExpectedTokens { get; }

        /// <summary>The line kind's prefix token.</summary>
        internal string Prefix { get; }

        /// <summary>The line kind's name.</summary>
        internal string Kind { get; }

        /// <summary>The line's fields, in order.</summary>
        internal string[] Fields { get; }
    }

    /// <summary>
    /// Captures the rendezvous decision off the query trace bus, so a cell's
    /// route is the engine that actually ran rather than the policy's name.
    /// Carries its state as an object rather than a closure, and rides the warm
    /// drain only.
    /// </summary>
    private sealed class EngineSelectionProbe
    {
        /// <summary>The engine the rendezvous selected.</summary>
        internal QueryEngineKind Engine { get; private set; }

        /// <summary>Why the rendezvous selected it.</summary>
        internal EngineSelectionReason Reason { get; private set; }

        /// <summary>Whether a selection event was observed at all.</summary>
        internal bool Observed { get; private set; }

        /// <summary>Records a selection event. Method-group convertible to the trace handler shape.</summary>
        /// <param name="traceEvent">The trace event.</param>
        internal void Handle(in QueryTraceEvent traceEvent)
        {
            if(traceEvent.Kind == QueryTraceEventKind.EngineSelected)
            {
                Engine = traceEvent.Engine;
                Reason = traceEvent.SelectionReason;
                Observed = true;
            }
        }
    }

    /// <summary>One drained rendezvous cell.</summary>
    /// <param name="Repetitions">The timed repetitions the budget allowed.</param>
    /// <param name="TotalMilliseconds">The best repetition's elapsed milliseconds.</param>
    /// <param name="Rows">The drained row count.</param>
    /// <param name="Capped">Whether the drain reached the backstop.</param>
    /// <param name="Engine">The engine the rendezvous selected.</param>
    /// <param name="Reason">Why the rendezvous selected it.</param>
    /// <param name="EngineObserved">Whether a selection event was observed.</param>
    private sealed record RendezvousCell(int Repetitions, double TotalMilliseconds, long Rows, bool Capped, QueryEngineKind Engine, EngineSelectionReason Reason, bool EngineObserved);

    /// <summary>One directly driven cell, whose two phases are timed apart and whose relations the harness owns.</summary>
    /// <param name="Repetitions">The timed repetitions the budget allowed.</param>
    /// <param name="BuildMilliseconds">The best repetition's structure-build milliseconds.</param>
    /// <param name="DriveMilliseconds">The best repetition's drive or emit milliseconds.</param>
    /// <param name="TotalMilliseconds">The best repetition's total.</param>
    /// <param name="Rows">The drained or stood-for row count.</param>
    /// <param name="Capped">Whether the drive reached the backstop.</param>
    /// <param name="Relations">The relations of the last repetition, whose footprint the per-relation lines report.</param>
    private sealed record DirectCell(int Repetitions, double BuildMilliseconds, double DriveMilliseconds, double TotalMilliseconds, long Rows, bool Capped, List<GeneralizedHashTrie> Relations);
}
