using Lumoin.Veritas.Geo.SimpleFeatures;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The pinned clearance outcome of every CITE corpus artifact — the test face of the corpus
/// ledger the exhaustive-clearance ruling requires (the prose ledger lives in
/// <c>Geo/CiteGmlCorpus/PROVENANCE.md</c>). Every vendored artifact and every
/// derived twin has exactly one recorded outcome: a parse whose kind, emptiness,
/// coordinate-reference identity, and canonical text digest are pinned, or a refusal whose kind
/// and byte anchor are pinned. Regenerating an entry is a deliberate contract-change event, never
/// a test-time convenience.
/// </summary>
internal static class CiteGmlCorpusExpectations
{
    /// <summary>One pinned clearance outcome. Exactly one of the two faces is meaningful per row.</summary>
    internal readonly record struct CorpusExpectation(
        bool Accepts,
        GeometryKind GeometryKind,
        bool IsEmpty,
        string CoordinateReferenceIri,
        string WktSha256,
        GeometryCodecRefusalKind RefusalKind,
        int RefusalByteOffset);

    /// <summary>A runtime adaptation twin: its source artifact, rule, pinned derived bytes, and outcome.</summary>
    internal readonly record struct TwinExpectation(
        string SourcePath,
        CorpusDerivationRule Rule,
        string DerivedSha256,
        CorpusExpectation Outcome);

