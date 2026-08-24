using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Builds a replica's content-hash sketch image: it projects every triple through a
/// <see cref="ContentHashReconciliationProjection"/> into content-key items and persists them as a sketch at a
/// symbol budget. One place builds the content-hash sketch — the content-hash session loads the image back as a
/// verified sketch, and the content-hash sketch channel server writes it to a peer — so the projection pass and its
/// overflow guard never diverge between the two.
/// </summary>
internal static class ContentHashSketch
{
    /// <summary>Projects an index's triples to content-hash items and writes their sketch at a budget into the output.</summary>
    /// <param name="index">The replica whose triples are projected.</param>
    /// <param name="projection">The content-hash projection.</param>
    /// <param name="symbolBudget">The number of coded symbols to persist.</param>
    /// <param name="pool">The pool the transient item buffer is rented from.</param>
    /// <param name="output">The buffer the sketch image is written to.</param>
    /// <exception cref="InvalidDataException">The index holds more items than a single projected-item buffer can address.</exception>
    /// <exception cref="NotSupportedException">A triple holds a term the content-hash projection does not project (a blank node or an RDF 1.2 triple term).</exception>
    internal static void WriteImage(ColumnarTripleIndex index, ContentHashReconciliationProjection projection, int symbolBudget, MemoryPool<byte> pool, IBufferWriter<byte> output)
    {
        int itemCount = index.TripleCount;
        long itemByteCount = (long)itemCount * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The replica holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemsOwner = pool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemsOwner.Memory.Span)[..itemCount];
        int index2 = 0;
        foreach(EncodedTriple triple in index.EnumerateTriples())
        {
            items[index2] = projection.Project(triple);
            index2++;
        }

        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, output);
    }
}
