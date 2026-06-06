# Decision record — Veritas as the RDF backend for Verifiable's Data Integrity canonicalization

Status: **proposed (2026-06-03)**. Supersedes the earlier "provenance seam, consumer-blocked" framing
of this record, which was wrong: the consumer API already exists in `..\Verifiable`, and Veritas
already exposes every primitive it needs. Companion concerns: the planned access/identity seam
(`AccessControlDelegate` with an opaque `AccessContext` threaded through the read paths) and the
JSON family layering (`Lumoin.Veritas.Json` is the STJ-free value model; `Json.Stj` is the only
System.Text.Json engine; `JsonLd` layers on the model).

## Context

The motivating use case is **selective disclosure (SD)** of Verifiable Credentials (VC Data Integrity
`ecdsa-sd-2023` / `bbs-2023`). `..\Verifiable` already implements SD — but its RDF canonicalization is
supplied through a **delegate seam** that is today backed by **dotNetRdf**, pulling the entire
dotNetRdf suite (`dotNetRdf.dll`, `.Shacl`, `.Query.*`, `.Inferencing`, …) into the dependency graph.

The seam is `Verifiable.Core.Model.DataIntegrity.CanonicalizationDelegate`:

```csharp
delegate ValueTask<CanonicalizationResult> CanonicalizationDelegate(
    string json, ContextResolverDelegate? contextResolver, CancellationToken ct);

sealed class CanonicalizationResult
{
    string CanonicalForm { get; init; }                       // canonical N-Quads
    IReadOnlyDictionary<string,string>? LabelMap { get; init; } // canonical bnode id -> original bnode id
}
```

The dotNetRdf implementation lives in the test infrastructure (`CanonicalizationTestUtilities`, used by
`DataIntegrity` / `FlowTests`). It expands JSON-LD, builds a triple store, runs `RdfCanonicalizer`, and
returns `SerializedNQuads` + `IssuedIdentifierMap`. The user's question: would Veritas providing this
implementation make ECDSA-SD easier?

## Finding: Veritas already provides the whole primitive

Every piece `CanonicalizationDelegate` needs is already public and conformance-tested in Veritas:

1. **JSON-LD → quads** — `JsonLdExpansionTree.ExpandAsync(doc, base, resolver, parse, …)` then
   `JsonLdRdfSerializer.Serialize(expanded, pool)` → `List<Quad>`. (Expand + toRdf are 100% W3C
   conformant.)
2. **Quads → canonical N-Quads + label map** — `RdfCanonicalizer.CanonicalizeWithMap(quads, hash)`
   (`src/Lumoin.Veritas.Canonicalization/`) → `RdfCanonicalizationResult(string Canonical,
   IReadOnlyDictionary<string,string> IssuedIdentifiers)`. (RDFC-1.0 is conformant against the vendored
   W3C rdf-canon suite, the §4.8.2 automorphism branch included.) `IssuedIdentifiers` is exactly the
   blank-node **label map** ecdsa-sd needs to join the reduced vs full canonicalizations.

So `RdfCanonicalizationResult` maps one-to-one onto `CanonicalizationResult`
(`Canonical → CanonicalForm`, `IssuedIdentifiers → LabelMap`), and `hash` is the SHA-256 seam
(`SHA256.HashData`; per-item hot-path functions stay synchronous behind
swappable delegate seams; async crypto belongs at boundaries, not value-level hashing). The only adapter work is two small mappings: invert
`IssuedIdentifiers` (Veritas returns original→canonical; the seam documents canonical→original) and
reconcile the `"_:"` prefix convention, and bridge Verifiable's `ContextResolverDelegate (Uri→string)`
to Veritas's `ContextResolverDelegate`.

## Decision

**Yes — Veritas should back Verifiable's `CanonicalizationDelegate`, replacing dotNetRdf.** The win is a
large dependency reduction (drop the whole dotNetRdf suite) onto a purpose-built, STJ-free,
AOT-friendly, fully-conformant stack, with the blank-node label map ecdsa-sd requires already exposed.

The adapter (~10–20 lines) lives in `..\Verifiable` (it references Veritas + `Verifiable.Core`); Veritas
must not reference Verifiable. Veritas's responsibility is only to keep the primitives composable and to
**prove the composition** — a Veritas test takes a small JSON-LD credential, expands → serializes →
`CanonicalizeWithMap`, and asserts canonical N-Quads plus a non-empty label map (the shape the adapter
returns). The earlier "thread JSON-Pointer provenance through expansion" idea is **not needed**:
ecdsa-sd's selective disclosure is driven by joining two canonicalizations through their shared label
maps (the reduced credential is canonicalized independently), which the label map already supports;
per-field JSON-Pointer provenance is a separate, later concern only if a non-canonicalization SD scheme
needs it.

### Open choices (small)

- **Convenience entry point:** optionally add a one-call `JSON-LD string → RdfCanonicalizationResult`
  to Veritas so consumers don't re-discover the expand→serialize→canonicalize chain. It would need a
  home that references both `Lumoin.Veritas.JsonLd` and `Lumoin.Veritas.Canonicalization` (peers
  today) — a small new project or a higher composition layer. Deferred unless a second consumer wants
  it; the chain is short enough to live in Verifiable's adapter for now.
- **Oracle:** validate the adapter in Verifiable against the `ecdsa-sd-2023` selective-disclosure test
  vectors (a real oracle), confirming byte-identical canonical N-Quads and label-map joins vs the
  dotNetRdf path before removing dotNetRdf.

## Status of the sibling items shipped alongside this record

- JSON Schema **Detailed/Verbose** output — done (meta-schema-validated).
- CBOR **EDN** diagnostic notation (incl. embedded `<<>>`) — done.
- This item — **design corrected**; Veritas already provides the primitive, the adapter is thin and
  lives in Verifiable; recommended to prove the composition with a Veritas test next.
