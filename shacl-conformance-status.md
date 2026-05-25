# SHACL 1.2 conformance status

Measured against the vendored W3C SHACL 1.2 test suite
(`test/Lumoin.Veritas.Tests/Material/Shacl/data-shapes-test-suite/tests/`,
`w3c/data-shapes` gh-pages `6efe110`) by `W3cShaclTests` driving `W3cShaclRunner`,
on branch `shacl-conformance-harness` (2026-05-25).

## Headline

| Outcome | Count |
| --- | --- |
| Total discovered | 120 |
| **Passed** | **63** |
| Failed | 50 |
| Skipped | 7 |

63 / 120 = **52.5%** of all discovered tests; **55.8%** of the 113 that are
comparable as a report (excluding the 7 skips).

The 7 skips are `mf:result sht:Failure` tests (all in `sparql/pre-binding/`):
they expect the validation *process* to fail rather than to produce a report,
so there is no expected report to compare under isomorphism. They are reported
Inconclusive, never silently passed.

## Method

Each `sht:Validate` leaf is validated with `ShaclValidator` (the iterative
engine, `ShaclBuiltInEvaluators.All` + `ShaclBuiltInComponents.All`). The
produced `ValidationReport` is serialized to RDF by `ValidationReportSerializer`
and compared to the manifest's inline expected `sh:ValidationReport` under
blank-node isomorphism (RDFC-1.0), after `sh:resultMessage` is stripped from
both sides — message text is advisory and the suite does not require it to
match. The data and shapes graphs come from `sht:dataGraph` / `sht:shapesGraph`
(often the empty IRI `<>`, i.e. the test file itself; sometimes sibling
`*-data.ttl` / `*-shapes.ttl` files); the expected report is read from the
manifest file regardless of where the graphs live.

**Comparison caveat.** Isomorphism-minus-messages is somewhat *stricter* than
the W3C result-tuple-set rule: a report that differs only by one extra or
absent property fails here even when the underlying result tuples agree. So 63
is a conservative lower bound — a looser result-tuple-set comparison would pass
at least as many. Some "structural near-miss" failures below are likely
comparison strictness rather than semantic gaps.

## Failure clusters (50)

1. **Under-validation — 32.** The validator finds no violations
   (`sh:conforms true`, zero results) where the expected report has them.
   Dominated by **SHACL-SPARQL** (`sh:sparql` / `sh:SPARQLConstraint`, the
   `sparql/*` sub-suites), which the engine does not implement, plus a tail of
   constraints that do not fire on these particular fixtures. Largest and most
   clustered group.
2. **Structural near-miss — 14.** The validator reports `sh:conforms false`
   with some results, but the report is not isomorphic to expected (frequently
   off by only a few quads). A mix of partial constraint coverage and
   report-shape differences (value-node presence, path serialization); see the
   comparison caveat — several of these may be strictness, not gaps.
3. **Loader rejects valid SHACL — 3.** The shape loader throws on input the
   spec permits:
   - a custom `sh:severity` IRI (`core/misc/severity-002`) — the loader accepts
     only `sh:Violation` / `sh:Warning` / `sh:Info`;
   - the boolean lexical form `"1"` rejected as "not a valid boolean"
     (`"1"`/`"0"` are valid `xsd:boolean`);
   - a required parameter reported absent on an otherwise-loadable shape.
4. **Info-only — 1.** The validator emits an `Info` (not-implemented) result in
   place of the expected violation, leaving `sh:conforms true`.

## Notes for the fix batches

- SHACL-SPARQL (cluster 1) is the single largest lever and a distinct feature;
  it is out of scope for this measurement batch.
- Cluster 3 is small and concrete (loader leniency: custom severities, boolean
  lexical forms, parameter strictness) — low-risk early wins.
- Before chasing cluster 2 case by case, evaluate loosening the report
  comparison to the W3C result-tuple-set rule; that may reclassify several
  near-misses as passes.

`W3cShaclTests` is currently expected-red: this batch *measures*, it does not
gate. Whether to gate SHACL conformance in CI (e.g. a category excluded from
the green build until the fix batches land) is a separate decision.
