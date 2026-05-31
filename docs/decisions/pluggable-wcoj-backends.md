# Decision record — pluggable WCOJ index/join machinery via delegate seams

Status: **proposed / exploratory (2026-05-31)**. Records the design direction and the seam shape
so the reasoning survives; to be validated by the Phase-1 refactor (below), which must be
behaviour-identical. Scope: generalizing the worst-case-optimal-join (WCOJ) machinery so
alternative indexing/join schemes can be hosted and *measured* against the current hypertrie,
without committing to any one of them. Companion to
[`sparql-join-and-triple-term-matching.md`](sparql-join-and-triple-term-matching.md), which set the
two-join-layer model and deferred the performance rework behind the (now-complete) conformance
harness.

## Context

The conformance harness is green (all SPARQL 1.2 eval + SHACL 1.2 + RDFC-1.0), so the gate that
deferred the performance rework is satisfied. Target workload is large-graph (city IFC, digital
product passports, supply chains, OWL reasoning) where build cost and cache behaviour are
first-order. The hypertrie's **construction cost** is a hard, already-paid constraint: an early
build was ~10 GiB / dog-slow for ~1M triples and was reworked at real expense — the current build
is healthy, and the bar for any alternative backend is "beat a *healthy* hypertrie," not "rescue a
broken one."

Five recent systems were studied to ask one question: **can we generalize the join/index machinery
so we can try and even run these different indexing schemes in our engine, and benchmark them?**

- **CLTJ** (Compact LTJ, VLDBJ 2025) — succinct LOUDS tries, all 6 orders in ~3.3× raw, *updatable*.
- **Sonic** (EDBT 2023) — cache-conscious hash-trie; attacks *per-query index build cost*.
- **Diamond** (Birler, VLDB 2024) — adaptive algebra unifying binary hash-join and ternary WCOJ.
- **SieveJoin** (DSE 2026) — Bloom-filter semijoin propagated along the join path (~1 bit/tuple).
- **HoneyComb** (Wu & Suciu, SIGMOD 2025) — *parallel* WCOJ on multicore: HyperCube partitioning of
  every variable's domain over shared memory, no replication, lock-free; a sort-built columnar
  trie (CoCo) + a cost model that co-chooses variable order and partition shares.

## Key realization — our engine already has the layered shape; we are one abstraction short

The reference WCOJ codebases (CLTJ, HoneyComb) are *already* factored as `driver(iterator(index))`
with a pluggable variable-order. Our engine is the same architecture with **concrete** types:

| Role | CLTJ (C++ templates) | Our engine (concrete today) |
|---|---|---|
| WCOJ driver | `ltj_algorithm::search` | `BasicGraphPatternEvaluator` |
| intersection | `seek()` round-robin `leap` | `LeapfrogIntersection.TryFindNextCommonKey` |
| trie cursor | `ltj_iterator_lite` (`down/up/leap/leap_done/seek_all`) | `TriejoinIterator` (`Open/Up/Seek/Next/Key/AtEnd/CurrentVariable`) |
| index | `cltj_index_spo_lite` (6 succinct tries) | `HypertrieGraphStore` (depth-3 hypertrie) |
| variable order | `veo_simple` / `veo_adaptive` | `Planner` |

So generalization is **extraction, not rewrite**: lift the seam out of `TriejoinIterator` /
`LeapfrogIntersection` / `Planner` and let the existing hypertrie be the first backend behind it.

## The taxonomy — four seams, and the scheme that exercises each

The five systems attack different layers, so the engine needs four extension points, not one:

- **Seam A — index / cursor** (a leapfrog trie-iterator: open/up · seek-leap · key · atEnd, over a
  swappable index, plus a degree/weight metric and an optional `Contains` view).
  Backends: **hypertrie** (ours) · **CLTJ** succinct · **Sonic** hash-trie · **CoCo** columnar
  sorted-array (with a *swappable `search` sub-delegate*: galloping / quadratic / linear). Targets
  **build cost** and **memory footprint**.
