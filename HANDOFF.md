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

**Status: Steps 1–3 done.**
- **Step 1** (commit `2a4bc69`) — `Core/Diagnostics/`: `Diagnostic`,
  `DiagnosticSeverity`, `DiagnosticBag`, `WellKnownDiagnostics` catalogue + tests.
- **Step 2** (lexer recovery, both lexers) — `SparqlTokenKind.Error` /
  `TurtleTokenKind.Error` added; `recoverFromErrors` removed and recovery made the
  only mode; the `TokenizeWithRecoveryAsync` method collapsed into an always-recovering
  `TokenizeAsync`; every lexer error site now records the (kept) `SparqlLexDiagnostic`/
  `LexDiagnostic`, emits an `Error` token spanning failure→resync via a shared
  `RecordErrorAndRecover` helper, and continues (no lexer-layer throw). The
  `LexDiagnostic`→`LX####` bridge is `SparqlLexDiagnosticBridge` / `TurtleLexDiagnosticBridge`
  (`ToDiagnostic(in …) → Core Diagnostic`), the single place that knows each
  `*LexErrorCode` enum. `WellKnownDiagnostics.Lexer` expanded to **fine-grained 1:1**
  `LX0001`–`LX0030` (the SPARQL∪Turtle condition union; LX0001–05 kept verbatim from
  Step 1, LX0005 `UnknownDirective` reserved/parser-level with no lexer emitter).
  Lexer-test sweep done (SPARQL 2 + Turtle 6 throw-tests → Error-token+diagnostic
  assertions; 3 Turtle async tests retargeted; 2 new bridge-coverage tests). **Full
  suite green: 0 new failures** (50 = the unchanged pre-existing SHACL baseline).
