namespace Lumoin.Veritas.Geo.Transforms;

/// <summary>
/// The closed set of reasons the transform surface refuses a request by
/// value. The set is closed against silent additions: a new member is a
/// design amendment, never a code-level convenience, so a consumer switching
/// over these kinds can be exhaustive and stay exhaustive.
/// </summary>
public enum CoordinateTransformRefusalKind
{
    /// <summary>
    /// No refusal — the value a successful call reports. A successful call
    /// always carries this kind with an element index of minus one.
    /// </summary>
    None = 0,

    /// <summary>
    /// The source coordinate reference system is not a recognized roster
    /// member. A default-constructed <see cref="CoordinateReferenceSystem"/>
    /// refuses with this kind.
    /// </summary>
    SourceCrsUnrecognized = 1,

    /// <summary>
    /// The target coordinate reference system is not a recognized roster
    /// member. A default-constructed <see cref="CoordinateReferenceSystem"/>
    /// refuses with this kind.
    /// </summary>
    TargetCrsUnrecognized = 2,

    /// <summary>
    /// A source coordinate is NaN or infinite. The element index names the
    /// first non-finite double in the source span.
    /// </summary>
    NonFiniteCoordinate = 3,

    /// <summary>
    /// A finite source coordinate lies outside the source system's declared
    /// domain, read in the source system's declared axis order. The element
    /// index names the first offending double in the source span.
    /// </summary>
    CoordinateOutsideSourceDomain = 4,

    /// <summary>
    /// A finite, source-valid coordinate the target pair cannot answer for:
    /// a geographic latitude poleward of the Web Mercator limit, or a Web
    /// Mercator boundary coordinate whose computed geographic image the
    /// reverse geographic-to-Web-Mercator operation would not accept back —
    /// the image may be a perfectly representable geographic value; the
    /// refusal is about round-trip acceptance, not representability. The
    /// element index names the first offending double in the source span.
    /// </summary>
    CoordinateOutsideTargetDomain = 5
}
