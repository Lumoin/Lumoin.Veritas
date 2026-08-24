using System;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// A span recognizer for the lexical shape of a GML geometry literal: one XML fragment whose root is a
/// geometry element of the GML 3.2 namespace <c>http://www.opengis.net/gml/3.2</c>, scanned in one
/// forward pass over an explicit frame stack bounded by <see cref="MaximumNestingDepth"/>, with no
/// recursion and no runtime regular expressions.
/// </summary>
/// <remarks>
/// <para>
/// The supported profile is GML 3.2, the geometry elements of OGC 07-036. The certified roster is
/// <c>Point</c>, <c>LineString</c>, <c>Polygon</c>, <c>MultiPoint</c>, <c>MultiCurve</c>,
/// <c>MultiSurface</c> and <c>MultiGeometry</c>, whose content models are certified as follows:
/// <c>pos</c> and <c>posList</c> carry whitespace-separated numbers; <c>exterior</c> and <c>interior</c>
/// wrap a <c>LinearRing</c>; the member wrappers come in singular and plural pairs —
/// <c>pointMember</c>, <c>curveMember</c>, <c>surfaceMember</c> and <c>geometryMember</c> wrap one
/// certified member, while <c>pointMembers</c>, <c>curveMembers</c>, <c>surfaceMembers</c> and
/// <c>geometryMembers</c> wrap any number of them; and the object preamble <c>metaDataProperty</c>,
/// <c>description</c>, <c>descriptionReference</c>, <c>identifier</c> and <c>name</c> carries
/// uncertified simple content. Attributes are allowed everywhere and their values are semantics rather
/// than lexical shape, so <c>srsName</c>, <c>srsDimension</c> and <c>gml:id</c> pass uncertified.
/// </para>
/// <para>
/// The abstention set is everything the profile leaves unmodeled: a root in the GML 3.2 namespace whose
/// local name is outside the certified roster, which includes <c>LinearRing</c>, certified as ring
/// content only because a ring does not stand for a geometry in GML 3.2, and the geometry elements whose
/// content models are not encoded here — <c>Curve</c>, <c>OrientableCurve</c>, <c>CompositeCurve</c>,
/// <c>Surface</c>, <c>PolyhedralSurface</c>, <c>TriangulatedSurface</c>, <c>Tin</c>,
/// <c>OrientableSurface</c>, <c>CompositeSurface</c>, <c>Solid</c>, <c>CompositeSolid</c> and
/// <c>MultiSolid</c> among them, named as examples rather than as a closed set; a root in the sibling
/// namespace <c>http://www.opengis.net/gml</c>, a profile this implementation does not document; the
/// deprecated <c>coordinates</c> element, whose separators are configurable through its own attributes;
/// and any other unmodeled child of a certified element, whose absence from this content model proves
/// nothing about the schema.
/// </para>
/// <para>
/// A root in any other namespace, or in no namespace, is malformed: the GML schema's target namespace is
/// fixed, so such an element is provably not from it. An empty or all-whitespace body is well-formed and
/// denotes the empty geometry.
/// </para>
/// </remarks>
public static class GmlLexical
{
    /// <summary>
    /// The hard cap on element nesting depth. The cap carries the thirty-two-level geometry nesting bound
    /// the readers of this format certify, measured in the open elements this scan counts: a geometry
    /// nested that far spells thirty-one wrapping aggregates, each costing two open elements — the
    /// aggregate and its member wrapper — sixty-two in all, plus the deepest leaf chain of four — a
    /// <c>Polygon</c>, its ring wrapper, the <c>LinearRing</c> and its <c>posList</c> — sixty-six open
    /// elements together, so no fragment inside the geometry bound is ever answered on depth here. A
    /// fragment needing more open elements than this answers
    /// <see cref="GeometryLexicalRecognition.DepthExceeded"/> instead of being scanned further.
    /// </summary>
    public const int MaximumNestingDepth = 96;

    /// <summary>The content-model kinds of the certified GML profile.</summary>
    private enum ElementKind : byte
    {
        /// <summary>No certified element.</summary>
        None = 0,

        /// <summary>A <c>Point</c>.</summary>
        Point,

        /// <summary>A <c>LineString</c>.</summary>
        LineString,

        /// <summary>A <c>Polygon</c>.</summary>
        Polygon,

        /// <summary>A <c>LinearRing</c>, certified as ring content.</summary>
        LinearRing,

        /// <summary>A <c>MultiPoint</c>.</summary>
        MultiPoint,

        /// <summary>A <c>MultiCurve</c>.</summary>
        MultiCurve,

        /// <summary>A <c>MultiSurface</c>.</summary>
        MultiSurface,

        /// <summary>A <c>MultiGeometry</c>.</summary>
        MultiGeometry,

