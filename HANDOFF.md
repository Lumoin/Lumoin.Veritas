# Handoff — Lumoin.Veritas.Sparql (Batch 1: Milestones A–C, SHACL-SPARQL unlock)

Branch: `sparql-query-engine` (off `main`). Executing the SPARQL implementation
specification. Two-batch split: **Batch 1 = Milestones A–C** (lexer → parser →
AST → algebra → translator → hypertrie backend + executor subset → SHACL-SPARQL
unlock). **Batch 2 = Milestones D–F** (full algebra/aggregation, CONSTRUCT/
DESCRIBE/JSON results, conformance push).

## Status

- Milestone A: **in progress.** Project skeleton builds. **Lexer complete and
  tested** (13 `SparqlLexerTests` green). **AST complete** — ~60 record types
  across 12 files in `Ast/`. **Parser: the bulk is built and tested**
  (66 `SparqlParserTests` green), and it is **resumable/streaming** — incremental
  `FeedToken` + `TryParseRequest`(`ParseStatus`), a `Drive()` that suspends on
  `NeedMore` with the work stack intact, bounded `MaxStepLookahead=4`, every
  unbounded production driven one bounded item per step. Materialised
  `ParseRequest` is a convenience on top. Built so far (each its own commit):
    - Prologue (BASE/PREFIX with prefix-map + base IRI resolution via Core
      `IriResolver`; unbound prefix throws).
    - Forms: SELECT (DISTINCT/REDUCED, `*` or projection list incl.
      `(expr AS ?v)`) and ASK. CONSTRUCT/DESCRIBE recognised + throw "not yet".
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
  - **Style decisions recorded** (apply going forward): the parser uses C# switch
    *expressions* for all dispatch (state + token kind), per user preference; and
    no `Co-Authored-By` trailers on commits/PRs (user directive — branch history
    was rewritten to strip them).
  - **Remaining for Milestone A:**
    - Slice 4b — unbounded term sugar in subject/object position: RDF collections
      `( ... )`, blank-node property lists `[ p o ]`, and RDF 1.2 reified triples
      `<< s p o >>` / triple terms `<<( s p o )>>`. **OPEN DESIGN DECISION:** the
      committed `TriplePatternTerm` AST has only ConstantTerm / VariableTerm /
      PropertyPathTerm / TripleTerm — no Collection or BlankNodePropertyList node.
      So either (a) **expand at parse time** into fresh blank nodes + extra
      `rdf:first`/`rest`/`nil` and `[ ]` triples appended to the enclosing BGP run
      (needs a parser-scoped "side triples" sink tied to the active Triple frame),
      or (b) **add AST term nodes** and let the translator lower them (matches the
      Turtle precedent where CollectionTerm/BlankNodePropertyListTerm are AST
      nodes expanded at emission). Decide before implementing.
    - Slice 5 — CONSTRUCT template, DESCRIBE targets, trailing + inline VALUES,
      sub-SELECT pattern; then vendor the W3C SPARQL 1.2 syntax suite and wire
      `SparqlPositiveSyntax`/`SparqlNegativeSyntax` into the conformance harness.
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
