using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The streaming <c>DISTINCT</c>: a seen-set over CANONICAL solution keys (binding-order-insensitive value
/// equality — two rows binding the same variables to the same terms are one solution regardless of binding
/// order), emitting first occurrences immediately — the same first-occurrence rule as the materialised path,
/// but incremental, so a window above it terminates early (the dedup-trap shape streams correctly: a cap
/// above counts post-DISTINCT survivors by construction). Memory equals the materialised dedup set.
/// </summary>
internal sealed class DistinctCursor : SolutionCursor
{
    private readonly SolutionCursor input;

    private readonly HashSet<SparqlSolution> seen;

    /// <summary>Constructs the cursor over its input child.</summary>
    /// <param name="input">The input cursor.</param>
    public DistinctCursor(SolutionCursor input)
    {
        this.input = input;
        seen = new HashSet<SparqlSolution>(CanonicalSolutionComparer.Instance);
    }

    /// <inheritdoc/>
    public override SparqlSolution Current => input.Current;

    /// <inheritdoc/>
    public override bool IsOrderPreserving => true;

    /// <inheritdoc/>
    public override async ValueTask<bool> MoveNextAsync(CancellationToken cancellationToken)
    {
        while(await input.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            if(seen.Add(input.Current))
            {
                RowsProduced++;

                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public override async ValueTask ResetAsync(SparqlSolution preBinding)
    {
        await input.ResetAsync(preBinding).ConfigureAwait(false);
        seen.Clear();
        RowsProduced = 0;
    }

    /// <inheritdoc/>
    public override ValueTask DisposeAsync()
    {
        seen.Clear();

        return ValueTask.CompletedTask;
    }

    /// <summary>Binding-order-insensitive value equality over solutions: equal binding counts and every binding of one matched by variable and term value in the other; the hash is an order-insensitive combination of the per-binding hashes.</summary>
    private sealed class CanonicalSolutionComparer : IEqualityComparer<SparqlSolution>
    {
        /// <summary>The shared comparer instance.</summary>
        public static CanonicalSolutionComparer Instance { get; } = new();

        /// <summary>Returns whether two solutions bind the same variables to the same terms.</summary>
        /// <param name="x">The first solution.</param>
        /// <param name="y">The second solution.</param>
        /// <returns><see langword="true"/> when the solutions are value-equal as mappings.</returns>
        public bool Equals(SparqlSolution? x, SparqlSolution? y)
        {
            if(ReferenceEquals(x, y))
            {
                return true;
            }

            if(x is null || y is null || x.Bindings.Count != y.Bindings.Count)
            {
                return false;
            }

            foreach(SparqlBinding binding in x.Bindings)
            {
                if(!y.TryGetValue(binding.Variable, out RdfTerm value) || !value.Equals(binding.Value))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Computes the order-insensitive mapping hash of a solution.</summary>
        /// <param name="solution">The solution.</param>
        /// <returns>The hash code.</returns>
        public int GetHashCode(SparqlSolution solution)
        {
            int hash = 0;
            foreach(SparqlBinding binding in solution.Bindings)
            {
                hash ^= System.HashCode.Combine(binding.Variable, binding.Value);
            }

            return hash;
        }
    }
}
