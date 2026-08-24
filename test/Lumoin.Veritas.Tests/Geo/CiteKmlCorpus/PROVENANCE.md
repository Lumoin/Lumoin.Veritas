# CITE KML 2.2 corpus — provenance

Verbatim copies of the instance artifacts from two OGC CITE conformance-suite repositories,
vendored under an exhaustive-clearance rule: every instance artifact is run against the shipped
KML reader and cleared — parses to the expected value space or refuses with the recorded kind —
with the outcomes asserted by the corpus test family beside this tree; upstream artifacts that are
not instance documents stay unvendored, with the reasons recorded below.

Sources, both licensed Apache-2.0 by the Open Geospatial Consortium:

| Here | Upstream | Pin |
|---|---|---|
| `originals/kml22/**` | https://github.com/opengeospatial/ets-kml22 `src/test/resources/**/*.{xml,kml,kmz}` | commit `cd07d5db32b1f6d9cf01e2263d4b9302336ef641` (2025-01-23) |
| `originals/kml2/**` | https://github.com/opengeospatial/ets-kml2 `src/test/resources/**/*.{xml,kml,kmz}` | commit `a210dfaf2dbf85b5c0ff0f3c0e060c53bb19787c` (2022-02-01) |
| `fragments/**` | derived here — see below | n/a |

`ets-kml22` is the KML 2.2 (07-147r2) suite and the clearance target; `ets-kml2` is the KML 2.x
suite (2.3-primary under OGC 12-007r2, version-dispatched on the root `version` attribute, 2.2 by
default), enumerated whole and dispositioned — its 2.3-construct artifacts run and clear with
version annotations in the ledger, never exempted. The vendored universe per suite is every
`.xml`, `.kml`, and `.kmz` file under `src/test/resources`: fifty-three loose XML documents plus
two KMZ archives for `ets-kml22`; seventy-six loose XML documents plus one KMZ archive for
`ets-kml2`. The `small_world.kmz` archives in the two trees are byte-identical (verified by
SHA-256 at vendoring); each suite tree vendors its own copy so every manifest path mirrors its
upstream location. The `ets-kml22` site-documentation archive
`src/site/resources/Supporting_Docs_KML_2.2_GDAL.zip` is NOT vendored — it is a third party's
(GDAL's) certification-submission evidence archived under the suite's site documentation, never a
fixture the harness consumes, and its nested artifacts are that submission's own inputs and
outputs, outside the suite's instance corpus; the exclusion reason is exactly the one stated
here.

## Derived artifacts

All derivation is performed once at vendoring time; every derived file is pinned in the manifest
beside the originals. Two families exist:

- **KMZ entry extraction (five files).** Every `.kml` entry of every vendored KMZ archive is
  extracted byte-exact into `fragments/` (the archive path and entry path are encoded in the file
  name). The archives themselves also stay vendored: the raw container bytes are a clearance
  subject in their own right — the reader refuses them as not-XML — and the extracted entries
  clear as documents.
- **Geometry-element slicing (seventy-two files, all `-adapted`).** Each is a KML geometry element
  sliced verbatim out of a KML-vocabulary original (or extracted KMZ entry) whose own root is not
  a geometry element — feature envelopes, overlays, and view fixtures — with the source file and
  element index encoded in the file name. Slicing takes every element named `Point`, `LineString`,
  `LinearRing`, `Polygon`, `MultiGeometry`, or `Model` in the KML 2.2 namespace that is not nested
  inside another such element, in document order, mechanically — no selection judgment. The KML
  namespace binding the fragment root inherited from its envelope (the default declaration or the
  `kml` prefix binding) is materialized onto the fragment root; every one of the seventy-two
  slices required exactly this one adaptation and no other byte changed. Extraction was structural
  (depth-tracked tag walking over the raw bytes, comment- and CDATA-aware), performed once at
  vendoring time.

**There are no runtime adaptation twins.** The GML corpus's twin lane exists to substitute
out-of-roster `srsName` values at test run time; KML carries no coordinate-reference attribute,
and every vendored KML-vocabulary artifact already carries its namespace binding inline (verified
over the pinned bytes at vendoring). No pure string substitution would extend clearance, so no
derivation-rule machinery exists for this corpus — the absence is design, not omission.

## Rules

- **Frozen.** These files are never edited here. A new upstream pin is a deliberate re-sync event
  that updates this file and the manifest — never a floating download at test time.
- **Tamper-evident.** `MANIFEST.sha256` pins the SHA-256 of every file and is asserted by the corpus
  manifest tests (both directions: every listed file present with a matching hash, no unlisted files).
- **Byte-exact.** The tree is vendored with line-ending conversion disabled (the repository root
  `.gitattributes` carries a `-text` stanza for exactly this subtree): the manifest pin doubles as
  the upstream identity, the pinned refusal anchors are offsets into these bytes, and the tree
  mixes text with binary KMZ archives.
- **Roles.** The originals are clearance subjects: every artifact has exactly one recorded outcome
  (parse with pinned value facts, or refusal with pinned kind and anchor) asserted by the corpus
  test family. The extracted entries and sliced fragments extend clearance past the
  document-envelope refusals into the geometry value space. Non-XML artifacts upstream (the
  COLLADA models, rasters, `Jabberwocky.txt`, the schemas, Schematron files, property bundles, and
  Java sources) are not instance documents and stay unvendored for exactly that reason.
