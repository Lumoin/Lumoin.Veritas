using System;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// The library's hash function. Mixes a span of bytes into a
/// 64-bit value. Threaded through every layer that participates
/// in content-addressing — node identifier computation, commit
/// fingerprinting — so the application names the hash function
/// in exactly one place and every consumer agrees by
/// construction.
/// </summary>
/// <param name="bytes">The bytes to hash. The library composes the byte layout.</param>
/// <returns>A 64-bit hash. May legitimately be zero; the library applies the zero-sentinel upgrade where necessary.</returns>
/// <remarks>
/// <para>
/// <b>Why one delegate, not many.</b> Two consumers — node entry
/// mixing and edit commitment fingerprinting — accept different
/// inputs (a <c>(key, childId)</c> pair, a
/// <c>(kind, S, P, O)</c> tuple) but use the same underlying
/// hash function on different byte layouts. Lifting the hash
/// function out as the variability point lets the application
/// pick xxHash64-with-seed-zero for production, a SHA-256
/// truncation for cryptographic deployments, or a deterministic
/// identity-style mixer for tests, and have every content-
/// addressing consumer agree on the choice.
/// </para>
/// <para>
/// <b>Byte layouts are fixed.</b> The library composes the input
/// bytes for each consumer in helpers under
/// <see cref="NodeEntryHashing"/> and
/// <see cref="Lumoin.Veritas.Core.Hypertrie.Editing.EditCommitmentHashing"/>.
/// The layouts are protocol-pinned: a node's identifier must be
/// the same regardless of which build of the library produced
/// it, and a future PostgreSQL projection of the hypertrie must
/// compute the same identifier for the same content. Only the
/// hash function is configurable; the bytes that go into it are
/// not.
/// </para>
/// <para>
/// <b>Zero-sentinel handling.</b> Per-entry hashes whose
/// content bits are all zero would XOR-combine into the parent
/// identifier as a no-op, making the entry invisible to
/// deduplication. The library upgrades such an output to
/// <see cref="NodeIdentifier.ZeroSentinel"/> through
/// <see cref="NodeIdentifier.SanitizeContribution"/> inside the
/// helper that wraps each consumer's call to this delegate. The
/// delegate itself is allowed to return zero; callers go
/// through the helpers, not through the delegate directly.
/// </para>
/// <para>
/// <b>Distribution requirement.</b> Implementations should
/// distribute outputs well across the full 64-bit range. A
/// poor hash increases hash-collision rates in the intern table
/// and false-positive rates in idempotent-retry detection;
/// correctness is preserved (intern verifies content; replay
/// verifies edits), but performance and audit-fingerprint
/// quality are not.
/// </para>
/// </remarks>
public delegate ulong VeritasHash(ReadOnlySpan<byte> bytes);
