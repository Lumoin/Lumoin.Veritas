# Lumoin.Veritas.JsonLd

JSON-LD 1.1 processing for Veritas: context resolution, expansion to
RDF quads, and the adapter layer that bridges JSON-LD documents to
CBOR-LD.

## Public surface

- `JsonLdNode` — opaque-handle wrapper around a parsed JSON node plus
  a `JsonLdNodeNavigator` dispatch table. Lets format-agnostic
  LinkedData code carry JSON-LD subtrees without depending on a
  specific JSON library.
- `JsonLdNodeNavigator` and the `Document/` delegates
  (`GetNodeKindDelegate`, `GetStringValueDelegate`,
  `GetBooleanValueDelegate`, `GetRawNumberDelegate`,
  `TryGetPropertyDelegate`, `EnumerateArrayDelegate`,
  `EnumerateObjectDelegate`, `CloneNodeDelegate`) — the adapter
  surface that decouples JSON-LD from any single JSON parser.
- `JsonLdParserDelegate` — UTF-8 bytes to `JsonLdNode`.
- `ContextProcessor` — the JSON-LD-specific shell over the W3C JSON-LD
  1.1 §4.1 active-context algorithm. Extracts `@context` entries from
  a `JsonLdNode` subtree, calls the format-agnostic
  `Lumoin.Veritas.LinkedData.ContextProcessing` core, and wraps any
  `LinkedDataProcessingException` as a `JsonLdProcessingException`.
  Returns `LinkedDataContext`.
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

The W3C JSON-LD 1.1 §4.1 algorithm lives in the format-agnostic
`Lumoin.Veritas.LinkedData.ContextProcessing` core (generic over
`TNode`). This project's `ContextProcessor.cs` is a thin shell that
walks the `JsonLdNode` document tree, extracts the format-agnostic
POCO inputs (`LinkedDataContextEntry`, `LinkedDataTermSource`), and
delegates to the core. CBOR-LD uses the same core under a different
`TNode`.

## References

- W3C JSON-LD 1.1 — <https://www.w3.org/TR/json-ld11/>
- W3C JSON-LD 1.1 API — <https://www.w3.org/TR/json-ld11-api/>
- W3C CBOR-LD 1.0 — <https://www.w3.org/TR/cbor-ld-10/>
