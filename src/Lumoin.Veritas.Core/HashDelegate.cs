using System;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Computes a cryptographic hash of a byte sequence.
/// </summary>
/// <remarks>
/// <para>
/// Cryptographic hashing is needed across the project — RDF canonicalization,
/// content identifiers, payload digests, and proof witnesses all consume a
/// hash function. Rather than each consumer taking a direct dependency on a
/// specific cryptographic library, the hash function is injected by the
/// caller. This follows the project convention that cryptographic operations
/// are always supplied by the caller.
/// </para>
/// <para>
/// For SHA-256: <c>HashDelegate sha256 = SHA256.HashData;</c>
/// </para>
/// </remarks>
/// <param name="data">The bytes to hash.</param>
/// <returns>The hash as a byte array.</returns>
public delegate byte[] HashDelegate(ReadOnlySpan<byte> data);
