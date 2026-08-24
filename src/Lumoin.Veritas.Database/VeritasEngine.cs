using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Causality;
using Lumoin.Veritas.Core.Columnar;
using Lumoin.Veritas.Core.ContentAddressing;
using Lumoin.Veritas.Core.Columnar.Analytics;
using Lumoin.Veritas.Core.Encoding;
using Lumoin.Veritas.Core.Integrity;
using Lumoin.Veritas.Core.Memory;
using Lumoin.Veritas.Core.Persistence;
using Lumoin.Veritas.Core.Persistence.Journal;
using Lumoin.Veritas.Core.Persistence.Segment;
using Lumoin.Veritas.Core.Reconciliation;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Execution;
using Lumoin.Veritas.Core.Hypertrie;
using Lumoin.Veritas.Core.Hypertrie.AccessControl;
using Lumoin.Veritas.Core.Hypertrie.Editing;
using Lumoin.Veritas.Core.Hypertrie.Storage;
using Lumoin.Veritas.Core.Indexing;
using Lumoin.Veritas.Owl.Reasoning;
using Lumoin.Veritas.Rdf.Values;
using Lumoin.Veritas.Replication;
using Lumoin.Veritas.Shacl.Components;
using Lumoin.Veritas.Shacl.Loading;
using Lumoin.Veritas.Shacl.Validation;
using Lumoin.Veritas.Sparql.Algebra;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Parser;
using Lumoin.Veritas.Sparql.Results;
using Lumoin.Veritas.Sparql.Translation;
using DatasetJournalReplayEngine = Lumoin.Veritas.Core.Persistence.Journal.DatasetJournalRecovery;

namespace Lumoin.Veritas.Database;

/// <summary>
/// The Veritas database: one engine you open over data and query. It composes
/// the storage, the SPARQL query engine, the OWL reasoner, and the compute lane
/// into a coherent whole, fully wired and usable by default. The command-line,
/// MCP, and HTTP surfaces are thin transports that open one of these and serve
/// over it.
/// </summary>
/// <remarks>
/// Reasoning, when wired (the default), materialises its entailments into the
/// default graph as part of opening, so queries answer over the entailed graph.
/// The engine owns its compute lane and drains it on disposal.
/// </remarks>
public sealed class VeritasEngine: IAsyncDisposable
{
    /// <summary>The composed query engine an immutable database answers queries through, or <see langword="null"/> for a mutable database (which derives one per query off its dataset snapshot).</summary>
    private readonly SparqlQueryEngine? queryEngine;

    /// <summary>The mutable dataset a mutable database commits into and derives its per-query engine from, or <see langword="null"/> for an immutable database.</summary>
    private readonly MutableSparqlDataset? mutableDataset;

    /// <summary>The named-world registry of a mutable database, seeded with <see cref="mutableDataset"/> under <see cref="WellKnownWorlds.Primary"/>; <see langword="null"/> for an immutable database, whose world operations throw like its updates do.</summary>
    private readonly DatasetWorlds? worlds;

    /// <summary>The reconciliation feed tracking a mutable database's committed default graph, for reconciling from a peer; <see langword="null"/> for an immutable database.</summary>
    private readonly ReplicationIndexFeed? replicationFeed;

    /// <summary>The maintained incremental sketch encoder a mutable database serves its structural sketch from: subscribed to the default-graph delta observer seam, it advances <see cref="replicationFeed"/> and folds the same committed delta into one long-lived encoder under one gate; <see langword="null"/> for an immutable database. Owned and disposed by the database, strictly after commits have stopped.</summary>
    private readonly IncrementalSketchMaintainer? sketchMaintainer;

    /// <summary>The dotted commit ledger of a REMOVE-AWARE mutable database — the entry table, causal context, and StateId stamp advanced beside the feed by the composed delta observer — or <see langword="null"/> for an add-only or immutable database.</summary>
    private readonly DottedCommitLedger? commitLedger;

    /// <summary>The dotted commit ledger of a remove-aware mutable database, or <see langword="null"/> for an add-only or immutable database — the seam the dotted reconcile lanes and their battery read.</summary>
    internal DottedCommitLedger? CommitLedger
    {
        get
        {
            return commitLedger;
        }
    }

    /// <summary>Whether a host replica identity was supplied at open: with one, the database is remove-aware (<see cref="commitLedger"/> exists) or awaits the explicit baseline step; an immutable database and an identity-less mutable database carry <see langword="false"/>. Distinguishes <see cref="ReplicationCausalityState.AwaitingBaseline"/> from <see cref="ReplicationCausalityState.AddOnly"/> on the status surface.</summary>
    private readonly bool replicaIdentitySupplied;

    /// <summary>The value-based outcome of this open's explicit causality baseline request — the open-time result surfaced like recovery provenance; a refusal is an expected condition reported here, never thrown. <see cref="ReplicationBaselineOutcome.NotRequested"/> when the option was not set (an immutable database always).</summary>
    public ReplicationBaselineOutcome ReplicationBaseline { get; }

    /// <summary>The value-based standing of this open's metadata-plane coordination (<see cref="VeritasEngineOptions.MetadataCoordination"/>): what the identity claim and the two-phase lineage baseline established, surfaced like the baseline outcome. A pending or contested standing serves — an undecided plane fails open, per the plane's never-a-liveness-dependency constraint; only a definite adverse consultation refused the open, loudly, before this engine existed. <see cref="MetadataCoordinationStanding.NotConfigured"/> when no seams were set (an immutable database always).</summary>
    public MetadataCoordinationStanding MetadataCoordination { get; }

    /// <summary>The pool a mutable database's replication work (reconcile transients, sketch serving) rents from, owned and disposed by the database; also the durable dataset journal's per-append serialization pool when one is wired; <see langword="null"/> for an immutable database.</summary>
    private readonly VeritasMemoryPool<byte>? replicationPool;

    /// <summary>The durable dataset journal a mutable database records commits to and recovered its state from on open, owned and disposed by the database; <see langword="null"/> for an in-memory or immutable database.</summary>
    private readonly FileBackedDatasetJournal? datasetJournal;

    /// <summary>The compute lane an immutable database owns and drains on disposal, or <see langword="null"/> for a mutable database (whose per-query engines run lane-less).</summary>
    private readonly IComputeLane? computeLane;

    /// <summary>The term pool a store-opened database owns and disposes on disposal — it backs the recovered dictionary's terms for the engine's lifetime; <see langword="null"/> for an in-memory or mutable database, whose terms live in the caller's data or the dataset.</summary>
    private readonly Utf8StringPool? ownedTermPool;

    /// <summary>The triple pool a DEFERRED store-opened database owns and disposes on disposal — it backs the recovered triples the deferred source materialises the trie from on demand; <see langword="null"/> for an eager store-opened database (whose triples are consumed into the trie at open) or any other database.</summary>
    private readonly VeritasMemoryPool<EncodedTriple>? ownedTriplePool;

    /// <summary>The SPARQL <c>SERVICE</c> client (over the configured transport) the per-query engine federates through, or <see langword="null"/> when no transport is configured.</summary>
    private readonly SparqlClient? serviceClient;

    /// <summary>The configured <c>FROM</c> / <c>FROM NAMED</c> / <c>LOAD</c> graph-source resolver, or <see langword="null"/> when none is configured — in which case a query's dataset clause falls back to the engine's store-local <see cref="DatasetGraphSource"/> (the loaded named graphs, refusal by name for anything else) while <c>LOAD</c>, whose purpose is ingesting external documents, keeps refusing without a configured resolver.</summary>
    private readonly GraphSourceResolver? graphSource;

    /// <summary>The access-control policy consulted per candidate triple of every local graph read, or <see langword="null"/> to allow every triple.</summary>
    private readonly AccessControlDelegate? accessControl;

    /// <summary>The SPARQL-executor strategy policy every query engine this database constructs is built under; from <see cref="VeritasEngineOptions.SparqlExecution"/> (an immutable database reads it back off its composed engine).</summary>
    private readonly SparqlEnginePolicy sparqlPolicy;

    /// <summary>The SPARQL Update semantic options every update executes under (from <see cref="VeritasEngineOptions.SparqlUpdate"/>); the default on the immutable lanes, which accept no updates.</summary>
    private readonly SparqlUpdateOptions sparqlUpdateOptions;

    /// <summary>The implicit timezone every expression context this database creates normalizes naive temporal operands with (from <see cref="VeritasEngineOptions.ImplicitTimezone"/>; inferred from the open-time engine on the immutable paths).</summary>
    private TimeSpan EngineImplicitTimezone { get; }

    /// <summary>The value-layer datatype registry every expression context and validation run this database creates consults (from <see cref="VeritasEngineOptions.ValueDatatypes"/>; inferred from the open-time engine on the immutable paths).</summary>
    private ValueDatatypeRegistry EngineValueDatatypes { get; }

    /// <summary>The extension-function registry every expression context and validation run this database creates consults (from <see cref="VeritasEngineOptions.ExtensionFunctions"/>; inferred from the open-time engine on the immutable paths).</summary>
    private SparqlFunctionRegistry EngineExtensionFunctions { get; }

    /// <summary>The trace handler analytics-run events are emitted to, or <see langword="null"/> to emit none; from <see cref="VeritasEngineOptions.AnalyticsTrace"/>.</summary>
    private readonly TraceHandler<GraphAlgorithmTraceEvent>? analyticsTrace;

    /// <summary>The per-operator execution-trace handler threaded into every query engine this database constructs, or <see langword="null"/> for no tracing; from <see cref="VeritasEngineOptions.SparqlExecutionTrace"/>.</summary>
    private readonly TraceHandler<SparqlExecutionTraceEvent>? executionTrace;

    /// <summary>The checksum algorithm this engine's <see cref="Persist"/> writes the durable id-bearing artifacts under, or <see langword="null"/> to write under the built-in default; from <see cref="VeritasEngineOptions.Checksum"/>. A host-composed keyed algorithm makes the persisted artifacts tamper-evident.</summary>
    private ChecksumAlgorithm? PersistChecksum { get; }

    /// <summary>The resolver this engine's <see cref="Persist"/> reads the store's committed generation through when numbering the next one, or <see langword="null"/> to use the built-in resolver; from <see cref="VeritasEngineOptions.ResolveChecksum"/>. Pairs with <see cref="PersistChecksum"/> so a keyed store's prior generation is read under its key.</summary>
    private ResolveChecksumAlgorithmDelegate? PersistResolveChecksum { get; }

    /// <summary>The runner that brackets each analytics <c>SERVICE</c> run with trace events; one instance per engine so every run shares the engine's monotonic trace sequence.</summary>
    private GraphAnalyticsRunner AnalyticsRunner { get; } = new();

    /// <summary>The running background storage self-heal loop over the store this database was opened from, plus the resources it owns; <see langword="null"/> when no self-heal was wired (a non-store open, or store-backed with no <see cref="VeritasEngineOptions.SelfHeal"/>).</summary>
    private SelfHealRuntime? ActiveSelfHeal { get; set; }

    /// <summary>The number of retained per-generation CURRENT copies a self-heal publish keeps, matching the durable store's own retention window so a kept healed artifact always has a kept manifest.</summary>
    private const int SelfHealRetainedGenerationCount = 4;

    /// <summary>The symbol budget the generation's integrity sketch is persisted at (<see cref="Persist"/>) and a self-heal re-derive rebuilds it at, so the at-rest sketch keeps one size across persist and heal cycles. The sketch gates a peer heal by EQUALITY — a faithful healed set peels to an empty residual on the first combined symbol regardless of budget, and any unfaithful set is refused as a recovered or incomplete residual — so the budget sizes only the artifact, not the gate's soundness.</summary>
    private const int GenerationSketchSymbolBudget = 64;

    /// <summary>
    /// The provenance of a store-served database — which committed generation is served and whether recovery was
    /// degraded or rolled back — or <see langword="null"/> when the database was not opened from a durable store
    /// (an in-memory or mutable open). A host reads this to tell an exact recovery apart from a degraded or
    /// rolled-back one.
    /// </summary>
    public StoreRecoveryProvenance? RecoveryProvenance { get; }

    /// <summary>
    /// The provenance of a mutable database reopened over a durable dataset journal — how many acked commits were
    /// replayed and any torn-tail loss or commitment findings the journal recovery named — or <see langword="null"/>
    /// when no durable dataset journal was wired (an in-memory or immutable database, or a mutable database with no
    /// <see cref="VeritasEngineOptions.DatasetJournalPath"/>).
    /// </summary>
    public DatasetJournalRecoveryProvenance? DatasetJournalRecovery { get; }

    /// <summary>The reasoning provenance an immutable open recorded once at open, or <see langword="null"/> — the ctor-supplied value the immutable lane's <see cref="ReasoningProvenance"/> returns and the mutable lane's per-generation fallback.</summary>
    private ReasoningProvenance? OpenReasoningProvenance { get; }

    /// <summary>
    /// The provenance of the reasoning that serves the default graph's entailments — which strategy ran, what it
    /// decided, and exactly what its verdict does not cover — or <see langword="null"/> when no reasoning ran to a
    /// facade outcome: reasoning was unwired, or the open served an as-asserted generation from a durable store
    /// (whose persisted reasoning outcome is not recovered). On an IMMUTABLE open it is the single open-time
    /// outcome, fixed for the engine's life. On a reasoned MUTABLE engine it reads the CURRENT committed
    /// generation's payload — swapped atomically with the served store on each landed commit — so it tracks the
    /// last landed generation with no torn read: a refused or conflicted commit leaves it at the prior generation
    /// (the refusal exception carries the post-op provenance as a distinct object). A host reads this so a
    /// fragment-relative or inconsistent outcome is never mistaken for whole-truth.
    /// </summary>
    public ReasoningProvenance? ReasoningProvenance =>
        mutableDataset is not null
            ? mutableDataset.CurrentReasoningState() as ReasoningProvenance ?? OpenReasoningProvenance
            : OpenReasoningProvenance;

    /// <summary>The composed value-index registry this database's datasets carry (from <see cref="VeritasEngineOptions.ValueIndexes"/>) — read off the dataset's rendezvous, so it is the same instance every maintenance and probe path consults. <see cref="ValueIndexRegistry.Empty"/> unless the host registered methods.</summary>
    public ValueIndexRegistry ValueIndexes =>
        mutableDataset is not null
            ? mutableDataset.DefaultGraphRendezvous.ValueIndexes
            : queryEngine!.Dataset.DefaultGraphRendezvous.ValueIndexes;

    /// <summary>Constructs an immutable database over its composed engine and lane, with the federation and access seams threaded into per-query engines.</summary>
    /// <param name="queryEngine">The composed query engine.</param>
    /// <param name="computeLane">The owned compute lane.</param>
    /// <param name="serviceClient">The <c>SERVICE</c> client, or <see langword="null"/>.</param>
    /// <param name="graphSource">The <c>FROM</c> / <c>FROM NAMED</c> / <c>LOAD</c> resolver, or <see langword="null"/>.</param>
    /// <param name="accessControl">The access-control policy, or <see langword="null"/>.</param>
    /// <param name="analyticsTrace">The analytics-run trace handler, or <see langword="null"/> to emit none.</param>
    /// <param name="executionTrace">The per-operator execution-trace handler the per-call wrapper engines are constructed under, or <see langword="null"/> for no tracing; the fixed engine already carries it from its build.</param>
    /// <param name="ownedTermPool">The term pool backing a store-recovered dictionary, owned and disposed by the database, or <see langword="null"/> when the terms live elsewhere (the in-memory open paths).</param>
    /// <param name="ownedTriplePool">The triple pool backing a deferred store-opened database's recovered triples, owned and disposed by the database, or <see langword="null"/> when the triples were consumed into the trie at open (the eager paths).</param>
    /// <param name="recoveryProvenance">The store-recovery provenance when opened by serving from a durable store, or <see langword="null"/> for a non-store open.</param>
    /// <param name="reasoningProvenance">The provenance of the reasoning that materialised over the served default graph, or <see langword="null"/> when reasoning ran to no facade outcome (unwired, or a store-served as-asserted generation).</param>
    private VeritasEngine(SparqlQueryEngine queryEngine, IComputeLane computeLane, SparqlClient? serviceClient, GraphSourceResolver? graphSource, AccessControlDelegate? accessControl, TraceHandler<GraphAlgorithmTraceEvent>? analyticsTrace = null, TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null, Utf8StringPool? ownedTermPool = null, VeritasMemoryPool<EncodedTriple>? ownedTriplePool = null, StoreRecoveryProvenance? recoveryProvenance = null, ReasoningProvenance? reasoningProvenance = null)
    {
        this.queryEngine = queryEngine;
        this.computeLane = computeLane;
        this.serviceClient = WrapServiceClientWithAnalytics(serviceClient);
        this.graphSource = graphSource;
        this.accessControl = accessControl;
        this.analyticsTrace = analyticsTrace;
        this.executionTrace = executionTrace;
        this.ownedTermPool = ownedTermPool;
        this.ownedTriplePool = ownedTriplePool;
        sparqlPolicy = queryEngine.EnginePolicy;
        EngineImplicitTimezone = queryEngine.ImplicitTimezone;
        EngineValueDatatypes = queryEngine.ValueDatatypes;
        EngineExtensionFunctions = queryEngine.ExtensionFunctions;
        RecoveryProvenance = recoveryProvenance;
        OpenReasoningProvenance = reasoningProvenance;
    }

    /// <summary>Constructs a mutable database over a mutable dataset, its reconciliation feed, and the pool its replication work rents from; it derives a fresh query engine off the dataset snapshot per query, so reads see the latest committed state.</summary>
    /// <param name="mutableDataset">The mutable dataset the database commits into and queries off snapshots of.</param>
    /// <param name="replicationFeed">The reconciliation feed tracking the dataset's committed default graph; the maintainer advances it per commit.</param>
    /// <param name="sketchMaintainer">The maintained incremental sketch encoder subscribed to the dataset's delta observer seam (directly, or through the composed observer beside the ledger); owned and disposed by the database after commits have stopped.</param>
    /// <param name="commitLedger">The dotted commit ledger of a REMOVE-AWARE database (a host replica identity was supplied at open), or <see langword="null"/> for an add-only database.</param>
    /// <param name="replicaIdentitySupplied">Whether a host replica identity was supplied at open — <see langword="true"/> with a <see langword="null"/> <paramref name="commitLedger"/> means the store awaits the explicit baseline step.</param>
    /// <param name="replicationBaseline">The value-based outcome of the open's explicit causality baseline request.</param>
    /// <param name="replicationPool">The pool the database's replication work rents from; owned and disposed by the database.</param>
    /// <param name="serviceClient">The <c>SERVICE</c> client, or <see langword="null"/>.</param>
    /// <param name="graphSource">The <c>FROM</c> / <c>FROM NAMED</c> / <c>LOAD</c> resolver, or <see langword="null"/>.</param>
    /// <param name="accessControl">The access-control policy, or <see langword="null"/>.</param>
    /// <param name="analyticsTrace">The analytics-run trace handler, or <see langword="null"/> to emit none.</param>
    /// <param name="executionTrace">The per-operator execution-trace handler the per-query engines are constructed under, or <see langword="null"/> for no tracing (from <see cref="VeritasEngineOptions.SparqlExecutionTrace"/>).</param>
    /// <param name="datasetJournal">The durable dataset journal the database owns and disposes, or <see langword="null"/> for an in-memory mutable database.</param>
    /// <param name="ownedTermPool">The term pool backing the durable journal's restored dictionary, owned and disposed by the database, or <see langword="null"/> when the terms live in the caller's data.</param>
    /// <param name="datasetJournalRecovery">The provenance of a durable-journal reopen, or <see langword="null"/> for a fresh mutable open or one with no durable journal.</param>
    /// <param name="persistChecksum">The checksum algorithm <see cref="Persist"/> writes durable id-bearing artifacts under (from <see cref="VeritasEngineOptions.Checksum"/>), or <see langword="null"/> for the built-in default.</param>
    /// <param name="persistResolveChecksum">The resolver <see cref="Persist"/> reads the store's prior committed generation through (from <see cref="VeritasEngineOptions.ResolveChecksum"/>), or <see langword="null"/> for the built-in resolver.</param>
    /// <param name="sparqlPolicy">The SPARQL-executor strategy policy the per-query engines are constructed under (from <see cref="VeritasEngineOptions.SparqlExecution"/>).</param>
    /// <param name="implicitTimezone">The implicit timezone every expression context this database creates normalizes naive temporal operands with (from <see cref="VeritasEngineOptions.ImplicitTimezone"/>).</param>
    /// <param name="sparqlUpdateOptions">The SPARQL Update semantic options every update executes under (from <see cref="VeritasEngineOptions.SparqlUpdate"/>).</param>
    /// <param name="valueDatatypes">The value-layer datatype registry every expression context this database creates consults (from <see cref="VeritasEngineOptions.ValueDatatypes"/>); <see langword="null"/> uses <see cref="ValueDatatypeRegistry.Empty"/>.</param>
    /// <param name="extensionFunctions">The extension-function registry every expression context this database creates consults (from <see cref="VeritasEngineOptions.ExtensionFunctions"/>); <see langword="null"/> uses <see cref="SparqlFunctionRegistry.Empty"/>.</param>
    /// <param name="metadataCoordination">The standing the open's metadata-plane consultations established (from <see cref="VeritasEngineOptions.MetadataCoordination"/>); <see cref="MetadataCoordinationStanding.NotConfigured"/> when no seams were set.</param>
    private VeritasEngine(MutableSparqlDataset mutableDataset, ReplicationIndexFeed replicationFeed, IncrementalSketchMaintainer sketchMaintainer, DottedCommitLedger? commitLedger, bool replicaIdentitySupplied, ReplicationBaselineOutcome replicationBaseline, VeritasMemoryPool<byte> replicationPool, SparqlClient? serviceClient, GraphSourceResolver? graphSource, AccessControlDelegate? accessControl, TraceHandler<GraphAlgorithmTraceEvent>? analyticsTrace = null, TraceHandler<SparqlExecutionTraceEvent>? executionTrace = null, FileBackedDatasetJournal? datasetJournal = null, Utf8StringPool? ownedTermPool = null, DatasetJournalRecoveryProvenance? datasetJournalRecovery = null, ChecksumAlgorithm? persistChecksum = null, ResolveChecksumAlgorithmDelegate? persistResolveChecksum = null, SparqlEnginePolicy sparqlPolicy = default, TimeSpan implicitTimezone = default, SparqlUpdateOptions sparqlUpdateOptions = default, ValueDatatypeRegistry? valueDatatypes = null, SparqlFunctionRegistry? extensionFunctions = null, MetadataCoordinationStanding metadataCoordination = MetadataCoordinationStanding.NotConfigured)
    {
        this.mutableDataset = mutableDataset;
        worlds = new DatasetWorlds(WellKnownWorlds.Primary, mutableDataset);
        this.replicationFeed = replicationFeed;
        this.sketchMaintainer = sketchMaintainer;
        this.commitLedger = commitLedger;
        this.replicaIdentitySupplied = replicaIdentitySupplied;
        ReplicationBaseline = replicationBaseline;
        MetadataCoordination = metadataCoordination;
        this.replicationPool = replicationPool;
        this.serviceClient = WrapServiceClientWithAnalytics(serviceClient);
        this.graphSource = graphSource;
        this.accessControl = accessControl;
        this.analyticsTrace = analyticsTrace;
        this.executionTrace = executionTrace;
        this.datasetJournal = datasetJournal;
        this.ownedTermPool = ownedTermPool;
        this.sparqlPolicy = sparqlPolicy;
        this.sparqlUpdateOptions = sparqlUpdateOptions;
        EngineImplicitTimezone = implicitTimezone;
        EngineValueDatatypes = valueDatatypes ?? ValueDatatypeRegistry.Empty;
        EngineExtensionFunctions = extensionFunctions ?? SparqlFunctionRegistry.Empty;
        DatasetJournalRecovery = datasetJournalRecovery;
        PersistChecksum = persistChecksum;
        PersistResolveChecksum = persistResolveChecksum;
    }

