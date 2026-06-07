using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Plans a global variable order compatible with a three-rotation
/// index, or reports that none exists — the coordination problem
/// the reduced order set creates.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem.</b> The three rotations (SPO, POS, OSP) cover
/// every BOUND SET: whichever positions a pattern binds, one
/// rotation has them as its prefix, so any single pattern is
/// answerable. But a worst-case-optimal join needs ONE global
/// variable elimination order, and each pattern's iterator can
/// only present its variables in the chosen rotation's TAIL order
/// — the global order must restrict, per pattern, to that tail.
/// Those per-pattern constraints can contradict each other: in the
/// triangle <c>(?x p ?y) (?y p ?z) (?z p ?x)</c> every pattern
/// binds P, forcing rotation POS with tail (object, subject), so
/// the patterns demand y&lt;x, z&lt;y, and x&lt;z — a cycle. No
/// global order exists, and the query honestly falls back through
/// the rendezvous to an engine that carries all six orders.
/// </para>
/// <para>
/// <b>The search.</b> A pattern binding one or two positions has
/// exactly one admissible rotation, hence one induced ordering of
/// its variables; a fully-variable pattern admits all three
/// rotations. The planner backtracks over the unbound patterns'
/// rotation choices, accumulates the induced pairwise precedence
/// edges, and looks for an acyclic combination; the first one
/// found (rotation choices tried in permutation-index order)
/// yields the global order by topological sort, ties broken by
/// first occurrence in the query — deterministic for a given
/// query.
/// </para>
/// <para>
/// <b>What this cannot fix.</b> Rotation incompatibility is a
/// property of the query's shape under flat per-rotation columns,
/// not of the planner: serving all six orders at three orders'
/// cost requires rank/select-style structures rather than CSR
/// columns, and that is the recorded alternative if coverage ever
/// has to widen without the memory doubling. Under
/// <see cref="ColumnarOrderSetMode.AllSixOrders"/> every order is
/// materialised and planning is the identity.
/// </para>
/// </remarks>
public static class ColumnarRotationPlanner
{
    /// <summary>
    /// Finds a global variable order every pattern of
    /// <paramref name="query"/> can follow on
    /// <paramref name="index"/>, or <see langword="null"/> when the
    /// query's shape is rotation-incompatible. Under
    /// <see cref="ColumnarOrderSetMode.AllSixOrders"/> the query's
    /// own first-occurrence order is returned unchanged.
    /// </summary>
    /// <param name="index">The index whose materialised orders constrain the plan.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The global variable order, or <see langword="null"/> when none exists.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<Variable>? TryPlanGlobalOrder(ColumnarTripleIndex index, BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(index);

