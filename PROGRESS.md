# Veritas multi-phase build status

Tracks the four-phase work described in the original handoff (Cid → Cbor →
LinkedData → CborLd). Update on every session completion.

## At a glance

- Tests passing: **1125** (started at 901; ~224 new tests added).
- Build: 0 warnings, 0 errors. AOT-compatible. `TreatWarningsAsErrors=true` honoured.
- Phase gates: 1 ✅, 2 ✅, 3 ✅, 4 🟢 (passthrough encoder/decoder + 10,000-iter round-trip gate green; compression-driven entries and built-in value codecs remain).

## Phase 1 — Lumoin.Veritas.Cid: complete

Gate green. Files at `src/Lumoin.Veritas.Cid/` (`Cid.cs`, `CidCodec.cs`,
`CidParser.cs`, `CidFormatter.cs`, `CidHasher.cs`, `CidParseException.cs`).
Tests at `test/Lumoin.Veritas.Tests/Cid/` (4 classes, 35 tests).

Notable spec correction: handoff said "37-byte binary form" but the DASL
spec is 4 + 32 = 36 bytes. The CBOR Tag 42 form is 37 bytes once the
historical `0x00` multibase prefix is prepended; that prefix is added by
`CidCborConverter` (Phase 2), not by `CidFormatter`.

## Phase 2 — Lumoin.Veritas.Cbor: substantially done

Files at `src/Lumoin.Veritas.Cbor/`:

- Foundation: `CborMajorType`, `CborSimpleValue`, `CborConformanceMode`,
  `CborTag`, `CborConverter` (base + `CborConverter<T>` + factory),
  `CborSerializerOptions`, `CborThrowHelper`.
- Internal: `CborHeader`, `CborFloatConversions`, `SlabBufferWriter`.
- Codec: `CborWriter`, `CborReader`, `CborReaderState`. Covers all eight
  major types, definite + indefinite arrays/maps, half/single/double
  floats, signed/unsigned ints with length-minimised encoding, byte/text
  strings, booleans, null, undefined, simple values, tags. State machine
  validates definite-container counts and rejects half-emitted map pairs.
  Map sorting in `RfcCanonical` / `Ctap2Canonical` / `Cde` modes via
  per-frame buffering. Float reduction in `Cde` mode only (RFC 7049
  canonical does not reduce floats).
- Tag converters at `Converters/`: 10 standard tags (Tag 0, 1, 2, 3, 4,
  5, 32, 33, 34, 55799) plus `CborDecimalFraction` and `CborBigfloat`
  value structs.
- DRISL wrapper at `Drisl/`: `DrislWriter`, `DrislReader`, `DrislDefaults`,
  `CidCborConverter` (Tag 42).
- dCBOR wrapper at `Dcbor/`: `DcborWriter`, `DcborReader`, `DcborDefaults`.

Tests at `test/Lumoin.Veritas.Tests/Cbor/`:

- `CborWriterPrimitivesTests` (RFC 8949 Appendix A vectors)
- `CborReaderPrimitivesTests` (mirror of Appendix A vectors)
- `CborMapSortingTests` (canonical sort rules)
- `CborTagConverterTests` (round-trips for all 10 standard converters)
- `SlabBufferWriterTests`
- `DrislWriterTests` + `DcborWriterTests` (surface restrictions plus
  round-trip)
- `CborDifferentialPropertyTests` (writer-side CsCheck differential
  against BCL `System.Formats.Cbor.CborWriter` under `Canonical` and
  `Ctap2Canonical`, **10,000 iterations** each, byte-identical
  assertion).
- `CborReaderDifferentialPropertyTests` (reader-side CsCheck
  differential against BCL `System.Formats.Cbor.CborReader` under both
  canonical modes, **10,000 iterations** each, tree-equality assertion
  via the shared `DifferentialCborValueComparer`).
- `DifferentialCborValue.cs` carries the shared sum type, generator,
  and stack-based comparer used by both differential test classes.

### Phase 2 items closed (all deferred items now done):

- ✅ **10,000-iter differential count.** Both writer and reader
  differential property tests run at 10,000 iterations per conformance
  mode.
- ✅ **Reader-side differential.** Symmetric counterpart of the writer
  differential is in place.
- ✅ **`README.md` for the Cbor project** is written.
- ✅ **Indefinite-length chunked byte/text strings.** `CborWriter` exposes
  `WriteStartIndefinite{Byte,Text}String` plus `WriteEndIndefinite{Byte,Text}String`;
  chunks are emitted via the existing `WriteByteString` / `WriteTextString`
  calls while the frame is open. `CborReader` auto-concatenates indefinite
  chunks in `ReadByteString` / `ReadTextString`, rejects nested
  indefinite introducers per RFC 8949 §3.2.3, and rejects mismatched
  chunk types. 11 tests in `CborIndefiniteStringTests`.
- ✅ **BenchmarkDotNet harnesses.** `CborEncodingBenchmarks` (parametrised
  over `Lax`, `RfcCanonical`, `Cde`) and `DrislBenchmarks` (CID + payload
  document) added to `test/Lumoin.Veritas.Benchmarks/`.
