using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Persistence.Manifest;
using Lumoin.Veritas.Core.Persistence.Segment;
using ManifestGeneration = Lumoin.Veritas.Core.Persistence.Manifest.Manifest;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// Persists a database's durable, non-re-derivable core — the term dictionary and the row-major
/// system-of-record triples — as one generation-versioned manifest generation, and recovers it on restart so
/// a database opens over the store warm rather than re-ingesting its source. Each <see cref="Persist"/> writes
/// a <see cref="DictionarySegment"/> (the <see cref="ManifestFileRole.Dictionary"/> artifact) and an
/// <see cref="ItemSegment"/> (the <see cref="ManifestFileRole.DataSegment"/> artifact) and commits a manifest
/// naming both through the shipped atomic CURRENT-pointer publish (<see cref="ManifestWriter"/>), keyed by the
/// dictionary epoch. <see cref="TryLoad"/> recovers the live generation
/// (<see cref="ManifestRecovery"/>), verifies each artifact against the length and digest the manifest
/// recorded, and returns the reconstructed dictionary and the verified triples.
/// </summary>
/// <remarks>
/// <para>
/// The dictionary and the system-of-record are the integrity root: the encoded triples are meaningless without
/// the dictionary that decodes them, and neither is re-derivable from the other, so both are committed together
/// as one generation under one dictionary epoch. The columnar (Elias-Fano) query index is a re-derivable sidecar:
/// an optional <see cref="ManifestFileRole.Sidecar"/> artifact a caller may persist alongside so a restart loads
/// it warm — no re-sort, no re-pack — instead of rebuilding it from the triples; a missing or corrupt sidecar is
/// simply re-derived, never a load failure. An optional <see cref="ManifestFileRole.Sketch"/> artifact carries
/// the generation's own integrity sketch — the at-rest record the repair pass's peer-reconciliation faithfulness
/// gates verify a healed set against, so a generation persisted with one is peer-repairable. Superseded artifacts are
/// pruned to the same retention window the manifest writer keeps, after each commit publishes, so a long-running
/// database does not accumulate them and no surviving manifest names a deleted artifact.
/// </para>
/// </remarks>
public sealed class DurableSystemOfRecordStore
{
    /// <summary>The number of newest CURRENT generations the manifest writer retains, and the matching window of artifacts this store keeps.</summary>
    private const int RetainedGenerationCount = 4;

    /// <summary>The fixed prefix of a dictionary artifact's store name; the zero-padded generation and the suffix follow it.</summary>
    private const string DictionaryArtifactPrefix = "dict-";

    /// <summary>The fixed suffix of a dictionary artifact's store name.</summary>
    private const string DictionaryArtifactSuffix = ".dic";

    /// <summary>The fixed prefix of a (default-graph) system-of-record artifact's store name.</summary>
    private const string RecordArtifactPrefix = "sor-";

    /// <summary>The fixed suffix of a system-of-record artifact's store name.</summary>
    private const string RecordArtifactSuffix = ".sor";

    /// <summary>The fixed prefix of a NAMED-graph system-of-record artifact's store name; distinct from the default's so a prefix listing never conflates the two.</summary>
    private const string NamedRecordArtifactPrefix = "nsor-";

    /// <summary>The infix separating the generation from the graph-name term id in a named-graph artifact name (<c>nsor-&lt;generation&gt;-g&lt;graphTermId&gt;.sor</c>).</summary>
    private const string NamedGraphNameInfix = "-g";

    /// <summary>The fixed prefix of a columnar-sidecar artifact's store name.</summary>
    private const string SidecarArtifactPrefix = "cidx-";

    /// <summary>The fixed suffix of a columnar-sidecar artifact's store name.</summary>
    private const string SidecarArtifactSuffix = ".cidx";

    /// <summary>The fixed prefix of a generation integrity-sketch artifact's store name.</summary>
    private const string SketchArtifactPrefix = "isk-";

    /// <summary>The fixed suffix of a generation integrity-sketch artifact's store name.</summary>
    private const string SketchArtifactSuffix = ".skt";

    /// <summary>The fixed prefix of a value-index sidecar artifact's store name.</summary>
    private const string ValueIndexArtifactPrefix = "vidx-";

    /// <summary>The fixed suffix of a value-index sidecar artifact's store name.</summary>
    private const string ValueIndexArtifactSuffix = ".vidx";

    /// <summary>The manifest role a value-index sidecar artifact is named under, created through the open role mechanism (a reader that does not recognise the code skips the entry, so an older reader serves the generation without it).</summary>
    private static ManifestFileRole ValueIndexSidecarRole { get; } = ManifestFileRole.Create(9, "ValueIndexSidecar");

    /// <summary>The fixed prefix of a replication causality artifact's store name.</summary>
    private const string CausalityArtifactPrefix = "rcl-";

    /// <summary>The fixed suffix of a replication causality artifact's store name.</summary>
    private const string CausalityArtifactSuffix = ".rcl";

    /// <summary>The manifest role the replication causality artifact — a remove-aware database's dotted-ledger snapshot, captured from the same committed state as the system of record — is named under, created through the open role mechanism (a reader that does not recognise the code skips the entry, so an older reader serves the generation without it).</summary>
    private static ManifestFileRole ReplicationCausalityRole { get; } = ManifestFileRole.Create(10, "ReplicationCausality");

    /// <summary>The number of triples per system-of-record block; a block boundary is a triple boundary so a block's checksum names its exact item range.</summary>
    private const int RecordBlockItemCount = 4096;

    /// <summary>
    /// The default provenance epoch a generation is stamped with when the caller binds no committed dataset state
    /// (a direct persist of caller-held triples). When a live database persists, its captured dataset state
    /// identifier is threaded in as the provenance epoch instead: the provenance epoch IS the persisted dataset
    /// state id, the recovery-side cross-check affordance a recovered generation is matched against a journal head
    /// with. Zero is the no-state-bound default.
    /// </summary>
    private const long SystemOfRecordProvenanceEpoch = 0;

    /// <summary>The algorithm the manifest, CURRENT pointer, and the two artifacts' digests are checksummed under when the host selects none — the built-in default, byte-identical to prior releases.</summary>
    private static ChecksumAlgorithm DefaultChecksum { get; } = ChecksumAlgorithm.XxHash3;

    /// <summary>The algorithm this store writes the manifest, CURRENT pointer, and every artifact's digest under: the host-supplied algorithm when one was given, else <see cref="DefaultChecksum"/>. A keyed algorithm makes the written artifacts tamper-evident; the paired resolver verifies them on read.</summary>
    private ChecksumAlgorithm Checksum { get; }

