using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Hypertrie.Query;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The factorising counterpart of <see cref="SolutionBatchJoin"/>: it joins
/// two batch streams on their shared variables but emits a
/// <see cref="FactorizedBatch"/> rather than the flat row product. Grouping
/// by the join key, the build-side and probe-side matches for a key vary
/// independently of each other, so each key's group keeps them as two
/// branches — the build matches and the probe matches — instead of their
/// cross product. On a fan-out join the stored size is the sum of the two
/// branch sizes where the flat join would materialise their product.
/// </summary>
/// <remarks>
/// <para>
/// <b>Schema.</b> Identical to <see cref="SolutionBatchJoin"/>: the build
/// schema, then the probe schema's non-shared variables in probe order. The
/// shared variables become the groups' key columns; the build-only columns
/// are branch zero, the probe-only columns branch one.
/// </para>
/// <para>
/// <b>Keys.</b> One or two shared variables fold into a <see cref="JoinKey"/>
/// exactly as the flat join packs them; a pair sharing no variable, or more
/// than two, is the router's signal to stay on leapfrog — the same boundary
/// the flat join draws.
/// </para>
/// <para>
/// <b>Distinctness.</b> The branch unions preserve multiplicity: a scan over
/// a single pattern yields distinct tuples, so within a key the build (resp.
/// probe) matches are distinct and flattening reproduces the flat join's
/// rows one-for-one — the differential oracle that proves the equivalence.
/// </para>
/// </remarks>
public static class FactorizedBatchJoin
{
    /// <summary>
    /// Joins the two batch streams on their shared variables into a single
    /// factorised batch. The build side is grouped by key on first
    /// enumeration; the probe side is then folded into the matching groups.
    /// </summary>
    /// <param name="build">The side grouped into the key table.</param>
    /// <param name="buildSchema">The build side's schema.</param>
    /// <param name="probe">The side folded into the groups.</param>
    /// <param name="probeSchema">The probe side's schema.</param>
    /// <returns>The factorised join result over the concatenated schema.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The schemas share no variable, or more than <see cref="SolutionBatchJoin.MaximumJoinVariables"/>.</exception>
    public static FactorizedBatch Join(
        IEnumerable<SolutionBatch> build,
        IReadOnlyList<Variable> buildSchema,
        IEnumerable<SolutionBatch> probe,
        IReadOnlyList<Variable> probeSchema)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(buildSchema);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(probeSchema);

        if(!SolutionBatchJoin.CanJoin(buildSchema, probeSchema))
        {
            throw new ArgumentException("The schemas must share one or two variables; other shapes route to leapfrog.", nameof(probeSchema));
        }

        //Join-variable positions on both sides, ordered by build schema
        //appearance; the build key positions double as the output key
        //columns because the output schema opens with the build schema.
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

        //Build-only columns are branch zero; their output index equals their
        //build index. Probe-only columns are branch one, appended after the
        //build schema in probe order.
        List<int> buildCarryColumns = [];
        for(int b = 0; b < buildSchema.Count; b++)
        {
            if(!buildKeyColumns.Contains(b))
            {
                buildCarryColumns.Add(b);
            }
        }

        List<int> probeCarryColumns = [];
        List<Variable> outputSchema = [.. buildSchema];
        List<int> probeBranchColumns = [];
        for(int p = 0; p < probeSchema.Count; p++)
        {
            if(!Contains(buildSchema, probeSchema[p]))
            {
                probeBranchColumns.Add(outputSchema.Count);
                probeCarryColumns.Add(p);
                outputSchema.Add(probeSchema[p]);
            }
        }

        Dictionary<JoinKey, GroupBuilder> groups = BuildGroups(build, buildKeyColumns, buildCarryColumns);
        FoldProbe(groups, probe, probeKeyColumns, probeCarryColumns);

        int[] keyColumns = [.. buildKeyColumns];
        int[][] branchColumns = [[.. buildCarryColumns], [.. probeBranchColumns]];
        List<FactorizedGroup> emitted = EmitGroups(groups, buildKeyColumns.Count, buildCarryColumns.Count, probeCarryColumns.Count);

