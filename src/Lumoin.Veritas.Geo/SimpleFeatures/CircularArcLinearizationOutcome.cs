namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The outcome of one certified arc linearization: the success value first, then the
/// refusal classes. The kernel knows no document bytes, so a consuming codec maps every
/// non-certified outcome — together with the offending seed index the kernel reports —
/// onto its own refusal currency at the document offsets it captured during its walk.
/// The set is closed against silent additions: a new member is a design amendment,
/// never a code-level convenience.
/// </summary>
internal enum CircularArcLinearizationOutcome
{
    /// <summary>
    /// Every emitted vertex passed its exact annulus check and every emitted chord its
    /// exact sagitta check; the vertex run was appended in arc order.
    /// </summary>
    Certified = 0,

    /// <summary>
    /// Two control points coincide on value equality, so the circle through them is
    /// underdetermined. The offending seed index names the later point of the pair.
    /// </summary>
    CoincidentControlPoints = 1,

    /// <summary>
    /// The three control points are exactly collinear on the exact orientation
    /// predicate, so no circle passes through them. The offending seed index names the
    /// third control point.
    /// </summary>
    CollinearControlPoints = 2,

    /// <summary>
    /// An input ordinate, the radius, or a computed center ordinate or computed radius
    /// sits outside the magnitude walls the exact checks require — non-finite values
    /// included, because the wall test is written in acceptance form and a value that
    /// is not a number fails it. The offending seed index names the input control
    /// point, or minus one for a computed value.
    /// </summary>
    MagnitudeWall = 3,

    /// <summary>
    /// A vertex fell outside the published annulus around the certified circle — a
    /// document seed against a circle the plain-double solve mis-placed, or a
    /// constructed vertex a too-coarse coordinate grid could not host. The offending
    /// seed index names the seed, or minus one for a constructed vertex.
    /// </summary>
    VertexDrift = 4,

    /// <summary>
    /// A constructed split vertex failed the exact membership check on its own gap
    /// through both constructions — the midpoint direction and the pinned
    /// perpendicular. The offending seed index is minus one.
    /// </summary>
    SplitMembership = 5,

    /// <summary>
    /// A gap failed to certify within the published bisection depth. Certifiable
    /// input clears far below the cap, so reaching it means the arithmetic cannot
    /// certify this arc. The offending seed index is minus one.
    /// </summary>
    DepthCeiling = 6,
}
