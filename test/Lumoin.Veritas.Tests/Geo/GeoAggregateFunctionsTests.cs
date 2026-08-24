using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Sparql.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Lumoin.Veritas.Tests.Geo.GeoFunctionCalls;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The <c>geof:agg*</c> spatial-aggregate folds at the value level: each fold over a group's members
/// answers exactly what its scalar counterpart answers over the members composed as one geometry (the
/// aggregate semantics ARE the collection semantics), the group-wide one-CRS gate refuses a mixed group,
/// the explicit prefix carries when any member carried one, and the empty group, an ill-typed member,
/// and a malformed member answer the error value — an aggregate over silently fewer or
/// differently-referenced members would describe a different group.
/// </summary>
[TestClass]
internal sealed class GeoAggregateFunctionsTests
{
    /// <summary>The explicit CRS84 prefix spelling.</summary>
    private const string Crs84Prefix = "<http://www.opengis.net/def/crs/OGC/1.3/CRS84> ";

    /// <summary><c>geof:aggBoundingBox</c> over two points answers the scalar envelope of their multipoint.</summary>
    [TestMethod]
    public void AggBoundingBoxMatchesTheScalarEnvelopeOfTheCollection()
    {
        SparqlFunctionResult aggregate = InvokeAggregate(GeoFunctions.AggBoundingBox, Wkt("POINT (0 0)"), Wkt("POINT (2 1)"));
        SparqlFunctionResult scalar = Invoke(GeoFunctions.Envelope, Wkt("MULTIPOINT ((0 0), (2 1))"));

        AssertSameGeometryLiteral(scalar, aggregate);
    }

    /// <summary><c>geof:aggCentroid</c> over two points answers the scalar centroid of their collection.</summary>
    [TestMethod]
    public void AggCentroidMatchesTheScalarCentroidOfTheCollection()
    {
        SparqlFunctionResult aggregate = InvokeAggregate(GeoFunctions.AggCentroid, Wkt("POINT (0 0)"), Wkt("POINT (10 10)"));
        SparqlFunctionResult scalar = Invoke(GeoFunctions.Centroid, Wkt("GEOMETRYCOLLECTION (POINT (0 0), POINT (10 10))"));

        AssertSameGeometryLiteral(scalar, aggregate);
    }

    /// <summary><c>geof:aggConvexHull</c> over three points answers the scalar hull of their multipoint.</summary>
    [TestMethod]
    public void AggConvexHullMatchesTheScalarHullOfTheCollection()
    {
        SparqlFunctionResult aggregate = InvokeAggregate(GeoFunctions.AggConvexHull, Wkt("POINT (0 0)"), Wkt("POINT (4 0)"), Wkt("POINT (0 3)"));
        SparqlFunctionResult scalar = Invoke(GeoFunctions.ConvexHull, Wkt("MULTIPOINT ((0 0), (4 0), (0 3))"));

        AssertSameGeometryLiteral(scalar, aggregate);
    }

    /// <summary><c>geof:aggConcaveHull</c> over four points answers the scalar concave hull of their multipoint at the catalog's documented default ratio.</summary>
    [TestMethod]
    public void AggConcaveHullMatchesTheScalarHullOfTheCollection()
    {
        SparqlFunctionResult aggregate = InvokeAggregate(GeoFunctions.AggConcaveHull, Wkt("POINT (0 0)"), Wkt("POINT (4 0)"), Wkt("POINT (4 4)"), Wkt("POINT (0 4)"));
        SparqlFunctionResult scalar = Invoke(GeoFunctions.ConcaveHull, Wkt("MULTIPOINT ((0 0), (4 0), (4 4), (0 4))"));

        AssertSameGeometryLiteral(scalar, aggregate);
    }

    /// <summary><c>geof:aggBoundingCircle</c> over two points answers the scalar bounding circle of their multipoint, in the circumscribed-polygon rendering.</summary>
    [TestMethod]
    public void AggBoundingCircleMatchesTheScalarCircleOfTheCollection()
    {
        SparqlFunctionResult aggregate = InvokeAggregate(GeoFunctions.AggBoundingCircle, Wkt("POINT (0 0)"), Wkt("POINT (1 2)"));
        SparqlFunctionResult scalar = Invoke(GeoFunctions.BoundingCircle, Wkt("MULTIPOINT ((0 0), (1 2))"));

        AssertSameGeometryLiteral(scalar, aggregate);
    }

