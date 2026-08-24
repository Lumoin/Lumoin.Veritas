using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Sparql.Execution.Streaming;

/// <summary>
/// The binary cursors' drained build side: the right child's rows materialised once (binding-independent —
/// retained across <see cref="SolutionCursor.ResetAsync"/> re-arms as MATERIALISED ROWS, never a live
/// enumerator), plus a hash index over the variables EVERY build row binds (one or two, first-appearance
/// order) when that keying exists. A probe row that binds the whole key probes the index and VERIFIES full
/// compatibility on each candidate (equal keys capture only the keyed variables; residual shared variables
/// still decide), so the index is purely a candidate filter — the emitted multiset is identical to a full
/// compatibility scan on every input shape, mirroring the materialised join's answers with the
/// hash-eligibility heuristic softened into per-candidate verification.
/// </summary>
internal sealed class MaterialisedBuild
{
    /// <summary>Constructs the build; called by <see cref="DrainAsync"/> only.</summary>
    /// <param name="rows">The drained build rows.</param>
    /// <param name="index">The hash index over <paramref name="key"/>, or <see langword="null"/> when no sound keying exists.</param>
    /// <param name="key">The index's key variables; empty when <paramref name="index"/> is <see langword="null"/>.</param>
    private MaterialisedBuild(IReadOnlyList<SparqlSolution> rows, SolutionHashJoinIndex? index, SparqlVariable[] key)
    {
        Rows = rows;
        Index = index;
        Key = key;
    }

    /// <summary>The drained build rows, in the child's emission order.</summary>
    public IReadOnlyList<SparqlSolution> Rows { get; }

    /// <summary>The hash index over <see cref="Key"/>, or <see langword="null"/> when every probe scans.</summary>
    public SolutionHashJoinIndex? Index { get; }

    /// <summary>The index's key variables (bound by EVERY build row); empty without an index.</summary>
    public SparqlVariable[] Key { get; }

    /// <summary>Whether a probe row binds the whole index key (the precondition for probing the index instead of scanning).</summary>
    /// <param name="probe">The probe row.</param>
    /// <returns><see langword="true"/> when the index applies to this probe.</returns>
    public bool BindsKey(SparqlSolution probe)
    {
        if(Index is null)
        {
            return false;
        }

        foreach(SparqlVariable variable in Key)
        {
            if(!probe.TryGetValue(variable, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Drains a child cursor to exhaustion and builds the keyed form.</summary>
    /// <param name="source">The build-side child cursor; drained fully (its sources close at exhaustion).</param>
    /// <param name="cancellationToken">A token that aborts the drain.</param>
    /// <returns>The materialised build.</returns>
    public static async ValueTask<MaterialisedBuild> DrainAsync(SolutionCursor source, CancellationToken cancellationToken)
    {
        List<SparqlSolution> rows = [];
        while(await source.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(source.Current);
        }

        SparqlVariable[] key = AlwaysBoundKey(rows);
        SolutionHashJoinIndex? index = key.Length > 0 ? SolutionHashJoinIndex.Build(rows, key) : null;

        return new MaterialisedBuild(rows, index, key);
    }

    /// <summary>Selects up to two variables every build row binds, in the first row's binding order — the index keying; empty when the build is empty or no variable is bound throughout.</summary>
    /// <param name="rows">The build rows.</param>
    /// <returns>The key variables.</returns>
    private static SparqlVariable[] AlwaysBoundKey(List<SparqlSolution> rows)
    {
        if(rows.Count == 0)
        {
            return [];
        }

        List<SparqlVariable> key = [];
        foreach(SparqlBinding binding in rows[0].Bindings)
        {
            bool everywhere = true;
            for(int row = 1; row < rows.Count && everywhere; row++)
            {
                everywhere = rows[row].TryGetValue(binding.Variable, out _);
            }

            if(everywhere)
            {
                key.Add(binding.Variable);
                if(key.Count == 2)
                {
                    break;
                }
            }
        }

        return [.. key];
    }
}
