# Decision record — SPARQL join layers, triple-term matching, and the performance trajectory

Status: **accepted (2026-05-31)**. Records the options weighed and the decision taken, so the
reasoning survives. Scope: the SPARQL execution engine's join architecture and how
variable-bearing quoted-triple-term patterns are matched.

## Context

Target workload is **large-graph**: city-wide IFC models, digital product passports and the
supply chains they span, with state-of-the-art OWL reasoning planned on top. Efficiency and
cache behaviour are first-order design constraints, not premature optimisation.

A hard, already-paid constraint: the WCOJ hypertrie's **construction cost**. An early build of a
~1-million-triple engine cost ~10 GiB of memory and was dog-slow; that was reworked at real
expense and must not regress. Anything that adds per-triple (or per-triple-term) structures at
*build* time is therefore suspect.

### The two join layers (they are not the same join)

- **Layer 1 — Core hypertrie `BasicGraphPatternEvaluator`: leapfrog triejoin.** A worst-case-optimal
  multiway join. It joins all triple patterns *within one BGP over one immutable
  `HypertrieSnapshot`* on their shared variables. It is already efficient — proven by the
  multi-pattern join tests and `BasicGraphPatternJoinDifferentialPropertyTests` (random BGPs vs a
  naive join). Each `TriejoinIterator` is built over a single snapshot, so leapfrog only joins
  what lives in one trie.
- **Layer 2 — `SparqlQueryEngine.JoinSolutions`: naive nested-loop.** `O(n·m)` over already
  materialised `SparqlSolution` lists (rows of heap `(SparqlVariable, RdfTerm)` objects). This
  combines the *results of separate algebra operators* (BGP ⋈ OPTIONAL / sub-SELECT / UNION …).
  This — not leapfrog — is the inefficient join.

## Decision 1 — variable-bearing triple-term matching: post-join destructuring bridge (Option B)

`pattern-06` (`SELECT * { << <<:s :p2 :o>> :p3 :z>> :q ?q }`) normalises to a BGP whose pattern
`?q2 rdf:reifies <<(?q1 :p3 :z)>>` has a quoted triple term with a **variable subject** `?q1` that
joins the rest of the BGP. Today `TryEncodePosition` treats the component as ground, finds no such
interned value, and yields 0 rows.

Options considered:

- **(A) Native structural component index.** At store build, also index each triple-term value's
  (subject, predicate, object) so a variable-bearing triple-term pattern lowers to native BGP
  triples the leapfrog joins directly — pruned at join time, best cache behaviour. **Rejected for
  now:** it adds per-triple-term structures at build time, re-inflating the construction memory
  that was painfully reworked; and because the index cannot live in the main trie (it would leak
  into `?s ?p ?o` scans), it needs either a second trie with cross-trie leapfrog iterators or a
  reserved-namespace partition — a substrate rework that would block feature completion.
- **(B) Post-join destructuring (chosen).** Encode the triple-term position as a fresh join
  variable `Vtt`; the leapfrog binds it via its containing triple (already narrowed by the
  surrounding patterns — not a full dictionary scan). Then a per-solution step resolves `Vtt`,
  requires a triple-term value, and unifies its components against the solution (variable → bind,
  constant → check, nested → iterate). **Zero build-time cost, zero store growth**; cost is borne
  only per-solution on the rare triple-term query, on a candidate set the join already narrowed.
  The destructuring *logic* survives a later columnar rewrite even though its surrounding
  representation would change.

Rationale: provenance-heavy domains (digital product passports / supply-chain attestations) may
make triple-term queries common, which argues for (A) eventually — but the construction-cost
constraint and the "finish the feature set first" sequencing make (B) the right move now.

## Decision 2 — sequencing: correctness harness first, performance rework second

Finish **all of SPARQL 1.2 + the RDF and SHACL conformance suites** *without* a substrate rework
first. The complete, conformant feature set is the **correctness harness**: once the operating
bounds are known and green, performance rework can be validated against it rather than done blind.
Reworking the substrate before the harness exists risks silent breakage with no oracle.

## Deferred — the real large-graph performance project (Layer 2)

The dominant scale liability is Layer 2's `O(n·m)` nested-loop over heap-object solution rows. The
end-state, to be done **after** the conformance harness is complete and validated against it:

- A **columnar `TermId`-row** solution representation: fixed-width `uint` rows over a shared column
  schema, unbound = sentinel.
- A **hash / sort-merge join** (→ `O(n+m)`), with SPARQL's "compatible on shared *bound* variables,
  unbound = wildcard" handled in the key.
- The per-row compatibility check then becomes a **SIMD masked lane-equality** across `uint` lanes.

A round of **probing OWL work** may precede or accompany this, so the reasoner's real access
patterns and intermediate-result shapes drive the representation rather than guesswork. Note: OWL
materialisation need not be tableau / hyper-tableau, so it does not necessarily imply large
intermediate relations.
