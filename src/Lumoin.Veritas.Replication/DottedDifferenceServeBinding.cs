using Lumoin.Verisync.Core;
using DottedElement = Lumoin.Verisync.Core.DottedEntry<Lumoin.Veritas.Core.EncodedTriple>;

namespace Lumoin.Veritas.Replication;

/// <summary>
/// One accepted dotted-difference serve's pinned state and seams as a single bundle: the ledger projection the
/// responder session reconciles, and the apply seams bound to exactly that pinned snapshot — wire
/// classification pins to the session's exchanged snapshot context, so the seams and the projection must come
/// from one snapshot instant, which handing them out together makes structural.
/// </summary>
/// <param name="Projection">The pinned ledger projection this serve reconciles.</param>
/// <param name="ApplyElements">The seam that admits the initiator's pushed entries as durable adopts, classifying against the pinned snapshot's context.</param>
/// <param name="ApplyDrops">The seam that applies the initiator's drops durably.</param>
/// <param name="MergeContext">The terminal context-fold seam the initiator's completion frame licenses.</param>
public sealed record DottedDifferenceServeBinding(
    DottedLedgerProjection Projection,
    ApplyReconciliationElementsDelegate<DottedElement> ApplyElements,
    ApplyReconciliationDropsDelegate<DottedElement> ApplyDrops,
    MergeReconciliationContextDelegate MergeContext);
