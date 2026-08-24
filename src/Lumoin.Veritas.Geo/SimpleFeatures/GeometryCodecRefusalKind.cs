namespace Lumoin.Veritas.Geo.SimpleFeatures;

/// <summary>
/// The closed set of reasons the serialization codec family (the GeoJSON,
/// GML, and KML readers and writers, and the WKT reader) refuses an input or
/// an emission by value. The set
/// is closed against silent additions: a new member is a design amendment,
/// never a code-level convenience, so a consumer switching over these kinds
/// can be exhaustive and stay exhaustive.
/// </summary>
public enum GeometryCodecRefusalKind
{
    /// <summary>
    /// No refusal — the value a successful call reports, and the no-offense
    /// marker a forward cursor pairs with false at exhaustion. It always
    /// carries a byte offset of minus one.
    /// </summary>
    None = 0,

    /// <summary>
    /// A transport-level violation of the codec's document grammar: JSON or
    /// XML that is not well formed under the accepted subset, a broken
    /// coordinate-tuple syntax, a duplicated attribute or duplicated
    /// recognized member, a non-object element inside an object-only array,
    /// an illegal character reference, a foreign encoding declaration, a
    /// leading byte-order mark where the format refuses one, or a truncated
    /// document. The byte offset names the first byte at which the grammar
    /// could not be extended; for truncation and the zero-length input it is
    /// the input length. On the writer side, a carried opaque extent that
    /// fails its grammar or is not tight refuses with this kind at minus
    /// one.
    /// </summary>
    MalformedDocument = 1,

    /// <summary>
    /// A construct excluded from the accepted subset by the security floor:
    /// a document type declaration, any entity declaration, a processing
    /// instruction, a remote-reference member such as an xlink:href, or a
    /// vendor-extension element. These readers ingest untrusted literal
    /// values, so the exclusion is contract, not preference.
    /// </summary>
    ProhibitedConstruct = 2,

    /// <summary>
    /// Recognized but unrepresentable content: a geometry vocabulary the
    /// flat model does not carry (non-linear curves, surfaces with patches,
    /// solids, geometric complexes, models), a feature envelope where a bare
    /// geometry value is required, a recognized root type at the wrong typed
    /// entry point — a geometry root where a feature or collection envelope
    /// is required, and the reverse — an element outside the recognized
    /// namespace, or an unknown geometry element or type tag.
    /// </summary>
    UnsupportedGeometry = 3,

    /// <summary>
    /// A coordinate reference system declaration outside the codec's closed
    /// recognition set, a conflicting nested declaration, a declaration
    /// whose absence leaves the system genuinely unspecified where one is
    /// required, or a writer invoked with an unrecognized or default system.
    /// </summary>
    UnrecognizedCoordinateReferenceSystem = 4,

    /// <summary>
    /// An ordinate-dimension violation: a declared dimension outside two or
    /// three, conflicting dimension declarations along one element path, a
    /// coordinate list whose token count does not divide by the effective
    /// dimension, a dimension declaration missing where a dependent
    /// attribute requires it, non-uniform ordinate arity within one
    /// coordinate carrier, mixed arity across the members of one typed
    /// aggregate, a position carrying more elements than the format
    /// defines, or a bounding-box array whose length is neither four nor
    /// six.
    /// </summary>
    DimensionMismatch = 5,

    /// <summary>
    /// An ordinate whose parsed value is not finite: a NaN or infinity
    /// token where the grammar admits one, overflow of a syntactically
    /// finite token — bounding-box values included — or, on the writer
    /// side, a non-finite ordinate, a NaN slot under a node that declares
    /// the ordinate, or a non-finite bounding-box value at minus one.
    /// </summary>
    NonFiniteCoordinate = 6,

    /// <summary>
    /// A required structural member or child of the recognized kind is
    /// absent, duplicated, misplaced, or cardinality-violated: an unclosed
    /// ring, a ring with fewer than four positions, a line with fewer than
    /// two, a geometry object missing its type or coordinates member, a
    /// point element without a position, a primitive without coordinates, a
    /// memberless heterogeneous aggregate where the format defines no empty,
    /// a wrong per-kind tuple count, a member element inside a container
    /// that cannot carry it, an unrecognized mode token, or a stated count
    /// that disagrees with the actual position count.
    /// </summary>
    StructuralViolation = 7,

    /// <summary>
    /// Geometry nesting beyond the certified bound of thirty-two levels on
    /// the reader side, element nesting beyond the transport bound of
    /// ninety-six on the XML substrate's scanner, or — on the writer side —
    /// model nesting the format's own reader would refuse.
    /// </summary>
    NestingTooDeep = 8,

    /// <summary>
    /// Non-whitespace content after the single geometry value the document
    /// carries.
    /// </summary>
    TrailingContent = 9,

    /// <summary>
    /// A measured geometry offered to a format that cannot carry measures.
    /// None of the three codec formats represents an M ordinate; the writer
    /// refuses rather than silently dropping the measure.
    /// </summary>
    MeasureUnrepresentable = 10,

    /// <summary>
    /// An empty geometry offered to a format position that cannot express
    /// it: an empty primitive into GML, or any empty into KML.
    /// </summary>
    EmptyUnrepresentable = 11,
}
