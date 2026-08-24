namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The verdict of a circumscribed-ring coverage verification: whether an emitted polygon ring
/// certifiably covers a circle. A verdict is a value — verification refusal is an expected
/// condition of the rendering seam, never an exception.
/// </summary>
internal enum CircleCoverageVerdict
{
    /// <summary>The ring certifiably covers the circle, boundary included.</summary>
    Covers,

    /// <summary>
    /// The ring does not certifiably cover the circle: a shape, winding, convexity,
    /// center-side, or edge-distance gate failed. A short ring may become covering at a
    /// lifted circumradius.
    /// </summary>
    Short,

    /// <summary>
    /// An input violates the verifier's quantum walls — a non-finite ordinate or radius, an
    /// ordinate beyond the magnitude the degree-four exact evaluation stays finite for, or a
    /// radius below the magnitude the evaluation stays exact for. No lift can cure a wall
    /// violation.
    /// </summary>
    WallViolation
}
