<img style="display: block; margin-inline-start: auto; margin-inline-end: auto;" src="resources/lumoin-veritas-github-logo.svg" width="800" height="400" alt="Veritas project logo: A shield in green hues with a rounded top tapering downwards in a 'V' shape. In the center a stylized 'V' formed from three converging triples evokes an RDF graph, with one edge elongating across the left corner of the shield to the white background.">

# Lumoin.Veritas

**An integrated .NET stack for RDF and linked data: graph storage, SPARQL, canonicalization, JSON-LD and CBOR-LD, SHACL validation, OWL reasoning, and the serialization formats to carry them.**

![Main build workflow](https://github.com/Lumoin/Lumoin.Veritas/actions/workflows/main.yml/badge.svg)

---

## What is Veritas?

Veritas is a comprehensive .NET library for working with RDF graphs end-to-end: parsing, indexing, querying, transforming, validating, and serializing. The library implements the W3C [RDF 1.2 Concepts](https://www.w3.org/TR/rdf12-concepts/), [SPARQL 1.2 Query](https://www.w3.org/TR/sparql12-query/), [RDF Dataset Canonicalization](https://www.w3.org/TR/rdf-canon/), [JSON-LD 1.1](https://www.w3.org/TR/json-ld11/), [SHACL 1.2 Core](https://www.w3.org/TR/shacl12-core/), [OWL 2](https://www.w3.org/TR/owl2-overview/), and [SKOS](https://www.w3.org/TR/skos-reference/) on a shared substrate of encoded triples, term dictionaries, and pool-backed memory.

The core value proposition is graph data that can be parsed once, indexed compactly, queried with SPARQL, validated against shapes, reasoned about with RDFS and OWL 2 RL, canonicalized deterministically for signing, and serialized into multiple syntaxes — all without copying terms across encoding boundaries and without locking the application into a particular storage backend.

Veritas is designed to be a peer of credential, identity, and cryptography stacks rather than a dependency of any one of them. Storage, hashing, and term interning are wired in through delegates; the library does not own the choice of disk format, hash function, or string interner.

## Libraries

| Library | Purpose | NuGet |
|---------|---------|:-----:|
| **Lumoin.Veritas.Core** | Encoded terms, term dictionary, UTF-8 string pool, hypertrie indexing, in-memory graph store | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Core.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Core/) |
| **Lumoin.Veritas.Rdf** | Property path evaluation, transitive closure, RDFS inference, RDF collection traversal, graph fold/unfold primitives over the encoded triple store. Shared substrate for SPARQL, SHACL, OWL, and SKOS | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Rdf.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Rdf/) |
| **Lumoin.Veritas.Sparql** | SPARQL 1.2 query engine: lexer, parser, algebra translation, built-in function library, aggregations, in-process executor | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Sparql.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Sparql/) |
| **Lumoin.Veritas.Canonicalization** | RDF Dataset Canonicalization for deterministic graph hashing | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Canonicalization.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Canonicalization/) |
| **Lumoin.Veritas.Shacl** | SHACL 1.2 Core validation engine producing W3C-conformant validation reports, including SHACL-SPARQL constraints and RDF 1.2 triple term support via `sh:reifierShape` | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Shacl.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Shacl/) |
| **Lumoin.Veritas.Owl** | OWL 2 RL forward-chaining reasoner, OWL structural model with axiom annotations, functional-syntax reader, OWL DL checking, and EL classification. TBox loads in memory; ABox streams through | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Owl.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Owl/) |
| **Lumoin.Veritas.Skos** | SKOS concept scheme and vocabulary primitives | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Skos.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Skos/) |
| **Lumoin.Veritas.NQuads** | N-Quads reader and writer | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.NQuads.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.NQuads/) |
| **Lumoin.Veritas.Turtle** | Turtle reader and writer with RDF 1.2 triple term support | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Turtle.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Turtle/) |
| **Lumoin.Veritas.JsonLd** | JSON-LD 1.1 context processing, term definitions, document expansion, and compaction | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonLd.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonLd/) |
| **Lumoin.Veritas.CborLd** | CBOR-LD encoder and decoder with active-context-driven term compression and a deterministic profile | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.CborLd.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.CborLd/) |
| **Lumoin.Veritas.Cbor** | RFC 8949 CBOR codec with pool-aware buffers and DRISL/dCBOR profile support | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Cbor.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Cbor/) |
| **Lumoin.Veritas.Cid** | DASL Content Identifier (CID) type, parsing, and hash-based construction | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Cid.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Cid/) |
| **Lumoin.Veritas.LinkedData** | Format-agnostic linked-data primitives shared by JSON-LD and CBOR-LD | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.LinkedData.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.LinkedData/) |
| **Lumoin.Veritas.Json** | Backend-agnostic JSON value model and parse seam; carries no `System.Text.Json` dependency | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Json.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Json/) |
| **Lumoin.Veritas.Json.Stj** | `System.Text.Json` implementation of the JSON parse seam | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Json.Stj.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Json.Stj/) |
| **Lumoin.Veritas.JsonPointer** | RFC 6901 JSON Pointer value type: parse, compose, escape, compare, URI fragments | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonPointer.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonPointer/) |
| **Lumoin.Veritas.JsonSchema** | JSON Schema draft 2020-12 validator over the backend-agnostic JSON value model | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonSchema.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonSchema/) |
| **Lumoin.Veritas.Rdf.Json** | SPARQL 1.1 Query Results JSON Format reader and writer, RDF term and quad JSON converters | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Rdf.Json.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Rdf.Json/) |
| **Lumoin.Veritas.Xml** | SPARQL Query Results XML Format and `rdf:XMLLiteral` canonicalization | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Xml.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Xml/) |
| **Lumoin.Veritas.Cli** | Command-line tool, MCP server, and SPARQL 1.1 Protocol HTTP endpoint; ships as native-AOT packages per platform with a framework-dependent fallback | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Cli.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Cli/) |

