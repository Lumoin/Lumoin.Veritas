using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Lumoin.Veritas.Core.Integrity;

namespace Lumoin.Veritas.Core.Persistence.Manifest;

/// <summary>
/// Recovers the committed state from a <see cref="PersistenceStore"/> by following the CURRENT
/// pointer, never by guessing the highest generation on disk. CURRENT is the single commit point
/// (<see cref="ManifestWriter"/>), so the generation it names is the one that was atomically committed;
/// a publish interrupted before the rename leaves the prior CURRENT — and therefore the prior committed
/// generation — wholly in force.
/// </summary>
/// <remarks>
/// <para>
/// Recovery descends three tiers. The primary path reads the live CURRENT and loads the manifest it
/// names. If the live CURRENT is missing or fails its self-checksum (at-rest rot), or its manifest is
/// missing or rotten, the retained fallback walks the retained per-generation CURRENT copies
/// newest-first and returns the newest one that verifies and whose manifest verifies — a committed,
/// possibly older, generation. Only when no CURRENT pointer survives at all does the degraded scan
/// (<see cref="RecoverFromDegradedScan"/>) read the manifests directly; that path is named-degraded
/// because it cannot prove the highest verifying manifest was committed rather than orphaned by a torn
/// publish.
/// </para>
/// <para>
/// At-rest corruption (a failed self-checksum, a malformed image) is skipped so an older committed
/// generation can still be recovered. A reader incompatibility (an unknown checksum-algorithm id, an
/// unsupported format version) is not corruption and is not skipped — it surfaces as the
/// <see cref="NotSupportedException"/> the image readers raise, rather than being masked as a recovery
/// to an older generation.
/// </para>
/// </remarks>
public sealed class ManifestRecovery
{
    /// <summary>The store recovered from.</summary>
    private readonly PersistenceStore store;

    /// <summary>Resolves a stored checksum-algorithm id on read, or <see langword="null"/> to use the built-in resolver.</summary>
    private readonly ResolveChecksumAlgorithmDelegate? resolveChecksum;

