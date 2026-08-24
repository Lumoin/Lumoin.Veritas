# Attribution — the A5 DGGS fixture corpus and kernel port

## a5 (a5-js reference implementation)

- Source: https://github.com/felixpalmer/a5
- Pinned commit: `6ee88da29d44ef97e9fa7d0afb61e3edc3f58910` (the v0.9.0 release, 2026-06-17)
- License: Apache License 2.0
- Copyright: the a5 authors

The `Fixtures/` corpus under this directory is a verbatim, frozen copy of the reference
implementation's test fixtures (see `Fixtures/PROVENANCE.md` for the per-directory layout map and
the freeze rules); `Fixtures/MANIFEST.sha256` pins every file's hash, asserted in both directions
by the fixture-manifest tests. The A5 kernel under `src/Lumoin.Veritas.Geo/Dggs/` is a
formula-exact port of the same pinned release: numeric formulas and operation order match the
reference so the corpus certifies the port bit-for-bit on integer outputs and at the upstream
tolerances on floating-point outputs.

## Natural Earth populated places

- Source: Natural Earth, `ne_50m_populated_places` (name-only extract vendored via the corpus above)
- License: public domain

Used as an input-only property driver for the containment sweep tests.
