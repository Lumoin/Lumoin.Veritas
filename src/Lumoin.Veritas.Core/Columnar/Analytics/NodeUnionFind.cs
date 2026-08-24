using System.Collections.Generic;

namespace Lumoin.Veritas.Core.Columnar.Analytics;

/// <summary>
/// A growable disjoint-set (union-find) over dense node ids, with iterative path-compressed finds and union by
/// rank — the connected-components primitive. Ids are handed out by <see cref="Add"/> as nodes are first seen,
/// so the structure grows with the edge scan instead of needing the node count up front. Finds are iterative
/// (an explicit two-pass loop, never call-stack recursion).
/// </summary>
internal sealed class NodeUnionFind
{
    /// <summary>Each id's parent id; a root is its own parent.</summary>
    private List<int> Parents { get; } = [];

    /// <summary>Each root's rank — an upper bound on its tree height — for union by rank.</summary>
    private List<int> Ranks { get; } = [];

    /// <summary>The number of ids handed out so far.</summary>
    internal int Count => Parents.Count;

    /// <summary>Adds a new singleton id.</summary>
    /// <returns>The new id, equal to the prior <see cref="Count"/>.</returns>
    internal int Add()
    {
        int id = Parents.Count;
        Parents.Add(id);
        Ranks.Add(0);

        return id;
    }

    /// <summary>The representative root of <paramref name="id"/>'s set, compressing the path to the root on the way.</summary>
    /// <param name="id">The id to resolve.</param>
    /// <returns>The set's root id.</returns>
    internal int Find(int id)
    {
        int root = id;
        while(Parents[root] != root)
        {
            root = Parents[root];
        }

        while(Parents[id] != root)
        {
            int next = Parents[id];
            Parents[id] = root;
            id = next;
        }

        return root;
    }

    /// <summary>Merges the sets containing <paramref name="a"/> and <paramref name="b"/>; a no-op when they already share a root.</summary>
    /// <param name="a">A member of the first set.</param>
    /// <param name="b">A member of the second set.</param>
    internal void Union(int a, int b)
    {
        int rootA = Find(a);
        int rootB = Find(b);
        if(rootA == rootB)
        {
            return;
        }

        if(Ranks[rootA] < Ranks[rootB])
        {
            (rootA, rootB) = (rootB, rootA);
        }

        Parents[rootB] = rootA;
        if(Ranks[rootA] == Ranks[rootB])
        {
            Ranks[rootA]++;
        }
    }
}
