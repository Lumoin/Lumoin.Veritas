using System.Runtime.Intrinsics.Wasm;

namespace Lumoin.Veritas.Core.Columnar;

/// <summary>
/// The WebAssembly PackedSimd codec backend: activates under WASM
/// hosts that implement the 128-bit SIMD proposal. The kernels are
/// <see cref="ColumnarVector128Backend"/>'s — the portable 128-bit
/// vector API lowers to PackedSimd instructions on such hosts — so
/// this backend exists to claim its seat in the capability ladder
/// under PackedSimd's own support gate.
/// </summary>
public static class ColumnarWasmPackedSimdBackend
{
    /// <summary>Whether the WebAssembly 128-bit SIMD proposal is available on this host.</summary>
    public static bool IsSupported => PackedSimd.IsSupported;

    /// <summary>The PackedSimd backend bundle.</summary>
    /// <exception cref="System.PlatformNotSupportedException">PackedSimd is not available on this host.</exception>
    public static ColumnarKernelBackend Backend =>
        IsSupported
            ? ColumnarVector128Backend.BackendUnchecked
            : throw new System.PlatformNotSupportedException("WebAssembly PackedSimd is not available on this host.");
}
