using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo.Dggs;
using Lumoin.Veritas.Geo.Dggs.Core;
using Lumoin.Veritas.Geo.Json;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Spatial;
using Lumoin.Veritas.Geo.Transforms;
using Lumoin.Veritas.Geo.Xml;
using Lumoin.Veritas.Sparql.Execution;

namespace Lumoin.Veritas.Geo;

/// <summary>
/// The GeoSPARQL 1.1 extension-function catalog: the <c>geof:</c> functions computable over the flat
/// Simple Features substrate, each a named <see cref="SparqlFunctionEntry"/> whose implementation is a
/// static method group. Geometry arguments are geometry-valued typed literals read through the catalog's
/// one operand seam: a <c>geo:wktLiteral</c> decomposes its CRS prefix (<see cref="WktCrsPrefix"/>) and
/// parses through <see cref="WktGeometryReader"/>, a <c>geo:gmlLiteral</c>, <c>geo:geoJSONLiteral</c>, or
/// <c>geo:kmlLiteral</c> parses through that format's codec, a DGGS literal materializes through the cell
/// bridge, an empty body denotes the empty geometry, and any lexical, structural,
/// or domain violation — a non-literal argument, a foreign datatype, a malformed body, a wrong argument
/// count, an out-of-range member number, an undefined operation — answers
/// <see cref="SparqlFunctionResult.Error"/>, never an exception and never a fabricated value.
/// </summary>
/// <remarks>
/// <para>
/// <b>The computation model is planar.</b> Every magnitude is computed over the literal's coordinates as
/// given, so answers are magnitudes in the geometry's coordinate units. The specification fixes no
/// computation model for the <c>metric*</c> family and enumerates no units vocabulary; the house model is:
/// an explicit CRS's linear unit is taken to be the metre (correct precisely for metre-based coordinate
/// reference systems, which is the data publisher's responsibility), while the CRS84 default — whose
/// coordinate unit the specification fixes as degree — makes every metre-denominated answer the expression
/// error rather than a degree magnitude mislabelled as metres. The unit-parameterized variants recognize
/// exactly the <see cref="OgcUnitsOfMeasure"/> linear units: the metre argument follows the metre rule
/// above, the degree argument answers the planar magnitude exactly when the CRS is CRS84, and an
/// unrecognized units IRI answers the error value. Area magnitudes read the unit argument as the squared
/// linear unit.
/// </para>
/// <para>
/// <b>Binary geometry functions require one CRS.</b> The catalog never inserts an implicit coordinate
/// transformation, so every two-geometry function — the distance pair, <c>geof:relate</c> and the
/// topological predicates, and the overlay set operations — answers the error value when its operands'
/// resolved CRS IRIs differ. A two-geometry result literal carries an explicit CRS prefix when either
/// operand carried one. <c>geof:transform</c> is the catalog's one explicit transformation point: it
/// re-expresses a geometry over the transform surface's closed certified roster, and its result always
/// carries the explicit target-IRI prefix — a recorded divergence from the carry-the-source-prefix
/// emission every other geometry-producing function uses, because this function's answer names the
/// requested system.
/// </para>
/// <para>
/// <b>The relate family composes collections through union.</b> <c>geof:relate</c> and the twenty-four
/// predicate functions resolve a collection operand to its members' union before the matrix computes, so
/// the relate engine never sees a collection; the empty collection resolves to itself and stays refused.
/// Degenerate but defined geometric results — typed empties, eroded-away buffers, degenerate hulls — are
/// ordinary literals, never errors: only refused operand kinds, malformed arguments, and a detected
/// noding inconsistency answer the error value.
/// </para>
/// <para>
/// <b>Materialized geometries are heap-backed.</b> The catalog parses through the reader's default
/// allocator seam, so disposal of every intermediate geometry is a no-op; serialized results copy the
/// writer's buffer into literal-owned bytes.
/// </para>
/// </remarks>
public static class GeoFunctions
{
    /// <summary>
    /// The arc tessellation of the <c>geof:boundingCircle</c> result polygon: eight segments per
    /// quadrant, the quantum the buffer family's arcs already use, so the two circle-producing
    /// functions answer at one resolution. The substrate answers a centre and a radius; the
    /// polygonization is a seam decision, never a substrate constant.
    /// </summary>
    private const int BoundingCircleQuadrantSegments = 8;

    /// <summary>
    /// The catalog's documented default concaveness ratio for <c>geof:concaveHull</c>: the midpoint
    /// of the closed unit interval the substrate's scale-free edge-length ratio is denominated in.
    /// The specification leaves the concaveness parameter implementation-defined and asks
    /// implementers to document their default; this constant is that default. It sits inside the
    /// interval, so the seam never reaches the malformed-ratio refusal.
    /// </summary>
    private const double DefaultConcaveHullEdgeLengthRatio = 0.5;

    /// <summary>The <c>xsd:boolean</c> datatype node of boolean results.</summary>
    private static NamedNode XsdBooleanDatatype { get; } = new(Vocabulary.Xsd.Boolean);

    /// <summary>The <c>xsd:integer</c> datatype node of integer results.</summary>
    private static NamedNode XsdIntegerDatatype { get; } = new(Vocabulary.Xsd.Integer);

    /// <summary>The <c>xsd:double</c> datatype node of magnitude results.</summary>
    private static NamedNode XsdDoubleDatatype { get; } = new(Vocabulary.Xsd.Double);

    /// <summary>The <c>xsd:anyURI</c> datatype node of IRI-valued results.</summary>
    private static NamedNode XsdAnyUriDatatype { get; } = new(Vocabulary.Xsd.AnyUri);

    /// <summary>The <c>geo:wktLiteral</c> datatype node of geometry results.</summary>
    private static NamedNode WktLiteralDatatype { get; } = new(GeoVocabulary.Geo.WktLiteral);

    /// <summary>The <c>geo:gmlLiteral</c> datatype node of GML serialization results.</summary>
    private static NamedNode GmlLiteralDatatype { get; } = new(GeoVocabulary.Geo.GmlLiteral);

    /// <summary>The <c>geo:geoJSONLiteral</c> datatype node of GeoJSON serialization results.</summary>
    private static NamedNode GeoJsonLiteralDatatype { get; } = new(GeoVocabulary.Geo.GeoJsonLiteral);

    /// <summary>The <c>geo:kmlLiteral</c> datatype node of KML serialization results.</summary>
    private static NamedNode KmlLiteralDatatype { get; } = new(GeoVocabulary.Geo.KmlLiteral);

    /// <summary>The house <c>a5Literal</c> datatype node of DGGS cell-set results.</summary>
    private static NamedNode A5DggsLiteralDatatype { get; } = new(A5DggsVocabulary.DatatypeIri);

    /// <summary>
    /// The canonical CRS IRI of the certified roster's EPSG:4326 member, taken from the serialization
    /// codecs' own roster so an ingested literal's CRS identity and an emitted system declaration are the
    /// same bytes.
    /// </summary>
    private static Utf8String Epsg4326CrsIri { get; } = new(GmlSrsName.CanonicalIriOf(CoordinateReferenceSystem.Epsg4326).ToArray());

    /// <summary>
    /// The canonical CRS IRI of the certified roster's Web Mercator member, taken from the serialization
    /// codecs' own roster so an ingested literal's CRS identity and an emitted system declaration are the
    /// same bytes.
    /// </summary>
    private static Utf8String WebMercatorCrsIri { get; } = new(GmlSrsName.CanonicalIriOf(CoordinateReferenceSystem.WebMercator).ToArray());

    /// <summary>The <c>true</c> lexical form.</summary>
    private static Utf8String TrueLexical { get; } = new("true"u8.ToArray());

    /// <summary>The <c>false</c> lexical form.</summary>
    private static Utf8String FalseLexical { get; } = new("false"u8.ToArray());

    /// <summary><c>geof:asWKT(geom)</c> — the canonical well-known-text serialization of the geometry, with the input's explicit CRS prefix carried and the defaulted CRS staying implicit.</summary>
    public static SparqlFunctionEntry AsWkt { get; } = new(GeoVocabulary.Geof.AsWkt, EvaluateAsWkt);

    /// <summary><c>geof:asDGGS(geom, specificDggsDatatype)</c> — the house A5 cell-set serialization of the geometry, at the resolution the datatype argument's <c>?resolution=</c> query carries; the produced literal embeds the grid IRI of the flavour the argument indicates.</summary>
    public static SparqlFunctionEntry AsDggs { get; } = new(GeoVocabulary.Geof.AsDggs, EvaluateAsDggs);

    /// <summary><c>geof:asGML(geom)</c> — the canonical GML serialization of the geometry in the geometry's own coordinate reference system, whose canonical IRI the result always declares on the root element, the defaulted CRS84 included; a system outside the certified roster answers the error value.</summary>
    public static SparqlFunctionEntry AsGml { get; } = new(GeoVocabulary.Geof.AsGml, EvaluateAsGml);

    /// <summary><c>geof:asGeoJSON(geom)</c> — the canonical GeoJSON serialization of the geometry, re-expressed in CRS84 first because the format fixes that system and carries no declaration; a system outside the certified roster and a refused re-expression both answer the error value.</summary>
    public static SparqlFunctionEntry AsGeoJson { get; } = new(GeoVocabulary.Geof.AsGeoJson, EvaluateAsGeoJson);

    /// <summary><c>geof:asKML(geom)</c> — the canonical KML serialization of the geometry, re-expressed in CRS84 first because the format fixes that system and carries no declaration; a system outside the certified roster, a refused re-expression, and an empty geometry (the format expresses none) each answer the error value.</summary>
    public static SparqlFunctionEntry AsKml { get; } = new(GeoVocabulary.Geof.AsKml, EvaluateAsKml);

    /// <summary><c>geof:area(geom, units)</c> — the planar area in the squared recognized linear unit, under the catalog's unit rules.</summary>
    public static SparqlFunctionEntry Area { get; } = new(GeoVocabulary.Geof.Area, EvaluateArea);

    /// <summary><c>geof:boundary(geom)</c> — the combinatorial boundary as a geometry literal; the boundary of a heterogeneous collection is undefined and errs.</summary>
    public static SparqlFunctionEntry Boundary { get; } = new(GeoVocabulary.Geof.Boundary, EvaluateBoundary);

    /// <summary><c>geof:coordinateDimension(geom)</c> — the ordinate count per position: 2, plus one for a carried Z, plus one for a carried M.</summary>
    public static SparqlFunctionEntry CoordinateDimension { get; } = new(GeoVocabulary.Geof.CoordinateDimension, EvaluateCoordinateDimension);

    /// <summary><c>geof:dimension(geom)</c> — the topological dimension; typed empties keep their kind's dimension and the memberless collection answers −1.</summary>
    public static SparqlFunctionEntry Dimension { get; } = new(GeoVocabulary.Geof.Dimension, EvaluateDimension);

    /// <summary><c>geof:distance(geom1, geom2, units)</c> — the planar point-set distance in the recognized linear unit; empty operands and differing CRS IRIs err.</summary>
    public static SparqlFunctionEntry Distance { get; } = new(GeoVocabulary.Geof.Distance, EvaluateDistance);

    /// <summary><c>geof:envelope(geom)</c> — the axis-aligned envelope as a geometry literal with the conventional degenerate collapse (empty input answers the empty point).</summary>
    public static SparqlFunctionEntry Envelope { get; } = new(GeoVocabulary.Geof.Envelope, EvaluateEnvelope);

