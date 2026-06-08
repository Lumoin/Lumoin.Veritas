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
/// <para>
/// Column placement is owned by the parent <see cref="FactorizedBatch"/>:
/// <see cref="KeyValues"/> are positional against the batch's key columns
/// and each branch's packed values are positional against that branch's
/// column list. A branch with zero columns still carries a row count — its
/// rows are empty tuples whose only contribution is a multiplicity factor.
/// </para>
/// <para>
/// A branch may itself be a <em>nested</em> single-level
/// <see cref="FactorizedBatch"/> rather than a flat tuple union — the second
/// factorisation level a chain join needs, where the next join's variable
/// lives inside this branch and must be grouped before it can be joined
/// without flattening. <see cref="NestedBranches"/> is <see langword="null"/>
/// when every branch is flat (the star case); otherwise a non-<see langword="null"/>
/// entry replaces that branch's flat <see cref="BranchValues"/>/<see cref="BranchRowCounts"/>
/// with the sub-batch, whose schema is positional against the parent's branch
/// column list.
/// </para>
/// </remarks>
public sealed class FactorizedGroup
{
    /// <summary>The key tuple shared by every flat row of this group, one value per key column.</summary>
    public uint[] KeyValues { get; }

    /// <summary>Each branch's value tuples, packed row-major with the branch's column count as stride; indexed by branch then by <c>row * stride + column</c>. Ignored for a branch that is nested.</summary>
    public uint[][] BranchValues { get; }

    /// <summary>Each branch's row count — the number of value tuples in the union, kept explicit so a zero-column branch still carries its multiplicity. Ignored for a branch that is nested.</summary>
    public int[] BranchRowCounts { get; }

    /// <summary>The nested sub-batch for each branch, parallel to <see cref="BranchValues"/>, or <see langword="null"/> when every branch is flat. A non-<see langword="null"/> entry makes that branch a nested single-level batch instead of a flat tuple union.</summary>
    public FactorizedBatch?[]? NestedBranches { get; }

    /// <summary>Constructs a group over a key tuple and its branches' packed value tuples, optionally with some branches nested.</summary>
    /// <param name="keyValues">The key tuple, one value per key column.</param>
    /// <param name="branchValues">Each branch's packed value tuples, stride equal to that branch's column count; empty for a nested branch.</param>
    /// <param name="branchRowCounts">Each branch's row count, parallel to <paramref name="branchValues"/>; ignored for a nested branch.</param>
    /// <param name="nestedBranches">The nested sub-batch per branch, parallel to <paramref name="branchValues"/>, or <see langword="null"/> when every branch is flat.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The branch arrays differ in length.</exception>
    public FactorizedGroup(uint[] keyValues, uint[][] branchValues, int[] branchRowCounts, FactorizedBatch?[]? nestedBranches = null)
    {
        ArgumentNullException.ThrowIfNull(keyValues);
        ArgumentNullException.ThrowIfNull(branchValues);
        ArgumentNullException.ThrowIfNull(branchRowCounts);

        if(branchValues.Length != branchRowCounts.Length)
        {
            throw new ArgumentException("Each branch must carry both its packed values and its row count.", nameof(branchRowCounts));
        }

        if(nestedBranches is not null && nestedBranches.Length != branchValues.Length)
        {
            throw new ArgumentException("The nested-branch array must run parallel to the branches.", nameof(nestedBranches));
        }

        KeyValues = keyValues;
        BranchValues = branchValues;
        BranchRowCounts = branchRowCounts;
        NestedBranches = nestedBranches;
    }

    /// <summary>The nested sub-batch for a branch, or <see langword="null"/> when that branch is flat.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The nested sub-batch, or <see langword="null"/>.</returns>
    public FactorizedBatch? NestedAt(int branch)
    {
        return NestedBranches?[branch];
    }

    /// <summary>The number of flat rows a branch stands for: its nested sub-batch's flat-row count when nested, else its stored row count.</summary>
    /// <param name="branch">The branch index.</param>
    /// <returns>The branch's row count.</returns>
    public long BranchFlatRowCount(int branch)
    {
        FactorizedBatch? nested = NestedAt(branch);

        return nested is null ? BranchRowCounts[branch] : nested.FlatRowCount;
    }

    /// <summary>The number of flat rows this group stands for: the product of its branch row counts, or one when the group is key-only.</summary>
    public long FlatRowCount
    {
        get
        {
            long product = 1;
            for(int branch = 0; branch < BranchValues.Length; branch++)
            {
                product *= BranchFlatRowCount(branch);
            }

            return product;
        }
    }

    /// <summary>The number of stored value tuples across the branches: the sum the factorisation pays in place of <see cref="FlatRowCount"/>, counting a nested branch's own stored tuples.</summary>
    public long FactorizedTupleCount
    {
        get
        {
            long sum = 0;
            for(int branch = 0; branch < BranchValues.Length; branch++)
            {
                FactorizedBatch? nested = NestedAt(branch);
                sum += nested is null ? BranchRowCounts[branch] : nested.FactorizedTupleCount;
            }

            return sum;
        }
    }
}
