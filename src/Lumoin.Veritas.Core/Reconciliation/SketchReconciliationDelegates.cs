using System;
using Lumoin.Veritas.Core.ContentAddressing;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The delegate seams a host binds to a rateless-reconciliation encoder and decoder so the core persists and
/// reconciles integrity sketches without referencing the reconciliation library. The core speaks only its own
/// vocabulary — content-key items and raw symbol bytes — and the host runs the encoder and decoder behind
/// these two seams.
/// </summary>
public static class SketchReconciliationDelegates
{
    /// <summary>
    /// Folds a replica's projected items into a coded-symbol stream and writes the first
    /// <paramref name="symbolCount"/> symbols' raw bytes into <paramref name="destination"/>, each
    /// <paramref name="symbolWidth"/> bytes (the sum field followed by the checksum field, no count field),
    /// back to back.
    /// </summary>
    /// <remarks>
    /// The encode MUST be a pure function of the canonical item bytes — no wall-clock, node identity, or
    /// iteration-order dependence — so equal item sets yield byte-equal symbol prefixes. That determinism is
    /// the precondition that lets two replicas' streams combine by XOR and cancel their shared items cleanly.
    /// </remarks>
    /// <param name="items">This replica's projected reconciliation items.</param>
    /// <param name="symbolCount">The number of symbols to produce.</param>
    /// <param name="symbolWidth">The serialized width of one symbol in bytes.</param>
    /// <param name="destination">The buffer to fill; exactly <paramref name="symbolCount"/> times <paramref name="symbolWidth"/> bytes long.</param>
    public delegate void EncodeSketchSymbols(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination);

    /// <summary>
    /// Combines two replicas' verified sketches and decodes their symmetric difference, writing the recovered
    /// items into <paramref name="recovered"/> and returning how many were recovered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operands are <see cref="VerifiedSketch"/> values, which only a verifying load produces, so detection
    /// precedes this combine BY CONSTRUCTION — the decode cannot be handed unverified bytes. The host
    /// reconstructs each symbol from a sketch's bytes (splitting a symbol-width record into its sum and checksum
    /// fields), combines the two streams index-wise, and absorbs until the decoder converges or
    /// <paramref name="symbolCap"/> symbols are absorbed. If <paramref name="recovered"/> is too small it returns
    /// the needed count without writing past the span.
    /// </para>
    /// <para>
    /// A guaranteed-complete peel requires both sketches to carry a sufficient and coherent symbol budget: the
    /// combinable prefix is bounded by the SHORTER stream, so a difference larger than the shorter sketch's
    /// symbol count cannot fully peel and the recovered set is then a partial result, not the whole difference.
    /// </para>
    /// </remarks>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb before giving up.</param>
    /// <param name="recovered">The sink for the recovered difference items.</param>
    /// <returns>The number of recovered items; when it exceeds <paramref name="recovered"/>'s length nothing was written.</returns>
    public delegate int DecodeSketchDifference(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered);

    /// <summary>
    /// Combines two replicas' verified sketches and recovers their symmetric difference, returning the recovered
    /// items alongside whether the decoder fully converged — the completeness-aware counterpart of
    /// <see cref="DecodeSketchDifference"/>.
    /// </summary>
    /// <remarks>
    /// The count-only seam cannot tell a complete peel from a partial one: both write recovered items and report a
    /// count. A repair rung that must act on the recovered set — re-ingesting a lost system-of-record block from a
    /// peer — needs that distinction, because acting on a partial difference would heal incompletely. The operands
    /// are <see cref="VerifiedSketch"/> values, so detection precedes the combine by construction, exactly as for
    /// the count-only seam.
    /// </remarks>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb before giving up.</param>
    /// <param name="recovered">The sink for the recovered difference items; when the recovered count exceeds its length nothing is written.</param>
    /// <returns>The recovered count, whether the decoder converged, and how many symbols were absorbed.</returns>
    public delegate SketchDifference RecoverSketchDifference(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered);
}
