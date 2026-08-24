using System;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// A span recognizer for the lexical shape of a KML geometry literal: one XML fragment whose root is a
/// geometry element of the KML 2.2 namespace <c>http://www.opengis.net/kml/2.2</c>, scanned in one
/// forward pass over an explicit frame stack bounded by <see cref="MaximumNestingDepth"/>, with no
/// recursion and no runtime regular expressions.
/// </summary>
/// <remarks>
/// <para>
/// The certified roster is <c>Point</c>, <c>LineString</c>, <c>LinearRing</c>, <c>Polygon</c> and
/// <c>MultiGeometry</c>. Their content models are certified as follows: <c>coordinates</c> carries
/// whitespace-separated tuples, one tuple being two or three comma-separated numbers — longitude,
/// latitude and an optional altitude — where whitespace adjacent to an intra-tuple comma binds to that
/// comma and keeps the tuple open, and a fourth component in a tuple is malformed; <c>outerBoundaryIs</c>
/// and <c>innerBoundaryIs</c> wrap a
/// <c>LinearRing</c>; <c>MultiGeometry</c> wraps certified members; <c>extrude</c> and <c>tessellate</c>
/// carry <c>0</c>, <c>1</c>, <c>true</c> or <c>false</c>; and <c>altitudeMode</c> carries
/// <c>clampToGround</c>, <c>relativeToGround</c> or <c>absolute</c>. Attributes are allowed everywhere
/// and their values are semantics rather than lexical shape, so the object identifier attributes pass
/// uncertified.
/// </para>
/// <para>
/// The abstention set is everything the content model leaves unmodeled: <c>Model</c>, a roster member
/// whose content grammar is not encoded here; a root in the KML namespace whose local name is outside
/// the certified roster, such as the feature elements, which are recognized without any claim in either
/// direction; a root bound to an extension namespace of the KML family, whose elements are not from the
/// OGC KML schema yet are not provably outside the KML specification either; a root carrying no
/// namespace declaration at all, whatever its local name, because a fragment torn from a document
/// conventionally loses the default binding; and any other unmodeled child of a certified element.
/// </para>
/// <para>
/// A root bound to a namespace outside the KML family is malformed, because the KML schema's target
/// namespace is fixed. An empty or all-whitespace body is well-formed and denotes the empty geometry.
/// The spatial reference system is CRS84 by this serialization's own definition and never appears in the
/// lexical form.
/// </para>
/// </remarks>
public static class KmlLexical
{
    /// <summary>
    /// The hard cap on element nesting depth. The cap carries the thirty-two-level geometry nesting bound
    /// the readers of this format certify, measured in the open elements this scan counts: a geometry
    /// nested that far spells thirty-one wrapping <c>MultiGeometry</c> elements, one open element each,
    /// plus the deepest leaf chain — a <c>Polygon</c>, its boundary wrapper, the <c>LinearRing</c> and its
    /// <c>coordinates</c> — thirty-five open elements in all, so no fragment inside the geometry bound is
    /// ever answered on depth here. A fragment needing more open elements than this answers
    /// <see cref="GeometryLexicalRecognition.DepthExceeded"/> instead of being scanned further.
    /// </summary>
    public const int MaximumNestingDepth = 96;

    /// <summary>How many numbers the shortest coordinate tuple carries: a longitude and a latitude.</summary>
    private const int MinimumTupleComponents = 2;

    /// <summary>How many numbers the longest coordinate tuple carries: a longitude, a latitude and an altitude.</summary>
    private const int MaximumTupleComponents = 3;

    /// <summary>The content-model kinds of the certified KML geometry elements.</summary>
    private enum ElementKind : byte
    {
        /// <summary>No certified element.</summary>
        None = 0,

        /// <summary>A <c>Point</c>.</summary>
        Point,

        /// <summary>A <c>LineString</c>.</summary>
        LineString,

        /// <summary>A <c>LinearRing</c>.</summary>
        LinearRing,

        /// <summary>A <c>Polygon</c>.</summary>
        Polygon,

        /// <summary>A <c>MultiGeometry</c>.</summary>
        MultiGeometry,

        /// <summary>An <c>outerBoundaryIs</c> or <c>innerBoundaryIs</c> wrapper, whose certified member is a ring.</summary>
        BoundaryProperty,

        /// <summary>A <c>coordinates</c> element, whose content is a list of tuples.</summary>
        Coordinates,

        /// <summary>An <c>extrude</c> or <c>tessellate</c> element, whose content is a boolean.</summary>
        BooleanFlag,

        /// <summary>An <c>altitudeMode</c> element, whose content is one of the altitude interpretations.</summary>
        AltitudeMode
    }

    /// <summary>The namespace of the certified geometry elements, OGC KML 2.2.</summary>
    private static ReadOnlySpan<byte> KmlNamespace => "http://www.opengis.net/kml/2.2"u8;

