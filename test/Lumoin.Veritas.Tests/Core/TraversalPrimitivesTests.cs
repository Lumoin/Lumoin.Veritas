using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core.Algebra;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Core.Algebra;

/// <summary>
/// Tests for <see cref="TraversalPrimitives"/> exercising both the
/// labeled and unlabeled overloads over int-typed graphs built by a
/// test helper class. The helper's instance methods are bound via
/// method group to the adjacency delegate shapes, avoiding lambda
/// closures over captured parameters.
/// </summary>
[TestClass]
internal sealed class TraversalPrimitivesTests
{
    //Expected reachable-node set for the canonical 1-2-3-4 fan-out
    //graph. Kept as a static get-only property to satisfy CA1861 at
    //call sites that pass the array to Assert.AreSequenceEqual.
    private static int[] ExpectedFrom1Via234 { get; } = [2, 3, 4];

    //Expected reachable-node set for the cross-label reachability
    //test.
    private static int[] ExpectedCrossLabel234 { get; } = [2, 3, 4];

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task LabeledTransitiveClosureReturnsAllReachableExcludingStart()
    {
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);
        graph.AddEdge(2, Label.Follows, 3);
        graph.AddEdge(3, Label.Follows, 4);
        graph.AddEdge(2, Label.Mentions, 99);

        List<int> reached = [];
        await foreach(int n in TraversalPrimitives.TransitiveClosureAsync(
            1, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(n);
        }

        Assert.AreSequenceEqual(ExpectedFrom1Via234, reached, SequenceOrder.InAnyOrder);
        Assert.DoesNotContain(1, reached);
        Assert.DoesNotContain(99, reached);
    }

    [TestMethod]
    public async Task LabeledTransitiveClosureTerminatesOnCycles()
    {
        //Three-node cycle 1 → 2 → 3 → 1.
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);
        graph.AddEdge(2, Label.Follows, 3);
        graph.AddEdge(3, Label.Follows, 1);

        HashSet<int> reached = [];
        await foreach(int n in TraversalPrimitives.TransitiveClosureAsync(
            1, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(n);
        }

        //Cycle means everything is reached but nothing twice.
        Assert.HasCount(2, reached);
        Assert.Contains(2, reached);
        Assert.Contains(3, reached);
    }

    [TestMethod]
    public async Task UnlabeledTransitiveClosureIgnoresEdgeLabels()
    {
        //Same graph, different labels — unlabeled closure should reach
        //everything regardless.
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);
        graph.AddEdge(2, Label.Mentions, 3);
        graph.AddEdge(3, Label.Follows, 4);

        HashSet<int> reached = [];
        await foreach(int n in TraversalPrimitives.TransitiveClosureAsync(
            1, graph.AnyForwardAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(n);
        }

        Assert.AreSequenceEqual(ExpectedCrossLabel234, new List<int>(reached), SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public async Task LabeledIsReachableFindsReachableTarget()
    {
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);
        graph.AddEdge(2, Label.Follows, 3);

        bool reachable = await TraversalPrimitives.IsReachableAsync(
            1, 3, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsTrue(reachable);
    }

    [TestMethod]
    public async Task LabeledIsReachableReturnsFalseForUnreachable()
    {
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);

        bool reachable = await TraversalPrimitives.IsReachableAsync(
            1, 99, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsFalse(reachable);
    }

    [TestMethod]
    public async Task LabeledShortestPathReturnsStartOnlyWhenStartEqualsTarget()
    {
        LabeledIntGraph graph = new();

        IReadOnlyList<int>? path = await TraversalPrimitives.ShortestPathAsync(
            5, 5, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(path);
        Assert.HasCount(1, path);
        Assert.AreEqual(5, path[0]);
    }

    [TestMethod]
    public async Task LabeledShortestPathPicksShortestOfMultipleRoutes()
    {
        //Two routes from 1 to 5: 1→2→5 (length 3) and 1→3→4→5 (length 4).
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);
        graph.AddEdge(2, Label.Follows, 5);
        graph.AddEdge(1, Label.Follows, 3);
        graph.AddEdge(3, Label.Follows, 4);
        graph.AddEdge(4, Label.Follows, 5);

        IReadOnlyList<int>? path = await TraversalPrimitives.ShortestPathAsync(
            1, 5, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNotNull(path);
        Assert.HasCount(3, path);
        Assert.AreEqual(1, path[0]);
        Assert.AreEqual(5, path[2]);
    }

    [TestMethod]
    public async Task LabeledShortestPathReturnsNullWhenUnreachable()
    {
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);

        IReadOnlyList<int>? path = await TraversalPrimitives.ShortestPathAsync(
            1, 99, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false);

        Assert.IsNull(path);
    }

    [TestMethod]
    public async Task LabeledClosurePushesLabelDownDoesNotSeeOtherLabelEdges()
    {
        //1 → 2 via Follows; 1 → 99 via Mentions. Closing along Follows
        //should yield only 2, proving the label is actually used (not
        //discarded by the primitive or the adapter).
        LabeledIntGraph graph = new();
        graph.AddEdge(1, Label.Follows, 2);
        graph.AddEdge(1, Label.Mentions, 99);

        HashSet<int> reached = [];
        await foreach(int n in TraversalPrimitives.TransitiveClosureAsync(
            1, Label.Follows, graph.LabeledForwardAsync, TestContext.CancellationToken).ConfigureAwait(false))
        {
            reached.Add(n);
        }

        Assert.HasCount(1, reached);
        Assert.Contains(2, reached);
        Assert.DoesNotContain(99, reached);
    }

    //Edge labels for the test graph. An enum rather than an int
    //constant so the test intent reads naturally at the call site.
    private enum Label
    {
        Follows,
        Mentions,
    }

    //A tiny in-memory labeled int graph whose adjacency methods are
    //method-group-convertible to the generic delegate shapes. No
    //lambda closures anywhere — the graph's fields hold the state and
    //the methods read from them.
    private sealed class LabeledIntGraph
    {
        private readonly Dictionary<(int Source, Label Label), List<int>> byLabel = [];
        private readonly Dictionary<int, List<int>> anyLabel = [];

        public void AddEdge(int source, Label label, int target)
        {
            if(!byLabel.TryGetValue((source, label), out List<int>? labeled))
            {
                labeled = [];
                byLabel[(source, label)] = labeled;
            }
            labeled.Add(target);

            if(!anyLabel.TryGetValue(source, out List<int>? any))
            {
                any = [];
                anyLabel[source] = any;
            }
            any.Add(target);
        }

        public async IAsyncEnumerable<int> LabeledForwardAsync(
            int source, Label label, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(byLabel.TryGetValue((source, label), out List<int>? targets))
            {
                foreach(int target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return target;
                }
            }
        }

        public async IAsyncEnumerable<int> AnyForwardAsync(
            int source, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if(anyLabel.TryGetValue(source, out List<int>? targets))
            {
                foreach(int target in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return target;
                }
            }
        }
    }
}
