# Decision record — OWL inference via two delegate seams (materialization + query-time expansion)

Status: **proposed (2026-06-04)**. Records the design for RDFS and OWL 2 RL inference so the
reasoning survives; to be validated by the RDFS materializer (plan phase 3) and the OWL 2 RL
materializer (plan phase 5). Companion to
[`pluggable-wcoj-backends.md`](pluggable-wcoj-backends.md) — the reasoner is a *workload* over the
join machinery that decision generalizes, and one requirement here feeds back into that seam's
shape (per-pattern index binding, below).

## Context

The conformance harness is complete for everything with a W3C oracle (SPARQL 1.2 eval, SHACL 1.2,
RDFC-1.0, JSON-LD 1.1, JSON Schema 2020-12), so reasoner work proceeds against a trusted oracle.
`src/Lumoin.Veritas.Owl` is a project stub whose description already commits to the shape: OWL 2 RL
profile axioms as forward-chaining rules to fixpoint, TBox in memory, ABox streaming. The README
promises RDFS + OWL 2 RL. Target workloads are large graphs (city IFC, digital product passports,
supply chains) where materialization cost rides on the same build/join machinery the performance
seams target — which is why the perf rework and the reasoner are sequenced together.

The W3C oracle for entailment is the SPARQL 1.1 `entailment` test suite (BGP answers under an
entailment regime). Its simple/RDF/RDFS/D arms are satisfiable by a rule reasoner; the
OWL-Direct-Semantics, SPARQL-DL, and RIF arms require a DL reasoner and stay documented red/skip
until one exists. OWL 2 RL itself additionally has the W3C OWL 2 conformance test suite's RL arm
(`proposed`/`approved` entailment and consistency tests), usable the same vendored way.

## Key realization — a rule body is a BGP; the engine already evaluates BGPs

OWL 2 RL (and the RDFS rules it subsumes) is a Datalog fragment: every rule body is a conjunction
of triple patterns, every head a triple template. The engine already has a worst-case-optimal BGP
evaluator (`HypertrieGraphStore.Query` → `BasicGraphPatternEvaluator`), an edit/commit path with a
content-addressed journal (`EditSession`/`JournalEntry`), and a trace bus
(`TraceHandler<TEvent>`). The reasoner therefore composes existing seams rather than introducing an
engine of its own:

| Reasoner need | Existing machinery |
|---|---|
| evaluate a rule body | `HypertrieGraphStore.Query` (WCOJ over the BGP) |
| commit derived triples | `EditSession.AddRange` → `CommitAsync` |
| provenance of derivation | `JournalEntry.Committed` (`Additions` + `SessionId`) — no schema change |
| derivation tracing | new `InferenceTraceEvent` on the existing trace bus, shared `CorrelationId` |
| closure walks (TBox) | `Core.Algebra.IterativeTraversal` (no recursion) |

This coupling is also the point of the sequencing: every backend that wins under
[`pluggable-wcoj-backends.md`](pluggable-wcoj-backends.md) speeds up materialization directly, and
reasoner fixpoint workloads become the missing build-heavy benchmark workloads for those backends.

## Decision — two seams

### Seam M — materialization (forward chaining to fixpoint)

A materializer takes a snapshot, a rule set, and the composition seams, and commits derived triples
through its own `EditSession`s — one commit per fixpoint round, so the journal reads as the
derivation history. Rule sets are data (a rule = body BGP + head template + optional datatype
guard), selected per entailment regime: `Simple`, `RDF`, `RDFS`, `D`, `Owl2Rl`.

**RDFS ships first, specialized.** The RDFS rule set needs no general Datalog machinery:

1. Extract the TBox (`rdfs:subClassOf`, `rdfs:subPropertyOf`, `rdfs:domain`, `rdfs:range`) into
   memory; compute the subclass/subproperty closures with `IterativeTraversal`.
2. The ABox rules (rdfs2, rdfs3, rdfs7, rdfs9) are then single-pass maps over streamed triples —
   `(s p o)` with `p ⊑ q` emits `(s q o)`; `(s rdf:type C)` with `C ⊑ D` emits `(s rdf:type D)`;
   domain/range emit types from the closure. One pass per round, no joins, fixpoint in
   ≤ closure-depth rounds.

**OWL 2 RL lands as semi-naive Datalog over the same seam** (plan phase 5). The join-bearing rules
(`prp-trp`, `prp-spo2` property chains, `cls-int1`, `cax-*`) evaluate their bodies through the BGP
engine. Property-chain and intersection lists are expanded at TBox load into fixed-length rule
instances (list walking happens once, in TBox extraction, not per rule firing). The schema rules
(`scm-*`) run on the in-memory TBox only.

Two OWL 2 RL design choices, recorded now:

- **`owl:sameAs` via union-find canonicalization, not materialization.** The `eq-rep-s/p/o`
  rules materialize every permutation of equal individuals — a known quadratic blowup. Instead:
  maintain equivalence classes (union-find), rewrite to a canonical representative during
  materialization, expand representatives back on read. The expansion point is the same place
  query-time expansion (Seam Q) plugs in.
- **Retraction v1 = re-materialize from base.** The journal distinguishes reasoner commits from
  base commits by `SessionId`, so the base graph is recoverable by replaying non-reasoner entries;
  on removal of a base triple, drop derived state and re-run. Incremental maintenance
  (DRed/counting) is deferred until measured workloads justify it.

### Seam Q — query-time expansion

A driver-level variant (Seam B in the WCOJ taxonomy) that answers BGPs under a regime without
materializing: pattern rewriting against the in-memory closures — `?s rdf:type C` matches any
`C' ⊑ C`; predicate `p` matches any `p' ⊑ p`; representatives expand through the sameAs classes.
This is the complement to Seam M for regimes/datasets where materialization is too write-heavy
(or the store is read-only). RDFS-level expansion is straightforward; it is **not** on the
critical path for conformance (materialization answers the suite) and lands after Seam M proves
the rule sets.

## Delta joins — a dedicated evaluator variant, not a shared-driver parameter

Semi-naive evaluation joins *delta-restricted* atoms against the *full* store: each rule body is
evaluated once per body atom, with that atom's cursor opened over the round's delta index and the
remaining atoms' cursors over the full index. That is a join whose cursors come from **two
different indexes**.

Per the revised decision in
[`pluggable-wcoj-backends.md`](pluggable-wcoj-backends.md) — the hot loop stays concrete; an
in-driver backend seam measured at −11–22% and was reverted — this lands as a **dedicated
delta-join evaluator variant** over the concrete cursor machinery (`TriejoinIterator` opened per
pattern over either the delta snapshot or the full snapshot), peer to
`BasicGraphPatternEvaluator`, not as a parameter of a shared generic driver. The same construction
serves GRAPH-composed datasets later. Until that variant exists, the v1 fallback is
restrict-by-filter: evaluate the body on the full store and keep derivations with at least one
premise in the delta — correct, less efficient, sufficient for the RDFS rule set whose ABox rules
are single-pass maps anyway.

## Conformance wiring

Vendor `sparql/sparql11/entailment/` from `w3c/rdf-tests@674da26` (same one-curl-loop process used
for the other suites; it was fetched once before and discarded). Wire the manifest like the
existing SPARQL conformance gates:

- **Green arms:** simple, RDF, RDFS, D — answered by Seam M materialization.
- **Documented red/skip:** OWL Direct Semantics, SPARQL-DL, RIF — require a DL reasoner; out of
  scope for the rule-based materializer, revisited only if a DL engine ever lands.
- **Phase 5 adds** the OWL 2 conformance suite's RL entailment/consistency arm as the OWL 2 RL
  oracle.

## What lives where

- `Lumoin.Veritas.Core` — nothing new except the `InferenceTraceEvent` kind on the trace bus; the
  reasoner composes seams Core already exports (`EditSession`, `Query`, journal, traversal).
- `Lumoin.Veritas.Owl` — rule-set data (`Rdfs`, `Owl2Rl`), TBox extraction + closures, the
  materializer (Seam M), the union-find equivalence store, query-time expansion (Seam Q) when it
  lands.
- Derived triples live **in the same graph** as the base (an entailment regime answers over the
  entailed graph); provenance lives in the journal and the trace bus, not in the data. This keeps
  the per-graph composed dataset design ([`project_graph_dataset_backend`]) orthogonal, and the
  deferred quad/order-4 dataset-index question is revisited with reasoner workloads as planned.

## Caveats and non-goals

- **No DL reasoner.** OWL 2 RL is the ceiling for this design; DS/EL/QL profiles and tableau-style
  reasoning are explicitly out of scope.
- **Fixpoint cost is the build cost.** Materialization rides the hypertrie's edit/commit path; the
  hypertrie construction constraint ([`project_wcoj_construction_cost`]) applies in full, and the
  reasoner benchmarks (derivation throughput, fixpoint rounds, build-cost delta) are the
  measurement, per plan phase 5.
- **D-entailment** reuses the SPARQL datatype machinery for value-space identity; no second
  datatype map.

## References

- OWL 2 RL profile rule tables: <https://www.w3.org/TR/owl2-profiles/#OWL_2_RL> (tables 4–9).
- RDFS entailment rules: <https://www.w3.org/TR/rdf11-mt/#rdfs-entailment> (RDF 1.2 carries them forward).
- SPARQL 1.1 entailment regimes: <https://www.w3.org/TR/sparql11-entailment/>.
- Test suites: `w3c/rdf-tests@674da26` (`sparql/sparql11/entailment/`); W3C OWL 2 conformance (RL arm).
