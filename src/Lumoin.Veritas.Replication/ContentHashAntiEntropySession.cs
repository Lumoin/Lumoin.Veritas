using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Reconciliation;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// The cross-fleet, content-hash variant of <see cref="AntiEntropySession"/>: it reconciles a local
/// <see cref="ColumnarTripleIndex"/> against a peer built under a DIFFERENT dictionary, using the content-hash item
/// domain so the same triple cancels even when the two replicas numbered its terms differently. It reuses the whole
/// rateless sketch machinery (the items are 16-byte content keys, so the codec is unchanged), and differs from the
/// structural session only in recovery: a content-hash item is not invertible, so instead of inverting a recovered
/// key to a triple it partitions the recovered symmetric difference through the local side-map (held items are
/// already local — skipped; lacked items are peer-only), fetches the peer-only triples as terms through
/// <see cref="AsyncContentTripleFetchDelegate"/>, re-encodes them into the local dictionary, and applies them. This
/// is the cross-org boundary domain; a shared-epoch cluster uses <see cref="AntiEntropySession"/>.
/// </summary>
/// <remarks>
/// <para>
/// The content-hash domain is for ground triples; a local triple holding a blank node or RDF 1.2 triple term makes
/// the projection throw (see <see cref="ContentHashReconciliationProjection"/>), surfacing the deferred case rather
/// than producing a silently epoch-local key. Building the local sketch and side-map each project every local
/// triple — the price of cross-node identity the structural domain avoids with an invertible key. Both replicas
/// must use the same <see cref="VeritasHash"/>.
/// </para>
/// <para>
/// <b>Collision trade-off.</b> Unlike the structural domain, whose packed key IS the triple and so cannot collide,
/// two distinct triples that hash to the same 128-bit key cancel as one in the rateless peel and a peer-only triple
/// is then silently missed (a false convergence). With the default non-cryptographic hash an accidental collision
/// is about 2^-64; a deployment that must resist a peer crafting a collision swaps in a cryptographic
/// <see cref="VeritasHash"/> (SHA-256 truncation) at this cross-org boundary.
/// </remarks>
public static class ContentHashAntiEntropySession
{
    /// <summary>The reserved dictionary epoch the content-hash domain requires: content-hash items are epoch-independent, so a stamped peer must carry <c>0</c> and any other value is out of contract.</summary>
    private const ulong ContentHashEpoch = 0;