        return new FactorizedBatch(outputSchema, keyColumns, branchColumns, emitted);
    }

    /// <summary>Groups the build stream by key, packing each row's build-only columns into its group's build branch.</summary>
    /// <param name="build">The build stream.</param>
    /// <param name="buildKeyColumns">The build-side key column indices, one or two.</param>
    /// <param name="buildCarryColumns">The build-only column indices.</param>
    /// <returns>The key-to-group table seeded with the build matches.</returns>
    private static Dictionary<JoinKey, GroupBuilder> BuildGroups(
        IEnumerable<SolutionBatch> build,
        List<int> buildKeyColumns,
        List<int> buildCarryColumns)
    {
        int key0 = buildKeyColumns[0];
        int key1 = buildKeyColumns.Count > 1 ? buildKeyColumns[1] : -1;

        Dictionary<JoinKey, GroupBuilder> groups = [];
        foreach(SolutionBatch batch in build)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(key0);
            ReadOnlySpan<uint> key1Column = key1 >= 0 ? batch.ColumnOf(key1) : default;

            for(int row = 0; row < count; row++)
            {
                uint keyValue0 = key0Column[row];
                uint keyValue1 = key1 >= 0 ? key1Column[row] : 0;
                JoinKey key = JoinKey.Pack(keyValue0, keyValue1);

                if(!groups.TryGetValue(key, out GroupBuilder? group))
                {
                    group = new GroupBuilder(keyValue0, keyValue1);
                    groups[key] = group;
                }

                foreach(int column in buildCarryColumns)
                {
                    group.BuildValues.Add(batch.ColumnOf(column)[row]);
                }

                group.BuildRows++;
            }
        }

        return groups;
    }

    /// <summary>Folds the probe stream into the existing groups, packing each matching row's probe-only columns into its group's probe branch.</summary>
    /// <param name="groups">The key-to-group table from the build pass.</param>
    /// <param name="probe">The probe stream.</param>
    /// <param name="probeKeyColumns">The probe-side key column indices, parallel to the build's.</param>
    /// <param name="probeCarryColumns">The probe-only column indices.</param>
    private static void FoldProbe(
        Dictionary<JoinKey, GroupBuilder> groups,
        IEnumerable<SolutionBatch> probe,
        List<int> probeKeyColumns,
        List<int> probeCarryColumns)
    {
        int key0 = probeKeyColumns[0];
        int key1 = probeKeyColumns.Count > 1 ? probeKeyColumns[1] : -1;

        foreach(SolutionBatch batch in probe)
        {
            int count = batch.Count;
            ReadOnlySpan<uint> key0Column = batch.ColumnOf(key0);
            ReadOnlySpan<uint> key1Column = key1 >= 0 ? batch.ColumnOf(key1) : default;

            for(int row = 0; row < count; row++)
            {
                JoinKey key = JoinKey.Pack(key0Column[row], key1 >= 0 ? key1Column[row] : 0);

                if(!groups.TryGetValue(key, out GroupBuilder? group))
                {
                    continue;
                }

                foreach(int column in probeCarryColumns)
                {
                    group.ProbeValues.Add(batch.ColumnOf(column)[row]);
                }

                group.ProbeRows++;
            }
        }
    }

    /// <summary>Materialises the groups that drew at least one probe match into immutable factorised groups.</summary>
    /// <param name="groups">The accumulated groups.</param>
    /// <param name="keyCount">The key column count.</param>
    /// <param name="buildCarryCount">The build-only column count.</param>
    /// <param name="probeCarryCount">The probe-only column count.</param>
    /// <returns>The emitted groups; a group with no probe match cannot reach a join row and is dropped.</returns>
    private static List<FactorizedGroup> EmitGroups(
        Dictionary<JoinKey, GroupBuilder> groups,
        int keyCount,
        int buildCarryCount,
        int probeCarryCount)
    {
        List<FactorizedGroup> emitted = new(groups.Count);
        foreach(GroupBuilder group in groups.Values)
        {
            if(group.ProbeRows == 0)
            {
                continue;
            }

            uint[] keyValues = keyCount > 1 ? [group.Key0, group.Key1] : [group.Key0];
            uint[][] branchValues = [PackOf(group.BuildValues, buildCarryCount), PackOf(group.ProbeValues, probeCarryCount)];
            int[] branchRowCounts = [group.BuildRows, group.ProbeRows];

            emitted.Add(new FactorizedGroup(keyValues, branchValues, branchRowCounts));
        }

        return emitted;
    }

    /// <summary>Copies a branch's accumulated values into a packed array, or the shared empty array when the branch has no columns.</summary>
    /// <param name="values">The accumulated branch values.</param>
    /// <param name="columnCount">The branch's column count.</param>
    /// <returns>The packed values, row-major with the column count as stride.</returns>
    private static uint[] PackOf(List<uint> values, int columnCount)
    {
        return columnCount == 0 ? [] : [.. values];
    }

    /// <summary>Whether the schema contains the variable.</summary>
    /// <param name="schema">The schema.</param>
    /// <param name="variable">The variable.</param>
    /// <returns><see langword="true"/> when present.</returns>
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

    /// <summary>
    /// The mutable accumulator for one key's group while the join runs: the
    /// key tuple, plus the build and probe branches growing as their sides are
    /// scanned. Sealed into an immutable <see cref="FactorizedGroup"/> at emit.
    /// </summary>
    private sealed class GroupBuilder
    {
        /// <summary>The first key value, packed into the group's <see cref="JoinKey"/>.</summary>
        public uint Key0 { get; }

        /// <summary>The second key value, or zero for a single-variable key.</summary>
        public uint Key1 { get; }

        /// <summary>The build branch's packed values, row-major with the build-only column count as stride.</summary>
        public List<uint> BuildValues { get; } = [];

        /// <summary>The build branch's row count, tracked apart from <see cref="BuildValues"/> so a zero-column branch still carries its multiplicity.</summary>
        public int BuildRows { get; set; }

        /// <summary>The probe branch's packed values, row-major with the probe-only column count as stride.</summary>
        public List<uint> ProbeValues { get; } = [];

        /// <summary>The probe branch's row count; a group with none drew no join match and is dropped.</summary>
        public int ProbeRows { get; set; }

        /// <summary>Constructs an empty group for a key tuple.</summary>
        /// <param name="key0">The first key value.</param>
        /// <param name="key1">The second key value, or zero.</param>
        public GroupBuilder(uint key0, uint key1)
        {
            Key0 = key0;
            Key1 = key1;
        }
    }
}
