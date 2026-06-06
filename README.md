<img style="display: block; margin-inline-start: auto; margin-inline-end: auto;" src="resources/lumoin-veritas-github-logo.svg" width="800" height="400" alt="Veritas project logo: A shield in green hues with a rounded top tapering downwards in a 'V' shape. In the center a stylized 'V' formed from three converging triples evokes an RDF graph, with one edge elongating across the left corner of the shield to the white background.">

# Lumoin.Veritas

**An integrated .NET stack for RDF: graph storage, canonicalization, JSON-LD processing, SHACL validation, OWL reasoning, and SKOS vocabularies.**

![Main build workflow](https://github.com/Lumoin/Lumoin.Veritas/actions/workflows/main.yml/badge.svg)

---

## What is Veritas?

Veritas is a comprehensive .NET library for working with RDF graphs end-to-end: parsing, indexing, querying, transforming, validating, and serializing. The library implements the W3C [RDF 1.2 Concepts](https://www.w3.org/TR/rdf12-concepts/), [RDF Dataset Canonicalization](https://www.w3.org/TR/rdf-canon/), [JSON-LD 1.1](https://www.w3.org/TR/json-ld11/), [SHACL 1.2 Core](https://www.w3.org/TR/shacl12-core/), [OWL 2](https://www.w3.org/TR/owl2-overview/), and [SKOS](https://www.w3.org/TR/skos-reference/) on a shared substrate of encoded triples, term dictionaries, and pool-backed memory.

The core value proposition is graph data that can be parsed once, indexed compactly, queried over property paths, validated against shapes, reasoned about with RDFS and OWL 2 RL, canonicalized deterministically for signing, and serialized into multiple syntaxes — all without copying terms across encoding boundaries and without locking the application into a particular storage backend.

Veritas is designed to be a peer of credential, identity, and cryptography stacks rather than a dependency of any one of them. Storage, hashing, and term interning are wired in through delegates; the library does not own the choice of disk format, hash function, or string interner.

## Libraries

| Library | Purpose | NuGet |
|---------|---------|:-----:|
| **Lumoin.Veritas.Core** | Encoded terms, term dictionary, UTF-8 string pool, hypertrie indexing, in-memory graph store | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Core.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Core/) |
| **Lumoin.Veritas.Rdf** | Property path evaluation, transitive closure, RDFS inference, RDF collection traversal, graph fold/unfold primitives over the encoded triple store. Shared substrate for SHACL, OWL, SPARQL, and SKOS | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Rdf.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Rdf/) |
| **Lumoin.Veritas.Canonicalization** | RDF Dataset Canonicalization for deterministic graph hashing | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Canonicalization.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Canonicalization/) |
| **Lumoin.Veritas.NQuads** | N-Quads reader and writer | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.NQuads.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.NQuads/) |
| **Lumoin.Veritas.JsonLd** | JSON-LD context processing, term definitions, and document expansion | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonLd.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonLd/) |
| **Lumoin.Veritas.Owl** | OWL 2 RL forward-chaining reasoner: SubClassOf, SubPropertyOf, TransitiveProperty, SymmetricProperty, InverseOf, PropertyChain, EquivalentClass. TBox loads in memory; ABox streams through | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Owl.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Owl/) |
| **Lumoin.Veritas.Shacl** | SHACL 1.2 Core validation engine producing W3C-conformant validation reports, including RDF 1.2 triple term support via `sh:reifierShape` | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Shacl.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Shacl/) |
| **Lumoin.Veritas.Skos** | SKOS concept scheme and vocabulary support | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Skos.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Skos/) |
| **Lumoin.Veritas.Json** | JSON serialization boundary. Owns the `System.Text.Json` dependency and provides AOT-compatible serialization for Veritas types that cross the JSON boundary | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Json.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Json/) |

## Key capabilities

**Encoded triple store with term interning.** Triples and quads are stored as encoded values rather than chains of strings. UTF-8 strings are interned through `Utf8StringPool`; terms are assigned dictionary IDs through `TermDictionary`. Hypertrie indexing supports efficient lookup across multiple access patterns without duplicating the underlying data.

**Graph computation primitives.** Property path evaluation, transitive closure, and RDFS inference are exposed alongside generic graph fold, unfold, cata, and hylo primitives. SHACL, OWL, SKOS, and any future SPARQL surface build on these primitives rather than reimplementing graph traversal.

