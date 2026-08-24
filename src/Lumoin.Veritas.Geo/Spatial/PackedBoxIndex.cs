using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// A bulk-loaded packed R-tree over <see cref="BoundingBox"/> items: the
/// public, feature-level 2D spatial query index. <see cref="TryBuild"/>
/// ingests an item sequence once; <see cref="Intersecting"/>,
/// <see cref="ContainedIn"/>, and <see cref="Containing"/> then enumerate the
/// registration indices of candidate items for a query box. Two packing
/// families are selectable through <see cref="PackedBoxIndexOptions"/> —
/// Sort-Tile-Recursive and Hilbert-curve — sharing one engine, one layout,
/// and one query path, so the ordering pass is the only degree of freedom
/// and candidate sets are identical across packings by construction.
/// </summary>
/// <remarks>
/// <para>
/// <b>The exactness contract has two levels.</b> At the box level the
/// enumerations are exact: leaf refinement applies the closed-interval
/// four-ordinate algebra (<see cref="BoundingBox.Intersects"/> /
/// <see cref="BoundingBox.Contains"/>, spelled over the stored columns) to
/// exactly-stored ordinates, so no over-report exists in box terms. At a
/// consumer's geometry level they are the exact-superset envelope filter: an
/// envelope hit never proves a geometry relation, an envelope miss disproves
/// one — over-report is allowed, a missed hit is impossible.
/// </para>
/// <para>
/// <b>The containment route.</b> <see cref="Containing"/> does not descend
/// the packed union-bound tree: a union of many small disjoint boxes is wide
/// and contains almost any probe while no leaf does, so that traversal
/// degrades toward linear node visits on archipelago-shaped input. Instead
/// an embedded four-axis dominance tree over the item slots — erected at the
/// route's FIRST use of each built epoch under the deferred default, or at
/// the tail of <see cref="TryBuild"/> under
/// <see cref="DominanceMaterializationMode.EagerAtBuild"/> — answers the
/// containment question as a dominance
/// query on the point (MinX, MaxX, MinY, MaxY), which a per-coordinate
/// half-space partition prunes where a union bound cannot — and every
/// dominance node carries its subtree's exact union box, so the descent
/// composes BOTH prune rules: at each pop the union box kills a subtree that
/// cannot contain the query in at most four compares (the conjunctive test a
/// one-axis partition is structurally blind to), and survivors fall through
/// to the half-space prune on the split coordinate. A containing query runs
/// one such descent, refines leaves with the same exact algebra as every
/// other mode, then emits its matches sorted by an emission rank fixed at
/// materialization. The rank is each item slot's position in the full
/// preorder walk of the packed tree, and pruning only ever restricts that
/// walk, so the emission order of every containing query is the same
/// contractual per-packing order the other modes emit — candidate sets and
/// order are unchanged by the route, by construction. The deferred
/// materialization is a pure function of the committed columns: WHICH caller
/// triggers it and WHEN cannot appear in the structure, so determinism is
/// untouched. This type carries the sub-linear containment envelope itself
/// and is the containment path a consumer builds unless its own measurement
/// says otherwise; <see cref="BoxContainmentIndex"/> — the standalone
/// containment-only structure — stays selectable for a workload that never
/// asks the other two questions.
/// </para>
/// <para>
/// <b>Determinism.</b> The index is a deterministic function of
/// (options, item sequence): the same options over the same sequence produce
/// the same tree and the same enumeration sequence for every query, machine-
/// and process-independent. Every ordering element carries a unique
/// tie-break index (registration order at the leaf pass, creation order
/// above), so no sorted sequence ever depends on a sort algorithm's tie
/// behaviour. Enumeration order is ascending tree preorder, contractual
/// within a (packing, capacity) configuration; candidate sets are equal
/// across configurations; the two packings do not share an order.
/// </para>
/// <para>
/// <b>Query well-formedness.</b> Queries are total — enumerators, never
/// <c>Try</c>: empty enumeration is an answer. A query box in the same
/// malformedness bucket the build side refuses (any non-finite ordinate, or
/// an inverted axis) enumerates nothing in every mode, by an explicit guard:
/// the closed-interval formulas answer nonsense for inverted boxes, and the
/// set-theoretic reading is an over-report footgun for every named consumer.
/// </para>
/// <para>
/// <b>Threading and lifetime.</b> After <see cref="TryBuild"/> returns, the
/// columns are read-only until the next build — EXCEPT the write-once,
/// internally-synchronized dominance materialization the containing route
/// performs at its first use of each epoch: concurrent first callers block
/// on an internal lock until the one materialization completes, concurrent
/// queries of the other modes read only columns the materialization never
/// writes, and no span moves during it (every capacity operation ran inside
/// <see cref="TryBuild"/>). Under
/// <see cref="DominanceMaterializationMode.EagerAtBuild"/> the same gate runs
/// at the build tail instead, so the columns are wholly read-only the moment
/// <see cref="TryBuild"/> returns. Each enumerator owns its own pooled traversal
/// stack and the pool is thread-safe, so nested and interleaved enumerations
/// over one index are legal and concurrent queries from multiple threads are
/// safe. An index handed to another thread must be published through a
/// synchronizing operation — unsynchronized handoff is undefined in every
/// mode. The single-writer rule is guarded only where the guard reaches:
/// every <see cref="TryBuild"/> (success and refusal) and
/// <see cref="Dispose"/> advances an internal build version, and a live
/// enumerator throws <see cref="InvalidOperationException"/> from its next
/// <see cref="Enumerator.MoveNext"/> when its captured version no longer
/// matches — but nothing observes a build or dispose racing an in-flight
/// query or materialization; that pattern is out of contract, and
/// <see cref="Dispose"/> takes the materialization lock so an in-flight
/// materialization completes before any column returns to the pool.
/// <see cref="Dispose"/> cascades every pooled column; disposed-state access
/// throws <see cref="ObjectDisposedException"/> from every member of this
/// type.
/// </para>
/// <para>
/// <b>Enumeration patterns.</b> The default, which cannot leak:
/// <c>foreach (int index in index.Intersecting(query)) { … }</c> — the
/// <see langword="foreach"/> pattern disposes the enumerator and returns its
/// stack rental. The manual form —
/// <c>using var enumerator = index.Intersecting(query).GetEnumerator(); while (enumerator.MoveNext()) { … }</c>
/// — is required where the enumerator's diagnostics are read after
/// traversal. An abandoned undisposed enumerator leaks its pooled segment
/// for the process lifetime, which is why <see langword="foreach"/> is the
/// documented default.
/// </para>
/// <para>
/// <b>Structure.</b> Items and node records live in structure-of-arrays
/// pooled columns over one slot parade: item slots first in leaf packing
/// order, then each node level in its packing order (each level is ordered
/// by the packing over the level's own entries), the root last. A node's
/// children occupy one contiguous run of the previous region, so a node
/// carries only a first-child slot and a child count; a node slot whose
/// first child lies below <see cref="Count"/> is a leaf node whose run
/// consists of item slots.
/// </para>
/// </remarks>
public sealed class PackedBoxIndex: IDisposable
{
    /// <summary>The smallest sanctioned node capacity; a capacity of one cannot form a tree.</summary>
    private const int MinimumNodeCapacity = 2;

    /// <summary>The largest sanctioned node capacity.</summary>
    private const int MaximumNodeCapacity = 65536;

    /// <summary>The shipped Hilbert grid width. 31 bits per axis: ties are the enemy of the determinism tie-break and of packing quality, a wider key shrinks tie classes at zero query-time cost.</summary>
    private const int DefaultHilbertGridBitsPerAxis = 31;

    /// <summary>The dominance tree's leaf ceiling: a range at or below this refines in place instead of splitting. Eight items — the derived node and depth bounds are stated against this value.</summary>
    private const int DominanceLeafSize = 8;

    /// <summary>The containing route's collect buffer starts at this small constant and grows on demand — an empty result never rents item-scale storage.</summary>
    private const int CollectInitialCapacity = 16;

    /// <summary>Pooled columns start small and grow on first build; the steady state reuses them across rebuilds.</summary>
    private const int InitialCapacity = 16;

    /// <summary>Per-slot bounds over the whole slot parade, declared in MinX, MinY, MaxX, MaxY order: an item slot carries the registered item's box, a node slot the exact union over its contiguous child run.</summary>
    private PackedBoxColumn<double> MinXColumn { get; }

    /// <summary>The minimum-y member of the per-slot bounds group.</summary>
    private PackedBoxColumn<double> MinYColumn { get; }

    /// <summary>The maximum-x member of the per-slot bounds group.</summary>
    private PackedBoxColumn<double> MaxXColumn { get; }

    /// <summary>The maximum-y member of the per-slot bounds group.</summary>
    private PackedBoxColumn<double> MaxYColumn { get; }

    /// <summary>Item slots: the registration index. Node slots: the first-child parade slot. The leaf/internal discriminator: a node slot is a leaf exactly when this value lies below <see cref="Count"/>.</summary>
    private PackedBoxColumn<int> ChildStartColumn { get; }

    /// <summary>Node slots: the child count. Item slots are never written and never read — pool segments recycle uncleared, and never reading an unwritten slot is what keeps builds process-independent.</summary>
    private PackedBoxColumn<int> ChildCountColumn { get; }

    /// <summary>Per node level, the parade slot where the level begins; the level's end is the next level's start, or the parade end for the root level.</summary>
    private PackedBoxColumn<int> LevelStartColumn { get; }

    /// <summary>Center ordinates of the entries under the current ordering pass, indexed by entry index (registration order at the leaf pass, creation order above). Build-time scratch.</summary>
    private PackedBoxColumn<double> CenterXColumn { get; }

    /// <summary>The center-y member of the ordering pass's center group.</summary>
    private PackedBoxColumn<double> CenterYColumn { get; }

    /// <summary>Hilbert ordering elements of the current pass. Build-time scratch, used only under <see cref="BoxIndexPacking.HilbertCurve"/>.</summary>
    private PackedBoxColumn<HilbertBoxKey> HilbertKeyColumn { get; }

    /// <summary>Sort-Tile-Recursive ordering elements of the current pass, re-keyed between the center-X and within-slice center-Y sorts. Build-time scratch under <see cref="BoxIndexPacking.SortTileRecursive"/>; the dominance materialization pass reuses it as the axis-sort key column under every packing.</summary>
    private PackedBoxColumn<StrBoxKey> StrKeyColumn { get; }

    /// <summary>The sorted entry order of the current ordering pass: position → entry index. Build-time scratch for the ordering passes; the dominance materialization pass then reuses it as the rank walk's traversal stack and as the dominance build's MinX axis order.</summary>
    private PackedBoxColumn<int> OrderColumn { get; }

    /// <summary>The level under construction, formed in creation order before its ordering pass sorts it into parade order. Build-time scratch (the transient the build's storage-order rule names).</summary>
    private PackedBoxColumn<PackedNodeRecord> NodeScratchColumn { get; }

    /// <summary>
    /// Per item slot, the slot's rank in the full preorder walk of the packed
    /// tree — the fixed total order every query's emission is a restriction
    /// of, written by the dominance materialization pass. The containing
    /// route sorts its dominance matches by this column to reproduce the
    /// contractual per-packing order.
    /// </summary>
    private PackedBoxColumn<int> EmissionRankColumn { get; }

    /// <summary>The dominance tree's permutation of item slots; a dominance node owns one contiguous range of it. Slots, not registration indices — leaf refinement reads the parade columns by slot, and only emission translates through the child-start column.</summary>
    private PackedBoxColumn<int> DominanceOrderColumn { get; }

    /// <summary>Dominance nodes, appended depth-first: a leaf has left −1 and owns its item range; an internal node carries a split axis and value and two children. Every node carries its item range, so no slot is ever read unwritten.</summary>
    private PackedBoxColumn<int> DominanceNodeLeftColumn { get; }

    /// <summary>The right-child member of the dominance node group; written for internal nodes only, and never read at a leaf.</summary>
    private PackedBoxColumn<int> DominanceNodeRightColumn { get; }

    /// <summary>The split axis in the fixed binding 0 = MinX, 1 = MaxX, 2 = MinY, 3 = MaxY — the dominance coordinates of the containment predicate, cycled by depth.</summary>
    private PackedBoxColumn<int> DominanceNodeAxisColumn { get; }

    /// <summary>The split value member of the dominance node group; written for internal nodes only, and never read at a leaf.</summary>
    private PackedBoxColumn<double> DominanceNodeSplitColumn { get; }

    /// <summary>The first dominance-order position of the node's item range; written for every node.</summary>
    private PackedBoxColumn<int> DominanceNodeItemStartColumn { get; }

    /// <summary>The length of the node's item range; written for every node.</summary>
    private PackedBoxColumn<int> DominanceNodeItemSpanColumn { get; }

    /// <summary>The dominance build's pending-subtree stack, written by the dominance materialization pass; capacity reserved at build by the depth bound — transient, excluded from the permanent per-item storage accounting.</summary>
    private PackedBoxColumn<DominanceBuildWorkItem> DominanceBuildStackColumn { get; }

