using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Lumoin.Veritas.Core.Memory;

namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// A k-d tree over a set of axis-aligned bounding boxes that answers, for a query
/// box, which stored boxes contain it. <see cref="Build"/> registers all boxes;
/// <see cref="Containers"/> enumerates the stored boxes whose bounds enclose the
/// query box (each coordinate non-strictly: <c>box.MinX ≤ query.MinX</c>,
/// <c>box.MaxX ≥ query.MaxX</c>, and likewise in Y).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a k-d tree and not a bounding-volume hierarchy.</b> "Contains the query
/// box" is a four-dimensional dominance query — a box <c>B</c> contains <c>Q</c>
/// exactly when the point <c>(B.MinX, B.MaxX, B.MinY, B.MaxY)</c> is dominated by
/// <c>(Q.MinX, Q.MaxX, Q.MinY, Q.MaxY)</c> in the mixed order (≤ on the two
/// minimum axes, ≥ on the two maximum axes). A spatial hierarchy whose node bounds
/// are the axis-aligned union of its children cannot prune this: a cluster of small
/// disjoint boxes has a wide union, so the necessary condition always passes even
/// though no single box in the cluster contains the query — the archipelago case
/// degrades it to a linear scan. A k-d tree splits one dominance coordinate at a
/// time, so a half-space that violates the query's bound on that coordinate is
/// pruned whole, which is what keeps the dense-island query sub-linear.
/// </para>
/// <para>
/// <b>The selectable alternative to <see cref="PackedBoxIndex.Containing"/>.</b>
/// The packed index answers the same question sub-linearly through its own
/// embedded dominance tree, materialized per built epoch and carried beside its
/// other two modes, and is the containment path a consumer builds unless its
/// own measurement says otherwise. This type is the containment-only selection:
/// a consumer whose workload never asks an intersection or contained-in
/// question can build just this structure, and it answers only that one
/// question — it has no intersection and no contained-in mode, and no
/// enumeration order contract.
/// </para>
/// <para>
/// <b>An exact filter.</b> The prune at an internal node only discards a subtree
/// every box of which provably fails the query's bound on the split coordinate;
/// each leaf then applies the full four-coordinate containment test. So the
/// enumerator yields precisely the boxes that contain the query — never fewer. A
/// caller that re-applies its own containment predicate to the yielded boxes
/// therefore reaches the same verdict as scanning every registered box.
/// </para>
/// <para>
/// <b>Iterative build.</b> The tree is built depth-first over an explicit pooled
/// work stack, never by recursion. Pushing the right child before the left makes
/// the LIFO order reproduce a pre-order traversal exactly, so the node
/// numbering — and therefore the tree — is identical for one registration
/// sequence however the machine schedules the loop.
/// </para>
/// <para>
/// <b>Stateful by design.</b> The tree owns every buffer and reuses it across
/// builds; a fresh <see cref="Build"/> overwrites the working set, so steady-state
/// assembly allocates only when a buffer must grow. The buffers are pooled rentals
/// from caller-provided <see cref="VeritasMemoryPool{T}"/> instances (never a naked
/// array); <see cref="Dispose"/> returns them. Single-threaded: at most one
/// <see cref="DominanceEnumerator"/> may be live at a time because the traversal
/// stack is shared.
/// </para>
/// <para>
/// Median split on a cycling axis (MinX, MaxX, MinY, MaxY), up to eight boxes per
/// leaf. The split is by item count, so depth stays logarithmic for any input; the
/// tree shape influences only how many boxes are examined, never which can be
/// yielded — the per-box test at the leaf is exact.
/// </para>
/// </remarks>
public sealed class BoxContainmentIndex: IDisposable
{
    /// <summary>The largest box count a leaf node holds; a work item at or below it becomes a leaf.</summary>
    private const int LeafSize = 8;

    /// <summary>Pooled columns start tiny and grow on demand; the steady state reuses them across builds.</summary>
    private const int InitialCapacity = 16;

    /// <summary>Per-box bounds, indexed by item id (the registration index, which is what the enumerator yields); declared in axis-cycle order 0=MinX, 1=MaxX, 2=MinY, 3=MaxY.</summary>
    private PackedBoxColumn<double> MinX { get; }

    /// <summary>The maximum-x member of the per-box bounds group.</summary>
    private PackedBoxColumn<double> MaxX { get; }

    /// <summary>The minimum-y member of the per-box bounds group.</summary>
    private PackedBoxColumn<double> MinY { get; }