    /// <summary>The resolver every read verifies an artifact's on-disk checksum-algorithm id through, or <see langword="null"/> to use <see cref="ChecksumAlgorithm.DefaultResolver"/>. A keyed store supplies a resolver that maps the keyed id only when its key is present, so a read under absent or wrong key refuses rather than downgrading.</summary>
    private ResolveChecksumAlgorithmDelegate? ResolveChecksum { get; }

    /// <summary>The durable store the generations are committed into and recovered from.</summary>
    private PersistenceStore Store { get; }

    /// <summary>The pool the transient image and digest buffers are rented from.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>Creates a durable system-of-record store over a persistence store.</summary>
    /// <param name="store">The durable named-artifact store the generations are committed into and recovered from.</param>
    /// <param name="pool">The pool the transient image and digest buffers are rented from.</param>
    /// <param name="checksum">The algorithm the manifest, CURRENT pointer, and artifact digests are written under; <see langword="null"/> uses <see cref="DefaultChecksum"/> (byte-identical to prior releases). A host-composed keyed algorithm makes the written artifacts tamper-evident.</param>
    /// <param name="resolveChecksum">The resolver every read verifies an artifact's checksum-algorithm id through; <see langword="null"/> uses <see cref="ChecksumAlgorithm.DefaultResolver"/>. Supply the resolver that maps the keyed <paramref name="checksum"/>'s id (only when its key is present) to read a keyed store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> or <paramref name="pool"/> is <see langword="null"/>.</exception>
    public DurableSystemOfRecordStore(PersistenceStore store, MemoryPool<byte> pool, ChecksumAlgorithm? checksum = null, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pool);