    /// <summary>Vendored artifacts by corpus-relative path.</summary>
    public static IReadOnlyDictionary<string, CorpusExpectation> Artifacts { get; } = new Dictionary<string, CorpusExpectation>(StringComparer.Ordinal)
    {
        ["fragments/gml32-features-FeatureCollection-1-fragment-0-adapted.xml"] = new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "7dd827a9fedbf894fde42d71a774c35b86bc4549b49a206d7e15805d7449faee", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-0-adapted.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "a59b925f96dc9f24e87e93e3a3eac92c05b23345b06e62c8eec46100a83c715e", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-1-adapted.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "622a40552fb77268b2dc56aba8ca8790051c5d1b7307c283f0618fa66543fd9f", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-2-adapted.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "8583c38e305d449aafbb11a023f6fae171aad56c28cf73403e86fea394b1fc32", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-3-adapted.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "bb097a15dd0cec610fe96f9fe70ad6be293f635d0d65ef9f1dd7363e17dc0b2e", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-4-adapted.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "78f0f08c8da3ee13ab2fc0955ae1c7fe725b9e8501922d2ce77d27963b294acb", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-features-FZK-Haus-LoD2-KIT-fragment-5-adapted.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "c50e72572745dfe8991440a470f023a6052301940a85d21f11f94c77e9321bc3", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-geom-CompositeCurve-fragment-0-adapted.xml"] = new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "ed190a981c50ace1b3cf6b00cb1d5665e7d7eec198477e5c08343a0666895ce6", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-geom-CompositeCurve-fragment-1-adapted.xml"] = new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "b2b69ca04a64e056540ceeb2ccc366675842389459f09ee8f1b0672363ea3b94", GeometryCodecRefusalKind.None, -1),
        ["fragments/gml32-SimpleFeature-2-fragment-0-adapted.xml"] = new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "7dd827a9fedbf894fde42d71a774c35b86bc4549b49a206d7e15805d7449faee", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32-data/Alpha-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32-data/Alpha-xinclude.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32-data/atom-feed-2.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 80),
        ["originals/gml32-data/atom-feed.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32-data/capabilities-simple.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32-data/Gamma.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32-data/SimpleFeature-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32-data/SimpleFeature-2.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 92),
        ["originals/gml32-data/SimpleFeature-xml-model.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.ProhibitedConstruct, 39),
        ["originals/gml32/aixm/AirportHeliport.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/Alpha-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.ProhibitedConstruct, 39),
        ["originals/gml32/Alpha-xinclude.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/atom-feed.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/basic-message.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/capabilities-simple.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/envelopes/Envelope-httpRef.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 106),
        ["originals/gml32/envelopes/Envelope-invalidCorner.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 106),
        ["originals/gml32/envelopes/Envelope-invalidCRS.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 133),
        ["originals/gml32/envelopes/Envelope-noCRS.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 95),
        ["originals/gml32/envelopes/Envelope-valid.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 106),
        ["originals/gml32/features/FeatureCollection-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/features/FZK-Haus-LoD2-KIT.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/Gamma-any.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/Gamma.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/geom/AIXMSurface-InteriorCCW.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 85),
        ["originals/gml32/geom/AIXMSurface-InteriorCrossesExterior.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 91),
        ["originals/gml32/geom/AIXMSurface.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/geom/CompositeCurve.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 128),
        ["originals/gml32/geom/Curve-ArcByCenterPoint.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 279),
        ["originals/gml32/geom/Curve-disconnected.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.StructuralViolation, 377),
        ["originals/gml32/geom/Curve-empty.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 76),
        ["originals/gml32/geom/Curve-GeodesicString.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 172),
        ["originals/gml32/geom/Curve-ID_250.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 170),
        ["originals/gml32/geom/Curve-LineString-axisOrder.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 125),
        ["originals/gml32/geom/Curve-LineString.xml"] = new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "62c03fb7a24d3f803f70179635c24ce746b3b2f81a801319d0fe1e167153e184", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Curve-tripartite.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 166),
        ["originals/gml32/geom/ElevatedSurface.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/geom/LineString-invalidCoords.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 189),
        ["originals/gml32/geom/LineString-srsName-http.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 251),
        ["originals/gml32/geom/LineString.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 156),
        ["originals/gml32/geom/MultiCurve-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 86),
        ["originals/gml32/geom/MultiCurve-2.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 86),
        ["originals/gml32/geom/MultiPoint-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 86),
        ["originals/gml32/geom/MultiSurface-ROSPA0080.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 109),
        ["originals/gml32/geom/MultiSurface.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 123),
        ["originals/gml32/geom/Point-2.5D.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 120),
        ["originals/gml32/geom/Point-27700.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 176),
        ["originals/gml32/geom/Point-axisOrder.xml"] = new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "d557cde08edbf60fa9c2310aea11445f5b1fc0584ed9f016e39c59403e968abe", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Point-epsg3045.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 129),
        ["originals/gml32/geom/Point-srsNameOnPos.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 183),
        ["originals/gml32/geom/PointWithBearing.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/geom/Polygon-InteriorCrossesExterior.xml"] = new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "6c52b434376acf55be8c07a1521e4f1c6e7cf2584664168e15039f6d1ce6d660", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Polygon-InteriorNotClosed.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.StructuralViolation, 577),
        ["originals/gml32/geom/Polygon-InteriorRing.xml"] = new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "c8e1c10ad6e2873608fa5c0ed916c92f9070e8d72d0b4801552afa19363f3921", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Polygon-InteriorTouchesExterior.xml"] = new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "8cdb67df098f2d91e83168db34c02b677d70be5c3576cac0647f827207416798", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Polygon-NotClosed.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.StructuralViolation, 331),
        ["originals/gml32/geom/Polygon-UTM.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 43),
        ["originals/gml32/geom/Surface-Curve-ID_250.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 366),
        ["originals/gml32/geom/Surface-DiscontiguousPatches.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 127),
        ["originals/gml32/geom/Surface-ExteriorCCW.xml"] = new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "75eec794756a4386556b2801057a1eb427eb27163a63a59cc91e358097c5b81a", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Surface-ExteriorCW.xml"] = new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "8d419973fee3d4b1b60dba17b1392b904a3489459825f854a2aa49ccdfbc8bdb", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Surface-InteriorCCW.xml"] = new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "0e067a93f88b70f18a2ebe6fbf82070ab48911040a373e870cbd2ac90f6446ea", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Surface-PolygonPatch-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 104),
        ["originals/gml32/geom/Surface-PolygonPatch-2.xml"] = new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "3130c967014ac6412e0822b2924ed7e24fa987c82a164fa4debb66b1db8033aa", GeometryCodecRefusalKind.None, -1),
        ["originals/gml32/geom/Surface-PolygonPatch-3.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 234),
        ["originals/gml32/geom/Surface-PolygonPatch-AxisOrder.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 217),
        ["originals/gml32/geom/Surface-PolygonPatch-ExteriorCurve.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 366),
        ["originals/gml32/geom/Surface-PolygonPatch-ExteriorCurveCW.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 476),
        ["originals/gml32/geom/Surface-RectangleTriangle.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 113),
        ["originals/gml32/gmlring2.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 292),
        ["originals/gml32/note.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/schema-catalog-gml-3.2.1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/schema-catalog.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/SimpleFeature-1.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/SimpleFeature-2.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/soapui/ets-gml32-soapui-project.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39),
        ["originals/gml32/test-run-props.xml"] = new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.ProhibitedConstruct, 39),
    };

    /// <summary>Runtime adaptation twins by twin identifier.</summary>
    public static IReadOnlyDictionary<string, TwinExpectation> Twins { get; } = new Dictionary<string, TwinExpectation>(StringComparer.Ordinal)
    {
        ["gml32-envelopes-Envelope-httpRef-adapted"] = new("originals/gml32/envelopes/Envelope-httpRef.xml", CorpusDerivationRule.CrsAdapted, "502dbd01099a702ff11b2a1abf286ae05fb9e73ec970530a96bc296c8bf1693f", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39)),
        ["gml32-envelopes-Envelope-invalidCorner-adapted"] = new("originals/gml32/envelopes/Envelope-invalidCorner.xml", CorpusDerivationRule.CrsAdapted, "b69acb33568f042a08e18cf0a1bc9a6e3fdf08312ae6b24511afd6f5382dff16", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39)),
        ["gml32-envelopes-Envelope-invalidCRS-adapted"] = new("originals/gml32/envelopes/Envelope-invalidCRS.xml", CorpusDerivationRule.CrsAdapted, "73681f28f2a090a5f0b618e9ad1c854e5a7582ad45c25b347a86f8b205a9a99f", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 66)),
        ["gml32-envelopes-Envelope-noCRS-adapted"] = new("originals/gml32/envelopes/Envelope-noCRS.xml", CorpusDerivationRule.CrsAdapted, "cc140013bc58f978cae22d8d0bd1774f08cf19b3ecf0dbda2ac16f359b72f89a", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39)),
        ["gml32-envelopes-Envelope-valid-adapted"] = new("originals/gml32/envelopes/Envelope-valid.xml", CorpusDerivationRule.CrsAdapted, "502dbd01099a702ff11b2a1abf286ae05fb9e73ec970530a96bc296c8bf1693f", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39)),
        ["gml32-geom-AIXMSurface-InteriorCCW-renamespaced-adapted"] = new("originals/gml32/geom/AIXMSurface-InteriorCCW.xml", CorpusDerivationRule.Renamespaced, "a038a2b3b05af23bf63aceedde4bdd27f312c3c7ab3db2decd2c1a57f3be89b3", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "97f048b65fcc166f301b480a13eb54833b01f2faf4e1308869c8dc6ea1669470", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-AIXMSurface-InteriorCrossesExterior-renamespaced-adapted"] = new("originals/gml32/geom/AIXMSurface-InteriorCrossesExterior.xml", CorpusDerivationRule.Renamespaced, "669d514cce61e9aad61051ea46eaa7183cd44791502773893e10b0d8cd5776bf", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "07666b7fc017cae4d1483f982d7dc2ff8627a8dcbe933409cb6ab84e91cb3276", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-AIXMSurface-renamespaced-adapted"] = new("originals/gml32/geom/AIXMSurface.xml", CorpusDerivationRule.Renamespaced, "5d512a85f5db5c56056ca984adc36a6726e7073911ed7644462571dc04461acc", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "a28fb0c3b6d3a3df883993da34666f194c7015cff005220005720c73cbf60b0b", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-CompositeCurve-rootcrs-adapted"] = new("originals/gml32/geom/CompositeCurve.xml", CorpusDerivationRule.RootCrsAdapted, "d5ccec410a5bd39688d83f23844ac3d3502d58b1cbf18712efbcad8512188c49", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 39)),
        ["gml32-geom-Curve-empty-adapted"] = new("originals/gml32/geom/Curve-empty.xml", CorpusDerivationRule.CrsAdapted, "1ca65b23900932e11ee2ba1b3bc98963580aaf705484d6cb79541cbd0e31dfa2", new(true, GeometryKind.LineString, true, "http://www.opengis.net/def/crs/EPSG/0/4326", "5983ad9d177593873e1fcdef85952c35a370297dd13d05da7de62f9d84ea64f4", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Curve-LineString-axisOrder-adapted"] = new("originals/gml32/geom/Curve-LineString-axisOrder.xml", CorpusDerivationRule.CrsAdapted, "0d44b45bb231b35e49bd55e426fddcddfcbe7e6793955ba9adb5c54e84255f17", new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "8d6ccb0c0337091bdd628af1c532c3491d8f9e3b6e10a9ef66e195f532084c45", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-LineString-adapted"] = new("originals/gml32/geom/LineString.xml", CorpusDerivationRule.CrsAdapted, "010bfd2f43c89a9184ddb8bcba01d414304ebd3814c2af9d3c2e3be59008ff3d", new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "92c5c4cb0e4e432f45ea509b6a81389e14fb258bb4be3a152e983a17d2c14dcf", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-LineString-invalidCoords-adapted"] = new("originals/gml32/geom/LineString-invalidCoords.xml", CorpusDerivationRule.CrsAdapted, "74b378d7bb307cc353a811510d350c9f47d806cb4fea77780c0fb23d5891efcf", new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "bc6bf9ee2e1e8162a389dbb4162bac05bd4de90638e7e86f8bc9a1fe8255f7da", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-LineString-srsName-http-adapted"] = new("originals/gml32/geom/LineString-srsName-http.xml", CorpusDerivationRule.CrsAdapted, "fac89f0309dc4e53a8ee9c6ca266e1f4178e7e72a72fb50a1f378b9e284fce46", new(true, GeometryKind.LineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "9fa421a595c0430721bc0a76c2497049dc3d54cdceff583271386340612cd4b7", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-MultiCurve-1-adapted"] = new("originals/gml32/geom/MultiCurve-1.xml", CorpusDerivationRule.CrsAdapted, "7e7a19873bb0e366ef9206c2784cd2c400fe9f581edf736f0efebe4557e57325", new(true, GeometryKind.MultiLineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "39fe3c671da3ee2cf53bebedc13424fc50494dcbd7b4630e91f9e9741adf44f8", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-MultiCurve-2-adapted"] = new("originals/gml32/geom/MultiCurve-2.xml", CorpusDerivationRule.CrsAdapted, "7b62b741e8e32db172379a4c0890b703ab1f486c699dfddc9840fba2a735a2f0", new(true, GeometryKind.MultiLineString, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "39fe3c671da3ee2cf53bebedc13424fc50494dcbd7b4630e91f9e9741adf44f8", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-MultiPoint-1-adapted"] = new("originals/gml32/geom/MultiPoint-1.xml", CorpusDerivationRule.CrsAdapted, "23175fbf2d63c604543c13e5e58978578a7c310b09885570f4158aa39faa66d5", new(true, GeometryKind.MultiPoint, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "99d7808c8820630d88c67cf6d460e784ea79ba77b981060e33378418cd1b298d", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-MultiSurface-adapted"] = new("originals/gml32/geom/MultiSurface.xml", CorpusDerivationRule.CrsAdapted, "1f9e27eae624772fdbc2c84bdaa1012edc11ae4db89155b965a9f161515336b9", new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "3785f8a8ae26c9960712d92a256a81109f3f71fe594b2d4cb24efd4f0f844b6a", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-MultiSurface-ROSPA0080-adapted"] = new("originals/gml32/geom/MultiSurface-ROSPA0080.xml", CorpusDerivationRule.CrsAdapted, "774c93f9a9dbf37cb1e44c62aaae68acb5814063366d6e5f62ebc1ba409aae43", new(true, GeometryKind.MultiPolygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "a25b5a71b6248c0df6712dd01606f90a987dc89d67c4222744cfba226d5b8141", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Point-2.5D-adapted"] = new("originals/gml32/geom/Point-2.5D.xml", CorpusDerivationRule.CrsAdapted, "50cd88f0e695bb5fb4df068cd93a18d76580b42f9fab44e2a930a4cdf56a3fcc", new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "d79913adc7242f1938049a7b33b58a705f7d0a5faf0c868cd9188474d0aaf45f", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Point-27700-adapted"] = new("originals/gml32/geom/Point-27700.xml", CorpusDerivationRule.CrsAdapted, "c37b83c066581575ba5d36d3936afc6f871de23af15d3bfc9588480b0ca45bef", new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "0b18b0e2036e7bf7f988a0ec4ec4a9c910af97df0d2ac185590e262c1650f738", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Point-epsg3045-adapted"] = new("originals/gml32/geom/Point-epsg3045.xml", CorpusDerivationRule.CrsAdapted, "122611284abd6340538e4a5ddafa862e73f111a1b4498ec60266e0929772243b", new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "5fcf20ccaef0077f9228ad98784e7f27891f661e72a5d53d312a4295f944cb7c", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Point-srsNameOnPos-adapted"] = new("originals/gml32/geom/Point-srsNameOnPos.xml", CorpusDerivationRule.CrsAdapted, "681688e8f1bb59263614138e43d8ddfe648374a63f749f1d6f0b8b5d7f03467e", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 183)),
        ["gml32-geom-Point-srsNameOnPos-rootcrs-adapted"] = new("originals/gml32/geom/Point-srsNameOnPos.xml", CorpusDerivationRule.RootCrsAdapted, "9db906f3fa18f59d782a32f94d93487e55e66ecb20a2c3298743d5fcd75522e7", new(true, GeometryKind.Point, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "5fcf20ccaef0077f9228ad98784e7f27891f661e72a5d53d312a4295f944cb7c", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Polygon-UTM-adapted"] = new("originals/gml32/geom/Polygon-UTM.xml", CorpusDerivationRule.CrsAdapted, "83bde9789bc6bc884d877119fecaf1a3dc0c898912447d1833e21331ac4ca61e", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "945ab06b3b60f6ecf980cd95acc67eeb8beaaa7d346ed17e83f38a220462580b", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Surface-DiscontiguousPatches-adapted"] = new("originals/gml32/geom/Surface-DiscontiguousPatches.xml", CorpusDerivationRule.CrsAdapted, "50dc3e06ae511b5dc011b984358f71045d4e0110ea7a8a43b1406bf416c8b488", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 221)),
        ["gml32-geom-Surface-PolygonPatch-1-adapted"] = new("originals/gml32/geom/Surface-PolygonPatch-1.xml", CorpusDerivationRule.CrsAdapted, "c1ae971df5885b7399626c287569d0a853a1ad21a08fa40d4eeba86a6cdb1ef1", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "827a7a03de31044e1e4e9459f56ff4bc61a9dc596fafad0e695cd6e614b29bca", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Surface-PolygonPatch-3-adapted"] = new("originals/gml32/geom/Surface-PolygonPatch-3.xml", CorpusDerivationRule.CrsAdapted, "94bca78ce307854c60072a691a84c779162054d7059f57b07f0cdc11173cb5a0", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "9421c0eaab0b76a3d0ee8209663a66832fd2627111f6ca7ef45b51ce5a542a7d", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Surface-PolygonPatch-AxisOrder-adapted"] = new("originals/gml32/geom/Surface-PolygonPatch-AxisOrder.xml", CorpusDerivationRule.CrsAdapted, "984b93c5a6509990cb2706f1d2442c67109c9123976093c2f129e4a560da8e72", new(true, GeometryKind.Polygon, false, "http://www.opengis.net/def/crs/EPSG/0/4326", "74ecec0af05f38aa81e266b519027124079d028ccc557c6b3b0c4c089fb7879c", GeometryCodecRefusalKind.None, -1)),
        ["gml32-geom-Surface-RectangleTriangle-adapted"] = new("originals/gml32/geom/Surface-RectangleTriangle.xml", CorpusDerivationRule.CrsAdapted, "716cb6e8f3e699c911b289d40ba58d523e29227886cb5306ebbcd5fb0aae787f", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnsupportedGeometry, 207)),
        ["gml32-gmlring2-adapted"] = new("originals/gml32/gmlring2.xml", CorpusDerivationRule.CrsAdapted, "719511fc10c4637ce6fba08e7c3473728f61835fb16d065cb74fa8b43f60a60d", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.UnrecognizedCoordinateReferenceSystem, 292)),
        ["gml32-gmlring2-rootcrs-adapted"] = new("originals/gml32/gmlring2.xml", CorpusDerivationRule.RootCrsAdapted, "4b59dca15443444e450ab90df9ae4a0485f7fd1962ab81a8eb98084dcfe1dd25", new(false, GeometryKind.Point, false, "", "", GeometryCodecRefusalKind.StructuralViolation, 415)),
    };
}