    /// <summary>Reconciles the local replica against a peer in the content-hash domain, converging the local replica to the union on a complete peel.</summary>
    /// <param name="local">The local replica to reconcile and, on a complete peel, converge.</param>
    /// <param name="dictionary">The local dictionary the local triples are encoded against and fetched peer triples are re-encoded into.</param>
    /// <param name="hash">The deterministic content hash; every replica that reconciles together must use the same one.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under.</param>
    /// <param name="pool">The pool the session's transient buffers are rented from.</param>
    /// <param name="timeProvider">The clock the session's elapsed time and event timestamp are measured against.</param>
    /// <param name="sketchFetch">The seam that returns the peer's content-hash sketch image at a symbol budget.</param>
    /// <param name="tripleFetch">The seam that fetches the peer's triples (as terms) for the peer-only recovered items.</param>
    /// <param name="trace">The diagnostics sink each reconcile emits its outcome to; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The correlation id the emitted event carries.</param>
    /// <param name="cancellationToken">The token that cancels the fetch.</param>
    /// <returns>The session result, with the converged index on a complete peel or the unchanged local index on a decline.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">A local triple holds a term the content-hash projection does not project (a blank node or an RDF 1.2 triple term).</exception>
    /// <exception cref="InvalidDataException">The combined budget addresses more recovered items than a single buffer can hold.</exception>
    public static async ValueTask<AntiEntropySessionResult> ReconcileAsync(
        ColumnarTripleIndex local,
        TermDictionary dictionary,
        VeritasHash hash,
        ReplicationPolicy policy,
        MemoryPool<byte> pool,
        TimeProvider timeProvider,
        AsyncSketchFetchDelegate sketchFetch,
        AsyncContentTripleFetchDelegate tripleFetch,
        TraceHandler<ReplicationTraceEvent>? trace = null,
        Guid correlationId = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(dictionary);
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(sketchFetch);
        ArgumentNullException.ThrowIfNull(tripleFetch);

        long startTimestamp = timeProvider.GetTimestamp();
        int localItemCount = local.TripleCount;
        int symbolBudget = policy.SymbolBudget(localItemCount);

        ContentHashReconciliationProjection projection = new(dictionary, hash, pool);
        ContentHashSideMap sideMap = ContentHashSideMap.Build(local, projection.Projection);
        VerifiedSketch localSketch = PersistLocalSketch(local, projection, symbolBudget, pool);

        //The fetched result OWNS its pooled image; the using releases it once the verifying load has copied its
        //symbols out, so the pooled peer image may be disposed right after the load.
        using SketchFetchResult peer = await sketchFetch(symbolBudget, cancellationToken).ConfigureAwait(false);
        if(peer.IsUnavailable)
        {
            //No framed response at all: an unreachable peer, so there is no source to reconcile against.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerUnavailable, localItemCount, symbolBudget, 0, 0, local);
        }

        if(peer.Domain != SketchChannelDomain.ContentHash || peer.DictionaryEpoch != ContentHashEpoch)
        {
            //A peer stamped for the structural domain, or a content-hash peer advertising a non-zero epoch its
            //epoch-independent domain reserves at zero, is out of this session's contract: its image is shaped for a
            //different item space, so the session refuses at the stamp rather than inverting or re-hashing garbage.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerContractMismatch, localItemCount, symbolBudget, 0, 0, local);
        }

        if(!peer.HasImage)
        {
            //A stamped decline (a serveable image was not built) is an unavailable peer for this round.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerUnavailable, localItemCount, symbolBudget, 0, 0, local);
        }

        VerifiedSketch peerSketch;
        try
        {
            peerSketch = SketchPersistence.LoadVerifiedSketch(peer.Image.Span, SketchContract.Structural);
        }
        catch(Exception exception) when(exception is InvalidDataException or NotSupportedException)
        {
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerSketchRejected, localItemCount, symbolBudget, 0, 0, local);
        }

        int sinkCapacity = localSketch.SymbolCount + peerSketch.SymbolCount;
        long sinkByteCount = (long)sinkCapacity * ContentKey128.ByteWidth;
        if(sinkByteCount > Array.MaxLength)
        {
            throw new InvalidDataException("The combined sketch budget addresses more recovered items than a single buffer can hold.");
        }

        using IMemoryOwner<byte> recoveredOwner = pool.Rent((int)Math.Max(1, sinkByteCount));
        Span<ContentKey128> recovered = MemoryMarshal.Cast<byte, ContentKey128>(recoveredOwner.Memory.Span)[..sinkCapacity];
        SketchDifference difference = new RatelessSketchCodec(pool).RecoverDifference(localSketch, peerSketch, symbolBudget, recovered);

