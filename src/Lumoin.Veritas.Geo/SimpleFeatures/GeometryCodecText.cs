using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// Text primitives shared by the serialization codec family (GeoJSON, GML,
/// KML): the shortest-round-trip number emission recipe, the finite-only
/// number-token parse, the geometry nesting bound, and the XML whitespace
/// predicate. The recipe matches the shipped WKT pair byte for byte so one
/// double formats identically across every text codec in the assembly.
/// </summary>
internal static class GeometryCodecText
{
    /// <summary>
    /// The maximum geometry nesting depth every codec reader certifies:
    /// thirty-one wrapping collections around one leaf parse, thirty-two
    /// wrappers refuse. The value matches the WKT reader's bound and moves
    /// only as a design amendment, never independently per format.
    /// </summary>
    public const int MaximumNestingDepth = 32;

    /// <summary>
    /// The transport-level structural depth every codec's tokenizer is
    /// configured to, sized above the worst structural cost of a
    /// geometry-nesting-bound document so transport never refuses a
    /// document the geometry bound accepts: a depth-32 collection nest
    /// costs roughly two structural levels per geometry level plus the
    /// leaf's own elements, and ninety-six leaves stated headroom.
    /// </summary>
    public const int MaximumTransportDepth = 96;

    /// <summary>The UTF-8 byte-order mark.</summary>
    public static ReadOnlySpan<byte> Utf8ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Answers whether the input opens with the UTF-8 byte-order mark.</summary>
    public static bool StartsWithByteOrderMark(ReadOnlySpan<byte> input)
    {
        return input.StartsWith(Utf8ByteOrderMark);
    }

    /// <summary>
    /// Writes one coordinate in shortest-round-trip invariant form,
    /// formatted straight into the destination's own span — no character
    /// intermediate.
    /// </summary>
    public static void WriteNumber(double value, IBufferWriter<byte> destination)
    {
        Span<byte> span = destination.GetSpan(32);
        bool formatted = value.TryFormat(span, out int bytesWritten, format: default, CultureInfo.InvariantCulture);

        if(!formatted)
        {
            //The shortest round-trip form of any finite double fits 32 bytes; a failure
            //here is a sizing defect, not a data condition.
            throw new InvalidOperationException("A coordinate did not fit the number buffer.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>
    /// Parses one complete number token to a finite double: the token must
    /// start with a digit, sign, or dot byte so the numeric parser's
    /// <c>NaN</c>/<c>Infinity</c> spellings are unreachable, the whole token
    /// must be consumed so malformed tails are never silently accepted, and
    /// the parsed value must be finite so overflow of a syntactically finite
    /// token is refused rather than carried.
    /// </summary>
    public static bool TryParseFiniteDouble(ReadOnlySpan<byte> token, out double value)
    {
        value = 0;

        if(token.Length == 0 || !IsNumberStart(token[0]))
        {
            return false;
        }

        if(!Utf8Parser.TryParse(token, out value, out int consumed) || consumed != token.Length || !double.IsFinite(value))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Answers whether a byte opens a number token: an ASCII digit, a sign,
    /// or a dot.
    /// </summary>
    public static bool IsNumberStart(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.';
    }
}
