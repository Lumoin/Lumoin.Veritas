using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Tests.Integrity;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Tests.Persistence;

/// <summary>
/// The sketch-update round: a replica's integrity sketch is produced from its just-written system-of-record
/// image, feeding the encoder only the items of blocks that pass their checksum (the feed face of
/// <see cref="PersistenceInvariant.DetectionPrecedesXor"/>), under a budgeted symbol cap. The produced sketch
/// reconciles against a peer's; a corrupt system-of-record block's items are excluded and named, never folded;
/// framing damage is refused; and the staged sketch co-versions with the generation by being listed in the
/// manifest before the single atomic CURRENT publish, so a crash at the publish leaves the prior generation —
/// and its sketch — wholly in force. Every serialized artifact is an <see cref="ArtifactImage"/> over a buffer
/// rented from the test's pool.
/// </summary>
[TestClass]
internal sealed class SketchUpdateRoundTests
{
    /// <summary>The header (8 magic + 1 major + 1 minor + 8 feature mask + 1 algo id) and scalar (3×4) byte size before the per-block checksum section.</summary>
    private const int FrontMatterBase = 19 + 12;

    /// <summary>The byte size of one row-major item record: subject, predicate, object as three little-endian 32-bit ids.</summary>
    private const int ItemByteSize = 3 * sizeof(uint);

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

    /// <summary>The host-bound forward seam: folds the items into a reconciliation encoder and writes the first <paramref name="symbolCount"/> symbols' bytes. A static method, so it captures nothing.</summary>
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

    /// <summary>The host-bound reverse seam: combines two verified streams index-wise, absorbs until convergence or the cap, and writes the recovered items. A static method, so it captures nothing.</summary>
    /// <param name="left">One replica's verified sketch.</param>
    /// <param name="right">The other replica's verified sketch.</param>
    /// <param name="symbolCap">The maximum number of symbols to absorb.</param>
    /// <param name="recovered">The sink for the recovered difference items.</param>
    /// <returns>The number of recovered items.</returns>
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

    /// <summary>Serializes triples as a system-of-record item-segment image into a buffer rented from <paramref name="pool"/>.</summary>
    /// <param name="triples">The canonical triples.</param>
    /// <param name="blockItemCount">The triples per block.</param>
    /// <param name="checksum">The per-block checksum algorithm, or <see langword="null"/> for an unchecksummed segment.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The pooled image.</returns>
    private static ArtifactImage SerializeSegment(EncodedTriple[] triples, int blockItemCount, ChecksumAlgorithm? checksum, MemoryPool<byte> pool)
    {
        ItemSegment segment = new(triples, blockItemCount, blockAlignment: 64);
        int size = (int)segment.ComputeSerializedSize(checksum);
        IMemoryOwner<byte> owner = pool.Rent(size);
        segment.WriteTo(owner.Memory.Span[..size], checksum);

        return ArtifactImage.Own(owner, size, ManifestFileRole.DataSegment);
    }

    /// <summary>Runs the sketch-update round over a system-of-record image and returns the staged sketch as a pooled image.</summary>
    /// <param name="systemOfRecord">The system-of-record image.</param>
    /// <param name="symbolBudget">The budgeted symbol cap.</param>
    /// <param name="checksum">The per-block checksum algorithm.</param>
    /// <param name="bytePool">The byte pool the round and the produced image rent from.</param>
    /// <param name="triplePool">The triple pool the feed rents from.</param>
    /// <param name="report">The round's verdict.</param>
    /// <returns>The pooled sketch image.</returns>
    private static ArtifactImage RunRound(ArtifactImage systemOfRecord, int symbolBudget, ChecksumAlgorithm checksum, MemoryPool<byte> bytePool, MemoryPool<EncodedTriple> triplePool, out SketchUpdateRoundReport report)
    {
        ArrayBufferWriter<byte> writer = new();
        report = SketchPersistence.RunSketchUpdateRound(
            systemOfRecord.Bytes,
            SketchContract.Structural,
            symbolBudget,
            checksum,
            bytePool,
            triplePool,
            StructuralReconciliationProjection.Projection,
            HostEncode,
            writer);

        return ArtifactImage.Copy(writer.WrittenSpan, ManifestFileRole.Sketch, bytePool);
    }