    /// <summary>
    /// The dominance build's three additional per-axis slot orders — MaxX,
    /// MinY, MaxY; the MinX order reuses <see cref="OrderColumn"/>, which is
    /// free once the rank walk ends. Each order holds every item slot sorted
    /// once by its axis's (ordinate, slot) composite; every split then
    /// partitions all four orders stably, preserving each order's sortedness
    /// within both halves, so no per-node re-sort ever runs below the four
    /// initial sorts. Retained-capacity scratch of the dominance
    /// materialization pass under the persistent-scratch convention.
    /// </summary>
    private PackedBoxColumn<int> DominanceMaxXOrderColumn { get; }

    /// <summary>The minimum-y member of the added axis-order group.</summary>
    private PackedBoxColumn<int> DominanceMinYOrderColumn { get; }

    /// <summary>The maximum-y member of the added axis-order group.</summary>
    private PackedBoxColumn<int> DominanceMaxYOrderColumn { get; }

    /// <summary>Per item slot, the side of the current split the slot falls on (0 left, 1 right). Every split writes the flag for BOTH halves of its whole range before any partition reads it, so a stale ancestor flag is never read. Retained-capacity scratch of the dominance materialization pass.</summary>
    private PackedBoxColumn<byte> DominanceSideFlagColumn { get; }

    /// <summary>The stable partition's staging column: a non-split order's range is partitioned into it by side flag and copied back in place. Retained-capacity scratch of the dominance materialization pass.</summary>
    private PackedBoxColumn<int> DominancePartitionScratchColumn { get; }

    /// <summary>
    /// Per dominance node, the exact union box over the node's whole item
    /// range — written for every node, leaves included. The containing
    /// descent tests it at pop before anything else: a subtree whose union
    /// does not contain the query holds no container of it, because the
    /// union's extremes bound every member's ordinates — the conjunctive
    /// prune the one-axis half-space partition is structurally blind to.
    /// </summary>
    private PackedBoxColumn<double> DominanceNodeUnionMinXColumn { get; }

    /// <summary>The minimum-y member of the dominance union group.</summary>
    private PackedBoxColumn<double> DominanceNodeUnionMinYColumn { get; }

    /// <summary>The maximum-x member of the dominance union group.</summary>
    private PackedBoxColumn<double> DominanceNodeUnionMaxXColumn { get; }

    /// <summary>The maximum-y member of the dominance union group.</summary>
    private PackedBoxColumn<double> DominanceNodeUnionMaxYColumn { get; }

    /// <summary>The caller-owned pool the per-query traversal and dominance descent stacks rent from; the same pool backs the integer columns.</summary>
    private VeritasMemoryPool<int> IndexPool { get; }

    /// <summary>The configuration this index was created with; the public <see cref="Options"/> reads it behind the disposed guard.</summary>
    private PackedBoxIndexOptions OptionsValue { get; }

    /// <summary>The Hilbert grid width in bits per axis — an internal build knob (16 or 31); candidate sets are width-invariant, pinned by test.</summary>
    internal int HilbertGridBitsPerAxis { get; }

    /// <summary>The item count of the current build; the public <see cref="Count"/> reads it behind the disposed guard.</summary>
    private int BuiltCount { get; set; }

    /// <summary>The node level count of the current build.</summary>
    private int BuiltLevelCount { get; set; }

    /// <summary>The node slot count of the current build.</summary>
    private int BuiltNodeCount { get; set; }

    /// <summary>The root's parade slot; −1 while never built, refused, or built empty.</summary>
    private int RootSlot { get; set; } = -1;

    /// <summary>The per-query stack rental size established by the current build.</summary>
    private int TraversalStackBound { get; set; }

    /// <summary>The dominance tree's root node; −1 while never built, refused, or built empty.</summary>
    private int DominanceRootNode { get; set; } = -1;

    /// <summary>The dominance node count committed by the current epoch's materialization; zero while pending.</summary>
    private int BuiltDominanceNodeCount { get; set; }

    /// <summary>The containing query's stack rental size established by the current build — the count-halving depth bound, distinct from the union-bound tree's <see cref="TraversalStackBound"/>.</summary>
    internal int DominanceTraversalStackBound { get; private set; }

    /// <summary>Whether this index has been disposed.</summary>
    private bool Disposed { get; set; }

    /// <summary>
    /// Advanced by every <see cref="TryBuild"/> (success and refusal) and by
    /// <see cref="Dispose"/>; enumerators capture it at construction and fail
    /// loud on mismatch — rebuilding under a live enumerator would otherwise
    /// re-rent columns out from under its hoisted spans, silently.
    /// </summary>
    internal int BuildVersion { get; private set; }

    /// <summary>
    /// Rental counters ride naked fields rather than properties: the increments run on the
    /// query path, whose contract admits concurrent callers, so they go through Interlocked —
    /// which requires a ref to the storage a property's backing field cannot give. The
    /// containing route counts two rental classes per query — its descent stack in the
    /// stack class and its collect buffer in its own class — so the leak gate can watch both.
    /// </summary>
    private long stackRentalsIssued;

    /// <summary>The returned half of the stack rental-balance pair; naked for the same Interlocked reason.</summary>
    private long stackRentalsReturned;

    /// <summary>The issued half of the collect rental-balance pair; naked for the same Interlocked reason.</summary>
    private long collectRentalsIssued;

    /// <summary>The returned half of the collect rental-balance pair; naked for the same Interlocked reason.</summary>
    private long collectRentalsReturned;

    /// <summary>
    /// Whether the current epoch's dominance structure is materialized. A
    /// naked field for the documented reason the rental counters share:
    /// Volatile.Read/Write need the ref a property's backing field cannot
    /// give. True at construction and after every refusal, empty build, and
    /// build fault — the rootless states are trivially materialized — and
    /// false exactly from a non-empty build's commit until the pass runs:
    /// at the containing route's (or a dominance accessor's) first use under
    /// the deferred default, or still inside the build tail under
    /// <see cref="DominanceMaterializationMode.EagerAtBuild"/>, where the
    /// window never escapes <see cref="TryBuild"/>.
    /// </summary>
    private bool dominanceMaterialized = true;

    /// <summary>Completed materialization publications across this index's lifetime. Incremented under the materialization lock after the pass completes and before the flag publishes, so the count equals completed publications in every interleaving. A naked field for the documented reason the rental counters share: Interlocked.Increment on the publish path and Interlocked.Read on the diagnostic pin need the ref a property's backing field cannot give.</summary>
    private long dominanceMaterializationCount;

    /// <summary>
    /// Serializes the deferred dominance materialization — and
    /// <see cref="Dispose"/>, so an in-flight pass completes before any
    /// column returns to the pool. A blocking lock, following the shared
    /// pool's precedent, because materialization runs for seconds at scale
    /// and a concurrent containing caller must block for it, not spin.
    /// </summary>
    private Lock DominanceMaterializationLock { get; } = new Lock();

    /// <summary>Traversal-stack rentals issued across this index's lifetime. With <see cref="StackRentalsReturned"/>, makes rental balance assertable, concurrent queries included.</summary>
    internal long StackRentalsIssued => Interlocked.Read(ref stackRentalsIssued);

    /// <summary>Traversal-stack rentals returned across this index's lifetime.</summary>
    internal long StackRentalsReturned => Interlocked.Read(ref stackRentalsReturned);

    /// <summary>Containing-route collect buffers issued across this index's lifetime — the second counted rental class.</summary>
    internal long CollectRentalsIssued => Interlocked.Read(ref collectRentalsIssued);

    /// <summary>Containing-route collect buffers returned across this index's lifetime.</summary>
    internal long CollectRentalsReturned => Interlocked.Read(ref collectRentalsReturned);

    /// <summary>The deferral pin: reports the flag without forcing materialization. Diagnostic surface; does not throw on a disposed index, like the rental counters beside it.</summary>
    internal bool DominanceMaterialized => Volatile.Read(ref dominanceMaterialized);

    /// <summary>The exactly-once pin: completed materialization publications, read without forcing. Diagnostic surface; does not throw on a disposed index.</summary>
    internal long DominanceMaterializationCount => Interlocked.Read(ref dominanceMaterializationCount);

