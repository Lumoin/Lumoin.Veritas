using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Verisync.Core;
using DottedElement = Lumoin.Verisync.Core.DottedEntry<Lumoin.Veritas.Core.EncodedTriple>;

namespace Lumoin.Veritas.Database;

/// <summary>
/// One dotted exchange's session seams bound over the engine's dataset, ledger, and pinned projection: the
/// initiator's classification of the decoded difference, the uniform apply that admits peer entries and
/// answers covered dots as push-drops, the drop apply, and the terminal context fold — each apply landing as
/// one durable, causality-annotated commit through <see cref="ReconcileWriteBack.ApplyAdoptAsync"/>, whose
/// commit-time guard re-validates every adoption against the live ledger. Wire classification reads the PINNED
/// snapshot context only — mid-flight staleness is the commit-time guard's job — and the binding accumulates
/// the committed and transferred counts the operator outcome is composed from. One instance per exchange; the
/// session's single consumer loop serializes every call, so the binding holds no synchronization.
/// </summary>
internal sealed class DottedReconcileBinding
{
    /// <summary>The mutable dataset the adopt commits land in.</summary>
    private MutableSparqlDataset Dataset { get; }

    /// <summary>The dotted commit ledger the adopt plans are guarded against.</summary>
    private DottedCommitLedger Ledger { get; }

    /// <summary>The pinned projection of the snapshot this exchange reconciles.</summary>
    private DottedLedgerProjection Projection { get; }

    /// <summary>The peer's exchanged context in the house representation, converted once on first use — the session hands the same exchanged state to every seam.</summary>
    private CausalContext? peerContext;

    /// <summary>The addition assignments the committed adopt plans admitted.</summary>
    public int AdoptedAdditions { get; private set; }

    /// <summary>The drop assignments the committed adopt plans applied.</summary>
    public int AdoptedDrops { get; private set; }

    /// <summary>The entries pushed to the peer as its genuine additions.</summary>
    public int PushedEntries { get; private set; }

    /// <summary>The local tombstone dots answered back to the peer as push-drops.</summary>
    public int PushedDropDots { get; private set; }

    /// <summary>Creates the binding for one exchange.</summary>
    /// <param name="dataset">The mutable dataset the adopt commits land in.</param>
    /// <param name="ledger">The dotted commit ledger the adopt plans are guarded against.</param>
    /// <param name="projection">The pinned projection of the snapshot this exchange reconciles.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DottedReconcileBinding(MutableSparqlDataset dataset, DottedCommitLedger ledger, DottedLedgerProjection projection)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(projection);

