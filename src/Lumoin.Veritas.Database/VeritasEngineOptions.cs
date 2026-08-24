using System;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Epistemics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The configuration of a <see cref="VeritasEngine"/>: what is wired and how it
/// behaves. By default everything is wired and fully usable; setting a knob to
/// <c>null</c> is the deliberate optimisation that drops a feature for a
/// lighter, faster deployment that links less machinery.
/// </summary>
public sealed record VeritasEngineOptions
{
    /// <summary>The reasoning configuration; the default reasons fully (bounded), and <c>null</c> links no reasoning machinery so the engine serves simple-entailment results.</summary>
    public ReasoningConfiguration? Reasoning { get; init; } = ReasoningConfiguration.Default;

    /// <summary>The execution policy sizing the compute lane and the host's resource use.</summary>
    public ExecutionPolicy Execution { get; init; } = ExecutionPolicy.Default;

    /// <summary>The SPARQL-executor strategy policy every query engine this database constructs is built under — queries, ASK, streaming SELECT, update <c>WHERE</c> evaluation, and <c>sh:sparql</c> constraint validation; the default keeps the materialising executor everywhere. The policy selects between evaluation routes only; it never changes an answer.</summary>
    public SparqlEnginePolicy SparqlExecution { get; init; } = SparqlEnginePolicy.Default;

    /// <summary>The SPARQL Update semantic options every update this database executes runs under — currently the contextual-assertion <c>LOAD</c> destination policy (a plain <c>LOAD</c> lands in a fresh blank-node graph whose provenance the default graph records). Unlike <see cref="SparqlExecution"/>, these options CHANGE an update's effect by design; the default keeps the SPARQL-specification behaviour everywhere.</summary>
    public SparqlUpdateOptions SparqlUpdate { get; init; } = SparqlUpdateOptions.Default;

    /// <summary>The SPARQL <c>SERVICE</c> transport a federated query or update step uses — the outbound federation seam (in-process dispatch, an HTTP client, a cache); <see langword="null"/> means a non-silent <c>SERVICE</c> is unsupported. The caller's opaque <see cref="AccessContext"/> is forwarded to it per call.</summary>
    public SparqlServiceTransport? ServiceTransport { get; init; }

    /// <summary>The resolver for <c>FROM</c> / <c>FROM NAMED</c> dataset graphs and <c>LOAD</c> documents — the outbound graph-fetch seam. <see langword="null"/> leaves dataset clauses to the engine's store-local default (a clause IRI resolves to the loaded named graph of that name and refuses anything else by name — never a network fetch) and leaves <c>LOAD</c>, whose purpose is ingesting external documents, unsupported. A configured resolver overrides the store-local default entirely and serves both. It yields the source document's triples as an asynchronous stream, so <c>LOAD</c> encodes each triple as it arrives and applies nothing until the stream completes (a mid-stream failure leaves the target unchanged). The caller's opaque <see cref="AccessContext"/> is forwarded to it per call.</summary>
    public GraphSourceResolver? GraphSource { get; init; }

    /// <summary>The access-control policy consulted per candidate triple of every local graph read; <see langword="null"/> allows every triple at zero cost. The protocol (OAuth, DID/VC, ZKP, …) lives in this policy and the opaque <see cref="AccessContext"/> the caller supplies per query or update, not in the engine.</summary>
    public AccessControlDelegate? AccessControl { get; init; }

    /// <summary>The composed value-index registry: the accepted value-typed access methods this database's datasets maintain and consult. The default is <see cref="ValueIndexRegistry.Empty"/> — no methods, zero per-query overhead; hosts compose one through <see cref="ValueIndexRegistryBuilder"/>, whose acceptance ladder every registration passes at build time.</summary>
    public ValueIndexRegistry ValueIndexes { get; init; } = ValueIndexRegistry.Empty;

    /// <summary>The composed epistemic-reason registry: the dark-by-default epistemic-surface seam carrying the reason codes, explanations, and projection-coverage declarations this database's reasoning reports through. The default is <see cref="EpistemicReasonRegistry.Empty"/> — no codes, zero per-query overhead; hosts compose one through <see cref="EpistemicReasonRegistryBuilder"/>, whose acceptance ladder every registration passes at build time.</summary>
    public EpistemicReasonRegistry EpistemicReasons { get; init; } = EpistemicReasonRegistry.Empty;

