# Lumoin.Veritas.LinkedData

Format-agnostic Linked Data primitives shared between JSON-LD and CBOR-LD.

## Public surface

- `ContextResolverDelegate` — the I/O boundary delegate that resolves a
  context URI to its document bytes. Used by JSON-LD and CBOR-LD pipelines
  alike.

## When to use this

If you are building a Linked Data pipeline (JSON-LD, CBOR-LD, or another
W3C-style format), reach for `Lumoin.Veritas.LinkedData` for the primitives
that don't belong to any one serialization format. Keep format-specific
processing (JSON parsing, CBOR codec details) in the format's own project.

## Scope note

This project currently exposes only the context-resolver delegate. Deeper
extraction of the active-context machinery (term map, term definitions, IRI
expansion across formats) requires a focused refactor of the JSON-LD
processor's coupling to its node abstraction; that work is out of scope for
the current iteration.
