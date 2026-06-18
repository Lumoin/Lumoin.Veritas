# W3C OWL 2 Conformance Test Cases

Source: the W3C OWL Working Group test repository archive at
<https://www.w3.org/2009/11/owl-test/>, snapshotted on 2026-06-05. This is the
stable w3.org archive of the repository formerly curated at
`owl.semanticweb.org` (see <https://www.w3.org/2009/owl-test-cases>); the test
case format and conformance conditions are defined by
[OWL 2 Conformance (Second Edition)](https://www.w3.org/TR/owl2-conformance/).

Original paths within the archive:

- `approved/*.rdf` → `Material/Owl2/approved/` — test cases with
  `test:status Test:Approved` (355 test cases in `all.rdf`).
- `proposed/*.rdf` → `Material/Owl2/proposed/` — proposed-status test cases
  (86 test cases in `all.rdf`).

Each slice file (`profile-EL.rdf`, `profile-QL.rdf`, `profile-RL.rdf`,
`syntax-dl.rdf`, `semantics-direct.rdf`, `type-consistency.rdf`,
`type-inconsistency.rdf`, `type-positive-entailment.rdf`,
`type-negative-entailment.rdf`) is a filtered export of the same test pool;
`all.rdf` is the superset. The upstream `RL-RDF-rules-tests.rdf` category is
empty in both status arms (zero test cases) and is not vendored.

Format: RDF/XML manifests in the `http://www.w3.org/2007/OWL/testOntology#`
vocabulary. Each `test:TestCase` is multi-typed
(`ProfileIdentificationTest`, `PositiveEntailmentTest`,
`NegativeEntailmentTest`, `ConsistencyTest`, `InconsistencyTest`) and carries
its premise/conclusion (or input) ontology documents **inline** as escaped
strings (`test:rdfXmlPremiseOntology`, `test:fsPremiseOntology`, …) — no
sibling-file resolution. `test:profile` / `test:species` annotations state
positive syntactic membership (EL/QL/RL, DL/FULL); per the conformance
document every test doubles as a profile-identification test, with absence of
a profile marker meaning the ontology is NOT in that profile.

Approved-arm census (2026-06-05): 355 test cases, every one typed
`ProfileIdentificationTest` (= 1,065 profile verdicts across EL/QL/RL: 182
positive markers — EL 67 / QL 45 / RL 70 — and 883 negatives by absence);
additional types on the same tests: 237 Consistency, 118 Inconsistency,
143 PositiveEntailment, 9 NegativeEntailment. Premise documents: 335
`rdfXmlPremiseOntology`, 60 `fsPremiseOntology` (normative syntax RDFXML 355 /
FUNCTIONAL 60; functional-syntax-only tests require an OWL functional-syntax
reader or are documented skips), 3 `rdfXmlInputOntology`, 3 imports-bearing
tests (`test:importedOntology`).

Licensed under the W3C Software and Document licence / W3C Test Suite licence
terms applicable to the OWL WG test repository. Copyright © W3C® and the test
case authors.

These files are vendored into this repository to make the conformance test
suite reproducible without external setup. Updating the corpus is a deliberate
re-copy: replace the contents of the subfolders, update the source notes
above, and re-run the conformance suite.
