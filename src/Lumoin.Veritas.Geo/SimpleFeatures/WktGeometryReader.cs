using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The WKT reader of the Simple Features substrate: parses geometry text into a
/// <see cref="FlatGeometry"/>. The grammar is the datatype layer's certified
/// value space, never wider where that layer rejects: case-insensitive tags, separated
/// ordinate markers (<c>POINT Z (1 2 3)</c>; glued <c>POINTZ(</c> is malformed), bare
/// <c>EMPTY</c> at every tagged level, both multipoint member spellings (but no
/// <c>EMPTY</c> members inside multi kinds), nesting to depth 32, and finite invariant
/// numbers only — <c>NaN</c>/<c>Infinity</c> spellings never reach the numeric parser
/// because every number token is gated on a leading digit, sign, or dot byte.
/// <c>TRIANGLE</c> normalizes to a polygon, <c>TIN</c> and <c>POLYHEDRALSURFACE</c> to
/// multipolygons; curve tags, EWKT SRID prefixes, and CRS IRIs are malformed here (the
/// host strips its CRS prefix upstream). Structural validity is enforced beyond the
/// lexical layer: rings close on XY and carry at least four positions, linestrings at
/// least two, and ordinate arity is uniform within one tagged geometry. Every rejection
/// is returned by value as a <see cref="GeometryCodecRefusal"/> naming the reason and
/// the first offending byte — nothing is thrown, and a refused read rents nothing.
/// </summary>
public static class WktGeometryReader
{
    private const int MaximumNestingDepth = 32;