**Deterministic canonicalization.** RDF Dataset Canonicalization produces a deterministic ordering of quads suitable for hashing and signing. The hash function is exposed as a delegate so an application can choose SHA-256, BLAKE2, or any other digest without changing the canonicalizer.

**JSON-LD processing.** Context resolution, term definition handling, and document expansion follow the JSON-LD 1.1 algorithm. The actual JSON parsing lives in `Lumoin.Veritas.Json`; the processing model itself is JSON-format-agnostic and can be driven from any source.

**SHACL 1.2 Core validation.** All Core constraint components, property paths, and target declarations as defined in the W3C SHACL 1.2 Core specification, with RDF 1.2 triple-term support. Validation reports are W3C-conformant.

**OWL 2 RL reasoning.** Forward-chaining inference rules fire against the dataset to fixpoint. The TBox loads in memory; the ABox streams through, allowing reasoning over datasets larger than RAM.

**Memory-safe graph material.** UTF-8 strings, encoded terms, and dictionary IDs are wrapped in pool-backed types. There are no naked byte arrays or stringly-typed dictionary IDs in public APIs. The `VeritasMemoryPool` and `Utf8StringPool` provide exact-size allocation suitable for the graph hot path.

**Pluggable storage and hashing.** Storage and hashing are wired through delegates rather than tied to any specific persistence layer or digest implementation. The same processing code runs against an in-memory store during testing and a disk-backed store in production without changes at the call site.

## Architecture principles

The library follows data-oriented programming principles where code is separate from immutable data, generic data structures are favored, and general-purpose functions are implemented as static extensions over those structures. Domain types contain encoded graph material without serialization artifacts; encoding lives at boundaries in `Lumoin.Veritas.Json` and (when introduced) `Lumoin.Veritas.Cbor`.

Storage, hashing, and term interning use delegate-based extension points rather than direct implementations. This allows the same high-level API to work against an in-memory triple store, a memory-mapped file, or an external store without changing calling code. `StorageDelegates` is the canonical pattern.

The vocabulary projects (`Owl`, `Shacl`, `Skos`) and format projects (`NQuads`, `JsonLd`) build on top of `Core` and `Rdf`. They expose their semantics through delegates and well-known vocabulary classes (`RdfVocabulary`, `RdfsVocabularyIds`, `ShaclVocabulary`, and so on) rather than through hidden global state.

## Specifications implemented

Coming. See tests in the meanwhile.

## Getting started

Install the packages relevant to your use case:

```bash
# Core graph storage and indexing
dotnet add package Lumoin.Veritas.Core

# Graph computation primitives
dotnet add package Lumoin.Veritas.Rdf

# RDF Dataset Canonicalization
dotnet add package Lumoin.Veritas.Canonicalization

# N-Quads parsing and serialization
dotnet add package Lumoin.Veritas.NQuads

# JSON-LD processing (also requires Lumoin.Veritas.Json for actual JSON parsing)
dotnet add package Lumoin.Veritas.JsonLd
dotnet add package Lumoin.Veritas.Json

# SHACL validation
dotnet add package Lumoin.Veritas.Shacl

# OWL 2 RL reasoning
dotnet add package Lumoin.Veritas.Owl

# SKOS concept schemes
dotnet add package Lumoin.Veritas.Skos
```

## Development

The codebase runs on Windows, Linux, and macOS.

Press **.** on the repository page to open the codebase in VS Code web editor for quick exploration.

## Vulnerability disclosure

Please report suspected security vulnerabilities privately through [GitHub security advisories](https://github.com/Lumoin/Lumoin.Veritas/security/advisories), not through public issues.

## Contributing

Open issues for bugs, suggestions, or improvements, or create pull requests. Especially welcome:

- Tests using the W3C test suites for RDF Dataset Canonicalization, JSON-LD, N-Quads, and SHACL for cross-checking.
- Expanded OWL 2 RL coverage and additional OWL 2 profiles.
- SPARQL query support on top of the graph computation layer.
- Additional RDF syntaxes (Turtle, TriG, RDF/XML).
- Improved indexing strategies and storage backends.

## License

See the LICENSE file for details.

---

> **Note:** This is an early version under active development. APIs may change between versions.
