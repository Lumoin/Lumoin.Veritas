# JSON Schema Test Suite

The files under `tests/` and `remotes/` are vendored verbatim from the official
**JSON Schema Test Suite**:

- Source: <https://github.com/json-schema-org/JSON-Schema-Test-Suite>
- Pinned commit: `60755c1097769e313fae3ec4d63bcc9d49b5d2d5`
- License: MIT (Copyright (c) 2012 Julian Berman) — see `LICENSE` in this directory.

Only the `draft2020-12` test cases (`tests/draft2020-12/`, including `optional/`)
and the shared `remotes/` schemas are vendored, since the engine targets JSON
Schema draft 2020-12.

The suite is the conformance oracle for `Lumoin.Veritas.JsonSchema`, read directly
from the source tree by the test harness (anchored via `[CallerFilePath]`, like the
other vendored W3C corpora). It is deliberately neither compiled nor copied to the
build output.

## Metaschema documents

`metaschema/` holds the official JSON Schema **draft 2020-12 dialect metaschema** and its
vocabulary metaschemas (`schema`, `meta/core`, `meta/applicator`, `meta/unevaluated`,
`meta/validation`, `meta/meta-data`, `meta/format-annotation`, `meta/content`), retrieved from
`https://json-schema.org/draft/2020-12/`. They are published by the JSON Schema organisation and
are free to use. The harness serves them to `$ref`/`$dynamicRef` that target
`https://json-schema.org/draft/2020-12/…` (so a schema can be validated against the metaschema, and
custom metaschemas can extend the standard vocabularies).

## Remote reference convention

The suite resolves remote references under the base URI `http://localhost:1234/`
to files within `remotes/` (for example, `http://localhost:1234/integer.json`
maps to `remotes/integer.json`). The harness wires a resolver that maps that base
URI onto this directory.
