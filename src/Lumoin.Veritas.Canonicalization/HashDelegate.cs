using System;

namespace Lumoin.Veritas.Canonicalization;

/// <summary>
/// Computes a cryptographic hash of a byte sequence.
/// </summary>
/// <remarks>
/// <para>
/// The canonicalization algorithm requires SHA-256 hashing. Rather than taking a
/// direct dependency on a specific cryptographic library, the hash function is
/// injected by the caller. This follows the project convention that cryptographic
/// operations are always supplied by the caller.
/// </para>
/// <para>
/// For RDFC-1.0, use SHA-256: <c>HashDelegate sha256 = SHA256.HashData;</c>
/// </para>
/// </remarks>
/// <param name="data">The bytes to hash.</param>
/// <returns>The hash as a byte array.</returns>
public delegate byte[] HashDelegate(ReadOnlySpan<byte> data);