- **Step 3** (Turtle parser recovery — implementation landed) — `TurtleParser` recovery is
  value-based and the only mode: `DiagnosticBag` on both ctors + `ParseToResult()`; `Report`/
  `ReportRecoverable` (suppresses the parser code when the offending token is a lexer `Error` so the
  bridged `LX####` stands alone)/`ResyncTo`/`IsResyncToken` (resync table per `ParseFrameKind`)/
  `RecoverTerm`·`RecoverStatement`·`RecoverAnnotation`. Every recoverable throw site converted (structural
  → `Recover*` error node; validation → report-and-keep faithful term; unclosed list/graph-block at EOF →
  report-and-finalise-partial; missing-`.` → report+resync+keep the recoverable triple); invariants
  (unknown stage / unhandled frame / numeric-kind / `ConsumePrefixedName`+`ExtractPrefix` missing-`:` /
  DirLangTag missing-`--`) stay throws. `WellKnownDiagnostics.Turtle` expanded to TT0001–TT0027.
  **Emitter is now value-based too** (the user's call — [[project_turtle_emitter_value_based]]):
  `TurtleQuadEmitter` takes the shared `DiagnosticBag`, resolution is fallible (`RdfTerm?`/`NamedNode?`),
  unresolvable-relative-IRI/undeclared-prefix record `TT0026`/`TT0027` and skip, error nodes skip (no NRE).
  Reader: bare `ReadAsync` gained a **required** `DiagnosticBag` (bridges `lexer.Diagnostics` via
  `TurtleLexDiagnosticBridge`, threads it to parser+emitter, never throws on malformed input);
  `ReadWithSourceAsync` returns `ParseResult<TurtleDocument>`. Test sweep done: 5 throw-tests →
  `HasErrors`+code; the shared W3C runner keeps its catch-based negative detection via a test-only
  `TurtleConformanceReader` adapter that re-raises on `HasErrors` (NQuads/SPARQL delegates untouched);
  `TurtleReaderLimitsTests` stays a throw. **Key realisation while implementing:** a stray top-level closer
  (`}`/`)`/`]`…) is consumed by no frame, so `StepStatement` skips any token that fails `CanStartStatement`
  (and list frames push a Term sub-frame only on `CanStartTerm`) — this is what guarantees the work stack
  always progresses under malformed input. **Build clean (0 warnings, TreatWarningsAsErrors); 313/313
  Turtle tests pass; full suite 50 failed = the unchanged SHACL baseline, 0 new failures.**
  *Additive recovery tests landed* (`TurtleParserRecoveryTests`, 12 tests): per-frame resync
  (collection/BNPL/triple-term/annotation-block/graph-block/directive/missing-`.`/stray top-level `}`),
  recovery-continuation (a trailing clean triple still parses), cascade-suppression (bounded diagnostics
  for one unclosed `[`), real spans on every diagnostic, the emitter's `UndeclaredPrefix`, and a
  reactive-editor incremental feed (error node + diagnostic, then recovery keeps producing). 325/325
  Turtle tests pass. **Step 3 complete.**
**Steps 4–6 remain:** (4) SPARQL parser recovery — same on the larger
parser (the conformance jump lands here); (5) `SparqlInvalidRequestException`
type; (6) docs audit (every code linked + referenced by name).

**Step 2 decisions recorded (§10 + locked-adaptations follow-through):**
- **LX granularity (§10 Q4 + the user's explicit call):** fine-grained, one `LX####`
  per distinct lexical condition (≈1:1 with the internal enums), *not* coarse buckets —
  so an editor branches on the code alone (quick-fix / per-code severity / dedup) without
  parsing the message, and the lexer layer stays as specific as the future `EX####`
  layer. Splitting a coarse code later would be the breaking change; widening is additive.
- **Bridge ownership (§5.1/§5.3):** the lexer keeps emitting its rich
  `SparqlLexDiagnostic`/`LexDiagnostic` (preserving the on-demand `GetMessage()` and the
  fine enum) via `Diagnostics`; the enum→`LX####` mapping lives only in the bridge helper.
  The parser-owned shared `DiagnosticBag` threading is deferred to Steps 3–4 (the parser
  owns the bag); Step 2 ships the bridge but does not yet wire it into a parse.
- **Bridge completeness:** the `MapCode` switch has a throwing `_` default (an unmapped
  code is an internal bug — "internal-invariant guards stay throws"); the bridge-coverage
  test enumerates every enum value, so a missing mapping fails a test rather than compiling.
- **Error-token shape:** `Error` token `Value` is the empty `Utf8String`; its `Span` covers
  failure-start→resync-end (the offending run). The matching diagnostic is correlated by
  span in `Diagnostics`. Parser treats `Error` as a resync point (Steps 3–4).
- **Transitional green-branch (Step 2 only):** the unchanged parsers throw on the
  unexpected `Error` token, so negative parser/conformance tests stay red-on-bad-input;
  Steps 3–4 replace that with `StepResult.Done(errorNode)` recovery. Verified: SPARQL’s
  8 negative parser tests are all lexically-valid (syntactic/semantic), so they never
  involve an `Error` token.
- **English-only messages (§10 Q2):** the bridge renders `Diagnostic.Message` from
  `GetMessage()` (English) once, at error time; the i18n `MessageKey` seam stays deferred.

**Step 3 (Turtle parser recovery) — foundations committed (`42d48f1`), parser pass NOT started:**
(Foundations = `ParseResult<TTree>` + the three error-node classes, green. The parser-recovery
rewrite was scoped + designed below but not coded; an exploratory attempt was reverted because it is
atomic — see the RESUME ANCHOR at the end of this Step 3 block for the execution checklist.)
- **Surface decision (final-form now, confirmed with the user):** take the real API this
  step, not a transitional throw — because Turtle has *no future engine layer* to defer the
  `HasErrors` decision to (the reader is the top consumer), so deferring would just mean a
  throwaway throwing reader + a double caller migration. **Recovery is the single mode
  everywhere; the reader never throws on malformed input** (only genuine non-parse faults —
  I/O, cancellation — throw). There is **0 backwards-compat need** (see
  [[feedback_no_backwards_compat]]), so no optional/throw-for-compat mode exists; every
  throw-expecting caller and test migrates to a `HasErrors`/bag check. Two surfaces:
  - **Whole-document path** (`ReadWithSourceAsync` / the document-returning entry) → returns
    `ParseResult<TurtleDocument>` (tree possibly-with-error-nodes + diagnostics + `HasErrors`).
    Callers migrate (bounded: ~mostly tests + `NQuadsReader`/`TurtleWriter`/`TurtleQuadEmitter`).
  - **Streaming quad path** (`ReadAsync` → `IAsyncEnumerable<Quad>`) → takes a **required**
    consumer-owned `DiagnosticBag` (the locked "shared bag the consumer reads"): it recovers,
    emits quads for valid statements, records diagnostics, and never throws on bad input. The
    caller inspects `bag.HasErrors` after enumerating and applies its own policy. (My earlier
    "optional bag, null → throw" was a backwards-compat hedge — dropped.)
- **`ParseResult<TTree>` location:** single generic def in `Core/Diagnostics/ParseResult.cs`
  (depends only on `Diagnostic`), **not** duplicated per parser namespace as §6.1 illustrates.
- **Error-node variants = the three Turtle abstract bases:** `ErrorStatement : Statement`
  (covers triple-statements *and* directives — both derive from `Statement`), `ErrorTerm : Term`,
  `ErrorAnnotation : Annotation`. Sealed classes matching the class-based AST style, each
  `(nodeId, span, Utf8String ExpectedProduction, ImmutableArray<Utf8String> DiagnosticCodes,
  ImmutableArray<TurtleToken> SkippedTokens)`. **DONE + builds green** — confirmed no AST
  consumer has an exhaustive type-switch that the new subtypes break.
- **Frame contract (StepResult Pop/Push/Continue):** error nodes flow up via `Pop` carrying the
  error node (the locked "no new status enum; `StepResult.Done(errorNode)`" adaptation). On an
  unexpected or lexer-`Error` token: emit `TT####`/bridged `LX####` diagnostics into the
  parser-owned `DiagnosticBag`, resync to the frame's resync set collecting `SkippedTokens`,
  build the nearest-base error node, `Pop`. The reader bridges `lexer.Diagnostics` via
  `TurtleLexDiagnosticBridge` into that same bag (the locked shared-bag model).
- **Recovery mechanism = value-based everywhere (locked, the user's call; see
  [[feedback_value_based_control_flow]]):** *one* recovery path, no exceptions for control flow.
  Leaf helpers (`ParsePrefixDeclaration`, `ConsumeLeafTermFor`, `ParseStringLiteral`, `Validate*`,
  …) record the diagnostic into the bag and **return a typed error node** (an `ErrorTerm` in term
  positions) rather than throwing; frame-level detection sites likewise `return Done(errorNode)`.
  **Internal-invariant guards stay throws** (the "reached unknown stage" / "unhandled frame kind"
  set — our bugs, not control flow). (Rejected alternative: a recoverable exception caught once in
  `Drive` — surgical but two mechanisms and throwing-for-control-flow.)
  - **Recovery is LOCAL per frame — the key realization that avoids unwinding:** because `ErrorTerm`
    *is* a `Term` (and `ErrorStatement` a `Statement`, `ErrorAnnotation` an `Annotation`), an error
    node slots into any parent that expected that base, so the existing value flow carries it up — no
    multi-frame `Drive` unwind, **no new `StepAction`** (keeps the locked `Done(errorNode)` model).
    Each frame resyncs to its own resync set and produces its own appropriately-typed error node.
  - **The list/pair frames need NO error variant:** `PredicateObjectList`/`ObjectList` (→
    `ImmutableArray<…>`), `PredicateObject`, `AnnotatedObject` simply *wrap* the `ErrorTerm`s their
    child term-frames hand up. `ErrorStatement` is produced only when statement-level structure is
    broken (bad directive, missing `.`); `ErrorAnnotation` only for a broken annotation. That is why
    the three error-node types (Statement/Term/Annotation) suffice.
  - **Note on incrementality:** the change is effectively atomic — `TreatWarningsAsErrors` flags
    unused private members, so the recovery helpers can't be added before they're used; and converting
    any throw site stops the reader throwing there, breaking its negative test until the reader change
    lands. So parser-recovery + reader-surface + test-migration land together (the new resync/cascade/
    reactive *tests* are additive and may follow in a second commit).

### ⏵ STEP 3 RESUME ANCHOR — execution checklist (re-paste the "Parser error recovery — Handoff" spec, then do this)

Branch `sparql-query-engine`. Commits: `b428500` (Step 2), `42d48f1` (Step 3 foundations). Tree clean+green.
**Build/test (MTP — `dotnet test` does NOT work the usual way):** build with
`dotnet build src/Lumoin.Veritas.Turtle/Lumoin.Veritas.Turtle.csproj -clp:ErrorsOnly --nologo`; run tests via the
built exe `test\Lumoin.Veritas.Tests\bin\Debug\net10.0\Lumoin.Veritas.Tests.exe --filter "FullyQualifiedName~Turtle"`
(the full suite has a **50-test pre-existing SHACL-conformance baseline** that is expected-red; Step-3 success = no *new* failures).

1. **Parser infra** (`src/Lumoin.Veritas.Turtle/Parser/TurtleParser.cs`): add `using Lumoin.Veritas.Core.Diagnostics;`;
   add `DiagnosticBag? diagnostics = null` to both ctors → store `Diagnostics = diagnostics ?? new DiagnosticBag()`;
   add `internal DiagnosticBag Diagnostics { get; }`; add
   `public ParseResult<TurtleDocument> ParseToResult()` = `new(Parse(), Diagnostics.Diagnostics, Diagnostics.HasErrors)`.
2. **Recovery helpers** (place by `Expected`; KEEP `Expected` until all callers are converted so the build
   stays green mid-edit): `Report(code, span, msg)` → `Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, Utf8String.FromString(msg)))`;
   `ResyncTo(ParseFrameKind)` → consume `Current` collecting skipped tokens until `IsResyncToken` (guard `index==before`
   break for the incremental clamp); `IsResyncToken(kind, token)` resync table — Statement/SubjectStatement→`{Period,CloseBrace}`,
   GraphBlock→`{CloseBrace}`, Collection→`{CloseParen}`, BlankNodePropertyList→`{CloseBracket}`, TripleTerm→`{CloseTripleTerm,Period,CloseBrace}`,
   ReifiedTriple→`{CloseReifiedTriple,Period,CloseBrace}`, AnnotationBlock→`{CloseAnnotation}`, default→`{Period,Semicolon,Comma,Close*}` (+`EndOfInput` always);
   `RecoverTerm/RecoverStatement/RecoverAnnotation(frameKind, startSpan, code, errorSpan, message, expectedProduction)` → `Report` + `ResyncTo` +
   `NewNode(id => new Error{Term,Statement,Annotation}(id, CombineSpans(startSpan, lastSkippedOrErrorSpan), Utf8String.FromString(expectedProduction), [code], skipped))`.
3. **Expand `WellKnownDiagnostics.Turtle`** (`src/Lumoin.Veritas.Core/Diagnostics/WellKnownDiagnostics.cs`) — currently
   TT0001–TT0006; add codes for: version-directive-arg, graph-block-open/close, only-triples-in-graph-block, annotation
   (empty / expected verb-or-`|}`), datatype-after-`^^`, triple-term/reified-triple subject/object validation, expected-term,
   unexpected-`Error`-token. Reference each by name at its emission site (no string-literal codes).
4. **Convert throw sites → value-based recover** (taxonomy: input-reachable ⇒ recover; invariant ⇒ keep throw):
   - **Term-position** (→ `RecoverTerm`, frameKind=Term/Collection/BNPL/TripleTerm/ReifiedTriple): `StepTerm` default `Expected("term")`;
     `StepCollection`/`StepBlankNodePropertyList` EOF/verb sites; `StepTripleTerm`/`StepReifiedTriple` close-token sites + the
     `Validate{TripleTerm,ReifiedTriple}{Subject,Object}` statics (make them return the offending term as an `ErrorTerm` or report+recover);
     `ConsumeLeafTermFor`/`ParsePredicateLeaf` `_ => Expected(...)` → return an `ErrorTerm`; `ParseStringLiteral` datatype-after-`^^`.
   - **Statement-position** (→ `RecoverStatement`, frameKind=Statement/SubjectStatement/GraphBlock): `ParsePrefixDeclaration`/
     `ParseBaseDeclaration`/`ParseVersionDeclaration` (return `Statement`; on bad token report+`RecoverStatement`); the version
     long-string check; `StepSubjectStatement` missing-`.`; `StepGraphBlock` open-`{`/close-`}`/only-triples sites.
   - **Annotation-position** (→ `RecoverAnnotation`, frameKind=AnnotationBlock): `StepAnnotationBlock` empty-block / expected verb-or-`|}`.
   - **KEEP as throws (invariants):** every `"reached unknown stage"`, `"Unhandled frame kind"`, `"undefined state"`,
     `"Numeric kind out of range"`; `ConsumePrefixedName` missing-`:` and `ExtractPrefix` missing-`:` (lexer guarantees the colon — quasi-invariant).
5. **`Drive` handles incoming lexer `Error` tokens:** when `Current.Kind == TurtleTokenKind.Error`, the lexer diagnostic is already
   in the shared bag (bridged by the reader, step 6) — skip the `Error` token via the top frame's recover (don't double-report).
6. **Reader** (`src/Lumoin.Veritas.Turtle/TurtleReader.cs`): create a `DiagnosticBag`, pass it to both lexer (bridge `lexer.Diagnostics`
   via `TurtleLexDiagnosticBridge.ToDiagnostic` into it) and `TurtleParser`. Document path returns `ParseResult<TurtleDocument>`
   (document + diagnostics). Streaming `ReadAsync` gains a **required** `DiagnosticBag` param; recover, emit quads for non-error
   statements, **never throw** on malformed input. Verify `TurtleQuadEmitter`/`TurtleWriter` skip error nodes (they built green with
   the new subtypes, so no exhaustive-switch break — just confirm no NRE on an `ErrorStatement`/`ErrorTerm`).
7. **Migrate callers/tests** (small surface): `TurtleParserTests` 81/87/340 and `TurtleIriResolutionTests` 76/85 (`Assert.Throws<TurtleParseException>`
   → `ParseToResult()`/`ReadAsync(bag)` then assert `HasErrors` + the expected code/error-node); W3C Turtle/TriG conformance harness
   (catch-exception → check `HasErrors`); wrap positive tests to assert no errors. **NQuads is a separate reader — unaffected.**
   `TurtleReaderLimitsTests` uses `TurtleLimitExceededException` (a resource guard, **stays a throw**).
8. **New tests (additive, may be a 2nd commit):** per-`ParseFrameKind` resync test; cascade-suppression (one unclosed `]`/`)` ⇒ bounded
   diagnostics); **reactive-editor** (feed tokens incrementally as if typed; assert error node + diagnostic at the right span, then feed
   valid input and assert recovery continues) — these exercise the incremental `FeedToken`/`TryParseStatement` + shared-bag path.
- **Reactive-editor tests (added per the user):** beyond positive/negative sweeps, add incremental
  tests that feed tokens as if typed, assert the error node + diagnostic land at the **right span**
  (so an editor highlights correctly), then feed valid input and assert the parser recovers and keeps
  producing. Intellisense/auto-correct is a later layer (spec §1 out of scope), but the error nodes'
  `ExpectedProduction` + `DiagnosticCodes` + spans are the deliberate data hook for it.
- **Remaining:** resync table per `ParseFrameKind` (14 kinds); the frame-step rewrite across
  `TurtleParser` (~1600 lines) + `Drive` handling `Pop(errorNode)` and incoming `Error` tokens;
  a materialised `ParseResult` entry + diagnostic threading; the reader surface + caller
  migration; the Turtle test sweep (negative → `HasErrors`+code; positive → no-error tree) and
  new resync / cascade-suppression / coverage tests.

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
