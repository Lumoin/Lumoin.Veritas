using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Geo.Json;

/// <summary>
/// Writes a flat geometry as one RFC 7946 GeoJSON Geometry object in the
/// codec's canonical form: no whitespace, the <c>type</c> member first, then
/// <c>coordinates</c> or <c>geometries</c>, numbers in shortest-round-trip
/// invariant form so text-to-value round-trips are bit-exact, ring order and
/// orientation verbatim — the writer never reorients. GeoJSON fixes its
/// coordinate reference system to CRS84 longitude-latitude degrees; by
/// calling this writer the caller asserts the geometry's XY columns carry
/// exactly that. Empty nodes emit the empty-array forms; an uninitialized
/// geometry degrades to the empty geometry collection. Refusals are returned
/// by value, never thrown, and the whole geometry is validated before the
/// first destination write, so a refused call leaves the destination
/// untouched. A measured geometry refuses — no format in this codec family
/// carries an M ordinate. The one exception is the caller contract: a null
/// destination throws.
/// </summary>
public static class GeoJsonGeometryWriter
{
    /// <summary>
    /// Writes the geometry as one canonical GeoJSON Geometry object, or
    /// refuses by value with the destination untouched.
    /// </summary>
    /// <param name="geometry">The geometry to write.</param>
    /// <param name="destination">The UTF-8 destination.</param>
    /// <param name="refusal">
    /// The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on
    /// success.
    /// </param>
    /// <returns>True when the geometry was written.</returns>
    public static bool TryWrite(in FlatGeometry geometry, IBufferWriter<byte> destination, out GeometryCodecRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if(geometry.Nodes.Length == 0)
        {
            destination.Write(GeoJsonVocabulary.EmptyCollectionDocument);
            refusal = GeometryCodecRefusal.None;

            return true;
        }

        if(!GeometryCodecWriteValidation.TryValidate(in geometry, out refusal))
        {
            return false;
        }

        WriteNode(in geometry, destination);
        refusal = GeometryCodecRefusal.None;

        return true;
    }

    /// <summary>
    /// The string convenience over <see cref="TryWrite"/> for hosts and
    /// tests: the canonical text on success, the empty string on refusal.
    /// The refusal-less string convenience of the WKT writer is deliberately
    /// not mirrored — this codec refuses on data, and the refusal must have
    /// a channel.
    /// </summary>
    /// <param name="geometry">The geometry to write.</param>
    /// <param name="text">The canonical text, or empty on refusal.</param>
    /// <param name="refusal">
    /// The refusal on failure; <see cref="GeometryCodecRefusal.None"/> on
    /// success.
    /// </param>
    /// <returns>True when the geometry was written.</returns>
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
    /// Emits the validated tree iteratively in document order: an explicit
    /// frame stack carries open collections, so nesting never recurses.
    /// </summary>
    private static void WriteNode(in FlatGeometry geometry, IBufferWriter<byte> destination)
    {
        ReadOnlySpan<FlatGeometryNode> nodes = geometry.Nodes;
        Stack<CollectionFrame> frames = new();
        int nodeIndex = 0;

        while(true)
        {
            FlatGeometryNode node = nodes[nodeIndex];

            if(node.Kind == GeometryKind.GeometryCollection)
            {
                destination.Write(GeoJsonVocabulary.CollectionOpening);

                if(node.ChildCount > 0)
                {
                    frames.Push(new CollectionFrame(node.FirstChild, node.ChildCount, 1));
                    nodeIndex = node.FirstChild;

                    continue;
                }

                destination.Write("]}"u8);
            }
            else
            {
                WriteLeaf(in geometry, nodeIndex, destination);
            }

            //A finished member closes every collection whose child run it ended, then
            //steps to the next sibling of the innermost still-open collection.
            bool moved = false;

            while(frames.Count > 0)
            {
                CollectionFrame frame = frames.Pop();

                if(frame.EmittedCount < frame.ChildCount)
                {
                    destination.Write(","u8);
                    frames.Push(new CollectionFrame(frame.FirstChild, frame.ChildCount, frame.EmittedCount + 1));
                    nodeIndex = frame.FirstChild + frame.EmittedCount;
                    moved = true;

                    break;
                }

                destination.Write("]}"u8);
            }

            if(!moved)
            {
                break;
            }
        }
    }