    /// <summary>
    /// Opens a database over a default graph and any named graphs, composing
    /// the configured machinery. When reasoning is wired, its entailments
    /// materialise into the default graph as part of opening.
    /// </summary>
    /// <param name="defaultGraph">The default-graph triples.</param>
    /// <param name="namedGraphs">The named graphs, each its graph-name term paired with its triples.</param>
    /// <param name="options">The configuration; <c>null</c> uses the fully-wired <see cref="VeritasEngineOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> or <paramref name="namedGraphs"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested — an immutable open cannot commit a baseline entry.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenAsync(
        IEnumerable<DataTriple> defaultGraph,
        IReadOnlyList<(RdfTerm Name, IEnumerable<DataTriple> Triples)> namedGraphs,
        VeritasEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);
        ArgumentNullException.ThrowIfNull(namedGraphs);

        options ??= VeritasEngineOptions.Default;
        EnsureNoBaselineOnImmutableOpen(options);
        IComputeLane lane = ComputeLane.ForCurrentPlatform(options.Execution);
        try
        {
            ReasoningMaterialization? materialization = options.Reasoning is { } configuration
                ? CreateReasoning(configuration, options)
                : null;
            ReasoningMaterializationDelegate? reasoning = materialization is null ? null : materialization.MaterializeAsync;

            //Encode into one shared dictionary in the exact order the encoded-input core consumes — the default
            //graph first, then each named graph's name before its triples — then build through the shared core the
            //quad-stream overload also feeds, so the list and stream ingest paths cannot diverge below the encode.
            TermDictionary dictionary = new();
            List<EncodedTriple> defaultEncoded = EncodeGraph(defaultGraph, dictionary);
            List<(TermId Name, List<EncodedTriple> Triples)> named = new(namedGraphs.Count);
            foreach((RdfTerm name, IEnumerable<DataTriple> triples) in namedGraphs)
            {
                TermId graphName = dictionary.GetOrAdd(name);
                named.Add((graphName, EncodeGraph(triples, dictionary)));
            }

            SparqlQueryEngine engine = await BuildImmutableEngineAsync(dictionary, defaultEncoded, named, lane, options.SparqlExecution, options.SparqlExecutionTrace, reasoning, options.ValueIndexes, options.ImplicitTimezone, options.ValueDatatypes, options.ExtensionFunctions, cancellationToken).ConfigureAwait(false);

            return new VeritasEngine(engine, lane, BuildServiceClient(options), options.GraphSource, options.AccessControl, options.AnalyticsTrace, options.SparqlExecutionTrace, reasoningProvenance: materialization?.Provenance);
        }
        catch
        {
            //Do not leak the lane when opening fails after it started.
            await lane.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>Opens a database over a single default graph and no named graphs.</summary>
    /// <param name="defaultGraph">The default-graph triples.</param>
    /// <param name="options">The configuration; <c>null</c> uses the fully-wired <see cref="VeritasEngineOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested — an immutable open cannot commit a baseline entry.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static ValueTask<VeritasEngine> OpenAsync(
        IEnumerable<DataTriple> defaultGraph,
        VeritasEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return OpenAsync(defaultGraph, [], options, cancellationToken);
    }

    /// <summary>
    /// Opens an immutable database directly from a stream of quads — the streaming-ingest boundary. The stream is
    /// enumerated EXACTLY ONCE: each quad's terms are encoded into the shared dictionary as it arrives and bucketed
    /// into the default graph (a quad with a <see langword="null"/> <see cref="Quad.Graph"/>) or its named graph (the
    /// graph term encoded into the same dictionary), so no <see cref="Quad"/> list and no
    /// <see cref="DataTriple"/> list is ever materialised. Peak ingest memory is therefore the ENCODED dataset (the
    /// per-graph <see cref="EncodedTriple"/> lists) plus the dictionary, not the parse objects. The database is then
    /// built through the same shared-arena encoded-input core the list overload uses, so this open is behaviourally
    /// identical to <see cref="OpenAsync(IEnumerable{DataTriple}, IReadOnlyList{ValueTuple{RdfTerm, IEnumerable{DataTriple}}}, VeritasEngineOptions?, CancellationToken)"/>
    /// over the same data — same reasoning materialisation, same query answers, same dictionary term count. It is the
    /// open a streaming parser (a Turtle/N-Quads reader over a pipe) feeds without draining into intermediate lists.
    /// </summary>
    /// <param name="quads">The quads to ingest, enumerated exactly once; a quad with a <see langword="null"/> graph is a default-graph triple, otherwise a triple of that named graph.</param>
    /// <param name="options">The configuration; <c>null</c> uses the fully-wired <see cref="VeritasEngineOptions.Default"/>. When reasoning is wired (the default) its entailments materialise into the default graph as part of opening.</param>
    /// <param name="cancellationToken">A token that aborts opening (observed at each quad).</param>
    /// <returns>The opened database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested — an immutable open cannot commit a baseline entry.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenAsync(
        IAsyncEnumerable<Quad> quads,
        VeritasEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);

        options ??= VeritasEngineOptions.Default;
        EnsureNoBaselineOnImmutableOpen(options);
        IComputeLane lane = ComputeLane.ForCurrentPlatform(options.Execution);
        try
        {
            ReasoningMaterialization? materialization = options.Reasoning is { } configuration
                ? CreateReasoning(configuration, options)
                : null;
            ReasoningMaterializationDelegate? reasoning = materialization is null ? null : materialization.MaterializeAsync;

            TermDictionary dictionary = new();
            List<EncodedTriple> defaultGraph = [];
            Dictionary<TermId, List<EncodedTriple>> namedBuckets = [];
            List<(TermId Name, List<EncodedTriple> Triples)> named = [];
            await foreach(Quad quad in quads.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                EncodedTriple triple = new(dictionary.GetOrAdd(quad.Subject), dictionary.GetOrAdd(quad.Predicate), dictionary.GetOrAdd(quad.Object));
                if(quad.Graph is null)
                {
                    defaultGraph.Add(triple);
                }
                else
                {
                    TermId graphName = dictionary.GetOrAdd(quad.Graph);
                    if(!namedBuckets.TryGetValue(graphName, out List<EncodedTriple>? bucket))
                    {
                        bucket = [];
                        namedBuckets[graphName] = bucket;
                        named.Add((graphName, bucket));
                    }

                    bucket.Add(triple);
                }
            }

            SparqlQueryEngine engine = await BuildImmutableEngineAsync(dictionary, defaultGraph, named, lane, options.SparqlExecution, options.SparqlExecutionTrace, reasoning, options.ValueIndexes, options.ImplicitTimezone, options.ValueDatatypes, options.ExtensionFunctions, cancellationToken).ConfigureAwait(false);

            return new VeritasEngine(engine, lane, BuildServiceClient(options), options.GraphSource, options.AccessControl, options.AnalyticsTrace, options.SparqlExecutionTrace, reasoningProvenance: materialization?.Provenance);
        }
        catch
        {
            //Do not leak the lane when opening fails after it started.
            await lane.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Encodes a graph's triples into <paramref name="dictionary"/>, minting each term in subject-predicate-object
    /// order so the id assignment matches the streaming ingest's per-quad order for equal data.
    /// </summary>
    /// <param name="triples">The graph's triples.</param>
    /// <param name="dictionary">The shared term dictionary every graph of the dataset encodes into.</param>
    /// <returns>The encoded triples.</returns>
    private static List<EncodedTriple> EncodeGraph(IEnumerable<DataTriple> triples, TermDictionary dictionary)
    {
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in triples)
        {
            encoded.Add(new EncodedTriple(dictionary.GetOrAdd(triple.Subject), dictionary.GetOrAdd(triple.Predicate), dictionary.GetOrAdd(triple.Object)));
        }

        return encoded;
    }

    /// <summary>
    /// Builds the immutable query engine from an already-encoded dataset — the single build core the list and
    /// quad-stream immutable opens both route through, so they cannot diverge below the encode. With no named graphs
    /// the default graph builds a lone store (the single-graph fast path); with named graphs every graph builds
    /// through ONE shared node arena, keyed by graph-name term id, exactly as the dataset build does. Reasoning, when
    /// supplied, materialises over the built default graph before the engine serves it.
    /// </summary>
    /// <param name="dictionary">The shared dictionary every graph was encoded against.</param>
    /// <param name="defaultGraph">The encoded default-graph triples.</param>
    /// <param name="namedGraphs">The encoded named graphs, each its graph-name term id paired with its triples, in first-seen order.</param>
    /// <param name="lane">The owned compute lane threaded into the engine's rendezvous.</param>
    /// <param name="enginePolicy">The SPARQL-executor strategy policy the engine is built under (from <see cref="VeritasEngineOptions.SparqlExecution"/>).</param>
    /// <param name="executionTrace">The per-operator execution-trace handler the engine is built under, or <see langword="null"/> for no tracing (from <see cref="VeritasEngineOptions.SparqlExecutionTrace"/>).</param>
    /// <param name="reasoning">The reasoning materialisation seam run over the built default graph, or <see langword="null"/> to serve the asserted graph.</param>
    /// <param name="valueIndexes">The composed value-index registry (from <see cref="VeritasEngineOptions.ValueIndexes"/>).</param>
    /// <param name="implicitTimezone">The implicit timezone the engine's expression context normalizes naive temporal operands with (from <see cref="VeritasEngineOptions.ImplicitTimezone"/>).</param>
    /// <param name="valueDatatypes">The value-layer datatype registry the engine's expression context consults (from <see cref="VeritasEngineOptions.ValueDatatypes"/>).</param>
    /// <param name="extensionFunctions">The extension-function registry the engine's expression context consults (from <see cref="VeritasEngineOptions.ExtensionFunctions"/>).</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The query engine over the dataset.</returns>
    private static async ValueTask<SparqlQueryEngine> BuildImmutableEngineAsync(
        TermDictionary dictionary,
        List<EncodedTriple> defaultGraph,
        List<(TermId Name, List<EncodedTriple> Triples)> namedGraphs,
        IComputeLane lane,
        SparqlEnginePolicy enginePolicy,
        TraceHandler<SparqlExecutionTraceEvent>? executionTrace,
        ReasoningMaterializationDelegate? reasoning,
        ValueIndexRegistry valueIndexes,
        TimeSpan implicitTimezone,
        ValueDatatypeRegistry valueDatatypes,
        SparqlFunctionRegistry extensionFunctions,
        CancellationToken cancellationToken)
    {
        if(namedGraphs.Count == 0)
        {
            HypertrieGraphStore store = await HypertrieGraphStore.BuildAsync(defaultGraph, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
            if(reasoning is not null)
            {
                store = await reasoning(store, dictionary, cancellationToken).ConfigureAwait(false);
            }

            return new SparqlQueryEngine(store, dictionary, expressionContext: SparqlExpressionContext.CreateDefault(implicitTimezone: implicitTimezone, valueDatatypes: valueDatatypes, extensionFunctions: extensionFunctions), executionTrace: executionTrace, computeLane: lane, enginePolicy: enginePolicy, valueIndexes: valueIndexes);
        }

        List<IEnumerable<EncodedTriple>> encodedGraphs = new(namedGraphs.Count + 1) { defaultGraph };
        foreach((TermId _, List<EncodedTriple> triples) in namedGraphs)
        {
            encodedGraphs.Add(triples);
        }

        IReadOnlyList<HypertrieGraphStore> stores = await HypertrieGraphStore.BuildSharedAsync(encodedGraphs, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        HypertrieGraphStore defaultStore = stores[0];
        if(reasoning is not null)
        {
            defaultStore = await reasoning(defaultStore, dictionary, cancellationToken).ConfigureAwait(false);
        }

        Dictionary<TermId, HypertrieGraphStore> named = new(namedGraphs.Count);
        for(int i = 0; i < namedGraphs.Count; i++)
        {
            named[namedGraphs[i].Name] = stores[i + 1];
        }

        return new SparqlQueryEngine(new SparqlDataset(defaultStore, named, lane, initialColumnarView: null, valueIndexes), dictionary, expressionContext: SparqlExpressionContext.CreateDefault(implicitTimezone: implicitTimezone, valueDatatypes: valueDatatypes, extensionFunctions: extensionFunctions), executionTrace: executionTrace, enginePolicy: enginePolicy);
    }

    /// <summary>
    /// Opens a query-only database by serving from a durable <see cref="PersistenceStore"/>: it recovers the
    /// live committed generation (the term dictionary and the system-of-record triples) through
    /// <see cref="DurableSystemOfRecordStore"/>, rebuilds the query store from the recovered triples, and serves
    /// — warm-started, with no re-ingestion of source data. The database owns the term pool the recovered terms
    /// are interned into and disposes it on <see cref="DisposeAsync"/>. The persisted system-of-record is served
    /// as asserted; reasoning is not re-materialised here. The recovery fidelity — whether the served generation
    /// was recovered exactly, degraded, or rolled back to an older generation after an artifact failure — is
    /// surfaced on <see cref="RecoveryProvenance"/> so a host is never left treating a degraded open as committed truth.
    /// </summary>
    /// <param name="store">The durable store a generation was committed into by <see cref="DurableSystemOfRecordStore.Persist"/>.</param>
    /// <param name="options">The configuration; <c>null</c> uses <see cref="VeritasEngineOptions.Default"/>. Only the federation and access seams apply on this query-only path.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened database serving the recovered generation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested — an immutable open cannot commit a baseline entry.</exception>
    /// <exception cref="InvalidDataException">The store holds no recoverable committed generation, or a recovered artifact failed its at-rest verification.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenAsync(PersistenceStore store, VeritasEngineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        options ??= VeritasEngineOptions.Default;
        EnsureSelfHealConfigurationValid(options);
        EnsureNoBaselineOnImmutableOpen(options);

        bool deferred = options.HypertrieResidency == HypertrieResidency.Deferred;

        //The term pool backs the recovered dictionary for the database's lifetime, so the database owns it. The
        //buffer pool is transient. The triple pool is transient under eager residency (the recovered triples are
        //consumed into the tries at build) but, under deferred residency, it backs the recovered DEFAULT-graph
        //triples the deferred source materialises the trie from later, so the database owns it then. The finally
        //disposes whatever ownership was not transferred (nulled): the default-triples segment unless the deferred
        //source took it, and every named-graph segment (always consumed into its store at build).
        Utf8StringPool? termPool = new();
        VeritasMemoryPool<EncodedTriple>? triplePool = new();
        DecodedItemSegment? defaultTriples = null;
        IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> namedGraphSegments = [];
        try
        {
            using VeritasMemoryPool<byte> bufferPool = new();

            DurableSystemOfRecordLoad load = new DurableSystemOfRecordStore(store, bufferPool, options.Checksum, options.ResolveChecksum).TryLoad(termPool, triplePool);
            if(load.Outcome != DurableSystemOfRecordLoadOutcome.Loaded)
            {
                throw new InvalidDataException($"The persistence store holds no servable committed generation ({load.Outcome}).");
            }

            defaultTriples = load.Triples!;
            namedGraphSegments = load.NamedGraphs;
            TermDictionary dictionary = load.Dictionary!;

            IComputeLane lane = ComputeLane.ForCurrentPlatform(options.Execution);
            try
            {
                //When the generation carried a persisted columnar sidecar it is loaded warm (no re-sort, no re-pack)
                //and seeds the default-graph rendezvous, so the first multi-pattern query serves from the Elias-Fano
                //index immediately rather than rebuilding it; absent one, the rendezvous builds on demand. The named
                //graphs always build eagerly; the default graph honours the residency knob.
                SparqlQueryEngine engine = await BuildRecoveredEngineAsync(
                    defaultTriples, namedGraphSegments, dictionary, load.Sidecar, deferred, lane, options.SparqlExecution, options.SparqlExecutionTrace, options.ValueIndexes, options.ImplicitTimezone, options.ValueDatatypes, options.ExtensionFunctions, cancellationToken).ConfigureAwait(false);

                //A verified value-index sidecar warm-installs into the registered methods all-or-nothing (each
                //method validates its own configuration stamps, e.g. the temporal implicit-timezone stamp); a
                //declined install leaves the cold rebuild-at-first-probe path, which is always correct.
                if(load.ValueIndexes is { } valueIndexImage)
                {
                    engine.Dataset.DefaultGraphRendezvous.TryInstallValueIndexSnapshots(valueIndexImage);
                }

                //The recovery provenance is carried onto the database so a host can see whether the served
                //generation was recovered exactly, degraded (no surviving CURRENT pointer), or rolled back to an
                //older generation because the live one's artifacts failed verification.
                StoreRecoveryProvenance provenance = new(load.Generation, load.IsDegraded, load.CommitEvidenced, load.IsRollback);

                //Ownership now passes to the database: the term pool always; and, under deferred residency, the
                //triple pool (it backs the deferred default triples) and the default-triples segment (the deferred
                //source holds it). Null those locals so the finally does not dispose what the database owns.
                VeritasEngine database = deferred
                    ? new VeritasEngine(engine, lane, BuildServiceClient(options), options.GraphSource, options.AccessControl, options.AnalyticsTrace, options.SparqlExecutionTrace, termPool, triplePool, provenance)
                    : new VeritasEngine(engine, lane, BuildServiceClient(options), options.GraphSource, options.AccessControl, options.AnalyticsTrace, options.SparqlExecutionTrace, termPool, recoveryProvenance: provenance);

                termPool = null;
                if(deferred)
                {
                    defaultTriples = null;
                    triplePool = null;
                }

                StartSelfHealIfConfigured(database, store, options);

                return database;
            }
            catch
            {
                await lane.DisposeAsync().ConfigureAwait(false);

                throw;
            }
        }
        finally
        {
            defaultTriples?.Dispose();
            foreach((TermId _, DecodedItemSegment segment) in namedGraphSegments)
            {
                segment.Dispose();
            }

            triplePool?.Dispose();
            termPool?.Dispose();
        }
    }

    /// <summary>
    /// Builds the query engine for a recovered generation: the named graphs always build eagerly (their columnar
    /// views rebuild on demand), and the default graph honours the residency choice — deferred (the trie
    /// materialises on first demand from <paramref name="defaultTriples"/>, which the deferred source takes over) or
    /// eager (built now, sharing one node arena with the named graphs). The default-graph rendezvous is seeded with
    /// the warm <paramref name="sidecar"/>.
    /// </summary>
    /// <param name="defaultTriples">The recovered default-graph triples; under deferred residency the deferred source takes ownership.</param>
    /// <param name="namedGraphSegments">The recovered named graphs, each its graph-name term id and triples.</param>
    /// <param name="dictionary">The recovered shared dictionary every graph is encoded against.</param>
    /// <param name="sidecar">The warm default-graph columnar sidecar, or <see langword="null"/>.</param>
    /// <param name="deferred">Whether the default graph defers its trie.</param>
    /// <param name="lane">The owned compute lane threaded into the default-graph rendezvous.</param>
    /// <param name="enginePolicy">The SPARQL-executor strategy policy the engine is built under (from <see cref="VeritasEngineOptions.SparqlExecution"/>).</param>
    /// <param name="executionTrace">The per-operator execution-trace handler the engine is built under, or <see langword="null"/> for no tracing (from <see cref="VeritasEngineOptions.SparqlExecutionTrace"/>).</param>
    /// <param name="valueIndexes">The composed value-index registry (from <see cref="VeritasEngineOptions.ValueIndexes"/>).</param>
    /// <param name="implicitTimezone">The implicit timezone the engine's expression context normalizes naive temporal operands with (from <see cref="VeritasEngineOptions.ImplicitTimezone"/>).</param>
    /// <param name="valueDatatypes">The value-layer datatype registry the engine's expression context consults (from <see cref="VeritasEngineOptions.ValueDatatypes"/>).</param>
    /// <param name="extensionFunctions">The extension-function registry the engine's expression context consults (from <see cref="VeritasEngineOptions.ExtensionFunctions"/>).</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The query engine over the recovered dataset.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The deferred source's ownership transfers to the engine's rendezvous (disposed after the on-demand build); on a build failure the default-triples segment it wraps is disposed by OpenAsync's finally.")]
    private static async ValueTask<SparqlQueryEngine> BuildRecoveredEngineAsync(
        DecodedItemSegment defaultTriples,
        IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> namedGraphSegments,
        TermDictionary dictionary,
        ColumnarTripleIndex? sidecar,
        bool deferred,
        IComputeLane lane,
        SparqlEnginePolicy enginePolicy,
        TraceHandler<SparqlExecutionTraceEvent>? executionTrace,
        ValueIndexRegistry valueIndexes,
        TimeSpan implicitTimezone,
        ValueDatatypeRegistry valueDatatypes,
        SparqlFunctionRegistry extensionFunctions,
        CancellationToken cancellationToken)
    {
        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault(implicitTimezone: implicitTimezone, valueDatatypes: valueDatatypes, extensionFunctions: extensionFunctions);
        if(namedGraphSegments.Count == 0)
        {
            //Single default graph — byte-identical to the original serve-from-disk open.
            if(deferred)
            {
                DeferredTrieSource deferredStore = new(defaultTriples, VeritasHashing.Default);

                return new SparqlQueryEngine(deferredStore, dictionary, expressionContext: context, executionTrace: executionTrace, computeLane: lane, initialColumnarView: sidecar, enginePolicy: enginePolicy, valueIndexes: valueIndexes);
            }

            HypertrieGraphStore graphStore = await HypertrieGraphStore.BuildAsync(MemoryMarshal.ToEnumerable<EncodedTriple>(defaultTriples.Memory), VeritasHashing.Default, cancellationToken).ConfigureAwait(false);

            return new SparqlQueryEngine(graphStore, dictionary, expressionContext: context, executionTrace: executionTrace, computeLane: lane, initialColumnarView: sidecar, enginePolicy: enginePolicy, valueIndexes: valueIndexes);
        }

        if(deferred)
        {
            //Deferred default + eager named graphs (the named graphs share one node arena among themselves).
            (_, Dictionary<TermId, HypertrieGraphStore> named) = await BuildRecoveredGraphStoresAsync(leadingDefault: null, namedGraphSegments, cancellationToken).ConfigureAwait(false);
            DeferredTrieSource deferredStore = new(defaultTriples, VeritasHashing.Default);
            QueryEnginePolicy deferredPolicy = QueryEnginePolicy.Default with { HypertrieResidency = HypertrieResidency.Deferred };
            QueryEngineRendezvous rendezvous = new(store: null, deferredPolicy, lane, sidecar, deferredStore, valueIndexes);
            SparqlDataset dataset = new(defaultGraph: null, named, rendezvous);

            return new SparqlQueryEngine(dataset, dictionary, expressionContext: context, executionTrace: executionTrace, enginePolicy: enginePolicy);
        }

        //Eager default + named graphs through one shared node arena (the default leads, so it shares the arena).
        (HypertrieGraphStore? defaultStore, Dictionary<TermId, HypertrieGraphStore> eagerNamed) = await BuildRecoveredGraphStoresAsync(defaultTriples, namedGraphSegments, cancellationToken).ConfigureAwait(false);
        SparqlDataset eagerDataset = new(defaultStore!, eagerNamed, lane, sidecar, valueIndexes);

        return new SparqlQueryEngine(eagerDataset, dictionary, expressionContext: context, executionTrace: executionTrace, enginePolicy: enginePolicy);
    }

    /// <summary>
    /// Builds the recovered graphs' stores through ONE shared node arena and keys the named graphs by graph-name
    /// term id. When <paramref name="leadingDefault"/> is supplied it builds at the front of the arena and is
    /// returned as the default store (the eager default graph, sharing the arena with the named graphs); otherwise
    /// only the named graphs build (the deferred-default case) and the default store is <see langword="null"/>.
    /// </summary>
    /// <param name="leadingDefault">The default-graph triples to build at the front of the shared arena, or <see langword="null"/> to build only the named graphs.</param>
    /// <param name="namedGraphSegments">The recovered named graphs, each its graph-name term id and triples.</param>
    /// <param name="cancellationToken">A token that aborts the builds.</param>
    /// <returns>The default-graph store (or <see langword="null"/> when no leading default was supplied) and the named-graph stores keyed by graph-name term id.</returns>
    private static async ValueTask<(HypertrieGraphStore? Default, Dictionary<TermId, HypertrieGraphStore> Named)> BuildRecoveredGraphStoresAsync(
        DecodedItemSegment? leadingDefault,
        IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> namedGraphSegments,
        CancellationToken cancellationToken)
    {
        bool hasDefault = leadingDefault is not null;
        List<IEnumerable<EncodedTriple>> graphs = new(namedGraphSegments.Count + (hasDefault ? 1 : 0));
        if(leadingDefault is not null)
        {
            graphs.Add(MemoryMarshal.ToEnumerable<EncodedTriple>(leadingDefault.Memory));
        }

        foreach((TermId _, DecodedItemSegment segment) in namedGraphSegments)
        {
            graphs.Add(MemoryMarshal.ToEnumerable<EncodedTriple>(segment.Memory));
        }

        IReadOnlyList<HypertrieGraphStore> stores = await HypertrieGraphStore.BuildSharedAsync(graphs, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
        int namedOffset = hasDefault ? 1 : 0;
        Dictionary<TermId, HypertrieGraphStore> named = new(namedGraphSegments.Count);
        for(int i = 0; i < namedGraphSegments.Count; i++)
        {
            named[namedGraphSegments[i].GraphName] = stores[i + namedOffset];
        }

        return (hasDefault ? stores[0] : null, named);
    }

    /// <summary>
    /// Persists this mutable database's current committed state — the shared term dictionary, the default-graph
    /// system-of-record triples, and every named graph's triples — durably into a persistence store as one manifest
    /// generation, so the database can be reopened over the store with
    /// <see cref="OpenAsync(PersistenceStore, VeritasEngineOptions?, CancellationToken)"/>. Only a mutable database
    /// persists its state; an immutable one is built from source the caller already holds. The default graph
    /// persists its warm-start columnar sidecar; named graphs persist as system-of-record segments whose columnar
    /// views rebuild on demand at reopen.
    /// </summary>
    /// <remarks>
    /// The default graph and every named graph are captured from ONE committed dataset state
    /// (<see cref="MutableSparqlDataset.CaptureCommittedState"/>), so the persisted generation is always a state
    /// that was committed as a whole - a commit racing the persist lands wholly before or wholly after the capture
    /// and can never split the default graph from the named graphs. The captured state's identifier is stamped into
    /// the manifest's provenance epoch as the durable binding a recovery cross-checks against a journal head.
    /// </remarks>
    /// <param name="store">The durable store the generation is committed into.</param>
    /// <returns>The receipt: the committed generation, the dictionary epoch, the term count, and the total triple count across all graphs.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    public DurableSystemOfRecordCommit Persist(PersistenceStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if(mutableDataset is null || replicationFeed is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) persists its state; an immutable database is built from source the caller already holds.");
        }

        //One committed dataset state is captured atomically: the default graph and every named graph are read from
        //the same immutable, content-addressed state object. This is the atomicity fence - the persisted generation
        //can never mix a default graph from one committed state with named graphs from another, a cross-graph state
        //that was never committed.
        DatasetPersistCapture capture = mutableDataset.CaptureCommittedState();

        //A remove-aware database pairs the dataset capture with the ledger snapshot BY StateId: each read is
        //individually atomic but separately gated, so a commit can land between them. Re-capturing until the
        //stamps match converges as soon as no commit intervenes, and every derived artifact below is built from
        //the paired capture. The ledger advances inside the same publish that moves the dataset, and always
        //after it, so the ledger read is never older than the capture read taken before it.
        DottedLedgerSnapshot? causalitySnapshot = null;
        if(commitLedger is { } ledger)
        {
            causalitySnapshot = ledger.Snapshot();
            while(causalitySnapshot.StateId != capture.StateId)
            {
                capture = mutableDataset.CaptureCommittedState();
                causalitySnapshot = ledger.Snapshot();
            }
        }

        using VeritasMemoryPool<byte> bufferPool = new();

        //The warm-start columnar sidecar is built from the captured state's own default triples, so the Elias-Fano
        //index a reopen serves from is the exact index over the persisted default-graph system of record - sidecar
        //and record are one captured state, never two. Named graphs persist as system-of-record segments without
        //sidecars (their columnar views rebuild on demand at reopen). The captured state identifier becomes the
        //manifest's provenance epoch.
        ColumnarTripleIndex sidecar = ColumnarTripleIndex.Build(MemoryMarshal.ToEnumerable<EncodedTriple>(capture.DefaultGraph));

        //The value-index sidecar follows the same one-captured-state discipline: every registered access method's
        //snapshot is built from the capture's own default triples and stamped with the capture's state identifier,
        //never serialized from live index state.
        ValueIndexImage? valueIndexes = BuildValueIndexImage(capture);

        //The generation's integrity sketch follows the same one-captured-state discipline: projected from the
        //capture's own default triples through the real rateless codec, so the at-rest record a peer heal is
        //verified against describes exactly the persisted system of record.
        using SlabBufferWriter sketchWriter = new(bufferPool);
        WriteGenerationSketch(capture.DefaultGraph, bufferPool, sketchWriter);
        int sketchLength = sketchWriter.BytesWritten;
        using IMemoryOwner<byte> sketchOwner = sketchWriter.Detach();

        //The causality artifact serializes the paired ledger snapshot — same captured instant as the system of
        //record, stamped with the same StateId — so a reopen recovers observed-remove knowledge that describes
        //exactly the persisted committed set. An add-only database serializes none.
        int causalitySize = causalitySnapshot?.ComputeSerializedSize() ?? 0;
        using IMemoryOwner<byte> causalityOwner = bufferPool.Rent(Math.Max(1, causalitySize));
        if(causalitySnapshot is not null)
        {
            causalitySize = causalitySnapshot.WriteTo(causalityOwner.Memory.Span[..causalitySize]);
        }

        DurableSystemOfRecordStore durable = new(store, bufferPool, PersistChecksum, PersistResolveChecksum);

        DurableSystemOfRecordCommit CommitCapture()
        {
            return durable.Persist(
                mutableDataset.Dictionary,
                capture.DefaultGraph,
                capture.NamedGraphs,
                sidecar,
                valueIndexes,
                provenanceEpoch: unchecked((long)capture.StateId.Value),
                integritySketch: sketchOwner.Memory[..sketchLength],
                causalitySnapshot: causalityOwner.Memory[..causalitySize]);
        }

        //A background self-heal loop's heal publish and this foreground persist must not interleave their staging
        //and rename windows in-process; when a self-heal loop is active over THIS store instance, the persist runs
        //under the same commit mutex the loop's publish takes, so the two atomic publishes are serialized.
        if(ActiveSelfHeal is { } runtime && ReferenceEquals(runtime.Store, store))
        {
            lock(runtime.CommitMutex)
            {
                return CommitCapture();
            }
        }

        return CommitCapture();
    }

    /// <summary>
    /// Builds the value-index sidecar image for a persist: every registered access method's snapshot, built from
    /// the SAME captured default graph the columnar sidecar is built from (consistency by construction) and
    /// stamped with the capture's dataset state identifier — the staleness gate recovery validates against the
    /// manifest's provenance epoch. Returns <see langword="null"/> — no value-index sidecar is persisted — when no
    /// methods are registered or when any registration declines snapshots: recovery installs all-or-nothing, so a
    /// partial image could never warm-install.
    /// </summary>
    /// <param name="capture">The committed dataset state being persisted.</param>
    /// <returns>The image, or <see langword="null"/>.</returns>
    private ValueIndexImage? BuildValueIndexImage(DatasetPersistCapture capture)
    {
        ValueIndexRegistry registry = mutableDataset!.DefaultGraphRendezvous.ValueIndexes;
        if(registry.IsEmpty)
        {
            return null;
        }

        CapturedGraphValueSegmentSource source = new(capture.DefaultGraph, mutableDataset.Dictionary);
        List<ValueIndexImageEntry> entries = new(registry.Registrations.Count);
        for(int i = 0; i < registry.Registrations.Count; i++)
        {
            ValueIndexRegistration registration = registry.Registrations[i];
            ValueIndexSnapshot? snapshot = registration.Method.BuildSnapshot(source);
            if(snapshot is null)
            {
                return null;
            }

            byte[] payload = new byte[snapshot.PayloadSize];
            snapshot.WriteTo(payload);
            entries.Add(new ValueIndexImageEntry(
                registration.Method.DatatypeIri,
                registration.Axis.StartPredicateIri,
                registration.Axis.EndPredicateIri,
                payload));
        }

        return new ValueIndexImage(capture.StateId.Value, entries);
    }

    /// <summary>
    /// Serializes the generation's integrity sketch over the captured default-graph triples: each triple is
    /// projected to its structural reconciliation item and the item set is persisted through the real rateless
    /// codec at <see cref="GenerationSketchSymbolBudget"/> symbols under the persist checksum. This is the
    /// at-rest record the repair pass's peer-reconciliation faithfulness gates peel a healed set against, so a
    /// generation persisted by this database is peer-repairable.
    /// </summary>
    /// <param name="defaultGraph">The captured default-graph triples the sketch describes.</param>
    /// <param name="pool">The pool the projection buffer and the codec's transients are rented from.</param>
    /// <param name="destination">The sink the framed sketch image is written to.</param>
    /// <exception cref="InvalidOperationException">The default graph holds more triples than a single projected-item buffer can address; split the dataset across generations.</exception>
    private void WriteGenerationSketch(ReadOnlyMemory<EncodedTriple> defaultGraph, MemoryPool<byte> pool, IBufferWriter<byte> destination)
    {
        int count = defaultGraph.Length;
        long itemByteCount = (long)count * ContentKey128.ByteWidth;
        if(itemByteCount > Array.MaxLength)
        {
            throw new InvalidOperationException("The default graph holds more triples than a single projected-item buffer can address; split the dataset across generations.");
        }

        using IMemoryOwner<byte> itemOwner = pool.Rent((int)Math.Max(1, itemByteCount));
        Span<ContentKey128> items = MemoryMarshal.Cast<byte, ContentKey128>(itemOwner.Memory.Span)[..count];
        ReadOnlySpan<EncodedTriple> triples = defaultGraph.Span;
        for(int i = 0; i < count; i++)
        {
            items[i] = StructuralReconciliationProjection.Project(triples[i]);
        }

        SketchPersistence.PersistSketch(items, SketchContract.Structural, GenerationSketchSymbolBudget, PersistChecksum ?? ChecksumAlgorithm.XxHash3, pool, new RatelessSketchCodec(pool).Encode, destination);
    }

    /// <summary>The distinct triples of a seed list in first-appearance order — the set the committed store deduplicates its input to, which is what a baseline annotation dots.</summary>
    /// <param name="triples">The seed triples, duplicates permitted.</param>
    /// <returns>The distinct triples.</returns>
    private static List<EncodedTriple> DistinctTriples(List<EncodedTriple> triples)
    {
        HashSet<EncodedTriple> seen = new(triples.Count);
        List<EncodedTriple> distinct = new(triples.Count);
        foreach(EncodedTriple triple in triples)
        {
            if(seen.Add(triple))
            {
                distinct.Add(triple);
            }
        }

        return distinct;
    }

    /// <summary>
    /// A resumed database's ledger-recovery outcome: the recovered ledger when the store proved remove-aware, and
    /// whether ANY causality trace was seen — an artifact image (verified or unreadable), an artifact refusal, or
    /// an annotated journal entry. The trace is the eligibility gate of the explicit baseline step: the step runs
    /// only on a store with NO trace at all, because a fresh baseline mints its dot counters from one, and over
    /// surviving causal history those counters could re-issue dots the history already names for other events.
    /// </summary>
    /// <param name="Ledger">The recovered ledger, or <see langword="null"/> when the store is not remove-aware.</param>
    /// <param name="CausalityTraceSeen">Whether any causality trace was seen during recovery.</param>
    private readonly record struct LedgerRecovery(DottedCommitLedger? Ledger, bool CausalityTraceSeen);

    /// <summary>
    /// Recovers the dotted commit ledger of a RESUMED remove-aware database: restores the generation's causality
    /// artifact when one loaded, folds every journal entry over it in sequence order (each fold is idempotent
    /// per entry — a covered addition dot is incorporated history and skips — so no position bookkeeping exists
    /// to drift), and cross-checks the final StateId stamp against the committed state actually served. The
    /// whole-log read costs nothing new asymptotically: the content recovery beside it already materialises the
    /// same log, and pre-artifact entries fold as skips. The result carries no ledger — the store is NOT
    /// remove-aware and remove-aware reconciliation refuses by the no-causality-pair rule — when no causality
    /// source exists (no artifact and no baseline-annotated journal), when the artifact was named but refused
    /// verification, when its pairing stamp does not match the generation it rode with, or when the recovered
    /// stamp does not match the committed state. Becoming remove-aware from there is the explicit baseline
    /// step, never an ambient upgrade — gated by the result's causality-trace flag.
    /// </summary>
    /// <param name="identity">The host replica identity supplied at open.</param>
    /// <param name="load">The durable generation load, carrying the causality artifact's image or its refusal.</param>
    /// <param name="journalRead">The durable dataset journal's read seam whose annotated entries fold over the artifact, or <see langword="null"/> for a journal-less reopen. The platform-neutral delegate keeps this method callable from the platform-neutral open path; the caller captures it inside the browser-guarded journal branch.</param>
    /// <param name="committedStateId">The dataset StateId the reopened database serves — the cross-check target.</param>
    /// <param name="cancellationToken">A token that aborts the journal read.</param>
    /// <returns>The recovery outcome: the ledger when the store is remove-aware, and the causality-trace flag.</returns>
    private static async ValueTask<LedgerRecovery> RecoverLedgerAsync(
        ReplicaAxis identity,
        DurableSystemOfRecordLoad load,
        DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? journalRead,
        NodeIdentifier committedStateId,
        CancellationToken cancellationToken)
    {
        bool causalityTraceSeen = load.CausalityImage is not null || load.CausalityRefused;
        bool causalitySourceSeen = false;
        DottedCommitLedger candidate;
        if(load.CausalityImage is { } image)
        {
            DottedLedgerSnapshot snapshot;
            try
            {
                snapshot = DottedLedgerSnapshot.ReadFrom(image.Span);
            }
            catch(InvalidDataException)
            {
                return new LedgerRecovery(Ledger: null, causalityTraceSeen);
            }
            catch(NotSupportedException)
            {
                return new LedgerRecovery(Ledger: null, causalityTraceSeen);
            }

            //The pairing stamp: the artifact was captured from the same committed state as its generation, so
            //its StateId must equal the manifest's provenance epoch. A torn pair is refused, never served.
            if(snapshot.StateId != new NodeIdentifier(unchecked((ulong)load.ProvenanceEpoch)))
            {
                return new LedgerRecovery(Ledger: null, causalityTraceSeen);
            }

            candidate = DottedCommitLedger.RestoreSnapshot(identity, snapshot);
            causalitySourceSeen = true;
        }
        else
        {
            if(load.CausalityRefused)
            {
                return new LedgerRecovery(Ledger: null, causalityTraceSeen);
            }

            candidate = new DottedCommitLedger(identity, baseline: null, NodeIdentifier.Empty);
        }

        //Causality is ACTIVE from the artifact's restore point, or from the first annotated journal entry. On a
        //remove-aware store every default-graph commit is annotated, so an UNANNOTATED default-graph commit
        //after causality began proves an identity-less mutable session committed outside the regime — a broken
        //causal lineage whose recorded knowledge no longer describes the committed set. The store is then not
        //remove-aware, and the break counts as a causality trace: the explicit baseline step refuses over it
        //rather than re-minting dot counters the surviving history already names for other events.
        bool causalityActive = causalitySourceSeen;
        if(journalRead is not null)
        {
            await foreach(DatasetJournalEntry entry in journalRead(0, cancellationToken).ConfigureAwait(false))
            {
                if(causalityActive
                    && entry.Causality is null
                    && entry.EntryKind is EditSessionEntryKind.Initial or EditSessionEntryKind.Committed
                    && MovesDefaultGraphContent(entry))
                {
                    return new LedgerRecovery(Ledger: null, CausalityTraceSeen: true);
                }

                candidate.FoldRecoveredEntry(entry);
                if(entry.Causality is not null)
                {
                    causalityTraceSeen = true;
                    causalityActive = true;
                }

                if(entry.Causality is { IsBaseline: true })
                {
                    causalitySourceSeen = true;
                }
            }
        }

        if(!causalitySourceSeen)
        {
            return new LedgerRecovery(Ledger: null, causalityTraceSeen);
        }

        return candidate.StateId == committedStateId
            ? new LedgerRecovery(candidate, CausalityTraceSeen: true)
            : new LedgerRecovery(Ledger: null, CausalityTraceSeen: true);
    }

    /// <summary>Whether a journal entry's transitions move DEFAULT-graph content — the graph causality annotations cover; named-graph transitions are outside the dotted regime.</summary>
    /// <param name="entry">The journal entry.</param>
    /// <returns><see langword="true"/> when a default-graph transition adds or removes triples.</returns>
    private static bool MovesDefaultGraphContent(in DatasetJournalEntry entry)
    {
        foreach(DatasetGraphTransition transition in entry.Transitions)
        {
            if(transition.Graph == TermId.None && (transition.Additions.Length > 0 || transition.Removals.Length > 0))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Commits the explicit causality baseline of a resumed, identity-supplied store that recovery proved has no
    /// causality trace at all: one causality-only annotated commit whose baseline annotation dots every present
    /// committed triple on the host identity's axis, minted by the caller over the empty ledger wired moments
    /// before (hoisted so a coordinated open records the baseline's intent before this durable commit).
    /// The commit rides the ordinary annotated journal-append-then-publish path under the causality commit gate,
    /// so the ledger folds the baseline exactly as journal replay will on the next open — one code path, one
    /// semantics — and the entry is what later journal-only recoveries claim remove-awareness through. Runs
    /// inside the open, before the engine serves: no commit races it, which is what makes the
    /// quiesce-before-baseline discipline structural for the local store.
    /// </summary>
    /// <param name="dataset">The dataset, already wired with the composed observer and the ledger's causality builder.</param>
    /// <param name="baseline">The minted baseline causality, hoisted to the caller so a coordinated open records its intent before this durable commit.</param>
    /// <param name="cancellationToken">A token that aborts the commit.</param>
    private static async ValueTask CommitExplicitBaselineAsync(MutableSparqlDataset dataset, CommitCausality baseline, CancellationToken cancellationToken)
    {
        using CausalityCommitScope scope = await dataset.EnterCausalityCommitScopeAsync(cancellationToken).ConfigureAwait(false);
        DatasetEditSession session = await dataset.OpenSessionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await session.CommitAsync(baseline, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Validates a mutable open's replication-causality configuration before any resource is acquired, so a misconfiguration fails the open loudly and early: the explicit baseline step mints on the host identity's axis, so requesting it without an identity is an argument error.</summary>
    /// <param name="options">The engine options carrying the replication-causality settings.</param>
    /// <exception cref="ArgumentException">The explicit baseline step is requested without a replica identity.</exception>
    private static void EnsureReplicationCausalityConfigurationValid(VeritasEngineOptions options)
    {
        if(options.BaselineReplicationCausality && options.ReplicaIdentity is null)
        {
            throw new ArgumentException("The explicit causality baseline step requires a replica identity (VeritasEngineOptions.ReplicaIdentity): the baseline mints one dot per present committed triple on the host identity's axis.", nameof(options));
        }
    }

    /// <summary>Refuses the explicit baseline step on a query-only open before any resource is acquired: the step commits a baseline journal entry, which only a mutable database can do, so an immutable open answering the request would silently drop a commanded action — it refuses loudly instead.</summary>
    /// <param name="options">The engine options carrying the replication-causality settings.</param>
    /// <exception cref="ArgumentException">The explicit baseline step is requested on an immutable open.</exception>
    private static void EnsureNoBaselineOnImmutableOpen(VeritasEngineOptions options)
    {
        if(options.BaselineReplicationCausality)
        {
            throw new ArgumentException("The explicit causality baseline step (VeritasEngineOptions.BaselineReplicationCausality) applies to mutable opens only: the step commits a baseline journal entry, which a query-only database cannot do. Open the store with OpenMutableAsync to baseline it.", nameof(options));
        }
    }

    /// <summary>
    /// Claims the host's replica identity on the deployment's coordinated metadata record before this open mints
    /// under it. An undecided or configuration-refused consultation FAILS OPEN — the plane is never a liveness
    /// dependency of the data lane — while the definite answer that ANOTHER live minter holds the identity
    /// refuses the open in the same open-refusal family as an identity-less remove-aware open: minting under an
    /// axis a quorum says belongs to someone else is the silent dot collision the claim exists to prevent.
    /// </summary>
    /// <param name="options">The engine options carrying the seams and the identity.</param>
    /// <param name="cancellationToken">A token that aborts the consultation.</param>
    /// <returns>The standing the claim established; <see cref="MetadataCoordinationStanding.NotConfigured"/> without seams or an identity.</returns>
    /// <exception cref="InvalidOperationException">The coordinated record holds this identity for another minter.</exception>
    private static async ValueTask<MetadataCoordinationStanding> ConsultIdentityClaimAsync(VeritasEngineOptions options, CancellationToken cancellationToken)
    {
        if(options.MetadataCoordination is not { } seams || options.ReplicaIdentity is not { } identity)
        {
            return MetadataCoordinationStanding.NotConfigured;
        }

        IdentityClaimOutcome outcome = await seams.ClaimIdentity(identity, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            IdentityClaimOutcome.Claimed or IdentityClaimOutcome.AlreadyClaimedBySelf => MetadataCoordinationStanding.Confirmed,
            IdentityClaimOutcome.RefusedHeldByOther => throw new InvalidOperationException("The deployment's coordinated metadata record holds this replica identity for another minter: opening under it would produce colliding dots (distinct events under one name). Supply this host's own identity, or retire the holding replica from the coordinated membership first."),
            _ => MetadataCoordinationStanding.Pending
        };
    }

    /// <summary>
    /// Records the lineage baseline's INTENT on the coordinated record before the local durable commit: the
    /// digest over the minted causality is the only lineage identity that exists at this point. An undecided
    /// consultation fails open with the intent pending; the definite answer that the lineage already descends
    /// from a DIFFERENT baseline refuses the open, because committing would seed the second independent lineage
    /// the coordination exists to prevent.
    /// </summary>
    /// <param name="options">The engine options carrying the seams and the identity.</param>
    /// <param name="causality">The minted baseline causality.</param>
    /// <param name="pool">The pool the digest's canonical encoding is staged in.</param>
    /// <param name="cancellationToken">A token that aborts the consultation.</param>
    /// <returns>The digest the confirm is later matched by (or <see langword="null"/> without seams), and the standing the intent established.</returns>
    /// <exception cref="InvalidOperationException">The coordinated record already carries a different lineage baseline.</exception>
    private static async ValueTask<(NodeIdentifier? Digest, MetadataCoordinationStanding Standing)> ConsultBaselineIntentAsync(VeritasEngineOptions options, CommitCausality causality, MemoryPool<byte> pool, CancellationToken cancellationToken)
    {
        if(options.MetadataCoordination is not { } seams || options.ReplicaIdentity is not { } identity)
        {
            return (null, MetadataCoordinationStanding.NotConfigured);
        }

        NodeIdentifier digest = LineageDigests.DigestOf(causality, pool);
        BaselineRecordOutcome outcome = await seams.RecordBaselineIntent(identity, digest, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            BaselineRecordOutcome.Recorded or BaselineRecordOutcome.AlreadyRecorded or BaselineRecordOutcome.Confirmed => (digest, MetadataCoordinationStanding.Confirmed),
            BaselineRecordOutcome.ConflictingLineage => throw new InvalidOperationException("The deployment's coordinated metadata record already carries a lineage baseline with a different causality digest: committing this baseline would seed a second independent lineage. Clone from the coordinated lineage instead, or supersede the recorded baseline at the operator level."),
            _ => (digest, MetadataCoordinationStanding.Pending)
        };
    }

    /// <summary>
    /// Confirms the lineage baseline on the coordinated record after the local durable commit, filling the
    /// dataset StateId and the dictionary epoch on the intent the digest matches. The local commit already
    /// happened, so nothing refuses here: a conflicting answer marks the standing CONTESTED for the operator,
    /// and an undecided one leaves it pending for the next open's idempotent retry.
    /// </summary>
    /// <param name="options">The engine options carrying the seams.</param>
    /// <param name="digest">The intent's digest, or <see langword="null"/> when no intent was made (nothing to confirm).</param>
    /// <param name="stateId">The dataset StateId the committed baseline produced.</param>
    /// <param name="dictionaryEpoch">The term-dictionary epoch the committed baseline was written under, reinterpreted from the dictionary's unsigned epoch exactly as the dotted wire headers reinterpret it — one bit pattern, two signednesses, never a value change.</param>
    /// <param name="cancellationToken">A token that aborts the consultation.</param>
    /// <returns>The standing the confirm established; <see cref="MetadataCoordinationStanding.NotConfigured"/> with nothing to confirm.</returns>
    private static async ValueTask<MetadataCoordinationStanding> ConsultBaselineConfirmAsync(VeritasEngineOptions options, NodeIdentifier? digest, NodeIdentifier stateId, long dictionaryEpoch, CancellationToken cancellationToken)
    {
        if(options.MetadataCoordination is not { } seams || digest is not { } lineageDigest)
        {
            return MetadataCoordinationStanding.NotConfigured;
        }

        BaselineRecordOutcome outcome = await seams.ConfirmBaseline(lineageDigest, stateId, dictionaryEpoch, cancellationToken).ConfigureAwait(false);

        return outcome switch
        {
            BaselineRecordOutcome.Confirmed or BaselineRecordOutcome.AlreadyRecorded or BaselineRecordOutcome.Recorded => MetadataCoordinationStanding.Confirmed,
            BaselineRecordOutcome.ConflictingLineage => MetadataCoordinationStanding.Contested,
            _ => MetadataCoordinationStanding.Pending
        };
    }

    /// <summary>Combines two coordination standings by severity: contested over pending over confirmed over not-configured, so one adverse step names the open's whole standing.</summary>
    /// <param name="first">The standing so far.</param>
    /// <param name="second">The step's standing.</param>
    /// <returns>The severer of the two.</returns>
    private static MetadataCoordinationStanding WorstStanding(MetadataCoordinationStanding first, MetadataCoordinationStanding second)
    {
        return first >= second ? first : second;
    }

    /// <summary>
    /// Opens a MUTABLE database over a default graph: SPARQL Update mutations commit into a mutable dataset, and
    /// queries answer off the latest committed snapshot (read-your-writes). With no
    /// <see cref="VeritasEngineOptions.DatasetJournalPath"/> the journal is in memory (the development, testing, and
    /// correctness engine); with one set, this CREATES a durable dataset over an append-only log — every commit is
    /// flushed to stable storage before it is acknowledged, so an acked commit survives a crash, and the initial
    /// build lands durably. Reopening durable history goes through
    /// <see cref="OpenMutableAsync(PersistenceStore, VeritasEngineOptions?, CancellationToken)"/>; this create
    /// overload REFUSES a journal path whose log already holds entries. When <see cref="VeritasEngineOptions.Reasoning"/>
    /// is configured the engine is REASONED: its RL entailments are materialised into the served default graph at
    /// open and MAINTAINED incrementally on every commit, so queries answer over the entailed graph continuously
    /// (the journal, replication, and persistence still describe exactly the asserted graph). An open under
    /// <see cref="ReasoningConfiguration.RefuseInconsistent"/> over inconsistent seed data throws
    /// <see cref="ReasoningInconsistencyException"/>. A mutable open with reasoning left unwired serves the asserted
    /// graph, byte-identical to before.
    /// </summary>
    /// <param name="defaultGraph">The default-graph triples to seed the database with.</param>
    /// <param name="options">The configuration; for a mutable database the federation and access seams (<see cref="VeritasEngineOptions.ServiceTransport"/>, <see cref="VeritasEngineOptions.GraphSource"/>, <see cref="VeritasEngineOptions.AccessControl"/>), <see cref="VeritasEngineOptions.Reasoning"/>, and <see cref="VeritasEngineOptions.DatasetJournalPath"/> apply. <c>null</c> wires none.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The opened mutable database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="defaultGraph"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested without a <see cref="VeritasEngineOptions.ReplicaIdentity"/>.</exception>
    /// <exception cref="InvalidDataException">A <see cref="VeritasEngineOptions.DatasetJournalPath"/> is set and its log already holds entries — this overload creates a dataset; reopen through the store overload instead.</exception>
    /// <exception cref="PlatformNotSupportedException">A <see cref="VeritasEngineOptions.DatasetJournalPath"/> is set on a browser runtime — the file-backed dataset journal is host-only; a browser wires its own durable backend behind the journal contract.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenMutableAsync(IEnumerable<DataTriple> defaultGraph, VeritasEngineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultGraph);

        options ??= VeritasEngineOptions.Default;
        EnsureReplicationCausalityConfigurationValid(options);
        TermDictionary dictionary = new(MintReplicationEpoch());
        List<EncodedTriple> encoded = EncodeGraph(defaultGraph, dictionary);

        return await OpenMutableFromEncodedAsync(dictionary, encoded, namedGraphs: null, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a MUTABLE database directly from a stream of quads — the streaming-ingest boundary for the mutable
    /// create path. The stream is enumerated EXACTLY ONCE: each quad's terms are encoded into the shared dictionary
    /// as it arrives and bucketed into the default graph (a quad with a <see langword="null"/>
    /// <see cref="Quad.Graph"/>) or its named graph, so no <see cref="Quad"/> list and no <see cref="DataTriple"/>
    /// list is materialised — peak ingest memory is the encoded dataset plus the dictionary. The dataset is then
    /// created through the same create path the list overload uses, so this open is behaviourally identical to
    /// <see cref="OpenMutableAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> over the same
    /// data, and the streamed named graphs seed the mutable dataset's named graphs. The
    /// <see cref="VeritasEngineOptions.DatasetJournalPath"/> is honoured exactly as the list overload does: with none
    /// the journal is in memory; with one set this CREATES a durable dataset (flush-before-ack) whose initial state
    /// lands durably, and it REFUSES a journal path whose log already holds entries (reopen through
    /// <see cref="OpenMutableAsync(PersistenceStore, VeritasEngineOptions?, CancellationToken)"/>). When
    /// <see cref="VeritasEngineOptions.Reasoning"/> is configured the engine is REASONED, materialising and
    /// maintaining its RL entailments over the served default graph exactly as the list overload does.
    /// </summary>
    /// <param name="quads">The quads to ingest, enumerated exactly once; a quad with a <see langword="null"/> graph is a default-graph triple, otherwise a triple of that named graph.</param>
    /// <param name="options">The configuration; the federation and access seams and <see cref="VeritasEngineOptions.DatasetJournalPath"/> apply. <c>null</c> wires none.</param>
    /// <param name="cancellationToken">A token that aborts opening (observed at each quad).</param>
    /// <returns>The opened mutable database.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested without a <see cref="VeritasEngineOptions.ReplicaIdentity"/>.</exception>
    /// <exception cref="InvalidDataException">A <see cref="VeritasEngineOptions.DatasetJournalPath"/> is set and its log already holds entries — this overload creates a dataset; reopen through the store overload instead.</exception>
    /// <exception cref="PlatformNotSupportedException">A <see cref="VeritasEngineOptions.DatasetJournalPath"/> is set on a browser runtime — the file-backed dataset journal is host-only.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenMutableAsync(IAsyncEnumerable<Quad> quads, VeritasEngineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);

        options ??= VeritasEngineOptions.Default;
        EnsureReplicationCausalityConfigurationValid(options);
        TermDictionary dictionary = new(MintReplicationEpoch());
        List<EncodedTriple> defaultEncoded = [];
        Dictionary<TermId, List<EncodedTriple>> namedBuckets = [];
        await foreach(Quad quad in quads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            EncodedTriple triple = new(dictionary.GetOrAdd(quad.Subject), dictionary.GetOrAdd(quad.Predicate), dictionary.GetOrAdd(quad.Object));
            if(quad.Graph is null)
            {
                defaultEncoded.Add(triple);
            }
            else
            {
                TermId graphName = dictionary.GetOrAdd(quad.Graph);
                if(!namedBuckets.TryGetValue(graphName, out List<EncodedTriple>? bucket))
                {
                    bucket = [];
                    namedBuckets[graphName] = bucket;
                }

                bucket.Add(triple);
            }
        }

        return await OpenMutableFromEncodedAsync(dictionary, defaultEncoded, ToNamedGraphMap(namedBuckets), options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Materialises the streamed named-graph buckets as the read-only build map <see cref="MutableSparqlDataset.CreateAsync"/> consumes, or <see langword="null"/> when no named graph was streamed (the default-only case).</summary>
    /// <param name="buckets">The named-graph triple buckets keyed by graph-name term id.</param>
    /// <returns>The named graphs keyed by graph-name term id, or <see langword="null"/> for none.</returns>
    private static Dictionary<TermId, IReadOnlyList<EncodedTriple>>? ToNamedGraphMap(Dictionary<TermId, List<EncodedTriple>> buckets)
    {
        if(buckets.Count == 0)
        {
            return null;
        }

        Dictionary<TermId, IReadOnlyList<EncodedTriple>> map = new(buckets.Count);
        foreach((TermId graphName, List<EncodedTriple> triples) in buckets)
        {
            map[graphName] = triples;
        }

        return map;
    }

    /// <summary>
    /// Creates a mutable database from an already-encoded dataset — the single create core the list and quad-stream
    /// mutable opens both route through, so they cannot diverge below the encode. It owns the buffer pool, the durable
    /// journal, and the journal's term pool through the create (transferring them to the engine on success and
    /// disposing whatever was not transferred on failure), wires the durable-or-in-memory journal per
    /// <see cref="VeritasEngineOptions.DatasetJournalPath"/> (with the browser refusal and the existing-history
    /// refusal), and seeds the reconciliation feed from the committed default graph.
    /// </summary>
    /// <param name="dictionary">The shared dictionary the triples are encoded against (its replication epoch already minted).</param>
    /// <param name="defaultTriples">The encoded default-graph triples the reconciliation feed is seeded from.</param>
    /// <param name="namedGraphs">The encoded named graphs keyed by graph-name term id, or <see langword="null"/> for a default-only dataset.</param>
    /// <param name="options">The configuration (non-<see langword="null"/>).</param>
    /// <param name="cancellationToken">A token that aborts the create.</param>
    /// <returns>The opened mutable database.</returns>
    /// <exception cref="InvalidDataException">A durable journal path's log already holds entries.</exception>
    /// <exception cref="PlatformNotSupportedException">A durable journal path is set on a browser runtime.</exception>
    private static async ValueTask<VeritasEngine> OpenMutableFromEncodedAsync(
        TermDictionary dictionary,
        List<EncodedTriple> defaultTriples,
        IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>>? namedGraphs,
        VeritasEngineOptions options,
        CancellationToken cancellationToken)
    {
        //Ownership-transfer pattern: the buffer pool (the replication pool, also the durable journal's per-append
        //serialization pool), the journal, and its term pool are owned here until the engine takes them; the finally
        //disposes whatever ownership was not transferred (nulled).
        VeritasMemoryPool<byte>? bufferPool = new();
        Utf8StringPool? termPool = null;
        FileBackedDatasetJournal? journal = null;
        try
        {
            DatasetJournalDelegates.AppendDatasetJournalEntryAsync journalAppend;
            DatasetJournalDelegates.ReadDatasetJournalEntriesAsync journalRead;
            if(options.DatasetJournalPath is { } journalPath)
            {
                //The durable dataset journal is host-only: a browser has no file system to append into and wires its
                //own durable backend behind the journal contract, so the file-backed journal path refuses there.
                if(OperatingSystem.IsBrowser())
                {
                    throw new PlatformNotSupportedException("A durable dataset journal (DatasetJournalPath) is host-only; a browser runtime wires its own durable backend.");
                }

                //The term pool backs the journal's restored terms (a fresh create restores nothing, but the ctor
                //replays whatever file exists), so the database owns it for the dictionary's lifetime.
                termPool = new();

                //A create-path v2 log: the header carries no anchor (this is a self-contained build whose Initial
                //record opens the log), the dictionary's replication epoch (restored on a later journal-only reopen),
                //and a zero attach term watermark (the Initial captures the whole dictionary). The record stream stays
                //on the built-in checksum regardless of options.Checksum; the header records that algorithm id so a
                //later keyed-journal flip resolves and refuses an unreadable stream instead of truncating it.
                journal = FileBackedDatasetJournal.OpenV2(journalPath, dictionary, termPool, VeritasHashing.Default, ChecksumAlgorithm.XxHash3, TimeProvider.System, bufferPool, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush, NodeIdentifier.Empty, attachTermWatermark: 0, options.ResolveChecksum);
                if(journal.Length > 0)
                {
                    throw new InvalidDataException("The durable dataset journal already holds entries; this create overload cannot onboard existing durable history — reopen through OpenMutableAsync(PersistenceStore) instead.");
                }

                journalAppend = journal.AppendDelegate;
                journalRead = journal.ReadDelegate;
            }
            else
            {
                InMemoryDatasetJournal inMemory = new();
                journalAppend = inMemory.AppendDelegate;
                journalRead = inMemory.ReadDelegate;
            }

            //When reasoning is wired, build the maintenance object over the initial base (one remat), honour
            //RefuseInconsistent at open (a decided-inconsistent open throws inside CreateReasoningMaintenanceAsync
            //before the engine is returned, its lane and journal freed by the ownership-transfer finally), and seed
            //the served store with the initial derived overlay so the first query answers the closure.
            ReasoningMaintenance? maintenance = null;
            bool refuseInconsistent = false;
            if(options.Reasoning is { } reasoningConfiguration)
            {
                maintenance = await CreateReasoningMaintenanceAsync(reasoningConfiguration, options, defaultTriples, dictionary, cancellationToken).ConfigureAwait(false);
                refuseInconsistent = reasoningConfiguration.RefuseInconsistent;
            }

            //A store CREATED with a host identity is remove-aware from birth: the Initial entry carries the
            //baseline annotation dotting every seed triple on the supplied axis — the Initial entry IS the
            //baseline — and the SAME annotation object seeds the ledger below, one source of truth for both.
            //The baseline mints over the distinct triples, because the committed store deduplicates its input.
            //With coordination seams, the identity is claimed on the coordinated record before anything mints
            //under it, and the baseline's intent is recorded before the local commit — the digest over the
            //minted causality is the only lineage identity that exists at this point.
            MetadataCoordinationStanding coordinationStanding = await ConsultIdentityClaimAsync(options, cancellationToken).ConfigureAwait(false);
            CommitCausality? initialCausality = null;
            NodeIdentifier? lineageDigest = null;
            if(options.ReplicaIdentity is { } mintingIdentity)
            {
                initialCausality = DottedCommitLedger.MintBaseline(mintingIdentity, DistinctTriples(defaultTriples));
                (NodeIdentifier? intentDigest, MetadataCoordinationStanding intentStanding) = await ConsultBaselineIntentAsync(options, initialCausality, bufferPool, cancellationToken).ConfigureAwait(false);
                lineageDigest = intentDigest;
                coordinationStanding = WorstStanding(coordinationStanding, intentStanding);
            }

            MutableSparqlDataset dataset = maintenance is null
                ? await MutableSparqlDataset.CreateAsync(
                    dictionary,
                    defaultTriples,
                    namedGraphs: namedGraphs,
                    journalAppend: journalAppend,
                    journalRead: journalRead,
                    valueIndexes: options.ValueIndexes,
                    initialCausality: initialCausality,
                    cancellationToken: cancellationToken).ConfigureAwait(false)
                : await MutableSparqlDataset.CreateAsync(
                    dictionary,
                    defaultTriples,
                    [.. maintenance.InitialState.ServedAdditions],
                    ReasoningProvenance.From(maintenance.InitialState),
                    namedGraphs,
                    journalAppend,
                    journalRead,
                    options.ValueIndexes,
                    initialCausality,
                    cancellationToken).ConfigureAwait(false);

            WireMaintenance(dataset, maintenance, refuseInconsistent);

            //Seed the reconciliation feed from the same committed triples and subscribe the delta observer: the
            //sketch maintainer advances the feed AND folds the same committed delta into the maintained encoder
            //under one gate, so the served sketch tracks the committed default graph exactly. With a host
            //identity, the ledger is seeded from the same baseline annotation the Initial entry carries and the
            //dataset's ACTUAL StateId, and the composed observer fans the same delta to maintainer and ledger.
            ReplicationIndexFeed feed = new(defaultTriples, default);
            IncrementalSketchMaintainer sketchMaintainer = new(feed, bufferPool, IncrementalSketchMaintainerOptions.Default, dictionary.Epoch);
            DottedCommitLedger? ledger = null;
            if(options.ReplicaIdentity is { } replicaIdentity)
            {
                ledger = new DottedCommitLedger(replicaIdentity, initialCausality, dataset.StateId);
                ComposedCommittedDeltaObserver composedObserver = new(sketchMaintainer, ledger);
                dataset.ObserveDefaultGraphDelta(composedObserver.OnDefaultGraphDelta);
                dataset.RegisterCausalityBuilder(ledger.BuildLocalCausality);
            }
            else
            {
                dataset.ObserveDefaultGraphDelta(sketchMaintainer.OnDefaultGraphDelta);
            }

            //The baseline's confirm fills the StateId and the dictionary epoch on the recorded intent, now that
            //both exist; a conflicting answer marks the standing contested rather than refusing a commit that
            //already happened.
            coordinationStanding = WorstStanding(coordinationStanding, await ConsultBaselineConfirmAsync(options, lineageDigest, dataset.StateId, unchecked((long)dictionary.Epoch), cancellationToken).ConfigureAwait(false));

            VeritasEngine engine = new(dataset, feed, sketchMaintainer, ledger, options.ReplicaIdentity is not null, options.BaselineReplicationCausality ? ReplicationBaselineOutcome.AlreadyRemoveAware : ReplicationBaselineOutcome.NotRequested, bufferPool, BuildServiceClient(options), options.GraphSource, options.AccessControl, options.AnalyticsTrace, options.SparqlExecutionTrace, journal, termPool, persistChecksum: options.Checksum, persistResolveChecksum: options.ResolveChecksum, sparqlPolicy: options.SparqlExecution, implicitTimezone: options.ImplicitTimezone, sparqlUpdateOptions: options.SparqlUpdate, valueDatatypes: options.ValueDatatypes, extensionFunctions: options.ExtensionFunctions, metadataCoordination: coordinationStanding);
            bufferPool = null;
            termPool = null;
            journal = null;

            return engine;
        }
        finally
        {
            DisposeJournal(journal);
            termPool?.Dispose();
            bufferPool?.Dispose();
        }
    }

    /// <summary>Disposes a durable dataset journal through the platform-neutral <see cref="IDisposable"/> seam: the file-backed journal is host-only (browser-unsupported), and disposing it through <see cref="IDisposable"/> keeps this unconditional teardown callable from the platform-neutral open and dispose paths without a platform guard that would leave the dispose conditional.</summary>
    /// <param name="journal">The journal to dispose, or <see langword="null"/> when none was wired.</param>
    [SuppressMessage("Performance", "CA1859", Justification = "The IDisposable parameter is deliberate: the concrete file-backed journal is host-only (browser-unsupported), so disposing through IDisposable is what keeps this unconditional teardown compiling on every platform. The virtual dispatch cost is immaterial at engine teardown.")]
    private static void DisposeJournal(IDisposable? journal)
    {
        journal?.Dispose();
    }

    /// <summary>
    /// Reopens a MUTABLE database from a durable <see cref="PersistenceStore"/>, recovering acked commits: it loads
    /// the live committed generation (through <see cref="DurableSystemOfRecordStore"/>), and when a
    /// <see cref="VeritasEngineOptions.DatasetJournalPath"/> is configured it folds that generation forward through
    /// the durable dataset journal — replaying every commit acknowledged after the persisted generation — and
    /// resumes the mutable dataset over the reconstructed head state, verified content-addressed against the journal
    /// head. With no journal path this is a plain warm mutable start over the loaded generation (an in-memory
    /// journal from here on). A mutable database is always eager (mutation needs real stores), so the residency
    /// option does not apply on this path. When <see cref="VeritasEngineOptions.Reasoning"/> is configured the
    /// reopened engine is REASONED: the closure is in-memory by design and never persisted, so reasoning is rebuilt
    /// at open over the recovered asserted base (one remat) and MAINTAINED on every subsequent commit, and the
    /// served default graph carries the entailments from the first query — an open under
    /// <see cref="ReasoningConfiguration.RefuseInconsistent"/> over an inconsistent recovered base throws
    /// <see cref="ReasoningInconsistencyException"/>.
    /// </summary>
    /// <remarks>
    /// A fresh journal path over an existing generation ATTACHES: the engine creates a dataset-journal format v2 log
    /// anchored at the loaded generation's state and durably acks commits onward, so a bulk build persisted with no
    /// journal can gain a durable journal on reopen. A generation-less reopen of an existing v2 log restores the
    /// dictionary's replication epoch from the header rather than minting a fresh one. The reopen still refuses loudly
    /// when the store and the journal disagree: a persisted generation whose state neither appears in a record nor is
    /// the log's header anchor (different histories), a v2 journal whose header replication epoch differs from the
    /// loaded dictionary's (the identity discriminator content-addressed anchors cannot provide), an attached log
    /// whose first record does not continue the anchor, a generation with no dataset state binding, or a rebuilt head
    /// state that does not match the journal head. Recovery fidelity — how many commits replayed, any torn-tail loss,
    /// and any commitment findings — is surfaced on <see cref="DatasetJournalRecovery"/>.
    /// </remarks>
    /// <param name="store">The durable store a generation was committed into by <see cref="Persist"/>; may hold no generation, in which case a generation-less recovery serves the journal (or an empty dataset).</param>
    /// <param name="options">The configuration; the federation and access seams, <see cref="VeritasEngineOptions.Reasoning"/>, <see cref="VeritasEngineOptions.DatasetJournalPath"/>, and the replication-causality settings (<see cref="VeritasEngineOptions.ReplicaIdentity"/>, <see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) apply. <c>null</c> uses <see cref="VeritasEngineOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token that aborts opening.</param>
    /// <returns>The reopened mutable database serving its recovered state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The explicit causality baseline step (<see cref="VeritasEngineOptions.BaselineReplicationCausality"/>) is requested without a <see cref="VeritasEngineOptions.ReplicaIdentity"/>.</exception>
    /// <exception cref="InvalidOperationException">The store's live generation carries replication causality (the store is remove-aware) and no <see cref="VeritasEngineOptions.ReplicaIdentity"/> was supplied — an identity-less mutable open would fork the causal lineage silently. A refused baseline REQUEST, by contrast, is a value on <see cref="ReplicationBaseline"/>, never an exception.</exception>
    /// <exception cref="InvalidDataException">The store holds an unservable generation, a recovered artifact failed verification, the persisted generation neither appears in the journal nor is its header anchor, the journal's header replication epoch differs from the loaded dictionary's, an attached log's first record does not continue the anchor, the generation carries no dataset state binding, or the rebuilt state does not match the journal head.</exception>
    /// <exception cref="PlatformNotSupportedException">A <see cref="VeritasEngineOptions.DatasetJournalPath"/> is set on a browser runtime — the file-backed dataset journal is host-only; a browser wires its own durable backend behind the journal contract.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public static async ValueTask<VeritasEngine> OpenMutableAsync(PersistenceStore store, VeritasEngineOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        options ??= VeritasEngineOptions.Default;
        EnsureSelfHealConfigurationValid(options);
        EnsureReplicationCausalityConfigurationValid(options);

        //Ownership discipline mirrors OpenAsync(PersistenceStore): the term pool backs the recovered dictionary for
        //the database's lifetime, the buffer pool is owned (the replication pool and the journal's serialization
        //pool), the journal is owned; the triple pool and the decoded segments are transient (a mutable dataset is
        //eager, so their triples are consumed into the stores at open). The finally disposes whatever ownership was
        //not transferred (nulled).
        Utf8StringPool? termPool = new();
        VeritasMemoryPool<EncodedTriple> triplePool = new();
        VeritasMemoryPool<byte>? bufferPool = new();
        DecodedItemSegment? defaultTriples = null;
        IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> namedGraphSegments = [];
        FileBackedDatasetJournal? journal = null;
        try
        {
            using VeritasMemoryPool<byte> loadPool = new();
            DurableSystemOfRecordLoad load = new DurableSystemOfRecordStore(store, loadPool, options.Checksum, options.ResolveChecksum).TryLoad(termPool, triplePool);

            bool generationExists;
            TermDictionary dictionary;
            switch(load.Outcome)
            {
                case DurableSystemOfRecordLoadOutcome.NotFound:
                    generationExists = false;
                    dictionary = new TermDictionary(MintReplicationEpoch());
                    break;
                case DurableSystemOfRecordLoadOutcome.Loaded:
                    generationExists = true;
                    dictionary = load.Dictionary!;
                    defaultTriples = load.Triples!;
                    namedGraphSegments = load.NamedGraphs;
                    break;
                default:
                    throw new InvalidDataException($"The persistence store holds no servable committed generation ({load.Outcome}).");
            }

            //A store whose live generation carries replication causality (a verified OR refused causality
            //artifact) is remove-aware: an identity-less MUTABLE open would commit unannotated entries and
            //persist generations WITHOUT the causality artifact — silently forking the causal lineage and
            //erasing the very evidence the baseline-eligibility check reads. No degraded-but-safe mutable
            //service exists to return, so the open refuses to protect the lineage invariant; a query-only read
            //stays available through OpenAsync. This is the open-refusal family (the journal-epoch mismatch
            //sibling), not an expected per-call condition.
            if(options.ReplicaIdentity is null && (load.CausalityImage is not null || load.CausalityRefused))
            {
                throw new InvalidOperationException("The store's live generation carries replication causality (the store is remove-aware); a mutable open requires VeritasEngineOptions.ReplicaIdentity so committed history stays causally annotated — an identity-less mutable open would fork the causal lineage silently. Supply the host's replica identity, or open the store read-only with OpenAsync.");
            }

            MutableSparqlDataset dataset;
            DatasetJournalRecoveryProvenance? recovery = null;
            List<EncodedTriple> committedDefault;

            //The ledger recovery's journal read seam, captured inside the browser-guarded journal branch below
            //so the platform-neutral recovery helper never touches the host-only journal type's members.
            DatasetJournalDelegates.ReadDatasetJournalEntriesAsync? ledgerRecoveryRead = null;

            //Reasoning is rebuilt at open on the reasoned reopen path (the closure is in-memory by design and is
            //never persisted): the maintenance is built over the recovered asserted base and seeds the served store,
            //so a reasoned reopen serves entailments from the first query, not from the first commit.
            ReasoningMaintenance? maintenance = null;
            bool refuseInconsistent = options.Reasoning?.RefuseInconsistent ?? false;

            //A store CREATED here (no persisted generation to resume) with a host identity is remove-aware from
            //birth: the Initial entry carries the baseline annotation and the same object seeds the ledger. A
            //store RESUMED over an existing generation never mints ambiently — its remove-awareness comes from
            //the recovered causality artifact and the annotated journal, or it stays add-only until the
            //explicit baseline step. With coordination seams, the identity is claimed on the coordinated record
            //before anything mints under it.
            MetadataCoordinationStanding coordinationStanding = await ConsultIdentityClaimAsync(options, cancellationToken).ConfigureAwait(false);
            NodeIdentifier? lineageDigest = null;
            CommitCausality? initialCausality = null;

            if(options.DatasetJournalPath is not { } journalPath)
            {
                //No durable journal: a warm mutable start over the loaded generation, in memory from here (the
                //Initial entry is cheap in memory). Generation-less + no journal is an empty mutable dataset.
                committedDefault = LoadedDefaultTriples(defaultTriples);
                Dictionary<TermId, IReadOnlyList<EncodedTriple>>? loadedNamed = LoadedNamedGraphs(namedGraphSegments);
                InMemoryDatasetJournal inMemory = new();
                maintenance = await MaybeCreateReasoningMaintenanceAsync(options, committedDefault, dictionary, cancellationToken).ConfigureAwait(false);
                if(!generationExists && options.ReplicaIdentity is { } freshIdentity)
                {
                    initialCausality = DottedCommitLedger.MintBaseline(freshIdentity, DistinctTriples(committedDefault));
                    (NodeIdentifier? intentDigest, MetadataCoordinationStanding intentStanding) = await ConsultBaselineIntentAsync(options, initialCausality, bufferPool, cancellationToken).ConfigureAwait(false);
                    lineageDigest = intentDigest;
                    coordinationStanding = WorstStanding(coordinationStanding, intentStanding);
                }

                dataset = maintenance is null
                    ? await MutableSparqlDataset.CreateAsync(dictionary, committedDefault, loadedNamed, inMemory.AppendDelegate, inMemory.ReadDelegate, options.ValueIndexes, initialCausality, cancellationToken).ConfigureAwait(false)
                    : await MutableSparqlDataset.CreateAsync(dictionary, committedDefault, [.. maintenance.InitialState.ServedAdditions], ReasoningProvenance.From(maintenance.InitialState), loadedNamed, inMemory.AppendDelegate, inMemory.ReadDelegate, options.ValueIndexes, initialCausality, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                //The durable dataset journal is host-only: a browser has no file system to append into and wires its
                //own durable backend behind the journal contract, so the file-backed recovery path refuses there.
                if(OperatingSystem.IsBrowser())
                {
                    throw new PlatformNotSupportedException("A durable dataset journal (DatasetJournalPath) is host-only; a browser runtime wires its own durable backend.");
                }

                //A loaded generation with no dataset state binding (provenance epoch zero — a direct store-level
                //persist, never the engine's) cannot anchor a durable journal: zero is also the no-generation
                //sentinel, and anchoring to it would silently full-replay the log and discard the generation's
                //content. This fires FIRST, before the journal is created, so the attach never stamps a header over
                //an unanchorable generation.
                if(generationExists && load.ProvenanceEpoch == 0)
                {
                    throw new InvalidDataException("The persisted generation carries no dataset state binding (provenance epoch zero), so it cannot be reconciled with the durable dataset journal — refusing to serve; persist through the engine so the generation is stamped with its dataset state.");
                }

                //A generation-less reopen of an existing v2 log restores the dictionary's replication epoch from the
                //header instead of minting a fresh one, so a crash-restarted node stays reconcilable under the wire
                //epoch stamps; a v1 log (or a fresh path) keeps the minted epoch and the documented caveat.
                if(!generationExists && FileBackedDatasetJournal.TryReadReplicationEpoch(journalPath, options.ResolveChecksum) is ulong headerEpoch)
                {
                    dictionary = new TermDictionary(headerEpoch);
                }

                //On a FRESH file this creates the v2 log: an attach header anchored at the loaded generation's state,
                //whose term count seeds the watermark chain so the first attached append re-captures only the terms
                //minted after attachment; or a create-path header (no anchor, watermark 0) for a generation-less
                //start. On an EXISTING file the anchor and watermark are read from disk and these are ignored. The
                //record stream stays on the built-in checksum; the header records that algorithm id for a later
                //keyed-journal flip to resolve and refuse against, never truncate.
                NodeIdentifier anchor = generationExists ? new NodeIdentifier(unchecked((ulong)load.ProvenanceEpoch)) : NodeIdentifier.Empty;
                int attachTermWatermark = generationExists ? dictionary.Count : 0;
                journal = FileBackedDatasetJournal.OpenV2(journalPath, dictionary, termPool, VeritasHashing.Default, ChecksumAlgorithm.XxHash3, TimeProvider.System, bufferPool, AtomicPublish.DefaultBarrier, AtomicPublish.DefaultFlush, anchor, attachTermWatermark, options.ResolveChecksum);
                ledgerRecoveryRead = journal.ReadDelegate;

                //State identifiers are content-addressed, so an anchor match alone cannot tell two independently
                //built stores with identical encoded content apart — their generations share the anchor while their
                //dictionaries carry distinct minted epochs. The header's replication epoch is the identity
                //discriminator: a v2 journal attached under one dictionary epoch is refused against a generation
                //carrying another. Every legitimate flow passes for free — attach and create both stamp the header
                //from the live dictionary's epoch.
                if(generationExists && journal.HeaderReplicationEpoch is { } journalEpoch && journalEpoch != dictionary.Epoch)
                {
                    throw new InvalidDataException($"The durable dataset journal was attached under dictionary replication epoch {journalEpoch:X16}, but the loaded generation's dictionary carries epoch {dictionary.Epoch:X16} — refusing to serve; the journal and the store come from different histories.");
                }

                if(!generationExists && journal.Length == 0)
                {
                    //A generation-less fresh log: a fresh empty create so the Initial lands durably behind the header.
                    committedDefault = [];
                    maintenance = await MaybeCreateReasoningMaintenanceAsync(options, committedDefault, dictionary, cancellationToken).ConfigureAwait(false);
                    if(options.ReplicaIdentity is { } freshIdentity)
                    {
                        initialCausality = DottedCommitLedger.MintBaseline(freshIdentity, committedDefault);
                        (NodeIdentifier? intentDigest, MetadataCoordinationStanding intentStanding) = await ConsultBaselineIntentAsync(options, initialCausality, bufferPool, cancellationToken).ConfigureAwait(false);
                        lineageDigest = intentDigest;
                        coordinationStanding = WorstStanding(coordinationStanding, intentStanding);
                    }

                    dataset = maintenance is null
                        ? await MutableSparqlDataset.CreateAsync(dictionary, committedDefault, namedGraphs: null, journal.AppendDelegate, journal.ReadDelegate, options.ValueIndexes, initialCausality, cancellationToken).ConfigureAwait(false)
                        : await MutableSparqlDataset.CreateAsync(dictionary, committedDefault, [.. maintenance.InitialState.ServedAdditions], ReasoningProvenance.From(maintenance.InitialState), namedGraphs: null, journal.AppendDelegate, journal.ReadDelegate, options.ValueIndexes, initialCausality, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    //Fold the log forward: over the loaded generation (the attach anchor, or a record's child when a
                    //newer generation was persisted after attach), or from empty for a self-contained generation-less
                    //log. The header anchor lets the recovery accept an attached log's records as a post-anchor suffix
                    //when the generation appears in no record's child.
                    RecoveredBaseContent baseContent = new(defaultTriples, namedGraphSegments);
                    DatasetJournalReplayResult replay = await DatasetJournalReplayEngine
                        .ReplayAsync(journal.ReadDelegate, journal.Head, anchor, journal.HeaderAnchor, baseContent.Resolve, cancellationToken)
                        .ConfigureAwait(false);
                    if(replay.Outcome == DatasetJournalReplayOutcome.Diverged)
                    {
                        throw new InvalidDataException($"The persisted generation's state {anchor.Value:X16} does not appear in the durable dataset journal (head {journal.Head.Value:X16}) — refusing to serve; the store and the journal come from different histories.");
                    }

                    //The journal head names the state the dataset resumes at: the folded head state once records
                    //exist, or the attach anchor for an attached log with no post-attach records yet. The
                    //head-mismatch oracle in ResumeAsync is the final integrity gate over the rebuilt content.
                    (committedDefault, IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>> mergedNamed) = MergeRecoveredContent(replay, defaultTriples, namedGraphSegments);
                    maintenance = await MaybeCreateReasoningMaintenanceAsync(options, committedDefault, dictionary, cancellationToken).ConfigureAwait(false);
                    dataset = maintenance is null
                        ? await MutableSparqlDataset
                            .ResumeAsync(dictionary, committedDefault, mergedNamed, journal.AppendDelegate, journal.ReadDelegate, journal.Head, options.ValueIndexes, cancellationToken)
                            .ConfigureAwait(false)
                        : await MutableSparqlDataset
                            .ResumeAsync(dictionary, committedDefault, [.. maintenance.InitialState.ServedAdditions], ReasoningProvenance.From(maintenance.InitialState), mergedNamed, journal.AppendDelegate, journal.ReadDelegate, journal.Head, options.ValueIndexes, cancellationToken)
                            .ConfigureAwait(false);
                    recovery = new DatasetJournalRecoveryProvenance(replay.EntriesReplayed, journal.RecoveryReport, journal.CommitmentFindings);
                }
            }

            WireMaintenance(dataset, maintenance, refuseInconsistent);

            //Seed the reconciliation feed from the final committed default triples and subscribe the delta
            //observer, mirroring the create path: the sketch maintainer advances the feed AND folds the same
            //committed delta into the maintained encoder under one gate. With a host identity, the ledger comes
            //from the fresh-create baseline, or from the recovered causality artifact with the annotated journal
            //folded over it in sequence order and the StateId cross-check at the end; a store without a
            //loadable, consistent causality source stays add-only until the explicit baseline step.
            ReplicationIndexFeed feed = new(committedDefault, default);
            DottedCommitLedger? ledger = null;
            bool causalityTraceSeen = false;
            if(options.ReplicaIdentity is { } replicaIdentity)
            {
                if(initialCausality is not null)
                {
                    ledger = new DottedCommitLedger(replicaIdentity, initialCausality, dataset.StateId);
                }
                else
                {
                    LedgerRecovery recovered = await RecoverLedgerAsync(replicaIdentity, load, ledgerRecoveryRead, dataset.StateId, cancellationToken).ConfigureAwait(false);
                    ledger = recovered.Ledger;
                    causalityTraceSeen = recovered.CausalityTraceSeen;
                }
            }

            //The fresh-create baseline's confirm fills the StateId and the dictionary epoch on the recorded
            //intent, now that both exist; a resume that minted nothing has nothing to confirm here.
            coordinationStanding = WorstStanding(coordinationStanding, await ConsultBaselineConfirmAsync(options, lineageDigest, dataset.StateId, unchecked((long)dictionary.Epoch), cancellationToken).ConfigureAwait(false));

            //The explicit baseline step: an operator-requested upgrade of a resumed store recovery proved
            //causality-free — an empty ledger is wired like any remove-aware store's and the baseline commits
            //below, after the observer wiring, so the ledger folds it through the one ordinary publish path.
            //The outcome is a VALUE on the engine, surfaced like recovery provenance — an expected condition is
            //never an exception: a store with a causality trace but no recoverable pair refuses the step and
            //serves in its awaiting-baseline standing, because a fresh baseline's counters could re-issue dots
            //that surviving history already names for other events — the silent corruption the refusal exists
            //to prevent. The remedy is operator-level (re-clone from a healthy remove-aware replica), never an
            //in-place re-baseline.
            ReplicationBaselineOutcome baselineOutcome = ReplicationBaselineOutcome.NotRequested;
            bool commitExplicitBaseline = false;
            if(options.BaselineReplicationCausality)
            {
                if(ledger is not null)
                {
                    baselineOutcome = ReplicationBaselineOutcome.AlreadyRemoveAware;
                }
                else if(causalityTraceSeen)
                {
                    baselineOutcome = ReplicationBaselineOutcome.RefusedCausalityTrace;
                }
                else if(options.ReplicaIdentity is { } baselineIdentity)
                {
                    ledger = new DottedCommitLedger(baselineIdentity, baseline: null, dataset.StateId);
                    commitExplicitBaseline = true;
                    baselineOutcome = ReplicationBaselineOutcome.Baselined;
                }
            }

            //The maintainer is owned here until the engine takes it: the coordinated explicit-baseline span
            //between its creation and the engine's construction can throw (a conflicting-lineage intent refuses
            //the open), so ownership rides a dedicated disposal local nulled at the transfer.
            IncrementalSketchMaintainer? sketchMaintainerToDispose = new(feed, bufferPool, IncrementalSketchMaintainerOptions.Default, dictionary.Epoch);
            try
            {
                IncrementalSketchMaintainer sketchMaintainer = sketchMaintainerToDispose;
                if(ledger is not null)
                {
                    ComposedCommittedDeltaObserver composedObserver = new(sketchMaintainer, ledger);
                    dataset.ObserveDefaultGraphDelta(composedObserver.OnDefaultGraphDelta);
                    dataset.RegisterCausalityBuilder(ledger.BuildLocalCausality);
                }
                else
                {
                    dataset.ObserveDefaultGraphDelta(sketchMaintainer.OnDefaultGraphDelta);
                }

                if(commitExplicitBaseline)
                {
                    //The explicit baseline coordinates exactly as a creation baseline does: the mint is hoisted
                    //here so its intent precedes the durable commit, and the confirm follows it with the same
                    //StateId the ledger was stamped with.
                    CommitCausality explicitBaseline = DottedCommitLedger.MintBaseline(ledger!.Identity, committedDefault);
                    (NodeIdentifier? explicitDigest, MetadataCoordinationStanding explicitIntentStanding) = await ConsultBaselineIntentAsync(options, explicitBaseline, bufferPool, cancellationToken).ConfigureAwait(false);
                    coordinationStanding = WorstStanding(coordinationStanding, explicitIntentStanding);
                    await CommitExplicitBaselineAsync(dataset, explicitBaseline, cancellationToken).ConfigureAwait(false);
                    coordinationStanding = WorstStanding(coordinationStanding, await ConsultBaselineConfirmAsync(options, explicitDigest, dataset.StateId, unchecked((long)dictionary.Epoch), cancellationToken).ConfigureAwait(false));
                }

                VeritasEngine engine = new(dataset, feed, sketchMaintainer, ledger, options.ReplicaIdentity is not null, baselineOutcome, bufferPool, BuildServiceClient(options), options.GraphSource, options.AccessControl, options.AnalyticsTrace, options.SparqlExecutionTrace, journal, termPool, recovery, options.Checksum, options.ResolveChecksum, options.SparqlExecution, options.ImplicitTimezone, options.SparqlUpdate, options.ValueDatatypes, options.ExtensionFunctions, metadataCoordination: coordinationStanding);
                sketchMaintainerToDispose = null;
                bufferPool = null;
                termPool = null;
                journal = null;

                StartSelfHealIfConfigured(engine, store, options);

                return engine;
            }
            finally
            {
                sketchMaintainerToDispose?.Dispose();
            }
        }
        finally
        {
            defaultTriples?.Dispose();
            foreach((TermId _, DecodedItemSegment segment) in namedGraphSegments)
            {
                segment.Dispose();
            }

            triplePool.Dispose();
            DisposeJournal(journal);
            termPool?.Dispose();
            bufferPool?.Dispose();
        }
    }

    /// <summary>Materialises the recovered default-graph triples as a list, or an empty list when no generation supplied them.</summary>
    /// <param name="defaultTriples">The recovered default-graph segment, or <see langword="null"/>.</param>
    /// <returns>The default-graph triples.</returns>
    private static List<EncodedTriple> LoadedDefaultTriples(DecodedItemSegment? defaultTriples)
    {
        return defaultTriples is null ? [] : [.. MemoryMarshal.ToEnumerable<EncodedTriple>(defaultTriples.Memory)];
    }

    /// <summary>Materialises the recovered named graphs as a build map keyed by graph-name term id, or <see langword="null"/> when none were recovered.</summary>
    /// <param name="namedGraphSegments">The recovered named-graph segments.</param>
    /// <returns>The named graphs' triples keyed by graph-name term id, or <see langword="null"/> for none.</returns>
    private static Dictionary<TermId, IReadOnlyList<EncodedTriple>>? LoadedNamedGraphs(IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> namedGraphSegments)
    {
        if(namedGraphSegments.Count == 0)
        {
            return null;
        }

        Dictionary<TermId, IReadOnlyList<EncodedTriple>> named = new(namedGraphSegments.Count);
        foreach((TermId graphName, DecodedItemSegment segment) in namedGraphSegments)
        {
            named[graphName] = [.. MemoryMarshal.ToEnumerable<EncodedTriple>(segment.Memory)];
        }

        return named;
    }

    /// <summary>
    /// Merges a replay's folded content with the loaded base generation: the final default graph is the replay's
    /// touched default (when the suffix moved it) else the loaded default, and the named graphs are the loaded named
    /// graphs minus the dropped ones, overlaid with the replay's touched named graphs (which include graphs created
    /// after the anchor).
    /// </summary>
    /// <param name="replay">The replay result.</param>
    /// <param name="loadedDefault">The loaded default-graph segment, or <see langword="null"/>.</param>
    /// <param name="loadedNamed">The loaded named-graph segments.</param>
    /// <returns>The merged default-graph triples and named-graph build map.</returns>
    private static (List<EncodedTriple> Default, IReadOnlyDictionary<TermId, IReadOnlyList<EncodedTriple>> Named) MergeRecoveredContent(
        DatasetJournalReplayResult replay,
        DecodedItemSegment? loadedDefault,
        IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> loadedNamed)
    {
        List<EncodedTriple> mergedDefault = replay.TouchedGraphs.TryGetValue(TermId.None, out HashSet<EncodedTriple>? touchedDefault)
            ? [.. touchedDefault]
            : LoadedDefaultTriples(loadedDefault);

        HashSet<TermId> dropped = [.. replay.DroppedGraphs];
        Dictionary<TermId, IReadOnlyList<EncodedTriple>> mergedNamed = [];
        foreach((TermId graphName, DecodedItemSegment segment) in loadedNamed)
        {
            //A loaded graph the suffix dropped is gone; one the suffix touched is overlaid below (its touched set
            //already folds the base content in), so only the loaded-and-untouched graphs come straight from the base.
            if(dropped.Contains(graphName) || replay.TouchedGraphs.ContainsKey(graphName))
            {
                continue;
            }

            mergedNamed[graphName] = [.. MemoryMarshal.ToEnumerable<EncodedTriple>(segment.Memory)];
        }

        foreach((TermId graph, HashSet<EncodedTriple> content) in replay.TouchedGraphs)
        {
            if(graph == TermId.None)
            {
                continue;
            }

            mergedNamed[graph] = [.. content];
        }

        return (mergedDefault, mergedNamed);
    }

    /// <summary>
    /// Serves a recovered base generation's per-graph content to a durable dataset-journal replay: the default graph
    /// under <see cref="TermId.None"/>, named graphs by their term id, and <see langword="null"/> for a graph the
    /// generation did not hold. Carries the recovered segments as explicit state so the resolver is a bound method
    /// group rather than a lambda closing over them.
    /// </summary>
    /// <param name="defaultTriples">The recovered default-graph segment, or <see langword="null"/> when no generation loaded.</param>
    /// <param name="namedGraphSegments">The recovered named-graph segments.</param>
    private sealed class RecoveredBaseContent
    {
        /// <summary>The recovered default-graph segment, or <see langword="null"/>.</summary>
        private DecodedItemSegment? DefaultTriples { get; }

        /// <summary>The recovered named-graph segments keyed by graph-name term id.</summary>
        private Dictionary<uint, DecodedItemSegment> NamedByTermId { get; }

        /// <summary>Constructs the resolver over the recovered segments.</summary>
        /// <param name="defaultTriples">The recovered default-graph segment, or <see langword="null"/>.</param>
        /// <param name="namedGraphSegments">The recovered named-graph segments.</param>
        public RecoveredBaseContent(DecodedItemSegment? defaultTriples, IReadOnlyList<(TermId GraphName, DecodedItemSegment Triples)> namedGraphSegments)
        {
            DefaultTriples = defaultTriples;
            NamedByTermId = new Dictionary<uint, DecodedItemSegment>(namedGraphSegments.Count);
            foreach((TermId graphName, DecodedItemSegment segment) in namedGraphSegments)
            {
                NamedByTermId[graphName.Encoded] = segment;
            }
        }

        /// <summary>Resolves a graph's base-generation content, or <see langword="null"/> when the generation did not hold it.</summary>
        /// <param name="graph">The graph-name term id, or <see cref="TermId.None"/> for the default graph.</param>
        /// <returns>The graph's base triples, or <see langword="null"/>.</returns>
        public IEnumerable<EncodedTriple>? Resolve(TermId graph)
        {
            if(graph == TermId.None)
            {
                return DefaultTriples is null ? null : MemoryMarshal.ToEnumerable<EncodedTriple>(DefaultTriples.Memory);
            }

            return NamedByTermId.TryGetValue(graph.Encoded, out DecodedItemSegment? segment)
                ? MemoryMarshal.ToEnumerable<EncodedTriple>(segment.Memory)
                : null;
        }
    }

    /// <summary>The term dictionary the database's graphs are encoded against — the shared epoch an intra-cluster peer's reconciliation sketch must share. Encoding and resolving terms against it is how a caller lines a peer sketch (or query) up with the served data.</summary>
    public TermDictionary Dictionary => mutableDataset is not null ? mutableDataset.Dictionary : queryEngine!.Dictionary;

    /// <summary>
    /// Attempts to obtain a read-only graph-analytics view over the default graph's current triples, for the
    /// graph-algorithm surfaces — the synchronous null-policy fast path. Reuses the query engine's columnar index
    /// when it is built and delta-free, so the analytics is near-free — it pays nothing for the index a columnar
    /// query already materialised — and otherwise builds a delta-free index from the system of record. Refuses
    /// (returns <see langword="false"/>) when an access-control policy is configured: graph analytics read the order
    /// columns directly and so bypass the per-triple authorization the query path enforces, and the access-scoped
    /// view that filters them to the caller's authorized triples is built asynchronously — use
    /// <see cref="TryGetDefaultGraphAnalyticsAsync"/> under a policy.
    /// </summary>
    /// <param name="analytics">On success, the analytics view over the default graph.</param>
    /// <param name="dictionary">On success, the dictionary that decodes analytics result term ids back to RDF terms.</param>
    /// <returns><see langword="true"/> when a view is available; <see langword="false"/> when a policy is configured or the index is not materialised.</returns>
    public bool TryGetDefaultGraphAnalytics([NotNullWhen(true)] out ColumnarGraphAnalytics? analytics, [NotNullWhen(true)] out TermDictionary? dictionary)
    {
        analytics = null;
        dictionary = null;

        if(accessControl is not null)
        {
            return false;
        }

        ColumnarTripleIndex? view = DefaultGraphRendezvous.TryGetAnalyticsView();
        if(view is null)
        {
            return false;
        }

        analytics = new ColumnarGraphAnalytics(view);
        dictionary = Dictionary;

        return true;
    }

    /// <summary>
    /// Obtains a read-only graph-analytics view over the default graph's current triples for the graph-algorithm
    /// surfaces, honouring a configured access-control policy. With no policy this is the near-free fast path: the
    /// query engine's own columnar index when it is built and delta-free — paying nothing for an index a columnar
    /// query already materialised — else a freshly built delta-free index from the system of record. With a policy
    /// configured the view is built <em>filtered</em> to <paramref name="accessContext"/>: every triple is put to the
    /// policy and only the ones it answers <see cref="AccessDecision.Allow"/> enter the index, so the algorithms —
    /// which read the order columns directly, bypassing the per-triple authorization the query path enforces —
    /// compute over only what the caller may see, and no triple a policy would hide can leak through analytics. This
    /// is the access-scoped successor to <see cref="TryGetDefaultGraphAnalytics"/>, which serves only the null-policy
    /// case synchronously and refuses under a policy.
    /// </summary>
    /// <param name="accessContext">The caller's access context the policy decides against; required (non-<see langword="null"/>) when a policy is configured, ignored when none is.</param>
    /// <param name="cancellationToken">A token that aborts the filtered build.</param>
    /// <returns>The analytics view and the dictionary that decodes its result ids, or <see langword="null"/> when the system of record is not materialised (deferred residency before the first build).</returns>
    public async ValueTask<(ColumnarGraphAnalytics Analytics, TermDictionary Dictionary)?> TryGetDefaultGraphAnalyticsAsync(AccessContext? accessContext, CancellationToken cancellationToken)
    {
        QueryEngineRendezvous rendezvous = DefaultGraphRendezvous;

        if(accessControl is null)
        {
            //No policy: the near-free path — reuse the serving view when delta-free, else build delta-free.
            ColumnarTripleIndex? view = rendezvous.TryGetAnalyticsView();

            return view is null ? null : (new ColumnarGraphAnalytics(view), Dictionary);
        }

        //A policy is configured: build the index filtered to what the caller may see, so analytics that read the
        //order columns directly never observe a denied triple.
        ColumnarTripleIndex? filtered = await rendezvous
            .BuildFilteredAnalyticsViewAsync(accessControl, accessContext, cancellationToken)
            .ConfigureAwait(false);

        return filtered is null ? null : (new ColumnarGraphAnalytics(filtered), Dictionary);
    }

    /// <summary>The default graph's query-engine rendezvous — the mutable database's, or the immutable query engine's — the analytics seams acquire their delta-free index from.</summary>
    private QueryEngineRendezvous DefaultGraphRendezvous => mutableDataset is not null
        ? mutableDataset.DefaultGraphRendezvous
        : queryEngine!.Dataset.DefaultGraphRendezvous;

    /// <summary>Composes the configured SERVICE client with the in-process analytics endpoints, so an analytics SERVICE is answered locally and every other endpoint falls through to <paramref name="inner"/>.</summary>
    /// <param name="inner">The configured outbound SERVICE client, or <see langword="null"/> when none is wired.</param>
    /// <returns>The composed client.</returns>
    private SparqlClient WrapServiceClientWithAnalytics(SparqlClient? inner)
    {
        return new SparqlClient(new AnalyticsServiceTransport(this, inner).DispatchAsync);
    }

    /// <summary>
    /// Runs a graph-analytics <c>SERVICE</c> over the default graph: looks the algorithm up in the catalog, obtains
    /// the access-scoped analytics view for <paramref name="accessContext"/> (filtered to the caller's authorized
    /// triples when a policy is configured), and renders the algorithm's rows as the SERVICE result set. Throws when
    /// the algorithm is unknown or the analytics view is unavailable (the system of record is not materialised) — a
    /// non-silent SERVICE surfaces the error, a SILENT one contributes nothing.
    /// </summary>
    /// <param name="algorithm">The algorithm name parsed from the endpoint IRI.</param>
    /// <param name="parameters">The <c>name=value</c> parameters parsed from the endpoint IRI's query string.</param>
    /// <param name="accessContext">The caller's access context, scoping the analytics index to authorized triples under a policy.</param>
    /// <param name="cancellationToken">A token that aborts the run.</param>
    /// <returns>The algorithm's result set.</returns>
    /// <exception cref="NotSupportedException">The algorithm is unknown, or the analytics view is unavailable.</exception>
    private async ValueTask<SparqlResultSet> RunDefaultGraphAnalyticsServiceAsync(string algorithm, IReadOnlyList<string> parameters, AccessContext? accessContext, CancellationToken cancellationToken)
    {
        if(!GraphAnalyticsCatalog.TryGet(algorithm, out GraphAnalyticsDescriptor descriptor))
        {
            throw new NotSupportedException($"Unknown analytics algorithm '{algorithm}'.");
        }

        (ColumnarGraphAnalytics Analytics, TermDictionary Dictionary)? acquired = await TryGetDefaultGraphAnalyticsAsync(accessContext, cancellationToken).ConfigureAwait(false);
        if(acquired is null)
        {
            throw new NotSupportedException("The default-graph analytics view is unavailable (the system of record is not materialised).");
        }

        AnalyticsContext context = new(acquired.Value.Analytics, acquired.Value.Dictionary, new AnalyticsParameters(parameters), cancellationToken);

        return AnalyticsRunner.Run(descriptor, context, analyticsTrace);
    }

    /// <summary>
    /// The SERVICE transport that answers the in-process analytics endpoints and falls through to the configured
    /// outbound client for every other endpoint. Holds the engine and the inner client as state so the transport
    /// is a bound method group rather than a lambda capturing them.
    /// </summary>
    /// <param name="engine">The engine whose default graph the analytics run over.</param>
    /// <param name="inner">The configured outbound SERVICE client, or <see langword="null"/>.</param>
    private sealed class AnalyticsServiceTransport(VeritasEngine engine, SparqlClient? inner)
    {
        /// <summary>The engine whose default graph the analytics run over.</summary>
        private VeritasEngine Engine { get; } = engine;

        /// <summary>The configured outbound SERVICE client, or <see langword="null"/>.</summary>
        private SparqlClient? Inner { get; } = inner;

        /// <summary>Answers an analytics endpoint locally, or forwards to the inner client; matches <see cref="SparqlServiceTransport"/>.</summary>
        /// <param name="endpoint">The SERVICE endpoint IRI.</param>
        /// <param name="query">The rendered inner-pattern query, unused for an analytics endpoint (the algorithm fixes the columns).</param>
        /// <param name="accessContext">The caller's access context: scopes the analytics view to authorized triples for an analytics endpoint, and is forwarded to the inner client for any other.</param>
        /// <param name="cancellationToken">A token that aborts the call.</param>
        /// <returns>The result set.</returns>
        /// <exception cref="NotSupportedException">A non-analytics endpoint with no inner client to forward to.</exception>
        public ValueTask<SparqlResultSet> DispatchAsync(IriRef endpoint, string query, AccessContext? accessContext, CancellationToken cancellationToken)
        {
            if(GraphAnalyticsServices.TryParseEndpoint(endpoint.Value, out string algorithm, out IReadOnlyList<string> parameters))
            {
                return Engine.RunDefaultGraphAnalyticsServiceAsync(algorithm, parameters, accessContext, cancellationToken);
            }

            return Inner is not null
                ? Inner.QueryAsync(endpoint, query, accessContext, cancellationToken)
                : throw new NotSupportedException("SERVICE requires a SparqlClient transport, but the engine was constructed without one.");
        }
    }

    /// <summary>Mints a unique, non-zero replication epoch for a mutable database's dictionary from the sanctioned entropy seam, so two independently opened databases carry distinct epochs and a structural reconcile between mismatched dictionaries is detectable.</summary>
    /// <returns>A non-zero dictionary epoch.</returns>
    private static ulong MintReplicationEpoch()
    {
        RandomnessValue value = VeritasRandomness.System(new RandomnessRequest(RandomnessKind.Bytes, default, sizeof(ulong), default));
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(value.Bytes.Span);

        //0 is the unspecified epoch (a non-replicating dictionary), so a minted epoch is forced non-zero.
        return epoch == 0 ? 1UL : epoch;
    }

    /// <summary>Builds the SERVICE client from the configured transport, or <see langword="null"/> when no transport is configured.</summary>
    /// <param name="options">The engine options carrying the optional SERVICE transport.</param>
    /// <returns>The SERVICE client, or <see langword="null"/>.</returns>
    private static SparqlClient? BuildServiceClient(VeritasEngineOptions options)
    {
        return options.ServiceTransport is { } transport ? new SparqlClient(transport) : null;
    }

    /// <summary>
    /// Evaluates a SPARQL query and returns its result — SELECT bindings, an
    /// ASK boolean, or a CONSTRUCT/DESCRIBE graph — discriminated by the
    /// query's form, so a transport renders it without parsing the query to
    /// learn the form.
    /// </summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="baseIri">The base IRI relative references in the query resolve against; <c>null</c> for none.</param>
    /// <param name="accessContext">The opaque "who is asking" context for this read — handed to the access-control policy and forwarded to any <c>SERVICE</c>/<c>FROM</c> IO; <c>null</c> for none.</param>
    /// <param name="protocolDataset">A dataset description supplied OUTSIDE the query text (the SPARQL Protocol's <c>default-graph-uri</c>/<c>named-graph-uri</c> parameters); non-<c>null</c> replaces the query's own <c>FROM</c>/<c>FROM NAMED</c> clause per the protocol's precedence rule, <c>null</c> leaves the query's clause in force.</param>
    /// <param name="world">The registered world this read is scoped to, or <c>null</c> for the primary world (the only world an immutable database serves).</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns>The query result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, is a SPARQL Update rather than a query, or names a world that is not registered.</exception>
    /// <exception cref="InvalidOperationException">A world was named on an immutable database.</exception>
    /// <exception cref="UnknownGraphSourceException">The effective dataset clause names a graph the engine's store-local graph source cannot serve (no such loaded named graph) and no <see cref="VeritasEngineOptions.GraphSource"/> resolver was configured.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<VeritasQueryResult> QueryAsync(Utf8String sparql, Utf8String? baseIri = null, AccessContext? accessContext = null, DatasetClause? protocolDataset = null, string? world = null, CancellationToken cancellationToken = default)
    {
        using Utf8StringPool pool = new();
        SparqlQuery query = Parse(sparql, baseIri, pool);
        SparqlQuery normalized = (SparqlQuery)new SparqlNormalizer(pool).Normalize(query);
        AlgebraOperator algebra = SparqlTranslator.Translate(normalized, EngineExtensionFunctions.AggregateIris);
        SparqlQueryEngine engine = ResolveEngine(accessContext, ResolveWorldOrPrimary(world));

        //A dataset clause resolves through the configured graph-source seam when one is wired; otherwise the
        //engine's own store-local source serves the loaded named graphs (and refuses anything else by name), so
        //FROM / FROM NAMED and the protocol dataset work out of the box without ever fetching over a network.
        DatasetClause dataset = protocolDataset ?? query.Dataset;
        engine = await engine.WithDatasetAsync(dataset, graphSource ?? new DatasetGraphSource(engine.Dataset, engine.Dictionary).ResolveAsync, cancellationToken).ConfigureAwait(false);

        if(normalized.Form is AskQuery)
        {
            return VeritasQueryResult.ForAsk(await engine.EvaluateAskAsync(algebra, cancellationToken).ConfigureAwait(false));
        }

        //The normalized form carries the CONSTRUCT template with its standalone nodes already lowered to
        //plain triple patterns, which is the shape the instantiation machinery consumes.
        if(normalized.Form is ConstructQuery construct)
        {
            IReadOnlyList<SparqlSolution> constructSolutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

            return VeritasQueryResult.ForGraph(SparqlGraphConstruction.Construct(construct.Template, constructSolutions));
        }

        if(normalized.Form is DescribeQuery describe)
        {
            return VeritasQueryResult.ForGraph(await DescribeResultAsync(engine, describe, algebra, cancellationToken).ConfigureAwait(false));
        }

        IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);

        return VeritasQueryResult.ForSelect(SparqlResultSet.ForSelect(SelectVariables(query), solutions));
    }

    /// <summary>
    /// Answers a DESCRIBE query's result graph: the description of the form's explicit IRI targets plus,
    /// when the form names variables or is <c>DESCRIBE *</c>, the IRI/blank-node/engine-node values those
    /// variables bind across the evaluated WHERE solutions (a star form takes every bound variable;
    /// literals describe nothing and are filtered). The WHERE pattern is evaluated only when a variable
    /// target or the star form needs its bindings.
    /// </summary>
    /// <param name="engine">The engine, already scoped to the query's effective dataset.</param>
    /// <param name="describe">The normalized DESCRIBE form.</param>
    /// <param name="algebra">The query's translated WHERE algebra.</param>
    /// <param name="cancellationToken">A token that aborts evaluation and the description walk.</param>
    /// <returns>The description graph, as default-graph quads.</returns>
    private static async ValueTask<IReadOnlyList<Quad>> DescribeResultAsync(SparqlQueryEngine engine, DescribeQuery describe, AlgebraOperator algebra, CancellationToken cancellationToken)
    {
        List<RdfTerm> resources = [];
        HashSet<RdfTerm> seen = [];
        bool wantsBindings = describe.IsStar;
        foreach(DescribeTarget target in describe.Targets)
        {
            if(target is DescribeIri iri)
            {
                NamedNode node = new(iri.Iri.Value);
                if(seen.Add(node))
                {
                    resources.Add(node);
                }
            }
            else
            {
                wantsBindings = true;
            }
        }

        if(wantsBindings)
        {
            IReadOnlyList<SparqlSolution> solutions = await engine.EvaluateAsync(algebra, cancellationToken).ConfigureAwait(false);
            foreach(SparqlSolution solution in solutions)
            {
                foreach(SparqlBinding binding in solution.Bindings)
                {
                    if(!describe.IsStar && !IsDescribedVariable(describe, binding.Variable))
                    {
                        continue;
                    }

                    if(binding.Value is NamedNode or BlankNode or EngineNode && seen.Add(binding.Value))
                    {
                        resources.Add(binding.Value);
                    }
                }
            }
        }

        return await engine.DescribeAsync(resources, strategy: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether a DESCRIBE form's explicit target list names <paramref name="variable"/>.</summary>
    /// <param name="describe">The DESCRIBE form.</param>
    /// <param name="variable">The candidate variable.</param>
    /// <returns><see langword="true"/> when the variable is a describe target.</returns>
    private static bool IsDescribedVariable(DescribeQuery describe, SparqlVariable variable)
    {
        foreach(DescribeTarget target in describe.Targets)
        {
            if(target is DescribeVariable candidate && candidate.Variable == variable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Evaluates a SPARQL <c>ASK</c> query and returns whether a solution exists; a convenience over <see cref="QueryAsync"/>.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="accessContext">The opaque "who is asking" context for this read; <c>null</c> for none.</param>
    /// <param name="world">The registered world this read is scoped to, or <c>null</c> for the primary world.</param>
    /// <param name="cancellationToken">A token that aborts evaluation.</param>
    /// <returns><see langword="true"/> when the query has at least one solution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, is a SPARQL Update, is not an <c>ASK</c> query, or names a world that is not registered.</exception>
    /// <exception cref="InvalidOperationException">A world was named on an immutable database.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<bool> AskAsync(Utf8String sparql, AccessContext? accessContext = null, string? world = null, CancellationToken cancellationToken = default)
    {
        VeritasQueryResult result = await QueryAsync(sparql, accessContext: accessContext, world: world, cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.Boolean ?? throw new ArgumentException("The query is not an ASK query.", nameof(sparql));
    }

    /// <summary>
    /// Streams a SELECT query's solutions for incremental consumption (server-sent events, paging). The returned
    /// <see cref="VeritasSelectStream"/> carries the head variables (for the result header) and an
    /// <see cref="IAsyncEnumerable{T}"/> that yields solutions as they are produced — truly incremental for the
    /// common shape (a basic graph pattern, optionally projected and/or limited), and otherwise answered through the
    /// materialized path with an identical result set. Dispose the returned stream once enumerated (a <c>using</c>
    /// around the enumeration); it holds the query's parse resources until then.
    /// </summary>
    /// <param name="sparql">The SELECT query text.</param>
    /// <param name="baseIri">The base IRI relative references resolve against; <see langword="null"/> for none.</param>
    /// <param name="accessContext">The opaque "who is asking" context consulted per candidate triple by the access-control policy; <see langword="null"/> for none.</param>
    /// <param name="world">The registered world this read is scoped to, or <see langword="null"/> for the primary world.</param>
    /// <param name="cancellationToken">A token that aborts streaming.</param>
    /// <returns>The streaming SELECT result.</returns>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, is a SPARQL Update, is not a SELECT query, or names a world that is not registered.</exception>
    /// <exception cref="InvalidOperationException">A world was named on an immutable database.</exception>
    public async ValueTask<VeritasSelectStream> StreamSelectAsync(Utf8String sparql, Utf8String? baseIri = null, AccessContext? accessContext = null, string? world = null, CancellationToken cancellationToken = default)
    {
        Utf8StringPool pool = new();
        try
        {
            SparqlQuery query = Parse(sparql, baseIri, pool);
            if(query.Form is not SelectQuery)
            {
                throw new ArgumentException("StreamSelectAsync handles SELECT queries; use AskAsync for ASK, and CONSTRUCT/DESCRIBE are not streamed.", nameof(sparql));
            }

            AlgebraOperator algebra = SparqlTranslator.Translate((SparqlQuery)new SparqlNormalizer(pool).Normalize(query), EngineExtensionFunctions.AggregateIris);
            IReadOnlyList<Utf8String> variables = SelectVariables(query);
            SparqlQueryEngine engine = ResolveEngine(accessContext, ResolveWorldOrPrimary(world));
            engine = await engine.WithDatasetAsync(query.Dataset, graphSource ?? new DatasetGraphSource(engine.Dataset, engine.Dictionary).ResolveAsync, cancellationToken).ConfigureAwait(false);

            return new VeritasSelectStream(variables, engine.EvaluateStreamingAsync(algebra, cancellationToken), pool);
        }
        catch
        {
            pool.Dispose();

            throw;
        }
    }

    /// <summary>The number of times an update execution is re-attempted when a concurrent committer advances the journal head, before the concurrency exception propagates.</summary>
    private const int MaxUpdateAttempts = 16;

    /// <summary>
    /// Executes a SPARQL Update, committing its mutation into the database's
    /// mutable dataset; a subsequent query sees the change (read-your-writes).
    /// Only a mutable database accepts updates. A concurrent committer is
    /// retried a bounded number of times against the advanced journal head.
    /// </summary>
    /// <param name="sparql">The update text.</param>
    /// <param name="baseIri">The base IRI relative references in the update resolve against; <c>null</c> for none.</param>
    /// <param name="accessContext">The opaque "who is asking" context — handed to the access-control policy and forwarded to any <c>SERVICE</c> (in a WHERE) or <c>LOAD</c> IO; <c>null</c> for none.</param>
    /// <param name="world">The registered world this update commits into, or <c>null</c> for the primary world. A fork's commits ride its own in-memory journal and never touch the primary world or the durable journal.</param>
    /// <param name="cancellationToken">A token that aborts execution.</param>
    /// <returns>A task that completes when the update has committed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sparql"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, is a SPARQL query rather than an update, or names a world that is not registered.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened with <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> rather than <see cref="OpenMutableAsync"/>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask UpdateAsync(Utf8String sparql, Utf8String? baseIri = null, AccessContext? accessContext = null, string? world = null, CancellationToken cancellationToken = default)
    {
        if(mutableDataset is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) accepts updates; this database is immutable.");
        }

        MutableSparqlDataset target = ResolveWorldOrPrimary(world) ?? mutableDataset;

        using Utf8StringPool pool = new();
        SparqlUpdateRequest request = ParseUpdate(sparql, baseIri, pool);
        SparqlExpressionContext context = SparqlExpressionContext.CreateDefault(implicitTimezone: EngineImplicitTimezone, valueDatatypes: EngineValueDatatypes, extensionFunctions: EngineExtensionFunctions);

        for(int attempt = 1; ; attempt++)
        {
            try
            {
                await SparqlUpdateExecutor.ExecuteAsync(request, target, context, graphSource: graphSource, serviceClient: serviceClient, accessControl: accessControl, accessContext: accessContext, enginePolicy: sparqlPolicy, updateOptions: sparqlUpdateOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

                return;
            }
            catch(EditSessionConcurrencyException) when(attempt < MaxUpdateAttempts)
            {
                //A concurrent committer advanced the journal head between this update's read and its append. The
                //mutation stack is linearised through one journal, so re-running against the new head converges in
                //a bounded number of attempts; on exhaustion the concurrency exception propagates to the caller.
            }
        }
    }

    /// <summary>
    /// Validates the served default graph against a SHACL shapes graph and
    /// returns the report. The shapes are loaded over the same dictionary the
    /// data uses, so a term denotes the same thing in both; when reasoning is
    /// wired the validation runs over the entailed graph the database serves.
    /// </summary>
    /// <param name="shapes">The SHACL shapes graph as triples.</param>
    /// <param name="world">The registered world whose data is validated, or <c>null</c> for the primary world.</param>
    /// <param name="cancellationToken">A token that aborts validation.</param>
    /// <returns>The validation report — whether the data conforms and, if not, the results.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="shapes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="world"/> names a world that is not registered.</exception>
    /// <exception cref="InvalidOperationException">A world was named on an immutable database.</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<ValidationReport> ValidateAsync(IEnumerable<DataTriple> shapes, string? world = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shapes);

        SparqlQueryEngine engine = ResolveEngine(accessContext: null, ResolveWorldOrPrimary(world));
        TermDictionary dictionary = engine.Dictionary;
        HypertrieGraphStore shapesStore = await BuildShapesStore(shapes, dictionary, cancellationToken).ConfigureAwait(false);
        ShapeRegistry registry = await ShapeLoader
            .LoadAsync(shapesStore.AsMatchDelegate(), dictionary, ShaclBuiltInComponents.All, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        //Validation scans the whole default graph, which needs the trie's Match ops; under deferred residency that
        //materialises the trie on demand.
        HypertrieGraphStore defaultGraph = await engine.Dataset.RequireDefaultGraphAsync(cancellationToken).ConfigureAwait(false);

        //The validation's sh:sparql constraint engine follows this database's engine-wide executor strategy and
        //carries the same extension-function registry the query paths do; sh:datatype evaluation consults the
        //same value-layer datatype registry.
        ShaclValidatorOptions validatorOptions = ShaclValidatorOptions.Default with { SparqlExecution = sparqlPolicy, ValueDatatypes = EngineValueDatatypes, ExtensionFunctions = EngineExtensionFunctions };

        return await ShaclValidator
            .ValidateAsync(registry, defaultGraph.AsMatchOps(), dictionary, ShaclBuiltInEvaluators.All, TimeProvider.System, options: validatorOptions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The names of this database's registered worlds. A mutable database always carries at least the
    /// primary world (<see cref="WellKnownWorlds.Primary"/>); an immutable database carries none.
    /// </summary>
    public IReadOnlyCollection<string> WorldNames => worlds?.Names ?? [];

    /// <summary>
    /// Describes this database's registered worlds — each world's name, its current content-addressed
    /// state identifier, and its fork parent's name — the listing a worlds wire or picker presents.
    /// The primary world (<see cref="WellKnownWorlds.Primary"/>) comes first and the remaining worlds
    /// follow in ordinal name order, so the listing is stable across calls whatever order the registry
    /// snapshots in. An immutable database carries no worlds and answers empty, the same harmless read
    /// <see cref="WorldNames"/> gives it.
    /// </summary>
    /// <returns>The registered worlds, primary first; empty on an immutable database.</returns>
    public ImmutableArray<WorldDescriptor> DescribeWorlds()
    {
        if(worlds is null)
        {
            return [];
        }

        ImmutableArray<DatasetWorldEntry> entries = worlds.Describe();
        WorldDescriptor[] descriptors = new WorldDescriptor[entries.Length];
        for(int i = 0; i < entries.Length; i++)
        {
            descriptors[i] = new WorldDescriptor(entries[i].Name, entries[i].World.StateId, entries[i].Parent);
        }

        Array.Sort(descriptors, CompareWorldDescriptors);

        return ImmutableCollectionsMarshal.AsImmutableArray(descriptors);
    }

    /// <summary>Orders world descriptors for presentation: the primary world first, then ordinal name order.</summary>
    /// <param name="left">The first descriptor.</param>
    /// <param name="right">The second descriptor.</param>
    /// <returns>The ordering of <paramref name="left"/> relative to <paramref name="right"/>.</returns>
    private static int CompareWorldDescriptors(WorldDescriptor left, WorldDescriptor right)
    {
        if(string.Equals(left.Name, right.Name, StringComparison.Ordinal))
        {
            return 0;
        }

        if(string.Equals(left.Name, WellKnownWorlds.Primary, StringComparison.Ordinal))
        {
            return -1;
        }

        if(string.Equals(right.Name, WellKnownWorlds.Primary, StringComparison.Ordinal))
        {
            return 1;
        }

        return string.CompareOrdinal(left.Name, right.Name);
    }

    /// <summary>
    /// Forks a world's current committed state into a new world registered under
    /// <paramref name="newWorld"/> — the start of the what-if flow: fork, apply a hypothetical in the
    /// fork, query and diff the consequence in isolation, drop or keep it. Forking copies nothing; the
    /// worlds share the term dictionary and node arena, and only divergence allocates. The fork keeps
    /// its own in-memory journal, so it accepts committed updates but never touches the durable journal
    /// the primary world rides. An unknown source and a taken name are expected conditions and answer
    /// as outcomes.
    /// </summary>
    /// <param name="sourceWorld">The world to fork from.</param>
    /// <param name="newWorld">The new world's name.</param>
    /// <param name="cancellationToken">A token that aborts the fork.</param>
    /// <returns>The fork outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sourceWorld"/> or <paramref name="newWorld"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened with <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> rather than <see cref="OpenMutableAsync"/>).</exception>
    public async ValueTask<WorldForkOutcome> ForkWorldAsync(string sourceWorld, string newWorld, CancellationToken cancellationToken = default)
    {
        WorldFork fork = await RequireWorlds().TryForkAsync(sourceWorld, newWorld, cancellationToken).ConfigureAwait(false);

        return fork.Outcome;
    }

    /// <summary>
    /// Removes a world's name from the registry. Existing holders keep the world usable — an execution
    /// racing the drop completes on the departed world — and once unreferenced, the world's unshared
    /// roots become sweepable through the arena's weak registries. The primary world is never
    /// droppable; an unknown name is an expected condition and answers as an outcome.
    /// </summary>
    /// <param name="world">The world's name.</param>
    /// <returns>The drop outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened with <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> rather than <see cref="OpenMutableAsync"/>).</exception>
    public WorldDropOutcome DropWorld(string world)
    {
        ArgumentNullException.ThrowIfNull(world);

        DatasetWorlds registry = RequireWorlds();
        if(string.Equals(world, WellKnownWorlds.Primary, StringComparison.Ordinal))
        {
            return WorldDropOutcome.PrimaryWorld;
        }

        return registry.Drop(world) ? WorldDropOutcome.Dropped : WorldDropOutcome.UnknownWorld;
    }

    /// <summary>
    /// Computes the net per-graph transitions that carry <paramref name="baseWorld"/>'s current
    /// committed state to <paramref name="world"/>'s, decoded to terms through the shared dictionary —
    /// the consequence readout of the what-if flow. Content-based over the shared arena, so the result
    /// is exact regardless of how the two histories diverged; an unknown name on either side is an
    /// expected condition and answers as an outcome.
    /// </summary>
    /// <param name="world">The world whose state the transitions produce.</param>
    /// <param name="baseWorld">The world the transitions start from.</param>
    /// <returns>The diff outcome, carrying the decoded transitions on <see cref="WorldDiffOutcome.Diffed"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="world"/> or <paramref name="baseWorld"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened with <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> rather than <see cref="OpenMutableAsync"/>).</exception>
    public WorldDiff DiffWorlds(string world, string baseWorld)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(baseWorld);

        DatasetWorlds registry = RequireWorlds();
        if(!registry.TryGet(world, out MutableSparqlDataset? diffed) || diffed is null)
        {
            return new WorldDiff(WorldDiffOutcome.UnknownWorld, []);
        }

        if(!registry.TryGet(baseWorld, out MutableSparqlDataset? baseline) || baseline is null)
        {
            return new WorldDiff(WorldDiffOutcome.UnknownWorld, []);
        }

        return new WorldDiff(WorldDiffOutcome.Diffed, DecodeTransitions(diffed.DiffFrom(baseline), diffed.Dictionary));
    }

    /// <summary>Answers the world registry, which only a mutable database carries.</summary>
    /// <returns>The registry.</returns>
    /// <exception cref="InvalidOperationException">This is an immutable database.</exception>
    private DatasetWorlds RequireWorlds() =>
        worlds ?? throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) carries worlds; this database is immutable.");

    /// <summary>
    /// Resolves a world name to its dataset for a world-scoped execution: <see langword="null"/> in
    /// answers <see langword="null"/> out (the caller keeps its primary path), and a named world must
    /// exist — naming a world an execution believes present is a contract the caller carries, unlike
    /// the racing management operations, which answer outcomes.
    /// </summary>
    /// <param name="world">The world's name, or <see langword="null"/> for the primary path.</param>
    /// <returns>The named world's dataset, or <see langword="null"/> when no world was named.</returns>
    /// <exception cref="ArgumentException">No world is registered under <paramref name="world"/>.</exception>
    /// <exception cref="InvalidOperationException">A world was named on an immutable database.</exception>
    private MutableSparqlDataset? ResolveWorldOrPrimary(string? world)
    {
        if(world is null)
        {
            return null;
        }

        if(!RequireWorlds().TryGet(world, out MutableSparqlDataset? dataset) || dataset is null)
        {
            throw new ArgumentException($"No world is registered under the name '{world}'.", nameof(world));
        }

        return dataset;
    }

    /// <summary>Decodes per-graph transitions to terms through the shared dictionary.</summary>
    /// <param name="transitions">The encoded transitions.</param>
    /// <param name="dictionary">The dictionary the triples were encoded into.</param>
    /// <returns>The decoded transitions, in the input order.</returns>
    private static ImmutableArray<WorldGraphTransition> DecodeTransitions(ImmutableArray<DatasetGraphTransition> transitions, TermDictionary dictionary)
    {
        ImmutableArray<WorldGraphTransition>.Builder decoded = ImmutableArray.CreateBuilder<WorldGraphTransition>(transitions.Length);
        foreach(DatasetGraphTransition transition in transitions)
        {
            RdfTerm? graph = transition.Graph == TermId.None ? null : dictionary.Resolve(transition.Graph);
            decoded.Add(new WorldGraphTransition(graph, DecodeTriples(transition.Additions, dictionary), DecodeTriples(transition.Removals, dictionary)));
        }

        return decoded.MoveToImmutable();
    }

    /// <summary>Decodes encoded triples to term triples through the shared dictionary.</summary>
    /// <param name="triples">The encoded triples.</param>
    /// <param name="dictionary">The dictionary the triples were encoded into.</param>
    /// <returns>The decoded triples, in the input order.</returns>
    private static List<DataTriple> DecodeTriples(ImmutableArray<EncodedTriple> triples, TermDictionary dictionary)
    {
        List<DataTriple> decoded = new(triples.Length);
        foreach(EncodedTriple triple in triples)
        {
            decoded.Add(new DataTriple(dictionary.Resolve(triple.Subject), dictionary.Resolve(triple.Predicate), dictionary.Resolve(triple.Object)));
        }

        return decoded;
    }

    /// <summary>
    /// Reconciles this mutable database from one peer: it runs a bounded anti-entropy
    /// loop against the peer's sketch fetch, and on convergence writes the recovered
    /// delta back through the dataset journal — so a converged reconcile becomes an
    /// ordinary committed mutation a subsequent query sees and that re-advances the
    /// reconciliation feed. The peer is a sketch-fetch delegate (the transport seam),
    /// so this carries no network dependency. Additions-only: the union of both
    /// replicas converges across rounds and peers; removals do not propagate through
    /// anti-entropy in this slice.
    /// <para>
    /// <b>Intra-cluster only — guarded by the dictionary epoch.</b> The structural sketch carries term IDENTIFIERS,
    /// not terms, and recovered identifiers are committed verbatim, so a peer that numbered its terms under a
    /// different <see cref="Dictionary"/> would silently corrupt. The caller passes the peer's advertised dictionary
    /// epoch (<paramref name="peerEpoch"/>, the peer's <see cref="Dictionary"/>.<see cref="TermDictionary.Epoch"/>);
    /// if it does not match this database's, the reconcile is refused before it begins
    /// (<see cref="PeerReconcileOutcome.PeerEpochMismatch"/>, nothing reconciled or written). Two independently
    /// opened databases carry distinct minted epochs, so structural reconcile across them is correctly refused; it
    /// succeeds only within a shared-dictionary cluster (same epoch). For peers that numbered their terms
    /// independently — the cross-organisation case — use the content-hash reconcile domain, which transfers terms.
    /// </para>
    /// </summary>
    /// <param name="peerFetch">The peer's sketch fetch — the transport seam each round drives.</param>
    /// <param name="peerEpoch">The peer's advertised dictionary epoch; must equal this database's <see cref="Dictionary"/>.<see cref="TermDictionary.Epoch"/> or the reconcile is refused.</param>
    /// <param name="policy">The budget shape both sides' sketches are sized under.</param>
    /// <param name="maxRounds">The maximum number of reconcile rounds to run; positive.</param>
    /// <param name="cancellationToken">A token that aborts the reconcile.</param>
    /// <returns>The reconcile outcome: whether it converged, the rounds run, how the delta was written back, and whether it was refused for an epoch mismatch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="peerFetch"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxRounds"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened with <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> rather than <see cref="OpenMutableAsync"/>).</exception>
    public async ValueTask<PeerReconcileOutcome> ReconcileFromPeerAsync(AsyncSketchFetchDelegate peerFetch, ulong peerEpoch, ReplicationPolicy policy, int maxRounds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peerFetch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRounds);
        if(mutableDataset is null || replicationFeed is null || replicationPool is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) reconciles from a peer; this database is immutable.");
        }

        if(peerEpoch != mutableDataset.Dictionary.Epoch)
        {
            //The peer numbered its terms under a different dictionary; its recovered identifiers would denote
            //different local terms, so the reconcile is refused before it begins rather than committing corruption.
            return new PeerReconcileOutcome(Converged: false, Rounds: 0, WriteBackOutcome.NoOp, PeerEpochMismatch: true, AntiEntropyOutcome.PeerEpochMismatch);
        }

        ReplicationGeneration generation = replicationFeed.Current();
        ReplicaReconcileResult result = await ReplicaReconcileLoop
            .RunUntilConvergedAsync(generation.Index, mutableDataset.Dictionary.Epoch, peerFetch, policy, replicationPool, TimeProvider.System, maxRounds, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if(!result.Converged)
        {
            return new PeerReconcileOutcome(Converged: false, result.Rounds, WriteBackOutcome.NoOp, PeerEpochMismatch: false, result.LastOutcome);
        }

        WriteBackOutcome writeBack = await ReconcileWriteBack
            .ApplyAsync(mutableDataset, result.RecoveredAdditions, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new PeerReconcileOutcome(Converged: true, result.Rounds, writeBack, PeerEpochMismatch: false, result.LastOutcome);
    }

    /// <summary>
    /// Produces a sketch-fetch delegate that serves this mutable database's own
    /// structural sketch over its current reconciliation generation — so this node
    /// is a peer another node reconciles FROM: one node's fetch from here is its
    /// <see cref="ReconcileFromPeerAsync"/> peer fetch. Each fetch reflects the
    /// latest committed default graph. The structural sketch carries term
    /// identifiers, so a consuming node must share this database's dictionary epoch
    /// (an intra-cluster peer); the cross-organisation case is the content-hash
    /// reconcile domain.
    /// </summary>
    /// <returns>The sketch-fetch delegate serving this node's structural sketch.</returns>
    /// <remarks>The delegate borrows this database's owned replication pool, so it must not be invoked after the database is disposed; a fetch on a disposed database's delegate throws <see cref="ObjectDisposedException"/> from the pool. Bind it for the database's lifetime, not beyond.</remarks>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened with <see cref="OpenAsync(IEnumerable{DataTriple}, VeritasEngineOptions?, CancellationToken)"/> rather than <see cref="OpenMutableAsync"/>).</exception>
    public AsyncSketchFetchDelegate CreateSketchFetch()
    {
        if(mutableDataset is null || sketchMaintainer is null || replicationPool is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) serves a sketch; this database is immutable.");
        }

        //The served image is stamped with this database's dictionary epoch, so a consuming node's session refuses a
        //cross-epoch peer at the wire — the same guard ReconcileFromPeerAsync applies to an inbound sketch. The
        //serve reads the maintained encoder's symbol prefix, not a whole-set re-projection, byte-identically.
        return new StructuralSketchSource(sketchMaintainer, replicationPool).FetchAsync;
    }

    /// <summary>
    /// Ingests a stream of default-graph triples into this MUTABLE database as ONE journalled commit: each quad's
    /// terms are encoded into the shared dictionary as the stream is drained (the ingest parse is the dictionary's
    /// minting seam), and the encoded triples commit through the same journal, query store, replication feed, and
    /// maintained sketch a SPARQL update flows through — so a subsequent query, serve, or reconcile sees them.
    /// Already-present triples are filtered by the edit session, so re-ingesting a document converges.
    /// </summary>
    /// <param name="quads">The quads to ingest, enumerated exactly once; every quad must be a default-graph triple (a <see langword="null"/> <see cref="Quad.Graph"/>).</param>
    /// <param name="cancellationToken">A token that aborts the ingest, observed at each quad and through the commit.</param>
    /// <returns>The receipt: the number of triples the stream submitted and how the journalled write-back landed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="quads"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidDataException">A quad carries a named graph — this surface ingests the replicated default graph only.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    /// <exception cref="OperationCanceledException">Cancelled via <paramref name="cancellationToken"/>.</exception>
    public async ValueTask<IngestReceipt> IngestAsync(IAsyncEnumerable<Quad> quads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quads);
        if(mutableDataset is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) ingests; this database is immutable.");
        }

        TermDictionary dictionary = mutableDataset.Dictionary;
        List<EncodedTriple> encoded = [];
        await foreach(Quad quad in quads.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if(quad.Graph is not null)
            {
                throw new InvalidDataException("The ingest surface commits the replicated default graph only; a named-graph quad is refused.");
            }

            encoded.Add(new EncodedTriple(dictionary.GetOrAdd(quad.Subject), dictionary.GetOrAdd(quad.Predicate), dictionary.GetOrAdd(quad.Object)));
        }

        WriteBackOutcome outcome = await ReconcileWriteBack
            .ApplyAsync(mutableDataset, encoded.ToArray(), cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new IngestReceipt(encoded.Count, outcome);
    }

    /// <summary>
    /// Serves this mutable database's structural sketch to ONE peer connection over a duplex pipe pair: budget
    /// requests are read from <paramref name="requestReader"/> and each is answered with the maintained encoder's
    /// stamped image on <paramref name="responseWriter"/>, until the requesting side completes. The wire-serving
    /// sibling of <see cref="CreateSketchFetch"/>: a host binds a listener and serves each accepted sketch
    /// connection through this call, so this node is a peer another PROCESS reconciles from.
    /// </summary>
    /// <param name="requestReader">The pipe budget requests are read from.</param>
    /// <param name="responseWriter">The pipe stamped sketch images are written to.</param>
    /// <param name="cancellationToken">A token that cancels the serve.</param>
    /// <returns>A task that completes when the requesting side ends and the response side is completed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestReader"/> or <paramref name="responseWriter"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    public Task ServeSketchChannelAsync(PipeReader requestReader, PipeWriter responseWriter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);
        if(mutableDataset is null || sketchMaintainer is null || replicationPool is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) serves a sketch; this database is immutable.");
        }

        return new SketchChannelServer(sketchMaintainer, replicationPool, requestReader, responseWriter, mutableDataset.Dictionary.Epoch).ServeAsync(cancellationToken);
    }

    /// <summary>
    /// Produces the serve-snapshot seam a shard-difference server reads: each invocation projects the CURRENT
    /// committed default graph's triples into their structural reconciliation keys — one fresh snapshot per serve,
    /// so a long-lived endpoint always serves its latest committed set. Bind it into a
    /// <see cref="ShardDifferenceChannelServer"/> beside this database's <see cref="Dictionary"/> epoch so this
    /// node's shards are peer-repair sources for another process.
    /// </summary>
    /// <returns>The snapshot provider over this database's committed reconciliation index.</returns>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    public ProvideShardServeSnapshotDelegate CreateShardServeSnapshotProvider()
    {
        if(replicationFeed is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) serves shard snapshots; this database is immutable.");
        }

        return new CommittedStructuralSnapshot(replicationFeed).Provide;
    }

    /// <summary>
    /// Reconciles this REMOVE-AWARE mutable database with one peer over the dotted-difference channel: one
    /// bidirectional remove-aware session over a pinned ledger snapshot, through which retractions propagate as
    /// drops, tombstones answer pushes as push-drops instead of resurrecting, and concurrent net additions
    /// survive under add-wins — every applied step landing as a durable, causality-annotated commit whose
    /// commit-time guard re-validates against the live ledger, so an interrupted exchange leaves a consistent
    /// prefix and a re-run converges. The outcome is a VALUE naming exactly how the exchange ended; falling
    /// back to the add-only lane against a peer that refuses is an operator choice
    /// (<see cref="ReconcileFromPeerAsync"/> remains), never an automatic downgrade.
    /// </summary>
    /// <remarks>
    /// The dotted wire exchanges only crash-durable causal history: a store without a durable dataset journal
    /// refuses by name (<see cref="DottedReconcileOutcomeKind.LocalNotDurable"/>), because a crash could lose
    /// minted dots a peer already covers and a reopen would re-mint those counters for other events — the
    /// two-events-under-one-dot corruption no local check can detect once re-minting passes the lost maximum.
    /// A store without a host replica identity, or awaiting the explicit baseline step, refuses as
    /// <see cref="DottedReconcileOutcomeKind.LocalNotRemoveAware"/>.
    /// </remarks>
    /// <param name="openPeerConnection">The seam that opens one fresh duplex connection to the peer's dotted-difference endpoint.</param>
    /// <param name="symbolCap">The symbol ceiling that bounds a non-terminating decode into an abort; positive.</param>
    /// <param name="trace">The diagnostics sink dotted fault events are emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="cancellationToken">Cancels the exchange; propagates as itself.</param>
    /// <returns>The exchange's value outcome, with the committed and transferred counts.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="openPeerConnection"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="symbolCap"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    public async ValueTask<DottedReconcileOutcome> ReconcileRemoveAwareFromPeerAsync(OpenPeerDottedConnectionDelegate openPeerConnection, int symbolCap, TraceHandler<DottedDifferenceFaultEvent>? trace = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openPeerConnection);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(symbolCap);
        if(mutableDataset is null || replicationPool is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) reconciles from a peer; this database is immutable.");
        }

        if(commitLedger is not { } ledger)
        {
            return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.LocalNotRemoveAware);
        }

        if(datasetJournal is null)
        {
            return DottedReconcileOutcome.ForKind(DottedReconcileOutcomeKind.LocalNotDurable);
        }

        DottedLedgerProjection projection = new(ledger.Snapshot(), replicationPool);
        DottedReconcileBinding binding = new(mutableDataset, ledger, projection);

        //Only a wired handler pays for a correlation id, minted per reconcile so an operator can tie every
        //fault event to the exchange that drove it; the untraced path constructs none.
        Guid correlationId = trace is not null ? VeritasIdentifiers.System(new IdentifierRequest(IdentifierPurpose.Correlation, default)) : default;
        DottedDifferenceChannelClient client = new(openPeerConnection, mutableDataset.Dictionary.Epoch, ledger.Identity, ledger.ReadOwnAxisMaximum, TimeProvider.System, trace, correlationId);
        DottedReconcileOutcome outcome = await client
            .ExchangeAsync(projection, symbolCap, binding.ResolveDifference, binding.ApplyElementsAsync, binding.ApplyDropsAsync, binding.MergeContextAsync, replicationPool, cancellationToken)
            .ConfigureAwait(false);

        return outcome with
        {
            AdoptedAdditions = binding.AdoptedAdditions,
            AdoptedDrops = binding.AdoptedDrops,
            PushedEntries = binding.PushedEntries,
            PushedDropDots = binding.PushedDropDots,
        };
    }

    /// <summary>
    /// Serves one dotted-difference exchange to a peer connection over a duplex pipe pair — the wire-serving
    /// sibling of <see cref="ReconcileRemoveAwareFromPeerAsync"/>: a host binds a listener and serves each
    /// accepted dotted connection through this call. A database that is not remove-aware, or keeps no durable
    /// dataset journal, still answers — with the reply header's NAMED decline
    /// (<see cref="DottedDifferenceDeclineReason.NotRemoveAware"/> or
    /// <see cref="DottedDifferenceDeclineReason.NotDurable"/>) — so the requesting operator sees a reason,
    /// never a silent close. An accepted serve applies the initiator's pushes and drops as durable,
    /// causality-annotated commits through the same guarded adopt path the initiating side uses.
    /// </summary>
    /// <param name="requestReader">The pipe request frames are read from.</param>
    /// <param name="responseWriter">The pipe response frames are written to.</param>
    /// <param name="trace">The diagnostics sink dotted fault events are emitted to; <see langword="null"/> emits nothing.</param>
    /// <param name="cancellationToken">Cancels the serve.</param>
    /// <returns>A task that completes when the connection's exchange ends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestReader"/> or <paramref name="responseWriter"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    public Task ServeDottedDifferenceAsync(PipeReader requestReader, PipeWriter responseWriter, TraceHandler<DottedDifferenceFaultEvent>? trace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestReader);
        ArgumentNullException.ThrowIfNull(responseWriter);
        if(mutableDataset is null || replicationPool is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) serves the dotted-difference channel; this database is immutable.");
        }

        if(commitLedger is not { } ledger)
        {
            DottedDifferenceChannelServer declining = new(mutableDataset.Dictionary.Epoch, DottedDifferenceDeclineReason.NotRemoveAware, replicationPool, TimeProvider.System);

            return declining.ServeAsync(requestReader, responseWriter, cancellationToken);
        }

        if(datasetJournal is null)
        {
            DottedDifferenceChannelServer notDurable = new(mutableDataset.Dictionary.Epoch, DottedDifferenceDeclineReason.NotDurable, replicationPool, TimeProvider.System);

            return notDurable.ServeAsync(requestReader, responseWriter, cancellationToken);
        }

        DottedServeBindingFactory factory = new(mutableDataset, ledger, replicationPool);

        //Only a wired handler pays for a correlation id, minted per serve so an operator can tie every fault
        //event to the connection that raised it; the untraced path constructs none.
        Guid correlationId = trace is not null ? VeritasIdentifiers.System(new IdentifierRequest(IdentifierPurpose.Correlation, default)) : default;
        DottedDifferenceChannelServer server = new(mutableDataset.Dictionary.Epoch, factory.Provide, ledger.Identity, ledger.ReadOwnAxisMaximum, replicationPool, TimeProvider.System, trace, correlationId);

        return server.ServeAsync(requestReader, responseWriter, cancellationToken);
    }

    /// <summary>
    /// Builds one pinned serve binding per accepted dotted serve — a fresh ledger snapshot, its projection, and
    /// the adopt seams bound to exactly that snapshot instant — carrying the dataset, ledger, and pool as
    /// explicit state so the provider seam is a bound method group rather than a lambda closing over them.
    /// </summary>
    /// <param name="dataset">The mutable dataset the serve's adopt commits land in.</param>
    /// <param name="ledger">The dotted commit ledger the serve snapshots and guards against.</param>
    /// <param name="pool">The pool the projection's framing scratch is rented from.</param>
    private sealed class DottedServeBindingFactory(MutableSparqlDataset dataset, DottedCommitLedger ledger, VeritasMemoryPool<byte> pool)
    {
        /// <summary>The mutable dataset the serve's adopt commits land in.</summary>
        private MutableSparqlDataset Dataset { get; } = dataset;

        /// <summary>The dotted commit ledger the serve snapshots and guards against.</summary>
        private DottedCommitLedger Ledger { get; } = ledger;

        /// <summary>The pool the projection's framing scratch is rented from.</summary>
        private VeritasMemoryPool<byte> Pool { get; } = pool;

        /// <summary>Supplies one pinned serve binding.</summary>
        /// <returns>The binding over a fresh ledger snapshot.</returns>
        public DottedDifferenceServeBinding Provide()
        {
            DottedLedgerProjection projection = new(Ledger.Snapshot(), Pool);
            DottedReconcileBinding binding = new(Dataset, Ledger, projection);

            return new DottedDifferenceServeBinding(projection, binding.ApplyElementsAsync, binding.ApplyDropsAsync, binding.MergeContextAsync);
        }
    }

    /// <summary>
    /// Reads this mutable database's replication-facing state: the committed default-graph triple count, the
    /// dictionary epoch and term count, the maintained sketch generation, and the remove-aware standing — the
    /// causality state and the dotted commit ledger's fold generation. Each field is read atomically but the
    /// reads are not one transaction — a commit racing the read may land between fields; a host that needs exact
    /// agreement quiesces writes first, as an operational status surface expects.
    /// </summary>
    /// <returns>The status snapshot.</returns>
    /// <exception cref="InvalidOperationException">This is an immutable database (opened over data or a store rather than with <see cref="OpenMutableAsync"/>).</exception>
    public VeritasReplicationStatus ReadReplicationStatus()
    {
        if(mutableDataset is null || replicationFeed is null || sketchMaintainer is null)
        {
            throw new InvalidOperationException("Only a mutable database (opened with OpenMutableAsync) reports replication status; this database is immutable.");
        }

        ReplicationCausalityState causalityState = (commitLedger, replicaIdentitySupplied) switch
        {
            (not null, _) => ReplicationCausalityState.RemoveAware,
            (null, true) => ReplicationCausalityState.AwaitingBaseline,
            (null, false) => ReplicationCausalityState.AddOnly
        };

        return new VeritasReplicationStatus(
            replicationFeed.Current().Index.TripleCount,
            mutableDataset.Dictionary.Epoch,
            sketchMaintainer.Generation,
            mutableDataset.Dictionary.Count,
            causalityState,
            commitLedger?.Generation ?? 0);
    }

    /// <summary>
    /// Projects the committed reconciliation index into structural keys per serve, carrying the feed as explicit
    /// state so the snapshot seam is a bound method group rather than a lambda closing over it.
    /// </summary>
    /// <param name="feed">The reconciliation feed whose current committed index each serve projects.</param>
    private sealed class CommittedStructuralSnapshot(ReplicationIndexFeed feed)
    {
        /// <summary>The reconciliation feed whose current committed index each serve projects.</summary>
        private ReplicationIndexFeed Feed { get; } = feed;

        /// <summary>Projects the current committed set into sixteen-byte structural keys, all slices of one backing array — one fresh snapshot per call. The concrete list return binds covariantly to the snapshot delegate's read-only view.</summary>
        /// <returns>One projected key per committed default-graph triple.</returns>
        /// <exception cref="InvalidOperationException">The committed set holds more triples than a single projected-key buffer can address.</exception>
        public List<ReadOnlyMemory<byte>> Provide()
        {
            ColumnarTripleIndex index = Feed.Current().Index;
            int count = index.TripleCount;
            long backingByteCount = (long)count * ContentKey128.ByteWidth;
            if(backingByteCount > Array.MaxLength)
            {
                throw new InvalidOperationException("The committed set holds more triples than a single projected-key buffer can address.");
            }

            byte[] backing = new byte[Math.Max(1, (int)backingByteCount)];
            List<ReadOnlyMemory<byte>> keys = new(count);
            int offset = 0;
            foreach(EncodedTriple triple in index.EnumerateTriples())
            {
                StructuralReconciliationProjection.Project(triple).WriteBytes(backing.AsSpan(offset, ContentKey128.ByteWidth));
                keys.Add(new ReadOnlyMemory<byte>(backing, offset, ContentKey128.ByteWidth));
                offset += ContentKey128.ByteWidth;
            }

            return keys;
        }
    }

    /// <summary>Stops the background self-heal loop (cancel and join, swallowing only its cancellation) and disposes the resources it owns, then drains the owned compute lane and disposes the owned replication/buffer pool, the owned durable dataset journal, the owned term pool (store- or journal-recovered), and — for a deferred store-opened database — the owned triple pool; an immutable in-memory database owns no pool and a mutable one owns no lane.</summary>
    /// <returns>A task that completes when the self-heal loop has stopped and the lane has drained.</returns>
    public async ValueTask DisposeAsync()
    {
        //The self-heal loop reads and re-publishes into the store through the pools it was lent; it is stopped and
        //joined (its bounded cancellation swallowed) before the rest of the teardown, so no round is in flight when
        //its pools are returned.
        if(ActiveSelfHeal is { } selfHeal)
        {
            await selfHeal.DisposeAsync().ConfigureAwait(false);
        }

        //The maintainer's delta observer runs on the committing thread and throws once disposed, so it is disposed
        //only during teardown, when the host has ceased committing into the dataset — and strictly BEFORE the
        //replication pool its encoder's rentals return to.
        sketchMaintainer?.Dispose();

        //The journal only closes its write handle; it reads no term memory on dispose, so ordering against the term
        //pool is immaterial. It is disposed before the pools all the same, through the platform-neutral seam.
        DisposeJournal(datasetJournal);
        replicationPool?.Dispose();
        ownedTermPool?.Dispose();
        ownedTriplePool?.Dispose();

        if(computeLane is not null)
        {
            await computeLane.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Validates a store-backed open's self-heal configuration before any resource is acquired, so a misconfiguration fails the open loudly and early rather than after the recovery pools and lane are built. Does nothing when no self-heal is wired.</summary>
    /// <param name="options">The engine options carrying the optional self-heal policy.</param>
    /// <exception cref="ArgumentOutOfRangeException">The self-heal jitter fraction is outside [0, 1).</exception>
    private static void EnsureSelfHealConfigurationValid(VeritasEngineOptions options)
    {
        if(options.SelfHeal is { } selfHeal)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(selfHeal.JitterFraction);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selfHeal.JitterFraction, 1.0);
        }
    }

    /// <summary>Starts the background self-heal loop over a store-opened database when a self-heal policy is configured, attaching the running loop and its owned resources to the database; a no-op when none is configured.</summary>
    /// <param name="engine">The freshly opened store-backed database the loop is attached to.</param>
    /// <param name="store">The store the loop verifies, repairs, and re-publishes.</param>
    /// <param name="options">The engine options carrying the self-heal policy and the storage-trace seam.</param>
    private static void StartSelfHealIfConfigured(VeritasEngine engine, PersistenceStore store, VeritasEngineOptions options)
    {
        if(options.SelfHeal is { } selfHeal)
        {
            engine.StartSelfHeal(store, selfHeal, options.StorageTrace, options.Clock, options.Checksum, options.ResolveChecksum);
        }
    }

    /// <summary>
    /// Builds and starts the background storage self-heal service over a store: it wires the production repair
    /// configuration (the selected checksum algorithm, the structural sketch and reconciliation seams, owned pools),
    /// the commit coordinator dependencies, the shared commit mutex, and the storage-trace sink, then starts the loop
    /// on a background task the database owns. The database creates ONE commit mutex — the caller's when the policy
    /// supplied one, else a fresh one — and shares it as both the loop's commit-serialization lock and the lock its
    /// own <see cref="Persist"/> takes over this store, so a heal publish and a foreground persist never interleave.
    /// </summary>
    /// <param name="store">The store the loop verifies, repairs, and re-publishes.</param>
    /// <param name="selfHeal">The self-heal policy (cadence, jitter, round observation, and optional caller mutex).</param>
    /// <param name="storageTrace">The storage-trace sink the loop's events flow to, or <see langword="null"/>.</param>
    /// <param name="clock">The clock the loop schedules its round delays on (from <see cref="VeritasEngineOptions.Clock"/>).</param>
    /// <param name="checksum">The checksum algorithm the heal verifies and re-writes the store's artifacts under (from <see cref="VeritasEngineOptions.Checksum"/>), or <see langword="null"/> for the built-in <see cref="ChecksumAlgorithm.XxHash3"/> — it must match the algorithm the store was written under, so a keyed store heals under its key.</param>
    /// <param name="resolveChecksum">The resolver the heal reads the store's artifacts through (from <see cref="VeritasEngineOptions.ResolveChecksum"/>), or <see langword="null"/> for the built-in resolver.</param>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The service, its cancellation source, and the two pools are transferred to the returned SelfHealRuntime, which the engine's DisposeAsync disposes; the finally returns whatever ownership this start did not transfer (the pools and cancellation are nulled on success), and the service ctor cannot throw after the early self-heal validation.")]
    private void StartSelfHeal(PersistenceStore store, SelfHealOptions selfHeal, TraceHandler<StorageTraceEvent>? storageTrace, TimeProvider clock, ChecksumAlgorithm? checksum = null, ResolveChecksumAlgorithmDelegate? resolveChecksum = null)
    {
        //The pools back the repair configuration and the commit coordinator for the loop's whole lifetime, so the
        //database owns them and disposes them only after the loop is joined; the finally returns whatever ownership
        //this start did not transfer to the runtime (nulled on success).
        VeritasMemoryPool<byte>? bytePool = new();
        VeritasMemoryPool<EncodedTriple>? triplePool = new();
        CancellationTokenSource? cancellation = null;
        try
        {
            RepairConfiguration configuration = new(
                checksum ?? ChecksumAlgorithm.XxHash3,
                bytePool,
                triplePool,
                SketchContract.Structural,
                GenerationSketchSymbolBudget,
                StructuralReconciliationProjection.Projection,
                new RatelessSketchCodec(bytePool).Encode);

            //One mutex serializes both the loop's heal publish and this database's Persist over the store: the
            //caller's when the policy supplied one (they may share it with their own writer), else a fresh one.
            Lock commitMutex = selfHeal.CommitMutex ?? new Lock();
            SelfHealOptions effective = selfHeal.CommitMutex is null ? selfHeal with { CommitMutex = commitMutex } : selfHeal;

            StorageSelfHealService service = new(
                store,
                configuration,
                checksum ?? ChecksumAlgorithm.XxHash3,
                bytePool,
                SelfHealRetainedGenerationCount,
                resolveChecksum: resolveChecksum,
                storageTrace,
                clock,
                effective);

            cancellation = new CancellationTokenSource();
            Task loop = service.RunAsync(cancellation.Token);
            ActiveSelfHeal = new SelfHealRuntime(store, commitMutex, service, cancellation, loop, bytePool, triplePool);
            bytePool = null;
            triplePool = null;
            cancellation = null;
        }
        finally
        {
            cancellation?.Dispose();
            triplePool?.Dispose();
            bytePool?.Dispose();
        }
    }

    /// <summary>
    /// The running background self-heal loop and the resources a store-opened database owns for it: the store it
    /// heals (the identity a <see cref="Persist"/> matches to serialize against the loop), the shared commit mutex,
    /// the service, the loop task and its cancellation, and the two pools the repair configuration and commit
    /// coordinator rent from. Disposing it stops the loop (cancel and bounded join, swallowing only the
    /// cancellation) and returns the pools it owns.
    /// </summary>
    /// <param name="store">The store the loop heals; a foreground persist over this same instance serializes under <paramref name="commitMutex"/>.</param>
    /// <param name="commitMutex">The mutex serializing the loop's heal publish against a foreground persist.</param>
    /// <param name="service">The self-heal service driving the loop.</param>
    /// <param name="cancellation">The token source stopping the loop.</param>
    /// <param name="loop">The background loop task.</param>
    /// <param name="bytePool">The byte pool the repair configuration and commit coordinator rent from.</param>
    /// <param name="triplePool">The triple pool the repair feed rents from.</param>
    private sealed class SelfHealRuntime(
        PersistenceStore store,
        Lock commitMutex,
        StorageSelfHealService service,
        CancellationTokenSource cancellation,
        Task loop,
        VeritasMemoryPool<byte> bytePool,
        VeritasMemoryPool<EncodedTriple> triplePool): IAsyncDisposable
    {
        /// <summary>The store the loop heals; a foreground persist over this same instance serializes against the loop.</summary>
        public PersistenceStore Store { get; } = store;

        /// <summary>The mutex serializing the loop's heal publish against a foreground persist.</summary>
        public Lock CommitMutex { get; } = commitMutex;

        /// <summary>The self-heal service driving the loop.</summary>
        private StorageSelfHealService Service { get; } = service;

        /// <summary>The token source stopping the loop.</summary>
        private CancellationTokenSource Cancellation { get; } = cancellation;

        /// <summary>The background loop task.</summary>
        private Task Loop { get; } = loop;

        /// <summary>The byte pool the repair configuration and commit coordinator rent from.</summary>
        private VeritasMemoryPool<byte> BytePool { get; } = bytePool;

        /// <summary>The triple pool the repair feed rents from.</summary>
        private VeritasMemoryPool<EncodedTriple> TriplePool { get; } = triplePool;

        /// <summary>One once this runtime has been disposed; the second and later disposals are no-ops. A naked field because <see cref="Interlocked"/> takes it by reference.</summary>
        private int disposed;

        /// <summary>Stops the loop (cancel and join) and returns the pools it owns; a round in flight completes before the join returns, so no pool is returned under a live round. Idempotent, and the resources are returned however the loop ended: the expected stop is the loop's own cancellation, and any fault the loop died with was already recorded on the storage trace when it happened, so teardown completes rather than rethrowing it.</summary>
        /// <returns>A task that completes when the loop has stopped and the pools are returned.</returns>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Disposal must return the owned pools and complete the engine teardown however the loop ended; a loop fault was already recorded on the storage trace when it happened, and rethrowing it here would leak the pools and abort the rest of the engine's disposal.")]
        public async ValueTask DisposeAsync()
        {
            if(Interlocked.Exchange(ref disposed, 1) == 1)
            {
                return;
            }

            try
            {
                await Cancellation.CancelAsync().ConfigureAwait(false);
                await Loop.ConfigureAwait(false);
            }
            catch(OperationCanceledException)
            {
                //The bounded, expected stop: the loop cancels its own delay or its in-flight round completes and it
                //exits.
            }
            catch(Exception)
            {
                //The loop ended with a fault it already recorded on the storage trace; teardown still completes.
            }
            finally
            {
                Service.Dispose();
                Cancellation.Dispose();
                BytePool.Dispose();
                TriplePool.Dispose();
            }
        }
    }

    /// <summary>Resolves the query engine for one read, threading the federation and access seams plus this call's access context: a mutable database derives a fresh engine off its current dataset snapshot (read-your-writes) — the primary world's, or a named world's when one is passed; an immutable database returns its fixed engine, or a cheap per-call wrapper over the same dataset when a seam or access context applies.</summary>
    /// <param name="accessContext">The opaque "who is asking" context for this read, or <see langword="null"/>.</param>
    /// <param name="worldDataset">The named world's dataset this read is scoped to, or <see langword="null"/> for the primary path.</param>
    /// <returns>The query engine to evaluate against.</returns>
    private SparqlQueryEngine ResolveEngine(AccessContext? accessContext, MutableSparqlDataset? worldDataset = null)
    {
        MutableSparqlDataset? mutableSource = worldDataset ?? mutableDataset;
        if(mutableSource is not null)
        {
            return new SparqlQueryEngine(mutableSource.Snapshot(), mutableSource.Dictionary, expressionContext: SparqlExpressionContext.CreateDefault(implicitTimezone: EngineImplicitTimezone, valueDatatypes: EngineValueDatatypes, extensionFunctions: EngineExtensionFunctions), serviceClient: serviceClient, accessControl: accessControl, accessContext: accessContext, executionTrace: executionTrace, enginePolicy: sparqlPolicy);
        }

        //An immutable database answers off its fixed, already-reasoned engine; it rebuilds a per-call wrapper over
        //the same dataset only when a federation or access seam is in play (the wrapper is cheap — the dataset and
        //its built views are reused), so the common no-federation read keeps the fixed engine at zero cost.
        if(serviceClient is null && accessControl is null && accessContext is null)
        {
            return queryEngine!;
        }

        return new SparqlQueryEngine(queryEngine!.Dataset, queryEngine.Dictionary, expressionContext: SparqlExpressionContext.CreateDefault(implicitTimezone: EngineImplicitTimezone, valueDatatypes: EngineValueDatatypes, extensionFunctions: EngineExtensionFunctions), serviceClient: serviceClient, accessControl: accessControl, accessContext: accessContext, executionTrace: executionTrace, enginePolicy: sparqlPolicy);
    }

    /// <summary>Builds a store over the shapes triples, encoding them into the data's dictionary so shapes and data share term identity.</summary>
    /// <param name="shapes">The shapes triples.</param>
    /// <param name="dictionary">The shared dictionary.</param>
    /// <param name="cancellationToken">A token that aborts the build.</param>
    /// <returns>The shapes store.</returns>
    private static async ValueTask<HypertrieGraphStore> BuildShapesStore(IEnumerable<DataTriple> shapes, TermDictionary dictionary, CancellationToken cancellationToken)
    {
        List<EncodedTriple> encoded = [];
        foreach(DataTriple triple in shapes)
        {
            encoded.Add(new EncodedTriple(dictionary.GetOrAdd(triple.Subject), dictionary.GetOrAdd(triple.Predicate), dictionary.GetOrAdd(triple.Object)));
        }

        return await HypertrieGraphStore.BuildAsync(encoded, VeritasHashing.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Parses a query over the supplied pool, which must outlive the evaluation that reads the algebra's interned terms.</summary>
    /// <param name="sparql">The query text.</param>
    /// <param name="baseIri">The base IRI relative references resolve against, or <c>null</c> for none.</param>
    /// <param name="pool">The pool the query parses into.</param>
    /// <returns>The parsed query.</returns>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL Update rather than a query.</exception>
    private static SparqlQuery Parse(Utf8String sparql, Utf8String? baseIri, Utf8StringPool pool)
    {
        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(sparql.Memory, pool, baseIri);
        if(parsed.HasErrors)
        {
            throw new ArgumentException($"The query did not parse: {DescribeFirstError(parsed.Diagnostics)}", nameof(sparql));
        }

        if(parsed.Tree is not SparqlQuery query)
        {
            throw new ArgumentException("The request is a SPARQL Update, not a query.", nameof(sparql));
        }

        return query;
    }

    /// <summary>Parses and normalizes an update over the supplied pool, which must outlive the execution that reads the request's interned terms.</summary>
    /// <param name="sparql">The update text.</param>
    /// <param name="baseIri">The base IRI relative references resolve against, or <c>null</c> for none.</param>
    /// <param name="pool">The pool the update parses into.</param>
    /// <returns>The parsed, normalized update request.</returns>
    /// <exception cref="ArgumentException"><paramref name="sparql"/> does not parse, or is a SPARQL query rather than an update.</exception>
    private static SparqlUpdateRequest ParseUpdate(Utf8String sparql, Utf8String? baseIri, Utf8StringPool pool)
    {
        ParseResult<SparqlRequest> parsed = SparqlParser.ParseRequest(sparql.Memory, pool, baseIri);
        if(parsed.HasErrors)
        {
            throw new ArgumentException($"The update did not parse: {DescribeFirstError(parsed.Diagnostics)}", nameof(sparql));
        }

        if(parsed.Tree is not SparqlUpdateRequest)
        {
            throw new ArgumentException("The request is a SPARQL query, not an update.", nameof(sparql));
        }

        return (SparqlUpdateRequest)new SparqlNormalizer(pool).Normalize(parsed.Tree);
    }

    /// <summary>Collects a <c>SELECT</c> query's projected variable names in head order — the result columns.</summary>
    /// <param name="query">The query.</param>
    /// <returns>The projected variable names, empty for a non-<c>SELECT</c> query.</returns>
    private static List<Utf8String> SelectVariables(SparqlQuery query)
    {
        List<Utf8String> variables = [];
        if(query.Form is not SelectQuery select)
        {
            return variables;
        }

        foreach(SelectProjection projection in select.Projections)
        {
            Utf8String? name = projection switch
            {
                SelectVariable variable => variable.Variable.Name,
                SelectExpressionAs expressionAs => expressionAs.AsVariable.Name,
                _ => null,
            };

            if(name is { } variableName)
            {
                variables.Add(variableName);
            }
        }

        return variables;
    }

    /// <summary>
    /// Builds the engine's reasoning seam from a configuration: the three-tier
    /// composition <c>ElCoupled(ContextSaturation(SatBacked))</c> behind a
    /// rendezvous, materialising its post-commit store. A module wholly within
    /// the EL⊥ fragment is decided by the EL fast path; a module the EL arm
    /// declines but the Horn-ALCHI survey admits is decided by consequence-based
    /// context saturation; every other module — and any context module whose
    /// saturation exhausts the inference budget — is decided by the SAT-backed
    /// oracle. The consistency bit is identical to the SAT-backed engine alone on
    /// every module; the two saturation tiers widen the decisive fragment (a
    /// beyond-ALC(H) module the context tier decides reads whole rather than
    /// fragment-relative) and add the fast path for the modules that dominate real
    /// workloads.
    /// </summary>
    /// <param name="configuration">The reasoning configuration.</param>
    /// <param name="options">The engine options carrying the reasoning trace seams the materialisation emits on.</param>
    /// <returns>The materialisation the engine applies at build time and reads the outcome off afterwards.</returns>
    private static ReasoningMaterialization CreateReasoning(ReasoningConfiguration configuration, VeritasEngineOptions options)
    {
        DescriptionLogicDelegate descriptionLogic = ReasoningEngines.ElCoupled(
            configuration.Datatypes,
            ReasoningEngines.ContextSaturation(
                configuration.Datatypes,
                configuration.Budget,
                ReasoningEngines.SatBacked(configuration.Datatypes, configuration.Budget, configuration.SearchMode)));
        ReasoningRendezvous rendezvous = new(configuration.Policy, descriptionLogic);

        return new ReasoningMaterialization(rendezvous, options.ReasoningTrace, options.ReasoningDecisionTrace, configuration.RefuseInconsistent);
    }

    /// <summary>
    /// Builds the reasoned MUTABLE engine's maintenance object when reasoning is wired, or returns
    /// <see langword="null"/> when it is not — the per-open decision the create and reopen paths share.
    /// </summary>
    /// <param name="options">The engine options, whose <see cref="VeritasEngineOptions.Reasoning"/> is the wiring decision.</param>
    /// <param name="initialBase">The initial (or recovered) asserted default-graph base the closure is built over.</param>
    /// <param name="dictionary">The term dictionary the base and every later delta encode with.</param>
    /// <param name="cancellationToken">A token that aborts the build and open-time delegation.</param>
    /// <returns>The maintenance object, or <see langword="null"/> when reasoning is unwired.</returns>
    /// <exception cref="ReasoningInconsistencyException">Reasoning decided the initial base inconsistent and <see cref="ReasoningConfiguration.RefuseInconsistent"/> is set.</exception>
    private static async ValueTask<ReasoningMaintenance?> MaybeCreateReasoningMaintenanceAsync(
        VeritasEngineOptions options,
        IReadOnlyList<EncodedTriple> initialBase,
        TermDictionary dictionary,
        CancellationToken cancellationToken)
    {
        return options.Reasoning is { } configuration
            ? await CreateReasoningMaintenanceAsync(configuration, options, initialBase, dictionary, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <summary>
    /// Builds the reasoned mutable engine's <see cref="ReasoningMaintenance"/> over the initial asserted base
    /// (one remat), mirroring <see cref="CreateReasoning"/>'s three-tier
    /// <c>ElCoupled(ContextSaturation(SatBacked))</c> seam and the immutable
    /// lane's trace wiring: a clock and a per-open correlation id are minted only when a handler is wired, so the
    /// untraced path constructs neither. It honours <see cref="ReasoningConfiguration.RefuseInconsistent"/> AT
    /// OPEN — a decided-inconsistent open throws before the engine is returned, carrying the provenance the
    /// served database would otherwise have exposed; the open method's ownership-transfer finally frees the lane,
    /// journal, and pools, and the maintenance object holds no unmanaged state.
    /// </summary>
    /// <param name="configuration">The reasoning configuration (non-<see langword="null"/>).</param>
    /// <param name="options">The engine options carrying the three reasoning trace seams.</param>
    /// <param name="initialBase">The initial (or recovered) asserted default-graph base.</param>
    /// <param name="dictionary">The term dictionary the base and every later delta encode with.</param>
    /// <param name="cancellationToken">A token that aborts the build and open-time delegation.</param>
    /// <returns>The maintenance object, its <see cref="ReasoningMaintenance.InitialState"/> carrying the served seed and open verdict.</returns>
    /// <exception cref="ReasoningInconsistencyException">Reasoning decided the initial base inconsistent and <see cref="ReasoningConfiguration.RefuseInconsistent"/> is set.</exception>
    private static async ValueTask<ReasoningMaintenance> CreateReasoningMaintenanceAsync(
        ReasoningConfiguration configuration,
        VeritasEngineOptions options,
        IReadOnlyList<EncodedTriple> initialBase,
        TermDictionary dictionary,
        CancellationToken cancellationToken)
    {
        DescriptionLogicDelegate descriptionLogic = ReasoningEngines.ElCoupled(
            configuration.Datatypes,
            ReasoningEngines.ContextSaturation(
                configuration.Datatypes,
                configuration.Budget,
                ReasoningEngines.SatBacked(configuration.Datatypes, configuration.Budget, configuration.SearchMode)));

        //Only a wired handler pays for a clock and a correlation id, so the default path constructs neither and
        //the open-time build is byte-identical to an untraced materialisation. The correlation id is minted the
        //same way the immutable lane mints its per-run id.
        bool traced = options.ReasoningMaintenanceTrace is not null || options.ReasoningTrace is not null || options.ReasoningDecisionTrace is not null;
        TimeProvider? timeProvider = traced ? TimeProvider.System : null;
        Guid correlationId = traced ? VeritasIdentifiers.System(new IdentifierRequest(IdentifierPurpose.Correlation, default)) : default;

        ReasoningMaintenance maintenance = await ReasoningMaintenance.CreateAsync(
            initialBase,
            dictionary,
            configuration.Policy,
            descriptionLogic,
            options.ReasoningMaintenanceTrace,
            options.ReasoningTrace,
            options.ReasoningDecisionTrace,
            timeProvider,
            correlationId,
            cancellationToken).ConfigureAwait(false);

        //The per-commit veto's open-time counterpart: a decided inconsistency at open fails the open loudly with
        //the same provenance a served-and-surfaced open would have exposed (a fragment-relative CONSISTENT verdict
        //is not a decided inconsistency and does not refuse).
        if(configuration.RefuseInconsistent && !maintenance.InitialState.IsConsistent)
        {
            throw new ReasoningInconsistencyException(ReasoningProvenance.From(maintenance.InitialState));
        }

        return maintenance;
    }

    /// <summary>
    /// Registers the per-commit maintenance seam on a reasoned mutable dataset, binding
    /// <see cref="ReasoningMaintenance.MaintainCommit"/> and its single outcome notification through a
    /// <see cref="ReasoningMaintenanceBinding"/>; a no-op when reasoning is unwired (<paramref name="maintenance"/>
    /// is <see langword="null"/>), keeping an unwired mutable open byte-identical to before.
    /// </summary>
    /// <param name="dataset">The mutable dataset to register the seam on.</param>
    /// <param name="maintenance">The maintenance object, or <see langword="null"/> when reasoning is unwired.</param>
    /// <param name="refuseInconsistent">Whether a per-commit decided inconsistency vetoes the commit.</param>
    private static void WireMaintenance(MutableSparqlDataset dataset, ReasoningMaintenance? maintenance, bool refuseInconsistent)
    {
        if(maintenance is null)
        {
            return;
        }

        ReasoningMaintenanceBinding binding = new(maintenance, refuseInconsistent);
        dataset.RegisterMaintenance(binding.MaintainAsync, binding.OnCommitOutcome);
    }

    /// <summary>
    /// The Database-layer binding of the Sparql <see cref="ClosureMaintenanceDelegate"/> seam to the Owl
    /// <see cref="ReasoningMaintenance"/> object: it runs each commit's maintenance, maps the maintained result
    /// onto the served-store delta plus the round-tripped <see cref="ReasoningProvenance"/> payload, applies the
    /// per-commit refusal veto, and forwards the single commit-outcome seam that rolls the maintenance forward on
    /// landing or discards it otherwise. One instance per reasoned mutable engine; the dataset's maintenance mutex
    /// serializes its invocations, so it holds no synchronization of its own.
    /// </summary>
    /// <param name="maintenance">The owned maintenance object.</param>
    /// <param name="refuseInconsistent">Whether a decided-inconsistent commit is vetoed.</param>
    private sealed class ReasoningMaintenanceBinding(ReasoningMaintenance maintenance, bool refuseInconsistent)
    {
        /// <summary>The owned maintenance object one instance drives.</summary>
        private ReasoningMaintenance Maintenance { get; } = maintenance;

        /// <summary>Whether a decided-inconsistent commit fails pre-append with a <see cref="ReasoningInconsistencyException"/> rather than landing and serving asserted-only.</summary>
        private bool RefuseInconsistent { get; } = refuseInconsistent;

        /// <summary>
        /// The <see cref="ClosureMaintenanceDelegate"/> binding: maintains the closure over the commit's asserted
        /// base delta, applies the per-commit refusal veto, and maps the maintained result onto the served-store
        /// delta and the round-tripped provenance payload. It runs pre-append under the dataset's maintenance mutex;
        /// a refusal throw fails the commit before it linearises and rides the single not-landed outcome, which
        /// invalidates the maintenance so the next commit rebuilds.
        /// </summary>
        /// <param name="baseAdded">The triples the commit added to the asserted default graph — the true sequential net.</param>
        /// <param name="baseRemoved">The triples the commit removed from the asserted default graph — the true sequential net.</param>
        /// <param name="tentativeAssertedStore">The session's tentative post-op asserted default-graph store.</param>
        /// <param name="wholesaleReplace">Whether the caller detected a wholesale default-graph replacement, which rebuilds instead of feeding a degenerate apply.</param>
        /// <param name="cancellationToken">A token that aborts maintenance; observed pre-append.</param>
        /// <returns>The served-store delta, the overlay-on flag, and the provenance payload.</returns>
        /// <exception cref="ReasoningInconsistencyException"><see cref="RefuseInconsistent"/> is set and the commit's folded verdict is a decided inconsistency.</exception>
        public async ValueTask<MaintainedCommitDelta> MaintainAsync(
            IReadOnlyCollection<EncodedTriple> baseAdded,
            IReadOnlyCollection<EncodedTriple> baseRemoved,
            HypertrieGraphStore tentativeAssertedStore,
            bool wholesaleReplace,
            CancellationToken cancellationToken)
        {
            ReasoningMaintainedCommit commit = await Maintenance
                .MaintainCommit(baseAdded, baseRemoved, tentativeAssertedStore, wholesaleReplace, cancellationToken)
                .ConfigureAwait(false);

            ReasoningProvenance provenance = ReasoningProvenance.From(commit);

            if(RefuseInconsistent && !commit.IsConsistent)
            {
                throw new ReasoningInconsistencyException(provenance);
            }

            return new MaintainedCommitDelta
            {
                ServedAdditions = commit.ServedAdditions,
                ServedRemovals = commit.ServedRemovals,
                OverlayOn = commit.OverlayOn,
                ReasoningState = provenance,
            };
        }

        /// <summary>The <see cref="ClosureMaintenanceOutcomeDelegate"/> binding: forwards the single per-invocation outcome, rolling the maintenance forward on landing or discarding it otherwise.</summary>
        /// <param name="landed">Whether the commit the delegate maintained linearised (published).</param>
        public void OnCommitOutcome(bool landed)
        {
            Maintenance.OnCommitOutcome(landed);
        }
    }

    /// <summary>
    /// Materialises the post-commit reasoning store through a rendezvous, carrying the rendezvous and the
    /// options-wired trace seams as explicit state so the engine's <see cref="ReasoningMaterializationDelegate"/>
    /// is a bound method group rather than a lambda closing over them, and recording the outcome on
    /// <see cref="Provenance"/> for the facade to read after the build.
    /// </summary>
    /// <remarks>
    /// Reasoning runs at most once per open — over the built default graph only — so <see cref="Provenance"/>
    /// is set exactly once, by the single <see cref="MaterializeAsync"/> call the immutable build makes.
    /// </remarks>
    /// <param name="rendezvous">The reasoning rendezvous the materialisation runs through.</param>
    /// <param name="reasoningTrace">The handler each materialisation's strategy-selection event goes to, or <see langword="null"/> to emit none.</param>
    /// <param name="reasoningDecisionTrace">The handler a delegated beyond-RL decision's event goes to, or <see langword="null"/> to emit none.</param>
    /// <param name="refuseInconsistent">Whether a derived inconsistency refuses the open rather than serving the partial closure.</param>
    private sealed class ReasoningMaterialization(
        ReasoningRendezvous rendezvous,
        TraceHandler<ReasoningTraceEvent>? reasoningTrace,
        TraceHandler<ReasoningDecisionTraceEvent>? reasoningDecisionTrace,
        bool refuseInconsistent)
    {
        /// <summary>The reasoning rendezvous the materialisation runs through.</summary>
        private ReasoningRendezvous Rendezvous { get; } = rendezvous;

        /// <summary>The handler each materialisation's strategy-selection event goes to, or <see langword="null"/> to emit none.</summary>
        private TraceHandler<ReasoningTraceEvent>? ReasoningTrace { get; } = reasoningTrace;

        /// <summary>The handler a delegated beyond-RL decision's event goes to, or <see langword="null"/> to emit none.</summary>
        private TraceHandler<ReasoningDecisionTraceEvent>? ReasoningDecisionTrace { get; } = reasoningDecisionTrace;

        /// <summary>Whether a derived inconsistency refuses the open rather than serving the partial closure.</summary>
        private bool RefuseInconsistent { get; } = refuseInconsistent;

        /// <summary>The correlation-id source, minting one fresh id per traced materialisation run — the same mechanism <see cref="GraphAnalyticsRunner"/> mints its per-run ids through.</summary>
        private IdentifierDelegate Identifiers { get; } = VeritasIdentifiers.System;

        /// <summary>The reasoning outcome of the single materialisation run, or <see langword="null"/> until it runs.</summary>
        public ReasoningProvenance? Provenance { get; private set; }

        /// <summary>Materialises the reasoned store for a committed system-of-record store, recording its outcome on <see cref="Provenance"/>.</summary>
        /// <param name="store">The committed system-of-record store.</param>
        /// <param name="dictionary">The term dictionary the store is encoded against.</param>
        /// <param name="cancellationToken">A token to cancel materialisation.</param>
        /// <returns>The materialised reasoned store.</returns>
        /// <exception cref="ReasoningInconsistencyException">Reasoning derived an inconsistency and <see cref="RefuseInconsistent"/> is set.</exception>
        public async ValueTask<HypertrieGraphStore> MaterializeAsync(HypertrieGraphStore store, TermDictionary dictionary, CancellationToken cancellationToken)
        {
            //Only a wired handler pays for a clock and a correlation id, so the default path constructs neither
            //and the rendezvous call is byte-identical to an untraced materialisation.
            bool traced = ReasoningTrace is not null || ReasoningDecisionTrace is not null;
            TimeProvider? timeProvider = traced ? TimeProvider.System : null;
            Guid correlationId = traced ? Identifiers(new IdentifierRequest(IdentifierPurpose.Correlation, default)) : default;

            ReasoningResult result = await Rendezvous
                .MaterializeAsync(store, dictionary, ReasoningTrace, timeProvider, correlationId, ReasoningDecisionTrace, cancellationToken)
                .ConfigureAwait(false);

            ReasoningProvenance provenance = ReasoningProvenance.From(result);
            Provenance = provenance;

            //The folded consistency verdict drives the refusal: a fired falsity rule and a delegated
            //condemnation both flip it, while a fragment-relative consistent verdict does not.
            if(RefuseInconsistent && !result.IsConsistent)
            {
                throw new ReasoningInconsistencyException(provenance);
            }

            return result.Store;
        }
    }

    /// <summary>Describes the first error diagnostic (code and message), for a parse-failure exception message.</summary>
    /// <param name="diagnostics">The diagnostics.</param>
    /// <returns>A one-line description of the first error, or a generic message when none carry detail.</returns>
    private static string DescribeFirstError(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach(Diagnostic diagnostic in diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return $"{diagnostic.Code} {diagnostic.Message}";
            }
        }

        return "unspecified parse error.";
    }
}