    /// <summary>Creates a recovery over <paramref name="store"/>.</summary>
    /// <param name="store">The store recovered from.</param>
    /// <param name="resolveChecksum">Resolves a stored checksum-algorithm id on read; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ManifestRecovery(PersistenceStore store, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        this.store = store;
        this.resolveChecksum = resolveChecksum;
    }

    /// <summary>Recovers the committed state: the live CURRENT first, then the newest verifying retained CURRENT, then the degraded direct scan as a last resort.</summary>
    /// <returns>The recovered generation, whether it came from the degraded scan, and whether a retained CURRENT copy attests it was committed.</returns>
    /// <exception cref="InvalidDataException">No CURRENT pointer and no manifest could be recovered — the store holds no recoverable committed state.</exception>
    /// <exception cref="NotSupportedException">A surviving image uses a checksum algorithm or format version this reader does not support.</exception>
    /// <remarks>
    /// The retained-CURRENT tier is where commit evidence is preferred: a retained <c>current-{g}</c> copy
    /// is written only after the CURRENT rename (<see cref="ManifestWriter.Commit"/>), so its existence is
    /// proof generation g was committed. That tier walks the copies newest-first and returns the newest
    /// committed generation before recovery ever reaches the evidence-less degraded scan, so an orphan of a
    /// torn publish is never chosen over a committed generation whose retained copy survived.
    /// </remarks>
    public RecoveryResult Recover()
    {
        byte[]? liveBytes = store.Read(ManifestNaming.CurrentPointerName);
        if(liveBytes is not null
            && TryReadPointerGeneration(liveBytes, out long liveGeneration)
            && TryReadManifest(liveGeneration, out Manifest? live))
        {
            return new RecoveryResult(live, false, true);
        }

        if(TryRecoverFromRetainedPointers(long.MaxValue, out RecoveryResult retained))
        {
            return retained;
        }

        return RecoverFromDegradedScan();
    }

    /// <summary>
    /// Recovers the newest committed state strictly below <paramref name="exclusiveUpperBound"/>, excluding the
    /// live CURRENT: the newest verifying retained CURRENT below the bound first, then the degraded direct scan
    /// below the bound. This is the artifact-failure fallback seam — when a generation's artifacts fail their at-rest
    /// verification, its loader excludes it (and every generation at or above it) and recovers the next candidate.
    /// </summary>
    /// <param name="exclusiveUpperBound">The exclusive upper generation bound; only generations strictly less than it are considered.</param>
    /// <returns>The recovered generation below the bound, whether it came from the degraded scan, and whether a retained CURRENT copy attests it was committed.</returns>
    /// <exception cref="InvalidDataException">No committed generation below the bound could be recovered — the candidates are exhausted.</exception>
    /// <exception cref="NotSupportedException">A surviving image uses a checksum algorithm or format version this reader does not support.</exception>
    internal RecoveryResult RecoverBelow(long exclusiveUpperBound)
    {
        if(TryRecoverFromRetainedPointers(exclusiveUpperBound, out RecoveryResult retained))
        {
            return retained;
        }

        return RecoverFromDegradedScan(exclusiveUpperBound);
    }

    /// <summary>Scans the manifests directly and returns the highest-stamped one below the bound that verifies, marked degraded — the named last resort when no CURRENT pointer survived.</summary>
    /// <param name="exclusiveUpperBound">The exclusive upper generation bound; only manifests strictly below it are scanned. Defaults to <see cref="long.MaxValue"/> (every manifest).</param>
    /// <returns>The highest verifying manifest below the bound, with <see cref="RecoveryResult.IsDegraded"/> set and <see cref="RecoveryResult.CommitEvidenced"/> set only when that generation still has a verifying retained CURRENT copy.</returns>
    /// <exception cref="InvalidDataException">No manifest below the bound verifies.</exception>
    /// <exception cref="NotSupportedException">A manifest or retained pointer uses a checksum algorithm or format version this reader does not support.</exception>
    /// <remarks>
    /// This path cannot distinguish the last committed generation from an orphan left by a publish that
    /// wrote its manifest but never reached the CURRENT rename, so it does not claim
    /// <see cref="PersistenceInvariant.PublishIsAtomic"/> exactness; the result is reported degraded so a
    /// caller can refuse to treat it as an exact recovery. It deliberately returns the highest verifying
    /// manifest regardless of evidence (so a diagnostic caller can see the orphan a torn publish left on
    /// disk) and instead labels the pick: <see cref="RecoveryResult.CommitEvidenced"/> is set only when a
    /// verifying retained <c>current-{g}</c> copy still attests the pick was committed. Preference for a
    /// committed generation over an orphan belongs to the retained-CURRENT tier of <see cref="Recover"/>,
    /// which runs first and consumes any surviving evidence, so the evidence-less pick here is the genuine
    /// no-proof-of-commit case.
    /// </remarks>
    public RecoveryResult RecoverFromDegradedScan(long exclusiveUpperBound = long.MaxValue)
    {
        List<long> generations = ListGenerations(ManifestNaming.ManifestPrefix);
        for(int i = generations.Count - 1; i >= 0; i--)
        {
            long generation = generations[i];
            if(generation >= exclusiveUpperBound)
            {
                continue;
            }

            if(TryReadManifest(generation, out Manifest? manifest))
            {
                return new RecoveryResult(manifest, true, TryRetainedCopyAttestsCommit(generation));
            }
        }

        throw new InvalidDataException("No recoverable committed manifest generation was found in the store.");
    }

    /// <summary>Walks the retained CURRENT copies newest-first below the bound and returns the newest one that verifies and whose manifest verifies.</summary>
    /// <param name="exclusiveUpperBound">The exclusive upper generation bound; only retained copies naming a generation strictly below it are followed.</param>
    /// <param name="result">The recovered result when one is found.</param>
    /// <returns><see langword="true"/> when a retained CURRENT below the bound yielded a verifying manifest.</returns>
    private bool TryRecoverFromRetainedPointers(long exclusiveUpperBound, out RecoveryResult result)
    {
        List<long> generations = ListGenerations(ManifestNaming.RetainedCurrentPrefix);
        for(int i = generations.Count - 1; i >= 0; i--)
        {
            if(generations[i] >= exclusiveUpperBound)
            {
                continue;
            }

            byte[]? pointerBytes = store.Read(ManifestNaming.RetainedCurrentName(generations[i]));
            if(pointerBytes is not null
                && TryReadPointerGeneration(pointerBytes, out long pointerGeneration)
                && pointerGeneration < exclusiveUpperBound
                && TryReadManifest(pointerGeneration, out Manifest? manifest))
            {
                result = new RecoveryResult(manifest, false, true);
                return true;
            }
        }

        result = default;

        return false;
    }

    /// <summary>The newest generation any surviving retained CURRENT copy attests was committed, independent of whether that generation's manifest or artifacts still verify — a retained copy is written only after the commit rename, so a verifying copy proves its generation was once live even when nothing else of it survives. This is the strongest surviving commit evidence, the rollback baseline a served generation is measured against; <see langword="null"/> when no retained copy survives.</summary>
    /// <returns>The newest commit-evidenced generation, or <see langword="null"/>.</returns>
    /// <exception cref="NotSupportedException">A retained pointer uses a checksum algorithm or format version this reader does not support.</exception>
    internal long? HighestCommitEvidencedGeneration()
    {
        List<long> generations = ListGenerations(ManifestNaming.RetainedCurrentPrefix);
        for(int i = generations.Count - 1; i >= 0; i--)
        {
            if(TryRetainedCopyAttestsCommit(generations[i]))
            {
                return generations[i];
            }
        }

        return null;
    }

    /// <summary>Reports whether a verifying retained CURRENT copy for a generation exists — the commit evidence a degraded pick is labelled with, because a retained copy is written only after the commit rename.</summary>
    /// <param name="generation">The generation whose retained CURRENT copy is checked.</param>
    /// <returns><see langword="true"/> when a retained <c>current-{generation}</c> exists, verifies its self-checksum, and names that generation.</returns>
    /// <exception cref="NotSupportedException">The retained pointer uses a checksum algorithm or format version this reader does not support.</exception>
    private bool TryRetainedCopyAttestsCommit(long generation)
    {
        byte[]? pointerBytes = store.Read(ManifestNaming.RetainedCurrentName(generation));

        return pointerBytes is not null
            && TryReadPointerGeneration(pointerBytes, out long named)
            && named == generation;
    }

    /// <summary>Lists the generations stamped by the artifacts under a prefix, ascending, skipping names that do not parse.</summary>
    /// <param name="prefix">The artifact-name prefix (<see cref="ManifestNaming.ManifestPrefix"/> or <see cref="ManifestNaming.RetainedCurrentPrefix"/>).</param>
    /// <returns>The parsed generations in ascending order.</returns>
    private List<long> ListGenerations(string prefix)
    {
        List<long> generations = [];
        foreach(string name in store.List(prefix))
        {
            if(ManifestNaming.TryParseGeneration(name, prefix, out long generation))
            {
                generations.Add(generation);
            }
        }

        generations.Sort();

        return generations;
    }

    /// <summary>Reads a CURRENT pointer image and returns the generation it names, treating at-rest rot as a skip rather than a failure.</summary>
    /// <param name="pointerBytes">The pointer image.</param>
    /// <param name="generation">The named generation when the image verifies; 0 otherwise.</param>
    /// <returns><see langword="true"/> when the image is a verifying CURRENT pointer.</returns>
    /// <exception cref="NotSupportedException">The pointer uses a checksum algorithm or format version this reader does not support.</exception>
    private bool TryReadPointerGeneration(byte[] pointerBytes, out long generation)
    {
        try
        {
            generation = CurrentPointer.ReadFrom(pointerBytes, resolveChecksum).CommitGeneration;

            return true;
        }
        catch(InvalidDataException)
        {
            generation = 0;

            return false;
        }
    }

    /// <summary>Reads the manifest image for a generation, treating a missing file or at-rest rot as a skip rather than a failure.</summary>
    /// <param name="generation">The generation whose manifest is read.</param>
    /// <param name="manifest">The verified manifest when present and valid; <see langword="null"/> otherwise.</param>
    /// <returns><see langword="true"/> when the manifest is present and verifies.</returns>
    /// <exception cref="NotSupportedException">The manifest uses a checksum algorithm or format version this reader does not support.</exception>
    private bool TryReadManifest(long generation, [NotNullWhen(true)] out Manifest? manifest)
    {
        byte[]? manifestBytes = store.Read(ManifestNaming.ManifestName(generation));
        if(manifestBytes is null)
        {
            manifest = null;

            return false;
        }

        try
        {
            manifest = Manifest.ReadFrom(manifestBytes, resolveChecksum);

            return true;
        }
        catch(InvalidDataException)
        {
            manifest = null;

            return false;
        }
    }
}