        Store = store;
        Pool = pool;
        Checksum = checksum ?? DefaultChecksum;
        ResolveChecksum = resolveChecksum;
    }

    /// <summary>
    /// Persists the dictionary and the system-of-record triples as the next durable generation: it serializes
    /// both segments, stages them, and commits a manifest naming both — keyed by the dictionary epoch — through
    /// the atomic CURRENT-pointer publish, so a crash leaves the prior committed generation wholly in force or
    /// the new one wholly live.
    /// </summary>
    /// <param name="dictionary">The term dictionary; its epoch is recorded so a restart refuses a foreign-dictionary generation. The one shared dictionary interns every graph's terms and the graph-name IRIs.</param>
    /// <param name="triples">The default-graph system-of-record triples, encoded against <paramref name="dictionary"/>.</param>
    /// <param name="namedGraphs">The named graphs, each its graph-name term id (interned in <paramref name="dictionary"/>) paired with that graph's triples; <see langword="null"/> or empty persists a default-graph-only generation.</param>
    /// <param name="sidecar">The default-graph columnar query index to persist alongside as a re-derivable warm-start sidecar, or <see langword="null"/> to persist no sidecar (a restart rebuilds the index from the triples).</param>
    /// <param name="valueIndexes">The value-index sidecar image to persist alongside as a second re-derivable warm-start sidecar — the registered access methods' snapshots built from the same captured default graph, stamped with the dataset state identifier — or <see langword="null"/> to persist none (a restart rebuilds the value indexes from the triples at the first probe).</param>
    /// <param name="provenanceEpoch">The provenance epoch stamped into the manifest: the dataset state identifier the persisted generation reflects, so a recovery can cross-check a recovered generation against a journal head. Defaults to <see cref="SystemOfRecordProvenanceEpoch"/> when the caller binds no committed dataset state.</param>
    /// <param name="integritySketch">The generation's serialized integrity sketch over the default-graph system of record (the <see cref="ManifestFileRole.Sketch"/> artifact) — the independent pre-damage record a repair pass's peer-reconciliation faithfulness gates verify a healed set against; empty persists none, which leaves those gates unsourced so a peer heal of this generation declines fail-closed.</param>
    /// <param name="causalitySnapshot">The serialized replication causality artifact — a remove-aware database's dotted-ledger snapshot, captured from the SAME committed state as <paramref name="triples"/> and paired with it by dataset StateId; empty persists none (an add-only database, byte-identical to prior generations).</param>
    /// <returns>The receipt: the committed generation, the dictionary epoch, the term count, and the total triple count across all graphs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dictionary"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A segment image would exceed the single-image size limit; split the dataset across generations.</exception>
    public DurableSystemOfRecordCommit Persist(
        TermDictionary dictionary,
        ReadOnlyMemory<EncodedTriple> triples,
        IReadOnlyList<(TermId GraphName, ReadOnlyMemory<EncodedTriple> Triples)>? namedGraphs = null,
        ColumnarTripleIndex? sidecar = null,
        ValueIndexImage? valueIndexes = null,
        long provenanceEpoch = SystemOfRecordProvenanceEpoch,
        ReadOnlyMemory<byte> integritySketch = default,
        ReadOnlyMemory<byte> causalitySnapshot = default)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        long generation = NextGeneration();

        //Sizes (and the system-of-record size guard) are computed before any artifact is staged, so an oversized
        //record never leaves a staged dictionary behind.
        string dictionaryName = ArtifactName(DictionaryArtifactPrefix, generation, DictionaryArtifactSuffix);
        DictionarySegment dictionarySegment = new(dictionary);
        int dictionarySize = dictionarySegment.ComputeSerializedSize(Checksum);

        string recordName = ArtifactName(RecordArtifactPrefix, generation, RecordArtifactSuffix);
        ItemSegment recordSegment = new(triples, RecordBlockItemCount);
        int recordSize = SegmentImageSize(recordSegment);

        List<ManifestEntry> entries = [];
        int totalTriples = triples.Length;

        //Every artifact's image and digest stay rented through the manifest commit, which copies the digests into
        //the manifest image; they are released together after the commit publishes.
        List<IMemoryOwner<byte>> rentedThroughCommit = [];
        try
        {
            IMemoryOwner<byte> dictionaryImage = Pool.Rent(dictionarySize);
            dictionarySegment.WriteTo(dictionaryImage.Memory.Span[..dictionarySize], Checksum);
            StageArtifact(dictionaryName, dictionaryImage, dictionarySize, ManifestFileRole.Dictionary, entries, rentedThroughCommit);

            IMemoryOwner<byte> recordImage = Pool.Rent(recordSize);
            recordSegment.WriteTo(recordImage.Memory.Span[..recordSize], Checksum);
            StageArtifact(recordName, recordImage, recordSize, ManifestFileRole.DataSegment, entries, rentedThroughCommit);

            if(namedGraphs is not null)
            {
                foreach((TermId graphName, ReadOnlyMemory<EncodedTriple> graphTriples) in namedGraphs)
                {
                    string namedName = NamedGraphArtifactName(generation, graphName);
                    ItemSegment namedSegment = new(graphTriples, RecordBlockItemCount);
                    int namedSize = SegmentImageSize(namedSegment);

                    IMemoryOwner<byte> namedImage = Pool.Rent(namedSize);
                    namedSegment.WriteTo(namedImage.Memory.Span[..namedSize], Checksum);
                    StageArtifact(namedName, namedImage, namedSize, ManifestFileRole.NamedGraphSegment, entries, rentedThroughCommit);

                    totalTriples += graphTriples.Length;
                }
            }

            //The columnar (Elias-Fano) index is an optional, re-derivable warm-start sidecar; when supplied it is
            //staged and named so a restart loads it with no re-sort or re-pack.
            if(sidecar is not null)
            {
                string sidecarName = ArtifactName(SidecarArtifactPrefix, generation, SidecarArtifactSuffix);
                int sidecarSize = sidecar.ComputeSerializedSize(Checksum);
                IMemoryOwner<byte> sidecarImage = Pool.Rent(sidecarSize);
                sidecar.WriteTo(sidecarImage.Memory.Span[..sidecarSize], Checksum);
                StageArtifact(sidecarName, sidecarImage, sidecarSize, ManifestFileRole.Sidecar, entries, rentedThroughCommit);
            }

            //The value-index sidecar is likewise re-derivable and optional: the registered access methods'
            //snapshots built from the same captured default graph, stamped with the dataset state identifier the
            //recovery validates against the manifest's provenance epoch before any warm install.
            if(valueIndexes is not null)
            {
                string valueIndexName = ArtifactName(ValueIndexArtifactPrefix, generation, ValueIndexArtifactSuffix);
                int valueIndexSize = valueIndexes.ComputeSerializedSize();
                IMemoryOwner<byte> valueIndexImage = Pool.Rent(valueIndexSize);
                valueIndexes.WriteTo(valueIndexImage.Memory.Span[..valueIndexSize]);
                StageArtifact(valueIndexName, valueIndexImage, valueIndexSize, ValueIndexSidecarRole, entries, rentedThroughCommit);
            }

            //The integrity sketch is the generation's own at-rest record of its item set — the authority the
            //repair pass's peer-reconciliation faithfulness gates peel a healed set against, so a store-backed
            //generation is peer-repairable at all. Staged like any other artifact under the Sketch role; the
            //caller builds the image through the shared sketch persistence framing so the scrub verify pass
            //reads it back block-checked.
            if(!integritySketch.IsEmpty)
            {
                string sketchName = ArtifactName(SketchArtifactPrefix, generation, SketchArtifactSuffix);
                int sketchSize = integritySketch.Length;
                IMemoryOwner<byte> sketchImage = Pool.Rent(sketchSize);
                integritySketch.Span.CopyTo(sketchImage.Memory.Span[..sketchSize]);
                StageArtifact(sketchName, sketchImage, sketchSize, ManifestFileRole.Sketch, entries, rentedThroughCommit);
            }

            //The replication causality artifact rides the generation like the sketch: staged under its open
            //role, digest-verified at load, retention-pruned with its generation. It is causal knowledge, not
            //triple content — a reader that does not recognise the role serves the generation without it, and
            //recovery without a loadable artifact falls to the explicit baseline rule rather than serving
            //degraded knowledge silently.
            if(!causalitySnapshot.IsEmpty)
            {
                string causalityName = ArtifactName(CausalityArtifactPrefix, generation, CausalityArtifactSuffix);
                int causalitySize = causalitySnapshot.Length;
                IMemoryOwner<byte> causalityImage = Pool.Rent(causalitySize);
                causalitySnapshot.Span.CopyTo(causalityImage.Memory.Span[..causalitySize]);
                StageArtifact(causalityName, causalityImage, causalitySize, ReplicationCausalityRole, entries, rentedThroughCommit);
            }

            ManifestGeneration manifest = new(generation, (long)dictionary.Epoch, provenanceEpoch, entries);
            new ManifestWriter(Store, Checksum, Pool, RetainedGenerationCount).Commit(manifest);
            CollectSupersededArtifacts(generation);
        }
        finally
        {
            foreach(IMemoryOwner<byte> buffer in rentedThroughCommit)
            {
                buffer.Dispose();
            }
        }

        return new DurableSystemOfRecordCommit(generation, dictionary.Epoch, dictionary.Count, totalTriples);
    }

    /// <summary>
    /// Stages one already-written artifact image durably, computes its digest, appends its manifest entry, and
    /// records the image and digest buffers for release after the commit publishes — both must outlive the manifest
    /// commit, which copies the digest into the manifest image. The caller rents the image and writes its segment
    /// into it (each segment type has its own <c>WriteTo</c>), then hands it here.
    /// </summary>
    /// <param name="name">The artifact's store name.</param>
    /// <param name="image">The rented image the caller has written the segment into; this method records it for release after the commit.</param>
    /// <param name="size">The written image's byte length.</param>
    /// <param name="role">The manifest file role the artifact plays.</param>
    /// <param name="entriesToAppendTo">The caller's manifest-entry list this artifact's entry is APPENDED to (an in/out accumulator, not just read).</param>
    /// <param name="rentedBuffersToAppendTo">The caller's release-after-commit buffer list the image and its freshly-rented digest are APPENDED to (an in/out accumulator, not just read).</param>
    private void StageArtifact(string name, IMemoryOwner<byte> image, int size, ManifestFileRole role, List<ManifestEntry> entriesToAppendTo, List<IMemoryOwner<byte>> rentedBuffersToAppendTo)
    {
        rentedBuffersToAppendTo.Add(image);
        Store.WriteStaged(name, image.Memory.Span[..size]);

        IMemoryOwner<byte> digest = Pool.Rent(Checksum.ByteWidth);
        rentedBuffersToAppendTo.Add(digest);
        Checksum.Compute(image.Memory.Span[..size], digest.Memory.Span[..Checksum.ByteWidth]);
        entriesToAppendTo.Add(new ManifestEntry(role, name, 0, size, digest.Memory[..Checksum.ByteWidth]));
    }

    /// <summary>Computes a segment image's byte size, refusing one that would exceed the single-image size limit.</summary>
    /// <param name="segment">The item segment to size.</param>
    /// <returns>The image byte size.</returns>
    /// <exception cref="InvalidOperationException">The image would exceed the single-image size limit.</exception>
    private int SegmentImageSize(ItemSegment segment)
    {
        long size = segment.ComputeSerializedSize(Checksum);
        if(size > Array.MaxLength)
        {
            throw new InvalidOperationException("A system-of-record segment exceeds the single-image size limit; split the dataset across generations.");
        }

        return (int)size;
    }

    /// <summary>
    /// Loads the durably persisted dictionary and system-of-record for the recovered committed generation,
    /// verifying each artifact before returning, and falling back down the retained generation ladder when the
    /// recovered generation's artifacts fail their at-rest verification. Returns a value-based outcome:
    /// <see cref="DurableSystemOfRecordLoadOutcome.NotFound"/> when no generation is committed,
    /// <see cref="DurableSystemOfRecordLoadOutcome.NoDictionaryEntry"/> or
    /// <see cref="DurableSystemOfRecordLoadOutcome.NoDataSegmentEntry"/> when the manifest names neither,
    /// <see cref="DurableSystemOfRecordLoadOutcome.Rejected"/> when no candidate generation loads fully, and
    /// <see cref="DurableSystemOfRecordLoadOutcome.Loaded"/> with the reconstructed dictionary and the owned,
    /// pooled triples otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recovery fixes a starting generation (the live CURRENT's, a retained CURRENT's, or the degraded scan's) and
    /// this method verifies that generation's artifacts. When they fail — a rotted or missing segment, a
    /// mismatched digest, an undecodable image — the generation is excluded and the next candidate strictly below
    /// it is recovered (the newest retained pointer below it, then the degraded scan below it), verified, and
    /// served if it loads fully. The exclusion bound strictly decreases each step, so the ladder is finite; when
    /// every candidate is exhausted the first (expected) generation's failure outcome is returned, exactly as a
    /// single-generation load would have. Recovery is read-only: it never writes a pointer, prunes an artifact, or
    /// otherwise repairs the store.
    /// </para>
    /// <para>
    /// The returned <see cref="DurableSystemOfRecordLoad"/> carries the recovery fidelity additively:
    /// <see cref="DurableSystemOfRecordLoad.IsDegraded"/> and <see cref="DurableSystemOfRecordLoad.CommitEvidenced"/>
    /// come from the recovery that produced the served generation, and <see cref="DurableSystemOfRecordLoad.IsRollback"/>
    /// is set when the ladder fell back to a generation older than the one recovery first fixed. A degraded or
    /// rolled-back load is therefore never indistinguishable from a clean committed one.
    /// </para>
    /// </remarks>
    /// <param name="termPool">The pool the recovered terms are interned into; it must outlive the returned dictionary, which holds views over its memory.</param>
    /// <param name="triplePool">The pool the recovered triples' buffer is rented from; the caller owns and disposes the returned <see cref="DurableSystemOfRecordLoad.Triples"/>.</param>
    /// <returns>The load outcome, with the reconstructed dictionary, the owned triples, and the recovery-fidelity flags when loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="termPool"/> or <paramref name="triplePool"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">A recovered manifest, CURRENT pointer, or artifact uses a checksum algorithm or format version this reader does not support — a reader incompatibility, not at-rest rot, so it propagates rather than mapping to a value.</exception>
    public DurableSystemOfRecordLoad TryLoad(Utf8StringPool termPool, MemoryPool<EncodedTriple> triplePool)
    {
        ArgumentNullException.ThrowIfNull(termPool);
        ArgumentNullException.ThrowIfNull(triplePool);

        ManifestRecovery recovery = new(Store, ResolveChecksum);

        RecoveryResult recovered;
        try
        {
            recovered = recovery.Recover();
        }
        catch(InvalidDataException)
        {
            return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.NotFound, null, null, null, 0, []);
        }

        //The rollback baseline is the strongest SURVIVING commit evidence: the generation the live CURRENT
        //pointer names when readable, or the newest generation a verifying retained copy attests — a retained
        //copy is written only after the commit rename, so it proves its generation was committed even when the
        //live pointer and that generation's manifest are both gone. A served generation older than the baseline
        //is a rollback, never silently clean. When no pointer of either kind survives, the first generation
        //recovery fixed is the baseline: a rollback past a generation nothing surviving names is locally
        //undetectable, the honest floor of this signal.
        long? liveNamed = TryReadLiveNamedGeneration();
        long? evidencedNamed = recovery.HighestCommitEvidencedGeneration();
        long rollbackBaseline = Math.Max(
            liveNamed ?? long.MinValue,
            Math.Max(evidencedNamed ?? long.MinValue, recovered.Manifest.CommitGeneration));

        //The first generation the load reached (the live pointer's, a retained pointer's, or the degraded pick's)
        //is the one the host expected; its failure, captured once, is the terminal outcome when the whole ladder is
        //exhausted, so an exhausted fallback returns exactly the failure a single-generation load would have.
        DurableSystemOfRecordLoadOutcome firstFailureOutcome = DurableSystemOfRecordLoadOutcome.Rejected;
        long firstFailureGeneration = recovered.Manifest.CommitGeneration;
        bool anyFailure = false;

        while(true)
        {
            ManifestGeneration manifest = recovered.Manifest;
            DurableSystemOfRecordLoad attempt = TryLoadGeneration(manifest, termPool, triplePool);
            if(attempt.Outcome == DurableSystemOfRecordLoadOutcome.Loaded)
            {
                return attempt with
                {
                    IsDegraded = recovered.IsDegraded,
                    CommitEvidenced = recovered.CommitEvidenced,
                    IsRollback = manifest.CommitGeneration < rollbackBaseline,
                };
            }

            if(!anyFailure)
            {
                firstFailureOutcome = attempt.Outcome;
                firstFailureGeneration = manifest.CommitGeneration;
                anyFailure = true;
            }

            try
            {
                //Exclude the failed generation (and every generation at or above it) and recover the next candidate:
                //the newest retained pointer below it, then the degraded scan below it. The exclusion bound strictly
                //decreases each step, so the ladder is finite and cannot loop.
                recovered = recovery.RecoverBelow(manifest.CommitGeneration);
            }
            catch(InvalidDataException)
            {
                return new DurableSystemOfRecordLoad(firstFailureOutcome, null, null, null, firstFailureGeneration, []);
            }
        }
    }

    /// <summary>
    /// Surfaces the durable loss record the live committed generation carries, or <see langword="null"/> when it
    /// names none: the affordance a reopened store learns a generation was healed with unrecoverable losses by,
    /// so a lossy heal is not indistinguishable from a pristine generation across a restart. A generation never
    /// healed, or one healed clean, names no loss record and reads back <see langword="null"/> — a present record
    /// is exactly the visibly-lossy signal, and its <see cref="Lumoin.Veritas.Core.Integrity.DurableLossRecord.Losses"/>
    /// name each loss (kind, artifact role and name, and item range). The record is verified against the length
    /// and whole-image digest the manifest recorded before its losses are returned, so at-rest rot reads back
    /// <see langword="null"/> rather than surfacing corrupt losses.
    /// </summary>
    /// <returns>The recovered loss record, or <see langword="null"/> when the live generation names none, its record fails verification, or no generation is committed.</returns>
    /// <exception cref="NotSupportedException">The loss record uses a checksum algorithm or format version this reader does not support — a reader incompatibility, not at-rest rot.</exception>
    public DurableLossRecord? TryReadRecordedLosses()
    {
        ManifestGeneration manifest;
        try
        {
            manifest = new ManifestRecovery(Store, ResolveChecksum).Recover().Manifest;
        }
        catch(InvalidDataException)
        {
            return null;
        }

        if(FindEntry(manifest, ManifestFileRole.Losses) is not { } lossEntry)
        {
            return null;
        }

        //The loss record decodes as one span; an artifact past a span's range is not a loss record this
        //reader can decode, so it reads as absent rather than throwing at the whole-image accessor.
        using SegmentImageSource? source = Store.OpenImage(lossEntry.FileName);
        if(source is null || source.Length > int.MaxValue || !MatchesEntry(source, lossEntry))
        {
            return null;
        }

        return DurableLossRecord.TryRead(source.Image, ResolveChecksum);
    }

    /// <summary>Reads the generation the live CURRENT pointer names, independent of whether its manifest or artifacts verify — the rollback baseline the artifact-failure ladder is measured against.</summary>
    /// <returns>The live pointer's named generation, or <see langword="null"/> when the live pointer is absent or fails its self-checksum.</returns>
    /// <exception cref="NotSupportedException">The live pointer uses a checksum algorithm or format version this reader does not support.</exception>
    private long? TryReadLiveNamedGeneration()
    {
        byte[]? liveBytes = Store.Read(ManifestNaming.CurrentPointerName);
        if(liveBytes is null)
        {
            return null;
        }

        try
        {
            return CurrentPointer.ReadFrom(liveBytes, ResolveChecksum).CommitGeneration;
        }
        catch(InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads and verifies one recovered generation's artifacts: the dictionary and default system-of-record
    /// (both required and integrity-root), every named graph (required, not re-derivable), and the re-derivable
    /// columnar sidecar (dropped to null on damage). Returns a payload-bearing
    /// <see cref="DurableSystemOfRecordLoadOutcome.Loaded"/> whose triples and named graphs the caller owns, or a
    /// resource-free failure outcome having disposed anything it decoded. The recovery-fidelity flags are left at
    /// their defaults; the caller (<see cref="TryLoad"/>) stamps them from the recovery that produced the generation.
    /// </summary>
    /// <param name="manifest">The recovered manifest generation to load.</param>
    /// <param name="termPool">The pool the recovered terms are interned into.</param>
    /// <param name="triplePool">The pool the recovered triples' buffers are rented from.</param>
    /// <returns>The per-generation load outcome and, when loaded, the owned dictionary, triples, sidecar, and named graphs.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The decoded system-of-record segment's ownership is transferred to the caller through the returned DurableSystemOfRecordLoad.Triples, which the caller disposes; every failure path disposes it before returning a resource-free failure outcome.")]
    private DurableSystemOfRecordLoad TryLoadGeneration(ManifestGeneration manifest, Utf8StringPool termPool, MemoryPool<EncodedTriple> triplePool)
    {
        if(FindEntry(manifest, ManifestFileRole.Dictionary) is not { } dictionaryEntry)
        {
            return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.NoDictionaryEntry, null, null, null, manifest.CommitGeneration, []);
        }

        if(FindEntry(manifest, ManifestFileRole.DataSegment) is not { } recordEntry)
        {
            return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.NoDataSegmentEntry, null, null, null, manifest.CommitGeneration, []);
        }

        //The two integrity-root segments are mapped together and stay mapped through both decodes; the decoded
        //dictionary and triples copy out of the images, so the sources are released at method exit (and, on the
        //named-graph/sidecar paths below, each is released the moment its segment is decoded).
        using SegmentImageSource? dictionarySource = Store.OpenImage(dictionaryEntry.FileName);
        using SegmentImageSource? recordSource = Store.OpenImage(recordEntry.FileName);

        //The record segment decodes through bounded windows at any length; the dictionary still decodes as one
        //span (its write side caps it), so a dictionary artifact past a span's range is REJECTED rather than
        //thrown at the whole-image accessor.
        if(dictionarySource is null || dictionarySource.Length > int.MaxValue || !MatchesEntry(dictionarySource, dictionaryEntry) || recordSource is null || !MatchesEntry(recordSource, recordEntry))
        {
            return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.Rejected, null, null, null, manifest.CommitGeneration, []);
        }

        TermDictionary dictionary;
        DecodedItemSegment? triples = null;
        try
        {
            //Detection precedes use: each segment verifies its framing and every block checksum before it is decoded.
            dictionary = DictionarySegment.ReadFrom(dictionarySource.Image, termPool, ResolveChecksum);
            triples = ItemSegment.ReadFrom(recordSource, triplePool, ResolveChecksum);
        }
        catch(InvalidDataException)
        {
            triples?.Dispose();

            return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.Rejected, null, null, null, manifest.CommitGeneration, []);
        }

        //Named graphs are system-of-record-class — not re-derivable — so a missing or corrupt named-graph artifact
        //fails the load, unlike the re-derivable sidecar below. The verified default triples are disposed on that path.
        List<(TermId GraphName, DecodedItemSegment Triples)>? namedGraphs = TryLoadNamedGraphs(manifest, triplePool);
        if(namedGraphs is null)
        {
            triples.Dispose();

            return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.Rejected, null, null, null, manifest.CommitGeneration, []);
        }

        //The columnar index is re-derivable, so a missing or corrupt sidecar drops to null (the engine rebuilds)
        //rather than failing a load whose dictionary and system-of-record both verified. The value-index sidecar
        //follows the same rule, with the additional staleness gate on its dataset-state stamp.
        ColumnarTripleIndex? sidecar = TryLoadSidecar(manifest, triplePool);
        ValueIndexImage? valueIndexes = TryLoadValueIndexImage(manifest);

        //The causality artifact is its own class: not re-derivable (it is causal knowledge, not content) yet
        //never a load failure — the generation serves without it and remove-awareness falls to the explicit
        //baseline rule. Absent and refused stay distinguishable so the engine can refuse loudly, never
        //silently downgrade.
        ReadOnlyMemory<byte>? causality = TryLoadCausalityImage(manifest, out bool causalityRefused);

        return new DurableSystemOfRecordLoad(DurableSystemOfRecordLoadOutcome.Loaded, dictionary, triples, sidecar, manifest.CommitGeneration, namedGraphs, ProvenanceEpoch: manifest.ProvenanceEpoch, ValueIndexes: valueIndexes, CausalityImage: causality, CausalityRefused: causalityRefused);
    }

    /// <summary>
    /// Recovers the replication causality artifact's image bytes. Returns <see langword="null"/> with
    /// <paramref name="refused"/> <see langword="false"/> when the manifest names none (an add-only generation),
    /// and <see langword="null"/> with <paramref name="refused"/> <see langword="true"/> when the manifest names
    /// one that is missing, length-mismatched, or digest-refused — a distinguishable refusal the engine surfaces
    /// under the baseline rule, never a silent absence. The bytes are copied out so nothing holds the image
    /// source open.
    /// </summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="refused">Set when the manifest names a causality artifact that failed its at-rest verification.</param>
    /// <returns>The verified image bytes, or <see langword="null"/>.</returns>
    private ReadOnlyMemory<byte>? TryLoadCausalityImage(ManifestGeneration manifest, out bool refused)
    {
        refused = false;
        if(FindEntry(manifest, ReplicationCausalityRole) is not { } causalityEntry)
        {
            return null;
        }

        using SegmentImageSource? source = Store.OpenImage(causalityEntry.FileName);
        if(source is null || source.Length > int.MaxValue || !MatchesEntry(source, causalityEntry))
        {
            refused = true;

            return null;
        }

        return source.Image.ToArray();
    }

    /// <summary>Recovers the optional columnar warm-start sidecar, returning <see langword="null"/> when the manifest names none, the artifact is missing or length/digest-mismatched, or it fails its at-rest verification — the sidecar is re-derivable, so its absence is never a load failure.</summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="deltaPool">The pool the index's transient delta triples are rented from during reconstruction; nothing from it outlives the call.</param>
    /// <returns>The warm-loaded columnar index, or <see langword="null"/>.</returns>
    private ColumnarTripleIndex? TryLoadSidecar(ManifestGeneration manifest, MemoryPool<EncodedTriple> deltaPool)
    {
        if(FindEntry(manifest, ManifestFileRole.Sidecar) is not { } sidecarEntry)
        {
            return null;
        }

        //The columnar sidecar decodes as one span; a sidecar past a span's range is re-derivable like any
        //other refused sidecar, so it reads as missing rather than throwing at the whole-image accessor.
        using SegmentImageSource? sidecarSource = Store.OpenImage(sidecarEntry.FileName);
        if(sidecarSource is null || sidecarSource.Length > int.MaxValue || !MatchesEntry(sidecarSource, sidecarEntry))
        {
            return null;
        }

        try
        {
            //Detection precedes use: the columnar container verifies its framing and every blob checksum before
            //any column is reloaded.
            return ColumnarTripleIndex.ReadFrom(sidecarSource.Image, deltaPool, ResolveChecksum);
        }
        catch(InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recovers the optional value-index sidecar image, returning <see langword="null"/> when the manifest names
    /// none, the artifact is missing or length/digest-mismatched, the image fails its structural parse, or its
    /// dataset-state stamp differs from the generation's provenance epoch — a stale image must never warm-install
    /// over data it was not built from. The sidecar is re-derivable, so its absence is never a load failure: the
    /// registered access methods rebuild from the served store at the first probe.
    /// </summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <returns>The verified image, or <see langword="null"/>.</returns>
    private ValueIndexImage? TryLoadValueIndexImage(ManifestGeneration manifest)
    {
        if(FindEntry(manifest, ValueIndexSidecarRole) is not { } valueIndexEntry)
        {
            return null;
        }

        //The value-index sidecar decodes as one span; an image past a span's range is re-derivable like any
        //other refused sidecar, so it reads as missing rather than throwing at the whole-image accessor.
        using SegmentImageSource? source = Store.OpenImage(valueIndexEntry.FileName);
        if(source is null || source.Length > int.MaxValue || !MatchesEntry(source, valueIndexEntry))
        {
            return null;
        }

        if(!ValueIndexImage.TryReadFrom(source.Image, out ValueIndexImage? image))
        {
            return null;
        }

        //The staleness gate: the image warms only the exact dataset state it was built from. The stamp is the
        //capture's content-addressed state identifier, the same value the manifest records as its provenance
        //epoch, so a sidecar carried against any other data is dropped and the methods rebuild at first probe.
        return image!.StateId == unchecked((ulong)manifest.ProvenanceEpoch) ? image : null;
    }

    /// <summary>
    /// Recovers every named-graph system-of-record segment the manifest names, each verified against the length and
    /// digest the manifest recorded and decoded before use, paired with the graph-name term id its artifact name
    /// encodes. Returns <see langword="null"/> — disposing any segments already decoded — when ANY named-graph
    /// artifact is missing, length/digest-mismatched, malformed-named, or fails its at-rest verification: named
    /// graphs are system-of-record-class, not re-derivable, so a damaged one fails the load rather than being
    /// silently dropped.
    /// </summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="triplePool">The pool each named graph's triple buffer is rented from; the caller owns and disposes the returned segments.</param>
    /// <returns>The recovered named graphs (empty when none were persisted), or <see langword="null"/> when one failed.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each decoded named-graph segment's ownership is transferred to the caller through the returned list, which the caller disposes; any failure path disposes the already-decoded segments before returning null.")]
    private List<(TermId GraphName, DecodedItemSegment Triples)>? TryLoadNamedGraphs(ManifestGeneration manifest, MemoryPool<EncodedTriple> triplePool)
    {
        List<(TermId GraphName, DecodedItemSegment Triples)> recovered = [];
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role.Code != ManifestFileRole.NamedGraphSegment.Code)
            {
                continue;
            }

            if(!TryParseNamedGraphArtifact(entry.FileName, out _, out uint graphTermId))
            {
                DisposeNamedGraphs(recovered);

                return null;
            }

            using SegmentImageSource? source = Store.OpenImage(entry.FileName);
            if(source is null || !MatchesEntry(source, entry))
            {
                DisposeNamedGraphs(recovered);

                return null;
            }

            DecodedItemSegment? segment = null;
            try
            {
                //Detection precedes use: the segment verifies its framing and every block checksum before decode.
                segment = ItemSegment.ReadFrom(source, triplePool, ResolveChecksum);
            }
            catch(InvalidDataException)
            {
                segment?.Dispose();
                DisposeNamedGraphs(recovered);

                return null;
            }

            recovered.Add((TermId.FromEncoded(graphTermId), segment));
        }

        return recovered;
    }

    /// <summary>Disposes the triple buffers of partially-recovered named graphs on a load-failure path.</summary>
    /// <param name="recovered">The named graphs decoded so far.</param>
    private static void DisposeNamedGraphs(List<(TermId GraphName, DecodedItemSegment Triples)> recovered)
    {
        foreach((TermId _, DecodedItemSegment segment) in recovered)
        {
            segment.Dispose();
        }
    }

    /// <summary>
    /// Resolves the generation to publish next: one past the recovered generation, or zero when the store holds
    /// none.
    /// </summary>
    /// <returns>The next monotonic commit generation.</returns>
    private long NextGeneration()
    {
        try
        {
            return new ManifestRecovery(Store, ResolveChecksum).Recover().Manifest.CommitGeneration + 1;
        }
        catch(InvalidDataException)
        {
            return 0;
        }
    }

    /// <summary>Finds the manifest's first entry of a role, or <see langword="null"/> when it names none.</summary>
    /// <param name="manifest">The recovered manifest.</param>
    /// <param name="role">The role to find.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    private static ManifestEntry? FindEntry(ManifestGeneration manifest, ManifestFileRole role)
    {
        foreach(ManifestEntry entry in manifest.Entries)
        {
            if(entry.Role.Code == role.Code)
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>The window a streamed digest verification folds per append — large enough to amortize the per-window call, small enough to stay cache-friendly.</summary>
    private const int DigestVerifyWindowBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Verifies a read artifact against the length and digest the manifest recorded for it, binding the file to
    /// the generation that named it. This catches truncation, over-length tampering, and at-rest rot in regions
    /// the segment's own per-block checksums do not cover. The digest is compared only when the manifest recorded
    /// it under this store's checksum algorithm; a foreign-algorithm manifest is left to the segment's own
    /// verification. An artifact that fits a single span digests one-shot; a larger one streams through the
    /// algorithm's incremental session in bounded windows — and when the algorithm carries no session, the
    /// verification FAILS CLOSED (the artifact reads as rejected) rather than passing unverified.
    /// </summary>
    /// <param name="source">The segment image source read from the store.</param>
    /// <param name="entry">The manifest entry that named the artifact.</param>
    /// <returns><see langword="true"/> when the artifact matches the manifest's recorded length and, where comparable, digest.</returns>
    private bool MatchesEntry(SegmentImageSource source, ManifestEntry entry)
    {
        if(source.Length != entry.ByteLength)
        {
            return false;
        }

        if(entry.Checksum.Length != Checksum.ByteWidth)
        {
            return true;
        }

        using IMemoryOwner<byte> digest = Pool.Rent(Checksum.ByteWidth);
        Span<byte> recomputed = digest.Memory.Span[..Checksum.ByteWidth];
        if(source.Length <= int.MaxValue)
        {
            Checksum.Compute(source.Image, recomputed);

            return recomputed.SequenceEqual(entry.Checksum.Span);
        }

        if(Checksum.CreateSession is not { } createSession)
        {
            //A one-shot-only algorithm cannot digest an artifact past a span's range: refuse rather than skip.
            return false;
        }

        using(ChecksumSession session = createSession())
        {
            long offset = 0;
            long remaining = source.Length;
            while(remaining > 0)
            {
                int window = (int)Math.Min(remaining, DigestVerifyWindowBytes);
                session.Append(source.Slice(offset, window));
                offset += window;
                remaining -= window;
            }

            session.Finish(recomputed);
        }

        return recomputed.SequenceEqual(entry.Checksum.Span);
    }

    /// <summary>The store name of an artifact for a generation, zero-padded so a lexical listing matches generation order.</summary>
    /// <param name="prefix">The artifact name prefix.</param>
    /// <param name="generation">The commit generation.</param>
    /// <param name="suffix">The artifact name suffix.</param>
    /// <returns>The artifact name.</returns>
    private static string ArtifactName(string prefix, long generation, string suffix)
    {
        return prefix + generation.ToString("D20", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>The store name of a named-graph system-of-record artifact: <c>nsor-&lt;generation&gt;-g&lt;graphTermId&gt;.sor</c>, both fields zero-padded so a lexical listing matches generation order and the graph term id round-trips.</summary>
    /// <param name="generation">The commit generation.</param>
    /// <param name="graphName">The graph-name term id this segment holds.</param>
    /// <returns>The named-graph artifact name.</returns>
    private static string NamedGraphArtifactName(long generation, TermId graphName)
    {
        return NamedRecordArtifactPrefix
            + generation.ToString("D20", CultureInfo.InvariantCulture)
            + NamedGraphNameInfix
            + graphName.Encoded.ToString("D10", CultureInfo.InvariantCulture)
            + RecordArtifactSuffix;
    }

    /// <summary>Parses the generation and graph-name term id a named-graph artifact name encodes, or declines a name that does not match the named-graph shape.</summary>
    /// <param name="name">The store artifact name.</param>
    /// <param name="generation">The parsed generation when the name matches; 0 otherwise.</param>
    /// <param name="graphTermId">The parsed graph-name term id when the name matches; 0 otherwise.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is a named-graph artifact name.</returns>
    private static bool TryParseNamedGraphArtifact(string name, out long generation, out uint graphTermId)
    {
        generation = 0;
        graphTermId = 0;
        if(!name.StartsWith(NamedRecordArtifactPrefix, StringComparison.Ordinal) || !name.EndsWith(RecordArtifactSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        int infix = name.IndexOf(NamedGraphNameInfix, NamedRecordArtifactPrefix.Length, StringComparison.Ordinal);
        if(infix < 0)
        {
            return false;
        }

        ReadOnlySpan<char> generationSpan = name.AsSpan(NamedRecordArtifactPrefix.Length, infix - NamedRecordArtifactPrefix.Length);
        int idStart = infix + NamedGraphNameInfix.Length;
        int idLength = name.Length - idStart - RecordArtifactSuffix.Length;
        if(generationSpan.Length == 0 || idLength <= 0)
        {
            return false;
        }

        return long.TryParse(generationSpan, NumberStyles.None, CultureInfo.InvariantCulture, out generation)
            && uint.TryParse(name.AsSpan(idStart, idLength), NumberStyles.None, CultureInfo.InvariantCulture, out graphTermId);
    }

    /// <summary>
    /// Deletes dictionary and system-of-record artifacts older than the manifest writer's retention window, so a
    /// long-running database does not accumulate them. The window matches <see cref="RetainedGenerationCount"/>,
    /// so a kept artifact always has a kept manifest; collection runs only after the commit has published, so no
    /// surviving manifest names a deleted artifact.
    /// </summary>
    /// <param name="committedGeneration">The generation just committed.</param>
    private void CollectSupersededArtifacts(long committedGeneration)
    {
        long oldestRetained = committedGeneration - RetainedGenerationCount + 1;
        if(oldestRetained <= 0)
        {
            return;
        }

        CollectSupersededArtifacts(DictionaryArtifactPrefix, DictionaryArtifactSuffix, oldestRetained);
        CollectSupersededArtifacts(RecordArtifactPrefix, RecordArtifactSuffix, oldestRetained);
        CollectSupersededArtifacts(SidecarArtifactPrefix, SidecarArtifactSuffix, oldestRetained);
        CollectSupersededArtifacts(SketchArtifactPrefix, SketchArtifactSuffix, oldestRetained);
        CollectSupersededArtifacts(ValueIndexArtifactPrefix, ValueIndexArtifactSuffix, oldestRetained);
        CollectSupersededArtifacts(CausalityArtifactPrefix, CausalityArtifactSuffix, oldestRetained);
        CollectSupersededNamedGraphArtifacts(oldestRetained);
        CollectSupersededHealedArtifacts(oldestRetained, committedGeneration);
    }

    /// <summary>
    /// Deletes the healed images and loss records a repair publish staged under a superseded generation, so a
    /// self-heal does not leak them: those artifacts are named outside this store's own prefixes (a healed image
    /// is <c>{role}-{generation}</c>, a loss record <c>losses-{generation}</c>), so the per-prefix generation
    /// collection above never reaches them. Collection is keyed on what the retained window's manifests still
    /// name, not on the stamp alone: a heal re-lists an undamaged healed image forward under an older
    /// generation's stamp, so a stamp below the window is not proof it is superseded — anything a surviving
    /// generation still names is kept, mirroring the retention window discipline exactly.
    /// </summary>
    /// <param name="oldestRetained">The oldest generation still in the retention window.</param>
    /// <param name="committedGeneration">The just-committed generation, always retained.</param>
    private void CollectSupersededHealedArtifacts(long oldestRetained, long committedGeneration)
    {
        HashSet<string> retainedNames = CollectRetainedArtifactNames(oldestRetained, committedGeneration);
        foreach(string prefix in HealedArtifactNaming.CollectiblePrefixes)
        {
            foreach(string name in Store.List(prefix))
            {
                if(!retainedNames.Contains(name))
                {
                    Store.Delete(name);
                }
            }
        }
    }

    /// <summary>Collects the store names every generation in the retention window still lists, so a healed artifact a surviving generation carried forward is never mistaken for superseded. A manifest missing or unreadable at rest contributes nothing (its generation is a recovery concern handled elsewhere).</summary>
    /// <param name="oldestRetained">The oldest generation still in the retention window.</param>
    /// <param name="committedGeneration">The just-committed generation, always retained.</param>
    /// <returns>The set of file names the retained generations name.</returns>
    private HashSet<string> CollectRetainedArtifactNames(long oldestRetained, long committedGeneration)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        for(long generation = oldestRetained; generation <= committedGeneration; generation++)
        {
            byte[]? image = Store.Read(ManifestNaming.ManifestName(generation));
            if(image is null)
            {
                continue;
            }

            ManifestGeneration manifest;
            try
            {
                manifest = ManifestGeneration.ReadFrom(image, ResolveChecksum);
            }
            catch(InvalidDataException)
            {
                continue;
            }
            catch(NotSupportedException)
            {
                continue;
            }

            foreach(ManifestEntry entry in manifest.Entries)
            {
                names.Add(entry.FileName);
            }
        }

        return names;
    }

    /// <summary>Deletes named-graph artifacts whose generation falls below the oldest retained generation; named-graph names carry a graph-id infix the plain generation parse does not match, so they are collected through their own parse.</summary>
    /// <param name="oldestRetained">The oldest generation to keep.</param>
    private void CollectSupersededNamedGraphArtifacts(long oldestRetained)
    {
        foreach(string name in Store.List(NamedRecordArtifactPrefix))
        {
            if(TryParseNamedGraphArtifact(name, out long generation, out _) && generation < oldestRetained)
            {
                Store.Delete(name);
            }
        }
    }

    /// <summary>Deletes the artifacts of one prefix/suffix whose generation falls below the oldest retained generation.</summary>
    /// <param name="prefix">The artifact name prefix.</param>
    /// <param name="suffix">The artifact name suffix.</param>
    /// <param name="oldestRetained">The oldest generation to keep.</param>
    private void CollectSupersededArtifacts(string prefix, string suffix, long oldestRetained)
    {
        foreach(string name in Store.List(prefix))
        {
            if(TryParseGeneration(name, prefix, suffix, out long generation) && generation < oldestRetained)
            {
                Store.Delete(name);
            }
        }
    }

    /// <summary>Parses the generation an artifact name encodes, or declines a name that does not match the artifact shape.</summary>
    /// <param name="name">The store artifact name.</param>
    /// <param name="prefix">The expected prefix.</param>
    /// <param name="suffix">The expected suffix.</param>
    /// <param name="generation">The parsed generation when the name matches; 0 otherwise.</param>
    /// <returns><see langword="true"/> when <paramref name="name"/> is an artifact name of this shape.</returns>
    private static bool TryParseGeneration(string name, string prefix, string suffix, out long generation)
    {
        generation = 0;
        if(!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        int start = prefix.Length;
        int length = name.Length - prefix.Length - suffix.Length;
        if(length <= 0)
        {
            return false;
        }

        return long.TryParse(name.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture, out generation);
    }
}


