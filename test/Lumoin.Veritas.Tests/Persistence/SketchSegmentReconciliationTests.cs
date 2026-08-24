using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Sketch;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Tests.Integrity;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The integrity-sketch reconciliation seam: two diverged replicas each encode their items into a coded-symbol
/// stream, persist it through the <see cref="SketchPersistence"/> / <see cref="SketchSegment"/> format, load it
/// back (verifying every block before any symbol is folded — I2 / <see cref="PersistenceInvariant.DetectionPrecedesXor"/>),
/// combine the two verified streams, decode their exact symmetric difference, and converge by repair-as-ingest.
/// A corrupt sketch block is refused before any combine; a wrong-geometry sketch is refused; and the core
/// assembly carries no reconciliation-library reference — the host binds the encoder and decoder.
/// </summary>
[TestClass]
internal sealed class SketchSegmentReconciliationTests
{
    /// <summary>The governed pool the reconciliation encoder and decoder rent from, shared across the suite — the same pool kind production threads, so the tests exercise the tracked allocation path rather than an untracked shared allocator.</summary>
    private static VeritasMemoryPool<byte> Pool { get; } = new();

    /// <summary>A line of triples with a shared predicate: subjects <c>[start, start + count)</c>, each linked to the next identifier.</summary>
    /// <param name="start">The first subject identifier.</param>
    /// <param name="count">The number of triples.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] Line(uint start, uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            uint subject = start + i;
            triples[i] = EncodedTriple.FromEncoded(subject, 10, subject + 1);
        }

        return triples;
    }

    /// <summary>The host-side Verisync contract both binders pin: the structural domain, a 16-byte item, an 8-byte well-known-keyed checksum.</summary>
    /// <returns>The reconciliation contract.</returns>
    private static ReconciliationContract StructuralContract()
    {
        return new ReconciliationContract(
            ReconciliationItemDomain.Structural,
            ContentKey128.ByteWidth,
            8,
            ReconciliationContract.WellKnownChecksumKeyLow,
            ReconciliationContract.WellKnownChecksumKeyHigh);
    }

    /// <summary>The host-bound forward seam: folds the items into a reconciliation encoder and writes the first <paramref name="symbolCount"/> symbols' bytes (sum then checksum) into the destination. A static method, so it captures nothing.</summary>
    /// <param name="items">The replica's projected items.</param>
    /// <param name="symbolCount">The number of symbols to produce.</param>
    /// <param name="symbolWidth">The serialized width of one symbol.</param>
    /// <param name="destination">The buffer to fill, exactly <paramref name="symbolCount"/> times <paramref name="symbolWidth"/> bytes.</param>
    private static void HostEncode(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        using ReconciliationEncoder encoder = new(StructuralContract(), ReconciliationInjectivityEnforcement.None, Pool);
        Span<byte> itemBytes = stackalloc byte[ContentKey128.ByteWidth];
        foreach(ContentKey128 item in items)
        {
            item.WriteBytes(itemBytes);
            encoder.Add(itemBytes);
        }

        for(int i = 0; i < symbolCount; i++)
        {
            ReconciliationSymbol symbol = encoder.ProduceNext();
            symbol.Sum.Span.CopyTo(destination.Slice(i * symbolWidth, ContentKey128.ByteWidth));
            symbol.Checksum.Span.CopyTo(destination.Slice((i * symbolWidth) + ContentKey128.ByteWidth, checksumWidth));
        }
    }

    /// <summary>The host-bound reverse seam: reconstructs each verified sketch's symbols, combines the two streams index-wise, absorbs until the decoder converges or the cap is hit, and writes the recovered items. A static method, so it captures nothing.</summary>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb.</param>
    /// <param name="recovered">The sink for the recovered difference items.</param>
    /// <returns>The number of recovered items; when it exceeds <paramref name="recovered"/>'s length nothing was written.</returns>
    private static int HostDecode(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered)
    {
        int symbolWidth = left.SymbolWidth;
        ReadOnlySpan<byte> leftSymbols = left.Symbols.Span;
        ReadOnlySpan<byte> rightSymbols = right.Symbols.Span;
        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        int pairs = Math.Min(leftSymbols.Length / symbolWidth, rightSymbols.Length / symbolWidth);
        using ReconciliationDecoder decoder = new(StructuralContract(), Pool);

        int absorbed = 0;
        for(int i = 0; i < pairs && !decoder.IsComplete && absorbed < symbolCap; i++)
        {
            int offset = i * symbolWidth;
            ReconciliationSymbol leftSymbol = new(leftSymbols.Slice(offset, ContentKey128.ByteWidth), leftSymbols.Slice(offset + ContentKey128.ByteWidth, checksumWidth));
            ReconciliationSymbol rightSymbol = new(rightSymbols.Slice(offset, ContentKey128.ByteWidth), rightSymbols.Slice(offset + ContentKey128.ByteWidth, checksumWidth));
            decoder.Absorb(leftSymbol.Combine(rightSymbol));
            absorbed++;
        }

        IReadOnlyList<ReadOnlyMemory<byte>> decoded = decoder.DecodedItems;
        if(decoded.Count > recovered.Length)
        {
            return decoded.Count;
        }

        for(int i = 0; i < decoded.Count; i++)
        {
            recovered[i] = ContentKey128.FromBytes(decoded[i].Span);
        }

        return decoded.Count;
    }

    /// <summary>A decode binder that counts how many times it is entered — so a refusal test can assert the decode is never reached.</summary>
    private sealed class CountingDecoder
    {
        /// <summary>The number of times <see cref="Decode"/> has been entered.</summary>
        public int CallCount { get; private set; }

        /// <summary>Counts the entry and delegates to the host decode.</summary>
        /// <param name="left">One replica's verified sketch.</param>
        /// <param name="right">The other replica's verified sketch.</param>
        /// <param name="symbolCap">The maximum number of symbols to absorb.</param>
        /// <param name="recovered">The sink for the recovered difference items.</param>
        /// <returns>The number of recovered items.</returns>
        public int Decode(VerifiedSketch left, VerifiedSketch right, int symbolCap, Span<ContentKey128> recovered)
        {
            CallCount++;

            return HostDecode(left, right, symbolCap, recovered);
        }
    }

    /// <summary>Persists a replica's items as a structural sketch into a buffer rented from the caller's pool and returns it as a pooled, owned image — the sketch artifact — rather than copying the bytes out to a loose array.</summary>
    /// <param name="items">The replica's projected items.</param>
    /// <param name="symbolCount">The number of symbols to persist.</param>
    /// <param name="imagePool">The pool the image buffer is rented from; the returned image owns the buffer and returns it on dispose.</param>
    /// <returns>The pooled sketch image; the caller disposes it.</returns>
    private static ArtifactImage PersistToImage(ContentKey128[] items, int symbolCount, MemoryPool<byte> imagePool)
    {
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolCount, ChecksumAlgorithm.XxHash3, imagePool, HostEncode, writer);

        return ArtifactImage.Copy(writer.WrittenSpan, ManifestFileRole.Sketch, imagePool);
    }

    /// <summary>Loads both verified sketches and reconciles them through the counting decoder — a static helper so the refusal test's body captures nothing.</summary>
    /// <param name="imageA">One replica's sketch image.</param>
    /// <param name="imageB">The other replica's sketch image.</param>
    /// <param name="symbolCap">The decode symbol cap.</param>
    /// <param name="counter">The counting decoder.</param>
    private static void ReconcileThroughCounter(ArtifactImage imageA, ArtifactImage imageB, int symbolCap, CountingDecoder counter)
    {
        VerifiedSketch leftV = SketchPersistence.LoadVerifiedSketch(imageA.Bytes, SketchContract.Structural);
        VerifiedSketch rightV = SketchPersistence.LoadVerifiedSketch(imageB.Bytes, SketchContract.Structural);
        ContentKey128[] recovered = new ContentKey128[leftV.SymbolCount + rightV.SymbolCount];
        SketchReconciliationDelegates.DecodeSketchDifference decode = counter.Decode;
        decode(leftV, rightV, symbolCap, recovered);
    }

    /// <summary>Two diverged replicas reconcile to exactly their symmetric difference THROUGH the persisted, verified sketch format, and repair-as-ingest converges both to identical state.</summary>
    [TestMethod]
    public void ConvergeThroughPersistedSketches()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        ColumnarTripleIndex replicaA = ColumnarTripleIndex.Build(triplesA);
        ColumnarTripleIndex replicaB = ColumnarTripleIndex.Build(triplesB);

        ContentKey128[] itemsA = [.. triplesA.Select(StructuralReconciliationProjection.Project)];
        ContentKey128[] itemsB = [.. triplesB.Select(StructuralReconciliationProjection.Project)];
        int cap = 100 + (20 * (replicaA.TripleCount + replicaB.TripleCount));

        using ArtifactImage imageA = PersistToImage(itemsA, cap, imagePool);
        using ArtifactImage imageB = PersistToImage(itemsB, cap, imagePool);

        VerifiedSketch leftV = SketchPersistence.LoadVerifiedSketch(imageA.Bytes, SketchContract.Structural);
        VerifiedSketch rightV = SketchPersistence.LoadVerifiedSketch(imageB.Bytes, SketchContract.Structural);

        ContentKey128[] recovered = new ContentKey128[leftV.SymbolCount + rightV.SymbolCount];
        SketchReconciliationDelegates.DecodeSketchDifference decode = HostDecode;
        int n = decode(leftV, rightV, cap, recovered);

        HashSet<ContentKey128> recoveredKeys = [.. recovered[..n]];
        HashSet<ContentKey128> expected = [.. itemsA];
        expected.SymmetricExceptWith(itemsB);
        Assert.IsTrue(expected.SetEquals(recoveredKeys), "Reconciliation through persisted sketches must recover exactly the symmetric difference.");

        //Repair-as-ingest: applying the recovered triples to BOTH replicas converges them.
        EncodedTriple[] recoveredTriples = [.. recoveredKeys.Select(StructuralReconciliationProjection.Invert)];
        ColumnarTripleIndex convergedA = replicaA.Apply(recoveredTriples, []);
        ColumnarTripleIndex convergedB = replicaB.Apply(recoveredTriples, []);

        HashSet<EncodedTriple> union = [.. triplesA];
        union.UnionWith(triplesB);
        HashSet<EncodedTriple> finalA = [.. convergedA.EnumerateTriples()];
        HashSet<EncodedTriple> finalB = [.. convergedB.EnumerateTriples()];
        Assert.IsTrue(union.SetEquals(finalA), "Replica A must converge to the union of both replicas.");
        Assert.IsTrue(union.SetEquals(finalB), "Replica B must converge to the union of both replicas.");
        Assert.IsTrue(finalA.SetEquals(finalB), "Both replicas must reach identical state.");
    }

    /// <summary>I2: a byte flipped in a sketch block fails its checksum on load, so the corrupt symbols are refused before any combine — the decoder is never reached.</summary>
    [TestMethod]
    public void CorruptSketchBlockIsRefusedBeforeCombine()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        ContentKey128[] itemsA = [.. Line(0, 150).Select(StructuralReconciliationProjection.Project)];
        ContentKey128[] itemsB = [.. Line(50, 150).Select(StructuralReconciliationProjection.Project)];
        int cap = 100 + (20 * 300);
        using ArtifactImage imageA = PersistToImage(itemsA, cap, imagePool);
        using ArtifactImage imageB = PersistToImage(itemsB, cap, imagePool);

        //Flip the first byte of block 0 (the first page-aligned block at the default 4 KiB alignment).
        imageA.WritableBytes[SketchSegment.DefaultBlockAlignment] ^= 0xFF;

        CountingDecoder counter = new();
        Assert.ThrowsExactly<InvalidDataException>(() => ReconcileThroughCounter(imageA, imageB, cap, counter));
        int decodeCalls = counter.CallCount;
        Assert.AreEqual(0, decodeCalls, "A corrupt sketch block must be refused before the decoder is reached.");
    }

    /// <summary>A sketch loaded under a geometry that does not match what it was written with is refused, not combined into an incompatible byte space.</summary>
    [TestMethod]
    public void WrongGeometryIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        ContentKey128[] items = [.. Line(0, 50).Select(StructuralReconciliationProjection.Project)];
        using ArtifactImage image = PersistToImage(items, 100 + (20 * 50), imagePool);

        //Persisted as Structural (symbol width 24); load against a 20-byte-symbol contract.
        SketchContract wrong = new(ContentKey128.ByteWidth, 4, 256);
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchPersistence.LoadVerifiedSketch(image.Bytes, wrong); });
    }

    /// <summary>A sketch loaded under a contract with the SAME symbol width but a different symbols-per-block is refused — the second geometry-mismatch branch, distinct from the symbol-width branch.</summary>
    [TestMethod]
    public void WrongSymbolsPerBlockIsRefused()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        ContentKey128[] items = [.. Line(0, 50).Select(StructuralReconciliationProjection.Project)];
        using ArtifactImage image = PersistToImage(items, 100 + (20 * 50), imagePool);

        //Persisted as Structural (symbol width 24, 256 symbols per block); load against the same symbol width but 128 per block.
        SketchContract wrong = new(ContentKey128.ByteWidth, 8, 128);
        Assert.ThrowsExactly<InvalidDataException>(() => { _ = SketchPersistence.LoadVerifiedSketch(image.Bytes, wrong); });
    }

    /// <summary>When the recovered sink is smaller than the symmetric difference, the decode writes nothing and returns the needed count — the documented overflow contract.</summary>
    [TestMethod]
    public void UndersizedRecoveredSinkReturnsNeededCountAndWritesNothing()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        ContentKey128[] itemsA = [.. Line(0, 150).Select(StructuralReconciliationProjection.Project)];
        ContentKey128[] itemsB = [.. Line(50, 150).Select(StructuralReconciliationProjection.Project)];
        int cap = 100 + (20 * 300);

        HashSet<ContentKey128> difference = [.. itemsA];
        difference.SymmetricExceptWith(itemsB);
        int expectedDiff = difference.Count;

        using ArtifactImage imageA = PersistToImage(itemsA, cap, imagePool);
        using ArtifactImage imageB = PersistToImage(itemsB, cap, imagePool);
        VerifiedSketch leftV = SketchPersistence.LoadVerifiedSketch(imageA.Bytes, SketchContract.Structural);
        VerifiedSketch rightV = SketchPersistence.LoadVerifiedSketch(imageB.Bytes, SketchContract.Structural);

        //A sink one shy of the difference cannot hold the result.
        ContentKey128[] undersized = new ContentKey128[expectedDiff - 1];
        int needed = HostDecode(leftV, rightV, cap, undersized);

        Assert.AreEqual(expectedDiff, needed, "The decode must report the needed count when the sink is too small.");
        Assert.IsTrue(undersized.All(static key => key == ContentKey128.Zero), "An overflowing decode must not write into the sink.");
    }

    /// <summary>Two replicas with identical item sets reconcile to an empty difference through the persisted, verified sketches — no phantom items recovered (the protocol's steady state).</summary>
    [TestMethod]
    public void IdenticalReplicasRecoverNoDifferenceThroughSketches()
    {
        using VeritasMemoryPool<byte> imagePool = new();
        ContentKey128[] items = [.. Line(0, 100).Select(StructuralReconciliationProjection.Project)];
        int cap = 100 + (20 * 200);

        using ArtifactImage imageA = PersistToImage(items, cap, imagePool);
        using ArtifactImage imageB = PersistToImage(items, cap, imagePool);
        VerifiedSketch leftV = SketchPersistence.LoadVerifiedSketch(imageA.Bytes, SketchContract.Structural);
        VerifiedSketch rightV = SketchPersistence.LoadVerifiedSketch(imageB.Bytes, SketchContract.Structural);

        ContentKey128[] recovered = new ContentKey128[leftV.SymbolCount + rightV.SymbolCount];
        int n = HostDecode(leftV, rightV, cap, recovered);

        Assert.AreEqual(0, n, "Identical replicas must reconcile to an empty difference.");
    }

    /// <summary>The core assembly carries no reconciliation-library reference: the encode/decode seams are bound by the host. Asserted by reflection because the cited <see cref="PersistenceInvariant"/> is internal to the core and a cref cannot reach across the assembly boundary.</summary>
    [TestMethod]
    public void CoreAssemblyDoesNotReferenceVerisync()
    {
        AssemblyName[] referenced = typeof(SketchContract).Assembly.GetReferencedAssemblies();
        foreach(AssemblyName name in referenced)
        {
            bool isVerisync = name.Name is string assemblyName && assemblyName.StartsWith("Lumoin.Verisync", StringComparison.Ordinal);
            Assert.IsFalse(isVerisync, $"Lumoin.Veritas.Core must not reference {name.Name}; the reconciliation seam is host-bound.");
        }
    }
}
