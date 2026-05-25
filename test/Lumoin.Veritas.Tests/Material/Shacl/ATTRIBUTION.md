# W3C SHACL 1.2 Test Suite

Source: https://github.com/w3c/data-shapes/tree/gh-pages/data-shapes-test-suite
Branch: gh-pages
Pinned commit: 6efe110dfd76208e117846d65c678b33c1915221 (verified 2026-05-25)

Licensed under the W3C Software and Document Notice and License
(BSD-style).

Copyright © W3C® and the Test Case authors.

These files are vendored to make the conformance test suite
reproducible without external setup. Updating the corpus is a
deliberate re-copy: replace the contents of
`data-shapes-test-suite/`, update the commit SHA above, and re-run
the conformance suite.

Only the `tests/` subtree is vendored; the upstream `index.html`,
`javascripts/`, `stylesheets/`, `reports/`, `makefile`, and
`README.md` are rendering and tooling assets, not test material.

## Corpus shape

Each leaf SHACL test file is self-contained: a single Turtle file
declares its own `mf:Manifest` with one `mf:entries` list of one
entry, and the entry's `mf:action` carries `sht:dataGraph <>` plus
`sht:shapesGraph <>` (both pointing at the file itself).
`mf:result` is an inline blank-node-rooted expected
`sh:ValidationReport` graph. The directory-level `manifest.ttl`
files chain to these leaves through repeated `mf:include` triples
(not an RDF list); the harness loader handles both that form and
the RDF-list form used by the RDF / SPARQL test suites.

## Vendored layout

```
data-shapes-test-suite/tests/
  manifest.ttl                                 root (includes core + sparql)
  core/manifest.ttl                            includes 7 sub-area manifests
  core/{complex,misc,node,path,property,targets,validation-reports}/manifest.ttl
  sparql/manifest.ttl                          includes 4 sub-area manifests
  sparql/{component,node,pre-binding,property}/manifest.ttl
```

14 `manifest.ttl` files; 136 leaf `.ttl` test files (150 `.ttl` total).
There is no `rules/` or `node-expressions/` subtree — those SHACL 1.2
features have no test corpus in this snapshot.