    /// <summary>Emits one non-collection node with its kind tag and coordinates.</summary>
    private static void WriteLeaf(in FlatGeometry geometry, int nodeIndex, IBufferWriter<byte> destination)
    {
        FlatGeometryNode node = geometry.Nodes[nodeIndex];

        destination.Write(GeoJsonVocabulary.LeafOpening);
        destination.Write(GeoJsonVocabulary.TagOf(node.Kind));
        destination.Write(GeoJsonVocabulary.CoordinatesOpening);

        if(node.PartCount == 0)
        {
            destination.Write("[]"u8);
            destination.Write("}"u8);

            return;
        }

        ReadOnlySpan<FlatGeometryPart> parts = geometry.Parts.Slice(node.FirstPart, node.PartCount);

        switch(node.Kind)
        {
            case GeometryKind.Point:
                WritePosition(in geometry, parts[0].Start, node.HasZ, destination);

                break;

            case GeometryKind.LineString:
                WritePositionArray(in geometry, parts[0], node.HasZ, destination);

                break;

            case GeometryKind.Polygon:
                WriteRingArray(in geometry, parts, node.HasZ, destination);

                break;

            case GeometryKind.MultiPoint:
                destination.Write("["u8);

                for(int index = 0; index < parts.Length; index++)
                {
                    WriteSeparator(index, destination);
                    WritePosition(in geometry, parts[index].Start, node.HasZ, destination);
                }

                destination.Write("]"u8);

                break;

            case GeometryKind.MultiLineString:
                destination.Write("["u8);

                for(int index = 0; index < parts.Length; index++)
                {
                    WriteSeparator(index, destination);
                    WritePositionArray(in geometry, parts[index], node.HasZ, destination);
                }

                destination.Write("]"u8);

                break;

            case GeometryKind.MultiPolygon:
                WritePolygonList(in geometry, parts, node.HasZ, destination);

                break;

            default:
                throw new InvalidOperationException("A collection kind reached the leaf writer.");
        }

        destination.Write("}"u8);
    }

    /// <summary>
    /// Groups a multipolygon's flat part run positionally — each exterior
    /// ring opens a polygon, following interior rings belong to it — and
    /// emits the nested ring arrays.
    /// </summary>
    private static void WritePolygonList(in FlatGeometry geometry, ReadOnlySpan<FlatGeometryPart> parts, bool hasZ, IBufferWriter<byte> destination)
    {
        destination.Write("["u8);
        int polygonIndex = 0;
        int partIndex = 0;

        while(partIndex < parts.Length)
        {
            WriteSeparator(polygonIndex, destination);
            destination.Write("["u8);
            WritePositionArray(in geometry, parts[partIndex], hasZ, destination);
            partIndex++;

            while(partIndex < parts.Length && parts[partIndex].Role == FlatGeometryPartRole.InteriorRing)
            {
                destination.Write(","u8);
                WritePositionArray(in geometry, parts[partIndex], hasZ, destination);
                partIndex++;
            }

            destination.Write("]"u8);
            polygonIndex++;
        }

        destination.Write("]"u8);
    }

    /// <summary>Emits a polygon's rings, exterior first, as a nested array.</summary>
    private static void WriteRingArray(in FlatGeometry geometry, ReadOnlySpan<FlatGeometryPart> parts, bool hasZ, IBufferWriter<byte> destination)
    {
        destination.Write("["u8);

        for(int index = 0; index < parts.Length; index++)
        {
            WriteSeparator(index, destination);
            WritePositionArray(in geometry, parts[index], hasZ, destination);
        }

        destination.Write("]"u8);
    }

    /// <summary>Emits one part's vertex run as an array of positions.</summary>
    private static void WritePositionArray(in FlatGeometry geometry, FlatGeometryPart part, bool hasZ, IBufferWriter<byte> destination)
    {
        destination.Write("["u8);

        for(int index = 0; index < part.Length; index++)
        {
            WriteSeparator(index, destination);
            WritePosition(in geometry, part.Start + index, hasZ, destination);
        }

        destination.Write("]"u8);
    }

    /// <summary>Emits one position as a two- or three-element number array.</summary>
    private static void WritePosition(in FlatGeometry geometry, int vertexIndex, bool hasZ, IBufferWriter<byte> destination)
    {
        Point2d vertex = geometry.Vertices[vertexIndex];

        destination.Write("["u8);
        GeometryCodecText.WriteNumber(vertex.X, destination);
        destination.Write(","u8);
        GeometryCodecText.WriteNumber(vertex.Y, destination);

        if(hasZ)
        {
            destination.Write(","u8);
            GeometryCodecText.WriteNumber(geometry.ZOrdinates[vertexIndex], destination);
        }

        destination.Write("]"u8);
    }

    /// <summary>Writes the element separator before every item but the first.</summary>
    private static void WriteSeparator(int index, IBufferWriter<byte> destination)
    {
        if(index > 0)
        {
            destination.Write(","u8);
        }
    }

    /// <summary>
    /// One open collection during the iterative emission: its child run and
    /// how many members have been emitted.
    /// </summary>
    /// <param name="FirstChild">The first child node's table index.</param>
    /// <param name="ChildCount">The total child count.</param>
    /// <param name="EmittedCount">How many children have been emitted so far.</param>
    private readonly record struct CollectionFrame(int FirstChild, int ChildCount, int EmittedCount);
}
