using Lumoin.Veritas.Geo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The GeoSPARQL vocabulary constants are authored from the requirement census,
/// so the rows pin the census-derived
/// bytes — including the functions the published function vocabulary omits or case-breaks, which is why
/// the census and not that vocabulary is the row source.
/// </summary>
[TestClass]
internal sealed class GeoVocabularyTests
{
    /// <summary>The literal datatype IRIs carry the ontology's exact casing, including the mixed-case GeoJSON datatype.</summary>
    [TestMethod]
    public void DatatypeIriBytesExact()
    {
        Assert.IsTrue(GeoVocabulary.Geo.WktLiteral.Span.SequenceEqual("http://www.opengis.net/ont/geosparql#wktLiteral"u8));
        Assert.IsTrue(GeoVocabulary.Geo.GeoJsonLiteral.Span.SequenceEqual("http://www.opengis.net/ont/geosparql#geoJSONLiteral"u8));
        Assert.IsTrue(GeoVocabulary.Geo.DggsLiteral.Span.SequenceEqual("http://www.opengis.net/ont/geosparql#dggsLiteral"u8));
    }

    /// <summary>The six functions absent from the published function vocabulary exist here with the census bytes.</summary>
    [TestMethod]
    public void CensusFunctionsAbsentFromPublishedVocabularyPresent()
    {
        Assert.IsTrue(GeoVocabulary.Geof.CoordinateDimension.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/coordinateDimension"u8));
        Assert.IsTrue(GeoVocabulary.Geof.GeometryType.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/geometryType"u8));
        Assert.IsTrue(GeoVocabulary.Geof.Is3D.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/is3D"u8));
        Assert.IsTrue(GeoVocabulary.Geof.IsMeasured.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/isMeasured"u8));
        Assert.IsTrue(GeoVocabulary.Geof.SpatialDimension.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/spatialDimension"u8));
        Assert.IsTrue(GeoVocabulary.Geof.AggConvexHull.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/aggConvexHull"u8));
    }

    /// <summary>The bounding aggregates carry the census casing the published vocabulary case-breaks.</summary>
    [TestMethod]
    public void AggregateFunctionCasingFollowsCensus()
    {
        Assert.IsTrue(GeoVocabulary.Geof.AggBoundingBox.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/aggBoundingBox"u8));
        Assert.IsTrue(GeoVocabulary.Geof.AggBoundingCircle.Span.SequenceEqual("http://www.opengis.net/def/function/geosparql/aggBoundingCircle"u8));
    }

    /// <summary>The Simple Features roster carries the ratified ontology's bytes, including the all-caps TIN class.</summary>
    [TestMethod]
    public void SimpleFeaturesRosterSpotChecks()
    {
        Assert.IsTrue(GeoVocabulary.Sf.Point.Span.SequenceEqual("http://www.opengis.net/ont/sf#Point"u8));
        Assert.IsTrue(GeoVocabulary.Sf.Tin.Span.SequenceEqual("http://www.opengis.net/ont/sf#TIN"u8));
        Assert.IsTrue(GeoVocabulary.Sf.GeometryCollection.Span.SequenceEqual("http://www.opengis.net/ont/sf#GeometryCollection"u8));
    }
}