    /// <summary>The maximum-y member of the per-box bounds group.</summary>
    private PackedBoxColumn<double> MaxY { get; }

    /// <summary>The permutation the build partitions; a node owns Order[ItemStart..ItemStart + ItemSpan).</summary>
    private PackedBoxColumn<int> Order { get; }

    /// <summary>Nodes, appended depth-first; a leaf has Left = -1 and owns an item span, an internal node a split axis/value and two children.</summary>
    private PackedBoxColumn<int> NodeLeft { get; }

    /// <summary>The right-child slot of each node; meaningful only for internal nodes.</summary>
    private PackedBoxColumn<int> NodeRight { get; }

    /// <summary>The split axis of each internal node, in the cycle order 0=MinX, 1=MaxX, 2=MinY, 3=MaxY.</summary>
    private PackedBoxColumn<int> NodeAxis { get; }

    /// <summary>The split value of each internal node, read on the node's own axis.</summary>
    private PackedBoxColumn<double> NodeSplit { get; }

    /// <summary>The first position in <see cref="Order"/> each leaf node owns.</summary>
    private PackedBoxColumn<int> NodeItemStart { get; }

    /// <summary>The number of positions in <see cref="Order"/> each leaf node owns.</summary>
    private PackedBoxColumn<int> NodeItemSpan { get; }

    /// <summary>Pending subtrees for the iterative depth-first build; sized to the tree depth, never recursion.</summary>
    private PackedBoxColumn<BuildWorkItem> BuildStack { get; }

    /// <summary>The shared per-query descent stack; sized to the tree depth by the same median-split bound.</summary>
    private PackedBoxColumn<int> TraversalStack { get; }

    /// <summary>The axis the current partition sorts on; read by <see cref="CompareByAxis"/>.</summary>
    private int SortAxis { get; set; }

    /// <summary>The bound comparison the partition sort runs, held once so the sort takes no per-call delegate allocation.</summary>
    private Comparison<int> AxisComparison { get; }

    /// <summary>Whether this index has been disposed.</summary>
    private bool Disposed { get; set; }

    /// <summary>The slot of the tree root, or -1 when no build has produced one.</summary>
    private int RootNode { get; set; }

    /// <summary>Nodes in the current build, leaves included. Diagnostic surface for the structure tests: a rebuild must re-derive it, never accumulate.</summary>
    internal int NodeCount { get; private set; }

    /// <summary>Assigns the columns the factory rented; the constructor itself acquires nothing.</summary>
    /// <param name="minX">The minimum-x bounds column.</param>
    /// <param name="maxX">The maximum-x bounds column.</param>
    /// <param name="minY">The minimum-y bounds column.</param>
    /// <param name="maxY">The maximum-y bounds column.</param>
    /// <param name="order">The item-permutation column.</param>
    /// <param name="nodeLeft">The left-child column.</param>
    /// <param name="nodeRight">The right-child column.</param>
    /// <param name="nodeAxis">The split-axis column.</param>
    /// <param name="nodeSplit">The split-value column.</param>
    /// <param name="nodeItemStart">The leaf item-start column.</param>
    /// <param name="nodeItemSpan">The leaf item-span column.</param>
    /// <param name="buildStack">The depth-first build stack column.</param>
    /// <param name="traversalStack">The shared query descent stack column.</param>
    private BoxContainmentIndex(
        PackedBoxColumn<double> minX,
        PackedBoxColumn<double> maxX,
        PackedBoxColumn<double> minY,
        PackedBoxColumn<double> maxY,
        PackedBoxColumn<int> order,
        PackedBoxColumn<int> nodeLeft,
        PackedBoxColumn<int> nodeRight,
        PackedBoxColumn<int> nodeAxis,
        PackedBoxColumn<double> nodeSplit,
        PackedBoxColumn<int> nodeItemStart,
        PackedBoxColumn<int> nodeItemSpan,
        PackedBoxColumn<BuildWorkItem> buildStack,
        PackedBoxColumn<int> traversalStack)
    {
        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
        Order = order;
        NodeLeft = nodeLeft;
        NodeRight = nodeRight;
        NodeAxis = nodeAxis;
        NodeSplit = nodeSplit;
        NodeItemStart = nodeItemStart;
        NodeItemSpan = nodeItemSpan;
        BuildStack = buildStack;
        TraversalStack = traversalStack;
        AxisComparison = CompareByAxis;
        RootNode = -1;
    }