## Key capabilities

**Encoded triple store with term interning.** Triples and quads are stored as encoded values rather than chains of strings. UTF-8 strings are interned through `Utf8StringPool`; terms are assigned dictionary IDs through `TermDictionary`. Hypertrie indexing supports efficient lookup across multiple access patterns without duplicating the underlying data.

**SPARQL 1.2 querying.** A full query pipeline — lexer, parser, algebra translation, built-in functions, aggregations — executing in-process against the encoded store. The same engine evaluates SHACL-SPARQL constraints and backs the CLI's SPARQL Protocol endpoint.

**Graph computation primitives.** Property path evaluation, transitive closure, and RDFS inference are exposed alongside generic graph fold, unfold, cata, and hylo primitives. SPARQL, SHACL, OWL, and SKOS build on these primitives rather than reimplementing graph traversal.

**Deterministic canonicalization.** RDF Dataset Canonicalization produces a deterministic ordering of quads suitable for hashing and signing. The hash function is exposed as a delegate so an application can choose SHA-256, BLAKE2, or any other digest without changing the canonicalizer.

**Linked-data processing in JSON and CBOR.** JSON-LD 1.1 context resolution, expansion, and compaction; CBOR-LD encoding and decoding with a deterministic profile for byte-stable documents. The processing models are format-agnostic — JSON parsing is a seam (`Lumoin.Veritas.Json`) with a `System.Text.Json` implementation supplied separately (`Lumoin.Veritas.Json.Stj`).

**SHACL 1.2 Core validation.** Core constraint components, property paths, and target declarations as defined in the W3C SHACL 1.2 Core specification, plus SHACL-SPARQL constraints and RDF 1.2 triple-term support. Validation reports are W3C-conformant.

**OWL 2 reasoning and structure.** A forward-chaining OWL 2 RL reasoner fires against the dataset to fixpoint — the TBox loads in memory, the ABox streams through, allowing reasoning over datasets larger than RAM. Alongside it: an OWL structural model with axiom annotations, a functional-syntax reader, OWL DL checking, and EL classification.

**Serialization syntaxes.** N-Quads and Turtle readers and writers (both RDF 1.2-aware), SPARQL query results in JSON and XML, an RFC 8949 CBOR codec with DRISL/dCBOR profiles, and DASL Content Identifiers for hash-addressed content.

