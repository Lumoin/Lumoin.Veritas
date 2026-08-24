# A5 fixture corpus — provenance

Verbatim copies from the a5-js reference implementation (https://github.com/felixpalmer/a5), pinned at commit
`6ee88da29d44ef97e9fa7d0afb61e3edc3f58910` (the v0.9.0 release, 2026-06-17). License: Apache-2.0 (see
`THIRD-PARTY-NOTICES.md` at the repository root).

Directory layout mirrors the upstream paths relative to the upstream `tests/` directory:

| Here | Upstream |
|---|---|
| `fixtures/**` | `tests/fixtures/**` (24 files) |
| `geometry/fixtures/*.json` | `tests/geometry/fixtures/*.json` (4 files) |
| `projections/fixtures/*.json` | `tests/projections/fixtures/*.json` (4 files) |
| `integration/wireframe*.json` | `tests/integration/wireframe*.json` (8 files) |
| `data/ne_50m_populated_places_nameonly.json` | `tests/data/` (1 file, input-only property driver) |

Deliberately NOT copied: `tests/projections/data/crs-vertices.json` (a distinct, test-unreferenced file — not the
same as `tests/fixtures/crs-vertices.json`); `tests/projections/data/equal-area-test-data.ts` (inline TypeScript
data — transcribed into the corresponding test class instead).

Rules:

- **Frozen.** These files are never regenerated here. Upstream generates them from the TypeScript implementation
  (some generators use unseeded `Math.random`, so regeneration is not reproducible); ports copy, never generate.
  A new upstream pin is a deliberate re-sync event that updates this file and the manifest.
- **Tamper-evident.** `MANIFEST.sha256` pins the SHA-256 of every file and is asserted by `A5FixtureManifestTests`
  (both directions: every listed file present with a matching hash, no unlisted files).
- **Assertion regimes** (integer outputs bit-for-bit, floats at upstream test tolerances) are fixed per
  fixture by the test class that reads it.
