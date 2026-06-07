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

    /// <summary>Walks the capability ladder once.</summary>
    /// <returns>The best supported backend.</returns>
    private static ColumnarKernelBackend ComputeBest()
    {
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
