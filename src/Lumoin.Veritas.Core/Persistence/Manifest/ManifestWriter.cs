using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// Commits a manifest generation into a <see cref="PersistenceStore"/> through the single
/// atomic-publish commit point. A generation's manifest image and the data, sidecar, and sketch files
/// it names are staged and flushed before the one rename that makes it live, so a crash at any point
/// either leaves the prior committed generation wholly in force or makes the new one wholly live, never
/// a torn mix (<see cref="PersistenceInvariant.PublishIsAtomic"/>). Whether that commit is durable in
/// full at the instant it is acknowledged is conditional on the platform reach of the post-rename
/// directory barrier: on Linux and the Apple platforms the barrier flushes the parent directory, so the
/// live pointer is on stable storage before the acknowledgement; on Windows there is no public
/// directory-fsync API, so the barrier is a no-op and the acknowledgement can precede the rename's
/// durability. NTFS metadata journaling keeps the rename crash-consistent and eventually durable, but a
/// power loss shortly after an acknowledged commit can revert to the prior committed generation — which
/// stays wholly intact, so atomicity holds even where ack-durability does not.
/// </summary>
/// <remarks>
/// <para>
/// The commit order is: stage the manifest image durably; publish the CURRENT pointer by one atomic
/// rename (the commit point); then write the retained per-generation CURRENT copy. The retained copy
/// is written <em>after</em> the publish, never before, so a retained <c>current-{gen}</c> exists only
/// for a generation that was actually committed — recovery's retained fallback therefore never mistakes
/// a generation whose publish was interrupted for a committed one. A crash between the publish and the
/// retained-copy write simply omits that one retained copy; the generation is committed (the live
/// CURRENT names it) and the fallback's retention depth for it is one shallower, which is a safe
/// degradation, not data loss.
/// </para>
/// <para>
/// Garbage collection keeps the newest few retained CURRENT copies and deletes the older ones together
/// with the manifests they named, but never a manifest at or above the oldest retained generation — so
/// no surviving CURRENT pointer is left naming a deleted manifest. The caller assigns a strictly
/// increasing commit generation to each <see cref="Commit"/> (typically the recovered generation plus
/// one); the writer is the mechanism, not the generation-assignment policy, and a single store is
/// committed to by one writer at a time.
/// </para>
/// </remarks>
public sealed class ManifestWriter
{
    /// <summary>The store the generation is committed into.</summary>
    private readonly PersistenceStore store;

    /// <summary>The checksum algorithm the manifest and CURRENT pointer are self-checksummed under, or <see langword="null"/> for none.</summary>
    private readonly ChecksumAlgorithm? checksum;

    /// <summary>The pool the staging buffers are rented from.</summary>
    private readonly MemoryPool<byte> bufferPool;

    /// <summary>The number of newest retained CURRENT copies kept; older copies and their manifests are collected.</summary>
    private readonly int retainedCurrentPointerCount;