        /// <summary>An <c>exterior</c> or <c>interior</c> wrapper, whose certified member is a ring.</summary>
        RingProperty,

        /// <summary>A <c>pointMember</c> or <c>pointMembers</c> wrapper.</summary>
        PointProperty,

        /// <summary>A <c>curveMember</c> or <c>curveMembers</c> wrapper.</summary>
        CurveProperty,

        /// <summary>A <c>surfaceMember</c> or <c>surfaceMembers</c> wrapper.</summary>
        SurfaceProperty,

        /// <summary>A <c>geometryMember</c> or <c>geometryMembers</c> wrapper.</summary>
        GeometryProperty,

        /// <summary>An object preamble element, whose simple content is uncertified.</summary>
        Preamble,

        /// <summary>A <c>pos</c>, whose content is a list of numbers.</summary>
        Position,

        /// <summary>A <c>posList</c>, whose content is a list of numbers.</summary>
        PositionList
    }

    /// <summary>The namespace of the certified profile, GML 3.2.</summary>
    private static ReadOnlySpan<byte> GmlNamespace => "http://www.opengis.net/gml/3.2"u8;

    /// <summary>The sibling namespace of the earlier GML profiles, which this implementation does not document.</summary>
    private static ReadOnlySpan<byte> SiblingProfileNamespace => "http://www.opengis.net/gml"u8;

    /// <summary>Lexically recognizes one GML geometry body.</summary>
    /// <param name="body">The candidate GML fragment as UTF-8 bytes.</param>
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

