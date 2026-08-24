# CITE GML 3.2 corpus — provenance

Verbatim copies of the XML instance artifacts from two OGC CITE conformance-suite repositories,
vendored under an exhaustive-clearance rule: every instance artifact is run against the shipped
GML reader and cleared — parses to the expected value space or refuses with the recorded kind —
with the outcomes asserted by the corpus test family beside this tree; upstream artifacts that are
not instance documents stay unvendored, with the reasons recorded below.

Sources, both licensed Apache-2.0 by the Open Geospatial Consortium:

| Here | Upstream | Pin |
|---|---|---|
| `originals/gml32/**` | https://github.com/opengeospatial/ets-gml32 `src/test/resources/**/*.xml` (the `xsd/` schema tree excluded — schema documents bind schema validators, not instance parsers) | commit `29079ceba4a22b05c68bbc051cd164520f4881d3` (2025-02-07) |
| `originals/gml32-data/*.xml` | https://github.com/opengeospatial/ets-gml32-data `src/test/resources/*.xml` | commit `e609a8fd9404490460fd5a5b8f5e36768b873c5b` (2016-05-27) |
| `fragments/*.xml` | derived here — see below | n/a |

An SF-profile-specific CITE suite does not exist (checked 2026-08-10: the `opengeospatial/ets-gmlsfv2`
repository name resolves to nothing; the SF profile's Annex A is an abstract test suite for schema
interpreters, with no executable instance corpus). `ets-gml32` and `ets-gml32-data` are therefore the
complete executable-corpus universe for this stage; the KML 2.2 suite joins at stage 4.

## Derived artifacts

Two derivation families exist so the corpus exercises the parser's value space and not merely its
CRS roster gate. Both are recorded transformations of the pinned originals — input, never
specification:

- **`fragments/*.xml` (ten files, vendored).** Each is a top-level `gml:` geometry element sliced
  verbatim out of a feature-level original (the source file and element index are encoded in the
  file name), with the `gml` namespace declaration materialized onto the fragment root when it
  lived on an ancestor, and every out-of-roster `srsName` value replaced by the canonical
  `urn:ogc:def:crs:EPSG::4326` (a roster srsName injected onto the fragment root when the original
  carried none). Extraction was structural (same-name open/close token counting), performed once at
  vendoring time.
- **Runtime adaptation twins (not files).** The pure string-substitution derivations — replace every
  out-of-roster `srsName` value with `urn:ogc:def:crs:EPSG::4326`, inject a root `srsName` where the
  root start tag lacks one, rename the three `aixm:Surface` roots to `gml:Surface` — are applied at
  test run time by the documented derivation rules in the corpus test family, so the transformation
  itself stays reviewable code and the vendored bytes stay the pinned originals.

## Rules

- **Frozen.** These files are never edited here. A new upstream pin is a deliberate re-sync event
  that updates this file and the manifest — never a floating download at test time.
- **Tamper-evident.** `MANIFEST.sha256` pins the SHA-256 of every file and is asserted by the corpus
  manifest tests (both directions: every listed file present with a matching hash, no unlisted files).
- **Roles.** The originals are clearance subjects: every artifact has exactly one recorded outcome
  (parse with pinned value facts, or refusal with pinned kind and anchor) asserted by the corpus
  test family. The fragments and runtime twins extend clearance past the CRS roster gate to the
  geometry value space. Non-XML and schema artifacts upstream (`Jabberwocky.txt`, `*.xsd`, `*.sch`,
  `*.rnc`, `*.properties`, the Java sources) are not instance documents and stay unvendored for
  exactly that reason.
