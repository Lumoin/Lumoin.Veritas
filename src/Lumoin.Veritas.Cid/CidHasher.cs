using System;
using Lumoin.Veritas.Core;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// Constructs a <see cref="Cid"/> by hashing content bytes with a
/// caller-supplied <see cref="HashDelegate"/>.
/// </summary>
/// <remarks>
/// The DASL CID specification fixes the hash to SHA-256 and the digest to
/// 32 bytes. The hash function is supplied by the caller so this project
/// does not take a direct dependency on a specific cryptographic library;
/// callers typically pass <c>SHA256.HashData</c>. The hasher validates that
/// the delegate returned exactly 32 bytes.
/// </remarks>
public static class CidHasher
{
    private const int DigestLength = 32;

    /// <summary>
    /// Computes a <see cref="Cid"/> over <paramref name="content"/> using
    /// <paramref name="hash"/>. The resulting CID carries the supplied
    /// <paramref name="codec"/> and the hash output as its 32-byte digest.
    /// </summary>
    /// <param name="content">The bytes to hash.</param>
    /// <param name="codec">The codec the resulting CID will identify with.</param>
    /// <param name="hash">A hash delegate that produces a 32-byte SHA-256 digest of its input.</param>
    /// <returns>A new <see cref="Cid"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="codec"/> is not a defined <see cref="CidCodec"/> value.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="hash"/> returned a digest of any length other than 32 bytes.
    /// </exception>
    public static Cid ComputeFromBytes(ReadOnlySpan<byte> content, CidCodec codec, HashDelegate hash)
    {
        ArgumentNullException.ThrowIfNull(hash);

        if(!Enum.IsDefined(codec))
        {
            throw new ArgumentException(
                $"CID codec must be a defined CidCodec value; got 0x{(byte)codec:X2}.",
                nameof(codec));
        }

        byte[] digest = hash(content);
        if(digest is null || digest.Length != DigestLength)
        {
            throw new InvalidOperationException(
                $"Hash delegate must return exactly {DigestLength} bytes; got {(digest is null ? "null" : digest.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))}.");
        }

        return new Cid
        {
            Codec = codec,
            Digest = Digest32.FromSpan(digest)
        };
    }
}
