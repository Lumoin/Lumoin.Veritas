using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Core.Persistence.Segment;

namespace Lumoin.Veritas.Core.Persistence;

/// <summary>
/// The result of a <see cref="DurableSystemOfRecordStore.TryLoad"/>. When <see cref="Outcome"/> is
/// <see cref="DurableSystemOfRecordLoadOutcome.Loaded"/>, <see cref="Dictionary"/> carries the recovered terms
/// (interned into the caller's pool) and <see cref="Triples"/> is the owned, pooled default-graph system-of-record
/// the caller disposes; otherwise both are <see langword="null"/>. <see cref="NamedGraphs"/> carries the recovered
/// named graphs, each its graph-name term id paired with its owned, pooled triples the caller disposes (empty when
/// none, or when not loaded). <see cref="Sidecar"/> is the warm-loaded default-graph columnar index when one was
/// persisted and verified, or <see langword="null"/> when none was persisted or it was re-derivable damage — it is
/// a GC-backed value with no disposal contract.
/// </summary>
/// <remarks>
/// The three recovery-fidelity flags are additive and default to <see langword="false"/>, so an existing
/// caller that inspects only <see cref="Outcome"/> is unaffected while a host that cares can distinguish a
/// degraded or rolled-back recovery from a clean one: a <see cref="DurableSystemOfRecordLoadOutcome.Loaded"/>
/// generation is never silently indistinguishable from committed truth WITHIN WHAT SURVIVING EVIDENCE CAN
/// ATTEST — a rollback past a generation that no surviving pointer, retained copy, or manifest names is
/// locally undetectable, the epistemic floor of any local signal. <see cref="IsDegraded"/> means the
/// recovery could not follow any surviving CURRENT pointer and scanned the manifests directly;
/// <see cref="CommitEvidenced"/> means a retained CURRENT copy attests the loaded generation was committed
/// (always true on a pointer-followed load); <see cref="IsRollback"/> means the loaded generation is older
/// than the newest generation the surviving evidence names — the live pointer's or the newest verifying
/// retained copy's — because everything newer failed verification and recovery fell back to an intact older
/// generation. Recovery stays read-only: none of these repair the store.
/// </remarks>
/// <param name="Outcome">The load outcome.</param>
/// <param name="Dictionary">The recovered dictionary when loaded; otherwise <see langword="null"/>.</param>
/// <param name="Triples">The owned, pooled recovered default-graph triples when loaded — the caller disposes them; otherwise <see langword="null"/>.</param>
/// <param name="Sidecar">The warm-loaded default-graph columnar query index when a verified sidecar was persisted; otherwise <see langword="null"/> (the caller rebuilds it from <see cref="Triples"/>).</param>
/// <param name="Generation">The committed generation the load reflects, or 0 when none was found.</param>
/// <param name="NamedGraphs">The recovered named graphs — each graph-name term id with its owned, pooled triples the caller disposes; empty when none were persisted or the load did not succeed.</param>
/// <param name="IsDegraded"><see langword="true"/> when the loaded generation was recovered by the degraded direct manifest scan (no surviving CURRENT pointer) rather than by following a CURRENT pointer.</param>
/// <param name="CommitEvidenced"><see langword="true"/> when a retained CURRENT copy attests the loaded generation was committed; always true on a pointer-followed load, false only on an evidence-less degraded pick (a manifest with no proof it was ever committed).</param>
/// <param name="IsRollback"><see langword="true"/> when the loaded generation is older than the generation the live pointer named — the live generation's artifacts failed verification and recovery fell back to an intact older generation.</param>
/// <param name="ProvenanceEpoch">The persisted dataset state identifier the generation was captured from (the manifest's provenance epoch), <see cref="Lumoin.Veritas.Core.Hypertrie.Storage.NodeIdentifier"/>-valued as an <c>unchecked</c> reinterpretation of this <see cref="long"/>; <c>0</c> when the generation bound no dataset state. A durable dataset-journal recovery cross-checks the recovered generation against a journal head through this identifier.</param>
/// <param name="ValueIndexes">The verified value-index sidecar image when one was persisted, structurally valid, and stamped with exactly this generation's provenance epoch; otherwise <see langword="null"/> (the registered access methods rebuild from the served store at the first probe). Like <see cref="Sidecar"/> it is a GC-backed value with no disposal contract.</param>
/// <param name="CausalityImage">The verified replication causality artifact's image bytes when the generation persisted one — a remove-aware database's dotted-ledger snapshot — otherwise <see langword="null"/>. A GC-backed copy with no disposal contract.</param>
/// <param name="CausalityRefused"><see langword="true"/> when the manifest names a causality artifact that failed its at-rest verification (missing, length-mismatched, or digest-refused). Distinguishable from an absent artifact so the engine surfaces the refusal under the baseline rule instead of silently serving as add-only.</param>
public readonly record struct DurableSystemOfRecordLoad(
    DurableSystemOfRecordLoadOutcome Outcome,
    TermDictionary? Dictionary,
    DecodedItemSegment? Triples,
    ColumnarTripleIndex? Sidecar,
    long Generation,
    IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> NamedGraphs,
    bool IsDegraded = false,
    bool CommitEvidenced = false,
    bool IsRollback = false,
    long ProvenanceEpoch = 0,
    ValueIndexImage? ValueIndexes = null,
    ReadOnlyMemory<byte>? CausalityImage = null,
    bool CausalityRefused = false);
