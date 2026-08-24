using System;

namespace Lumoin.Veritas.Cid;

/// <summary>
/// Parses a <see cref="Cid"/> from its canonical string form (lowercase
/// RFC 4648 base32 with no padding, prefixed by the literal <c>b</c>) or its
/// canonical 36-byte binary form. All validation rules from the DASL CID
/// specification are enforced strictly; any deviation rejects the input.
/// </summary>
/// <remarks>
/// <para>
/// Validation rules:
/// </para>
/// <list type="bullet">
///   <item>The string form must start with the literal character <c>b</c>.</item>
///   <item>Every character after the prefix must be a lowercase letter <c>a..z</c> or a digit <c>2..7</c>.</item>
///   <item>The decoded byte sequence must be exactly 36 bytes.</item>
///   <item>Byte 0 (version) must be <c>0x01</c>.</item>
///   <item>Byte 1 (codec) must be <c>0x55</c> (raw) or <c>0x71</c> (DRISL).</item>
///   <item>Byte 2 (hash type) must be <c>0x12</c> (SHA-256).</item>
///   <item>Byte 3 (hash length) must be <c>0x20</c> (32 bytes).</item>
///   <item>Trailing bits in the final base32 character must be zero (canonical no-padding form).</item>
/// </list>
/// </remarks>
public static class CidParser
{
    private const byte Version = 0x01;
    private const byte HashTypeSha256 = 0x12;
    private const byte HashLength32 = 0x20;
    private const int BinaryLength = 36;
    private const int StringLengthAfterPrefix = 58;
    private const char StringPrefix = 'b';

