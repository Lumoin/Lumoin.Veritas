using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Lumoin.Veritas.Core;

/// <summary>
/// Production and test <see cref="IdentifierDelegate"/> defaults. This is the one
/// sanctioned place (alongside the UUID path of <see cref="VeritasRandomness"/>)
/// that calls <see cref="Guid.NewGuid"/>; every other component consumes an
/// <see cref="IdentifierDelegate"/> so its identities are injected.
/// </summary>
public static class VeritasIdentifiers
{
    /// <summary>Real <see cref="Guid.NewGuid"/> identities. The production default.</summary>
    public static IdentifierDelegate System { get; } = SystemImpl;

    /// <summary><see cref="Guid.Empty"/> for every request. Use only in tests where a constant identity is acceptable.</summary>
    public static IdentifierDelegate Zero { get; } = ZeroImpl;

    /// <summary>
    /// Returns a delegate that hands out deterministic, monotonically increasing
    /// identifiers (<c>00000000-0000-0000-0000-000000000001</c>, <c>…002</c>, …),
    /// independent of any salt. Each returned delegate has its own counter.
    /// </summary>
    /// <returns>A deterministic sequential identifier delegate.</returns>
    public static IdentifierDelegate Sequential()
    {
        return new SequentialIdentifierSource().Next;
    }

    [SuppressMessage(
        "ApiDesign",
        "RS0030:Do not use banned APIs",
        Justification = "VeritasIdentifiers is the sanctioned producer of fresh GUID identities; Guid.NewGuid is banned everywhere else so identities flow through the injected IdentifierDelegate.")]
    private static Guid SystemImpl(in IdentifierRequest request)
    {
        return Guid.NewGuid();
    }

    private static Guid ZeroImpl(in IdentifierRequest request)
    {
        return Guid.Empty;
    }

    /// <summary>Builds a GUID whose final eight bytes encode <paramref name="value"/> big-endian, giving a readable, ordered sequence.</summary>
    /// <param name="value">The counter value to encode.</param>
    /// <returns>The encoded GUID.</returns>
    private static Guid FromCounter(long value)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes[8..], value);

        return new Guid(bytes);
    }

    /// <summary>
    /// Holds the monotonic counter behind a <see cref="Sequential"/> delegate so the
    /// produced <see cref="IdentifierDelegate"/> carries its state in an explicit field
    /// rather than a captured local.
    /// </summary>
    private sealed class SequentialIdentifierSource
    {
        /// <summary>The most recently issued counter value; incremented atomically per request.</summary>
        private long counter;

        /// <summary>Issues the next deterministic, monotonically increasing identifier.</summary>
        /// <param name="request">The identifier request; ignored, as the sequence is independent of any salt.</param>
        /// <returns>The next identifier in the sequence.</returns>
        public Guid Next(in IdentifierRequest request)
        {
            return FromCounter(Interlocked.Increment(ref counter));
        }
    }
}
