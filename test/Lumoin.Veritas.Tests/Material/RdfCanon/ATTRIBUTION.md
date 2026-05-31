# W3C RDF Dataset Canonicalization (RDFC-1.0) Test Cases

Source: <https://github.com/w3c/rdf-canon/> at branch `main`, pinned to
commit `15619df2fda7a4ca88308733789b6774517f9638`, snapshotted on
2026-05-31.

Original paths within the upstream repository:

- `tests/manifest.ttl` → `Material/RdfCanon/manifest.ttl`
- `tests/rdfc10/` → `Material/RdfCanon/rdfc10/`

The `rdfc10/` directory holds, per test `NNN`: `testNNN-in.nq` (the input
dataset), `testNNN-rdfc10.nq` (the expected canonical N-Quads output), and —
for the map tests — `testNNN-rdfc10map.json` (the expected issued-identifier
map). `test001-in.nq` / `test001-rdfc10.nq` are empty upstream (the canonical
form of the empty dataset is the empty string).

Line endings were normalized from the upstream CRLF (a Windows-checkout
artifact) to LF so the expected `.nq` files match the canonical RDFC-1.0
line form (`\n`) the project's serializer emits.

The suite is consumed by `W3cRdfCanonTests` / `W3cRdfCanonRunner`
(`test/Lumoin.Veritas.Tests/Conformance/`): the `rdfc:RDFC10EvalTest` entries
are run and compared byte-for-byte (LF-normalized) against their expected
output; the `rdfc:RDFC10MapTest` (issued-identifier map) and
`rdfc:RDFC10NegativeEvalTest` (poison-graph rejection) entries are reported
Inconclusive (skipped) — the `RdfCanonicalizer.Canonicalize` API returns the
canonical string, not the issuer map, and applies no complexity rejection.

Distributed under the W3C Test Suite License and the W3C 3-clause BSD License
(see the upstream `tests/LICENCE.md`).
