# Benchmark corpora

Corpora vendored for the reasoning benchmark harnesses (the delegation-rate
KPI harness scans every corpus directory under this folder; see
`Owl/DelegationRateHarness.cs`). Each corpus lives in its own subdirectory
with its upstream license or public-domain dedication recorded beside the data.

## OWL2Bench (`OWL2Bench/`)

Source: the OWL2Bench benchmark repository at
<https://github.com/kracr/owl2bench>, snapshotted on 2026-07-04 at commit
`0730bdddfb2d868d21dff88f1b2539f5c427ba73`. OWL2Bench is described in
"OWL2Bench: A Benchmark for OWL 2 Reasoners" (ISWC 2020,
<https://doi.org/10.1007/978-3-030-62466-8_6>).

Vendored files (repository root → `Material/Benchmark/OWL2Bench/`):

- `UNIV-BENCH-OWL2EL.owl` — the OWL 2 EL profile TBox
  (131 classes, 81 object properties, 12 data properties, 81 individuals).
- `UNIV-BENCH-OWL2QL.owl` — the OWL 2 QL profile TBox
  (135 classes, 89 object properties, 12 data properties, 68 individuals).
- `UNIV-BENCH-OWL2RL.owl` — the OWL 2 RL profile TBox
  (136 classes, 89 object properties, 12 data properties, 68 individuals).
- `UNIV-BENCH-OWL2DL.owl` — the OWL 2 DL TBox
  (132 classes, 85 object properties, 12 data properties, 81 individuals).
- `LICENSE` — the upstream Apache License 2.0 text, retained verbatim.

Format: RDF/XML, one self-contained university-domain ontology per OWL 2
profile with no `owl:imports`. Census counts above are declaration-element
counts (`<owl:Class rdf:about=…>` and peers) in the vendored snapshot.

License: Apache License 2.0 (the repository's `LICENSE`, copy retained in the
corpus directory). Copyright the OWL2Bench authors (KRaCR, IIIT-Delhi).

Deliberately NOT vendored: the ABox generator (a ~64 MB assembled jar plus
its Maven source tree) and the `Experiments/` result sets (~196 MB). The
harness consumes the fixed per-profile TBoxes only; when a scaled ABox is
needed, fetch the generator from the pinned commit above and generate at test
time rather than committing generated data.

These files are vendored to make the benchmark harness reproducible without
external setup. Updating the corpus is a deliberate re-copy: replace the
files, update the commit pin and census above, and re-run the harness.

## ORE 2014 (`ORE2014/`)

One inverse-heavy ontology (`galen-heart-alchoi-d.ofn`) from the ORE 2014
Reasoner Competition dataset (Zenodo record 10791, CC0 aggregate), the
committed inverse-role soak (inverse roles, nominals, datatype restrictions;
no role chains). Source, MD5 pin, per-ontology caveat, census, and update
discipline are recorded in `ORE2014/ATTRIBUTION.md`.
The fuller GALEN slice and a size-diverse calibration subset are fetched and
cached machine-locally (never committed), reachable through the harness's
machine-local corpus-root environment variable.
