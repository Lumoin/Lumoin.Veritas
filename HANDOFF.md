# HANDOFF — continuation notes

This file is the cross-machine continuation note for in-progress engine work. It is **git-tracked on
purpose** so an agent on a fresh clone can read it and continue. The fuller working notes and design
records live in a `tempdocs/` tree that is **gitignored and does not travel** — so everything needed to
resume the current thread is summarized here.

Keep this file current: when an increment ships, record it under "Recently shipped" and move the pointer
in "Next".

---

## Current state

- Branch `main`, tree clean at commit **`08d0db1`**.
- Full suite: **8070 passed / 0 failed / 546 skipped**.
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

### §4.2 — Factorized intermediates (the immediate next move)

Represent intermediate results as a compressed **product-of-unions** instead of flat rows, so many-to-many
joins compress (and sometimes never materialize). More fundamental than late materialization: it changes
the intermediate *representation*, not just when decode happens. Gated on the columnar substrate, which is
in place.

Where it slots: a **factorized batch variant** alongside `SolutionBatch`, produced and consumed by the
pipeline operators. The substrate was kept "factorization-aware" — every operator already has the shape
"consume batch → produce batch" — so the variant should slot in without re-signing the operator APIs. Keep
the hot path concrete (a concrete factorized type, not an interface), per the substrate design rule above.
Suggested first increment: the representation + a flatten-to-`SolutionBatch` bridge (the differential
oracle), then teach one operator (join) to produce it, measured on a fan-out-heavy fixture where the
factorized form is strictly smaller than the flat product.

### §4.3 — Free Join hybrid

Unify the two join engines into one execution model: the operator-layer binary hash join (the row-layer
`SolutionHashJoinIndex` and the columnar `SolutionBatchJoin`) and the leapfrog WCOJ inside BGPs. This is
the largest item and depends on the factorized representation being in place.

### After the tier

- A deterministic, seed-stable partition hash seam, scoped to the partition/persistence boundary only (for
  distributed HyperCube/shuffle joins); local ephemeral join tables stay on the randomized default hash.
  Lands when distribution work starts; decoupled from the tier.
- A radix-partitioned / open-addressing (Swiss-table) join-table swap behind the existing concrete index
  APIs — profile-gated, constant-factor (~1.5–3× estimate), with the standard library `Dictionary` kept as
  the differential oracle.

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
