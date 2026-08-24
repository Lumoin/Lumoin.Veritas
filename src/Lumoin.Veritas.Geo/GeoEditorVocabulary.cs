using System.Collections.Generic;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The geospatial contribution to an editor's fixed completion corpus: the GeoSPARQL ontology
/// (<c>geo:</c>), the GeoSPARQL function (<c>geof:</c>), the Simple Features (<c>sf:</c>), and the GML
/// (<c>gml:</c>) term sets, each paired with the conventional prefix a buffer declares for it. The terms are
/// the canonical vocabulary constants themselves, so the corpus has one source of truth. The composing host
/// hands these groups to the corpus writer, which keeps the corpus assembly free of any geospatial
/// dependency.
/// </summary>
public static class GeoEditorVocabulary
{
    /// <summary>The conventional-prefix + term-set groups this library contributes to an editor's completion corpus.</summary>
    public static IReadOnlyList<(string Prefix, IReadOnlyList<Utf8String> Terms)> Groups { get; } =
    [
        ("geo", GeoVocabulary.Geo.All),
        ("geof", GeoVocabulary.Geof.All),
        ("sf", GeoVocabulary.Sf.All),
        ("gml", GeoVocabulary.Gml.All)
    ];
}
