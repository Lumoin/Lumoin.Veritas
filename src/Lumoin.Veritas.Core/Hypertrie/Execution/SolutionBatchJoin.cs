using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The batched hash join over <see cref="SolutionBatch"/> streams:
/// the build side materialises into per-column buffers keyed on
/// the shared variables, the probe side streams batch by batch,
/// and matches emit as batches over the concatenated schema. The
/// binary-join half of the join layer's hybrid — the measured
/// winner on acyclic shapes, with leapfrog keeping the cyclic
/// ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys.</b> Up to two join variables pack into one
/// <c>ulong</c> key (the overwhelmingly common SPARQL case);
/// wider key sets are the router's signal to stay on leapfrog
/// rather than grow a general tuple-key path no measured query
/// needs.
/// </para>
/// <para>
/// <b>Output schema.</b> The build schema, then the probe schema's
/// non-shared variables in probe order. Deterministic for a given
/// pair of inputs.
/// </para>
/// </remarks>
public static class SolutionBatchJoin
{
    /// <summary>The maximum shared-variable count the packed key supports.</summary>
    public const int MaximumJoinVariables = 2;

    /// <summary>
    /// Whether the two schemas are joinable here: at least one
    /// shared variable (no cartesian products) and at most
    /// <see cref="MaximumJoinVariables"/> of them.
    /// </summary>
    /// <param name="buildSchema">The build side's schema.</param>
    /// <param name="probeSchema">The probe side's schema.</param>
    /// <returns><c>true</c> when <see cref="HashJoin"/> accepts the pair.</returns>
    public static bool CanJoin(IReadOnlyList<Variable> buildSchema, IReadOnlyList<Variable> probeSchema)
    {
        ArgumentNullException.ThrowIfNull(buildSchema);
        ArgumentNullException.ThrowIfNull(probeSchema);

        int shared = 0;
        foreach(Variable variable in buildSchema)
        {
            if(Contains(probeSchema, variable))
            {
                shared++;
            }
        }

        return shared is >= 1 and <= MaximumJoinVariables;
    }

    /// <summary>
    /// Joins the two batch streams on their shared variables. The
    /// build side is consumed eagerly on first enumeration; the
    /// probe side streams.
    /// </summary>
    /// <param name="build">The side materialised into the hash table — the smaller side by the caller's estimate.</param>
    /// <param name="buildSchema">The build side's schema.</param>
    /// <param name="probe">The streamed side.</param>
    /// <param name="probeSchema">The probe side's schema.</param>
    /// <returns>The joined batch stream over the concatenated schema.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The schemas share no variable, or more than <see cref="MaximumJoinVariables"/>.</exception>
    public static IEnumerable<SolutionBatch> HashJoin(
        IEnumerable<SolutionBatch> build,
        IReadOnlyList<Variable> buildSchema,
        IEnumerable<SolutionBatch> probe,
        IReadOnlyList<Variable> probeSchema)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(probe);

        if(!CanJoin(buildSchema, probeSchema))
        {
            throw new ArgumentException("The schemas must share one or two variables; other shapes route to leapfrog.", nameof(probeSchema));
        }

        //Join-variable positions on both sides, ordered by build
        //schema appearance; probe-only output columns in probe
        //order.
        List<int> buildKeyColumns = [];
        List<int> probeKeyColumns = [];
        for(int b = 0; b < buildSchema.Count; b++)
        {
            int p = IndexOf(probeSchema, buildSchema[b]);
            if(p >= 0)
            {
                buildKeyColumns.Add(b);
                probeKeyColumns.Add(p);
            }
        }

        List<int> probeCarryColumns = [];
        List<Variable> outputSchema = [.. buildSchema];
        for(int p = 0; p < probeSchema.Count; p++)
        {
            if(!Contains(buildSchema, probeSchema[p]))
            {
                probeCarryColumns.Add(p);
                outputSchema.Add(probeSchema[p]);
            }
        }

        return JoinCore(build, buildSchema, probe, buildKeyColumns, probeKeyColumns, probeCarryColumns, outputSchema);
    }

    /// <summary>The iterator behind <see cref="HashJoin"/>: build the table, stream the probes.</summary>
    /// <param name="build">The build stream.</param>
    /// <param name="buildSchema">The build schema.</param>
    /// <param name="probe">The probe stream.</param>
    /// <param name="buildKeyColumns">The key columns on the build side.</param>
    /// <param name="probeKeyColumns">The key columns on the probe side, parallel to <paramref name="buildKeyColumns"/>.</param>
    /// <param name="probeCarryColumns">The probe columns carried to the output.</param>
    /// <param name="outputSchema">The concatenated output schema.</param>
    /// <returns>The joined batches.</returns>
    private static IEnumerable<SolutionBatch> JoinCore(
        IEnumerable<SolutionBatch> build,
        IReadOnlyList<Variable> buildSchema,
        IEnumerable<SolutionBatch> probe,
        List<int> buildKeyColumns,
        List<int> probeKeyColumns,
        List<int> probeCarryColumns,
        List<Variable> outputSchema)
    {
        int probeKey0 = probeKeyColumns[0];
        int probeKey1 = probeKeyColumns.Count > 1 ? probeKeyColumns[1] : -1;
        int buildColumnCount = buildSchema.Count;

        SolutionBatchHashTable table = SolutionBatchHashTable.Build(
            build, buildColumnCount, buildKeyColumns[0], buildKeyColumns.Count > 1 ? buildKeyColumns[1] : -1);

        //Stream the probes; chain-walk each match into output
        //batches. Probe keys precompute contiguously per batch so
        //the pack is one tight pass the JIT can keep in registers.
        SolutionBatch output = new(outputSchema);
        int outputRows = 0;
        JoinKey[] probeKeys = new JoinKey[SolutionBatch.BatchLength];

        foreach(SolutionBatch batch in probe)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(probeKey0);
            ReadOnlySpan<uint> key1Column = probeKey1 >= 0 ? batch.ColumnOf(probeKey1) : default;

            if(probeKey1 >= 0)
            {
                for(int row = 0; row < count; row++)
                {
                    probeKeys[row] = JoinKey.Pack(key0Column[row], key1Column[row]);
                }
            }
            else
            {
                for(int row = 0; row < count; row++)
                {
                    probeKeys[row] = JoinKey.Pack(key0Column[row], 0);
                }
            }

            for(int row = 0; row < count; row++)
            {
                int matchRow = table.FirstMatch(probeKeys[row]);

                while(matchRow >= 0)
                {
                    for(int column = 0; column < buildColumnCount; column++)
                    {
                        output.ColumnSpan(column)[outputRows] = table.ValueAt(column, matchRow);
                    }

                    for(int carry = 0; carry < probeCarryColumns.Count; carry++)
                    {
                        output.ColumnSpan(buildColumnCount + carry)[outputRows] = batch.ColumnOf(probeCarryColumns[carry])[row];
                    }

                    outputRows++;

                    if(outputRows == SolutionBatch.BatchLength)
                    {
                        output.SetCount(outputRows);

                        yield return output;

                        output = new SolutionBatch(outputSchema);
                        outputRows = 0;
                    }

                    matchRow = table.NextMatch(matchRow);
                }
            }
        }

        if(outputRows > 0)
        {
            output.SetCount(outputRows);

            yield return output;
        }
    }

    /// <summary>Whether the schema contains the variable.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="variable">The variable.</param>
    /// <returns><c>true</c> when present.</returns>
    private static bool Contains(IReadOnlyList<Variable> schema, Variable variable)
    {
        return IndexOf(schema, variable) >= 0;
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
