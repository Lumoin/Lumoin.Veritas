# Lumoin.Veritas.CborLd

CBOR-LD encoder and decoder for Veritas. Implements the W3C CBOR-LD
specification with active-context-driven term-codec compression, a
registry pattern for compiled and dynamically loaded compression tables,
built-in value codecs, and a Deterministic profile suitable for direct
signing.

## Status

This project is currently a structural scaffolding only. The public
surface for the profile selector, registry, registry entry, codec
dictionaries, the load delegate, and the format-specific processing
exception is in place. The encoder and decoder are the next sub-batch's
work; see the repo `PROGRESS.md` for the node-abstraction decision the
encoder needs to settle.

## Public surface (so far)

- `CborLdProfile` — `Default` or `Deterministic`.
- `CborLdKeywordCodec` and `CborLdTermCodec` — `readonly record struct`
  data carriers mapping keyword/term strings to their compressed integer
  identifiers.
- `CborLdRegistryEntry` — POCO carrying a registry-entry id plus its
  keyword and term codec dictionaries. `CborLdRegistryEntry.Passthrough`
  is a shared empty entry (id `0`, no compression).
- `CborLdRegistry` — in-memory registry that always includes the
  passthrough entry. Exposes `TryGet` and an `AsDelegate()` adapter
  returning a `LoadCborLdRegistryEntryDelegate`.
- `LoadCborLdRegistryEntryDelegate` — the delegate-shaped lookup
  boundary for caller-supplied registry sources.
- `CborLdProcessingException` — surfaces encode/decode failures.

## When to use this

When you need a CBOR encoding of a Linked Data document that other
parties can decode back to a JSON-LD-equivalent representation. The
Deterministic profile additionally pins all encoding-discretion
decisions so the output is byte-stable across encoders; that property
is what makes the resulting bytes suitable for content-addressing,
signing, or any protocol that needs equality on serialised form.

## Namespace note

The project file is `Lumoin.Veritas.CborLd.csproj` but its namespace is
`Lumoin.Veritas.Cbor.CborLd`. This is intentional: the CBOR family
discoverability principle keeps everything under `Lumoin.Veritas.Cbor.*`
while the project name reflects the standalone-distribution shape.