    /// <summary>The composed value-layer datatype registry: the registered custom literal datatypes whose declared facets SPARQL <c>=</c>/<c>!=</c> comparisons and SHACL <c>sh:datatype</c> validation consult, threaded into every expression context and validation run this database creates. The default is <see cref="ValueDatatypeRegistry.Empty"/> — no definitions, one predicted branch per consult site, built-in semantics exactly; hosts compose one through <see cref="ValueDatatypeRegistryBuilder"/>, whose acceptance rule every registration passes at build time. Independent of <see cref="Reasoning"/> — a database with reasoning unwired still consults the registry.</summary>
    public ValueDatatypeRegistry ValueDatatypes { get; init; } = ValueDatatypeRegistry.Empty;

    /// <summary>The composed extension-function registry: the registered SPARQL extension functions (§17.6) IRI-named function calls evaluate through, threaded into every expression context and validation run this database creates. The default is <see cref="SparqlFunctionRegistry.Empty"/> — no functions, one predicted branch per call site, every extension-function IRI evaluating to the expression error value; hosts compose one through <see cref="SparqlFunctionRegistryBuilder"/>, whose acceptance rule every registration passes at build time. Independent of <see cref="Reasoning"/> — a database with reasoning unwired still consults the registry.</summary>
    public SparqlFunctionRegistry ExtensionFunctions { get; init; } = SparqlFunctionRegistry.Empty;

    /// <summary>The implicit timezone the SPARQL evaluator normalizes timezone-naive temporal operands with (SPARQL §17.3), threaded into every expression context this database creates. The default is <see cref="TimeSpan.Zero"/> (UTC). A registered value-index method that declares an implicit timezone must agree with this value — the engine refuses the composition loudly otherwise, so a probe and a scan can never order naive values differently.</summary>
    public TimeSpan ImplicitTimezone { get; init; }

    /// <summary>The clock the engine's owned background services schedule on — today the self-heal loop's round delays. The default is <see cref="TimeProvider.System"/>; a test injects a controllable clock so the loop's timing is driven, never waited out.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>
    /// Whether the hypertrie system of record is held resident at all times (<see cref="Columnar.HypertrieResidency.Eager"/>,
    /// the default — today's behaviour) or deferred so a warm-loaded columnar view answers columnar-capable shapes and
    /// the trie materialises on demand only when a query needs it (<see cref="Columnar.HypertrieResidency.Deferred"/>,
    /// the warm read-serving start). Applies only to a query-only database opened over a
    /// <see cref="Lumoin.Veritas.Core.Persistence.PersistenceStore"/>
    /// (<see cref="VeritasEngine.OpenAsync(Lumoin.Veritas.Core.Persistence.PersistenceStore, VeritasEngineOptions?, System.Threading.CancellationToken)"/>);
    /// the in-memory and mutable opens are always eager. Access-controlled queries always evaluate on the trie under
    /// both, so security is unchanged; deferred trades a possible cold-start trie build for not holding the trie at
    /// all on warm read generations.
    /// </summary>
    public HypertrieResidency HypertrieResidency { get; init; } = HypertrieResidency.Eager;

    /// <summary>The trace handler the engine emits a <see cref="GraphAlgorithmTraceEvent"/> to around every analytics <c>SERVICE</c> run (Started before, Completed after with the result-row count and duration), keyed by a per-run correlation id; <see langword="null"/> emits nothing at zero cost. The analytics observability seam — the same trace bus the query and inference events flow on.</summary>
    public TraceHandler<GraphAlgorithmTraceEvent>? AnalyticsTrace { get; init; }

    /// <summary>The trace handler the engine emits a <see cref="ReasoningTraceEvent"/> to for the strategy the reasoning materialisation selected on an immutable open, keyed by a per-run correlation id; <see langword="null"/> emits nothing at zero cost. The reasoning observability seam, the same trace bus the query and analytics events flow on.</summary>
    public TraceHandler<ReasoningTraceEvent>? ReasoningTrace { get; init; }

    /// <summary>The trace handler the engine emits a <see cref="ReasoningDecisionTraceEvent"/> to for a delegated beyond-RL decision on an immutable open, keyed by the materialisation's correlation id; <see langword="null"/> emits nothing at zero cost. The per-decision companion to <see cref="ReasoningTrace"/> on the same trace bus.</summary>
    public TraceHandler<ReasoningDecisionTraceEvent>? ReasoningDecisionTrace { get; init; }

    /// <summary>The trace handler the engine emits a <see cref="DatalogMaintenanceTraceEvent"/> to for each landed commit a reasoned MUTABLE engine maintains — the base-delta-in / closure-delta-out counts, the maintenance counters, and the elapsed time, keyed by the engine's per-open correlation id; <see langword="null"/> emits nothing at zero cost. The per-commit maintenance companion to <see cref="ReasoningTrace"/> on the same trace bus; it fires only on the reasoned mutable lane, once per commit that actually landed.</summary>
    public TraceHandler<DatalogMaintenanceTraceEvent>? ReasoningMaintenanceTrace { get; init; }

