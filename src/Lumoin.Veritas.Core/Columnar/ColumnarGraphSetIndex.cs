using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Lumoin.Veritas.Core.Encoding;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The columnar shared arena: one physical column set per
/// permutation over MANY named graphs, graph-major. Each order's
/// columns are the concatenation of per-graph CSR runs with
/// absolute offsets, and a directory records each graph's level-0
/// range per order — so a graph resolves to a
/// <see cref="ColumnarTripleIndex"/> VIEW over the shared columns
/// at the cost of a directory entry, not a per-graph index paying
/// block and metadata floors.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why graph-major.</b> The arity decision record measured that
/// the graph dimension PARTITIONS rather than joins: bind-first
/// graph selection beat the quad-descent stand-in on every fixture.
/// Graph-major concatenation makes <c>GRAPH &lt;g&gt;</c> a range
/// restriction — the view's level-0 slice — while the per-graph
/// fixed cost collapses to one directory row, mirroring what the
/// hypertrie side's shared <c>NodeStore</c> arena did for stores.
/// </para>
/// <para>
/// <b>Isolation.</b> Group boundaries never merge across graphs
/// (each graph's run emits separately), so two adjacent graphs
/// sharing level values stay distinct groups; a view's descent
/// cannot leave its graph because its level-0 slice and the
/// absolute offsets bound every deeper level.
/// </para>
/// <para>
/// <b>Mutability.</b> The set is immutable; advancing a dataset
/// rebuilds it lazily (the rendezvous's generation pattern).
/// Per-graph delta evolution over the shared columns is the
/// recorded follow-up. <see cref="ColumnarTripleIndex.Apply"/> on
/// a VIEW compacts into a standalone per-graph index and leaves
/// the set untouched.
/// </para>
/// </remarks>
[DebuggerDisplay("ColumnarGraphSetIndex Graphs={GraphCount} Triples={TripleCount}")]
public sealed class ColumnarGraphSetIndex
{
    /// <summary>One graph's directory row.</summary>
    /// <param name="Level0Ranges">The graph's level-0 (start, end) range per permutation index; unmaterialised orders carry the default.</param>
    /// <param name="TripleCount">The graph's distinct triple count.</param>
    private sealed record GraphEntry((int Start, int End)[] Level0Ranges, int TripleCount);

    private readonly ColumnarOrder?[] orders;

    private readonly Dictionary<TermId, GraphEntry> directory;

    //Views memoized per graph: a view is a small object over the
    //shared columns, minted once per set lifetime on first touch.
    private readonly Dictionary<TermId, ColumnarTripleIndex> views = [];

    /// <summary>Guards <see cref="views"/>; the set itself is immutable.</summary>
    private Lock ViewLock { get; } = new();

    /// <summary>Which permutation set the shared columns materialise.</summary>
    public ColumnarOrderSetMode OrderSetMode { get; }

    /// <summary>Where the shared columns' block-packed payloads live; carried into each graph view.</summary>
    internal ColumnPayloadBacking Backing { get; }

    /// <summary>The number of graphs in the set.</summary>
    public int GraphCount => directory.Count;

    /// <summary>The total distinct triple count across all graphs.</summary>
    public long TripleCount { get; }

    /// <summary>The packed size in bytes across the shared orders — the number the soak ladder tracks. Directory rows add ~the ranges array per graph.</summary>
    public long PackedByteCount
    {
        get
        {
            long total = 0;
            for(int i = 0; i < orders.Length; i++)
            {
                total += orders[i]?.PackedByteCount ?? 0;
            }

            return total;
        }
    }

    private ColumnarGraphSetIndex(
        ColumnarOrder?[] orders,
        ColumnarOrderSetMode orderSetMode,
        Dictionary<TermId, GraphEntry> directory,
        long tripleCount,
        ColumnPayloadBacking backing)
    {
        this.orders = orders;
        OrderSetMode = orderSetMode;
        this.directory = directory;
        TripleCount = tripleCount;
        Backing = backing;
    }