    /// <summary>
    /// Constructs the index with its pooled columns rented at initial capacity —
    /// acquisition happens in the factory body, and the private constructor only
    /// assigns; <see cref="Build"/> only grows.
    /// </summary>
    /// <param name="ordinatePool">
    /// The caller-owned pool the five ordinate columns rent from; the shared pool
    /// when omitted. The pool stays the caller's to dispose and must outlive the
    /// index.
    /// </param>
    /// <param name="indexPool">
    /// The caller-owned pool the seven integer columns rent from; the shared pool
    /// when omitted.
    /// </param>
    /// <returns>The constructed index.</returns>
    public static BoxContainmentIndex Create(
        VeritasMemoryPool<double>? ordinatePool = null,
        VeritasMemoryPool<int>? indexPool = null)
    {
        VeritasMemoryPool<double> ordinates = ordinatePool ?? VeritasMemoryPool<double>.Shared;
        VeritasMemoryPool<int> indices = indexPool ?? VeritasMemoryPool<int>.Shared;

        PackedBoxColumn<double>? minX = null;
        PackedBoxColumn<double>? maxX = null;
        PackedBoxColumn<double>? minY = null;
        PackedBoxColumn<double>? maxY = null;
        PackedBoxColumn<int>? order = null;
        PackedBoxColumn<int>? nodeLeft = null;
        PackedBoxColumn<int>? nodeRight = null;
        PackedBoxColumn<int>? nodeAxis = null;
        PackedBoxColumn<double>? nodeSplit = null;
        PackedBoxColumn<int>? nodeItemStart = null;
        PackedBoxColumn<int>? nodeItemSpan = null;
        PackedBoxColumn<BuildWorkItem>? buildStack = null;
        PackedBoxColumn<int>? traversalStack = null;

        try
        {
            minX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            maxX = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            minY = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            maxY = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            order = new PackedBoxColumn<int>(indices, InitialCapacity);
            nodeLeft = new PackedBoxColumn<int>(indices, InitialCapacity);
            nodeRight = new PackedBoxColumn<int>(indices, InitialCapacity);
            nodeAxis = new PackedBoxColumn<int>(indices, InitialCapacity);
            nodeSplit = new PackedBoxColumn<double>(ordinates, InitialCapacity);
            nodeItemStart = new PackedBoxColumn<int>(indices, InitialCapacity);
            nodeItemSpan = new PackedBoxColumn<int>(indices, InitialCapacity);
            buildStack = new PackedBoxColumn<BuildWorkItem>(VeritasMemoryPool<BuildWorkItem>.Shared, InitialCapacity);
            traversalStack = new PackedBoxColumn<int>(indices, InitialCapacity);

            return new BoxContainmentIndex(
                minX, maxX, minY, maxY, order,
                nodeLeft, nodeRight, nodeAxis, nodeSplit,
                nodeItemStart, nodeItemSpan, buildStack, traversalStack);
        }
        catch
        {
            traversalStack?.Dispose();
            buildStack?.Dispose();
            nodeItemSpan?.Dispose();
            nodeItemStart?.Dispose();
            nodeSplit?.Dispose();
            nodeAxis?.Dispose();
            nodeRight?.Dispose();
            nodeLeft?.Dispose();
            order?.Dispose();
            maxY?.Dispose();
            minY?.Dispose();
            maxX?.Dispose();
            minX?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Registers the boxes and builds the tree, discarding any previously registered set. The box
    /// values are copied into structure-of-arrays columns; the span is not retained.
    /// </summary>
    /// <param name="boxes">The boxes to register; their positions are the item ids the enumerator yields.</param>
    public void Build(ReadOnlySpan<BoundingBox> boxes)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        int count = boxes.Length;

        if(count == 0)
        {
            NodeCount = 0;
            RootNode = -1;

            return;
        }

        //Build overwrites the whole working set each time, so growth need not preserve the old contents.
        MinX.EnsureCapacity(count);
        MaxX.EnsureCapacity(count);
        MinY.EnsureCapacity(count);
        MaxY.EnsureCapacity(count);
        Order.EnsureCapacity(count);

        //A median-split leaf holds at least LeafSize / 2 boxes, so the tree has fewer than count nodes; +4 covers the tiny inputs.
        int nodeBound = count + 4;
        NodeLeft.EnsureCapacity(nodeBound);
        NodeRight.EnsureCapacity(nodeBound);
        NodeAxis.EnsureCapacity(nodeBound);
        NodeSplit.EnsureCapacity(nodeBound);
        NodeItemStart.EnsureCapacity(nodeBound);
        NodeItemSpan.EnsureCapacity(nodeBound);

        //The depth-first work stack peaks at one pending right-sibling per level plus the node in hand; the
        //median split keeps the depth logarithmic.
        BuildStack.EnsureCapacity(BitOperations.Log2((uint)count) + 32);

        //Median splits halve the count each level, so the depth is bounded by log2(count) regardless of the box coordinates.
        TraversalStack.EnsureCapacity(BitOperations.Log2((uint)count) + 8);

        //Hoist every column once the grows are settled: the spans are stable for the rest of this call.
        Span<double> minX = MinX.Span;
        Span<double> maxX = MaxX.Span;
        Span<double> minY = MinY.Span;
        Span<double> maxY = MaxY.Span;
        Span<int> order = Order.Span;
        Span<int> nodeLeft = NodeLeft.Span;
        Span<int> nodeRight = NodeRight.Span;
        Span<int> nodeAxis = NodeAxis.Span;
        Span<double> nodeSplit = NodeSplit.Span;
        Span<int> nodeItemStart = NodeItemStart.Span;
        Span<int> nodeItemSpan = NodeItemSpan.Span;
        Span<BuildWorkItem> buildStack = BuildStack.Span;

        for(int item = 0; item < count; item++)
        {
            BoundingBox box = boxes[item];
            minX[item] = box.MinX;
            maxX[item] = box.MaxX;
            minY[item] = box.MinY;
            maxY[item] = box.MaxY;
            order[item] = item;
        }

        //Depth-first build over the explicit stack. A work item records which parent slot to fill once the
        //node is created, so the linkage a return value would carry happens here as a write.
        int nodeCount = 0;
        int root = -1;
        int stackTop = 0;
        buildStack[stackTop++] = new BuildWorkItem(0, count, Depth: 0, ParentNode: -1, IsLeftChild: false);

        while(stackTop > 0)
        {
            BuildWorkItem work = buildStack[--stackTop];
            int node = nodeCount;
            nodeCount++;

            if(work.ParentNode < 0)
            {
                root = node;
            }
            else if(work.IsLeftChild)
            {
                nodeLeft[work.ParentNode] = node;
            }
            else
            {
                nodeRight[work.ParentNode] = node;
            }

            if(work.ItemCount <= LeafSize)
            {
                nodeLeft[node] = -1;
                nodeItemStart[node] = work.ItemStart;
                nodeItemSpan[node] = work.ItemCount;

                continue;
            }

            //Cycle the split axis through the four dominance coordinates: MinX, MaxX, MinY, MaxY.
            int axis = work.Depth & 3;
            SortAxis = axis;
            order.Slice(work.ItemStart, work.ItemCount).Sort(AxisComparison);

            int half = work.ItemCount / 2;
            int median = order[work.ItemStart + half];

            nodeAxis[node] = axis;
            nodeSplit[node] = axis switch
            {
                0 => minX[median],
                1 => maxX[median],
                2 => minY[median],
                _ => maxY[median]
            };

            //Push the right child first so the left is popped first: the LIFO order then matches a
            //pre-order, giving the identical node numbering on every run over one registration sequence.
            buildStack[stackTop++] = new BuildWorkItem(work.ItemStart + half, work.ItemCount - half, work.Depth + 1, node, IsLeftChild: false);
            buildStack[stackTop++] = new BuildWorkItem(work.ItemStart, half, work.Depth + 1, node, IsLeftChild: true);
        }

        RootNode = root;
        NodeCount = nodeCount;
    }

    /// <summary>
    /// Enumerates the item ids of every registered box that contains <paramref name="query"/>
    /// (each bound non-strict). The query box's own item, if registered, is yielded too — it
    /// contains itself. The selectable alternative to <see cref="PackedBoxIndex.Containing"/>,
    /// for a workload that never asks an intersection or contained-in question.
    /// </summary>
    /// <param name="query">The query box.</param>
    /// <returns>The enumerator over the containing item ids.</returns>
    public DominanceEnumerator Containers(in BoundingBox query)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        return new DominanceEnumerator(this, query.MinX, query.MinY, query.MaxX, query.MaxY);
    }