    /// <summary>Creates a writer that commits into <paramref name="store"/>.</summary>
    /// <param name="store">The store the generations are committed into.</param>
    /// <param name="checksum">The checksum algorithm the manifest and CURRENT pointer are self-checksummed under, or <see langword="null"/> for none; production selects one so at-rest rot is detectable.</param>
    /// <param name="bufferPool">The pool the staging buffers are rented from.</param>
    /// <param name="retainedCurrentPointerCount">The number of newest retained CURRENT copies to keep (at least 1); these bound the at-rest-rot fallback depth.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retainedCurrentPointerCount"/> is less than 1.</exception>
    public ManifestWriter(PersistenceStore store, ChecksumAlgorithm? checksum, MemoryPool<byte> bufferPool, int retainedCurrentPointerCount)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(bufferPool);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedCurrentPointerCount, 1);

        this.store = store;
        this.checksum = checksum;
        this.bufferPool = bufferPool;
        this.retainedCurrentPointerCount = retainedCurrentPointerCount;
    }

    /// <summary>Commits a generation: stages its manifest image durably, atomically publishes the CURRENT pointer naming it, writes the retained CURRENT copy, then collects superseded generations.</summary>
    /// <param name="manifest">The generation to commit; its commit generation must be strictly greater than the last committed one (the caller's precondition).</param>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A manifest entry's checksum width does not match the writer's algorithm.</exception>
    public void Commit(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        long generation = manifest.CommitGeneration;

        StageManifest(manifest, generation);
        PublishAndRetainPointer(generation);
        CollectSupersededGenerations(generation);
    }

    /// <summary>Writes the manifest image durably under its generation-stamped name; a half-written image after a crash is never referenced because CURRENT still names the prior generation.</summary>
    /// <param name="manifest">The generation whose image is staged.</param>
    /// <param name="generation">The commit generation.</param>
    private void StageManifest(Manifest manifest, long generation)
    {
        int size = manifest.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = bufferPool.Rent(size);
        Span<byte> buffer = owner.Memory.Span[..size];
        manifest.WriteTo(buffer, checksum);
        store.WriteStaged(ManifestNaming.ManifestName(generation), buffer);
    }

    /// <summary>Publishes the CURRENT pointer naming the generation by one atomic rename — the commit point — then writes the retained CURRENT copy after the publish.</summary>
    /// <param name="generation">The commit generation made live.</param>
    private void PublishAndRetainPointer(long generation)
    {
        CurrentPointer pointer = new(generation);
        int size = CurrentPointer.ComputeSerializedSize(checksum);
        using IMemoryOwner<byte> owner = bufferPool.Rent(size);
        Span<byte> buffer = owner.Memory.Span[..size];
        pointer.WriteTo(buffer, checksum);

        store.WriteStaged(ManifestNaming.CurrentStagingName, buffer);
        store.Publish(ManifestNaming.CurrentStagingName, ManifestNaming.CurrentPointerName);

        //The retained copy is written only after the publish, so a retained current-{gen} marks a generation that was actually committed.
        store.WriteStaged(ManifestNaming.RetainedCurrentName(generation), buffer);
    }

    /// <summary>Deletes retained CURRENT copies and manifests older than the retention window, keeping every manifest at or above the oldest retained generation so no surviving CURRENT names a deleted manifest.</summary>
    /// <param name="committedGeneration">The just-committed generation, always kept.</param>
    private void CollectSupersededGenerations(long committedGeneration)
    {
        List<long> retainedGenerations = [];
        foreach(string name in store.List(ManifestNaming.RetainedCurrentPrefix))
        {
            if(ManifestNaming.TryParseGeneration(name, ManifestNaming.RetainedCurrentPrefix, out long generation))
            {
                retainedGenerations.Add(generation);
            }
        }

        if(!retainedGenerations.Contains(committedGeneration))
        {
            retainedGenerations.Add(committedGeneration);
        }

        retainedGenerations.Sort();

        int keepFrom = Math.Max(0, retainedGenerations.Count - retainedCurrentPointerCount);
        long oldestKeptGeneration = retainedGenerations[keepFrom];

        //Delete a superseded retained CURRENT copy before its manifest, so a crash never leaves a retained CURRENT naming a missing manifest.
        for(int i = 0; i < keepFrom; i++)
        {
            store.Delete(ManifestNaming.RetainedCurrentName(retainedGenerations[i]));
        }

        //Pin every manifest at or above the oldest retained generation; that set contains every manifest a surviving CURRENT (live or retained) can name.
        foreach(string name in store.List(ManifestNaming.ManifestPrefix))
        {
            if(ManifestNaming.TryParseGeneration(name, ManifestNaming.ManifestPrefix, out long generation) && generation < oldestKeptGeneration)
            {
                store.Delete(ManifestNaming.ManifestName(generation));
            }
        }
    }
}
