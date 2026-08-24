namespace Lumoin.Veritas.Geo;

/// <summary>
/// The house-minted identity of the A5 pentagonal DGGS flavour this library implements: the grid IRI a
/// DGGS literal carries inside its angle-bracket prefix, and the subclass datatype IRI that indicates
/// the flavour, per the specification's guidance that a specific DGGS implementation should be indicated
/// by a subclass of the generic DGGS literal datatype. Both IRIs are minted under the house namespace;
/// the OGC ontology namespace carries only OGC's own terms.
/// </summary>
public static class A5DggsVocabulary
{
    private static byte[] GridIriBytes { get; } = "https://lumoin.com/veritas/dggs/a5"u8.ToArray();
    private static byte[] DatatypeIriBytes { get; } = "https://lumoin.com/veritas/dggs/a5Literal"u8.ToArray();
    private static byte[] ResolutionQueryPrefixBytes { get; } = "?resolution="u8.ToArray();

    /// <summary>The A5 grid IRI a house-flavour literal carries inside its angle-bracket prefix.</summary>
    public static Utf8String GridIri { get; } = new(GridIriBytes);

    /// <summary>The <c>a5Literal</c> subclass datatype IRI indicating the house A5 flavour.</summary>
    public static Utf8String DatatypeIri { get; } = new(DatatypeIriBytes);

    /// <summary>
    /// The query prefix through which <c>geof:asDGGS</c>'s datatype argument carries the target
    /// resolution: the datatype IRI followed by <c>?resolution=</c> and a decimal value 0 through 30
    /// with no leading zero. The specification's signature provides no resolution parameter, and a
    /// geometry-to-cells conversion is resolution-parametric, so the argument must state it; a bare
    /// datatype IRI answers the error value rather than fabricating a default.
    /// </summary>
    public static Utf8String ResolutionQueryPrefix { get; } = new(ResolutionQueryPrefixBytes);
}
