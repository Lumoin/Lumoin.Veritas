using System;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The flat-packed branch storage of a <see cref="FactorizedGroup"/>: every
/// branch's value tuples concatenated into one arena-allocated values run,
/// with a per-branch start offset, row count, and stride (its column count)
/// packed into one arena-allocated metadata run. A group holds one values run
/// rather than one per branch, and branch <c>b</c>'s tuple at row <c>r</c>
/// reads at <c>start[b] + r·stride[b]</c>. A zero-column branch keeps its row
/// count as a multiplicity factor and contributes no values.
/// </summary>
/// <remarks>
/// Both runs live in the <see cref="FactorizedArena"/> they were allocated
/// from — the one explicit lifetime every factorised buffer of a query
/// shares — so this storage is valid only until that arena is disposed.
/// <see cref="Append"/> and <see cref="WithCleared"/> allocate fresh runs
/// and abandon the superseded ones inside the arena; a bump arena reclaims
/// nothing per run, everything at its single disposal.
/// </remarks>
public sealed class FactorizedBranches
{
    /// <summary>The empty branch set — a group that is key-only, or whose every branch is nested. Backed by empty slices, so it is valid under any arena.</summary>
    public static FactorizedBranches Empty { get; } = new(ArenaSlice.Empty, ArenaSlice.Empty, 0);

    /// <summary>Every branch's value tuples concatenated row-major; branch <c>b</c> occupies <c>[start(b), start(b) + rowCount(b)·stride(b))</c>. Valid until the allocating arena is disposed.</summary>
    private readonly ArenaSlice values;

    /// <summary>The per-branch metadata, three runs of <see cref="Count"/> values each: the start offsets, then the row counts, then the strides.</summary>
    private readonly ArenaSlice meta;

    /// <summary>Constructs the storage over its packed runs; the caller owns the layout invariant that the metadata starts match the prefix sums of <c>rowCount·stride</c>.</summary>
    /// <param name="values">The concatenated value tuples.</param>
    /// <param name="meta">The packed per-branch metadata: starts, row counts, strides.</param>
    /// <param name="count">The branch count.</param>
    private FactorizedBranches(ArenaSlice values, ArenaSlice meta, int count)
    {
        this.values = values;
        this.meta = meta;
        Count = count;
    }

    /// <summary>The number of branches.</summary>
    public int Count { get; }

    /// <summary>A branch's start offset into the values run.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The start offset.</returns>
    private int StartOf(int branch)
    {
        return (int)meta[branch];
    }

    /// <summary>A branch's row count — the number of value tuples in its union.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The row count.</returns>
    public int RowCountOf(int branch)
    {
        return (int)meta[Count + branch];
    }

    /// <summary>A branch's stride — the column count of its tuples.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The stride.</returns>
    public int StrideOf(int branch)
    {
        return (int)meta[(2 * Count) + branch];
    }

    /// <summary>One value of a branch's tuple.</summary>
    /// <param name="branch">The branch index.</param>
    /// <param name="row">The tuple's row within the branch.</param>
    /// <param name="column">The column within the tuple, below <see cref="StrideOf"/>.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int branch, int row, int column)
    {
        return values[StartOf(branch) + (row * StrideOf(branch)) + column];
    }

    /// <summary>A branch's values run as a writable span, for a producer filling storage made by <see cref="Allocate"/> in place.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The branch's run, row-major with the branch's stride.</returns>
    public Span<uint> BranchSpan(int branch)
    {
        return values.Span.Slice(StartOf(branch), RowCountOf(branch) * StrideOf(branch));
    }

    /// <summary>
    /// Allocates storage for branches of known shape with unfilled values — the
    /// writer-style builder for a producer that knows each branch's row count
    /// upfront and fills the runs in place through <see cref="BranchSpan"/>,
    /// with no transient per-branch array between it and the arena.
    /// </summary>
    /// <param name="rowCounts">Each branch's row count.</param>
    /// <param name="strides">Each branch's stride, parallel to <paramref name="rowCounts"/>.</param>
    /// <param name="arena">The arena the runs are allocated from; the storage is valid until it is disposed.</param>
    /// <returns>The branch storage, its values awaiting the producer's fill.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arena"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The branch spans differ in length.</exception>
    public static FactorizedBranches Allocate(ReadOnlySpan<int> rowCounts, ReadOnlySpan<int> strides, FactorizedArena arena)
    {
        ArgumentNullException.ThrowIfNull(arena);

        if(rowCounts.Length != strides.Length)
        {
            throw new ArgumentException("The branch spans must run parallel.", nameof(strides));
        }

        int branchCount = rowCounts.Length;
        ArenaSlice meta = arena.Allocate(3 * branchCount);
        int total = 0;
        for(int branch = 0; branch < branchCount; branch++)
        {
            meta.Span[branch] = (uint)total;
            meta.Span[branchCount + branch] = (uint)rowCounts[branch];
            meta.Span[(2 * branchCount) + branch] = (uint)strides[branch];
            total += rowCounts[branch] * strides[branch];
        }

        return new FactorizedBranches(arena.Allocate(total), meta, branchCount);
    }

