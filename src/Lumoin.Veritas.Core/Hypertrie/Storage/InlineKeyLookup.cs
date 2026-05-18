using System;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// Looks up a key in a small contiguous sequence of <see cref="uint"/>
/// keys, returning the index where the key was found or <c>-1</c>
/// if absent.
/// </summary>
/// <param name="keys">The keys to search. Up to 8 entries; longer spans are not supported by Inline-tier consumers.</param>
/// <param name="needle">The key being searched for.</param>
/// <returns>The zero-based index of <paramref name="needle"/> in <paramref name="keys"/>, or <c>-1</c> if not present.</returns>
/// <remarks>
/// <para>
/// The delegate is the boundary between <see cref="EdgeMap"/>'s
/// Inline-tier lookup and the implementation strategy. Different
/// implementations trade off scalar-loop simplicity, SIMD
/// acceleration on supported hardware (AVX2, AVX-512 on x64; NEON
/// on ARM), and verification ease. Callers obtain a delegate from
/// <see cref="InlineKeyLookups.SelectBestAvailable"/> at startup
/// and pass it through to every <see cref="EdgeMap.TryGetChild"/>
/// call site.
/// </para>
/// <para>
/// Implementations must be pure (no side effects, no captures) so
/// the static method group can be referenced directly as a
/// delegate target. The scalar reference implementation in
/// <see cref="InlineKeyLookups"/> sets the correctness benchmark
/// for any future hardware-accelerated implementation.
/// </para>
/// </remarks>
public delegate int InlineKeyLookup(ReadOnlySpan<uint> keys, uint needle);