    /// <summary><c>geof:aggUnion</c> over two polygons answers the pairwise scalar union.</summary>
    [TestMethod]
    public void AggUnionMatchesThePairwiseScalarUnion()
    {
        SparqlFunctionResult aggregate = InvokeAggregate(GeoFunctions.AggUnion, Wkt("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))"), Wkt("POLYGON ((3 0, 5 0, 5 2, 3 2, 3 0))"));
        SparqlFunctionResult scalar = Invoke(GeoFunctions.Union, Wkt("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))"), Wkt("POLYGON ((3 0, 5 0, 5 2, 3 2, 3 0))"));

        AssertSameGeometryLiteral(scalar, aggregate);
    }

    /// <summary><c>geof:aggUnion</c> over a single member answers that member in canonical form.</summary>
    [TestMethod]
    public void AggUnionOverASingleMemberAnswersTheCanonicalForm()
    {
        SparqlFunctionResult result = InvokeAggregate(GeoFunctions.AggUnion, Wkt("point(1 2)"));

        AssertLexical(result, "POINT (1 2)", GeoVocabulary.Geo.WktLiteral);
    }

    /// <summary>The empty group answers the error value on every spatial aggregate — there is no geometry to answer.</summary>
    [TestMethod]
    public void EmptyGroupAnswersTheErrorValueOnEveryAggregate()
    {
        foreach(SparqlFunctionEntry entry in new[] { GeoFunctions.AggBoundingBox, GeoFunctions.AggBoundingCircle, GeoFunctions.AggCentroid, GeoFunctions.AggConcaveHull, GeoFunctions.AggConvexHull, GeoFunctions.AggUnion })
        {
            Assert.IsTrue(InvokeAggregate(entry).IsError, $"{entry.FunctionIri}: the empty group must answer the error value.");
        }
    }

    /// <summary>A group whose members resolve different CRS IRIs answers the error value — the gate is group-wide.</summary>
    [TestMethod]
    public void MixedCrsGroupAnswersTheErrorValue()
    {
        SparqlFunctionResult result = InvokeAggregate(GeoFunctions.AggUnion, Wkt("<http://example.org/def/crs/metric> POINT (0 0)"), Wkt("POINT (1 1)"));

        Assert.IsTrue(result.IsError, "A member under a different resolved CRS must refuse the whole group.");
    }

    /// <summary>The result carries the explicit CRS prefix when any member carried one, under one resolved CRS.</summary>
    [TestMethod]
    public void AnyExplicitMemberCarriesTheExplicitPrefix()
    {
        SparqlFunctionResult result = InvokeAggregate(GeoFunctions.AggBoundingBox, Wkt(Crs84Prefix + "POINT (0 0)"), Wkt("POINT (2 1)"));

        Assert.IsFalse(result.IsError);
        Assert.IsInstanceOfType<Literal>(result.Term);
        Assert.StartsWith(Crs84Prefix, ((Literal)result.Term).Value.ToString(), StringComparison.Ordinal);
    }

    /// <summary>An ill-typed member — a non-geometry literal — answers the error value, never a fold over fewer members.</summary>
    [TestMethod]
    public void IllTypedMemberAnswersTheErrorValue()
    {
        Assert.IsTrue(InvokeAggregate(GeoFunctions.AggUnion, Wkt("POINT (0 0)"), Integer("1")).IsError);
    }

    /// <summary>A malformed member answers the error value, never a fold over fewer members.</summary>
    [TestMethod]
    public void MalformedMemberAnswersTheErrorValue()
    {
        Assert.IsTrue(InvokeAggregate(GeoFunctions.AggCentroid, Wkt("POINT (0 0)"), Wkt("POINT(1")).IsError);
    }

    /// <summary>Asserts two invocations answered the same bound <c>geo:wktLiteral</c>.</summary>
    /// <param name="expected">The invocation whose answer sets the expectation.</param>
    /// <param name="actual">The invocation under test.</param>
    private static void AssertSameGeometryLiteral(SparqlFunctionResult expected, SparqlFunctionResult actual)
    {
        Assert.IsFalse(expected.IsError, "The expectation-setting invocation must answer a bound literal.");
        Assert.IsInstanceOfType<Literal>(expected.Term);

        AssertLexical(actual, ((Literal)expected.Term).Value.ToString(), GeoVocabulary.Geo.WktLiteral);
    }
}
