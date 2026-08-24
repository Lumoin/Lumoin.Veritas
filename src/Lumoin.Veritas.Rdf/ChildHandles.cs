using System;
using System.Runtime.CompilerServices;

namespace Lumoin.Veritas.Rdf;

/// <summary>
/// An algebra's view of its node's outgoing children during a
/// <see cref="GraphKFold"/> reduction.
/// </summary>
/// <remarks>
/// <para>
/// An algebra receives one <see cref="ChildHandles{TResult}"/> per invocation.
/// The handle exposes:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       The number of outgoing children via <see cref="Count"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       The status of each child's folded value via
///       <see cref="IsComputed(int)"/>. A child becomes computed only after
///       the algebra <c>yield return</c>s a <see cref="ForceRequest.Force(int)"/>
///       and the driver resumes the algebra.
///     </description>
///   </item>
///   <item>
///     <description>
///       The value of a computed child via <see cref="Get(int)"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       The algebra's own result-writing channel via
///       <see cref="SetResult(TResult)"/>. The algebra must call this
///       exactly once before completing its iterator.
///     </description>
///   </item>
/// </list>
/// <para>
/// The struct is a <c>readonly struct</c>, not a <c>ref struct</c>, so it
/// can be stored as a field on an iterator state machine. This is required
/// because iterator methods compile to a state machine class that captures
/// parameters as fields, and <c>ref struct</c> cannot be a field on a
/// non-ref type. The struct is sixteen bytes on 64-bit platforms (one
/// reference and two ints), passed by value without allocation.
/// </para>
/// <para>
/// Every instance borrows from a single <see cref="ReductionState{TResult}"/>
/// owned by the driver. The base-index and count identify this node's slice
/// of the state's per-child arrays. Algebras never interact with
/// <see cref="ReductionState{TResult}"/> directly.
/// </para>
/// </remarks>
/// <typeparam name="TResult">The fold's result type.</typeparam>
public readonly struct ChildHandles<TResult>: IEquatable<ChildHandles<TResult>>
{
    private readonly ReductionState<TResult> state;
    private readonly int baseIndex;
    private readonly int count;
    private readonly int thisNodeIndex;

    internal ChildHandles(ReductionState<TResult> state, int baseIndex, int count, int thisNodeIndex)
    {
        this.state = state;
        this.baseIndex = baseIndex;
        this.count = count;
        this.thisNodeIndex = thisNodeIndex;
    }

    /// <summary>
    /// Gets the number of outgoing children this algebra can reference.
    /// </summary>
    public int Count => count;

    /// <summary>
    /// Returns <c>true</c> if the child at <paramref name="childIndex"/>
    /// has been computed and its value can be read via <see cref="Get(int)"/>.
    /// </summary>
    /// <param name="childIndex">A zero-based child index in <c>[0, Count)</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="childIndex"/> is outside the valid range.
    /// </exception>
    public bool IsComputed(int childIndex)
    {
        ValidateIndex(childIndex);
        return state.GetChildStatus(baseIndex + childIndex) == ChildStatus.Computed;
    }

    /// <summary>
    /// Returns the folded result of the child at <paramref name="childIndex"/>.
    /// </summary>
    /// <remarks>
    /// Only valid when <see cref="IsComputed(int)"/> returns <c>true</c>.
    /// Calling before the child has been forced throws.
    /// </remarks>
    /// <param name="childIndex">A zero-based child index in <c>[0, Count)</c>.</param>
    /// <returns>The child's folded result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="childIndex"/> is outside the valid range.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The child has not yet been forced.
    /// </exception>
    public TResult Get(int childIndex)
    {
        ValidateIndex(childIndex);
        return state.GetChildValue(baseIndex + childIndex);
    }

    /// <summary>
    /// Writes this algebra's folded result. Must be called exactly once
    /// before the iterator completes.
    /// </summary>
    /// <param name="result">The folded result for this node.</param>
    public void SetResult(TResult result)
    {
        state.SetNodeResult(thisNodeIndex, result);
    }

    private void ValidateIndex(int childIndex)
    {
        if((uint)childIndex >= (uint)count)
        {
            throw new ArgumentOutOfRangeException(nameof(childIndex));
        }
    }

    /// <summary>
    /// Returns <c>true</c> if this handle references the same underlying
    /// reduction state slice as <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// Equality compares the referenced <see cref="ReductionState{TResult}"/>
    /// by reference and the three integer fields by value. Two default-valued
    /// handles (no state, zero indices) are equal.
    /// </remarks>
    /// <param name="other">The handle to compare against.</param>
    /// <returns><c>true</c> if the handles are equivalent.</returns>
    public bool Equals(ChildHandles<TResult> other)
    {
        return ReferenceEquals(state, other.state)
            && baseIndex == other.baseIndex
            && count == other.count
            && thisNodeIndex == other.thisNodeIndex;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is ChildHandles<TResult> other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            state is null ? 0 : RuntimeHelpers.GetHashCode(state),
            baseIndex,
            count,
            thisNodeIndex);
    }

    /// <summary>
    /// Returns <c>true</c> when the two handles reference the same slice.
    /// </summary>
    /// <param name="left">The left-hand handle.</param>
    /// <param name="right">The right-hand handle.</param>
    /// <returns><c>true</c> if equivalent.</returns>
    public static bool operator ==(ChildHandles<TResult> left, ChildHandles<TResult> right)
        => left.Equals(right);

    /// <summary>
    /// Returns <c>true</c> when the two handles reference different slices.
    /// </summary>
    /// <param name="left">The left-hand handle.</param>
    /// <param name="right">The right-hand handle.</param>
    /// <returns><c>true</c> if not equivalent.</returns>
    public static bool operator !=(ChildHandles<TResult> left, ChildHandles<TResult> right)
        => !left.Equals(right);
}
