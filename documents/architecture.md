# Lumoin.Veritas — System Architecture

This document explains what the system is, how its parts fit together, and the principles that
decide its trade-offs. It distinguishes throughout between what is **shipped** (in the tree,
tested, measured) and what is **designed** (decided architecture whose implementation is phased
and pending). For the live work ledger see `HANDOFF.md`; for measured numbers see the soak
outputs and HANDOFF's tables — this document deliberately carries no benchmark figures, because
they go stale here and stay current there.

Last revised: 2026-06-24.

---

## 1. What the system is

Lumoin.Veritas is a knowledge-graph engine for .NET: an RDF data plane with a SPARQL 1.2 query
engine, SHACL validation, and OWL 2 reasoning, built to run unchanged from a browser WebAssembly
host to a many-core container. Its sibling library, Lumoin.Verisync, supplies the replicated
ordered log (consensus, CRDTs) that the engine's replication story builds on; Veritas itself is
designed as a deterministic state machine over that log.

The durable persistence layer is **shipped behind the storage seams** (§8): a checksummed
append-only journal, a content-addressed manifest published by an atomic generation swap,
row-major system-of-record segments, and a verify→repair→commit self-heal loop, all tested. What
is still pending is making them the engine's *default* — the default engine wiring builds its
stores and indexes in RAM over an in-memory journal and rebuilds them per process; the durable
journal and manifest are selectable behind the seams rather than the default commit path, and
warm-start from a persisted columnar sidecar is the next step (§8.6).

### Project layout

| Area | Projects |
|---|---|
| Data plane | `Lumoin.Veritas.Core` — stores, indexes, joins, dictionary, memory pools, diagnostics |
| Query language | `Lumoin.Veritas.Sparql` — parser → AST → algebra → `SparqlQueryEngine` |
| Validation / reasoning | `Lumoin.Veritas.Shacl`, `Lumoin.Veritas.Owl`, `Lumoin.Veritas.Skos` |
| RDF model + interchange | `Lumoin.Veritas.Rdf`, `.Turtle`, `.NQuads`, `.Xml`, `.JsonLd`, `.LinkedData`, `.Canonicalization` |
| JSON / binary interchange | `Lumoin.Veritas.Json`, `.Json.Stj`, `.JsonPointer`, `.JsonSchema`, `.Cbor`, `.CborLd`, `.Cid`, `.Rdf.Json` |
| Application surface | `Lumoin.Veritas.Cli` — `query` and `serve` commands, an HTTP SPARQL endpoint, an MCP stdio mode |
| Replication / governance (in development) | `Lumoin.Veritas.Replication` (anti-entropy, sketch channels, the engine bridge) and `Lumoin.Veritas.Core.Network` (governance gate, firewall, rate limiter) — implemented and tested, not yet on the released line (§9) |
| Tests / measurement | `test/Lumoin.Veritas.Tests` (one MSTest assembly; the W3C conformance corpora are vendored under `Material/`), `test/Lumoin.Veritas.Benchmarks` (soaks behind `--profile-*` flags) |

---

## 2. Design principles

These are load-bearing: most architectural decisions below trace to one of them.

- **Determinism.** No wall-clock reads, no ambient randomness, culture-invariant text handling.
  This is enforced project-wide because the engine is designed to be a replicated state machine:
  replicas that apply the same ordered operations must reach byte-identical states, which also
  makes recovery a replay and divergence a corruption signal (§8.5).
- **Explicit memory.** Flat arrays with offsets, value types, arenas and pools
  (`VeritasMemoryPool` rather than the shared array pool), ownership with explicit disposal.
  The engine is designed to port to a language with explicit ownership semantics, so
  GC-reliance is treated as debt. The concurrency twin of this rule: explicit executors,
  queues, and turns rather than ambient thread-pool scheduling (§6).
- **Opt-in, measured alternatives.** New techniques land default-off behind policy flags; a
  default changes only on decisive measurement. Losing experiments are kept as selectable modes,
  never deleted — including hardware paths the development box cannot execute, which land behind
  capability guards with portable fallbacks so other machines exercise them.
