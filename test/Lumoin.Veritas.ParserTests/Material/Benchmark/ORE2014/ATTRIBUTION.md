# ORE 2014 — vendored slice (attribution)

One inverse-heavy ontology from the ORE 2014 Reasoner Competition dataset, vendored for the
reasoning benchmark harnesses (the delegation-rate KPI harness scans every corpus directory under
`Material/Benchmark/`; see `Owl/DelegationRateHarness.cs`). It is the committed inverse-role soak —
the `2^|E|` carrier the ELI arc flags as otherwise unmeasured — the piece no synthetic ladder or
profile TBox exercises. Its expressivity is inverse roles, nominals, and datatype restrictions
(the ALCHOI(D) fragment); it carries no role chains or transitivity.

## Source

- Dataset: ORE 2014 Reasoner Competition dataset, Zenodo record 10791
  (`https://zenodo.org/records/10791`), file `ore2014_dataset.zip`.
- Snapshotted 2026-07-08. The zip's MD5 is `7f5f53026a79ad9a1c467a3a9e7df771` (verified on download
  before extraction).
- The dataset is serialised in OWL functional syntax throughout; the vendored member is renamed to
  the `.ofn` extension the harness loader keys on. Upstream member → vendored file:
  - `dataset/files/GALEN-Heart_ALCHOI(D).owl_functional.owl` → `galen-heart-alchoi-d.ofn`

## License

- The ORE 2014 dataset is dedicated to the public domain under **Creative Commons Zero v1.0
  Universal (CC0)** at the aggregate level. CC0 waives copyright; no license file is required
  beside the data, so none is retained here — this note records the dedication in its place.
- **Per-ontology caveat:** CC0 is the curators' aggregate dedication. The vendored member is derived
  from the OpenGALEN model (a GALEN-family ontology, the ALCHOI(D) Heart subset). For this specific
  ontology the upstream authors' terms may still bind a redistributor of the *named* original; the
  CC0 ORE-2014 aggregate is the cleaner provenance under which it is vendored here for benchmarking,
  and it is used only as a fixed reasoning-benchmark input, never re-released under the OpenGALEN
  name.

## Census (`galen-heart-alchoi-d.ofn`)

- Size 2,498,197 bytes; SHA-256 `7b11cc2b9279250eef90679f626e4f570b08c6c29eb2a8b1ad176416f8aaeb22`.
- Declarations: 3,432 classes, 236 object properties, 2 data properties, 0 named individuals
  (TBox-only — no ABox).
- Axiom mix (polarity-qualified full-construct census, top constructs): 9,587 `ObjectSomeValuesFrom`,
  9,400 `SubClassOf`, 961 `EquivalentClasses`, 184 `ObjectOneOf(n=1)`, 173 `SubObjectPropertyOf`,
  **94 `InverseObjectProperties`**, 56 `DataFacet`, 48 `DataSomeValuesFrom`, 48 `DatatypeRestriction`,
  38 `DataHasValue`.
- The inverse-role and nominal/datatype content places the ontology outside the context engine's
  Horn-ALCHI admission slice and is expected to exceed the tableau engines' practical budget — the
  intended soak profile. The measured decision cost is recorded in the banked stand report, not
  asserted here.

## Update discipline

Updating the vendored slice is a deliberate re-copy: re-download `ore2014_dataset.zip` from Zenodo
record 10791, re-verify its MD5 `7f5f53026a79ad9a1c467a3a9e7df771`, re-extract the named member,
rename to `.ofn`, refresh the size/SHA-256/census above, and re-run the load-integrity census pin
(`DelegationRateHarness.VendoredBenchmarkCorporaLoadAndMapCleanly`). The fuller GALEN slice and a
size-diverse calibration subset are fetched-and-cached machine-locally (never committed), reachable
through the harness's machine-local corpus-root environment variable.