    /// <summary>The items in the current build.</summary>
    public int Count
    {
        get
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            return BuiltCount;
        }
    }

    /// <summary>The configuration this index was created with.</summary>
    public PackedBoxIndexOptions Options
    {
        get
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            return OptionsValue;
        }
    }

    /// <summary>Node levels in the current build — 0 for the empty (and never-built) index, 1 when the sole leaf node is the root. Diagnostic surface for the structure tests.</summary>
    internal int LevelCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            return BuiltLevelCount;
        }
    }

    /// <summary>Node slots in the current build (item slots excluded): Σ ceil(N / capacity^L). Diagnostic surface for the structure tests.</summary>
    internal int NodeCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            return BuiltNodeCount;
        }
    }

    /// <summary>Dominance-tree nodes in the current build, bounded by the derived 2 · ⌈N / 4⌉ − 1 formula. Diagnostic surface for the dominance structure tests; forces the deferred materialization, which at scale runs the whole pass.</summary>
    internal int DominanceNodeCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            EnsureDominanceMaterialized();

            return BuiltDominanceNodeCount;
        }
    }

    /// <summary>Assigns the validated configuration and the columns the factory rented; the constructor itself acquires nothing.</summary>
    /// <param name="options">The validated build configuration.</param>
    /// <param name="hilbertGridBitsPerAxis">The validated Hilbert grid width.</param>
    /// <param name="indexPool">The pool the integer columns and the per-query stacks rent from.</param>
    /// <param name="minXColumn">The minimum-x bounds column.</param>
    /// <param name="minYColumn">The minimum-y bounds column.</param>
    /// <param name="maxXColumn">The maximum-x bounds column.</param>
    /// <param name="maxYColumn">The maximum-y bounds column.</param>
    /// <param name="childStartColumn">The child-start column.</param>
    /// <param name="childCountColumn">The child-count column.</param>
    /// <param name="levelStartColumn">The per-level start column.</param>
    /// <param name="centerXColumn">The center-x scratch column.</param>
    /// <param name="centerYColumn">The center-y scratch column.</param>
    /// <param name="hilbertKeyColumn">The Hilbert ordering-element scratch column.</param>
    /// <param name="strKeyColumn">The composite-key scratch column serving the Sort-Tile-Recursive ordering passes and the dominance axis sorts.</param>
    /// <param name="orderColumn">The sorted-order scratch column.</param>
    /// <param name="nodeScratchColumn">The level-under-construction scratch column.</param>
    /// <param name="emissionRankColumn">The per-item emission-rank column.</param>
    /// <param name="dominanceOrderColumn">The dominance order column.</param>
    /// <param name="dominanceNodeLeftColumn">The dominance left-child column.</param>
    /// <param name="dominanceNodeRightColumn">The dominance right-child column.</param>
    /// <param name="dominanceNodeAxisColumn">The dominance split-axis column.</param>
    /// <param name="dominanceNodeSplitColumn">The dominance split-value column.</param>
    /// <param name="dominanceNodeItemStartColumn">The dominance item-range start column.</param>
    /// <param name="dominanceNodeItemSpanColumn">The dominance item-range span column.</param>
    /// <param name="dominanceBuildStackColumn">The dominance build's pending-subtree stack column.</param>
    /// <param name="dominanceMaxXOrderColumn">The maximum-x axis-order column.</param>
    /// <param name="dominanceMinYOrderColumn">The minimum-y axis-order column.</param>
    /// <param name="dominanceMaxYOrderColumn">The maximum-y axis-order column.</param>
    /// <param name="dominanceSideFlagColumn">The per-slot side-flag column.</param>
    /// <param name="dominancePartitionScratchColumn">The stable partition's staging column.</param>
    /// <param name="dominanceNodeUnionMinXColumn">The dominance union minimum-x column.</param>
    /// <param name="dominanceNodeUnionMinYColumn">The dominance union minimum-y column.</param>
    /// <param name="dominanceNodeUnionMaxXColumn">The dominance union maximum-x column.</param>
    /// <param name="dominanceNodeUnionMaxYColumn">The dominance union maximum-y column.</param>
    private PackedBoxIndex(
        PackedBoxIndexOptions options,
        int hilbertGridBitsPerAxis,
        VeritasMemoryPool<int> indexPool,
        PackedBoxColumn<double> minXColumn,
        PackedBoxColumn<double> minYColumn,
        PackedBoxColumn<double> maxXColumn,
        PackedBoxColumn<double> maxYColumn,
        PackedBoxColumn<int> childStartColumn,
        PackedBoxColumn<int> childCountColumn,
        PackedBoxColumn<int> levelStartColumn,
        PackedBoxColumn<double> centerXColumn,
        PackedBoxColumn<double> centerYColumn,
        PackedBoxColumn<HilbertBoxKey> hilbertKeyColumn,
        PackedBoxColumn<StrBoxKey> strKeyColumn,
        PackedBoxColumn<int> orderColumn,
        PackedBoxColumn<PackedNodeRecord> nodeScratchColumn,
        PackedBoxColumn<int> emissionRankColumn,
        PackedBoxColumn<int> dominanceOrderColumn,
        PackedBoxColumn<int> dominanceNodeLeftColumn,
        PackedBoxColumn<int> dominanceNodeRightColumn,
        PackedBoxColumn<int> dominanceNodeAxisColumn,
        PackedBoxColumn<double> dominanceNodeSplitColumn,
        PackedBoxColumn<int> dominanceNodeItemStartColumn,
        PackedBoxColumn<int> dominanceNodeItemSpanColumn,
        PackedBoxColumn<DominanceBuildWorkItem> dominanceBuildStackColumn,
        PackedBoxColumn<int> dominanceMaxXOrderColumn,
        PackedBoxColumn<int> dominanceMinYOrderColumn,
        PackedBoxColumn<int> dominanceMaxYOrderColumn,
        PackedBoxColumn<byte> dominanceSideFlagColumn,
        PackedBoxColumn<int> dominancePartitionScratchColumn,
        PackedBoxColumn<double> dominanceNodeUnionMinXColumn,
        PackedBoxColumn<double> dominanceNodeUnionMinYColumn,
        PackedBoxColumn<double> dominanceNodeUnionMaxXColumn,
        PackedBoxColumn<double> dominanceNodeUnionMaxYColumn)
    {
        OptionsValue = options;
        HilbertGridBitsPerAxis = hilbertGridBitsPerAxis;
        IndexPool = indexPool;
        MinXColumn = minXColumn;
        MinYColumn = minYColumn;
        MaxXColumn = maxXColumn;
        MaxYColumn = maxYColumn;
        ChildStartColumn = childStartColumn;
        ChildCountColumn = childCountColumn;
        LevelStartColumn = levelStartColumn;
        CenterXColumn = centerXColumn;
        CenterYColumn = centerYColumn;
        HilbertKeyColumn = hilbertKeyColumn;
        StrKeyColumn = strKeyColumn;
        OrderColumn = orderColumn;
        NodeScratchColumn = nodeScratchColumn;
        EmissionRankColumn = emissionRankColumn;
        DominanceOrderColumn = dominanceOrderColumn;
        DominanceNodeLeftColumn = dominanceNodeLeftColumn;
        DominanceNodeRightColumn = dominanceNodeRightColumn;
        DominanceNodeAxisColumn = dominanceNodeAxisColumn;
        DominanceNodeSplitColumn = dominanceNodeSplitColumn;
        DominanceNodeItemStartColumn = dominanceNodeItemStartColumn;
        DominanceNodeItemSpanColumn = dominanceNodeItemSpanColumn;
        DominanceBuildStackColumn = dominanceBuildStackColumn;
        DominanceMaxXOrderColumn = dominanceMaxXOrderColumn;
        DominanceMinYOrderColumn = dominanceMinYOrderColumn;
        DominanceMaxYOrderColumn = dominanceMaxYOrderColumn;
        DominanceSideFlagColumn = dominanceSideFlagColumn;
        DominancePartitionScratchColumn = dominancePartitionScratchColumn;
        DominanceNodeUnionMinXColumn = dominanceNodeUnionMinXColumn;
        DominanceNodeUnionMinYColumn = dominanceNodeUnionMinYColumn;
        DominanceNodeUnionMaxXColumn = dominanceNodeUnionMaxXColumn;
        DominanceNodeUnionMaxYColumn = dominanceNodeUnionMaxYColumn;
    }

    /// <summary>
    /// Validates <paramref name="options"/> and constructs the index with its
    /// pooled columns rented at initial capacity — acquisition happens in the
    /// factory body, and the private constructor only assigns;
    /// <see cref="TryBuild"/> only grows. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for an undefined packing, a
    /// node capacity outside [2, 65536], or an undefined dominance
    /// materialization mode — including
    /// <c>default(PackedBoxIndexOptions)</c>, whose zero capacity is the
    /// record-struct default trap; <see cref="PackedBoxIndexOptions.Default"/>
    /// is the sanctioned default.
    /// </summary>
    /// <param name="options">The build configuration.</param>
    /// <param name="ordinatePool">
    /// The caller-owned pool the ordinate columns rent from; the shared
    /// pool when omitted. The pool stays the caller's to dispose and must
    /// outlive the index.
    /// </param>
    /// <param name="indexPool">
    /// The caller-owned pool the integer columns and every per-query
    /// stack rent from; the shared pool when omitted.
    /// </param>
    /// <returns>The constructed index.</returns>
    public static PackedBoxIndex Create(
        PackedBoxIndexOptions options,
        VeritasMemoryPool<double>? ordinatePool = null,
        VeritasMemoryPool<int>? indexPool = null)
    {
        return CreateCore(options, ordinatePool, indexPool, DefaultHilbertGridBitsPerAxis);
    }

    /// <summary>
    /// The internal width-knob overload: <paramref name="hilbertGridBitsPerAxis"/>
    /// must be 16 or 31. Candidate sets are width-invariant (pinned by the
    /// parity suite's config axis); the public surface always builds at 31.
    /// </summary>
    /// <param name="options">The build configuration.</param>
    /// <param name="hilbertGridBitsPerAxis">The Hilbert grid width in bits per axis; 16 or 31.</param>
    /// <returns>The constructed index.</returns>
    internal static PackedBoxIndex Create(PackedBoxIndexOptions options, int hilbertGridBitsPerAxis)
    {
        return CreateCore(options, ordinatePool: null, indexPool: null, hilbertGridBitsPerAxis);
    }

    /// <summary>The single validating factory body both entry points funnel through.</summary>
    /// <param name="options">The build configuration.</param>
    /// <param name="ordinatePool">The ordinate-column pool, or <see langword="null"/> for the shared pool.</param>
    /// <param name="indexPool">The integer-column and stack pool, or <see langword="null"/> for the shared pool.</param>
    /// <param name="hilbertGridBitsPerAxis">The Hilbert grid width in bits per axis; 16 or 31.</param>
    /// <returns>The constructed index.</returns>
    private static PackedBoxIndex CreateCore(
        PackedBoxIndexOptions options,
        VeritasMemoryPool<double>? ordinatePool,
        VeritasMemoryPool<int>? indexPool,
        int hilbertGridBitsPerAxis)
    {
        if(options.Packing != BoxIndexPacking.SortTileRecursive && options.Packing != BoxIndexPacking.HilbertCurve)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Packing {options.Packing} is not a defined {nameof(BoxIndexPacking)} member.");
        }

        if(options.NodeCapacity < MinimumNodeCapacity || options.NodeCapacity > MaximumNodeCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"NodeCapacity {options.NodeCapacity} is outside the sanctioned range [{MinimumNodeCapacity}, {MaximumNodeCapacity}]. Note that default({nameof(PackedBoxIndexOptions)}) carries capacity 0; use {nameof(PackedBoxIndexOptions)}.{nameof(PackedBoxIndexOptions.Default)}.");
        }

        if(options.DominanceMaterialization != DominanceMaterializationMode.DeferredToFirstUse && options.DominanceMaterialization != DominanceMaterializationMode.EagerAtBuild)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"DominanceMaterialization {options.DominanceMaterialization} is not a defined {nameof(DominanceMaterializationMode)} member.");
        }

        if(hilbertGridBitsPerAxis != 16 && hilbertGridBitsPerAxis != DefaultHilbertGridBitsPerAxis)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hilbertGridBitsPerAxis),
                $"The Hilbert grid width must be 16 or {DefaultHilbertGridBitsPerAxis} bits per axis; got {hilbertGridBitsPerAxis}.");
        }

        VeritasMemoryPool<double> ordinates = ordinatePool ?? VeritasMemoryPool<double>.Shared;
        VeritasMemoryPool<int> indices = indexPool ?? VeritasMemoryPool<int>.Shared;

        PackedBoxColumn<double>? minX = null;
        PackedBoxColumn<double>? minY = null;
        PackedBoxColumn<double>? maxX = null;
        PackedBoxColumn<double>? maxY = null;
        PackedBoxColumn<int>? childStart = null;
        PackedBoxColumn<int>? childCount = null;
        PackedBoxColumn<int>? levelStart = null;
        PackedBoxColumn<double>? centerX = null;
        PackedBoxColumn<double>? centerY = null;
        PackedBoxColumn<HilbertBoxKey>? hilbertKeys = null;
        PackedBoxColumn<StrBoxKey>? strKeys = null;
        PackedBoxColumn<int>? order = null;
        PackedBoxColumn<PackedNodeRecord>? nodeScratch = null;
        PackedBoxColumn<int>? emissionRanks = null;
        PackedBoxColumn<int>? dominanceOrder = null;
        PackedBoxColumn<int>? dominanceLeft = null;
        PackedBoxColumn<int>? dominanceRight = null;
        PackedBoxColumn<int>? dominanceAxis = null;
        PackedBoxColumn<double>? dominanceSplit = null;
        PackedBoxColumn<int>? dominanceItemStart = null;
        PackedBoxColumn<int>? dominanceItemSpan = null;
        PackedBoxColumn<DominanceBuildWorkItem>? dominanceBuildStack = null;
        PackedBoxColumn<int>? dominanceMaxXOrder = null;
        PackedBoxColumn<int>? dominanceMinYOrder = null;
        PackedBoxColumn<int>? dominanceMaxYOrder = null;
        PackedBoxColumn<byte>? dominanceSideFlags = null;
        PackedBoxColumn<int>? dominancePartitionScratch = null;
        PackedBoxColumn<double>? dominanceUnionMinX = null;
        PackedBoxColumn<double>? dominanceUnionMinY = null;
        PackedBoxColumn<double>? dominanceUnionMaxX = null;
        PackedBoxColumn<double>? dominanceUnionMaxY = null;

        try
        {
            minX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            minY = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            maxX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            maxY = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            childStart = new PackedBoxColumn<int>(indices, InitialCapacity);
            childCount = new PackedBoxColumn<int>(indices, InitialCapacity);
            levelStart = new PackedBoxColumn<int>(indices, InitialCapacity);
            centerX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            centerY = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            hilbertKeys = new PackedBoxColumn<HilbertBoxKey>(VeritasMemoryPool<HilbertBoxKey>.Shared, InitialCapacity);
            strKeys = new PackedBoxColumn<StrBoxKey>(VeritasMemoryPool<StrBoxKey>.Shared, InitialCapacity);
            order = new PackedBoxColumn<int>(indices, InitialCapacity);
            nodeScratch = new PackedBoxColumn<PackedNodeRecord>(VeritasMemoryPool<PackedNodeRecord>.Shared, InitialCapacity);
            emissionRanks = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceOrder = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceLeft = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceRight = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceAxis = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceSplit = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            dominanceItemStart = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceItemSpan = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceBuildStack = new PackedBoxColumn<DominanceBuildWorkItem>(VeritasMemoryPool<DominanceBuildWorkItem>.Shared, InitialCapacity);
            dominanceMaxXOrder = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceMinYOrder = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceMaxYOrder = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceSideFlags = new PackedBoxColumn<byte>(VeritasMemoryPool<byte>.Shared, InitialCapacity);
            dominancePartitionScratch = new PackedBoxColumn<int>(indices, InitialCapacity);
            dominanceUnionMinX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            dominanceUnionMinY = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            dominanceUnionMaxX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            dominanceUnionMaxY = new PackedBoxColumn<double>(ordinates, InitialCapacity);

            return new PackedBoxIndex(
                options, hilbertGridBitsPerAxis, indices,
                minX, minY, maxX, maxY,
                childStart, childCount, levelStart,
                centerX, centerY, hilbertKeys, strKeys, order, nodeScratch,
                emissionRanks, dominanceOrder,
                dominanceLeft, dominanceRight, dominanceAxis, dominanceSplit,
                dominanceItemStart, dominanceItemSpan, dominanceBuildStack,
                dominanceMaxXOrder, dominanceMinYOrder, dominanceMaxYOrder,
                dominanceSideFlags, dominancePartitionScratch,
                dominanceUnionMinX, dominanceUnionMinY, dominanceUnionMaxX, dominanceUnionMaxY);
        }
        catch
        {
            dominanceUnionMaxY?.Dispose();
            dominanceUnionMaxX?.Dispose();
            dominanceUnionMinY?.Dispose();
            dominanceUnionMinX?.Dispose();
            dominancePartitionScratch?.Dispose();
            dominanceSideFlags?.Dispose();
            dominanceMaxYOrder?.Dispose();
            dominanceMinYOrder?.Dispose();
            dominanceMaxXOrder?.Dispose();
            dominanceBuildStack?.Dispose();
            dominanceItemSpan?.Dispose();
            dominanceItemStart?.Dispose();
            dominanceSplit?.Dispose();
            dominanceAxis?.Dispose();
            dominanceRight?.Dispose();
            dominanceLeft?.Dispose();
            dominanceOrder?.Dispose();
            emissionRanks?.Dispose();
            nodeScratch?.Dispose();
            order?.Dispose();
            strKeys?.Dispose();
            hilbertKeys?.Dispose();
            centerY?.Dispose();
            centerX?.Dispose();
            levelStart?.Dispose();
            childCount?.Dispose();
            childStart?.Dispose();
            maxY?.Dispose();
            maxX?.Dispose();
            minY?.Dispose();
            minX?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds the index over <paramref name="items"/>, overwriting any prior
    /// build. Returns false exactly when an item is malformed — any
    /// non-finite ordinate, or an inverted axis — checked before any key
    /// computation or column write. Refusal is destructive: the prior working
    /// set is discarded (<see cref="Count"/> 0, every mode enumerates
    /// nothing), never silently retained behind a false return; rebuilding
    /// after a refusal is legal and fully working. An empty span is not
    /// malformed: it builds successfully with <see cref="Count"/> 0.
    /// </summary>
    /// <remarks>
    /// The binding capacity ceiling is the slot parade (items plus nodes),
    /// not the item count: every per-slot column must stay addressable under
    /// <see cref="Array.MaxLength"/>, per
    /// <see cref="PackedBoxIndexPrimitives.ComputeLayout"/> — a build whose
    /// parade exceeds it throws <see cref="ArgumentOutOfRangeException"/>
    /// rather than refusing, because the input is well-formed and the limit
    /// is structural. A fault inside the reservation region — the layout
    /// guard or any capacity request — lands the index in the refused shape
    /// before the rethrow, so no prior epoch's observables ever ride
    /// partially re-rented columns.
    /// </remarks>
    /// <param name="items">The item sequence to ingest; its positions are the registration indices queries answer with.</param>
    /// <returns><see langword="true"/> when the build succeeded; <see langword="false"/> when an item was malformed.</returns>
    public bool TryBuild(ReadOnlySpan<BoundingBox> items)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        //Every build attempt — success or refusal — invalidates live enumerators.
        BuildVersion++;

        //Sanitation first, before any key computation or column write: refusal must leave the
        //index empty without needing a single write, which is what lets it compose with the
        //destructive-refusal contract.
        for(int index = 0; index < items.Length; index++)
        {
            BoundingBox item = items[index];
            bool wellFormed =
                double.IsFinite(item.MinX) && double.IsFinite(item.MinY)
                && double.IsFinite(item.MaxX) && double.IsFinite(item.MaxY)
                && item.MinX <= item.MaxX && item.MinY <= item.MaxY;

            if(!wellFormed)
            {
                ResetToEmpty();

                return false;
            }
        }

        if(items.Length == 0)
        {
            //The empty build short-circuits the whole pipeline before any capacity request
            //(every sizing path rejects a non-positive requirement): no levels, no nodes, no
            //root, and every query short-circuits before any stack rental.
            ResetToEmpty();

            return true;
        }

        int capacity = OptionsValue.NodeCapacity;
        int itemCount = items.Length;
        PackedBoxIndexLayout layout;
        int dominanceStackBound;

        //The reservation region: everything that can throw — the layout guard and every
        //capacity request — runs before the first column write, and a fault here lands the
        //index in the refused shape rather than orphaning the prior build's observables
        //over partially re-rented columns. A faulted build is a refused build; the deferred
        //dominance pass then writes through already-sized columns and cannot fault at all.
        try
        {
            layout = PackedBoxIndexPrimitives.ComputeLayout(items.Length, capacity);

            if(layout.TotalSlots > Array.MaxLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(items),
                    $"A build of {items.Length} items at capacity {capacity} needs a slot parade of {layout.TotalSlots} (items + {layout.NodeCount} nodes), above the {Array.MaxLength}-slot pooled-column ceiling ({nameof(PackedBoxIndexPrimitives)}.{nameof(PackedBoxIndexPrimitives.ComputeLayout)} is the arithmetic).");
            }

            int paradeSlots = (int)layout.TotalSlots;
            int leafNodeCount = (int)((itemCount + (long)capacity - 1L) / capacity);

            MinXColumn.EnsureCapacity(paradeSlots);
            MinYColumn.EnsureCapacity(paradeSlots);
            MaxXColumn.EnsureCapacity(paradeSlots);
            MaxYColumn.EnsureCapacity(paradeSlots);
            ChildStartColumn.EnsureCapacity(paradeSlots);
            ChildCountColumn.EnsureCapacity(paradeSlots);
            LevelStartColumn.EnsureCapacity(layout.LevelCount);
            CenterXColumn.EnsureCapacity(itemCount);
            CenterYColumn.EnsureCapacity(itemCount);
            OrderColumn.EnsureCapacity(itemCount);
            NodeScratchColumn.EnsureCapacity(leafNodeCount);

            if(OptionsValue.Packing == BoxIndexPacking.HilbertCurve)
            {
                HilbertKeyColumn.EnsureCapacity(itemCount);
            }

            //The deferred dominance pass rents nothing itself: every column it writes is
            //sized here, with the other capacity requests, so the pass is throw-free
            //straight-line arithmetic and no span ever moves under a live enumerator when
            //it later runs. The composite-key column serves the pass's four initial
            //axis-order sorts under BOTH packings, so it is sized unconditionally.
            StrKeyColumn.EnsureCapacity(itemCount);
            DominanceMaxXOrderColumn.EnsureCapacity(itemCount);
            DominanceMinYOrderColumn.EnsureCapacity(itemCount);
            DominanceMaxYOrderColumn.EnsureCapacity(itemCount);
            DominanceSideFlagColumn.EnsureCapacity(itemCount);
            DominancePartitionScratchColumn.EnsureCapacity(itemCount);

            int dominanceNodeBound = (int)PackedBoxIndexPrimitives.ComputeDominanceNodeBound(itemCount);
            dominanceStackBound = (int)PackedBoxIndexPrimitives.ComputeDominanceTraversalStackBound(itemCount);
            EmissionRankColumn.EnsureCapacity(itemCount);
            DominanceOrderColumn.EnsureCapacity(itemCount);
            DominanceNodeLeftColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeRightColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeAxisColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeSplitColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeItemStartColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeItemSpanColumn.EnsureCapacity(dominanceNodeBound);
            DominanceBuildStackColumn.EnsureCapacity(dominanceStackBound);
            DominanceNodeUnionMinXColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeUnionMinYColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeUnionMaxXColumn.EnsureCapacity(dominanceNodeBound);
            DominanceNodeUnionMaxYColumn.EnsureCapacity(dominanceNodeBound);
        }
        catch
        {
            ResetToEmpty();

            throw;
        }

        //Hoist every column once the grows are settled; the spans are stable for the rest of
        //this call.
        Span<double> minXs = MinXColumn.Span;
        Span<double> minYs = MinYColumn.Span;
        Span<double> maxXs = MaxXColumn.Span;
        Span<double> maxYs = MaxYColumn.Span;
        Span<int> childStarts = ChildStartColumn.Span;
        Span<int> childCounts = ChildCountColumn.Span;
        Span<int> levelStarts = LevelStartColumn.Span;
        Span<double> centerXs = CenterXColumn.Span;
        Span<double> centerYs = CenterYColumn.Span;
        Span<int> order = OrderColumn.Span;
        Span<PackedNodeRecord> nodeScratch = NodeScratchColumn.Span;

        //The leaf ordering pass: item centers by registration index, ordered by the packing;
        //item slots are then written in that leaf packing order, carrying the registration
        //index as the slot's child-start value.
        for(int registration = 0; registration < itemCount; registration++)
        {
            BoundingBox item = items[registration];
            centerXs[registration] = PackedBoxIndexPrimitives.BoxCenter(item.MinX, item.MaxX);
            centerYs[registration] = PackedBoxIndexPrimitives.BoxCenter(item.MinY, item.MaxY);
        }

        ComputeOrdering(itemCount, centerXs, centerYs, order);

        for(int slot = 0; slot < itemCount; slot++)
        {
            int registration = order[slot];
            BoundingBox item = items[registration];
            minXs[slot] = item.MinX;
            minYs[slot] = item.MinY;
            maxXs[slot] = item.MaxX;
            maxYs[slot] = item.MaxY;
            childStarts[slot] = registration;
        }

        //Bottom-up level loop. Each level's node records are formed in creation order over the
        //previous region's parade runs (so a node's children are one contiguous run by
        //construction), then the level's ordering pass sorts the records and they are appended
        //to the parade in that sorted order — each level is ordered by the packing over the
        //level's own entries. The root appends directly: a one-entry ordering is the identity.
        int levelStartSlot = 0;
        int levelEntryCount = itemCount;
        int nextSlot = itemCount;
        int nodeLevelIndex = 0;

        while(true)
        {
            int nodesFormed = (int)((levelEntryCount + (long)capacity - 1L) / capacity);

            for(int node = 0; node < nodesFormed; node++)
            {
                //Slice-boundary arithmetic runs long and narrows checked (the parade bound
                //keeps every in-range value representable; the widening is the guard against
                //a wrap passing as an in-range value).
                long firstChildLong = levelStartSlot + ((long)node * capacity);
                int firstChild = checked((int)firstChildLong);
                int memberCount = (int)Math.Min(capacity, (levelStartSlot + (long)levelEntryCount) - firstChildLong);

                double unionMinX = double.PositiveInfinity;
                double unionMinY = double.PositiveInfinity;
                double unionMaxX = double.NegativeInfinity;
                double unionMaxY = double.NegativeInfinity;

                for(int member = firstChild; member < firstChild + memberCount; member++)
                {
                    unionMinX = Math.Min(unionMinX, minXs[member]);
                    unionMinY = Math.Min(unionMinY, minYs[member]);
                    unionMaxX = Math.Max(unionMaxX, maxXs[member]);
                    unionMaxY = Math.Max(unionMaxY, maxYs[member]);
                }

                nodeScratch[node] = new PackedNodeRecord(unionMinX, unionMinY, unionMaxX, unionMaxY, firstChild, memberCount);
            }

            levelStarts[nodeLevelIndex] = nextSlot;

            if(nodesFormed == 1)
            {
                WriteNodeSlot(minXs, minYs, maxXs, maxYs, childStarts, childCounts, nextSlot, nodeScratch[0]);
                RootSlot = nextSlot;
                nodeLevelIndex++;

                break;
            }

            for(int node = 0; node < nodesFormed; node++)
            {
                PackedNodeRecord record = nodeScratch[node];
                centerXs[node] = PackedBoxIndexPrimitives.BoxCenter(record.MinX, record.MaxX);
                centerYs[node] = PackedBoxIndexPrimitives.BoxCenter(record.MinY, record.MaxY);
            }

            ComputeOrdering(nodesFormed, centerXs, centerYs, order);

            for(int position = 0; position < nodesFormed; position++)
            {
                WriteNodeSlot(minXs, minYs, maxXs, maxYs, childStarts, childCounts, nextSlot + position, nodeScratch[order[position]]);
            }

            levelStartSlot = nextSlot;
            levelEntryCount = nodesFormed;
            nextSlot += nodesFormed;
            nodeLevelIndex++;
        }

        BuiltCount = itemCount;
        BuiltLevelCount = nodeLevelIndex;
        BuiltNodeCount = (int)layout.NodeCount;
        TraversalStackBound = (int)layout.TraversalStackBound;
        DominanceTraversalStackBound = dominanceStackBound;

        //The dominance structure is deferred to the containing route's first use: its
        //observables reset here so no stale prior-epoch value is readable through any
        //path, and the pending flag routes that first use into the one-time
        //materialization. Everything the pass needs is already sized above, so the pass
        //itself cannot fault and no span will move under a live enumerator when it runs.
        //The eager carriage drives the identical gate right here instead, so the
        //exactly-once counter and the publication order are carriage-invariant.
        BuiltDominanceNodeCount = 0;
        DominanceRootNode = -1;
        Volatile.Write(ref dominanceMaterialized, false);

        if(OptionsValue.DominanceMaterialization == DominanceMaterializationMode.EagerAtBuild)
        {
            EnsureDominanceMaterialized();
        }

        return true;
    }

    /// <summary>
    /// The containing route's, the dominance accessors', and the eager
    /// carriage's build-tail gate: runs the dominance pass exactly once per
    /// built epoch. The fast path is
    /// one volatile load; first callers of an epoch serialize on the
    /// materialization lock, where the winner re-checks disposal (a dispose
    /// that won the lock must fail this caller loud, never let it write
    /// disposed columns), runs the pass, and publishes count-then-flag only
    /// if the build version never moved under it. The version re-check is
    /// blast-radius reduction for an out-of-contract rebuild racing the
    /// pass, not a certainty — the reads are plain and a racing build
    /// remains undefined in every mode; what it buys is that the observed
    /// interleavings skip publication instead of certifying a structure
    /// computed from mixed epochs. A rootless index short-circuits: the
    /// never-built, refused, empty, and faulted states are trivially
    /// materialized.
    /// </summary>
    internal void EnsureDominanceMaterialized()
    {
        if(Volatile.Read(ref dominanceMaterialized))
        {
            return;
        }

        if(RootSlot < 0)
        {
            Volatile.Write(ref dominanceMaterialized, true);

            return;
        }

        lock(DominanceMaterializationLock)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);

            if(dominanceMaterialized)
            {
                return;
            }

            int versionSnapshot = BuildVersion;
            (int nodeCount, int rootNode) = MaterializeDominance();

            if(BuildVersion == versionSnapshot)
            {
                BuiltDominanceNodeCount = nodeCount;
                DominanceRootNode = rootNode;
                Interlocked.Increment(ref dominanceMaterializationCount);
                Volatile.Write(ref dominanceMaterialized, true);
            }
        }
    }

    /// <summary>
    /// The deferred dominance pass — the identical work an eager build tail
    /// runs in place, over the same committed columns, producing the same
    /// structure bit for bit; only WHEN it runs moves. Writes through
    /// already-sized columns only (every capacity operation ran inside
    /// <see cref="TryBuild"/>), so it is throw-free under the type's
    /// contract, rents nothing, allocates nothing, and never moves a span —
    /// which is what keeps live enumerators of the other modes legal across
    /// it. Callers hold the materialization lock; the dominance observables
    /// commit at the caller under its version check.
    /// </summary>
    /// <returns>The dominance node count and the root node.</returns>
    private (int NodeCount, int RootNode) MaterializeDominance()
    {
        int itemCount = BuiltCount;
        ReadOnlySpan<double> minXs = MinXColumn.Span;
        ReadOnlySpan<double> minYs = MinYColumn.Span;
        ReadOnlySpan<double> maxXs = MaxXColumn.Span;
        ReadOnlySpan<double> maxYs = MaxYColumn.Span;
        ReadOnlySpan<int> childStarts = ChildStartColumn.Span;
        ReadOnlySpan<int> childCounts = ChildCountColumn.Span;
        Span<int> order = OrderColumn.Span;

        //The emission-rank pass: one full preorder walk of the committed parade, recording
        //each item slot's position in the walk. Every query's emission is a restriction of
        //this one fixed order — pruning skips entries, never reorders survivors — so the
        //containing route reproduces the contractual order by sorting its matches on this
        //column. The order scratch serves as the walk's stack: the build's ordering passes
        //are done with it, and the unpruned pending-sibling peak is the layout's traversal
        //bound, which never exceeds the item count.
        Span<int> emissionRanks = EmissionRankColumn.Span;
        int walkTop = 0;
        order[walkTop] = RootSlot;
        walkTop++;

        int nextRank = 0;
        while(walkTop > 0)
        {
            walkTop--;
            int slot = order[walkTop];
            int runStart = childStarts[slot];
            int runCount = childCounts[slot];

            if(runStart < itemCount)
            {
                for(int itemSlot = runStart; itemSlot < runStart + runCount; itemSlot++)
                {
                    emissionRanks[itemSlot] = nextRank;
                    nextRank++;
                }

                continue;
            }

            for(int child = runStart + runCount - 1; child >= runStart; child--)
            {
                order[walkTop] = child;
                walkTop++;
            }
        }

        //The dominance pass: a four-axis k-d tree over the item slots, split by the median
        //of (coordinate, slot) composite keys — the unique slot closes every coordinate tie,
        //so the tree is a pure function of the slot parade and never leans on a sort
        //algorithm's tie behaviour. Construction is pre-sorted: each axis's slot order is
        //sorted ONCE up front, and every split partitions all four orders stably by a
        //per-slot side flag — stability preserves each order's sortedness within both
        //halves, so a node's median is read straight out of the split axis's order and no
        //re-sort ever runs below the four initial ones. The tree is the one the per-node
        //re-sorting construction produced, column for column: the same composite keys
        //select the same median element at every node. Depth-first over the explicit work
        //stack, right child pushed first so the pop order reproduces a recursive preorder
        //and its node numbering exactly. The MinX order reuses the packing's order scratch
        //(the rank walk above was its last other reader); the dominance order column is
        //written leaf by leaf and the leaves tile the item range, so every slot of it is
        //written.
        Span<int> dominanceOrder = DominanceOrderColumn.Span;
        Span<StrBoxKey> sortKeys = StrKeyColumn.Span;
        Span<int> minXOrder = order;
        Span<int> maxXOrder = DominanceMaxXOrderColumn.Span;
        Span<int> minYOrder = DominanceMinYOrderColumn.Span;
        Span<int> maxYOrder = DominanceMaxYOrderColumn.Span;
        Span<byte> sideFlags = DominanceSideFlagColumn.Span;
        Span<int> partitionScratch = DominancePartitionScratchColumn.Span;

        SortAxisOrder(minXs, sortKeys, minXOrder, itemCount);
        SortAxisOrder(maxXs, sortKeys, maxXOrder, itemCount);
        SortAxisOrder(minYs, sortKeys, minYOrder, itemCount);
        SortAxisOrder(maxYs, sortKeys, maxYOrder, itemCount);

        Span<int> dominanceLeft = DominanceNodeLeftColumn.Span;
        Span<int> dominanceRight = DominanceNodeRightColumn.Span;
        Span<int> dominanceAxis = DominanceNodeAxisColumn.Span;
        Span<double> dominanceSplit = DominanceNodeSplitColumn.Span;
        Span<int> dominanceItemStart = DominanceNodeItemStartColumn.Span;
        Span<int> dominanceItemSpan = DominanceNodeItemSpanColumn.Span;
        Span<DominanceBuildWorkItem> buildStack = DominanceBuildStackColumn.Span;
        Span<double> unionMinXs = DominanceNodeUnionMinXColumn.Span;
        Span<double> unionMinYs = DominanceNodeUnionMinYColumn.Span;
        Span<double> unionMaxXs = DominanceNodeUnionMaxXColumn.Span;
        Span<double> unionMaxYs = DominanceNodeUnionMaxYColumn.Span;

        int dominanceNodeCount = 0;
        int dominanceRoot = -1;
        int buildTop = 0;
        buildStack[buildTop] = new DominanceBuildWorkItem(0, itemCount, Depth: 0, ParentNode: -1, IsLeftChild: false);
        buildTop++;

        while(buildTop > 0)
        {
            buildTop--;
            DominanceBuildWorkItem work = buildStack[buildTop];
            int node = dominanceNodeCount;
            dominanceNodeCount++;

            if(work.ParentNode < 0)
            {
                dominanceRoot = node;
            }
            else if(work.IsLeftChild)
            {
                dominanceLeft[work.ParentNode] = node;
            }
            else
            {
                dominanceRight[work.ParentNode] = node;
            }

            //Every node records its item range — internal nodes included, so no slot of
            //these recycled pool segments is ever read unwritten.
            dominanceItemStart[node] = work.ItemStart;
            dominanceItemSpan[node] = work.ItemCount;

            if(work.ItemCount <= DominanceLeafSize)
            {
                dominanceLeft[node] = -1;

                //The leaf's slot sequence is its range of the parent's split-axis order —
                //exactly the sequence the re-sorting construction's last sort of this range
                //left behind. The root-as-leaf has no parent and keeps ascending slots.
                if(work.Depth == 0)
                {
                    for(int position = 0; position < work.ItemCount; position++)
                    {
                        dominanceOrder[position] = position;
                    }
                }
                else
                {
                    int parentAxis = (work.Depth - 1) % 4;
                    Span<int> parentOrder = parentAxis switch
                    {
                        0 => minXOrder,
                        1 => maxXOrder,
                        2 => minYOrder,
                        _ => maxYOrder
                    };
                    parentOrder.Slice(work.ItemStart, work.ItemCount).CopyTo(dominanceOrder.Slice(work.ItemStart, work.ItemCount));
                }

                //The leaf's union box folds over its just-written slot range; internal
                //unions compose from child pairs in the reverse sweep after this loop.
                //A leaf range is never empty, so the fold never leaves an infinity behind.
                double leafUnionMinX = double.PositiveInfinity;
                double leafUnionMinY = double.PositiveInfinity;
                double leafUnionMaxX = double.NegativeInfinity;
                double leafUnionMaxY = double.NegativeInfinity;

                for(int position = work.ItemStart; position < work.ItemStart + work.ItemCount; position++)
                {
                    int slot = dominanceOrder[position];
                    leafUnionMinX = Math.Min(leafUnionMinX, minXs[slot]);
                    leafUnionMinY = Math.Min(leafUnionMinY, minYs[slot]);
                    leafUnionMaxX = Math.Max(leafUnionMaxX, maxXs[slot]);
                    leafUnionMaxY = Math.Max(leafUnionMaxY, maxYs[slot]);
                }

                unionMinXs[node] = leafUnionMinX;
                unionMinYs[node] = leafUnionMinY;
                unionMaxXs[node] = leafUnionMaxX;
                unionMaxYs[node] = leafUnionMaxY;

                continue;
            }

            int axis = work.Depth % 4;
            ReadOnlySpan<double> axisColumn = axis switch
            {
                0 => minXs,
                1 => maxXs,
                2 => minYs,
                _ => maxYs
            };
            Span<int> axisOrder = axis switch
            {
                0 => minXOrder,
                1 => maxXOrder,
                2 => minYOrder,
                _ => maxYOrder
            };

            int half = work.ItemCount / 2;
            int medianSlot = axisOrder[work.ItemStart + half];
            dominanceAxis[node] = axis;
            dominanceSplit[node] = axisColumn[medianSlot];

            //Both halves' side flags are written before any partition reads them: a slot's
            //flag left over from an ancestor's split would otherwise leak into this node's
            //partition.
            for(int position = work.ItemStart; position < work.ItemStart + half; position++)
            {
                sideFlags[axisOrder[position]] = 0;
            }

            for(int position = work.ItemStart + half; position < work.ItemStart + work.ItemCount; position++)
            {
                sideFlags[axisOrder[position]] = 1;
            }

            //The split axis's own order already lists the left half before the right; the
            //other three partition stably, each keeping its sortedness within both halves.
            if(axis != 0)
            {
                PartitionOrderRange(minXOrder, sideFlags, partitionScratch, work.ItemStart, half, work.ItemCount);
            }

            if(axis != 1)
            {
                PartitionOrderRange(maxXOrder, sideFlags, partitionScratch, work.ItemStart, half, work.ItemCount);
            }

            if(axis != 2)
            {
                PartitionOrderRange(minYOrder, sideFlags, partitionScratch, work.ItemStart, half, work.ItemCount);
            }

            if(axis != 3)
            {
                PartitionOrderRange(maxYOrder, sideFlags, partitionScratch, work.ItemStart, half, work.ItemCount);
            }

            buildStack[buildTop] = new DominanceBuildWorkItem(work.ItemStart + half, work.ItemCount - half, work.Depth + 1, node, IsLeftChild: false);
            buildTop++;
            buildStack[buildTop] = new DominanceBuildWorkItem(work.ItemStart, half, work.Depth + 1, node, IsLeftChild: true);
            buildTop++;
        }

        //Internal unions compose in one reverse-index sweep: the stack build numbers every
        //child strictly greater than its parent (right pushed first, pop order reproduces
        //recursive preorder), so descending node order visits both children before their
        //parent and each internal union is the min/max of two already-final child unions.
        for(int node = dominanceNodeCount - 1; node >= 0; node--)
        {
            if(dominanceLeft[node] < 0)
            {
                continue;
            }

            int leftChild = dominanceLeft[node];
            int rightChild = dominanceRight[node];
            unionMinXs[node] = Math.Min(unionMinXs[leftChild], unionMinXs[rightChild]);
            unionMinYs[node] = Math.Min(unionMinYs[leftChild], unionMinYs[rightChild]);
            unionMaxXs[node] = Math.Max(unionMaxXs[leftChild], unionMaxXs[rightChild]);
            unionMaxYs[node] = Math.Max(unionMaxYs[leftChild], unionMaxYs[rightChild]);
        }

        return (dominanceNodeCount, dominanceRoot);
    }

    /// <summary>Enumerates the registration indices of items whose boxes intersect <paramref name="query"/> (closed intervals; touching counts).</summary>
    /// <param name="query">The query box.</param>
    /// <returns>The non-owning candidate view.</returns>
    public Candidates Intersecting(in BoundingBox query)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        return new Candidates(this, query, QueryMode.Intersecting);
    }

    /// <summary>Enumerates the registration indices of items enclosed by <paramref name="query"/> (items ⊆ query, bounds non-strict).</summary>
    /// <param name="query">The query box.</param>
    /// <returns>The non-owning candidate view.</returns>
    public Candidates ContainedIn(in BoundingBox query)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        return new Candidates(this, query, QueryMode.ContainedIn);
    }

    /// <summary>
    /// Enumerates the registration indices of items enclosing
    /// <paramref name="query"/> (items ⊇ query, bounds non-strict). Answered
    /// by the embedded dominance tree, whose descent composes two prune
    /// rules — each node's subtree union box first, the half-space partition
    /// on the split coordinate second — with the candidate set and the
    /// contractual per-packing emission order identical to a packed-tree
    /// route by construction; see the type remarks' containment route. Under
    /// the deferred default the route's FIRST enumeration of a built epoch
    /// materializes the dominance structure under an internal lock — a
    /// one-time cost of the same order as the deferred build work, during
    /// which concurrent first callers block; every later enumeration of the
    /// epoch is lock-free. Under
    /// <see cref="DominanceMaterializationMode.EagerAtBuild"/> the build tail
    /// already materialized, so every enumeration of the epoch — the first
    /// included — is lock-free and carries no materialization. This route is
    /// the containment path a consumer builds unless its own measurement says
    /// otherwise; <see cref="BoxContainmentIndex"/> is the selectable
    /// containment-only alternative.
    /// </summary>
    /// <param name="query">The query box.</param>
    /// <returns>The non-owning candidate view.</returns>
    public Candidates Containing(in BoundingBox query)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        return new Candidates(this, query, QueryMode.Containing);
    }

    /// <summary>
    /// The parade slot range [start, end) of one node level, 0 = the leaf
    /// node level, ascending to the root level. Diagnostic surface: with the
    /// stored child runs it makes the level's fill observable — sorting a
    /// level's (start, count) child runs by start must tile the previous
    /// region exactly, which is the invariant that distinguishes the
    /// run-of-capacity fill from an even-fill on every (N, capacity) pair.
    /// </summary>
    /// <param name="nodeLevel">The node level, 0 for the leaf node level.</param>
    /// <returns>The level's parade slot range.</returns>
    internal (int Start, int End) LevelSlots(int nodeLevel)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(nodeLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nodeLevel, BuiltLevelCount);

        int start = LevelStartColumn.Span[nodeLevel];
        int end = nodeLevel + 1 < BuiltLevelCount ? LevelStartColumn.Span[nodeLevel + 1] : BuiltCount + BuiltNodeCount;

        return (start, end);
    }

    /// <summary>The stored child-run pair of one node slot. Diagnostic surface for the structure tests, read against <see cref="LevelSlots"/>.</summary>
    /// <param name="nodeSlot">The node's parade slot.</param>
    /// <returns>The node's child-run start and count.</returns>
    internal (int ChildStart, int ChildCount) NodeChildRun(int nodeSlot)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeSlot, BuiltCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(nodeSlot, BuiltCount + BuiltNodeCount);

        return (ChildStartColumn.Span[nodeSlot], ChildCountColumn.Span[nodeSlot]);
    }

    /// <summary>
    /// One dominance node's always-written facts: the left-child node (−1 for
    /// a leaf) and the item range the node owns over the dominance order.
    /// Diagnostic surface for the dominance structure tests — node 0 is the
    /// root by the preorder numbering. Forces the deferred materialization
    /// before its range guards, so the guards judge the CURRENT epoch's
    /// structure, never a stale one.
    /// </summary>
    /// <param name="node">The dominance node.</param>
    /// <returns>The node's left child and item range.</returns>
    internal (int Left, int ItemStart, int ItemSpan) DominanceNodeRange(int node)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        EnsureDominanceMaterialized();
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, BuiltDominanceNodeCount);

        return (DominanceNodeLeftColumn.Span[node], DominanceNodeItemStartColumn.Span[node], DominanceNodeItemSpanColumn.Span[node]);
    }

    /// <summary>
    /// One internal dominance node's split facts: the right-child node, the
    /// split axis (0 MinX, 1 MaxX, 2 MinY, 3 MaxY), and the split value.
    /// Throws <see cref="ArgumentException"/> for a leaf node — a leaf never
    /// writes these slots, and never reading an unwritten slot is what keeps
    /// builds process-independent, the diagnostic surface included. Forces
    /// the deferred materialization before its range guards.
    /// </summary>
    /// <param name="node">The dominance node.</param>
    /// <returns>The node's right child, split axis, and split value.</returns>
    internal (int Right, int Axis, double Split) DominanceNodeSplitFacts(int node)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        EnsureDominanceMaterialized();
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, BuiltDominanceNodeCount);

        if(DominanceNodeLeftColumn.Span[node] < 0)
        {
            throw new ArgumentException($"Dominance node {node} is a leaf; its right/axis/split slots are never written and are never read.", nameof(node));
        }

        return (DominanceNodeRightColumn.Span[node], DominanceNodeAxisColumn.Span[node], DominanceNodeSplitColumn.Span[node]);
    }

    /// <summary>The dominance order's item slot at one position — the permutation whose contiguous ranges the dominance leaves own. Diagnostic surface for the dominance structure tests; forces the deferred materialization before its range guards.</summary>
    /// <param name="position">The dominance-order position.</param>
    /// <returns>The item slot at the position.</returns>
    internal int DominanceOrderSlot(int position)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        EnsureDominanceMaterialized();
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(position, BuiltCount);

        return DominanceOrderColumn.Span[position];
    }

    /// <summary>One item slot's registration index — the item's position in the sequence the build ingested. Diagnostic surface: the bridge from parade slots back to the caller's items, which is what lets a test derive slot-level structures independently.</summary>
    /// <param name="itemSlot">The item's parade slot.</param>
    /// <returns>The slot's registration index.</returns>
    internal int ItemSlotRegistration(int itemSlot)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(itemSlot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(itemSlot, BuiltCount);

        return ChildStartColumn.Span[itemSlot];
    }

    /// <summary>One dominance node's subtree union box — the exact min/max fold over the node's whole item range, written for every node, leaves included. Diagnostic surface for the dominance union tests; forces the deferred materialization before its range guards.</summary>
    /// <param name="node">The dominance node.</param>
    /// <returns>The node's subtree union box.</returns>
    internal (double MinX, double MinY, double MaxX, double MaxY) DominanceNodeUnion(int node)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);
        EnsureDominanceMaterialized();
        ArgumentOutOfRangeException.ThrowIfNegative(node);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(node, BuiltDominanceNodeCount);

        return (DominanceNodeUnionMinXColumn.Span[node], DominanceNodeUnionMinYColumn.Span[node], DominanceNodeUnionMaxXColumn.Span[node], DominanceNodeUnionMaxYColumn.Span[node]);
    }

    /// <summary>Returns every pooled column and invalidates live enumerators; idempotent. Takes the materialization lock so an in-flight deferred pass completes before any column returns to the pool.</summary>
    public void Dispose()
    {
        if(Disposed)
        {
            return;
        }

        //The materialization lock serializes disposal against an in-flight deferred pass:
        //the pass completes before any column returns to the pool, so it can never write
        //through spans whose slabs another component has re-rented, and a waiter that
        //loses to this dispose fails loud from its own disposed re-check under the lock.
        lock(DominanceMaterializationLock)
        {
            if(Disposed)
            {
                return;
            }

            DisposeColumns();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>The dispose body, run under the materialization lock: marks the disposed state, invalidates live enumerators through the version, and cascades every pooled column.</summary>
    private void DisposeColumns()
    {
        Disposed = true;
        BuildVersion++;
        MinXColumn.Dispose();
        MinYColumn.Dispose();
        MaxXColumn.Dispose();
        MaxYColumn.Dispose();
        ChildStartColumn.Dispose();
        ChildCountColumn.Dispose();
        LevelStartColumn.Dispose();
        CenterXColumn.Dispose();
        CenterYColumn.Dispose();
        HilbertKeyColumn.Dispose();
        StrKeyColumn.Dispose();
        OrderColumn.Dispose();
        NodeScratchColumn.Dispose();
        EmissionRankColumn.Dispose();
        DominanceOrderColumn.Dispose();
        DominanceNodeLeftColumn.Dispose();
        DominanceNodeRightColumn.Dispose();
        DominanceNodeAxisColumn.Dispose();
        DominanceNodeSplitColumn.Dispose();
        DominanceNodeItemStartColumn.Dispose();
        DominanceNodeItemSpanColumn.Dispose();
        DominanceBuildStackColumn.Dispose();
        DominanceMaxXOrderColumn.Dispose();
        DominanceMinYOrderColumn.Dispose();
        DominanceMaxYOrderColumn.Dispose();
        DominanceSideFlagColumn.Dispose();
        DominancePartitionScratchColumn.Dispose();
        DominanceNodeUnionMinXColumn.Dispose();
        DominanceNodeUnionMinYColumn.Dispose();
        DominanceNodeUnionMaxXColumn.Dispose();
        DominanceNodeUnionMaxYColumn.Dispose();
    }

    /// <summary>Clears the working set to the empty shape shared by never-built, refused, faulted, and empty-build states — dominance and rank state invalidate with everything else, and the rootless epoch is trivially materialized (there is nothing to defer).</summary>
    private void ResetToEmpty()
    {
        BuiltCount = 0;
        BuiltLevelCount = 0;
        BuiltNodeCount = 0;
        RootSlot = -1;
        TraversalStackBound = 0;
        BuiltDominanceNodeCount = 0;
        DominanceRootNode = -1;
        DominanceTraversalStackBound = 0;
        Volatile.Write(ref dominanceMaterialized, true);
    }

    /// <summary>
    /// Orders the current pass's <paramref name="entryCount"/> entries by the
    /// configured packing over their centers, writing the sorted entry
    /// indices to <paramref name="order"/>. Entry indices are the tie-break:
    /// registration order on the leaf pass, creation order above — every
    /// element is unique, so the sorted sequence never depends on the sort
    /// algorithm's tie behaviour.
    /// </summary>
    /// <param name="entryCount">The number of entries in this pass.</param>
    /// <param name="centerXs">The entries' center-x ordinates.</param>
    /// <param name="centerYs">The entries' center-y ordinates.</param>
    /// <param name="order">The destination for the sorted entry indices.</param>
    private void ComputeOrdering(int entryCount, ReadOnlySpan<double> centerXs, ReadOnlySpan<double> centerYs, Span<int> order)
    {
        if(OptionsValue.Packing == BoxIndexPacking.HilbertCurve)
        {
            OrderByHilbert(entryCount, centerXs, centerYs, order);
        }
        else
        {
            OrderBySortTileRecursive(entryCount, centerXs, centerYs, order);
        }
    }

    /// <summary>
    /// The Hilbert ordering pass: centers normalize to the grid over this
    /// pass's own center extent (half-form offsets and extent, ratio before
    /// scale — total over sanitation-legal input), key on the Hilbert
    /// distance, sort, emit the order.
    /// </summary>
    /// <param name="entryCount">The number of entries in this pass.</param>
    /// <param name="centerXs">The entries' center-x ordinates.</param>
    /// <param name="centerYs">The entries' center-y ordinates.</param>
    /// <param name="order">The destination for the sorted entry indices.</param>
    private void OrderByHilbert(int entryCount, ReadOnlySpan<double> centerXs, ReadOnlySpan<double> centerYs, Span<int> order)
    {
        HilbertKeyColumn.EnsureCapacity(entryCount);
        Span<HilbertBoxKey> keys = HilbertKeyColumn.Span[..entryCount];

        double minCenterX = double.PositiveInfinity;
        double maxCenterX = double.NegativeInfinity;
        double minCenterY = double.PositiveInfinity;
        double maxCenterY = double.NegativeInfinity;

        for(int entry = 0; entry < entryCount; entry++)
        {
            minCenterX = Math.Min(minCenterX, centerXs[entry]);
            maxCenterX = Math.Max(maxCenterX, centerXs[entry]);
            minCenterY = Math.Min(minCenterY, centerYs[entry]);
            maxCenterY = Math.Max(maxCenterY, centerYs[entry]);
        }

        //The half-form extent stays finite for every finite center pair; the direct
        //subtraction overflows to infinity at the rim and would collapse every key.
        double extentHalfX = (maxCenterX / 2d) - (minCenterX / 2d);
        double extentHalfY = (maxCenterY / 2d) - (minCenterY / 2d);
        uint gridMaximum = (uint)((1UL << HilbertGridBitsPerAxis) - 1UL);
        uint gridSide = 1u << HilbertGridBitsPerAxis;

        for(int entry = 0; entry < entryCount; entry++)
        {
            uint gridX = PackedBoxIndexPrimitives.GridCoordinate(centerXs[entry], minCenterX, extentHalfX, gridMaximum);
            uint gridY = PackedBoxIndexPrimitives.GridCoordinate(centerYs[entry], minCenterY, extentHalfY, gridMaximum);
            keys[entry] = new HilbertBoxKey(PackedBoxIndexPrimitives.HilbertDistance(gridSide, gridX, gridY), entry);
        }

        keys.Sort();

        for(int position = 0; position < entryCount; position++)
        {
            order[position] = keys[position].Index;
        }
    }

    /// <summary>
    /// The Sort-Tile-Recursive ordering pass: sort by center X, partition
    /// into vertical slices of (slice bound × capacity) consecutive entries —
    /// the count-driven loop forms fewer slices when a level divides evenly
    /// and can never form an empty trailing slice — then re-key and sort each
    /// slice by center Y. Runs of the node capacity over the result leave at
    /// most one partial node per level.
    /// </summary>
    /// <param name="entryCount">The number of entries in this pass.</param>
    /// <param name="centerXs">The entries' center-x ordinates.</param>
    /// <param name="centerYs">The entries' center-y ordinates.</param>
    /// <param name="order">The destination for the sorted entry indices.</param>
    private void OrderBySortTileRecursive(int entryCount, ReadOnlySpan<double> centerXs, ReadOnlySpan<double> centerYs, Span<int> order)
    {
        StrKeyColumn.EnsureCapacity(entryCount);
        Span<StrBoxKey> keys = StrKeyColumn.Span[..entryCount];

        for(int entry = 0; entry < entryCount; entry++)
        {
            keys[entry] = new StrBoxKey(centerXs[entry], entry);
        }

        keys.Sort();

        int capacity = OptionsValue.NodeCapacity;
        long nodesThisLevel = (entryCount + (long)capacity - 1L) / capacity;

        //Exact across the whole reachable range: the root of a value at or below 2³¹ sits
        //orders of magnitude clear of double's integer-rounding boundary.
        long sliceBound = (long)Math.Ceiling(Math.Sqrt(nodesThisLevel));
        long sliceEntryCount = sliceBound * capacity;

        for(long sliceStart = 0L; sliceStart < entryCount; sliceStart += sliceEntryCount)
        {
            int sliceLength = (int)Math.Min(sliceEntryCount, entryCount - sliceStart);
            Span<StrBoxKey> slice = keys.Slice(checked((int)sliceStart), sliceLength);

            for(int position = 0; position < sliceLength; position++)
            {
                int entry = slice[position].Index;
                slice[position] = new StrBoxKey(centerYs[entry], entry);
            }

            slice.Sort();
        }

        for(int position = 0; position < entryCount; position++)
        {
            order[position] = keys[position].Index;
        }
    }

    /// <summary>
    /// The dominance build's one-time axis ordering: every item slot keyed by
    /// its (ordinate, slot) composite on one axis and sorted — the identical
    /// total order a per-node re-sorting construction reaches range by
    /// range, established once so the splits below only ever partition.
    /// </summary>
    /// <param name="axisColumn">The axis's ordinate column.</param>
    /// <param name="sortKeys">The composite-key scratch column.</param>
    /// <param name="axisOrder">The destination order column.</param>
    /// <param name="itemCount">The item count of the build.</param>
    private static void SortAxisOrder(ReadOnlySpan<double> axisColumn, Span<StrBoxKey> sortKeys, Span<int> axisOrder, int itemCount)
    {
        for(int slot = 0; slot < itemCount; slot++)
        {
            sortKeys[slot] = new StrBoxKey(axisColumn[slot], slot);
        }

        Span<StrBoxKey> keys = sortKeys[..itemCount];
        keys.Sort();

        for(int position = 0; position < itemCount; position++)
        {
            axisOrder[position] = keys[position].Index;
        }
    }

    /// <summary>
    /// One stable partition of a non-split order's node range: slots flagged
    /// left stream into the range's first <paramref name="leftCount"/>
    /// positions and the flagged-right rest follow, both sides in their
    /// incoming relative order — which is what preserves the order's per-axis
    /// sortedness within each half — staged through the scratch column and
    /// copied back in place.
    /// </summary>
    /// <param name="axisOrder">The order column being partitioned.</param>
    /// <param name="sideFlags">The per-slot side flags of the current split.</param>
    /// <param name="partitionScratch">The staging column.</param>
    /// <param name="start">The range's first position.</param>
    /// <param name="leftCount">The number of left-flagged positions.</param>
    /// <param name="count">The range's length.</param>
    private static void PartitionOrderRange(Span<int> axisOrder, ReadOnlySpan<byte> sideFlags, Span<int> partitionScratch, int start, int leftCount, int count)
    {
        int leftCursor = start;
        int rightCursor = start + leftCount;

        for(int position = start; position < start + count; position++)
        {
            int slot = axisOrder[position];

            if(sideFlags[slot] == 0)
            {
                partitionScratch[leftCursor] = slot;
                leftCursor++;
            }
            else
            {
                partitionScratch[rightCursor] = slot;
                rightCursor++;
            }
        }

        partitionScratch.Slice(start, count).CopyTo(axisOrder.Slice(start, count));
    }

    /// <summary>Writes one formed node record into its parade slot across the six per-slot columns.</summary>
    /// <param name="minXs">The minimum-x column.</param>
    /// <param name="minYs">The minimum-y column.</param>
    /// <param name="maxXs">The maximum-x column.</param>
    /// <param name="maxYs">The maximum-y column.</param>
    /// <param name="childStarts">The child-start column.</param>
    /// <param name="childCounts">The child-count column.</param>
    /// <param name="slot">The destination parade slot.</param>
    /// <param name="record">The node record to write.</param>
    private static void WriteNodeSlot(
        Span<double> minXs, Span<double> minYs, Span<double> maxXs, Span<double> maxYs,
        Span<int> childStarts, Span<int> childCounts,
        int slot, in PackedNodeRecord record)
    {
        minXs[slot] = record.MinX;
        minYs[slot] = record.MinY;
        maxXs[slot] = record.MaxX;
        maxYs[slot] = record.MaxY;
        childStarts[slot] = record.ChildStart;
        childCounts[slot] = record.ChildCount;
    }

    /// <summary>
    /// The descent predicate of the intersect-based modes against a node's
    /// union box: descend nodes that meet the query — sound for contained-in
    /// because a well-formed stored item inside the query is non-empty, meets
    /// the query, and its node box encloses it. Only those two modes ever
    /// traverse the union-bound tree; the containing mode runs the dominance
    /// descent and refines items with <see cref="ItemMatches"/> directly, so
    /// no containing predicate exists here.
    /// </summary>
    /// <param name="minXs">The minimum-x column.</param>
    /// <param name="minYs">The minimum-y column.</param>
    /// <param name="maxXs">The maximum-x column.</param>
    /// <param name="maxYs">The maximum-y column.</param>
    /// <param name="slot">The node's parade slot.</param>
    /// <param name="query">The query box.</param>
    /// <returns><see langword="true"/> when the node survives the descent.</returns>
    private static bool NodeSurvives(
        ReadOnlySpan<double> minXs, ReadOnlySpan<double> minYs, ReadOnlySpan<double> maxXs, ReadOnlySpan<double> maxYs,
        int slot, in BoundingBox query)
    {
        return minXs[slot] <= query.MaxX && maxXs[slot] >= query.MinX && minYs[slot] <= query.MaxY && maxYs[slot] >= query.MinY;
    }

    /// <summary>The leaf refinement of one query mode against a stored item box — the exact closed-interval algebra, applied to exactly-stored ordinates.</summary>
    /// <param name="minXs">The minimum-x column.</param>
    /// <param name="minYs">The minimum-y column.</param>
    /// <param name="maxXs">The maximum-x column.</param>
    /// <param name="maxYs">The maximum-y column.</param>
    /// <param name="slot">The item's parade slot.</param>
    /// <param name="query">The query box.</param>
    /// <param name="mode">The query mode.</param>
    /// <returns><see langword="true"/> when the stored item answers the mode's predicate.</returns>
    private static bool ItemMatches(
        ReadOnlySpan<double> minXs, ReadOnlySpan<double> minYs, ReadOnlySpan<double> maxXs, ReadOnlySpan<double> maxYs,
        int slot, in BoundingBox query, QueryMode mode)
    {
        return mode switch
        {
            QueryMode.Intersecting => minXs[slot] <= query.MaxX && maxXs[slot] >= query.MinX && minYs[slot] <= query.MaxY && maxYs[slot] >= query.MinY,
            QueryMode.ContainedIn => query.MinX <= minXs[slot] && query.MaxX >= maxXs[slot] && query.MinY <= minYs[slot] && query.MaxY >= maxYs[slot],
            _ => minXs[slot] <= query.MinX && maxXs[slot] >= query.MaxX && minYs[slot] <= query.MinY && maxYs[slot] >= query.MaxY,
        };
    }

    /// <summary>The three candidate enumerations, one enumerator type: a loop-invariant, perfectly predicted discriminator.</summary>
    internal enum QueryMode
    {
        /// <summary>Items whose boxes meet the query box.</summary>
        Intersecting = 0,

        /// <summary>Items whose boxes lie inside the query box.</summary>
        ContainedIn = 1,

        /// <summary>Items whose boxes enclose the query box.</summary>
        Containing = 2
    }

    /// <summary>
    /// A non-owning query view: the three query methods return it cheaply,
    /// and <see cref="GetEnumerator"/> performs the per-query rentals —
    /// one lazy traversal stack for the intersect-based modes, the eager
    /// dominance descent with its collect buffer for the containing mode —
    /// returning the owning <see cref="Enumerator"/>. The split exists
    /// because a rental-owning enumerator whose
    /// <c>GetEnumerator() => this</c> would hand <see langword="foreach"/> a
    /// struct copy, and the copy's dispose would return the rental under the
    /// original.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible",
        Justification = "The nested pair is the owning index's enumeration pattern — Candidates is the non-owning query view whose GetEnumerator performs the per-query stack rental, Enumerator owns that rental — and both are meaningless apart from the index whose columns they traverse. This mirrors the BCL's nested enumerator idiom (List<T>.Enumerator, Span<T>.Enumerator).")]
    public readonly ref struct Candidates
    {
        /// <summary>The index whose columns the enumeration traverses.</summary>
        private PackedBoxIndex Index { get; }

        /// <summary>The query box this view answers for.</summary>
        private BoundingBox Query { get; }

        /// <summary>The query mode this view answers in.</summary>
        private QueryMode Mode { get; }

        /// <summary>Captures the query without renting anything; the rentals happen in <see cref="GetEnumerator"/>.</summary>
        /// <param name="index">The owning index.</param>
        /// <param name="query">The query box.</param>
        /// <param name="mode">The query mode.</param>
        internal Candidates(PackedBoxIndex index, BoundingBox query, QueryMode mode)
        {
            Index = index;
            Query = query;
            Mode = mode;
        }

        /// <summary>
        /// Starts the enumeration. A rootless index (never built, refused, or
        /// built empty) and a malformed query box (any non-finite ordinate, or
        /// an inverted axis) both short-circuit to an empty enumeration before
        /// any rental — whose <see cref="Enumerator.Dispose"/> is then a
        /// no-op. The intersect-based modes rent one traversal stack and
        /// enumerate lazily; the containing mode first materializes the
        /// epoch's dominance structure if this is the route's first use (a
        /// one-time blocking cost under the internal lock; see the type
        /// remarks), then runs its whole dominance descent here — two counted
        /// rentals, the descent stack returned before this call answers — and
        /// hands the enumerator its rank-sorted matches.
        /// </summary>
        /// <returns>The owning enumerator.</returns>
        public Enumerator GetEnumerator()
        {
            ObjectDisposedException.ThrowIf(Index.Disposed, Index);

            BoundingBox query = Query;
            bool queryWellFormed =
                double.IsFinite(query.MinX) && double.IsFinite(query.MinY)
                && double.IsFinite(query.MaxX) && double.IsFinite(query.MaxY)
                && query.MinX <= query.MaxX && query.MinY <= query.MaxY;

            if(Index.RootSlot < 0 || !queryWellFormed)
            {
                return new Enumerator(Index);
            }

            if(Mode == QueryMode.Containing)
            {
                //The route's first use of an epoch materializes the dominance structure —
                //after the disposed and rootless short-circuits, before the descent, so a
                //malformed probe or an empty index never triggers it.
                Index.EnsureDominanceMaterialized();

                return RunContainingDescent(in query);
            }

            //The root is tested here so the stack only ever holds surviving nodes; a query the
            //root already fails is an empty enumeration that still never rents.
            bool rootSurvives = NodeSurvives(
                Index.MinXColumn.Span, Index.MinYColumn.Span, Index.MaxXColumn.Span, Index.MaxYColumn.Span,
                Index.RootSlot, in query);

            if(!rootSurvives)
            {
                return new Enumerator(Index);
            }

            IMemoryOwner<int> stackRental = Index.IndexPool.Rent(Index.TraversalStackBound);
            Interlocked.Increment(ref Index.stackRentalsIssued);

            return new Enumerator(Index, stackRental, query, Mode);
        }

        /// <summary>
        /// The containing route: one dominance descent under the composed
        /// union-and-half-space pruning, run eagerly. Matched
        /// item slots collect into the pooled grow-on-demand buffer as packed
        /// (emission rank, slot) keys; a comparer-less sort of those keys then
        /// reproduces the contractual per-packing emission order, because the
        /// rank column is the fixed preorder every query's emission
        /// restricts. Leaves refine with the same <see cref="ItemMatches"/>
        /// algebra as every other mode — the candidate set is unchanged by the
        /// route, by construction. The descent stack is a counted rental
        /// returned before this method answers; the collect buffer is the
        /// second counted rental class and rides the enumerator until its
        /// dispose. An empty result keeps the buffer at its small initial
        /// capacity — item-scale storage is never rented for it.
        /// </summary>
        /// <param name="query">The query box.</param>
        /// <returns>The owning enumerator over the rank-sorted matches.</returns>
        private Enumerator RunContainingDescent(scoped in BoundingBox query)
        {
            PackedBoxColumn<long>? collectBuffer = null;

            try
            {
                collectBuffer = new PackedBoxColumn<long>(VeritasMemoryPool<long>.Shared, CollectInitialCapacity);
                Interlocked.Increment(ref Index.collectRentalsIssued);

                int matchCount = 0;
                int visitedNodes = 0;
                IMemoryOwner<int> stackRental = Index.IndexPool.Rent(Index.DominanceTraversalStackBound);
                Interlocked.Increment(ref Index.stackRentalsIssued);

                try
                {
                    Span<int> stack = stackRental.Memory.Span;
                    ReadOnlySpan<double> minXs = Index.MinXColumn.Span;
                    ReadOnlySpan<double> minYs = Index.MinYColumn.Span;
                    ReadOnlySpan<double> maxXs = Index.MaxXColumn.Span;
                    ReadOnlySpan<double> maxYs = Index.MaxYColumn.Span;
                    ReadOnlySpan<int> dominanceOrder = Index.DominanceOrderColumn.Span;
                    ReadOnlySpan<int> emissionRanks = Index.EmissionRankColumn.Span;
                    ReadOnlySpan<int> nodeLeft = Index.DominanceNodeLeftColumn.Span;
                    ReadOnlySpan<int> nodeRight = Index.DominanceNodeRightColumn.Span;
                    ReadOnlySpan<int> nodeAxis = Index.DominanceNodeAxisColumn.Span;
                    ReadOnlySpan<double> nodeSplit = Index.DominanceNodeSplitColumn.Span;
                    ReadOnlySpan<int> nodeItemStart = Index.DominanceNodeItemStartColumn.Span;
                    ReadOnlySpan<int> nodeItemSpan = Index.DominanceNodeItemSpanColumn.Span;
                    ReadOnlySpan<double> unionMinXs = Index.DominanceNodeUnionMinXColumn.Span;
                    ReadOnlySpan<double> unionMinYs = Index.DominanceNodeUnionMinYColumn.Span;
                    ReadOnlySpan<double> unionMaxXs = Index.DominanceNodeUnionMaxXColumn.Span;
                    ReadOnlySpan<double> unionMaxYs = Index.DominanceNodeUnionMaxYColumn.Span;

                    int top = 0;
                    stack[top] = Index.DominanceRootNode;
                    top++;

                    while(top > 0)
                    {
                        top--;
                        int node = stack[top];
                        visitedNodes++;

                        //The union prune, tested at pop before anything else: a container
                        //must lie at or outside the query on all four bounds at once, and
                        //the union's ordinates are extremes over the whole subtree — if the
                        //subtree's smallest MinX already exceeds the query's, no member's
                        //MinX is at or below it, so no member contains the query; four-fold
                        //for the four bounds. One failed bound kills the subtree in at most
                        //four compares; survivors run the half-space partition prune below
                        //unchanged, so the visit set only ever shrinks against it.
                        if(unionMinXs[node] > query.MinX || unionMaxXs[node] < query.MaxX
                            || unionMinYs[node] > query.MinY || unionMaxYs[node] < query.MaxY)
                        {
                            continue;
                        }

                        if(nodeLeft[node] < 0)
                        {
                            int start = nodeItemStart[node];
                            int end = start + nodeItemSpan[node];

                            for(int position = start; position < end; position++)
                            {
                                int slot = dominanceOrder[position];

                                if(ItemMatches(minXs, minYs, maxXs, maxYs, slot, in query, QueryMode.Containing))
                                {
                                    if(matchCount == collectBuffer.Capacity)
                                    {
                                        collectBuffer.GrowPreservingContents(matchCount + 1, matchCount);
                                    }

                                    collectBuffer.Span[matchCount] = ((long)emissionRanks[slot] << 32) | (uint)slot;
                                    matchCount++;
                                }
                            }

                            continue;
                        }

                        //One-sided half-space prunes on the split coordinate: left holds the
                        //smaller values, right the larger, and only the side every member of
                        //which provably fails the query's bound is dropped.
                        int axis = nodeAxis[node];
                        double split = nodeSplit[node];
                        bool descendLeft;
                        bool descendRight;

                        if(axis % 2 == 0)
                        {
                            //MinX / MinY: a container needs its value at or below the query's,
                            //so a right side entirely above the bound is dead.
                            double bound = axis == 0 ? query.MinX : query.MinY;
                            descendLeft = true;
                            descendRight = split <= bound;
                        }
                        else
                        {
                            //MaxX / MaxY: a container needs its value at or above the query's,
                            //so a left side entirely below the bound is dead.
                            double bound = axis == 1 ? query.MaxX : query.MaxY;
                            descendRight = true;
                            descendLeft = split >= bound;
                        }

                        if(descendRight)
                        {
                            stack[top] = nodeRight[node];
                            top++;
                        }

                        if(descendLeft)
                        {
                            stack[top] = nodeLeft[node];
                            top++;
                        }
                    }
                }
                finally
                {
                    stackRental.Dispose();
                    Interlocked.Increment(ref Index.stackRentalsReturned);
                }

                collectBuffer.Span[..matchCount].Sort();

                return new Enumerator(Index, collectBuffer, matchCount, visitedNodes);
            }
            catch
            {
                if(collectBuffer is not null)
                {
                    collectBuffer.Dispose();
                    Interlocked.Increment(ref Index.collectRentalsReturned);
                }

                throw;
            }
        }
    }

    /// <summary>
    /// The owning query enumerator. For the intersect-based modes it yields
    /// registration indices in ascending tree preorder, owns one pooled
    /// traversal stack, and returns it on <see cref="Dispose"/> —
    /// <see langword="foreach"/> invokes that by the C# pattern; the manual
    /// pattern must <see langword="using"/> it. Only node slots enter the
    /// stack (a leaf node's item run is scanned in place through an ascending
    /// cursor), which is what makes the rented bound exact. For the containing
    /// mode the descent already ran at <c>GetEnumerator</c>: this enumerator
    /// owns the collect buffer of rank-sorted matches and only emits them —
    /// the identical contractual order, restriction of the same preorder.
    /// After the owning index rebuilds or disposes, <see cref="MoveNext"/>
    /// throws <see cref="InvalidOperationException"/> (the build-version
    /// guard); after this enumerator's own <see cref="Dispose"/>,
    /// <see cref="MoveNext"/> and <see cref="Current"/> throw
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible",
        Justification = "The nested pair is the owning index's enumeration pattern — Candidates is the non-owning query view whose GetEnumerator performs the per-query stack rental, Enumerator owns that rental — and both are meaningless apart from the index whose columns they traverse. This mirrors the BCL's nested enumerator idiom (List<T>.Enumerator, Span<T>.Enumerator).")]
    public ref struct Enumerator
    {
        /// <summary>The owning index; null only for <c>default(Enumerator)</c>.</summary>
        private PackedBoxIndex? Owner { get; }

        /// <summary>The traversal-stack rental this enumerator owns, nulled on return.</summary>
        private IMemoryOwner<int>? StackRental { get; set; }

        /// <summary>The containing route's rank-sorted matches, owned until this enumerator's dispose; null on every other path.</summary>
        private PackedBoxColumn<long>? CollectBuffer { get; set; }

        /// <summary>The sorted (rank, slot) keys hoisted from the collect buffer, one per match.</summary>
        private ReadOnlySpan<long> SortedMatches { get; }

        /// <summary>The ascending cursor over <see cref="SortedMatches"/>.</summary>
        private int MatchCursor { get; set; }

        /// <summary>The traversal stack over the rental.</summary>
        private Span<int> Stack { get; }

        /// <summary>The hoisted minimum-x column.</summary>
        private ReadOnlySpan<double> MinXs { get; }

        /// <summary>The hoisted minimum-y column.</summary>
        private ReadOnlySpan<double> MinYs { get; }

        /// <summary>The hoisted maximum-x column.</summary>
        private ReadOnlySpan<double> MaxXs { get; }

        /// <summary>The hoisted maximum-y column.</summary>
        private ReadOnlySpan<double> MaxYs { get; }

        /// <summary>The hoisted child-start column.</summary>
        private ReadOnlySpan<int> ChildStarts { get; }

        /// <summary>The hoisted child-count column.</summary>
        private ReadOnlySpan<int> ChildCounts { get; }

        /// <summary>The leaf/internal discriminator boundary: a node slot whose child start lies below this is a leaf over item slots.</summary>
        private int ItemSlotCount { get; }

        /// <summary>The query box being answered.</summary>
        private BoundingBox Query { get; }

        /// <summary>The query mode being answered in.</summary>
        private QueryMode Mode { get; }

        /// <summary>The owning index's build version at construction time.</summary>
        private int CapturedVersion { get; }

        /// <summary>The traversal stack's height.</summary>
        private int StackTop { get; set; }

        /// <summary>The ascending cursor over the current leaf node's item run.</summary>
        private int LeafCursor { get; set; }

        /// <summary>The exclusive end of the current leaf node's item run.</summary>
        private int LeafEnd { get; set; }

        /// <summary>The registration index most recently yielded.</summary>
        private int CurrentItem { get; set; }

        /// <summary>Whether this enumerator has been disposed.</summary>
        private bool IsDisposed { get; set; }

        /// <summary>
        /// Nodes visited by this query: for the intersect-based modes, node
        /// slots popped from the traversal stack so far; for the containing
        /// mode, dominance nodes popped by the descent, fixed before
        /// enumeration begins — the observable behind the containment
        /// measurement rows.
        /// </summary>
        internal int VisitedNodeCount { get; private set; }

        /// <summary>The empty enumeration: no rental, no spans; MoveNext answers false under the same version guard as a live one.</summary>
        /// <param name="owner">The owning index.</param>
        internal Enumerator(PackedBoxIndex owner)
        {
            Owner = owner;
            CapturedVersion = owner.BuildVersion;
            CurrentItem = -1;
        }

        /// <summary>The containing route: the descent already ran; this enumerator owns the collect buffer and emits its sorted matches through the child-start translation.</summary>
        /// <param name="owner">The owning index.</param>
        /// <param name="collectBuffer">The collect buffer of rank-sorted matches this enumerator owns.</param>
        /// <param name="matchCount">The number of matches in the buffer.</param>
        /// <param name="visitedNodes">The dominance nodes the descent popped.</param>
        internal Enumerator(PackedBoxIndex owner, PackedBoxColumn<long> collectBuffer, int matchCount, int visitedNodes)
        {
            Owner = owner;
            CollectBuffer = collectBuffer;
            SortedMatches = collectBuffer.Span[..matchCount];
            ChildStarts = owner.ChildStartColumn.Span;
            CapturedVersion = owner.BuildVersion;
            CurrentItem = -1;
            VisitedNodeCount = visitedNodes;
        }

        /// <summary>The live intersect-based enumeration: takes ownership of the rental and hoists the columns it traverses.</summary>
        /// <param name="owner">The owning index.</param>
        /// <param name="stackRental">The traversal-stack rental this enumerator owns.</param>
        /// <param name="query">The query box.</param>
        /// <param name="mode">The query mode.</param>
        internal Enumerator(PackedBoxIndex owner, IMemoryOwner<int> stackRental, BoundingBox query, QueryMode mode)
        {
            Owner = owner;
            StackRental = stackRental;
            Stack = stackRental.Memory.Span;
            MinXs = owner.MinXColumn.Span;
            MinYs = owner.MinYColumn.Span;
            MaxXs = owner.MaxXColumn.Span;
            MaxYs = owner.MaxYColumn.Span;
            ChildStarts = owner.ChildStartColumn.Span;
            ChildCounts = owner.ChildCountColumn.Span;
            ItemSlotCount = owner.BuiltCount;
            Query = query;
            Mode = mode;
            CapturedVersion = owner.BuildVersion;
            CurrentItem = -1;
            Stack[0] = owner.RootSlot;
            StackTop = 1;
        }

        /// <summary>The current candidate's registration index — the item's position in the sequence <see cref="TryBuild"/> ingested.</summary>
        public int Current
        {
            get
            {
                ObjectDisposedException.ThrowIf(IsDisposed, typeof(Enumerator));

                return CurrentItem;
            }
        }

        /// <summary>Advances to the next candidate in ascending tree preorder; false when the enumeration is exhausted.</summary>
        /// <returns><see langword="true"/> when a candidate was produced.</returns>
        public bool MoveNext()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, typeof(Enumerator));

            if(Owner is null)
            {
                //Only default(Enumerator) has no owner; it enumerates nothing.
                return false;
            }

            if(Owner.BuildVersion != CapturedVersion)
            {
                throw new InvalidOperationException(
                    "The index was rebuilt or disposed under this live enumerator; its hoisted views are stale. Finish or dispose every enumerator before calling TryBuild or Dispose.");
            }

            if(CollectBuffer is not null)
            {
                //The containing route: the matches are already collected and rank-sorted;
                //emission only walks the sorted keys and translates slot to registration.
                if(MatchCursor >= SortedMatches.Length)
                {
                    return false;
                }

                int matchSlot = (int)(SortedMatches[MatchCursor] & 0xFFFFFFFFL);
                MatchCursor++;
                CurrentItem = ChildStarts[matchSlot];

                return true;
            }

            while(true)
            {
                while(LeafCursor < LeafEnd)
                {
                    int itemSlot = LeafCursor;
                    LeafCursor++;

                    if(ItemMatches(MinXs, MinYs, MaxXs, MaxYs, itemSlot, Query, Mode))
                    {
                        CurrentItem = ChildStarts[itemSlot];

                        return true;
                    }
                }

                if(StackTop == 0)
                {
                    return false;
                }

                StackTop--;
                int nodeSlot = Stack[StackTop];
                VisitedNodeCount++;

                int childStart = ChildStarts[nodeSlot];
                int childCount = ChildCounts[nodeSlot];

                if(childStart < ItemSlotCount)
                {
                    //A leaf node: its run consists of item slots, scanned in place ascending.
                    LeafCursor = childStart;
                    LeafEnd = childStart + childCount;

                    continue;
                }

                //An internal node: push surviving children in reverse slot order so the run's
                //first slot pops first — ascending preorder emission.
                for(int child = childStart + childCount - 1; child >= childStart; child--)
                {
                    if(NodeSurvives(MinXs, MinYs, MaxXs, MaxYs, child, Query))
                    {
                        Stack[StackTop] = child;
                        StackTop++;
                    }
                }
            }
        }

        /// <summary>Returns whichever rental this enumerator owns — the traversal stack or the containing route's collect buffer; idempotent; a no-op for the rental-free empty enumeration.</summary>
        public void Dispose()
        {
            if(IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            if(StackRental is not null)
            {
                StackRental.Dispose();
                StackRental = null;
                Interlocked.Increment(ref Owner!.stackRentalsReturned);
            }

            if(CollectBuffer is not null)
            {
                CollectBuffer.Dispose();
                CollectBuffer = null;
                Interlocked.Increment(ref Owner!.collectRentalsReturned);
            }
        }
    }
}
