using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial3D;
using Lumoin.Veritas.Geo.Transforms;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The GML reader over the flat geometry model: a bare geometry element in the GML
/// 3.2 namespace materializes into a value-type carrier, with the coordinate
/// reference system recognized from the root's own declaration and returned beside
/// the value — never assumed, never transformed. The acceptance space is the simple
/// features profile's whole geometry value space: the linear elements, the
/// curve-bounded and patch-composed forms, and the three circular segment types by
/// certified linearization; recognized vocabulary outside it refuses with a typed
/// reason at the first offending byte. Refusals are returned, never thrown, and a
/// refused parse rents nothing — the one exception is the inherited allocator seam
/// contract, where an allocator violating the exact-length contract makes the build
/// throw after acceptance. Coordinates ride in the declared system's own axis
/// order; the reader never reorders and never validates domains.
/// </summary>
public static class GmlGeometryReader
{
    /// <summary>
    /// Reads a GML geometry document with the default heap allocators. False
    /// reports the refusal; the geometry and the coordinate reference system are
    /// their default values on every refusal, even when recognition succeeded
    /// before a later offense.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> utf8Document, out FlatGeometry geometry, out CoordinateReferenceSystem coordinateReferenceSystem, out GeometryCodecRefusal refusal)
    {
        return TryRead(utf8Document, FlatGeometryAllocators.Default, out geometry, out coordinateReferenceSystem, out refusal);
    }

    /// <summary>
    /// Reads a GML geometry document through the allocator seam. Nothing is rented
    /// before the whole document is accepted, so a refused parse leaves nothing to
    /// dispose.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> utf8Document, FlatGeometryAllocators allocators, out FlatGeometry geometry, out CoordinateReferenceSystem coordinateReferenceSystem, out GeometryCodecRefusal refusal)
    {
        FlatGeometryBuilder builder = new();

        if(!ParseDocument(utf8Document, builder, out int rootIndex, out coordinateReferenceSystem, out refusal))
        {
            geometry = default;
            coordinateReferenceSystem = default;

            return false;
        }

        builder.RootIndex = rootIndex;
        geometry = builder.ToGeometry(allocators);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// The character-span convenience: transcodes to UTF-8 and reads. Refusal
    /// offsets index the transcoded UTF-8 representation, not character positions.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<char> text, out FlatGeometry geometry, out CoordinateReferenceSystem coordinateReferenceSystem, out GeometryCodecRefusal refusal)
    {
        return TryRead(text, FlatGeometryAllocators.Default, out geometry, out coordinateReferenceSystem, out refusal);
    }

    /// <summary>
    /// The character-span convenience over the allocator seam. Refusal offsets
    /// index the transcoded UTF-8 representation, not character positions.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<char> text, FlatGeometryAllocators allocators, out FlatGeometry geometry, out CoordinateReferenceSystem coordinateReferenceSystem, out GeometryCodecRefusal refusal)
    {
        byte[] utf8Document = new byte[Encoding.UTF8.GetByteCount(text)];
        Encoding.UTF8.GetBytes(text, utf8Document);

        return TryRead(utf8Document, allocators, out geometry, out coordinateReferenceSystem, out refusal);
    }

    /// <summary>The undeclared-dimension marker in the inheritance walk.</summary>
    private const int UndeclaredDimension = 0;

    /// <summary>
    /// The per-parse state: the builder, the recognized system with its root
    /// spelling index, the ordinate scratch, and the collection-depth accounting.
    /// </summary>
    private sealed class GmlParseContext
    {
        /// <summary>The builder accumulating the parse.</summary>
        public FlatGeometryBuilder Builder { get; }

        /// <summary>The recognized document system.</summary>
        public CoordinateReferenceSystem System { get; }

        /// <summary>The root declaration's roster spelling index — the constant every nested declaration must repeat byte for byte.</summary>
        public int RootSpellingIndex { get; }

        /// <summary>Scratch for one coordinate carrier's parsed ordinates.</summary>
        public List<double> Ordinates { get; } = [];

        /// <summary>The count of open collection wrappers, for the geometry bound.</summary>
        public int CollectionDepth { get; set; }

        /// <summary>
        /// The three-dimensional kernel's scratch carrier, created on the first
        /// three-dimensional circular dispatch and reused for the rest of the
        /// parse — a parse that never reaches one allocates nothing for the tier.
        /// </summary>
        public Orientation3dScratch? Scratch3d { get; set; }

        /// <summary>Builds the context for one parse.</summary>
        public GmlParseContext(FlatGeometryBuilder builder, CoordinateReferenceSystem system, int rootSpellingIndex)
        {
            Builder = builder;
            System = system;
            RootSpellingIndex = rootSpellingIndex;
        }
    }

    /// <summary>One open heterogeneous collection in the iterative tree walk.</summary>
    private readonly record struct CollectionFrame(int NodeIndex, List<int> Children, int DeclaredDimension, bool InPluralMembers);

    /// <summary>
    /// Parses the whole document: the root element with its required system
    /// declaration, the geometry tree, and the epilog drain — the scanner must
    /// reach its exhaustion return before anything is built, so a trailing offense
    /// refuses with nothing rented.
    /// </summary>
    private static bool ParseDocument(ReadOnlySpan<byte> document, FlatGeometryBuilder builder, out int rootIndex, out CoordinateReferenceSystem coordinateReferenceSystem, out GeometryCodecRefusal refusal)
    {
        rootIndex = -1;
        coordinateReferenceSystem = default;

        XmlFragmentScanner scanner = new(document);

        try
        {
            if(!scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
            {
                if(refusal == GeometryCodecRefusal.None)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, document.Length);
                }

                return false;
            }

            if(kind != XmlFragmentTokenKind.ElementOpen)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.TokenStartOffset);

                return false;
            }

            if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

