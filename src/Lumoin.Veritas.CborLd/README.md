# Lumoin.Veritas.CborLd

CBOR-LD encoder and decoder for Veritas. Implements the W3C CBOR-LD 1.0
specification with active-context-driven term-codec compression, a
registry pattern for compiled and dynamically loaded compression tables,
a delegate-based typed-value codec registry, and a Deterministic profile
suitable for direct signing.

## Status

- Passthrough encoder (`CborLdEncoder.EncodeAsync`) and decoder
  (`CborLdDecoder.DecodeAsync`) are implemented over `CborLdInputNode`.
- Compression-driven entries (term and keyword substitution per
  §5.2.5 / §5.2.6) are implemented, including the singular/plural rule.
- Typed-value codecs are dispatched through a process-wide static
  registry (`CborLdTypedValueCodecs`) using named delegates. The
  library ships **no** concrete codec implementations; consumers wire
  their own. The test suite's reference implementations live in
  `CborLdTestSetup` in the test project.
- Wire-form pinning tests (`CborLdWireFormPinningTests`) pin canonical
  hex sequences for passthrough, registry-only compression, and
  typed-value byte-string encoding.
- Round-trip property gates: passthrough (10,000 iters) +
  compression (10,000 iters), both green.
- DoS hardening on the read path: configurable
  `MaxByteStringLength` / `MaxTextStringLength` / `MaxArrayLength` /
  `MaxMapEntryCount` / `MaxDepth` / `MaxTagDepth` /
  `MaxIndefiniteStringChunks` on `CborSerializerOptions`.
- Active-context-driven term-map construction per W3C CBOR-LD 1.0 §5.3
  is implemented end-to-end. Embedded `@context`, type-scoped, and
  property-scoped contexts dynamically remap terms during encode/decode;
  term-defined typed values (`{"@id": ..., "@type": "..."}`) dispatch
  through the codec registry without static term codec registration.
- Caller-provided type tables per W3C CBOR-LD 1.0
  (`"callerProvidedTable"` sentinel) are supported: registry entries can
  declare a type table as caller-supplied, and the caller passes the
  mapping at encode/decode time via `CborLdCallerProvidedTypeTables`.

## Public surface

- `CborLdProfile` — `Default` or `Deterministic`.
- `CborLdStrategy` — `Compression` or `Decompression`.
- `CborLdKeywords` — fixed JSON-LD keyword id table per §5.4.1.
- `CborLdKeywordCodec` and `CborLdTermCodec` — `readonly record
  struct` data carriers. `CborLdTermCodec.Type` (nullable) marks a
  term as carrying a typed value.
- `CborLdRegistryEntry` — POCO with id, keyword/term codec
  dictionaries, processing model, provisional flag, and type tables.
  `Passthrough` is the shared empty entry (id `0`). Type-table values
  are of type `CborLdTypeTableSource` — either registry-supplied
  mappings or a caller-provided marker.
- `CborLdTypeTableSource` — discriminated union of
  `CborLdRegistryProvidedTypeTable` (carries mappings) and
  `CborLdCallerProvidedTypeTableMarker` (declares the table caller-
  supplied per the W3C CBOR-LD 1.0 `"callerProvidedTable"` sentinel).
- `CborLdCallerProvidedTypeTable` /
  `CborLdCallerProvidedTypeTables` — per-call collection holding
  caller-supplied mappings keyed by type name; the reverse direction
  for decoding is derived lazily from the supplied forward mapping.
- `CborLdRegistry` — in-memory registry; exposes `TryGet` and an
  `AsDelegate()` adapter returning a `LoadCborLdRegistryEntryDelegate`.
- `LoadCborLdRegistryEntryDelegate` — delegate-shaped lookup boundary.
- `CborLdInputNode` and concrete subtypes (`Null`, `Bool`, `Int`,
  `Double`, `String`, `Bytes`, `Array`, `Map`) — the format-neutral
  node abstraction. Bytes wraps `ReadOnlyMemory<byte>` and can alias
  source memory on the decode path.
- `CborLdTypedValueEncodeDelegate` / `CborLdTypedValueDecodeDelegate`
  — named delegates the consumer implements per type.