**Memory-safe graph material.** UTF-8 strings, encoded terms, and dictionary IDs are wrapped in pool-backed types. There are no naked byte arrays or stringly-typed dictionary IDs in public APIs. The `VeritasMemoryPool` and `Utf8StringPool` provide exact-size allocation suitable for the graph hot path.

**Pluggable storage and hashing.** Storage and hashing are wired through delegates rather than tied to any specific persistence layer or digest implementation. The same processing code runs against an in-memory store during testing and a disk-backed store in production without changes at the call site.

## Architecture principles

The library follows data-oriented programming principles where code is separate from immutable data, generic data structures are favored, and general-purpose functions are implemented as static extensions over those structures. Domain types contain encoded graph material without serialization artifacts; encoding lives at boundaries in the format projects (`Json`, `Json.Stj`, `Cbor`, `NQuads`, `Turtle`, `Xml`).

Storage, hashing, and term interning use delegate-based extension points rather than direct implementations. This allows the same high-level API to work against an in-memory triple store, a memory-mapped file, or an external store without changing calling code. `StorageDelegates` is the canonical pattern.

The vocabulary projects (`Owl`, `Shacl`, `Skos`) and format projects (`NQuads`, `Turtle`, `JsonLd`, `CborLd`) build on top of `Core` and `Rdf`. They expose their semantics through delegates and well-known vocabulary classes (`RdfVocabulary`, `RdfsVocabularyIds`, `ShaclVocabulary`, and so on) rather than through hidden global state.

## Specifications implemented

- W3C RDF 1.2 Concepts; N-Quads and Turtle syntaxes with triple terms.
- W3C SPARQL 1.2 Query; SPARQL 1.1 Protocol; SPARQL Query Results in JSON and XML.
- W3C RDF Dataset Canonicalization (RDFC-1.0).
- W3C JSON-LD 1.1 (expansion and compaction); CBOR-LD.
- W3C SHACL 1.2 Core, including SHACL-SPARQL constraints.
- W3C OWL 2 (RL profile reasoning, structural specification, functional syntax).
- W3C SKOS concept schemes.
- RFC 8949 CBOR with dCBOR/DRISL profiles; RFC 6901 JSON Pointer; JSON Schema draft 2020-12.

Conformance is exercised against the relevant W3C test suites in the test projects.

## Getting started

Install the packages relevant to your use case:

```bash
# Core graph storage and indexing
dotnet add package Lumoin.Veritas.Core

# Graph computation primitives
dotnet add package Lumoin.Veritas.Rdf

# SPARQL querying
dotnet add package Lumoin.Veritas.Sparql

# RDF Dataset Canonicalization
dotnet add package Lumoin.Veritas.Canonicalization

# SHACL validation
dotnet add package Lumoin.Veritas.Shacl

# OWL 2 reasoning
dotnet add package Lumoin.Veritas.Owl

# Syntaxes and formats follow the same naming:
# Lumoin.Veritas.NQuads, .Turtle, .JsonLd (+ .Json.Stj for JSON parsing),
# .CborLd, .Cbor, .Cid, .JsonPointer, .JsonSchema, .Rdf.Json, .Xml, .Skos
```

The command-line tool — including an MCP server and a SPARQL 1.1 Protocol HTTP endpoint — installs as a dotnet tool:

```bash
dotnet tool install --global Lumoin.Veritas.Cli
```

## Development

The codebase runs on Windows, Linux, and macOS.

Press **.** on the repository page to open the codebase in VS Code web editor for quick exploration.

## Vulnerability disclosure

Please report suspected security vulnerabilities privately through [GitHub security advisories](https://github.com/Lumoin/Lumoin.Veritas/security/advisories), not through public issues.

## Contributing

Open issues for bugs, suggestions, or improvements, or create pull requests. Especially welcome:

- Tests using the W3C test suites for RDF Dataset Canonicalization, JSON-LD, N-Quads, Turtle, SPARQL, and SHACL for cross-checking.
- Expanded OWL 2 profile coverage.
- Additional RDF syntaxes (TriG, RDF/XML).
- Improved indexing strategies and storage backends.

## License

See the LICENSE file for details.

---

> **Note:** This is an early version under active development. APIs may change between versions.