- **Seam B — join driver / plan**: leapfrog WCOJ (ours) · Diamond's adaptive binary↔WCOJ algebra ·
  HoneyComb's lift-redundant-intersection rewrite. Needs **two intersection strategies** behind one
  contract — sorted-merge (leapfrog) and scan-smaller-side / hash-probe (for `Hashed` cursors).
- **Seam C — pre-pass filter**: SieveJoin Bloom semijoin propagation. *Composes* with any driver;
  needs only `add`/`contains` + an attribute scan in join order. Targets **intermediate-result
  blowup** (the Layer-2 `O(n·m)` problem) and is update-friendly.
- **Seam D — parallel partition + cost-model planner** (HoneyComb): HyperCube share/order planning +
  a lock-free, read-only-snapshot task fan-out. **Composes A and B as delegate arguments.**

## Decision — express the seams as delegates (record-of-delegates bundles), not interfaces

Consistent with the codebase idiom (`GraphMatchOps` is a `readonly record struct` of delegates;
randomness/identity/blank-node/hash flow through `Veritas*`/`HashDelegate` seams —
[`project_veritas_injection_seams`], [`feedback_hotpath_sync_behind_delegates`]). A backend is a
value, a bundle of delegates; the driver is generic over the cursor type and composes them.

```csharp
// TCursor = the backend's cursor state (our TriejoinIterator, a CLTJ/Sonic/CoCo cursor).
// Generic over TCursor so the JIT monomorphizes the hot loop (the templates' free inlining,
// recovered by generic specialization rather than per-call interface dispatch).
public readonly record struct WcojBackend<TIndex, TCursor>(
    BuildIndexDelegate<TIndex>           Build,        // (triples, hash) -> TIndex        [once]
    OpenCursorsDelegate<TIndex, TCursor> OpenCursors,  // (index, patterns, order) -> TCursor[]
    IntersectDelegate<TCursor>           Intersect,    // (cursors sharing the var) -> TermId?  [HOT]
    DescendDelegate<TCursor>             Descend,      // (cursors, key) -> void                [HOT]
    AscendDelegate<TCursor>              Ascend,       // (cursors) -> void                     [HOT]
    WeightDelegate<TCursor>              Weight,       // (in cursor) -> long   (VEO metric)
    IntersectionKind                     Kind);        // Sorted (merge) | Hashed (scan-smaller-probe)
```

**Granularity is the design crux** (cursors are stateful, and the leapfrog inner loop calls
`seek/key` very hot). The swappable delegates are the *cursor factory* and the *per-variable-step*
ops (`Intersect`/`Descend`/`Ascend`), each doing real work; the *per-seek micro-ops* stay concrete
*inside* each backend's `Intersect`. That is the [`feedback_hotpath_sync_behind_delegates`] sweet
spot — the per-item fn behind the delegate amortizes the indirect call, and we never pay a delegate
per seek.

**Higher-order composition** (delegates that take delegates) is where it earns its keep — exactly
the three places one piece must vary without rewriting the rest:

```csharp
// VEO metric is pluggable — CLTJ's trait_size / trait_distinct, verbatim.
delegate Variable VariableOrderDelegate<TCursor>(PlanState s, WeightDelegate<TCursor> weight);

// SieveJoin pre-pass over the membership delegates.
delegate Filters SemijoinReduceDelegate(JoinGraph g, AddDelegate add, ContainsDelegate contains);

// HoneyComb (Seam D) — the parallel dispatcher COMPOSES the Seam-A build and Seam-B body delegates.
delegate (VariableOrder order, int[] shares) PlanParallel(
    ConjunctiveQuery q, RelationStats stats, int threadBudget, WcojCostDelegate cost);
delegate IEnumerable<ResultPartition> ExecuteHyperCube(
    Relations r, int[] shares, VeritasHash[] perVarHash,   // per-var hashes via the entropy seam
    BuildIndexDelegate buildIndex /* Seam A */, GenericJoinDelegate joinBody /* Seam B */, TaskPool pool);
```

