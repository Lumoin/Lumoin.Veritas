using Lumoin.Veritas.Core.Algebra;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Tests.Core.Algebra;

/// <summary>
/// Tests for <see cref="IterativeTraversal"/>. Cover depth-first and
/// breadth-first traversal over a tiny unlabeled graph, multi-seed
/// input, cycle tolerance, and key-based de-duplication on composite
/// node types.
/// </summary>
[TestClass]
internal sealed class IterativeTraversalTests
{
    //Expected union of the two disjoint components in the multi-seed
    //BFS test. Extracted to a static get-only property to satisfy
    //CA1861 at the CollectionAssert call site.
    private static int[] ExpectedMultiSeedReach { get; } = [1, 2, 10, 11];

    //Expected set of keys discovered by the key-based dedup BFS.
    private static int[] ExpectedPairGraphKeys { get; } = [1, 2, 3];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task DepthFirstVisitsEveryReachableNodeExactlyOnce()
    {
        //     1
        //   / | \
        //  2  3  4
        //  |
        //  5
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(1, 3);
        graph.AddEdge(1, 4);
        graph.AddEdge(2, 5);

        List<int> visited = [];
        await foreach(int n in IterativeTraversal.DepthFirstAsync<int>(
            [1], graph.AdjacencyAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(n);
        }

        Assert.HasCount(5, visited);
        Assert.HasCount(5, visited.Distinct());
        Assert.Contains(1, visited);
        Assert.Contains(5, visited);
    }

    [TestMethod]
    public async Task DepthFirstProducesLifoOrder()
    {
        //Children of 1 in the order 2, 3, 4 are pushed in that order.
        //Stack pops in LIFO, so 4's subtree is explored before 3's,
        //and 3's before 2's.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(1, 3);
        graph.AddEdge(1, 4);

        List<int> visited = [];
        await foreach(int n in IterativeTraversal.DepthFirstAsync<int>(
            [1], graph.AdjacencyAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(n);
        }

        Assert.AreEqual(1, visited[0]);
        //After the seed, LIFO order: last-pushed child first.
        Assert.AreEqual(4, visited[1]);
        Assert.AreEqual(3, visited[2]);
        Assert.AreEqual(2, visited[3]);
    }

    [TestMethod]
    public async Task BreadthFirstProducesLevelOrder()
    {
        //Level 0: seed 1. Level 1: 2, 3, 4. Level 2: 5.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(1, 3);
        graph.AddEdge(1, 4);
        graph.AddEdge(2, 5);

        List<int> visited = [];
        await foreach(int n in IterativeTraversal.BreadthFirstAsync<int>(
            [1], graph.AdjacencyAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(n);
        }

        Assert.AreEqual(1, visited[0]);
        //Level-one trio in insertion order.
        Assert.AreEqual(2, visited[1]);
        Assert.AreEqual(3, visited[2]);
        Assert.AreEqual(4, visited[3]);
        Assert.AreEqual(5, visited[4]);
    }

    [TestMethod]
    public async Task DepthFirstTerminatesOnCycles()
    {
        //Cycle 1 → 2 → 3 → 1.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(2, 3);
        graph.AddEdge(3, 1);

        HashSet<int> visited = [];
        await foreach(int n in IterativeTraversal.DepthFirstAsync<int>(
            [1], graph.AdjacencyAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(n);
        }

        Assert.HasCount(3, visited);
    }

    [TestMethod]
    public async Task MultipleSeedsAreAllProcessed()
    {
        //Disjoint components: 1 → 2 and 10 → 11.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(10, 11);

        HashSet<int> visited = [];
        await foreach(int n in IterativeTraversal.BreadthFirstAsync<int>(
            [1, 10], graph.AdjacencyAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(n);
        }

        Assert.HasCount(4, visited);
        Assert.AreSequenceEqual(ExpectedMultiSeedReach, new List<int>(visited), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task DuplicateSeedsAreDeduplicated()
    {
        IntGraph graph = new();
        graph.AddEdge(1, 2);

        List<int> visited = [];
        await foreach(int n in IterativeTraversal.BreadthFirstAsync<int>(
            [1, 1, 1], graph.AdjacencyAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(n);
        }

        Assert.HasCount(2, visited);
    }

    [TestMethod]
    public async Task KeyBasedDedupeTreatsNodesSharingKeyAsVisited()
    {
        //Compound nodes keyed on the first int of a pair. (1, 100) has
        //three children: (2, 200), (2, 999), and (3, 300). The first
        //child claims key 2; the second tries to claim key 2 again and
        //is suppressed by the visited set; the third claims key 3 and
        //is enqueued. Without key-based dedup we would visit four
        //nodes; with it we visit exactly three, with keys {1, 2, 3}.
        PairGraph graph = new();
        graph.AddEdge((1, 100), (2, 200));
        graph.AddEdge((1, 100), (2, 999));
        graph.AddEdge((1, 100), (3, 300));

        List<(int Key, int Payload)> visited = [];
        await foreach((int Key, int Payload) node in IterativeTraversal.BreadthFirstAsync(
            [(1, 100)],
            PairGraph.KeyOf,
            graph.AdjacencyAsync,
            TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(node);
        }

        Assert.HasCount(3, visited);
        HashSet<int> keys = [.. visited.Select(static v => v.Key)];
        Assert.AreSequenceEqual(ExpectedPairGraphKeys, new List<int>(keys), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void PostOrderYieldsChildrenBeforeParents()
    {
        //     1
        //    / \
        //   2   3
        //  / \
        // 4   5
        //Every parent must appear after every reachable descendant.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(1, 3);
        graph.AddEdge(2, 4);
        graph.AddEdge(2, 5);

        List<int> visited = [.. IterativeTraversal.PostOrder<int, int>(
            seeds: [1],
            keyOf: static n => n,
            adjacency: graph.Adjacency)];

        //Five reachable nodes, each yielded exactly once.
        Assert.HasCount(5, visited);
        Assert.HasCount(5, visited.Distinct());

        //Parent-after-descendants check: for each parent → child
        //edge, the child's position in `visited` must be earlier
        //than the parent's. The two-stack idiom guarantees this for
        //every reachable edge.
        AssertChildBeforeParent(visited, parent: 1, child: 2);
        AssertChildBeforeParent(visited, parent: 1, child: 3);
        AssertChildBeforeParent(visited, parent: 2, child: 4);
        AssertChildBeforeParent(visited, parent: 2, child: 5);
    }

    [TestMethod]
    public void PostOrderOnDagYieldsSharedNodeOnce()
    {
        //     1
        //    / \
        //   2   3
        //    \ /
        //     4   <-- shared between 2 and 3
        //The shared node must be yielded exactly once and must
        //appear before both 2 and 3.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(1, 3);
        graph.AddEdge(2, 4);
        graph.AddEdge(3, 4);

        List<int> visited = [.. IterativeTraversal.PostOrder<int, int>(
            seeds: [1],
            keyOf: static n => n,
            adjacency: graph.Adjacency)];

        Assert.HasCount(4, visited);
        Assert.ContainsSingle(visited.Where(static n => n == 4));
        AssertChildBeforeParent(visited, parent: 2, child: 4);
        AssertChildBeforeParent(visited, parent: 3, child: 4);
    }

    [TestMethod]
    public void PostOrderOnCycleYieldsEveryReachableNodeExactlyOnce()
    {
        //  1 → 2 → 3 → 1 (cycle back to seed)
        //Discovery dedup must terminate the walk; output is the
        //three reachable nodes in some valid post-order.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(2, 3);
        graph.AddEdge(3, 1);

        List<int> visited = [.. IterativeTraversal.PostOrder<int, int>(
            seeds: [1],
            keyOf: static n => n,
            adjacency: graph.Adjacency)];

        Assert.HasCount(3, visited);
        Assert.HasCount(3, visited.Distinct());
    }

    [TestMethod]
    public async Task PostOrderAsyncYieldsChildrenBeforeParents()
    {
        //Same graph and assertion as PostOrderYieldsChildrenBeforeParents,
        //exercising the async overload to confirm the two shapes
        //produce the same ordering on the same input.
        IntGraph graph = new();
        graph.AddEdge(1, 2);
        graph.AddEdge(1, 3);
        graph.AddEdge(2, 4);
        graph.AddEdge(2, 5);

        List<int> visited = [];
        await foreach(int node in IterativeTraversal.PostOrderAsync<int, int>(
            seeds: [1],
            keyOf: static n => n,
            adjacency: graph.AdjacencyAsync,
            cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
        {
            visited.Add(node);
        }

        Assert.HasCount(5, visited);
        Assert.HasCount(5, visited.Distinct());
        AssertChildBeforeParent(visited, parent: 1, child: 2);
        AssertChildBeforeParent(visited, parent: 1, child: 3);
        AssertChildBeforeParent(visited, parent: 2, child: 4);
        AssertChildBeforeParent(visited, parent: 2, child: 5);
    }

    //Asserts that `child` appears strictly before `parent` in
    //`visited`. Used to verify the post-order ordering invariant
    //independent of any specific tie-breaking among siblings.
    //
    //Assert.IsLessThan in MSTest 4.x has the signature
    //(T upperBound, T value) and asserts value < upperBound, so
    //the call here passes parentIndex first and childIndex
    //second to express "childIndex < parentIndex".
    private static void AssertChildBeforeParent(List<int> visited, int parent, int child)
    {
        int parentIndex = visited.IndexOf(parent);
        int childIndex = visited.IndexOf(child);
        Assert.IsLessThan(parentIndex, childIndex,
            $"Expected child {child} (index {childIndex}) to appear before parent {parent} (index {parentIndex}).");
    }

    //A tiny unlabeled int-keyed graph used for the simple DFS/BFS
    //coverage tests.
    private sealed class IntGraph
    {
        private readonly Dictionary<int, List<int>> edges = [];

        public void AddEdge(int source, int target)
        {
            if(!edges.TryGetValue(source, out List<int>? list))
            {
                list = [];
                edges[source] = list;
            }
            list.Add(target);
        }

        public async IAsyncEnumerable<int> AdjacencyAsync(
            int node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(edges.TryGetValue(node, out List<int>? targets))
            {
                foreach(int target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return target;
                }
            }
        }

        public IEnumerable<int> Adjacency(int node)
        {
            if(edges.TryGetValue(node, out List<int>? targets))
            {
                foreach(int target in targets)
                {
                    yield return target;
                }
            }
        }
    }

    //A graph whose nodes are (int, int) pairs, used to exercise
    //key-based dedup. The key is the first component.
    private sealed class PairGraph
    {
        private readonly Dictionary<(int, int), List<(int, int)>> edges = [];

        public static int KeyOf((int Key, int Payload) node) => node.Key;

        public void AddEdge((int, int) source, (int, int) target)
        {
            if(!edges.TryGetValue(source, out List<(int, int)>? list))
            {
                list = [];
                edges[source] = list;
            }
            list.Add(target);
        }

        public async IAsyncEnumerable<(int, int)> AdjacencyAsync((int, int) node, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(edges.TryGetValue(node, out List<(int, int)>? targets))
            {
                foreach((int, int) target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return target;
                }
            }
        }
    }
}