    /// <summary>The path segment every namespace of the KML family carries.</summary>
    private static ReadOnlySpan<byte> KmlFamilySegment => "/kml/"u8;

    /// <summary>Lexically recognizes one KML geometry body.</summary>
    /// <param name="body">The candidate KML fragment as UTF-8 bytes.</param>
    /// <returns>The recognition outcome; an empty or all-whitespace body is well-formed (an empty geometry).</returns>
    public static GeometryLexicalRecognition Recognize(ReadOnlySpan<byte> body)
    {
        int index = 0;
        XmlFragmentLexical.SkipWhitespace(body, ref index);
        if(index == body.Length)
        {
            return GeometryLexicalRecognition.WellFormed;
        }

        Span<XmlFragmentFrame> frames = stackalloc XmlFragmentFrame[MaximumNestingDepth];

        return XmlFragmentLexical.Recognize(body, KmlNamespace, ClassifyRoot, ClassifyChild, ValidateTokenContent, frames);
    }

    /// <summary>Classifies the fragment's root element.</summary>
    /// <param name="namespaceUri">The root's resolved namespace URI, empty when the root carries no namespace declaration.</param>
    /// <param name="localName">The root's local name.</param>
    /// <returns>The classification of the root.</returns>
    private static XmlContentClassification ClassifyRoot(ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName)
    {
        if(namespaceUri.SequenceEqual(KmlNamespace))
        {
            return TryClassifyGeometry(localName, out ElementKind kind)
                ? XmlContentClassification.Model((byte)kind)
                : XmlContentClassification.Suppressed;
        }

        if(namespaceUri.IsEmpty || namespaceUri.IndexOf(KmlFamilySegment) >= 0)
        {
            return XmlContentClassification.Suppressed;
        }

        return XmlContentClassification.Malformed;
    }

    /// <summary>Classifies one child of a certified element.</summary>
    /// <param name="parentKind">The content-model kind of the parent element.</param>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyChild(byte parentKind, ReadOnlySpan<byte> localName)
    {
        return (ElementKind)parentKind switch
        {
            ElementKind.Point => ClassifyGeometryChild(localName, tessellated: false),
            ElementKind.LineString or ElementKind.LinearRing => ClassifyGeometryChild(localName, tessellated: true),
            ElementKind.Polygon => ClassifyPolygonChild(localName),
            ElementKind.MultiGeometry => ClassifyMultiGeometryChild(localName),
            ElementKind.BoundaryProperty => ClassifyBoundaryChild(localName),
            _ => XmlContentClassification.Suppressed
        };
    }

    /// <summary>Classifies one child of a <c>Point</c>, a <c>LineString</c> or a <c>LinearRing</c>.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <param name="tessellated">Whether the parent admits a <c>tessellate</c> flag, which a point does not.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyGeometryChild(ReadOnlySpan<byte> localName, bool tessellated)
    {
        if(localName.SequenceEqual("coordinates"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.Coordinates);
        }

        if(tessellated && localName.SequenceEqual("tessellate"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.BooleanFlag);
        }

        return ClassifySimpleChild(localName);
    }

    /// <summary>Classifies one child of a <c>Polygon</c>.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyPolygonChild(ReadOnlySpan<byte> localName)
    {
        if(localName.SequenceEqual("outerBoundaryIs"u8) || localName.SequenceEqual("innerBoundaryIs"u8))
        {
            return XmlContentClassification.SingleMemberModel((byte)ElementKind.BoundaryProperty);
        }

        if(localName.SequenceEqual("tessellate"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.BooleanFlag);
        }

        return ClassifySimpleChild(localName);
    }

    /// <summary>Classifies one child of a <c>MultiGeometry</c>.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyMultiGeometryChild(ReadOnlySpan<byte> localName)
    {
        return TryClassifyGeometry(localName, out ElementKind kind)
            ? XmlContentClassification.Model((byte)kind)
            : XmlContentClassification.Suppressed;
    }

    /// <summary>Classifies one child of a boundary wrapper, whose certified member is a ring.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyBoundaryChild(ReadOnlySpan<byte> localName)
    {
        return localName.SequenceEqual("LinearRing"u8)
            ? XmlContentClassification.Model((byte)ElementKind.LinearRing)
            : XmlContentClassification.Suppressed;
    }

    /// <summary>Classifies one child against the simple elements every certified geometry admits.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifySimpleChild(ReadOnlySpan<byte> localName)
    {
        if(localName.SequenceEqual("extrude"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.BooleanFlag);
        }

        if(localName.SequenceEqual("altitudeMode"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.AltitudeMode);
        }

        return XmlContentClassification.Suppressed;
    }