    /// <summary>Compares two item ids on the partition's current split axis.</summary>
    /// <param name="leftItem">The first item id.</param>
    /// <param name="rightItem">The second item id.</param>
    /// <returns>The sign of the two items' ordering on <see cref="SortAxis"/>.</returns>
    private int CompareByAxis(int leftItem, int rightItem)
    {
        Span<double> column = SortAxis switch
        {
            0 => MinX.Span,
            1 => MaxX.Span,
            2 => MinY.Span,
            _ => MaxY.Span
        };

        return column[leftItem].CompareTo(column[rightItem]);
    }

    /// <summary>Returns every pooled column; idempotent.</summary>
    public void Dispose()
    {
        if(Disposed)
        {
            return;
        }

        Disposed = true;
        MinX.Dispose();
        MaxX.Dispose();
        MinY.Dispose();
        MaxY.Dispose();
        Order.Dispose();
        NodeLeft.Dispose();
        NodeRight.Dispose();
        NodeAxis.Dispose();
        NodeSplit.Dispose();
        NodeItemStart.Dispose();
        NodeItemSpan.Dispose();
        BuildStack.Dispose();
        TraversalStack.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One pending subtree for the iterative build: the item range, its k-d depth (axis = depth &amp; 3), and which parent slot to fill on creation.</summary>
    /// <param name="ItemStart">The first position in the permutation this subtree owns.</param>
    /// <param name="ItemCount">The number of positions this subtree owns.</param>
    /// <param name="Depth">The k-d depth, whose low two bits select the split axis.</param>
    /// <param name="ParentNode">The parent node slot to link on creation, or -1 for the root.</param>
    /// <param name="IsLeftChild">Whether the created node fills the parent's left slot.</param>
    private readonly record struct BuildWorkItem(int ItemStart, int ItemCount, int Depth, int ParentNode, bool IsLeftChild);

    /// <summary>
    /// Walks the tree for one containment query, yielding the item ids of boxes
    /// that enclose the query box. A mutable <see langword="ref struct"/> over
    /// spans hoisted from the tree's pooled columns and over the shared traversal
    /// stack — do not keep two alive at once.
    /// </summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible",
        Justification = "The enumerator is the owning index's enumeration pattern and is meaningless apart from the tree whose columns and shared traversal stack it walks. This mirrors the BCL's nested enumerator idiom (List<T>.Enumerator, Span<T>.Enumerator).")]
    public ref struct DominanceEnumerator
    {
        /// <summary>The hoisted minimum-x column.</summary>
        private ReadOnlySpan<double> MinX { get; }

        /// <summary>The hoisted maximum-x column.</summary>
        private ReadOnlySpan<double> MaxX { get; }

        /// <summary>The hoisted minimum-y column.</summary>
        private ReadOnlySpan<double> MinY { get; }

        /// <summary>The hoisted maximum-y column.</summary>
        private ReadOnlySpan<double> MaxY { get; }

        /// <summary>The hoisted item permutation.</summary>
        private ReadOnlySpan<int> Order { get; }

        /// <summary>The hoisted left-child column.</summary>
        private ReadOnlySpan<int> NodeLeft { get; }

        /// <summary>The hoisted right-child column.</summary>
        private ReadOnlySpan<int> NodeRight { get; }

        /// <summary>The hoisted split-axis column.</summary>
        private ReadOnlySpan<int> NodeAxis { get; }

        /// <summary>The hoisted split-value column.</summary>
        private ReadOnlySpan<double> NodeSplit { get; }

        /// <summary>The hoisted leaf item-start column.</summary>
        private ReadOnlySpan<int> NodeItemStart { get; }

        /// <summary>The hoisted leaf item-span column.</summary>
        private ReadOnlySpan<int> NodeItemSpan { get; }

        /// <summary>The shared descent stack this enumeration writes into.</summary>
        private Span<int> TraversalStack { get; }

        /// <summary>The query bounds, named by the constraint each places on a containing box.</summary>
        private double QMinX { get; }

        /// <summary>The query's maximum x; a container's maximum x must be at least this.</summary>
        private double QMaxX { get; }

        /// <summary>The query's minimum y; a container's minimum y must be at most this.</summary>
        private double QMinY { get; }

        /// <summary>The query's maximum y; a container's maximum y must be at least this.</summary>
        private double QMaxY { get; }

        /// <summary>The number of node slots currently on the descent stack.</summary>
        private int StackTop { get; set; }

        /// <summary>The next permutation position the current leaf scan reads.</summary>
        private int LeafCursor { get; set; }

        /// <summary>The permutation position the current leaf scan stops before.</summary>
        private int LeafEnd { get; set; }

        /// <summary>The item id most recently yielded, or -1 before the first <see cref="MoveNext"/>.</summary>
        public int Current { get; private set; }

        /// <summary>Hoists the tree's columns and seeds the descent stack with the root, if the tree has one.</summary>
        /// <param name="tree">The tree whose columns the enumeration walks.</param>
        /// <param name="qMinX">The query's minimum x.</param>
        /// <param name="qMinY">The query's minimum y.</param>
        /// <param name="qMaxX">The query's maximum x.</param>
        /// <param name="qMaxY">The query's maximum y.</param>
        internal DominanceEnumerator(BoxContainmentIndex tree, double qMinX, double qMinY, double qMaxX, double qMaxY)
        {
            MinX = tree.MinX.Span;
            MaxX = tree.MaxX.Span;
            MinY = tree.MinY.Span;
            MaxY = tree.MaxY.Span;
            Order = tree.Order.Span;
            NodeLeft = tree.NodeLeft.Span;
            NodeRight = tree.NodeRight.Span;
            NodeAxis = tree.NodeAxis.Span;
            NodeSplit = tree.NodeSplit.Span;
            NodeItemStart = tree.NodeItemStart.Span;
            NodeItemSpan = tree.NodeItemSpan.Span;
            TraversalStack = tree.TraversalStack.Span;
            QMinX = qMinX;
            QMaxX = qMaxX;
            QMinY = qMinY;
            QMaxY = qMaxY;
            LeafCursor = 0;
            LeafEnd = 0;
            Current = -1;

            if(tree.RootNode < 0)
            {
                StackTop = 0;

                return;
            }

            TraversalStack[0] = tree.RootNode;
            StackTop = 1;
        }

        /// <summary>The <see langword="foreach"/> pattern's entry point; the enumerator is its own enumerable.</summary>
        /// <returns>This enumerator.</returns>
        public readonly DominanceEnumerator GetEnumerator()
        {
            return this;
        }

        /// <summary>Advances to the next containing box.</summary>
        /// <returns><see langword="true"/> when <see cref="Current"/> holds a further item id; otherwise <see langword="false"/>.</returns>
        public bool MoveNext()
        {
            while(true)
            {
                while(LeafCursor < LeafEnd)
                {
                    int item = Order[LeafCursor];
                    LeafCursor++;

                    //The full four-coordinate containment test — the authority the prune is only allowed to anticipate.
                    if(MinX[item] <= QMinX && MaxX[item] >= QMaxX && MinY[item] <= QMinY && MaxY[item] >= QMaxY)
                    {
                        Current = item;

                        return true;
                    }
                }

                if(StackTop == 0)
                {
                    return false;
                }

                StackTop--;
                int node = TraversalStack[StackTop];

                if(NodeLeft[node] < 0)
                {
                    LeafCursor = NodeItemStart[node];
                    LeafEnd = LeafCursor + NodeItemSpan[node];

                    continue;
                }

                PushSurvivingChildren(node);
            }
        }

        /// <summary>Descend only the children that can still hold a containing box, by the split coordinate's bound.</summary>
        /// <param name="node">The internal node whose children are being considered.</param>
        private void PushSurvivingChildren(int node)
        {
            int axis = NodeAxis[node];
            double split = NodeSplit[node];
            int left = NodeLeft[node];
            int right = NodeRight[node];

            //Left holds the smaller split-axis values, right the larger (the build sorts ascending before halving).
            bool descendLeft;
            bool descendRight;

            if((axis & 1) == 0)
            {
                //MinX / MinY: a container needs box-value ≤ query-value, so a subtree all above the query bound is dead.
                double bound = axis == 0 ? QMinX : QMinY;
                descendLeft = true;
                descendRight = split <= bound;
            }
            else
            {
                //MaxX / MaxY: a container needs box-value ≥ query-value, so a subtree all below the query bound is dead.
                double bound = axis == 1 ? QMaxX : QMaxY;
                descendRight = true;
                descendLeft = split >= bound;
            }

            if(descendLeft)
            {
                TraversalStack[StackTop] = left;
                StackTop++;
            }

            if(descendRight)
            {
                TraversalStack[StackTop] = right;
                StackTop++;
            }
        }
    }
}