- **Honest verdicts.** Verdict-bearing components carry scope-relative claims: what they cannot
  decide surfaces as a *named, counted abstention*, never a confident wrong answer. The same
  contract extends to durability (§8.4): unrecoverable data is an enumerated, reported loss,
  never a silently served gap.
- **Single-core measurement discipline.** Algorithmic adequacy is proven per decision on one
  core before any parallelism, so parallel speedup can never mask a per-decision defect.
  Parallelism then arrives on three separated axes: between decisions (execution lanes, §6),
  inside a decision (SIMD only, §6.3), and between nodes (replication, §9).
- **Conformance as the oracle.** External specs are tested against vendored W3C corpora as hard
  zero-failure gates. Components without an external spec require a differential oracle (naive
  reference, cross-engine agreement, or round-trip identity) plus boundary-enumerated tests, and
  the suites themselves are audited by deliberate mutation passes (§10).

---

## 3. The data plane (shipped)

### 3.1 Terms and the dictionary

RDF terms intern once into a dictionary; the engine computes over dense integer `TermId`s, and
decode back to terms happens once at the output boundary — survivors only, never per
intermediate row. Triples flow as `EncodedTriple` (three `TermId`s).

### 3.2 The system of record: the hypertrie store

`HypertrieGraphStore` is a depth-3 hypertrie over encoded triples: content-addressed,
deduplicated nodes in a `NodeStore`, immutable after build. Snapshots (`HypertrieSnapshot`) are
reference-counted handles pinning a root; a snapshot's identifier plus the journal that produced
it is sufficient to rebuild it deterministically. Edits go through journaled sessions; the
journal contract (`JournalDelegates`, `DatasetJournalDelegates`) is an append/read seam with
optimistic concurrency, currently implemented in-memory only — durable journal backends plug
into this seam (§8). A simpler `InMemoryGraphStore` coexists as a peer store.

Whole-graph construction (`BuildAsync`) is the **bulk-load path** and is treated as a design
axis of its own: storage changes are judged against bulk-load, random-write, and query
workloads, not query alone. The build dedups and radix-sorts in memory; scratch comes from the
pooled allocator.

### 3.3 Derived views: the columnar index

`ColumnarTripleIndex` is a read-optimized view built on demand from the store (governed by
`QueryEnginePolicy`): compressed-sparse-row permutation orders — all six, or a three-rotation
subset that roughly halves memory but cannot serve cyclic join shapes — with block-packed,
per-column-selected encodings (frame-of-reference, prefixed deltas, Elias-Fano for monotone
value columns, partitioned Elias-Fano within groups; the builder keeps the smaller). Succinct
encodings are served by SIMD kernels selected once per process over a capability ladder
(256-bit → WebAssembly packed SIMD → 128-bit → portable scalar) and reached by **injection**:
lower layers declare packer/unpacker delegates and never reference the kernel layer. The view is
generation-paired with the store and drops on commit (rebuild, not delta-evolution).

### 3.4 The succinct self-index

`TripleSelfIndex` is a second, opt-in storage point: wavelet-matrix-indexed rotation columns
(rank/select bit vectors with hardware bit-deposit select) that serve **all six orders from one
structure**, including the cyclic shapes the three-rotation columnar set cannot answer, at a
whole-structure footprint competitive with the packed columnar view. It backs its own
worst-case-optimal join driver and answers rotation-incompatible queries when enabled
(`PreferSelfIndex`); it also exposes exact range cardinalities for any binding pattern, which
the future join-strategy selector consumes.

### 3.5 Join execution

A multi-pattern basic graph pattern is one algebra leaf; its joins run *inside* the data plane:

- **Leapfrog worst-case-optimal join** for cyclic shapes.
- **Batched scan-and-hash pipeline** for acyclic shapes (detected by GYO reduction):
  column-major `SolutionBatch` streams, packed join keys, left-deep hash joins, with Yannakakis
  semijoin reduction at three or more patterns. The measured winner on acyclic shapes and the
  default.
- **Factorized star/chain routes** keep fan-out joins as products-of-unions instead of
  materialized cross products; measurement showed flattening dominates when the flat output is
  required, so factorization pays at the **consumers**: fast paths answer `COUNT(*)`, `DISTINCT`
  over star keys, `ASK` (first solution), and `LIMIT` (leaf row cap) without flattening.