    /// <summary>
    /// Builds the shared columns over the given graphs. Each
    /// graph's triples deduplicate independently; graphs emit in
    /// ascending graph-id order so rebuilds are deterministic.
    /// </summary>
    /// <param name="graphs">The graphs' triples keyed by graph-name term id.</param>
    /// <param name="orderSetMode">Which permutation set to materialise; see <see cref="ColumnarOrderSetMode"/> for the trade.</param>
    /// <param name="backing">Where the shared columns' block-packed payloads live; default managed. See <see cref="ColumnPayloadBacking"/>.</param>
    /// <returns>The graph-set index.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graphs"/> is <c>null</c>.</exception>
    public static ColumnarGraphSetIndex Build(
        IReadOnlyDictionary<TermId, IEnumerable<EncodedTriple>> graphs,
        ColumnarOrderSetMode orderSetMode = ColumnarOrderSetMode.AllSixOrders,
        ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        ArgumentNullException.ThrowIfNull(graphs);

        //Deterministic graph order; per-graph dedup once, shared by
        //every order's sort (each order re-sorts the same arrays in
        //place, exactly as the single-graph build does).
        List<TermId> graphIds = [.. graphs.Keys];
        graphIds.Sort(static (left, right) => left.Encoded.CompareTo(right.Encoded));

        List<EncodedTriple[]> runs = new(graphIds.Count);
        long tripleCount = 0;
        foreach(TermId graph in graphIds)
        {
            HashSet<EncodedTriple> distinct = [.. graphs[graph]];
            EncodedTriple[] run = [.. distinct];
            runs.Add(run);
            tripleCount += run.Length;
        }

        ColumnarOrder?[] orders = new ColumnarOrder?[6];
        (int Start, int End)[][] rangesPerOrder = new (int, int)[6][];

        for(int i = 0; i < 6; i++)
        {
            if(!ColumnarTripleIndex.IsPermutationInMode(i, orderSetMode))
            {
                continue;
            }

            (int Start, int End)[] level0Ranges = new (int, int)[runs.Count];
            orders[i] = ColumnarOrder.BuildConcatenated(runs, [.. ColumnarTripleIndex.PermutationAt(i)], level0Ranges, backing: backing);
            rangesPerOrder[i] = level0Ranges;
        }

        Dictionary<TermId, GraphEntry> directory = new(graphIds.Count);
        for(int g = 0; g < graphIds.Count; g++)
        {
            (int Start, int End)[] perOrder = new (int, int)[6];
            for(int i = 0; i < 6; i++)
            {
                if(rangesPerOrder[i] is not null)
                {
                    perOrder[i] = rangesPerOrder[i][g];
                }
            }

            directory[graphIds[g]] = new GraphEntry(perOrder, runs[g].Length);
        }

        return new ColumnarGraphSetIndex(orders, orderSetMode, directory, tripleCount, backing);
    }

    /// <summary>The graph-name term ids in the set.</summary>
    public IReadOnlyCollection<TermId> GraphNames => directory.Keys;

    /// <summary>Whether the set has a graph for a graph name.</summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <returns><c>true</c> when the graph exists.</returns>
    public bool ContainsGraph(TermId graph)
    {
        return directory.ContainsKey(graph);
    }

    /// <summary>
    /// Resolves a graph to a queryable <see cref="ColumnarTripleIndex"/>
    /// view over the shared columns, minting and memoizing it on
    /// first touch.
    /// </summary>
    /// <param name="graph">The graph-name term id.</param>
    /// <returns>The view, or <c>null</c> when the set has no such graph.</returns>
    public ColumnarTripleIndex? GetView(TermId graph)
    {
        if(!directory.TryGetValue(graph, out GraphEntry? entry))
        {
            return null;
        }

        lock(ViewLock)
        {
            if(views.TryGetValue(graph, out ColumnarTripleIndex? existing))
            {
                return existing;
            }

            ColumnarTripleIndex view = ColumnarTripleIndex.CreateView(orders, OrderSetMode, entry.TripleCount, entry.Level0Ranges, Backing);
            views[graph] = view;

            return view;
        }
    }
}
