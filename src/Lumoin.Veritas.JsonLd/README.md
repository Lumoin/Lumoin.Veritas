# Lumoin.Veritas.JsonLd

JSON-LD 1.1 processing for Veritas: context resolution, expansion to
RDF quads, and the adapter layer that bridges JSON-LD documents to
CBOR-LD.

## Public surface

- `JsonLdNode` — opaque-handle wrapper around a parsed JSON node plus
  a `JsonLdNodeNavigator` dispatch table. Implements
  `ILinkedDataDocumentNode` so format-agnostic LinkedData code can
  consume it without depending on a specific JSON library.
- `JsonLdNodeNavigator` and the `Document/` delegates
  (`GetNodeKindDelegate`, `GetStringValueDelegate`,
  `GetBooleanValueDelegate`, `GetRawNumberDelegate`,
  `TryGetPropertyDelegate`, `EnumerateArrayDelegate`,
  `EnumerateObjectDelegate`, `CloneNodeDelegate`) — the adapter
  surface that decouples JSON-LD from any single JSON parser.
- `JsonLdParserDelegate` — UTF-8 bytes to `JsonLdNode`.
- `ContextProcessor` — the W3C JSON-LD 1.1 §4.1 context-processing
  algorithm. Consumes `JsonLdNode` for `@context` extraction; produces
  `JsonLdContext` (defined in `Lumoin.Veritas.LinkedData`).
- `JsonLdExpander` — expansion to RDF quads.
- `JsonLdProcessingException` — surfaces processing failures.
- `Adapters/CborLdInputNodeAdapter.FromJsonLd` — converts a
  `JsonLdNode` tree into a `CborLdInputNode` tree for handoff to the
  CBOR-LD encoder. The reverse direction (`ToJsonLd`) is deferred
  until a consumer needs it with a concrete builder pattern.

## When to use this

For any JSON-LD parsing, context processing, or quad extraction
work. The adapter sub-namespace bridges to `Lumoin.Veritas.CborLd`
for cross-format encoding.

## Active-context algorithm

The JSON-LD 1.1 §4.1 algorithm currently lives in this project's
`ContextProcessor.cs` and is coupled to `JsonLdNode`. The
format-agnostic POCO inputs needed for a future extraction
(`LinkedDataContextEntry`, `LinkedDataTermSource`) are in place in
`Lumoin.Veritas.LinkedData`. Extraction of the algorithm to a
shared `LinkedData/ContextProcessing.cs` is planned; the migration
will keep `ContextProcessor`'s public surface stable as a thin
shell.

## References

- W3C JSON-LD 1.1 — <https://www.w3.org/TR/json-ld11/>
- W3C JSON-LD 1.1 API — <https://www.w3.org/TR/json-ld11-api/>
- W3C CBOR-LD 1.0 — <https://www.w3.org/TR/cbor-ld-10/>