    /// <summary>Persists an explicit item set as a structural sketch image — the oracle the round's output is compared against.</summary>
    /// <param name="items">The items to encode.</param>
    /// <param name="symbolCount">The symbol budget.</param>
    /// <param name="checksum">The per-block checksum algorithm.</param>
    /// <param name="pool">The pool the image is rented from.</param>
    /// <returns>The pooled sketch image.</returns>
    private static ArtifactImage PersistItems(ContentKey128[] items, int symbolCount, ChecksumAlgorithm checksum, MemoryPool<byte> pool)
    {
        ArrayBufferWriter<byte> writer = new();
        SketchPersistence.PersistSketch(items, SketchContract.Structural, symbolCount, checksum, pool, HostEncode, writer);

        return ArtifactImage.Copy(writer.WrittenSpan, ManifestFileRole.Sketch, pool);
    }

    /// <summary>Loads both sketch images, reconciles them, and returns the recovered difference as a set.</summary>
    /// <param name="imageA">One replica's sketch image.</param>
    /// <param name="imageB">The other replica's sketch image.</param>
    /// <param name="symbolCap">The decode symbol cap.</param>
    /// <returns>The recovered difference.</returns>
    private static HashSet<ContentKey128> Reconcile(ArtifactImage imageA, ArtifactImage imageB, int symbolCap)
    {
        VerifiedSketch left = SketchPersistence.LoadVerifiedSketch(imageA.Bytes, SketchContract.Structural);
        VerifiedSketch right = SketchPersistence.LoadVerifiedSketch(imageB.Bytes, SketchContract.Structural);
        ContentKey128[] recovered = new ContentKey128[left.SymbolCount + right.SymbolCount];
        int n = HostDecode(left, right, symbolCap, recovered);

        return [.. recovered[..n]];
    }

    /// <summary>The aligned byte stride between block starts for the given geometry.</summary>
    /// <param name="blockItemCount">The triples per block.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The block stride.</returns>
    private static long BlockStride(int blockItemCount, int alignment)
    {
        return (((long)blockItemCount * ItemByteSize) + alignment - 1) / alignment * alignment;
    }

    /// <summary>The byte offset of the first item block: the front matter plus the per-block checksum section, rounded up to the alignment.</summary>
    /// <param name="blockCount">The block count.</param>
    /// <param name="checksumWidth">The checksum byte width.</param>
    /// <param name="alignment">The block alignment.</param>
    /// <returns>The first block's byte offset.</returns>
    private static int FirstBlockOffset(int blockCount, int checksumWidth, int alignment)
    {
        int frontMatterEnd = FrontMatterBase + (blockCount * checksumWidth);

        return (frontMatterEnd + alignment - 1) / alignment * alignment;
    }

    /// <summary>A sketch produced from a replica's system-of-record image reconciles against a diverged peer's to exactly their symmetric difference — the round feeds the encoder the system-of-record's items end to end.</summary>
    [TestMethod]
    public void SketchFromSystemOfRecordReconcilesToTheSymmetricDifference()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triplesA = Line(0, 150);
        EncodedTriple[] triplesB = Line(50, 150);
        int budget = 100 + (20 * (triplesA.Length + triplesB.Length));

        using ArtifactImage systemOfRecordA = SerializeSegment(triplesA, 16, ChecksumAlgorithm.XxHash3, bytePool);
        using ArtifactImage imageA = RunRound(systemOfRecordA, budget, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport reportA);
        using ArtifactImage systemOfRecordB = SerializeSegment(triplesB, 16, ChecksumAlgorithm.XxHash3, bytePool);
        using ArtifactImage imageB = RunRound(systemOfRecordB, budget, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport reportB);

        Assert.AreEqual(triplesA.Length, reportA.ItemsFed);
        Assert.AreEqual(triplesB.Length, reportB.ItemsFed);
        Assert.IsTrue(reportA.IsClean);
        Assert.IsTrue(reportB.IsClean);

