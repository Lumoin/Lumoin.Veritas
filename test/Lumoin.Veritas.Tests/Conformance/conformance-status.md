# W3C RDF 1.2 conformance status

**Snapshot date:** 2026-05-23.
**Corpus:** vendored at `test/Lumoin.Veritas.Tests/Material/`,
sourced from <https://github.com/w3c/rdf-tests/> on the `main`
branch (RDF 1.2 corpus only). Verified 2026-05-23 against upstream
`main` HEAD `4c255ad` (2026-05-21): the snapshot is at or after that
commit, so no re-vendoring was required. See the `ATTRIBUTION.md`
files for provenance. The corpus is read directly from the source
tree by the harness; it is not copied to the build output.

This is a measurement. Failures listed below are deferred items
queued for the remaining FIX batches, surfaced by running the W3C
test cases through the harness in `Conformance/`.

## Headline counts

| Suite     | Total | Passed | Failed | Skipped |
|-----------|-------|--------|--------|---------|
| N-Triples |    70 |     70 |      0 |       0 |
| N-Quads   |    68 |     68 |      0 |       0 |
| Turtle    |   103 |     94 |      9 |       0 |
| TriG      |    60 |     52 |      8 |       0 |
| **Total** | **301** | **284** | **17** | **0** |

The previous measurement was **271/301**. The FIX-1 batch (four small
grammar/lexer fixes) lifted this to **284/301 (~94 %)**. N-Triples and
N-Quads are now fully conformant. No structural skips remain.

## FIX-1 deltas

| Sub-fix | Effect |
|---------|--------|
| A — whitespace before language tag / datatype marker | N-Triples 65 → 67, N-Quads 64 → 66 |
| B — reject long-quoted VERSION argument | Turtle 90 → 94 |
| C — escape Unicode noncharacters in canonical output | N-Triples 67 → 69, N-Quads 66 → 68 |
| D — reject scheme-less IRIs in N-Triples/N-Quads | N-Triples 69 → 70 |

Phase-0 verification corrected the handoff's framing of sub-fix C: the
sole noncharacter test is a *positive* C14N test expecting
noncharacters accepted and escaped, not rejected (the corpus has no
noncharacter rejection tests). The fix is therefore escaping in the
canonical serializer, not rejection in the lexer.

## Remaining failures

### Blank nodes nested in triple terms not canonicalized (FIX-2, ~3)

`Turtle 1.2 - blank node labels`,
`Turtle 1.2 - Annotation on triple with reified triple subject`, and
the related TriG case. `RdfCanonicalizer` discovers blank nodes only
in the top-level subject, object, and graph positions; a blank node
inside a `TripleTerm` is neither collected nor assigned a canonical
label, so isomorphic datasets sharing such a blank node compare
unequal. Recursing the blank-node collection and the n-degree hashing
into triple terms is FIX-2.

### Nested annotation blocks (FIX-3, ~3)

`Turtle 1.2 - Annotation - nested`, `TriG 1.2 - Annotation - nested`,
and `TriG 1.2 - Annotation form (PositiveSyntax)`. An annotation block
nested inside another block annotates the outer block's annotation
triple, not the original statement; the emitter does not recurse into
annotations attached to the objects within a block. FIX-3.

### Reified-triple structural validation (FIX-4, ~9)

The `bad - …` negative-syntax tests the parser does not yet reject —
`literal in subject position of reified triple`,
`reified with list object`, `compound blank node expression`,
`empty annotation`, and the `N-Triples 1.2 as Turtle 1.2 - Bad - …`
triple-term/reified-triple subject cases. The parser accepts these;
enforcing the grammar's positional restrictions is FIX-4, which needs
its own spec-investigation phase first.

### TriG direction-tagged positive cases (2)

Two `base direction ltr` positive tests in TriG. These manifest
alongside the annotation/reification handling above and will be
re-evaluated once FIX-2 and FIX-3 land.

## Reproducing this report

```
dotnet test --project test/Lumoin.Veritas.Tests/Lumoin.Veritas.Tests.csproj `
    --no-build --filter "FullyQualifiedName~W3c"
```

`Inconclusive` (skipped) is reserved for structural skips; there are
currently none. Real defects are reported as failures.

## Notes for the remaining arc

- The earlier missing-fixture concern (`literal_needing_uchar_escaping-02`)
  resolved: the fixture is present and the test passes with FIX-1C.
- Error reporting is exception-based (`TurtleParseException` /
  `NQuadsParseException`, each carrying a `SourceSpan`). A
  diagnostics-collecting parse path with error recovery — for editors
  that want every error at once rather than the first — is a separate
  batch; the natural shape mirrors SHACL's `ValidationResult`, keeping
  the throwing entry point as a thin wrapper.
