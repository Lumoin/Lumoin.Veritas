namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// One tagged geometry in a <see cref="FlatGeometry"/>'s node table. The table is laid
/// out breadth-first so a collection's children occupy the contiguous run
/// [<see cref="FirstChild"/>, <see cref="FirstChild"/> + <see cref="ChildCount"/>);
/// only <see cref="GeometryKind.GeometryCollection"/> nodes have children. Every other
/// kind — the three multi kinds included — owns the contiguous part run
/// [<see cref="FirstPart"/>, <see cref="FirstPart"/> + <see cref="PartCount"/>) instead.
/// An empty node owns zero parts and zero children while keeping its kind.
/// </summary>
/// <param name="Kind">The tagged kind of this node.</param>
/// <param name="FirstChild">The first child-node index; zero when the node has no children.</param>
/// <param name="ChildCount">The child-node count; nonzero only on non-empty collections.</param>
/// <param name="FirstPart">The first part index; zero when the node owns no parts.</param>
/// <param name="PartCount">The part count; zero for empty primitives and all collections.</param>
/// <param name="HasZ">Whether this node's positions carry a Z ordinate.</param>
/// <param name="HasM">Whether this node's positions carry an M ordinate.</param>
public readonly record struct FlatGeometryNode(
    GeometryKind Kind,
    int FirstChild,
    int ChildCount,
    int FirstPart,
    int PartCount,
    bool HasZ,
    bool HasM);
