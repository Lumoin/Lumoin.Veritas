using System;

namespace Lumoin.Veritas.Core.Hypertrie.Execution;

/// <summary>
/// One product node of a <see cref="FactorizedBatch"/>: a single key
/// tuple paired with one independent branch per child variable group.
/// The flat rows this group stands for are the cross product
/// <c>{key} × branch₀ × branch₁ × …</c> — each branch a union of value
/// tuples that, given the key, varies independently of the others. The
/// compression is exactly that independence: the group stores the SUM of
/// the branch sizes but stands for their PRODUCT of rows.
/// </summary>
/// <remarks>
/// Column placement is owned by the parent <see cref="FactorizedBatch"/>:
/// <see cref="KeyValues"/> are positional against the batch's key columns
/// and each branch's packed values are positional against that branch's
/// column list. A branch with zero columns still carries a row count — its
/// rows are empty tuples whose only contribution is a multiplicity factor.
/// </remarks>
public sealed class FactorizedGroup
{
    /// <summary>The key tuple shared by every flat row of this group, one value per key column.</summary>
    public uint[] KeyValues { get; }

    /// <summary>Each branch's value tuples, packed row-major with the branch's column count as stride; indexed by branch then by <c>row * stride + column</c>.</summary>
    public uint[][] BranchValues { get; }

    /// <summary>Each branch's row count — the number of value tuples in the union, kept explicit so a zero-column branch still carries its multiplicity.</summary>
    public int[] BranchRowCounts { get; }

    /// <summary>Constructs a group over a key tuple and its branches' packed value tuples.</summary>
    /// <param name="keyValues">The key tuple, one value per key column.</param>
    /// <param name="branchValues">Each branch's packed value tuples, stride equal to that branch's column count.</param>
    /// <param name="branchRowCounts">Each branch's row count, parallel to <paramref name="branchValues"/>.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="branchValues"/> and <paramref name="branchRowCounts"/> differ in length.</exception>
    public FactorizedGroup(uint[] keyValues, uint[][] branchValues, int[] branchRowCounts)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        ArgumentNullException.ThrowIfNull(branchValues);
        ArgumentNullException.ThrowIfNull(branchRowCounts);

        if(branchValues.Length != branchRowCounts.Length)
        {
            throw new ArgumentException("Each branch must carry both its packed values and its row count.", nameof(branchRowCounts));
        }

        KeyValues = keyValues;
        BranchValues = branchValues;
        BranchRowCounts = branchRowCounts;
    }

    /// <summary>The number of flat rows this group stands for: the product of its branch row counts, or one when the group is key-only.</summary>
    public long FlatRowCount
    {
        get
        {
            long product = 1;
            foreach(int count in BranchRowCounts)
            {
                product *= count;
            }

            return product;
        }
    }

    /// <summary>The number of stored value tuples across the branches: the sum the factorisation pays in place of <see cref="FlatRowCount"/>.</summary>
    public long FactorizedTupleCount
    {
        get
        {
            long sum = 0;
            foreach(int count in BranchRowCounts)
            {
                sum += count;
            }

            return sum;
        }
    }
}
