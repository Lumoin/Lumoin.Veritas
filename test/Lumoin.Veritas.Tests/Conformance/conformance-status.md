# W3C RDF 1.2 conformance status

**Snapshot date:** 2026-05-23.
**Corpus:** vendored at `test/Lumoin.Veritas.Tests/Material/`,
sourced from <https://github.com/w3c/rdf-tests/> on the `main`
branch (RDF 1.2 corpus only). See the `ATTRIBUTION.md` files
alongside the corpus for provenance and scope notes. The corpus is
read directly from the source tree by the harness; it is not copied
to the build output.

This is a measurement. Failures listed below are real defects or
deliberately deferred items, surfaced by running the W3C test cases
through the harness in `Conformance/`.

## Headline counts

| Suite     | Total | Passed | Failed | Skipped |
|-----------|-------|--------|--------|---------|
| N-Triples |    70 |     65 |      5 |       0 |
| N-Quads   |    68 |     64 |      4 |       0 |
| Turtle    |   103 |     90 |     13 |       0 |
| TriG      |    60 |     52 |      8 |       0 |
| **Total** | **301** | **271** | **30** | **0** |

The previous measurement was **135/301**. The four-phase fix batch
plus the supporting set-semantics and corpus-loading changes lifted
this to **271/301 (~90 %)**. No structural skips remain.

## Per-phase deltas

| Change | N-Triples | N-Quads | Turtle | TriG |
|--------|-----------|---------|--------|------|
| Batch start | 24 | 26 | 57 | 28 |
| Phase 1 — canonical escaping + order-preserving C14N dispatch | 50 | 52 | 57 | 28 |
| Phase 3 — N-Quads triple terms + language tightening | 65 | 64 | 70 | 37 |
| Phase 2 — reification/annotation emission + set-semantics comparison | 65 | 64 | 83 | 52 |
| Phase 4 — lone-surrogate validation | 65 | 64 | 90 | 52 |

Phases 1 and 3 also lifted Turtle and TriG because the
annotation/reification evaluation tests can only be measured once the
canonical serializer emits the triple-term form and the N-Quads reader
parses it in the expected fixtures. The phases were therefore
implemented in the order 1 then 3 then 2 then 4.

## Remaining failures

### Canonicaliser does not canonicalize blank nodes nested in triple terms (2)

`Turtle 1.2 - blank node labels` and
`Turtle 1.2 - Annotation on triple with reified triple subject`.
`RdfCanonicalizer` discovers blank nodes only in the top-level
subject, object, and graph positions; a blank node appearing inside a
`TripleTerm` is not collected, so it is not assigned a canonical
label and isomorphic datasets that share such a blank node compare
unequal. Fixing this means recursing the blank-node collection and
the n-degree hashing into triple terms — a delicate change to the
RDFC-1.0 implementation, deferred to its own batch.

### Nested annotation blocks (2)

`Turtle 1.2 - Annotation - nested` and the TriG equivalent. An
annotation block nested inside another block (`{| :a :b {| :a2 :b2 |} |}`)
annotates the *outer block's* annotation triple
(`reifier :a :b`), not the original statement. The emitter expands a
block's predicate-object list but does not recurse into annotations
attached to the objects within a block. Deferred.

### VERSION directive accepts triple-quoted strings (4)

`VERSION` / `@version` with a triple-quoted string is accepted where
the grammar restricts the version specifier to a short-quoted string.
Out of scope for this batch per the handoff (VERSION-string handling
is a separate item).

### Reified-triple structural validation (6)

Negative-syntax tests the parser does not yet reject:
`bad - literal in subject position of reified triple`,
`bad - reified with list object`,
`bad - compound blank node expression`, and the
`N-Triples 1.2 as Turtle 1.2 - Bad - ...` triple-term/reified-triple
subject cases. The parser accepts these constructs; enforcing the
grammar's positional restrictions is the reified-triple-validation
item, out of scope for this batch.

### Whitespace tolerance in canonical N-Triples/N-Quads inputs (4)

`C14N extra_whitespace-03` / `-04` in both N-Triples and N-Quads. The
inputs place whitespace before the language tag, around `^^`, or
before the terminating `.`; the reader does not tolerate it. The
canonical serializer is correct — this is a reader-tolerance gap.

### Relative IRI without scheme (1)

`N-Triples - Bad - invalid IRI` (`<//example/missing-scheme>`). The
reader does not validate that an IRI is absolute. IRI scheme
validation is a broader concern, deferred.

### Noncharacter escaping in the canonical serializer (1)

`C14N literal_needing_uchar_escaping-01`. The expected canonical form
escapes the Unicode noncharacters U+FFFE and U+FFFF as the UCHAR forms
`\uFFFE` / `\uFFFF`; the serializer escapes only the C0 controls and
DEL. The exact noncharacter set the canonical form requires is left
for a follow-up rather than guessed at.

### Missing expected fixture (1)

`C14N literal_needing_uchar_escaping-02` has no `-c14n` result file in
the vendored corpus, so the runner reports a missing-fixture failure.
This is a corpus gap, not a defect; it is surfaced honestly rather
than skipped.

### TriG positive base-direction / annotation-form (3)

`TriG 1.2 - Annotation form (PositiveSyntax)`,
`TriG 1.2 - bad - empty annotation`, and two `base direction ltr`
positive tests. These overlap with the nested-annotation and
reified-validation items above as they manifest in TriG mode.

## Reproducing this report

```
dotnet test --project test/Lumoin.Veritas.Tests/Lumoin.Veritas.Tests.csproj `
    --no-build --filter "FullyQualifiedName~W3c"
```

`Inconclusive` (skipped) is reserved for structural skips; there are
currently none. Real defects are reported as failures.

## What this batch did

- **Phase 1**: canonical N-Triples/N-Quads literal escaping (ECHAR +
  uppercase UCHAR + pass-through), language-tag lower-casing,
  xsd:string suppression, triple-term rendering, and an
  order-preserving `SerializeStatements` entry point used by the C14N
  dispatch.
- **Phase 3**: N-Quads triple-term parsing (iterative, stack-based)
  and emission, BCP 47 language-tag validation with `--ltr` / `--rtl`
  direction, rejection of `rdf:langString` / `rdf:dirLangString` as
  explicit datatypes, and the missing `\b` / `\f` / `\U` unescapes.
- **Phase 2**: reified triples no longer assert their inner triple;
  each annotation block takes its own reifier; the evaluation
  comparison is set-based.
- **Phase 4**: the Turtle lexer rejects lone surrogate and
  out-of-range `\u` / `\U` escapes and encodes astral code points
  without truncation.
- Corpus loading moved to a manifest-aware `ITestDataSource` reading
  from the source tree, dropping the build-output copy.
