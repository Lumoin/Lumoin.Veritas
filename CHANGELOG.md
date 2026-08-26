# Change Log

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

<!-- Available types of changes:
### Added
### Changed
### Fixed
### Deprecated
### Removed
### Security
-->

## [Unreleased]

### Changed

- Studio: the workspace shell is responsive. Phones and short landscape viewports show one complete
  pane at a time behind a persistent Edit / Results / Why navigation driven by the location hash;
  tablets keep the editor beside the stacked Results and Why panes; the top bar, worlds bar, status
  bar, scenario dialog, and graph overlays reflow without horizontal overflow.

## [0.0.1] - 2026-08-24

Initial release.

### Added

- The Lumoin.Veritas stack: RDF 1.2 graph storage with hypertrie and columnar indexing, a SPARQL
  1.2 query engine, RDF Dataset Canonicalization, JSON-LD 1.1 and CBOR-LD processing, SHACL 1.2
  Core validation with SHACL-SPARQL, an OWL 2 RL reasoner with EL classification and OWL DL
  checking, SKOS primitives, GeoSPARQL functions with DGGS support, JSONata, JSON Schema, JSON
  Pointer, CBOR and CID codecs, durable storage with a verify-repair-commit self-heal loop,
  many-worlds dataset branching with world-scoped queries, updates, validation, and diffs on the
  engine facade and its wire documents (the worlds listing with state identifiers and fork
  lineage, and the bounded diff with exact totals), rateless anti-entropy replication with a
  consensus-coordinated
  metadata plane, and the N-Quads, Turtle, TriG, RDF/XML, and SPARQL-results serialization
  surfaces.
- The `veritas` command-line tool: the `query` command, an MCP server, a SPARQL 1.1 Protocol HTTP
  endpoint with first-party `/worlds` routes over a mutable serve (fork, drop, world-scoped query
  and update, diff), the Veritas Studio browser workbench served on the same origin — its worlds
  bar driving the what-if flow (create a scenario world, with knobs from dataset-declared levers,
  update in it, bounded tabular diff, drop) with the primary world untouched — and the
  `replicate` command hosting a store-backed replica with its metadata plane.
