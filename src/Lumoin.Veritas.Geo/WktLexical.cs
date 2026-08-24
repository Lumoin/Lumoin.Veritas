using System;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// A span recognizer for the lexical shape of well-known-text geometry: a case-insensitive geometry tag,
/// an optional <c>Z</c>/<c>M</c>/<c>ZM</c> dimension modifier, and either the <c>EMPTY</c> keyword or a
/// balanced parenthesized body of coordinate positions — with nested tagged geometries inside
/// <c>GEOMETRYCOLLECTION</c>. The scan is one forward pass over an explicit frame stack bounded by
/// <see cref="MaximumNestingDepth"/>, with no recursion and no runtime regular expressions.
/// </summary>
/// <remarks>
/// <para>
/// The tag roster is closed by the referenced standards, so a body whose leading tag is outside it is
/// <see cref="GeometryLexicalRecognition.Malformed"/>, never an abstention. The certified content
/// grammars are the Simple Features tags; the curve tags (<c>CIRCULARSTRING</c>, <c>COMPOUNDCURVE</c>,
/// <c>CURVEPOLYGON</c>, <c>MULTICURVE</c>, <c>MULTISURFACE</c>) are recognized as roster members but
/// their mixed tagged-and-bare content grammar is not certified, so they answer
/// <see cref="GeometryLexicalRecognition.Unrecognized"/>.
/// </para>
/// <para>
/// The recognizer is lexical: it certifies token shape and nesting structure, not geometry semantics. A
/// coordinate position is two to four numbers regardless of the dimension modifier, and minimum position
/// counts per geometry kind are not enforced, so no form a standard admits is ever rejected.
/// </para>
/// </remarks>
public static class WktLexical
{
    /// <summary>
    /// The hard cap on parenthesis nesting depth. A body needing more open parentheses than this answers
    /// <see cref="GeometryLexicalRecognition.DepthExceeded"/> instead of being scanned further.
    /// </summary>
    public const int MaximumNestingDepth = 32;

    /// <summary>What one open parenthesis level of a WKT body expects as its items.</summary>
    private enum FrameKind : byte
    {
        /// <summary>Tagged geometries separated by commas (<c>GEOMETRYCOLLECTION</c> content).</summary>
        GeometryItems,

        /// <summary>Coordinate positions separated by commas (<c>LINESTRING</c> content, ring content).</summary>
        Positions,

        /// <summary>Parenthesized position lists separated by commas (<c>POLYGON</c>, <c>TRIANGLE</c>, <c>MULTILINESTRING</c> content).</summary>
        Rings,

        /// <summary>Parenthesized ring lists separated by commas (<c>MULTIPOLYGON</c>, <c>POLYHEDRALSURFACE</c>, <c>TIN</c> content).</summary>
        RingLists,

        /// <summary>Exactly one coordinate position with no comma (<c>POINT</c> content, a parenthesized <c>MULTIPOINT</c> item).</summary>
        PointSingle,

        /// <summary>Bare positions or parenthesized single positions separated by commas (<c>MULTIPOINT</c> content).</summary>
        MultiPointItems,
    }

    /// <summary>The scan state of one open parenthesis level.</summary>
    private struct Frame
    {
        /// <summary>What this level expects as its items.</summary>
        public FrameKind Kind;

        /// <summary>How many numbers the current coordinate position has accumulated at this level.</summary>
        public int NumbersInPosition;

        /// <summary>Whether the next non-whitespace token must begin a new item at this level.</summary>
        public bool ExpectingItem;
    }

