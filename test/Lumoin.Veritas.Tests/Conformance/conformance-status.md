# W3C RDF 1.2 conformance status

**Snapshot date:** 2026-05-22.
**Corpus:** vendored at `test/Lumoin.Veritas.Tests/Material/`,
sourced from <https://github.com/w3c/rdf-tests/> on the `main`
branch. Commit SHA was not captured at vendoring; see the
`ATTRIBUTION.md` files alongside the corpus for details.

This is a measurement, not a target. Failures listed below are
real defects in the project's parsers, emitters, or
canonicaliser, surfaced by running the W3C test cases through
the harness in `Conformance/`. Each named failure is a
candidate for a follow-up batch.

## Headline counts

| Suite     | Total | Passed | Failed | Skipped |
|-----------|-------|--------|--------|---------|
| N-Triples |    70 |     24 |     46 |       0 |
| N-Quads   |    68 |     26 |     42 |       0 |
| Turtle    |   103 |     57 |     46 |       0 |
| TriG      |    60 |     28 |     32 |       0 |
| **Total** | **301** | **135** | **166** | **0** |

There are no structural skips in the current state.
Canonicalisation tests (`TestNTriplesPositiveC14N`,
`TestNQuadsPositiveC14N`) dispatch through `RdfCanonicalizer`
and contribute real pass/fail measurements; the
`PositiveC14N` test type was added in a mid-batch extension
after the initial run.

**Unresolved includes.** The RDF 1.2 top-level manifests
include `../../rdf11/...` for the RDF 1.1 corpus. That sub-tree
was not vendored; the loader records the absent paths on
`W3cManifest.UnresolvedIncludes` and proceeds. Adding RDF 1.1 is
a future copy-step that would substantially grow the corpus
(~21 MB).

## Failure patterns

The 166 failures cluster into a small number of root
causes. The numbers in parentheses are the count of failing
tests attributable to each cause.

### Canonicaliser literal serialisation diverges from RDFC-1.0 expected form (~70)

The dominant failure category across both N-Triples and
N-Quads suites. The canonicaliser emits literals using a
different escape representation than the spec's canonical
form. Inspection of one failing case:

```
Input:    "x"y"
Expected: "x\"y"
```

The expected canonical form uses the shortest valid escape
(`\"` instead of `"`). The current serialiser preserves
the source escape form. Similar shape repeats for `\`
→ `\\`, escape sequences around control characters
(`literal_with_BACKSPACE`, `literal_with_CARRIAGE_RETURN`,
`literal_with_LINE_FEED`, `literal_with_CHARACTER_TABULATION`,
`literal_with_FORM_FEED`, `literal_all_controls`),
and Unicode boundaries
(`literal_with_UTF8_boundaries`,
`literal_needing_uchar_escaping-01/02`,
`nt-syntax-str-esc-01..03`).

Additional canonicaliser issues:

- **Triple-term canonical form** is wrong or absent
  (`triple-term-01..04` in both NT and NQ suites). The
  canonicaliser likely emits these as something other than
  the RDFC-1.0 expected encoding.
- **Whitespace canonicalisation** is incomplete
  (`extra_whitespace-02..04`,
  `literal_with_extra_whitespace`,
  `minimal_whitespace-02`). The canonicaliser doesn't
  fully normalise whitespace around tokens to the spec's
  expected form.
- **Language and direction-tagged literals** disagree
  (`langtagged_string`, `literal with base direction ltr`).

These are defects in
`Lumoin.Veritas.Canonicalization.NQuadsSerializer`, not in
the canonicalisation algorithm proper; the blank-node hashing
and labelling logic in `RdfCanonicalizer` works for the
evaluation tests that pass on the Turtle side.

### `NQuadsReader` does not parse RDF 1.2 constructs (10)

`NQuadsReader` was written against RDF 1.1 and does not
recognise:

- Triple terms `<<( s p o )>>` in object position. Affects
  five N-Triples and five N-Quads `PositiveSyntax` tests:
  `N-Triples-12 - object triple term`,
  `N-Triples-12 - object triple term, no whitespace`,
  `N-Triples-12 - Nested, no whitespace`,
  `N-Triples-12 - Nested object term`, and the matching
  N-Quads cases.

### `NQuadsReader` accepts inputs the spec rejects (8)

The reader is permissive in several places that the W3C
negative-syntax tests catch:

- Invalid IRIs slip through (`N-Triples - Bad - invalid IRI`).
- Language tags without subtag content are accepted
  (`literal with missing language tag`,
  `literal with bad language tag`).
- Direction tags that are not exactly `ltr` / `rtl` are
  accepted (`undefined base direction`, `upper case LTR`,
  `missing language tag and direction`).

### Turtle / TriG reified-triple and triple-term emission does not match the spec (~25 in Turtle, ~22 in TriG)

The W3C evaluation tests for reified triples
(`Turtle 1.2 - subject reification` and family) compare the
parser's emitted quad set to a canonical N-Triples expected
file. The current emitter's output for `<< s p o >>` (reified
triple) and `<<( s p o )>>` (triple term) differs from the
expected encoding under RDFC-1.0 canonicalisation, so the
isomorphism check fails. Same pattern for annotation forms.

The Turtle batch's emitter made a defensible choice locally
(emit the inner triple AND a
`reifier rdf:reifies <<(s p o)>>` link); the spec's expected
output appears to use a different shape that needs to be
reconciled.

Affected names include:
`subject reification`, `object reification`,
`subject/object reification with identifier`,
`subject/object reification with bnode identifier`,
`subject/object reification with empty identifier`,
`object triple term`, `object triple term, no whitespace`,
`Nested, no whitespace`, `Nested object term`,
`Annotation form`, `Annotation example`,
`Annotation - predicate and object lists`,
`Annotation - nested`, `Annotation object list`,
`Annotation with identifier`,
`Different annotations have different default identifiers`,
`Annotation form with explicit identifier`,
`Annotation form with multiple reifiers`,
`Annotation form with multiple annotation blocks`,
`Annotation form with annotation block followed by reifier`,
`Annotation form with alternating reifiers and annotation blocks`,
`Annotation with reified triples`,
`Annotation on triple with reified triple subject`,
`Annotation on triple with reified triple object`.

The blank-node Evaluation tests (`Turtle 1.2 - blank node
label`, `Turtle 1.2 - blank node labels`) fall in the same
bucket: the quad sets differ in blank-node *naming* in a way
RDFC-1.0 does not equate, which points at a divergent
canonical encoding of the parsed bnodes.

### Turtle / TriG lexer does not reject surrogate-pair edge cases (7)

`\uXXXX` escapes in string literals are decoded individually
without validating Unicode surrogate-pair semantics:

- `Surrogate pair numeric escape sequence`,
  `Surrogate pair via numeric escape sequence`,
  `Surrogates - wrong order`,
  `Surrogates - high-high`, `Surrogates - low-low`,
  `Surrogates - lone high surrogate`,
  `Surrogates - lone low surrogate`.

The lexer should reject lone surrogates and mis-ordered
high/low pairs. The fix is local to
`TurtleLexer.ReadUcharEscape`.

### VERSION directive accepts triple-quoted string forms (4)

The grammar restricts `VersionSpecifier` to short-quoted
strings; the parser currently accepts any string literal
including the long triple-quoted forms:

- `Turtle 1.2 - VERSION - triple-'-quoted string`,
  `VERSION - triple-"-quoted string`,
  `@version - triple-'-quoted string`,
  `@version - triple-"-quoted string`.

