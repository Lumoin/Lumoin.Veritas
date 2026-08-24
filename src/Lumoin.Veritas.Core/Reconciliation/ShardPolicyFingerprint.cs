using System;
using System.Buffers.Binary;

namespace Lumoin.Veritas.Core.Reconciliation;

/// <summary>
/// The typed declaration of a <see cref="PrefixShardPolicy"/>'s identity — exactly the fields shard assignment
/// is a function of — exchanged between replicas so a sharded reconciliation refuses a policy mismatch by name
/// instead of corrupting difference-stream cancellation silently. Value equality is the compatibility test: two
/// replicas may reconcile a sharded generation only when their fingerprints are equal.
/// </summary>
/// <remarks>
/// <para>
/// The wire encoding is a stable byte contract host transports carry verbatim: a version byte, the shard-bit
/// count as one byte, then the mixing code as a four-byte big-endian integer (<see cref="EncodedByteLength"/>
/// bytes in all). <see cref="TryRead"/> is deliberately STRUCTURAL only — it refuses a short frame or an unknown
/// encoding version, but carries any shard-bit count or mixing code as declared, because a foreign peer's values
/// must survive parsing so the equality comparison can refuse them with both sides' values available to the
/// diagnostic; a parse-time refusal would erase the evidence. A frame <see cref="TryRead"/> refuses is the host
/// transport's refusal to surface — the engine never sees a fingerprint for it.
/// </para>
/// <para>
/// <see langword="default"/> carries mixing code zero, which no constructible policy uses
/// (<see cref="PrefixShardPolicy"/> admits only registered strategies), so an unset declaration can never equal
/// a real policy's fingerprint and always compares as a mismatch. The constructor validates nothing, by the
/// same design: the type is a declaration CARRIER, foreign values included — <see cref="PrefixShardPolicy"/>
/// is the validating construction path, and its minted fingerprints always fit the encoding.
/// </para>
/// </remarks>
/// <param name="ShardBitCount">The declared base-two logarithm of the shard count.</param>
/// <param name="Mixing">The declared strategy the balancing bits are derived under.</param>
public readonly record struct ShardPolicyFingerprint(int ShardBitCount, ShardKeyMixing Mixing)
{
    /// <summary>The encoded size in bytes: the version byte, the shard-bit-count byte, and the four-byte mixing code.</summary>
    public const int EncodedByteLength = sizeof(byte) + sizeof(byte) + sizeof(int);

    /// <summary>The encoding version this build writes and the only one <see cref="TryRead"/> accepts.</summary>
    private const byte EncodingVersion = 1;

    /// <summary>
    /// Writes the canonical encoding into the first <see cref="EncodedByteLength"/> bytes of
    /// <paramref name="destination"/>. Policy-minted fingerprints always fit the encoding; the shard-bit count is
    /// written as its low byte.
    /// </summary>
    /// <param name="destination">The buffer to write into, at least <see cref="EncodedByteLength"/> bytes.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination"/> is shorter than <see cref="EncodedByteLength"/>.</exception>
    public void Write(Span<byte> destination)
    {
        if(destination.Length < EncodedByteLength)
        {
            throw new ArgumentException($"A shard-policy fingerprint needs {EncodedByteLength} bytes to encode.", nameof(destination));
        }

        destination[0] = EncodingVersion;
        destination[1] = (byte)ShardBitCount;
        BinaryPrimitives.WriteInt32BigEndian(destination[2..], Mixing.Code);
    }

    /// <summary>
    /// Reads a fingerprint from the first <see cref="EncodedByteLength"/> bytes of <paramref name="source"/>.
    /// Structural refusal only: a short frame or an unknown version returns <see langword="false"/>; the declared
    /// shard-bit count and mixing code are carried as-is so a mismatching peer's values reach the comparison
    /// diagnostic intact.
    /// </summary>
    /// <param name="source">The encoded bytes, at least <see cref="EncodedByteLength"/> long.</param>
    /// <param name="fingerprint">The decoded declaration, or <see langword="default"/> on refusal.</param>
    /// <returns>Whether the frame was structurally a fingerprint this build reads.</returns>
    public static bool TryRead(ReadOnlySpan<byte> source, out ShardPolicyFingerprint fingerprint)
    {
        if(source.Length < EncodedByteLength || source[0] != EncodingVersion)
        {
            fingerprint = default;

            return false;
        }

        fingerprint = new ShardPolicyFingerprint(source[1], new ShardKeyMixing(BinaryPrimitives.ReadInt32BigEndian(source[2..])));

        return true;
    }
}