        if(!difference.IsComplete || difference.RecoveredCount > recovered.Length)
        {
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.IncompletePeel, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        if(difference.FalseDecodeProbabilityBound > policy.MaxFalseDecodeProbability)
        {
            //A complete peel whose evidence quality is below the policy ceiling: the union bound that no masquerading
            //symbol forged the peel is too weak to act on. This gate precedes the empty-peel branch, so even an
            //implausibly-bounded "already consistent" claim is refused rather than laundered into convergence.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.FalseDecodeBoundExceeded, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        if(difference.RecoveredCount == 0)
        {
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.AlreadyConsistent, localItemCount, symbolBudget, 0, difference.AbsorbedSymbols, local);
        }

        //Partition the symmetric difference through the local side-map: an item the local holds is a local-only
        //difference the peer lacks (nothing for the local to do); an item the local lacks is a peer-only difference
        //to fetch and ingest. This replaces the structural session's invert-and-apply, which a content hash cannot do.
        List<ContentKey128> peerOnly = [];
        for(int i = 0; i < difference.RecoveredCount; i++)
        {
            if(!sideMap.Contains(recovered[i]))
            {
                peerOnly.Add(recovered[i]);
            }
        }

        if(peerOnly.Count == 0)
        {
            //Every recovered difference is local-only: the local replica already holds the union, so it converges
            //with nothing to apply.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.Converged, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        //Verify before applying. Each fetched triple must hash (under the local projection of its terms) to one of
        //the peer-only keys it was requested for — a triple that does not is a misbehaving or untrusted peer (this
        //is a cross-org boundary) and is ignored. The reconcile converges only when EVERY peer-only key is
        //satisfied; otherwise — a short fetch (the peer may have mutated between serving its sketch and answering),
        //or wrong triples — it declines and leaves the local index unchanged, never half-applying, so a later round
        //retries. The structural session has no analogue because it inverts every recovered key locally.
        //
        //Each fetched triple is BORROWED: its terms view a pooled per-item buffer valid only for the handler call.
        //Its terms reach only ProjectTerms, which reads their spans into a rented buffer and retains nothing. The
        //INVARIANT is that TermDictionary.GetOrAdd RETAINS the term instance it is handed, so only owned terms may
        //reach it; each verified peer-only triple is therefore materialized into heap-owned terms (CloneOwned)
        //before it is re-encoded. Only the verified triples materialize owned copies, so a bogus triple's terms
        //never pollute the dictionary — that (plus the pooled wire buffers) is the allocation win.
        HashSet<ContentKey128> requested = new(peerOnly);
        HashSet<ContentKey128> satisfied = new(peerOnly.Count);
        List<EncodedTriple> additions = [];
        try
        {
            await tripleFetch(peerOnly, (in ContentTriple triple) =>
            {
                ContentKey128 key = projection.ProjectTerms(triple.Subject, triple.Predicate, triple.Object);
                if(requested.Contains(key) && satisfied.Add(key))
                {
                    additions.Add(EncodedTriple.FromEncoded(
                        dictionary.GetOrAdd(CloneOwned(triple.Subject)).Encoded,
                        dictionary.GetOrAdd(CloneOwned(triple.Predicate)).Encoded,
                        dictionary.GetOrAdd(CloneOwned(triple.Object)).Encoded));
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch(InvalidDataException)
        {
            //A malformed or hostile response frame (the wire is a cross-org trust boundary) declines by value rather
            //than faulting the reconcile, mirroring the peer-sketch load: nothing is applied and a later round
            //retries. A transport failure (a dropped connection) is not an expected condition and still propagates.
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerTriplesIncomplete, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        if(satisfied.Count != peerOnly.Count)
        {
            return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.PeerTriplesIncomplete, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, local);
        }

        EncodedTriple[] recoveredAdditions = additions.ToArray();
        ColumnarTripleIndex converged = local.Apply(recoveredAdditions, []);

        return Complete(trace, correlationId, timeProvider, startTimestamp, AntiEntropyOutcome.Converged, localItemCount, symbolBudget, difference.RecoveredCount, difference.AbsorbedSymbols, converged, recoveredAdditions);
    }

    /// <summary>Materializes a heap-owned copy of a borrowed wire-backed term so the dictionary may retain it — a NamedNode's IRI and a Literal's value, datatype IRI, and language are each copied into an independent buffer, and the literal's base direction is preserved. Only owned terms may reach <see cref="TermDictionary.GetOrAdd(RdfTerm)"/>, which retains the instance it is handed.</summary>
    /// <param name="term">The borrowed term whose <see cref="Utf8String"/> memory views a pooled per-item buffer.</param>
    /// <returns>An independent term backed by heap-owned memory.</returns>
    /// <exception cref="NotSupportedException">The term is neither an IRI nor a literal; the content-hash wire carries only those.</exception>
    private static RdfTerm CloneOwned(RdfTerm term)
    {
        return term switch
        {
            NamedNode named => new NamedNode(CloneUtf8(named.Iri)),
            Literal literal => new Literal(CloneUtf8(literal.Value), new NamedNode(CloneUtf8(literal.Datatype.Iri)), literal.Language is { } language ? CloneUtf8(language) : null, literal.BaseDirection),
            _ => throw new NotSupportedException($"The content-hash reconcile materializes only IRIs and literals, not '{term.GetType().Name}'."),
        };
    }

    /// <summary>Copies a borrowed UTF-8 string's bytes into an independent heap array, so the returned value survives the release of the pooled buffer the argument viewed.</summary>
    /// <param name="value">The borrowed UTF-8 string viewing pooled memory.</param>
    /// <returns>An independent UTF-8 string over a heap array.</returns>
    private static Utf8String CloneUtf8(Utf8String value)
    {
        return new Utf8String(value.Memory.ToArray());
    }

    /// <summary>Projects the local replica's triples to content-hash items, persists them as a sketch, and loads the image back as a verified sketch.</summary>
    /// <param name="local">The local replica whose triples are projected.</param>
    /// <param name="projection">The content-hash projection.</param>
    /// <param name="symbolBudget">The number of coded symbols to persist.</param>
    /// <param name="pool">The pool the transient item and image buffers are rented from.</param>
    /// <returns>The local replica's verified sketch.</returns>
    private static VerifiedSketch PersistLocalSketch(ColumnarTripleIndex local, ContentHashReconciliationProjection projection, int symbolBudget, MemoryPool<byte> pool)
    {
        using SlabBufferWriter writer = new(pool);
        ContentHashSketch.WriteImage(local, projection, symbolBudget, pool, writer);
        int imageLength = writer.BytesWritten;
        using IMemoryOwner<byte> imageOwner = writer.Detach();

        return SketchPersistence.LoadVerifiedSketch(imageOwner.Memory.Span[..imageLength], SketchContract.Structural);
    }

    /// <summary>Builds the session result for an outcome and, when a handler is attached, emits the matching trace event.</summary>
    /// <param name="trace">The diagnostics sink; <see langword="null"/> emits nothing.</param>
    /// <param name="correlationId">The reconcile's correlation id.</param>
    /// <param name="timeProvider">The clock the event timestamp and elapsed time are read from.</param>
    /// <param name="startTimestamp">The timestamp the session began at.</param>
    /// <param name="outcome">How the reconcile ended.</param>
    /// <param name="localItemCount">The local replica's item count.</param>
    /// <param name="symbolBudget">The symbol budget the sketches were built and the recovery capped at.</param>
    /// <param name="recoveredCount">The recovered difference-item count.</param>
    /// <param name="absorbedSymbols">The symbols the decoder absorbed.</param>
    /// <param name="convergedIndex">The resulting index — the converged union, or the unchanged local index on a decline.</param>
    /// <param name="recoveredAdditions">The verified peer-only triples applied to converge; empty (the default) on every decline.</param>
    /// <returns>The session result.</returns>
    private static AntiEntropySessionResult Complete(
        TraceHandler<ReplicationTraceEvent>? trace,
        Guid correlationId,
        TimeProvider timeProvider,
        long startTimestamp,
        AntiEntropyOutcome outcome,
        int localItemCount,
        int symbolBudget,
        int recoveredCount,
        int absorbedSymbols,
        ColumnarTripleIndex convergedIndex,
        ReadOnlyMemory<EncodedTriple> recoveredAdditions = default)
    {
        if(trace is not null)
        {
            ReplicationTraceEvent evt = new(0, timeProvider.GetUtcNow().UtcTicks, correlationId, outcome, localItemCount, symbolBudget, recoveredCount, absorbedSymbols);
            trace(in evt);
        }

        return new AntiEntropySessionResult(recoveredCount, convergedIndex, outcome, absorbedSymbols, timeProvider.GetElapsedTime(startTimestamp), recoveredAdditions);
    }
}
