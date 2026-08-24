using System;
using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Ast;

namespace Lumoin.Veritas.Sparql.Execution;

/// <summary>
/// The build side of a row-level hash join over <see cref="SparqlSolution"/>s, indexed by the values of the join
/// variables: an allocation-lean CHAINED hash table. One dictionary entry per distinct key holds the
/// most-recently-inserted build row carrying it, and a parallel <c>next</c> array links every earlier row with the
/// same key. Matching a probe is one dictionary lookup then a pointer walk — no per-key collection is ever
/// allocated.
/// </summary>
/// <remarks>
/// <para>
/// This is the SPARQL solution-layer analogue of the columnar <see cref="Core.Hypertrie.Execution.SolutionBatchHashTable"/>:
/// the same chained-table shape, but keyed on decoded <see cref="RdfTerm"/> values rather than packed encoded ids,
/// because at this layer rows are partial solution mappings, not fixed-schema columnar batches.
/// </para>
/// <para>
/// The index supports one or two join variables (the common multi-BGP join shapes); the engine checks eligibility
/// and that every build and probe row binds all join variables BEFORE building, so key extraction here never
/// encounters an unbound join variable. Wider join-variable sets or partial bindings route to the nested-loop
/// fallback instead.
/// </para>
/// <para>The table is built once and then read-only, and is single-threaded by construction, like the engine that drives it.</para>
/// </remarks>
[DebuggerDisplay("SolutionHashJoinIndex Rows={rows.Length} Keys={head.Count}")]
internal sealed class SolutionHashJoinIndex
{
    private const int NoMatch = -1;

    private readonly SparqlVariable[] joinVariables;

    private readonly SparqlSolution[] rows;

    private readonly int[] next;

    private readonly Dictionary<JoinValueKey, int> head;

    private SolutionHashJoinIndex(SparqlVariable[] joinVariables, SparqlSolution[] rows, int[] next, Dictionary<JoinValueKey, int> head)
    {
        this.joinVariables = joinVariables;
        this.rows = rows;
        this.next = next;
        this.head = head;
    }

    /// <summary>
    /// Builds the index over <paramref name="buildSide"/>, keying every row on its <paramref name="joinVariables"/>
    /// values and chaining rows that share a key.
    /// </summary>
    /// <param name="buildSide">The build-side solutions; held by reference, each row must bind every join variable.</param>
    /// <param name="joinVariables">The one or two join variables, in key order.</param>
    /// <returns>The built index.</returns>
    public static SolutionHashJoinIndex Build(IReadOnlyList<SparqlSolution> buildSide, SparqlVariable[] joinVariables)
    {
        SparqlSolution[] rows = new SparqlSolution[buildSide.Count];
        int[] next = new int[buildSide.Count];
        Dictionary<JoinValueKey, int> head = new(buildSide.Count);
        for(int rowId = 0; rowId < buildSide.Count; rowId++)
        {
            SparqlSolution solution = buildSide[rowId];
            rows[rowId] = solution;

            JoinValueKey key = KeyOf(solution, joinVariables);
            next[rowId] = head.TryGetValue(key, out int previous) ? previous : NoMatch;
            head[key] = rowId;
        }

        return new SolutionHashJoinIndex(joinVariables, rows, next, head);
    }

    /// <summary>Returns the first build row compatible with <paramref name="probe"/> (sharing all join-variable values), or −1 when none match.</summary>
    /// <param name="probe">The probe-side solution; must bind every join variable.</param>
    /// <returns>The first matching build row id, or −1.</returns>
    public int FirstMatch(SparqlSolution probe)
    {
        return head.TryGetValue(KeyOf(probe, joinVariables), out int rowId) ? rowId : NoMatch;
    }

    /// <summary>Returns the next build row sharing the current row's key, or −1 at the chain's end.</summary>
    /// <param name="rowId">A build row id from <see cref="FirstMatch"/> or a prior <see cref="NextMatch"/>.</param>
    /// <returns>The next matching build row id, or −1.</returns>
    public int NextMatch(int rowId)
    {
        return next[rowId];
    }

    /// <summary>Reads the build solution at a row id.</summary>
    /// <param name="rowId">The build row id.</param>
    /// <returns>The build solution.</returns>
    public SparqlSolution RowAt(int rowId)
    {
        return rows[rowId];
    }

    /// <summary>Extracts the join key from a solution that binds every join variable.</summary>
    /// <param name="solution">The solution to key.</param>
    /// <param name="joinVariables">The one or two join variables, in key order.</param>
    /// <returns>The packed join-value key.</returns>
    private static JoinValueKey KeyOf(SparqlSolution solution, SparqlVariable[] joinVariables)
    {
        solution.TryGetValue(joinVariables[0], out RdfTerm first);
        RdfTerm? second = null;
        if(joinVariables.Length > 1)
        {
            solution.TryGetValue(joinVariables[1], out RdfTerm secondValue);
            second = secondValue;
        }

        return new JoinValueKey(first, second);
    }

    /// <summary>A hashable key over one or two <see cref="RdfTerm"/> join values; the second slot is <see langword="null"/> for a single-variable join.</summary>
    private readonly struct JoinValueKey: IEquatable<JoinValueKey>
    {
        private readonly RdfTerm first;

        private readonly RdfTerm? second;

        /// <summary>Constructs a key over the given join values.</summary>
        /// <param name="first">The first join value.</param>
        /// <param name="second">The second join value, or <see langword="null"/> for a single-variable join.</param>
        public JoinValueKey(RdfTerm first, RdfTerm? second)
        {
            this.first = first;
            this.second = second;
        }

        /// <summary>Returns whether this key equals another (value equality of both join terms).</summary>
        /// <param name="other">The key to compare against.</param>
        /// <returns><see langword="true"/> when both join values are equal.</returns>
        public bool Equals(JoinValueKey other)
        {
            return first.Equals(other.first) && Equals(second, other.second);
        }

        /// <summary>Returns whether this key equals another object.</summary>
        /// <param name="obj">The object to compare against.</param>
        /// <returns><see langword="true"/> when <paramref name="obj"/> is an equal key.</returns>
        public override bool Equals(object? obj)
        {
            return obj is JoinValueKey other && Equals(other);
        }

        /// <summary>Returns the combined hash of the join values.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(first, second);
        }
    }
}
