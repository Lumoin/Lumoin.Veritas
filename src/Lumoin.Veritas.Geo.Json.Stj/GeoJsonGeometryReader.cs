using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Json.Stj;

/// <summary>
/// The RFC 7946 GeoJSON reader of the Simple Features substrate: parses one
/// bare Geometry object into a <see cref="FlatGeometry"/> and refuses
/// everything else by value with the first offense's byte offset. The
/// accepted surface: the seven case-sensitive geometry type tags with free
/// member order (the type member may follow the coordinates), positions of
/// two or three finite numbers with uniform arity per geometry (collection
/// members may differ — each carries its own node), typed empties as empty
/// coordinate arrays, geometry collections nested to the certified bound,
/// foreign members and bbox ignored, and the removed legacy crs member
/// recognized only in its 2008 name form naming CRS84. Feature and
/// FeatureCollection roots refuse — the literal seam wants geometry values.
/// GeoJSON fixes its coordinate reference system to CRS84
/// longitude-latitude degrees; the reader yields exactly that in XY.
/// A refused parse rents nothing; the one exception is the inherited
/// allocator seam contract — an allocator violating the exact-length
/// contract makes the build throw after acceptance. Malformed JSON is
/// contained at the codec boundary: the underlying reader's exception is
/// translated to a refusal whose offset is the consumed-byte snapshot at
/// the failing read, never rethrown.
/// </summary>
/// <remarks>
/// <see cref="System.Text.Json"/> types are confined to this binding
/// assembly and never appear in the Geo library's signatures: the library
/// reaches this reader only through the
/// <see cref="GeoJsonGeometryReadDelegate"/> contract a composing host
/// supplies at function registration, the same confinement discipline the
/// JSON parse seam uses for its own System.Text.Json adapter.
/// </remarks>
public static class GeoJsonGeometryReader
{
    /// <summary>
    /// The transport depth rides the shared codec constant so every format's
    /// tokenizer is sized by the same derivation.
    /// </summary>
    private const int TransportMaxDepth = GeometryCodecText.MaximumTransportDepth;

    /// <summary>
    /// Parses one UTF-8 GeoJSON Geometry object into a
    /// <see cref="FlatGeometry"/> with heap-backed columns.
    /// </summary>
    /// <param name="utf8Document">The complete UTF-8 document.</param>
    /// <param name="geometry">The parsed geometry, or default on refusal.</param>
    /// <param name="refusal">
    /// The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on
    /// success.
    /// </param>
    /// <returns>True when the document was accepted.</returns>
    public static bool TryRead(ReadOnlySpan<byte> utf8Document, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryRead(utf8Document, FlatGeometryAllocators.Default, out geometry, out refusal);
    }

    /// <summary>
    /// Parses one UTF-8 GeoJSON Geometry object, renting the vertex-scale
    /// columns through the caller's allocator seam — a pooling host binds
    /// its own pool here and then owns the built geometry's disposal. An
    /// allocator that violates the exact-length contract makes the build
    /// throw; a refused parse disposes nothing (no rental happens before
    /// the document is accepted).
    /// </summary>
    /// <param name="utf8Document">The complete UTF-8 document.</param>
    /// <param name="allocators">The column allocator seam.</param>
    /// <param name="geometry">The parsed geometry, or default on refusal.</param>
    /// <param name="refusal">
    /// The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on
    /// success.
    /// </param>
    /// <returns>True when the document was accepted.</returns>
    public static bool TryRead(ReadOnlySpan<byte> utf8Document, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        geometry = default;

        if(utf8Document.Length == 0)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, 0);