        Dataset = dataset;
        Ledger = ledger;
        Projection = projection;
    }

    /// <summary>
    /// The initiator's classification seam: partitions the decoded dotted difference against the peer's
    /// EXCHANGED context — an item this side holds whose dot the peer observed and removed becomes a local
    /// drop, an item it holds that the peer never observed becomes a push, and an item it lacks becomes a
    /// fetch. Classification reads the exchanged context and the pinned projection only, never the live ledger.
    /// </summary>
    /// <param name="decodedItems">The decoded symmetric difference in the dotted item domain.</param>
    /// <param name="peerContextState">The peer's exchanged context.</param>
    /// <returns>The partitioned resolution.</returns>
    public ReconciliationDifferenceResolution<DottedElement> ResolveDifference(IReadOnlyList<ReadOnlyMemory<byte>> decodedItems, VectorClockState peerContextState)
    {
        CausalContext peer = PeerContext(peerContextState);
        ImmutableArray<ReadOnlyMemory<byte>>.Builder fetch = ImmutableArray.CreateBuilder<ReadOnlyMemory<byte>>();
        ImmutableArray<ReconciliationElementEntry<DottedElement>>.Builder push = ImmutableArray.CreateBuilder<ReconciliationElementEntry<DottedElement>>();
        ImmutableArray<DotState>.Builder localDrops = ImmutableArray.CreateBuilder<DotState>();
        foreach(ReadOnlyMemory<byte> item in decodedItems)
        {
            if(Projection.Projection.TryResolve(item, out DottedElement? entry))
            {
                CausalDot dot = DottedLedgerProjection.ToCausalDot(entry);
                if(peer.Covers(dot))
                {
                    localDrops.Add(new DotState(entry.Replica, entry.Counter));
                }
                else
                {
                    push.Add(new ReconciliationElementEntry<DottedElement>(item, entry));
                }
            }
            else
            {
                fetch.Add(item);
            }
        }

        PushedEntries += push.Count;

        return new ReconciliationDifferenceResolution<DottedElement>(fetch.DrainToImmutable(), push.DrainToImmutable(), localDrops.DrainToImmutable());
    }

    /// <summary>
    /// The uniform apply seam: an entry whose dot the PINNED snapshot context already covers is a local
    /// tombstone — not adopted, its dot answered as a push-drop — and every other entry is adopted through one
    /// durable annotated commit whose commit-time guard re-validates against the live ledger. The peer's
    /// context folds with the same commit.
    /// </summary>
    /// <param name="entries">The item-to-element resolutions to apply.</param>
    /// <param name="peerContextState">The peer's exchanged context.</param>
    /// <param name="cancellationToken">Cancels the apply.</param>
    /// <returns>The push-drop dots the pinned context already covered.</returns>
    /// <exception cref="DottedAdoptConflictExhaustedException">The adopt write-back exhausted its commit retries.</exception>
    public async ValueTask<ImmutableArray<DotState>> ApplyElementsAsync(IReadOnlyList<ReconciliationElementEntry<DottedElement>> entries, VectorClockState peerContextState, CancellationToken cancellationToken)
    {
        CausalContext peer = PeerContext(peerContextState);
        ImmutableArray<DotState>.Builder pushDrops = ImmutableArray.CreateBuilder<DotState>();

        //Adoptions coalesce PER TRIPLE: a triple pushed under two peer dots arrives as two entries, and the
        //plan expects one assignment carrying every adopted dot of its triple.
        Dictionary<EncodedTriple, List<CausalDot>> adoptionsByTriple = [];
        List<EncodedTriple> adoptionOrder = [];
        foreach(ReconciliationElementEntry<DottedElement> entry in entries)
        {
            DottedElement element = entry.Element;
            CausalDot dot = DottedLedgerProjection.ToCausalDot(element);
            if(Projection.SnapshotContext.Covers(dot))
            {
                pushDrops.Add(new DotState(element.Replica, element.Counter));

                continue;
            }

            if(!adoptionsByTriple.TryGetValue(element.Value, out List<CausalDot>? dots))
            {
                dots = [];
                adoptionsByTriple[element.Value] = dots;
                adoptionOrder.Add(element.Value);
            }

            dots.Add(dot);
        }

        List<DottedTripleAssignment> adoptions = new(adoptionOrder.Count);
        foreach(EncodedTriple triple in adoptionOrder)
        {
            adoptions.Add(new DottedTripleAssignment(triple, [.. adoptionsByTriple[triple]]));
        }

        await ApplyAdoptGuardedAsync(adoptions, [], peer, cancellationToken).ConfigureAwait(false);
        PushedDropDots += pushDrops.Count;

        return pushDrops.DrainToImmutable();
    }

    /// <summary>
    /// The drop seam: resolves each dot against the pinned snapshot to the triple it tags and applies the drops
    /// through one durable annotated commit, folding the peer's context with it. A dot outside the pinned
    /// snapshot resolves to nothing — its entry was never pinned here — and the context fold still carries the
    /// observation.
    /// </summary>
    /// <param name="dots">The dots whose present entries this side drops.</param>
    /// <param name="peerContextState">The peer's exchanged context.</param>
    /// <param name="cancellationToken">Cancels the apply.</param>
    /// <returns>A task that completes when the drops have committed.</returns>
    /// <exception cref="DottedAdoptConflictExhaustedException">The adopt write-back exhausted its commit retries.</exception>
    public async ValueTask ApplyDropsAsync(IReadOnlyList<DotState> dots, VectorClockState peerContextState, CancellationToken cancellationToken)
    {
        CausalContext peer = PeerContext(peerContextState);
        Dictionary<EncodedTriple, List<CausalDot>> dropsByTriple = [];
        List<EncodedTriple> dropOrder = [];
        foreach(DotState dotState in dots)
        {
            CausalDot dot = DottedLedgerProjection.ToCausalDot(dotState);
            if(!Projection.TryResolveDot(in dot, out EncodedTriple triple))
            {
                continue;
            }

            if(!dropsByTriple.TryGetValue(triple, out List<CausalDot>? tripleDots))
            {
                tripleDots = [];
                dropsByTriple[triple] = tripleDots;
                dropOrder.Add(triple);
            }

            tripleDots.Add(dot);
        }

        List<DottedTripleAssignment> dropAssignments = new(dropOrder.Count);
        foreach(EncodedTriple triple in dropOrder)
        {
            dropAssignments.Add(new DottedTripleAssignment(triple, [.. dropsByTriple[triple]]));
        }

        await ApplyAdoptGuardedAsync([], dropAssignments, peer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The terminal context-fold seam for quiescent paths: folds the peer's exchanged context through one causality-only commit when it adds knowledge; a no-op when it is already covered.</summary>
    /// <param name="peerContextState">The peer's exchanged context.</param>
    /// <param name="cancellationToken">Cancels the fold.</param>
    /// <returns>A task that completes when the fold has committed.</returns>
    /// <exception cref="DottedAdoptConflictExhaustedException">The adopt write-back exhausted its commit retries.</exception>
    public async ValueTask MergeContextAsync(VectorClockState peerContextState, CancellationToken cancellationToken)
    {
        await ApplyAdoptGuardedAsync([], [], PeerContext(peerContextState), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs one guarded adopt write-back, accumulating the committed counts and converting retry exhaustion into the session's documented fail-closed signal.</summary>
    /// <param name="peerAdditions">The peer entries classified as genuine adds.</param>
    /// <param name="peerDrops">The peer-commanded removals.</param>
    /// <param name="peer">The peer's exchanged context, folded whole.</param>
    /// <param name="cancellationToken">Cancels the write-back.</param>
    /// <returns>A task that completes when the write-back landed or no-opped.</returns>
    /// <exception cref="DottedAdoptConflictExhaustedException">The write-back exhausted its commit retries.</exception>
    private async ValueTask ApplyAdoptGuardedAsync(IReadOnlyList<DottedTripleAssignment> peerAdditions, IReadOnlyList<DottedTripleAssignment> peerDrops, CausalContext peer, CancellationToken cancellationToken)
    {
        DottedAdoptReceipt receipt = await ReconcileWriteBack.ApplyAdoptAsync(Dataset, Ledger, peerAdditions, peerDrops, peer, cancellationToken: cancellationToken).ConfigureAwait(false);
        if(receipt.Outcome == WriteBackOutcome.ConflictExhausted)
        {
            throw new DottedAdoptConflictExhaustedException();
        }

        AdoptedAdditions += receipt.AdoptedAdditions;
        AdoptedDrops += receipt.AdoptedDrops;
    }

    /// <summary>Converts the peer's exchanged context once and caches it — the session hands the same exchanged state to every seam of one exchange.</summary>
    /// <param name="peerContextState">The peer's exchanged context.</param>
    /// <returns>The house representation.</returns>
    private CausalContext PeerContext(VectorClockState peerContextState)
    {
        return peerContext ??= DottedLedgerProjection.ToCausalContext(peerContextState);
    }
}
