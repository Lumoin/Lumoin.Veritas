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

The engine is currently **memory-only**: stores and indexes are built in RAM and rebuilt per
process. The durable on-disk story (§8) is designed and phased, not yet implemented.

### Project layout

| Area | Projects |
|---|---|
| Data plane | `Lumoin.Veritas.Core` — stores, indexes, joins, dictionary, memory pools, diagnostics |
| Query language | `Lumoin.Veritas.Sparql` — parser → AST → algebra → `SparqlQueryEngine` |
| Validation / reasoning | `Lumoin.Veritas.Shacl`, `Lumoin.Veritas.Owl`, `Lumoin.Veritas.Skos` |
| RDF model + interchange | `Lumoin.Veritas.Rdf`, `.Turtle`, `.NQuads`, `.Xml`, `.JsonLd`, `.LinkedData`, `.Canonicalization` |
| JSON / binary interchange | `Lumoin.Veritas.Json`, `.Json.Stj`, `.JsonPointer`, `.JsonSchema`, `.Cbor`, `.CborLd`, `.Cid`, `.Rdf.Json` |
| Application surface | `Lumoin.Veritas.Cli` — `query` and `serve` commands, an HTTP SPARQL endpoint, an MCP stdio mode |
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

The interchange front end is **byte-native end to end**: every reader scans UTF-8 directly, with
no UTF-16 or DOM round-trip. The reading shape follows who *produces* the format. A
human-authored grammar is editor-incremental — fed chunk by chunk through a `Feed`/`Status`/
`Complete` surface that reports completeness as a value (an unfinished tail is *needs-more*, never
an error) and yields a navigable AST with source spans (the editor/provenance pathway); this is
shared across the authored grammars (Turtle, the three OWL syntaxes, and — in the query and JSON
families — SPARQL and Jsonata). A machine-produced format is forward-streaming or whole-DOM.

Turtle/TriG reading is byte-incremental: an async lexer over a pipe reader, a per-statement
emitter, and a coroutine-suspending parser, so peak memory tracks the largest single statement;
a whole-document variant and the editor-incremental reader both return the navigable AST.

RDF/XML and SPARQL-results XML share **one** byte-native XML scanner: the whole-buffer readers
fold its event stream into an element tree, while a forward-streaming mode hands each completed
top-level subtree to the reader and then reclaims the scanner's consumed buffer, so peak memory
tracks a single subtree rather than the document — the bounded-memory property Turtle has per
statement, validated by a streaming-equals-buffered differential oracle. XML-literal
canonicalization is byte-native as well; the front end carries no XML DOM dependency, and only the
optional strict Canonical XML 1.0 variant reaches for the platform XML libraries. N-Quads,
JSON-LD, and dataset canonicalization round out the interchange family; CBOR/DAG-CBOR and content
identifiers serve the linked-data and content-addressing paths.

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

## 6. Execution and resources (DESIGNED — phases A–C pending)

> Status: design complete (the platform-resource memo); implementation phased. Phase A (host
> hardening) and phase B (compute lane + policy) land together before OWL reasoning serves
> requests over the HTTP host.

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
graph shape (`GraphStatistics`), layout/footprint (`ColumnarStatistics`) — both shipped — and
runtime storage access (`StorageTraceEvent`, designed). Metrics ride the OpenTelemetry `Meter`
(`veritas.*` instruments; the memory pool and string pool are instrumented today; lane queue
depth, turns, and shed counts are designed alongside the lanes).

Reasoner telemetry (designed) emits **per-decision** trace events — counters for solver calls
and time, joint rebuilds, learned clauses, memo and blocking hits — never per-solve spans. Its
consumers, in order: budget cancellation that names its measured reason, progress-aware
cancellation (counters moving distinguishes slow-but-converging from wedged), and eventually
data-driven engine selection across the catalog.

---

## 8. Integrity and durability (DESIGNED — phases P1–P3 pending)

> Status: design complete; nothing durable exists today (verified by inventory: no file format,
> no write path, no checksums in the engine). This is greenfield with one pre-built seam — the
> journal delegate contract — and one discipline to honor from day one: forward versioning.

### 8.1 What persists

Two tiers plus co-versioned artifacts, all published by **one atomic manifest swap**:

- **System of record** — a durable columnar table tier (an open columnar table format such as
  Parquet) holding the encoded triples, plus the durable journal behind the existing seam. The
  replicated-state-machine contract holds: `Snapshot()` *is* these files, `Apply` is the journal.
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
source — `ColumnSource`: native 64-byte-aligned allocation, a memory-mapped span, a browser
origin-private-file-system buffer, or a ranged HTTP reader. One abstraction makes "works in the
browser", "works with mapped files", and "works at scale" the same property (§8.6).

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

Mapped versus streamed column access is a policy axis, not a format property: the container's
offset-addressable layout serves memory-mapping on capable hosts and ranged reads (browser
origin-private file system, HTTP) elsewhere, behind the same `ColumnSource` seam. The durable
tier makes bulk load resumable (ingest lands record segments first; sidecars rebuild from
scans) and ends rebuild-from-triples-every-process: warm start is mapping a sidecar whose
provenance epoch matches.

---

## 9. Replication (DESIGNED — its own arc)

Veritas composes with Lumoin.Verisync as a deterministic state machine over a replicated
ordered log: consensus orders operations; determinism makes replicas byte-identical; snapshots
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

The durability design adds its own instance of gate 4 by construction: a deterministic
fault-injecting byte-source decorator, steppable periodic rounds, named persistence invariants
(detection-precedes-use, detection-precedes-reconciliation, repair-faithful-or-enumerated-loss,
atomic publish, epoch consistency, repair-is-ordinary-ingest, loss-is-named), and a
boundary-enumerated damage × repair-source matrix that must demonstrate every invariant in every
cell before the durable tier lands.

---

## 11. Status ledger

| Capability | Status |
|---|---|
| Hypertrie store, snapshots, journaled edits (in-memory) | Shipped |
| Columnar view, encodings, SIMD kernel ladder, self-index | Shipped |
| Join engines (leapfrog, batched, factorized, Free Join) + consumer fast paths | Shipped (alternates opt-in) |
| SPARQL 1.2 engine + federation | Shipped, conformance-complete |
| SHACL validation | Shipped, conformance-complete |
| RDFS/RL closure, profiles, species | Shipped |
| OWL Direct: modular reasoning, snapshot + SAT-backed engines behind the seam | Shipped (catalog expanding) |
| Trace bus + statistics, pool metrics | Shipped |
| Execution lanes, `ExecutionPolicy`, lane metrics, reasoner decision telemetry | Designed (phases A–C) |
| Durable tiers, container format, manifest, checksums, scrub, repair ladder | Designed (phases P1–P3; the format build gated separately) |
| Replication: log integration, anti-entropy, adjudication | Designed (own arc; sibling library shipped) |

The authoritative running state — what landed last, what is next, with measurements — is
`HANDOFF.md` at the repository root.
