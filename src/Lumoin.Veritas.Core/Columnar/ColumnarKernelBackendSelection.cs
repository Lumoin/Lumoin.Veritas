using Lumoin.Veritas.Core.Execution;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// Picks the highest-capability codec backend the current machine
/// supports. The selection runs once and the result is cached for
/// the process lifetime; consumers reading
/// <see cref="ColumnarKernelBackend.Default"/> repeatedly do not
/// pay a per-call detection cost.
/// </summary>
/// <remarks>
/// <para>
/// Capability ordering (highest to lowest):
/// </para>
/// <list type="number">
///   <item><description>512-bit vectors (AVX-512-class) — <see cref="ColumnarVector512Backend"/>.</description></item>
///   <item><description>256-bit vectors (AVX2-class) — <see cref="ColumnarVector256Backend"/>.</description></item>
///   <item><description>WebAssembly PackedSimd — <see cref="ColumnarWasmPackedSimdBackend"/>; activates under WASM hosts implementing the 128-bit SIMD proposal.</description></item>
///   <item><description>128-bit vectors (SSE/NEON-class) — <see cref="ColumnarVector128Backend"/>.</description></item>
///   <item><description>Portable scalar — <see cref="ColumnarPortableBackend"/>, always available.</description></item>
/// </list>
/// </remarks>
internal static class ColumnarKernelBackendSelection
{
    private static ColumnarKernelBackend Cached { get; } = ComputeBest();

    /// <summary>Returns the cached best-available backend for this process.</summary>
    /// <returns>The backend.</returns>
    public static ColumnarKernelBackend SelectBest() => Cached;

    /// <summary>
    /// Returns the best supported backend whose vector width does not
    /// exceed <paramref name="cap"/> — the policy ceiling applied to the
    /// static capability ladder. <see cref="KernelWidthCap.Auto"/> is
    /// uncapped and returns the cached best.
    /// </summary>
    /// <param name="cap">The width ceiling.</param>
    /// <returns>The selected backend.</returns>
    public static ColumnarKernelBackend SelectForCap(KernelWidthCap cap)
    {
        return cap == KernelWidthCap.Auto ? Cached : ComputeForCap(cap);
    }

    /// <summary>Walks the capability ladder, skipping rungs wider than the cap.</summary>
    /// <param name="cap">The width ceiling; never <see cref="KernelWidthCap.Auto"/>.</param>
    /// <returns>The best supported backend at or below the cap.</returns>
    private static ColumnarKernelBackend ComputeForCap(KernelWidthCap cap)
    {
        bool allow256 = cap == KernelWidthCap.Bits256;
        bool allow128 = cap is KernelWidthCap.Bits256 or KernelWidthCap.Bits128;

        if(allow256 && ColumnarVector256Backend.IsSupported)
        {
            return ColumnarVector256Backend.Backend;
        }

        if(allow128 && ColumnarWasmPackedSimdBackend.IsSupported)
        {
            return ColumnarWasmPackedSimdBackend.Backend;
        }

        if(allow128 && ColumnarVector128Backend.IsSupported)
        {
            return ColumnarVector128Backend.Backend;
        }

        return ColumnarPortableBackend.Backend;
    }

    /// <summary>Walks the capability ladder once.</summary>
    /// <returns>The best supported backend.</returns>
    private static ColumnarKernelBackend ComputeBest()
    {
        if(ColumnarVector512Backend.IsSupported)
        {
            return ColumnarVector512Backend.Backend;
        }

        if(ColumnarVector256Backend.IsSupported)
        {
            return ColumnarVector256Backend.Backend;
        }

        if(ColumnarWasmPackedSimdBackend.IsSupported)
        {
            return ColumnarWasmPackedSimdBackend.Backend;
        }

        if(ColumnarVector128Backend.IsSupported)
        {
            return ColumnarVector128Backend.Backend;
        }

        return ColumnarPortableBackend.Backend;
    }
}