            return false;
        }

        if(GeometryCodecText.StartsWithByteOrderMark(utf8Document))
        {
            //A leading byte-order mark refuses: the format's grammar starts at the first
            //byte, in lockstep with the sibling recognizer's exact reading.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, 0);

            return false;
        }

        var builder = new FlatGeometryBuilder();

        if(!ParseDocument(utf8Document, builder, out int rootIndex, out refusal))
        {
            return false;
        }

        builder.RootIndex = rootIndex;
        geometry = builder.ToGeometry(allocators);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Parses GeoJSON from characters by transcoding to UTF-8 first; a
    /// convenience overload for hosts and tests, off the byte-oriented
    /// primary path. Refusal offsets index the transcoded UTF-8
    /// representation, not character positions.
    /// </summary>
    /// <param name="text">The complete document text.</param>
    /// <param name="geometry">The parsed geometry, or default on refusal.</param>
    /// <param name="refusal">
    /// The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on
    /// success.
    /// </param>
    /// <returns>True when the document was accepted.</returns>
    public static bool TryRead(ReadOnlySpan<char> text, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        return TryRead(text, FlatGeometryAllocators.Default, out geometry, out refusal);
    }

    /// <summary>
    /// Parses GeoJSON from characters through the caller's allocator seam;
    /// see the byte-oriented overload for the rental contract and the
    /// character overload above for the offset frame.
    /// </summary>
    /// <param name="text">The complete document text.</param>
    /// <param name="allocators">The column allocator seam.</param>
    /// <param name="geometry">The parsed geometry, or default on refusal.</param>
    /// <param name="refusal">
    /// The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on
    /// success.
    /// </param>
    /// <returns>True when the document was accepted.</returns>
    public static bool TryRead(ReadOnlySpan<char> text, FlatGeometryAllocators allocators, out FlatGeometry geometry, out GeometryCodecRefusal refusal)
    {
        byte[] utf8Document = new byte[Encoding.UTF8.GetByteCount(text)];
        Encoding.UTF8.GetBytes(text, utf8Document);

        return TryRead(utf8Document, allocators, out geometry, out refusal);
    }

    /// <summary>
    /// Parses the root geometry object and every nested collection member
    /// iteratively: an explicit frame stack carries open collections whose
    /// member extents were pre-sliced during the member scan, so nesting
    /// never recurses and each member parses over its own complete slice.
    /// </summary>
    internal static bool ParseDocument(ReadOnlySpan<byte> document, FlatGeometryBuilder builder, out int rootIndex, out GeometryCodecRefusal refusal)
    {
        rootIndex = -1;

        //Frames exist only for geometry collections; every other kind parses inline.
        var frames = new Stack<CollectionFrame>();
        int sliceStart = 0;
        int sliceLength = document.Length;
        bool isRoot = true;
        int completedNode;

        while(true)
        {
            if(frames.Count + 1 > GeometryCodecText.MaximumNestingDepth)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NestingTooDeep, sliceStart);

                return false;
            }

            if(!ScanGeometryObject(document.Slice(sliceStart, sliceLength), sliceStart, isRoot, out ScannedObject scanned, out refusal))
            {
                return false;
            }

            if(isRoot)
            {
                int trailingOffset = scanned.ConsumedEnd;

                while(trailingOffset < document.Length && IsJsonWhitespace(document[trailingOffset]))
                {
                    trailingOffset++;
                }

                if(trailingOffset < document.Length)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.TrailingContent, trailingOffset);

                    return false;
                }

                isRoot = false;
            }

            if(scanned.Kind == GeometryKind.GeometryCollection)
            {
                List<(int Start, int Length)> memberExtents = [];

                if(!SliceCollectionMembers(document.Slice(scanned.GeometriesStart, scanned.GeometriesLength), scanned.GeometriesStart, memberExtents, out refusal))
                {
                    return false;
                }

                //A collection node carries no part run, so its first-part slot is pinned
                //at zero like every other producer's — a nested collection recording the
                //running part count here would compare unequal to the same value from
                //any other reader.
                int collectionNode = builder.AddNode(GeometryKind.GeometryCollection, hasZ: false, hasM: false, firstPart: 0, partCount: 0);

                if(memberExtents.Count == 0)
                {
                    //A memberless collection attaches no child list at all: an empty list
                    //would still record a child run and push the node's first-child index
                    //past the table, so the built value would differ from the typed empty
                    //every other producer makes.
                    completedNode = collectionNode;
                }
                else
                {
                    frames.Push(new CollectionFrame(collectionNode, [], memberExtents));
                    (sliceStart, sliceLength) = memberExtents[0];

                    continue;
                }
            }
            else
            {
                if(!ParseLeafCoordinates(document.Slice(scanned.CoordinatesStart, scanned.CoordinatesLength), scanned.CoordinatesStart, scanned.Kind, builder, out completedNode, out refusal))
                {
                    return false;
                }
            }

            //A finished member attaches upward, closing every collection whose member
            //list it completed, then steps to the next pending member extent.
            bool moved = false;

            while(frames.Count > 0)
            {
                CollectionFrame frame = frames.Peek();

                frame.Children.Add(completedNode);

                if(frame.Children.Count < frame.MemberExtents.Count)
                {
                    (sliceStart, sliceLength) = frame.MemberExtents[frame.Children.Count];
                    moved = true;

                    break;
                }

                builder.SetChildren(frame.NodeIndex, frame.Children);
                completedNode = frame.NodeIndex;
                frames.Pop();
            }

            if(!moved)
            {
                rootIndex = completedNode;
                refusal = GeometryCodecRefusal.None;

                return true;
            }
        }
    }

    /// <summary>
    /// Scans one geometry object's members in whatever order the document
    /// carries them, recording the type and the extents of the coordinate
    /// payload for the kind-aware pass, adjudicating the kind-independent
    /// members (crs, bbox, foreign members, duplicates) immediately.
    /// </summary>
    private static bool ScanGeometryObject(ReadOnlySpan<byte> slice, int baseOffset, bool isRoot, out ScannedObject scanned, out GeometryCodecRefusal refusal)
    {
        scanned = default;
        var reader = new Utf8JsonReader(slice, new JsonReaderOptions { MaxDepth = TransportMaxDepth });

        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        if(reader.TokenType != JsonTokenType.StartObject)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, baseOffset + (int)reader.TokenStartIndex);

            return false;
        }

        bool typeSeen = false;
        bool coordinatesSeen = false;
        bool geometriesSeen = false;
        bool crsSeen = false;
        bool bboxSeen = false;
        GeometryKind kind = default;
        int coordinatesNameOffset = -1;
        int geometriesNameOffset = -1;
        int coordinatesStart = 0;
        int coordinatesLength = 0;
        int geometriesStart = 0;
        int geometriesLength = 0;

        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            int nameOffset = baseOffset + (int)reader.TokenStartIndex;

            if(reader.ValueTextEquals(GeoJsonVocabulary.TypeMemberName))
            {
                if(typeSeen)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, nameOffset);

                    return false;
                }

                typeSeen = true;

                if(!TryAdvance(ref reader, baseOffset, out refusal))
                {
                    return false;
                }

                int valueOffset = baseOffset + (int)reader.TokenStartIndex;

                if(reader.TokenType != JsonTokenType.String)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, valueOffset);

                    return false;
                }

                if(!TryClassifyType(ref reader, out kind))
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnsupportedGeometry, valueOffset);

                    return false;
                }
            }
            else if(reader.ValueTextEquals(GeoJsonVocabulary.CoordinatesMemberName))
            {
                if(coordinatesSeen)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, nameOffset);

                    return false;
                }

                coordinatesSeen = true;
                coordinatesNameOffset = nameOffset;

                if(!TrySliceValue(ref reader, baseOffset, out coordinatesStart, out coordinatesLength, out refusal))
                {
                    return false;
                }
            }
            else if(reader.ValueTextEquals(GeoJsonVocabulary.GeometriesMemberName))
            {
                if(geometriesSeen)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, nameOffset);

                    return false;
                }

                geometriesSeen = true;
                geometriesNameOffset = nameOffset;

                if(!TrySliceValue(ref reader, baseOffset, out geometriesStart, out geometriesLength, out refusal))
                {
                    return false;
                }
            }
            else if(reader.ValueTextEquals(GeoJsonVocabulary.CrsMemberName))
            {
                if(crsSeen)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, nameOffset);

                    return false;
                }

                crsSeen = true;

                if(!ParseCrsMember(ref reader, baseOffset, out refusal))
                {
                    return false;
                }
            }
            else if(reader.ValueTextEquals(GeoJsonVocabulary.BoundingBoxMemberName))
            {
                if(bboxSeen)
                {
                    refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, nameOffset);

                    return false;
                }

                bboxSeen = true;

                //The value is form-validated and discarded: the flat model is
                //geometry-only, and a bounding box over it is derivable metadata.
                if(!TryParseBoundingBoxMember(ref reader, baseOffset, out _, out refusal))
                {
                    return false;
                }
            }
            else if(reader.ValueTextEquals(GeoJsonVocabulary.GeometryMemberName) || reader.ValueTextEquals(GeoJsonVocabulary.PropertiesMemberName) || reader.ValueTextEquals(GeoJsonVocabulary.FeaturesMemberName))
            {
                //The defining members of the Feature and FeatureCollection types are
                //carved out of the foreign-member tolerance: a Geometry object must not
                //carry them, so their presence is a structural offense, not an extension.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, nameOffset);

                return false;
            }
            else
            {
                //Foreign members are ignored whole, duplicated or not — only the
                //recognized members carry the duplication refusal.
                if(!TrySkipValue(ref reader, baseOffset, out refusal))
                {
                    return false;
                }
            }
        }

        int objectEndOffset = baseOffset + (int)reader.TokenStartIndex;

        if(!typeSeen)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, objectEndOffset);

            return false;
        }

        if(kind == GeometryKind.GeometryCollection)
        {
            if(coordinatesSeen)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, coordinatesNameOffset);

                return false;
            }

            if(!geometriesSeen)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, objectEndOffset);

                return false;
            }
        }
        else
        {
            if(geometriesSeen)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, geometriesNameOffset);

                return false;
            }

            if(!coordinatesSeen)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, objectEndOffset);

                return false;
            }
        }

        scanned = new ScannedObject(
            kind,
            coordinatesStart,
            coordinatesLength,
            geometriesStart,
            geometriesLength,
            isRoot ? baseOffset + (int)reader.BytesConsumed : 0);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Enumerates a geometries array into per-member slices, each a
    /// complete geometry object extent the driver loop parses in turn.
    /// </summary>
    private static bool SliceCollectionMembers(ReadOnlySpan<byte> slice, int baseOffset, List<(int Start, int Length)> memberExtents, out GeometryCodecRefusal refusal)
    {
        var reader = new Utf8JsonReader(slice, new JsonReaderOptions { MaxDepth = TransportMaxDepth });

        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        if(reader.TokenType != JsonTokenType.StartArray)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, baseOffset + (int)reader.TokenStartIndex);

            return false;
        }

        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                refusal = GeometryCodecRefusal.None;

                return true;
            }

            if(reader.TokenType != JsonTokenType.StartObject)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, baseOffset + (int)reader.TokenStartIndex);

                return false;
            }

            int memberStart = baseOffset + (int)reader.TokenStartIndex;

            if(!TrySkipCurrent(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            memberExtents.Add((memberStart, baseOffset + (int)reader.BytesConsumed - memberStart));
        }
    }

    /// <summary>
    /// Parses one leaf kind's coordinates payload in a single forward pass,
    /// building parts and vertices; the payload slice was well-formedness
    /// validated during the member scan, so every refusal here is
    /// geometry-shaped, reported at its document-order byte.
    /// </summary>
    private static bool ParseLeafCoordinates(ReadOnlySpan<byte> slice, int baseOffset, GeometryKind kind, FlatGeometryBuilder builder, out int nodeIndex, out GeometryCodecRefusal refusal)
    {
        nodeIndex = -1;
        var reader = new Utf8JsonReader(slice, new JsonReaderOptions { MaxDepth = TransportMaxDepth });

        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        if(reader.TokenType != JsonTokenType.StartArray)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

            return false;
        }

        var state = new LeafState(builder.PartCount);

        bool accepted = kind switch
        {
            GeometryKind.Point => ParsePointBody(ref reader, baseOffset, builder, ref state, out refusal),
            GeometryKind.LineString => ParseLineBody(ref reader, baseOffset, builder, ref state, FlatGeometryPartRole.Line, minimumPositions: 2, requireClosed: false, allowEmpty: true, out refusal),
            GeometryKind.Polygon => ParseRingListBody(ref reader, baseOffset, builder, ref state, allowEmpty: true, out refusal),
            GeometryKind.MultiPoint => ParseMultiPointBody(ref reader, baseOffset, builder, ref state, out refusal),
            GeometryKind.MultiLineString => ParseListOfLinesBody(ref reader, baseOffset, builder, ref state, out refusal),
            GeometryKind.MultiPolygon => ParseMultiPolygonBody(ref reader, baseOffset, builder, ref state, out refusal),
            _ => Unreachable(out refusal)
        };

        if(!accepted)
        {
            return false;
        }

        //A partless node pins its first-part slot at zero — a typed-empty member
        //recording the running part count would compare unequal to the same value
        //from any other reader.
        int partCount = builder.PartCount - state.FirstPart;
        nodeIndex = builder.AddNode(kind, state.HasZ, hasM: false, partCount > 0 ? state.FirstPart : 0, partCount);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Parses a point body: one position, or the empty array as the typed empty.</summary>
    private static bool ParsePointBody(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, out GeometryCodecRefusal refusal)
    {
        //The point's coordinates member IS one position, so the array already opened by
        //the caller is either empty (the typed empty) or the position itself.
        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        if(reader.TokenType == JsonTokenType.EndArray)
        {
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        if(!ParsePositionElements(ref reader, baseOffset, builder, ref state, firstTokenAlreadyRead: true, out refusal))
        {
            return false;
        }

        builder.AddPart(new FlatGeometryPart(builder.VertexCount - 1, 1, FlatGeometryPartRole.Point));

        return true;
    }

    /// <summary>
    /// Parses an array of positions into one part, enforcing the minimum
    /// count and, for rings, exact-XY closure.
    /// </summary>
    private static bool ParseLineBody(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, FlatGeometryPartRole role, int minimumPositions, bool requireClosed, bool allowEmpty, out GeometryCodecRefusal refusal)
    {
        int firstVertex = builder.VertexCount;

        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if(reader.TokenType != JsonTokenType.StartArray)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

                return false;
            }

            if(!ParsePositionElements(ref reader, baseOffset, builder, ref state, firstTokenAlreadyRead: false, out refusal))
            {
                return false;
            }
        }

        int positionCount = builder.VertexCount - firstVertex;
        int runEndOffset = baseOffset + (int)reader.TokenStartIndex;

        if(positionCount == 0)
        {
            //A whole geometry may be the typed empty; a member of a multi kind may not —
            //the empty-member reading the sibling recognizer takes for the text codec.
            if(!allowEmpty)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runEndOffset);

                return false;
            }

            refusal = GeometryCodecRefusal.None;

            return true;
        }

        if(positionCount < minimumPositions)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runEndOffset);

            return false;
        }

        if(requireClosed && !builder.VerticesEqualXy(firstVertex, builder.VertexCount - 1))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, runEndOffset);

            return false;
        }

        builder.AddPart(new FlatGeometryPart(firstVertex, positionCount, role));
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Parses a polygon body: an array of closed rings, exterior first.</summary>
    private static bool ParseRingListBody(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, bool allowEmpty, out GeometryCodecRefusal refusal)
    {
        int ringIndex = 0;

        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if(reader.TokenType != JsonTokenType.StartArray)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

                return false;
            }

            FlatGeometryPartRole role = ringIndex == 0 ? FlatGeometryPartRole.ExteriorRing : FlatGeometryPartRole.InteriorRing;

            if(!ParseLineBody(ref reader, baseOffset, builder, ref state, role, minimumPositions: 4, requireClosed: true, allowEmpty: false, out refusal))
            {
                return false;
            }

            ringIndex++;
        }

        if(ringIndex == 0 && !allowEmpty)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Parses a multipoint body: an array of positions, one part each.</summary>
    private static bool ParseMultiPointBody(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, out GeometryCodecRefusal refusal)
    {
        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                refusal = GeometryCodecRefusal.None;

                return true;
            }

            if(reader.TokenType != JsonTokenType.StartArray)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

                return false;
            }

            if(!ParsePositionElements(ref reader, baseOffset, builder, ref state, firstTokenAlreadyRead: false, out refusal))
            {
                return false;
            }

            builder.AddPart(new FlatGeometryPart(builder.VertexCount - 1, 1, FlatGeometryPartRole.Point));
        }
    }

    /// <summary>Parses a multilinestring body: an array of line arrays.</summary>
    private static bool ParseListOfLinesBody(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, out GeometryCodecRefusal refusal)
    {
        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                refusal = GeometryCodecRefusal.None;

                return true;
            }

            if(reader.TokenType != JsonTokenType.StartArray)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

                return false;
            }

            if(!ParseLineBody(ref reader, baseOffset, builder, ref state, FlatGeometryPartRole.Line, minimumPositions: 2, requireClosed: false, allowEmpty: false, out refusal))
            {
                return false;
            }
        }
    }

    /// <summary>Parses a multipolygon body: an array of polygon ring lists.</summary>
    private static bool ParseMultiPolygonBody(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, out GeometryCodecRefusal refusal)
    {
        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                refusal = GeometryCodecRefusal.None;

                return true;
            }

            if(reader.TokenType != JsonTokenType.StartArray)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, baseOffset + (int)reader.TokenStartIndex);

                return false;
            }

            if(!ParseRingListBody(ref reader, baseOffset, builder, ref state, allowEmpty: false, out refusal))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Parses one position's number elements, enforcing two-or-three arity,
    /// per-geometry arity uniformity, and finiteness of every parsed value —
    /// overflow of a syntactically finite token refuses like a NaN would.
    /// </summary>
    private static bool ParsePositionElements(ref Utf8JsonReader reader, int baseOffset, FlatGeometryBuilder builder, ref LeafState state, bool firstTokenAlreadyRead, out GeometryCodecRefusal refusal)
    {
        Span<double> elements = stackalloc double[3];
        int arity = 0;
        int positionStartOffset = baseOffset + (int)reader.TokenStartIndex;
        bool tokenPending = firstTokenAlreadyRead;

        while(true)
        {
            if(!tokenPending && !TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            tokenPending = false;

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            int elementOffset = baseOffset + (int)reader.TokenStartIndex;

            if(reader.TokenType != JsonTokenType.Number)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, elementOffset);

                return false;
            }

            if(arity == 3)
            {
                //A fourth element is unstandardized wild data; the closed model does
                //not guess at its semantics.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, elementOffset);

                return false;
            }

            if(!reader.TryGetDouble(out double value) || !double.IsFinite(value))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NonFiniteCoordinate, elementOffset);

                return false;
            }

            elements[arity] = value;
            arity++;
        }

        int runEndOffset = baseOffset + (int)reader.TokenStartIndex;

        if(arity < 2)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, runEndOffset);

            return false;
        }

        bool hasZ = arity == 3;

        if(state.AritySeen && state.HasZ != hasZ)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, positionStartOffset);

            return false;
        }

        state = state with { AritySeen = true, HasZ = hasZ };
        builder.AddVertex(new Point2d(elements[0], elements[1]), hasZ ? elements[2] : double.NaN, double.NaN);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Adjudicates a bbox member's value: an array of exactly four or six
    /// finite numbers in RFC 7946 slot order. Values are domain-free — only
    /// the form is validated. A non-number element adjudicates before the
    /// element count, so a seventh non-number element refuses as structure,
    /// not as length.
    /// </summary>
    internal static bool TryParseBoundingBoxMember(ref Utf8JsonReader reader, int baseOffset, out GeoJsonBoundingBox boundingBox, out GeometryCodecRefusal refusal)
    {
        boundingBox = default;

        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        int valueOffset = baseOffset + (int)reader.TokenStartIndex;

        if(reader.TokenType != JsonTokenType.StartArray)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, valueOffset);

            return false;
        }

        Span<double> elements = stackalloc double[6];
        int count = 0;

        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            int elementOffset = baseOffset + (int)reader.TokenStartIndex;

            if(reader.TokenType != JsonTokenType.Number)
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.StructuralViolation, elementOffset);

                return false;
            }

            if(count == elements.Length)
            {
                //The seventh element is where an overrun becomes inevitable —
                //six was the longest legal form.
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, elementOffset);

                return false;
            }

            if(!reader.TryGetDouble(out double value) || !double.IsFinite(value))
            {
                refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.NonFiniteCoordinate, elementOffset);

                return false;
            }

            elements[count] = value;
            count++;
        }

        if(count != 4 && count != 6)
        {
            //A shortfall becomes inevitable only at the closing bracket — a
            //five-element array could still have grown to six.
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.DimensionMismatch, baseOffset + (int)reader.TokenStartIndex);

            return false;
        }

        boundingBox = count == 4
            ? new GeoJsonBoundingBox(elements[0], elements[1], elements[2], elements[3])
            : new GeoJsonBoundingBox(elements[0], elements[1], elements[2], elements[3], elements[4], elements[5]);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Adjudicates the removed legacy crs member: only the 2008 name-form
    /// object naming CRS84 is tolerated, everything else refuses — silent
    /// wrong-system ingestion is the failure this forecloses.
    /// </summary>
    internal static bool ParseCrsMember(ref Utf8JsonReader reader, int baseOffset, out GeometryCodecRefusal refusal)
    {
        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        int valueOffset = baseOffset + (int)reader.TokenStartIndex;

        if(reader.TokenType == JsonTokenType.Null)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, valueOffset);

            return false;
        }

        if(reader.TokenType != JsonTokenType.StartObject)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, valueOffset);

            return false;
        }

        bool typeIsName = false;
        bool nameIsCrs84 = false;

        while(true)
        {
            if(!TryAdvance(ref reader, baseOffset, out refusal))
            {
                return false;
            }

            if(reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if(reader.ValueTextEquals(GeoJsonVocabulary.TypeMemberName))
            {
                if(!TryAdvance(ref reader, baseOffset, out refusal))
                {
                    return false;
                }

                typeIsName = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(GeoJsonVocabulary.CrsNameMemberName);
            }
            else if(reader.ValueTextEquals(GeoJsonVocabulary.PropertiesMemberName))
            {
                if(!TryAdvance(ref reader, baseOffset, out refusal))
                {
                    return false;
                }

                if(reader.TokenType != JsonTokenType.StartObject)
                {
                    if(!TrySkipCurrent(ref reader, baseOffset, out refusal))
                    {
                        return false;
                    }

                    continue;
                }

                while(true)
                {
                    if(!TryAdvance(ref reader, baseOffset, out refusal))
                    {
                        return false;
                    }

                    if(reader.TokenType == JsonTokenType.EndObject)
                    {
                        break;
                    }

                    if(reader.ValueTextEquals(GeoJsonVocabulary.CrsNameMemberName))
                    {
                        if(!TryAdvance(ref reader, baseOffset, out refusal))
                        {
                            return false;
                        }

                        nameIsCrs84 = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(GeoJsonVocabulary.LegacyCrs84Name);
                    }
                    else if(!TrySkipValue(ref reader, baseOffset, out refusal))
                    {
                        return false;
                    }
                }
            }
            else if(!TrySkipValue(ref reader, baseOffset, out refusal))
            {
                return false;
            }
        }

        if(!typeIsName || !nameIsCrs84)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, valueOffset);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>Classifies a type value against the seven case-sensitive tags.</summary>
    private static bool TryClassifyType(ref Utf8JsonReader reader, out GeometryKind kind)
    {
        if(reader.ValueTextEquals(GeoJsonVocabulary.PointTag))
        {
            kind = GeometryKind.Point;
        }
        else if(reader.ValueTextEquals(GeoJsonVocabulary.LineStringTag))
        {
            kind = GeometryKind.LineString;
        }
        else if(reader.ValueTextEquals(GeoJsonVocabulary.PolygonTag))
        {
            kind = GeometryKind.Polygon;
        }
        else if(reader.ValueTextEquals(GeoJsonVocabulary.MultiPointTag))
        {
            kind = GeometryKind.MultiPoint;
        }
        else if(reader.ValueTextEquals(GeoJsonVocabulary.MultiLineStringTag))
        {
            kind = GeometryKind.MultiLineString;
        }
        else if(reader.ValueTextEquals(GeoJsonVocabulary.MultiPolygonTag))
        {
            kind = GeometryKind.MultiPolygon;
        }
        else if(reader.ValueTextEquals(GeoJsonVocabulary.GeometryCollectionTag))
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

    /// <summary>Reads the member's value token and slices its complete extent.</summary>
    internal static bool TrySliceValue(ref Utf8JsonReader reader, int baseOffset, out int start, out int length, out GeometryCodecRefusal refusal)
    {
        start = 0;
        length = 0;

        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        start = baseOffset + (int)reader.TokenStartIndex;

        if(!TrySkipCurrent(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        length = baseOffset + (int)reader.BytesConsumed - start;

        return true;
    }

    /// <summary>Reads and discards the member's whole value.</summary>
    internal static bool TrySkipValue(ref Utf8JsonReader reader, int baseOffset, out GeometryCodecRefusal refusal)
    {
        if(!TryAdvance(ref reader, baseOffset, out refusal))
        {
            return false;
        }

        return TrySkipCurrent(ref reader, baseOffset, out refusal);
    }

    /// <summary>
    /// Skips the current value's children with the underlying reader's
    /// exception contained to the codec refusal contract.
    /// </summary>
    internal static bool TrySkipCurrent(ref Utf8JsonReader reader, int baseOffset, out GeometryCodecRefusal refusal)
    {
        long consumedBefore = reader.BytesConsumed;

        try
        {
            reader.Skip();
        }
        catch(JsonException)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, baseOffset + (int)consumedBefore);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// Advances the underlying reader one token with its exception
    /// contained: the refusal names the consumed-byte snapshot at the
    /// failing read — the first byte at which the grammar could not be
    /// extended. An end of data where a token was required reports the
    /// same snapshot.
    /// </summary>
    internal static bool TryAdvance(ref Utf8JsonReader reader, int baseOffset, out GeometryCodecRefusal refusal)
    {
        long consumedBefore = reader.BytesConsumed;
        bool moved;

        try
        {
            moved = reader.Read();
        }
        catch(JsonException)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, baseOffset + (int)consumedBefore);

            return false;
        }

        if(!moved)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.MalformedDocument, baseOffset + (int)reader.BytesConsumed);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>The guard for kinds the leaf dispatcher can never receive.</summary>
    private static bool Unreachable(out GeometryCodecRefusal refusal)
    {
        throw new InvalidOperationException("A collection kind reached the leaf coordinate parser.");
    }

    /// <summary>Whether a byte is JSON whitespace.</summary>
    internal static bool IsJsonWhitespace(byte value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
    }

    /// <summary>
    /// One scanned geometry object: its kind and the recorded extents of
    /// its coordinate payload, plus — for the root — where consumption
    /// ended so trailing content can be adjudicated.
    /// </summary>
    /// <param name="Kind">The geometry kind read from the type member.</param>
    /// <param name="CoordinatesStart">The coordinates member's value start offset.</param>
    /// <param name="CoordinatesLength">The coordinates member's value length.</param>
    /// <param name="GeometriesStart">The geometries member's value start offset.</param>
    /// <param name="GeometriesLength">The geometries member's value length.</param>
    /// <param name="ConsumedEnd">Where consumption ended; meaningful for the root object only.</param>
    private readonly record struct ScannedObject(
        GeometryKind Kind,
        int CoordinatesStart,
        int CoordinatesLength,
        int GeometriesStart,
        int GeometriesLength,
        int ConsumedEnd);

    /// <summary>
    /// One open collection during the iterative parse: its node, the
    /// completed children, and the pre-sliced member extents.
    /// </summary>
    /// <param name="NodeIndex">The collection node's table index.</param>
    /// <param name="Children">The completed child node indices, in document order.</param>
    /// <param name="MemberExtents">The pre-sliced document extents of every member.</param>
    private readonly record struct CollectionFrame(
        int NodeIndex,
        List<int> Children,
        List<(int Start, int Length)> MemberExtents);

    /// <summary>
    /// The per-leaf parse state: the node's first part, and the arity
    /// commitment the first position makes for the whole geometry.
    /// </summary>
    /// <param name="FirstPart">The part table index the leaf's first run occupies.</param>
    private readonly record struct LeafState(int FirstPart)
    {
        /// <summary>Whether any position has committed the arity yet.</summary>
        public bool AritySeen { get; init; }

        /// <summary>Whether the committed arity carries Z.</summary>
        public bool HasZ { get; init; }
    }
}
