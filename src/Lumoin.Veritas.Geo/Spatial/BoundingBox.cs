namespace Lumoin.Veritas.Geo.Spatial;

/// <summary>
/// An axis-aligned bounding box in the plane — the inclusive extent
/// [<see cref="MinX"/>, <see cref="MaxX"/>] × [<see cref="MinY"/>, <see cref="MaxY"/>].
/// A value type: no allocation, array-of-structures at call sites.
/// </summary>
/// <param name="MinX">The minimum x extent (inclusive).</param>
/// <param name="MinY">The minimum y extent (inclusive).</param>
/// <param name="MaxX">The maximum x extent (inclusive).</param>
/// <param name="MaxY">The maximum y extent (inclusive).</param>
public readonly record struct BoundingBox(double MinX, double MinY, double MaxX, double MaxY)
{
    /// <summary>
    /// Whether this box encloses <paramref name="other"/>, each bound non-strict (touching and
    /// identical boxes enclose each other).
    /// </summary>
    public bool Contains(BoundingBox other)
    {
        return MinX <= other.MinX && MaxX >= other.MaxX && MinY <= other.MinY && MaxY >= other.MaxY;
    }

    /// <summary>
    /// Whether this box and <paramref name="other"/> meet in at least one point, each bound
    /// non-strict — touching edges and touching corners intersect, mirroring
    /// <see cref="Contains"/>'s closed intervals. Any NaN ordinate answers false by IEEE
    /// comparison semantics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This states publicly the same four-ordinate algebra the packed box index's leaf
    /// refinement applies to its stored columns; the two spellings are gated to agree by the
    /// index's parity harness.
    /// </para>
    /// <para>
    /// Symmetry (<c>a.Intersects(b) == b.Intersects(a)</c>) holds unconditionally, NaN and
    /// inverted operands included. The implication <c>Contains ⇒ Intersects</c> holds for
    /// well-formed operands only: <see cref="BoundingBox"/> carries no invariant, and an
    /// operand with an inverted axis can satisfy <see cref="Contains"/> while failing this
    /// predicate.
    /// </para>
    /// </remarks>
    public bool Intersects(BoundingBox other)
    {
        return MinX <= other.MaxX && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;
    }
}
