using System;
using Lumoin.Veritas.Geo.Spatial;

namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The flat, value-type geometry carrier of the Simple Features substrate: a
/// breadth-first node table of tagged geometries, a part table of vertex-run slices,
/// and vertex columns (XY always; Z and M only when some node carries the ordinate,
/// with <see cref="double.NaN"/> in the slots of nodes that do not). Instances come
/// only from the factories (<see cref="WktGeometryReader"/>, <see cref="Empty"/>, and
/// the operation builders); the columns never escape mutable. All point-set
/// computation is planar XY — Z and M are carried, never computed with. Ordinates a
/// certified construction consumes or generates under per-emission exact
/// verification against published bounds sit outside that carriage sentence's
/// scope: certification is the license to compute with them.
/// </summary>
/// <remarks>
/// Every classification property computes live from the columns, so
/// <c>default(FlatGeometry)</c> — which zero-initialization can always produce —
/// degrades to the answers of an empty geometry collection rather than caching a
/// wrong precomputed bit. Equality is structural and bitwise on coordinates
/// (negative zero differs from zero), hand-authored because synthesized
/// record-struct equality would compare the column arrays by reference. The
/// vertex-scale columns are <see cref="System.Buffers.IMemoryOwner{T}"/> rentals from
/// the build's <see cref="FlatGeometryAllocators"/> seam: with the heap default,
/// <see cref="Dispose"/> is a no-op; with a pooling host's allocators the caller owns
/// the geometry's lifetime, no span may be touched after disposal, and struct copies
/// share the one rental.
/// </remarks>
public readonly struct FlatGeometry: IEquatable<FlatGeometry>, IDisposable
{
    /// <summary>The backing node table; null only on an uninitialized instance.</summary>
    private FlatGeometryNode[]? NodeTable { get; }

    /// <summary>The backing part table; null only on an uninitialized instance.</summary>
    private FlatGeometryPart[]? PartTable { get; }

    /// <summary>The XY column rental; null when the geometry is empty.</summary>
    private System.Buffers.IMemoryOwner<Point2d>? VertexColumn { get; }

    /// <summary>The Z column rental; null when no node carries Z.</summary>
    private System.Buffers.IMemoryOwner<double>? ZColumn { get; }

    /// <summary>The M column rental; null when no node carries M.</summary>
    private System.Buffers.IMemoryOwner<double>? MColumn { get; }

    /// <summary>Assigns the columns; builders guarantee their consistency and exact rental lengths.</summary>
    internal FlatGeometry(
        FlatGeometryNode[] nodes,
        FlatGeometryPart[] parts,
        System.Buffers.IMemoryOwner<Point2d>? vertexColumn,
        System.Buffers.IMemoryOwner<double>? zColumn,
        System.Buffers.IMemoryOwner<double>? mColumn)
    {
        NodeTable = nodes;
        PartTable = parts;
        VertexColumn = vertexColumn;
        ZColumn = zColumn;
        MColumn = mColumn;
    }

    /// <summary>The node table, breadth-first; index 0 is the root tagged geometry.</summary>
    public ReadOnlySpan<FlatGeometryNode> Nodes => NodeTable;

    /// <summary>The part table the nodes' part runs index into.</summary>
    public ReadOnlySpan<FlatGeometryPart> Parts => PartTable;

    /// <summary>The XY vertex column the parts slice.</summary>
    public ReadOnlySpan<Point2d> Vertices => VertexColumn is null ? default : VertexColumn.Memory.Span;

    /// <summary>The Z column, aligned with <see cref="Vertices"/>; empty when no node carries Z.</summary>
    public ReadOnlySpan<double> ZOrdinates => ZColumn is null ? default : ZColumn.Memory.Span;

    /// <summary>The M column, aligned with <see cref="Vertices"/>; empty when no node carries M.</summary>
    public ReadOnlySpan<double> MOrdinates => MColumn is null ? default : MColumn.Memory.Span;

    /// <summary>
    /// Returns the column rentals to their allocator. A no-op for heap-backed columns
    /// and for an uninitialized instance; after disposing a pooled geometry, no span of
    /// it — nor of any copy, since copies share the rental — may be touched.
    /// </summary>
    public void Dispose()
    {
        VertexColumn?.Dispose();
        ZColumn?.Dispose();
        MColumn?.Dispose();
    }

    /// <summary>
    /// The root tagged kind. An uninitialized instance answers
    /// <see cref="GeometryKind.GeometryCollection"/>, completing its degradation to the
    /// empty collection.
    /// </summary>
    public GeometryKind Kind => NodeTable is { Length: > 0 } ? NodeTable[0].Kind : GeometryKind.GeometryCollection;

    /// <summary>
    /// Whether the geometry is the empty point set — no positions anywhere, which for a
    /// collection means no members or only empty members.
    /// </summary>
    public bool IsEmpty => Vertices.Length == 0;

    /// <summary>Whether any node's positions carry a Z ordinate.</summary>
    public bool Is3D => AnyNodeCarries(z: true);

    /// <summary>Whether any node's positions carry an M ordinate.</summary>
    public bool IsMeasured => AnyNodeCarries(z: false);

    /// <summary>
    /// The topological dimension: 0 for puntal, 1 for lineal, 2 for polygonal kinds —
    /// kind-intrinsic, so typed empties keep their kind's answer; a collection takes the
    /// maximum over its members and answers −1 exactly when that maximum is −1 (no
    /// members, or only empty-collection members, recursively). Collections contribute
    /// nothing themselves, so the answer is the maximum over all non-collection nodes.
    /// </summary>
    public int TopologicalDimension
    {
        get
        {
            int maximum = -1;

            if(NodeTable is not null)
            {
                foreach(FlatGeometryNode node in NodeTable)
                {
                    int intrinsic = node.Kind switch
                    {
                        GeometryKind.Point or GeometryKind.MultiPoint => 0,
                        GeometryKind.LineString or GeometryKind.MultiLineString => 1,
                        GeometryKind.Polygon or GeometryKind.MultiPolygon => 2,
                        _ => -1,
                    };

                    if(intrinsic > maximum)
                    {
                        maximum = intrinsic;
                    }
                }
            }

            return maximum;
        }
    }

    /// <summary>
    /// The ordinate count per position: 2, plus one when any node carries Z, plus one
    /// when any node carries M. For mixed-scheme collections this any-fold is the house
    /// convention — no conformance material pins the mixed case.
    /// </summary>
    public int CoordinateDimension => 2 + (Is3D ? 1 : 0) + (IsMeasured ? 1 : 0);

    /// <summary>The spatial axis count per position: 2, plus one when any node carries Z.</summary>
    public int SpatialDimension => 2 + (Is3D ? 1 : 0);

    /// <summary>The typed empty geometry of the given kind.</summary>
    public static FlatGeometry Empty(GeometryKind kind)
    {
        return new FlatGeometry(
            [new FlatGeometryNode(kind, 0, 0, 0, 0, false, false)],
            [],
            vertexColumn: null,
            zColumn: null,
            mColumn: null);
    }

    /// <summary>Structural equality: same tables and bitwise-identical coordinate columns.</summary>
    public bool Equals(FlatGeometry other)
    {
        return SpansEqual(Nodes, other.Nodes)
            && SpansEqual(Parts, other.Parts)
            && VerticesBitwiseEqual(Vertices, other.Vertices)
            && OrdinatesBitwiseEqual(ZOrdinates, other.ZOrdinates)
            && OrdinatesBitwiseEqual(MOrdinates, other.MOrdinates);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return obj is FlatGeometry other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Nodes.Length);
        hash.Add(Parts.Length);
        hash.Add(Vertices.Length);

        if(NodeTable is { Length: > 0 })
        {
            hash.Add(NodeTable[0].Kind);
        }

        ReadOnlySpan<Point2d> vertices = Vertices;

        if(vertices.Length > 0)
        {
            hash.Add(BitConverter.DoubleToInt64Bits(vertices[0].X));
            hash.Add(BitConverter.DoubleToInt64Bits(vertices[0].Y));
        }

        return hash.ToHashCode();
    }

    /// <summary>Structural equality, see <see cref="Equals(FlatGeometry)"/>.</summary>
    public static bool operator ==(FlatGeometry left, FlatGeometry right)
    {
        return left.Equals(right);
    }

    /// <summary>Structural inequality, see <see cref="Equals(FlatGeometry)"/>.</summary>
    public static bool operator !=(FlatGeometry left, FlatGeometry right)
    {
        return !left.Equals(right);
    }

    /// <summary>Whether any node carries the selected ordinate: Z when <paramref name="z"/> is true, M otherwise.</summary>
    private bool AnyNodeCarries(bool z)
    {
        if(NodeTable is not null)
        {
            foreach(FlatGeometryNode node in NodeTable)
            {
                if(z ? node.HasZ : node.HasM)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Element-wise equality of two table spans.</summary>
    private static bool SpansEqual<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right)
        where T: IEquatable<T>
    {
        return left.SequenceEqual(right);
    }

    /// <summary>Bitwise coordinate equality of two vertex columns; negative zero differs from zero.</summary>
    private static bool VerticesBitwiseEqual(ReadOnlySpan<Point2d> left, ReadOnlySpan<Point2d> right)
    {
        if(left.Length != right.Length)
        {
            return false;
        }

        for(int index = 0; index < left.Length; index++)
        {
            if(BitConverter.DoubleToInt64Bits(left[index].X) != BitConverter.DoubleToInt64Bits(right[index].X)
                || BitConverter.DoubleToInt64Bits(left[index].Y) != BitConverter.DoubleToInt64Bits(right[index].Y))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Bitwise equality of two ordinate columns; NaN slots compare equal to themselves.</summary>
    private static bool OrdinatesBitwiseEqual(ReadOnlySpan<double> left, ReadOnlySpan<double> right)
    {
        if(left.Length != right.Length)
        {
            return false;
        }

        for(int index = 0; index < left.Length; index++)
        {
            if(BitConverter.DoubleToInt64Bits(left[index]) != BitConverter.DoubleToInt64Bits(right[index]))
            {
                return false;
            }
        }

        return true;
    }
}