- **Free Join** (generalized hash tries) and an adaptive per-shape selector exist as selectable
  modes, default-off pending the selector research.

The hot path is concrete — concrete batch and table types, no per-batch virtual dispatch (an
in-driver delegate seam measured as a regression and was reverted; the rule is recorded).

### 3.6 Reading and writing RDF

Turtle/TriG reading is byte-incremental end to end: an async lexer over a pipe reader, a
per-statement emitter, and a coroutine-suspending parser, so peak memory tracks the largest
single statement. A whole-document variant returns a navigable AST with source spans (the
editor/provenance pathway). N-Quads, RDF/XML, JSON-LD, and canonicalization round out the
interchange family; CBOR/DAG-CBOR and content identifiers serve the linked-data and
content-addressing paths.

---

## 4. Query language and conformance (shipped)

`Lumoin.Veritas.Sparql` implements SPARQL 1.2 (parser → AST → algebra → engine). The operator
layer carries column-major solution tables with a single decode bridge; algebra-level fast
paths short-circuit qualifying aggregations and slices into the data-plane consumers (§3.5).
Federation (`SERVICE`) runs over in-process and HTTP transports.

Conformance, measured against the vendored W3C corpora (zero failures everywhere, as of
2026-06-12): SHACL complete; Turtle, N-Quads, RDF tests complete; SPARQL complete except a
small set of named optional-feature abstentions; OWL 2 suites pass everything in scope with
every out-of-scope row abstained *by name* (cross-suite abstentions and corpus contradictions,
each counted). The conformance suites double as the differential oracle for every execution
change: alternate engines must answer identically with the corpora live.

---

## 5. The reasoning plane (shipped core, expanding)

- **SHACL**: an iterative post-order validator (no recursion) with a streaming core; the
  validation report is the fold accumulator.
- **RDFS/RL**: a streaming RDFS pass when the TBox allows it, the full RL closure otherwise,
  selected by `ReasoningPolicy`; EL/QL/RL profile checks and DL/Full species classification.
- **OWL Direct semantics**: reasoning is **modular**. Syntactic locality modules are extracted
  per question (the module is the unit of work — and the planned lane-turn grain, §6.2);
  beyond-fragment input forecloses the verdict to fragment-relative and surfaces as a named
  abstention *before* any unbounded search.
- **Engine catalog behind a seam.** Description-logic engines are stored options behind a named
  delegate seam (`DescriptionLogicDelegate`): a snapshot tableau reasoner (`AlcModuleReasoner`,
  the default) and a SAT-backed engine (`SatTableauModuleReasoner`) that decides modules as
  propositional worlds over **one shared, monotonically growing CNF** — the soundness keystone
  is that only TBox-entailed clauses ever enter the shared CNF, while world-specific facts
  travel as assumptions; modal conflicts are learned as persistent clauses; asserted-edge
  modules solve as one joint instance with per-individual variable blocks. The seam admits
  further engines (a hyper-tableau is the probable next) and makes engine comparison measurable
  on the same corpus. The internal SAT solver supports assumptions, incremental re-solve,
  optional first-UIP conflict learning, and failed-assumption cores — each landed behind a mode
  knob with differential sweeps as the oracle.

---

## 6. Execution and resources (shipped; periodic scrub scheduling pending)

> Status: the compute lane and `ExecutionPolicy` are shipped and wired — the engine opens a lane
> per process (`ComputeLane.ForCurrentPlatform`, threaded on a server, cooperative on WebAssembly)
> and runs columnar view builds as lane turns. What remains designed-not-built: admitting the
> periodic scrub round (§8.3) onto the lane at its resolved cadence.

### 6.1 The problem and the model

