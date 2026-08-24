using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Database;

namespace Lumoin.Veritas.Tests.Integrity;

/// <summary>
/// The keyed-checksum tamper-evidence seam: an id-bearing artifact written under a host-composed keyed
/// message-authentication algorithm round-trips only under a resolver holding the same key, and every forgery a
/// key-less attacker can mount — a valid unkeyed tag recomputed over tampered bytes under the keyed id, a bare
/// bit-flip against a stale keyed tag, a read with no key at all, and a read with the wrong key — is refused, not
/// downgraded. The refusal is loud: an unresolvable keyed id is a <see cref="NotSupportedException"/> and a tag
/// mismatch an <see cref="InvalidDataException"/>, never a silent skip. The engine forwards the seam end to end so a
/// keyed database reads its own artifacts and refuses under default options, while the append-only journal stays on
/// the built-in checksum by design. The keyed algorithm is built in the test only, from the in-box
/// <see cref="HMACSHA256"/>; no crypto dependency lands in the library.
/// </summary>
[TestClass]
internal sealed class KeyedChecksumForgeryTests
{
    /// <summary>The example-namespace prefix the engine-level test data and queries share.</summary>
    private const string Ex = "http://example.org/";

    /// <summary>The header (8 magic + 1 major + 1 minor + 8 feature mask + 1 algo id) plus scalars (3×4) byte size before the per-block checksum section — the layout a payload-block tamper offset is measured from.</summary>
    private const int FrontMatterBase = 19 + 12;

    /// <summary>The block alignment the test segments are laid out under.</summary>
    private const int BlockAlignment = 64;

    /// <summary>The number of triples per item block the test segments use; a handful of blocks with a partial last one.</summary>
    private const int BlockItemCount = 8;

    /// <summary>The keyed tag width every fixture writes — the reserved keyed ids' permanently bound width.</summary>
    private const int KeyedTagWidth = ChecksumAlgorithm.ReservedKeyedByteWidth;

    /// <summary>The encoded-triple record width in the item-segment format: three four-byte terms.</summary>
    private const int ItemByteSize = 12;

    /// <summary>The triple count the fixtures serialize — several full blocks plus a partial last one.</summary>
    private const uint SampleTripleCount = 30;