        return TryPlanGlobalOrder(index.OrderSetMode, query);
    }

    /// <summary>
    /// Plans against an order-set MODE rather than a built index —
    /// the rendezvous consults this before any view exists, so a
    /// rotation-incompatible query never triggers a view build it
    /// cannot use.
    /// </summary>
    /// <param name="orderSetMode">The order set the prospective index materialises.</param>
    /// <param name="query">The basic graph pattern.</param>
    /// <returns>The global variable order, or <see langword="null"/> when none exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<Variable>? TryPlanGlobalOrder(ColumnarOrderSetMode orderSetMode, BasicGraphPattern query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if(orderSetMode == ColumnarOrderSetMode.AllSixOrders)
        {
            return query.Variables;
        }

        //Collect, per variable-bearing pattern, its candidate
        //induced variable sequences — one per admissible rotation.
        //Patterns with fewer than two variables impose no ordering
        //and drop out.
        List<List<Variable[]>> constraintCandidates = [];

        foreach(TriplePattern pattern in query.Patterns)
        {
            List<Variable[]> candidates = CandidateSequencesOf(orderSetMode, pattern);

            if(candidates.Count == 0)
            {
                //Every bound set admits at least one rotation; an
                //empty candidate list means the pattern has fewer
                //than two variables and constrains nothing.
                continue;
            }

            constraintCandidates.Add(candidates);
        }

        //Backtrack over the candidate choices, looking for an
        //acyclic union of precedence edges. Choice counts are tiny:
        //only fully-variable patterns have more than one candidate.
        Dictionary<Variable, int> firstOccurrence = [];
        for(int i = 0; i < query.Variables.Count; i++)
        {
            firstOccurrence[query.Variables[i]] = i;
        }

        Variable[]?[] chosen = new Variable[]?[constraintCandidates.Count];
        int depth = 0;
        Span<int> nextCandidate = stackalloc int[constraintCandidates.Count + 1];

        while(true)
        {
            if(depth == constraintCandidates.Count)
            {
                IReadOnlyList<Variable>? order = TryTopologicalOrder(query.Variables, chosen, firstOccurrence);

                if(order is not null)
                {
                    return order;
                }

                //The full combination is cyclic; backtrack.
                depth--;
            }

            while(depth >= 0 && nextCandidate[depth] >= constraintCandidates[depth].Count)
            {
                nextCandidate[depth] = 0;
                chosen[depth] = null;
                depth--;
            }

            if(depth < 0)
            {
                return null;
            }

            chosen[depth] = constraintCandidates[depth][nextCandidate[depth]];
            nextCandidate[depth]++;
            depth++;
        }
    }

    /// <summary>
    /// The pattern's candidate induced variable sequences: for each
    /// materialised permutation whose prefix covers the pattern's
    /// bound set, the pattern's variables in that permutation's
    /// tail order. Empty when the pattern has fewer than two
    /// variables (no ordering constraint).
    /// </summary>
    /// <param name="orderSetMode">The mode whose materialised orders are admissible.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The candidate sequences.</returns>
    private static List<Variable[]> CandidateSequencesOf(ColumnarOrderSetMode orderSetMode, TriplePattern pattern)
    {
        List<Variable[]> candidates = [];
        Span<byte> boundPositions = stackalloc byte[3];
        int boundLength = 0;
        int variableCount = 0;

        for(int rdfPosition = 0; rdfPosition < 3; rdfPosition++)
        {
            if(pattern.At(rdfPosition).IsBound)
            {
                boundPositions[boundLength++] = (byte)rdfPosition;
            }
            else if(pattern.At(rdfPosition).IsVariable)
            {
                variableCount++;
            }
        }

        if(variableCount < 2)
        {
            return candidates;
        }

        for(int permutationIndex = 0; permutationIndex < 6; permutationIndex++)
        {
            if(!ColumnarTripleIndex.IsPermutationInMode(permutationIndex, orderSetMode))
            {
                continue;
            }

            ReadOnlySpan<byte> permutation = ColumnarTripleIndex.PermutationAt(permutationIndex);
            bool prefixCoversBound = true;
            for(int j = 0; j < boundLength && prefixCoversBound; j++)
            {
                prefixCoversBound = boundPositions[..boundLength].IndexOf(permutation[j]) >= 0;
            }

            if(!prefixCoversBound)
            {
                continue;
            }

            Variable[] sequence = new Variable[variableCount];
            for(int j = 0; j < variableCount; j++)
            {
                sequence[j] = pattern.At(permutation[boundLength + j]).Variable;
            }

            candidates.Add(sequence);
        }

        return candidates;
    }

    /// <summary>
    /// Orders the query's variables consistently with every chosen
    /// per-pattern sequence, or returns <see langword="null"/> when
    /// the union of precedence edges is cyclic. Ties break by first
    /// occurrence in the query, keeping the result deterministic.
    /// </summary>
    /// <param name="variables">The query's variables.</param>
    /// <param name="chosenSequences">The chosen per-pattern sequences; trailing entries may be <see langword="null"/> during backtracking.</param>
    /// <param name="firstOccurrence">Each variable's first-occurrence rank, the tie-break.</param>
    /// <returns>The global order, or <see langword="null"/> on a cycle.</returns>
    private static List<Variable>? TryTopologicalOrder(
        IReadOnlyList<Variable> variables,
        Variable[]?[] chosenSequences,
        Dictionary<Variable, int> firstOccurrence)
    {
        Dictionary<Variable, HashSet<Variable>> successors = [];
        Dictionary<Variable, int> indegree = [];

        foreach(Variable variable in variables)
        {
            successors[variable] = [];
            indegree[variable] = 0;
        }

        foreach(Variable[]? sequence in chosenSequences)
        {
            if(sequence is null)
            {
                continue;
            }

            for(int i = 1; i < sequence.Length; i++)
            {
                if(successors[sequence[i - 1]].Add(sequence[i]))
                {
                    indegree[sequence[i]]++;
                }
            }
        }

        //Kahn's algorithm with a deterministic frontier: always
        //emit the ready variable with the smallest first-occurrence
        //rank. Variable counts are small; a linear scan per step is
        //fine.
        List<Variable> ready = [];
        foreach(Variable variable in variables)
        {
            if(indegree[variable] == 0)
            {
                ready.Add(variable);
            }
        }

        List<Variable> order = new(variables.Count);
        while(ready.Count > 0)
        {
            int best = 0;
            for(int i = 1; i < ready.Count; i++)
            {
                if(firstOccurrence[ready[i]] < firstOccurrence[ready[best]])
                {
                    best = i;
                }
            }

            Variable next = ready[best];
            ready.RemoveAt(best);
            order.Add(next);

            foreach(Variable successor in successors[next])
            {
                if(--indegree[successor] == 0)
                {
                    ready.Add(successor);
                }
            }
        }

        return order.Count == variables.Count ? order : null;
    }
}
