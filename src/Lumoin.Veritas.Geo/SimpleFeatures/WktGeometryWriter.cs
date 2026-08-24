using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The WKT writer of the Simple Features substrate, emitting the canonical form:
/// uppercase tags; one space between tag, ordinate marker, and body
/// (<c>POINT (1 2)</c>, <c>POINT Z (1 2 3)</c>); <c>", "</c> between list items; bare
/// <c>EMPTY</c> per kind; markers per tagged node from its Z/M carriage; numbers in
/// .NET shortest-round-trip invariant formatting emitted straight into the UTF-8
/// destination, so text → value round-trips are bit-exact. Ring order and orientation
/// serialize verbatim — the writer never reorients.
/// </summary>
public static class WktGeometryWriter
{
    /// <summary>Writes the geometry as UTF-8 WKT into the destination buffer.</summary>
    public static void Write(in FlatGeometry geometry, IBufferWriter<byte> destination)
    {
        if(geometry.Nodes.Length == 0)
        {
            //An uninitialized carrier degrades to the empty collection everywhere.
            destination.Write("GEOMETRYCOLLECTION EMPTY"u8);

            return;
        }

        WriteNode(in geometry, 0, destination);
    }

    /// <summary>Writes the geometry as a WKT string; a convenience for hosts and tests.</summary>
    public static string WriteString(in FlatGeometry geometry)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Write(in geometry, buffer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Writes tagged nodes from an explicit step stack — the emission is iterative,
    /// never recursive. A node step emits its tag, marker, and leaf body inline; a
    /// collection step instead pushes a closing step and its members, each member after
    /// the first behind a separator step.
    /// </summary>
    private static void WriteNode(in FlatGeometry geometry, int nodeIndex, IBufferWriter<byte> destination)
    {
        var pending = new Stack<WriteStep>();
        pending.Push(new WriteStep(nodeIndex, EmitsClosingParenthesis: false, EmitsSeparator: false));

        while(pending.Count > 0)
        {
            WriteStep step = pending.Pop();

            if(step.EmitsClosingParenthesis)
            {
                destination.Write(")"u8);

                continue;
            }

            if(step.EmitsSeparator)
            {
                destination.Write(", "u8);
            }

            FlatGeometryNode node = geometry.Nodes[step.NodeIndex];

            destination.Write(TagOf(node.Kind));

            if(node.HasZ && node.HasM)
            {
                destination.Write(" ZM"u8);
            }
            else if(node.HasZ)
            {
                destination.Write(" Z"u8);
            }
            else if(node.HasM)
            {
                destination.Write(" M"u8);
            }

            if(node.PartCount == 0 && node.ChildCount == 0)
            {
                destination.Write(" EMPTY"u8);

                continue;
            }

            destination.Write(" ("u8);

            if(node.Kind == GeometryKind.GeometryCollection)
            {
                pending.Push(new WriteStep(0, EmitsClosingParenthesis: true, EmitsSeparator: false));

                for(int index = node.ChildCount - 1; index >= 0; index--)
                {
                    pending.Push(new WriteStep(node.FirstChild + index, EmitsClosingParenthesis: false, EmitsSeparator: index > 0));
                }

                continue;
            }

            switch(node.Kind)
            {
                case GeometryKind.Point:
                    WritePositions(in geometry, geometry.Parts[node.FirstPart], node, destination);
                    break;

                case GeometryKind.LineString:
                    WritePositions(in geometry, geometry.Parts[node.FirstPart], node, destination);
                    break;

                case GeometryKind.Polygon:
                    WriteRingList(in geometry, node, node.FirstPart, node.PartCount, destination);
                    break;

                case GeometryKind.MultiPoint:
                    for(int index = 0; index < node.PartCount; index++)
                    {
                        WriteSeparator(index, destination);
                        destination.Write("("u8);
                        WritePositions(in geometry, geometry.Parts[node.FirstPart + index], node, destination);
                        destination.Write(")"u8);
                    }

                    break;

                case GeometryKind.MultiLineString:
                    for(int index = 0; index < node.PartCount; index++)
                    {
                        WriteSeparator(index, destination);
                        destination.Write("("u8);
                        WritePositions(in geometry, geometry.Parts[node.FirstPart + index], node, destination);
                        destination.Write(")"u8);
                    }

                    break;

                case GeometryKind.MultiPolygon:
                    WritePolygonList(in geometry, node, destination);
                    break;

                default:
                    break;
            }

            destination.Write(")"u8);
        }
    }

    /// <summary>
    /// One pending emission step: a closing parenthesis, or a node — the latter behind
    /// the list separator when it follows an earlier collection member.
    /// </summary>
    /// <param name="NodeIndex">The node to emit; meaningless on a closing step.</param>
    /// <param name="EmitsClosingParenthesis">Whether this step only closes an open collection body.</param>
    /// <param name="EmitsSeparator">Whether the <c>", "</c> list separator precedes the node.</param>
    private readonly record struct WriteStep(int NodeIndex, bool EmitsClosingParenthesis, bool EmitsSeparator);

    /// <summary>Writes a polygon's ring list: the exterior ring then its holes, each parenthesized.</summary>
    private static void WriteRingList(
        in FlatGeometry geometry, FlatGeometryNode node, int firstPart, int partCount, IBufferWriter<byte> destination)
    {
        for(int index = 0; index < partCount; index++)
        {
            WriteSeparator(index, destination);
            destination.Write("("u8);
            WritePositions(in geometry, geometry.Parts[firstPart + index], node, destination);
            destination.Write(")"u8);
        }
    }

    /// <summary>
    /// Writes a multipolygon's polygons by grouping its part run: each exterior ring
    /// opens a new polygon, interior rings belong to the last opened one.
    /// </summary>
    private static void WritePolygonList(in FlatGeometry geometry, FlatGeometryNode node, IBufferWriter<byte> destination)
    {
        int index = 0;
        int polygonIndex = 0;

        while(index < node.PartCount)
        {
            int groupStart = index;
            index++;

            while(index < node.PartCount && geometry.Parts[node.FirstPart + index].Role == FlatGeometryPartRole.InteriorRing)
            {
                index++;
            }

            WriteSeparator(polygonIndex, destination);
            destination.Write("("u8);
            WriteRingList(in geometry, node, node.FirstPart + groupStart, index - groupStart, destination);
            destination.Write(")"u8);
            polygonIndex++;
        }
    }

    /// <summary>Writes one part's positions, comma-separated, with the node's carried ordinates.</summary>
    private static void WritePositions(
        in FlatGeometry geometry, FlatGeometryPart part, FlatGeometryNode node, IBufferWriter<byte> destination)
    {
        for(int index = 0; index < part.Length; index++)
        {
            WriteSeparator(index, destination);
            int vertexIndex = part.Start + index;

            WriteNumber(geometry.Vertices[vertexIndex].X, destination);
            destination.Write(" "u8);
            WriteNumber(geometry.Vertices[vertexIndex].Y, destination);

            if(node.HasZ)
            {
                destination.Write(" "u8);
                WriteNumber(geometry.ZOrdinates[vertexIndex], destination);
            }

            if(node.HasM)
            {
                destination.Write(" "u8);
                WriteNumber(geometry.MOrdinates[vertexIndex], destination);
            }
        }
    }

    /// <summary>Writes the <c>", "</c> list separator before every item but the first.</summary>
    private static void WriteSeparator(int index, IBufferWriter<byte> destination)
    {
        if(index > 0)
        {
            destination.Write(", "u8);
        }
    }

    /// <summary>
    /// Writes one coordinate in shortest-round-trip invariant form, formatted straight
    /// into the destination's own span — no character intermediate.
    /// </summary>
    private static void WriteNumber(double value, IBufferWriter<byte> destination)
    {
        Span<byte> span = destination.GetSpan(32);
        bool formatted = value.TryFormat(span, out int bytesWritten, format: default, CultureInfo.InvariantCulture);

        if(!formatted)
        {
            //The shortest round-trip form of any finite double fits 32 bytes; a failure
            //here is a sizing defect, not a data condition.
            throw new InvalidOperationException("A coordinate did not fit the number buffer.");
        }

        destination.Advance(bytesWritten);
    }

    /// <summary>The uppercase tag bytes of a kind.</summary>
    private static ReadOnlySpan<byte> TagOf(GeometryKind kind)
    {
        return kind switch
        {
            GeometryKind.Point => "POINT"u8,
            GeometryKind.LineString => "LINESTRING"u8,
            GeometryKind.Polygon => "POLYGON"u8,
            GeometryKind.MultiPoint => "MULTIPOINT"u8,
            GeometryKind.MultiLineString => "MULTILINESTRING"u8,
            GeometryKind.MultiPolygon => "MULTIPOLYGON"u8,
            GeometryKind.GeometryCollection => "GEOMETRYCOLLECTION"u8,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown geometry kind."),
        };
    }
}
