# HANDOFF — continuation notes

This file is the cross-machine continuation note for in-progress engine work. It is **git-tracked on
purpose** so an agent on a fresh clone can read it and continue. The fuller working notes and design
records live in a `tempdocs/` tree that is **gitignored and does not travel** — so everything needed to
resume the current thread is summarized here.

Keep this file current: when an increment ships, record it under "Recently shipped" and move the pointer
in "Next".

---

## Current state

- Branch `main`, tree clean at commit **`e43578b`** (Elias-Fano succinct primitive; `e3e95d2` Free Join generic join full-depth, `793206a` GHT foundation, `3ff1f61` open-addressed join table, `ef037a7` depth-2 chain factorization, `08d0db1` Yannakakis). **§4.3 design pass done** (`tempdocs/free-join-design-memo.md`).
- Full suite: **8097 / 0 failed / 548 skipped**.
- **Conformance (measured 2026-06-09, not from memory):** zero failures anywhere. W3C SHACL **120/120**, Turtle 328, NQuads 205, RDF 146 — 100%. SPARQL 1221/1248 (27 optional-feature skips), OWL 986/1430 (444 entailment-regime skips). The old "SHACL 63/120 / SHACL-SPARQL open" memory was **stale** — SHACL is fully conformant; corrected.
- Full suite: **8083 total / 0 failed / 548 skipped** (the new factorized-join + open-addressed-table tests over the pre-§4.2 count; the skip count jitters ±2 with the environment-conditional OWL-entailment `Inconclusive` tests). Factorization is opt-in (default-off): the corpus routes stars/chains through the streamed path by default.
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

### Decided — factorization is opt-in; strategy selection is a research issue

`PreferFactorizedStar` shipped default-on (`3a521e0`) but the measurement showed it **regresses low-fan-out
(property-table) stars**. Resolved (`31d2a12`, owner call): **flipped default-off**. Factorization stays a
built, tested, opt-in technique; **choosing among the join strategies — a cost-based optimizer plus a
user-force override — is a deferred research issue**, to start once more techniques and self-test code exist.
The seam already exists in skeleton: `QueryEnginePolicy` (the proto-policy) plus the
`QueryTraceEventKind.EngineSelected` trace bus ("the feedback an adaptive selection policy learns from"). A
cheap fan-out estimate is derivable from the columnar index's CSR per-level counts; the measured break-evens
(`fan²/3`, `(fanA·S)/(fanA+S)`) are the thresholds. Do **not** build the selector ad hoc in the pipeline — it
is its own increment. (See memory `join-strategy-selection-is-a-future-research-issue`.)

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

**Next on §4.3:** (a) **leaf vectors** — the binary-join interpolation: a relation at depth < full carries
its remaining columns as a leaf tuple vector, cross-producted at emit; this is the half that makes Free Join
*beat* WCOJ on acyclic shapes. (b) **pipeline wiring** — `FreeJoinExecutor` behind `PreferFreeJoin`
(default-off), the existing engines as oracle. (c) **factorized output** (`FactorizedBatch`) — §4.2 ∩ §4.3.

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

#### Column-encoding axis — the bits/triple spectrum (owner-requested next)

- **Elias-Fano column codec — primitive shipped (`e43578b`), wiring is the follow-up.**
  `Collections/EliasFanoSequence` — the near-entropy succinct encoding for monotone sequences: low bits
  packed, high bits a sampled-`select₁` gap bit-vector; `Access` + `NextGEQ` (the successor / leapfrog
  `Seek`). Measured (`--profile-elias-fano`): **9–11 bits/value at u/n 64–256 (the "~10")** vs
  `FrameOfReference` ~17–19 — a ×1.7–2.5 size win, right on the EF bound + ~1 bit (the select sample).
  Standalone, not wired. **Follow-ups (recorded):**
  1. **`select₀`-based `NextGEQ`.** Today's `NextGEQ` is a naive binary-search over `Access` (O(log n)
     selects), measured **×0.3–0.6 (2–3× slower seek)** than `FrameOfReference.LowerBound`. The proper EF
     successor jumps via `select₀` to the high-part block then scans low bits — O(1)+short — which should
     close most of that gap. Add a `select₀` sample alongside the `select₁` one.
  2. **Wire EF behind `BlockPackedColumnReader`** for the **monotone** CSR columns — `level0Values` (distinct
     sorted, globally monotone) and the **offset** columns (cumulative, monotone) slot in directly;
     `FrameOfReference` stays the differential oracle. This turns the ~10 into a real index-memory win.
  3. **Partitioned EF** for `level1Values`/`level2Values` — only *piecewise*-monotone (reset per parent
     group), so EF must partition at the group boundaries the offsets already mark. The deepest value column
     dominates bits/triple, so this is where the memory actually moves.
  (Memory `column-encoding-bits-per-triple-spectrum`.)
- **The Ring (owner-interested, after EF).** The wavelet-tree WCOJ self-index — one structure indexing all
  orders at ~one order's space, the succinct end carried furthest. Bigger than a codec (a whole index), but
  the natural follow-on to EF on this axis; it loses cheap any-order access for space (three-rotation echo
  already in-tree as `ColumnarOrderSetMode.ThreeRotations`).

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
