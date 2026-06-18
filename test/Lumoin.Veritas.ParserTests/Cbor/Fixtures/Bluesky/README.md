# Bluesky / AT Protocol fixtures

Real CAR-file snapshots of public AT Protocol repositories, committed
to exercise the DAG-CBOR codec against production data.

## Status

The fixture files themselves are **not yet committed**. The agent's
execution environment for the previous batch did not have network
access; the human is expected to populate this directory with three
CAR files downloaded from public PDS endpoints:

| Filename             | DID                                                | Download date | PDS endpoint                                                                                             |
|----------------------|----------------------------------------------------|---------------|----------------------------------------------------------------------------------------------------------|
| `atproto-com.car`    | (TBD — fill in)                                    | (TBD)         | `https://bsky.social/xrpc/com.atproto.sync.getRepo?did=did:plc:ewvi7nxzyoun6zhxrhs64oiz`                 |
| `fixture-two.car`    | (TBD — pick a public DID with modest activity)     | (TBD)         | (TBD)                                                                                                    |
| `fixture-three.car`  | (TBD — pick a public DID with modest activity)     | (TBD)         | (TBD)                                                                                                    |

Target total committed bytes: under 5 MB across all three fixtures.

Once fixtures are in place, `BlueskyDagCborTests.cs` will pick them up
automatically (it skips with an inconclusive result when a fixture is
absent).

## How the fixtures are used

The test class `BlueskyDagCborTests.cs`:

1. Loads each CAR file from this directory.
2. Walks it via `CarFileReader` (in `../Helpers/CarFileReader.cs`).
3. For each block: parses the bytes via `DagCborReader(strict: false)`
   and walks every value to confirm parse success without exception.
4. Re-parses the same block as a multi-segment `ReadOnlySequence<byte>`
   at multiple split offsets and asserts the resulting tree equals the
   contiguous parse — validating the streaming reader (G) against real
   data.

## License / provenance

AT Protocol repository data is public; the
`com.atproto.sync.getRepo` endpoint is unauthenticated. These
fixtures are point-in-time snapshots used as format-conformance test
inputs, not redistributed for content value. They are not maintained
or kept current.

## How to download (manually)

```bash
curl -L 'https://bsky.social/xrpc/com.atproto.sync.getRepo?did=did:plc:ewvi7nxzyoun6zhxrhs64oiz' \
    -o atproto-com.car
```

(Replace the DID for the other two fixtures.)
