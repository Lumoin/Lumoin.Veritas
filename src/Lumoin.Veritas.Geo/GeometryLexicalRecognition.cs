namespace Lumoin.Veritas.Geo;

/// <summary>
/// The outcome of lexically recognizing a geometry-literal body, shared by every serialization
/// recognizer (well-known text, GML, GeoJSON, KML, DGGS). The abstention value is
/// <see cref="Unrecognized"/> at ordinal zero, so a defaulted value never asserts well-formedness or
/// malformedness. Each recognizer's own documentation names its abstention set — the constructs it
/// recognizes without certifying.
/// </summary>
public enum GeometryLexicalRecognition
{
    /// <summary>
    /// The body contains a construct the recognizer does not certify — the sound abstention, and the
    /// zero default. Nothing is claimed in either direction.
    /// </summary>
    Unrecognized = 0,

    /// <summary>The body is provably well-formed under the certified grammar, or is empty (an empty geometry).</summary>
    WellFormed,

    /// <summary>The body is provably outside the serialization's grammar.</summary>
    Malformed,

    /// <summary>
    /// Recognition stopped at the recognizer's hard nesting cap — a resource bound, not a grammar
    /// verdict, so it carries no well-formedness claim in either direction.
    /// </summary>
    DepthExceeded,
}