    /// <summary>
    /// Parses UTF-8 WKT into a <see cref="FlatGeometry"/> with heap-backed columns.
    /// Returns false on any lexical or structural violation, including trailing
    /// content after the geometry, and reports the refusal that stopped the read.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> utf8Text, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryRead(utf8Text, FlatGeometryAllocators.Default, out geometry, out refusal);
    }

    /// <summary>
    /// Parses UTF-8 WKT, renting the vertex-scale columns through the caller's
    /// allocator seam — a pooling host binds its own pool here and then owns the built
    /// geometry's disposal. An allocator that violates the exact-length contract makes
    /// the build throw; a parse failure disposes nothing (no rental happens before the
    /// text is accepted) and reports the refusal that stopped the read.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> utf8Text, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        var builder = new FlatGeometryBuilder();
        int position = 0;

        if(!ParseGeometryTree(utf8Text, ref position, builder, out refusal))
        {
            geometry = default;

            return false;
        }

        SkipWhitespace(utf8Text, ref position);

        if(position != utf8Text.Length)
        {
            geometry = default;
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.TrailingContent, position);

            return false;
        }

        geometry = builder.ToGeometry(allocators);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses WKT from characters by transcoding to UTF-8 first; a convenience overload
    /// for hosts and tests, off the byte-oriented primary path. Refusal offsets index
    /// the transcoded UTF-8 representation, not character positions.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<char> text, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryRead(text, FlatGeometryAllocators.Default, out geometry, out refusal);
    }

    /// <summary>
    /// Parses WKT from characters through the caller's allocator seam; see the
    /// byte-oriented overload for the rental contract. Refusal offsets index the
    /// transcoded UTF-8 representation, not character positions.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<char> text, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        byte[] utf8Text = new byte[Encoding.UTF8.GetByteCount(text)];
        Encoding.UTF8.GetBytes(text, utf8Text);

        return TryRead(utf8Text, allocators, out geometry, out refusal);
    }

    /// <summary>
    /// Parses one tagged geometry and its nested members with an explicit collection
    /// stack; on success the builder holds the completed scratch tree and the refusal
    /// is <see cref="GeometryCodecRefusal.None"/>.
    /// </summary>
    private static bool ParseGeometryTree(ReadOnlySpan<byte> text, ref int position, FlatGeometryBuilder builder, out GeometryCodecRefusal refusal)
    {
        //Frames exist only for geometry collections; every other kind parses inline.
        var frames = new Stack<CollectionFrame>();

        while(true)
        {
            if(frames.Count + 1 > MaximumNestingDepth)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NestingTooDeep, position);

                return false;
            }

            if(!ParseTaggedHeader(text, ref position, out GeometryKind kind, out bool hasZ, out bool hasM, out bool isEmpty, out refusal))
            {
                return false;
            }

            int nodeIndex;

            if(kind == GeometryKind.GeometryCollection)
            {
                nodeIndex = builder.AddNode(kind, hasZ, hasM, firstPart: 0, partCount: 0);

                if(!isEmpty)
                {
                    if(!Expect(text, ref position, (byte)'(', out refusal))
                    {
                        return false;
                    }

                    frames.Push(new CollectionFrame(nodeIndex, new List<int>()));

                    //The next loop iteration parses the first member.
                    continue;
                }
            }
            else
            {
                if(!ParseLeafBody(text, ref position, builder, kind, hasZ, hasM, isEmpty, out nodeIndex, out refusal))
                {
                    return false;
                }
            }

            //A tagged geometry is complete; attach it upward, closing any collections
            //whose member list ends here.
            while(true)
            {
                if(frames.Count == 0)
                {
                    builder.RootIndex = nodeIndex;
                    refusal = GeometryCodecRefusal.None;

                    return true;
                }

                CollectionFrame frame = frames.Peek();
                frame.Children.Add(nodeIndex);

                SkipWhitespace(text, ref position);

                if(position < text.Length && text[position] == (byte)',')
                {
                    position++;

                    break;
                }

                if(position < text.Length && text[position] == (byte)')')
                {
                    position++;
                    frames.Pop();
                    builder.SetChildren(frame.NodeIndex, frame.Children);
                    nodeIndex = frame.NodeIndex;

                    continue;
                }

                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

                return false;
            }
        }
    }

    /// <summary>
    /// Reads a geometry tag, its optional separated ordinate marker, and whether the
    /// body is the <c>EMPTY</c> keyword; the cursor stops before <c>(</c> otherwise. A
    /// tag outside the roster refuses as unsupported at the tag's first byte, and every
    /// other break refuses as malformed at the cursor.
    /// </summary>
    private static bool ParseTaggedHeader(
        ReadOnlySpan<byte> text, ref int position, out GeometryKind kind, out bool hasZ, out bool hasM, out bool isEmpty, out GeometryCodecRefusal refusal)
    {
        kind = default;
        hasZ = false;
        hasM = false;
        isEmpty = false;

        SkipWhitespace(text, ref position);

        int tagStart = position;

        if(!TryReadIdentifier(text, ref position, out ReadOnlySpan<byte> tag))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }

        if(!TryClassifyTag(tag, out kind))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, tagStart);

            return false;
        }

        SkipWhitespace(text, ref position);

        if(TryPeekIdentifier(text, position, out ReadOnlySpan<byte> word, out int afterWord))
        {
            if(IdentifierEquals(word, "ZM"u8))
            {
                hasZ = true;
                hasM = true;
                position = afterWord;
                SkipWhitespace(text, ref position);
            }
            else if(IdentifierEquals(word, "Z"u8))
            {
                hasZ = true;
                position = afterWord;
                SkipWhitespace(text, ref position);
            }
            else if(IdentifierEquals(word, "M"u8))
            {
                hasM = true;
                position = afterWord;
                SkipWhitespace(text, ref position);
            }
        }

        if(TryPeekIdentifier(text, position, out word, out afterWord) && IdentifierEquals(word, "EMPTY"u8))
        {
            isEmpty = true;
            position = afterWord;
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        if(position < text.Length && text[position] == (byte)'(')
        {
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

        return false;
    }

    /// <summary>
    /// Parses the parenthesized body of a non-collection kind into parts and vertices;
    /// a kind carrying no leaf body refuses as unsupported at the cursor.
    /// </summary>
    private static bool ParseLeafBody(
        ReadOnlySpan<byte> text,
        ref int position,
        FlatGeometryBuilder builder,
        GeometryKind kind,
        bool hasZ,
        bool hasM,
        bool isEmpty,
        out int nodeIndex,
        out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        if(isEmpty)
        {
            nodeIndex = builder.AddNode(kind, hasZ, hasM, firstPart: 0, partCount: 0);
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        //A marker fixes the position arity exactly; without one the first position
        //infers it (2 = XY, 3 = XYZ, 4 = XYZM) and every later position must match.
        //Only an M marker without Z makes the third ordinate a measure.
        int arity = hasZ || hasM ? 3 + ((hasZ && hasM) ? 1 : 0) : 0;
        bool thirdIsM = hasM && !hasZ;
        bool inferZ = hasZ;
        bool inferM = hasM;
        int firstPart = builder.PartCount;
        bool parsed;

        switch(kind)
        {
            case GeometryKind.Point:
            {
                parsed = ParsePointBody(text, ref position, builder, ref arity, thirdIsM, out refusal);

                break;
            }

            case GeometryKind.LineString:
            {
                parsed = ParseLineBody(text, ref position, builder, ref arity, thirdIsM, FlatGeometryPartRole.Line, out refusal);

                break;
            }

            case GeometryKind.Polygon:
            {
                parsed = ParsePolygonBody(text, ref position, builder, ref arity, thirdIsM, out refusal);

                break;
            }

            case GeometryKind.MultiPoint:
            {
                parsed = ParseMultiPointBody(text, ref position, builder, ref arity, thirdIsM, out refusal);

                break;
            }

            case GeometryKind.MultiLineString:
            {
                parsed = ParseListBody(text, ref position, builder, ref arity, thirdIsM, polygonItems: false, out refusal);

                break;
            }

            case GeometryKind.MultiPolygon:
            {
                parsed = ParseListBody(text, ref position, builder, ref arity, thirdIsM, polygonItems: true, out refusal);

                break;
            }

            default:
            {
                parsed = false;
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, position);

                break;
            }
        }

        if(!parsed)
        {
            return false;
        }

        if(!inferZ && !inferM && arity > 2)
        {
            inferZ = true;
            inferM = arity == 4;
        }

        nodeIndex = builder.AddNode(kind, inferZ, inferM, firstPart, builder.PartCount - firstPart);

        return true;
    }

    /// <summary>Parses <c>( position )</c> for a point.</summary>
    private static bool ParsePointBody(
        ReadOnlySpan<byte> text, ref int position, FlatGeometryBuilder builder, ref int arity, bool thirdIsM, out GeometryCodecRefusal refusal)
    {
        if(!Expect(text, ref position, (byte)'(', out refusal))
        {
            return false;
        }

        int start = builder.VertexCount;

        if(!ParsePosition(text, ref position, builder, ref arity, thirdIsM, out refusal))
        {
            return false;
        }

        if(!Expect(text, ref position, (byte)')', out refusal))
        {
            return false;
        }

        builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));

        return true;
    }

    /// <summary>
    /// Parses <c>( position, position, … )</c> into one run of the given role, enforcing the role's minimum
    /// count and ring closure; a count or closure shortfall refuses as a structural violation at the byte
    /// closing the run.
    /// </summary>
    private static bool ParseLineBody(
        ReadOnlySpan<byte> text,
        ref int position,
        FlatGeometryBuilder builder,
        ref int arity,
        bool thirdIsM,
        FlatGeometryPartRole role,
        out GeometryCodecRefusal refusal)
    {
        if(!Expect(text, ref position, (byte)'(', out refusal))
        {
            return false;
        }

        int start = builder.VertexCount;
        int runEnd = position;

        while(true)
        {
            if(!ParsePosition(text, ref position, builder, ref arity, thirdIsM, out refusal))
            {
                return false;
            }

            SkipWhitespace(text, ref position);

            if(position < text.Length && text[position] == (byte)',')
            {
                position++;

                continue;
            }

            if(position < text.Length && text[position] == (byte)')')
            {
                runEnd = position;
                position++;

                break;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }

        int length = builder.VertexCount - start;
        bool isRing = role is FlatGeometryPartRole.ExteriorRing or FlatGeometryPartRole.InteriorRing;

        if(isRing)
        {
            if(length < 4 || !builder.VerticesEqualXy(start, start + length - 1))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runEnd);

                return false;
            }
        }
        else if(length < 2)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runEnd);

            return false;
        }

        builder.AddPart(new FlatGeometryPart(start, length, role));

        return true;
    }

    /// <summary>Parses <c>( ring, ring, … )</c>: one exterior ring then interior rings.</summary>
    private static bool ParsePolygonBody(
        ReadOnlySpan<byte> text, ref int position, FlatGeometryBuilder builder, ref int arity, bool thirdIsM, out GeometryCodecRefusal refusal)
    {
        if(!Expect(text, ref position, (byte)'(', out refusal))
        {
            return false;
        }

        bool first = true;

        while(true)
        {
            FlatGeometryPartRole role = first ? FlatGeometryPartRole.ExteriorRing : FlatGeometryPartRole.InteriorRing;

            if(!ParseLineBody(text, ref position, builder, ref arity, thirdIsM, role, out refusal))
            {
                return false;
            }

            first = false;
            SkipWhitespace(text, ref position);

            if(position < text.Length && text[position] == (byte)',')
            {
                position++;

                continue;
            }

            if(position < text.Length && text[position] == (byte)')')
            {
                position++;

                return true;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }
    }

    /// <summary>
    /// Parses the multipoint body, each member either a bare position or a
    /// parenthesized one; an <c>EMPTY</c> member refuses as an unrepresentable empty,
    /// matching <see cref="WktLexical"/>'s verdict.
    /// </summary>
    private static bool ParseMultiPointBody(
        ReadOnlySpan<byte> text, ref int position, FlatGeometryBuilder builder, ref int arity, bool thirdIsM, out GeometryCodecRefusal refusal)
    {
        if(!Expect(text, ref position, (byte)'(', out refusal))
        {
            return false;
        }

        while(true)
        {
            SkipWhitespace(text, ref position);
            int start = builder.VertexCount;

            if(TryPeekIdentifier(text, position, out ReadOnlySpan<byte> member, out _) && IdentifierEquals(member, "EMPTY"u8))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.EmptyUnrepresentable, position);

                return false;
            }

            if(position < text.Length && text[position] == (byte)'(')
            {
                position++;

                if(!ParsePosition(text, ref position, builder, ref arity, thirdIsM, out refusal) || !Expect(text, ref position, (byte)')', out refusal))
                {
                    return false;
                }
            }
            else if(!ParsePosition(text, ref position, builder, ref arity, thirdIsM, out refusal))
            {
                return false;
            }

            builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));
            SkipWhitespace(text, ref position);

            if(position < text.Length && text[position] == (byte)',')
            {
                position++;

                continue;
            }

            if(position < text.Length && text[position] == (byte)')')
            {
                position++;

                return true;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }
    }

    /// <summary>
    /// Parses <c>( item, item, … )</c> where each item is a linestring body
    /// (multilinestring) or a polygon body (multipolygon and the normalized surface tags);
    /// an <c>EMPTY</c> member refuses as an unrepresentable empty.
    /// </summary>
    private static bool ParseListBody(
        ReadOnlySpan<byte> text,
        ref int position,
        FlatGeometryBuilder builder,
        ref int arity,
        bool thirdIsM,
        bool polygonItems,
        out GeometryCodecRefusal refusal)
    {
        if(!Expect(text, ref position, (byte)'(', out refusal))
        {
            return false;
        }

        while(true)
        {
            SkipWhitespace(text, ref position);

            if(TryPeekIdentifier(text, position, out ReadOnlySpan<byte> member, out _) && IdentifierEquals(member, "EMPTY"u8))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.EmptyUnrepresentable, position);

                return false;
            }

            bool parsed = polygonItems
                ? ParsePolygonBody(text, ref position, builder, ref arity, thirdIsM, out refusal)
                : ParseLineBody(text, ref position, builder, ref arity, thirdIsM, FlatGeometryPartRole.Line, out refusal);

            if(!parsed)
            {
                return false;
            }

            SkipWhitespace(text, ref position);

            if(position < text.Length && text[position] == (byte)',')
            {
                position++;

                continue;
            }

            if(position < text.Length && text[position] == (byte)')')
            {
                position++;

                return true;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }
    }

    /// <summary>
    /// Parses one position of two to four numbers, inferring or enforcing the arity;
    /// absent Z and M ordinates record <see cref="double.NaN"/> in their columns. A
    /// position short of two numbers refuses as malformed and an arity disagreement as a
    /// dimension mismatch, both at the byte where the position stopped.
    /// </summary>
    private static bool ParsePosition(
        ReadOnlySpan<byte> text, ref int position, FlatGeometryBuilder builder, ref int arity, bool thirdIsM, out GeometryCodecRefusal refusal)
    {
        Span<double> ordinates = stackalloc double[4];
        int count = 0;

        while(count < 4)
        {
            SkipWhitespace(text, ref position);

            if(!TryReadNumber(text, ref position, out double value, out GeometryCodecRefusal numberRefusal))
            {
                //A cursor that starts no number token simply ends the position; a token
                //that started and then broke is the offense itself.
                if(numberRefusal.Kind != GeometryCodecRefusalKind.None)
                {
                    refusal = numberRefusal;

                    return false;
                }

                break;
            }

            ordinates[count] = value;
            count++;

            //Numbers are whitespace-separated inside a position; a comma or closing
            //paren ends it.
            int peek = position;
            SkipWhitespace(text, ref peek);

            if(peek < text.Length && (text[peek] == (byte)',' || text[peek] == (byte)')'))
            {
                break;
            }
        }

        if(count < 2)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }

        if(arity == 0)
        {
            arity = count;
        }
        else if(count != arity)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, position);

            return false;
        }

        builder.AddVertex(
            new Point2d(ordinates[0], ordinates[1]),
            arity >= 3 && !thirdIsM ? ordinates[2] : double.NaN,
            arity == 4 ? ordinates[3] : (thirdIsM && arity >= 3 ? ordinates[2] : double.NaN));

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Reads one number token, gated on a leading digit, sign, or dot byte so the
    /// numeric parser's <c>NaN</c>/<c>Infinity</c> path is unreachable, and requiring
    /// the whole token to parse so malformed tails are never silently consumed. A cursor
    /// that begins no token reports no refusal — the caller's position ends there —
    /// while a token that parses to a non-finite value refuses as a non-finite
    /// coordinate and one that does not parse whole refuses as malformed, both at the
    /// token's first byte.
    /// </summary>
    private static bool TryReadNumber(ReadOnlySpan<byte> text, ref int position, out double value, out GeometryCodecRefusal refusal)
    {
        value = 0;

        if(position >= text.Length || !IsNumberStart(text[position]))
        {
            refusal = GeometryCodecRefusal.None;

            return false;
        }

        int end = position;

        while(end < text.Length && IsNumberByte(text[end]))
        {
            end++;
        }

        if(!Utf8Parser.TryParse(text[position..end], out value, out int consumed) || consumed != end - position)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

            return false;
        }

        if(!double.IsFinite(value))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NonFiniteCoordinate, position);

            return false;
        }

        position = end;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Maps a tag to its kind, normalizing the polygon-shaped surface tags; unknown and curve tags fail.</summary>
    private static bool TryClassifyTag(ReadOnlySpan<byte> tag, out GeometryKind kind)
    {
        if(IdentifierEquals(tag, "POINT"u8))
        {
            kind = GeometryKind.Point;
        }
        else if(IdentifierEquals(tag, "LINESTRING"u8))
        {
            kind = GeometryKind.LineString;
        }
        else if(IdentifierEquals(tag, "POLYGON"u8) || IdentifierEquals(tag, "TRIANGLE"u8))
        {
            kind = GeometryKind.Polygon;
        }
        else if(IdentifierEquals(tag, "MULTIPOINT"u8))
        {
            kind = GeometryKind.MultiPoint;
        }
        else if(IdentifierEquals(tag, "MULTILINESTRING"u8))
        {
            kind = GeometryKind.MultiLineString;
        }
        else if(IdentifierEquals(tag, "MULTIPOLYGON"u8) || IdentifierEquals(tag, "TIN"u8) || IdentifierEquals(tag, "POLYHEDRALSURFACE"u8))
        {
            kind = GeometryKind.MultiPolygon;
        }
        else if(IdentifierEquals(tag, "GEOMETRYCOLLECTION"u8))
        {
            kind = GeometryKind.GeometryCollection;
        }
        else
        {
            kind = default;

            return false;
        }

        return true;
    }

    /// <summary>Reads a run of ASCII letters, failing on anything else at the cursor.</summary>
    private static bool TryReadIdentifier(ReadOnlySpan<byte> text, ref int position, out ReadOnlySpan<byte> identifier)
    {
        if(!TryPeekIdentifier(text, position, out identifier, out int after))
        {
            return false;
        }

        position = after;

        return true;
    }

    /// <summary>Peeks a run of ASCII letters without moving the cursor.</summary>
    private static bool TryPeekIdentifier(ReadOnlySpan<byte> text, int position, out ReadOnlySpan<byte> identifier, out int after)
    {
        int end = position;

        while(end < text.Length && IsAsciiLetter(text[end]))
        {
            end++;
        }

        identifier = text[position..end];
        after = end;

        return end > position;
    }

    /// <summary>ASCII case-insensitive equality against an uppercase reference token.</summary>
    private static bool IdentifierEquals(ReadOnlySpan<byte> identifier, ReadOnlySpan<byte> uppercaseReference)
    {
        if(identifier.Length != uppercaseReference.Length)
        {
            return false;
        }

        for(int index = 0; index < identifier.Length; index++)
        {
            byte upper = identifier[index] is >= (byte)'a' and <= (byte)'z'
                ? (byte)(identifier[index] - 32)
                : identifier[index];

            if(upper != uppercaseReference[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Requires the given byte at the cursor after optional whitespace; a mismatch and
    /// an exhausted text alike refuse as malformed at the cursor.
    /// </summary>
    private static bool Expect(ReadOnlySpan<byte> text, ref int position, byte expected, out GeometryCodecRefusal refusal)
    {
        SkipWhitespace(text, ref position);

        if(position < text.Length && text[position] == expected)
        {
            position++;
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, position);

        return false;
    }

    /// <summary>Advances over the whitespace set <see cref="WktLexical"/> admits: space, tab, CR, LF.</summary>
    private static void SkipWhitespace(ReadOnlySpan<byte> text, ref int position)
    {
        while(position < text.Length && text[position] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            position++;
        }
    }

    /// <summary>Whether a byte can begin a number token: digit, sign, or dot.</summary>
    private static bool IsNumberStart(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.';
    }

    /// <summary>Whether a byte can continue a number token.</summary>
    private static bool IsNumberByte(byte value)
    {
        return value is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'e' or (byte)'E';
    }

    /// <summary>Whether a byte is an ASCII letter.</summary>
    private static bool IsAsciiLetter(byte value)
    {
        return value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';
    }

    /// <summary>An open geometry collection whose members are still being parsed.</summary>
    private readonly record struct CollectionFrame(int NodeIndex, List<int> Children);

}
