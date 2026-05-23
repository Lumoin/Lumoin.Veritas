# W3C RDF 1.2 conformance status

**Snapshot date:** 2026-05-23.
**Corpus:** vendored at `test/Lumoin.Veritas.Tests/Material/`,
sourced from <https://github.com/w3c/rdf-tests/> on the `main`
branch (RDF 1.2 corpus only). Verified 2026-05-23 against upstream
`main` HEAD `4c255ad` (2026-05-21): the snapshot is at or after that
commit, so no re-vendoring was required. See the `ATTRIBUTION.md`
files for provenance. The corpus is read directly from the source
tree by the harness; it is not copied to the build output.

## Headline counts

| Suite     | Total | Passed | Failed | Skipped |
|-----------|-------|--------|--------|---------|
| N-Triples |    70 |     70 |      0 |       0 |
| N-Quads   |    68 |     68 |      0 |       0 |
| Turtle    |   103 |    103 |      0 |       0 |
| TriG      |    60 |     60 |      0 |       0 |
| **Total** | **301** | **301** | **0** | **0** |

**Full RDF 1.2 conformance.** The arc that began at 135/301 reached
301/301. The arc's projected endpoint was 300/301 with one
upstream-missing fixture left Inconclusive; that fixture
(`literal_needing_uchar_escaping-02`) turned out present in the
vendored snapshot and passes, so all 301 cases pass with no skips.

## Arc summary

| Batch | Scope | Result |
|-------|-------|--------|
| Initial harness | Vendor corpus, build harness | 135/301 |
| Phase 1–4 | Canonical escaping, N-Quads triple terms + lang, reification/annotation emission, surrogate validation | 271/301 |
| FIX-1 A–D | Whitespace tolerance, VERSION long-string rejection, noncharacter escaping, scheme-less IRI rejection | 284/301 |
| FIX-2 | Blank nodes nested in triple terms canonicalised | 286/301 |
| FIX-3 | Nested annotation blocks emitted | 288/301 |
| FIX-4 | Reified-triple / triple-term structural validation | 300/301 |
| TriG graph block | Optional trailing period on the final triple | 301/301 |

## Known limitations (no failing test, documented for completeness)

- **RDFC-1.0 n-degree traversal does not recurse into triple terms.**
  Blank-node discovery and first-degree hashing do; the n-degree
  related-node extraction inspects only top-level positions. No
  current test reaches the n-degree path with a triple-term-nested
  blank node, and the RDF-star position semantics there are not yet
  pinned down. Documented at the call site in `RdfCanonicalizer`.
- **Turtle relative-IRI resolution is RFC 3986 §5** via
  `Lumoin.Veritas.Core.Iris.IriResolver` (shared with JSON-LD and
  CBOR-LD, backed by `System.Uri`); chained `@base` directives
  compose. **Enforcement of "remains relative" is still deferred.**
  N-Triples and N-Quads reject scheme-less IRIs, but Turtle passes an
  unresolved relative reference through unchanged rather than throwing.
  Rejecting it requires a document-context base on `TurtleReader`
  (which the reader does not yet accept) plus a rework of the
  conformance harness's manifest traversal, whose `manifest.ttl` files
  use relative IRIs against their own retrieval URL. Tracked as the
  next sub-batch.

## Reproducing this report

```
dotnet test --project test/Lumoin.Veritas.Tests/Lumoin.Veritas.Tests.csproj `
    --no-build --filter "FullyQualifiedName~W3c"
```

`Inconclusive` (skipped) is reserved for structural skips; there are
none.

## Notes for future work

- Error reporting is exception-based (`TurtleParseException` /
  `NQuadsParseException`, each carrying a `SourceSpan`). A
  diagnostics-collecting parse path with error recovery — for editors
  that want every error at once rather than the first — is a separate
  batch; the natural shape mirrors SHACL's `ValidationResult`, keeping
  the throwing entry point as a thin wrapper.
- The RDF 1.1 corpus is not vendored; only RDF 1.2 is measured here.
