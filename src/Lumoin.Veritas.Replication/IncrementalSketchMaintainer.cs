using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Verisync.Core;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// Maintains one long-lived rateless <see cref="ReconciliationEncoder"/> as an additional observer of the committed
/// default-graph delta, so a peer's integrity sketch is served from an incrementally-updated encoder instead of a
/// whole-set re-projection per serve. The maintainer subscribes to the dataset's committed-delta observer seam
/// (directly, or fanned to by the engine's composed observer beside the dotted commit ledger): on each
/// committed delta it advances the reconciliation feed and then folds the delta's effective additions and removals
/// into the encoder as self-inverse XOR add and remove operations — both under one gate, so a concurrent serve or
/// re-seed that takes the gate sees the feed's committed index and the encoder's net set at one generation. It serves
/// a generation-pinned symbol prefix, re-seeds on a host cadence to bound the encoder's append-only arena, and is
/// seeded at its epoch at engine open.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread-safety discipline.</b> The wrapped encoder is single-threaded and unsafe for concurrent calls, and its
/// cell buffer grows by copy — a span a reader holds is valid only until the next produce or add. Every touch of the
/// encoder therefore runs under one exclusive gate: the delta observer, the serve, the re-seed, and dispose. The
/// observer fires synchronously on the committing thread under the dataset's state lock after the journal append, so
/// the gate is taken UNDER that lock and must stay leaf-level (it takes only the feed's own leaf gate beneath it, in
/// one fixed order, and never reaches back up to a dataset lock). Because the observer runs on the commit path it does
/// no I/O, does not await, does not rebuild, and is proportional to the delta.
/// </para>
/// <para>
/// <b>Fault isolation on the commit path.</b> The observer advances the feed FIRST — the feed is the durable-truth
/// index a rebuild recovers from — and only then folds the delta into the live encoder. The fold is wrapped so that a
/// mid-fold fault does not escape onto the committing thread and poison a commit whose journal append already durably
/// succeeded: on any fold fault the encoder is marked <see cref="Dirty"/> (it may be half-folded, which is fine) rather
/// than rethrown, and the next gated touch that serves the encoder rebuilds it from <c>Feed.Current()</c> before
/// proceeding. The feed/encoder atomicity argument therefore now covers only the incremental fold on the success path;
/// the dirty-rebuild covers the fault window, and because a rebuild from the committed index is fully self-correcting
/// the served bytes are unaffected. When a diagnostic enforcement override is configured (test/diagnostic builds) the
/// fold fault is allowed to surface instead of being swallowed, so a non-net delta is caught rather than hidden.
/// </para>
/// <para>
/// <b>Re-seed is off the commit path.</b> The observer never rebuilds. When the churn since the last re-seed crosses
/// the operation budget (or the optional time interval elapses) it only raises <see cref="NeedsReseed"/>; the host
/// cadence (scrub/persist) calls <see cref="Reseed"/>. A serve that arrives while <see cref="NeedsReseed"/> is set is
/// served from the current encoder — that encoder is valid, merely memory-heavy — so a pending re-seed never blocks or
/// alters a serve. This is distinct from <see cref="Dirty"/>: dirty means the encoder is corrupt and MUST be rebuilt
/// on the next touch; needs-reseed means the encoder is correct but its arena wants reclaiming on the cadence.
/// </para>
/// <para>
/// <b>Byte-compatibility.</b> The served image still flows through <see cref="SketchPersistence.PersistSketch"/> at the
/// structural sketch geometry, so its segment framing and per-block checksums are unchanged. Only the symbol producer
/// is swapped: instead of re-projecting the whole set and folding a fresh encoder, the serve copies the maintained
/// encoder's symbol prefix. By the encoder's history-erasure law the symbol at any index equals the symbol a fresh
/// encoder over the current net set would produce, and symbols are pure order-independent functions of the set, so the
/// served bytes are identical to the whole-set re-projection they replace.
/// </para>
/// </remarks>
public sealed class IncrementalSketchMaintainer: IDisposable
{
    //The Lock is a synchronization primitive, not mutable data state; a readonly field is the idiomatic form for the
    //C# lock statement over System.Threading.Lock, and it is reentrant, so a serve may hold it across the persist call
    //while the symbol producer re-enters it.
    private readonly Lock gate = new();