    /// <summary>The trace handler the engine emits a <see cref="SparqlExecutionTraceEvent"/> to for each operator evaluation, rewrite-rule application, and interception firing of every query engine this database constructs — queries, <c>ASK</c>, streaming <c>SELECT</c>, and update <c>WHERE</c> evaluation — keyed by a per-evaluation correlation id; <see langword="null"/> emits nothing at zero cost. The query-execution observability seam, the same trace bus the analytics and reasoning events flow on.</summary>
    public TraceHandler<SparqlExecutionTraceEvent>? SparqlExecutionTrace { get; init; }

    /// <summary>
    /// The append-only durable dataset-journal log a mutable database records its commits to; <see langword="null"/>
    /// (the default) keeps a mutable database in memory. When set, a mutable open writes every dataset commit
    /// durably (flush-before-ack) to this log, and a mutable reopen
    /// (<see cref="VeritasEngine.OpenMutableAsync(Lumoin.Veritas.Core.Persistence.PersistenceStore, VeritasEngineOptions?, System.Threading.CancellationToken)"/>)
    /// recovers the acked commits from it. Host-only: a browser runtime has no file system to append into and wires
    /// its own durable backend behind the same journal contract.
    /// </summary>
    public string? DatasetJournalPath { get; init; }

    /// <summary>
    /// The background storage self-heal policy a store-backed open runs a <see cref="StorageSelfHealService"/>
    /// under; <see langword="null"/> (the default) runs no background healing. When set, the store-backed opens
    /// (<see cref="VeritasEngine.OpenAsync(Lumoin.Veritas.Core.Persistence.PersistenceStore, VeritasEngineOptions?, System.Threading.CancellationToken)"/>
    /// and <see cref="VeritasEngine.OpenMutableAsync(Lumoin.Veritas.Core.Persistence.PersistenceStore, VeritasEngineOptions?, System.Threading.CancellationToken)"/>)
    /// start a background loop that periodically verifies, repairs, and atomically re-publishes the store's
    /// committed generation, and the engine serializes its own <see cref="VeritasEngine.Persist"/> against that
    /// loop's heal publish in-process. Single-process only: a store written by more than one process coordinates
    /// through the store's own atomic-publish contract, not this in-memory serialization.
    /// </summary>
    public SelfHealOptions? SelfHeal { get; init; }

    /// <summary>The trace handler the background storage self-heal loop emits its <see cref="StorageTraceEvent"/> stream to (verify verdicts, re-derive and named-loss outcomes, healed-generation and round-failed markers), keyed by a per-round correlation id; <see langword="null"/> emits nothing at zero cost. The storage observability seam, alongside <see cref="AnalyticsTrace"/>.</summary>
    public TraceHandler<StorageTraceEvent>? StorageTrace { get; init; }

    /// <summary>
    /// The checksum algorithm the id-bearing durable artifacts a store-backed open persists are written under —
    /// the system-of-record and dictionary segments, named-graph segments, manifests, the CURRENT pointer, the
    /// warm-start columnar sidecar, sketches, and loss records; <see langword="null"/> (the default) writes them
    /// under the built-in <see cref="ChecksumAlgorithm.XxHash3"/>, byte-identical to prior releases. Set this to a
    /// host-composed keyed message-authentication algorithm (built with <see cref="ChecksumAlgorithm.Create"/>, its
    /// compute delegate closed over the key at the composition root) to make those artifacts tamper-evident; pair it
    /// with a <see cref="ResolveChecksum"/> that resolves the same keyed id on read. The append-only journals are
    /// EXCLUDED by design: a journal record carries no in-band algorithm id, so a wrong or absent key on replay
    /// would silently truncate the log at the first mismatch instead of refusing — the journals stay on the built-in
    /// checksum until the journal record format carries an id (the anchored-attach journal header).
    /// </summary>
    public ChecksumAlgorithm? Checksum { get; init; }

