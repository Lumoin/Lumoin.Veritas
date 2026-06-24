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
/// The sketch image's own per-block checksums use XxHash3 (the shipped structural-sketch geometry), and this
/// store self-checksums its manifest and CURRENT pointer under the same algorithm. Superseded sketch
/// artifacts are not garbage-collected by this store (the manifest writer collects only manifests and CURRENT
/// pointers); a later increment can prune sketch artifacts below the oldest retained generation.
/// </para>
/// </remarks>
public sealed class DurableSketchStore
{
    /// <summary>The number of newest CURRENT generations the manifest writer retains as a recovery-fallback depth.</summary>
    private const int RetainedGenerationCount = 4;

    /// <summary>The algorithm the manifest, CURRENT pointer, and sketch-entry digest are checksummed under — XxHash3, matching the structural sketch image's own per-block checksums.</summary>
    private static ChecksumAlgorithm Checksum { get; } = ChecksumAlgorithm.XxHash3;

    /// <summary>The durable store the sketch generations are committed into and recovered from.</summary>
    private PersistenceStore Store { get; }

    /// <summary>The pool the transient image, digest, and projection buffers are rented from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a durable sketch store over a persistence store.</summary>
    /// <param name="store">The durable named-artifact store the sketch generations are committed into and recovered from.</param>
    /// <param name="pool">The pool the transient image, digest, and projection buffers are rented from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public DurableSketchStore(PersistenceStore store, MemoryPool<byte> pool)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pool);

        Store = store;
        Pool = pool;
    }

    /// <summary>
    /// Persists the feed's current generation's structural sketch as the next durable generation: it builds the
    /// sketch image at <paramref name="symbolBudget"/> symbols, stages it, and commits a manifest naming it,
    /// keyed by the generation's StateId and the given dictionary epoch, through the atomic CURRENT-pointer
    /// publish — so a crash leaves the prior committed sketch wholly in force or the new one wholly live.
    /// </summary>
    /// <param name="feed">The reconciliation feed whose current generation is persisted.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the feed's identifiers are numbered under (the engine's <c>Dictionary.Epoch</c>); recorded so a restart refuses a foreign-dictionary sketch.</param>
    /// <param name="symbolBudget">The number of coded symbols the persisted sketch carries; not negative.</param>
    /// <returns>The receipt: the committed generation, the StateId it was keyed to, and the image byte length.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="feed"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolBudget"/> is negative.</exception>
    public DurableSketchCommit Persist(ReplicationIndexFeed feed, ulong dictionaryEpoch, int symbolBudget)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentOutOfRangeException.ThrowIfNegative(symbolBudget);

        ReplicationGeneration generation = feed.Current();
        long commitGeneration = NextGeneration();
        string sketchName = SketchArtifactName(commitGeneration);

        using SlabBufferWriter writer = new(Pool);
        LocalSketchImage.Write(generation.Index, symbolBudget, Pool, writer);
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
        Manifest manifest = new(commitGeneration, (long)dictionaryEpoch, (long)generation.StateId.Value, entries);
        new ManifestWriter(Store, Checksum, Pool, RetainedGenerationCount).Commit(manifest);

        return new DurableSketchCommit(commitGeneration, generation.StateId, imageLength);
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
    /// <exception cref="NotSupportedException">A recovered image uses a checksum algorithm or format version this reader does not support — a reader incompatibility, not at-rest rot.</exception>
    public DurableSketchLoad TryLoad(ulong dictionaryEpoch)
    {
        ManifestRecovery recovery = new(Store);
        Manifest manifest;
        try
        {
            manifest = recovery.Recover().Manifest;
        }
        catch(InvalidDataException)
        {
            return new DurableSketchLoad(DurableSketchLoadOutcome.NotFound, default, NodeIdentifier.Empty, 0);
        }

        if(manifest.DictionaryEpoch != (long)dictionaryEpoch)
        {
            return new DurableSketchLoad(DurableSketchLoadOutcome.EpochMismatch, default, NodeIdentifier.Empty, manifest.CommitGeneration);
        }

        string? sketchName = FindSketchEntryName(manifest);
        if(sketchName is null)
        {
            return new DurableSketchLoad(DurableSketchLoadOutcome.NoSketchEntry, default, NodeIdentifier.Empty, manifest.CommitGeneration);
        }

        byte[]? imageBytes = Store.Read(sketchName);
        if(imageBytes is null)
        {
            return new DurableSketchLoad(DurableSketchLoadOutcome.Rejected, default, NodeIdentifier.Empty, manifest.CommitGeneration);
        }

        try
        {
            //Detection precedes use: the geometry and every block checksum must pass before the image is served.
            _ = SketchPersistence.LoadVerifiedSketch(imageBytes, SketchContract.Structural);
        }
        catch(InvalidDataException)
        {
            return new DurableSketchLoad(DurableSketchLoadOutcome.Rejected, default, NodeIdentifier.Empty, manifest.CommitGeneration);
        }

        NodeIdentifier stateId = new((ulong)manifest.ProvenanceEpoch);

        return new DurableSketchLoad(DurableSketchLoadOutcome.Loaded, imageBytes, stateId, manifest.CommitGeneration);
    }

    /// <summary>Resolves the generation to publish next: one past the last committed generation, or zero when the store holds none.</summary>
    /// <returns>The next monotonic commit generation.</returns>
    private long NextGeneration()
    {
        ManifestRecovery recovery = new(Store);
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

    /// <summary>Finds the file name of the manifest's sketch entry, or <see langword="null"/> when it names none.</summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <returns>The sketch artifact's store name, or <see langword="null"/>.</returns>
    private static string? FindSketchEntryName(Manifest manifest)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role.Code == ManifestFileRole.Sketch.Code)
            {
                return entry.FileName;
            }
        }

        return null;
    }

    /// <summary>The store name of the sketch artifact for a generation, zero-padded so a lexical listing matches generation order.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <returns>The artifact name.</returns>
    private static string SketchArtifactName(long generation)
    {
        return "sketch-" + generation.ToString("D20", CultureInfo.InvariantCulture) + ".skt";
    }
}