        return XmlFragmentLexical.Recognize(body, GmlNamespace, ClassifyRoot, ClassifyChild, ValidateTokenContent, frames);
    }

    /// <summary>Classifies the fragment's root element.</summary>
    /// <param name="namespaceUri">The root's resolved namespace URI.</param>
    /// <param name="localName">The root's local name.</param>
    /// <returns>The classification of the root.</returns>
    private static XmlContentClassification ClassifyRoot(ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> localName)
    {
        if(namespaceUri.SequenceEqual(GmlNamespace))
        {
            return TryClassifyGeometry(localName, out ElementKind kind)
                ? XmlContentClassification.Model((byte)kind)
                : XmlContentClassification.Suppressed;
        }

        if(namespaceUri.SequenceEqual(SiblingProfileNamespace))
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
            ElementKind.Point => ClassifyPointChild(localName),
            ElementKind.LineString or ElementKind.LinearRing => ClassifyCurveChild(localName),
            ElementKind.Polygon => ClassifyPolygonChild(localName),
            ElementKind.MultiPoint => ClassifyMemberWrapper(localName, "pointMember"u8, "pointMembers"u8, ElementKind.PointProperty),
            ElementKind.MultiCurve => ClassifyMemberWrapper(localName, "curveMember"u8, "curveMembers"u8, ElementKind.CurveProperty),
            ElementKind.MultiSurface => ClassifyMemberWrapper(localName, "surfaceMember"u8, "surfaceMembers"u8, ElementKind.SurfaceProperty),
            ElementKind.MultiGeometry => ClassifyMemberWrapper(localName, "geometryMember"u8, "geometryMembers"u8, ElementKind.GeometryProperty),
            ElementKind.RingProperty => ClassifyMember(localName, "LinearRing"u8, ElementKind.LinearRing),
            ElementKind.PointProperty => ClassifyMember(localName, "Point"u8, ElementKind.Point),
            ElementKind.CurveProperty => ClassifyMember(localName, "LineString"u8, ElementKind.LineString),
            ElementKind.SurfaceProperty => ClassifyMember(localName, "Polygon"u8, ElementKind.Polygon),
            ElementKind.GeometryProperty => ClassifyGeometryMember(localName),
            _ => XmlContentClassification.Suppressed
        };
    }

    /// <summary>Classifies one child of a <c>Point</c>.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyPointChild(ReadOnlySpan<byte> localName)
    {
        if(localName.SequenceEqual("pos"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.Position);
        }

        return ClassifyPreamble(localName);
    }

    /// <summary>Classifies one child of a <c>LineString</c> or a <c>LinearRing</c>.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyCurveChild(ReadOnlySpan<byte> localName)
    {
        if(localName.SequenceEqual("posList"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.PositionList);
        }

        if(localName.SequenceEqual("pos"u8))
        {
            return XmlContentClassification.Token((byte)ElementKind.Position);
        }

        return ClassifyPreamble(localName);
    }

    /// <summary>Classifies one child of a <c>Polygon</c>.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyPolygonChild(ReadOnlySpan<byte> localName)
    {
        if(localName.SequenceEqual("exterior"u8) || localName.SequenceEqual("interior"u8))
        {
            return XmlContentClassification.SingleMemberModel((byte)ElementKind.RingProperty);
        }

        return ClassifyPreamble(localName);
    }

    /// <summary>
    /// Classifies one child of an aggregate geometry: the singular wrapper takes one certified member,
    /// the plural wrapper takes any number of them.
    /// </summary>
    /// <param name="localName">The child's local name.</param>
    /// <param name="singular">The singular wrapper's local name.</param>
    /// <param name="plural">The plural wrapper's local name.</param>
    /// <param name="wrapperKind">The content-model kind both wrappers carry.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyMemberWrapper(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> singular, ReadOnlySpan<byte> plural, ElementKind wrapperKind)
    {
        if(localName.SequenceEqual(singular))
        {
            return XmlContentClassification.SingleMemberModel((byte)wrapperKind);
        }

        if(localName.SequenceEqual(plural))
        {
            return XmlContentClassification.Model((byte)wrapperKind);
        }

        return ClassifyPreamble(localName);
    }

    /// <summary>Classifies one child of a wrapper whose certified member is a single element.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <param name="memberName">The certified member's local name.</param>
    /// <param name="memberKind">The certified member's content-model kind.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyMember(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> memberName, ElementKind memberKind)
    {
        return localName.SequenceEqual(memberName)
            ? XmlContentClassification.Model((byte)memberKind)
            : XmlContentClassification.Suppressed;
    }

    /// <summary>Classifies one child of a <c>geometryMember</c> or <c>geometryMembers</c> wrapper.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyGeometryMember(ReadOnlySpan<byte> localName)
    {
        return TryClassifyGeometry(localName, out ElementKind kind)
            ? XmlContentClassification.Model((byte)kind)
            : XmlContentClassification.Suppressed;
    }

    /// <summary>Classifies one child against the object preamble, whose simple content is uncertified.</summary>
    /// <param name="localName">The child's local name.</param>
    /// <returns>The classification of the child.</returns>
    private static XmlContentClassification ClassifyPreamble(ReadOnlySpan<byte> localName)
    {
        bool preamble = localName.SequenceEqual("metaDataProperty"u8)
            || localName.SequenceEqual("description"u8)
            || localName.SequenceEqual("descriptionReference"u8)
            || localName.SequenceEqual("identifier"u8)
            || localName.SequenceEqual("name"u8);

        return preamble
            ? XmlContentClassification.Model((byte)ElementKind.Preamble)
            : XmlContentClassification.Suppressed;
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

        if(localName.SequenceEqual("Polygon"u8))
        {
            kind = ElementKind.Polygon;

            return true;
        }

        if(localName.SequenceEqual("MultiPoint"u8))
        {
            kind = ElementKind.MultiPoint;

            return true;
        }

        if(localName.SequenceEqual("MultiCurve"u8))
        {
            kind = ElementKind.MultiCurve;

            return true;
        }

        if(localName.SequenceEqual("MultiSurface"u8))
        {
            kind = ElementKind.MultiSurface;

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
            ElementKind.Position or ElementKind.PositionList => IsNumberList(content),
            _ => true
        };
    }

    /// <summary>
    /// Whether the content is a whitespace-separated list of <c>xsd:double</c> lexical forms: numbers
    /// plus the special values <c>NaN</c>, <c>INF</c> and <c>-INF</c>, which the double lexical space
    /// admits case-sensitively. How many values a position or a position list carries is semantics rather
    /// than lexical shape, so an empty list stands.
    /// </summary>
    /// <param name="content">The character data under test.</param>
    /// <returns><see langword="true"/> when every token is a double lexical form.</returns>
    private static bool IsNumberList(ReadOnlySpan<byte> content)
    {
        int index = 0;
        XmlFragmentLexical.SkipWhitespace(content, ref index);
        while(index < content.Length)
        {
            if(!TryReadDoubleToken(content, ref index))
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

    /// <summary>Reads one <c>xsd:double</c> lexical form: a special value or a number.</summary>
    /// <param name="content">The character data being scanned.</param>
    /// <param name="index">The scan position, advanced past the token when it is valid.</param>
    /// <returns><see langword="true"/> when a double lexical form was read.</returns>
    private static bool TryReadDoubleToken(ReadOnlySpan<byte> content, ref int index)
    {
        ReadOnlySpan<byte> rest = content[index..];
        if(rest.StartsWith("NaN"u8))
        {
            index += 3;

            return true;
        }

        if(rest.StartsWith("INF"u8))
        {
            index += 3;

            return true;
        }

        if(rest.StartsWith("-INF"u8))
        {
            index += 4;

            return true;
        }

        return XmlFragmentLexical.TryReadNumericToken(content, ref index);
    }
}
