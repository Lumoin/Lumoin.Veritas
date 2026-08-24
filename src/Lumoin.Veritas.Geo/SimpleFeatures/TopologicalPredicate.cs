namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The twenty-four named topological predicates the relate engine evaluates —
/// the Simple Features, Egenhofer, and RCC8 families — named to match the
/// GeoSPARQL vocabulary constants so a function seam maps identifier to value
/// mechanically. Every predicate reads the computed
/// <see cref="IntersectionMatrix"/>; the Simple Features crosses and overlaps
/// members additionally carry the standard's own dimension conditions.
/// </summary>
public enum TopologicalPredicate
{
    /// <summary>Simple Features equals: mutual within, <c>T*F**FFF*</c>.</summary>
    SfEquals = 0,

    /// <summary>Simple Features disjoint: <c>FF*FF****</c>.</summary>
    SfDisjoint = 1,

    /// <summary>Simple Features intersects: the negation of disjoint.</summary>
    SfIntersects = 2,

    /// <summary>Simple Features touches: boundary contact without interior contact.</summary>
    SfTouches = 3,

    /// <summary>Simple Features crosses: dimension-branched interior crossing.</summary>
    SfCrosses = 4,

    /// <summary>Simple Features within: <c>T*F**F***</c>.</summary>
    SfWithin = 5,

    /// <summary>Simple Features contains: <c>T*****FF*</c>.</summary>
    SfContains = 6,

    /// <summary>Simple Features overlaps: equal-dimension partial interior overlap.</summary>
    SfOverlaps = 7,

    /// <summary>Egenhofer equals: <c>TFFFTFFFT</c>.</summary>
    EhEquals = 8,

    /// <summary>Egenhofer disjoint: <c>FF*FF****</c>.</summary>
    EhDisjoint = 9,

    /// <summary>Egenhofer meet: boundary contact without interior contact.</summary>
    EhMeet = 10,

    /// <summary>Egenhofer overlap: the bare pattern <c>T*T***T**</c>, ungated.</summary>
    EhOverlap = 11,

    /// <summary>Egenhofer covers: <c>T*TFT*FF*</c>.</summary>
    EhCovers = 12,

    /// <summary>Egenhofer coveredBy: <c>TFF*TFT**</c>.</summary>
    EhCoveredBy = 13,

    /// <summary>Egenhofer inside: <c>TFF*FFT**</c>.</summary>
    EhInside = 14,

    /// <summary>Egenhofer contains: <c>T*TFF*FF*</c>.</summary>
    EhContains = 15,

    /// <summary>RCC8 equals: <c>TFFFTFFFT</c>.</summary>
    Rcc8Eq = 16,

    /// <summary>RCC8 disconnected: <c>FFTFFTTTT</c>.</summary>
    Rcc8Dc = 17,

    /// <summary>RCC8 externally connected: <c>FFTFTTTTT</c>.</summary>
    Rcc8Ec = 18,

    /// <summary>RCC8 partially overlapping: <c>TTTTTTTTT</c>.</summary>
    Rcc8Po = 19,

    /// <summary>RCC8 tangential proper part inverse: <c>TTTFTTFFT</c>.</summary>
    Rcc8Tppi = 20,

    /// <summary>RCC8 tangential proper part: <c>TFFTTFTTT</c>.</summary>
    Rcc8Tpp = 21,

    /// <summary>RCC8 non-tangential proper part: <c>TFFTFFTTT</c>.</summary>
    Rcc8Ntpp = 22,

    /// <summary>RCC8 non-tangential proper part inverse: <c>TTTFFTFFT</c>.</summary>
    Rcc8Ntppi = 23,
}
