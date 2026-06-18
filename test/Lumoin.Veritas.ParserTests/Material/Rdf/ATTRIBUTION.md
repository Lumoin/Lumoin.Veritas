# W3C RDF/XML Test Cases

Source: <https://github.com/w3c/rdf-tests/>, pinned commit
`674da267646573441808560564451fe5cc77aef7`.

Original paths within the upstream repository:

- `rdf/rdf11/rdf-xml/` → `Material/Rdf/rdf-xml/`
- `rdf/rdf12/rdf-xml/` → `Material/Rdf/rdf-xml-12/`

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

The upstream archive artifacts (`TESTS.tar.gz`, `TESTS.zip`), the
`convert-manifest.rb` helper script, and the generated `reports/`
directory are deliberately omitted — only the manifests, the
`.rdf` input fixtures, and the `.nt` expected fixtures are
vendored.

Each suite's `manifest.ttl` carries an `mf:assumedTestBase` HTTP
IRI (`https://w3c.github.io/rdf-tests/rdf/rdf11/rdf-xml/` and the
RDF 1.2 `.../rdf12/rdf-xml/` + `.../rdf12/rdf-xml/eval/`); the
conformance harness composes that base with each input file name
so relative references in the fixtures resolve as upstream
intends.

The RDF 1.2 manifest (`rdf-xml-12/manifest.ttl`) includes
`../../rdf11/rdf-xml/manifest.ttl`; that cross-reference does not
resolve against this vendored layout and is recorded as an
unresolved include rather than failing the load (the RDF 1.1
suite is vendored and run separately).
