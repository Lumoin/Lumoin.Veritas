# Handoff — Lumoin.Veritas.Sparql (Batch 1: Milestones A–C, SHACL-SPARQL unlock)

Branch: `sparql-query-engine` (off `main`). Executing the SPARQL implementation
specification. Two-batch split: **Batch 1 = Milestones A–C** (lexer → parser →
AST → algebra → translator → hypertrie backend + executor subset → SHACL-SPARQL
unlock). **Batch 2 = Milestones D–F** (full algebra/aggregation, CONSTRUCT/
DESCRIBE/JSON results, conformance push).

## ⏵ CURRENT WORK — Parser error-recovery batch (prerequisite before Milestone B)

A separate handoff doc, **"Parser error recovery — Handoff"** (companion to
`Sparql.Spec.md`; the user holds it — re-paste on resume for the full spec),
retrofits *both* the Turtle and SPARQL parsers from throw-on-error to
recovery-with-diagnostics. (Naming note: that doc calls itself "Batch 2"; it is
*not* the "Batch 2 = Milestones D–F" below — it's a prerequisite slotting in
before Milestone B so the algebra translator is built against possibly-error
AST from day one.) This is the cross-cutting error-model rework deferred earlier
(see [[project_sparql_ast_faithful]]); it also subsumes the parser side of the
`Core/Lexing/` consolidation.

**Status: Step 1 done** (commit `2a4bc69`) — `Core/Diagnostics/`: `Diagnostic`,
`DiagnosticSeverity`, `DiagnosticBag`, `WellKnownDiagnostics` catalogue + tests.
**Steps 2–6 remain:** (2) lexer recovery — `Error` tokens, remove
`recoverFromErrors`, `LexDiagnostic`→`LX####` bridge; (3) Turtle parser recovery
— error-node AST variants per abstract base, resync tables per frame kind,
`ParseResult`, test sweep; (4) SPARQL parser recovery — same on the larger
parser (the conformance jump lands here); (5) `SparqlInvalidRequestException`
type; (6) docs audit (every code linked + referenced by name).

**Q1–Q3 resolved (locked):**
- **Sequencing:** do error-recovery now; 4b-ii's four remaining known-gap
  features (standalone reified `<< >> .`, VALUES/expression triple terms,
  `VERSION`) land *after*, on the recovered parser.
- **Engine:** `SparqlQueryEngine` / `ExecuteAsync(string)` / `SparqlEvaluationContext`
  / `AlgebraOperator` / `SparqlQueryEngineOptions` **do not exist yet** (Milestone C).
  Land Core + lexer + both parsers + the `SparqlInvalidRequestException` *type*;
  put `MaxDiagnostics` as a parser-level option; **defer §7/Step-5 `ExecuteAsync`
  wiring** until the engine is built.
- **Streaming:** keep the resumable pull (`FeedToken`/`TryParseRequest`/Turtle
  `TryParseStatement`) with a **shared `DiagnosticBag`** the consumer reads; the
  static `ParseRequest(text)` is a facade over the instance API that drains the
  bag into a `ParseResult`.

**Codebase adaptations (locked — the handoff doc assumes some things that differ):**
- `WellKnownDiagnostics` codes are pool-free `Utf8String` the **`Vocabulary` way**
  (`new("LX0001"u8.ToArray())`) — there is **no `WellKnownMediaTypes` and no
  `ToUtf8String()`** in the tree.
- Enums are **not `: byte`** (CA1028 rejects it); the grouped nested catalogue
  classes carry the same **CA1034 suppression** `Vocabulary.Xsd` uses.
- Frame steps return **`StepResult`** (`Done`/`Push`/`Continue`), *not* `ParseStatus`
  (which is the coarse `Continue`/`NeedMore`/`Produced` streaming enum). Error nodes
  flow via `StepResult.Done(errorNode)` — **no new status enum needed**.
