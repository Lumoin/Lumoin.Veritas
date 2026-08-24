# Third-party notices

This repository vendors a small amount of third-party content and models some demonstration
data on public vocabularies. Every vendored corpus carries an `ATTRIBUTION.md` beside it
stating the source, the pinned upstream revision, the license, and the copyright; this file
is the index.

## Vendored content

- **Conformance and benchmark corpora** under `test/Lumoin.Veritas.ParserTests/Material/` —
  W3C test suites and related community corpora used to certify conformance. Each corpus
  directory (`Rdf`, `Turtle`, `NQuads`, `Sparql`, `Shacl`, `Owl2`, `RdfCanon`, `JsonLd`,
  `JsonSchema`, `Jsonata`, `Geo`, `Benchmark`, `Benchmark/ORE2014`) carries its own
  `ATTRIBUTION.md` with the license and provenance.
- **DGGS fixture corpus** under `test/Lumoin.Veritas.Tests/Geo/Dggs/Fixtures/` — frozen
  cross-implementation fixtures derived from an Apache-2.0 upstream at a pinned commit; see
  `test/Lumoin.Veritas.Tests/Geo/Dggs/ATTRIBUTION.md` and the `PROVENANCE.md` beside the
  fixtures. The Apache-2.0 license text is the repository root `LICENSE` file.
- **The Commissioner variable font** under `src/Lumoin.Veritas.Studio.Web/src/fonts/` —
  SIL Open Font License 1.1; see the `ATTRIBUTION.md` beside it.

## Studio runtime dependencies

The Studio's built bundle carries two npm packages, both redistributed under licences that
require their notices to travel with them:

- **@babylonjs/core** — Apache License 2.0. The licence text is the repository root
  `LICENSE` file.
- **uhtml** — MIT License, Copyright (c) 2020 Andrea Giammarchi.

## Studio sample datasets

The three demonstration datasets under `src/Lumoin.Veritas.Studio.Web/public/datasets/`
(mirrored into the built `wwwroot/` bundle) are original, hand-authored illustrative data
written for this repository, and every vocabulary they use is this project's own, under
`https://veritas.app/ns/`. Where they reference third-party work:

- The battery sample's digital-product-passport framing follows the concepts the UN/CEFACT
  United Nations Transparency Protocol describes. It reuses none of that protocol's
  vocabulary IRIs, and its figures are invented for the demonstration.
- The social-network and university samples are shaped like the well-known friend-of-friend
  and class-hierarchy workloads so those query patterns are recognizable. The triples are
  original illustrative data, not extracted from any benchmark's generators or published
  distributions.