    /// <summary>Maps a local name to a geometry of the certified roster.</summary>
    /// <param name="localName">The element's local name.</param>
    /// <param name="kind">The geometry's content-model kind.</param>
    /// <returns><see langword="true"/> when the local name is a certified geometry.</returns>
    private static bool TryClassifyGeometry(ReadOnlySpan<byte> localName, out ElementKind kind)
    {
        if(localName.SequenceEqual("Point"u8))
        {
            kind = ElementKind.Point;

            return true;
        }

        if(localName.SequenceEqual("LineString"u8))
        {
            kind = ElementKind.LineString;

            return true;
        }

        if(localName.SequenceEqual("LinearRing"u8))
        {
            kind = ElementKind.LinearRing;

            return true;
        }

        if(localName.SequenceEqual("Polygon"u8))
        {
            kind = ElementKind.Polygon;

            return true;
        }

        if(localName.SequenceEqual("MultiGeometry"u8))
        {
            kind = ElementKind.MultiGeometry;

            return true;
        }

        kind = ElementKind.None;

        return false;
    }

    /// <summary>Certifies the character data of a token-content element.</summary>
    /// <param name="kind">The element's token kind.</param>
    /// <param name="content">The element's raw character data.</param>
    /// <returns><see langword="true"/> when the content fits the token grammar.</returns>
    private static bool ValidateTokenContent(byte kind, ReadOnlySpan<byte> content)
    {
        return (ElementKind)kind switch
        {
            ElementKind.Coordinates => IsCoordinateTupleList(content),
            ElementKind.BooleanFlag => IsBooleanValue(content),
            ElementKind.AltitudeMode => IsAltitudeMode(content),
            _ => true
        };
    }

    /// <summary>
    /// Whether the content is a whitespace-separated list of coordinate tuples. A tuple is two or three
    /// comma-separated numbers. Whitespace decides which separator it belongs to: a run of whitespace that
    /// ends at a comma binds to that comma and the tuple continues past it, and a run that ends at anything
    /// else ends the tuple. A comma that never gains its component, at the end of the content or where the
    /// next component must begin, is outside the grammar. How many tuples a geometry carries is semantics
    /// rather than lexical shape, so an empty list stands.
    /// </summary>
    /// <param name="content">The character data under test.</param>
    /// <returns><see langword="true"/> when every tuple fits the grammar.</returns>
    private static bool IsCoordinateTupleList(ReadOnlySpan<byte> content)
    {
        int index = 0;
        XmlFragmentLexical.SkipWhitespace(content, ref index);
        while(index < content.Length)
        {
            int components = 0;
            while(true)
            {
                if(!XmlFragmentLexical.TryReadNumericToken(content, ref index))
                {
                    return false;
                }

                components++;
                if(components > MaximumTupleComponents)
                {
                    return false;
                }

                int separator = index;
                XmlFragmentLexical.SkipWhitespace(content, ref separator);
                if(separator == content.Length || content[separator] != (byte)',')
                {
                    break;
                }

                index = separator + 1;
                XmlFragmentLexical.SkipWhitespace(content, ref index);
                if(index == content.Length)
                {
                    return false;
                }
            }

            if(components < MinimumTupleComponents)
            {
                return false;
            }

            if(index < content.Length && !XmlFragmentLexical.IsWhitespace(content[index]))
            {
                return false;
            }

            XmlFragmentLexical.SkipWhitespace(content, ref index);
        }

        return true;
    }

    /// <summary>
    /// Whether the content is a boolean value. An element carrying no token at all states nothing about
    /// shape, so it stands.
    /// </summary>
    /// <param name="content">The character data under test.</param>
    /// <returns><see langword="true"/> when the value fits the grammar.</returns>
    private static bool IsBooleanValue(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> value = TrimWhitespace(content);

        return value.IsEmpty
            || value.SequenceEqual("0"u8)
            || value.SequenceEqual("1"u8)
            || value.SequenceEqual("true"u8)
            || value.SequenceEqual("false"u8);
    }

    /// <summary>
    /// Whether the content is an altitude interpretation. An element carrying no token at all states
    /// nothing about shape, so it stands.
    /// </summary>
    /// <param name="content">The character data under test.</param>
    /// <returns><see langword="true"/> when the value fits the grammar.</returns>
    private static bool IsAltitudeMode(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> value = TrimWhitespace(content);

        return value.IsEmpty
            || value.SequenceEqual("clampToGround"u8)
            || value.SequenceEqual("relativeToGround"u8)
            || value.SequenceEqual("absolute"u8);
    }

    /// <summary>Trims whitespace from both ends of character data.</summary>
    /// <param name="content">The character data to trim.</param>
    /// <returns>The trimmed content.</returns>
    private static ReadOnlySpan<byte> TrimWhitespace(ReadOnlySpan<byte> content)
    {
        int start = 0;
        while(start < content.Length && XmlFragmentLexical.IsWhitespace(content[start]))
        {
            start++;
        }

        int end = content.Length;
        while(end > start && XmlFragmentLexical.IsWhitespace(content[end - 1]))
        {
            end--;
        }

        return content[start..end];
    }
}