    /// <summary>Whether the maintainer is disposed. After dispose the observer throws on the commit thread, so dispose only after the dataset has stopped accepting commits (see <see cref="Dispose"/>).</summary>
    private bool Disposed { get; set; }

    /// <summary>Whether the live encoder is corrupt — a prior incremental fold faulted mid-fold and may have left it half-folded. A dirty encoder MUST be rebuilt from the committed index before it is served or re-seeded; the rebuild is fully self-correcting. Distinct from <see cref="NeedsReseed"/>, which is a memory-pressure hint over a still-correct encoder.</summary>
    private bool Dirty { get; set; }

    /// <summary>The injectivity enforcement the maintained encoder is built with: <see cref="ReconciliationInjectivityEnforcement.None"/> in production (the net-effective delta guarantees exact transitions), a checking mode in diagnostic/test builds. When it checks, a fold fault is a genuine injectivity violation and is allowed to surface rather than being isolated as <see cref="Dirty"/>.</summary>
    private ReconciliationInjectivityEnforcement Enforcement { get; }

    /// <summary>The structural reconciliation contract the maintained encoder produces against — the one shared library value, so a maintained image always combines against a codec-produced or wire-served image.</summary>
    private static ReconciliationContract StructuralContract => StructuralReconciliationContract.Value;

    /// <summary>The enforcement a maintainer defaults to when the options carry no diagnostic override: none in release, a debug-assert membership check in debug builds so a non-net delta is caught during development instead of silently corrupting the sketch.</summary>
    private static ReconciliationInjectivityEnforcement DefaultEnforcement =>
#if DEBUG
        ReconciliationInjectivityEnforcement.DebugAssert;
#else
        ReconciliationInjectivityEnforcement.None;
#endif

    /// <summary>The reconciliation feed the maintainer advances by the same delta it folds into the encoder, so the feed's committed index and the encoder's net set share one generation; it is also the enumeration source a re-seed or a dirty-rebuild rebuilds the encoder from.</summary>
    private ReplicationIndexFeed Feed { get; }

    /// <summary>The governed pool the maintained encoder and the serve's transient buffers rent from; the maintainer never disposes the pool.</summary>
    private MemoryPool<byte> Pool { get; }

    /// <summary>The host re-seed cadence and clock policy.</summary>
    private IncrementalSketchMaintainerOptions Options { get; }

    /// <summary>The live maintained encoder, replaced wholesale on a re-seed or a dirty-rebuild.</summary>
    private ReconciliationEncoder Encoder { get; set; }

    /// <summary>The re-seed timestamp the time-based cadence measures its interval from, read through the injected time provider.</summary>
    private long LastReseedTimestamp { get; set; }

    /// <summary>The generation the encoder's net set is at: the count of committed delta batches folded in so far. A serve pins this with the prefix it copies.</summary>
    public long Generation { get; private set; }

    /// <summary>The dataset StateId the encoder's net set is at: the seed generation's StateId until the first committed delta, then the most recent committed delta's.</summary>
    public NodeIdentifier LastStateId { get; private set; }

    /// <summary>The dictionary epoch the projected item keys are valid within; it is fixed for the life of the maintainer because the structural projection packs epoch-relative term identifiers and epochs are minted only per engine open.</summary>
    public ulong DictionaryEpoch { get; private set; }

    /// <summary>The number of committed add and remove operations folded in since the last re-seed; the operation-budget cadence raises <see cref="NeedsReseed"/> on it.</summary>
    public long OperationsSinceReseed { get; private set; }

    /// <summary>Whether the re-seed cadence has tripped since the last re-seed: the operation budget has been met or the time interval has elapsed. It is a hint for the host cadence — the encoder is still correct and is served normally while it is set; only <see cref="Reseed"/> clears it. The observer never rebuilds, so this never blocks a commit.</summary>
    public bool NeedsReseed { get; private set; }

