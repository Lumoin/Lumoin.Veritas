using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Xml;

/// <summary>
/// The KML writer over the flat geometry model: canonical emission natively inside
/// the format's schema — the default namespace declared once on the root, paired
/// tags always, the schema's child order, the strict tuple form with commas only
/// inside a tuple and one space between tuples, and the absolute altitude mode
/// emitted exactly on the nodes that carry a third dimension, because without it
/// consumers clamp and discard the altitude. The caller asserts by calling this
/// writer that the geometry's planar ordinates are longitude-latitude degrees —
/// the format fixes its coordinate system and no parameter exists. Typed
/// aggregates and collections collapse one way into the format's single
/// heterogeneous aggregate; a nested collection emits nested aggregates, which the
/// schema admits natively — no exception clause. The format has no empty
/// semantics, so every empty value refuses; the writer validates the whole
/// geometry before its first destination write, and a refused call leaves the
/// destination untouched.
/// </summary>
public static class KmlGeometryWriter
{
    /// <summary>
    /// Writes a geometry as canonical KML. False reports the refusal — the
    /// uninitialized carrier and every empty node refuse as unrepresentable, the
    /// format having no empty semantics — and leaves the destination untouched.
    /// </summary>
    public static bool TryWrite(in FlatGeometry geometry, IBufferWriter<byte> destination, out GeometryCodecRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if(geometry.Nodes.Length == 0)
        {
            refusal = new GeometryCodecRefusal(GeometryCodecRefusalKind.EmptyUnrepresentable, -1);

            return false;
        }

        if(!GeometryCodecWriteValidation.TryValidate(in geometry, CheckNode, out refusal))
        {
            return false;
        }

        WriteTree(in geometry, destination);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// The string convenience over <see cref="TryWrite"/>: the text is empty on
    /// refusal — the refusal-less convenience of the older text writer is
    /// deliberately not mirrored, because this codec refuses on data and the
    /// refusal must have a channel.
    /// </summary>
    public static bool TryWriteString(in FlatGeometry geometry, out string text, out GeometryCodecRefusal refusal)
    {
        ArrayBufferWriter<byte> buffer = new();

        if(!TryWrite(in geometry, buffer, out refusal))
        {
            text = string.Empty;

            return false;
        }

        text = Encoding.UTF8.GetString(buffer.WrittenSpan);

        return true;
    }

    /// <summary>
    /// The format's per-node representability check for the shared validation
    /// walk: the format has no empty semantics, so EVERY empty node refuses —
    /// primitives and typed aggregates without parts, collections without
    /// children.
    /// </summary>
    private static bool CheckNode(in FlatGeometry geometry, int nodeIndex, out GeometryCodecRefusal refusal)
    {
        FlatGeometryNode node = geometry.Nodes[nodeIndex];
        bool empty = node.Kind == GeometryKind.GeometryCollection ? node.ChildCount == 0 : node.PartCount == 0;

        if(empty)
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
    /// The iterative breadth-first emission: collections push frames and their
    /// members nest directly — the format's shape — while every other kind emits
    /// inline.
    /// </summary>
    private static void WriteTree(in FlatGeometry geometry, IBufferWriter<byte> destination)
    {
        Stack<CollectionFrame> frames = new();
        int nodeIndex = 0;
        bool isRoot = true;

        while(true)
        {
            FlatGeometryNode node = geometry.Nodes[nodeIndex];

            if(node.Kind == GeometryKind.GeometryCollection)
            {
                WriteElementOpen(destination, KmlVocabulary.MultiGeometryName, isRoot);
                frames.Push(new CollectionFrame(node.FirstChild, node.ChildCount, 0));
                isRoot = false;
                nodeIndex = node.FirstChild;

                continue;
            }

            WriteLeaf(in geometry, nodeIndex, isRoot, destination);
            isRoot = false;

            //A node finished; move to the next member or close the enclosing aggregates.
            bool moved = false;

            while(frames.Count > 0)
            {
                CollectionFrame frame = frames.Pop();
                int emitted = frame.EmittedCount + 1;

                if(emitted < frame.ChildCount)
                {
                    frames.Push(new CollectionFrame(frame.FirstChild, frame.ChildCount, emitted));
                    nodeIndex = frame.FirstChild + emitted;
                    moved = true;

                    break;
                }

                WriteElementClose(destination, KmlVocabulary.MultiGeometryName);
            }

            if(!moved)
            {
                break;
            }
        }
    }

    /// <summary>Emits one non-collection node in its canonical form — the typed aggregates collapse into the heterogeneous aggregate.</summary>
    private static void WriteLeaf(in FlatGeometry geometry, int nodeIndex, bool isRoot, IBufferWriter<byte> destination)
    {
        FlatGeometryNode node = geometry.Nodes[nodeIndex];

        switch(node.Kind)
        {
            case GeometryKind.Point:
                WritePointElement(in geometry, geometry.Parts[node.FirstPart], isRoot, destination, node.HasZ);

                break;
            case GeometryKind.LineString:
                WriteLineStringElement(in geometry, node.FirstPart, isRoot, destination, node.HasZ);

                break;
            case GeometryKind.Polygon:
                WritePolygonElement(in geometry, node.FirstPart, node.PartCount, isRoot, destination, node.HasZ);

                break;
            case GeometryKind.MultiPoint:
                WriteElementOpen(destination, KmlVocabulary.MultiGeometryName, isRoot);

                for(int partIndex = node.FirstPart; partIndex < node.FirstPart + node.PartCount; partIndex++)
                {
                    WritePointElement(in geometry, geometry.Parts[partIndex], isRoot: false, destination, node.HasZ);
                }

                WriteElementClose(destination, KmlVocabulary.MultiGeometryName);

                break;
            case GeometryKind.MultiLineString:
                WriteElementOpen(destination, KmlVocabulary.MultiGeometryName, isRoot);

                for(int partIndex = node.FirstPart; partIndex < node.FirstPart + node.PartCount; partIndex++)
                {
                    WriteLineStringElement(in geometry, partIndex, isRoot: false, destination, node.HasZ);
                }

                WriteElementClose(destination, KmlVocabulary.MultiGeometryName);

                break;
            default:
                WriteMultiPolygon(in geometry, node, isRoot, destination);

                break;
        }
    }

    /// <summary>Emits a multi polygon as the aggregate of its polygons, each exterior opening a new one.</summary>
    private static void WriteMultiPolygon(in FlatGeometry geometry, FlatGeometryNode node, bool isRoot, IBufferWriter<byte> destination)
    {
        WriteElementOpen(destination, KmlVocabulary.MultiGeometryName, isRoot);

        int partIndex = node.FirstPart;
        int end = node.FirstPart + node.PartCount;

        while(partIndex < end)
        {
            int groupEnd = partIndex + 1;

            while(groupEnd < end && geometry.Parts[groupEnd].Role == FlatGeometryPartRole.InteriorRing)
            {
                groupEnd++;
            }

            WritePolygonElement(in geometry, partIndex, groupEnd - partIndex, isRoot: false, destination, node.HasZ);
            partIndex = groupEnd;
        }

        WriteElementClose(destination, KmlVocabulary.MultiGeometryName);
    }

    /// <summary>Emits a point element: the absolute altitude mode when the node carries a third dimension, then its single tuple.</summary>
    private static void WritePointElement(in FlatGeometry geometry, FlatGeometryPart part, bool isRoot, IBufferWriter<byte> destination, bool hasZ)
    {
        WriteElementOpen(destination, KmlVocabulary.PointName, isRoot);
        WriteAltitudeModeIfCarried(destination, hasZ);
        WriteCoordinates(in geometry, part, destination, hasZ);
        WriteElementClose(destination, KmlVocabulary.PointName);
    }

    /// <summary>Emits a line-string element from one line part.</summary>
    private static void WriteLineStringElement(in FlatGeometry geometry, int partIndex, bool isRoot, IBufferWriter<byte> destination, bool hasZ)
    {
        WriteElementOpen(destination, KmlVocabulary.LineStringName, isRoot);
        WriteAltitudeModeIfCarried(destination, hasZ);
        WriteCoordinates(in geometry, geometry.Parts[partIndex], destination, hasZ);
        WriteElementClose(destination, KmlVocabulary.LineStringName);
    }

    /// <summary>Emits a polygon element: the mode when carried, the exterior boundary, then every interior one — the schema's order.</summary>
    private static void WritePolygonElement(in FlatGeometry geometry, int firstPart, int partCount, bool isRoot, IBufferWriter<byte> destination, bool hasZ)
    {
        WriteElementOpen(destination, KmlVocabulary.PolygonName, isRoot);
        WriteAltitudeModeIfCarried(destination, hasZ);

        for(int partIndex = firstPart; partIndex < firstPart + partCount; partIndex++)
        {
            ReadOnlySpan<byte> boundaryName = geometry.Parts[partIndex].Role == FlatGeometryPartRole.InteriorRing ? KmlVocabulary.InnerBoundaryName : KmlVocabulary.OuterBoundaryName;
            WriteElementOpen(destination, boundaryName, isRoot: false);
            WriteElementOpen(destination, KmlVocabulary.LinearRingName, isRoot: false);
            WriteCoordinates(in geometry, geometry.Parts[partIndex], destination, hasZ);
            WriteElementClose(destination, KmlVocabulary.LinearRingName);
            WriteElementClose(destination, boundaryName);
        }

        WriteElementClose(destination, KmlVocabulary.PolygonName);
    }

    /// <summary>Emits the absolute altitude mode on a third-dimension node — without it consumers clamp and discard the altitude.</summary>
    private static void WriteAltitudeModeIfCarried(IBufferWriter<byte> destination, bool hasZ)
    {
        if(hasZ)
        {
            destination.Write(KmlVocabulary.AbsoluteAltitudeModeElement);
        }
    }

    /// <summary>Emits one coordinates run: commas only inside a tuple, one space between tuples.</summary>
    private static void WriteCoordinates(in FlatGeometry geometry, FlatGeometryPart part, IBufferWriter<byte> destination, bool hasZ)
    {
        destination.Write("<"u8);
        destination.Write(KmlVocabulary.CoordinatesName);
        destination.Write(">"u8);

        for(int vertexIndex = part.Start; vertexIndex < part.Start + part.Length; vertexIndex++)
        {
            if(vertexIndex > part.Start)
            {
                destination.Write(" "u8);
            }

            Point2d vertex = geometry.Vertices[vertexIndex];
            GeometryCodecText.WriteNumber(vertex.X, destination);
            destination.Write(","u8);
            GeometryCodecText.WriteNumber(vertex.Y, destination);

            if(hasZ)
            {
                destination.Write(","u8);
                GeometryCodecText.WriteNumber(geometry.ZOrdinates[vertexIndex], destination);
            }
        }

        destination.Write("</"u8);
        destination.Write(KmlVocabulary.CoordinatesName);
        destination.Write(">"u8);
    }

    /// <summary>Emits an element's start tag, the default namespace declared on the root only.</summary>
    private static void WriteElementOpen(IBufferWriter<byte> destination, ReadOnlySpan<byte> localName, bool isRoot)
    {
        destination.Write("<"u8);
        destination.Write(localName);

        if(isRoot)
        {
            destination.Write(KmlVocabulary.RootNamespaceDeclaration);
        }

        destination.Write(">"u8);
    }

    /// <summary>Emits an element's end tag.</summary>
    private static void WriteElementClose(IBufferWriter<byte> destination, ReadOnlySpan<byte> localName)
    {
        destination.Write("</"u8);
        destination.Write(localName);
        destination.Write(">"u8);
    }
}
