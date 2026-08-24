using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Transforms;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The GML writer over the flat geometry model: canonical, linear-only emission
/// strictly inside the simple features profile's instance space — singular member
/// containers, position lists with the single-position element for points, the
/// system declared once on the root as the canonical IRI, the third dimension
/// declared per coordinate carrier on the nodes that carry it, and deterministic
/// document-order object identifiers. The writer validates the whole geometry
/// before its first destination write, so a refused call leaves the destination
/// untouched; the system parameter adjudicates before everything, the empty
/// short-circuit included, because the empty form itself carries the declaration.
/// A nested collection emits nested aggregates — valid against the full schema,
/// outside the profile's instance space, the one stated exception.
/// </summary>
public static class GmlGeometryWriter
{
    /// <summary>
    /// Writes a geometry as canonical GML. False reports the refusal — an
    /// unrecognized or default system before anything else, then the shared
    /// validation walk — and leaves the destination untouched. An uninitialized
    /// carrier degrades to the memberless heterogeneous aggregate.
    /// </summary>
    public static bool TryWrite(in FlatGeometry geometry, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination, out GeometryCodecRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if(coordinateReferenceSystem == default(CoordinateReferenceSystem))
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, -1);

            return false;
        }

        if(geometry.Nodes.Length == 0)
        {
            int identifier = 0;
            WriteElementOpen(destination, GmlVocabulary.MultiGeometryName, ref identifier, isRoot: true, coordinateReferenceSystem, selfClosing: true);
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        if(!GeometryCodecWriteValidation.TryValidate(in geometry, CheckNode, out refusal))
        {
            return false;
        }

        WriteTree(in geometry, coordinateReferenceSystem, destination);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// The string convenience over <see cref="TryWrite"/>: the text is empty on
    /// refusal — the refusal-less convenience of the older text writer is
    /// deliberately not mirrored, because this codec refuses on data and the
    /// refusal must have a channel.
    /// </summary>
    public static bool TryWriteString(in FlatGeometry geometry, CoordinateReferenceSystem coordinateReferenceSystem, out string text, out GeometryCodecRefusal refusal)
    {
        ArrayBufferWriter<byte> buffer = new();

        if(!TryWrite(in geometry, coordinateReferenceSystem, buffer, out refusal))
        {
            text = string.Empty;

            return false;
        }

        text = Encoding.UTF8.GetString(buffer.WrittenSpan);

        return true;
    }

    /// <summary>
    /// The format's per-node representability check for the shared validation
    /// walk: an empty primitive has no encoding in this format — memberless
    /// aggregates do, so only the primitive kinds refuse.
    /// </summary>
    private static bool CheckNode(in FlatGeometry geometry, int nodeIndex, out GeometryCodecRefusal refusal)
    {
        FlatGeometryNode node = geometry.Nodes[nodeIndex];
        bool primitive = node.Kind is GeometryKind.Point or GeometryKind.LineString or GeometryKind.Polygon;

        if(primitive && node.PartCount == 0)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.EmptyUnrepresentable, -1);

            return false;
        }

        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>One open heterogeneous collection in the iterative emission walk.</summary>
    private readonly record struct CollectionFrame(int FirstChild, int ChildCount, int EmittedCount);

    /// <summary>
    /// The iterative breadth-first emission: collections push frames, every other
    /// kind emits inline, and the object-identifier counter advances in document
    /// order over every object element — member elements included, the ring
    /// elements never.
    /// </summary>
    private static void WriteTree(in FlatGeometry geometry, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination)
    {
        Stack<CollectionFrame> frames = new();
        int nodeIndex = 0;
        int identifier = 0;
        bool isRoot = true;

        while(true)
        {
            FlatGeometryNode node = geometry.Nodes[nodeIndex];

            if(node.Kind == GeometryKind.GeometryCollection)
            {
                if(node.ChildCount == 0)
                {
                    WriteElementOpen(destination, GmlVocabulary.MultiGeometryName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: true);
                }
                else
                {
                    WriteElementOpen(destination, GmlVocabulary.MultiGeometryName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: false);
                    frames.Push(new CollectionFrame(node.FirstChild, node.ChildCount, 0));
                    isRoot = false;
                    destination.Write(GmlVocabulary.PrefixOpening);
                    destination.Write(GmlVocabulary.GeometryMemberName);
                    destination.Write(">"u8);
                    nodeIndex = node.FirstChild;

                    continue;
                }
            }
            else
            {
                WriteLeaf(in geometry, nodeIndex, ref identifier, isRoot, coordinateReferenceSystem, destination);
            }

            isRoot = false;

            //A node finished; unwind the member plumbing.
            bool moved = false;

            while(frames.Count > 0)
            {
                CollectionFrame frame = frames.Pop();
                destination.Write(GmlVocabulary.PrefixClosing);
                destination.Write(GmlVocabulary.GeometryMemberName);
                destination.Write(">"u8);

                int emitted = frame.EmittedCount + 1;

                if(emitted < frame.ChildCount)
                {
                    frames.Push(new CollectionFrame(frame.FirstChild, frame.ChildCount, emitted));
                    destination.Write(GmlVocabulary.PrefixOpening);
                    destination.Write(GmlVocabulary.GeometryMemberName);
                    destination.Write(">"u8);
                    nodeIndex = frame.FirstChild + emitted;
                    moved = true;

                    break;
                }

                WriteElementClose(destination, GmlVocabulary.MultiGeometryName);
            }

            if(!moved)
            {
                break;
            }
        }
    }

    /// <summary>Emits one non-collection node in its canonical form.</summary>
    private static void WriteLeaf(in FlatGeometry geometry, int nodeIndex, ref int identifier, bool isRoot, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination)
    {
        FlatGeometryNode node = geometry.Nodes[nodeIndex];

        switch(node.Kind)
        {
            case GeometryKind.Point:
                WritePointElement(in geometry, node, ref identifier, isRoot, coordinateReferenceSystem, destination);

                break;
            case GeometryKind.LineString:
                WriteLineStringElement(in geometry, node.FirstPart, ref identifier, isRoot, coordinateReferenceSystem, destination, node.HasZ);

                break;
            case GeometryKind.Polygon:
                WritePolygonElement(in geometry, node.FirstPart, node.PartCount, ref identifier, isRoot, coordinateReferenceSystem, destination, node.HasZ);

                break;
            case GeometryKind.MultiPoint:
                WriteAggregate(in geometry, node, GmlVocabulary.MultiPointName, GmlVocabulary.PointMemberName, ref identifier, isRoot, coordinateReferenceSystem, destination);

                break;
            case GeometryKind.MultiLineString:
                WriteAggregate(in geometry, node, GmlVocabulary.MultiCurveName, GmlVocabulary.CurveMemberName, ref identifier, isRoot, coordinateReferenceSystem, destination);

                break;
            default:
                WriteAggregate(in geometry, node, GmlVocabulary.MultiSurfaceName, GmlVocabulary.SurfaceMemberName, ref identifier, isRoot, coordinateReferenceSystem, destination);

                break;
        }
    }

    /// <summary>Emits a typed aggregate through its singular member containers, the member elements taking their own identifiers.</summary>
    private static void WriteAggregate(in FlatGeometry geometry, FlatGeometryNode node, ReadOnlySpan<byte> aggregateName, ReadOnlySpan<byte> memberName, ref int identifier, bool isRoot, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination)
    {
        if(node.PartCount == 0)
        {
            WriteElementOpen(destination, aggregateName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: true);

            return;
        }

        WriteElementOpen(destination, aggregateName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: false);

        if(node.Kind == GeometryKind.MultiPolygon)
        {
            int partIndex = node.FirstPart;
            int end = node.FirstPart + node.PartCount;

            while(partIndex < end)
            {
                int groupEnd = partIndex + 1;

                while(groupEnd < end && geometry.Parts[groupEnd].Role == FlatGeometryPartRole.InteriorRing)
                {
                    groupEnd++;
                }

                WriteMemberOpen(destination, memberName);
                WritePolygonElement(in geometry, partIndex, groupEnd - partIndex, ref identifier, isRoot: false, coordinateReferenceSystem, destination, node.HasZ);
                WriteMemberClose(destination, memberName);
                partIndex = groupEnd;
            }
        }
        else
        {
            for(int partIndex = node.FirstPart; partIndex < node.FirstPart + node.PartCount; partIndex++)
            {
                WriteMemberOpen(destination, memberName);

                if(node.Kind == GeometryKind.MultiPoint)
                {
                    WritePointPartElement(in geometry, geometry.Parts[partIndex], ref identifier, coordinateReferenceSystem, destination, node.HasZ);
                }
                else
                {
                    WriteLineStringElement(in geometry, partIndex, ref identifier, isRoot: false, coordinateReferenceSystem, destination, node.HasZ);
                }

                WriteMemberClose(destination, memberName);
            }
        }

        WriteElementClose(destination, aggregateName);
    }

    /// <summary>Emits a point element from its node.</summary>
    private static void WritePointElement(in FlatGeometry geometry, FlatGeometryNode node, ref int identifier, bool isRoot, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination)
    {
        WritePointPartElement(in geometry, geometry.Parts[node.FirstPart], ref identifier, coordinateReferenceSystem, destination, node.HasZ, isRoot);
    }

    /// <summary>Emits a point element from one point part.</summary>
    private static void WritePointPartElement(in FlatGeometry geometry, FlatGeometryPart part, ref int identifier, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination, bool hasZ, bool isRoot = false)
    {
        WriteElementOpen(destination, GmlVocabulary.PointName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: false);
        destination.Write(GmlVocabulary.PrefixOpening);
        destination.Write(GmlVocabulary.PosName);

        if(hasZ)
        {
            destination.Write(GmlVocabulary.SrsDimensionThreeAttribute);
        }

        destination.Write(">"u8);
        WritePosition(in geometry, part.Start, destination, hasZ);
        destination.Write(GmlVocabulary.PrefixClosing);
        destination.Write(GmlVocabulary.PosName);
        destination.Write(">"u8);
        WriteElementClose(destination, GmlVocabulary.PointName);
    }

    /// <summary>Emits a line-string element from one line part.</summary>
    private static void WriteLineStringElement(in FlatGeometry geometry, int partIndex, ref int identifier, bool isRoot, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination, bool hasZ)
    {
        WriteElementOpen(destination, GmlVocabulary.LineStringName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: false);
        WritePositionList(in geometry, geometry.Parts[partIndex], destination, hasZ);
        WriteElementClose(destination, GmlVocabulary.LineStringName);
    }

    /// <summary>Emits a polygon element from its ring parts: the exterior opens, the interiors follow.</summary>
    private static void WritePolygonElement(in FlatGeometry geometry, int firstPart, int partCount, ref int identifier, bool isRoot, CoordinateReferenceSystem coordinateReferenceSystem, IBufferWriter<byte> destination, bool hasZ)
    {
        WriteElementOpen(destination, GmlVocabulary.PolygonName, ref identifier, isRoot, coordinateReferenceSystem, selfClosing: false);

        for(int partIndex = firstPart; partIndex < firstPart + partCount; partIndex++)
        {
            ReadOnlySpan<byte> boundaryName = geometry.Parts[partIndex].Role == FlatGeometryPartRole.InteriorRing ? GmlVocabulary.InteriorName : GmlVocabulary.ExteriorName;
            WriteMemberOpen(destination, boundaryName);
            destination.Write(GmlVocabulary.PrefixOpening);
            destination.Write(GmlVocabulary.LinearRingName);
            destination.Write(">"u8);
            WritePositionList(in geometry, geometry.Parts[partIndex], destination, hasZ);
            destination.Write(GmlVocabulary.PrefixClosing);
            destination.Write(GmlVocabulary.LinearRingName);
            destination.Write(">"u8);
            WriteMemberClose(destination, boundaryName);
        }

        WriteElementClose(destination, GmlVocabulary.PolygonName);
    }

    /// <summary>Emits a position-list carrier for one part, the third dimension declared on the carrier when the node carries it.</summary>
    private static void WritePositionList(in FlatGeometry geometry, FlatGeometryPart part, IBufferWriter<byte> destination, bool hasZ)
    {
        destination.Write(GmlVocabulary.PrefixOpening);
        destination.Write(GmlVocabulary.PosListName);

        if(hasZ)
        {
            destination.Write(GmlVocabulary.SrsDimensionThreeAttribute);
        }

        destination.Write(">"u8);

        for(int vertexIndex = part.Start; vertexIndex < part.Start + part.Length; vertexIndex++)
        {
            if(vertexIndex > part.Start)
            {
                destination.Write(" "u8);
            }

            WritePosition(in geometry, vertexIndex, destination, hasZ);
        }

        destination.Write(GmlVocabulary.PrefixClosing);
        destination.Write(GmlVocabulary.PosListName);
        destination.Write(">"u8);
    }

    /// <summary>Emits one vertex's ordinates.</summary>
    private static void WritePosition(in FlatGeometry geometry, int vertexIndex, IBufferWriter<byte> destination, bool hasZ)
    {
        Point2d vertex = geometry.Vertices[vertexIndex];
        GeometryCodecText.WriteNumber(vertex.X, destination);
        destination.Write(" "u8);
        GeometryCodecText.WriteNumber(vertex.Y, destination);

        if(hasZ)
        {
            destination.Write(" "u8);
            GeometryCodecText.WriteNumber(geometry.ZOrdinates[vertexIndex], destination);
        }
    }

    /// <summary>
    /// Emits an object element's start tag in the canonical attribute order — the
    /// namespace declaration on the root only, the generated identifier, the
    /// system on the root only — advancing the identifier counter.
    /// </summary>
    private static void WriteElementOpen(IBufferWriter<byte> destination, ReadOnlySpan<byte> localName, ref int identifier, bool isRoot, CoordinateReferenceSystem coordinateReferenceSystem, bool selfClosing)
    {
        destination.Write(GmlVocabulary.PrefixOpening);
        destination.Write(localName);

        if(isRoot)
        {
            destination.Write(GmlVocabulary.RootNamespaceDeclaration);
        }

        destination.Write(GmlVocabulary.IdAttributeOpening);
        WriteIdentifier(destination, identifier);
        destination.Write("\""u8);
        identifier++;

        if(isRoot)
        {
            destination.Write(GmlVocabulary.SrsNameAttributeOpening);
            destination.Write(GmlSrsName.CanonicalIriOf(coordinateReferenceSystem));
            destination.Write("\""u8);
        }

        destination.Write(selfClosing ? "/>"u8 : ">"u8);
    }

    /// <summary>Emits an element's end tag.</summary>
    private static void WriteElementClose(IBufferWriter<byte> destination, ReadOnlySpan<byte> localName)
    {
        destination.Write(GmlVocabulary.PrefixClosing);
        destination.Write(localName);
        destination.Write(">"u8);
    }

    /// <summary>Emits a member or boundary property's start tag.</summary>
    private static void WriteMemberOpen(IBufferWriter<byte> destination, ReadOnlySpan<byte> memberName)
    {
        destination.Write(GmlVocabulary.PrefixOpening);
        destination.Write(memberName);
        destination.Write(">"u8);
    }

    /// <summary>Emits a member or boundary property's end tag.</summary>
    private static void WriteMemberClose(IBufferWriter<byte> destination, ReadOnlySpan<byte> memberName)
    {
        destination.Write(GmlVocabulary.PrefixClosing);
        destination.Write(memberName);
        destination.Write(">"u8);
    }

    /// <summary>Emits the decimal digits of a non-negative identifier without allocation.</summary>
    private static void WriteIdentifier(IBufferWriter<byte> destination, int identifier)
    {
        Span<byte> digits = destination.GetSpan(11);
        bool formatted = Utf8Formatter.TryFormat(identifier, digits, out int written);

        if(!formatted)
        {
            throw new InvalidOperationException("An identifier did not fit the digit buffer.");
        }

        destination.Advance(written);
    }
}
