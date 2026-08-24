using System;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// Formats a <see cref="Cid"/> into its canonical 36-byte binary form or its
/// canonical string form (lowercase RFC 4648 base32 with no padding, prefixed
/// by the literal <c>b</c>).
/// </summary>
/// <remarks>
/// The formatter validates the input <see cref="Cid"/> before producing
/// output. A digest of any length other than 32 bytes, or a codec value
/// outside the <see cref="CidCodec"/> enum, is rejected with
/// <see cref="ArgumentException"/>.
/// </remarks>
public static class CidFormatter
{
    private const byte Version = 0x01;
    private const byte HashTypeSha256 = 0x12;
    private const byte HashLength32 = 0x20;
    private const int BinaryLength = 36;
    private const char StringPrefix = 'b';

    //RFC 4648 Section 6 base32 alphabet, lowercase.
    private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    /// <summary>
    /// Formats <paramref name="cid"/> as its canonical 36-byte binary form.
    /// </summary>
    /// <param name="cid">The CID to format. Must have a 32-byte digest and a defined codec.</param>
    /// <returns>A new 36-byte array containing the canonical wire form.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cid"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// The digest is not exactly 32 bytes, or the codec is not a defined <see cref="CidCodec"/> value.
    /// </exception>
    public static byte[] ToBytes(Cid cid)
    {
        ArgumentNullException.ThrowIfNull(cid);
        ValidateForFormat(cid);

        byte[] output = new byte[BinaryLength];
        output[0] = Version;
        output[1] = (byte)cid.Codec;
        output[2] = HashTypeSha256;
        output[3] = HashLength32;
        cid.Digest.CopyTo(output.AsSpan(4));
        return output;
    }

    /// <summary>
    /// Formats <paramref name="cid"/> as its canonical string form: the
    /// literal character <c>b</c> followed by the lowercase RFC 4648 base32
    /// encoding of the 36 binary bytes, with no padding.
    /// </summary>
    /// <param name="cid">The CID to format. Must have a 32-byte digest and a defined codec.</param>
    /// <returns>The 59-character canonical string representation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cid"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// The digest is not exactly 32 bytes, or the codec is not a defined <see cref="CidCodec"/> value.
    /// </exception>
    public static string ToCanonicalString(Cid cid)
    {
        ArgumentNullException.ThrowIfNull(cid);
        ValidateForFormat(cid);

        Span<byte> binary = stackalloc byte[BinaryLength];
        binary[0] = Version;
        binary[1] = (byte)cid.Codec;
        binary[2] = HashTypeSha256;
        binary[3] = HashLength32;
        cid.Digest.CopyTo(binary[4..]);

        //Encoded length: ceil(36 * 8 / 5) = 58 characters; one byte for the 'b' prefix.
        Span<char> chars = stackalloc char[1 + 58];
        chars[0] = StringPrefix;
        EncodeBase32(binary, chars[1..]);
        return new string(chars);
    }

    /// <summary>
    /// Encodes <paramref name="input"/> into <paramref name="output"/> as
    /// lowercase RFC 4648 base32 with no padding. <paramref name="output"/>
    /// must be sized to <c>ceil(input.Length * 8 / 5)</c> characters.
    /// </summary>
    /// <param name="input">The bytes to encode.</param>
    /// <param name="output">The destination character span.</param>
    private static void EncodeBase32(ReadOnlySpan<byte> input, Span<char> output)
    {
        int bitBuffer = 0;
        int bitCount = 0;
        int outIndex = 0;
        for(int i = 0; i < input.Length; i++)
        {
            bitBuffer = (bitBuffer << 8) | input[i];
            bitCount += 8;
            while(bitCount >= 5)
            {
                bitCount -= 5;
                output[outIndex++] = Base32Alphabet[(bitBuffer >> bitCount) & 0x1F];
            }
        }

        //Final partial group: shift the remaining bits up to the top of a 5-bit slot
        //and emit one more character. Trailing low-order bits remain zero by
        //construction, which is the canonical no-padding form.
        if(bitCount > 0)
        {
            output[outIndex] = Base32Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F];
        }
    }

    private static void ValidateForFormat(Cid cid)
    {
        //Digest length is fixed by Digest32's inline-array shape; no length
        //check needed. Codec is the only remaining wire-form invariant.
        if(!Enum.IsDefined(cid.Codec))
        {
            throw new ArgumentException(
                $"CID codec must be a defined CidCodec value; got 0x{(byte)cid.Codec:X2}.",
                nameof(cid));
        }
    }
}
