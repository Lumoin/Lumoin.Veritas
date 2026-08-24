using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Persists a node's structural reconciliation sketch as a durable, generation-versioned artifact and loads
/// it back on restart, so a node serves its sketch from disk instead of re-deriving it from its feed every
/// boot. Each <see cref="Persist"/> writes the current <see cref="ReplicationIndexFeed"/> generation's sketch
/// image as a <see cref="ManifestFileRole.Sketch"/> entry committed through the shipped atomic manifest publish
/// (<see cref="ManifestWriter"/>), keyed by the dataset StateId (in the manifest's provenance epoch) and the
/// term-dictionary epoch (in the manifest's dictionary epoch). <see cref="TryLoad"/> recovers the live
/// generation (<see cref="ManifestRecovery"/>), refuses a foreign dictionary epoch, verifies the image
/// (<see cref="PersistenceInvariant.DetectionPrecedesUse"/>), and returns it with the StateId it reflects.
/// </summary>
/// <remarks>
/// <para>
/// The structural sketch carries term IDENTIFIERS, not terms, so a sketch is only meaningful against the
/// dictionary epoch it was numbered under. The epoch is recorded with the sketch and checked on load, so a
/// sketch from a foreign dictionary is refused (<see cref="DurableSketchLoadOutcome.EpochMismatch"/>) rather
/// than served as if it denoted local terms. The StateId is the content-addressed generation tag the feed
/// carries; a consumer compares the loaded StateId with the feed's current one to tell a current durable
/// sketch from a stale one.
/// </para>
/// <para>
/// The sketch image's own per-block checksums use XxHash3 (the shipped structural-sketch geometry). The
/// manifest, the CURRENT pointer, and the sketch entry's whole-image digest are checksummed under the algorithm
/// this store was constructed with — the built-in XxHash3 by default, or a host-composed keyed
/// message-authentication algorithm to make the manifest layer tamper-evident; the paired resolver verifies it on
/// read. Superseded sketch artifacts are pruned to the same retention window the manifest writer keeps (the newest
/// few generations) after each commit publishes, so a long-running node does not accumulate them unbounded and no
/// surviving manifest ever names a deleted sketch.
/// </para>
/// </remarks>
public sealed class DurableSketchStore
{
    /// <summary>The number of newest CURRENT generations the manifest writer retains as a recovery-fallback depth, and the matching window of sketch artifacts this store keeps.</summary>
    private const int RetainedGenerationCount = 4;

    /// <summary>The fixed prefix of a sketch artifact's store name; the zero-padded generation and the suffix follow it.</summary>
    private const string SketchArtifactPrefix = "sketch-";

    /// <summary>The fixed suffix of a sketch artifact's store name.</summary>
    private const string SketchArtifactSuffix = ".skt";

    /// <summary>The algorithm the manifest, CURRENT pointer, and sketch-entry digest are checksummed under when the host selects none — XxHash3, matching the structural sketch image's own per-block checksums.</summary>
    private static ChecksumAlgorithm DefaultChecksum { get; } = ChecksumAlgorithm.XxHash3;

    /// <summary>The algorithm this store writes the manifest, CURRENT pointer, and sketch-entry digest under: the host-supplied algorithm when one was given, else <see cref="DefaultChecksum"/>. A keyed algorithm makes the manifest layer tamper-evident; the paired resolver verifies it on read.</summary>
    private ChecksumAlgorithm Checksum { get; }

    /// <summary>The resolver every read verifies an artifact's on-disk checksum-algorithm id through, or <see langword="null"/> to use <see cref="ChecksumAlgorithm.DefaultResolver"/>. A keyed store supplies a resolver that maps the keyed id only when its key is present, so a read under absent or wrong key refuses rather than downgrading.</summary>
    private ResolveChecksumAlgorithmDelegate? ResolveChecksum { get; }

    /// <summary>The durable store the sketch generations are committed into and recovered from.</summary>
    private PersistenceStore Store { get; }