- ✅ **Spec-accurate canonical float reduction.** `CborWriter.WriteDouble`
  applies shortest-IEEE-754 reduction under every deterministic
  conformance mode (`RfcCanonical`, `Ctap2Canonical`, `Cde`) per
  RFC 8949 §4.1 / §4.2.2. NaN canonicalises to `0xF9 7E 00`, +/- infinity
  to `0xF9 7C 00` / `0xF9 FC 00`. The standalone-case agreement with BCL
  is pinned by `CborCanonicalFloatDivergenceTests`; the project follows
  the spec unconditionally even if a future BCL version diverges.
- ✅ **dCBOR float reduction.** `DcborWriter.WriteDouble` applies the
  numeric-reduction rules from draft-mcnally-deterministic-cbor §2.5
  (latest draft -17): NaN / +Inf / -Inf canonicalise to half-precision;
  integer-valued finite floats in `[-2^63, 2^64-1]` emit as integers
  (collapsing `0`, `0.0`, and `-0.0` onto `0x00`); other finite values
  emit shortest IEEE 754. `DcborReader` accepts canonical half-precision
  encodings. 14 new tests in `DcborFloatReductionTests`.

### Phase 2 known design decisions (do not revisit):

- `CborConformanceMode` is the enum from the handoff: `Lax`, `Strict`,
  `RfcCanonical`, `Ctap2Canonical`, `Cde`. Float reduction applies in
  all three deterministic modes per RFC 8949 §4.1.
- Map sorting uses a per-frame `ArrayBufferWriter<byte>` to capture each
  pair's encoded bytes. Nested sort frames each have their own buffer;
  the topmost wins as `ActiveSink`. `bytesWritten` is updated only when
  writes go to the original destination, so it reflects "bytes durably
  emitted" rather than "bytes captured into intermediate buffers."
- Tag pairing collapses pending tags onto the next completed data item;
  the parent container is bumped exactly once for the whole tagged
  group. No recursion. State machine details are in `OnDataItemCompleted`.

### Phase 2 infrastructure changes (committed):

- `BannedSymbols.Serialization.txt` now bans `System.Text.Json` only.
  `BannedSymbols.Cbor.txt` bans `System.Formats.Cbor` and is added to
  every project except `Lumoin.Veritas.Cbor` and `*.Tests` projects (so
  test projects can reference BCL CBOR for differential tests). Both
  files are listed in the slnx Solution Items.
- `Directory.Packages.props` adds `System.Formats.Cbor` under the
  Serialization label and updates `ValidateSerializationProjectReferences`
  to additionally allow `Lumoin.Veritas.CborLd` to reference
  `Lumoin.Veritas.Cbor`.

## Phase 3 — Lumoin.Veritas.LinkedData: substantially done

Done:

- `src/Lumoin.Veritas.LinkedData/` project created with project file,
  `README.md`.
- **Active-context types migrated:** `TermDefinition.cs` and
  `JsonLdContext.cs` moved from JsonLd to LinkedData. The same names
  preserved for naming continuity with the JSON-LD spec; the structure
  is format-neutral.
- **Marker interface:** `ILinkedDataDocumentNode` added to LinkedData
  (with a `[SuppressMessage(CA1040)]` justification — the empty marker
  is the design intent). `JsonLdNode` (the struct in JsonLd) implements
  it. `TermDefinition.LocalContext` is typed
  `ILinkedDataDocumentNode?`; format adapters cast to `JsonLdNode` at
  use sites.
- **`ContextResolverDelegate`** moved from JsonLd to LinkedData with a
  format-neutral XML doc.
- **Consumer updates:** `ContextProcessor.cs` (typing change at line
  613, `localContextValue` is now `ILinkedDataDocumentNode?`),
  `JsonLdExpander.cs` (pattern matches `is JsonLdNode scopedContext`),
  `IriUtils.cs`, `JsonLdNode.cs`, `JsonLdParserDelegate.cs`, plus the
  two test files all import `Lumoin.Veritas.LinkedData`.
- `Lumoin.Veritas.JsonLd.csproj` references LinkedData; tests csproj
  references LinkedData; slnx lists LinkedData under `/src/`.

### Phase 3 follow-ups (deferred to be CborLd-driven):

- [ ] **`ContextProcessor` parameterisation.** The 954-line algorithm
  operates on `JsonLdNode` and the `JsonLd.Document` delegates; CBOR-LD
  will need an analogous processor. Wait for CborLd to prove out the
  shape — either parameterise over the document node type, or split
  format-agnostic context-processing into LinkedData with a clean
  algorithm-shape boundary. Avoid speculative generalisation until a
  second consumer exists.
- [ ] **`JsonLdContext.Clone()` widened from `internal` to `public`**
  during the move. If the previous internal scope was load-bearing,
  reinstate via an interface or InternalsVisibleTo.

### Phase 3 known not-to-touch items:

- `JsonLdNode` and the `JsonLd/Document/` delegate set stay in JsonLd
  per the handoff. The marker interface in LinkedData is the bridge.
- `JsonLdProcessingException` stays in JsonLd. CBOR-LD will define its
  own `CborLdProcessingException`.