    /// <summary>
    /// The read-side resolver a store-backed open verifies id-bearing durable artifacts through, mapping each
    /// artifact's on-disk checksum-algorithm id to its algorithm; <see langword="null"/> (the default) uses
    /// <see cref="ChecksumAlgorithm.DefaultResolver"/>, byte-identical to prior releases. A host reading artifacts
    /// written under a keyed <see cref="Checksum"/> supplies a resolver that maps the keyed id to the keyed
    /// algorithm (typically chaining to <see cref="ChecksumAlgorithm.DefaultResolver"/> for the built-ins); a
    /// resolver must map a keyed id ONLY when the key is present and must never fall back to a keyless algorithm, so
    /// a read under absent or wrong key refuses the artifact (a propagating <see cref="System.NotSupportedException"/>)
    /// rather than downgrading its integrity check. The journals verify under the built-in checksum regardless (see
    /// <see cref="Checksum"/>).
    /// </summary>
    public ResolveChecksumAlgorithmDelegate? ResolveChecksum { get; init; }

    /// <summary>
    /// The replica identity axis this HOST mints causal dots on — what makes a mutable database REMOVE-AWARE:
    /// with an identity, the engine keeps a dotted commit ledger beside the replication feed, every commit's
    /// journal entry carries its causality annotation, and a persist writes the at-rest causality artifact;
    /// <see langword="null"/> (the default) keeps the database add-only, byte-identical to prior behaviour.
    /// The identity is HOST state: it is supplied here at open, persisted by the host in its own configuration
    /// location, and never travels with store bytes — copying a store directory to seed a peer must not copy
    /// who the replica is. Replica-identity distinctness is a declared deployment obligation: two hosts
    /// minting concurrently under one identity produce colliding dots (distinct events under one name), which
    /// is silent corruption no local check can detect in general.
    /// </summary>
    public ReplicaAxis? ReplicaIdentity { get; init; }

    /// <summary>
    /// Whether a mutable open performs the EXPLICIT causality baseline step when the resumed store is not already
    /// remove-aware: after recovery, the open mints one fresh dot per present committed triple on the
    /// <see cref="ReplicaIdentity"/> axis, sets the causal context to exactly those dots, and commits the baseline
    /// as a causality-only annotated journal entry — from there every commit is annotated and every retraction is
    /// protected observed-remove knowledge. The step claims no knowledge that never existed: retractions from
    /// before the baseline stay outside observed-remove protection. It requires <see cref="ReplicaIdentity"/>
    /// (refused as an argument error without one) and a MUTABLE open (an immutable open cannot commit the
    /// baseline entry and refuses the request as an argument error rather than dropping it silently). The
    /// request's result is a VALUE on <see cref="VeritasEngine.ReplicationBaseline"/>, never an exception:
    /// <see cref="ReplicationBaselineOutcome.AlreadyRemoveAware"/> on a store that already is (a store created
    /// at this open included — the creation baseline covers it), <see cref="ReplicationBaselineOutcome.Baselined"/>
    /// when the step ran, and <see cref="ReplicationBaselineOutcome.RefusedCausalityTrace"/> on a store that
    /// carries a causality trace — a refused or torn causality artifact, annotated journal entries, or a broken
    /// causal lineage — without a recoverable pair, because a fresh baseline's counters could re-issue dots that
    /// surviving history already names for other events; the store then serves in its awaiting-baseline
    /// standing. Running the step at open is what makes the quiesce-before-baseline discipline structural for
    /// the local store: no commit races an open. Durability follows the store's own posture: with a durable
    /// journal the baseline entry is durable at once; without one it is durable from the next persist's
    /// causality artifact. <see langword="false"/> (the default) never baselines — an identity-supplied store
    /// without a causality pair stays add-only, never ambiently upgraded.
    /// </summary>
    public bool BaselineReplicationCausality { get; init; }

    /// <summary>
    /// The metadata-plane consultations an identity-bearing MUTABLE open makes when the deployment coordinates
    /// replica identity and the lineage baseline by consensus; <see langword="null"/> (the default) runs
    /// planeless, byte-identical to prior behaviour — the embedded and decentralized postures never set this.
    /// With seams, the open claims <see cref="ReplicaIdentity"/> on the coordinated record before minting under
    /// it, records the lineage baseline as a two-phase intent and confirm around the local durable commit, and
    /// reports the coordination's standing on <see cref="VeritasEngine.MetadataCoordination"/> as a VALUE. The
    /// plane is NEVER a liveness dependency: an undecided or unreachable consultation fails open and the open
    /// proceeds with the coordination pending; only a DEFINITE adverse answer — another live minter holds the
    /// identity, or the lineage already descends from a different baseline — refuses the open, because that is
    /// correctness rather than liveness. A host whose confirmed facts are already persisted beside its identity
    /// skips the plane on a routine reopen by leaving this <see langword="null"/> for that open.
    /// </summary>
    public MetadataCoordinationSeams? MetadataCoordination { get; init; }

    /// <summary>The fully-wired default options.</summary>
    public static VeritasEngineOptions Default { get; } = new();
}