- `DiagnosticBag` is **append-only, no dedup** (§3 over §6.4's conflicting "guard in
  Add"); cascade suppression is the **parser's** job.
- **Internal-invariant guards stay throws** (`"reached unknown stage"` etc. are bugs,
  not user syntax errors); only genuine syntax errors become diagnostics.

**§10 open questions — self-decide and record when reached:** per-frame max-errors
count; English-only messages (note the `MessageKey` i18n seam); `ErrorPrologueDeclaration`
reparenting; the Turtle `LexDiagnostic`→`LX####` bridge table; algebra-input error
validation (decide at Milestone B).

## Spec reconciliation — the full implementation spec arrived after Milestone A was built

The authoritative implementation spec was provided after the lexer/parser/AST and
the query-syntax conformance harness were already built this session. The built
work is essentially the spec's **Milestone A and beyond** and aligns with the
locked decisions (byte-resumable lexer, iterative frame-stack parser, algebra as
the future public contract, no co-author trailers, switch-expression dispatch).
The following diverge from the spec body and must be reconciled before the
algebra/executor milestones build on the AST.

**Will align to the spec (no decision needed):**
- A static `SparqlParser.ParseRequest(text/bytes, pool?, documentId?, baseIri?)`
  facade over the existing resumable instance machinery (§6, §9.1).
- The §7.0 documentation convention: every token kind and grammar-production AST
  record carries a `<remarks>` block citing its SPARQL 1.2 §19.8 anchor. Retrofit
  across lexer / AST / parser; verify by the grep-the-anchors audit.
- `VERSION` prologue declaration (`VersionKeyword` + `VersionDecl`, §4.3 / §7.0) —
  currently absent from the lexer and parser.
- Spans on every AST node (the "spans apply to everything" decision; Slice S).
- Namespace/folder layout per §5.1 (`Algebra/`, `Translation/`, `Execution/`, …).

**Resolutions (confirmed by the user; all three instincts upheld):**

1. **Collections / blank-node property lists — faithful AST holds.** Keep
   `CollectionTerm`, `BlankNodePropertyListTerm`, and the reified-triple shorthand
   as faithful AST nodes with source spans. Sugar lowering is a **dedicated
   normalization pass between parsing and algebra translation** — not in the parser,
   not in the translator. It uses `BlankNodeDelegate` for fresh blank nodes and
   preserves spans onto the lowered triples. After normalization the
   translator-input term set is the four core cases (`ConstantTerm`,
   `VariableTerm`, `PropertyPathTerm`, `TripleTermTerm`). **Spec §8.5 was updated**
   to describe this post-normalization shape; take the updated §8.5 as authoritative.
2. **Built-in / aggregate identity — enums.** Two parallel representations: the
   lexer still interns the canonical name as a `Utf8String` token payload (keep);
   the **parser maps payload-name → enum value at the AST construction site**. The
   enum is the AST-level identity. Exhaustive `switch` for dispatch; the algebra-JSON
   contract gets a deterministic lexical form per enum value, deserializer rejects
   unknown values. Mapping table is one source file.
3. **Pre-A now — standalone PR (§4.4).** `Core` gets `BlankNodeDelegate`,
   `BlankNodeRequest`, `VeritasBlankNodes`, `RandomnessDelegate`, `RandomnessRequest`,
   `RandomnessValue`, `VeritasRandomness` + the four banned-symbols entries. Same PR
   retrofits the Turtle parser (delete `nextSyntheticBlankNode` /
   `AllocateSyntheticBlankNodeLabel`) and the SPARQL parser's `nextBlankNode`; both
   take `BlankNodeDelegate` (default `VeritasBlankNodes.System`). Green-criterion:
   existing Turtle tests pass under both no-delegate and explicit-`System` forms;
   SPARQL parser tests pass after the substitution.

**Execution order (then Milestones B, C):**
1. Pre-A (standalone PR). **DONE** — committed on `sparql-query-engine`.
2. Built-in/aggregate enum realignment in the SPARQL AST (lexer payloads unchanged). **DONE** — `Ast/SparqlFunctions.cs` (`BuiltInFunction`/`AggregateFunction` + name↔enum mapping).
3. §7.0 documentation-convention pass (every terminal/production cites its §19.8
   anchor; grep audit at end of Milestone A).
4. Slice 4b-ii — RDF 1.2 lexical completeness: `~` (Tilde) reifier token, `VERSION`
   keyword + version-label, any §19.8 annotation brackets (audit terminal-by-terminal,
   no skips); then vendor `sparql12/syntax-triple-terms-{positive,negative}/`.
5. Early-expansion normalization pass (§8.5; uses Core's `BlankNodeDelegate`; closes
   the standalone-bnode known gap).
Slice S (spans on remaining AST layers) is non-blocking; interleave with 2–5.
Then B (algebra + translator, §10–§11) and C (hypertrie backend + executor subset,
§16–§17) — C runs against the Core delegates from Pre-A, no mid-batch Core round-trip.

## Status

- **Pre-A (Core injection seams): DONE.** `Lumoin.Veritas.Core/Injection/` adds the
  `BlankNodeDelegate` (→ `Utf8String`; `BlankNodeRequest` carries the interning pool
  and a correlation key for `BNODE(literal)` per-solution semantics),
  `RandomnessDelegate` (`RandomnessKind`/`Request`/`Value`), and `IdentifierDelegate`
  (→ `Guid`; `IdentifierPurpose`/`Request` — the abstract identifier type is deferred,
  YAGNI) seams, each with a `Veritas*` defaults static (`System`/`Zero`/`Seeded`|
  `Sequential`, plus `VeritasBlankNodes.ByCallSite`). The entropy/identity bans
  (`Random` ctors + `Random.Shared`, `RandomNumberGenerator`, `Guid.NewGuid`) live in
  a dedicated `BannedSymbols.Entropy.txt` applied to every project **except** the
  Benchmarks/Workbench harnesses (where seeded `Random` for synthetic fixtures is
  idiomatic — mirrors the existing `BannedSymbols.Cbor.txt` carve-out); the Core
  `RandomSources` seam, the `Veritas*` producers, and two synthetic-data test classes
  suppress RS0030 with justification. Retrofitted producers: Turtle + SPARQL parsers
  take an optional `BlankNodeDelegate` (default `System`; the per-parser
  `nextBlankNode`/`AllocateSyntheticBlankNodeLabel` counters are gone);
  `SessionId.NewId`, `HypertrieGraphStore.Query`, and `ShaclValidatorOptions` take an
  `IdentifierDelegate`. 10 new `VeritasInjectionSeamsTests`; full suite green — the
  50 failures are the pre-existing SHACL-conformance baseline, unchanged by this work.
- Milestone A: **in progress.** Project skeleton builds. **Lexer complete and
  tested** (13 `SparqlLexerTests` green). **AST complete** — ~60 record types
  across 12 files in `Ast/`. **Parser: the full SPARQL 1.1 query grammar is built
  and tested** (`SparqlParserTests` plus the W3C query-syntax conformance suites —
  198 Sparql tests, 185 green, 13 known-gap skips, 0 fail), and it is
  **resumable/streaming** — incremental
  `FeedToken` + `TryParseRequest`(`ParseStatus`), a `Drive()` that suspends on
  `NeedMore` with the work stack intact, bounded `MaxStepLookahead=4`, every
  unbounded production driven one bounded item per step. Materialised
  `ParseRequest` is a convenience on top. Built so far (each its own commit):
    - Prologue (BASE/PREFIX with prefix-map + base IRI resolution via Core
      `IriResolver`; unbound prefix throws).
    - All four forms: SELECT (DISTINCT/REDUCED, `*` or projection list incl.
      `(expr AS ?v)`), ASK, CONSTRUCT (`{ template }` and the `CONSTRUCT WHERE`
      short form), and DESCRIBE (`VarOrIri+` / `*`, optional WHERE).
    - Dataset FROM / FROM NAMED.
    - WHERE group graph pattern + all members: nested groups, OPTIONAL, MINUS,
      `{ } UNION { }`, GRAPH, SERVICE [SILENT], FILTER(expr), BIND(expr AS ?v).
    - Full expression precedence climber: || && comparisons(non-chaining)
      + - * / , unary ! + -, bracketed; built-in calls, iriOrFunction calls,
      IN/NOT IN, IF/COALESCE/BOUND, EXISTS/NOT EXISTS, aggregates
      (COUNT(*)/DISTINCT/GROUP_CONCAT SEPARATOR), via an `ArgumentList` frame.
    - Solution modifiers: GROUP BY / HAVING / ORDER BY (ASC/DESC) / LIMIT / OFFSET.
    - Property paths: alt `|`, seq `/`, inverse `^`, quantifiers `?*+`, grouped
      `( )`, negated property sets `!(...)`; a bare predicate unwraps to a
      constant term.
    - Collections `( ... )` and blank-node property lists `[ p o ; ... ]` as
      faithful un-expanded AST term nodes (Slice 4b-i), nesting recursively in
      subject and object position. Term-layer **source spans** are collected
      (full extents via `lastConsumedSpan` + `CombineSpans`); the heterogeneous
      work-stack hand-up reads through a documented `Pop<T>(incoming)` helper.
    - VALUES (inline member + trailing block, one-var and full tuple forms, UNDEF,
      with row-arity and distinct-variable checks) and sub-SELECT (a `{ SELECT ... }`
      group reusing the request frame, finalising at the enclosing `}`).
    - **W3C query-syntax conformance wired** (Slice 5c): the SPARQL 1.1
      `syntax-query` (94) and SPARQL 1.2 `syntax` (6) suites are vendored
      (`Material/Sparql/`, pinned commit `674da26`) and run through the
      manifest-driven harness — **87/100 pass**, 13 self-correcting known-gap skips
      (see `W3cSparqlSyntaxTests.KnownGaps`: scope analysis, nested aggregates, a
      PN_LOCAL-escape refinement, and the standalone blank-node list awaiting the
      expansion pass).
  - **Style decisions recorded** (apply going forward): the parser uses C# switch
    *expressions* for all dispatch (state + token kind), per user preference; and
    no `Co-Authored-By` trailers on commits/PRs (user directive — branch history
    was rewritten to strip them).
  - **Remaining for Milestone A:**
    - Slice 4b-ii — RDF 1.2 reification. **In progress, committed incrementally:**
      - **DONE (lexer):** `~` (Tilde), `{|`/`|}` (Open/CloseAnnotation, NeedMore-aware),
        `VERSION` keyword; `<< >> <<( )>>` were already lexed. + lexer tests.
      - **DONE (triple terms):** `<<( s verb o )>>` wired through the compound-term
        mechanism (so it parses in triple subject/object, collection items, and
        bnode-list objects). `StepTripleTerm` frame; `Ast.TripleTerm` node (already
        existed). Inner subject is a leaf (no nesting), verb is IRI/`a`/var (no
        paths), object is a leaf or nested triple term. + 8 parser tests.
      - **DONE (reified triples + reifiers):** `<< s verb o ~r? >>` as a faithful
        `Ast.ReifiedTriple(Span, Inner, Reifier?)` term (`Reifier` null = anonymous),
        `StepReifiedTriple` frame; subject/object may nest a reified triple or triple
        term; rejected in expression position. + 7 parser tests.
      - **DONE (object annotations):** `s p o ~r {| pol |}` as a faithful
        `Ast.AnnotatedObject(object, Annotation[])` with `Annotation` /
        `ReifierAnnotation` / `AnnotationBlock` — **mirrors the Turtle parser** (NOT
        the earlier "converge onto TriplePattern" sketch, which the user rejected as
        unfaithful). The Triple frame reads the `( ~id? | {| pol |} )*` tail after each
        object; the block reuses the bnode-list pol shape via `StepAnnotationBlock`.
        Annotations require a non-path verb. + 6 parser tests.
      - **DONE (conformance vendored):** `syntax-triple-terms-{positive,negative}` +
        `version` vendored under `Material/Sparql/` and wired into the harness. The
        suite found one real bug (triple-term subjects MAY nest — the WHERE-context
        `TripleTerm` differs from the VALUES-context `TripleTermData`), now fixed.
      - **Remaining (tracked as self-correcting known-gaps in `KnownGaps`):**
        - **Standalone reified triple** `<< s p o >> .` (empty property list; 12 tests
          `basic-{anon,}reifier-08..13`). Needs the BGP to hold a subject-only
          assertion — Turtle allows a subject statement with an empty predicate-object
          list; SPARQL's `BasicGraphPatternBlock.Triples` is `List<TriplePattern>`, so
          this needs a small block-shape decision (hold the standalone `ReifiedTriple`
          alongside the triples). Do NOT unwrap at parse (faithful).
        - **Triple terms in VALUES** (`basic-tripleterm-05`) — wire the VALUES
          data-block value parser to accept `<<( )>>` (note VALUES `TripleTermData` is
          more restrictive: no nested-triple-term subject, no variables — see the
          `tripleterm-subject-*` negatives).
        - **Triple terms in expressions** (`expr-tripleterm-01`,`-06`) — the primary
          expression parser needs a `<<( )>>` arm and an expression node wrapping a
          triple term.
        - **VERSION prologue decl** (`version-01..06`) — `VERSION "…"` after/with the
          prologue; only a short `"…"` string label (`"""…"""`/`'''…'''` are bad,
          per `version-bad-*`). `version-01/02` also embed a standalone `<< >>` so
          they need the standalone fix too.
    - Slice S — **spans on the remaining AST layers** (graph-pattern members,
      expressions, property-path nodes, clauses, query forms, request), applying
      the same `frame.StartSpan` + `lastConsumedSpan` + `CombineSpans` mechanism,
      so coverage is uniform across the whole AST (per user: spans apply to
      everything; the Turtle AST already spans every node via `TurtleAstNode`).
    - Early-expansion normalization pass — **"have both"**: lower
      `CollectionTerm`/`BlankNodePropertyListTerm` (and reified-triple shorthand)
      into fresh blank nodes + `rdf:first`/`rest`/`nil` and `[ ]` triples once, so
      the translator/backend see plain triples and don't each special-case the
      sugar. Likely lands with the algebra translator (Milestone B). Closing this
      also fixes the `syntax-propertyPaths-01` known gap (standalone bnode list).
    - When 4b-ii lands, vendor the SPARQL 1.2 triple-term syntax suites
      (`sparql/sparql12/syntax-triple-terms-{positive,negative}/`) and drop their
      query-only tests' Update entries.
- Milestones B–F: not started.

## Verified against the codebase (so the spec's §22 assumptions are grounded)

- **Project conventions.** `Directory.Build.props` is net10.0, `Nullable`,
  `TreatWarningsAsErrors`, AOT-compatible, `AnalysisMode=All`, banned-API +
  serialization-ban analyzers wired for all projects (STJ banned outside
  `Json`/`Cbor`). Per-project csproj is minimal (RootNamespace + Description +
  ProjectReferences). `InternalsVisibleTo Lumoin.Veritas.Tests` is global.
- **`Lumoin.Veritas.Sparql.csproj`** created → refs `Core` + `Rdf` only (per
  spec §5.1), added to `Lumoin.Veritas.slnx`. **Builds clean.**
- **Network vendoring works.** `git ls-remote https://github.com/w3c/rdf-tests`
  reachable; `main` = `674da267646573441808560564451fe5cc77aef7` at check time.
  The SPARQL 1.2 suite will be vendored from a pinned commit under
  `test/Lumoin.Veritas.Tests/Material/Sparql/` when Milestone A reaches the
  syntax-conformance step.
- **Lexer template confirmed** (`src/Lumoin.Veritas.Turtle/Lexer/`): token kind
  is a `public enum` with `///` on every member; token is
  `public readonly record struct TurtleToken(Kind, SourceSpan, Utf8String Value)`;
  `LexStatus` is an **internal** enum (`Complete`/`NeedMore`/`Error`);
  `LexDiagnostic` is a `public readonly record struct(Code, SourceSpan, Detail?)`
  with an error-code enum and on-demand `GetMessage()`; limits are a sealed class
  exposing growth-guard delegates with protective defaults.
- **`SourceSpan`** (`Core/Sourcing/`): `readonly record struct` with byte +
  line/column endpoints (zero-based, half-open), `None`, `SingleLine(...)`.
- **Deeper seams located** (not yet deep-read; will verify at the milestone that
  consumes each): `Core/Hypertrie/Execution/BasicGraphPatternEvaluator.cs`,
  `Core/Hypertrie/Query/{Variable,VariableRegistry}.cs`, `Rdf/PropertyPath.cs`,
  `Rdf/PropertyPathEvaluator.cs`, `Core/GraphMatchOps.cs`,
  `Rdf/Values/RdfValueComparer.cs`, `Core/Hypertrie/{HypertrieSnapshot,
  HypertrieGraphStore}.cs`, `Core/Hypertrie/Tracing/QueryTraceEventKind.cs`,
  `Core/Diagnostics/TraceHandle.cs`.

## Decisions taken (per spec §21 — agent decides, records here)

- **§5.3 / §21.1 shared lexer types:** duplicate into the Sparql project
  (option a). Names: `SparqlLexStatus` (internal), `SparqlLexErrorCode`,
  `SparqlLexDiagnostic`, `SparqlLexerLimits`. Lifting the Turtle types into
  `Core/Lexing/` is deferred to a future cross-cutting batch. Rationale:
  self-contained project now, cheaper; the boundary is already marked.
- **Token vocabulary (refinement of §7.1):** built-in-function and aggregate
  names are NOT ~70 distinct enum members. They fold into two kinds —
  `BuiltInFunctionName` and `AggregateFunctionName` — each carrying the canonical
  upper-case name as its interned `Value`. This satisfies §7.1's intent (the
  parser sees them as clean terminals, dispatches on kind+payload, never confuses
  them with prefixed names) without an unmaintainable enum. `true`/`false` fold
  into `BooleanLiteral` and `a` into `A`, matching the Turtle precedent.
  Structural keywords carry distinct `XxxKeyword` kinds. `^` is `Caret` (the
  spec's §7.1 list mentioned both `Hat` and `Caret`; unified to one).
- **`ImplicitUsings` is OFF** for src projects (only the test project enables it).
  Every src file needs explicit `using` directives — e.g. `using System;` for
  `FormattableString`.
- **Full final form, no consumer-driven subsets (user directive).** The lexer is
  the complete byte-resumable implementation per §3 — `SequenceReader<byte>`,
  `NeedMore`/`atFinalBuffer`, the synchronous `Tokenize()` AND the pipe-fed async
  `TokenizeAsync` / `TokenizeWithRecoveryAsync` paths, recovery, limits — even
  though SHACL-SPARQL only needs synchronous whole-buffer tokenisation. A
  whole-buffer-only lexer was explicitly rejected. This principle applies to every
  component in the batch.
- **AST file granularity (follows the codebase, not spec §5.1's one-file-per-type
  listing).** `Rdf/PropertyPath.cs` co-locates its abstract base + 7 subtypes in
  one file, so the SPARQL AST groups each closed hierarchy into one file (12 files
  total) instead of ~60. This also lets the mutually-recursive cluster compile.
- **AST representation refinements (recorded):** built-in/aggregate function
  identity is a canonical-name `Utf8String` (matching the lexer payload), not an
  enum — the closed dispatch set belongs to the expression evaluator (§13/§14).
  `AggregateExpression` adds an `IsCountStar` flag for `COUNT(*)`.
  `ServicePattern.Endpoint` is a `GraphTerm` (VarOrIri) to accept `SERVICE ?v`
  for syntax conformance, though execution still throws. `ConstructQuery.Template`
  reuses `TriplePattern`. `FilterPattern` is a `GraphPattern` subtype (no separate
  `GraphPatternMember` discriminator); the translator collects filters per §18.2.2.
  The named-graph indirection is `GraphGraphPattern` (the spec's §5.1 list named it
  `GraphPattern`, colliding with the abstract base).
- **Verified Core APIs for the lexer:** `UnicodeConstants.{SurrogateRangeFirst
  =0xD800, SurrogateRangeLast=0xDFFF, MaximumCodePoint=0x10FFFF}` (public);
  `Utf8StringPool.Intern(ReadOnlySpan<byte>)` / `Intern(ReadOnlySequence<byte>)` /
  `Intern(string)` / `RentScratch(int)→IMemoryOwner<byte>` (public).

## Open verification to do at the consuming milestone

Before Milestone C (backend bridge) I must confirm verbatim, and record any spec
deltas here: `BasicGraphPatternEvaluator` constructor arity/params; the `Solution`
binding-row shape and how to read a variable's bound `TermId`; `Variable` /
`VariableRegistry` API; the `Planner` type (spec calls it `Planner` — not yet
confirmed by that name); `PropertyPath` extensibility (can `NegatedPropertySet`
be added — same project, so yes, but confirm the base is `abstract record`);
`RdfValueComparer` API; `GraphMatchOps` shape; `HypertrieSnapshot` acquisition;
`TraceHandler<T>` delegate and `QueryTraceEvent` (located `QueryTraceEventKind`
but not the event record itself).

## Next steps (Milestone A continuation)

1. Read `TurtleLexer.cs` body (resumable state-machine template).
2. Write lexer foundation: `SparqlTokenKind` (per spec §7.1), `SparqlToken`,
   `SparqlLexStatus`, `SparqlLexErrorCode`, `SparqlLexDiagnostic`,
   `SparqlLexerLimits`.
3. Write `SparqlLexer` (byte-resumable, iterative; keyword recognition strategy
   per the Turtle precedent — decision to be recorded here once the body is read).
4. Lexer unit tests, then AST records, then the iterative parser, then
   `SparqlParser.ParseRequest`.
5. Vendor the SPARQL 1.2 suite; wire `SparqlPositiveSyntax` /
   `SparqlNegativeSyntax` into the conformance harness; measure.