    /// <summary>The pool the transient image, digest, and projection buffers are rented from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a durable sketch store over a persistence store.</summary>
    /// <param name="store">The durable named-artifact store the sketch generations are committed into and recovered from.</param>
    /// <param name="pool">The pool the transient image, digest, and projection buffers are rented from.</param>
    /// <param name="checksum">The algorithm the manifest, CURRENT pointer, and sketch-entry digest are written under; <see langword="null"/> uses <see cref="DefaultChecksum"/> (byte-identical to prior releases). A host-composed keyed algorithm makes the manifest layer tamper-evident.</param>
    /// <param name="resolveChecksum">The resolver every read verifies an artifact's checksum-algorithm id through; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>. Supply the resolver that maps the keyed <paramref name="checksum"/>'s id (only when its key is present) to read a keyed store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public DurableSketchStore(PersistenceStore store, MemoryPool<byte> pool, ChecksumAlgorithm? checksum = null, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pool);

        Store = store;
        Pool = pool;
        Checksum = checksum ?? DefaultChecksum;
        ResolveChecksum = resolveChecksum;
    }

    /// <summary>
    /// Persists the maintained encoder's current generation's structural sketch as the next durable generation: it
    /// serves the sketch image at <paramref name="symbolBudget"/> symbols off the maintainer, stages it, and commits
    /// a manifest naming it, keyed by the served generation's StateId and the maintainer's dictionary epoch, through
    /// the atomic CURRENT-pointer publish — so a crash leaves the prior committed sketch wholly in force or the new
    /// one wholly live. The serve's receipt and its bytes are captured under the maintainer's one gate, so the
    /// manifest's StateId names exactly the set version the staged image encodes.
    /// </summary>
    /// <param name="maintainer">The maintained encoder whose current generation is persisted; it carries the dictionary epoch the manifest records so a restart refuses a foreign-dictionary sketch.</param>
    /// <param name="symbolBudget">The number of coded symbols the persisted sketch carries; not negative.</param>
    /// <returns>The receipt: the committed generation, the StateId it was keyed to, and the image byte length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="maintainer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolBudget"/> is negative.</exception>
    public DurableSketchCommit Persist(IncrementalSketchMaintainer maintainer, int symbolBudget)
    {
        ArgumentNullException.ThrowIfNull(maintainer);
        ArgumentOutOfRangeException.ThrowIfNegative(symbolBudget);

        long commitGeneration = NextGeneration();
        string sketchName = SketchArtifactName(commitGeneration);

        using SlabBufferWriter writer = new(Pool);
        SketchServeReceipt receipt = maintainer.WriteSketchImage(symbolBudget, Pool, writer);
        int imageLength = writer.BytesWritten;
        using IMemoryOwner<byte> imageOwner = writer.Detach();
        ReadOnlySpan<byte> image = imageOwner.Memory.Span[..imageLength];
        Store.WriteStaged(sketchName, image);

        //The digest must stay alive until Commit copies it into the manifest image, so it is rented for the
        //whole of the commit and released after.
        using IMemoryOwner<byte> digestOwner = Pool.Rent(Checksum.ByteWidth);
        ReadOnlyMemory<byte> digest = digestOwner.Memory[..Checksum.ByteWidth];
        Checksum.Compute(image, digestOwner.Memory.Span[..Checksum.ByteWidth]);

        ManifestEntry[] entries = [new ManifestEntry(ManifestFileRole.Sketch, sketchName, 0, imageLength, digest)];
        Manifest manifest = new(commitGeneration, (long)maintainer.DictionaryEpoch, (long)receipt.StateId.Value, entries);
        new ManifestWriter(Store, Checksum, Pool, RetainedGenerationCount).Commit(manifest);
        CollectSupersededSketchArtifacts(commitGeneration);

        return new DurableSketchCommit(commitGeneration, receipt.StateId, imageLength);
    }

    /// <summary>
    /// Loads the durably persisted structural sketch for the live committed generation, verifying it before
    /// returning. Returns a value-based outcome: <see cref="DurableSketchLoadOutcome.NotFound"/> when no
    /// generation is committed, <see cref="DurableSketchLoadOutcome.EpochMismatch"/> when the persisted sketch
    /// was numbered under a different dictionary epoch, <see cref="DurableSketchLoadOutcome.Rejected"/> when the
    /// sketch artifact is missing or fails its at-rest verification, and
    /// <see cref="DurableSketchLoadOutcome.Loaded"/> with the verified image and its StateId otherwise.
    /// </summary>
    /// <param name="dictionaryEpoch">The live term-dictionary epoch the loaded sketch must match (the engine's <c>Dictionary.Epoch</c>).</param>
    /// <returns>The load outcome, with the verified image and StateId when loaded.</returns>
    /// <exception cref="NotSupportedException">A recovered manifest, CURRENT pointer, or the sketch artifact itself uses a checksum algorithm or format version this reader does not support — a reader incompatibility, not at-rest rot, so it propagates rather than mapping to a value.</exception>
    public DurableSketchLoad TryLoad(ulong dictionaryEpoch)
    {
        ManifestRecovery recovery = new(Store, ResolveChecksum);
        Manifest manifest;
        try
        {
            manifest = recovery.Recover().Manifest;
        }
        catch(InvalidDataException)
        {
            return DurableSketchLoad.ForOutcome(DurableSketchLoadOutcome.NotFound, 0);
        }

        if(manifest.DictionaryEpoch != (long)dictionaryEpoch)
        {
            return DurableSketchLoad.ForOutcome(DurableSketchLoadOutcome.EpochMismatch, manifest.CommitGeneration);
        }

        if(FindSketchEntry(manifest) is not { } sketchEntry)
        {
            return DurableSketchLoad.ForOutcome(DurableSketchLoadOutcome.NoSketchEntry, manifest.CommitGeneration);
        }

        //The sketch image is RETAINED past this call (the returned load serves it to a peer), so it is read into a
        //pooled buffer the load owns rather than the transient memory-mapped seam. Ownership transfers to the
        //returned load only on success; on every other exit — a refusal, a corrupt-image InvalidDataException, or a
        //propagating reader-incompatibility NotSupportedException — the finally returns the rented buffer to the pool.
        PooledSegmentImageSource? imageSource = Store.OpenPooledImage(sketchEntry.FileName, Pool);
        bool ownershipTransferred = false;
        try
        {
            if(imageSource is null || !MatchesManifestEntry(imageSource.Image, sketchEntry))
            {
                //Missing, a length that differs from the manifest's record, or a whole-image digest mismatch — refused
                //before the sketch's own verification, so corruption the per-block checksums do not cover (alignment
                //padding) and truncation or over-length tampering are caught against the generation that named it.
                return DurableSketchLoad.ForOutcome(DurableSketchLoadOutcome.Rejected, manifest.CommitGeneration);
            }

            try
            {
                //Detection precedes use: the geometry and every block checksum must pass before the image is served.
                _ = SketchPersistence.LoadVerifiedSketch(imageSource.Image, SketchContract.Structural, ResolveChecksum);
            }
            catch(InvalidDataException)
            {
                return DurableSketchLoad.ForOutcome(DurableSketchLoadOutcome.Rejected, manifest.CommitGeneration);
            }

            NodeIdentifier stateId = new((ulong)manifest.ProvenanceEpoch);
            ownershipTransferred = true;

            //Ownership of the pooled image transfers to the returned load, which the caller disposes.
            return DurableSketchLoad.ForLoaded(imageSource, stateId, manifest.CommitGeneration);
        }
        finally
        {
            if(!ownershipTransferred)
            {
                imageSource?.Dispose();
            }
        }
    }

    /// <summary>
    /// Resolves the generation to publish next: one past the recovered generation, or zero when the store holds
    /// none. Under a normal (non-degraded) recovery this is strictly greater than the last committed generation, as
    /// <see cref="ManifestWriter"/> requires; if the control plane is damaged enough that recovery falls to the
    /// degraded scan, it is one past the highest verifying manifest, which the single-writer, atomic-publish model
    /// bounds.
    /// </summary>
    /// <returns>The next monotonic commit generation.</returns>
    private long NextGeneration()
    {
        ManifestRecovery recovery = new(Store, ResolveChecksum);
        try
        {
            return recovery.Recover().Manifest.CommitGeneration + 1;
        }
        catch(InvalidDataException)
        {
            //No committed generation exists yet, so the first generation is zero.
            return 0;
        }
    }

    /// <summary>Finds the manifest's sketch entry, or <see langword="null"/> when it names none.</summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <returns>The sketch entry, or <see langword="null"/>.</returns>
    private static ManifestEntry? FindSketchEntry(Manifest manifest)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role.Code == ManifestFileRole.Sketch.Code)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Verifies a read artifact against the length and digest the manifest recorded for it, binding the file to the
    /// generation that named it. This catches truncation, over-length tampering, and at-rest rot in regions the
    /// sketch's own per-block checksums do not cover (alignment padding). The digest is compared only when the
    /// manifest recorded it under this store's checksum algorithm (a matching digest width); a foreign-algorithm
    /// manifest is left to the sketch image's own verification.
    /// </summary>
    /// <param name="image">The bytes read from the store.</param>
    /// <param name="entry">The manifest entry that named the artifact.</param>
    /// <returns><see langword="true"/> when the artifact matches the manifest's recorded length and, where comparable, digest.</returns>
    private bool MatchesManifestEntry(ReadOnlySpan<byte> image, ManifestEntry entry)
    {
        if(image.Length != entry.ByteLength)
        {
            return false;
        }

        if(entry.Checksum.Length != Checksum.ByteWidth)
        {
            //The manifest recorded the digest under a different algorithm; the sketch image's own checksums verify it.
            return true;
        }

        using IMemoryOwner<byte> digestOwner = Pool.Rent(Checksum.ByteWidth);
        Span<byte> recomputed = digestOwner.Memory.Span[..Checksum.ByteWidth];
        Checksum.Compute(image, recomputed);

        return recomputed.SequenceEqual(entry.Checksum.Span);
    }

    /// <summary>The store name of the sketch artifact for a generation, zero-padded so a lexical listing matches generation order.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The artifact name.</returns>
    private static string SketchArtifactName(long generation)
    {
        return SketchArtifactPrefix + generation.ToString("D20", CultureInfo.InvariantCulture) + SketchArtifactSuffix;
    }

    /// <summary>
    /// Deletes sketch artifacts older than the manifest writer's retention window, so a long-running node does not
    /// accumulate them unbounded. The window matches <see cref="RetainedGenerationCount"/>, so a kept sketch always
    /// has a kept manifest; collection runs only after the commit has published, so no surviving manifest names a
    /// deleted sketch. Best-effort: a crash mid-collection leaves a harmless orphan reclaimed on the next commit.
    /// </summary>
    /// <param name="committedGeneration">The generation just committed.</param>
    private void CollectSupersededSketchArtifacts(long committedGeneration)
    {
        long oldestRetained = committedGeneration - RetainedGenerationCount + 1;
        if(oldestRetained <= 0)
        {
            //Every generation so far is still within the retention window.
            return;
        }

        foreach(string name in Store.List(SketchArtifactPrefix))
        {
            if(TryParseSketchGeneration(name, out long generation) && generation < oldestRetained)
            {
                Store.Delete(name);
            }
        }
    }

    /// <summary>Parses the generation a sketch artifact name encodes, or declines a name that does not match the sketch artifact shape.</summary>
    /// <param name="name">The store artifact name.</param>
    /// <param name="generation">The parsed generation when the name matches; 0 otherwise.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is a sketch artifact name.</returns>
    private static bool TryParseSketchGeneration(string name, out long generation)
    {
        generation = 0;
        if(!name.StartsWith(SketchArtifactPrefix, StringComparison.Ordinal) || !name.EndsWith(SketchArtifactSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        int start = SketchArtifactPrefix.Length;
        int length = name.Length - SketchArtifactPrefix.Length - SketchArtifactSuffix.Length;
        if(length <= 0)
        {
            return false;
        }

        return long.TryParse(name.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out generation);
    }
}