    /// <summary>The host-bound forward seam this maintainer offers in place of a fresh-encoder codec: it ignores the item span and copies the maintained encoder's symbol prefix, taking the gate itself so a standalone caller is safe. Pass to <see cref="SketchPersistence.PersistSketch"/> when a caller frames its own image outside <see cref="WriteSketchImage"/>.</summary>
    public SketchReconciliationDelegates.EncodeSketchSymbols Encode => EncodeMaintainedSymbols;

    /// <summary>Creates a maintainer seeded from the feed's committed snapshot. Construction runs at wiring time before commits begin, so it seeds without the gate.</summary>
    /// <param name="feed">The reconciliation feed the maintainer both advances and re-seeds from.</param>
    /// <param name="pool">The governed pool the encoder and serve buffers rent from.</param>
    /// <param name="options">The re-seed cadence and clock policy.</param>
    /// <param name="dictionaryEpoch">The dictionary epoch the seed's projected keys are valid within; fixed for the maintainer's life.</param>
    /// <exception cref="ArgumentNullException">A reference argument is <see langword="null"/>.</exception>
    public IncrementalSketchMaintainer(ReplicationIndexFeed feed, MemoryPool<byte> pool, IncrementalSketchMaintainerOptions options, ulong dictionaryEpoch)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(options);

        Feed = feed;
        Pool = pool;
        Options = options;
        DictionaryEpoch = dictionaryEpoch;
        Enforcement = options.EnforcementOverride ?? DefaultEnforcement;
        LastReseedTimestamp = options.TimeProvider.GetTimestamp();

