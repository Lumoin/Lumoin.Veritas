using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The columnar semijoin: reduces a target relation to the rows
/// whose value on the variables shared with a probe relation occurs
/// in the probe. The reducing primitive behind Yannakakis' two-pass
/// full reduction — a semijoin never grows its target (the result is
/// always a subset of the target's rows), so a chain of them strips
/// every dangling tuple before the binary-join pipeline runs, which
/// is what bounds an acyclic query's intermediates by its input and
/// output sizes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys.</b> The shared variables fold into a <see cref="JoinKey"/>
/// exactly as <see cref="SolutionBatchJoin"/> packs them — one or two
/// values into a 64-bit key. A target and probe sharing no variable,
/// or more than two, is the router's signal to stay on leapfrog, the
/// same boundary the binary join draws.
/// </para>
/// <para>
/// <b>Shape.</b> The probe is drained once into a key set; the target
/// streams, and surviving rows accumulate into batches over the
/// target's schema, full except the last. The target's row order is
/// preserved.
/// </para>
/// </remarks>
public static class SolutionBatchSemijoin
{
    /// <summary>
    /// Reduces <paramref name="target"/> to the rows whose value on
    /// the variables shared with <paramref name="probe"/> matches
    /// some probe row. The target's schema and surviving-row order are
    /// preserved; only non-matching rows are dropped.
    /// </summary>
    /// <param name="target">The relation being reduced; its batches are read, not mutated.</param>
    /// <param name="targetSchema">The target's schema, positional against its columns.</param>
    /// <param name="probe">The relation supplying the matching keys.</param>
    /// <param name="probeSchema">The probe's schema, positional against its columns.</param>
    /// <returns>The reduced target batches over <paramref name="targetSchema"/>, full except the last.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The schemas share no variable, or more than <see cref="SolutionBatchJoin.MaximumJoinVariables"/>.</exception>
    public static IReadOnlyList<SolutionBatch> Reduce(
        IReadOnlyList<SolutionBatch> target,
        IReadOnlyList<Variable> targetSchema,
        IReadOnlyList<SolutionBatch> probe,
        IReadOnlyList<Variable> probeSchema)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetSchema);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(probeSchema);

        List<int> targetKeyColumns = [];
        List<int> probeKeyColumns = [];
        for(int t = 0; t < targetSchema.Count; t++)
        {
            int p = IndexOf(probeSchema, targetSchema[t]);
            if(p >= 0)
            {
                targetKeyColumns.Add(t);
                probeKeyColumns.Add(p);
            }
        }

        if(targetKeyColumns.Count is < 1 or > SolutionBatchJoin.MaximumJoinVariables)
        {
            throw new ArgumentException("The relations must share one or two variables; other shapes route to leapfrog.", nameof(probeSchema));
        }

        HashSet<JoinKey> keys = BuildKeys(probe, probeKeyColumns);

        return Filter(target, targetSchema, targetKeyColumns, keys);
    }

    /// <summary>Drains the probe into the set of its rows' packed shared-variable keys.</summary>
    /// <param name="probe">The probe relation.</param>
    /// <param name="probeKeyColumns">The probe's shared-variable column indices, one or two.</param>
    /// <returns>The distinct probe keys.</returns>
    private static HashSet<JoinKey> BuildKeys(IReadOnlyList<SolutionBatch> probe, List<int> probeKeyColumns)
    {
        int key0 = probeKeyColumns[0];
        int key1 = probeKeyColumns.Count > 1 ? probeKeyColumns[1] : -1;

        HashSet<JoinKey> keys = [];
        foreach(SolutionBatch batch in probe)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(key0);
            ReadOnlySpan<uint> key1Column = key1 >= 0 ? batch.ColumnOf(key1) : default;

            for(int row = 0; row < count; row++)
            {
                keys.Add(JoinKey.Pack(key0Column[row], key1 >= 0 ? key1Column[row] : 0));
            }
        }

        return keys;
    }

    /// <summary>Streams the target, keeping rows whose packed key is present, into batches over the target schema.</summary>
    /// <param name="target">The target relation.</param>
    /// <param name="targetSchema">The target's schema.</param>
    /// <param name="targetKeyColumns">The target's shared-variable column indices, one or two.</param>
    /// <param name="keys">The probe key set.</param>
    /// <returns>The surviving target rows as batches, full except the last.</returns>
    private static List<SolutionBatch> Filter(
        IReadOnlyList<SolutionBatch> target,
        IReadOnlyList<Variable> targetSchema,
        List<int> targetKeyColumns,
        HashSet<JoinKey> keys)
    {
        int key0 = targetKeyColumns[0];
        int key1 = targetKeyColumns.Count > 1 ? targetKeyColumns[1] : -1;
        int columnCount = targetSchema.Count;

        List<SolutionBatch> reduced = [];
        SolutionBatch output = new(targetSchema);
        int outputRows = 0;

        foreach(SolutionBatch batch in target)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(key0);
            ReadOnlySpan<uint> key1Column = key1 >= 0 ? batch.ColumnOf(key1) : default;

            for(int row = 0; row < count; row++)
            {
                JoinKey key = JoinKey.Pack(key0Column[row], key1 >= 0 ? key1Column[row] : 0);
                if(!keys.Contains(key))
                {
                    continue;
                }

                for(int column = 0; column < columnCount; column++)
                {
                    output.ColumnSpan(column)[outputRows] = batch.ColumnOf(column)[row];
                }

                outputRows++;

                if(outputRows == SolutionBatch.BatchLength)
                {
                    output.SetCount(outputRows);
                    reduced.Add(output);

                    output = new SolutionBatch(targetSchema);
                    outputRows = 0;
                }
            }
        }

        if(outputRows > 0)
        {
            output.SetCount(outputRows);
            reduced.Add(output);
        }

        return reduced;
    }

    /// <summary>The variable's position in the schema, or −1.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="variable">The variable.</param>
    /// <returns>The position, or −1 when absent.</returns>
    private static int IndexOf(IReadOnlyList<Variable> schema, Variable variable)
    {
        for(int i = 0; i < schema.Count; i++)
        {
            if(schema[i] == variable)
            {
                return i;
            }
        }

        return -1;
    }
}