### Reified-triple structural rules (6)

A handful of `NegativeSyntax` tests check spec rules the
current parser does not enforce:

- `bad - literal in subject position of reified triple`,
- `bad - reified with list object`,
- `bad - compound blank node expression`,
- The N-Triples-1.2-as-Turtle tests.

### TriG annotation-block edge cases (2)

- `TriG 1.2 - Annotation form (PositiveSyntax)` — a
  positive test fails, suggesting the annotation-block
  expansion in the emitter diverges from the spec's
  expected shape in TriG mode.
- `TriG 1.2 - bad - empty annotation` — the parser accepts
  `{| |}` where the spec rejects it.

### Direction-tagged literal positive tests (2)

Two `base direction ltr` positive tests fail in TriG.
Cross-reference the Turtle suite's direction-tagged tests
to isolate whether this is TriG-specific.

## Mapping back to the Turtle-batch risk register

The Turtle batch flagged four risks; the W3C suite confirms
three of them and adds several.

1. **`PN_CHARS_BASE` permissiveness** — not directly surfaced
   by the failures above. Inputs in the corpus do not exercise
   the permissive range.
2. **IRI relative resolution as prepend-base** — not surfaced
   either. The corpus does not exercise relative-IRI
   resolution requiring strict RFC 3986.
3. **Writer does not reconstruct collection / blank-node
   sugar** — out of scope here (the W3C tests parse only).
4. **Deeply-nested annotation expansion** — confirmed
   substantially. Annotation-emission failures (~14 tests)
   are the dominant Turtle/TriG failure pattern.

Additional risks the corpus surfaces:

5. **Canonicaliser literal-escape canonical form**
   — *new and largest cluster*: the canonicaliser's
   `NQuadsSerializer` produces a non-canonical escape form
   for control characters, quotes, backslashes, and
   numerically-escaped Unicode. See the C14N section above.
6. **Reified-triple emission shape** — see above.
7. **Surrogate-pair validation in string-literal escapes**
   — see above.
8. **VERSION-directive string-form restriction** — see above.
9. **`NQuadsReader`'s permissiveness on language and
   direction tags** — see above.

## Reproducing this report

```
dotnet test --project test/Lumoin.Veritas.Tests/Lumoin.Veritas.Tests.csproj `
    --no-build --filter "FullyQualifiedName~W3c"
```

The runner reports each test as `passed`, `failed`, or
`skipped`. `Inconclusive` (the MSTest mapping for skipped)
indicates a structural skip — an unrecognised test type IRI
or a manifest pointing at an unimplemented format — never a
buried parser defect.

## What this batch did NOT do

Per the handoff:

- No parser, emitter, or canonicaliser defects were fixed.
  The list above is the input to follow-up batches.
- The RDF 1.1 corpus is not vendored.
- No RDF/XML or other unparsed formats are wired in.
