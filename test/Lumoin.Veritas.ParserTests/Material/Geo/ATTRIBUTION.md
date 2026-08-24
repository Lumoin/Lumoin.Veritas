# OGC GeoSPARQL 1.1 RDF resources and examples

Three upstream sources are vendored under this folder. Sources 1 and 2
are each pinned to a commit and copied blob-exact (every vendored
file's git blob hash verified identical to the pinned commit's blob).
Source 3 is a digest-pinned served artifact: no ratified git home
exists for it, so the pin is its URL, retrieval date, and recorded
digests. All vendored files are LF.

## Source 1: `semantic-resources/`

Source: https://github.com/opengeospatial/geosemantics-semantic-resources/tree/main/resources/geosparql-swg
Branch: main
Pinned commit: 57540ccc528e4e8a5d61154e6dcafa94af7e533f (verified 2026-07-30)

Licensed under the Apache License, Version 2.0 (explicit root
`LICENSE` file upstream; `geo.ttl` additionally self-declares its
license and copyright as RDF triples).

Copyright © Open Geospatial Consortium.

This repository is the authoritative current home of the ratified
GeoSPARQL 1.1 RDF: the `ogc-geosparql` README states the RDF files
are maintained in the Geosemantics DWG's repository, from where they
are loaded into the OGC Definitions Service.

## Source 2: `examples/`

Source: https://github.com/opengeospatial/ogc-geosparql/tree/geosparql-1.1/examples
Branch: geosparql-1.1
Pinned commit: 6637ee2e14fb0f9b33eac905c392ae95e73a8f25 (verified 2026-07-30)

Licensed under the Apache License, Version 2.0 for software and data
per the OGC software licensing policy; the `ogc-geosparql` repository
carries no LICENSE file, and its README's License section states the
Apache-2.0 grant for software and data (the standard's prose falls
under the OGC Document License and is not vendored here).

Copyright © Open Geospatial Consortium.

## Source 3: `schemas/`

Source: https://schemas.opengis.net/gml/3.2.1/gml_32_geometries.rdf
Retrieved: 2026-08-12
Size: 13659 bytes; SHA256
`E203BAE3295172E3E85E9CEBED777A792F046811E1F9A888C1498FB3BECEB917`;
git blob SHA1 `51bf90ec000e05150a0421b59fbd2f565d781e14`.

One file: the GML 3.2.1 geometry class-hierarchy vocabulary
(`http://www.opengis.net/ont/gml#`) as published in the OGC schema
repository, vendored for the RDFS Entailment Extension conformance
rows. No ratified git home exists for this artifact (verified
2026-08-12: the Source 1 repository carries no GML ontology on its
main branch in either the geosparql-1.0 or geosparql-1.1 folders, and
the Source 2 repository's `geosparql-1.1` branch carries none either),
so the pin is the URL, the retrieval date, and the digests above —
digest-pinned, not commit-pinned.

The file's own header comment reads, verbatim: "GeoSPARQL 1.0 is an
OGC Standard. Copyright (c) 2012 Open Geospatial Consortium. To obtain
additional rights of use, visit http://www.opengeospatial.org/legal/ .
Version: 1.0.1". The notice names GeoSPARQL 1.0, not GML: the file is
the GML class hierarchy as the GeoSPARQL standard published it. The
Apache-2.0 grant for software and data per the OGC software licensing
policy applies as for Source 2.

Copyright © Open Geospatial Consortium.

These files are vendored to make the conformance test suite
reproducible without external setup. Updating the corpus is a
deliberate re-copy: replace the contents of `semantic-resources/`
and/or `examples/`, update the pinned commit SHA(s) above, and re-run
the conformance suite; for `schemas/`, re-fetch the URL, re-record the
retrieval date and digests, and re-run the suite.

Exclusions, stated: from `resources/geosparql-swg/` the
`alignments-source/` build inputs, `profiles/`, `servicedescription/`,
`catalogue.ttl`, and `manifest.ttl` are not vendored (outside the
ruled pin list; a later deliberate re-copy may add them). From
`examples/` the `test_shapes.py` pytest harness (the house arm is
MSTest-based), both upstream `README.md` files, and everything else on
the branch (spec prose, site tooling) are not vendored.

No upstream conformance-test manifest exists for GeoSPARQL 1.1; the
Geo arm's manifests are authored in-house from the requirement census
and live under `manifests/` beside the vendored subtrees, never inside
them. `manifests/` is house content, not vendored, and is excluded
from the vendored file counts below.

## Corpus shape

`semantic-resources/` is the ratified 1.1 RDF: the `geo:` ontology
(`ontologies/geo.ttl`), the Simple Features geometry class hierarchy
(`ontologies/sf_geometries.ttl`), external-vocabulary alignments
(`ontologies/alignments.ttl`), the SKOS vocabularies for the spec's
functions, requirements, and rules (`vocabs/`), and the informative
SHACL validator (`validators/geo-validator.ttl` — upstream declares it
informative in 1.1, not normative).

`examples/shacl/` holds valid/invalid Turtle fixture pairs targeting
the validator's shape groups: S01–S04 and S09–S24 are present
(S05–S08 have no fixtures upstream; S20 has only a valid fixture).
Each file is a small self-contained data graph named
`Snn-valid.ttl` / `Snn-invalid[-mm].ttl`.

`examples/` root holds one worked demo dataset (`demo-dataset.ttl`)
and one feature (Moreton Island) in five parallel serializations —
DGGS AusPIX, GeoJSON, GML, KML, and WKT — an interop smoke-test set.

## Vendored layout

```
Geo/
  semantic-resources/
    ontologies/{geo,sf_geometries,alignments}.ttl
    vocabs/{functions,requirements,rules}.ttl
    validators/geo-validator.ttl
  examples/
    demo-dataset.ttl
    moreton-island.{auspix,geojson,gml,kml,wkt}
    shacl/S*.ttl
  schemas/
    gml/3.2.1/gml_32_geometries.rdf
```

62 files: 56 `.ttl` (7 semantic-resources, 48 SHACL fixtures across 20
shape groups, 1 demo dataset), the 5 Moreton Island serializations,
plus the 1 RDF/XML GML geometry class-hierarchy document (`schemas/`).
