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
- **Step 4** (SPARQL parser recovery) — **COMPLETE.** Foundations (`62740e7`): `Ast/SparqlErrorNodes.cs`
  — the seven error-node records, one per product base the work stack hands up (`ErrorGraphPattern`,
  `ErrorQueryForm`, `ErrorExpression`, `ErrorPropertyPath`, `ErrorTriplePatternTerm`, `ErrorGraphTerm`,
  `ErrorAnnotation`), each `(SourceSpan Span, Utf8String ExpectedProduction, ImmutableArray<Utf8String>
  DiagnosticCodes, ImmutableArray<SparqlToken> SkippedTokens)`. Infra (`8e076a2`): `DiagnosticBag` on both
  ctors + `Diagnostics` + `ParseToResult()` + the static `ParseRequest(source, pool?, baseIri?)→ParseResult`
  facade bridging `lexer.Diagnostics`. Rewrite (`4cc29a3`): the recovery helpers (`Report`/`ReportRecoverable`/
  `ResyncTo`/`IsResyncToken` resync-table-per-`ParseFrameKind` + the seven `Recover*` builders), all ~80
  grammar throw sites converted (structural→`Recover*`; validation→report-and-keep faithful node;
  close/list→report+finalise-partial; unbound-prefix→`SP0006`; a bad base direction `@en--xyz` is
  input-reachable in SPARQL so it recovers via `SP0042`), invariants kept as throws, `Expected` removed,
  `WellKnownDiagnostics.Sparql` expanded to `SP0042`. Progress guards (`CanStartGroupMember` + a bounded
  group stray-token skip) guarantee the work stack always advances (the Turtle realisation). The 9 negative
  `SparqlParserTests` → `HasErrors`+code; `W3cSparqlSyntaxTests` re-raises on `HasErrors` (shared W3C runner
  unchanged); 13 `KnownGaps` still skip. Recovery tests (`SparqlParserRecoveryTests`, 10) added.
  **Build clean (0 warnings); 366/366 SPARQL tests pass; full suite 50 failed = the unchanged SHACL
  baseline, 0 new failures.** *(The mechanical conversion was delegated to a subagent against this anchor +
  the Turtle template, then reviewed line-by-line + re-tested.)*
- **Step 5** (`SparqlInvalidRequestException` + `MaxDiagnostics`) — **COMPLETE.** `SparqlInvalidRequestException`
  is the execution-boundary guard: parsing never throws (returns a `ParseResult` with `HasErrors`);
  *executing* an errored request is the contract violation the future engine refuses via
  `SparqlInvalidRequestException.ThrowIfInvalid(ParseResult<SparqlRequest>)`, which carries the
  `Diagnostics`. `MaxDiagnostics` is a parser-level ctor option (default unbounded): once the cap of
  parser-recorded diagnostics is reached, `Report` records `SP9999 ExcessDiagnostics` once and suppresses
  the rest (the AST still assembles) — a runaway-error backstop. §7/`ExecuteAsync` wiring stays deferred
  (no engine yet). Tests added to `SparqlParserRecoveryTests`; full suite still 0 new failures over the
  SHACL baseline.
- **Step 6** (diagnostic-code audit) — **COMPLETE.** Every `LX`/`TT`/`SP` code is now either referenced by
  name at an emission site or documented as **reserved**; no emission site uses a string-literal code. The
  9 reserved codes (not renumbered, per the stable-catalogue rule) are: `LX0005 UnknownDirective` (no lexer
  emitter — a leading `@` lexes as a tag/identifier); `TT0001 ExpectedSubject` + `TT0003 ExpectedObject`
  (the Turtle parser reports the position-agnostic `ExpectedTerm`); `TT0005 UnclosedBlankNodePropertyList`
  (Turtle reports `ExpectedPredicate`); `SP0002 ExpectedWhereKeyword` (WHERE is optional; dispatch is on
  `{`); `SP0007 InvalidBaseIri`, `SP0008 UpdateNotSupported`, `SP0009 NestedAggregateNotSupported`,
  `SP0010 AggregateOutsideProjection` (not-yet-implemented checks — base-IRI validation, Update detection,
  aggregate scope analysis). **The error-recovery batch (Steps 1–6) is COMPLETE.**

## ⏵ CURRENT WORK — Pre-Milestone-B pass (cleanups + Milestone-A completion)

After the recovery batch, a pre-B pass to finish Milestone A and reach the algebra on-ramp. **Done so far
(all committed, green, full suite at the 50-test SHACL baseline = 0 new failures):**
- **Code cleanups** (`8ed01fd`): removed the dead `NotImplemented` helper; fixed the stale `RequestConstruct`
  "short form not supported" summary (the CONSTRUCT WHERE short form is supported).
- **§7.0 §19.8 doc-anchor pass** (`8aa859b`): every `SparqlTokenKind` member + every grammar-production AST
  record cites its SPARQL 1.2 §19.8 anchor in `<remarks>` (mirrors the `WellKnownDiagnostics.Sparql` style).
- **Slice S — uniform AST spans** (`bad6756`): every higher-layer AST record now carries a `SourceSpan Span`
  threaded with the real extent (`CombineSpans(frame.StartSpan, lastConsumedSpan)` for frame nodes, child
  spans for binary/unary expressions, zero-width spans at the cursor for empty productions — never `None`).
  `SparqlAstSpanTests` pins exact source slices.
- **Slice 4b-ii: VERSION** (`2e765d5`): the `VERSION` prologue declaration — `VersionDecl` record +
  `Prologue.Versions`; `ParseVersionDeclaration` recovers (`SP0043 InvalidVersionArgument`) on a long/non-string
  arg. version-03/04/06 un-skipped.

**Items 1–3 DONE (committed on `sparql-query-engine`, each green at the 50-test SHACL baseline = 0 new failures):**
1. **Slice 4b-ii — triple term in expression** (`d0f230d`) — `TripleTermExpression(Span, TriplePattern Inner)`
   added to `Ast/ExpressionNode.cs`; `DispatchPrimaryExpression` gains an `OpenTripleTerm` arm pushing a
   `TripleTerm` frame under `TripleTermContext.Expression`; expression stage 9 receives the popped term
   (re-typing an error node to `ErrorExpression`). `expr-tripleterm-01/06` closed.
2. **Slice 4b-ii — triple term in VALUES** (`e8f73f5`) — `StepValues(frame, incoming)`; `ValuesOneVarRow`/
   `ValuesRowValue` push a `TripleTerm` frame under `TripleTermContext.Data` on `<<(` (frame-driven because the
   construct nests unboundedly — inline parse would break the ≤4-token-per-step resumability), then convert the
   popped term to a ground `Core.TripleTerm` at new stages 5/6. `basic-tripleterm-05` closed.
3. **Slice 4b-ii — standalone reified triple** (`362f230`) — `BasicGraphPatternBlock` gains
   `IReadOnlyList<TriplePatternTerm> StandaloneNodes`; `StepTriple`'s `TripleVerb` routes a verb-less
   `ReifiedTriple` subject to the standalone channel (`Done(frame.Subject)`); `StepGroupGraphPattern` collects a
   popped `TriplePatternTerm` into `PendingStandaloneNodes`; `FlushPendingTriples` flushes a block holding either
   or both. A bare triple term `<<( … )>>`/collection standalone still errors (the `tripleterm-separate-*`
   negatives keep failing). 12 `basic-*reifier-08..13` + 3 `version-01/02/05` closed.

