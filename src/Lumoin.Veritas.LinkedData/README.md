# Lumoin.Veritas.LinkedData

Format-agnostic Linked Data primitives shared between JSON-LD and
CBOR-LD. Consumed by both `Lumoin.Veritas.JsonLd` (W3C JSON-LD 1.1) and
`Lumoin.Veritas.CborLd` (W3C CBOR-LD 1.0).

## Public surface

- `LinkedDataContext` — the active context: term definitions, base
  IRI, vocabulary, language, direction. Immutable; every mutation
  operation returns a new instance. Format-neutral and shared by
  JSON-LD and CBOR-LD.
- `TermDefinition` — the per-term mapping (IRI, type, container,
  language, direction, scoped context). Scoped contexts ride on
  `ScopedContextEntries` as a format-neutral POCO list, pre-extracted
  at term-definition time.
- `ContextProcessing` — the W3C JSON-LD 1.1 §4.1 / CBOR-LD 1.0 §5.3
  active-context algorithm.
- `ContextResolverDelegate`, `FetchRemoteResourceDelegate`,
  `ParseRemoteContextDelegate` — the I/O boundary delegates that
  resolve a context URI to its document bytes and parse them.
- `IriUtils` — pure helpers: `IsKeyword`, `IsKeywordLike`,
  `IsAbsoluteIri`, `IsRelativeIri`, `ResolveIri`. `ExpandIri` is an
  instance method on `LinkedDataContext`.
- `LinkedDataContextEntry` — a single `@context` entry as either a
  URL, an inline dictionary, or a reset. Format-neutral input to the
  active-context processing core.
- `LinkedDataTermSource` — format-neutral description of an inline
  term definition (IRI, type, container, language, direction, reverse,
  scoped context).

## When to use this

If you are building a Linked Data pipeline (JSON-LD, CBOR-LD, or
another W3C-style format), reach for `Lumoin.Veritas.LinkedData` for
the primitives that don't belong to any one serialization format. Keep
format-specific processing (JSON parsing, CBOR codec details) in the
format's own project.

## Scope note

The W3C §4.1 / §5.3 active-context algorithm is implemented in
`ContextProcessing` (split across `ContextProcessing.cs` for the
step-level helpers and `ContextProcessing.Algorithms.cs` for the
async orchestration). It never inspects format-specific document-tree
content; the format-specific shells
(`Lumoin.Veritas.JsonLd.ContextProcessor`,
`Lumoin.Veritas.CborLd.CborLdActiveContextScope`) own document-tree
extraction and format-specific exception wrapping.

## References

- W3C JSON-LD 1.1 — <https://www.w3.org/TR/json-ld11/>
- W3C JSON-LD 1.1 API — <https://www.w3.org/TR/json-ld11-api/>
- W3C CBOR-LD 1.0 — <https://www.w3.org/TR/cbor-ld-10/>
