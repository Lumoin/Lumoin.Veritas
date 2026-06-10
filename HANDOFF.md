# HANDOFF — continuation notes

This file is the cross-machine continuation note for in-progress engine work. It is **git-tracked on
purpose** so an agent on a fresh clone can read it and continue. The fuller working notes and design
records live in a `tempdocs/` tree that is **gitignored and does not travel** — so everything needed to
resume the current thread is summarized here.

Keep this file current: when an increment ships, record it under "Recently shipped" and move the pointer
in "Next".

---

## Current state

- Branch `main`, tree clean at **`f76fb8b`**. This session shipped the §4.3 Free Join arc: `255c2f4` **(a)** routed Free Join behind a default-off `PreferFreeJoin`; `6ad4ca7` **(b)** factorized-star output (the §4.2 ∩ §4.3 convergence, `FreeJoinExecutor.ExecuteFactorized` / `FreeJoinPipeline.RunFactorized`); `69ce5e5` **(c)** flat-1D `FactorizedBranches`; `82eaf32` **(d.1)** `FactorizedArena` (query-scoped bump arena); `b18c612` **(d.2)** `FactorizedBranches` values onto `ArenaSlice`, §4.2 routes self-dispose at the stream boundary; `176d0a2` **(d.3)** `FreeJoinExecutor.Drive` onto per-level reusable flat buffers — zero per-bound-value allocation; `3d9d64a` **(d.4)** nested flatten reads flat branches in place + reusable materialise buffers (no per-row tuples); `f76fb8b` **(d.5)** group keys + branch metadata onto the arena, `ExecuteFactorized` fills branch runs in place (`FactorizedBranches.Allocate`/`BranchSpan`). The §4.3(d) explicit-memory arc is COMPLETE: keys, metadata, and values all share the one query-scoped arena lifetime; executor and flatten scratch are per-level/per-slot reusable buffers.
- **Measured after (d.5)** (`--profile-factorized`, Release, 2026-06-10, all rungs MATCH): two-pattern join ×4.5–8.5 time / ×7.2–12.4 alloc at fan 40–60; star ×43.8–93.3 time / ×54.7–138.8 alloc at fan 20–30 (×133–300 compression); chain ×5.4–14.3 time / ×7.1–7.8 alloc at the two-sided fan-out rungs (×22.5–28.6 compression). No-win controls unchanged in character: fan=1 rungs still lose (×0.1–0.9 time) — factorisation stays opt-in for the deferred selector.
- **Deeper factorisation SHIPPED (`f629420`):** `ExecuteFactorized` generalised from the single-key star to a **star with chain extensions** — an extension relation (two-column trie over a centre branch variable then a fresh variable) nests as a depth-2 sub-batch under that branch, with the semijoin drop and empty-branch group kill; `FreeJoinPipeline.TryPlanFactorizedOrder` tries candidate centre keys in first-appearance order, so a four-hop chain factorises around its middle with BOTH branches nested (a shape neither §4.2 route covers). Differentials: 3-hop chain, star+chain hybrid, 4-hop chain vs leapfrog AND the flat `Run`, all compressing; triangle still declines.
- **Data-storage axis, SIMD pass SHIPPED (`a878c3f`).** Grounding first corrected the stale plan: the EF + partitioned-EF **wiring was already done** — `ColumnarTripleIndex.Build(..., ColumnarValueColumnEncoding.EliasFanoWhenMonotone)` (opt-in, default `FrameOfReference`) offers whole-column EF at level 0 and partitioned EF at levels 1/2 over the parent-offset boundaries, each column keeping the smaller packing. The genuinely missing piece was the SIMD pass, now in (+ layering fix `49f49c8`): shared `BitSelect.InWord` (hardware bit-deposit select, portable clear-loop fallback) under every sampled select of BOTH sequences; the lane kernels reach `EliasFanoSequence` by **injection** — Collections declares `BitLanePacker`/`BitLaneUnpacker`, `Build` takes them optionally (portable without), and `BlockPackedColumn` injects `backend.Pack`/`backend.DecodeFrame` at its EliasFano build — so Collections never references Columnar (the recorded layering rule; a direct reference was committed then corrected). `Decode` bulk-unpacks low bits via the injected unpacker (zero base) after a scalar head to word alignment. **Measured: EF seek vs FoR ×3.6–5.3 (was ×2.6–3.1), sizes unchanged; partitioned EF healthy, ×2.32–3.03 at fan-out 32–8192.**
- **`EliasFanoWhenMonotone` is now the DEFAULT (`f611fa5`, owner said proceed).** `ColumnarTripleIndex.Build` + the internal `ColumnarOrder` builders default to the succinct policy; `FrameOfReference` stays selectable (differential baseline / build-time-sensitive callers). Measured basis: **74.6 → 54.2 bits/triple** (ThreeRotations, 106.7 → 77.5 MiB at 12M triples); community graph (200k nodes, clustered ids, 5% cross edges) **59.9 → 44.1 bits/triple ≈ 124 → 91 B/node**; seek ×3.6–5.3 faster on qualifying columns; cost = +20–30% view-build time at 6–12M triples (packing candidates), faster at 1.5M. The WHOLE conformance corpus ran under the new default — 8140 green, answers identical. Revert = the two parameter defaults in `ColumnarTripleIndex.Build`/`ColumnarOrder.Build`/`BuildConcatenated`.
- **Join-strategy selector SHIPPED with a redirecting research finding (`d21db5e`).** `JoinStrategySelector` + `JoinStrategyChoice` + `QueryEnginePolicy.AdaptiveJoinStrategies` (default-off): the estimator reads exact per-key fan-outs off the CSR columns (order `[bound, key, other]`, two offset differences), engages the factorised star/chain routes above threshold compression (`StarEngageRatio`/`ChainEngageRatio` = 4.0), conservative on anything it cannot read; the rendezvous consults it at both batched planning sites; answer-identity proven both regimes. **THE FINDING (`--profile-join-selector`):** with the flat output held constant, the factorised ROUTES are time-neutral-to-slightly-slower (±~30%) at every fan-out — flattening dominates; the earlier ×43–93 wins measured building the factorised form WITHOUT flattening. So factorisation pays only where the compressed form survives past the join (aggregation/COUNT, selective downstream operators, late materialisation) — **the valuable follow-on is factorised CONSUMERS, not sharper gating.** Adaptive engagement is storage-positive and time-safe but modest; default-off stands.
- **Factorised consumers, increment 1 SHIPPED (`ebe9506`):** `ColumnarBatchPipeline.TryCount(index, query)` counts without flattening when the shape factorises (plans with both detections on, builds via the now-shared `TryBuildFactorizedStar`/`TryBuildFactorizedChain`, returns `FlatRowCount`; constraints→0, non-factorisable→null/drain). **Measured (`--profile-join-selector` count rungs, flat output ~1.5M held constant): ×2.6 at fan 4, ×6.7 at 8, ×18.9 at 12, ×24.1 at 20 vs draining; parity at fan 1–2; all MATCH** — the consumer-side win the flatten-bound finding predicted.
- **Factorised consumers, increment 2 SHIPPED (`3dd6c64`): the SPARQL `COUNT(*)` interception.** The algebra driver's expand phase short-circuits a qualifying `AggregateJoin` — implicit (key-less) `Group` over a plain `Bgp`, every aggregation a non-DISTINCT `COUNT(*)` — answering from `QueryEngineRendezvous.TryCountBatched` (same gating as the batched query path → `ColumnarBatchPipeline.TryCount`) without materialising the BGP's solutions. The BGP encoding prologue is now a shared `EncodeBgp` helper (`EncodedBgp`), so the fast path applies every normal-path guard: unencodable constant → 0; triple-terms/self-joins/type-expansions/named-graph/access-control/DISTINCT/grouped/non-COUNT(*) → decline to normal. Whole conformance corpus green with the interception live.
- **→ RESUME HERE — next candidates:** (1) **late materialisation over factorised intermediates** in the Sparql operators — the bigger factorised-consumer step: carry the compressed form through selective operators (Slice/LIMIT, Filter on key columns) before flattening; (2) **The Ring** (owner-interested storage axis); (3) the **succinct set-code catalog** below (owner-requested). Smaller: partitioned-EF build-side `Pack` reuse; id-clustering as a bulk-load pass.
- Full suite **8148 / 0 failed / 546 skipped** at `3dd6c64`.
- **Conformance (measured 2026-06-09, not from memory):** zero failures anywhere. W3C SHACL **120/120**, Turtle 328, NQuads 205, RDF 146 — 100%. SPARQL 1221/1248 (27 optional-feature skips). OWL `W3cOwl2` conformance suites **881/1323, 442 skip, 0 fail** — the 442 are correct cross-suite abstentions (367 RL + 65 species) + 10 corpus contradictions, **NOT** unimplemented regimes; the 2 genuine Direct-semantics entailment tests (`WebOnt-I4.6-005-Direct`, `WebOnt-equivalentClass-008-Direct`) fixed 2026-06-09 by projecting away the conclusion's annotations in `W3cOwl2RlTests` before the RL embedding check. The old "SHACL 63/120 / SHACL-SPARQL open" and "444 OWL regimes unimplemented" memories were **stale** — both corrected.
- **Known flaky test (pre-existing, NOT from this session's changes):** ~1 full-suite run in ~4 fails exactly 1 test; never reproduced when hunted (6 clean differential-suite runs + 4 clean full runs). Likely a method-level-parallel race (the suite runs `Workers=0`, all cores). **NOT yet named** — I filtered it out of the one failing run before realising. Mitigation now in place: **every full-suite run captures FULL output** (`dotnet test ... 2>&1 | Out-File <tmp>` then grep the file), so the next failure is named with its CsCheck seed if applicable; root-cause it then. HARD RULE: never paper over (no retries, no timeouts, no dismiss-as-flaky) — root-cause or discuss. The "it's a CsCheck differential test" hypothesis was tested and **cleared**. Factorization stays opt-in (default-off); the corpus routes stars/chains through the streamed path by default.
- One unrelated working-tree change persists: `generate-local-test-nuget-packages.ps1` (a sister project's edit excluding the AOT CLI from local packaging) — not mine, left uncommitted on purpose.
- There are local commits not pushed to origin. **Do not `git push`** without explicit owner approval —
  pushing triggers paid CI and the budget is exhausted. Commit locally; validate with the full local
  suite instead.
- Work cadence: implement → build → run the full suite → commit locally → next. Stop and ask only for:
  origin pushes, releases, reordering owner-set priorities, destructive operations.

---

## Repository orientation

- `src/Lumoin.Veritas.Core` — the data plane: hypertrie store, the columnar read-optimized view, the
  worst-case-optimal (leapfrog) join, the batched scan-and-hash pipeline, encoding/`TermId` dictionary.
- `src/Lumoin.Veritas.Sparql` — the SPARQL 1.2 query language engine (parser → AST → algebra →
  `SparqlQueryEngine`). Conformance-complete; it is the correctness oracle for execution changes.
- Other `src/` projects: OWL, SHACL, RDF, JSON/CBOR families, the CLI/MCP/HTTP app. Not involved in the
  current thread.
- `test/Lumoin.Veritas.Tests` — the single test assembly (MSTest). The conformance corpora live here.
- `test/Lumoin.Veritas.Benchmarks` — soaks dispatched by `--profile-*` flags from `Program.cs`.

### The execution substrate (what the current work builds on)

A multi-pattern Basic Graph Pattern (BGP) is one algebra leaf carrying N triple patterns; the joins
between its patterns are evaluated **inside** the BGP by the data plane, not as algebra `Join` operators.
Two engines coexist there:

- **Leapfrog WCOJ** — worst-case-optimal, used for cyclic shapes (e.g. triangles).
- **Batched scan-and-hash pipeline** (`Core/Columnar/ColumnarBatchPipeline`) — used for **acyclic** shapes,
  which a GYO reduction detects. It scans each pattern into column-major `SolutionBatch`es and joins them
  left-deep with `SolutionBatchJoin` (a packed 1–2 variable `JoinKey` hash join). This is the measured
  winner on acyclic shapes.

Key column-major types in `Core/Hypertrie/Execution`: `SolutionBatch` (uint columns, 1024-row blocks,
positional schema over `Variable`), `SolutionBatchHashTable` (chained build/probe), `JoinKey` (packs 1–2
`uint` into a `ulong`). `Core/Columnar/ColumnarBatchScan` streams a pattern's matches as batches.

The SPARQL operator layer (above the BGP) carries results as `SolutionTable`
(`Sparql/Execution/SolutionTable.cs`): a SPARQL-native column-major `uint`/`TermId` table over a
`SparqlVariable` schema (cell `0` = unbound) **or** a materialized row list; `AsRows()` is the single
decode bridge. The columnar operators (Project/Distinct/Slice/Union/Join/Filter/Minus/LeftJoin/group
partition) live in `Sparql/Execution/ColumnarOperators.cs`. Decode (`TermId → RdfTerm`) happens once at
the output boundary — survivors only, never per intermediate row.

**Design rule for the whole substrate:** the hot path is **concrete** (concrete batch/table types and
method signatures), never per-batch virtual/interface dispatch. An in-driver delegate seam was measured at
−11–22% and reverted. "Interface" in prose means the API *shape*, not a C# `interface`. Keep every
operator "consume batch/table → produce batch/table" so a factorized batch variant slots in without
re-signing (see Next).

---

## Recently shipped — Factorized intermediates §4.2 (first increment)

The representation + the factorizing join + the differential oracle, as a **standalone primitive not
yet wired into `ColumnarBatchPipeline`**. Shipped in one commit:

- **`ccbe7f3`:**
  - `Core/Hypertrie/Execution/FactorizedBatch.cs` + `FactorizedGroup.cs` — a union of product nodes over
    a schema partitioned into **key columns** and independent **branch column groups**. Each group stores,
    per key tuple, the branches that vary independently given that key, so it pays the **sum** of the branch
    sizes for what flattens to their **product** of rows. `Flatten()` walks each group's cross product via a
    recursion-free mixed-radix odometer back into the `SolutionBatch` stream — the differential oracle.
  - `Core/Hypertrie/Execution/FactorizedBatchJoin.cs` — the factorizing counterpart of `SolutionBatchJoin`:
    identical output schema and `JoinKey` packing (1–2 shared variables), but groups by key and keeps the
    build matches and probe matches as two branches instead of their cross product. Branch-union multiplicity
    is preserved (scans are duplicate-free), so flatten reproduces the flat join row-for-row.
  - Tests (`test/Lumoin.Veritas.Tests/Columnar/FactorizedBatchJoinTests.cs`): flattened factorized join ==
    `SolutionBatchJoin.HashJoin` as a multiset on a fan-out fixture and on a fully-bound build side (a
    zero-column branch carrying only multiplicity); plus an odometer cross-product unit test.
  - Soak (`test/Lumoin.Veritas.Benchmarks/FactorizationSoak.cs`, `--profile-factorized`, Release): on a
    fan-out join the factorized build measures **×20–30 compression** (rows/stored-tuples), **×4.8 time,
    ×8–15 alloc** vs materializing the flat product; the no-fan-out rung (fan=1 on one side) shows ×1.0
    compression and the honest grouping overhead (~×0.4 time) when nothing fans out.

- **`77249b0` — the consume side (`FactorizedBatchJoin.AddBranch`):** extends a `FactorizedBatch` by one
  pattern that joins on its **group key** (a star step) — the probe must share exactly the key variables, so
  the key stays put and the probe's per-key matches attach as a fresh branch; non-matching groups drop
  (the semijoin a star entails). Returns `null` when the probe joins on a *branch* variable (that needs the
  deeper multi-level form). A chain of `AddBranch` evaluates a multi-pattern star with no flat product
  between joins. Differential: a 3-arm star flattens to the flat `ColumnarBatchPipeline` result as a
  multiset; soak measures **×133–300 compression, ×5.8–46 time, ×55–140 alloc** vs the flat cubic
  intermediate.

- **`3a521e0` — wired into the pipeline:** `ColumnarBatchPipeline.TryPlan(useFactorizedStar)` detects a
  star (`TryStarKey`: ≥3 patterns, the first two share 1–2 vars, every later pattern shares exactly that
  key — no branch-variable join) and records it on `ColumnarBatchPlan.StarKey`, taking precedence over the
  Yannakakis tree. `Run` routes a `StarKey` plan through `RunFactorizedStar` (`Join` → chain of `AddBranch`
  → `Flatten`), with a defensive fall-back to `RunStreaming`. `QueryEnginePolicy.PreferFactorizedStar` gates
  it, **on by default**; `QueryEngineRendezvous` passes it through both batched entry points. Answer-identical,
  so the whole conformance corpus is the oracle (full suite 8076 green).

- **`ef037a7` — multi-level (depth-2) factorization:** a chain factorizes when the next join's variable
  lives inside a branch, not the group key. `FactorizedGroup` gains an optional parallel `NestedBranches`
  array — a branch may be a nested single-level `FactorizedBatch`; `FactorizedBatch.HasNestedBranches`
  (decided once at construction) routes `Flatten` to the flat odometer or a nested path that materialises
  each nested sub-batch's rows before the cross product (no recursion — the depth-2 guarantee keeps the
  sub-batch single-level). `FactorizedBatchJoin.NestBranch(left, branchVariable, probe, probeSchema)`
  regroups the branch binding `branchVariable` into a sub-batch keyed on it, attaching the probe's per-value
  matches; returns `null` outside the step (already nested, not a single-column branch, or a wider join).
  Chain soak: **×22–28 compression, ×2.3–5.7 time, ×7.8–8.5 alloc** vs the flat streamed chain. Standalone.

- **`112716b` — measurement, no-win controls (the `08d0db1` standard):** the soak now characterizes WHERE
  factorization wins. Star compression is `fan²/3`, chain compression `(fanA·S)/(fanA+S)` with `S=fanB·fanC`.
  **Key finding:** at fan=1 the factorized form is *strictly worse* — stores 3× (star) / 2× (chain) the flat
  rows and runs ~×0.2–0.7 the speed; the chain needs fan-out on BOTH the independent arm AND the sub-tree
  (one-sided is ×1.0). So this is a **specialized** optimization, not a free win — break-even at star fan≈1.7.

### Decided — factorization is opt-in; the selector landed and redirected the research

`PreferFactorizedStar` shipped default-on (`3a521e0`) but the measurement showed it **regresses low-fan-out
(property-table) stars**. Resolved (`31d2a12`, owner call): **flipped default-off**. The deferred selector
research issue then landed its first increment (`d21db5e`): `JoinStrategySelector` (its own component, not
ad hoc in the pipeline) with the CSR-statistics fan-out estimator and threshold gating behind
`QueryEnginePolicy.AdaptiveJoinStrategies` (default-off). Its soak (`--profile-join-selector`) produced the
**redirecting finding**: flatten-bounded plans — which the batched BGP routes always are — do not reward
factorisation at any fan-out (±~30%, output production dominates); the technique's measured wins (×43–93)
belong to consumers that KEEP the compressed form. The research issue therefore continues as **factorised
consumers** (aggregation over groups, late materialisation in the Sparql operator layer), with the selector's
estimator and thresholds ready to gate those routes where the consumer qualifies. (See memory
`join-strategy-selection-is-a-future-research-issue`.)

- **`b3308cf` — chain pipeline route (opt-in):** `TryPlan(useFactorizedChain)` detects a three-pattern chain
  (`TryChainNestVariable`: first two join on 1–2 vars, the third shares exactly one variable — a branch, not
  the key) and records it on `ColumnarBatchPlan.ChainNestVariable`, mutually exclusive with the star. `Run`
  routes it through `RunFactorizedChain` (`Join` → `NestBranch` → `Flatten`, fall-back to `RunStreaming`).
  `QueryEnginePolicy.PreferFactorizedChain` gates it, **default off** like the star. Pipeline test agrees with
  streaming/leapfrog. The opt-in technique catalog is now: streaming, semijoin-reduced, factorized-star,
  factorized-chain, leapfrog — all built, all tested, selection still manual via the `Prefer*` flags.

**Next on §4.2:** depth-3+ factorization — `NestBranch` returns `null` on an already-nested input, so a
four-pattern chain factorizes only its leading joins (`TryChainNestVariable` caps at three patterns). Lifting
this needs the nested-path flatten to handle arbitrary depth via an explicit stack rather than the
single-level sub-batch shortcut, and `NestBranch` to nest inside an existing nested branch. Then **§4.3 Free
Join** (the largest item; unifies the binary hash join and the leapfrog WCOJ — depends on the factorized
representation, now in place). Keep the hot path concrete. The strategy-selection optimizer remains the
deferred research issue (memory `join-strategy-selection-is-a-future-research-issue`).

## Recently shipped — Yannakakis semijoin reduction (roadmap item §4.1)

The first item of the beyond-textbook join tier. On an **acyclic** BGP, Yannakakis' two semijoin passes
strip every "dangling" tuple (one that cannot reach a full answer) before the join runs, so no
intermediate exceeds the input or output size. Built in three commits:

- **`2dc408b` — foundation (two pure, unit-tested Core primitives, 11 tests):**
  - `Core/Hypertrie/Execution/SolutionBatchSemijoin.cs` — `Reduce(target, targetSchema, probe,
    probeSchema)` keeps the target relation's rows whose 1–2 shared-variable `JoinKey` occurs in the
    probe. Reuses the batched join's key packing; output is always a subset of the target; batches are
    full-except-last. Throws if the schemas share 0 or >2 variables (the caller guards via the join tree).
  - `Core/Columnar/GyoJoinTree.cs` — `TryBuild(edges)` runs the GYO ear-removal with **witness tracking**:
    a variable in a single surviving edge is dropped; an edge then contained in another is an ear, absorbed
    into that witness (the recorded parent). Returns a `GyoJoinTree(Parent[], PostOrder)` (children before
    parents) or `null` for cyclic / disconnected / over-wide-separator shapes.
  - Tests: `test/Lumoin.Veritas.Tests/Columnar/SolutionBatchSemijoinTests.cs`,
    `GyoJoinTreeTests.cs`.

- **`c921cfb` — wiring into the pipeline:**
  - `ColumnarBatchPlan` carries an optional `GyoJoinTree`.
  - `ColumnarBatchPipeline.TryPlan(index, query, useSemijoinReduction)` attaches the tree for **≥3 scan
    patterns** only (a 2-pattern join's single intermediate is its output — no gain). The tree is built
    over the execution `Order` so its indices address the materialized relations directly.
  - `ColumnarBatchPipeline.Run` dispatches: tree present → `RunReduced` (materialize each relation →
    upward semijoin pass over the post-order, reducing each parent by its children → downward pass over the
    reverse, reducing each child by its parent → left-deep join over the now globally-consistent
    relations); tree absent → the unchanged `RunStreaming`.
  - `QueryEnginePolicy.PreferSemijoinReduction` gates it, **on by default**; both batched entry points in
    `QueryEngineRendezvous` pass it through.
  - It is answer-identical to the unreduced stream (reduction only removes tuples that cannot reach a full
    answer), so the whole conformance corpus exercises it as the oracle. Differential test
    (`ColumnarBatchPipelineTests.SemijoinReductionAgreesWithStreamingAndLeapfrog`): reduced == streamed ==
    leapfrog on a 3-pattern chain; the plan attaches a tree only with reduction enabled at ≥3 patterns.

- **`08d0db1` — measurement** (`test/Lumoin.Veritas.Benchmarks/YannakakisSoak.cs`,
  `--profile-yannakakis`, Release): on a chain `?a p1 ?x . ?x p2 ?b . ?b p3 ?c` whose middle join blows up
  but whose output is small:
  - selective, output 7,500 of ~500,000 intermediate: **×16.9 time, ×15.6 alloc** (39.6→2.3 ms,
    23.5→1.5 MiB);
  - a second selective rung: **×15.8 / ×24.7**;
  - no selectivity (output == intermediate == 500,000): **×1.0 / ×1.0** — the two passes cost ~3% (within
    noise) when nothing dangles, so default-on carries no regression on non-blow-up shapes.

### Open follow-up on §4.1 (correctness is fine; this is a sharpening)

`RunReduced` performs the final join in the greedy `plan.Order`, not strictly along the join tree. This is
**correct** — after the tree-driven full reduction the database is globally consistent for the acyclic
schema (acyclic + tree-edge pairwise consistency ⟹ global consistency), so any *connected* left-deep order
(which `plan.Order` already is) has every intermediate tuple extend to a full answer, bounding it by the
output. The intermediate-size guarantee therefore holds, and the differential + corpus are the proof.
A cleaner form would join directly along the tree, making the bound independent of the greedy planner's
order. Worth doing when the join order is next touched.

---

## Next — the rest of the beyond-textbook tier

Risk order on the columnar substrate:

### §4.2 — Factorized intermediates (in progress)

Represent intermediate results as a compressed **product-of-unions** instead of flat rows, so many-to-many
joins compress (and sometimes never materialize). More fundamental than late materialization: it changes
the intermediate *representation*, not just when decode happens. Gated on the columnar substrate, which is
in place.

**Shipped (`ccbe7f3` produce, `77249b0` consume, `3a521e0` pipeline-star, `ef037a7` depth-2 chain,
`b3308cf` pipeline-chain; see Recently shipped):** the `FactorizedBatch` representation + flatten oracle,
`Join` (×20–30 fan-out join), `AddBranch` (star, ×133–300), `NestBranch` (depth-2 chain, ×22–28), and both
pipeline routes gated by `PreferFactorizedStar` / `PreferFactorizedChain` — **both default-off** (factorization
is specialised: it regresses low-fan-out shapes, so engaging it per shape is the deferred selector research
issue, memory `join-strategy-selection-is-a-future-research-issue`).

**Remaining:** depth-3+ factorization — `NestBranch` returns `null` on an already-nested input, so deeper
chains factorize only their leading joins; lifting that needs the nested flatten to handle arbitrary depth
via an explicit stack and `NestBranch` to descend into a nested branch (a recursion-shaped tree transform,
written iteratively). Narrow payoff — weigh against §4.3. Keep the hot path concrete.

### §4.3 — Free Join hybrid

Unify the two join engines into one execution model: the columnar binary hash join (`SolutionBatchJoin` /
`ColumnarBatchPipeline`) and the leapfrog WCOJ (`ColumnarBasicGraphPatternEvaluator` /
`ColumnarTriejoinIterator`). Largest item; depends on the factorized representation (now in place).

**Design pass done** (memo `tempdocs/free-join-design-memo.md`). Key findings: both engines already share
the trie geometry, the planner, and the `Solution` output — they differ only in the per-level intersection
(leapfrog track-max over the *sorted* CSR vs Free Join's hash-trie probe). Free Join's **Generalized Hash
Trie** (leaf = tuple vector, internal node = hash map) makes depth-1 = binary hash join and full-depth =
WCOJ one structure; the executor interpolates. Central decision: **reuse the sorted CSR trie first** (option
2 — generalize the driver to mix depth-1 hash joins with deep leapfrog, reusing `ColumnarTriejoinIterator`),
then evaluate a hash-trie build (`OpenAddressedTable` nodes) where build cost pays. **Augment, not replace**:
new `FreeJoinExecutor` behind `PreferFreeJoin` (default-off), the two existing engines as the differential
oracle. Reusable pieces: `OpenAddressedTable<TValue>` (GHT node), `SolutionBatch` (GHT leaf),
`ColumnarBatchScan` (source), the planner/variable-order machinery. Staged plan (memo): (1) `FreeJoinPlan`
= variable order + per-relation depth that can *reproduce* each existing engine; (2) the generic-join
executor, differential vs batched (acyclic) and leapfrog (cyclic); (3) a minimal depth heuristic (real
selection = the deferred selector research); (4) hash-trie nodes where measured; (5) **factorized Free Join
output** (`FactorizedBatch`) — where §4.2 and §4.3 converge, the high-value payoff.

**Shipped: GHT foundation (`793206a`) + generic join, full-depth (`e3e95d2`).**
`GeneralizedHashTrie` — build one relation into a multi-level hash trie (`OpenAddressedTable` internal nodes
— gained an `internal` struct `Enumerator` — packed leaf vectors); `TrieColumns` hashed, `LeafColumns` at
leaves; depth-1 = binary-join shape, full-depth = WCOJ trie; `Flatten` (recursion-free) is lossless (5
tests). `FreeJoinExecutor.Execute(relations, variableOrder)` — the variable-at-a-time multiway hash
intersection (iterate the smallest node's keys, probe the others, descend together), explicit-stack DFS,
producing `SolutionBatch`es. **Full-depth (WCOJ form) only this increment**; agrees with leapfrog on
triangle/star/chain (3 tests). Took the **hash-trie route (option 1)** — self-contained, no leapfrog-driver
surgery.

**Shipped: leaf vectors (`FreeJoinExecutor` below full depth).** A relation's trailing columns drop into a
leaf tuple vector. At each variable the participants are the relations descending a trie node there AND the
at-leaf relations whose leaf carries that variable; the candidates are the intersection of the trie nodes'
keys and the leaf participants' distinct values (smallest set drives), so a join variable may live in a
relation's leaf — the binary-join build leaf and the WCOJ trie meet in one rule. `GeneralizedHashTrie` gained
`LeafTupleCount`/`LeafValue`. Differential vs leapfrog: star at depth-1, chain with a private terminal leaf,
chain all-depth-1 (leaf-meets-trie), and a diamond where `?z` is a leaf in two relations (leaf-leaf) — plus
the 3 full-depth tests, all green. Precondition (validated): trie levels follow the global order, and leaf
columns follow the relation's deepest trie level.

**Shipped: §4.3(a) pipeline wiring.** `Columnar/FreeJoinPipeline.Run(index, query)` scans each pattern into a
full-depth GHT (every column a trie level, ordered by the `ColumnarRotationPlanner` global order) and drives
`FreeJoinExecutor`; returns `null` for a rotation-incompatible shape (the boundary the other columnar engines
draw). Routed behind a default-off `PreferFreeJoin` on `QueryEnginePolicy` in `QueryEngineRendezvous.Query`
(`TryQueryFreeJoin`, ahead of the batched path, `QueryEngineKind.FreeJoin`, flatten via
`SolutionBatch.FlattenAsync`). Differential: vs leapfrog on a triangle+chain through the pipeline, and vs the
default routing through the rendezvous with the flag flipped (`FreeJoinPipelineTests`). Default-off, so the
corpus is unaffected unless enabled; full depth reproduces WCOJ — no speed edge yet, the per-relation-depth
interpolation toward the binary join is the deferred selector's lever.

**Shipped: §4.3(b) factorized output (star).** `FreeJoinExecutor.ExecuteFactorized` / `FreeJoinPipeline.RunFactorized`
emit a single-level `FactorizedBatch` for a single-key star — the one variable shared by every pattern bound
first, then each relation's matches under a key as an independent branch — the §4.2 ∩ §4.3 convergence (the
same `FactorizedGroup`/`FactorizedBatch` the §4.2 `FactorizedBatchJoin` produces). A 2-pattern chain centres on
its shared middle variable and factorises too; a shape with no variable in every pattern (triangle, 3-hop
chain) declines to `null`. Differential vs leapfrog AND the flat `Run`: the star compresses (51 stored / 126
flat), the chain agrees 1-1, the triangle declines (`FreeJoinPipelineTests`). Per-key scratch hoisted out of the
key loop. NOT wired into a route — a tested building block, like §4.2's factorising join.

**Shipped: §4.3(c) `FactorizedBranches` (flat-1D).** The jagged `uint[][]` branch values + the parallel
`int[] BranchRowCounts` on `FactorizedGroup` are replaced by a single `FactorizedBranches` (flat `values` +
per-branch `starts`/`rowCounts`/`strides`), so a group holds one values array instead of one per branch. Both
producers — §4.2 `FactorizedBatchJoin` (Emit/Extend/Nest via `Of`/`Append`/`WithCleared`) and §4.3
`ExecuteFactorized` — and the `Flatten`/`BranchRowsOf` consumer read through it; the layout is now internal to
one type. Behaviour-preserving: both differential suites + the soak compile/green (8125/0/546 full).

**Shipped: §4.3(d) explicit memory ownership, increments 1+2.** The settled design — ONE query-scoped arena, no
per-buffer `IMemoryOwner`/`IDisposable` cascade (a first `IDisposable`+`IMemoryOwner` attempt was built then
**reverted**: `IDisposable` on the shared `FactorizedBatch` made CA2000 demand disposal at every consumer
including the §4.2 lazy-`Flatten` path, for GC-backed inert churn) — is now in place end to end. `82eaf32`
**(d.1)** `FactorizedArena`: a bump arena over `VeritasMemoryPool<uint>` handing out value-type `ArenaSlice`s
(backing array + offset + length, cursor-style reads — no `Memory<uint>`, no per-rental owner), every slab
returned in one `Dispose`; 7 lifecycle tests incl. a counting pool. `b18c612` **(d.2)** `FactorizedBranches`
holds its values as an `ArenaSlice`; `Of`/`Append`/`WithCleared` allocate from the arena (append/clear allocate
a fresh run and abandon the superseded one — bump semantics, reclaimed at the single disposal); all producers
thread the arena (`FactorizedBatchJoin.Join`/`AddBranch`/`NestBranch`, `FreeJoinExecutor.ExecuteFactorized`,
`FreeJoinPipeline.RunFactorized` caller-owned); the §4.2 routes `RunFactorizedStar`/`RunFactorizedChain` own a
query-scoped arena over `VeritasMemoryPool<uint>.Shared` as lazy iterators (`using` + `yield`), so disposal
fires when the consumer drains or abandons the stream — the self-disposing flatten boundary. Lifecycle tests pin
superseded-run readability and slab return. Rust target: bump arena + `&[u32]`.

**Shipped: §4.3(d.3) executor hot path (`176d0a2`).** `FreeJoinExecutor.Drive` allocates nothing per bound
value: the per-value `Frame` class (five arrays + a participant concat per descent step), the per-intersection
closure + `HashSet[]` + `List<uint>` keys, and the per-narrowing `List<int>` are replaced by one reusable
**`LevelFrame` per global level** — the stack is always the contiguous level prefix, so one live frame per
level suffices. Its candidate-keys buffer (with cursor), saved participant state, cleared-and-refilled leaf
value sets, and arrival/narrow row buffers are flat arrays + counts, grown on demand, reused across visits.
Aliasing safety: `Apply` writes into the frame's own buffers and always reads a shallower level's (state is
restored before each re-visit). The scratch is plain arrays grown geometrically, NOT `VeritasMemoryPool`-rented
— renting the per-level buffers is a possible follow-up if measurement warrants; the arena is wrong for this
scratch (bump-only, never frees, while these buffers are rebuilt per visit).

**Shipped: §4.3(d.4)+(d.5) — the memory tail (`3d9d64a`, `f76fb8b`).** (d.4) `FactorizedBatch.FlattenNested`
reads flat branches in place and materialises a nested branch's sub-rows row-major into a per-branch-slot
reusable flat buffer — the per-row `uint[]` tuples and `List<uint[]>` are gone. (d.5) `FactorizedGroup.KeyValues`
is an `ArenaSlice` (producers stage the tuple in bounded stack scratch, copy via `FactorizedArena.AllocateFrom`);
`FactorizedBranches` packs starts/rowCounts/strides into one arena metadata run beside the values run; the new
writer pair `FactorizedBranches.Allocate(rowCounts, strides, arena)` + `BranchSpan(branch)` lets
`ExecuteFactorized` fill each branch run in place from the trie nodes (no transient per-branch arrays). The
`GroupBuilder`/`ProbeBucket` `List<uint>` accumulators stay GC-transient BY DESIGN: they grow during the build
under a hash table of live keys, which a bump arena cannot serve.

**Shipped: deeper factorisation (`f629420`).** `ExecuteFactorized` accepts a star whose branches may each be
extended by chain relations: centres are two-column tries over the key then a distinct branch variable, an
extension a two-column trie over one centre branch variable then a fresh variable (depth two only — an
extension on the key, on another extension's variable, or between two branches declines). An extended branch
carries zero flat rows and a nested single-level sub-batch keyed on the branch variable, one inner branch per
extension; branch values absent from any extension drop (the chain's semijoin), and a branch left valueless
kills its group before the flat branches are written. The planner (`TryPlanFactorizedOrder`) tries candidate
centre keys in first-appearance order — any accepted key is a correct grouping — which lets a four-hop chain
factorise around its middle with both branches nested. Differential tests: 3-hop chain, star+chain hybrid,
4-hop chain (two nested branches in one group) vs leapfrog and the flat `Run`, all compressing.

**Next on §4.3:** the cost-based per-relation depth/selector that picks Free Join's depth and the engaging of
the factorised routes per shape (the deferred join-strategy-selection research issue, its own arc).

### After the tier

- A deterministic, seed-stable partition hash seam, scoped to the partition/persistence boundary only (for
  distributed HyperCube/shuffle joins); local ephemeral join tables stay on the randomized default hash.
  Lands when distribution work starts; decoupled from the tier.
- A radix-partitioned / open-addressing (Swiss-table) join-table swap behind the existing concrete index
  APIs, with `Dictionary` kept as the differential oracle. **First cut measured (`3ff1f61`,
  `--profile-jointable`):** `Collections/OpenAddressedTable<TValue>` (reusable ulong-keyed open-addressed
  table) + `OpenAddressedBatchHashTable` over it. The ~1.5–3× estimate did **not** hold — with a realistic
  shuffled probe (probing in insertion order falsely prefetched the chained entries, a 14× artifact) the
  result is: probe **parity** (×0.94–1.20, open ahead when cache-resident), open allocates ~2× **less** and
  is more compact (18–34 B/key), but open **build** is slower (×0.30–0.65, incremental rehash). A
  memory/probe-parity win paid for by build cost — a per-workload trade for the selector, not a clean swap.
  Kept as a tested primitive + characterization soak; **not wired into `SolutionBatchJoin`**. A real probe
  win would need the SIMD control-group scan / lower load factor of a full Swiss-table; build is improvable
  by pre-sizing. The `OpenAddressedTable<TValue>` primitive is reusable for the other ulong-keyed maps
  (semijoin set, factorization buckets).

#### Column-encoding axis — the bits/triple spectrum (owner-requested)

- **Elias-Fano column codec — primitive, wiring, and SIMD pass ALL SHIPPED.**
  `Collections/EliasFanoSequence` (`e43578b`) — the near-entropy succinct encoding for monotone sequences:
  low bits packed, high bits a sampled-`select₁` gap bit-vector; `Access` + `NextGEQ` (the successor /
  leapfrog `Seek`). **9–11 bits/value at u/n 64–256** vs `FrameOfReference` ~17–19 (×1.5–1.8 size).
  1. ✅ **`select₀`-based `NextGEQ`** (`a0b31fa`): a `select₀` jump to the target high group + a low-bit scan.
  2. ✅ **Wiring** — `ColumnarTripleIndex.Build(..., ColumnarValueColumnEncoding.EliasFanoWhenMonotone)`
     (opt-in, default `FrameOfReference`): whole-column `BlockPackedColumnMode.EliasFano` for the
     globally-monotone level-0 column, `PartitionedEliasFano` for the within-group level-1/2 columns over
     the parent-offset boundaries, every column keeping the smaller of the candidate and its
     frame-of-reference packing (`ColumnarOrder.BuildValueColumn` / `SmallerEncoding` — the candidate can
     never enlarge a column). Offset columns stay `PrefixedDeltas`.
  3. ✅ **SIMD pass** (`a878c3f`, layering fix `49f49c8`): shared `BitSelect.InWord` — hardware bit-deposit
     select where available, portable clear-loop otherwise — under every sampled select of both sequences;
     the lane kernels reach EF by **injection** (Collections-declared `BitLanePacker`/`BitLaneUnpacker`
     delegates, `BlockPackedColumn` passes `backend.Pack`/`backend.DecodeFrame`; portable paths without) so
     Collections never references Columnar; `Decode` bulk-unpacks a run's low bits through the injected
     unpacker (zero frame base) after a scalar head to word alignment. **Measured: seek vs
     `FrameOfReference.LowerBound` ×3.6–5.3 (was ×2.6–3.1) across u/n 4–1024, sizes unchanged; partitioned
     EF ×2.32–3.03 at fan-out 32–8192, picked by the index at L2.**
  4. ✅ **Default flip** (`f611fa5`, owner-approved): `EliasFanoWhenMonotone` is the default everywhere
     (`ColumnarTripleIndex.Build` + internal `ColumnarOrder.Build`/`BuildConcatenated`); `FrameOfReference`
     selectable as the baseline. **74.6 → 54.2 bits/triple** (ThreeRotations, 12M triples 106.7 → 77.5 MiB);
     clustered-with-outliers community graph **59.9 → 44.1 bits/triple (~124 → 91 B/node at 200k nodes)**;
     whole corpus green under the new default. Cost: +20–30% view-build at 6–12M triples.
  **Remaining on this codec:** partitioned-EF build-side `Pack` reuse needs word-aligned group starts (a
  format change — measured trade); sparser select samples (~rate 256: 8.5 b/val, still ×2.4 seek) as an
  optimizer knob. (Memory `column-encoding-bits-per-triple-spectrum`.)
- **Succinct set-code catalog (owner-requested 2026-06-10): implement the tighter-compressing families as
  measured building blocks, to learn empirically where each breaks and at which boundaries.** Candidates, each
  as a `BlockPackedColumnMode` (or bitvector backend) behind the footprint-driven `SmallerEncoding` selector,
  with characterization soaks in the `--profile-elias-fano-columns` style:
  1. **Binary interpolative coding** as a BLOCK mode — beats partitioned EF on clustered postings by
     ~1–1.5 bits/value but decodes sequentially (each code's width depends on its enclosing range). The key
     architectural insight: the reader is already decode-block-then-search (`BlockPackedColumnReader`'s block
     cache), so a sequential-only codec is usable as a block mode — a cold seek pays one block decode instead
     of EF's two selects; the descent's locality (cached block) amortises it. SIMD does NOT give it random
     access, but sibling subtrees decode independently once the parent midpoint is known, so within-block
     parallelism opens after the first levels.
  2. **Interleaved ANS over gaps** as a block mode — the entropy-optimal end of the family EF approximates.
     The standard SIMD answer to its sequential decode is N interleaved ANS streams decoded in lanes
     (vectorises throughput well); same block-mode boundary for access. Pairs with the
     `ColumnarKernelBackend` seam.
  3. **RRR-backed bitvectors** — the fully-indexable-dictionary floor with rank/select; constants hurt in the
     sparse n ≪ u regime but it is the right backend candidate for the RING's wavelet-tree bitvectors (dense,
     locally clustered), so build it on that arc, not before.
  Expected boundaries to pin by soak: per-seek latency (block decode vs select-jump), bits/value vs PEF on the
  clustered/outlier corpora, build cost; the win region is likely cold/archival tiers and the Ring's internals.
- **The Ring (owner-interested, after EF).** The wavelet-tree WCOJ self-index — one structure indexing all
  orders at ~one order's space, the succinct end carried furthest. Bigger than a codec (a whole index), but
  the natural follow-on to EF on this axis; it loses cheap any-order access for space (three-rotation echo
  already in-tree as `ColumnarOrderSetMode.ThreeRotations`).

#### OWL DL reasoning arc — toward full OWL2 DL (state-of-the-art; partly the other-agent thread)

The route to closing the ~367 OWL Direct-Semantics conformance abstentions (see Conformance above — "full OWL"
needs a DL reasoner; nothing is failing, the RL runner just abstains from non-RL entailment). **Already in
this clone and green:** `Core.Sat.SatSolver` + `SatLiteral` (a DPLL/CDCL-style SAT solver — the "naive SAT
resolver"), `Owl/Reasoning/SyntacticLocalityModule` (locality-based module extraction, the scalability key),
`Owl/Reasoning/AlcModuleReasoner` (a worklist tableau deciding **ALC(H)** over a locality module — internalized
TBox, copy-on-branch disjunctions, blocking, clash; `ModuleVerdict.UnsupportedConstructs` names what's
excluded), `Owl/El/ElClassifier` (OWL EL), `Owl/Reasoning/DescriptionLogicDelegate` + `ReasoningRendezvous`
(the whole-engine seam), plus RL entailment + RDFS materialization. **Remaining trajectory:** (1) the
**SAT-backed tableau** sibling (per-world propositional abstraction over `SatSolver`, conflict clauses instead
of snapshots — recorded in the `AlcModuleReasoner` doc); (2) extend **ALC(H) → SROIQ(D)** construct by
construct (qualified cardinality, inverses, role chains/RBox, nominals, self, keys, datatypes, negative
assertions); (3) **hypertableau** (HermiT-style and-branching control) as the state-of-the-art target; (4)
wire the DL reasoner into a **Direct-Semantics OWL2 conformance runner** so the abstained cases get decided.
A major multi-increment arc; the seam (`DescriptionLogicDelegate`) is already the integration point.

---

## Build / test / run

Commands are PowerShell on Windows (the dev box). Paths below are relative to the repo root; adjust to the
clone location. The test exe is invoked by absolute path on the dev box to match a permission allowlist —
on another machine just run it from `test/Lumoin.Veritas.Tests/bin/Debug/net10.0/`.

- Build a project (own call, errors only):
  `dotnet build src/Lumoin.Veritas.Core/Lumoin.Veritas.Core.csproj -clp:ErrorsOnly --nologo`
  Build the test assembly (compiles Core + Sparql + tests):
  `dotnet build test/Lumoin.Veritas.Tests/Lumoin.Veritas.Tests.csproj -clp:ErrorsOnly --nologo`
- Run a filtered slice of tests:
  `test/Lumoin.Veritas.Tests/bin/Debug/net10.0/Lumoin.Veritas.Tests.exe --filter "FullyQualifiedName~GyoJoinTree" 2>&1 | Select-Object -Last 10`
- Run the full suite (it takes ~1–2 minutes; run it before every commit):
  `test/Lumoin.Veritas.Tests/bin/Debug/net10.0/Lumoin.Veritas.Tests.exe 2>&1 | Select-Object -Last 8`
- Run the Yannakakis soak (Release):
  `dotnet build test/Lumoin.Veritas.Benchmarks/Lumoin.Veritas.Benchmarks.csproj -c Release -clp:ErrorsOnly --nologo`
  then `test/Lumoin.Veritas.Benchmarks/bin/Release/net10.0/Lumoin.Veritas.Benchmarks.exe --profile-yannakakis`

The build treats analyzer findings as errors (e.g. CA1859 wants concrete return/parameter types on
private helpers; CA1305 wants invariant formatting; CA1303 flags `Console.WriteLine` of an all-literal
string — embed a runtime value). Fix at the source; do not suppress.

---

## Conventions (enforced; match the surrounding code)

- Switch **expressions** always, never C-style `switch` statements.
- **No recursion** — walk trees/graphs with an explicit `Stack`/`Queue` work-list.
- Properties (`{ get; }` / `{ get; set; }`) over naked fields; naked fields only where `ref`/`Interlocked`
  semantics require them, with a comment naming the reason.
- XML `<summary>` docs on **every** function and accessor, including private and test helpers. Comments
  state the contract/invariant — never narrate change history.
- Blank line before a `return` and after a closing `}` when another statement follows. No banner comments.
- Compare over `Utf8String` bytes/spans, not `System.String`; materialize a string only for a real
  transform. Well-known literals get one canonical home, not inline re-typing.
- Thread recoverable/expected conditions as return values; reserve exceptions for genuine invariants.
- Build components in final form (no compat shims, no backwards-compat modes); migrate all callers.
- In **tracked** docs and code: cite specs and papers and describe the implementation in its own terms; do
  not name external products/toolkits/implementations.
- Commit messages: no AI co-author trailer. Do not push to origin without approval.