The production HTTP host serves latency-sensitive I/O from the runtime thread pool — the same
pool that synchronous CPU bursts (on-demand view builds, reasoning decisions, bulk sorts)
saturate. Pool starvation was measured, reproduced, and shown to be invisible without explicit
instrumentation. The architectural answer is **turn-based execution lanes**: a lane is a named
set of dedicated worker threads draining an explicit *bounded* queue in turns; a turn runs one
work item to completion on one worker. Queues are observable and admission is an explicit
verdict, so overload becomes measurable load-shedding instead of invisible starvation. Because
mailboxes and turns run correctly on a single cooperative thread, the same architecture spans
no-thread WebAssembly to many-core containers by configuration alone.

Lane taxonomy: the **serve** path stays on the hardened runtime pool; a **compute lane** takes
view builds, bulk sorts, reasoning decisions, and sketch updates; the **scrub** work class
(§8.3) is the compute lane's lowest priority class rather than its own thread set; an optional
per-store **turn executor** can later formalize the existing single-writer commit discipline as
a mailbox. A transitional pool floor protects the serve path until the lane removes mis-placed
CPU work, then lifts on a measured gate.

### 6.2 Parallel grains

- **Between decisions:** the lane turn. For reasoning, one locality module = one decision = one
  turn; when many modules are ready, the atomic-decomposition dependency order schedules them.
  Never threads inside a decision.
- **Inside a decision:** SIMD only, behind the same capability-guarded injection seams as the
  columnar kernels, measured not assumed.
- **Between nodes:** replication (§9).

Solver state composes with lanes by an affinity rule: a solver session lives within one turn, or
within a lane-pinned context successive turns of the same module may reuse.

### 6.3 The execution policy

`ExecutionPolicy` (a sibling of `QueryEnginePolicy`) carries the knobs — compute-lane width,
queue capacity, pool-floor multiplier, a SIMD width *cap* (capability is detected once per
process and cannot change; only capping below capability is meaningful), a three-state memory-
protection assumption (§8.2), scrub cadence, and the column access mode (§8.6) — every knob
defaulting to *derive*: resolution reads the container CPU quota (processor count is frozen at
process start, so quota changes need explicit observation), probes for hardware error-corrected
memory best-effort, and degenerates correctly on WebAssembly. Policy (user intent) and resolved
plan (environment facts) are separate types.

---

## 7. Observability

One trace bus (`TraceHandler<TEvent>`, null handler = zero cost) carries the statistics triad:
graph shape (`GraphStatistics`), layout/footprint (`ColumnarStatistics`), and runtime storage
access and self-heal verdicts (`StorageTraceEvent`) — all shipped. Replication and network
governance ride the same bus with their own events (`ReplicationTraceEvent`,
`NetworkGovernanceTraceEvent`). Metrics ride the OpenTelemetry `Meter` (`veritas.*` instruments):
the memory pool and string pool, the compute lane (`veritas.compute_lane.workers`, `queue_depth`,
`turns_total`, `shed_total`, `turn_duration`), and the reasoner
(`veritas.reasoning.decisions_total`, `solve_count`, `duration`) are instrumented today.

Reasoner telemetry (shipped: `ReasoningDecisionTraceEvent` plus the `veritas.reasoning.*` Meter)
emits **per-decision** trace events — counters for solver calls
and time, joint rebuilds, learned clauses, memo and blocking hits — never per-solve spans. Its
consumers, in order: budget cancellation that names its measured reason, progress-aware
cancellation (counters moving distinguishes slow-but-converging from wedged), and eventually
data-driven engine selection across the catalog.

---

## 8. Integrity and durability (shipped behind the storage seams; container format + engine-default wiring pending)

> Status: the durable write path is **shipped and tested behind the storage seams**. Shipped: a
> checksummed append-only journal (`FileBackedJournal`, torn-tail recovery), the system-of-record
> item-segment tier (`ItemSegment`, per-block checksums), the content-addressed manifest with the
> atomic CURRENT-pointer publish (`ManifestWriter` + `AtomicPublish`), three-tier crash recovery
> (`ManifestRecovery`: live → retained → degraded scan), the verify→repair→commit self-heal loop
> (`ScrubRound` / `RepairSourceLadder` / `GenerationCommitCoordinator`), pluggable checksums
> (`ChecksumAlgorithm`: XxHash3, CRC-32), and `StorageTraceEvent` telemetry — exercised by a
> boundary-enumerated damage × repair-source matrix (§10). What remains designed-not-built: wiring
> the durable journal/manifest as the engine's *default* (the default engine still runs in-memory),
> warm-start hot-load from a persisted columnar sidecar (today every process rebuilds the index
> from the journal, §8.6), a self-tuning periodic scrub scheduler (§6, §8.3), and the
> columnar/native container and Parquet-interop formats (the shipped system-of-record format is the
> row-major item segment, not a columnar table). Forward versioning is honored from day one.