        HashSet<ContentKey128> recovered = Reconcile(imageA, imageB, budget);
        HashSet<ContentKey128> expected = [.. triplesA.Select(StructuralReconciliationProjection.Project)];
        expected.SymmetricExceptWith(triplesB.Select(StructuralReconciliationProjection.Project));
        Assert.IsTrue(expected.SetEquals(recovered), "The system-of-record-fed sketch must reconcile to exactly the symmetric difference.");
    }

    /// <summary>I2 feed face: a corrupt system-of-record block's items are excluded from the sketch and named in the report — the sketch encodes only the surviving items, and the corrupt block's items are exactly what a sketch over the whole set differs by.</summary>
    [TestMethod]
    public void CorruptSystemOfRecordBlockIsExcludedFromTheSketch()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = Line(0, 30);
        using ArtifactImage systemOfRecord = SerializeSegment(triples, 10, ChecksumAlgorithm.XxHash3, bytePool);
        int budget = 100 + (20 * triples.Length);

        //Flip the first item byte of block 1 — items [10, 20).
        int firstBlock = FirstBlockOffset(blockCount: 3, ChecksumAlgorithm.XxHash3.ByteWidth, alignment: 64);
        systemOfRecord.WritableBytes[firstBlock + (int)BlockStride(10, 64)] ^= 0xFF;

        using ArtifactImage roundImage = RunRound(systemOfRecord, budget, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport report);

        Assert.AreEqual(20, report.ItemsFed, "The corrupt block's ten items must not be fed.");
        Assert.HasCount(1, report.SkippedRanges);
        Assert.AreEqual(new SkippedItemRange(1, 10, 10), report.SkippedRanges[0]);

        //A sketch over ONLY the surviving items reconciles to nothing against the round's sketch — same set.
        ContentKey128[] survivors = [.. triples[..10].Concat(triples[20..]).Select(StructuralReconciliationProjection.Project)];
        using ArtifactImage survivorImage = PersistItems(survivors, budget, ChecksumAlgorithm.XxHash3, bytePool);
        Assert.IsEmpty(Reconcile(roundImage, survivorImage, budget));

        //A sketch over the WHOLE set differs from the round's sketch by exactly the excluded block's items.
        ContentKey128[] all = [.. triples.Select(StructuralReconciliationProjection.Project)];
        using ArtifactImage allImage = PersistItems(all, budget, ChecksumAlgorithm.XxHash3, bytePool);
        HashSet<ContentKey128> excluded = [.. triples[10..20].Select(StructuralReconciliationProjection.Project)];
        Assert.IsTrue(excluded.SetEquals(Reconcile(roundImage, allImage, budget)), "The round's sketch must differ from the whole-set sketch by exactly the excluded block's items.");
    }

    /// <summary>A clean system-of-record feeds every item and reports nothing excluded.</summary>
    [TestMethod]
    public void CleanSystemOfRecordFeedsEveryItem()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = Line(0, 50);
        using ArtifactImage systemOfRecord = SerializeSegment(triples, 10, ChecksumAlgorithm.XxHash3, bytePool);

        using ArtifactImage image = RunRound(systemOfRecord, 100 + (20 * triples.Length), ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport report);

        Assert.AreEqual(triples.Length, report.ItemsFed);
        Assert.IsTrue(report.IsClean);
        Assert.IsTrue(report.WasChecksumGated, "A checksummed system-of-record feeds a verified-clean round.");
        Assert.IsEmpty(report.SkippedRanges);
    }

    /// <summary>An unchecksummed system-of-record cannot be gated: the round still feeds every block, but the report says it was not verified rather than appearing clean-because-verified.</summary>
    [TestMethod]
    public void UnchecksummedSystemOfRecordReportsNotGated()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = Line(0, 30);
        using ArtifactImage systemOfRecord = SerializeSegment(triples, 10, null, bytePool);

        using ArtifactImage image = RunRound(systemOfRecord, 100 + (20 * triples.Length), ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport report);

        Assert.AreEqual(triples.Length, report.ItemsFed);
        Assert.IsTrue(report.IsClean);
        Assert.IsFalse(report.WasChecksumGated, "An unchecksummed system-of-record cannot be gated, and the report must say so rather than claim verification.");
    }

    /// <summary>A zero symbol budget produces a valid empty sketch on the injected pool rather than failing on a zero-length rent.</summary>
    [TestMethod]
    public void ZeroBudgetProducesAnEmptySketch()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = Line(0, 20);
        using ArtifactImage systemOfRecord = SerializeSegment(triples, 16, ChecksumAlgorithm.XxHash3, bytePool);

        using ArtifactImage image = RunRound(systemOfRecord, 0, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport report);

        Assert.AreEqual(0, report.SymbolCount);
        Assert.AreEqual(triples.Length, report.ItemsFed);
        VerifiedSketch verified = SketchPersistence.LoadVerifiedSketch(image.Bytes, SketchContract.Structural);
        Assert.AreEqual(0, verified.SymbolCount);
    }

    /// <summary>The budget caps the produced symbol stream: the persisted sketch carries exactly the budgeted symbol count.</summary>
    [TestMethod]
    public void BudgetCapsTheProducedSymbolCount()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = Line(0, 40);
        using ArtifactImage systemOfRecord = SerializeSegment(triples, 16, ChecksumAlgorithm.XxHash3, bytePool);
        const int Budget = 321;

        using ArtifactImage image = RunRound(systemOfRecord, Budget, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport report);

        Assert.AreEqual(Budget, report.SymbolCount);
        VerifiedSketch verified = SketchPersistence.LoadVerifiedSketch(image.Bytes, SketchContract.Structural);
        Assert.AreEqual(Budget, verified.SymbolCount);
    }

    /// <summary>Framing damage to the system-of-record is untrusted geometry the round refuses outright, rather than producing a sketch over a guessed item set.</summary>
    [TestMethod]
    public void FramingDamageInSystemOfRecordIsRefused()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = Line(0, 30);
        using ArtifactImage systemOfRecord = SerializeSegment(triples, 10, ChecksumAlgorithm.XxHash3, bytePool);
        systemOfRecord.WritableBytes[0] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => { _ = RunRound(systemOfRecord, 100, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out _); });
    }

    /// <summary>An empty system-of-record produces a sketch over no items; two such sketches reconcile to an empty difference.</summary>
    [TestMethod]
    public void EmptySystemOfRecordProducesAnEmptySketch()
    {
        using VeritasMemoryPool<byte> bytePool = new();
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        using ArtifactImage systemOfRecord = SerializeSegment([], 8, ChecksumAlgorithm.XxHash3, bytePool);
        const int Budget = 200;

        using ArtifactImage imageA = RunRound(systemOfRecord, Budget, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out SketchUpdateRoundReport report);
        using ArtifactImage imageB = RunRound(systemOfRecord, Budget, ChecksumAlgorithm.XxHash3, bytePool, triplePool, out _);

        Assert.AreEqual(0, report.ItemsFed);
        Assert.IsTrue(report.IsClean);
        Assert.IsEmpty(Reconcile(imageA, imageB, Budget));
    }

    /// <summary>Creates a fresh, uniquely-named temp directory for one test's store.</summary>
    /// <returns>The directory path.</returns>
    private static string CreateTempDirectory()
    {
        return Directory.CreateTempSubdirectory("veritas-sketch-round-").FullName;
    }

    /// <summary>A directory durability barrier that does nothing — the injected substitute that keeps the tests platform-independent and timer-free.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Computes a file's whole-image checksum digest into a buffer rented from <paramref name="pool"/>, valid until the rented owner is returned.</summary>
    /// <param name="image">The file bytes.</param>
    /// <param name="checksum">The checksum algorithm.</param>
    /// <param name="pool">The pool the digest buffer is rented from.</param>
    /// <returns>The rented digest owner; its first <see cref="ChecksumAlgorithm.ByteWidth"/> bytes are the digest.</returns>
    private static IMemoryOwner<byte> Digest(ReadOnlySpan<byte> image, ChecksumAlgorithm checksum, MemoryPool<byte> pool)
    {
        IMemoryOwner<byte> owner = pool.Rent(checksum.ByteWidth);
        checksum.Compute(image, owner.Memory.Span[..checksum.ByteWidth]);

        return owner;
    }

    /// <summary>Commits a generation naming a staged data segment and its co-versioned sketch, renting each entry's digest from <paramref name="pool"/> for the duration of the commit.</summary>
    /// <param name="writer">The manifest writer that publishes the generation.</param>
    /// <param name="generation">The commit generation.</param>
    /// <param name="segment">The staged data-segment image.</param>
    /// <param name="sketch">The staged sketch image.</param>
    /// <param name="checksum">The checksum algorithm whose width the entry digests must match.</param>
    /// <param name="pool">The pool the entry digests are rented from.</param>
    private static void CommitGeneration(ManifestWriter writer, long generation, ArtifactImage segment, ArtifactImage sketch, ChecksumAlgorithm checksum, MemoryPool<byte> pool)
    {
        int width = checksum.ByteWidth;
        using IMemoryOwner<byte> segmentDigest = Digest(segment.Bytes, checksum, pool);
        using IMemoryOwner<byte> sketchDigest = Digest(sketch.Bytes, checksum, pool);
        ManifestEntry[] entries =
        [
            new(ManifestFileRole.DataSegment, $"segment-{generation:D20}.dat", 0, segment.Length, segmentDigest.Memory[..width]),
            new(ManifestFileRole.Sketch, $"sketch-{generation:D20}.skt", 0, sketch.Length, sketchDigest.Memory[..width]),
        ];
        writer.Commit(new Manifest(generation, generation * 11, generation * 13, entries));
    }

    /// <summary>The sketch co-versions with its generation: produced and staged before the single atomic CURRENT publish, listed in the manifest, and recovered as part of the same committed generation.</summary>
    [TestMethod]
    public void SketchCoVersionsWithTheGenerationThroughTheManifest()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> bytePool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            ChecksumAlgorithm checksum = ChecksumAlgorithm.XxHash3;
            const long Generation = 7;

            EncodedTriple[] triples = Line(0, 120);
            using ArtifactImage segmentImage = SerializeSegment(triples, 16, checksum, bytePool);
            int budget = 100 + (20 * triples.Length);
            using ArtifactImage sketchImage = RunRound(segmentImage, budget, checksum, bytePool, triplePool, out SketchUpdateRoundReport report);
            Assert.IsTrue(report.IsClean);

            //Stage both artifacts durably BEFORE the publish, then publish the generation atomically.
            string sketchName = $"sketch-{Generation:D20}.skt";
            store.WriteStaged($"segment-{Generation:D20}.dat", segmentImage.Bytes);
            store.WriteStaged(sketchName, sketchImage.Bytes);

            ManifestWriter writer = new(store, checksum, bytePool, retainedCurrentPointerCount: 4);
            CommitGeneration(writer, Generation, segmentImage, sketchImage, checksum, bytePool);

            RecoveryResult result = new ManifestRecovery(store).Recover();
            Assert.AreEqual(Generation, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);

            ManifestEntry sketchEntry = result.Manifest.Entries.Single(static entry => entry.Role == ManifestFileRole.Sketch);
            Assert.AreEqual(sketchName, sketchEntry.FileName);

            //The staged sketch the recovered generation names loads and verifies.
            byte[] persisted = store.Read(sketchName) ?? throw new InvalidOperationException("The committed sketch is missing.");
            VerifiedSketch verified = SketchPersistence.LoadVerifiedSketch(persisted, SketchContract.Structural);
            Assert.AreEqual(budget, verified.SymbolCount);

            //The manifest entry authenticates the file it names: its stored digest matches the persisted bytes.
            using IMemoryOwner<byte> recomputed = Digest(persisted, checksum, bytePool);
            Assert.IsTrue(sketchEntry.Checksum.Span.SequenceEqual(recomputed.Memory.Span[..checksum.ByteWidth]), "The recovered manifest entry's checksum must authenticate the sketch file it names.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A crash at the commit point while publishing a new generation's sketch leaves the prior committed generation — and its sketch — wholly in force; the half-staged new sketch is never the one recovery names.</summary>
    [TestMethod]
    public void CrashBeforePublishLeavesThePriorGenerationsSketch()
    {
        string directory = CreateTempDirectory();
        try
        {
            using VeritasMemoryPool<byte> bytePool = new();
            using VeritasMemoryPool<EncodedTriple> triplePool = new();
            FileSystemPersistenceStore store = new(directory, NoOpBarrier);
            ChecksumAlgorithm checksum = ChecksumAlgorithm.XxHash3;

            //Generation 1 commits cleanly with its sketch.
            EncodedTriple[] triplesOne = Line(0, 80);
            using ArtifactImage segmentOne = SerializeSegment(triplesOne, 16, checksum, bytePool);
            using ArtifactImage sketchOne = RunRound(segmentOne, 100 + (20 * triplesOne.Length), checksum, bytePool, triplePool, out _);
            store.WriteStaged("segment-00000000000000000001.dat", segmentOne.Bytes);
            store.WriteStaged("sketch-00000000000000000001.skt", sketchOne.Bytes);
            ManifestWriter writerOne = new(store, checksum, bytePool, retainedCurrentPointerCount: 4);
            CommitGeneration(writerOne, 1, segmentOne, sketchOne, checksum, bytePool);

            //Generation 2 stages its sketch, then the publish crashes: the generation is never committed.
            EncodedTriple[] triplesTwo = Line(0, 200);
            using ArtifactImage segmentTwo = SerializeSegment(triplesTwo, 16, checksum, bytePool);
            using ArtifactImage sketchTwo = RunRound(segmentTwo, 100 + (20 * triplesTwo.Length), checksum, bytePool, triplePool, out _);
            store.WriteStaged("segment-00000000000000000002.dat", segmentTwo.Bytes);
            store.WriteStaged("sketch-00000000000000000002.skt", sketchTwo.Bytes);
            FailAtStepStore crashing = new(store, PublishFailStep.BeforeRename);
            ManifestWriter crashingWriter = new(crashing, checksum, bytePool, retainedCurrentPointerCount: 4);
            Assert.ThrowsExactly<IOException>(() => CommitGeneration(crashingWriter, 2, segmentTwo, sketchTwo, checksum, bytePool));

            //Recovery follows CURRENT to generation 1; the orphaned generation-2 sketch is never named.
            RecoveryResult result = new ManifestRecovery(store).Recover();
            Assert.AreEqual(1, result.Manifest.CommitGeneration);
            Assert.IsFalse(result.IsDegraded);
            ManifestEntry sketchEntry = result.Manifest.Entries.Single(static entry => entry.Role == ManifestFileRole.Sketch);
            Assert.AreEqual("sketch-00000000000000000001.skt", sketchEntry.FileName);

            //The prior generation's sketch is not merely named — it is still loadable and verifies, so the
            //generation-1 sketch is genuinely in force and the orphaned generation-2 sketch is not what survived.
            byte[] priorSketch = store.Read(sketchEntry.FileName) ?? throw new InvalidOperationException("The prior generation's sketch is missing.");
            VerifiedSketch priorVerified = SketchPersistence.LoadVerifiedSketch(priorSketch, SketchContract.Structural);
            Assert.AreEqual(100 + (20 * triplesOne.Length), priorVerified.SymbolCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The core assembly carries no reconciliation-library reference: the round's encode and projection seams are bound by the host.</summary>
    [TestMethod]
    public void CoreAssemblyDoesNotReferenceVerisync()
    {
        System.Reflection.AssemblyName[] referenced = typeof(SketchPersistence).Assembly.GetReferencedAssemblies();
        foreach(System.Reflection.AssemblyName name in referenced)
        {
            bool isVerisync = name.Name is string assemblyName && assemblyName.StartsWith("Lumoin.Verisync", StringComparison.Ordinal);
            Assert.IsFalse(isVerisync, $"Lumoin.Veritas.Core must not reference {name.Name}; the round's encode seam is host-bound.");
        }
    }
}
