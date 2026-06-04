# Decision record — pluggable WCOJ index/join machinery via delegate seams

Status: **decided 2026-06-04, by measurement: no abstraction inside the join loop.** The Phase-1
extraction was implemented as a delegate bundle, measured at **−11–22% on the hot query path**, and
reverted; the revised decision and the numbers are recorded below. Backends integrate at the
evaluation boundary (`store.Query`), where a seam is crossed once per query instead of several
times per solution row. Originally proposed/exploratory 2026-05-31. Scope: generalizing the worst-case-optimal-join (WCOJ) machinery so
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

## Decision (revised 2026-06-04, by measurement) — no abstraction inside the join loop

The in-driver seam was implemented as the codebase-idiomatic record-of-delegates bundle
(`WcojBackend<TIndex, TCursor>`: per-pattern cursor factory, `Intersect`/`Descend`/`Ascend`
step ops, cursor state probes, lifecycle) with the driver generic over `TCursor`, and measured
against the concrete baseline on identical hardware (Ryzen 9 7950X, .NET 10):

| Instrument | Concrete driver | Delegate-bundle driver | Drift |
|---|---|---|---|
| BDN SinglePattern 1k/10k/100k subjects | 403 μs / 4.10 ms / 43.4 ms | 454 μs / 4.57 ms / 48.9 ms | +11.5…12.6% |
| BDN TwoPatternJoin 1k/10k/100k | 566 μs / 6.58 ms / 84.1 ms | 683 μs / 7.25 ms / 87.0 ms | +20.8 / +10.2 / +3.4% |
| BDN Triangle 1k/10k/100k | 1.09 ms / 13.7 ms / 218 ms | 1.29 ms / 15.2 ms / 222.6 ms | +17.8 / +10.9 / +1.9% |
| Soak, single-pattern rows/s (10k subjects) | 5.97M rows/s | 4.76M rows/s | −20.3% |
| Soak, triangle queries/s (10k subjects) | 64.0 q/s | 49.8 q/s | −22.2% |
| Allocations (all BDN cells) | — | byte-identical | 0 |

dotnet-trace stacks (`tempdocs/wcoj-{baseline,delegates}.nettrace`) attribute the cost to three
stacked mechanisms, all per **solution row** (the driver crosses the seam ~6–8× per row: planner
snapshot, ascend, intersect, probes, descend, reseed probes):

1. **Delegate indirection is opaque to inlining** — `LeapfrogIntersection.TryFindNextCommonKey`
   and the `Ascend`/`Descend` wrappers appear as standalone frames in the delegate trace and are
   absent in the baseline, where the JIT folds them into the driver's state machine.
2. **Lost downstream optimization** — the baseline's `AtEnd`/`Key`/`CurrentVariable` reads are
   inlined property getters on a sealed class (field loads, registerized, CSE'd); behind a
   delegate each read is a call boundary.
3. **Shared-generic tax** — with a class-typed `TCursor` the runtime compiles one `__Canon`-shared
   body; `new TCursor[]` / `List<TCursor>` in `TryDescend`/`TryAdvance` then pay runtime
   generic-dictionary lookups per row. The trace shows the driver running as `__Canon` code.

The only zero-cost route .NET offers for a *shared* driver is a **struct-constrained generic
backend** (`where TBackend : struct, IWcojBackend<…>`), which forces full per-backend
specialization — devirtualized, inlined, no `__Canon`; the interface exists only at compile time
as the constraint vocabulary, and no interface dispatch survives at runtime. That route remains
viable. It was not adopted now because there is no second backend to share the driver with yet —
the concrete loop is the zero-risk option and gives identical performance — so the abstraction
waits for the first real second backend to justify it. The decision:

- **The hot loop stays concrete.** `BasicGraphPatternEvaluator`, `TriejoinIterator`,
  `LeapfrogIntersection`, `Planner` remain exactly as they are.
- **A backend is a whole unit — index + cursors + driver — and the seam is the evaluation
  boundary** (`store.Query(...)`-shaped), crossed once per query. This is the pattern the
  benchmarks already use to compare `InMemoryGraphStore` vs `HypertrieGraphStore`; an alternative
  scheme (Sonic/CoCo/CLTJ-style) lands as a peer store/evaluator and is measured end-to-end at
  that surface, build cost included.
- **The semi-naive reasoner's delta-joins** (one body atom over a delta index, the rest over the
  full index — see [`owl-inference-seams.md`](owl-inference-seams.md)) become a dedicated
  evaluator variant over the concrete cursor machinery, not a parameter of a shared driver.
- **The instrument is kept**: `WcojQuerySoak` (`--profile-wcoj-query`) measures the join driver's
  rows/s and queries/s in-process for sampling profilers, alongside the BDN grid.

**Granularity is the design crux, now with a measured boundary.** Delegate seams remain right
where they are crossed once per item with real work behind them
([`feedback_hotpath_sync_behind_delegates`] — hashing a value, matching a whole pattern, executing
a whole query). A seam crossed several times per solution row with property-read-sized work behind
it costs ~10–20% of the hottest path; that altitude is below the delegate line.

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
dotnet-trace/PerfView CPU stacks; `WcojQuerySoak` (`--profile-wcoj-query`) gives the join driver's
rows/s and queries/s in-process; `Workbench/HypertrieSoak` gives steady-state GC. A new backend
compares the way `BuildBenchmark` already compares the two stores — as a peer store/evaluator
measured at the query surface — so "does this backend win" is answerable end-to-end. Mirror
HoneyComb's methodology: a `cores` axis (scalability 1→N), the WCOJ query-shape
grid (triangle / 4-loop / 4-clique / Loomis-Whitney), the share/order ablation, and **including
index-build time in the measurement** — over our BIM-/DPP-shaped workloads
([`project_scale_performance_target`]), against a *healthy* hypertrie baseline.

## Phased plan

1. ~~Extract the delegate seam~~ — **done and reverted by measurement (2026-06-04)**: the
   extraction was behaviour-identical (full suite + differential property tests green) but cost
   11–22% on the hot path; the numbers and trace analysis are in the Decision section above.
2. **Baseline captured** (2026-06-04, `tempdocs/baseline-bdn`): the BDN grid + `WcojQuerySoak`
   rows/s numbers above are the reference any future backend or driver change measures against.
3. **Add one alternative backend as a peer store/evaluator** — start with a Sonic-style hash-trie
   (per-query build) or the CoCo columnar sorted-array (one parallel sort; SIMD-amenable), measured
   end-to-end at the query surface with build cost included.
4. **Later, separately measured**: SieveJoin pre-pass (Seam C — a pre-pass composes at the query
   boundary, not per row); HoneyComb parallel layer (Seam D); Diamond adaptive driver (Seam B).

## Caveats and non-goals

- **Generics vs templates — measured.** Delegates cost the inlining C++ templates get, and with a
  class-typed cursor the shared-generic (`__Canon`) body adds dictionary lookups per row. The one
  .NET mechanism that recovers template-grade codegen is the struct-constrained generic (above) —
  held in reserve until a second backend exists to share the driver with. Until then: concrete hot
  loop, seams at the evaluation boundary.
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