### 8.1 What persists

Two tiers plus co-versioned artifacts, all published by **one atomic manifest swap**:

- **System of record** — a durable row-major **item-segment** tier (`ItemSegment`: fixed-count
  triple blocks, per-block checksums, page-aligned) holding the encoded triples, plus the durable
  journal (`FileBackedJournal`) behind the existing seam. Both are shipped. A columnar table format
  (Parquet, etc.) is a *separate* interop ingest/export seam, deferred — not the system-of-record
  format. The replicated-state-machine contract holds: `Snapshot()` *is* these files, `Apply` is
  the journal.
- **Sidecars** — the engine's own self-describing container holding the columnar/succinct
  execution structures: a key-value header with format version and required-feature flags
  (readers skip unknown keys, fail loudly on unknown *required* flags — that asymmetry is the
  whole migration discipline), a per-column directory of encodings and parameters with
  inter-column references, provenance and dictionary epochs, then 64-byte-aligned column blobs.
  Opening a container is mapping it and reconstructing column views — zero deserialization.
  Sidecars are **rebuildable by construction**; their damage is never repaired, only re-derived.
- **Sketch** — an incrementally maintained rateless set-reconciliation stream over the record
  tier's content-defined item hashes (§9), itself checksummed and co-versioned.
- **Stats** and optional **parity** (a stored coded-symbol prefix usable as deletion-correcting
  redundancy) — all named in an extensible manifest file-role taxonomy.

Retained column payloads move off garbage-collected arrays onto views over an abstract byte
source — `ColumnSource`. Shipped backings: a managed array, a native 64-byte-aligned off-GC
allocation, and a memory-mapped file (`MemoryMappedColumnarImageSource`). A browser
origin-private-file-system buffer and a ranged HTTP reader are designed extensions of the same
seam (`ColumnarImageSource`), not yet built. One abstraction makes "works in the browser", "works
with mapped files", and "works at scale" the same property (§8.6).

### 8.2 The integrity calculus

**No in-place error-correction anywhere; checksums everywhere; repair sources by tier.**
In-memory structures are not checksummed — entropy-coded payloads near the information floor
cannot carry internal redundancy without surrendering exactly the density they exist for, so
integrity at rest is detection (per-block checksums, algorithm named by an id byte in the
header, switchable like every hash in the engine) and integrity in RAM is hardware's job,
acknowledged by a three-state policy knob: assume-protected, assume-unprotected, or auto-detect
(best-effort; virtualized environments read Unknown, which resolves to assume-unprotected — more
verification, not less). The resolved state scales verify-on-load aggressiveness and scrub
cadence.

Repair follows an exhaustive **resolution ladder**: re-derive locally (anything derived) →
local parity peeling (record-tier damage within stored-prefix capacity) → peer reconciliation
(the only network rung) → **named loss** — the unrecoverable item set enumerated, reported, and
fenced, never a corrupted answer. Repair itself is ordinary ingest: recovered items become a new
immutable segment plus a manifest swap; no privileged repair writer exists.

### 8.3 The periodic protocol

Detection is passive; repair rides a **periodic round** — a scrub walk locally, an anti-entropy
exchange between replicas — so no reactive machinery exists anywhere. The period is an explicit
vulnerability window: same-item damage on every repair source within one period is what the
parity role exists for. Every periodic round is a steppable, synchronously drivable function
with the scheduler a thin timer over it, which is also what makes the whole damage × repair
matrix testable (§10).

### 8.4 Write discipline