    /// <summary>The MSTest-supplied per-test context, used for the ambient cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>The fixed secret key the keyed algorithm and its resolver close over in this test — the key never crosses the algorithm type or the on-disk format.</summary>
    private static byte[] TestKey { get; } = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x00];

    /// <summary>A different secret key, so a wrong-key read recomputes a different tag than the one at rest.</summary>
    private static byte[] WrongKey { get; } = [0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10, 0x01, 0x23, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF];

    /// <summary>The keyed HMAC-SHA-256 algorithm under the reserved keyed id, its compute closed over <paramref name="key"/> so the key stays at the composition root.</summary>
    /// <param name="key">The secret key the tag is computed under.</param>
    /// <returns>The keyed algorithm (id 3, 32-byte tag).</returns>
    private static ChecksumAlgorithm KeyedHmac(byte[] key)
    {
        return ChecksumAlgorithm.CreateKeyed(ChecksumAlgorithm.KeyedHmacSha256Id, "HMAC-SHA-256", KeyedTagWidth, (data, destination) => HMACSHA256.HashData(key, data, destination));
    }

    /// <summary>A read-side resolver that maps the keyed id to the keyed algorithm under <paramref name="key"/> and chains to the built-in resolver for every other id.</summary>
    /// <param name="key">The secret key the keyed algorithm is composed under.</param>
    /// <returns>The resolver.</returns>
    private static ResolveChecksumAlgorithmDelegate KeyedResolver(byte[] key)
    {
        ChecksumAlgorithm keyed = KeyedHmac(key);

        return id => id == ChecksumAlgorithm.KeyedHmacSha256Id ? keyed : ChecksumAlgorithm.DefaultResolver(id);
    }

    /// <summary>A handful of distinct triples so a segment spans several item blocks with a partial last block.</summary>
    /// <param name="count">The triple count.</param>
    /// <returns>The triples.</returns>
    private static EncodedTriple[] SampleTriples(uint count)
    {
        EncodedTriple[] triples = new EncodedTriple[count];
        for(uint i = 0; i < count; i++)
        {
            triples[i] = EncodedTriple.FromEncoded(i + 1, (i * 7) + 3, (i * 13) + 5);
        }

        return triples;
    }

    /// <summary>Serializes an item segment over the triples under the given checksum algorithm into a fresh image array.</summary>
    /// <param name="triples">The triples to serialize.</param>
    /// <param name="checksum">The per-block checksum algorithm the image is written under.</param>
    /// <returns>The written image, and the segment so the caller can compute a block offset.</returns>
    private static (byte[] Image, ItemSegment Segment) WriteSegmentImage(EncodedTriple[] triples, ChecksumAlgorithm checksum)
    {
        ItemSegment segment = new(triples, BlockItemCount, BlockAlignment);
        int size = (int)segment.ComputeSerializedSize(checksum);
        byte[] image = new byte[size];
        segment.WriteTo(image, checksum);

        return (image, segment);
    }

    /// <summary>The byte offset of the first item block: the front matter plus the per-block checksum section, rounded up to the block alignment — a payload byte a block checksum covers.</summary>
    /// <param name="blockCount">The block count.</param>
    /// <param name="checksumWidth">The checksum byte width.</param>
    /// <returns>The first payload block's byte offset.</returns>
    private static int FirstBlockOffset(int blockCount, int checksumWidth)
    {
        int frontMatterEnd = FrontMatterBase + (blockCount * checksumWidth);

        return (frontMatterEnd + BlockAlignment - 1) / BlockAlignment * BlockAlignment;
    }

    /// <summary>The engine options selecting the keyed algorithm for writes and the matching resolver for reads.</summary>
    /// <returns>The keyed options.</returns>
    private static VeritasEngineOptions KeyedEngineOptions()
    {
        return new VeritasEngineOptions { Checksum = KeyedHmac(TestKey), ResolveChecksum = KeyedResolver(TestKey) };
    }

    /// <summary>A directory durability barrier that does nothing, so the store side does not depend on a real filesystem fsync.</summary>
    /// <param name="directoryPath">The directory whose metadata would be flushed.</param>
    private static void NoOpBarrier(string directoryPath)
    {
    }

    /// <summary>Inserts a triple through a SPARQL Update, minting any new IRIs into the database's dictionary.</summary>
    /// <param name="database">The mutable database.</param>
    /// <param name="subject">The subject local name.</param>
    /// <param name="predicate">The predicate local name.</param>
    /// <param name="obj">The object local name.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The asynchronous update.</returns>
    private static async Task InsertAsync(VeritasEngine database, string subject, string predicate, string obj, CancellationToken cancellationToken)
    {
        await database
            .UpdateAsync(Utf8Strings.From($"INSERT DATA {{ <{Ex}{subject}> <{Ex}{predicate}> <{Ex}{obj}> }}"), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Asks a boolean over the database.</summary>
    /// <param name="database">The database.</param>
    /// <param name="ask">The ASK query text.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The boolean answer.</returns>
    private static async Task<bool> AskAsync(VeritasEngine database, string ask, CancellationToken cancellationToken)
    {
        return await database.AskAsync(Utf8Strings.From(ask), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Case 1: an artifact written under the keyed algorithm round-trips when read back with a resolver holding the same key.</summary>
    [TestMethod]
    public void SameKeyRoundTripReadsBack()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(SampleTripleCount);
        (byte[] image, _) = WriteSegmentImage(triples, KeyedHmac(TestKey));

        using DecodedItemSegment restored = ItemSegment.ReadFrom(image, triplePool, KeyedResolver(TestKey));

        Assert.AreEqual(triples.Length, restored.Length);
        Assert.IsTrue(triples.AsSpan().SequenceEqual(restored.Span), "The keyed artifact did not round-trip under the matching key.");
    }

    /// <summary>Case 2: a key-less attacker who recomputes valid unkeyed tags over tampered bytes is refused by the keyed reader — the tags are not the keyed MAC. The attacker owns bytes, not the factory: the tampered block's stored tag and the front-matter trailer are byte-patched with raw unkeyed SHA-256 digests, since a keyless construction cannot be built under the reserved keyed id at all.</summary>
    [TestMethod]
    public void RecomputedUnkeyedTagOverTamperedBytesIsRefused()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        (byte[] forged, ItemSegment segment) = WriteSegmentImage(SampleTriples(SampleTripleCount), KeyedHmac(TestKey));

        //Tamper the first payload block, then recompute EVERY stored block tag and the front-matter trailer as
        //valid unkeyed SHA-256 digests — the fully self-consistent unkeyed image is the strongest forgery a
        //key-less attacker can write, and only the keyed MAC recomputation distinguishes it from a clean read.
        int firstBlock = FirstBlockOffset(segment.BlockCount, KeyedTagWidth);
        forged[firstBlock] ^= 0xFF;
        int blockStride = ((BlockItemCount * ItemByteSize) + BlockAlignment - 1) / BlockAlignment * BlockAlignment;
        for(int block = 0; block < segment.BlockCount; block++)
        {
            int itemsInBlock = Math.Min(BlockItemCount, (int)SampleTripleCount - (block * BlockItemCount));
            SHA256.HashData(forged.AsSpan(firstBlock + (block * blockStride), itemsInBlock * ItemByteSize), forged.AsSpan(FrontMatterBase + (block * KeyedTagWidth), KeyedTagWidth));
        }

        int frontMatterEnd = FrontMatterBase + (segment.BlockCount * KeyedTagWidth);
        SHA256.HashData(forged.AsSpan(0, frontMatterEnd), forged.AsSpan(forged.Length - KeyedTagWidth, KeyedTagWidth));

        //The legitimate reader resolves the keyed id to the keyed MAC, recomputes each tag with the key, and finds
        //the attacker's unkeyed tags do not match — a loud refusal, not a read under the wrong algorithm.
        Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(forged, triplePool, KeyedResolver(TestKey)));
    }

    /// <summary>Case 2 (continued): a bare payload bit-flip against a stale keyed tag is refused — the keyed block MAC no longer matches the tampered block.</summary>
    [TestMethod]
    public void PayloadBitFlipAgainstStaleKeyedTagIsRefused()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        EncodedTriple[] triples = SampleTriples(SampleTripleCount);
        (byte[] image, ItemSegment segment) = WriteSegmentImage(triples, KeyedHmac(TestKey));

        //Flip a byte inside the first item block's payload, leaving every keyed tag at rest untouched.
        int payloadOffset = FirstBlockOffset(segment.BlockCount, KeyedTagWidth);
        image[payloadOffset] ^= 0xFF;

        Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(image, triplePool, KeyedResolver(TestKey)));
    }

    /// <summary>Case 3: a reader with no key (the default resolver, and the null resolver that falls back to it) cannot resolve the keyed id and refuses loudly rather than downgrading to a skip.</summary>
    [TestMethod]
    public void KeylessReaderRefusesTheKeyedId()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        (byte[] image, _) = WriteSegmentImage(SampleTriples(SampleTripleCount), KeyedHmac(TestKey));

        Assert.ThrowsExactly<NotSupportedException>(() => ItemSegment.ReadFrom(image, triplePool, ChecksumAlgorithm.DefaultResolver));
        Assert.ThrowsExactly<NotSupportedException>(() => ItemSegment.ReadFrom(image, triplePool, resolveChecksum: null));
    }

    /// <summary>Case 4: a reader that resolves the keyed id under a different key recomputes a different tag and refuses.</summary>
    [TestMethod]
    public void WrongKeyReaderIsRefused()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        (byte[] image, _) = WriteSegmentImage(SampleTriples(SampleTripleCount), KeyedHmac(TestKey));

        Assert.ThrowsExactly<InvalidDataException>(() => ItemSegment.ReadFrom(image, triplePool, KeyedResolver(WrongKey)));
    }

    /// <summary>A downgrading resolver stand-in: answers the reserved keyed id with the keyless built-in XxHash3 — the miswired composition the resolution witness must refuse rather than let verify.</summary>
    /// <param name="id">The requested on-disk id.</param>
    /// <returns>XxHash3 for the keyed id; the honest built-in mapping otherwise.</returns>
    private static ChecksumAlgorithm? DowngradeKeyedIdToXxHash3(byte id)
    {
        return id == ChecksumAlgorithm.KeyedHmacSha256Id ? ChecksumAlgorithm.XxHash3 : ChecksumAlgorithm.DefaultResolver(id);
    }

    /// <summary>A misrouting resolver stand-in: answers the CRC-32 id with XxHash3 — a resolver defect on an ordinary unkeyed id the witness must also refuse by identity.</summary>
    /// <param name="id">The requested on-disk id.</param>
    /// <returns>XxHash3 for the CRC-32 id; the honest built-in mapping otherwise.</returns>
    private static ChecksumAlgorithm? MisrouteCrc32ToXxHash3(byte id)
    {
        return id == ChecksumAlgorithm.Crc32.Id ? ChecksumAlgorithm.XxHash3 : ChecksumAlgorithm.DefaultResolver(id);
    }

    /// <summary>The resolution witness at the reader: a resolver that downgrades the keyed id to a keyless algorithm is refused by identity before any byte is verified — never consulted for verification.</summary>
    [TestMethod]
    public void DowngradingResolverIsRefusedBeforeAnyVerification()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        (byte[] image, _) = WriteSegmentImage(SampleTriples(SampleTripleCount), KeyedHmac(TestKey));

        Assert.ThrowsExactly<InvalidOperationException>(() => ItemSegment.ReadFrom(image, triplePool, DowngradeKeyedIdToXxHash3));
    }

    /// <summary>The same-width keyless substitute under the keyed id is foreclosed at the mint: a keyless construction cannot carry the reserved keyed id, so the substitute a downgrading resolver would answer with cannot exist. The read-side belt over the same class is <see cref="DowngradingResolverIsRefusedBeforeAnyVerification"/>.</summary>
    [TestMethod]
    public void SameWidthKeylessSubstituteUnderTheKeyedIdIsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => { _ = ChecksumAlgorithm.Create(ChecksumAlgorithm.KeyedHmacSha256Id, "SHA-256", KeyedTagWidth, (data, destination) => SHA256.HashData(data, destination)); });
    }

    /// <summary>The identity witness on an ordinary unkeyed id: a resolver answering the CRC-32 id with XxHash3 is refused by identity, not consumed into a wrong-algorithm verification.</summary>
    [TestMethod]
    public void MisroutingResolverIsRefusedBeforeAnyVerification()
    {
        using VeritasMemoryPool<EncodedTriple> triplePool = new();
        (byte[] image, _) = WriteSegmentImage(SampleTriples(SampleTripleCount), ChecksumAlgorithm.Crc32);

        Assert.ThrowsExactly<InvalidOperationException>(() => ItemSegment.ReadFrom(image, triplePool, MisrouteCrc32ToXxHash3));
    }

    /// <summary>Case 5: the built-in resolver never resolves the reserved keyed ids, so a key-less composition refuses a keyed image instead of downgrading it.</summary>
    [TestMethod]
    public void DefaultResolverDoesNotResolveKeyedIds()
    {
        Assert.IsNull(ChecksumAlgorithm.DefaultResolver(ChecksumAlgorithm.KeyedHmacSha256Id));
        Assert.IsNull(ChecksumAlgorithm.DefaultResolver(ChecksumAlgorithm.KeyedBlake2b256Id));
    }

    /// <summary>Case 6: a keyed engine reads its own persisted artifacts under the matching options, and a reopen under default options refuses the keyed artifacts loudly.</summary>
    [TestMethod]
    public async Task EngineReadsKeyedArtifactsAndDefaultOptionsRefuse()
    {
        string storeDirectory = Directory.CreateTempSubdirectory("veritas-keyed-engine-").FullName;
        try
        {
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);
            VeritasEngineOptions keyed = KeyedEngineOptions();

            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], keyed, TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "a", "p", "b", TestContext.CancellationToken).ConfigureAwait(false);

                //Persist writes the id-bearing artifacts (segments, manifest, CURRENT pointer) under the keyed algorithm.
                mutable.Persist(store);
            }

            //Reopen under the same keyed options: the keyed resolver verifies the keyed artifacts and serves them.
            {
                VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, keyed, TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = reopened.ConfigureAwait(false);
                Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}a> <{Ex}p> <{Ex}b> }}", TestContext.CancellationToken).ConfigureAwait(false), "The keyed engine did not read back its own keyed artifacts.");
            }

            //Reopen under default options: the keyed id is unresolvable, so the open refuses loudly (before any engine
            //exists to dispose) rather than serving a downgraded or partial recovery.
            await Assert.ThrowsExactlyAsync<NotSupportedException>(
                async () => await VeritasEngine.OpenMutableAsync(store, VeritasEngineOptions.Default, TestContext.CancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(storeDirectory, true);
        }
    }

    /// <summary>Case 7: the scoping pin — the append-only dataset journal stays on the built-in checksum regardless of the keyed options, so a commit written by a keyed-options engine replays under default options. This pins ONLY the journal half of the scoping boundary: the keyed-segment refusal half is <see cref="EngineReadsKeyedArtifactsAndDefaultOptionsRefuse"/>, and the two cannot co-occur in one reopen because a persisted keyed generation refuses at the CURRENT pointer before the journal is ever constructed.</summary>
    [TestMethod]
    public async Task JournalReplaysUnderBuiltInChecksumRegardlessOfKeyedOptions()
    {
        string root = Directory.CreateTempSubdirectory("veritas-keyed-journal-").FullName;
        try
        {
            string storeDirectory = Path.Combine(root, "store");
            Directory.CreateDirectory(storeDirectory);
            string journalPath = Path.Combine(root, "journal", "dataset.journal");
            FileSystemPersistenceStore store = new(storeDirectory, NoOpBarrier);

            //A keyed-options engine records a commit to the durable journal; the journal record carries no in-band
            //algorithm id, so it is written under the built-in checksum, not the keyed one. No store generation is
            //persisted, so the commit lives only in the journal.
            VeritasEngineOptions keyedWithJournal = KeyedEngineOptions() with { DatasetJournalPath = journalPath };
            {
                VeritasEngine mutable = await VeritasEngine.OpenMutableAsync([], keyedWithJournal, TestContext.CancellationToken).ConfigureAwait(false);
                await using var scope = mutable.ConfigureAwait(false);
                await InsertAsync(mutable, "j", "p", "k", TestContext.CancellationToken).ConfigureAwait(false);
            }

            //A DEFAULT-options reopen replays the journal successfully — the pin that the journal was never keyed.
            VeritasEngine reopened = await VeritasEngine.OpenMutableAsync(store, new VeritasEngineOptions { DatasetJournalPath = journalPath }, TestContext.CancellationToken).ConfigureAwait(false);
            await using var reopenedScope = reopened.ConfigureAwait(false);
            Assert.IsTrue(await AskAsync(reopened, $"ASK {{ <{Ex}j> <{Ex}p> <{Ex}k> }}", TestContext.CancellationToken).ConfigureAwait(false), "The journal did not replay under default options, so it was not on the built-in checksum.");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