                return false;
            }

            if(!scanner.TryFindAttribute([], GmlVocabulary.SrsNameName, out int srsNameIndex))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, scanner.StartTagCloseOffset);

                return false;
            }

            if(!GmlSrsName.TryRecognize(scanner.AttributeValue(srsNameIndex), out CoordinateReferenceSystem recognized, out int spellingIndex))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, scanner.AttributeValueOffset(srsNameIndex));

                return false;
            }

            GmlParseContext context = new(builder, recognized, spellingIndex);

            if(!TryParseGeometryTree(ref scanner, context, out rootIndex, out refusal))
            {
                return false;
            }

            if(scanner.TryReadNext(out _, out refusal))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.TokenStartOffset);

                return false;
            }

            if(refusal != GeometryCodecRefusal.None)
            {
                return false;
            }

            coordinateReferenceSystem = recognized;
            refusal = GeometryCodecRefusal.None;

            return true;
        }
        finally
        {
            scanner.Dispose();
        }
    }

    /// <summary>
    /// The iterative geometry tree walk: heterogeneous collections nest through an
    /// explicit frame stack — the one self-nesting production — while every other
    /// element parses through the statically bounded grammar descent. The current
    /// token on entry is the ElementOpen of a geometry element whose namespace the
    /// caller verified.
    /// </summary>
    private static bool TryParseGeometryTree(ref XmlFragmentScanner scanner, GmlParseContext context, out int rootIndex, out GeometryCodecRefusal refusal)
    {
        List<CollectionFrame> frames = [];
        rootIndex = -1;

        while(true)
        {
            //The current token is the ElementOpen of a geometry element.
            int completedNode;

            if(context.CollectionDepth + 1 > GeometryCodecText.MaximumNestingDepth)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NestingTooDeep, scanner.TokenStartOffset);

                return false;
            }

            if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.MultiGeometryName))
            {
                if(!TryOpenGeometryElement(ref scanner, context, InheritedDimensionOf(frames), out int declaredDimension, out refusal))
                {
                    return false;
                }

                int nodeIndex = context.Builder.AddNode(GeometryKind.GeometryCollection, hasZ: false, hasM: false, firstPart: 0, partCount: 0);
                frames.Add(new CollectionFrame(nodeIndex, [], declaredDimension, InPluralMembers: false));
                context.CollectionDepth++;

                if(!TryStepIntoCollectionBody(ref scanner, frames, out bool collectionClosed, out refusal))
                {
                    return false;
                }

                if(!collectionClosed)
                {
                    //The walk stands at a member child's ElementOpen; parse it on the next loop turn.
                    continue;
                }

                completedNode = CloseCollection(context, frames);
            }
            else if(!TryParseLeaf(ref scanner, context, InheritedDimensionOf(frames), out completedNode, out refusal))
            {
                return false;
            }

            //A geometry finished. Attach upward, closing every collection whose members are exhausted.
            while(true)
            {
                if(frames.Count == 0)
                {
                    rootIndex = completedNode;
                    refusal = GeometryCodecRefusal.None;

                    return true;
                }

                frames[^1].Children.Add(completedNode);

                if(!TryResumeCollectionBody(ref scanner, frames, out bool memberFollows, out refusal))
                {
                    return false;
                }

                if(memberFollows)
                {
                    break;
                }

                completedNode = CloseCollection(context, frames);
            }
        }
    }

    /// <summary>The dimension a new element inherits: the innermost collection's declaration, or undeclared outside every collection.</summary>
    private static int InheritedDimensionOf(List<CollectionFrame> frames)
    {
        return frames.Count == 0 ? UndeclaredDimension : frames[^1].DeclaredDimension;
    }

    /// <summary>Closes the innermost collection: attaches the child list — or none for the memberless empty — and pops the frame.</summary>
    private static int CloseCollection(GmlParseContext context, List<CollectionFrame> frames)
    {
        CollectionFrame frame = frames[^1];
        frames.RemoveAt(frames.Count - 1);
        context.CollectionDepth--;

        if(frame.Children.Count > 0)
        {
            context.Builder.SetChildren(frame.NodeIndex, frame.Children);
        }

        return frame.NodeIndex;
    }

    /// <summary>
    /// Advances from a collection's start tag to its first member child or its
    /// close: skips ignorable whitespace, opens a member property — singular or
    /// plural — and leaves the walk at the child geometry's ElementOpen, or reports
    /// the collection closed for the memberless empty.
    /// </summary>
    private static bool TryStepIntoCollectionBody(ref XmlFragmentScanner scanner, List<CollectionFrame> frames, out bool collectionClosed, out GeometryCodecRefusal refusal)
    {
        collectionClosed = false;

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            collectionClosed = true;

            return true;
        }

        return TryOpenCollectionMember(ref scanner, frames, out collectionClosed, out refusal);
    }

    /// <summary>
    /// After a member child completed: consumes the singular member property's end
    /// tag when one is open, then advances to the next member or the collection's
    /// close. True with a following member leaves the walk at that child's
    /// ElementOpen.
    /// </summary>
    private static bool TryResumeCollectionBody(ref XmlFragmentScanner scanner, List<CollectionFrame> frames, out bool memberFollows, out GeometryCodecRefusal refusal)
    {
        memberFollows = false;

        CollectionFrame frame = frames[^1];

        if(!frame.InPluralMembers)
        {
            //The singular geometryMember wraps exactly one child; consume its end tag.
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind memberKind, out refusal))
            {
                return false;
            }

            if(memberKind != XmlFragmentTokenKind.ElementClose)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }
        }

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            if(frame.InPluralMembers)
            {
                //The plural members property closed; the collection's own close or another property follows.
                frames[^1] = frames[^1] with { InPluralMembers = false };

                if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind followerKind, out refusal))
                {
                    return false;
                }

                if(followerKind != XmlFragmentTokenKind.ElementClose)
                {
                    return TryOpenCollectionMemberAt(ref scanner, frames, out memberFollows, out refusal);
                }
            }

            return true;
        }

        if(frame.InPluralMembers)
        {
            //The next child inside the plural container.
            if(!TryRequireGmlGeometryOpen(ref scanner, out refusal))
            {
                return false;
            }

            memberFollows = true;

            return true;
        }

        return TryOpenCollectionMemberAt(ref scanner, frames, out memberFollows, out refusal);
    }

    /// <summary>Opens a member property the walk already stands on, reporting whether a member child follows.</summary>
    private static bool TryOpenCollectionMemberAt(ref XmlFragmentScanner scanner, List<CollectionFrame> frames, out bool memberFollows, out GeometryCodecRefusal refusal)
    {
        if(!TryOpenCollectionMember(ref scanner, frames, out bool collectionClosed, out refusal))
        {
            memberFollows = false;

            return false;
        }

        memberFollows = !collectionClosed;

        return true;
    }

    /// <summary>
    /// Opens a geometryMember or geometryMembers property: the remote-reference
    /// refusal, the empty-property adjudication, and the descent to the first
    /// child. On a plural container that closes childless, walks on to the next
    /// member or reports the collection closed.
    /// </summary>
    private static bool TryOpenCollectionMember(ref XmlFragmentScanner scanner, List<CollectionFrame> frames, out bool collectionClosed, out GeometryCodecRefusal refusal)
    {
        collectionClosed = false;

        while(true)
        {
            //The current token is an ElementOpen inside the collection body.
            if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

                return false;
            }

            bool plural = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.GeometryMembersName);

            if(!plural && !scanner.ElementLocalName.SequenceEqual(GmlVocabulary.GeometryMemberName))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(scanner.TryFindAttribute(GmlVocabulary.XlinkNamespace, GmlVocabulary.HrefName, out int hrefIndex))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.ProhibitedConstruct, scanner.AttributeNameOffset(hrefIndex));

                return false;
            }

            frames[^1] = frames[^1] with { InPluralMembers = plural };

            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementOpen)
            {
                return TryRequireGmlGeometryOpen(ref scanner, out refusal);
            }

            //The property closed childless. A singular member misses its child; a plural container may simply be empty.
            if(!plural)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            frames[^1] = frames[^1] with { InPluralMembers = false };

            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind nextKind, out refusal))
            {
                return false;
            }

            if(nextKind == XmlFragmentTokenKind.ElementClose)
            {
                collectionClosed = true;

                return true;
            }
        }
    }

    /// <summary>A member child must be a GML-namespace element; anything else refuses as unsupported.</summary>
    private static bool TryRequireGmlGeometryOpen(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Dispatches a non-collection geometry element: the linear kinds, the
    /// curve-bounded and patch-composed forms, the typed aggregates, and the typed
    /// refusals for everything else. The current token is the element's
    /// ElementOpen; on success the element's ElementClose has been consumed.
    /// </summary>
    private static bool TryParseLeaf(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PointName))
        {
            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int pointDimension, out refusal))
            {
                return false;
            }

            int firstPart = context.Builder.PartCount;

            if(!TryParsePointBody(ref scanner, context, pointDimension, out bool pointHasZ, out refusal))
            {
                return false;
            }

            nodeIndex = context.Builder.AddNode(GeometryKind.Point, pointHasZ, hasM: false, firstPart, 1);

            return true;
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.LineStringName))
        {
            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int lineDimension, out refusal))
            {
                return false;
            }

            int firstPart = context.Builder.PartCount;

            if(!TryParseLineContent(ref scanner, context, lineDimension, FlatGeometryPartRole.Line, requireClosed: false, out bool lineHasZ, out refusal))
            {
                return false;
            }

            nodeIndex = context.Builder.AddNode(GeometryKind.LineString, lineHasZ, hasM: false, firstPart, context.Builder.PartCount - firstPart);

            return true;
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CurveName))
        {
            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int curveDimension, out refusal))
            {
                return false;
            }

            if(!TryParseCurveVertices(ref scanner, context, curveDimension, out int start, out int length, out bool curveHasZ, out _, out refusal))
            {
                return false;
            }

            if(length == 0)
            {
                //The schema-valid memberless container reads as the typed empty at this position.
                nodeIndex = context.Builder.AddNode(GeometryKind.LineString, hasZ: false, hasM: false, firstPart: 0, partCount: 0);

                return true;
            }

            context.Builder.AddPart(new FlatGeometryPart(start, length, FlatGeometryPartRole.Line));
            nodeIndex = context.Builder.AddNode(GeometryKind.LineString, curveHasZ, hasM: false, context.Builder.PartCount - 1, 1);

            return true;
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PolygonName))
        {
            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int polygonDimension, out refusal))
            {
                return false;
            }

            int firstPart = context.Builder.PartCount;

            if(!TryParsePolygonBoundaries(ref scanner, context, polygonDimension, out bool polygonHasZ, out refusal))
            {
                return false;
            }

            int partCount = context.Builder.PartCount - firstPart;
            nodeIndex = context.Builder.AddNode(GeometryKind.Polygon, polygonHasZ, hasM: false, partCount > 0 ? firstPart : 0, partCount);

            return true;
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.SurfaceName))
        {
            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int surfaceDimension, out refusal))
            {
                return false;
            }

            int firstPart = context.Builder.PartCount;

            if(!TryParseSurfacePatches(ref scanner, context, surfaceDimension, out int patchCount, out bool surfaceHasZ, out refusal))
            {
                return false;
            }

            if(patchCount == 0)
            {
                //The patch-less surface reads as the typed empty polygon at this position.
                nodeIndex = context.Builder.AddNode(GeometryKind.Polygon, hasZ: false, hasM: false, firstPart: 0, partCount: 0);

                return true;
            }

            GeometryKind surfaceKind = patchCount == 1 ? GeometryKind.Polygon : GeometryKind.MultiPolygon;
            nodeIndex = context.Builder.AddNode(surfaceKind, surfaceHasZ, hasM: false, firstPart, context.Builder.PartCount - firstPart);

            return true;
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.MultiPointName))
        {
            return TryParseAggregate(ref scanner, context, inheritedDimension, GeometryKind.MultiPoint, out nodeIndex, out refusal);
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.MultiCurveName))
        {
            return TryParseAggregate(ref scanner, context, inheritedDimension, GeometryKind.MultiLineString, out nodeIndex, out refusal);
        }

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.MultiSurfaceName))
        {
            return TryParseAggregate(ref scanner, context, inheritedDimension, GeometryKind.MultiPolygon, out nodeIndex, out refusal);
        }

        refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

        return false;
    }

    /// <summary>
    /// The shared start-tag frame of a geometry element: a nested system
    /// declaration must repeat the effective root spelling byte for byte, a
    /// dimension declaration must be two or three and agree with the inherited
    /// one, and everything unrecognized is ignored. The scanner stays on the
    /// element's start tag.
    /// </summary>
    private static bool TryOpenGeometryElement(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out int effectiveDimension, out GeometryCodecRefusal refusal)
    {
        effectiveDimension = inheritedDimension;

        if(scanner.TryFindAttribute([], GmlVocabulary.SrsNameName, out int srsNameIndex)
            && !scanner.AttributeValue(srsNameIndex).SequenceEqual(GmlSrsName.SpellingAt(context.RootSpellingIndex)))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, scanner.AttributeValueOffset(srsNameIndex));

            return false;
        }

        return TryReadDimensionAttribute(ref scanner, inheritedDimension, out effectiveDimension, out refusal);
    }

    /// <summary>
    /// Reads a start tag's dimension declaration when one is present: the value
    /// must parse as two or three, and a declaration along one path must agree
    /// with its ancestors — disagreement refuses at the declaring value.
    /// </summary>
    private static bool TryReadDimensionAttribute(ref XmlFragmentScanner scanner, int inheritedDimension, out int effectiveDimension, out GeometryCodecRefusal refusal)
    {
        effectiveDimension = inheritedDimension;

        if(scanner.TryFindAttribute([], GmlVocabulary.SrsDimensionName, out int dimensionIndex))
        {
            ReadOnlySpan<byte> value = scanner.AttributeValue(dimensionIndex);

            if(!Utf8Parser.TryParse(value, out int declared, out int consumed) || consumed != value.Length || declared is not (2 or 3))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.AttributeValueOffset(dimensionIndex));

                return false;
            }

            if(inheritedDimension != UndeclaredDimension && declared != inheritedDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.AttributeValueOffset(dimensionIndex));

                return false;
            }

            effectiveDimension = declared;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses a Point element's body — one position, appended as one vertex and
    /// one point part. The point's ElementClose is consumed.
    /// </summary>
    private static bool TryParsePointBody(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out bool hasZ, out GeometryCodecRefusal refusal)
    {
        hasZ = false;

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            //A point without its position: the violation is final where the element ends.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!TryParseSinglePosition(ref scanner, context, inheritedDimension, out double x, out double y, out double z, out hasZ, out refusal))
        {
            return false;
        }

        int start = context.Builder.VertexCount;

        if(hasZ)
        {
            context.Builder.AddVertex(new Point2d(x, y), z, double.NaN);
        }
        else
        {
            context.Builder.AddVertex(new Point2d(x, y));
        }

        context.Builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));

        return TryConsumeElementClose(ref scanner, out refusal);
    }

    /// <summary>
    /// Parses one position from the element the walk stands on: a single-position
    /// element, or a one-position list under the documented widening. The carrier
    /// element's ElementClose is consumed.
    /// </summary>
    private static bool TryParseSinglePosition(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out double x, out double y, out double z, out bool hasZ, out GeometryCodecRefusal refusal)
    {
        x = 0.0;
        y = 0.0;
        z = double.NaN;
        hasZ = false;

        bool isPos = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosName);
        bool isPosList = !isPos && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosListName);

        if(!isPos && !isPosList)
        {
            return RefusePositionCarrier(ref scanner, out refusal);
        }

        if(!TryReadCarrierOrdinates(ref scanner, context, inheritedDimension, isPosList, out int carrierDimension, out int positionCount, out refusal))
        {
            return false;
        }

        if(positionCount != 1)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        x = context.Ordinates[0];
        y = context.Ordinates[1];
        hasZ = carrierDimension == 3;

        if(hasZ)
        {
            z = context.Ordinates[2];
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>The typed refusals for the position carriers this codec deliberately declines, and the structural refusal for everything else.</summary>
    private static bool RefusePositionCarrier(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        bool declined = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CoordinatesName)
            || scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PointPropertyName)
            || scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PointRepName)
            || GmlVocabulary.IsRefusedVocabulary(scanner.ElementLocalName);
        refusal = new GeometryCodecRefusal(declined ? GeometryCodecRefusalKind.UnsupportedGeometry : GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

        return false;
    }

    /// <summary>
    /// Reads one coordinate carrier — a single-position element or a position
    /// list — into the ordinate scratch: the carrier's own dimension declaration,
    /// the token walk with finiteness enforced by parsed value, the divisibility
    /// and count rules, and the carrier's ElementClose. The bare single position
    /// may still infer three from its own token count when nothing is declared.
    /// </summary>
    private static bool TryReadCarrierOrdinates(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, bool isPosList, out int carrierDimension, out int positionCount, out GeometryCodecRefusal refusal)
    {
        carrierDimension = 0;
        positionCount = 0;

        if(scanner.TryFindAttribute([], GmlVocabulary.SrsNameName, out int srsNameIndex)
            && !scanner.AttributeValue(srsNameIndex).SequenceEqual(GmlSrsName.SpellingAt(context.RootSpellingIndex)))
        {
            //A carrier-level declaration must repeat the effective root spelling byte for byte — the nested-element rule extended to the position carriers, which the schema gives the same reference group.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, scanner.AttributeValueOffset(srsNameIndex));

            return false;
        }

        if(!TryReadDimensionAttribute(ref scanner, inheritedDimension, out int declaredDimension, out refusal))
        {
            return false;
        }

        bool hasCount = scanner.TryFindAttribute([], GmlVocabulary.CountName, out int countIndex);
        int expectedCount = -1;
        int countValueOffset = -1;

        if(hasCount)
        {
            countValueOffset = scanner.AttributeValueOffset(countIndex);

            //The prose rule ties a stated count to a stated dimension.
            if(declaredDimension == UndeclaredDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, countValueOffset);

                return false;
            }

            ReadOnlySpan<byte> countValue = scanner.AttributeValue(countIndex);

            if(!Utf8Parser.TryParse(countValue, out expectedCount, out int consumed) || consumed != countValue.Length || expectedCount < 0)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, countValueOffset);

                return false;
            }
        }

        int closeAnchor = scanner.StartTagCloseOffset;
        context.Ordinates.Clear();

        if(!scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
        {
            if(refusal == GeometryCodecRefusal.None)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, closeAnchor);
            }

            return false;
        }

        if(kind == XmlFragmentTokenKind.Text)
        {
            if(!TryParseOrdinateTokens(ref scanner, context, out refusal))
            {
                return false;
            }

            if(!scanner.TryReadNext(out kind, out refusal))
            {
                if(refusal == GeometryCodecRefusal.None)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, closeAnchor);
                }

                return false;
            }
        }

        if(kind != XmlFragmentTokenKind.ElementClose)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.TokenStartOffset);

            return false;
        }

        int tokenCount = context.Ordinates.Count;

        if(isPosList)
        {
            carrierDimension = declaredDimension == UndeclaredDimension ? 2 : declaredDimension;

            if(tokenCount % carrierDimension != 0)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.TokenStartOffset);

                return false;
            }

            positionCount = tokenCount / carrierDimension;
        }
        else
        {
            if(tokenCount is not (2 or 3))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.TokenStartOffset);

                return false;
            }

            if(declaredDimension != UndeclaredDimension && tokenCount != declaredDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.TokenStartOffset);

                return false;
            }

            carrierDimension = tokenCount;
            positionCount = 1;
        }

        if(expectedCount >= 0 && expectedCount != positionCount)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, countValueOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Tokenizes the current text region into parsed finite ordinates: the
    /// non-finite tokens the double grammar legally admits refuse as non-finite
    /// values, and everything else that fails the number grammar refuses as
    /// malformed — each at its own first byte through the decoded-offset map.
    /// </summary>
    private static bool TryParseOrdinateTokens(ref XmlFragmentScanner scanner, GmlParseContext context, out GeometryCodecRefusal refusal)
    {
        ReadOnlySpan<byte> text = scanner.Text;
        int position = 0;

        while(position < text.Length)
        {
            if(XmlLexicon.IsWhitespace(text[position]))
            {
                position++;

                continue;
            }

            int tokenStart = position;

            while(position < text.Length && !XmlLexicon.IsWhitespace(text[position]))
            {
                position++;
            }

            if(!TryParseOrdinateToken(text[tokenStart..position], out double value, out GeometryCodecRefusalKind offense))
            {
                refusal = new GeometryCodecRefusal(offense, scanner.MapTextOffset(tokenStart));

                return false;
            }

            context.Ordinates.Add(value);
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses one ordinate token: a finite double on success; the schema-legal
    /// non-finite tokens and value overflow classify as non-finite, anything else
    /// as malformed.
    /// </summary>
    private static bool TryParseOrdinateToken(ReadOnlySpan<byte> token, out double value, out GeometryCodecRefusalKind offense)
    {
        if(GeometryCodecText.TryParseFiniteDouble(token, out value))
        {
            offense = GeometryCodecRefusalKind.None;

            return true;
        }

        bool nonFiniteToken = token.SequenceEqual("NaN"u8) || token.SequenceEqual("INF"u8) || token.SequenceEqual("-INF"u8);

        if(!nonFiniteToken && Utf8Parser.TryParse(token, out double parsed, out int consumed) && consumed == token.Length && !double.IsFinite(parsed))
        {
            nonFiniteToken = true;
        }

        offense = nonFiniteToken ? GeometryCodecRefusalKind.NonFiniteCoordinate : GeometryCodecRefusalKind.MalformedDocument;

        return false;
    }

    /// <summary>
    /// Parses linear ring or line-string content — repeated single positions or
    /// position lists — appending vertices and the part. Rings must close on the
    /// exact planar predicate with at least four positions; lines need two. The
    /// surrounding element's ElementClose is consumed.
    /// </summary>
    private static bool TryParseLineContent(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, FlatGeometryPartRole role, bool requireClosed, out bool hasZ, out GeometryCodecRefusal refusal)
    {
        hasZ = false;

        int start = context.Builder.VertexCount;
        int carrierDimension = UndeclaredDimension;
        int runTerminator;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                runTerminator = scanner.TokenStartOffset;

                break;
            }

            bool isPos = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosName);
            bool isPosList = !isPos && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosListName);

            if(!isPos && !isPosList)
            {
                return RefusePositionCarrier(ref scanner, out refusal);
            }

            int carrierAnchor = scanner.TokenStartOffset;

            if(!TryReadCarrierOrdinates(ref scanner, context, carrierDimension == UndeclaredDimension ? inheritedDimension : carrierDimension, isPosList, out int thisDimension, out int positionCount, out refusal))
            {
                return false;
            }

            if(carrierDimension != UndeclaredDimension && thisDimension != carrierDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, carrierAnchor);

                return false;
            }

            carrierDimension = thisDimension;
            AppendOrdinatesAsVertices(context, thisDimension, positionCount);
        }

        int length = context.Builder.VertexCount - start;

        if(requireClosed)
        {
            if(length < 4 || !context.Builder.VerticesEqualXy(start, (start + length) - 1))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runTerminator);

                return false;
            }
        }
        else if(length < 2)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runTerminator);

            return false;
        }

        context.Builder.AddPart(new FlatGeometryPart(start, length, role));
        hasZ = carrierDimension == 3;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Appends the ordinate scratch to the builder as vertices of the given arity.</summary>
    private static void AppendOrdinatesAsVertices(GmlParseContext context, int dimension, int positionCount)
    {
        for(int index = 0; index < positionCount; index++)
        {
            double x = context.Ordinates[index * dimension];
            double y = context.Ordinates[(index * dimension) + 1];

            if(dimension == 3)
            {
                context.Builder.AddVertex(new Point2d(x, y), context.Ordinates[(index * dimension) + 2], double.NaN);
            }
            else
            {
                context.Builder.AddVertex(new Point2d(x, y));
            }
        }
    }

    /// <summary>
    /// Parses a Curve element's body into a vertex run: the required segments
    /// container, the per-segment dispatch with the join discipline, and the
    /// certified linearization of the circular types. The Curve's ElementClose is
    /// consumed. A zero-length run reports the schema-valid memberless container —
    /// the caller adjudicates its legality at its position — and the last
    /// position's carrier anchor rides out for the cycle rule's refusals.
    /// </summary>
    private static bool TryParseCurveVertices(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out int start, out int length, out bool hasZ, out int lastCarrierAnchor, out GeometryCodecRefusal refusal)
    {
        start = context.Builder.VertexCount;
        length = 0;
        hasZ = false;
        lastCarrierAnchor = -1;

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            //An absent segments container is schema-invalid; the empty tolerance never extends to it.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace)
            || !scanner.ElementLocalName.SequenceEqual(GmlVocabulary.SegmentsName))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        SegmentJoinState join = new();
        int segmentIndex = 0;
        int curveDimension = UndeclaredDimension;
        bool sawCenterRadius = false;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                //The segments container closed; the Curve's own close follows.
                break;
            }

            if(sawCenterRadius)
            {
                //The sole-segment rule is bidirectional: nothing follows the center-and-radius circle, and the follower's identity is never inspected.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

                return false;
            }

            if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.LineStringSegmentName))
            {
                if(!TryParseLinearSegment(ref scanner, context, inheritedDimension, ref curveDimension, ref join, out refusal))
                {
                    return false;
                }

                segmentIndex++;

                continue;
            }

            bool isArc = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.ArcName);
            bool isCircle = !isArc && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CircleName);

            if(isArc || isCircle)
            {
                if(!TryParseCircularSegment(ref scanner, context, inheritedDimension, isCircle, ref curveDimension, ref join, out refusal))
                {
                    return false;
                }

                segmentIndex++;

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CircleByCenterPointName))
            {
                if(segmentIndex > 0)
                {
                    //The center-and-radius circle has no document-defined start point for the join rule; it is legal only alone.
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                if(!TryParseCenterRadiusSegment(ref scanner, context, inheritedDimension, ref curveDimension, ref join, out refusal))
                {
                    return false;
                }

                segmentIndex++;
                sawCenterRadius = true;

                continue;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

            return false;
        }

        if(!TryConsumeElementClose(ref scanner, out refusal))
        {
            return false;
        }

        length = context.Builder.VertexCount - start;
        hasZ = curveDimension == 3;
        lastCarrierAnchor = join.LastCarrierAnchor;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>The join carriage between adjacent curve segments: the previous segment's final position and its carrier anchor.</summary>
    private struct SegmentJoinState
    {
        /// <summary>Whether a previous segment has established the joining position.</summary>
        public bool HaveLast { get; set; }

        /// <summary>The previous segment's final X ordinate.</summary>
        public double LastX { get; set; }

        /// <summary>The previous segment's final Y ordinate.</summary>
        public double LastY { get; set; }

        /// <summary>The previous segment's final Z ordinate, or the no-value marker for planar carriage.</summary>
        public double LastZ { get; set; }

        /// <summary>The carrier anchor of the last position that landed, for the cycle rule's refusals.</summary>
        public int LastCarrierAnchor { get; set; }
    }

    /// <summary>
    /// Parses a linear segment: positions append directly, with the join rule
    /// requiring the first position to repeat the previous segment's last over
    /// every effective ordinate — the joined vertex is emitted once, the earlier
    /// copy verbatim. Token-level offenses inside the joining position win at
    /// their own bytes, because the join has no operand until the position is a
    /// defined value. The segment's own content carries at least two positions,
    /// the schema's floor, refused where the segment ends.
    /// </summary>
    private static bool TryParseLinearSegment(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, ref int curveDimension, ref SegmentJoinState join, out GeometryCodecRefusal refusal)
    {
        if(!TryCheckSegmentAttributes(ref scanner, GmlVocabulary.LinearValue, requireNumArc: false, out refusal))
        {
            return false;
        }

        bool joinPending = join.HaveLast;
        int segmentPositionCount = 0;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                break;
            }

            bool isPos = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosName);
            bool isPosList = !isPos && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosListName);

            if(!isPos && !isPosList)
            {
                return RefusePositionCarrier(ref scanner, out refusal);
            }

            int carrierAnchor = scanner.TokenStartOffset;

            if(!TryReadCarrierOrdinates(ref scanner, context, curveDimension == UndeclaredDimension ? inheritedDimension : curveDimension, isPosList, out int thisDimension, out int positionCount, out refusal))
            {
                return false;
            }

            if(curveDimension != UndeclaredDimension && thisDimension != curveDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, carrierAnchor);

                return false;
            }

            curveDimension = thisDimension;
            segmentPositionCount += positionCount;

            for(int index = 0; index < positionCount; index++)
            {
                double x = context.Ordinates[index * thisDimension];
                double y = context.Ordinates[(index * thisDimension) + 1];
                double z = thisDimension == 3 ? context.Ordinates[(index * thisDimension) + 2] : double.NaN;

                if(joinPending && index == 0)
                {
                    //The joining position must repeat the previous segment's end; it is not re-emitted.
                    bool joins = x == join.LastX && y == join.LastY && (thisDimension != 3 || z == join.LastZ);

                    if(!joins)
                    {
                        refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, carrierAnchor);

                        return false;
                    }

                    joinPending = false;

                    continue;
                }

                if(thisDimension == 3)
                {
                    context.Builder.AddVertex(new Point2d(x, y), z, double.NaN);
                }
                else
                {
                    context.Builder.AddVertex(new Point2d(x, y));
                }

                join.LastX = x;
                join.LastY = y;
                join.LastZ = z;
                join.HaveLast = true;
                join.LastCarrierAnchor = carrierAnchor;
                joinPending = false;
            }
        }

        if(segmentPositionCount < 2)
        {
            //The segment's own two-position floor holds join or no join; the violation is final where the segment ends.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses a three-point circular segment — an arc or a full circle — and
    /// linearizes it through the certified kernel of the carrier-agreed
    /// dimension: planar control points ride the two-dimensional kernel, and
    /// three-dimensional control points ride the plane-embedded kernel, their
    /// third ordinates consumed and generated under the same per-emission
    /// certification. The kernel emits the half-open run, so the segment's own
    /// first control point rides the join rule exactly like a linear segment's
    /// — value equality over every effective ordinate, the earlier copy kept.
    /// </summary>
    private static bool TryParseCircularSegment(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, bool isCircle, ref int curveDimension, ref SegmentJoinState join, out GeometryCodecRefusal refusal)
    {
        int segmentAnchor = scanner.TokenStartOffset;

        if(!TryCheckSegmentAttributes(ref scanner, GmlVocabulary.CircularArcValue, requireNumArc: false, out refusal))
        {
            return false;
        }

        Span<double> controlX = stackalloc double[3];
        Span<double> controlY = stackalloc double[3];
        Span<double> controlZ = stackalloc double[3];
        Span<int> controlAnchors = stackalloc int[3];

        int effectiveDimension = curveDimension == UndeclaredDimension ? inheritedDimension : curveDimension;
        if(!TryCollectControlPoints(ref scanner, context, effectiveDimension, controlX, controlY, controlZ, controlAnchors, out int collectionDimension, out refusal))
        {
            return false;
        }

        curveDimension = collectionDimension;

        bool threeDimensional = collectionDimension == 3;
        if(join.HaveLast && (controlX[0] != join.LastX || controlY[0] != join.LastY || (threeDimensional && controlZ[0] != join.LastZ)))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, controlAnchors[0]);

            return false;
        }

        if(!join.HaveLast)
        {
            if(threeDimensional)
            {
                context.Builder.AddVertex(new Point2d(controlX[0], controlY[0]), controlZ[0], double.NaN);
            }
            else
            {
                context.Builder.AddVertex(new Point2d(controlX[0], controlY[0]));
            }
        }

        bool certified;
        int offendingSeed;

        if(threeDimensional)
        {
            context.Scratch3d ??= Orientation3dScratch.Create();
            Vector3d startSeed = new(controlX[0], controlY[0], controlZ[0]);
            Vector3d middleSeed = new(controlX[1], controlY[1], controlZ[1]);
            Vector3d endSeed = new(controlX[2], controlY[2], controlZ[2]);
            CircularArcLinearization3dOutcome spatialOutcome;

            if(isCircle)
            {
                certified = CircularArcLinearization3d.TryLinearizeCircle(context.Scratch3d, startSeed, middleSeed, endSeed, context.Builder, out spatialOutcome, out offendingSeed);
            }
            else
            {
                certified = CircularArcLinearization3d.TryLinearizeArc(context.Scratch3d, startSeed, middleSeed, endSeed, context.Builder, out spatialOutcome, out offendingSeed);
            }

            if(!certified)
            {
                refusal = MapKernelOutcome3d(spatialOutcome, offendingSeed, controlAnchors, segmentAnchor);

                return false;
            }
        }
        else
        {
            Point2d startPoint = new(controlX[0], controlY[0]);
            Point2d middlePoint = new(controlX[1], controlY[1]);
            Point2d endPoint = new(controlX[2], controlY[2]);
            CircularArcLinearizationOutcome outcome;

            if(isCircle)
            {
                certified = CircularArcLinearization.TryLinearizeCircle(startPoint, middlePoint, endPoint, context.Builder, out outcome, out offendingSeed);
            }
            else
            {
                certified = CircularArcLinearization.TryLinearizeArc(startPoint, middlePoint, endPoint, context.Builder, out outcome, out offendingSeed);
            }

            if(!certified)
            {
                refusal = MapKernelOutcome(outcome, offendingSeed, controlAnchors, segmentAnchor);

                return false;
            }
        }

        int closingIndex = isCircle ? 0 : 2;
        join.LastX = controlX[closingIndex];
        join.LastY = controlY[closingIndex];
        join.LastZ = threeDimensional ? controlZ[closingIndex] : double.NaN;
        join.HaveLast = true;
        join.LastCarrierAnchor = controlAnchors[2];
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Collects a circular segment's exactly three control points from repeated
    /// single positions or position lists, threading the running collection
    /// dimension per carrier from the curve-effective start, and anchoring each
    /// point for the kernel-outcome mapping: an overrun refuses at the extra
    /// position's carrier, a shortfall where the element ends; dimension
    /// disagreements refuse inside the shared carrier machinery at its own
    /// anchors — a disagreeing declaration at its value byte, a bare
    /// token-count disagreement where the carrier's run ends. Planar carriers
    /// leave the third-ordinate slots at the no-value marker. The segment's
    /// ElementClose is consumed.
    /// </summary>
    private static bool TryCollectControlPoints(ref XmlFragmentScanner scanner, GmlParseContext context, int effectiveDimension, scoped Span<double> controlX, scoped Span<double> controlY, scoped Span<double> controlZ, scoped Span<int> controlAnchors, out int collectionDimension, out GeometryCodecRefusal refusal)
    {
        int collected = 0;
        collectionDimension = effectiveDimension;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                if(collected != 3)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                refusal = GeometryCodecRefusal.None;

                return true;
            }

            bool isPos = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosName);
            bool isPosList = !isPos && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosListName);

            if(!isPos && !isPosList)
            {
                return RefusePositionCarrier(ref scanner, out refusal);
            }

            int carrierAnchor = scanner.TokenStartOffset;

            if(!TryReadCarrierOrdinates(ref scanner, context, collectionDimension, isPosList, out int thisDimension, out int positionCount, out refusal))
            {
                return false;
            }

            if(collectionDimension != UndeclaredDimension && thisDimension != collectionDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, carrierAnchor);

                return false;
            }

            collectionDimension = thisDimension;

            for(int index = 0; index < positionCount; index++)
            {
                if(collected == 3)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, carrierAnchor);

                    return false;
                }

                controlX[collected] = context.Ordinates[index * thisDimension];
                controlY[collected] = context.Ordinates[(index * thisDimension) + 1];
                controlZ[collected] = thisDimension == 3 ? context.Ordinates[(index * thisDimension) + 2] : double.NaN;
                controlAnchors[collected] = carrierAnchor;
                collected++;
            }
        }
    }

    /// <summary>
    /// Parses the center-and-radius circle segment: the required arc-count token,
    /// the center through its single position, the radius with its system-matched
    /// unit, the two-dimensional pin, and the certified linearization emitting the
    /// closed cardinal ring.
    /// </summary>
    private static bool TryParseCenterRadiusSegment(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, ref int curveDimension, ref SegmentJoinState join, out GeometryCodecRefusal refusal)
    {
        int segmentAnchor = scanner.TokenStartOffset;

        if(!TryCheckSegmentAttributes(ref scanner, GmlVocabulary.CircularArcCenterValue, requireNumArc: true, out refusal))
        {
            return false;
        }

        if(curveDimension == 3 || inheritedDimension == 3)
        {
            //The two-dimensional pin: the schema itself restricts this representation to two dimensions.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.StartTagCloseOffset);

            return false;
        }

        bool haveCenter = false;
        bool haveRadius = false;
        double centerX = 0.0;
        double centerY = 0.0;
        double radius = 0.0;
        int segmentCloseAnchor;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                segmentCloseAnchor = scanner.TokenStartOffset;

                break;
            }

            if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosName) || scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PosListName))
            {
                if(haveCenter)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                int carrierAnchor = scanner.TokenStartOffset;

                if(!TryParseSinglePosition(ref scanner, context, inheritedDimension, out centerX, out centerY, out _, out bool centerHasZ, out refusal))
                {
                    return false;
                }

                if(centerHasZ)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, carrierAnchor);

                    return false;
                }

                haveCenter = true;

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.RadiusName))
            {
                if(haveRadius)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                if(!TryParseRadiusElement(ref scanner, context, out radius, out refusal))
                {
                    return false;
                }

                haveRadius = true;

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.StartAngleName) || scanner.ElementLocalName.SequenceEqual(GmlVocabulary.EndAngleName))
            {
                //The circle restriction removed the bearing angles; tolerating them would carry semantics the type forbids.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            return RefusePositionCarrier(ref scanner, out refusal);
        }

        if(!haveCenter || !haveRadius)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, segmentCloseAnchor);

            return false;
        }

        curveDimension = 2;

        if(!CircularArcLinearization.TryLinearizeCenterRadius(new Point2d(centerX, centerY), radius, context.Builder, out _, out _))
        {
            //Every kernel refusal here is a certification failure of a recognized arc.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, segmentAnchor);

            return false;
        }

        join.LastX = centerX + radius;
        join.LastY = centerY;
        join.LastZ = double.NaN;
        join.HaveLast = true;
        join.LastCarrierAnchor = segmentAnchor;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses the radius element: the required system-matched unit and the finite
    /// positive value, each refusing at its own byte. The radius's ElementClose is
    /// consumed.
    /// </summary>
    private static bool TryParseRadiusElement(ref XmlFragmentScanner scanner, GmlParseContext context, out double radius, out GeometryCodecRefusal refusal)
    {
        radius = 0.0;

        if(!scanner.TryFindAttribute([], GmlVocabulary.UomName, out int uomIndex))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.StartTagCloseOffset);

            return false;
        }

        if(!GmlSrsName.UnitMatches(scanner.AttributeValue(uomIndex), context.System))
        {
            //A recognized unit under the wrong system refuses exactly like an unknown token.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.AttributeValueOffset(uomIndex));

            return false;
        }

        int closeAnchor = scanner.StartTagCloseOffset;

        if(!scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
        {
            if(refusal == GeometryCodecRefusal.None)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, closeAnchor);
            }

            return false;
        }

        if(kind != XmlFragmentTokenKind.Text)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        ReadOnlySpan<byte> text = scanner.Text;
        int startIndex = 0;

        while(startIndex < text.Length && XmlLexicon.IsWhitespace(text[startIndex]))
        {
            startIndex++;
        }

        int endIndex = text.Length;

        while(endIndex > startIndex && XmlLexicon.IsWhitespace(text[endIndex - 1]))
        {
            endIndex--;
        }

        if(!TryParseOrdinateToken(text[startIndex..endIndex], out radius, out GeometryCodecRefusalKind offense))
        {
            refusal = new GeometryCodecRefusal(offense, scanner.MapTextOffset(startIndex));

            return false;
        }

        if(radius <= 0.0)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.MapTextOffset(startIndex));

            return false;
        }

        return TryConsumeElementClose(ref scanner, out refusal);
    }

    /// <summary>
    /// Validates a curve segment's start-tag attributes: the interpolation token
    /// must repeat the type's fixed value when present, and the arc-count token —
    /// required on the center-and-radius circle, optional elsewhere — must be the
    /// canonical numeral, the recorded lexical strictness.
    /// </summary>
    private static bool TryCheckSegmentAttributes(ref XmlFragmentScanner scanner, ReadOnlySpan<byte> fixedInterpolation, bool requireNumArc, out GeometryCodecRefusal refusal)
    {
        if(scanner.TryFindAttribute([], GmlVocabulary.InterpolationName, out int interpolationIndex)
            && !scanner.AttributeValue(interpolationIndex).SequenceEqual(fixedInterpolation))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.AttributeValueOffset(interpolationIndex));

            return false;
        }

        bool haveNumArc = scanner.TryFindAttribute([], GmlVocabulary.NumArcName, out int numArcIndex);

        if(haveNumArc && !scanner.AttributeValue(numArcIndex).SequenceEqual(GmlVocabulary.OneValue))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.AttributeValueOffset(numArcIndex));

            return false;
        }

        if(requireNumArc && !haveNumArc)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.StartTagCloseOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Maps a kernel outcome onto the refusal currency at the document anchors the walk captured.</summary>
    private static GeometryCodecRefusal MapKernelOutcome(CircularArcLinearizationOutcome outcome, int offendingSeed, ReadOnlySpan<int> controlAnchors, int segmentAnchor)
    {
        int anchor = offendingSeed >= 0 && offendingSeed < controlAnchors.Length ? controlAnchors[offendingSeed] : segmentAnchor;

        if(outcome is CircularArcLinearizationOutcome.CoincidentControlPoints or CircularArcLinearizationOutcome.CollinearControlPoints)
        {
            return new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, anchor);
        }

        return new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, anchor);
    }

    /// <summary>
    /// Maps a three-dimensional kernel outcome onto the refusal currency at the
    /// document anchors the walk captured: the two degeneracies refuse
    /// structurally at the offending control point's carrier, and every
    /// certification refusal — walls, radial drift, split membership, the depth
    /// ceiling, planar drift — refuses as unsupported at the named seed's
    /// carrier or, for computed and constructed values reported at seed minus
    /// one, at the segment. The switch is exhaustive over the closed refusal
    /// roster; any other value — the certified member included — is a defect,
    /// never a fallback.
    /// </summary>
    private static GeometryCodecRefusal MapKernelOutcome3d(CircularArcLinearization3dOutcome outcome, int offendingSeed, ReadOnlySpan<int> controlAnchors, int segmentAnchor)
    {
        int anchor = offendingSeed >= 0 && offendingSeed < controlAnchors.Length ? controlAnchors[offendingSeed] : segmentAnchor;

        return outcome switch
        {
            CircularArcLinearization3dOutcome.CoincidentControlPoints or CircularArcLinearization3dOutcome.CollinearControlPoints => new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, anchor),
            CircularArcLinearization3dOutcome.MagnitudeWall or CircularArcLinearization3dOutcome.VertexDrift or CircularArcLinearization3dOutcome.SplitMembership or CircularArcLinearization3dOutcome.DepthCeiling or CircularArcLinearization3dOutcome.PlanarDrift => new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, anchor),
            _ => throw new InvalidOperationException("The three-dimensional kernel reported an outcome outside the closed refusal roster."),
        };
    }

    /// <summary>
    /// Parses polygon boundary content — the optional exterior and the interiors —
    /// shared by the polygon element and the planar patch. The surrounding
    /// element's ElementClose is consumed; ring arities must agree.
    /// </summary>
    private static bool TryParsePolygonBoundaries(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out bool hasZ, out GeometryCodecRefusal refusal)
    {
        hasZ = false;

        bool sawExterior = false;
        int polygonDimension = UndeclaredDimension;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                break;
            }

            bool isExterior = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.ExteriorName);
            bool isInterior = !isExterior && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.InteriorName);

            if(!isExterior && !isInterior)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(isExterior && sawExterior)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(isInterior && !sawExterior)
            {
                //Interiors without an exterior are outside the simple-features space.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(scanner.TryFindAttribute(GmlVocabulary.XlinkNamespace, GmlVocabulary.HrefName, out int hrefIndex))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.ProhibitedConstruct, scanner.AttributeNameOffset(hrefIndex));

                return false;
            }

            if(!TryParseBoundaryRing(ref scanner, context, polygonDimension == UndeclaredDimension ? inheritedDimension : polygonDimension, isExterior ? FlatGeometryPartRole.ExteriorRing : FlatGeometryPartRole.InteriorRing, out bool ringHasZ, out int ringAnchor, out refusal))
            {
                return false;
            }

            int ringDimension = ringHasZ ? 3 : 2;

            if(polygonDimension != UndeclaredDimension && ringDimension != polygonDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, ringAnchor);

                return false;
            }

            polygonDimension = ringDimension;

            if(isExterior)
            {
                sawExterior = true;
            }
        }

        hasZ = sawExterior && polygonDimension == 3;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses one boundary property's ring — a linear ring, or a curve-bounded
    /// ring restricted to a single curve that must cycle — appending the ring
    /// part. The boundary property's ElementClose is consumed.
    /// </summary>
    private static bool TryParseBoundaryRing(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, FlatGeometryPartRole role, out bool hasZ, out int ringAnchor, out GeometryCodecRefusal refusal)
    {
        hasZ = false;
        ringAnchor = -1;

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            //A boundary property without its ring: the violation is final where the property ends.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

            return false;
        }

        ringAnchor = scanner.TokenStartOffset;

        if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.LinearRingName))
        {
            if(!TryReadDimensionAttribute(ref scanner, inheritedDimension, out int ringDimension, out refusal))
            {
                return false;
            }

            if(!TryParseLineContent(ref scanner, context, ringDimension, role, requireClosed: true, out hasZ, out refusal))
            {
                return false;
            }
        }
        else if(scanner.ElementLocalName.SequenceEqual(GmlVocabulary.RingName))
        {
            if(!TryParseCurveRing(ref scanner, context, inheritedDimension, role, out hasZ, out refusal))
            {
                return false;
            }
        }
        else
        {
            bool declined = GmlVocabulary.IsRefusedVocabulary(scanner.ElementLocalName);
            refusal = new GeometryCodecRefusal(declined ? GeometryCodecRefusalKind.UnsupportedGeometry : GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        return TryConsumeElementClose(ref scanner, out refusal);
    }

    /// <summary>
    /// Parses a curve-bounded ring: exactly one curve member holding exactly one
    /// curve, the ring aggregation token when present, the cycle rule as the join
    /// rule closed around the loop, and the four-position floor after
    /// linearization. The Ring's ElementClose is consumed.
    /// </summary>
    private static bool TryParseCurveRing(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, FlatGeometryPartRole role, out bool hasZ, out GeometryCodecRefusal refusal)
    {
        hasZ = false;

        if(scanner.TryFindAttribute([], GmlVocabulary.AggregationTypeName, out int aggregationIndex)
            && !scanner.AttributeValue(aggregationIndex).SequenceEqual(GmlVocabulary.SequenceValue))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.AttributeValueOffset(aggregationIndex));

            return false;
        }

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            //A ring without its curve member: the violation is final where the ring ends.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CurveMemberName))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(scanner.TryFindAttribute(GmlVocabulary.XlinkNamespace, GmlVocabulary.HrefName, out int hrefIndex))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.ProhibitedConstruct, scanner.AttributeNameOffset(hrefIndex));

            return false;
        }

        if(!TrySkipToElementToken(ref scanner, out kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace)
            || !scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CurveName))
        {
            //The container admits curves, so a line string or another curve category member is structural; non-simple-features vocabulary is unsupported.
            bool declined = scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace) && GmlVocabulary.IsRefusedVocabulary(scanner.ElementLocalName);
            refusal = new GeometryCodecRefusal(declined ? GeometryCodecRefusalKind.UnsupportedGeometry : GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int dimension, out refusal))
        {
            return false;
        }

        int ringStart = context.Builder.VertexCount;

        if(!TryParseCurveVertices(ref scanner, context, dimension, out int start, out int length, out bool curveHasZ, out int lastCarrierAnchor, out refusal))
        {
            return false;
        }

        if(length == 0)
        {
            //An empty curve cannot bound anything: the empty-member reading extended to boundary positions.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(length < 4 || !context.Builder.VerticesEqualXy(ringStart, (ringStart + length) - 1))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, lastCarrierAnchor >= 0 ? lastCarrierAnchor : scanner.TokenStartOffset);

            return false;
        }

        context.Builder.AddPart(new FlatGeometryPart(start, length, role));
        hasZ = curveHasZ;

        //Consume the curveMember close, then the Ring's own close; the boundary property's close is the caller's.
        if(!TryConsumeElementClose(ref scanner, out refusal))
        {
            return false;
        }

        if(!TrySkipToElementToken(ref scanner, out kind, out refusal))
        {
            return false;
        }

        if(kind != XmlFragmentTokenKind.ElementClose)
        {
            //The single-curve restriction: a second member is a structural offense where it opens, not a transport one.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses a Surface element's patches into polygon part groups, shared by the
    /// standalone surface and the flattening inside a surface aggregate. The
    /// Surface's ElementClose is consumed.
    /// </summary>
    private static bool TryParseSurfacePatches(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, out int patchCount, out bool hasZ, out GeometryCodecRefusal refusal)
    {
        patchCount = 0;
        hasZ = false;

        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            //An absent patches container is schema-invalid; the empty tolerance never extends to it.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace)
            || !scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PatchesName))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        int surfaceDimension = UndeclaredDimension;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                break;
            }

            if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace)
                || !scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PolygonPatchName))
            {
                bool declined = scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace) && GmlVocabulary.IsRefusedVocabulary(scanner.ElementLocalName);
                refusal = new GeometryCodecRefusal(declined ? GeometryCodecRefusalKind.UnsupportedGeometry : GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(scanner.TryFindAttribute([], GmlVocabulary.InterpolationName, out int interpolationIndex)
                && !scanner.AttributeValue(interpolationIndex).SequenceEqual(GmlVocabulary.PlanarValue))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.AttributeValueOffset(interpolationIndex));

                return false;
            }

            int patchAnchor = scanner.TokenStartOffset;

            if(!TryParsePolygonBoundaries(ref scanner, context, surfaceDimension == UndeclaredDimension ? inheritedDimension : surfaceDimension, out bool patchHasZ, out refusal))
            {
                return false;
            }

            int patchDimension = patchHasZ ? 3 : 2;

            if(surfaceDimension != UndeclaredDimension && patchDimension != surfaceDimension)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, patchAnchor);

                return false;
            }

            surfaceDimension = patchDimension;
            patchCount++;
        }

        if(!TryConsumeElementClose(ref scanner, out refusal))
        {
            return false;
        }

        hasZ = patchCount > 0 && surfaceDimension == 3;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses a typed aggregate — points, curves, or surfaces — through its
    /// singular and plural member properties: parts accumulate into the one node,
    /// members must agree on arity, and an empty member refuses where a whole
    /// geometry may still be the typed empty.
    /// </summary>
    private static bool TryParseAggregate(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, GeometryKind kind, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int dimension, out refusal))
        {
            return false;
        }

        int firstPart = context.Builder.PartCount;
        int aggregateDimension = UndeclaredDimension;
        bool sawMember = false;
        bool inPlural = false;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind tokenKind, out refusal))
            {
                return false;
            }

            if(tokenKind == XmlFragmentTokenKind.ElementClose)
            {
                if(inPlural)
                {
                    inPlural = false;

                    continue;
                }

                break;
            }

            if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

                return false;
            }

            if(!inPlural)
            {
                bool singular = MemberNameMatches(ref scanner, kind, plural: false);
                bool pluralProperty = !singular && MemberNameMatches(ref scanner, kind, plural: true);

                if(!singular && !pluralProperty)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                if(scanner.TryFindAttribute(GmlVocabulary.XlinkNamespace, GmlVocabulary.HrefName, out int hrefIndex))
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.ProhibitedConstruct, scanner.AttributeNameOffset(hrefIndex));

                    return false;
                }

                if(pluralProperty)
                {
                    inPlural = true;

                    continue;
                }

                //A singular member property: exactly one child geometry, then its close.
                if(!TrySkipToElementToken(ref scanner, out tokenKind, out refusal))
                {
                    return false;
                }

                if(tokenKind == XmlFragmentTokenKind.ElementClose)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                if(!TryParseAggregateMember(ref scanner, context, aggregateDimension == UndeclaredDimension ? dimension : aggregateDimension, kind, ref aggregateDimension, out refusal))
                {
                    return false;
                }

                sawMember = true;

                if(!TrySkipToElementToken(ref scanner, out tokenKind, out refusal))
                {
                    return false;
                }

                if(tokenKind != XmlFragmentTokenKind.ElementClose)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                continue;
            }

            //Inside the plural container: each child is a member geometry.
            if(!TryParseAggregateMember(ref scanner, context, aggregateDimension == UndeclaredDimension ? dimension : aggregateDimension, kind, ref aggregateDimension, out refusal))
            {
                return false;
            }

            sawMember = true;
        }

        bool hasZ = sawMember && aggregateDimension == 3;
        nodeIndex = context.Builder.AddNode(kind, hasZ, hasM: false, sawMember ? firstPart : 0, sawMember ? context.Builder.PartCount - firstPart : 0);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Whether the current element is the aggregate's member property, singular or plural.</summary>
    private static bool MemberNameMatches(ref XmlFragmentScanner scanner, GeometryKind kind, bool plural)
    {
        if(kind == GeometryKind.MultiPoint)
        {
            return scanner.ElementLocalName.SequenceEqual(plural ? GmlVocabulary.PointMembersName : GmlVocabulary.PointMemberName);
        }

        if(kind == GeometryKind.MultiLineString)
        {
            return scanner.ElementLocalName.SequenceEqual(plural ? GmlVocabulary.CurveMembersName : GmlVocabulary.CurveMemberName);
        }

        return scanner.ElementLocalName.SequenceEqual(plural ? GmlVocabulary.SurfaceMembersName : GmlVocabulary.SurfaceMemberName);
    }

    /// <summary>
    /// Parses one aggregate member: the kinds the container's category admits,
    /// with the surface member flattening its patches into the aggregate's part
    /// run and every empty member refused. Members must agree on their effective
    /// dimension — one node, one arity — refusing at the offending member.
    /// </summary>
    private static bool TryParseAggregateMember(ref XmlFragmentScanner scanner, GmlParseContext context, int inheritedDimension, GeometryKind kind, ref int aggregateDimension, out GeometryCodecRefusal refusal)
    {
        int memberAnchor = scanner.TokenStartOffset;

        if(!scanner.ElementNamespace.SequenceEqual(GmlVocabulary.GmlNamespace))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, memberAnchor);

            return false;
        }

        int memberDimension;

        if(kind == GeometryKind.MultiPoint)
        {
            if(!scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PointName))
            {
                refusal = MemberCategoryRefusal(ref scanner);

                return false;
            }

            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int pointDimension, out refusal))
            {
                return false;
            }

            if(!TryParsePointBody(ref scanner, context, pointDimension, out bool pointHasZ, out refusal))
            {
                return false;
            }

            memberDimension = pointHasZ ? 3 : 2;
        }
        else if(kind == GeometryKind.MultiLineString)
        {
            bool isLineString = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.LineStringName);
            bool isCurve = !isLineString && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.CurveName);

            if(!isLineString && !isCurve)
            {
                refusal = MemberCategoryRefusal(ref scanner);

                return false;
            }

            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int lineDimension, out refusal))
            {
                return false;
            }

            if(isLineString)
            {
                if(!TryParseLineContent(ref scanner, context, lineDimension, FlatGeometryPartRole.Line, requireClosed: false, out bool lineHasZ, out refusal))
                {
                    return false;
                }

                memberDimension = lineHasZ ? 3 : 2;
            }
            else
            {
                if(!TryParseCurveVertices(ref scanner, context, lineDimension, out int start, out int length, out bool curveHasZ, out _, out refusal))
                {
                    return false;
                }

                if(length == 0)
                {
                    //An empty member inside a typed aggregate refuses; only a whole geometry may be the typed empty.
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, memberAnchor);

                    return false;
                }

                context.Builder.AddPart(new FlatGeometryPart(start, length, FlatGeometryPartRole.Line));
                memberDimension = curveHasZ ? 3 : 2;
            }
        }
        else
        {
            bool isPolygon = scanner.ElementLocalName.SequenceEqual(GmlVocabulary.PolygonName);
            bool isSurface = !isPolygon && scanner.ElementLocalName.SequenceEqual(GmlVocabulary.SurfaceName);

            if(!isPolygon && !isSurface)
            {
                refusal = MemberCategoryRefusal(ref scanner);

                return false;
            }

            if(!TryOpenGeometryElement(ref scanner, context, inheritedDimension, out int surfaceDimension, out refusal))
            {
                return false;
            }

            if(isPolygon)
            {
                int before = context.Builder.PartCount;

                if(!TryParsePolygonBoundaries(ref scanner, context, surfaceDimension, out bool polygonHasZ, out refusal))
                {
                    return false;
                }

                if(context.Builder.PartCount == before)
                {
                    //A boundary-less polygon member is an empty member.
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, memberAnchor);

                    return false;
                }

                memberDimension = polygonHasZ ? 3 : 2;
            }
            else
            {
                if(!TryParseSurfacePatches(ref scanner, context, surfaceDimension, out int patchCount, out bool surfaceHasZ, out refusal))
                {
                    return false;
                }

                if(patchCount == 0)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, memberAnchor);

                    return false;
                }

                memberDimension = surfaceHasZ ? 3 : 2;
            }
        }

        if(aggregateDimension != UndeclaredDimension && memberDimension != aggregateDimension)
        {
            //One node carries one arity; a disagreeing member refuses where it began.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, memberAnchor);

            return false;
        }

        aggregateDimension = memberDimension;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>The container-precedence refusal for a member outside the aggregate's category: recognized non-simple-features vocabulary is unsupported where the category admits it, everything else is structural.</summary>
    private static GeometryCodecRefusal MemberCategoryRefusal(ref XmlFragmentScanner scanner)
    {
        bool declined = GmlVocabulary.IsRefusedVocabulary(scanner.ElementLocalName);

        return new GeometryCodecRefusal(declined ? GeometryCodecRefusalKind.UnsupportedGeometry : GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);
    }

    /// <summary>
    /// Skips ignorable whitespace to the next element token. Non-whitespace text
    /// where members are expected refuses at its first byte; transport refusals
    /// pass through unchanged.
    /// </summary>
    private static bool TrySkipToElementToken(ref XmlFragmentScanner scanner, out XmlFragmentTokenKind kind, out GeometryCodecRefusal refusal)
    {
        while(true)
        {
            if(!scanner.TryReadNext(out kind, out refusal))
            {
                if(refusal == GeometryCodecRefusal.None)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, 0);
                }

                return false;
            }

            if(kind == XmlFragmentTokenKind.Text)
            {
                if(scanner.TextIsWhitespace)
                {
                    continue;
                }

                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, FirstNonWhitespaceOffset(ref scanner));

                return false;
            }

            refusal = GeometryCodecRefusal.None;

            return true;
        }
    }

    /// <summary>The document offset of the first non-whitespace byte of the current text token.</summary>
    private static int FirstNonWhitespaceOffset(ref XmlFragmentScanner scanner)
    {
        ReadOnlySpan<byte> text = scanner.Text;

        for(int index = 0; index < text.Length; index++)
        {
            if(!XmlLexicon.IsWhitespace(text[index]))
            {
                return scanner.MapTextOffset(index);
            }
        }

        return scanner.TokenStartOffset;
    }

    /// <summary>Consumes the next token, requiring the current element's close; ignorable whitespace may precede it.</summary>
    private static bool TryConsumeElementClose(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind != XmlFragmentTokenKind.ElementClose)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.TokenStartOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }
}
