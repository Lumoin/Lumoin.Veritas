# AT Protocol CAR fixture

One real CARv1 snapshot of a public AT Protocol repository, committed so the
DAG-CBOR codec and the streaming CBOR reader can be exercised against
production-shaped data.

## The committed fixture

| Field | Value |
|-------|-------|
| Filename | `atproto-com.car` |
| Size | 8,927,359 bytes |
| Format | CARv1, DAG-CBOR blocks |
| DID | `did:plc:ewvi7nxzyoun6zhxrhs64oiz` |
| Source endpoint | `https://bsky.social/xrpc/com.atproto.sync.getRepo` |
| Snapshot date | 2026-05-15 (approximate; the date the file entered this repository) |

## Provenance and licence

AT Protocol repository data is public and the `com.atproto.sync.getRepo`
endpoint is unauthenticated, so no credential is needed to fetch it. This file
is a point-in-time snapshot used as a format-conformance test input, not
redistributed for its content. It is not maintained or kept current.

## How the fixture is used

`../../AtProtoCarDagCborTests.cs`:

1. Loads the CAR file from this directory.
2. Walks it via `CarFileReader` (in `../../Helpers/CarFileReader.cs`).
3. Parses each block with `DagCborReader(strict: false)` and walks every value
   to confirm the parse succeeds.
4. Re-parses the same block as a multi-segment `ReadOnlySequence<byte>` at
   several split offsets and asserts the resulting tree equals the contiguous
   parse, which validates the streaming reader against real data.

A probe whose fixture is absent passes with nothing to validate.

## Re-downloading it

```bash
curl -L 'https://bsky.social/xrpc/com.atproto.sync.getRepo?did=did:plc:ewvi7nxzyoun6zhxrhs64oiz' \
    -o atproto-com.car
```

A re-download yields a newer snapshot with different bytes; the tests assert
format properties only, so any valid CARv1 snapshot of a public repository
works.