**Shared mechanism added by 1–3 (read before item 4):** `StepTripleTerm` is now **context-aware** via
`ParseFrame.TripleTermContext` (`Pattern`/`Expression`/`Data` enum in `Parser/ParseFrame.cs`) — the authoritative
grammar (confirmed against the W3C SPARQL 1.2 EBNF): WHERE `TripleTerm ::= '<<(' VarOrTerm Verb VarOrTerm ')>>'`
(full, may nest a triple term in subject/object); expression `ExprTripleTerm` subject = `iri | Var` only, object
adds literals + nested, no blank node; data `TripleTermData` subject = `iri` only, verb = `iri | 'a'` (no var),
object = `iri | literal | nested`, **no variables**. Diagnostics `SP0044 InvalidTripleTermSubject` /
`SP0045 InvalidTripleTermObject`. (The pinned conformance corpus at `674da26` enforces these subject restrictions
via the `tripleterm-subject-01..06` negatives even though the *latest* W3C grammar relaxed
`ExprVarOrTerm`/`DataValueTerm`; the corpus is the arbiter.)

**Remaining pre-B work — item 4 (the design-heavy Milestone-B on-ramp; see the ITEM-4 RESUME ANCHOR below):**
4. **Normalization pass + B scaffold** (§8.5): a new early-expansion pass (between parse and algebra) lowering
   `CollectionTerm`/`BlankNodePropertyListTerm`/reified-triple/`AnnotatedObject`/`StandaloneNodes` sugar to plain
   triples (using Core's `BlankNodeDelegate`, preserving spans), so the translator sees the four core term cases
   (`ConstantTerm`, `VariableTerm`, `PropertyPathTerm`, `Ast.TripleTerm`); + the `Algebra/` and `Translation/`
   namespace layout (§5.1). The direct on-ramp to Milestone B; arguably its first slice.

### ⏵ ITEM-4 RESUME ANCHOR — normalization pass + B scaffold (start here after a clear)

State: branch `sparql-query-engine`, tree clean+green, HEAD `362f230`. **Items 1–3 (Slice 4b-ii additive
features) are DONE and committed** (`d0f230d`/`e8f73f5`/`362f230`); 17 conformance gaps closed; full suite at
the 50-test SHACL baseline = 0 new failures. Everything parses **value-based** (never throws on malformed input);
the SPARQL AST has **uniform spans**; the diagnostic catalogue is audited (now through `SP0045`). Item 4 is the
design-heavy Milestone-B on-ramp — do it on a **fresh context**.

**STATUS (2026-05-28 session): item 4(a) — the normalization pass — is IMPLEMENTED, committed (`b70bdb1`), and green.**
- New `Translation/SparqlNormalizer.cs` + `Translation/SparqlNormalizerOptions.cs`. Walks the query tree
  (groups / optional / minus / union / graph / service / sub-select + the CONSTRUCT template), lowering
  `CollectionTerm`, `BlankNodePropertyListTerm`, `ReifiedTriple`, `AnnotatedObject`, and a block's
  `StandaloneNodes` to plain core `TriplePattern`s; fresh bnodes via the parser's `BlankNodeDelegate` seam
  (same pool ⇒ the per-pool `b#` counter continues, no label collision); spans preserved onto every lowered
  triple; error nodes pass through; never throws. Mirrors `TurtleQuadEmitter` but outputs AST, keeps variables,
  does no IRI resolution, and leaves property-path predicates alone.
- **Reified-triple semantics RESOLVED (no-assert; see the §8.5 block below) and made CONFIGURABLE** per the
  user: default = spec no-assert (`reifier rdf:reifies <<( s p o )>>` only); opt-in
  `SparqlNormalizerOptions.AssertReifiedTripleInnerTriple` *also* asserts the base triple. Only the annotation
  `{| … |}` form asserts the base by default. The 3 contradicting AST doc comments were corrected.
- `rdf:first/rest/nil` were NOT added to Core — they already exist as `RdfVocabulary.Rdf.First/Rest/Nil`
  (`Lumoin.Veritas.Rdf`, already referenced by Sparql), exactly where the Core `Vocabulary.Rdf` doc delegates
  list vocabulary; `rdf:reifies` from `Vocabulary.Rdf.Reifies` (Core). The handoff's "add to Vocabulary.Rdf" was
  based on an incomplete view.
- 9 `SparqlNormalizerTests` pin the lowered shapes (empty/non-empty collection, blank-node property list,
  reified ×2 config modes, standalone reified, annotation-asserts-base, span preservation, nested-pattern
  recursion). Build clean (0 warnings); 415 Sparql tests pass; **full suite at exactly 50 ShaclValidate
  failures, 0 new** (Sparql filter = 0 failed, so all 50 are outside Sparql).
- **No consumer wiring done.** The normalizer is standalone; the translator/engine that will invoke it do not
  exist yet. **Known deferred gap:** the CONSTRUCT-WHERE short form's `ExtractTemplateTriples`
  (`SparqlParser.cs:773`) copies only `block.Triples` at parse time, dropping `StandaloneNodes` — a
  CONSTRUCT-template fidelity issue to resolve when CONSTRUCT execution is wired (Batch 2), not a normalizer bug.

**STATUS (cont.): item 4(b) — the `Algebra/` type hierarchy — is BUILT, committed (`6d4d36d`), and green.** The
user authorized the conventions layout (2026-05-28) and added the §10.1 base members.

**STATUS (cont.): the translator — AST → algebra (SPARQL 1.2 §18.2) — is BUILT, committed (`1eafdc5`; property-path pattern translation `71e995f`), and green.**
- New `Translation/SparqlTranslator.cs` (pure static function; expects a request already lowered by
  `SparqlNormalizer`). §18.2.2.6 group translation: members combined left-to-right from the join identity
  (`UnitTable` = the algebra `Z`); FILTERs collected and lifted to one `Filter` over the whole group (conjoined
  left-associatively via `AndExpression`); `OPTIONAL`→`LeftJoin` (a top-level inner `Filter` lifted to the join
  `Condition`); `MINUS`→`Minus`; `BIND`→`Extend`; everything else joined in with the §18.2.2.8
  `Join(Z,A)=A`/`Join(A,Z)=A` simplification. Patterns: `BasicGraphPatternBlock`→`Bgp` (empty run→`UnitTable`),
  `UNION`→`Union`, `GRAPH`→`Graph`, `SERVICE`→`Service`, inline/trailing `VALUES`→`Table` (trailing joins the
  pattern). Modifiers (§18.2.5): SELECT applies `(expr AS ?v)` `Extend`s → `OrderBy` → `Project` (named columns, or
  every visible variable for `SELECT *` via a deterministic source-ordered, scope-aware collector that drives the
  algebra's `Children` surface) → `Distinct`/`Reduced` → `Slice`. ASK→bare pattern; CONSTRUCT/DESCRIBE→`OrderBy`+
  `Slice`, no projection. Error nodes contribute nothing (`ErrorGraphPattern`→`UnitTable`; `ErrorQueryForm`→bare
  pattern). **A complex property-path predicate becomes a `Path` operator** (`71e995f`, §18.2.2.4): `TranslateBlock`
  splits a block into `Bgp` runs (plain predicates) and `Path` operators (each `PropertyPathTerm` triple), joined in
  source order; the parser already collapses a single-link path to a `ConstantTerm`, so a `PropertyPathTerm` is always
  a genuine path. The `Path` operator holds the path expression intact (seq/alt/inv as well as `*`/`+`/`?`/NPS) —
  decomposing those forms (joins/unions/swaps + evaluation semantics) is the later path-form-lowering slice.
  **Deferred (raise `NotSupportedException`): aggregation (`GROUP BY`/`HAVING`) and sub-`SELECT`.**
- 20 `SparqlTranslatorTests` pin the shapes (single BGP, filter lifting ×2, OPTIONAL→LeftJoin ±lifted condition,
  UNION/MINUS/BIND/GRAPH, empty group→UnitTable, modifier nesting, SELECT-expr Extend, ASK bare, trailing VALUES
  join, CONSTRUCT slice-no-project, sub-SELECT + GROUP BY refusal, + 3 path: complex→Path, sequence carried whole,
  mixed plain+path join in order). Build clean (0 warnings); full suite at exactly 50 ShaclValidate failures, 0 new.
  **One known limitation:** a SELECT with an aggregate expression but no explicit `GROUP BY`/`HAVING` (implicit
  grouping) is NOT yet detected — it currently lowers the aggregate as a plain `Extend`; the aggregation slice owns
  the fix (along with aggregate-in-HAVING/ORDER BY detection).

**STATUS (cont.): the `AlgebraWalker` (§10.5) — iterative Traverse / Transform — is BUILT, committed (`e970178`, reworked iterative `86d8245`), and green.**
- New `Algebra/AlgebraWalker.cs`: `Traverse(root)→IEnumerable<AlgebraOperator>` is lazy pre-order; `Transform(root,
  rewrite)` is a bottom-up rebuild. Both use an **explicit work `Stack`, not call-stack recursion**
  ([[feedback_no_recursion]] — the user's rule: recursion is banned anywhere; mirror the parsers' frame stack and
  `Core.Algebra.IterativeTraversal`). Transform's two-phase expand/combine stack rebuilds an operator only when a
  child changed, then applies the rewrite; untouched subtrees are returned by reference (identity rewrite ⇒ the
  same `root` instance). **Tree semantics, no dedupe** — value-equal sibling subtrees are distinct positions, both
  visited; that is why it mirrors `IterativeTraversal`'s technique rather than routing through its dedup-by-value
  graph walk (the user chose this — answered "A"). Adjacency is the fixed base `Children`.
- Transform's rebuild needed the structural dual of `ComputeChildren`: a new `internal abstract
  RebuildWithChildren(IReadOnlyList<AlgebraOperator>)` on `AlgebraOperator`, per operator (leaves return `this`);
  internal+abstract seals the operator set to this assembly and makes adding an operator a compile error until it
  is implemented. 8 `SparqlAlgebraWalkerTests` (incl. value-equal-siblings-not-deduped + a 50k-deep
  no-overflow case); build clean (0 warnings); full suite at exactly 50 ShaclValidate failures, 0 new.

✅ **RECURSION SWEEP COMPLETE — no call-stack recursion remains in the Sparql translation/algebra layer**
([[feedback_no_recursion]]; the parsers were already frame-stack machines). Cleared: **(a) `SparqlTranslator`**
(`65386e1`) — `TranslatePattern`/`TranslateGroup` → explicit post-order build with a reference-keyed results map
(`SubPatterns`/`TranslateNode`/`CombineGroup`); `VisibleVariables` → work-stack of visit/emit steps;
`CollectTermVariables` → term stack. **(b) `AlgebraScope.Collect`** + **(c) `AlgebraOperator.OutputVariables`**
(`7d9c603`) — Collect → term stack; OutputVariables warms the subtree bottom-up over an explicit post-order stack
(off-instance cache + value equality unchanged). **(d) `SparqlNormalizer`** — `NormalizePattern` → post-order
rebuild (`b683a90`); the **term-lowering** mutual recursion → an explicit **FIFO `Queue`** of
`(subject, predicate, object, span)` emission tasks, with the one bottom-up case (cores embedded in quoted/reified
triple terms) resolved by a local post-order stack in `ResolveCore`/`BuildQuotedCore` (`e3f08e8`). ⚠ The work-list
**must stay FIFO** — a LIFO stack reverses triple order and breaks `SELECT *` variable order. Deep-nesting tests
guard all of it (50k Join `OutputVariables`/Transform, 20k nested groups end-to-end, 10k nested collections).

**STATUS: aggregation operator scaffold BUILT, committed (`379b36e`), green (NO translation wiring yet).** The
shapes (built to convention — correct if the impl spec differs): `Group(IReadOnlyList<GroupCondition> Keys,
AlgebraOperator Input)` (empty `Keys` = the single implicit group; `OutputVariables` = named keys only — `?v` /
`(expr AS ?v)`, a bare grouping expr names nothing); `AggregateBinding(SparqlVariable Variable, AggregateExpression
Aggregate)`; `AggregateJoin(AlgebraOperator Input /* a Group */, IReadOnlyList<AggregateBinding> Aggregations)`
(`OutputVariables` = group keys ∪ each aggregate's result var). Both reuse the AST `GroupCondition`/
`AggregateExpression`, no spans, full `Children`/`OutputVariables`/`RebuildWithChildren`. 2 `SparqlAlgebraTests`.

**STATUS (cont.): the aggregation TRANSLATION — wire Group/AggregateJoin into `ApplyQueryForm` (§18.2.4.1) — is DONE, committed (`0057e17`), and green.**
- `SparqlTranslator.ApplyQueryForm` (no longer throws): aggregation applies on an explicit `GROUP BY`/`HAVING`, or
  on any `AggregateExpression` in the projection / `HAVING` / `ORDER BY` (implicit grouping). Pattern becomes
  `AggregateJoin(Group(keys ?? [], pattern), bindings)`; `HAVING` → a `Filter` over the `AggregateJoin`; the
  projection / `ORDER BY` expressions are rewritten so each aggregate refers to its fresh result var (`.agg0`,
  `.agg1`, … — a leading `.` no user var can carry). The existing `(expr AS ?v)` Extends / OrderBy / Project /
  Distinct / Slice then run over the rewritten forms (so `TranslateSelect`/`ApplyOrderAndSlice` now take the
  rewritten `projections`/`order` as params, not `select.Projections`/`modifier.Order`). Order of nesting:
  HAVING-Filter is applied *inside* the aggregation step, then the SELECT-expr Extend wraps it (§18.2.5).
- **Distinct-aggregate dedup is SOURCE-POSITION-INSENSITIVE.** `AggregateExpression` record equality includes its
  `Span` (and its `Argument`'s spans), so two textually identical aggregates at different positions are *not*
  value-equal. `NormalizeAggregate` zeroes the aggregate's span + every span in its argument (via
  `ExpressionWalker.Transform(arg, n => n with { Span = default })`; the aggregate itself is a walk leaf so its own
  span is zeroed directly) — that span-zeroed form is the dedup / lookup key, so `COUNT(?o)` in both the projection
  and HAVING shares ONE binding / ONE var. (⚠ if you ever change the dedup, note plain record equality would NOT
  merge cross-position duplicates.)
- **New `Ast/ExpressionWalker.cs`** — the expression-side analogue of `AlgebraWalker`: iterative (no-recursion,
  [[feedback_no_recursion]]) `Traverse` (lazy pre-order) / `Transform` (bottom-up rebuild, reference-equality
  short-circuit) over `ExpressionNode`, driven by a per-type `Children` switch + dual `Rebuild` switch (the switches
  live in the walker, NOT as members on the records). `AggregateExpression` is a **LEAF** of the walk (its
  `Argument` is never descended — it is replaced wholesale), as are `ExistsExpression`/`NotExistsExpression` (carry
  a graph pattern) and `TripleTermExpression` (carries a triple pattern). List-shaped children (`IN`/`NOT IN` set,
  function args, COALESCE alts) flatten in `Children` and re-split in `Rebuild`.
- 7 `SparqlTranslatorTests` (COUNT(*) implicit group, GROUP BY keys, aggregate in HAVING → Filter, aggregate inside
  arithmetic SELECT expr, GROUP BY `(expr AS ?v)`, repeated-aggregate dedup = one binding, 50k-deep aggregate
  expression no-overflow) replaced the old `GroupByIsNotSupportedYet`. Build clean (0 warnings); full suite at
  exactly 50 ShaclValidate failures, 0 new.

**STATUS (cont.): property-path form *lowering* (§18.2.2.5) — translate-time decomposition is DONE, committed (`6b6e4f2`), and green.**
- `SparqlTranslator.TranslateBlock` now lowers a complex path predicate via the new iterative (explicit
  expand/combine work stack, no recursion) `TranslatePathPattern`: `seq`→left-associated `Join` chained through
  fresh internal join vars; `inv`→endpoint swap; `alt`→`Union` of branches over the same endpoints; a single
  link→a one-triple `Bgp`. The four non-relational forms stay opaque `Path` leaves: `PathZeroOrMore`,
  `PathOneOrMore`, `PathZeroOrOne`, `PathNegatedSet`.
- Fresh seq join vars come from a query-wide `FreshVariables` allocator (names `.path0`, `.path1`, …; one
  instance per `Translate` so they never alias across unrelated paths). `VisibleVariables.Add` now filters
  `.`-prefixed internal vars (the `.pathN` join vars AND the `.aggN` aggregate results) out of `SELECT *`.
- 6 path tests (seq→Join-through-fresh-var + fresh var hidden from `SELECT *`, inverse swap, alt→Union, NPS
  stays Path, nested `(:a/^:b)|:c+` mixes Join/swap/Union + closure leaf, 20k-step seq no-overflow); the old
  "carried whole into Path" test was replaced. Build clean; full suite at exactly 50 ShaclValidate failures, 0 new.

▶ **NEXT AFTER A CLEAR:** HEAD `d0bffff`, tree clean, full suite green (exactly 50 ShaclValidate failures = the
unchanged baseline, 0 new). **Milestone B (AST→algebra translation) is COMPLETE; Milestone C (the executor) is
UNDERWAY — slices 1–2 done (`de58edf`, `d0bffff`).** `Sparql/Execution/SparqlQueryEngine` now executes BGP +
JOIN/UNION/MINUS + sub-SELECT SELECT/ASK end-to-end (parse→normalize→translate→execute); see the MILESTONE C —
EXECUTOR KICKOFF anchor below for the substrate map and the 6-slice plan (slices 1–2 ✅). **IMMEDIATE NEXT = slice
3: the expression evaluator** — an iterative (no-recursion) `ExpressionNode` → value evaluator (effective boolean
value, type coercion, the built-in function set; reuse the `ExpressionWalker` shape) — then wire `Filter`,
`Extend` (BIND), the `LeftJoin` condition (OPTIONAL), and `OrderBy`. Fold in the small deferred `Table` (VALUES →
solutions, UNDEF handling) here too. Then slice 4 = aggregation, slice 5 = `Path`-leaf closure + `Graph`/`Service`,
slice 6 = wire SHACL `sh:sparql`. **No external spec needed: consult the online W3C SPARQL 1.2 spec for semantics
(§17 Expressions, §18.6 evaluation), the codebase for C# conventions.** The property-path `Path`-leaf closure
(slice 5) reuses `Rdf.PropertyPathEvaluator` + the native pair-aware closure (PROPERTY-PATH plan items 2–4 below).
The trace-bus extensions ([[project_trace_bus_extensions]]) ride alongside. Deep-nesting
tests guard the iterative pipeline (20k nested groups, 10k nested collections, 2k nested sub-SELECTs, 50k Join
OutputVariables/Transform, 50k-deep aggregate expression, 20k-step sequence path). Relevant memory:
[[feedback_no_recursion]], [[project_sparql_algebra_design]], [[project_sparql_reified_triple_no_assert]],
[[project_trace_bus_extensions]]. Build/test commands below. **Ordering decided (user, 2026-05-29): build the FULL
SPARQL executor first, THEN attack the SHACL conformance failures — the executor is the SHACL-SPARQL unlock, no
need to first classify which of the 50 baseline failures are SPARQL- vs core-SHACL.**

### ⏵ MILESTONE C — EXECUTOR KICKOFF (substrate mapped 2026-05-29; ⚠ confirm impl-spec §5.1 layout + §7 `ExecuteAsync` contract before fixing the PUBLIC surface — the bridge/solution model below is spec-independent and safe to build now)

**Substrate that already exists (read these first — the executor wires them together, it does not reinvent them):**
- **BGP evaluation (WCOJ):** `HypertrieGraphStore.Query(BasicGraphPattern, TimeProvider, planner?, accessControl?, accessContext?, traceHandler?, correlationId?, …)` → `IAsyncEnumerable<Solution>` (`Core/Hypertrie/HypertrieGraphStore.cs:346`), backed by `BasicGraphPatternEvaluator` (iterative leapfrog triejoin + `Planner` + `LeapfrogIntersection`). Default planner `Planners.FirstOccurrence(query)`.
- **Solution model (encoded):** `Solution` (`Core/Hypertrie/Execution/Solution.cs`) = `IReadOnlyList<VariableBinding>` (`VariableBinding(Variable, TermId)`) + optional `Witnesses` (`IReadOnlyList<EmittedQuad>?` — **provenance: the triples that produced the answer**, for proof-system witness assembly; wire this into [[project_trace_bus_extensions]] / signing later). `Solution.TryGetValue(Variable, out TermId)`.
- **Term dictionary (the bridge):** `Core/TermDictionary.cs` — `GetOrAdd(RdfTerm)→TermId`, `GetIdOrDefault(RdfTerm)→TermId|None` (a constant absent from the dict ⇒ BGP yields nothing), and decode back (`Quad.Encode/Decode(TermDictionary)`). `TermId.Encoded` (uint) / `TermId.FromEncoded`. `EncodedTriple.FromEncoded(uint,uint,uint)`.
- **Encoded query types (`Core/Hypertrie/Query/`):** `BasicGraphPattern(IReadOnlyList<TriplePattern>, VariableRegistry)`; `TriplePattern(PatternPosition Subject, Predicate, Object)`; `PatternPosition.Bound(TermId)` / `PatternPosition.OfVariable(Variable)`; `Variable(int Id)`; `VariableRegistry.GetOrAdd(string name)→Variable`.
- **Store build:** `HypertrieGraphStore.BuildAsync(IEnumerable<EncodedTriple>, VeritasHashing.Default, ct)`. `AsMatchOps()` → `GraphMatchOps` for the path evaluator.
- **Property paths:** `Rdf.PropertyPathEvaluator.EvaluateAsync(start, Rdf.PropertyPath, GraphMatchOps, ct)` / `EvaluateFromSetAsync(starts,…)` — forward set-at-a-time BFS closure (see items 2–4 of the PROPERTY-PATH plan below for the binding-mode wrapper + native pair-aware closure needed for SPARQL both-var + OWL).

**The bridge (spec-independent, build first):** SPARQL algebra is over `SparqlVariable`/`RdfTerm` (`NamedNode`/literals/blank); the store is over `Variable`/`TermId`. Need: (a) build a store + `TermDictionary` from the data graph's AST triples; (b) `SparqlVariable`↔`Variable` map (via a `VariableRegistry`, `GetOrAdd(name.ToString())`); (c) encode an algebra `Bgp` → `BasicGraphPattern` (`ConstantTerm.Term`→`GetIdOrDefault`; `VariableTerm`→`OfVariable`; a missing constant short-circuits the BGP to empty); (d) decode a `Solution` → a SPARQL binding (`SparqlVariable`→`RdfTerm` via the inverse var-map + dict decode). Note nested `Ast.TripleTerm` terms encode via `TermDictionary.GetOrAdd(TripleTerm)`.

**Sliced plan (each slice: build to convention, green at the 50-SHACL baseline, commit):**
1. ✅ **DONE (`de58edf`) — Encoding bridge + pure-BGP SELECT/ASK end-to-end.** New `Sparql/Execution/`:
   `SparqlSolution`+`SparqlBinding` (decoded solution model), `DataTriple` (provisional in-memory data graph),
   `SparqlQueryEngine` (`BuildAsync(triples)` → TermDictionary+store; `EvaluateAsync(algebra)` → iterative
   expand/combine work-stack over the algebra; `Bgp` encoded to `BasicGraphPattern` + `SparqlVariable`↔`Variable`
   map, run through `HypertrieGraphStore.Query` (WCOJ), decoded back; a constant absent from the data short-circuits
   the BGP to empty). Supported: `Bgp`/`Project`/`Distinct`/`Reduced`/`Slice`/`UnitTable`; every other op →
   descriptive `NotSupportedException`. 7 `SparqlQueryEngineTests`, green at the 50-SHACL baseline. ⚠ Engine entry
   name / result model / streaming are **provisional pending §7** — the bridge + solution model are spec-independent.
   *(Note: `Join`-of-BGP merge into one `BasicGraphPattern` was NOT needed for this slice — the translator already
   merges consecutive plain triples into one `Bgp`; cross-operator `Join` is slice 2.)*
2. ✅ **DONE (`d0bffff`) — Solution-sequence operators** that don't need expressions: `Join` (compatible merge-join),
   `Union` (multiset, left-before-right), `Minus` (with the disjoint-domain exception — remove only when compatible
   AND shares a variable), `ToMultiSet`/`ToList` (sub-SELECT / passthrough). Helpers `AreCompatible`/`SharesVariable`/
   `Merge`. 3 tests (UNION, MINUS, sub-SELECT Join). **Still deferred to a later slice: trailing `Table` (VALUES)** —
   needs the `ValuesClause` → solutions conversion (UNDEF handling); small, do with slice 3.
3. **Expression evaluator** (`ExpressionNode` → value; effective boolean value, type coercion, the built-in fn set) — then `Filter`, `Extend` (BIND), `LeftJoin` condition, `OrderBy`. This is the big sub-component.
4. **Aggregation** evaluation: `Group` partition + `AggregateJoin` (COUNT/SUM/MIN/MAX/AVG/SAMPLE/GROUP_CONCAT, DISTINCT) over the rewritten projections.
5. **`Path`-leaf closure** evaluation via `PropertyPathEvaluator` + the native pair-aware closure (PROPERTY-PATH plan items 2–4); **`Graph`** (named-graph redirection) + **`Service`** (defer/stub).
6. **Wire SHACL `sh:sparql`** to the engine → start closing the 50 ShaclValidate baseline failures.
**Cross-cuts:** thread the `traceHandler`/`correlationId` (the side-channel) through; carry `Witnesses` for provenance; everything iterative ([[feedback_no_recursion]]).

### ⏵ PROPERTY-PATH LOWERING — PLAN (design-locked 2026-05-29; confirm shapes vs. §18.2.2.5 if the impl spec is to hand)

Decided after studying the existing substrate (`Rdf.PropertyPathEvaluator` + the leapfrog `TriejoinIterator` +
the `Core/Diagnostics` trace bus). **The split is principled, not a compromise:** relational path forms go to the
trie side (optimizer-visible, pushdown-friendly, pairing-preserving), only the inherently-fixpoint forms stay
opaque and call the closure oracle.

1. **Translate-time decomposition (a pure AST→algebra rewrite in `SparqlTranslator`).** ✅ **DONE (`6b6e4f2`)** —
   implemented exactly as planned below (`TranslatePathPattern`, iterative; `FreshVariables` `.pathN`; `SELECT *`
   filter). The plan text is kept as the record of the decision.
   The parser already collapses a single link to a `ConstantTerm`, so a `PropertyPathTerm` is always a genuinely
   complex path. Decompose a path *pattern* `X path Y` (must be **iterative**, work-stack, no recursion —
   [[feedback_no_recursion]], mirroring `TranslatePattern`):
   - `seq(P1,P2)` → fresh internal var `V`; `Join(translate(X,P1,V), translate(V,P2,Y))`.
   - `inv(P)` → translate `(Y, P, X)` (endpoint swap).
   - **`alt(P,Q)` → `Union(translate(X,P,Y), translate(X,Q,Y))`** (LOCKED — distributes over both endpoints
     regardless of branch shape; this is distinct from a negated set, which the AST already separates as
     `PathNegatedSet`, NOT `PathAlternative`). So **all of seq/inv/alt decompose**, and ONLY these four stay as
     `Path` leaves: `PathZeroOrMore`, `PathOneOrMore`, `PathZeroOrOne`, `PathNegatedSet`.
   - **Fresh seq vars** use the collision-free internal-name convention (a leading `.`, as `.aggN` does — e.g.
     `.pathN`); they must be filtered from `SELECT *` scope (the `VisibleVariables` / `ProjectionVariables`
     collectors skip `.`-prefixed names — add that filter when this lands, since seq vars are real BGP join vars).
2. **Evaluation of the remaining `Path` leaves = Milestone C (executor), reusing `Rdf.PropertyPathEvaluator`** (the
   iterative set-at-a-time BFS closure). Binding modes: X-bound/Y-var → forward `EvaluateAsync(X)`; X-var/Y-bound →
   `EvaluateAsync(Y, inv)`; both-bound → forward + membership test; **both-var → needs (start, reached) PAIRS**,
   which the current flat-`HashSet` API erases.
3. **Add a native pair-aware closure to `Rdf.PropertyPathEvaluator`** rather than wrapping per-seed at the SPARQL
   level (per-seed wrapping throws away the batch predicate-rooted descent — near-quadratic on small-world graphs).
   Shape: a **semantic value type, not a raw tuple** (the user's call — zero perf cost vs. `ValueTuple<TermId,TermId>`,
   but named fields and, crucially, room to later hang a provenance/witness handle for the trace/OWL-justification
   plan; a tuple leaks arity and can't grow). Working name `readonly record struct PathPair(TermId Start, TermId
   Reached)` (bikeshed the name). **The existing flat-set API becomes a projection of the pair API** (`.Reached`,
   deduped) — one closure implementation, a pairing mode toggled by need; SHACL keeps the cheap set path.
   **OWL reuses the same pair-aware closure** (subPropertyOf/subClassOf/TransitiveProperty = `+`/`*`,
   propertyChainAxiom = `seq`, inverseOf = `inv`, SymmetricProperty = `p|^p`) — it needs the pairs to materialise
   `(a,q,b)`, so pairing MUST be native or SPARQL and OWL both re-write the wrapper.
4. **Pushdown (Parquet/DB) reinforces all of the above:** the seam is `GraphMatchOps.MatchTriplesBySubjects(set,
   predicate,…)` — set-at-a-time + encoded `TermId`/`IriId` (→ Parquet dictionary pages). A *decomposed* path
   becomes BGP triples a backend can semijoin into one scan; an *opaque* whole-path operator can only be evaluated
   engine-side with per-step pushdown. So decomposition is the pushdown-friendly choice too.

**PERMISSIONS NOTE:** test runs are allowlisted by a *prefix* rule in `.claude/settings.local.json`
(`PowerShell(& "test\\…\\Lumoin.Veritas.Tests.exe" *)` + the Bash twin). Run tests as
`& "test\Lumoin.Veritas.Tests\bin\Debug\net10.0\Lumoin.Veritas.Tests.exe" [--filter …] 2>&1 | …` (start with the
exe, never a `$out = …` capture) and builds as standalone `dotnet build … -clp:ErrorsOnly --nologo`, so they don't
re-prompt. The old accumulation of ~25 exact per-filter test rules was pruned.
- New `Algebra/AlgebraOperator.cs` (abstract record base) + `Algebra/AlgebraOperators.cs` (operator records +
  `AlgebraScope` helpers). The base exposes **lazy/cached `Children`** (the uniform traversal surface the future
  `AlgebraWalker` §10.5 drives — no per-operator walking code) and **`OutputVariables`** (scope analysis +
  optimizer). Per §10.1 both are declared on the base now. Caches are held **off-instance** in a static
  `ConditionalWeakTable` so they don't break record value equality (a lazy cache in a record *field* participates
  in value equality and would). Algebra carries **no spans**; reuses AST `ExpressionNode` / `OrderCondition` /
  `ValuesClause` / `GraphTerm` / `PropertyPathExpression`. Base name `AlgebraOperator` (the handoff's name).
- Operators: Bgp (normalized AST `TriplePattern`s), Join, LeftJoin, Union, Minus, Filter, Graph, Extend, Service,
  Table (wraps `ValuesClause`), UnitTable (the `{}` join identity), Path, Project, Distinct, Reduced, OrderBy,
  Slice, ToList, ToMultiSet. 9 `SparqlAlgebraTests` pin Children/OutputVariables per shape, caching, and
  value-equality-after-traversal. Build clean; full suite at exactly 50 ShaclValidate failures, 0 new.

**STILL OPEN (Milestone B remainder) — done so far: group/modifier translator (`1eafdc5`), property-path *pattern*
translation (`71e995f`), `AlgebraWalker` (`86d8245`), sub-`SELECT` → `ToMultiSet` (`79b9447`), aggregation
*operators* scaffold (`379b36e`), aggregation *translation* (`0057e17`), property-path form lowering (`6b6e4f2`).
**Milestone B's AST→algebra translation surface is now COMPLETE.** What remains is Milestone C (the executor): the
`Path`-leaf closure *evaluation* (incl. the native pair-aware closure for the both-var mode, reused by OWL — items
2–4 of the PROPERTY-PATH LOWERING plan above) lands there with the rest of the algebra evaluator. Scope: full
SPARQL 1.2 core, not a SHACL-minimal subset.

**Build/test (MTP — `dotnet test` does NOT work; exact commands that worked all session):**
- Build: `dotnet build src/Lumoin.Veritas.Sparql/Lumoin.Veritas.Sparql.csproj -clp:ErrorsOnly --nologo`,
  then `dotnet build Lumoin.Veritas.slnx -clp:ErrorsOnly --nologo` (0 warnings — `TreatWarningsAsErrors`).
- **Rebuild the TEST project before running the exe** (`dotnet build test/Lumoin.Veritas.Tests/Lumoin.Veritas.Tests.csproj …`)
  — a stale exe silently runs old tests.
- Run: `& "test\Lumoin.Veritas.Tests\bin\Debug\net10.0\Lumoin.Veritas.Tests.exe" --filter "FullyQualifiedName~Sparql"`,
  and the full suite with no filter. **Success = exactly 50 failed (all `ShaclValidate`), 0 new.** `KnownGaps`
  remaining (12, in `W3cSparqlSyntaxTests.cs`): scope-analysis (SELECT*/GROUP BY/BIND scope, duplicate
  projection), nested aggregates, `syn-bad-pname-06` (PN_LOCAL), and **`syntax-propertyPaths-01`** (a *standalone
  blank-node property list* `[ :p|:q|:r ?X ]` — the one item-4-adjacent gap; see below).

**⚠ Before coding, re-paste the implementation spec's §5.1 (namespace/folder layout) and §8.5 (post-normalization
shape).** The user holds it. Item 4 has two halves with different readiness:
- **(a) The normalization pass — buildable now from the Turtle emitter as the reference** (the handoff's
  "mirror the Turtle emitter's explicit-stack lowering"). Read `src/Lumoin.Veritas.Turtle/Emission/TurtleQuadEmitter.cs`
  — it is the worked lowering: `ResolveTerm` (explicit `ResolveFrame` stack, bottom-up), `BuildCollection`
  (rdf:first/rest/nil chain; empty → rdf:nil), `EmitPropertyListBody` (fresh bnode + per-predicate triples),
  `ExpandReifiedTriple` (reifier + `reifier rdf:reifies <<( s p o )>>`), `EmitAnnotations` (reifier pending/fresh,
  nested blocks via an `AnnotationFrame` stack).
- **(b) The `Algebra/` type hierarchy + the algebra translator — needs §5.1 and is Milestone B proper.** Do NOT
  fabricate the algebra layout without the spec.

**Normalization pass — design (record decisions as you implement):**
- New namespace `src/Lumoin.Veritas.Sparql/Translation/`. A normalizer transforming the parsed AST so every
  `BasicGraphPatternBlock` holds only plain `TriplePattern`s whose terms are the **four core cases**:
  `ConstantTerm`, `VariableTerm`, `PropertyPathTerm` (predicate only), `Ast.TripleTerm` (object). It lowers
  `CollectionTerm`, `BlankNodePropertyListTerm`, `ReifiedTriple`, `AnnotatedObject`, and the block's
  `StandaloneNodes` into fresh-bnode + plain triples.
- **Differs from the Turtle emitter:** (1) SPARQL terms include **variables** — preserve `VariableTerm`, never
  resolve; (2) **no prefix/IRI resolution** here — `ConstantTerm` already carries the resolved `NamedNode` from
  parse; (3) output is **`TriplePattern` (AST)**, not `Quad`; (4) the predicate may be a `PropertyPathTerm` —
  leave it; (5) **fresh blank nodes come from Core's `BlankNodeDelegate`** ([[project_veritas_injection_seams]] —
  raw counters are banned; the parser already takes a `blankNodes` delegate, default `VeritasBlankNodes.System` —
  thread the same seam into the normalizer), not the emitter's private `nextEmitterBlankNode`.
- **`rdf:first`/`rdf:rest`/`rdf:nil` are NOT yet in `Vocabulary.Rdf`** (it has `Type`, `Reifies`,
  `PropositionForm*` only) — add `First`/`Rest`/`Nil` there (the emitter currently interns the literals inline).
- **Preserve spans** onto every lowered triple (use the sugar term's `Span`).
- **SEMANTIC QUESTION for §8.5 — RESOLVED against the official W3C spec (2026-05-28).** Does a reified triple assert
  its inner triple? **NO.** Neither a triple term `<<( s p o )>>` nor a reified triple `<< s p o ~r >>` (standalone
  or as a term) asserts/adds `s p o` to the graph. Authoritative, verbatim:
  - RDF 1.2 Turtle **§2.11 "Reifying Triples"** — *"Note that this graph does not assert that `employee38` has a
    `jobTitle` of "Assistant Designer"; it says that `employee22` has made that claim using a reifiedTriple."*
  - RDF 1.2 Turtle **§7.3.2 "Reified Triples"** — finishing a `reifiedTriple` *"yields the RDF triple curReifier
    `rdf:reifies` curTripleTerm"* (a fresh blank node if no `~reifier`) — and nothing else.
  - RDF 1.2 Turtle **§2.11.1 "Annotation Syntax"** — the annotation syntax `s p o {| … |}` is *"an annotation syntax
    to both reify **and assert** a triple"* — i.e. only `{| |}` asserts the base `s p o`.

  **So the normalizer lowers a standalone `<< s p o ~r >> .` to ONLY `reifier rdf:reifies <<( s p o )>>`** — exactly
  the existing Turtle emitter `ExpandReifiedTriple` (the emitter was correct). ⚠ The item-3 commit message
  (`362f230`) "asserts its inner triple and reifier" was the WRONG intended normalization — **discard it**; the
  faithful parse decides nothing, and the normalizer must NOT assert the base triple. Base `s p o` is emitted ONLY
  for the `AnnotatedObject` (`{| |}`) sugar. (Residual: the verbatim SPARQL-1.2 §-anchor for the reifier-in-pattern
  expansion couldn't be pulled via WebFetch — the single-page spec truncates the reader window; SPARQL follows the
  shared RDF 1.2 data-model semantics + the emitter template. Confirm the exact §4.x anchor when coding.)
- **`syntax-propertyPaths-01`** (standalone `[ … ]` bnode list): item 3 routed only `ReifiedTriple` to
  `StandaloneNodes`; a standalone collection/bnode-list subject still errors (`ExpectedVerb`). To close this gap,
  either extend `StepTriple`'s `TripleVerb` to also route a verb-less `CollectionTerm`/`BlankNodePropertyListTerm`
  subject to `StandaloneNodes` (then the normalizer lowers it), or handle in the normalizer — decide with §8.5.
- Consumers to update for the new shape: `ExtractTemplateTriples` (CONSTRUCT WHERE — currently reads only
  `block.Triples`, so a CONSTRUCT-WHERE reified/standalone template contributes nothing until normalization runs)
  and any `SingleBlockTriples`/`SingleBlockStandaloneNodes` test helpers.

**Working pattern that worked this session:** AST-shape + lowering design done directly (it ripples into
Milestone B); add a unit test per feature pinning the AST/normalized shape; commit each green milestone; the
full-suite SHACL baseline (exactly 50) is the regression gate. Delegating *mechanical* passes to a
`general-purpose` subagent worked earlier, but item 4's lowering is design-sensitive — do it directly.

**Not part of the recovery batch (the §7.0 pass covered it for SPARQL):** with both parsers recovering,
Milestone B (algebra translator) builds against possibly-error-node ASTs from day one.

### ⏵ STEP 4 RESUME ANCHOR — SPARQL parser recovery rewrite (executed; kept as the record of what landed)

Branch `sparql-query-engine`. Commits: `62740e7` (Step 4 foundations: the seven error-node records),
`123e004` (Turtle Step 3 done — **the worked template for everything below**). Tree clean+green.
**Re-paste the "Parser error recovery — Handoff" spec on resume** if its SPARQL-specific §-references
(the `SP####` taxonomy, `SparqlInvalidRequestException`) are needed; the checklist itself is self-sufficient.

**This is the same retrofit just done to the Turtle parser** (`src/Lumoin.Veritas.Turtle/Parser/TurtleParser.cs`
in `123e004`) applied to the larger `src/Lumoin.Veritas.Sparql/Parser/SparqlParser.cs` (4297 lines, ~25
`ParseFrameKind`s, ~110 throw sites of which ~80 are grammar errors). Read the Turtle parser's recovery
helpers + `123e004` diff first; the SPARQL shapes are identical, only more numerous. The parser is the same
frame-stack/`Drive`/`StepResult`/`FeedToken`+`TryParseRequest` machine, with one difference: products share
no base, so each receive site does `Pop<T>(incoming)` — which is exactly why there are **seven** error-node
types (one per `T`) instead of Turtle's three.

**Build/test (MTP — `dotnet test` does NOT work):** build with
`dotnet build src/Lumoin.Veritas.Sparql/Lumoin.Veritas.Sparql.csproj -clp:ErrorsOnly --nologo`; run via the
built exe `test\Lumoin.Veritas.Tests\bin\Debug\net10.0\Lumoin.Veritas.Tests.exe --filter "FullyQualifiedName~Sparql"`.
Full suite: success = **no new failures over the 50-test SHACL baseline** (SPARQL syntax conformance is
~198 tests, 185 green + 13 self-correcting known-gap skips — recovery must not regress these; negative-syntax
tests must still surface `HasErrors`).

1. **Parser infra** (`SparqlParser.cs`): `using Lumoin.Veritas.Core.Diagnostics;`; add `DiagnosticBag? diagnostics = null`
   to both ctors (public + internal) → store `Diagnostics = diagnostics ?? new DiagnosticBag()`; add
   `internal DiagnosticBag Diagnostics { get; }`; make instance `ParseRequest()` **non-throwing** on malformed
   input (it already drives to `Produced`; under recovery the request always completes — a `NeedMore`-at-end
   becomes an error node, not the current throw) and add
   `public ParseResult<SparqlRequest> ParseToResult()` = `new(ParseRequest(), Diagnostics.Diagnostics, Diagnostics.HasErrors)`.
   Add the **static facade** `public static ParseResult<SparqlRequest> ParseRequest(ReadOnlyMemory<byte> source, Utf8StringPool? pool = null, Utf8String? baseIri = null)`
   (and/or `string`) that lexes, bridges `lexer.Diagnostics` via `SparqlLexDiagnosticBridge` into the bag, runs the
   instance parser, and returns the `ParseResult` (the locked "static `ParseRequest(text)` is a facade draining
   the bag" — §6/§9.1). The lexer-bridge drain mirrors Turtle's `BridgeLexerDiagnostics`.
2. **Recovery helpers** (place by `Expected`; KEEP `Expected` until every caller is converted, then delete it):
   `Report(code, span, msg)`; `ReportRecoverable(code, span, msg)` → returns `[code]` unless `Current.Kind == SparqlTokenKind.Error`
   (then `[]`, no double-report — the bridged `LX####` stands alone); `ResyncTo(ParseFrameKind, startSpan, out lastSpan)`
   (the `index==before` clamp guard); `IsResyncToken(kind, token)` resync table (below); and the **seven**
   `Recover{GraphPattern,QueryForm,Expression,PropertyPath,TriplePatternTerm,GraphTerm,Annotation}` builders —
   each `ReportRecoverable` + `ResyncTo` + build its error record (`CombineSpans(startSpan, lastSpan)`, `[code]`, skipped).
   Plus `CanStartTerm`/`CanStartTriple` (already exists)/`CanStartGroupMember` guards so list/group frames push a
   sub-frame only on a token that can start the production — **the progress guarantee** (Turtle's key realisation:
   a stray closer no frame consumes must be skipped at the group/request level, else the stack spins).
3. **Resync table per `ParseFrameKind`** (default = nearest separator/closer): `Request`/`SelectClause`→`{Dot,CloseBrace,EndOfInput}`;
   `GroupGraphPattern`/`UnionPattern`/`Optional`/`Minus`/`Graph`/`Service`→`{CloseBrace}`; `Triple`→`{Dot,Semicolon,CloseBrace}`;
   `Collection`→`{CloseParen}`; `BlankNodePropertyList`→`{CloseBracket}`; `TripleTerm`→`{CloseTripleTerm,Dot,CloseBrace}`;
   `ReifiedTriple`→`{CloseReifiedTriple,Dot,CloseBrace}`; `AnnotationBlock`→`{CloseAnnotation}`; `Expression`/`ArgumentList`→`{CloseParen,Comma}`;
   `PropertyPath`/`PathSequence`/`PathElement`/`PathNegatedSet`→`{CloseParen,CloseBrace,Dot,Semicolon}`;
   `Values`→`{CloseBrace,CloseParen}`; `GroupBy`/`Having`/`OrderBy`/`Filter`/`Bind`→`{CloseBrace,Dot}`;
   `ConstructTemplate`→`{CloseBrace}`. (+`EndOfInput` always stops.) Confirm against each frame's closer while converting.
4. **Convert throw sites → value-based** (taxonomy mirrors Turtle exactly):
   - **Grammar errors** (the ~80 `throw Expected(...)` + the structural `throw new SparqlParseException` at a real
     cursor position) ⇒ recover. Map each to its product base's `Recover*` (term position → `RecoverTriplePatternTerm`;
     graph-designator → `RecoverGraphTerm`; pattern/member → `RecoverGraphPattern`; expression → `RecoverExpression`;
     path → `RecoverPropertyPath`; form dispatch (line 440) → `RecoverQueryForm`; annotation → `RecoverAnnotation`).
     Close-token-missing sites and list-frame "expected X or closer" sites → report + resync/finalise-partial (no error
     node where a list simply ends early — wrap the `Error*` children it already holds, as Turtle's list frames do).
   - **Validation / semantic** (complete construct violates a positional rule) ⇒ report-and-continue, keeping the faithful
     node: comparison-chaining (lines 1270/1298), `IF`-arity (1769), `VALUES` duplicate-var/arity (3593/3618), prefixed-name
     subject-vs-path checks. These already have specific SP codes.
   - **KEEP as throws (invariants)**: every `"reached an unknown stage"`, `"Parser driver reached an undefined state"`,
     `"production '…' is not yet implemented"`, `"is not a binary/unary operator"` (lines 2488/2505); `ConsumePrefixedName`
     missing-`:` (4060) and the dir-langtag missing-`--` (4102) (lexer-guaranteed quasi-invariants). The unbound-prefix
     resolution error is **input-reachable** (a prefixed name with no `PREFIX`) ⇒ recover with `SP0006 UnboundPrefix`
     (return an `ErrorTriplePatternTerm`/`ErrorGraphTerm` per position).
5. **`Drive` incoming lexer `Error` tokens**: the reader/facade bridges them already; the natural recover path with the
   `ReportRecoverable` Error-suppression handles them (Turtle added no special `Drive` code — same here). Keep the
   `Drive`/`Step` invariant throws.
6. **SP#### codes** (`WellKnownDiagnostics.Sparql`, currently SP0001–SP0014 + SP9999): **decide the granularity** (the
   LX/TT layers went fine-grained at the user's explicit call — §10 Q4; the SPARQL set is currently coarse). Reuse the
   existing specific codes (ExpectedQueryForm, ExpectedWhereKeyword, UnclosedGroupGraphPattern, ExpectedTriplePattern,
   ExpressionExpected, UnclosedExpression, UnboundPrefix, …); add structural-closer + expected-X codes as needed
   (unclosed collection/bracket/paren/triple-term/reified-triple/annotation, expected-path, expected-term, expected-var,
   …) referencing each by name at its site. `SP0005 UnexpectedToken` is the catch-all only where nothing specific fits.
7. **Reader / callers / tests** (small surface): the 9 negative `Assert.Throws<SparqlParseException>` in
   `SparqlParserTests` (lines 299, 383, 619, 1153, 1173, 1182, 1260, 1318, 1327) → `ParseToResult()`/facade then assert
   `HasErrors` + the expected code. `W3cSparqlSyntaxTests.ParseSparqlQuery` (line 154) re-raises on `HasErrors` so the
   shared `W3cTestRunner` keeps catch-based negative-syntax detection (the SPARQL analog of `TurtleConformanceReader` —
   or generalise that adapter). Positive `SparqlParserTests` keep using `ParseRequest()`/`ParseToResult` and assert no
   errors. **Verify the 13 `KnownGaps` still skip** (a gap that starts passing fails the harness) and **no negative test
   regresses** (recovery must still flag every currently-rejected query with `HasErrors`).
8. **Additive tests** (may be a 2nd commit, as Turtle's were): a `SparqlParserRecoveryTests` mirroring
   `TurtleParserRecoveryTests` — per-frame resync (unclosed `{`/`(`/`[`/`<<(`/`{|`, bad form, bad projection, bad path,
   missing predicate), recovery-continuation (a trailing clean clause still parses), cascade-suppression (bounded count
   for one unclosed brace), real spans, and a reactive-editor incremental `FeedToken`/`TryParseRequest` feed.

**Q resolved (locked, carried from Step 3):** value-based everywhere, single mode, no backwards-compat hedges; the
reader/facade never throws on malformed input (only I/O, cancellation, resource limits throw); internal-invariant guards
stay throws. The SPARQL engine (`SparqlQueryEngine`/`ExecuteAsync`) still does **not** exist — Step 4 lands the parser
recovery + the `ParseToResult`/facade only; §7/Step-5 `ExecuteAsync` wiring stays deferred to Milestone C.

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
