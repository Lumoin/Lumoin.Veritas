namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// A circle in the plane — a center and a radius, in the coordinate units of the
/// positions it was computed from. The result currency of
/// <see cref="SimpleFeatures.GeometryBoundingCircle"/>; the coverage guarantee lives
/// on the kernel that mints the value, not on this type. A value type styled on
/// <see cref="BoundingBox"/>: no allocation, no behavior beyond carriage.
/// </summary>
/// <param name="Center">The center position.</param>
/// <param name="Radius">The radius in coordinate units; zero for a single-position operand.</param>
public readonly record struct BoundingCircle(Point2d Center, double Radius);
