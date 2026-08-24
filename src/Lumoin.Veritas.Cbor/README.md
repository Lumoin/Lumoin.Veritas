# Lumoin.Veritas.Cbor

General-purpose CBOR codec for Veritas. Implements RFC 8949 encoding and
decoding with five conformance modes, ten standard tag converters, a
pool-aware slab-buffer-writer, and pipeline-friendly
`IBufferWriter<byte>` / `ReadOnlySequence<byte>` integration. The DRISL
and dCBOR profile wrappers live in sub-namespaces of this project.

## Public surface

- `CborWriter` and `CborReader` — the general-purpose codec. Cover all
  eight major types, definite- and indefinite-length items, half /
  single / double precision floats, simple values, and tags.
- `CborSerializerOptions` with `CborConformanceMode` (`Lax`, `Strict`,
  `RfcCanonical`, `Ctap2Canonical`, `Cde`) — the discipline selector.
  Canonical modes enforce length-minimised integers, sorted map keys,
  and rejection of indefinite-length items. CDE additionally applies
  float reduction (shortest IEEE 754 form that round-trips losslessly).
- `CborTag`, `CborMajorType`, `CborSimpleValue`, `CborReaderState`,
  `CborConverter` and `CborConverter<T>` — the wire-shape and converter
  surface.
- `Converters/` — ten standard tag converters: Tag 0 date/time string,
  Tag 1 epoch time, Tag 2 unsigned big integer, Tag 3 negative big
  integer, Tag 4 decimal fraction, Tag 5 bigfloat, Tag 32 URI, Tag 33
  base64url bytes, Tag 34 base64 bytes, Tag 55799 self-describe. Plus
  the value-struct shapes `CborDecimalFraction` and `CborBigfloat`.
- `Drisl/` — the project's deterministic CBOR profile (DASL DRISL).
  `DrislWriter` and `DrislReader` expose a restricted surface that
  makes non-DRISL output unreachable; `CidCborConverter` handles Tag 42
  CIDs with the multibase-prefix wrapping.
- `Dcbor/` — the dCBOR profile (draft-mcnally-deterministic-cbor),
  similar to DRISL but more permissive on key types and tags.

## When to use this

For any CBOR encoding or decoding work in the Veritas family. The
plain `CborWriter` / `CborReader` are appropriate for general use. The
DRISL wrapper is the right choice when the encoded bytes need to be
content-addressable or signable — its surface makes non-deterministic
constructs (NaN floats, indefinite length, arbitrary tags) impossible
to emit. The dCBOR wrapper covers the IETF deterministic profile for
interop with dCBOR-aware peers.

## Conformance and testing

- Writer-side differential property tests pair `CborWriter` against
  the BCL `System.Formats.Cbor.CborWriter` and assert byte-identical
  output under `Canonical` and `Ctap2Canonical` modes across many
  randomly generated trees.
- Reader-side differential tests symmetrically pair `CborReader`
  against the BCL reader, decoding the same bytes via both and
  asserting the resulting trees agree.
- The differential grammar covers integers, byte strings, text
  strings, booleans, null, arrays, and string-keyed maps. Floats are
  exercised by the RFC 8949 Appendix A vector tests; differential-test
  parity for floats under `Canonical` mode is tracked as a separate
  hardening item (BCL's float-reduction discipline has subtle edges).

## Architectural note

This project owns CBOR knowledge for the Veritas family. Library
projects that need CBOR-shaped serialisation consume `CborWriter` /
`CborReader` directly; they do not pick up `System.Formats.Cbor`
themselves (the project's banned-API analyser enforces that).
