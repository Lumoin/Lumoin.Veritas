using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The KML reader over the flat geometry model: a bare geometry element in the KML
/// 2.2 namespace materializes into a value-type carrier. The format fixes its
/// coordinate reference system — longitude-latitude degrees with optional metre
/// altitude — so no system is recognized and none is returned; coordinates ride
/// verbatim and domains are never validated. The acceptance space is the format's
/// coordinate geometry roster: the point, line string, linear ring, polygon, and
/// the heterogeneous aggregate, with the linear ring carried as its closed line
/// string and the aggregate always materializing the heterogeneous collection. The
/// textured model element and every element outside the format's namespace refuse
/// with a typed reason at the first offending byte; presentation elements inside
/// recognized geometry are consumed without inspection. Refusals are returned,
/// never thrown, and a refused parse rents nothing — the one exception is the
/// inherited allocator seam contract, where an allocator violating the
/// exact-length contract makes the build throw after acceptance.
/// </summary>
public static class KmlGeometryReader
{
    /// <summary>
    /// Reads a KML geometry document with the default heap allocators. False
    /// reports the refusal; the geometry is its default value on every refusal.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> utf8Document, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryRead(utf8Document, FlatGeometryAllocators.Default, out geometry, out refusal);
    }

    /// <summary>
    /// Reads a KML geometry document through the allocator seam. Nothing is rented
    /// before the whole document is accepted, so a refused parse leaves nothing to
    /// dispose.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> utf8Document, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        FlatGeometryBuilder builder = new();

        if(!ParseDocument(utf8Document, builder, out int rootIndex, out refusal))
        {
            geometry = default;

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
    public static bool TryRead(ReadOnlySpan<char> text, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryRead(text, FlatGeometryAllocators.Default, out geometry, out refusal);
    }

    /// <summary>
    /// The character-span convenience over the allocator seam. Refusal offsets
    /// index the transcoded UTF-8 representation, not character positions.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<char> text, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        byte[] utf8Document = new byte[Encoding.UTF8.GetByteCount(text)];
        Encoding.UTF8.GetBytes(text, utf8Document);

        return TryRead(utf8Document, allocators, out geometry, out refusal);
    }

    /// <summary>The uncommitted-arity marker for a leaf whose first tuple has not yet fixed the component count.</summary>
    private const int UncommittedArity = 0;

    /// <summary>
    /// The per-parse state: the builder, the per-run tuple anchor scratch, and the
    /// collection-depth accounting.
    /// </summary>
    private sealed class KmlParseContext
    {
        /// <summary>The builder accumulating the parse.</summary>
        public FlatGeometryBuilder Builder { get; }

        /// <summary>The document offsets of the current run's tuple starts, for count adjudication anchors.</summary>
        public List<int> TupleStarts { get; } = [];

        /// <summary>The count of open collection wrappers, for the geometry bound.</summary>
        public int CollectionDepth { get; set; }

        /// <summary>Builds the context for one parse.</summary>
        public KmlParseContext(FlatGeometryBuilder builder)
        {
            Builder = builder;
        }
    }

    /// <summary>One open heterogeneous collection in the iterative tree walk.</summary>
    private readonly record struct CollectionFrame(int NodeIndex, List<int> Children);

    /// <summary>
    /// Parses the whole document: the root geometry element, the tree, and the
    /// epilog drain — the scanner must reach its exhaustion return before anything
    /// is built, so a trailing offense refuses with nothing rented.
    /// </summary>
    private static bool ParseDocument(ReadOnlySpan<byte> document, FlatGeometryBuilder builder, out int rootIndex, out GeometryCodecRefusal refusal)
    {
        rootIndex = -1;

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

            if(!TryRequireKmlGeometryOpen(ref scanner, out refusal))
            {
                return false;
            }

            KmlParseContext context = new(builder);

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

            refusal = GeometryCodecRefusal.None;

            return true;
        }
        finally
        {
            scanner.Dispose();
        }
    }

    /// <summary>
    /// A geometry-identity position must hold a KML-namespace element: the vendor
    /// extension namespace refuses as prohibited before identity, and every other
    /// foreign or absent namespace refuses as unsupported.
    /// </summary>
    private static bool TryRequireKmlGeometryOpen(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        if(scanner.ElementNamespace.SequenceEqual(KmlVocabulary.GxNamespace))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.ProhibitedConstruct, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementNamespace.SequenceEqual(KmlVocabulary.KmlNamespace))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// The iterative geometry tree walk: the heterogeneous aggregate nests through
    /// an explicit frame stack — the format's one self-nesting production — while
    /// every other element parses through the statically bounded grammar descent.
    /// The current token on entry is the ElementOpen of a KML-namespace element the
    /// caller verified.
    /// </summary>
    private static bool TryParseGeometryTree(ref XmlFragmentScanner scanner, KmlParseContext context, out int rootIndex, out GeometryCodecRefusal refusal)
    {
        List<CollectionFrame> frames = [];
        rootIndex = -1;

        while(true)
        {
            //The current token is the ElementOpen of a KML-namespace element.
            int completedNode;

            if(context.CollectionDepth + 1 > GeometryCodecText.MaximumNestingDepth)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NestingTooDeep, scanner.TokenStartOffset);

                return false;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.MultiGeometryName))
            {
                int nodeIndex = context.Builder.AddNode(GeometryKind.GeometryCollection, hasZ: false, hasM: false, firstPart: 0, partCount: 0);
                frames.Add(new CollectionFrame(nodeIndex, []));
                context.CollectionDepth++;

                if(!TryStepIntoCollectionBody(ref scanner, out bool collectionClosed, out refusal))
                {
                    return false;
                }

                if(!collectionClosed)
                {
                    //The walk stands at a member child's ElementOpen; parse it on the next loop turn.
                    continue;
                }

                //The memberless aggregate is schema-valid but semantics-free; the violation is final where the element ends.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            if(!TryParseLeaf(ref scanner, context, out completedNode, out refusal))
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

                if(!TryStepIntoCollectionBody(ref scanner, out bool collectionClosed, out refusal))
                {
                    return false;
                }

                if(!collectionClosed)
                {
                    break;
                }

                CollectionFrame frame = frames[^1];
                frames.RemoveAt(frames.Count - 1);
                context.CollectionDepth--;
                context.Builder.SetChildren(frame.NodeIndex, frame.Children);
                completedNode = frame.NodeIndex;
            }
        }
    }

    /// <summary>
    /// Advances inside an aggregate body to its next member child or its close:
    /// skips ignorable whitespace and verifies the member's namespace identity.
    /// True with the collection open leaves the walk at the member's ElementOpen.
    /// </summary>
    private static bool TryStepIntoCollectionBody(ref XmlFragmentScanner scanner, out bool collectionClosed, out GeometryCodecRefusal refusal)
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

        return TryRequireKmlGeometryOpen(ref scanner, out refusal);
    }

    /// <summary>
    /// Dispatches a non-aggregate geometry element: the point, the line string,
    /// the linear ring carried as its closed line string, and the polygon; every
    /// other name in the format's namespace — the textured model included —
    /// refuses as unsupported. The current token is the element's ElementOpen; on
    /// success the element's ElementClose has been consumed.
    /// </summary>
    private static bool TryParseLeaf(ref XmlFragmentScanner scanner, KmlParseContext context, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.PointName))
        {
            return TryParsePoint(ref scanner, context, out nodeIndex, out refusal);
        }

        if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.LineStringName))
        {
            return TryParseLine(ref scanner, context, allowTessellate: true, requireClosed: false, out nodeIndex, out refusal);
        }

        if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.LinearRingName))
        {
            //The ring rules run, then the value lands as the closed line string — the one-way kind carriage.
            return TryParseLine(ref scanner, context, allowTessellate: true, requireClosed: true, out nodeIndex, out refusal);
        }

        if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.PolygonName))
        {
            return TryParsePolygon(ref scanner, context, out nodeIndex, out refusal);
        }

        refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, scanner.TokenStartOffset);

        return false;
    }

    /// <summary>
    /// Parses a Point element: the shared coordinate body with exactly one tuple —
    /// a second tuple refuses at its own first byte, an absent or empty run where
    /// the count became final. The element's ElementClose is consumed.
    /// </summary>
    private static bool TryParsePoint(ref XmlFragmentScanner scanner, KmlParseContext context, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        int start = context.Builder.VertexCount;
        int committedArity = UncommittedArity;

        if(!TryParseCoordinateBody(ref scanner, context, allowTessellate: false, ref committedArity, out int positionCount, out bool sawCoordinates, out int coordinatesCloseOffset, out int leafCloseOffset, out refusal))
        {
            return false;
        }

        if(!sawCoordinates)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, leafCloseOffset);

            return false;
        }

        if(positionCount == 0)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, coordinatesCloseOffset);

            return false;
        }

        if(positionCount > 1)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, context.TupleStarts[1]);

            return false;
        }

        context.Builder.AddPart(new FlatGeometryPart(start, 1, FlatGeometryPartRole.Point));
        nodeIndex = context.Builder.AddNode(GeometryKind.Point, committedArity == 3, hasM: false, context.Builder.PartCount - 1, 1);

        return true;
    }

    /// <summary>
    /// Parses a LineString or LinearRing element into one line part: the shared
    /// coordinate body, the per-kind count floor at the run-terminating byte, and
    /// for rings the closure rule on the exact planar predicate. The element's
    /// ElementClose is consumed.
    /// </summary>
    private static bool TryParseLine(ref XmlFragmentScanner scanner, KmlParseContext context, bool allowTessellate, bool requireClosed, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        int start = context.Builder.VertexCount;
        int committedArity = UncommittedArity;

        if(!TryParseCoordinateBody(ref scanner, context, allowTessellate, ref committedArity, out int positionCount, out bool sawCoordinates, out int coordinatesCloseOffset, out int leafCloseOffset, out refusal))
        {
            return false;
        }

        if(!sawCoordinates)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, leafCloseOffset);

            return false;
        }

        if(!TryCheckLineRun(context, start, positionCount, requireClosed, coordinatesCloseOffset, out refusal))
        {
            return false;
        }

        context.Builder.AddPart(new FlatGeometryPart(start, positionCount, FlatGeometryPartRole.Line));
        nodeIndex = context.Builder.AddNode(GeometryKind.LineString, committedArity == 3, hasM: false, context.Builder.PartCount - 1, 1);

        return true;
    }

    /// <summary>
    /// Adjudicates a completed line or ring run: lines need two positions, rings
    /// four with the first and last equal on the exact planar predicate — every
    /// shortfall anchored at the run-terminating byte, where the count became
    /// final.
    /// </summary>
    private static bool TryCheckLineRun(KmlParseContext context, int start, int positionCount, bool requireClosed, int runTerminator, out GeometryCodecRefusal refusal)
    {
        if(requireClosed)
        {
            if(positionCount < 4 || !context.Builder.VerticesEqualXy(start, (start + positionCount) - 1))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runTerminator);

                return false;
            }
        }
        else if(positionCount < 2)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runTerminator);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses a Polygon element: the presentation prefix, the required exterior
    /// boundary before every interior one, and the ring runs threaded through one
    /// arity commitment — the node carries one dimensionality. The element's
    /// ElementClose is consumed.
    /// </summary>
    private static bool TryParsePolygon(ref XmlFragmentScanner scanner, KmlParseContext context, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;

        int firstPart = context.Builder.PartCount;
        int committedArity = UncommittedArity;
        int lastSlot = -1;
        bool sawOuter = false;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                if(!sawOuter)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                break;
            }

            if(!TryClassifyLeafChild(ref scanner, out bool skipped, out refusal))
            {
                return false;
            }

            if(skipped)
            {
                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.ExtrudeName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 0, repeatable: false, out refusal) || !TrySkipSubtree(ref scanner, out refusal))
                {
                    return false;
                }

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.TessellateName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 1, repeatable: false, out refusal) || !TrySkipSubtree(ref scanner, out refusal))
                {
                    return false;
                }

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.AltitudeModeName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 2, repeatable: false, out refusal) || !TryReadAltitudeMode(ref scanner, out refusal))
                {
                    return false;
                }

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.OuterBoundaryName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 3, repeatable: false, out refusal))
                {
                    return false;
                }

                if(!TryParseBoundary(ref scanner, context, FlatGeometryPartRole.ExteriorRing, ref committedArity, out refusal))
                {
                    return false;
                }

                sawOuter = true;

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.InnerBoundaryName))
            {
                if(!sawOuter)
                {
                    //The required exterior boundary can no longer precede this one; the violation is inevitable at the interior's own name.
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 4, repeatable: true, out refusal))
                {
                    return false;
                }

                if(!TryParseBoundary(ref scanner, context, FlatGeometryPartRole.InteriorRing, ref committedArity, out refusal))
                {
                    return false;
                }

                continue;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        nodeIndex = context.Builder.AddNode(GeometryKind.Polygon, committedArity == 3, hasM: false, firstPart, context.Builder.PartCount - firstPart);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses one boundary property: at most one LinearRing among tolerated
    /// foreign children, its run appended with the boundary's role and threaded
    /// through the polygon's arity commitment. The exterior boundary requires its
    /// ring — the polygon must carry an outer boundary ring — while an interior
    /// boundary without one is the ring-less property the format's boundary
    /// content model admits and contributes no interior ring. The boundary's
    /// ElementClose is consumed.
    /// </summary>
    private static bool TryParseBoundary(ref XmlFragmentScanner scanner, KmlParseContext context, FlatGeometryPartRole role, ref int committedArity, out GeometryCodecRefusal refusal)
    {
        bool sawRing = false;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                if(!sawRing && role == FlatGeometryPartRole.ExteriorRing)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                    return false;
                }

                refusal = GeometryCodecRefusal.None;

                return true;
            }

            if(!TryClassifyLeafChild(ref scanner, out bool skipped, out refusal))
            {
                return false;
            }

            if(skipped)
            {
                continue;
            }

            if(!scanner.ElementLocalName.SequenceEqual(KmlVocabulary.LinearRingName) || sawRing)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

                return false;
            }

            int start = context.Builder.VertexCount;

            if(!TryParseCoordinateBody(ref scanner, context, allowTessellate: true, ref committedArity, out int positionCount, out bool sawCoordinates, out int coordinatesCloseOffset, out int leafCloseOffset, out refusal))
            {
                return false;
            }

            if(!sawCoordinates)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, leafCloseOffset);

                return false;
            }

            if(!TryCheckLineRun(context, start, positionCount, requireClosed: true, coordinatesCloseOffset, out refusal))
            {
                return false;
            }

            context.Builder.AddPart(new FlatGeometryPart(start, positionCount, role));
            sawRing = true;
        }
    }

    /// <summary>
    /// Classifies a child element inside a recognized element's content: the
    /// vendor extension namespace refuses as prohibited, an absent namespace
    /// refuses structurally, any other foreign namespace is skipped wholesale, and
    /// a KML-namespace child returns for the caller's content-model dispatch.
    /// </summary>
    private static bool TryClassifyLeafChild(ref XmlFragmentScanner scanner, out bool skipped, out GeometryCodecRefusal refusal)
    {
        skipped = false;

        if(scanner.ElementNamespace.SequenceEqual(KmlVocabulary.GxNamespace))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.ProhibitedConstruct, scanner.TokenStartOffset);

            return false;
        }

        if(scanner.ElementNamespace.IsEmpty)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        if(!scanner.ElementNamespace.SequenceEqual(KmlVocabulary.KmlNamespace))
        {
            if(!TrySkipSubtree(ref scanner, out refusal))
            {
                return false;
            }

            skipped = true;

            return true;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Enforces the content model's sequence: a slot behind or at the last taken
    /// one refuses at the element's own start — behind is out of order, at is a
    /// duplicate — except for the repeatable interior boundary.
    /// </summary>
    private static bool TryTakeSlot(ref XmlFragmentScanner scanner, ref int lastSlot, int slot, bool repeatable, out GeometryCodecRefusal refusal)
    {
        if(slot < lastSlot || (slot == lastSlot && !repeatable))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        lastSlot = slot;
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// The shared coordinate-geometry body: the presentation prefix in schema
    /// order, the altitude-mode adjudication, and the coordinate run — reporting
    /// the tuple count, whether the run element appeared at all, and the two
    /// close anchors the per-kind rules refuse at. The leaf element's ElementClose
    /// is consumed.
    /// </summary>
    private static bool TryParseCoordinateBody(ref XmlFragmentScanner scanner, KmlParseContext context, bool allowTessellate, ref int committedArity, out int positionCount, out bool sawCoordinates, out int coordinatesCloseOffset, out int leafCloseOffset, out GeometryCodecRefusal refusal)
    {
        positionCount = 0;
        sawCoordinates = false;
        coordinatesCloseOffset = -1;
        leafCloseOffset = -1;

        int lastSlot = -1;

        while(true)
        {
            if(!TrySkipToElementToken(ref scanner, out XmlFragmentTokenKind kind, out refusal))
            {
                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementClose)
            {
                leafCloseOffset = scanner.TokenStartOffset;
                refusal = GeometryCodecRefusal.None;

                return true;
            }

            if(!TryClassifyLeafChild(ref scanner, out bool skipped, out refusal))
            {
                return false;
            }

            if(skipped)
            {
                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.ExtrudeName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 0, repeatable: false, out refusal) || !TrySkipSubtree(ref scanner, out refusal))
                {
                    return false;
                }

                continue;
            }

            if(allowTessellate && scanner.ElementLocalName.SequenceEqual(KmlVocabulary.TessellateName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 1, repeatable: false, out refusal) || !TrySkipSubtree(ref scanner, out refusal))
                {
                    return false;
                }

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.AltitudeModeName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 2, repeatable: false, out refusal) || !TryReadAltitudeMode(ref scanner, out refusal))
                {
                    return false;
                }

                continue;
            }

            if(scanner.ElementLocalName.SequenceEqual(KmlVocabulary.CoordinatesName))
            {
                if(!TryTakeSlot(ref scanner, ref lastSlot, slot: 3, repeatable: false, out refusal))
                {
                    return false;
                }

                if(!TryReadCoordinateRun(ref scanner, context, ref committedArity, out positionCount, out coordinatesCloseOffset, out refusal))
                {
                    return false;
                }

                sawCoordinates = true;

                continue;
            }

            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }
    }

    /// <summary>
    /// Reads an altitude-mode element's simple content: the exactly-empty element
    /// is the schema default and accepts with no effect; a value token must equal
    /// one of the three enumeration constants byte for byte — padded and
    /// whitespace-only content refuse at the content's first byte; an element
    /// inside the simple content refuses as malformed at its own first byte. The
    /// element's ElementClose is consumed.
    /// </summary>
    private static bool TryReadAltitudeMode(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        if(!TryReadInsideSimpleContent(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementClose)
        {
            return true;
        }

        //The token is the region's decoded text; the comparison is schema-exact, no trimming.
        bool recognized = scanner.Text.SequenceEqual(KmlVocabulary.ClampToGroundValue)
            || scanner.Text.SequenceEqual(KmlVocabulary.RelativeToGroundValue)
            || scanner.Text.SequenceEqual(KmlVocabulary.AbsoluteValue);

        if(!recognized)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, scanner.TokenStartOffset);

            return false;
        }

        return TryConsumeSimpleContentClose(ref scanner, out refusal);
    }

    /// <summary>
    /// Reads the first token inside a simple-content element: the close for the
    /// empty form, the text region for a value, and the malformed refusal for a
    /// child element — simple content admits no element in any namespace, so the
    /// foreign-skip tolerance never applies here.
    /// </summary>
    private static bool TryReadInsideSimpleContent(ref XmlFragmentScanner scanner, out XmlFragmentTokenKind kind, out GeometryCodecRefusal refusal)
    {
        if(!scanner.TryReadNext(out kind, out refusal))
        {
            if(refusal == GeometryCodecRefusal.None)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, 0);
            }

            return false;
        }

        if(kind == XmlFragmentTokenKind.ElementOpen)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.TokenStartOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Consumes a simple-content element's close after its text region; a further element refuses as malformed at its own first byte.</summary>
    private static bool TryConsumeSimpleContentClose(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        if(!TryReadInsideSimpleContent(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        if(kind != XmlFragmentTokenKind.ElementClose)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.TokenStartOffset);

            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads one coordinates element: the run's tuples parse into the builder with
    /// their anchors recorded, and the close token's offset rides out as the
    /// run-terminating anchor for every count adjudication that fires after the
    /// run became final. The element's ElementClose is consumed.
    /// </summary>
    private static bool TryReadCoordinateRun(ref XmlFragmentScanner scanner, KmlParseContext context, ref int committedArity, out int positionCount, out int closeOffset, out GeometryCodecRefusal refusal)
    {
        positionCount = 0;
        closeOffset = -1;
        context.TupleStarts.Clear();

        if(!TryReadInsideSimpleContent(ref scanner, out XmlFragmentTokenKind kind, out refusal))
        {
            return false;
        }

        bool pendingShortTuple = false;

        if(kind == XmlFragmentTokenKind.Text)
        {
            if(!TryParseTupleRun(ref scanner, context, ref committedArity, ref positionCount, out pendingShortTuple, out refusal))
            {
                return false;
            }

            if(!TryConsumeSimpleContentClose(ref scanner, out refusal))
            {
                return false;
            }
        }

        closeOffset = scanner.TokenStartOffset;

        if(pendingShortTuple)
        {
            //The run's last tuple ended one component wide at the end of the content; the shortfall became final where the run ends.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, closeOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Tokenizes the current text region as the tuple grammar: whitespace
    /// separates tuples, a comma with its adjacent whitespace separates the
    /// components inside one — the format's one deliberate tolerance. Components
    /// parse as finite doubles with the shared non-finite classification; a
    /// missing component refuses where one was required, a fourth at its own
    /// first byte, a one-component tuple at its terminating byte, and an arity
    /// against the leaf's commitment at the disagreeing tuple's first byte.
    /// </summary>
    private static bool TryParseTupleRun(ref XmlFragmentScanner scanner, KmlParseContext context, ref int committedArity, ref int positionCount, out bool pendingShortTuple, out GeometryCodecRefusal refusal)
    {
        pendingShortTuple = false;

        ReadOnlySpan<byte> text = scanner.Text;
        int position = 0;

        while(position < text.Length && XmlLexicon.IsWhitespace(text[position]))
        {
            position++;
        }

        while(position < text.Length)
        {
            int tupleStart = scanner.MapTextOffset(position);
            int components = 0;
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            int terminator = -1;

            while(true)
            {
                if(text[position] == (byte)',')
                {
                    //A component must begin here; the comma that stands in its place is the offense.
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.MapTextOffset(position));

                    return false;
                }

                int componentStart = position;

                while(position < text.Length && text[position] != (byte)',' && !XmlLexicon.IsWhitespace(text[position]))
                {
                    position++;
                }

                components++;

                if(components > 3)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.MapTextOffset(componentStart));

                    return false;
                }

                if(!TryParseTupleComponent(text[componentStart..position], out double value, out GeometryCodecRefusalKind offense))
                {
                    refusal = new GeometryCodecRefusal(offense, scanner.MapTextOffset(componentStart));

                    return false;
                }

                if(components == 1)
                {
                    x = value;
                }
                else if(components == 2)
                {
                    y = value;
                }
                else
                {
                    z = value;
                }

                if(position >= text.Length)
                {
                    break;
                }

                if(XmlLexicon.IsWhitespace(text[position]))
                {
                    int whitespaceStart = position;

                    while(position < text.Length && XmlLexicon.IsWhitespace(text[position]))
                    {
                        position++;
                    }

                    if(position < text.Length && text[position] == (byte)',')
                    {
                        //The whitespace bound to the comma; the separator continues the tuple.
                        if(!TryConsumeComponentSeparator(ref scanner, text, ref position, out refusal))
                        {
                            return false;
                        }

                        continue;
                    }

                    terminator = whitespaceStart;

                    break;
                }

                //A comma directly after the component continues the tuple.
                if(!TryConsumeComponentSeparator(ref scanner, text, ref position, out refusal))
                {
                    return false;
                }
            }

            if(components == 1)
            {
                if(terminator < 0)
                {
                    //The shortfall becomes final only when the run does; the caller anchors it at the run terminator.
                    pendingShortTuple = true;
                    refusal = GeometryCodecRefusal.None;

                    return true;
                }

                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, scanner.MapTextOffset(terminator));

                return false;
            }

            if(committedArity == UncommittedArity)
            {
                committedArity = components;
            }
            else if(components != committedArity)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, tupleStart);

                return false;
            }

            if(components == 3)
            {
                context.Builder.AddVertex(new Point2d(x, y), z, double.NaN);
            }
            else
            {
                context.Builder.AddVertex(new Point2d(x, y));
            }

            context.TupleStarts.Add(tupleStart);
            positionCount++;

            while(position < text.Length && XmlLexicon.IsWhitespace(text[position]))
            {
                position++;
            }
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Consumes one comma component separator with its trailing whitespace, from
    /// the comma the caller stands on: a run that ends behind the comma refuses at
    /// the comma itself — the trailing separator never gained its component.
    /// </summary>
    private static bool TryConsumeComponentSeparator(ref XmlFragmentScanner scanner, ReadOnlySpan<byte> text, ref int position, out GeometryCodecRefusal refusal)
    {
        int commaOffset = position;
        position++;

        while(position < text.Length && XmlLexicon.IsWhitespace(text[position]))
        {
            position++;
        }

        if(position >= text.Length)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, scanner.MapTextOffset(commaOffset));

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses one tuple component: a finite double on success; the schema-legal
    /// non-finite spellings and value overflow classify as non-finite, anything
    /// else as malformed — the classification the GML reader ships, so the two
    /// XML codecs return identical kinds for identical tokens.
    /// </summary>
    private static bool TryParseTupleComponent(ReadOnlySpan<byte> token, out double value, out GeometryCodecRefusalKind offense)
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
    /// Consumes a tolerated element's whole subtree without content inspection —
    /// the transport floor still fires inside it, because transport is not content
    /// policy.
    /// </summary>
    private static bool TrySkipSubtree(ref XmlFragmentScanner scanner, out GeometryCodecRefusal refusal)
    {
        int depth = 1;

        while(depth > 0)
        {
            if(!scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
            {
                if(refusal == GeometryCodecRefusal.None)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, 0);
                }

                return false;
            }

            if(kind == XmlFragmentTokenKind.ElementOpen)
            {
                depth++;
            }
            else if(kind == XmlFragmentTokenKind.ElementClose)
            {
                depth--;
            }
        }

        refusal = GeometryCodecRefusal.None;

        return true;
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
}
