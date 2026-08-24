using System;
using System.IO.Hashing;

namespace Lumoin.Veritas.Core.Hypertrie.Storage;

/// <summary>
/// The canonical <see cref="VeritasHash"/> implementation. Plain
/// xxHash64 with seed zero, exposed as a named static method so
/// the application picks it explicitly at the composition root.
/// </summary>
/// <remarks>
/// <para>
/// xxHash64 is fast, well-distributed across the full 64-bit
/// range, and has no security claim — appropriate for an in-
/// memory data structure that verifies equality on collisions
/// and an audit fingerprint that is paired with the literal
/// edit list it commits to. Deployments that need
/// cryptographic-grade fingerprinting (cross-trust audit,
/// tamper-evident journals shipped over an untrusted channel)
/// substitute a different <see cref="VeritasHash"/> at the
/// composition root.
/// </para>
/// </remarks>
public static class VeritasHashing
{
    /// <summary>
    /// xxHash64 with seed zero. Calls
    /// <see cref="XxHash64.HashToUInt64(ReadOnlySpan{byte}, ulong)"/>
    /// with the default seed.
    /// </summary>
    /// <param name="bytes">The bytes to hash.</param>
    /// <returns>The xxHash64 of <paramref name="bytes"/>; may legitimately be zero.</returns>
    public static ulong Default(ReadOnlySpan<byte> bytes)
    {
        return XxHash64.HashToUInt64(bytes);
    }
}
