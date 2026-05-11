# Lumoin.Veritas.Cid

DASL Content Identifier (CID) primitives: a small, dependency-free type for
content-addressing referenced bytes by their SHA-256 digest plus a codec
indicating how those bytes should be interpreted.

## Public surface

- `Cid` — mutable POCO carrying a `CidCodec` and a 32-byte digest.
- `CidCodec` — enum with `Raw` (`0x55`) and `Drisl` (`0x71`).
- `CidParser` — parses the canonical string form (`b` + lowercase RFC 4648
  base32, no padding) and the canonical 36-byte binary form. Throws
  `CidParseException` on any deviation from the spec.
- `CidFormatter` — emits the canonical string and binary forms.
- `CidHasher` — builds a `Cid` from content bytes and a caller-supplied
  `HashDelegate`.
- `CidParseException` — `FormatException` subtype thrown by `CidParser`.

## When to use this

Reach for `Lumoin.Veritas.Cid` whenever bytes need a stable, recomputable
name: artefacts persisted to content-addressed storage, references between
documents in a deterministic dataset, deltas in a synchronisation log,
commitments in a cryptographic protocol. The type is wire-format-aware but
intentionally CBOR-free; the CBOR Tag 42 wrapping with its historical `0x00`
multibase prefix lives in the CBOR project.

## Example

```csharp
using System.Security.Cryptography;
using Lumoin.Veritas.Cid;
using Lumoin.Veritas.Core;

ReadOnlySpan<byte> content = "hello"u8;
HashDelegate sha256 = SHA256.HashData;

Cid cid = CidHasher.ComputeFromBytes(content, CidCodec.Raw, sha256);
string text = CidFormatter.ToCanonicalString(cid);
Cid roundTripped = CidParser.Parse(text);
```
