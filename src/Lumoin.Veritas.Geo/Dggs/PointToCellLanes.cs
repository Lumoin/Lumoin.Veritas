using System;
using System.Collections.Generic;
using System.Runtime.Intrinsics;

namespace Lumoin.Veritas.Geo.Dggs;

/// <summary>
/// Width abstraction over <see cref="Vector128{T}"/>/<see cref="Vector256{T}"/>/<see cref="Vector512{T}"/>
/// of <see cref="double"/> for the batch point-to-cell kernel core (<see cref="PointToCellBatchCore"/>):
/// the delicate lane-parallel floating-point staging is written and reviewed once, generically, and each
/// hardware rung instantiates it at its own lane width. The JIT specializes generic instantiations over
/// structs, so every member below compiles down to the direct vector instruction with no dispatch.
/// </summary>
/// <remarks>
/// <para>
/// Only IEEE-754 correctly-rounded operations are exposed: add, subtract, multiply, divide and square
/// root produce bit-identical results lane-wise to the equivalent scalar <see cref="double"/> operations
/// on every ISA (x64 SSE/AVX/AVX-512, AArch64 NEON, WASM SIMD128). Deliberately absent: fused
/// multiply-add (contracts two roundings into one — diverges from the scalar reference), vector
/// min/max (x86 <c>MINPD</c>/<c>MAXPD</c> NaN and signed-zero semantics differ per ISA — branches are
/// mirrored with <see cref="Select"/> over explicit comparisons instead), and reciprocal/rsqrt
/// approximations (not correctly rounded at all).
/// </para>
/// <para>
/// Comparison members return lane masks (all-bits-set for true lanes); <see cref="Select"/> blends per
/// lane on such a mask, and <see cref="IsLaneSet"/> reads one mask lane back as a <see cref="bool"/> for
/// the per-lane scalar stages interleaved between the vector stages.
/// </para>
/// </remarks>
internal interface IPointToCellLanes<TSelf>
    where TSelf : struct, IPointToCellLanes<TSelf>
{
    /// <summary>Number of <see cref="double"/> lanes this width carries.</summary>
    static abstract int LaneCount { get; }

    /// <summary>Fills every lane with <paramref name="value"/>.</summary>
    static abstract TSelf Broadcast(double value);

    /// <summary>Loads <see cref="LaneCount"/> consecutive values from <paramref name="source"/>.</summary>
    static abstract TSelf FromSpan(ReadOnlySpan<double> source);

    /// <summary>Stores all lanes into <paramref name="destination"/> (length at least <see cref="LaneCount"/>).</summary>
    static abstract void CopyTo(TSelf vector, Span<double> destination);

    /// <summary>Reads a single lane of a comparison mask: <see langword="true"/> when the lane's mask bits are set.</summary>
    static abstract bool IsLaneSet(TSelf mask, int lane);

    /// <summary>Lane-wise IEEE-754 addition.</summary>
    static abstract TSelf operator +(TSelf left, TSelf right);

    /// <summary>Lane-wise IEEE-754 subtraction.</summary>
    static abstract TSelf operator -(TSelf left, TSelf right);

    /// <summary>Lane-wise IEEE-754 multiplication.</summary>
    static abstract TSelf operator *(TSelf left, TSelf right);

    /// <summary>Lane-wise IEEE-754 division.</summary>
    static abstract TSelf operator /(TSelf left, TSelf right);

    /// <summary>Lane-wise IEEE-754 correctly-rounded square root.</summary>
    static abstract TSelf SquareRoot(TSelf value);

    /// <summary>Lane-wise absolute value (sign-bit clear; exact).</summary>
    static abstract TSelf Abs(TSelf value);

    /// <summary>Lane mask of <c>left &lt; right</c>.</summary>
    static abstract TSelf LessThan(TSelf left, TSelf right);

    /// <summary>Lane mask of <c>left &gt; right</c>.</summary>
    static abstract TSelf GreaterThan(TSelf left, TSelf right);

    /// <summary>Lane mask of <c>left &gt;= right</c>.</summary>
    static abstract TSelf GreaterThanOrEqual(TSelf left, TSelf right);

    /// <summary>Lane mask of <c>left == right</c>.</summary>
    static abstract TSelf EqualTo(TSelf left, TSelf right);

    /// <summary>Per-lane blend: mask lanes set take <paramref name="whenTrue"/>, others <paramref name="whenFalse"/>.</summary>
    static abstract TSelf Select(TSelf mask, TSelf whenTrue, TSelf whenFalse);
}

