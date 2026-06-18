# W3C JSON-LD 1.1 API + Framing Test Suites

Source (expand/compact/toRdf/fromRdf/flatten): https://github.com/w3c/json-ld-api/tree/main/tests
Pinned commit: 265e6b433a4eb25bad99d941c95f2ccecd6a8c1d (verified 2026-06-01)

Source (frame): https://github.com/w3c/json-ld-framing/tree/main/tests
Pinned commit: 5437dbc5c0db543ccfa1b6b36197cc3687fc1b34 (verified 2026-06-03)

Licensed under the W3C Software and Document Notice and License
(BSD-style); see `LICENSE.md`. Copyright © W3C® and the test authors.

These files are vendored to make the conformance test suite
reproducible without external setup. Updating the corpus is a
deliberate re-copy: replace the contents here, update the commit SHA
above, and re-run the conformance suite.

## What is vendored

Only the operations `Lumoin.Veritas.JsonLd` implements are vendored:

- `expand-manifest.jsonld` + `expand/`   — JSON-LD expansion tests
- `compact-manifest.jsonld` + `compact/` — JSON-LD compaction tests
- `toRdf-manifest.jsonld` + `toRdf/`     — JSON-LD → RDF (N-Quads) tests
- `fromRdf-manifest.jsonld` + `fromRdf/` — RDF (N-Quads) → JSON-LD tests
- `flatten-manifest.jsonld` + `flatten/` — JSON-LD flattening tests
- `frame-manifest.jsonld` + `frame/`     — JSON-LD framing tests (from the
  separate `json-ld-framing` repo; each entry adds a `frame` document)
- `manifest.jsonld`, `context.jsonld`, `vocab.jsonld`, `vocab.ttl`,
  `vocab_context.jsonld` — shared root manifest / contexts the tests
  reference.

The upstream `html/`, `remote-doc/` (require HTML extraction / real
remote fetching), and the rendering/tooling assets (`*.html`, `*.haml`,
`Rakefile`, `mk_vocab.rb`, `README.md`) are deliberately not vendored.

## Manifest shape

Each `*-manifest.jsonld` is a JSON-LD document with a top-level
`baseIri` (the suite's retrieval URL space) and a `sequence` of test
entries. Each entry carries `@id`, `@type` (e.g.
`jld:PositiveEvaluationTest` / `jld:NegativeEvaluationTest` plus
`jld:ExpandTest` / `jld:CompactTest` / `jld:ToRDFTest`), `name`,
`input`, and either `expect` (a result file) or `expectErrorCode` (for
negative tests), plus an optional `option` object (`base`,
`expandContext`, `processingMode`, `specVersion`, …). The harness loads
these via `JsonLdManifestLoader` and resolves the `baseIri` prefix to
this directory.
