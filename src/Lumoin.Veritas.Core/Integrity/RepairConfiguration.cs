using System;
using System.Buffers;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Core.Integrity;

/// <summary>
/// The injected knobs a repair pass re-derives damaged derived artifacts under: the checksum algorithm to
/// stamp regenerated images with, the pools the re-derive rents from, the columnar sidecar's order set,
/// value encoding, and payload backing, and the sketch's geometry, symbol budget, projection, and host-bound
/// encoder. The pass holds no ambient state — everything it needs to rebuild a sidecar or a sketch from the
/// verified system-of-record is supplied here.
/// </summary>
public sealed class RepairConfiguration
{
    /// <summary>Creates a repair configuration.</summary>
    /// <param name="checksum">The checksum algorithm stamped on every regenerated artifact image.</param>
    /// <param name="bytePool">The pool the sketch re-derive rents its transient item and symbol buffers from.</param>
    /// <param name="triplePool">The pool the verified-triple feed buffer is rented from.</param>
    /// <param name="sketchContract">The sketch geometry a regenerated sketch is laid out under.</param>
    /// <param name="symbolBudget">The number of symbols a regenerated sketch produces; not negative.</param>
    /// <param name="project">The projection from a triple to its reconciliation item the sketch re-derive folds with.</param>
    /// <param name="encodeSketchSymbols">The host-bound encoder a regenerated sketch folds its items into.</param>
    /// <param name="orderSetMode">The columnar order set a regenerated sidecar is built with.</param>
    /// <param name="valueEncoding">The columnar value-column encoding a regenerated sidecar is built with.</param>
    /// <param name="backing">Where a regenerated sidecar's column payloads live.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolBudget"/> is negative.</exception>
    public RepairConfiguration(
        ChecksumAlgorithm checksum,
        MemoryPool<byte> bytePool,
        MemoryPool<EncodedTriple> triplePool,
        SketchContract sketchContract,
        int symbolBudget,
        ProjectReconciliationItemDelegate project,
        SketchReconciliationDelegates.EncodeSketchSymbols encodeSketchSymbols,
        ColumnarOrderSetMode orderSetMode = ColumnarOrderSetMode.AllSixOrders,
        ColumnarValueColumnEncoding valueEncoding = ColumnarValueColumnEncoding.EliasFanoWhenMonotone,
        ColumnPayloadBacking backing = ColumnPayloadBacking.Managed)
    {
        ArgumentNullException.ThrowIfNull(checksum);
        ArgumentNullException.ThrowIfNull(bytePool);
        ArgumentNullException.ThrowIfNull(triplePool);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(encodeSketchSymbols);
        ArgumentOutOfRangeException.ThrowIfNegative(symbolBudget);

        Checksum = checksum;
        BytePool = bytePool;
        TriplePool = triplePool;
        SketchContract = sketchContract;
        SymbolBudget = symbolBudget;
        Project = project;
        EncodeSketchSymbols = encodeSketchSymbols;
        OrderSetMode = orderSetMode;
        ValueEncoding = valueEncoding;
        Backing = backing;
    }

    /// <summary>The checksum algorithm stamped on every regenerated artifact image.</summary>
    public ChecksumAlgorithm Checksum { get; }

    /// <summary>The pool the sketch re-derive rents its transient item and symbol buffers from.</summary>
    public MemoryPool<byte> BytePool { get; }

    /// <summary>The pool the verified-triple feed buffer is rented from.</summary>
    public MemoryPool<EncodedTriple> TriplePool { get; }

    /// <summary>The sketch geometry a regenerated sketch is laid out under.</summary>
    public SketchContract SketchContract { get; }

    /// <summary>The number of symbols a regenerated sketch produces.</summary>
    public int SymbolBudget { get; }

    /// <summary>The projection from a triple to its reconciliation item the sketch re-derive folds with.</summary>
    public ProjectReconciliationItemDelegate Project { get; }

    /// <summary>The host-bound encoder a regenerated sketch folds its items into.</summary>
    public SketchReconciliationDelegates.EncodeSketchSymbols EncodeSketchSymbols { get; }

    /// <summary>The columnar order set a regenerated sidecar is built with.</summary>
    public ColumnarOrderSetMode OrderSetMode { get; }

    /// <summary>The columnar value-column encoding a regenerated sidecar is built with.</summary>
    public ColumnarValueColumnEncoding ValueEncoding { get; }

    /// <summary>Where a regenerated sidecar's column payloads live.</summary>
    public ColumnPayloadBacking Backing { get; }
}