    /// <summary>
    /// Builds the storage from per-branch packed arrays, concatenating them into
    /// one arena-allocated values run; the packed inputs are copied, not
    /// retained. Each packed array is row-major with the matching stride, of
    /// length <c>rowCounts[b]·strides[b]</c> (empty for a zero-column branch).
    /// </summary>
    /// <param name="packedPerBranch">Each branch's packed value tuples.</param>
    /// <param name="rowCounts">Each branch's row count, parallel to <paramref name="packedPerBranch"/>.</param>
    /// <param name="strides">Each branch's stride, parallel to <paramref name="packedPerBranch"/>.</param>
    /// <param name="arena">The arena the runs are allocated from; the storage is valid until it is disposed.</param>
    /// <returns>The flat-packed branch storage.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The branch arrays differ in length.</exception>
    public static FactorizedBranches Of(uint[][] packedPerBranch, int[] rowCounts, int[] strides, FactorizedArena arena)
    {
        ArgumentNullException.ThrowIfNull(packedPerBranch);
        ArgumentNullException.ThrowIfNull(rowCounts);
        ArgumentNullException.ThrowIfNull(strides);

        if(packedPerBranch.Length != rowCounts.Length || packedPerBranch.Length != strides.Length)
        {
            throw new ArgumentException("The branch arrays must run parallel.", nameof(rowCounts));
        }

        FactorizedBranches branches = Allocate(rowCounts, strides, arena);
        for(int branch = 0; branch < packedPerBranch.Length; branch++)
        {
            packedPerBranch[branch].AsSpan(0, rowCounts[branch] * strides[branch]).CopyTo(branches.BranchSpan(branch));
        }

        return branches;
    }

    /// <summary>
    /// Returns a copy extended by one more branch — the star step that attaches a
    /// pattern's per-key matches as a fresh branch without flattening the product.
    /// The copy's runs are fresh from the arena; the superseded ones are
    /// abandoned there until the arena's disposal.
    /// </summary>
    /// <param name="packed">The new branch's packed value tuples, row-major with <paramref name="stride"/>.</param>
    /// <param name="rowCount">The new branch's row count.</param>
    /// <param name="stride">The new branch's stride.</param>
    /// <param name="arena">The arena the extended runs are allocated from.</param>
    /// <returns>The extended branch storage.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public FactorizedBranches Append(uint[] packed, int rowCount, int stride, FactorizedArena arena)
    {
        ArgumentNullException.ThrowIfNull(packed);
        ArgumentNullException.ThrowIfNull(arena);

        int branchCount = Count;
        int appended = rowCount * stride;

        ArenaSlice newMeta = arena.Allocate(3 * (branchCount + 1));
        for(int branch = 0; branch < branchCount; branch++)
        {
            newMeta.Span[branch] = (uint)StartOf(branch);
            newMeta.Span[branchCount + 1 + branch] = (uint)RowCountOf(branch);
            newMeta.Span[(2 * (branchCount + 1)) + branch] = (uint)StrideOf(branch);
        }

        newMeta.Span[branchCount] = (uint)values.Length;
        newMeta.Span[branchCount + 1 + branchCount] = (uint)rowCount;
        newMeta.Span[(2 * (branchCount + 1)) + branchCount] = (uint)stride;

        ArenaSlice newValues = arena.Allocate(values.Length + appended);
        values.Span.CopyTo(newValues.Span);
        packed.AsSpan(0, appended).CopyTo(newValues.Span[values.Length..]);

        return new FactorizedBranches(newValues, newMeta, branchCount + 1);
    }

    /// <summary>
    /// Returns a copy with one branch cleared to a zero-row, zero-stride branch —
    /// the chain step where that branch is regrouped into a nested sub-batch and
    /// so carries no flat values in the parent. The copy's runs are fresh from
    /// the arena; the superseded ones are abandoned there until the arena's
    /// disposal.
    /// </summary>
    /// <param name="branch">The branch to clear.</param>
    /// <param name="arena">The arena the compacted runs are allocated from.</param>
    /// <returns>The branch storage with the branch emptied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="arena"/> is <see langword="null"/>.</exception>
    public FactorizedBranches WithCleared(int branch, FactorizedArena arena)
    {
        ArgumentNullException.ThrowIfNull(arena);

        int branchCount = Count;
        ArenaSlice newMeta = arena.Allocate(3 * branchCount);
        int total = 0;
        for(int b = 0; b < branchCount; b++)
        {
            int rowCount = b == branch ? 0 : RowCountOf(b);
            int stride = b == branch ? 0 : StrideOf(b);
            newMeta.Span[b] = (uint)total;
            newMeta.Span[branchCount + b] = (uint)rowCount;
            newMeta.Span[(2 * branchCount) + b] = (uint)stride;
            total += rowCount * stride;
        }

        ArenaSlice newValues = arena.Allocate(total);
        FactorizedBranches cleared = new(newValues, newMeta, branchCount);
        for(int b = 0; b < branchCount; b++)
        {
            if(b == branch)
            {
                continue;
            }

            values.Span.Slice(StartOf(b), RowCountOf(b) * StrideOf(b)).CopyTo(cleared.BranchSpan(b));
        }

        return cleared;
    }
}
