using System.Numerics;

namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// Selector over the <see cref="CoordinateTransformKernel"/> backends for
/// WGS 84 → Web Mercator, following the house kernel-selection shape: each
/// property resolves once at first access and is a stable delegate
/// reference for the process lifetime.
/// </summary>
/// <remarks>
/// <para>
/// This selector does not silently prefer SIMD for <see cref="Default"/>.
/// The scalar kernel is the exact reference the surface's answers are
/// pinned against, so it stays the default; callers opt into
/// <see cref="Vectorized"/> explicitly when they want batch throughput
/// (including under WebAssembly packed SIMD) and accept its documented
/// accuracy envelope. That deliberate choice is the whole point of
/// exposing both rather than auto-selecting.
/// </para>
/// </remarks>
public static class CoordinateTransformKernelSelection
{
    /// <summary>
    /// The default kernel — the exact scalar WGS 84 → Web Mercator. Chosen
    /// as the default because it is the correctness reference the surface's
    /// answers are pinned against.
    /// </summary>
    public static CoordinateTransformKernel Default { get; } =
        ScalarWgs84ToWebMercator.GetTransform();

    /// <summary>The exact scalar kernel, by name; identical reference to <see cref="Default"/>.</summary>
    public static CoordinateTransformKernel Scalar { get; } =
        ScalarWgs84ToWebMercator.GetTransform();

    /// <summary>
    /// The vectorized kernel: projects a batch per instruction over
    /// <see cref="Vector{T}"/> of <see cref="double"/>, lowering to AVX2 /
    /// AVX-512 / NEON / WASM packed SIMD per host. It internally falls back
    /// to the scalar kernel for inputs below one vector width, for the
    /// trailing remainder, and on hosts without SIMD lowering, so it is safe
    /// to hold unconditionally; <see cref="IsVectorizationHardwareAccelerated"/>
    /// reports whether it actually vectorizes here.
    /// </summary>
    public static CoordinateTransformKernel Vectorized { get; } =
        VectorizedWgs84ToWebMercator.GetTransform();

    /// <summary>
    /// True when <see cref="Vector{T}"/> of <see cref="double"/> lowers to
    /// hardware SIMD on this host; false means <see cref="Vectorized"/> runs
    /// the scalar shim per lane and offers no throughput gain.
    /// </summary>
    public static bool IsVectorizationHardwareAccelerated => Vector.IsHardwareAccelerated;
}
