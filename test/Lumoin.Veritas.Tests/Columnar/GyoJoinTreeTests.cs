using System.Collections.Generic;
using System.Linq;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Tests.Columnar;

/// <summary>
/// The GYO join-tree builder's contract: acyclic shapes (chains,
/// stars, branching trees) produce a spanning tree whose every edge
/// shares one or two variables and whose post-order lists children
/// before parents; cyclic and disconnected shapes report no tree, so
/// they fall back to the unreduced pipeline.
/// </summary>
[TestClass]
internal sealed class GyoJoinTreeTests
{
    /// <summary>Builds the edge list — one variable set per pattern — for the builder.</summary>
    /// <param name="edges">The variable sets.</param>
    /// <returns>The edge list.</returns>
    private static List<IReadOnlyCollection<Variable>> Edges(params HashSet<Variable>[] edges)
    {
        List<IReadOnlyCollection<Variable>> list = [];
        foreach(HashSet<Variable> edge in edges)
        {
            list.Add(edge);
        }

        return list;
    }

    /// <summary>Asserts the tree is a spanning tree over <paramref name="edges"/>: one root, acyclic parent chains, every node once in the post-order, every tree edge sharing one or two variables, every node before its parent.</summary>
    /// <param name="tree">The built tree.</param>
    /// <param name="edges">The original edges.</param>
    private static void AssertSpanningTree(GyoJoinTree tree, List<IReadOnlyCollection<Variable>> edges)
    {
        int n = edges.Count;
        Assert.HasCount(n, tree.Parent);
        Assert.HasCount(n, tree.PostOrder);

        //Exactly one root.
        int roots = 0;
        for(int i = 0; i < n; i++)
        {
            if(tree.Parent[i] < 0)
            {
                roots++;
            }
        }

        Assert.AreEqual(1, roots);

        //The post-order is a permutation of the nodes.
        HashSet<int> seen = [.. tree.PostOrder];
        Assert.HasCount(n, seen);

        //Every node before its parent in the post-order.
        Dictionary<int, int> position = [];
        for(int p = 0; p < tree.PostOrder.Count; p++)
        {
            position[tree.PostOrder[p]] = p;
        }

        for(int i = 0; i < n; i++)
        {
            if(tree.Parent[i] >= 0)
            {
                Assert.IsLessThan(position[tree.Parent[i]], position[i]);
            }
        }

        //Every parent chain reaches the root without a cycle.
        for(int i = 0; i < n; i++)
        {
            int steps = 0;
            int node = i;
            while(tree.Parent[node] >= 0)
            {
                node = tree.Parent[node];
                steps++;
                Assert.IsLessThanOrEqualTo(n, steps);
            }
        }

        //Every tree edge shares one or two variables.
        for(int i = 0; i < n; i++)
        {
            if(tree.Parent[i] < 0)
            {
                continue;
            }

            int shared = 0;
            foreach(Variable variable in edges[i])
            {
                if(edges[tree.Parent[i]].Contains(variable))
                {
                    shared++;
                }
            }

            Assert.IsGreaterThanOrEqualTo(1, shared);
            Assert.IsLessThanOrEqualTo(2, shared);
        }
    }

    [TestMethod]
    public void ChainBuildsASpanningTree()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        List<IReadOnlyCollection<Variable>> edges = Edges([a, b], [b, c], [c, d]);
        GyoJoinTree? tree = GyoJoinTree.TryBuild(edges);

        Assert.IsNotNull(tree);
        AssertSpanningTree(tree, edges);
    }

    [TestMethod]
    public void StarBuildsASpanningTree()
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");

        List<IReadOnlyCollection<Variable>> edges = Edges([x, a], [x, b], [x, c]);
        GyoJoinTree? tree = GyoJoinTree.TryBuild(edges);

        Assert.IsNotNull(tree);
        AssertSpanningTree(tree, edges);
    }

    [TestMethod]
    public void BranchingTreeBuildsASpanningTree()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");
        Variable e = registry.GetOrAdd("e");

        //a-b is the trunk; b branches to c and to d; d extends to e.
        List<IReadOnlyCollection<Variable>> edges = Edges([a, b], [b, c], [b, d], [d, e]);
        GyoJoinTree? tree = GyoJoinTree.TryBuild(edges);

        Assert.IsNotNull(tree);
        AssertSpanningTree(tree, edges);
    }

    [TestMethod]
    public void SingleEdgeIsItsOwnRoot()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");

        GyoJoinTree? tree = GyoJoinTree.TryBuild(Edges([a, b]));

        Assert.IsNotNull(tree);
        Assert.AreEqual(-1, tree.Parent[0]);
        Assert.AreSequenceEqual(new List<int> { 0 }, new List<int>(tree.PostOrder));
    }

    [TestMethod]
    public void TriangleHasNoTree()
    {
        VariableRegistry registry = new();
        Variable x = registry.GetOrAdd("x");
        Variable y = registry.GetOrAdd("y");
        Variable z = registry.GetOrAdd("z");

        Assert.IsNull(GyoJoinTree.TryBuild(Edges([x, y], [y, z], [z, x])));
    }

    [TestMethod]
    public void DisconnectedComponentsHaveNoTree()
    {
        VariableRegistry registry = new();
        Variable a = registry.GetOrAdd("a");
        Variable b = registry.GetOrAdd("b");
        Variable c = registry.GetOrAdd("c");
        Variable d = registry.GetOrAdd("d");

        Assert.IsNull(GyoJoinTree.TryBuild(Edges([a, b], [c, d])));
    }

    [TestMethod]
    public void EmptyHasNoTree()
    {
        Assert.IsNull(GyoJoinTree.TryBuild([]));
    }
}
