namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The outcome of one certified three-dimensional arc linearization: the success value
/// first, then the refusal classes. The kernel knows no document bytes, so a consuming
/// codec maps every non-certified outcome — together with the offending seed index the
/// kernel reports — onto its own refusal currency at the document offsets it captured
/// during its walk. The roster is the planar kernel's plus the planarity member the
/// third dimension demands, and the set is closed against silent additions: a new
/// member is a design amendment, never a code-level convenience.
/// </summary>
internal enum CircularArcLinearization3dOutcome
{
    /// <summary>
    /// Every emitted vertex passed its exact radial and planarity bands and every
    /// emitted chord its exact sagitta check; the vertex run was appended in arc order.
    /// </summary>
    Certified = 0,

    /// <summary>
    /// Two control points coincide on value equality over all three ordinates, so the
    /// circle through them is underdetermined. The offending seed index names the later
    /// point of the pair.
    /// </summary>
    CoincidentControlPoints = 1,

    /// <summary>
    /// The three control points are exactly collinear — the exact expansion of their
    /// edge cross product is zero in all three components — so no plane and no circle
    /// pass through them. The offending seed index names the third control point.
    /// </summary>
    CollinearControlPoints = 2,

    /// <summary>
    /// An input ordinate, a computed center ordinate, the computed radius, or a
    /// constructed split vertex's ordinate sits outside the magnitude walls the exact
    /// predicates require — non-finite values included, because the wall test is
    /// written in acceptance form and a value that is not a number fails it. A
    /// plain-double construction poisoned by rounded projection collapse surfaces
    /// here through its non-finite computed values, and a constructed ordinate that
    /// cancels beneath the lower wall surfaces here before the exact bands would
    /// consume it. The offending seed index names the input control point, or minus
    /// one for a computed or constructed value.
    /// </summary>
    MagnitudeWall = 3,

    /// <summary>
    /// A vertex fell outside the published annulus around the certified sphere — a
    /// document seed against a circle the plain-double solve mis-placed, or a
    /// constructed vertex a too-coarse coordinate grid could not host. The offending
    /// seed index names the seed, or minus one for a constructed vertex.
    /// </summary>
    VertexDrift = 4,

    /// <summary>
    /// A constructed split vertex failed the exact membership check on its own gap
    /// through every construction — the midpoint direction and both signs of the
    /// pinned in-plane perpendicular. The offending seed index is minus one.
    /// </summary>
    SplitMembership = 5,

    /// <summary>
    /// A gap failed to certify within the published bisection depth. Certifiable
    /// input clears far below the cap, so reaching it means the arithmetic cannot
    /// certify this arc. The offending seed index is minus one.
    /// </summary>
    DepthCeiling = 6,

    /// <summary>
    /// The computed center at the once-per-arc gate, or a constructed split vertex,
    /// sat farther from the exact control-point plane than the published planarity
    /// band. Document seeds define that plane and are exactly planar — a determinant
    /// with a repeated row vanishes — so the offending seed index is always minus one.
    /// </summary>
    PlanarDrift = 7,
}