    /// <summary>Lexically recognizes one WKT geometry body, without any CRS IRI prefix.</summary>
    /// <param name="body">The candidate WKT text as UTF-8 bytes.</param>
    /// <param name="offendingOffset">
    /// The scan position at which recognition stopped, as an offset into <paramref name="body"/>: the first
    /// offending byte of a malformed body, the byte after the tag whose content grammar is uncertified for an
    /// abstention, the opening parenthesis exceeding <see cref="MaximumNestingDepth"/> for a depth refusal, and
    /// the body length where the text ends before the geometry closes. A well-formed body reports minus one.
    /// </param>
    /// <returns>The recognition outcome; an empty or all-whitespace body is well-formed (an empty geometry).</returns>
    public static GeometryLexicalRecognition Recognize(ReadOnlySpan<byte> body, out int offendingOffset)
    {
        int index = 0;
        SkipWhitespace(body, ref index);
        if(index == body.Length)
        {
            offendingOffset = -1;

            return GeometryLexicalRecognition.WellFormed;
        }

        Span<Frame> frames = stackalloc Frame[MaximumNestingDepth];
        int depth = 0;
        bool expectTag = true;

        while(true)
        {
            SkipWhitespace(body, ref index);

            if(expectTag)
            {
                ReadOnlySpan<byte> tag = ReadIdentifier(body, ref index);
                if(tag.IsEmpty)
                {
                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                if(IsCurvedTag(tag))
                {
                    offendingOffset = index;

                    return GeometryLexicalRecognition.Unrecognized;
                }

                if(!TryClassifyTag(tag, out FrameKind contentKind))
                {
                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                SkipWhitespace(body, ref index);

                //An optional Z/M/ZM dimension modifier, or the EMPTY keyword completing the geometry.
                bool complete = false;
                if(index < body.Length && IsAsciiLetter(body[index]))
                {
                    ReadOnlySpan<byte> word = ReadIdentifier(body, ref index);
                    if(MatchesKeyword(word, "empty"u8))
                    {
                        complete = true;
                    }
                    else if(MatchesKeyword(word, "z"u8) || MatchesKeyword(word, "m"u8) || MatchesKeyword(word, "zm"u8))
                    {
                        SkipWhitespace(body, ref index);
                        if(index < body.Length && IsAsciiLetter(body[index]))
                        {
                            ReadOnlySpan<byte> second = ReadIdentifier(body, ref index);
                            if(!MatchesKeyword(second, "empty"u8))
                            {
                                offendingOffset = index;

                                return GeometryLexicalRecognition.Malformed;
                            }

                            complete = true;
                        }
                    }
                    else
                    {
                        offendingOffset = index;

                        return GeometryLexicalRecognition.Malformed;
                    }
                }

                if(complete)
                {
                    if(depth == 0)
                    {
                        return FinishTopLevel(body, ref index, out offendingOffset);
                    }

                    //The completed EMPTY geometry is an item of the enclosing collection level.
                    frames[depth - 1].ExpectingItem = false;
                    expectTag = false;
                    continue;
                }

                SkipWhitespace(body, ref index);
                if(index == body.Length || body[index] != (byte)'(')
                {
                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                if(depth == MaximumNestingDepth)
                {
                    offendingOffset = index;

                    return GeometryLexicalRecognition.DepthExceeded;
                }

                frames[depth] = new Frame { Kind = contentKind, NumbersInPosition = 0, ExpectingItem = true };
                depth++;
                index++;
                expectTag = false;
                continue;
            }

            if(index == body.Length)
            {
                offendingOffset = index;

                return GeometryLexicalRecognition.Malformed;
            }

            byte current = body[index];
            ref Frame top = ref frames[depth - 1];
            switch(top.Kind)
            {
                case(FrameKind.GeometryItems):
                {
                    if(top.ExpectingItem)
                    {
                        if(IsAsciiLetter(current))
                        {
                            expectTag = true;
                            continue;
                        }

                        offendingOffset = index;

                        return GeometryLexicalRecognition.Malformed;
                    }

                    if(current == (byte)',')
                    {
                        top.ExpectingItem = true;
                        index++;
                        continue;
                    }

                    if(current == (byte)')')
                    {
                        index++;
                        depth--;
                        if(depth == 0)
                        {
                            return FinishTopLevel(body, ref index, out offendingOffset);
                        }

                        frames[depth - 1].ExpectingItem = false;
                        continue;
                    }

                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                case(FrameKind.Positions):
                case(FrameKind.PointSingle):
                {
                    if(IsNumberStart(current))
                    {
                        if(!TryReadNumber(body, ref index))
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        top.NumbersInPosition++;
                        if(top.NumbersInPosition > 4)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        continue;
                    }

                    if(current == (byte)',')
                    {
                        if(top.Kind == FrameKind.PointSingle || top.NumbersInPosition < 2)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        top.NumbersInPosition = 0;
                        index++;
                        continue;
                    }

                    if(current == (byte)')')
                    {
                        if(top.NumbersInPosition < 2)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        index++;
                        depth--;
                        if(depth == 0)
                        {
                            return FinishTopLevel(body, ref index, out offendingOffset);
                        }

                        frames[depth - 1].ExpectingItem = false;
                        continue;
                    }

                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                case(FrameKind.Rings):
                case(FrameKind.RingLists):
                {
                    if(top.ExpectingItem)
                    {
                        if(current == (byte)'(')
                        {
                            if(depth == MaximumNestingDepth)
                            {
                                offendingOffset = index;

                                return GeometryLexicalRecognition.DepthExceeded;
                            }

                            FrameKind itemKind = top.Kind == FrameKind.Rings ? FrameKind.Positions : FrameKind.Rings;
                            frames[depth] = new Frame { Kind = itemKind, NumbersInPosition = 0, ExpectingItem = true };
                            depth++;
                            index++;
                            continue;
                        }

                        offendingOffset = index;

                        return GeometryLexicalRecognition.Malformed;
                    }

                    if(current == (byte)',')
                    {
                        top.ExpectingItem = true;
                        index++;
                        continue;
                    }

                    if(current == (byte)')')
                    {
                        index++;
                        depth--;
                        if(depth == 0)
                        {
                            return FinishTopLevel(body, ref index, out offendingOffset);
                        }

                        frames[depth - 1].ExpectingItem = false;
                        continue;
                    }

                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                case(FrameKind.MultiPointItems):
                {
                    if(top.ExpectingItem && current == (byte)'(' && top.NumbersInPosition == 0)
                    {
                        if(depth == MaximumNestingDepth)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.DepthExceeded;
                        }

                        frames[depth] = new Frame { Kind = FrameKind.PointSingle, NumbersInPosition = 0, ExpectingItem = false };
                        depth++;
                        index++;
                        continue;
                    }

                    if(top.ExpectingItem && IsNumberStart(current))
                    {
                        if(!TryReadNumber(body, ref index))
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        top.NumbersInPosition++;
                        if(top.NumbersInPosition > 4)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        continue;
                    }

                    bool itemComplete = (!top.ExpectingItem && top.NumbersInPosition == 0) || top.NumbersInPosition >= 2;
                    if(current == (byte)',')
                    {
                        if(!itemComplete)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        top.ExpectingItem = true;
                        top.NumbersInPosition = 0;
                        index++;
                        continue;
                    }

                    if(current == (byte)')')
                    {
                        if(!itemComplete)
                        {
                            offendingOffset = index;

                            return GeometryLexicalRecognition.Malformed;
                        }

                        index++;
                        depth--;
                        if(depth == 0)
                        {
                            return FinishTopLevel(body, ref index, out offendingOffset);
                        }

                        frames[depth - 1].ExpectingItem = false;
                        continue;
                    }

                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }

                default:
                {
                    offendingOffset = index;

                    return GeometryLexicalRecognition.Malformed;
                }
            }
        }
    }

    /// <summary>
    /// Completes a scan that closed its outermost level: nothing but whitespace may follow the geometry.
    /// </summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position at the outermost close, advanced past trailing whitespace.</param>
    /// <param name="offendingOffset">The first trailing non-whitespace byte, or minus one when nothing follows.</param>
    /// <returns>Well-formed when the text ends there; otherwise malformed.</returns>
    private static GeometryLexicalRecognition FinishTopLevel(ReadOnlySpan<byte> body, ref int index, out int offendingOffset)
    {
        SkipWhitespace(body, ref index);

        if(index == body.Length)
        {
            offendingOffset = -1;

            return GeometryLexicalRecognition.WellFormed;
        }

        offendingOffset = index;

        return GeometryLexicalRecognition.Malformed;
    }

    /// <summary>Whether the byte is WKT whitespace: space, tab, carriage return, or line feed.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a whitespace byte.</returns>
    internal static bool IsWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>Advances the index past any whitespace.</summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past whitespace.</param>
    private static void SkipWhitespace(ReadOnlySpan<byte> body, ref int index)
    {
        while(index < body.Length && IsWhitespace(body[index]))
        {
            index++;
        }
    }

    /// <summary>Whether the byte is an ASCII letter.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>A</c>-<c>Z</c> or <c>a</c>-<c>z</c>.</returns>
    private static bool IsAsciiLetter(byte value)
    {
        return (uint)((value | 0x20) - (byte)'a') <= 'z' - 'a';
    }

    /// <summary>Whether the byte is an ASCII digit.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for <c>0</c>-<c>9</c>.</returns>
    private static bool IsAsciiDigit(byte value)
    {
        return (uint)(value - (byte)'0') <= 9;
    }

    /// <summary>Whether the byte can begin a WKT number: a digit, sign, or decimal point.</summary>
    /// <param name="value">The byte under test.</param>
    /// <returns><see langword="true"/> for a number-start byte.</returns>
    private static bool IsNumberStart(byte value)
    {
        return IsAsciiDigit(value) || value is (byte)'+' or (byte)'-' or (byte)'.';
    }

    /// <summary>Reads a maximal run of ASCII letters.</summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past the identifier.</param>
    /// <returns>The identifier bytes; empty when the position does not start a letter.</returns>
    private static ReadOnlySpan<byte> ReadIdentifier(ReadOnlySpan<byte> body, ref int index)
    {
        int start = index;
        while(index < body.Length && IsAsciiLetter(body[index]))
        {
            index++;
        }

        return body[start..index];
    }

    /// <summary>
    /// Reads one WKT number: an optional sign, digits with an optional decimal point, and an optional
    /// exponent — terminated by whitespace, a comma, a closing parenthesis, or the end of the text.
    /// </summary>
    /// <param name="body">The text being scanned.</param>
    /// <param name="index">The scan position, advanced past the number when it is valid.</param>
    /// <returns><see langword="true"/> when a valid number was read.</returns>
    private static bool TryReadNumber(ReadOnlySpan<byte> body, ref int index)
    {
        int i = index;
        if(i < body.Length && body[i] is (byte)'+' or (byte)'-')
        {
            i++;
        }

        int digits = 0;
        while(i < body.Length && IsAsciiDigit(body[i]))
        {
            i++;
            digits++;
        }

        if(i < body.Length && body[i] == (byte)'.')
        {
            i++;
            while(i < body.Length && IsAsciiDigit(body[i]))
            {
                i++;
                digits++;
            }
        }

        if(digits == 0)
        {
            return false;
        }

        if(i < body.Length && (body[i] | 0x20) == (byte)'e')
        {
            i++;
            if(i < body.Length && body[i] is (byte)'+' or (byte)'-')
            {
                i++;
            }

            int exponentDigits = 0;
            while(i < body.Length && IsAsciiDigit(body[i]))
            {
                i++;
                exponentDigits++;
            }

            if(exponentDigits == 0)
            {
                return false;
            }
        }

        if(i < body.Length && !IsWhitespace(body[i]) && body[i] != (byte)',' && body[i] != (byte)')')
        {
            return false;
        }

        index = i;

        return true;
    }

    /// <summary>Compares an identifier against a lowercase keyword, ASCII case-insensitively.</summary>
    /// <param name="identifier">The identifier read from the text.</param>
    /// <param name="lowerKeyword">The keyword in lowercase bytes.</param>
    /// <returns><see langword="true"/> when the identifier is the keyword in any casing.</returns>
    private static bool MatchesKeyword(ReadOnlySpan<byte> identifier, ReadOnlySpan<byte> lowerKeyword)
    {
        if(identifier.Length != lowerKeyword.Length)
        {
            return false;
        }

        for(int i = 0; i < identifier.Length; i++)
        {
            if((identifier[i] | 0x20) != lowerKeyword[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the tag is one of the roster's curve tags, whose content grammar the recognizer does not
    /// certify.
    /// </summary>
    /// <param name="tag">The tag identifier.</param>
    /// <returns><see langword="true"/> for a curve tag.</returns>
    private static bool IsCurvedTag(ReadOnlySpan<byte> tag)
    {
        return MatchesKeyword(tag, "circularstring"u8)
            || MatchesKeyword(tag, "compoundcurve"u8)
            || MatchesKeyword(tag, "curvepolygon"u8)
            || MatchesKeyword(tag, "multicurve"u8)
            || MatchesKeyword(tag, "multisurface"u8);
    }

    /// <summary>Maps a certified Simple Features tag to the content its parenthesized body carries.</summary>
    /// <param name="tag">The tag identifier.</param>
    /// <param name="contentKind">The content kind of the tag's body.</param>
    /// <returns><see langword="true"/> when the tag is certified; otherwise the tag is outside the roster.</returns>
    private static bool TryClassifyTag(ReadOnlySpan<byte> tag, out FrameKind contentKind)
    {
        if(MatchesKeyword(tag, "point"u8))
        {
            contentKind = FrameKind.PointSingle;

            return true;
        }

        if(MatchesKeyword(tag, "linestring"u8))
        {
            contentKind = FrameKind.Positions;

            return true;
        }

        if(MatchesKeyword(tag, "polygon"u8) || MatchesKeyword(tag, "triangle"u8) || MatchesKeyword(tag, "multilinestring"u8))
        {
            contentKind = FrameKind.Rings;

            return true;
        }

        if(MatchesKeyword(tag, "multipolygon"u8) || MatchesKeyword(tag, "polyhedralsurface"u8) || MatchesKeyword(tag, "tin"u8))
        {
            contentKind = FrameKind.RingLists;

            return true;
        }

        if(MatchesKeyword(tag, "multipoint"u8))
        {
            contentKind = FrameKind.MultiPointItems;

            return true;
        }

        if(MatchesKeyword(tag, "geometrycollection"u8))
        {
            contentKind = FrameKind.GeometryItems;

            return true;
        }

        contentKind = default;

        return false;
    }
}