### IriUtils split (done in this session):

- Pure helpers — `IsKeyword`, `IsKeywordLike`, `IsAbsoluteIri`,
  `IsRelativeIri`, `ResolveIri` — moved to
  `Lumoin.Veritas.LinkedData/IriUtils.cs` as `public static`.
- `ExpandIri` is now an instance method on `JsonLdContext` (in
  LinkedData). All ~12 call sites in `ContextProcessor.cs` and
  `JsonLdExpander.cs` updated to the instance form.

## Phase 4 — Lumoin.Veritas.CborLd: scaffolding done

Done (project + surface types, no encoder/decoder yet):

- `src/Lumoin.Veritas.CborLd/` project with `Lumoin.Veritas.CborLd.csproj`
  (root namespace `Lumoin.Veritas.Cbor.CborLd`, references Core + Cbor +
  LinkedData) and a `README.md` documenting current state.
- `CborLdProfile` enum — `Default` and `Deterministic`.
- `CborLdKeywordCodec` and `CborLdTermCodec` — `readonly record struct`
  data carriers.
- `CborLdRegistryEntry` — POCO carrying registry-entry id + keyword and
  term codec dictionaries. Static `Passthrough` instance for id 0.
- `CborLdRegistry` — in-memory Dictionary-backed registry that always
  includes the passthrough entry; `TryGet` plus an `AsDelegate()`
  adapter producing a `LoadCborLdRegistryEntryDelegate`.
- `LoadCborLdRegistryEntryDelegate` — the resolver-style boundary.
- `CborLdProcessingException`.
- Tests at `test/Lumoin.Veritas.Tests/CborLd/CborLdRegistryTests.cs`
  (8 tests covering the surface).
- Slnx and tests csproj include the project.

### Phase 4 deferred items (encoder/decoder, gated on node-abstraction decision):

- [ ] **Decide node abstraction.** The encoder needs to walk an input
  Linked Data document and emit CBOR; the decoder needs to produce a
  document tree. CborLd cannot reference `Lumoin.Veritas.JsonLd`, so it
  can't directly accept or produce `JsonLdNode`. Three options:
    1. **Extend `ILinkedDataDocumentNode`** with traversal methods
       (GetKind, TryGetProperty, EnumerateArray, GetStringValue, etc.).
       `JsonLdNode` implements them by delegating to its existing
       navigator. CborLd builds against the interface. This is the
       cleanest architectural move and the one PROGRESS.md flagged as
       likely.
    2. **JsonLd-side adapter** that converts JsonLdNode to/from a
       generic shape CborLd consumes (e.g. nested `object` tree). CborLd
       stays JsonLd-agnostic; an adapter in `Lumoin.Veritas.JsonLd` or
       `Lumoin.Veritas.Json` bridges the two. More code but no widening
       of the LinkedData surface.
    3. **CborLd defines its own node type** (`CborLdNode`) and the user
       supplies a converter. Most flexible, least sharing.
- [ ] **`CborLdEncoder.Encode(...)`** — walks the input via the chosen
  abstraction, emits CBOR via the project's `CborWriter`. Uses an
  explicit `Stack<ConversionFrame>` per the handoff's no-recursion rule.
  Outer tag follows the current W3C CBOR-LD draft; document the chosen
  tag value in the encoder's XML doc.
- [ ] **`CborLdDecoder.Decode(...)`** — reads CBOR via `CborReader`,
  reconstructs the document tree.
- [ ] **Built-in value codecs** (`Url`, `Date`, `DateTime`,
  `VocabTerm`, `Base64Url` per handoff Section 4 file layout).
- [ ] **Test classes** from the handoff: encoder/decoder passthrough,
  compression, deterministic profile, active-context, registry behaviour,
  value codecs, spec vectors. The handoff lists nine; the registry one
  is already in place.
- [ ] **10,000-iter round-trip property test** per the gate.

### Phase 4 infrastructure already in place:

- Banned-API analyzer: `Lumoin.Veritas.CborLd` is a library project and
  bans `System.Formats.Cbor` (must consume project's CBOR layer).
- `Directory.Packages.props` validator whitelist already permits
  `Lumoin.Veritas.CborLd` to reference `Lumoin.Veritas.Cbor`.

## Suggested next session opening

Read `PROGRESS.md`. Phase 4 scaffolding is in place; the encoder/decoder
is the next strategic unit of work. First decision: pick a path for the
node abstraction (the three options under Phase 4 deferred items). The
extension of `ILinkedDataDocumentNode` is the cleanest and is what
PROGRESS.md recommends; getting the user's vote before implementing
saves rework.

Once the abstraction is settled:

1. **CborLd encoder** as one sub-batch (walk + emit, passthrough mode).
2. **CborLd decoder** as a second sub-batch (read + build tree).
3. **Value codecs** as the third (Date, DateTime, Url, VocabTerm,
   Base64Url).
4. **Compression-driven registry entries** as the fourth (term/keyword
   substitution).
5. **10,000-iter round-trip property test** to close the gate.

Phase 2 hardening (float canonicalization parity with BCL) is the
alternative path if CborLd is paused.