HoneyComb is the strongest argument for delegates-over-interfaces: the parallel layer is *defined*
by taking the index-build and join-body functions as parameters, lock-free over a read-only
`HypertrieSnapshot`, accumulating thread-local outputs — a natural .NET work-stealing fan-out, and
its per-variable hashes route straight through `VeritasRandomness`.

## The measurement vehicle already exists

`test/Lumoin.Veritas.Benchmarks` (BenchmarkDotNet) already does backend A/B: `BuildBenchmark`
compares `InMemoryGraphStore` vs `HypertrieGraphStore` build over a triple-count grid (triples
generated once in `GlobalSetup`); `QueryBenchmark`/`MatchBySubjectsBenchmark` cover query time; the
allocation tracker gives GC bytes/collections; `Program.cs` `--profile-*` entrypoints give
dotnet-trace/PerfView CPU stacks; `Workbench/HypertrieSoak` gives steady-state GC. The seam slots in
as a BenchmarkDotNet `[Params]` axis over `WcojBackend`, so "does this backend win" is answerable
end-to-end. Mirror HoneyComb's methodology: a `cores` axis (scalability 1→N), the WCOJ query-shape
grid (triangle / 4-loop / 4-clique / Loomis-Whitney), the share/order ablation, and **including
index-build time in the measurement** — over our BIM-/DPP-shaped workloads
([`project_scale_performance_target`]), against a *healthy* hypertrie baseline.

## Phased plan

1. **Extract the delegate seam** from the current hypertrie path (`ITrie`-shaped delegates out of
   `TriejoinIterator` / `LeapfrogIntersection` / `Planner`) — pure refactor, behaviour-identical,
   guarded by the green conformance harness + `BasicGraphPatternJoinDifferentialPropertyTests`.
2. **Make `BuildBenchmark`/`QueryBenchmark` backend-parametric** over `WcojBackend`. With the
   hypertrie as the only backend, its numbers must match the pre-refactor baseline — the benchmark
   *is* the regression guard for the abstraction overhead (build/query/GC drift ≈ 0).
3. **Add one alternative backend** — start with a Sonic-style hash-trie (per-query build) or the
   CoCo columnar sorted-array (one parallel sort; SIMD-amenable), and let the existing
   build/query/soak + GC/CPU diagnosers decide its worth on real-shaped data.
4. **Later, separately measured**: SieveJoin pre-pass (Seam C); HoneyComb parallel layer (Seam D);
   Diamond adaptive driver (Seam B).

## Caveats and non-goals

- **Generics vs templates.** Delegates cost the free inlining C++ templates get. Mitigation: generic
  `TCursor` + coarse seam granularity (above); a specific backend may become a sealed-generic
  specialization if its hot loop ever needs it. Not pre-optimized.
- **Static-rebuild vs updatable store.** Sonic / HoneyComb (and CoCo) are static, in-memory, rebuilt
  per query-order, **non-updatable** — the opposite of our journaled/`EditSession` mutable store.
  So Seam D (and a Sonic/CoCo backend) is an **analytical execution mode over a frozen
  `HypertrieSnapshot`**, complementary to the incremental store, not a replacement. This boundary is
  part of the seam contract (`IsUpdatable`, and "build per query" vs "persistent" lifetimes).
- **Diamond and HoneyComb are larger lifts** (an adaptive plan algebra; a parallel partitioner + a
  pessimistic cost model) — treat Seam A (index) + Seam C (filter) as the near-term, low-risk wins.
- This dovetails with the deferred quad/order-4 **dataset** optimization, which we tied to the OWL
  reasoner work ([`project_graph_dataset_backend`]).

## References

In `tempdocs/` (user-provided): `compatctltj-s00778-025-00945-5.pdf` (CLTJ),
`sonic-join-3-paper-44.pdf` (Sonic), `diamond-p3215-birler.pdf` (Diamond),
`SieveJoin_*.pdf`, `honeycomb-3725307.pdf` (HoneyComb, ACM DOI 10.1145/3725307), and the CLTJ C++
reference at `tempdocs/cltj-master/` (`include/query/ltj_iterator_lite.hpp`,
`include/query/ltj_algorithm.hpp`, `include/veo/`).
