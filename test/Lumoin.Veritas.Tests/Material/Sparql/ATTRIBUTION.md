# W3C SPARQL Test Cases

Source: <https://github.com/w3c/rdf-tests/> at branch `main`, pinned to
commit `674da267646573441808560564451fe5cc77aef7`, snapshotted on
2026-05-26.

Original paths within the upstream repository:

- `sparql/sparql11/syntax-query/` → `Material/Sparql/syntax-query/`
- `sparql/sparql12/syntax/` → `Material/Sparql/syntax/`

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

Deferred:

- The RDF 1.2 triple-term syntax suites
  (`sparql/sparql12/syntax-triple-terms-{positive,negative}/`) wait on the
  RDF 1.2 reified-triple / triple-term parser slice; until then the parser
  would reject valid triple-term syntax and the results would mislead.
- SPARQL **Update** syntax tests (`mf:*UpdateSyntaxTest*`) are out of scope —
  this build parses queries, not update requests — and the conformance loader
  classifies those test types as unknown, so the runner skips them.
- Evaluation suites (query results) wait on the algebra/executor milestones.