    /// <summary><c>geof:geometryN(geom, n)</c> — the one-based n-th member: a collection's child, a multi kind's element, an atomic geometry itself at n = 1; out of range errs.</summary>
    public static SparqlFunctionEntry GeometryN { get; } = new(GeoVocabulary.Geof.GeometryN, EvaluateGeometryN);

    /// <summary><c>geof:geometryType(geom)</c> — the Simple Features class IRI of the root tagged kind, as <c>xsd:anyURI</c>; the non-SF surface tags normalize on read, so a triangle answers the polygon class.</summary>
    public static SparqlFunctionEntry GeometryType { get; } = new(GeoVocabulary.Geof.GeometryType, EvaluateGeometryType);

    /// <summary><c>geof:getSRID(geom)</c> — the geometry's coordinate reference system IRI as <c>xsd:anyURI</c>; a literal with no prefix answers the CRS84 default.</summary>
    public static SparqlFunctionEntry GetSrid { get; } = new(GeoVocabulary.Geof.GetSrid, EvaluateGetSrid);

    /// <summary><c>geof:is3D(geom)</c> — whether any position carries a Z ordinate.</summary>
    public static SparqlFunctionEntry Is3D { get; } = new(GeoVocabulary.Geof.Is3D, EvaluateIs3D);

    /// <summary><c>geof:isEmpty(geom)</c> — whether the geometry is the empty point set.</summary>
    public static SparqlFunctionEntry IsEmpty { get; } = new(GeoVocabulary.Geof.IsEmpty, EvaluateIsEmpty);

    /// <summary><c>geof:isMeasured(geom)</c> — whether any position carries an M ordinate.</summary>
    public static SparqlFunctionEntry IsMeasured { get; } = new(GeoVocabulary.Geof.IsMeasured, EvaluateIsMeasured);

    /// <summary><c>geof:length(geom, units)</c> — the planar length of the lineal parts in the recognized linear unit; polygonal rings answer through <see cref="Perimeter"/>, not here.</summary>
    public static SparqlFunctionEntry Length { get; } = new(GeoVocabulary.Geof.Length, EvaluateLength);

    /// <summary><c>geof:maxX(geom)</c> — the maximum X coordinate; the empty point set errs.</summary>
    public static SparqlFunctionEntry MaxX { get; } = new(GeoVocabulary.Geof.MaxX, EvaluateMaxX);

    /// <summary><c>geof:maxY(geom)</c> — the maximum Y coordinate; the empty point set errs.</summary>
    public static SparqlFunctionEntry MaxY { get; } = new(GeoVocabulary.Geof.MaxY, EvaluateMaxY);

    /// <summary><c>geof:maxZ(geom)</c> — the maximum carried Z ordinate; a geometry carrying no Z errs.</summary>
    public static SparqlFunctionEntry MaxZ { get; } = new(GeoVocabulary.Geof.MaxZ, EvaluateMaxZ);

    /// <summary><c>geof:metricArea(geom)</c> — the planar area in square metres under the catalog's metre rule; the CRS84 default errs.</summary>
    public static SparqlFunctionEntry MetricArea { get; } = new(GeoVocabulary.Geof.MetricArea, EvaluateMetricArea);

    /// <summary><c>geof:metricDistance(geom1, geom2)</c> — the planar point-set distance in metres under the catalog's metre rule; empty operands, differing CRS IRIs, and the CRS84 default err.</summary>
    public static SparqlFunctionEntry MetricDistance { get; } = new(GeoVocabulary.Geof.MetricDistance, EvaluateMetricDistance);

    /// <summary><c>geof:metricLength(geom)</c> — the planar lineal length in metres under the catalog's metre rule; the CRS84 default errs.</summary>
    public static SparqlFunctionEntry MetricLength { get; } = new(GeoVocabulary.Geof.MetricLength, EvaluateMetricLength);

    /// <summary><c>geof:metricPerimeter(geom)</c> — the planar polygonal perimeter in metres under the catalog's metre rule; the CRS84 default errs.</summary>
    public static SparqlFunctionEntry MetricPerimeter { get; } = new(GeoVocabulary.Geof.MetricPerimeter, EvaluateMetricPerimeter);

    /// <summary><c>geof:minX(geom)</c> — the minimum X coordinate; the empty point set errs.</summary>
    public static SparqlFunctionEntry MinX { get; } = new(GeoVocabulary.Geof.MinX, EvaluateMinX);

    /// <summary><c>geof:minY(geom)</c> — the minimum Y coordinate; the empty point set errs.</summary>
    public static SparqlFunctionEntry MinY { get; } = new(GeoVocabulary.Geof.MinY, EvaluateMinY);

    /// <summary><c>geof:minZ(geom)</c> — the minimum carried Z ordinate; a geometry carrying no Z errs.</summary>
    public static SparqlFunctionEntry MinZ { get; } = new(GeoVocabulary.Geof.MinZ, EvaluateMinZ);

    /// <summary><c>geof:numGeometries(geom)</c> — the member count under the SQL/MM convention: children for a collection, elements for a multi kind, one for an atomic geometry.</summary>
    public static SparqlFunctionEntry NumGeometries { get; } = new(GeoVocabulary.Geof.NumGeometries, EvaluateNumGeometries);

    /// <summary><c>geof:perimeter(geom, units)</c> — the planar polygonal ring length, shells and holes alike, in the recognized linear unit.</summary>
    public static SparqlFunctionEntry Perimeter { get; } = new(GeoVocabulary.Geof.Perimeter, EvaluatePerimeter);

    /// <summary><c>geof:spatialDimension(geom)</c> — the spatial axis count per position: 2, plus one for a carried Z.</summary>
    public static SparqlFunctionEntry SpatialDimension { get; } = new(GeoVocabulary.Geof.SpatialDimension, EvaluateSpatialDimension);

    /// <summary><c>geof:buffer(geom, radius, units)</c> — the buffer polygon at the radius read in the recognized linear unit and converted to coordinate units before the offset computes; the result is always polygonal, an eroded-away buffer is the ordinary empty polygon, and a non-finite radius errs.</summary>
    public static SparqlFunctionEntry Buffer { get; } = new(GeoVocabulary.Geof.Buffer, EvaluateBuffer);

    /// <summary><c>geof:boundingCircle(geom)</c> — the smallest enclosing circle as a geometry literal: the answered polygon circumscribes the certified minimum bounding circle with coverage verified per emission, so every operand point lies inside or on it, and the polygon exceeds the circle by at most the secant of half the tessellation step minus one — under half a percent of the radius; a coincident-position operand answers the centre point, an empty operand the empty point, and an unverifiable rendering the error value.</summary>
    public static SparqlFunctionEntry BoundingCircle { get; } = new(GeoVocabulary.Geof.BoundingCircle, EvaluateBoundingCircle);

    /// <summary><c>geof:centroid(geom)</c> — the effective-dimension centroid as a geometry literal; total over every operand kind, with an empty operand collapsing to the empty point.</summary>
    public static SparqlFunctionEntry Centroid { get; } = new(GeoVocabulary.Geof.Centroid, EvaluateCentroid);

    /// <summary><c>geof:concaveHull(geom)</c> — the chi-shape concave hull as a geometry literal at the catalog's documented default concaveness ratio; total over every operand kind, with an empty operand collapsing to the empty point through the convex-hull delegation.</summary>
    public static SparqlFunctionEntry ConcaveHull { get; } = new(GeoVocabulary.Geof.ConcaveHull, EvaluateConcaveHull);

    /// <summary><c>geof:convexHull(geom)</c> — the convex hull as a geometry literal; total over every operand kind, with degenerate operands collapsing to point or linestring results.</summary>
    public static SparqlFunctionEntry ConvexHull { get; } = new(GeoVocabulary.Geof.ConvexHull, EvaluateConvexHull);

    /// <summary><c>geof:difference(geom1, geom2)</c> — the point-set difference as a geometry literal; collection operands err, and the empty result is the typed empty of the first operand's dimension.</summary>
    public static SparqlFunctionEntry Difference { get; } = new(GeoVocabulary.Geof.Difference, EvaluateDifference);

    /// <summary><c>geof:intersection(geom1, geom2)</c> — the point-set intersection as a geometry literal; collection operands err, and the empty result is the typed empty of the minimum operand dimension.</summary>
    public static SparqlFunctionEntry Intersection { get; } = new(GeoVocabulary.Geof.Intersection, EvaluateIntersection);

    /// <summary><c>geof:isSimple(geom)</c> — whether the geometry is simple per its kind; total, with collections answering the member conjunction.</summary>
    public static SparqlFunctionEntry IsSimple { get; } = new(GeoVocabulary.Geof.IsSimple, EvaluateIsSimple);

    /// <summary><c>geof:metricBuffer(geom, radius)</c> — the buffer polygon at the radius in metres under the catalog's metre rule; the CRS84 default errs.</summary>
    public static SparqlFunctionEntry MetricBuffer { get; } = new(GeoVocabulary.Geof.MetricBuffer, EvaluateMetricBuffer);

    /// <summary><c>geof:relate(geom1, geom2, patternMatrix)</c> — whether the computed DE-9IM matrix matches the nine-symbol pattern; a malformed pattern errs.</summary>
    public static SparqlFunctionEntry Relate { get; } = new(GeoVocabulary.Geof.Relate, EvaluateRelate);

    /// <summary><c>geof:symDifference(geom1, geom2)</c> — the symmetric point-set difference as a geometry literal; collection operands err, and the empty result is the typed empty of the maximum operand dimension.</summary>
    public static SparqlFunctionEntry SymDifference { get; } = new(GeoVocabulary.Geof.SymDifference, EvaluateSymDifference);

    /// <summary><c>geof:transform(geom, srsIRI)</c> — the geometry re-expressed in the target coordinate reference system, over the transform surface's closed certified roster; a system outside the roster on either side, a 3D or measured operand, or a coordinate the pair's validation refuses answers the error value.</summary>
    public static SparqlFunctionEntry Transform { get; } = new(GeoVocabulary.Geof.Transform, EvaluateTransform);

    /// <summary><c>geof:union(geom1, geom2)</c> — the point-set union as a geometry literal; collections are accepted in either position through the stratified member fold.</summary>
    public static SparqlFunctionEntry Union { get; } = new(GeoVocabulary.Geof.Union, EvaluateUnion);

