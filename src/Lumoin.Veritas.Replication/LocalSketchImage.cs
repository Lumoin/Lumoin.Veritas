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
/// Writes a local replica's structural integrity-sketch image: projects the replica's triples to reconciliation
/// items and folds them through the rateless encoder at a symbol budget, into a caller-supplied buffer. Both the
/// in-process session (which loads the image back as a verified sketch) and the channel server (which sends the
/// image to a peer) need exactly this projection-and-persist step, so it lives here once rather than in each.
/// </summary>
internal static class LocalSketchImage
{
    /// <summary>Projects the replica's triples to structural items and persists them as a sketch image at <paramref name="symbolBudget"/> symbols into <paramref name="destination"/>.</summary>
    /// <param name="local">The local replica whose triples are projected.</param>
    /// <param name="symbolBudget">The number of coded symbols to produce.</param>
    /// <param name="pool">The pool the transient item and symbol buffers are rented from.</param>
    /// <param name="destination">The sink for the sketch image bytes.</param>
    /// <exception cref="InvalidDataException">The replica holds more items than a single projected-item buffer can address.</exception>
    internal static void Write(ColumnarTripleIndex local, int symbolBudget, MemoryPool<byte> pool, IBufferWriter<byte> destination)
    {
        int count = local.TripleCount;
        long itemByteCount = (long)count * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The local replica holds more items than a single projected-item buffer can address.");
        }

        using IMemoryOwner<byte> itemOwner = pool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..count];
        int index = 0;
        foreach(EncodedTriple triple in local.EnumerateTriples())
        {
            items[index] = StructuralReconciliationProjection.Project(triple);
            index++;
        }

        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, destination);
    }
}
