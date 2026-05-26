using System;
using System.Diagnostics;

namespace Lumoin.Veritas.Core;

/// <summary>
/// What a caller is asking a <see cref="RandomnessDelegate"/> for, with enough
/// context for the delegate to be deterministic when it chooses to be.
/// </summary>
/// <remarks>
/// The delegate may inspect <see cref="CorrelationId"/> (the per-query identity),
/// <see cref="CallSiteSalt"/> (an operator-and-row-derived byte sequence, so a
/// delegate can hash to a reproducible value), and <see cref="ByteCount"/> (for
/// <see cref="RandomnessKind.Bytes"/>). Passed by <see langword="in"/>; no
/// allocation.
/// </remarks>
/// <param name="Kind">The kind of randomness requested.</param>
/// <param name="CorrelationId">The per-query correlation identity of the requesting evaluation.</param>
/// <param name="ByteCount">The number of bytes requested when <see cref="Kind"/> is <see cref="RandomnessKind.Bytes"/>; otherwise ignored.</param>
/// <param name="CallSiteSalt">An operator-and-row-derived salt allowing a delegate to produce a deterministic value per call site.</param>
[DebuggerDisplay("{Kind} bytes={ByteCount} corr={CorrelationId}")]
public readonly record struct RandomnessRequest(
    RandomnessKind Kind,
    Guid CorrelationId,
    int ByteCount,
    ReadOnlyMemory<byte> CallSiteSalt);