Memory-mapped files are read-only, always. Every file writes temp-then-rename; the manifest
swap is the single atomic commit point publishing segments, dictionary epoch, stats, and sketch
as one unit — a crash leaves the prior manifest wholly in force. The read-mostly reality starts
at immutable-snapshot-plus-manifest-swap (crash-safe by construction, no write-ahead log); a
WAL/delta/compaction ladder is climbed only when durable incremental writes demand it, and the
journal seam is where that log plugs in.

### 8.5 Detection limits, stated honestly

Absolute detection is impossible in two places, and the design says so: corruption *before*
checksumming yields a validly-checksummed corrupt item (locally undetectable by construction),
and checksum collisions are nonzero forever (sized, and sizeable upward). The containments:
hashes compute at creation, not at persistence; the journal carries consensus-agreed content
hashes so **every replica verifies its own apply** — collapsing the blind window to the original
proposer's single computation; divergence between checksum-valid copies is never blind-merged
but adjudicated; and under determinism the periodic reconciliation round is itself a detector of
last resort (any divergence surfaces within one period). Adjudication is a ladder: journal
replay decides wherever available (wrongness is determined, not voted); byte-agreement majority
beats any single dissenting node regardless of its hardware protection; the protection state
decides only ties — it orders suspicion, repair-donor preference, and proposer preference, but
never overrules agreement, because it is self-reported evidence, not truth.

### 8.6 Access modes and bulk load

Mapped versus streamed column access is a policy axis, not a format property (`ColumnAccessMode`:
`Auto`, `MemoryMapped`, `Streamed`, resolved once per process): the container's offset-addressable
layout serves memory-mapping on capable hosts and streamed ranged reads elsewhere, behind the same
`ColumnarImageSource` seam (the memory-mapped and pooled-streamed backings ship; browser
origin-private file system and HTTP backings are designed extensions). The columnar sidecar
format, its checksums, and its recovery are shipped; **warm-start hot-load is designed but not yet
built** — today every process still rebuilds the columnar index from the journal
(rebuild-from-triples-every-process). When it lands, warm start is mapping a sidecar whose
provenance epoch matches rather than rebuilding from scans.

---

## 9. Replication (in development — anti-entropy and the engine bridge implemented; consensus/log integration designed)

**Status.** The anti-entropy core is implemented and tested (in development on the replication
branch, not yet on the released line): rateless set-reconciliation sessions in both a **structural**
domain (shared dictionary epoch, intra-cluster) and a **content-hash** domain (independent
dictionaries, cross-organization); the rateless codec; in-process and cross-process sketch
channels; and the engine bridge — a mutable `VeritasEngine` seeds a reconciliation feed on commit,
serves its own sketch (`CreateSketchFetch`), reconciles from a peer (`ReconcileFromPeerAsync`,
refused before it begins on a dictionary-epoch mismatch), and writes the recovered delta back
through the journal as an ordinary committed mutation a later query sees. **Network governance** —
a gate consulted by transport decorators, with a firewall, a per-peer token-bucket rate limiter,
and a hot-swappable policy chain plus a live control surface — is implemented and tested but not
yet wired into the engine/CLI default. Designed-not-built: the Verisync consensus / ordered-log
integration, multi-node topology and per-graph actor placement, durable cross-restart sketches,
and the adjudication ladder (§8.5).

The forward design: Veritas composes with Lumoin.Verisync as a deterministic state machine over a
replicated ordered log: consensus orders operations; determinism makes replicas byte-identical; snapshots
are the persisted files. Anti-entropy between replicas uses **rateless set reconciliation**
over content-defined item identities (canonical item hashes, never dictionary-relative ids):
one incrementally maintained coded-symbol stream per replica serves any peer at any divergence
with cost proportional to the difference, which is also the repair transport for the integrity
ladder's peer rung. Constellations may be heterogeneous in hardware protection — one
error-corrected-memory node measurably improves whole-fleet durability (loss requires the same
items damaged on *every* repair source within one period) and is the preferred proposer and
repair donor — subject to the adjudication ladder in §8.5. Per-graph actor placement and all
multi-node topology decisions belong to this arc, deliberately not pre-decided by the
single-node design.

---

## 10. Quality strategy

Codified as gates, each motivated by a class of bug that escaped a weaker gate:

