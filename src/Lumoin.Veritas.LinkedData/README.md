# Lumoin.Veritas.LinkedData

Format-agnostic Linked Data primitives shared between JSON-LD and
CBOR-LD. Consumed by both `Lumoin.Veritas.JsonLd` (W3C JSON-LD 1.1) and
`Lumoin.Veritas.CborLd` (W3C CBOR-LD 1.0).

## Public surface

- `JsonLdContext` — the active context: term definitions, base IRI,
  vocabulary, language, direction. Immutable; every mutation operation
  returns a new instance. Named for naming continuity with the
  JSON-LD spec; the structure is format-neutral.
- `TermDefinition` — the per-term mapping (IRI, type, container,
  language, direction, scoped context).
- `ILinkedDataDocumentNode` — empty marker interface. Format adapters
  implement it on their node types so the LinkedData surface can
  reference document nodes without taking a format dependency.
- `ContextResolverDelegate` — the I/O boundary delegate that resolves
  a context URI to its document bytes.
- `IriUtils` — pure helpers: `IsKeyword`, `IsKeywordLike`,
  `IsAbsoluteIri`, `IsRelativeIri`, `ResolveIri`. `ExpandIri` is an
  instance method on `JsonLdContext`.
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

The full active-context processing algorithm (JSON-LD 1.1 §4.1; W3C
CBOR-LD 1.0 §5.3) is currently implemented inside
`Lumoin.Veritas.JsonLd.ContextProcessor` and is tightly coupled to
`JsonLdNode`. The format-agnostic POCO inputs
(`LinkedDataContextEntry`, `LinkedDataTermSource`) are in place; full
extraction of the algorithm to a `LinkedData/ContextProcessing.cs`
static class remains as future work.

## References

- W3C JSON-LD 1.1 — <https://www.w3.org/TR/json-ld11/>
- W3C JSON-LD 1.1 API — <https://www.w3.org/TR/json-ld11-api/>
- W3C CBOR-LD 1.0 — <https://www.w3.org/TR/cbor-ld-10/>