- `ResolveCborLdTypedValueEncoderDelegate` /
  `ResolveCborLdTypedValueDecoderDelegate` — matcher delegates the
  consumer supplies to `CborLdTypedValueCodecs.Initialize`.
- `CborLdMatcherContext` — routing context (frozen
  `Dictionary<string, object>` parameter bag) passed to matcher
  delegates. Well-known keys in `CborLdContextKeys`.
- `CborLdTypedValueCodecs` — static, process-wide registry. Call
  `Initialize(encoderResolver, decoderResolver)` once at app start.
- `CborLdEncoder.EncodeAsync` / `CborLdDecoder.DecodeAsync` — public
  entrypoints. Both accept optional caller-provided type tables and
  memory pool parameters. Sibling overloads
  `EncodeWithRemoteContextsAsync` / `DecodeWithRemoteContextsAsync` add
  remote-`@context` resolution via the fetch/parse delegate trio.
- `CborLdProcessingException` — surfaces encode/decode failures.

## Configuring codecs

The codec registry is configured once at application start, typically
via a `[ModuleInitializer]` or explicit composition-root code:

```csharp
CborLdTypedValueCodecs.Initialize(
    (typeName, context) => typeName switch
    {
        "url" => MyUrlEncoder,
        "http://www.w3.org/2001/XMLSchema#date" => MyDateEncoder,
        _ => throw new ArgumentException($"Unknown type {typeName}")
    },
    (typeName, context) => typeName switch
    {
        "url" => MyUrlDecoder,
        "http://www.w3.org/2001/XMLSchema#date" => MyDateDecoder,
        _ => throw new ArgumentException($"Unknown type {typeName}")
    });
```

The test project's `CborLdTestSetup` is the canonical example.

## Caller-provided type tables

Some W3C CBOR-LD registry entries leave one or more type tables
unspecified, signalled by the sentinel string `"callerProvidedTable"`
in the entry's `typeTables` array. This pattern fits use cases where
the set of values to compress is known to a deployment but is not
globally registered — for example, a Verifiable Credentials issuer
maintaining its own URL set:

```csharp
//Caller-supplied mapping from issuer-specific URLs to integer ids.
//The same content the issuer would put in a registry entry's "url"
//type table, but kept under the issuer's control.
FrozenDictionary<string, int> issuerUrls = ...;

CborLdCallerProvidedTypeTables tables = new CborLdCallerProvidedTypeTables()
    .Add(new CborLdCallerProvidedTypeTable("url", issuerUrls));

await CborLdEncoder.EncodeAsync(input, registryEntry, profile, buffer, callerTables: tables);

//On the read side, supply the same forward mapping; the decoder
//derives the inverse internally.
CborLdDecodeResult result = await CborLdDecoder.DecodeAsync(
    bytes, registryLoader, callerTables: tables);
```

The library reads the supplied tables during a single call and does
not retain references afterwards; callers can hold tables as long-
lived state without copy overhead. If a registry entry declares a
caller-provided table but the caller supplies none, encode and decode
raise `CborLdProcessingException` with the error code
`"caller provided type table missing"` and a message naming the
missing type.

## When to use this

When you need a CBOR encoding of a Linked Data document that other
parties can decode back to a JSON-LD-equivalent representation. The
Deterministic profile pins encoding discretion so the output is
byte-stable across encoders; that property is what makes the resulting
bytes suitable for content-addressing, signing, or any protocol that
needs equality on serialised form.

## Spec coverage

Implemented: outer wrapping `tag(0xCB1D, [id, payload])` per §5.1;
keyword + term substitution per §5.2.5 / §5.2.6; singular/plural id
rule; unknown-key passthrough; typed-value codec dispatch per §5.5.2;
passthrough mode; §5.3 active-context processing (embedded /
type-scoped / property-scoped, dynamic term-id remapping, term-
defined typed values); caller-provided type tables.

Deferred: JsonLd ↔ CborLdInputNode adapter's `ToJsonLd` reverse
direction; true streaming over multi-segment `ReadOnlySequence<byte>`;
external W3C-registry loader package.

## References

- W3C CBOR-LD 1.0 — <https://www.w3.org/TR/cbor-ld-10/>
- JSON-LD 1.1 — <https://www.w3.org/TR/json-ld11/>
- RFC 8949 (CBOR) — <https://www.rfc-editor.org/rfc/rfc8949>