1. **External spec ⇒ conformance corpus** as a hard zero-failure gate, every abstention named
   and counted — an unnamed skip is a blind spot, not a pass.
2. **No external spec ⇒ no landing without a differential oracle** (naive reference,
   cross-engine agreement, round-trip identity) plus a boundary-enumerated matrix — every
   structural constant's edges tested by construction, not sampled.
3. **Honesty contracts on verdict-bearing components** — missing capability surfaces as named
   abstention, never a confident wrong answer.
4. **Audit the tests**: periodic mutation passes — break an invariant deliberately, confirm the
   suite goes red, record which test catches it, revert. A green suite proves nothing about its
   own sensitivity.
5. **No oracle exists (perf, memory, concurrency) ⇒ soaks** with anchor rows and match columns,
   suite-time budget watch, and instrumented stress campaigns.

The durability design carries its own instance of gate 4, now realized in code: a deterministic
fault-injecting store, steppable (non-timer) periodic rounds, the named persistence invariants
I1–I7 (detection-precedes-use, detection-precedes-reconciliation, repair-faithful-or-enumerated-loss,
atomic publish, epoch consistency, repair-is-ordinary-ingest, loss-is-named), and a
boundary-enumerated damage × repair-source matrix (`PersistenceCombinationMatrixTests`) that
demonstrates each invariant in each admitted cell. The cells the single-node gate does not yet
admit (the parity tier's own re-derive) are enumerated and skipped by name, not silently omitted.

---

## 11. Status ledger

| Capability | Status |
|---|---|
| Hypertrie store, snapshots, journaled edits | Shipped (in-memory journal is the engine default; durable journal selectable behind the seam) |
| Columnar view, encodings, SIMD kernel ladder, self-index | Shipped |
| Join engines (leapfrog, batched, factorized, Free Join) + consumer fast paths | Shipped (alternates opt-in) |
| SPARQL 1.2 engine + federation | Shipped, conformance-complete |
| SHACL validation | Shipped, conformance-complete |
| RDFS/RL closure, profiles, species | Shipped |
| OWL Direct: modular reasoning, snapshot + SAT-backed engines behind the seam | Shipped (catalog expanding) |
| Trace bus + statistics; pool, compute-lane, reasoning, and storage metrics | Shipped |
| Execution lanes, `ExecutionPolicy`, lane metrics, reasoner decision telemetry | Shipped (periodic scrub scheduling on the lane pending) |
| Durable persistence behind the seams: journal, manifest + atomic generation publish, item-segment system-of-record, pluggable checksums, 3-tier crash recovery, verify→repair→commit self-heal, `ColumnSource` (managed / native / memory-mapped) | Shipped, tested |
| Durable container format (columnar/native, Parquet interop), columnar-sidecar warm-start hot-load, engine-default durable wiring, OPFS / HTTP column sources, live scrub scheduler | Designed |
| Replication: anti-entropy (structural & content-hash), rateless codec, sketch channels, engine bridge | In development (implemented, tested; not on the released line) |
| Network governance: gate, firewall, per-peer rate limiter, live policy chain + control surface, governed transports | In development (implemented, tested; not wired into the engine/CLI default) |
| Replication: Verisync consensus / log integration, multi-node topology, adjudication | Designed (own arc; sibling library shipped) |

The authoritative running state — what landed last, what is next, with measurements — is
`HANDOFF.md` at the repository root.

---

## 12. Tuning knobs (disk and network geometry)

Every tunable is a value on a policy type or a constructor parameter; defaults are conservative and
self-deriving (`null` / `0` / `Auto` = derive from the environment). Policy (intent) and the
resolved plan (environment facts) are separate types, so a deployment states intent and the engine
records what it actually resolved.

**Disk / storage geometry.**

| Knob | Type (default) | Tunes |
|---|---|---|
| `ItemSegment` block item count | `int` (caller-set) | triples per system-of-record block — the checksum and repair granularity |
| block alignment (`ItemSegment` / `SketchSegment` / `ParitySegment`) | `int` (4096, one page) | block-start alignment so a torn write or faulted page maps to exactly one checksum domain |
| `SketchSegment` symbol width / symbols per block | `int` (caller-set) | the integrity-sketch coded-symbol geometry |
| `ManifestWriter` retained pointer count | `int` (≈3–4) | how many superseded CURRENT generations are kept as recovery-fallback depth |
| `ChecksumAlgorithm` | `ChecksumAlgorithm?` (XxHash3, id 1) | at-rest detection strength/cost; CRC-32 (id 2) or a custom id are selectable, switchable like every hash |
| `DurabilityBarrierDelegate` | delegate (fsync on Linux/macOS, no-op on Windows) | the directory-flush barrier after the atomic rename |
| `ExecutionPolicy.ColumnAccessMode` | `Auto` / `MemoryMapped` / `Streamed` (`Auto`) | memory-map local files vs stream ranged reads |
| `ColumnPayloadBacking` | `Managed` / `NativeAligned` (`Managed`) | block-packed column payloads on the GC heap vs a 64-byte-aligned off-GC buffer |
| columnar order set / value encoding | `ColumnarOrderSetMode` / `ColumnarValueColumnEncoding` | which permutation orders are materialized; entropy-coded vs frame-of-reference columns |

**Network geometry (replication + governance).**

| Knob | Type (default) | Tunes |
|---|---|---|
| `ReplicationPolicy.BaseSymbols` / `PerItemSymbols` | `int` (100 / 20) | the rateless **symbol budget** per round = `Base + PerItem · (2 · localItemCount)`; sizes a sketch against expected divergence |
| reconcile `maxRounds` | `int` (caller-set) | bound on the catch-up loop before a pass gives up |
| sketch-channel `maxFrameLength` | `int` (`MessageChannel.DefaultMaxFrameLength`) | the largest framed sketch image a peer will buffer |
| dictionary `Epoch` | `ulong` (minted non-zero per database) | the structural-reconcile compatibility key; a mismatch refuses cross-dictionary reconcile before it begins |
| rate limiter: token limit / tokens-per-period / period / queue limit | `int` / `int` / `TimeSpan` / `int` | per-peer token-bucket admission of outbound calls |
| firewall denylist / policy chain | per-peer entries / ordered `NetworkGovernanceDelegate[]` | which peers are blocked; the order policies are consulted (first non-permit wins) |

**Compute and integrity cadence.**

| Knob | Type (default) | Tunes |
|---|---|---|
| `ExecutionPolicy.ComputeLaneWorkers` | `int` (0 = derive `max(1, budget − 1)`) | dedicated compute-lane worker threads, leaving the serve pool headroom |
| `ExecutionPolicy.ComputeQueueCapacity` | `int` (0 = `workers · 4`) | bounded lane queue depth; overflow is observable load-shedding, not invisible starvation |
| `ExecutionPolicy.HostPoolFloorMultiplier` | `int` (0, disabled) | a transitional serve-pool floor |
| `ExecutionPolicy.KernelWidthCap` | `Auto` / `Bits256` / `Bits128` / `Portable` (`Auto`) | caps SIMD width below hardware capability |
| `ExecutionPolicy.EccAssumption` | `AutoDetect` / `AssumeProtected` / `AssumeUnprotected` (`AutoDetect`) | scales verify-on-load aggressiveness and scrub cadence |
| `ExecutionPolicy.ScrubCadence` (+ estimator) | `TimeSpan?` (null = estimator; `Zero` disables) | how often the periodic scrub round runs (the scheduler that drives it is still designed) |

**Telemetry.** Every subsystem above emits on one diagnostics bus (`TraceHandler<TEvent>`, null =
zero cost): `StorageTraceEvent` (scrub / repair verdicts), `ReplicationTraceEvent` (reconcile
outcome, budget, recovered count), `NetworkGovernanceTraceEvent` (per-decision permit / delay /
deny), `ReasoningDecisionTraceEvent`, and the `GraphStatistics` / `ColumnarStatistics` events.
Metrics ride the OpenTelemetry `Meter` under `veritas.*` (`veritas.compute_lane.*`,
`veritas.reasoning.*`, and the memory/string-pool instruments). A correlation id links an event
back to the operation that drove it.
