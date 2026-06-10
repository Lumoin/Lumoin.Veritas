using System;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// The flat-packed branch storage of a <see cref="FactorizedGroup"/>: every
/// branch's value tuples concatenated into one <c>values</c> array, with a
/// per-branch start offset, row count, and stride (its column count). A group
/// holds one values array rather than one per branch, and branch <c>b</c>'s
/// tuple at row <c>r</c> reads at <c>start[b] + r·stride[b]</c>. A zero-column
/// branch keeps its row count as a multiplicity factor and contributes no
/// values. Building from per-branch packed arrays concatenates once; the
/// transient per-branch arrays are not retained.
/// </summary>
public sealed class FactorizedBranches
{
    /// <summary>The empty branch set — a group that is key-only, or whose every branch is nested.</summary>
    public static FactorizedBranches Empty { get; } = new([], [], [], []);

    /// <summary>Every branch's value tuples concatenated row-major; branch <c>b</c> occupies <c>[starts[b], starts[b] + rowCounts[b]·strides[b])</c>.</summary>
    private readonly uint[] values;

    /// <summary>Each branch's start offset into <see cref="values"/>, parallel to <see cref="rowCounts"/>.</summary>
    private readonly int[] starts;

    /// <summary>Each branch's row count — kept explicit so a zero-column branch still carries its multiplicity.</summary>
    private readonly int[] rowCounts;

    /// <summary>Each branch's stride, the column count of its tuples.</summary>
    private readonly int[] strides;

    /// <summary>Constructs the storage over its packed arrays; the caller owns the layout invariant that <paramref name="starts"/> matches the prefix sums of <c>rowCounts·strides</c>.</summary>
    /// <param name="values">The concatenated value tuples.</param>
    /// <param name="starts">Each branch's start offset into <paramref name="values"/>.</param>
    /// <param name="rowCounts">Each branch's row count.</param>
    /// <param name="strides">Each branch's stride.</param>
    private FactorizedBranches(uint[] values, int[] starts, int[] rowCounts, int[] strides)
    {
        this.values = values;
        this.starts = starts;
        this.rowCounts = rowCounts;
        this.strides = strides;
    }

    /// <summary>The number of branches.</summary>
    public int Count => rowCounts.Length;

    /// <summary>A branch's row count — the number of value tuples in its union.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The row count.</returns>
    public int RowCountOf(int branch)
    {
        return rowCounts[branch];
    }

    /// <summary>A branch's stride — the column count of its tuples.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The stride.</returns>
    public int StrideOf(int branch)
    {
        return strides[branch];
    }

    /// <summary>One value of a branch's tuple.</summary>
    /// <param name="branch">The branch index.</param>
    /// <param name="row">The tuple's row within the branch.</param>
    /// <param name="column">The column within the tuple, below <see cref="StrideOf"/>.</param>
    /// <returns>The value.</returns>
    public uint ValueAt(int branch, int row, int column)
    {
        return values[starts[branch] + (row * strides[branch]) + column];
    }

    /// <summary>
    /// Builds the storage from per-branch packed arrays, concatenating them into
    /// one values array; the packed inputs are copied, not retained. Each packed
    /// array is row-major with the matching stride, of length
    /// <c>rowCounts[b]·strides[b]</c> (empty for a zero-column branch). The
    /// <paramref name="rowCounts"/> and <paramref name="strides"/> arrays are
    /// retained by reference.
    /// </summary>
    /// <param name="packedPerBranch">Each branch's packed value tuples.</param>
    /// <param name="rowCounts">Each branch's row count, parallel to <paramref name="packedPerBranch"/>.</param>
    /// <param name="strides">Each branch's stride, parallel to <paramref name="packedPerBranch"/>.</param>
    /// <returns>The flat-packed branch storage.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The branch arrays differ in length.</exception>
    public static FactorizedBranches Of(uint[][] packedPerBranch, int[] rowCounts, int[] strides)
    {
        ArgumentNullException.ThrowIfNull(packedPerBranch);
        ArgumentNullException.ThrowIfNull(rowCounts);
        ArgumentNullException.ThrowIfNull(strides);

        if(packedPerBranch.Length != rowCounts.Length || packedPerBranch.Length != strides.Length)
        {
            throw new ArgumentException("The branch arrays must run parallel.", nameof(rowCounts));
        }

        int branchCount = packedPerBranch.Length;
        int[] starts = new int[branchCount];
        int total = 0;
        for(int branch = 0; branch < branchCount; branch++)
        {
            starts[branch] = total;
            total += rowCounts[branch] * strides[branch];
        }

        uint[] values = total == 0 ? [] : new uint[total];
        for(int branch = 0; branch < branchCount; branch++)
        {
            Array.Copy(packedPerBranch[branch], 0, values, starts[branch], rowCounts[branch] * strides[branch]);
        }

        return new FactorizedBranches(values, starts, rowCounts, strides);
    }

    /// <summary>
    /// Returns a copy extended by one more branch — the star step that attaches a
    /// pattern's per-key matches as a fresh branch without flattening the product.
    /// </summary>
    /// <param name="packed">The new branch's packed value tuples, row-major with <paramref name="stride"/>.</param>
    /// <param name="rowCount">The new branch's row count.</param>
    /// <param name="stride">The new branch's stride.</param>
    /// <returns>The extended branch storage.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="packed"/> is <see langword="null"/>.</exception>
    public FactorizedBranches Append(uint[] packed, int rowCount, int stride)
    {
        ArgumentNullException.ThrowIfNull(packed);

        int branchCount = rowCounts.Length;
        int[] newStarts = new int[branchCount + 1];
        int[] newRowCounts = new int[branchCount + 1];
        int[] newStrides = new int[branchCount + 1];
        Array.Copy(starts, newStarts, branchCount);
        Array.Copy(rowCounts, newRowCounts, branchCount);
        Array.Copy(strides, newStrides, branchCount);

        int appended = rowCount * stride;
        newStarts[branchCount] = values.Length;
        newRowCounts[branchCount] = rowCount;
        newStrides[branchCount] = stride;

        uint[] newValues = new uint[values.Length + appended];
        Array.Copy(values, newValues, values.Length);
        Array.Copy(packed, 0, newValues, values.Length, appended);

        return new FactorizedBranches(newValues, newStarts, newRowCounts, newStrides);
    }

    /// <summary>
    /// Returns a copy with one branch cleared to a zero-row, zero-stride branch —
    /// the chain step where that branch is regrouped into a nested sub-batch and
    /// so carries no flat values in the parent.
    /// </summary>
    /// <param name="branch">The branch to clear.</param>
    /// <returns>The branch storage with the branch emptied.</returns>
    public FactorizedBranches WithCleared(int branch)
    {
        int branchCount = rowCounts.Length;
        int[] newRowCounts = (int[])rowCounts.Clone();
        int[] newStrides = (int[])strides.Clone();
        newRowCounts[branch] = 0;
        newStrides[branch] = 0;

        int[] newStarts = new int[branchCount];
        int total = 0;
        for(int b = 0; b < branchCount; b++)
        {
            newStarts[b] = total;
            total += newRowCounts[b] * newStrides[b];
        }

        uint[] newValues = total == 0 ? [] : new uint[total];
        for(int b = 0; b < branchCount; b++)
        {
            if(b == branch)
            {
                continue;
            }

            Array.Copy(values, starts[b], newValues, newStarts[b], rowCounts[b] * strides[b]);
        }

        return new FactorizedBranches(newValues, newStarts, newRowCounts, newStrides);
    }
}
