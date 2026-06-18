# W3C RDF Test Cases

Source: <https://github.com/w3c/rdf-tests/> at branch `main`, working
tree snapshotted on 2026-05-22. Verified on 2026-05-23 against
upstream `main` HEAD `4c255ad` (2026-05-21): the snapshot is at or
after that commit and includes the recent version-directive and
surrogate-pair test additions, so no re-vendoring was required.

Original paths within the upstream repository:

- `rdf/rdf12/rdf-turtle/` → `Material/Turtle/turtle/`
- `rdf/rdf12/rdf-trig/` → `Material/Turtle/trig/`

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

Only the RDF 1.2 corpus is vendored. The RDF 1.2 top-level
manifests include `../../rdf11/...` references for the RDF 1.1
test corpus; those includes will not resolve against this
vendored snapshot. The loader treats unresolved includes as
soft skips and records them in `Conformance/conformance-status.md`.
Vendoring the RDF 1.1 corpus is deferred.
