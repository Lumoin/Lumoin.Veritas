<img style="display: block; margin-inline-start: auto; margin-inline-end: auto;" src="resources/lumoin-veritas-github-logo.svg" width="800" height="400" alt="Veritas project logo: A circular emblem in green hues, concentric brush-stroke rings spiraling toward a deep-red center evoking layers of a graph converging on a shared truth, followed by the wordmark 'Veritas'.">

# Lumoin.Veritas

**An integrated .NET stack for RDF and linked data, and an in-process and WAN-ready graph engine: graph storage, SPARQL, GeoSPARQL, canonicalization, JSON-LD and CBOR-LD, SHACL validation, OWL reasoning, many-worlds branching for what-if evaluation, replication, and the serialization formats to carry them. Try it in your browser: [Veritas Studio](https://lumoin.github.io/Lumoin.Veritas/).**

![Main build workflow](https://github.com/Lumoin/Lumoin.Veritas/actions/workflows/main.yml/badge.svg)

---

## What is Veritas?

Veritas is a comprehensive .NET library for working with RDF graphs end-to-end: parsing, indexing, querying, transforming, validating, and serializing. The library implements the W3C [RDF 1.2 Concepts](https://www.w3.org/TR/rdf12-concepts/), [SPARQL 1.2 Query](https://www.w3.org/TR/sparql12-query/), [RDF Dataset Canonicalization](https://www.w3.org/TR/rdf-canon/), [JSON-LD 1.1](https://www.w3.org/TR/json-ld11/), [SHACL 1.2 Core](https://www.w3.org/TR/shacl12-core/), [OWL 2](https://www.w3.org/TR/owl2-overview/), [SKOS](https://www.w3.org/TR/skos-reference/), and [OGC GeoSPARQL](https://www.ogc.org/standards/geosparql/).

Try it without installing anything: the hosted [Veritas Studio](https://lumoin.github.io/Lumoin.Veritas/) runs the engine in your browser as WebAssembly — queries, validation, and reasoning execute on your machine and nothing leaves it. Many-worlds branching runs there too: the worlds strip forks the loaded dataset, commits a hypothetical into the fork, and diffs the consequence against the untouched primary.

The aim is a state-of-the-art system that is correct and fast: checked against the published conformance suites, and measured — every kept execution route is benchmarked against its alternatives. The engine runs in-process inside a .NET application, fully in-browser as WebAssembly, and as a server behind the CLI's SPARQL 1.1 Protocol endpoint, and it replicates across WAN distances. In short:

- Parse once: N-Quads, Turtle, TriG, RDF/XML, JSON-LD, and CBOR-LD readers and writers over one encoded-triple substrate.
- Index compactly: one system of record with derived presentations beside it — a hypertrie for point lookups and cyclic joins, a succinct columnar view (Elias-Fano and prefixed-delta columns) for batched scans — chosen per query shape and rebuildable at will.
- Query with SPARQL 1.2 and GeoSPARQL; validate with SHACL 1.2 Core; reason with RDFS, OWL 2 RL, and EL classification, with verdicts that say what was decided and what was not.
- Canonicalize deterministically (RDFC-1.0) for hashing and signing.
- Persist as a chain of immutable, checksummed generations committed by an atomic pointer swap, with a live self-healing scrub and a repair ladder of re-derivation, parity, and peers.
- Branch: fork a dataset into independently evolving worlds that share all unchanged content — apply a hypothetical in one, query and diff the consequence in isolation, and drop or keep it.
- Replicate: rateless set reconciliation converges replicas across wide-area links, and a consensus-coordinated metadata plane settles replica identities and lineage baselines; the CLI's `replicate` command hosts both.
- Extend: value-layer datatypes and extension functions register through the same points the GeoSPARQL layer uses, with the engine unchanged when nothing is registered.

Planned extensions, each described under [On the roadmap](#on-the-roadmap): graph exploration at billions-of-nodes scale; encryption at rest and hardware-bound data; identity and agent-driven operation; AI tooling for the Studio and the server; deontic, holonic, and mereological layers; zero-knowledge proofs.

For the system architecture — the shared encoded-triple substrate, the query / mutation / persistence / integrity-repair design, and the replication and network-governance design — see [ARCHITECTURE.md](ARCHITECTURE.md).

## Libraries

| Library | Purpose | NuGet |
|---------|---------|:-----:|
| **Lumoin.Veritas.Core** | Encoded terms, term dictionary, UTF-8 string pool, hypertrie indexing, in-memory graph store | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Core.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Core/) |
| **Lumoin.Veritas.Rdf** | Property path evaluation, transitive closure, RDFS inference, RDF collection traversal, graph fold/unfold primitives over the encoded triple store. Shared substrate for SPARQL, SHACL, OWL, and SKOS | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Rdf.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Rdf/) |
| **Lumoin.Veritas.Sparql** | SPARQL 1.2 query engine: lexer, parser, algebra translation, built-in function library, aggregations, in-process executor | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Sparql.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Sparql/) |
| **Lumoin.Veritas.Canonicalization** | RDF Dataset Canonicalization for deterministic graph hashing | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Canonicalization.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Canonicalization/) |
| **Lumoin.Veritas.Shacl** | SHACL 1.2 Core validation engine producing W3C-conformant validation reports, including SHACL-SPARQL constraints and RDF 1.2 triple term support via `sh:reifierShape` | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Shacl.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Shacl/) |
| **Lumoin.Veritas.Owl** | OWL 2 RL forward-chaining reasoner, OWL structural model with axiom annotations, functional-syntax reader, OWL DL checking, and EL classification | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Owl.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Owl/) |
| **Lumoin.Veritas.Skos** | SKOS concept scheme and vocabulary primitives | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Skos.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Skos/) |
| **Lumoin.Veritas.Geo** | GeoSPARQL, Simple Features, and GeoSPARQL function vocabularies as UTF-8 IRI constants; well-known-text literal decomposition into CRS IRI and geometry body; lexical recognition for all six geometry serialization datatypes and a column-backed planar geometry model with exact-predicate topology (DE-9IM matrix, the named topological predicates, boolean overlay, buffer, convex hull); GML, GeoJSON, and KML geometry readers and writers over that model with refusal by value; the `geo:wktLiteral` value-layer datatype definition, the `geof:` extension-function catalog, the topological-relations query-rewrite entry, and the RCC8 composition calculus with its singleton-cell derivation closure | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Geo.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Geo/) |
| **Lumoin.Veritas.NQuads** | N-Quads reader and writer | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.NQuads.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.NQuads/) |
| **Lumoin.Veritas.Turtle** | Turtle and TriG reader and writer with RDF 1.2 triple term support | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Turtle.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Turtle/) |
| **Lumoin.Veritas.JsonLd** | JSON-LD 1.1 context processing, term definitions, document expansion, and compaction | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonLd.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonLd/) |
| **Lumoin.Veritas.CborLd** | CBOR-LD encoder and decoder with active-context-driven term compression and a deterministic profile | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.CborLd.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.CborLd/) |
| **Lumoin.Veritas.Cbor** | RFC 8949 CBOR codec with pool-aware buffers and DRISL/dCBOR profile support | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Cbor.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Cbor/) |
| **Lumoin.Veritas.Cid** | DASL Content Identifier (CID) type, parsing, and hash-based construction | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Cid.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Cid/) |
| **Lumoin.Veritas.LinkedData** | Format-agnostic linked-data primitives shared by JSON-LD and CBOR-LD | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.LinkedData.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.LinkedData/) |
| **Lumoin.Veritas.Json** | Backend-agnostic JSON value model and parsing interface; carries no `System.Text.Json` dependency | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Json.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Json/) |
| **Lumoin.Veritas.Json.Stj** | `System.Text.Json` implementation of the JSON parsing interface | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Json.Stj.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Json.Stj/) |
| **Lumoin.Veritas.Jsonata** | JSONata query and transformation engine over the backend-agnostic JSON value model | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Jsonata.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Jsonata/) |
| **Lumoin.Veritas.JsonPointer** | RFC 6901 JSON Pointer value type: parse, compose, escape, compare, URI fragments | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonPointer.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonPointer/) |
| **Lumoin.Veritas.JsonSchema** | JSON Schema draft 2020-12 validator over the backend-agnostic JSON value model | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.JsonSchema.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.JsonSchema/) |
| **Lumoin.Veritas.Rdf.Json** | SPARQL 1.1 Query Results JSON Format reader and writer, RDF term and quad JSON converters | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Rdf.Json.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Rdf.Json/) |
| **Lumoin.Veritas.Xml** | RDF/XML reader, SPARQL Query Results XML Format, and `rdf:XMLLiteral` canonicalization | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Xml.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Xml/) |
| **Lumoin.Veritas.Cli** | Command-line tool, MCP server, and SPARQL 1.1 Protocol HTTP endpoint; ships as native-AOT packages per platform with a framework-dependent fallback | [![NuGet](https://img.shields.io/nuget/v/Lumoin.Veritas.Cli.svg?style=flat)](https://www.nuget.org/packages/Lumoin.Veritas.Cli/) |

## Key capabilities

**Encoded triple store with term interning.** Triples and quads are stored as encoded values rather than chains of strings. UTF-8 strings are interned through `Utf8StringPool`; terms are assigned dictionary IDs through `TermDictionary`. Hypertrie indexing supports efficient lookup across multiple access patterns without duplicating the underlying data.

**SPARQL 1.2 querying.** A full query pipeline — lexer, parser, algebra translation, built-in functions, aggregations — executing in-process against the encoded store and answering all four query forms (SELECT, ASK, CONSTRUCT, DESCRIBE). The same engine evaluates SHACL-SPARQL constraints and backs the CLI's SPARQL 1.1 Protocol endpoint: the three query-submission forms, the protocol dataset parameters over the loaded named graphs, content negotiation across the XML/JSON/CSV/TSV results formats and the N-Triples/Turtle graph serializations, the protocol fault codes, and a service description generated from the live extension registries.

**Graph computation primitives.** Property path evaluation, transitive closure, and RDFS inference are exposed alongside generic graph fold, unfold, cata, and hylo primitives. SPARQL, SHACL, OWL, and SKOS build on these primitives rather than reimplementing graph traversal.

**Deterministic canonicalization.** RDF Dataset Canonicalization produces a deterministic ordering of quads suitable for hashing and signing. The hash function is exposed as a delegate so an application can choose SHA-256, BLAKE2, or any other digest without changing the canonicalizer.

**Linked-data processing in JSON and CBOR.** JSON-LD 1.1 context resolution, expansion, and compaction; CBOR-LD encoding and decoding with a deterministic profile for byte-stable documents. The processing models are format-agnostic — JSON parsing is a pluggable interface (`Lumoin.Veritas.Json`) with a `System.Text.Json` implementation supplied separately (`Lumoin.Veritas.Json.Stj`).

**SHACL 1.2 Core validation.** Core constraint components, property paths, and target declarations as defined in the W3C SHACL 1.2 Core specification, plus SHACL-SPARQL constraints and RDF 1.2 triple-term support. Validation reports are W3C-conformant.

**OWL 2 reasoning and structure.** A forward-chaining OWL 2 RL reasoner fires against the dataset to fixpoint. Alongside it: an OWL structural model with axiom annotations, a functional-syntax reader, OWL DL checking, and EL classification. Reasoning answers are epistemically explicit — the engine can tell the difference between *cannot decide* and *not finished*, and says which one happened. A decision reports whether the verdict covers the module whole (`Decided`), holds only over the constructs the deciding calculus interprets — with every uninterpreted construct named on the verdict, never silently dropped (`DecidedFragmentRelative`) — or ran out of budget before a verdict was reached (`AbstainedBudget`). An inconsistency found in the interpreted fragment condemns the module whole regardless of any remainder, a consistency claim is scoped to what was read, and an answer is never a guess: where the engine cannot interpret a construct it either delegates the module to a tier that can, or names exactly what it left undecided.

**Value-layer datatype extension.** A host registers a value-layer definition for a datatype IRI the engine does not model — lexical validity consulted by SHACL `sh:datatype`, value equality consulted by SPARQL `=`/`!=` before the term-identity fall-through. Answers are three-valued and an abstention preserves the built-in behaviour; a reservation gate keeps every XSD, RDF, and engine-modelled datatype unregistrable, and with nothing registered the engine behaves byte-for-byte as if the extension point did not exist. The first registrant is the GeoSPARQL layer (`Lumoin.Veritas.Geo`): `geo:wktLiteral` well-known-text validation with CRS-prefix decomposition.

**Serialization syntaxes.** N-Quads, Turtle, and TriG readers and writers (all RDF 1.2-aware), an RDF/XML reader, SPARQL query results in JSON and XML, an RFC 8949 CBOR codec with DRISL/dCBOR profiles, and DASL Content Identifiers for hash-addressed content.

**Pluggable storage and hashing.** Storage and hashing are wired through delegates rather than tied to any specific persistence layer or digest implementation. The same processing code runs against an in-memory store during testing and a disk-backed store in production without changes at the call site.

**Durable storage with crash-safe commits and self-healing.** A database persists as a chain of immutable, checksummed generations committed by a single atomic pointer swap, with an at-rest integrity tier — per-block checksums, whole-image manifest binding, and a re-derive/parity/peer repair ladder — that runs as a live behavior: an options-driven background loop scrubs on a reliability-driven cadence, repairs what it can, and atomically republishes the healed generation. A mutable database can additionally wire a durable dataset journal: every commit, including its newly minted dictionary terms, is flushed to an append-only log before the acknowledgement, and a reopen folds the journal forward from the last persisted generation — verified by content addressing — so acknowledged commits survive a crash between checkpoints.

**Many-worlds datasets for what-if analysis.** A mutable dataset forks into independently evolving *worlds* over one shared term dictionary and node arena. Forking copies nothing — nodes are content-addressed and structurally shared, so only divergence allocates, and worlds that converge to the same content arrive at the same state identifier. Each world commits through its own journal under per-branch optimistic concurrency, the per-world logs form a directed acyclic graph through fork entries, and two worlds diff exactly as net per-graph transitions. Fork, apply a hypothetical, query and diff it in isolation, drop it — the substrate for counterfactual evaluation over live graph data.

**Replication across WAN distances.** A rateless set-reconciliation data plane, wired into the engine and hosted by the CLI's `replicate` command: replicas exchange sketches, decode their exact symmetric difference under a symbol budget, and apply it as repair-as-ingest, every round ending at a value-based outcome (converged, already consistent, or a named decline that applies nothing). Beside it, a consensus-coordinated metadata plane — replica identity, lineage baselines, policy, and the coordinator lease on a QuePaxa versioned register — which the same command composes from a founder list and an endpoint map: a replica claims its identity and records its lineage baseline through consensus at open, and a host that cannot reach a quorum answers by value and keeps serving, never blocking on the plane.

## On the roadmap

Veritas is already quietly at work in AI and agentic settings — for example as the graph
store an AI system keeps its structured knowledge in. What follows is on the way.

**Encryption at rest and hardware-bound data.** At-rest encryption over the persisted
generations and the commit journal, and hardware-rooted protection: data and policies sealed
to a TPM, so the material is usable only on the platform that holds the keys. The precise
shapes — key hierarchies, sealed policies, attestation — will be specified as the work lands.

**Further storage and query-engine optimization.** Encoded terms over pooled memory,
succinct Elias-Fano columns, columnar batched execution, and worst-case-optimal joins where
the shape demands them. The aim is to keep making this faster and to measure the progress:
every kept execution route is measured against its alternatives, and each optimization —
adaptive route selection, deeper compression, storage-layer tuning — lands measurement-first.
Check it out and share your read in an
[issue](https://github.com/Lumoin/Lumoin.Veritas/issues), on
[LinkedIn](https://www.linkedin.com/company/lumoin/), or on
[Bluesky](https://bsky.app/profile/lumoin.com).

**Billions of nodes.** Graph exploration is headed past the current interactive scale to
billions of nodes: the engine streams a server-produced hierarchy of truthful aggregates —
counts stated as exact or honestly approximate, never blurred — and the Studio explores it
through bounded scene deltas with drill-down into real child scopes. The worlds face is part
of the same contract: a world's content-addressed state identifier is the revision token that
scopes caching and streaming, and a worlds diff is the first of those bounded deltas. The
design work is underway on top of the engine's streaming evaluation.

**Identity and agent-driven operation.** OAuth, enterprise single sign-on, and decentralized
identity methods arrive at the SPARQL `SERVICE` boundary and across the enterprise management
surface, so federated queries and administration authenticate with the credentials an
organization already holds — and fully agentic operation runs over those same surfaces, with
delegation and policy carried by the identity layer.

**AI tooling for the Studio and the server.** Assistance for query authoring, diagnostics,
and operations — in the Studio and against a served engine alike.

**Zero-knowledge proofs.** Planned, in ways not yet disclosed. The deterministic
canonicalization and the content-addressed substrate are what they build on.

### Deontic, holonic, and mereological layers

- Deontic: obligations, permissions, and prohibitions evaluated beside what is true.
- Holonic: a node carries separate named graphs for what is true, what is allowed, what it projects outward, and the context it belongs to.
- Mereological: part-whole structure — parthood, composition, containment — reasoned beside class subsumption, so parts and wholes are query and validation targets.
- All three build on the existing named-graph, SHACL, OWL, provenance, and governance machinery and surface through the same extension points the GeoSPARQL extension established.

## Veritas Studio

The repository carries Veritas Studio, an in-browser workbench over the engine — vanilla
TypeScript and web components, no framework runtime.

**Try it live.** The hosted Studio at <https://lumoin.github.io/Lumoin.Veritas/> runs the
engine fully in the browser as a WebAssembly module, so queries, validation, and reasoning
execute on your machine and nothing leaves it.

The CLI hosts it together with the SPARQL
endpoint on one origin — build the site once (`npm ci && npm run build` in
`src/Lumoin.Veritas.Studio.Web`), then serve the build output:

```
veritas serve --data data.ttl --ui --ui-dir src/Lumoin.Veritas.Studio.Web/dist
```

opens the Studio against the served dataset: query editing with SPARQL result views, SHACL
validation reports, a graph view, a worlds strip, and bundled sample datasets. The sources
live under `src/Lumoin.Veritas.Studio.Web`.

**What if.** "Create a scenario world" forks the loaded dataset — a zero-copy fork over the
shared content-addressed store — and when the dataset declares scenario levers, the dialog
offers them as knobs whose moved settings become the new world's first update. Run then
executes in the active world: ask the questions, read the consequence in the Diff view
(per-graph additions and removals with exact totals), and drop the scenario when done. The
primary world stands untouched throughout, its state identifier visible in the strip. The
bundled "Water adaptation pathways" dataset shows the intent: a delta city's adaptation plan
whose assumptions — sea-level rise, rainfall change, demand growth, budget — are levers a
scenario world can move, so each scenario is one declared possible future, and a claim about
the plan is a count over the declared worlds it holds in. Worlds are a first-party capability,
available against the in-browser engine and a serving CLI alike; the strip hides on a generic
endpoint.

**Point it anywhere.** The topbar's engine-source picker switches the editor between the
in-browser engine, the serving CLI, and any conformant SPARQL 1.1 Protocol endpoint you
enter; the `serve` command's `--cors-origin` allowlist (for example
`veritas serve --data data.ttl --cors-origin https://studio.example`) grants a remotely
hosted Studio page cross-origin access to your served engine.

**Watch the engine work.** Running a query streams the engine's live execution trace into
the trace panel — each operator's evaluation strategy and row counts, and every rewrite
rule's verdict — from the in-browser engine and from a served engine alike (the latter over
the `/trace` Server-Sent-Events endpoint, `curl`-watchable too). A generic endpoint offers
no trace stream, so the panel disables there rather than guessing.

**Author with the engine's own eyes.** The editor proposes the GeoSPARQL vocabularies
(`geo:`, `geof:`, `sf:`, and the GML geometry classes) beside the SPARQL grammar and the
loaded data's terms, and marks a broken geometry literal at its first offending byte — the
codec that will read the literal is the same one diagnosing it, so an error mark means the
datatype's grammar is broken and a warning mark means the engine cannot evaluate the body
even though the datatype tolerates it. Parser-driven completion and the diagnosis both
answer from whichever first-party engine is active: the in-browser engine over its interop
face, or a served engine over `POST /completion` (variable datatypes resolved against the
served data), `POST /turtle-completion`, `GET /editor-vocabulary`, and
`POST /literal-diagnostics`. The vocabulary the editor offers enumerates the composition's
registered value datatypes, so the A5 DGGS literal datatype completes as its bracketed full
IRI. A generic endpoint offers none of these faces, so completion degrades to the token
heuristic and the editor simply paints no marks there.

**The editor, today and next.** The workbench's editing surfaces are engine-backed end to
end: parser-driven completion in the SPARQL buffer and in every Turtle-family buffer
(SHACL, Turtle, OWL, TriG), live re-query as you type, offset-precise literal diagnostics,
a SHACL conformance verdict for each loaded dataset, the live execution trace, and an
address bar that always carries the loaded dataset as a shareable link
(`?dataset=campus`). Datasets load through one path — the bundled cases are ordinary
loaded datasets, and several can be worked on at once in separate browser tabs, each with
its own engine. These editor features will be further refined: the engine already names
its token kinds on the completion wire, and whole-buffer syntax highlighting from the same
parser is on the way, alongside the AI-assisted tooling the roadmap names.

**Built for billions of entities — the plan.** The Studio's graph view is headed to
billion-entity scale: not billions of rendered objects, but an explorable hierarchy whose
overview shows a bounded set of truthful aggregates — counts stated as exact or honestly
approximate, never blurred — painted from the first row batches the engine streams, with
selection and drill-down into real child scopes that keep identity, consistency, and
access-control guarantees intact. The design work for this is underway on top of the
engine's streaming evaluation; the roadmap's billions-of-nodes entry states the contract.
And wishes of any kind — for the Studio, the editor, the engine, anything you'd want
Veritas to do — are genuinely welcome: open an issue.

## Architecture principles

The library follows data-oriented programming principles where code is separate from immutable data, generic data structures are favored, and general-purpose functions are implemented as static extensions over those structures. Domain types contain encoded graph material without serialization artifacts; encoding lives at boundaries in the format projects (`Json`, `Json.Stj`, `Cbor`, `NQuads`, `Turtle`, `Xml`).

Storage, hashing, and term interning use delegate-based extension points rather than direct implementations. This allows the same high-level API to work against an in-memory triple store, a memory-mapped file, or an external store without changing calling code. `StorageDelegates` is the canonical pattern.

The vocabulary projects (`Owl`, `Shacl`, `Skos`) and format projects (`NQuads`, `Turtle`, `JsonLd`, `CborLd`) build on top of `Core` and `Rdf`. They expose their semantics through delegates and well-known vocabulary classes (`RdfVocabulary`, `RdfsVocabularyIds`, `ShaclVocabulary`, and so on) rather than through hidden global state.

## Specifications implemented

- W3C RDF 1.2 Concepts; N-Quads, Turtle, and TriG syntaxes with triple terms; RDF/XML.
- W3C SPARQL 1.2 Query; SPARQL 1.1 Protocol; SPARQL Query Results in JSON and XML.
- W3C RDF Dataset Canonicalization (RDFC-1.0).
- W3C JSON-LD 1.1 (expansion and compaction); CBOR-LD.
- W3C SHACL 1.2 Core, including SHACL-SPARQL constraints.
- W3C OWL 2 (RL profile reasoning, structural specification, functional syntax).
- W3C SKOS concept schemes.
- RFC 8949 CBOR with dCBOR/DRISL profiles; RFC 6901 JSON Pointer; JSON Schema draft 2020-12.
- OGC GeoSPARQL 1.1 vocabularies, lexical validation for all six geometry serialization datatypes (`geo:wktLiteral`, `geo:gmlLiteral`, `geo:geoJSONLiteral`, `geo:kmlLiteral`, `geo:dggsLiteral`, and the house A5 DGGS subclass `a5Literal`) with full readers and writers behind three of them — GML 3.2, RFC 7946 GeoJSON, and OGC KML 2.2 geometry documents parse into the geometry model and serialize back out of it in a canonical byte form, refusing by value over a closed reason roster with the first offending byte named — a complete A5 pentagonal discrete global grid system kernel (cell identity at resolutions 0–30, SIMD point-to-cell assignment, cell boundary geometry, region fill, traversal) with a cells-to-geometry bridge into the planar Simple Features model, the `geof:` extension functions and spatial aggregate functions — `geof:asGML`, `geof:asGeoJSON`, `geof:asKML`, `geof:asDGGS`, and `geof:transform` over a closed certified coordinate-reference-system roster (CRS84, EPSG:4326, EPSG:3857 Web Mercator) included — over that model, and the query-rewrite transformation rules for the topological relation properties.

Conformance is exercised against the relevant W3C test suites and OGC GeoSPARQL conformance material in the test projects.

These are testable claims, and checking them is invited: the conformance suites, the census rows, and the benchmark soaks live in the test projects, the hosted Studio runs the engine in your browser, and anything you find belongs in an issue.

On the W3C OWL 2 test corpus, every one of the 441 target cases is accounted for: 439 are decided with zero wrong answers, and the remaining two — never-approved "extra credit" arithmetic cases inherited from the original WebOnt suite — declare entailments that a machine-checked counter-model refutes. Those two are recorded as corpus defects by executable rows that fail loudly by name if the corpus is ever corrected, so the defect claim is itself under regression. The GeoSPARQL arm is decided whole against a house-authored census of all 57 requirements of GeoSPARQL 1.1 (whose content is GeoSPARQL 1.2 / ISO 19186-1): every one of the 57 is decided with zero wrong answers and named evidence — none silenced. The protocol requirement is answered by the `veritas serve` HTTP endpoint, the engine's SPARQL 1.1 Protocol face under the server deployment posture.

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

The command-line tool — including an MCP server and a SPARQL 1.1 Protocol HTTP endpoint, with the GeoSPARQL extension (the `geof:` function catalog, the geometry serialization datatypes, and the topological-relations query rewrite) answering on every surface — installs as a dotnet tool:

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
- Improved indexing strategies and storage backends.

## License

See the LICENSE file for details.

---

> **Note:** This is an early version under active development. APIs may change between versions.