/// <summary>
/// Two-lane (<see cref="Vector128{T}"/>) instantiation of <see cref="IPointToCellLanes{TSelf}"/>: the
/// AArch64 NEON and WASM SIMD128 rung width (and SSE2 on x64, which is how this core is exercised by the
/// bit-identity gates on x64 hosts regardless of the ISA-named rungs' own support).
/// </summary>
internal readonly struct PointToCellLanes128 : IPointToCellLanes<PointToCellLanes128>
{
    /// <summary>The wrapped vector value.</summary>
    private Vector128<double> Value { get; }

    /// <summary>Wraps a raw vector.</summary>
    private PointToCellLanes128(Vector128<double> value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public static int LaneCount => Vector128<double>.Count;

    /// <inheritdoc/>
    public static PointToCellLanes128 Broadcast(double value)
    {
        return new PointToCellLanes128(Vector128.Create(value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 FromSpan(ReadOnlySpan<double> source)
    {
        return new PointToCellLanes128(Vector128.Create(source));
    }

    /// <inheritdoc/>
    public static void CopyTo(PointToCellLanes128 vector, Span<double> destination)
    {
        vector.Value.CopyTo(destination);
    }

    /// <inheritdoc/>
    public static bool IsLaneSet(PointToCellLanes128 mask, int lane)
    {
        return mask.Value.AsUInt64().GetElement(lane) != 0UL;
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 operator +(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(left.Value + right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 operator -(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(left.Value - right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 operator *(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(left.Value * right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 operator /(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(left.Value / right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 SquareRoot(PointToCellLanes128 value)
    {
        return new PointToCellLanes128(Vector128.Sqrt(value.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 Abs(PointToCellLanes128 value)
    {
        return new PointToCellLanes128(Vector128.Abs(value.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 LessThan(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(Vector128.LessThan(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 GreaterThan(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(Vector128.GreaterThan(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 GreaterThanOrEqual(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(Vector128.GreaterThanOrEqual(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 EqualTo(PointToCellLanes128 left, PointToCellLanes128 right)
    {
        return new PointToCellLanes128(Vector128.Equals(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes128 Select(PointToCellLanes128 mask, PointToCellLanes128 whenTrue, PointToCellLanes128 whenFalse)
    {
        return new PointToCellLanes128(Vector128.ConditionalSelect(mask.Value, whenTrue.Value, whenFalse.Value));
    }
}

/// <summary>
/// Four-lane (<see cref="Vector256{T}"/>) instantiation of <see cref="IPointToCellLanes{TSelf}"/>: the
/// AVX2 rung width.
/// </summary>
internal readonly struct PointToCellLanes256 : IPointToCellLanes<PointToCellLanes256>
{
    /// <summary>The wrapped vector value.</summary>
    private Vector256<double> Value { get; }

    /// <summary>Wraps a raw vector.</summary>
    private PointToCellLanes256(Vector256<double> value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public static int LaneCount => Vector256<double>.Count;

    /// <inheritdoc/>
    public static PointToCellLanes256 Broadcast(double value)
    {
        return new PointToCellLanes256(Vector256.Create(value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 FromSpan(ReadOnlySpan<double> source)
    {
        return new PointToCellLanes256(Vector256.Create(source));
    }

    /// <inheritdoc/>
    public static void CopyTo(PointToCellLanes256 vector, Span<double> destination)
    {
        vector.Value.CopyTo(destination);
    }

    /// <inheritdoc/>
    public static bool IsLaneSet(PointToCellLanes256 mask, int lane)
    {
        return mask.Value.AsUInt64().GetElement(lane) != 0UL;
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 operator +(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(left.Value + right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 operator -(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(left.Value - right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 operator *(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(left.Value * right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 operator /(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(left.Value / right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 SquareRoot(PointToCellLanes256 value)
    {
        return new PointToCellLanes256(Vector256.Sqrt(value.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 Abs(PointToCellLanes256 value)
    {
        return new PointToCellLanes256(Vector256.Abs(value.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 LessThan(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(Vector256.LessThan(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 GreaterThan(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(Vector256.GreaterThan(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 GreaterThanOrEqual(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(Vector256.GreaterThanOrEqual(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 EqualTo(PointToCellLanes256 left, PointToCellLanes256 right)
    {
        return new PointToCellLanes256(Vector256.Equals(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes256 Select(PointToCellLanes256 mask, PointToCellLanes256 whenTrue, PointToCellLanes256 whenFalse)
    {
        return new PointToCellLanes256(Vector256.ConditionalSelect(mask.Value, whenTrue.Value, whenFalse.Value));
    }
}

/// <summary>
/// Eight-lane (<see cref="Vector512{T}"/>) instantiation of <see cref="IPointToCellLanes{TSelf}"/>: the
/// AVX-512 rung width.
/// </summary>
internal readonly struct PointToCellLanes512 : IPointToCellLanes<PointToCellLanes512>
{
    /// <summary>The wrapped vector value.</summary>
    private Vector512<double> Value { get; }

    /// <summary>Wraps a raw vector.</summary>
    private PointToCellLanes512(Vector512<double> value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    public static int LaneCount => Vector512<double>.Count;

    /// <inheritdoc/>
    public static PointToCellLanes512 Broadcast(double value)
    {
        return new PointToCellLanes512(Vector512.Create(value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 FromSpan(ReadOnlySpan<double> source)
    {
        return new PointToCellLanes512(Vector512.Create(source));
    }

    /// <inheritdoc/>
    public static void CopyTo(PointToCellLanes512 vector, Span<double> destination)
    {
        vector.Value.CopyTo(destination);
    }

    /// <inheritdoc/>
    public static bool IsLaneSet(PointToCellLanes512 mask, int lane)
    {
        return mask.Value.AsUInt64().GetElement(lane) != 0UL;
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 operator +(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(left.Value + right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 operator -(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(left.Value - right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 operator *(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(left.Value * right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 operator /(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(left.Value / right.Value);
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 SquareRoot(PointToCellLanes512 value)
    {
        return new PointToCellLanes512(Vector512.Sqrt(value.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 Abs(PointToCellLanes512 value)
    {
        return new PointToCellLanes512(Vector512.Abs(value.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 LessThan(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(Vector512.LessThan(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 GreaterThan(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(Vector512.GreaterThan(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 GreaterThanOrEqual(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(Vector512.GreaterThanOrEqual(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 EqualTo(PointToCellLanes512 left, PointToCellLanes512 right)
    {
        return new PointToCellLanes512(Vector512.Equals(left.Value, right.Value));
    }

    /// <inheritdoc/>
    public static PointToCellLanes512 Select(PointToCellLanes512 mask, PointToCellLanes512 whenTrue, PointToCellLanes512 whenFalse)
    {
        return new PointToCellLanes512(Vector512.ConditionalSelect(mask.Value, whenTrue.Value, whenFalse.Value));
    }
}
