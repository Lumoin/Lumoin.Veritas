# W3C SPARQL Test Cases

Source: <https://github.com/w3c/rdf-tests/> at branch `main`, pinned to
commit `674da267646573441808560564451fe5cc77aef7`, snapshotted on
2026-05-26.

Original paths within the upstream repository:

Query-syntax suites:

- `sparql/sparql11/syntax-query/` → `Material/Sparql/syntax-query/`
- `sparql/sparql12/syntax/` → `Material/Sparql/syntax/`
- `sparql/sparql12/syntax-triple-terms-positive/` → `Material/Sparql/syntax-triple-terms-positive/`
- `sparql/sparql12/syntax-triple-terms-negative/` → `Material/Sparql/syntax-triple-terms-negative/`
- `sparql/sparql12/version/` → `Material/Sparql/version/`

Query-syntax (cont.):

- `sparql/sparql12/codepoint-escapes/` → `Material/Sparql/codepoint-escapes/`

Query-evaluation suites — the complete set the upstream
`manifest-sparql11-query.ttl` / sparql12 `manifest.ttl` reference
(snapshotted 2026-05-30, same pinned commit):

- `sparql/sparql11/aggregates/` → `Material/Sparql/aggregates/`
- `sparql/sparql11/bind/` → `Material/Sparql/bind/`
- `sparql/sparql11/bindings/` → `Material/Sparql/bindings/`
- `sparql/sparql11/cast/` → `Material/Sparql/cast/`
- `sparql/sparql11/construct/` → `Material/Sparql/construct/`
- `sparql/sparql11/exists/` → `Material/Sparql/exists/`
- `sparql/sparql11/functions/` → `Material/Sparql/functions/`
- `sparql/sparql11/grouping/` → `Material/Sparql/grouping/`
- `sparql/sparql11/negation/` → `Material/Sparql/negation/`
- `sparql/sparql11/project-expression/` → `Material/Sparql/project-expression/`
- `sparql/sparql11/property-path/` → `Material/Sparql/property-path/`
- `sparql/sparql11/subquery/` → `Material/Sparql/subquery/`
- `sparql/sparql12/expression/` → `Material/Sparql/expression/`
- `sparql/sparql12/eval-triple-terms/` → `Material/Sparql/eval-triple-terms/`
- `sparql/sparql12/grouping/` → `Material/Sparql/sparql12-grouping/` (renamed to avoid the sparql11 `grouping` collision)
- `sparql/sparql12/rdf11/` → `Material/Sparql/sparql12-rdf11/`
- `sparql/sparql12/lang-basedir/` → `Material/Sparql/lang-basedir/`

Licensed under the dual W3C Test Suite licence and W3C 3-clause
BSD licence per the upstream `LICENSE.md`:
<https://www.w3.org/Consortium/Legal/2008/04-testsuite-copyright.html>.

Copyright © W3C® and the Test Case authors.

These files are vendored into this repository to make the
conformance test suite reproducible without external setup.
Updating the corpus is a deliberate re-copy: replace the
contents of the subfolders, update the source notes above, and
re-run the conformance suite.

## Scope notes

Only the **query syntax** suites are vendored so far, and only those
covering features the parser implements:

- `syntax-query/` — SPARQL 1.1 query syntax (`mf:PositiveSyntaxTest11` /
  `mf:NegativeSyntaxTest11`).
- `syntax/` — SPARQL 1.2 general query syntax (`mf:PositiveSyntaxTest` /
  `mf:NegativeSyntaxTest`).
- `syntax-triple-terms-positive/` & `syntax-triple-terms-negative/` — RDF 1.2
  triple terms, reified triples, reifiers, and object annotations (Slice 4b-ii).
- `version/` — the SPARQL 1.2 `VERSION` prologue declaration.

A few RDF 1.2 sub-features the parser does not yet wire — standalone reified
triples (`<< … >>` with an empty property list), triple terms in VALUES and
expression positions, and the `VERSION` declaration itself — are recorded as
self-correcting known-gaps in `W3cSparqlSyntaxTests.KnownGaps` (reported
inconclusive; the run fails if any starts passing) until those slices land.

An initial set of query-**evaluation** suites is now vendored and run by
`W3cSparqlEvalTests` (parse → normalize → translate → execute → compare). A
`SELECT`/`ASK` test with a SPARQL Results fixture (`.srx`/`.srj`) is executed;
`CONSTRUCT`/`DESCRIBE` (RDF-graph results), update entries in mixed manifests,
queries using an unsupported operator, and tests whose data graph is in a format
the harness cannot read (RDF/XML, TriG) are reported inconclusive, not failed.
Genuine engine gaps are recorded in `W3cSparqlEvalTests.KnownGaps` (the
conformance frontier to drive down): aggregate numeric-tower typing,
`GROUP_CONCAT`, correlated `NOT EXISTS` set semantics, some sub-`SELECT`
semantics, `xsd:boolean` non-canonical lexicals, and RDF-star triple-term
pattern matching.

Deferred:

- SPARQL **Update** syntax/evaluation tests are out of scope — this build parses
  and evaluates queries, not update requests.
- Evaluation suites that need the quad/dataset backend (`GRAPH`/`FROM NAMED`),
  federation (`SERVICE`), entailment regimes, or RDF/XML data are not yet
  vendored or are skipped structurally. `construct`/`describe`, `service`,
  `entailment`, `csv-tsv-res`, and `json-res` remain unvendored.