    /// <summary><c>geof:sfEquals(geom1, geom2)</c> — the Simple Features equals predicate: mutual within, so coincident points equate.</summary>
    public static SparqlFunctionEntry SfEquals { get; } = new(GeoVocabulary.Geof.SfEquals, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfDisjoint(geom1, geom2)</c> — the Simple Features disjoint predicate.</summary>
    public static SparqlFunctionEntry SfDisjoint { get; } = new(GeoVocabulary.Geof.SfDisjoint, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfIntersects(geom1, geom2)</c> — the Simple Features intersects predicate.</summary>
    public static SparqlFunctionEntry SfIntersects { get; } = new(GeoVocabulary.Geof.SfIntersects, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfTouches(geom1, geom2)</c> — the Simple Features touches predicate: boundary contact without interior intersection.</summary>
    public static SparqlFunctionEntry SfTouches { get; } = new(GeoVocabulary.Geof.SfTouches, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfCrosses(geom1, geom2)</c> — the Simple Features crosses predicate, dimension-branched with kind-intrinsic gates.</summary>
    public static SparqlFunctionEntry SfCrosses { get; } = new(GeoVocabulary.Geof.SfCrosses, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfWithin(geom1, geom2)</c> — the Simple Features within predicate.</summary>
    public static SparqlFunctionEntry SfWithin { get; } = new(GeoVocabulary.Geof.SfWithin, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfContains(geom1, geom2)</c> — the Simple Features contains predicate.</summary>
    public static SparqlFunctionEntry SfContains { get; } = new(GeoVocabulary.Geof.SfContains, EvaluateTopologicalPredicate);

    /// <summary><c>geof:sfOverlaps(geom1, geom2)</c> — the Simple Features overlaps predicate, gated on equal dimensions with a same-dimension line intersection.</summary>
    public static SparqlFunctionEntry SfOverlaps { get; } = new(GeoVocabulary.Geof.SfOverlaps, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehEquals(geom1, geom2)</c> — the Egenhofer equals predicate over its literal pattern, so coincident points answer false.</summary>
    public static SparqlFunctionEntry EhEquals { get; } = new(GeoVocabulary.Geof.EhEquals, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehDisjoint(geom1, geom2)</c> — the Egenhofer disjoint predicate.</summary>
    public static SparqlFunctionEntry EhDisjoint { get; } = new(GeoVocabulary.Geof.EhDisjoint, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehMeet(geom1, geom2)</c> — the Egenhofer meet predicate.</summary>
    public static SparqlFunctionEntry EhMeet { get; } = new(GeoVocabulary.Geof.EhMeet, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehOverlap(geom1, geom2)</c> — the Egenhofer overlap predicate over its bare pattern, so crossing lines overlap.</summary>
    public static SparqlFunctionEntry EhOverlap { get; } = new(GeoVocabulary.Geof.EhOverlap, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehCovers(geom1, geom2)</c> — the Egenhofer covers predicate.</summary>
    public static SparqlFunctionEntry EhCovers { get; } = new(GeoVocabulary.Geof.EhCovers, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehCoveredBy(geom1, geom2)</c> — the Egenhofer covered-by predicate.</summary>
    public static SparqlFunctionEntry EhCoveredBy { get; } = new(GeoVocabulary.Geof.EhCoveredBy, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehInside(geom1, geom2)</c> — the Egenhofer inside predicate.</summary>
    public static SparqlFunctionEntry EhInside { get; } = new(GeoVocabulary.Geof.EhInside, EvaluateTopologicalPredicate);

    /// <summary><c>geof:ehContains(geom1, geom2)</c> — the Egenhofer contains predicate.</summary>
    public static SparqlFunctionEntry EhContains { get; } = new(GeoVocabulary.Geof.EhContains, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8eq(geom1, geom2)</c> — the RCC8 equals predicate over its literal pattern.</summary>
    public static SparqlFunctionEntry Rcc8Eq { get; } = new(GeoVocabulary.Geof.Rcc8Eq, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8dc(geom1, geom2)</c> — the RCC8 disconnected predicate.</summary>
    public static SparqlFunctionEntry Rcc8Dc { get; } = new(GeoVocabulary.Geof.Rcc8Dc, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8ec(geom1, geom2)</c> — the RCC8 externally-connected predicate.</summary>
    public static SparqlFunctionEntry Rcc8Ec { get; } = new(GeoVocabulary.Geof.Rcc8Ec, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8po(geom1, geom2)</c> — the RCC8 partially-overlapping predicate.</summary>
    public static SparqlFunctionEntry Rcc8Po { get; } = new(GeoVocabulary.Geof.Rcc8Po, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8tppi(geom1, geom2)</c> — the RCC8 tangential-proper-part-inverse predicate.</summary>
    public static SparqlFunctionEntry Rcc8Tppi { get; } = new(GeoVocabulary.Geof.Rcc8Tppi, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8tpp(geom1, geom2)</c> — the RCC8 tangential-proper-part predicate.</summary>
    public static SparqlFunctionEntry Rcc8Tpp { get; } = new(GeoVocabulary.Geof.Rcc8Tpp, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8ntpp(geom1, geom2)</c> — the RCC8 non-tangential-proper-part predicate.</summary>
    public static SparqlFunctionEntry Rcc8Ntpp { get; } = new(GeoVocabulary.Geof.Rcc8Ntpp, EvaluateTopologicalPredicate);

    /// <summary><c>geof:rcc8ntppi(geom1, geom2)</c> — the RCC8 non-tangential-proper-part-inverse predicate.</summary>
    public static SparqlFunctionEntry Rcc8Ntppi { get; } = new(GeoVocabulary.Geof.Rcc8Ntppi, EvaluateTopologicalPredicate);

    /// <summary><c>geof:aggBoundingBox(geoms)</c> — the aggregate axis-aligned envelope of a group's geometries, computed over their combined extent, with the group's CRS carriage; the empty group errs.</summary>
    public static SparqlFunctionEntry AggBoundingBox { get; } = new(GeoVocabulary.Geof.AggBoundingBox, Scalar: null, Aggregate: EvaluateAggBoundingBox);

    /// <summary><c>geof:aggBoundingCircle(geoms)</c> — the aggregate minimum bounding circle of a group's geometries, rendered as the certified circumscribed polygon with coverage verified per emission so the answer contains every operand point; the empty group and an unverifiable rendering err.</summary>
    public static SparqlFunctionEntry AggBoundingCircle { get; } = new(GeoVocabulary.Geof.AggBoundingCircle, Scalar: null, Aggregate: EvaluateAggBoundingCircle);

    /// <summary><c>geof:aggCentroid(geoms)</c> — the aggregate effective-dimension centroid of a group's geometries taken together; the empty group errs.</summary>
    public static SparqlFunctionEntry AggCentroid { get; } = new(GeoVocabulary.Geof.AggCentroid, Scalar: null, Aggregate: EvaluateAggCentroid);

    /// <summary><c>geof:aggConcaveHull(geoms)</c> — the aggregate chi-shape concave hull over a group's combined points at the catalog's documented default concaveness ratio; the empty group errs.</summary>
    public static SparqlFunctionEntry AggConcaveHull { get; } = new(GeoVocabulary.Geof.AggConcaveHull, Scalar: null, Aggregate: EvaluateAggConcaveHull);

    /// <summary><c>geof:aggConvexHull(geoms)</c> — the aggregate convex hull over a group's combined points; the empty group errs.</summary>
    public static SparqlFunctionEntry AggConvexHull { get; } = new(GeoVocabulary.Geof.AggConvexHull, Scalar: null, Aggregate: EvaluateAggConvexHull);

    /// <summary><c>geof:aggUnion(geoms)</c> — the aggregate set union of a group's geometries, folded pairwise in member order under the overlay engine; the empty group errs.</summary>
    public static SparqlFunctionEntry AggUnion { get; } = new(GeoVocabulary.Geof.AggUnion, Scalar: null, Aggregate: EvaluateAggUnion);

    /// <summary>Every entry of the catalog, in one list for bulk registration.</summary>
    public static IReadOnlyList<SparqlFunctionEntry> All { get; } =
    [
        AsWkt,
        AsDggs,
        AsGml,
        AsGeoJson,
        AsKml,
        Area,
        Boundary,
        CoordinateDimension,
        Dimension,
        Distance,
        Envelope,
        GeometryN,
        GeometryType,
        GetSrid,
        Is3D,
        IsEmpty,
        IsMeasured,
        Length,
        MaxX,
        MaxY,
        MaxZ,
        MetricArea,
        MetricDistance,
        MetricLength,
        MetricPerimeter,
        MinX,
        MinY,
        MinZ,
        NumGeometries,
        Perimeter,
        SpatialDimension,
        BoundingCircle,
        Buffer,
        Centroid,
        ConcaveHull,
        ConvexHull,
        Difference,
        Intersection,
        IsSimple,
        MetricBuffer,
        Relate,
        SymDifference,
        Transform,
        Union,
        SfEquals,
        SfDisjoint,
        SfIntersects,
        SfTouches,
        SfCrosses,
        SfWithin,
        SfContains,
        SfOverlaps,
        EhEquals,
        EhDisjoint,
        EhMeet,
        EhOverlap,
        EhCovers,
        EhCoveredBy,
        EhInside,
        EhContains,
        Rcc8Eq,
        Rcc8Dc,
        Rcc8Ec,
        Rcc8Po,
        Rcc8Tppi,
        Rcc8Tpp,
        Rcc8Ntpp,
        Rcc8Ntppi,
        AggBoundingBox,
        AggBoundingCircle,
        AggCentroid,
        AggConcaveHull,
        AggConvexHull,
        AggUnion
    ];

    /// <summary>Evaluates <c>geof:asWKT</c>: the canonical serialization with the input's CRS carriage.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The canonical <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAsWkt(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        return WktResult(in geometry, crsIri, crsSource);
    }

    /// <summary>
    /// Evaluates <c>geof:asGML</c>: the geometry serialized in the coordinate reference system it already
    /// carries, never re-expressed. The result declares that system's canonical IRI on the root element in
    /// every case, so a geometry whose literal left the system implicit answers a document naming CRS84
    /// outright; the coordinates are written in the system's declared axis order, exactly as the geometry
    /// carries them. Because the format's system declaration is closed to the certified roster, a geometry
    /// whose resolved system lies outside that roster answers the error value before anything is written,
    /// and so does a measured geometry, which no serialization format carries.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The <c>geo:gmlLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAsGml(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1
            || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _)
            || !TryRecognizeRosterMember(crsIri, out CoordinateReferenceSystem system))
        {
            return SparqlFunctionResult.Error;
        }

        var buffer = new ArrayBufferWriter<byte>();

        return GmlGeometryWriter.TryWrite(in geometry, system, buffer, out _)
            ? SparqlFunctionResult.Of(new Literal(new Utf8String(buffer.WrittenSpan.ToArray()), GmlLiteralDatatype))
            : SparqlFunctionResult.Error;
    }

    /// <summary>
    /// Evaluates <c>geof:asGeoJSON</c>: the geometry re-expressed in CRS84 and then serialized, because
    /// the format defines its coordinates in that system and carries no declaration of its own. A geometry
    /// already in CRS84 is written as it stands; one in another certified system is converted first, and a
    /// refused conversion answers the error value rather than a clamped or wrapped coordinate. A system
    /// outside the certified roster answers the error value, as does a measured geometry, which no
    /// serialization format carries.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The <c>geo:geoJSONLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAsGeoJson(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateCrs84Serialization(arguments, GeoJsonGeometryWriter.TryWrite, GeoJsonLiteralDatatype);
    }

    /// <summary>
    /// Evaluates <c>geof:asKML</c>: the CRS84 composition of <see cref="EvaluateAsGeoJson"/> over the KML
    /// writer, the format fixing the same system and likewise carrying no declaration. A carried Z ordinate
    /// is written as the format's absolute altitude; the format expresses no empty geometry, so an empty
    /// operand answers the error value rather than a fabricated document.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The <c>geo:kmlLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAsKml(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateCrs84Serialization(arguments, KmlGeometryWriter.TryWrite, KmlLiteralDatatype);
    }

    /// <summary>
    /// The emission shape shared by the serialization formats that fix their coordinate reference system:
    /// write the geometry or refuse by value, leaving the destination untouched.
    /// </summary>
    /// <param name="geometry">The geometry to serialize, already expressed in the format's system.</param>
    /// <param name="destination">The UTF-8 destination.</param>
    /// <param name="refusal">The refusal on failure.</param>
    /// <returns><see langword="true"/> when the geometry was written.</returns>
    private delegate bool Crs84SerializationWriter(in FlatGeometry geometry, IBufferWriter<byte> destination, out GeometryCodecRefusal refusal);

    /// <summary>
    /// Shared evaluation of the serializations whose format fixes CRS84: the operand is re-expressed in
    /// CRS84 first and the writer then emits the format's canonical text. A resolved system outside the
    /// certified roster, a refused re-expression, and a writer refusal each answer the error value, so no
    /// answer ever drops an ordinate or a system the operand carried.
    /// </summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="write">The format's writer.</param>
    /// <param name="datatype">The datatype node of the produced literal.</param>
    /// <returns>The typed serialization literal, or the error value.</returns>
    private static SparqlFunctionResult EvaluateCrs84Serialization(ReadOnlySpan<RdfTerm> arguments, Crs84SerializationWriter write, NamedNode datatype)
    {
        if(arguments.Length != 1
            || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _)
            || !TryExpressInCrs84(in geometry, crsIri, out FlatGeometry crs84Geometry))
        {
            return SparqlFunctionResult.Error;
        }

        var buffer = new ArrayBufferWriter<byte>();

        return write(in crs84Geometry, buffer, out _)
            ? SparqlFunctionResult.Of(new Literal(new Utf8String(buffer.WrittenSpan.ToArray()), datatype))
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:area</c>: the planar area under the unit rules, read as the squared linear unit.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The area as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateArea(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(GeometryMeasures.Area(in geometry), crsIri, ClassifyUnits(arguments[1]), out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:boundary</c>: the combinatorial boundary; undefined for heterogeneous collections.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The boundary as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateBoundary(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        return GeometryBoundary.TryCompute(in geometry, out FlatGeometry boundary)
            ? WktResult(in boundary, crsIri, crsSource)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:coordinateDimension</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The ordinate count as <c>xsd:integer</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateCoordinateDimension(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? IntegerResult(geometry.CoordinateDimension)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:dimension</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The topological dimension as <c>xsd:integer</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateDimension(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? IntegerResult(geometry.TopologicalDimension)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:distance</c>: the planar point-set distance under the one-CRS gate and the unit rules.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The distance as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateDistance(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 3
            || !TryReadOperand(arguments[0], out FlatGeometry first, out Utf8String firstCrs, out _)
            || !TryReadOperand(arguments[1], out FlatGeometry second, out Utf8String secondCrs, out _)
            || !firstCrs.Span.SequenceEqual(secondCrs.Span)
            || !GeometryDistance.TryCompute(in first, in second, out double distance))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(distance, firstCrs, ClassifyUnits(arguments[2]), out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:envelope</c>: the axis-aligned envelope geometry with the degenerate collapse.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The envelope as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateEnvelope(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry envelope = GeometryEnvelope.ComputeEnvelopeGeometry(in geometry);

        return WktResult(in envelope, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:geometryN</c>: the one-based member extraction.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The member as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateGeometryN(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2
            || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource)
            || !TryReadMemberNumber(arguments[1], out int memberNumber)
            || !GeometryMemberAccess.TryExtractMember(in geometry, memberNumber, out FlatGeometry member))
        {
            return SparqlFunctionResult.Error;
        }

        return WktResult(in member, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:geometryType</c>: the Simple Features class IRI of the root kind.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The class IRI as <c>xsd:anyURI</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateGeometryType(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _))
        {
            return SparqlFunctionResult.Error;
        }

        Utf8String sfClass = geometry.Kind switch
        {
            GeometryKind.Point => GeoVocabulary.Sf.Point,
            GeometryKind.LineString => GeoVocabulary.Sf.LineString,
            GeometryKind.Polygon => GeoVocabulary.Sf.Polygon,
            GeometryKind.MultiPoint => GeoVocabulary.Sf.MultiPoint,
            GeometryKind.MultiLineString => GeoVocabulary.Sf.MultiLineString,
            GeometryKind.MultiPolygon => GeoVocabulary.Sf.MultiPolygon,
            _ => GeoVocabulary.Sf.GeometryCollection,
        };

        return SparqlFunctionResult.Of(new Literal(sfClass, XsdAnyUriDatatype));
    }

    /// <summary>Evaluates <c>geof:getSRID</c>: the resolved CRS IRI, defaulted or explicit.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The CRS IRI as <c>xsd:anyURI</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateGetSrid(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out _, out Utf8String crsIri, out _)
            ? SparqlFunctionResult.Of(new Literal(crsIri, XsdAnyUriDatatype))
            : SparqlFunctionResult.Error;
    }

    /// <summary>
    /// Evaluates <c>geof:asDGGS</c>: the house A5 cell-set serialization of the geometry at the
    /// resolution the datatype argument carries. The operand must resolve to the default CRS or an
    /// explicit CRS84; a geometry covering no cell at the resolution — the empty geometry included —
    /// answers the empty <c>a5Literal</c>.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The <c>a5Literal</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAsDggs(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2
            || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource)
            || !TryReadA5ResolutionTarget(arguments[1], out int resolution))
        {
            return SparqlFunctionResult.Error;
        }

        if(crsSource == WktCrsSource.Explicit && !crsIri.Span.SequenceEqual(WktCrsPrefix.DefaultCrsIri.Span))
        {
            return SparqlFunctionResult.Error;
        }

        if(geometry.IsEmpty)
        {
            return SparqlFunctionResult.Of(new Literal(new Utf8String(Array.Empty<byte>()), A5DggsLiteralDatatype));
        }

        List<A5CellId> cells = [];
        if(!A5CellGeometry.TryConvertGeometry(in geometry, resolution, cells))
        {
            return SparqlFunctionResult.Error;
        }

        A5DggsBody.CanonicalizeSet(cells, 0);

        return SparqlFunctionResult.Of(new Literal(new Utf8String(A5DggsBody.WriteLiteral(CollectionsMarshal.AsSpan(cells))), A5DggsLiteralDatatype));
    }

    /// <summary>
    /// Reads the <c>geof:asDGGS</c> datatype argument: an IRI term or an <c>xsd:anyURI</c> literal
    /// whose bytes are exactly the house <c>a5Literal</c> datatype IRI followed by
    /// <c>?resolution=</c> and a decimal value 0 through 30 with no leading zero. Anything else — a
    /// bare datatype IRI (the specification's signature carries no resolution parameter, and no
    /// default is fabricated), a foreign datatype IRI, extra query content, a fragment, or an
    /// out-of-range value — is unreadable.
    /// </summary>
    /// <param name="term">The datatype argument term.</param>
    /// <param name="resolution">The parsed target resolution.</param>
    /// <returns><see langword="true"/> when the argument is readable.</returns>
    private static bool TryReadA5ResolutionTarget(RdfTerm term, out int resolution)
    {
        resolution = 0;
        Utf8String iri = term switch
        {
            NamedNode named => named.Iri,
            Literal literal when literal.Datatype.Iri.Span.SequenceEqual(Vocabulary.Xsd.AnyUri.Span) => literal.Value,
            _ => default,
        };

        ReadOnlySpan<byte> span = iri.Span;
        ReadOnlySpan<byte> datatypePrefix = A5DggsVocabulary.DatatypeIri.Span;
        if(span.Length <= datatypePrefix.Length || !span[..datatypePrefix.Length].SequenceEqual(datatypePrefix))
        {
            return false;
        }

        ReadOnlySpan<byte> query = span[datatypePrefix.Length..];
        ReadOnlySpan<byte> queryPrefix = A5DggsVocabulary.ResolutionQueryPrefix.Span;
        if(query.Length <= queryPrefix.Length || !query[..queryPrefix.Length].SequenceEqual(queryPrefix))
        {
            return false;
        }

        ReadOnlySpan<byte> digits = query[queryPrefix.Length..];
        if(digits.Length is 0 or > 2 || (digits.Length == 2 && digits[0] == (byte)'0'))
        {
            return false;
        }

        int value = 0;
        foreach(byte digit in digits)
        {
            if(digit is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            value = (value * 10) + (digit - (byte)'0');
        }

        if(value > A5.MaxResolution)
        {
            return false;
        }

        resolution = value;

        return true;
    }

    /// <summary>Evaluates <c>geof:is3D</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The flag as <c>xsd:boolean</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateIs3D(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? BooleanResult(geometry.Is3D)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:isEmpty</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The flag as <c>xsd:boolean</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateIsEmpty(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? BooleanResult(geometry.IsEmpty)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:isMeasured</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The flag as <c>xsd:boolean</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateIsMeasured(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? BooleanResult(geometry.IsMeasured)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:length</c>: the lineal segment-length sum under the unit rules.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The length as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateLength(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(SegmentSum(in geometry, polygonal: false), crsIri, ClassifyUnits(arguments[1]), out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:maxX</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The coordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMaxX(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateBoundsCoordinate(arguments, maximum: true, axisY: false);
    }

    /// <summary>Evaluates <c>geof:maxY</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The coordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMaxY(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateBoundsCoordinate(arguments, maximum: true, axisY: true);
    }

    /// <summary>Evaluates <c>geof:maxZ</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The ordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMaxZ(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateBoundsOrdinate(arguments, maximum: true);
    }

    /// <summary>Evaluates <c>geof:metricArea</c>: the planar area under the metre rule.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The area as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMetricArea(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(GeometryMeasures.Area(in geometry), crsIri, RecognizedUnit.Metre, out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:metricDistance</c>: the planar point-set distance under the one-CRS gate and the metre rule.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The distance as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMetricDistance(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2
            || !TryReadOperand(arguments[0], out FlatGeometry first, out Utf8String firstCrs, out _)
            || !TryReadOperand(arguments[1], out FlatGeometry second, out Utf8String secondCrs, out _)
            || !firstCrs.Span.SequenceEqual(secondCrs.Span)
            || !GeometryDistance.TryCompute(in first, in second, out double distance))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(distance, firstCrs, RecognizedUnit.Metre, out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:metricLength</c>: the lineal segment-length sum under the metre rule.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The length as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMetricLength(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(SegmentSum(in geometry, polygonal: false), crsIri, RecognizedUnit.Metre, out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:metricPerimeter</c>: the polygonal ring-length sum under the metre rule.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The perimeter as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMetricPerimeter(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(SegmentSum(in geometry, polygonal: true), crsIri, RecognizedUnit.Metre, out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:minX</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The coordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMinX(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateBoundsCoordinate(arguments, maximum: false, axisY: false);
    }

    /// <summary>Evaluates <c>geof:minY</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The coordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMinY(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateBoundsCoordinate(arguments, maximum: false, axisY: true);
    }

    /// <summary>Evaluates <c>geof:minZ</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The ordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMinZ(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateBoundsOrdinate(arguments, maximum: false);
    }

    /// <summary>Evaluates <c>geof:numGeometries</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The member count as <c>xsd:integer</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateNumGeometries(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? IntegerResult(GeometryMemberAccess.CountMembers(in geometry))
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:perimeter</c>: the polygonal ring-length sum under the unit rules.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The perimeter as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluatePerimeter(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _))
        {
            return SparqlFunctionResult.Error;
        }

        return TryConvertMagnitude(SegmentSum(in geometry, polygonal: true), crsIri, ClassifyUnits(arguments[1]), out double magnitude)
            ? DoubleResult(magnitude)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:spatialDimension</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The spatial axis count as <c>xsd:integer</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateSpatialDimension(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? IntegerResult(geometry.SpatialDimension)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:buffer</c>: the buffer polygon with the radius converted from the recognized unit to coordinate units.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The buffer as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateBuffer(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 3
            ? EvaluateBufferDistance(arguments, ClassifyUnits(arguments[2]))
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:metricBuffer</c>: the buffer polygon with the radius read in metres under the metre rule.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The buffer as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateMetricBuffer(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 2
            ? EvaluateBufferDistance(arguments, RecognizedUnit.Metre)
            : SparqlFunctionResult.Error;
    }

    /// <summary>
    /// Shared evaluation of the buffer pair: the radius argument converts from the requested unit into a
    /// plain coordinate-unit distance under the catalog's unit rules — the substrate itself never sees a
    /// unit — and the offset computes at the default arc tessellation.
    /// </summary>
    /// <param name="arguments">The evaluated arguments; the geometry first, the radius second.</param>
    /// <param name="unit">The unit the radius is denominated in.</param>
    /// <returns>The buffer as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateBufferDistance(ReadOnlySpan<RdfTerm> arguments, RecognizedUnit unit)
    {
        if(!TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource)
            || !TryReadNumericArgument(arguments[1], out double radius)
            || !TryConvertMagnitude(radius, crsIri, unit, out double coordinateDistance)
            || !GeometryBuffer.TryCompute(in geometry, coordinateDistance, out FlatGeometry buffered))
        {
            return SparqlFunctionResult.Error;
        }

        return WktResult(in buffered, crsIri, crsSource);
    }

    /// <summary>
    /// Evaluates <c>geof:boundingCircle</c>: the smallest enclosing circle of the operand's point set,
    /// rendered as a geometry literal with the input's CRS carriage. The substrate answers the certified
    /// circle as a centre and a radius in coordinate units, so the polygonization is this seam's own
    /// decision: a positive radius answers the certified circumscribing polygon at
    /// <see cref="BoundingCircleQuadrantSegments"/> through <see cref="CircumscribedCirclePolygon"/> —
    /// coverage of the whole disc, and therefore of every operand point, is verified per emission, an
    /// unverifiable emission answers the error value, and the polygon exceeds the circle by at most the
    /// secant of half the tessellation step minus one, under half a percent of the radius. A zero radius
    /// answers the centre point, an operand with no positions answers the empty point under the envelope
    /// family's collapse, and an operand outside the certified predicates' documented magnitude walls
    /// answers the error value.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The circle as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateBoundingCircle(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource)
            ? BoundingCircleResult(in geometry, crsIri, crsSource)
            : SparqlFunctionResult.Error;
    }

    /// <summary>
    /// Renders a geometry's minimum bounding circle: an empty input answers the empty point, a zero
    /// radius answers the centre point, and a positive radius answers the certified circumscribed
    /// polygon — the emitted ring is verified to cover the whole disc, boundary included, and a
    /// rendering that cannot be verified answers the error value rather than an approximation. The
    /// operand gate refuses ordinates beyond <see cref="CircumscribedCirclePolygon.MaximumOperandOrdinate"/>
    /// before the circle computes, which keeps every downstream exact predicate finite, so the seam
    /// answers a value on every operand.
    /// </summary>
    /// <param name="geometry">The geometry whose bounding circle is computed.</param>
    /// <param name="crsIri">The resolved CRS IRI the result carries.</param>
    /// <param name="crsSource">Whether the CRS was explicit or defaulted.</param>
    /// <returns>The rendering as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult BoundingCircleResult(in FlatGeometry geometry, Utf8String crsIri, WktCrsSource crsSource)
    {
        foreach(Spatial.Point2d vertex in geometry.Vertices)
        {
            if(!double.IsFinite(vertex.X)
                || !double.IsFinite(vertex.Y)
                || Math.Abs(vertex.X) > CircumscribedCirclePolygon.MaximumOperandOrdinate
                || Math.Abs(vertex.Y) > CircumscribedCirclePolygon.MaximumOperandOrdinate)
            {
                return SparqlFunctionResult.Error;
            }
        }

        if(!GeometryBoundingCircle.TryCompute(in geometry, out Spatial.BoundingCircle circle))
        {
            FlatGeometry empty = FlatGeometry.Empty(GeometryKind.Point);

            return WktResult(in empty, crsIri, crsSource);
        }

        if(circle.Radius == 0)
        {
            FlatGeometry centre = FlatGeometryFactory.CreatePoint(circle.Center);

            return WktResult(in centre, crsIri, crsSource);
        }

        if(!CircumscribedCirclePolygon.TryRender(circle, BoundingCircleQuadrantSegments, out Spatial.Point2d[] ring, out _))
        {
            return SparqlFunctionResult.Error;
        }

        List<Spatial.Point2d[]> rings = [ring];
        FlatGeometry polygon = FlatGeometryFactory.CreatePolygon(rings);

        return WktResult(in polygon, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:centroid</c>: the effective-dimension centroid with the input's CRS carriage.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The centroid as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateCentroid(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry centroid = GeometryCentroid.ComputeCentroidGeometry(in geometry);

        return WktResult(in centroid, crsIri, crsSource);
    }

    /// <summary>
    /// Evaluates <c>geof:concaveHull</c>: the chi-shape concave hull with the input's CRS carriage. The
    /// seam is unary and the concaveness parameter — implementation-defined territory the specification
    /// asks implementers to document — is the catalog's own
    /// <see cref="DefaultConcaveHullEdgeLengthRatio"/>, so any other argument count is a wrong arity and
    /// no argument of this function can be a malformed ratio. An empty operand collapses to the empty
    /// point through the convex-hull delegation.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The hull as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateConcaveHull(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        return GeometryConcaveHull.TryCompute(in geometry, DefaultConcaveHullEdgeLengthRatio, out FlatGeometry hull)
            ? WktResult(in hull, crsIri, crsSource)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:convexHull</c>: the total hull with the input's CRS carriage.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The hull as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateConvexHull(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry hull = GeometryConvexHull.Compute(in geometry);

        return WktResult(in hull, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:difference</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The difference as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateDifference(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateOverlayOperation(arguments, OverlayOperation.Difference);
    }

    /// <summary>Evaluates <c>geof:intersection</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The intersection as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateIntersection(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateOverlayOperation(arguments, OverlayOperation.Intersection);
    }

    /// <summary>Evaluates <c>geof:isSimple</c>: total per-kind simplicity.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The flag as <c>xsd:boolean</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateIsSimple(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return arguments.Length == 1 && TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _)
            ? BooleanResult(GeometrySimplicity.IsSimple(in geometry))
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:relate</c>: the DE-9IM pattern test under the one-CRS gate and the collection-union composition.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The match flag as <c>xsd:boolean</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateRelate(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 3
            || !TryReadOperand(arguments[0], out FlatGeometry first, out Utf8String firstCrs, out _)
            || !TryReadOperand(arguments[1], out FlatGeometry second, out Utf8String secondCrs, out _)
            || !firstCrs.Span.SequenceEqual(secondCrs.Span)
            || !TryReadPattern(arguments[2], out Utf8String pattern)
            || !TryResolveRelateOperand(in first, out FlatGeometry firstResolved)
            || !TryResolveRelateOperand(in second, out FlatGeometry secondResolved)
            || !GeometryRelate.TryRelate(in firstResolved, in secondResolved, pattern.Span, out bool matches))
        {
            return SparqlFunctionResult.Error;
        }

        return BooleanResult(matches);
    }

    /// <summary>Evaluates <c>geof:symDifference</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The symmetric difference as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateSymDifference(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateOverlayOperation(arguments, OverlayOperation.SymDifference);
    }

    /// <summary>
    /// Evaluates <c>geof:transform</c>: re-expresses the geometry in the target coordinate reference
    /// system named by the <c>srsIRI</c> argument, over the transform surface's closed certified
    /// roster. The operand's resolved CRS and the target must both be roster members — an
    /// unrecognized system on either side answers the error value, matching the surface's
    /// small-certified-roster-with-loud-refusals posture — as does a 3D or measured operand (the
    /// surface carries 2D interleaved coordinates only, and no third ordinate is fabricated) and any
    /// coordinate the pair's whole-span validation refuses (no clamping, no wrapping). The whole
    /// vertex column transforms in one surface call, so collections and every multi kind carry. The
    /// result always emits the explicit target-IRI prefix in the target system's declared axis
    /// order; an EPSG:4326 target therefore answers latitude-first coordinates.
    /// </summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The transformed geometry as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateTransform(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2
            || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _)
            || geometry.Is3D
            || geometry.IsMeasured)
        {
            return SparqlFunctionResult.Error;
        }

        Utf8String targetIri = arguments[1] switch
        {
            NamedNode named => named.Iri,
            Literal literal when literal.Datatype.Iri.Span.SequenceEqual(Vocabulary.Xsd.AnyUri.Span) => literal.Value,
            _ => default
        };

        if(!TryRecognizeRosterMember(crsIri, out CoordinateReferenceSystem source)
            || !TryRecognizeRosterMember(targetIri, out CoordinateReferenceSystem target))
        {
            return SparqlFunctionResult.Error;
        }

        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        if(vertices.IsEmpty)
        {
            return WktResult(in geometry, targetIri, WktCrsSource.Explicit);
        }

        IMemoryOwner<Point2d> transformedColumn = FlatGeometryAllocators.Default.VertexColumns(vertices.Length);
        if(!CoordinateReferenceTransform.TryTransform(
            source,
            target,
            MemoryMarshal.Cast<Point2d, double>(vertices),
            MemoryMarshal.Cast<Point2d, double>(transformedColumn.Memory.Span),
            out _))
        {
            transformedColumn.Dispose();

            return SparqlFunctionResult.Error;
        }

        var transformed = new FlatGeometry(geometry.Nodes.ToArray(), geometry.Parts.ToArray(), transformedColumn, null, null);

        return WktResult(in transformed, targetIri, WktCrsSource.Explicit);
    }

    /// <summary>
    /// Recognizes a CRS IRI's UTF-8 bytes against the transform surface's closed roster through the
    /// roster's one recognition point. The three canonical spellings are pure ASCII, so any byte
    /// beyond ASCII, or a length above every roster spelling's, refuses before transcoding;
    /// otherwise the bytes widen one-to-one into a stack buffer for the ordinal match.
    /// </summary>
    /// <param name="iri">The candidate IRI bytes.</param>
    /// <param name="system">The recognized roster member.</param>
    /// <returns><see langword="true"/> when the IRI is a canonical roster spelling.</returns>
    private static bool TryRecognizeRosterMember(Utf8String iri, out CoordinateReferenceSystem system)
    {
        system = default;
        ReadOnlySpan<byte> bytes = iri.Span;
        if(bytes.Length is 0 or > 64)
        {
            return false;
        }

        Span<char> characters = stackalloc char[bytes.Length];
        for(int index = 0; index < bytes.Length; index++)
        {
            byte value = bytes[index];
            if(value >= 0x80)
            {
                return false;
            }

            characters[index] = (char)value;
        }

        return CoordinateReferenceSystem.TryFromIri(characters, out system);
    }

    /// <summary>
    /// The canonical IRI a certified roster member is spelled with, as the exact bytes
    /// <see cref="TryRecognizeRosterMember"/> accepts. An ingested serialization's system identity passes
    /// through here, so however that document spelled its system, the operand carries the one spelling
    /// every downstream roster match, axis-order resolution, unit conversion, and system-preserving
    /// emission compares against. A value naming no roster member answers the empty IRI, which no roster
    /// spelling matches.
    /// </summary>
    /// <param name="system">The roster member.</param>
    /// <returns>The canonical IRI bytes.</returns>
    private static Utf8String RosterCrsIri(CoordinateReferenceSystem system)
    {
        return system.Kind switch
        {
            CoordinateReferenceSystemKind.Crs84 => WktCrsPrefix.DefaultCrsIri,
            CoordinateReferenceSystemKind.Epsg4326 => Epsg4326CrsIri,
            CoordinateReferenceSystemKind.WebMercator => WebMercatorCrsIri,
            _ => default
        };
    }

    /// <summary>
    /// Re-expresses a geometry in CRS84 for the serialization formats that fix that system. A geometry
    /// already in CRS84 passes through untouched, and so does a geometry with no positions, whose
    /// coordinates nothing can change. Everything else transforms its whole vertex column in one surface
    /// call over the certified roster, so collections and every multi kind carry. A system outside the
    /// roster, a coordinate the pair's validation refuses, and a carried third or measured ordinate all
    /// answer false: the transform surface is planar, so re-expressing such a geometry would have to drop
    /// the ordinate the operand carried, and dropping it silently is never an answer.
    /// </summary>
    /// <param name="geometry">The operand geometry.</param>
    /// <param name="crsIri">The geometry's resolved CRS IRI.</param>
    /// <param name="crs84Geometry">The geometry expressed in CRS84.</param>
    /// <returns><see langword="true"/> when the geometry is expressible in CRS84.</returns>
    private static bool TryExpressInCrs84(in FlatGeometry geometry, Utf8String crsIri, out FlatGeometry crs84Geometry)
    {
        crs84Geometry = default;
        if(!TryRecognizeRosterMember(crsIri, out CoordinateReferenceSystem source))
        {
            return false;
        }

        ReadOnlySpan<Point2d> vertices = geometry.Vertices;
        if(source == CoordinateReferenceSystem.Crs84 || vertices.IsEmpty)
        {
            crs84Geometry = geometry;

            return true;
        }

        if(geometry.Is3D || geometry.IsMeasured)
        {
            return false;
        }

        IMemoryOwner<Point2d> transformedColumn = FlatGeometryAllocators.Default.VertexColumns(vertices.Length);
        if(!CoordinateReferenceTransform.TryTransform(
            source,
            CoordinateReferenceSystem.Crs84,
            MemoryMarshal.Cast<Point2d, double>(vertices),
            MemoryMarshal.Cast<Point2d, double>(transformedColumn.Memory.Span),
            out _))
        {
            transformedColumn.Dispose();

            return false;
        }

        crs84Geometry = new FlatGeometry(geometry.Nodes.ToArray(), geometry.Parts.ToArray(), transformedColumn, null, null);

        return true;
    }

    /// <summary>Evaluates <c>geof:union</c>.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The union as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateUnion(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        return EvaluateOverlayOperation(arguments, OverlayOperation.Union);
    }

    /// <summary>Evaluates <c>geof:aggBoundingBox</c>: the envelope over the group's combined geometries.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated geometry values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The envelope as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAggBoundingBox(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        List<FlatGeometry> members = [];
        if(!TryReadGroupMembers(group, members, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry combined = CombineGroupMembers(members);
        FlatGeometry envelope = GeometryEnvelope.ComputeEnvelopeGeometry(in combined);

        return WktResult(in envelope, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:aggBoundingCircle</c>: the minimum bounding circle over the group's combined geometries, in the certified circumscribed-polygon rendering.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated geometry values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The rendering as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAggBoundingCircle(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        List<FlatGeometry> members = [];
        if(!TryReadGroupMembers(group, members, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry combined = CombineGroupMembers(members);

        return BoundingCircleResult(in combined, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:aggCentroid</c>: the effective-dimension centroid over the group's combined geometries.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated geometry values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The centroid as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAggCentroid(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        List<FlatGeometry> members = [];
        if(!TryReadGroupMembers(group, members, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry combined = CombineGroupMembers(members);
        FlatGeometry centroid = GeometryCentroid.ComputeCentroidGeometry(in combined);

        return WktResult(in centroid, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:aggConcaveHull</c>: the chi-shape concave hull over the group's combined points at the catalog's documented default concaveness ratio.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated geometry values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The hull as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAggConcaveHull(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        List<FlatGeometry> members = [];
        if(!TryReadGroupMembers(group, members, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry combined = CombineGroupMembers(members);

        return GeometryConcaveHull.TryCompute(in combined, DefaultConcaveHullEdgeLengthRatio, out FlatGeometry hull)
            ? WktResult(in hull, crsIri, crsSource)
            : SparqlFunctionResult.Error;
    }

    /// <summary>Evaluates <c>geof:aggConvexHull</c>: the convex hull over the group's combined points.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated geometry values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The hull as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAggConvexHull(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        List<FlatGeometry> members = [];
        if(!TryReadGroupMembers(group, members, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry combined = CombineGroupMembers(members);
        FlatGeometry hull = GeometryConvexHull.Compute(in combined);

        return WktResult(in hull, crsIri, crsSource);
    }

    /// <summary>Evaluates <c>geof:aggUnion</c>: the set union folded pairwise over the group's members in member order.</summary>
    /// <param name="functionIri">The invoked IRI, unused.</param>
    /// <param name="group">The group's evaluated geometry values.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The union as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateAggUnion(Utf8String functionIri, SparqlAggregateGroup group, SparqlExpressionContext context)
    {
        List<FlatGeometry> members = [];
        if(!TryReadGroupMembers(group, members, out Utf8String crsIri, out WktCrsSource crsSource))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry union = members[0];
        for(int i = 1; i < members.Count; i++)
        {
            if(!GeometryOverlay.TryUnion(in union, members[i], out FlatGeometry folded))
            {
                return SparqlFunctionResult.Error;
            }

            union = folded;
        }

        return WktResult(in union, crsIri, crsSource);
    }

    /// <summary>
    /// Reads a group's members under the group-wide one-CRS gate: every member must resolve to the same
    /// CRS IRI byte-for-byte, and the result carriage is explicit when any member carried an explicit
    /// prefix. The empty group, a non-geometry member, and a CRS mismatch answer <see langword="false"/> —
    /// an aggregate over silently fewer or differently-referenced members would describe a different group.
    /// </summary>
    /// <param name="group">The group's evaluated values.</param>
    /// <param name="membersToAppendTo">The list the parsed member geometries append to, in member order.</param>
    /// <param name="crsIri">The group's one resolved CRS IRI.</param>
    /// <param name="crsSource">Explicit when any member carried an explicit prefix; defaulted otherwise.</param>
    /// <returns><see langword="true"/> when every member parsed under one CRS and the group is non-empty.</returns>
    private static bool TryReadGroupMembers(SparqlAggregateGroup group, List<FlatGeometry> membersToAppendTo, out Utf8String crsIri, out WktCrsSource crsSource)
    {
        crsIri = default;
        crsSource = WktCrsSource.Defaulted;

        if(group.Values.Length == 0)
        {
            return false;
        }

        foreach(RdfTerm value in group.Values)
        {
            if(!TryReadOperand(value, out FlatGeometry geometry, out Utf8String memberCrs, out WktCrsSource memberSource))
            {
                return false;
            }

            if(membersToAppendTo.Count == 0)
            {
                crsIri = memberCrs;
            }
            else if(!crsIri.Span.SequenceEqual(memberCrs.Span))
            {
                return false;
            }

            if(memberSource == WktCrsSource.Explicit)
            {
                crsSource = WktCrsSource.Explicit;
            }

            membersToAppendTo.Add(geometry);
        }

        return true;
    }

    /// <summary>Composes parsed group members into one geometry: a single member stands alone; several compose as a geometry collection.</summary>
    /// <param name="members">The parsed member geometries, in member order.</param>
    /// <returns>The combined geometry.</returns>
    private static FlatGeometry CombineGroupMembers(List<FlatGeometry> members)
    {
        return members.Count == 1 ? members[0] : FlatGeometryFactory.CreateCollection(members);
    }

    /// <summary>
    /// Shared evaluation of the overlay set operations under the one-CRS gate. The refusal split is the
    /// substrate's: union accepts collection operands through the stratified fold, the other operations
    /// refuse them, and a detected noding inconsistency refuses. The result literal carries an explicit
    /// CRS prefix when either operand carried one.
    /// </summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="operation">The overlay operation to compute.</param>
    /// <returns>The result as a <c>geo:wktLiteral</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateOverlayOperation(ReadOnlySpan<RdfTerm> arguments, OverlayOperation operation)
    {
        if(arguments.Length != 2
            || !TryReadOperand(arguments[0], out FlatGeometry first, out Utf8String firstCrs, out WktCrsSource firstSource)
            || !TryReadOperand(arguments[1], out FlatGeometry second, out Utf8String secondCrs, out WktCrsSource secondSource)
            || !firstCrs.Span.SequenceEqual(secondCrs.Span))
        {
            return SparqlFunctionResult.Error;
        }

        FlatGeometry result;
        bool computed = operation switch
        {
            OverlayOperation.Intersection => GeometryOverlay.TryIntersection(in first, in second, out result),
            OverlayOperation.Union => GeometryOverlay.TryUnion(in first, in second, out result),
            OverlayOperation.Difference => GeometryOverlay.TryDifference(in first, in second, out result),
            _ => GeometryOverlay.TrySymDifference(in first, in second, out result),
        };

        if(!computed)
        {
            return SparqlFunctionResult.Error;
        }

        WktCrsSource resultSource = firstSource == WktCrsSource.Explicit || secondSource == WktCrsSource.Explicit
            ? WktCrsSource.Explicit
            : WktCrsSource.Defaulted;

        return WktResult(in result, firstCrs, resultSource);
    }

    /// <summary>The function-IRI-to-predicate dispatch table of the twenty-four named topological predicates.</summary>
    private static (Utf8String FunctionIri, TopologicalPredicate Predicate)[] PredicateTable { get; } =
    [
        (GeoVocabulary.Geof.SfEquals, TopologicalPredicate.SfEquals),
        (GeoVocabulary.Geof.SfDisjoint, TopologicalPredicate.SfDisjoint),
        (GeoVocabulary.Geof.SfIntersects, TopologicalPredicate.SfIntersects),
        (GeoVocabulary.Geof.SfTouches, TopologicalPredicate.SfTouches),
        (GeoVocabulary.Geof.SfCrosses, TopologicalPredicate.SfCrosses),
        (GeoVocabulary.Geof.SfWithin, TopologicalPredicate.SfWithin),
        (GeoVocabulary.Geof.SfContains, TopologicalPredicate.SfContains),
        (GeoVocabulary.Geof.SfOverlaps, TopologicalPredicate.SfOverlaps),
        (GeoVocabulary.Geof.EhEquals, TopologicalPredicate.EhEquals),
        (GeoVocabulary.Geof.EhDisjoint, TopologicalPredicate.EhDisjoint),
        (GeoVocabulary.Geof.EhMeet, TopologicalPredicate.EhMeet),
        (GeoVocabulary.Geof.EhOverlap, TopologicalPredicate.EhOverlap),
        (GeoVocabulary.Geof.EhCovers, TopologicalPredicate.EhCovers),
        (GeoVocabulary.Geof.EhCoveredBy, TopologicalPredicate.EhCoveredBy),
        (GeoVocabulary.Geof.EhInside, TopologicalPredicate.EhInside),
        (GeoVocabulary.Geof.EhContains, TopologicalPredicate.EhContains),
        (GeoVocabulary.Geof.Rcc8Eq, TopologicalPredicate.Rcc8Eq),
        (GeoVocabulary.Geof.Rcc8Dc, TopologicalPredicate.Rcc8Dc),
        (GeoVocabulary.Geof.Rcc8Ec, TopologicalPredicate.Rcc8Ec),
        (GeoVocabulary.Geof.Rcc8Po, TopologicalPredicate.Rcc8Po),
        (GeoVocabulary.Geof.Rcc8Tppi, TopologicalPredicate.Rcc8Tppi),
        (GeoVocabulary.Geof.Rcc8Tpp, TopologicalPredicate.Rcc8Tpp),
        (GeoVocabulary.Geof.Rcc8Ntpp, TopologicalPredicate.Rcc8Ntpp),
        (GeoVocabulary.Geof.Rcc8Ntppi, TopologicalPredicate.Rcc8Ntppi),
    ];

    /// <summary>
    /// Shared evaluation of the twenty-four named topological predicates, dispatched on the invoked
    /// function IRI, under the one-CRS gate and the collection-union composition.
    /// </summary>
    /// <param name="functionIri">The invoked IRI selecting the predicate.</param>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="context">The evaluation context, unused.</param>
    /// <returns>The predicate verdict as <c>xsd:boolean</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateTopologicalPredicate(Utf8String functionIri, ReadOnlySpan<RdfTerm> arguments, SparqlExpressionContext context)
    {
        if(arguments.Length != 2
            || !TryReadOperand(arguments[0], out FlatGeometry first, out Utf8String firstCrs, out _)
            || !TryReadOperand(arguments[1], out FlatGeometry second, out Utf8String secondCrs, out _)
            || !firstCrs.Span.SequenceEqual(secondCrs.Span)
            || !TryResolveRelateOperand(in first, out FlatGeometry firstResolved)
            || !TryResolveRelateOperand(in second, out FlatGeometry secondResolved))
        {
            return SparqlFunctionResult.Error;
        }

        foreach((Utf8String iri, TopologicalPredicate predicate) in PredicateTable)
        {
            if(functionIri.Span.SequenceEqual(iri.Span))
            {
                return GeometryRelate.TryEvaluate(in firstResolved, in secondResolved, predicate, out bool result)
                    ? BooleanResult(result)
                    : SparqlFunctionResult.Error;
            }
        }

        return SparqlFunctionResult.Error;
    }

    /// <summary>
    /// Resolves a relate-family operand: a non-collection operand passes through; a collection resolves
    /// to its members' union first, so the relate engine never sees a collection. The empty collection
    /// resolves to itself and stays refused downstream.
    /// </summary>
    /// <param name="operand">The parsed operand.</param>
    /// <param name="resolved">The relate-ready operand.</param>
    /// <returns><see langword="false"/> when the union fold detects an inconsistent arrangement.</returns>
    private static bool TryResolveRelateOperand(in FlatGeometry operand, out FlatGeometry resolved)
    {
        if(operand.Kind != GeometryKind.GeometryCollection)
        {
            resolved = operand;

            return true;
        }

        return GeometryOverlay.TryUnion(in operand, FlatGeometry.Empty(GeometryKind.GeometryCollection), out resolved);
    }

    /// <summary>Reads a DE-9IM pattern argument: an <c>xsd:string</c> literal, validated by the matrix matcher itself.</summary>
    /// <param name="term">The argument term.</param>
    /// <param name="pattern">The pattern bytes.</param>
    /// <returns><see langword="true"/> when the argument is a string literal.</returns>
    private static bool TryReadPattern(RdfTerm term, out Utf8String pattern)
    {
        pattern = default;
        if(term is not Literal literal || !literal.Datatype.Iri.Span.SequenceEqual(Vocabulary.Xsd.String.Span))
        {
            return false;
        }

        pattern = literal.Value;

        return true;
    }

    /// <summary>
    /// Reads a plain numeric argument, the buffer family's radius: a numeric literal whose
    /// whole lexical form parses under its own family's grammar — <c>xsd:integer</c> under the integer
    /// grammar, so an ill-typed fractional form is a malformed argument; <c>xsd:double</c>,
    /// <c>xsd:float</c>, and <c>xsd:decimal</c> under the double grammar.
    /// </summary>
    /// <param name="term">The argument term.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when the argument is a parseable numeric literal.</returns>
    private static bool TryReadNumericArgument(RdfTerm term, out double value)
    {
        value = 0;
        if(term is not Literal literal)
        {
            return false;
        }

        ReadOnlySpan<byte> datatype = literal.Datatype.Iri.Span;
        if(datatype.SequenceEqual(Vocabulary.Xsd.Integer.Span))
        {
            if(!Utf8Parser.TryParse(literal.Value.Span, out long integral, out int consumedIntegral) || consumedIntegral != literal.Value.Span.Length)
            {
                return false;
            }

            value = integral;

            return true;
        }

        bool floating = datatype.SequenceEqual(Vocabulary.Xsd.Double.Span)
            || datatype.SequenceEqual(Vocabulary.Xsd.Float.Span)
            || datatype.SequenceEqual(Vocabulary.Xsd.Decimal.Span);
        if(!floating)
        {
            return false;
        }

        return Utf8Parser.TryParse(literal.Value.Span, out value, out int consumed) && consumed == literal.Value.Span.Length;
    }

    /// <summary>The units-argument classifications the catalog recognizes.</summary>
    private enum RecognizedUnit
    {
        /// <summary>Not a recognized units IRI; every magnitude answers the error value.</summary>
        Unrecognized,

        /// <summary>The OGC metre IRI — the <c>metric*</c> family's unit.</summary>
        Metre,

        /// <summary>The OGC degree IRI — the CRS84 default's coordinate unit.</summary>
        Degree,
    }

    /// <summary>
    /// The installed GeoJSON read binding. The Geo library holds no JSON tokenizer of its own: the
    /// binding assembly that owns the System.Text.Json dependency implements
    /// <see cref="GeoJsonGeometryReadDelegate"/>, and a composing host supplies it through
    /// <see cref="GeoExtensionModule.RegisterFunctions"/>. Reaching a non-empty
    /// <c>geo:geoJSONLiteral</c> body without an installed binding is a composition defect, never a
    /// data condition.
    /// </summary>
    internal static GeoJsonGeometryReadDelegate? GeoJsonReader { get; set; }

    /// <summary>The installed GeoJSON read binding, asserted present at the read site.</summary>
    private static GeoJsonGeometryReadDelegate InstalledGeoJsonReader => GeoJsonReader
        ?? throw new InvalidOperationException(
            "No GeoJSON read binding is installed. A composing host registers the Geo function catalog together with the binding assembly's GeoJSON reader.");

    /// <summary>
    /// Reads a geometry operand: a <c>geo:wktLiteral</c> literal whose CRS prefix structure parses and
    /// whose body is well-formed WKT, with an empty body denoting the empty geometry. The
    /// <c>geo:gmlLiteral</c>, <c>geo:geoJSONLiteral</c>, and <c>geo:kmlLiteral</c> serialization datatypes
    /// read through their formats' codecs. An all-whitespace body — the zero-length form included —
    /// denotes the empty geometry in the default CRS, decided by one scan that runs ahead of every codec,
    /// because those recognizers read an all-whitespace body as no content and no codec has a reading for
    /// one; every other body is the codec's input, and a codec refusal answers false rather than raising.
    /// A GML body's coordinate reference system is the one its root element declares, and the operand
    /// carries that system's canonical roster IRI — the one spelling every downstream comparison expects,
    /// whichever accepted spelling the document wrote — as an explicit system, with the coordinates left
    /// in that system's declared axis order. GeoJSON and KML fix their coordinate reference system to the
    /// CRS84 default, so their operands carry the defaulted CRS. The <c>geo:dggsLiteral</c> and house
    /// <c>a5Literal</c> empty form is the exact zero-length literal only — the DGGS grammar gives a
    /// whitespace-only form no interpretation, so such a form is not readable here, matching the
    /// registered datatype's invalid verdict. A non-empty form of either DGGS datatype is readable exactly
    /// when it is a house-flavour cell-set literal whose cells materialize through the cells-to-geometry
    /// bridge at CRS84; a foreign grid's form is not readable here. The materialized geometry is
    /// heap-backed.
    /// </summary>
    /// <param name="term">The argument term.</param>
    /// <param name="geometry">The parsed geometry.</param>
    /// <param name="crsIri">The resolved CRS IRI, defaulted or explicit.</param>
    /// <param name="crsSource">Whether the CRS IRI was explicit in the lexical form or defaulted.</param>
    /// <returns><see langword="true"/> when the operand is a well-formed geometry literal.</returns>
    private static bool TryReadOperand(RdfTerm term, out FlatGeometry geometry, out Utf8String crsIri, out WktCrsSource crsSource)
    {
        geometry = default;
        crsIri = default;
        crsSource = WktCrsSource.Defaulted;
        if(term is not Literal literal)
        {
            return false;
        }

        ReadOnlySpan<byte> datatypeIri = literal.Datatype.Iri.Span;
        if(!datatypeIri.SequenceEqual(GeoVocabulary.Geo.WktLiteral.Span))
        {
            if(datatypeIri.SequenceEqual(GeoVocabulary.Geo.DggsLiteral.Span)
                || datatypeIri.SequenceEqual(A5DggsVocabulary.DatatypeIri.Span))
            {
                if(literal.Value.Span.Length == 0)
                {
                    geometry = FlatGeometry.Empty(GeometryKind.GeometryCollection);
                    crsIri = WktCrsPrefix.DefaultCrsIri;
                    crsSource = WktCrsSource.Defaulted;

                    return true;
                }

                List<A5CellId> cells = [];
                if(!A5DggsBody.TryReadCanonicalCells(literal.Value.Span, cells)
                    || !A5CellGeometry.TryBuildGeometry(CollectionsMarshal.AsSpan(cells), out geometry))
                {
                    return false;
                }

                crsIri = WktCrsPrefix.DefaultCrsIri;
                crsSource = WktCrsSource.Defaulted;

                return true;
            }

            bool gml = datatypeIri.SequenceEqual(GeoVocabulary.Geo.GmlLiteral.Span);
            bool geoJson = datatypeIri.SequenceEqual(GeoVocabulary.Geo.GeoJsonLiteral.Span);
            if(!gml && !geoJson && !datatypeIri.SequenceEqual(GeoVocabulary.Geo.KmlLiteral.Span))
            {
                return false;
            }

            ReadOnlySpan<byte> serializedBody = literal.Value.Span;
            if(IsBlankSerializationBody(serializedBody))
            {
                geometry = FlatGeometry.Empty(GeometryKind.GeometryCollection);
                crsIri = WktCrsPrefix.DefaultCrsIri;
                crsSource = WktCrsSource.Defaulted;

                return true;
            }

            if(gml)
            {
                if(!GmlGeometryReader.TryRead(serializedBody, FlatGeometryAllocators.Default, out geometry, out CoordinateReferenceSystem declared, out _))
                {
                    return false;
                }

                crsIri = RosterCrsIri(declared);
                crsSource = WktCrsSource.Explicit;

                return true;
            }

            bool read = geoJson
                ? InstalledGeoJsonReader(serializedBody, FlatGeometryAllocators.Default, out geometry, out _)
                : KmlGeometryReader.TryRead(serializedBody, FlatGeometryAllocators.Default, out geometry, out _);
            if(!read)
            {
                return false;
            }

            crsIri = WktCrsPrefix.DefaultCrsIri;
            crsSource = WktCrsSource.Defaulted;

            return true;
        }

        if(!WktCrsPrefix.TryParse(literal.Value, out WktCrsPrefix decomposition))
        {
            return false;
        }

        crsIri = decomposition.CrsIri;
        crsSource = decomposition.Source;
        if(decomposition.Body.Span.IsEmpty)
        {
            geometry = FlatGeometry.Empty(GeometryKind.GeometryCollection);

            return true;
        }

        return WktGeometryReader.TryRead(decomposition.Body.Span, out geometry, out _);
    }

    /// <summary>
    /// Whether a serialization literal's lexical form carries no content: the zero-length form and the
    /// all-whitespace form alike. This is the one scan standing ahead of every codec dispatch, so a body
    /// the formats read as no content is answered as the empty geometry and never reaches a codec, whose
    /// grammar has no reading for it.
    /// </summary>
    /// <param name="body">The literal's lexical form.</param>
    /// <returns><see langword="true"/> when the form holds no non-whitespace byte.</returns>
    private static bool IsBlankSerializationBody(ReadOnlySpan<byte> body)
    {
        for(int index = 0; index < body.Length; index++)
        {
            if(!WktLexical.IsWhitespace(body[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reads a one-based member number: an <c>xsd:integer</c> literal whose whole lexical form parses.</summary>
    /// <param name="term">The argument term.</param>
    /// <param name="memberNumber">The parsed member number.</param>
    /// <returns><see langword="true"/> when the argument is an integer literal in <see cref="int"/> range.</returns>
    private static bool TryReadMemberNumber(RdfTerm term, out int memberNumber)
    {
        memberNumber = 0;
        if(term is not Literal literal || !literal.Datatype.Iri.Span.SequenceEqual(Vocabulary.Xsd.Integer.Span))
        {
            return false;
        }

        if(!Utf8Parser.TryParse(literal.Value.Span, out long value, out int consumed)
            || consumed != literal.Value.Span.Length
            || value < int.MinValue
            || value > int.MaxValue)
        {
            return false;
        }

        memberNumber = (int)value;

        return true;
    }

    /// <summary>Classifies a units argument: an IRI term or an <c>xsd:anyURI</c> literal naming a recognized OGC linear unit.</summary>
    /// <param name="term">The units argument term.</param>
    /// <returns>The classification.</returns>
    private static RecognizedUnit ClassifyUnits(RdfTerm term)
    {
        Utf8String iri = term switch
        {
            NamedNode named => named.Iri,
            Literal literal when literal.Datatype.Iri.Span.SequenceEqual(Vocabulary.Xsd.AnyUri.Span) => literal.Value,
            _ => default,
        };

        if(iri.Span.SequenceEqual(OgcUnitsOfMeasure.Metre.Span))
        {
            return RecognizedUnit.Metre;
        }

        if(iri.Span.SequenceEqual(OgcUnitsOfMeasure.Degree.Span))
        {
            return RecognizedUnit.Degree;
        }

        return RecognizedUnit.Unrecognized;
    }

    /// <summary>
    /// Applies the catalog's unit rules to a planar magnitude. A system the certified roster recognizes
    /// answers exactly its declared unit — degrees for the two geographic members, metres for Web
    /// Mercator — so a metre-denominated answer over declared-degree coordinates is a refused
    /// fabrication rather than a value. A system outside the roster answers the metre unit under the
    /// explicit-CRS convention and never the degree unit, and an unrecognized unit never answers.
    /// </summary>
    /// <param name="planarMagnitude">The planar magnitude in coordinate units.</param>
    /// <param name="crsIri">The geometry's resolved CRS IRI.</param>
    /// <param name="unit">The requested unit.</param>
    /// <param name="magnitude">The answered magnitude.</param>
    /// <returns><see langword="true"/> when the magnitude is answerable in the requested unit.</returns>
    private static bool TryConvertMagnitude(double planarMagnitude, Utf8String crsIri, RecognizedUnit unit, out double magnitude)
    {
        bool answerable;
        if(TryRecognizeRosterMember(crsIri, out CoordinateReferenceSystem system))
        {
            answerable = unit switch
            {
                RecognizedUnit.Metre => system.Unit == CoordinateUnit.Metre,
                RecognizedUnit.Degree => system.Unit == CoordinateUnit.Degree,
                _ => false
            };
        }
        else
        {
            answerable = unit == RecognizedUnit.Metre;
        }

        magnitude = answerable ? planarMagnitude : 0;

        return answerable;
    }

    /// <summary>
    /// The planar segment-length sum over one family of parts: the polygonal rings (shells and holes
    /// alike) when <paramref name="polygonal"/> is set, else the lineal runs. The families are disjoint by
    /// node kind, so <c>geof:length</c> and <c>geof:perimeter</c> partition a geometry's segments.
    /// </summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <param name="polygonal">Whether to sum polygonal rings instead of lineal runs.</param>
    /// <returns>The segment-length sum in coordinate units.</returns>
    private static double SegmentSum(in FlatGeometry geometry, bool polygonal)
    {
        double total = 0;
        foreach(FlatGeometryNode node in geometry.Nodes)
        {
            bool selected = polygonal
                ? node.Kind is GeometryKind.Polygon or GeometryKind.MultiPolygon
                : node.Kind is GeometryKind.LineString or GeometryKind.MultiLineString;
            if(!selected)
            {
                continue;
            }

            for(int index = 0; index < node.PartCount; index++)
            {
                FlatGeometryPart part = geometry.Parts[node.FirstPart + index];
                for(int vertex = part.Start + 1; vertex < part.Start + part.Length; vertex++)
                {
                    double deltaX = geometry.Vertices[vertex].X - geometry.Vertices[vertex - 1].X;
                    double deltaY = geometry.Vertices[vertex].Y - geometry.Vertices[vertex - 1].Y;
                    total += Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                }
            }
        }

        return total;
    }

    /// <summary>
    /// Shared evaluation of the X/Y coordinate extrema: the selected extremum over every position; the
    /// empty point set errs. The requested axis resolves to an ordinate slot through the operand's
    /// declared axis order — X names the east axis (longitude or easting) and Y the north axis, so a
    /// literal in the roster's declared-latitude-first system reads the transposed slot and both
    /// geographic spellings of one geometry answer alike — while a system the certified roster does not
    /// recognize keeps the literal's own written order.
    /// </summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="maximum">Whether to answer the maximum instead of the minimum.</param>
    /// <param name="axisY">Whether to read the north-axis coordinate instead of the east-axis coordinate.</param>
    /// <returns>The coordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateBoundsCoordinate(ReadOnlySpan<RdfTerm> arguments, bool maximum, bool axisY)
    {
        if(arguments.Length != 1
            || !TryReadOperand(arguments[0], out FlatGeometry geometry, out Utf8String crsIri, out _)
            || geometry.Vertices.Length == 0)
        {
            return SparqlFunctionResult.Error;
        }

        bool readSecondOrdinate = axisY;
        if(TryRecognizeRosterMember(crsIri, out CoordinateReferenceSystem system)
            && system.AxisOrder == CoordinateAxisOrder.LatitudeLongitude)
        {
            readSecondOrdinate = !axisY;
        }

        double extremum = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        foreach(Spatial.Point2d vertex in geometry.Vertices)
        {
            double value = readSecondOrdinate ? vertex.Y : vertex.X;
            extremum = maximum ? Math.Max(extremum, value) : Math.Min(extremum, value);
        }

        return DoubleResult(extremum);
    }

    /// <summary>Shared evaluation of the Z extrema: the selected extremum over the carried Z ordinates (uncarried slots hold NaN and are skipped); a geometry carrying no Z errs.</summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <param name="maximum">Whether to answer the maximum instead of the minimum.</param>
    /// <returns>The ordinate as <c>xsd:double</c>, or the error value.</returns>
    private static SparqlFunctionResult EvaluateBoundsOrdinate(ReadOnlySpan<RdfTerm> arguments, bool maximum)
    {
        if(arguments.Length != 1 || !TryReadOperand(arguments[0], out FlatGeometry geometry, out _, out _))
        {
            return SparqlFunctionResult.Error;
        }

        bool found = false;
        double extremum = maximum ? double.NegativeInfinity : double.PositiveInfinity;
        foreach(double value in geometry.ZOrdinates)
        {
            if(double.IsNaN(value))
            {
                continue;
            }

            found = true;
            extremum = maximum ? Math.Max(extremum, value) : Math.Min(extremum, value);
        }

        return found ? DoubleResult(extremum) : SparqlFunctionResult.Error;
    }

    /// <summary>Wraps a boolean as an <c>xsd:boolean</c> literal result.</summary>
    /// <param name="value">The boolean value.</param>
    /// <returns>The result.</returns>
    private static SparqlFunctionResult BooleanResult(bool value)
    {
        return SparqlFunctionResult.Of(new Literal(value ? TrueLexical : FalseLexical, XsdBooleanDatatype));
    }

    /// <summary>Formats an integer as an <c>xsd:integer</c> literal result.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The result.</returns>
    private static SparqlFunctionResult IntegerResult(int value)
    {
        Span<byte> scratch = stackalloc byte[16];
        bool formatted = Utf8Formatter.TryFormat(value, scratch, out int written);
        System.Diagnostics.Debug.Assert(formatted, "A 16-byte scratch holds every int lexical form.");

        return SparqlFunctionResult.Of(new Literal(new Utf8String(scratch[..written].ToArray()), XsdIntegerDatatype));
    }

    /// <summary>
    /// Formats a magnitude as an <c>xsd:double</c> literal result in the substrate's shortest-round-trip
    /// invariant form; a non-finite magnitude answers the error value.
    /// </summary>
    /// <param name="value">The magnitude.</param>
    /// <returns>The result.</returns>
    private static SparqlFunctionResult DoubleResult(double value)
    {
        if(!double.IsFinite(value))
        {
            return SparqlFunctionResult.Error;
        }

        Span<byte> scratch = stackalloc byte[32];
        bool formatted = value.TryFormat(scratch, out int written, format: default, CultureInfo.InvariantCulture);
        System.Diagnostics.Debug.Assert(formatted, "A 32-byte scratch holds every shortest-round-trip double form.");

        return SparqlFunctionResult.Of(new Literal(new Utf8String(scratch[..written].ToArray()), XsdDoubleDatatype));
    }

    /// <summary>
    /// Serializes a geometry as a <c>geo:wktLiteral</c> result: an explicit CRS re-emits its
    /// <c>&lt;IRI&gt;</c> prefix and one separating space, a defaulted CRS stays implicit, and the body is
    /// the writer's canonical form. The literal owns a copy of the serialization buffer.
    /// </summary>
    /// <param name="geometry">The geometry to serialize.</param>
    /// <param name="crsIri">The geometry's resolved CRS IRI.</param>
    /// <param name="crsSource">Whether the CRS was explicit in the source lexical form.</param>
    /// <returns>The result.</returns>
    private static SparqlFunctionResult WktResult(in FlatGeometry geometry, Utf8String crsIri, WktCrsSource crsSource)
    {
        var buffer = new ArrayBufferWriter<byte>();
        if(crsSource == WktCrsSource.Explicit)
        {
            buffer.Write("<"u8);
            buffer.Write(crsIri.Span);
            buffer.Write("> "u8);
        }

        WktGeometryWriter.Write(in geometry, buffer);

        return SparqlFunctionResult.Of(new Literal(new Utf8String(buffer.WrittenSpan.ToArray()), WktLiteralDatatype));
    }
}