        //One coherent read seeds both the encoder's net set and the StateId a pre-first-commit serve pins, so a
        //receipt taken before any delta names the seed generation instead of an empty identifier.
        ReplicationGeneration seed = feed.Current();
        LastStateId = seed.StateId;
        Encoder = BuildEncoderFrom(seed.Index);
    }

    /// <summary>
    /// Advances the feed by one committed default-graph delta and folds the same delta into the maintained encoder.
    /// This is the <c>DefaultGraphDeltaObserver</c> the dataset fires: it binds as a method group without a closure.
    /// The delta is the net effective add/remove set, so a projected add always enters an absent item and a projected
    /// remove always clears a present one — the exact-transition precondition the encoder's no-enforcement mode needs.
    /// The feed is advanced first so a fault in the incremental fold cannot desynchronize the committed index the
    /// dirty-rebuild recovers from; a fold fault marks the encoder <see cref="Dirty"/> instead of poisoning the commit.
    /// </summary>
    /// <param name="additions">The triples the commit added to the default graph.</param>
    /// <param name="removals">The triples the commit removed from the default graph.</param>
    /// <param name="stateId">The dataset StateId the commit produced.</param>
    /// <param name="causality">The commit's causality annotation; accepted so the maintainer matches the observer seam and ignored — the sketch tracks only the committed triple set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="additions"/> or <paramref name="removals"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The maintainer is disposed.</exception>
    public void OnDefaultGraphDelta(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals, NodeIdentifier stateId, CommitCausality? causality)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ArgumentNullException.ThrowIfNull(removals);
        ObjectDisposedException.ThrowIf(Disposed, this);

        lock(gate)
        {
            //Advance the feed FIRST: it is the durable-truth committed index a dirty-rebuild recovers from, and it
            //cannot throw on the net-effective delta, so it completes before anything that could fault. The generation
            //and StateId a serve pins track the feed, so they advance here regardless of the fold's outcome.
            Feed.Advance(additions, removals, stateId);
            Generation++;
            LastStateId = stateId;
            OperationsSinceReseed += additions.Count + removals.Count;

            //Fold the delta into the live encoder incrementally. Once dirty, skip the fold — the encoder is garbage
            //until the next touch rebuilds it. A mid-fold fault may leave the encoder half-folded; capture it as dirty
            //rather than rethrow onto the committing thread (which would poison a durably-committed journal append),
            //because a rebuild from Feed.Current() is fully self-correcting. When a checking enforcement is configured
            //the fault is a genuine injectivity violation and is allowed to surface instead of being swallowed.
            if(!Dirty)
            {
                try
                {
                    FoldDelta(additions, removals);
                }
                catch when(Enforcement == ReconciliationInjectivityEnforcement.None)
                {
                    Dirty = true;
                }
            }

            //Re-seed is off the commit path: only raise the hint. The host cadence calls Reseed; a serve meanwhile is
            //served from the current, valid, merely memory-heavy encoder.
            if(CadenceTripped())
            {
                NeedsReseed = true;
            }
        }
    }

    /// <summary>
    /// Serves a generation-pinned sketch image: produces and copies the maintained encoder's first
    /// <paramref name="symbolBudget"/> symbols into <paramref name="destination"/> through the shipped sketch framing,
    /// and returns the generation and StateId that prefix reflects. The generation capture and the symbol copy-out run
    /// under one gate, so the receipt and the served bytes describe one set version; the gate-held symbol producer is
    /// passed directly (it does not re-acquire the gate), and the image bytes are an owned copy the caller streams
    /// after the gate releases. If a prior fold faulted, the encoder is rebuilt from the committed index first; a
    /// pending <see cref="NeedsReseed"/> does not force a rebuild here — the current encoder is valid and is served.
    /// </summary>
    /// <param name="symbolBudget">The number of coded symbols to serve.</param>
    /// <param name="pool">The pool the transient symbol buffer is rented from.</param>
    /// <param name="destination">The sink for the sketch image.</param>
    /// <returns>The generation and StateId the served prefix reflects.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pool"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The maintainer is disposed.</exception>
    public SketchServeReceipt WriteSketchImage(int symbolBudget, MemoryPool<byte> pool, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(Disposed, this);

        lock(gate)
        {
            //Recover a fault-dirtied encoder before serving; a no-op on the healthy path.
            EnsureCurrentEncoder();

            //The gate is held across the whole persist, and the gate-held symbol producer is passed directly (it
            //does not re-acquire), so the generation capture and the symbol production observe one coherent set
            //version without relying on lock reentrancy.
            SketchServeReceipt receipt = new(Generation, LastStateId, symbolBudget);
            SketchPersistence.PersistSketch(ReadOnlySpan<ContentKey128>.Empty, SketchContract.Structural, symbolBudget, ChecksumAlgorithm.XxHash3, pool, EncodeMaintainedSymbolsUnderGate, destination);

            return receipt;
        }
    }

    /// <summary>
    /// Re-seeds the maintained encoder from the feed's current committed index, reclaiming the append-only arena and
    /// pending-cursor heap that grew with churn since the last seed. The rebuild reads the feed under the gate, where
    /// the feed's index and the encoder's net set are coherent, so the rebuilt encoder is exactly the current net set;
    /// it then re-produces the previously served symbol prefix under the gate so no subsequent serve pays that drain.
    /// This is the host cadence's entry point (scrub/persist); the observer never calls it — it only raises
    /// <see cref="NeedsReseed"/>. Re-seeding also clears a <see cref="Dirty"/> encoder.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The maintainer is disposed.</exception>
    public void Reseed()
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        lock(gate)
        {
            ReseedUnderGate();
        }
    }

    /// <summary>
    /// Asserts the maintainer's fixed dictionary epoch and, when it matches, re-seeds from the current committed set —
    /// the durable-sketch epoch-check hook. The structural projection packs epoch-relative term identifiers, so the
    /// maintainer's epoch is fixed at engine open and cannot change mid-run: the live feed still holds OLD-epoch term
    /// ids, so rebuilding under a NEW epoch would serve a peer a garbage difference. A supplied epoch that differs from
    /// the current one therefore THROWS and directs the caller to reopen the engine, which is the only place a new
    /// epoch is minted and the only place a maintainer can be seeded at it.
    /// </summary>
    /// <param name="dictionaryEpoch">The dictionary epoch to validate against; must equal the current epoch.</param>
    /// <exception cref="ObjectDisposedException">The maintainer is disposed.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="dictionaryEpoch"/> differs from the maintainer's fixed epoch — a mid-run epoch change that requires an engine reopen.</exception>
    public void RebuildForEpoch(ulong dictionaryEpoch)
    {
        ObjectDisposedException.ThrowIf(Disposed, this);

        lock(gate)
        {
            if(dictionaryEpoch != DictionaryEpoch)
            {
                throw new InvalidOperationException(
                    $"A dictionary-epoch transition (from {DictionaryEpoch} to {dictionaryEpoch}) cannot be applied to a live maintainer: the reconciliation feed still holds old-epoch term identifiers, so rebuilding the encoder under a new epoch would serve a peer a garbage difference. Epochs are minted only per engine open; reopen the engine to seed a maintainer at the new epoch.");
            }

            ReseedUnderGate();
        }
    }

    /// <summary>
    /// Disposes the maintained encoder, releasing its rentals; the call is idempotent. The borrowed feed and pool are
    /// not disposed. Hard teardown constraint: dispose ONLY after the dataset has stopped accepting commits — after
    /// dispose the observer throws <see cref="ObjectDisposedException"/> on the committing thread, which would surface
    /// as a commit fault. Unsubscribe or quiesce the dataset first, then dispose.
    /// </summary>
    public void Dispose()
    {
        lock(gate)
        {
            if(Disposed)
            {
                return;
            }

            Disposed = true;
            Encoder.Dispose();
        }
    }

    /// <summary>The standalone-safe symbol producer: takes the gate, recovers a fault-dirtied encoder, then copies the maintained encoder's symbol prefix. Bound to <see cref="Encode"/> for callers that persist outside <see cref="WriteSketchImage"/> and do not already hold the gate.</summary>
    /// <param name="items">Ignored: the maintained encoder is the symbol source, not a whole-set re-projection.</param>
    /// <param name="symbolCount">The number of symbols to copy.</param>
    /// <param name="symbolWidth">The serialized width of one symbol in bytes — the sum field followed by the checksum field.</param>
    /// <param name="destination">The buffer to fill; exactly <paramref name="symbolCount"/> times <paramref name="symbolWidth"/> bytes long.</param>
    private void EncodeMaintainedSymbols(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        lock(gate)
        {
            EnsureCurrentEncoder();
            EncodeMaintainedSymbolsUnderGate(items, symbolCount, symbolWidth, destination);
        }
    }

    /// <summary>Copies the maintained encoder's symbol prefix into the destination, ignoring the item span; the symbols are the serve, not a re-projection of the passed items. The caller MUST hold the gate and MUST have recovered any dirty encoder first; it extends the produced prefix as needed.</summary>
    /// <param name="items">Ignored: the maintained encoder is the symbol source, not a whole-set re-projection.</param>
    /// <param name="symbolCount">The number of symbols to copy.</param>
    /// <param name="symbolWidth">The serialized width of one symbol in bytes — the sum field followed by the checksum field.</param>
    /// <param name="destination">The buffer to fill; exactly <paramref name="symbolCount"/> times <paramref name="symbolWidth"/> bytes long.</param>
    private void EncodeMaintainedSymbolsUnderGate(ReadOnlySpan<ContentKey128> items, int symbolCount, int symbolWidth, Span<byte> destination)
    {
        _ = items;

        while(Encoder.ProducedCount < symbolCount)
        {
            Encoder.ProduceNext();
        }

        int checksumWidth = symbolWidth - ContentKey128.ByteWidth;
        for(int i = 0; i < symbolCount; i++)
        {
            ReconciliationSymbol symbol = Encoder.SymbolAt(i);
            symbol.Sum.Span.CopyTo(destination.Slice(i * symbolWidth, ContentKey128.ByteWidth));
            symbol.Checksum.Span.CopyTo(destination.Slice((i * symbolWidth) + ContentKey128.ByteWidth, checksumWidth));
        }
    }

    /// <summary>Folds one net-effective delta into the live encoder: each added triple enters its projected item, each removed triple clears it. The caller holds the gate. A fault here may leave the encoder half-folded — the caller decides whether to isolate it as dirty or let it surface.</summary>
    /// <param name="additions">The added triples to enter.</param>
    /// <param name="removals">The removed triples to clear.</param>
    private void FoldDelta(IReadOnlyCollection<EncodedTriple> additions, IReadOnlyCollection<EncodedTriple> removals)
    {
        Span<byte> item = stackalloc byte[ContentKey128.ByteWidth];
        foreach(EncodedTriple triple in additions)
        {
            StructuralReconciliationProjection.Project(triple).WriteBytes(item);
            Encoder.Add(item);
        }

        foreach(EncodedTriple triple in removals)
        {
            StructuralReconciliationProjection.Project(triple).WriteBytes(item);
            Encoder.Remove(item);
        }
    }

    /// <summary>Rebuilds the encoder from the current committed index if a prior fold faulted and left it dirty; a no-op on the healthy path. The caller holds the gate. The rebuild discards the half-folded state and re-projects the current net set, so it is fully self-correcting.</summary>
    private void EnsureCurrentEncoder()
    {
        if(Dirty)
        {
            ReseedUnderGate();
        }
    }

    /// <summary>Decides whether the re-seed cadence has tripped: the operation budget is met, or the time interval has elapsed. The caller holds the gate. It only raises the hint — it never rebuilds — so it is safe on the commit path.</summary>
    /// <returns><see langword="true"/> when a re-seed is due on the host cadence.</returns>
    private bool CadenceTripped()
    {
        if(OperationsSinceReseed >= Options.ReseedOperationBudget)
        {
            return true;
        }

        if(Options.ReseedInterval != Timeout.InfiniteTimeSpan && Options.TimeProvider.GetElapsedTime(LastReseedTimestamp) >= Options.ReseedInterval)
        {
            return true;
        }

        return false;
    }

    /// <summary>Rebuilds the encoder from the feed's current index and resets the cadence counters and hints. The caller holds the gate, where the feed and the encoder are coherent.</summary>
    private void ReseedUnderGate()
    {
        RebuildEncoderFrom(Feed.Current().Index);
        OperationsSinceReseed = 0;
        NeedsReseed = false;
        LastReseedTimestamp = Options.TimeProvider.GetTimestamp();
    }

    /// <summary>
    /// Builds a fresh encoder over <paramref name="index"/>, records the outgoing encoder's produced-symbol count,
    /// disposes the outgoing encoder, swaps the fresh one in, clears the dirty flag, and then re-produces the recorded
    /// prefix on the fresh encoder. Builds before disposing so a failed rebuild leaves the live encoder untouched.
    /// </summary>
    /// <remarks>
    /// A fresh encoder starts at <c>ProducedCount == 0</c>, so the FIRST serve after a swap would otherwise pay the
    /// whole <c>ProduceNext</c> drain under the gate — measured 436–539 ms for a 2000-symbol prefix over a 120k-item
    /// set — while the commit observer blocks on the same gate. Re-producing the previous prefix here, still on the
    /// cadence thread and under the gate, amortizes that dominant in-gate cost onto the cadence so no serve pays it.
    /// </remarks>
    /// <param name="index">The committed index to rebuild the encoder from.</param>
    private void RebuildEncoderFrom(ColumnarTripleIndex index)
    {
        int previousProducedCount = Encoder.ProducedCount;
        ReconciliationEncoder rebuilt = BuildEncoderFrom(index);
        Encoder.Dispose();
        Encoder = rebuilt;
        Dirty = false;

        while(Encoder.ProducedCount < previousProducedCount)
        {
            Encoder.ProduceNext();
        }
    }

    /// <summary>Builds a fresh encoder over the index's triples, projecting each to its structural item. On a mid-build throw it disposes the partial encoder so no rental leaks.</summary>
    /// <param name="index">The committed index to project.</param>
    /// <returns>The freshly built encoder over the index's net set.</returns>
    private ReconciliationEncoder BuildEncoderFrom(ColumnarTripleIndex index)
    {
        ReconciliationEncoder rebuilt = new(StructuralContract, Enforcement, Pool, Options.CellCapacityHint);
        try
        {
            Span<byte> item = stackalloc byte[ContentKey128.ByteWidth];
            foreach(EncodedTriple triple in index.EnumerateTriples())
            {
                StructuralReconciliationProjection.Project(triple).WriteBytes(item);
                rebuilt.Add(item);
            }
        }
        catch
        {
            rebuilt.Dispose();

            throw;
        }

        return rebuilt;
    }
}
