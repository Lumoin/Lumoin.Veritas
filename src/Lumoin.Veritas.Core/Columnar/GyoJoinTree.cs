using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Execution;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// A join tree over an acyclic basic graph pattern, built by the GYO
/// ear-removal reduction with witness tracking: each non-root pattern
/// points at the parent edge it was absorbed into, and the post-order
/// lists every pattern before its parent. The structure Yannakakis'
/// two semijoin passes walk — the upward (reducing) pass follows the
/// post-order, the downward pass its reverse — so that after both
/// passes every relation holds only tuples that extend to a full
/// answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Relation to the acyclicity gate.</b> The same GYO reduction
/// <see cref="ColumnarBatchPipeline"/> uses to accept a query is run
/// here with the witness recorded, so the tree exists exactly when
/// the query is acyclic, connected, and every tree edge shares one or
/// two variables — the boundary <see cref="SolutionBatchSemijoin"/>
/// and <see cref="SolutionBatchJoin"/> both draw. A query that fails
/// any of those falls back to the unreduced left-deep pipeline, whose
/// answers are identical; the tree only removes dangling tuples first.
/// </para>
/// <para>
/// A variable occurring in a single surviving edge cannot bind two
/// patterns, so it is dropped before the subset test; an edge then
/// contained in another is an ear, absorbed into that witness. The
/// hypergraph is acyclic exactly when this leaves one edge, the root.
/// </para>
/// </remarks>
/// <param name="Parent">For each pattern, the index of its parent pattern, or −1 for the root.</param>
/// <param name="PostOrder">The pattern indices with every child before its parent; the root is last.</param>
public sealed record GyoJoinTree(IReadOnlyList<int> Parent, IReadOnlyList<int> PostOrder)
{
    /// <summary>
    /// Builds the join tree for <paramref name="edges"/> — the
    /// patterns' variable sets in pattern order — or returns
    /// <see langword="null"/> when the hypergraph is cyclic,
    /// disconnected, or a tree edge's separator falls outside the one
    /// or two variables the semijoin key packs.
    /// </summary>
    /// <param name="edges">The patterns' variable sets, in pattern order.</param>
    /// <returns>The join tree, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="edges"/> is <c>null</c>.</exception>
    public static GyoJoinTree? TryBuild(IReadOnlyList<IReadOnlyCollection<Variable>> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        int n = edges.Count;
        if(n == 0)
        {
            return null;
        }

        List<HashSet<Variable>> original = new(n);
        List<HashSet<Variable>> sets = new(n);
        foreach(IReadOnlyCollection<Variable> edge in edges)
        {
            original.Add([.. edge]);
            sets.Add([.. edge]);
        }

        int[] parent = new int[n];
        Array.Fill(parent, -1);
        bool[] removed = new bool[n];
        List<int> postOrder = new(n);
        int remaining = n;

        while(remaining > 1)
        {
            //Variables occurring in a single surviving edge cannot bind
            //two patterns; drop them so an ear shows as a subset.
            Dictionary<Variable, int> occurrences = [];
            for(int i = 0; i < n; i++)
            {
                if(removed[i])
                {
                    continue;
                }

                foreach(Variable variable in sets[i])
                {
                    occurrences[variable] = occurrences.TryGetValue(variable, out int count) ? count + 1 : 1;
                }
            }

            SingletonVariablePredicate isSingleton = new(occurrences);
            for(int i = 0; i < n; i++)
            {
                if(!removed[i])
                {
                    sets[i].RemoveWhere(isSingleton.IsSingleton);
                }
            }

            //An edge contained in another surviving edge is an ear:
            //absorb it into that witness and record the tree edge.
            bool earFound = false;
            for(int i = 0; i < n && !earFound; i++)
            {
                if(removed[i])
                {
                    continue;
                }

                for(int j = 0; j < n; j++)
                {
                    if(i == j || removed[j])
                    {
                        continue;
                    }

                    if(sets[i].IsSubsetOf(sets[j]))
                    {
                        parent[i] = j;
                        removed[i] = true;
                        postOrder.Add(i);
                        remaining--;
                        earFound = true;

                        break;
                    }
                }
            }

            if(!earFound)
            {
                //Irreducible with more than one edge left: cyclic.
                return null;
            }
        }

        int root = -1;
        for(int i = 0; i < n; i++)
        {
            if(!removed[i])
            {
                root = i;

                break;
            }
        }

        postOrder.Add(root);

        //Every tree edge must semijoin on one or two shared variables;
        //a zero-variable separator is a disconnected component absorbed
        //through an emptied subset, which leapfrog must take instead.
        for(int i = 0; i < n; i++)
        {
            if(parent[i] < 0)
            {
                continue;
            }

            int shared = SharedCount(original[i], original[parent[i]]);
            if(shared is < 1 or > SolutionBatchJoin.MaximumJoinVariables)
            {
                return null;
            }
        }

        return new GyoJoinTree(parent, postOrder);
    }

    /// <summary>The number of variables the two edges share.</summary>
    /// <param name="a">One edge's variables.</param>
    /// <param name="b">The other edge's variables.</param>
    /// <returns>The shared count.</returns>
    private static int SharedCount(HashSet<Variable> a, HashSet<Variable> b)
    {
        int shared = 0;
        foreach(Variable variable in a)
        {
            if(b.Contains(variable))
            {
                shared++;
            }
        }

        return shared;
    }

    /// <summary>
    /// Carries the per-variable occurrence counts as explicit state so the singleton test passed
    /// to the set's <c>RemoveWhere</c> is a bound method group, not a lambda closing over the
    /// enclosing counts.
    /// </summary>
    /// <param name="occurrences">Each variable's occurrence count across the surviving edges.</param>
    private sealed class SingletonVariablePredicate(Dictionary<Variable, int> occurrences)
    {
        /// <summary>Each variable's occurrence count across the surviving edges.</summary>
        private Dictionary<Variable, int> Occurrences { get; } = occurrences;

        /// <summary>Tests whether a variable occurs in exactly one surviving edge.</summary>
        /// <param name="variable">The variable to test.</param>
        /// <returns><see langword="true"/> when the variable occurs exactly once.</returns>
        public bool IsSingleton(Variable variable)
        {
            return Occurrences[variable] == 1;
        }
    }
}