    /// <summary>
    /// Parses a CID from its canonical string form.
    /// </summary>
    /// <param name="input">The string to parse.</param>
    /// <returns>The parsed <see cref="Cid"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <c>null</c>.</exception>
    /// <exception cref="CidParseException">The input does not satisfy the validation rules.</exception>
    public static Cid Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Parse(input.AsSpan());
    }

    /// <summary>
    /// Parses a CID from its canonical string form.
    /// </summary>
    /// <param name="input">The character span to parse.</param>
    /// <returns>The parsed <see cref="Cid"/>.</returns>
    /// <exception cref="CidParseException">The input does not satisfy the validation rules.</exception>
    public static Cid Parse(ReadOnlySpan<char> input)
    {
        if(input.Length == 0 || input[0] != StringPrefix)
        {
            throw new CidParseException(
                $"CID string must start with the literal '{StringPrefix}' prefix.");
        }

        ReadOnlySpan<char> body = input[1..];
        if(body.Length != StringLengthAfterPrefix)
        {
            throw new CidParseException(
                $"CID string must contain exactly {StringLengthAfterPrefix} base32 characters after the prefix; got {body.Length}.");
        }

        Span<byte> binary = stackalloc byte[BinaryLength];
        DecodeBase32(body, binary);
        return Parse((ReadOnlySpan<byte>)binary);
    }

    /// <summary>
    /// Parses a CID from its canonical 36-byte binary form.
    /// </summary>
    /// <param name="input">The bytes to parse.</param>
    /// <returns>The parsed <see cref="Cid"/>.</returns>
    /// <exception cref="CidParseException">The input does not satisfy the validation rules.</exception>
    public static Cid Parse(ReadOnlySpan<byte> input)
    {
        if(input.Length != BinaryLength)
        {
            throw new CidParseException(
                $"CID binary form must be exactly {BinaryLength} bytes; got {input.Length}.");
        }

        if(input[0] != Version)
        {
            throw new CidParseException(
                $"CID version byte must be 0x{Version:X2}; got 0x{input[0]:X2}.");
        }

        if(input[1] != (byte)CidCodec.Raw && input[1] != (byte)CidCodec.Drisl)
        {
            throw new CidParseException(
                $"CID codec byte must be 0x{(byte)CidCodec.Raw:X2} (raw) or 0x{(byte)CidCodec.Drisl:X2} (DRISL); got 0x{input[1]:X2}.");
        }

        if(input[2] != HashTypeSha256)
        {
            throw new CidParseException(
                $"CID hash-type byte must be 0x{HashTypeSha256:X2} (SHA-256); got 0x{input[2]:X2}.");
        }

        if(input[3] != HashLength32)
        {
            throw new CidParseException(
                $"CID hash-length byte must be 0x{HashLength32:X2} (32 bytes); got 0x{input[3]:X2}.");
        }

        return new Cid
        {
            Codec = (CidCodec)input[1],
            Digest = Digest32.FromSpan(input[4..])
        };
    }

    /// <summary>
    /// Parses a CID from its canonical 36-byte binary form WITHOUT materialising a <see cref="Cid"/>, yielding
    /// the validated codec and 32-byte digest directly. This is the zero-heap primitive for hot paths — a
    /// firehose reader comparing only digests across the thousands of CIDs in a repo snapshot avoids a
    /// per-CID <see cref="Cid"/> allocation. The validation is identical to
    /// <see cref="Parse(ReadOnlySpan{byte})"/>, which remains for callers that want a materialised
    /// <see cref="Cid"/> or the specific <see cref="CidParseException"/> messages on rejection.
    /// </summary>
    /// <param name="input">The bytes to parse.</param>
    /// <param name="codec">When this method returns <see langword="true"/>, the parsed codec; otherwise the default.</param>
    /// <param name="digest">When this method returns <see langword="true"/>, the parsed 32-byte digest; otherwise the default.</param>
    /// <returns><see langword="true"/> when <paramref name="input"/> is a valid canonical binary CID; otherwise <see langword="false"/>.</returns>
    public static bool TryParseDigest(ReadOnlySpan<byte> input, out CidCodec codec, out Digest32 digest)
    {
        codec = default;
        digest = default;

        if(input.Length != BinaryLength
            || input[0] != Version
            || (input[1] != (byte)CidCodec.Raw && input[1] != (byte)CidCodec.Drisl)
            || input[2] != HashTypeSha256
            || input[3] != HashLength32)
        {
            return false;
        }

        codec = (CidCodec)input[1];
        digest = Digest32.FromSpan(input[4..]);

        return true;
    }

    /// <summary>
    /// Decodes <paramref name="input"/> from lowercase RFC 4648 base32 (no
    /// padding) into <paramref name="output"/>. <paramref name="output"/>
    /// must be sized to <c>(input.Length * 5) / 8</c> bytes.
    /// </summary>
    /// <param name="input">The base32 characters to decode.</param>
    /// <param name="output">The destination byte span.</param>
    /// <exception cref="CidParseException">A character is outside the alphabet, or trailing bits are non-zero.</exception>
    private static void DecodeBase32(ReadOnlySpan<char> input, Span<byte> output)
    {
        int bitBuffer = 0;
        int bitCount = 0;
        int outIndex = 0;
        for(int i = 0; i < input.Length; i++)
        {
            int value = LookupAlphabet(input[i]);
            bitBuffer = (bitBuffer << 5) | value;
            bitCount += 5;
            if(bitCount >= 8)
            {
                bitCount -= 8;
                output[outIndex++] = (byte)((bitBuffer >> bitCount) & 0xFF);
            }
        }

        //Canonical-form check: any leftover bits in the buffer must be zero,
        //otherwise the input is a non-canonical encoding that would survive a
        //naive decode but fail to round-trip through the formatter.
        if(bitCount > 0)
        {
            int leftoverMask = (1 << bitCount) - 1;
            if((bitBuffer & leftoverMask) != 0)
            {
                throw new CidParseException(
                    "CID base32 encoding has non-zero trailing bits; the canonical no-padding form requires the unused low-order bits of the final character to be zero.");
            }
        }
    }

    private static int LookupAlphabet(char c)
    {
        if(c is >= 'a' and <= 'z')
        {
            return c - 'a';
        }
        if(c is >= '2' and <= '7')
        {
            return 26 + (c - '2');
        }
        throw new CidParseException(
            $"CID base32 character '{c}' is outside the lowercase alphabet 'a'..'z' and digits '2'..'7'.");
    }
}
