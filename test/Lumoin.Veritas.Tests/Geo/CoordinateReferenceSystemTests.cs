using Lumoin.Veritas.Geo.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The closed-roster canon for <see cref="CoordinateReferenceSystem"/>: the
/// three canonical IRIs recognize with their registry-faithful axis order and
/// unit, the static roster properties agree with recognition, a family of
/// near-miss spellings refuses (no urn: forms, no case folding, no trimming,
/// no alias of any kind), and the default value — which zero-initialization
/// always produces — refuses safely everywhere it is read.
/// </summary>
[TestClass]
internal sealed class CoordinateReferenceSystemTests
{
    /// <summary>A canonical CRS IRI recognizes with the registry-faithful kind, axis order, and unit.</summary>
    /// <param name="iri">The canonical IRI under test.</param>
    /// <param name="expectedKind">The expected recognized kind.</param>
    /// <param name="expectedAxisOrder">The expected recognized axis order.</param>
    /// <param name="expectedUnit">The expected recognized unit.</param>
    [TestMethod]
    [DataRow("http://www.opengis.net/def/crs/OGC/1.3/CRS84", CoordinateReferenceSystemKind.Crs84, CoordinateAxisOrder.LongitudeLatitude, CoordinateUnit.Degree, DisplayName = "CRS84 recognizes with longitude-latitude axis order in degrees")]
    [DataRow("http://www.opengis.net/def/crs/EPSG/0/4326", CoordinateReferenceSystemKind.Epsg4326, CoordinateAxisOrder.LatitudeLongitude, CoordinateUnit.Degree, DisplayName = "EPSG:4326 recognizes with latitude-longitude axis order in degrees")]
    [DataRow("http://www.opengis.net/def/crs/EPSG/0/3857", CoordinateReferenceSystemKind.WebMercator, CoordinateAxisOrder.EastingNorthing, CoordinateUnit.Metre, DisplayName = "EPSG:3857 recognizes with easting-northing axis order in metres")]
    public void CanonicalIriRecognizesWithTheCorrectDescriptor(
        string iri,
        CoordinateReferenceSystemKind expectedKind,
        CoordinateAxisOrder expectedAxisOrder,
        CoordinateUnit expectedUnit)
    {
        bool recognized = CoordinateReferenceSystem.TryFromIri(iri, out CoordinateReferenceSystem descriptor);

        Assert.IsTrue(recognized);
        Assert.AreEqual(expectedKind, descriptor.Kind);
        Assert.AreEqual(expectedAxisOrder, descriptor.AxisOrder);
        Assert.AreEqual(expectedUnit, descriptor.Unit);
        Assert.AreEqual(iri, descriptor.Iri);
    }

    /// <summary>The static roster properties equal the TryFromIri results for the same canonical IRIs.</summary>
    [TestMethod]
    public void StaticRosterPropertiesEqualTheTryFromIriResults()
    {
        CoordinateReferenceSystem.TryFromIri("http://www.opengis.net/def/crs/OGC/1.3/CRS84", out CoordinateReferenceSystem crs84);
        CoordinateReferenceSystem.TryFromIri("http://www.opengis.net/def/crs/EPSG/0/4326", out CoordinateReferenceSystem epsg4326);
        CoordinateReferenceSystem.TryFromIri("http://www.opengis.net/def/crs/EPSG/0/3857", out CoordinateReferenceSystem webMercator);

        Assert.AreEqual(CoordinateReferenceSystem.Crs84, crs84);
        Assert.AreEqual(CoordinateReferenceSystem.Epsg4326, epsg4326);
        Assert.AreEqual(CoordinateReferenceSystem.WebMercator, webMercator);
    }

    /// <summary>An unrecognized IRI refuses and leaves the out value default.</summary>
    /// <param name="iri">The IRI under test.</param>
    [TestMethod]
    [DataRow("urn:ogc:def:crs:OGC:1.3:CRS84", DisplayName = "The OGC urn: serialization does not recognize")]
    [DataRow("urn:ogc:def:crs:EPSG::4326", DisplayName = "The EPSG urn: serialization does not recognize")]
    [DataRow("HTTP://WWW.OPENGIS.NET/DEF/CRS/OGC/1.3/CRS84", DisplayName = "A case variant does not recognize — no case folding")]
    [DataRow("http://www.opengis.net/def/crs/OGC/1.3/CRS84/", DisplayName = "A trailing slash does not recognize — no trimming")]
    [DataRow(" http://www.opengis.net/def/crs/OGC/1.3/CRS84 ", DisplayName = "Whitespace padding does not recognize — no trimming")]
    [DataRow("http://www.opengis.net/def/crs/EPSG/0/4979", DisplayName = "EPSG:4979 is not on the roster")]
    [DataRow("http://www.opengis.net/def/crs/EPSG/0/25832", DisplayName = "EPSG:25832 is not on the roster")]
    [DataRow("", DisplayName = "The empty string does not recognize")]
    public void UnrecognizedIriRefusesAndLeavesTheOutValueDefault(string iri)
    {
        bool recognized = CoordinateReferenceSystem.TryFromIri(iri, out CoordinateReferenceSystem descriptor);

        Assert.IsFalse(recognized);
        Assert.AreEqual(default(CoordinateReferenceSystem), descriptor);
    }

    /// <summary>The default value has an unspecified kind, axis order, and unit, and an empty IRI.</summary>
    [TestMethod]
    public void DefaultValueHasUnspecifiedKindAxisOrderUnitAndEmptyIri()
    {
        var descriptor = default(CoordinateReferenceSystem);

        Assert.AreEqual(CoordinateReferenceSystemKind.Unspecified, descriptor.Kind);
        Assert.AreEqual(CoordinateAxisOrder.Unspecified, descriptor.AxisOrder);
        Assert.AreEqual(CoordinateUnit.Unspecified, descriptor.Unit);
        Assert.AreEqual(string.Empty, descriptor.Iri);
    }

    /// <summary>Two default values agree across both Equals overloads and produce a stable hash code.</summary>
    [TestMethod]
    public void DefaultValueEqualsMethodsAgreeAndHashCodeIsStable()
    {
        var first = default(CoordinateReferenceSystem);
        var second = default(CoordinateReferenceSystem);

        Assert.IsTrue(first.Equals(second));
        Assert.IsTrue(first.Equals((object)second));
        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
    }

    /// <summary>The default value's Equals returns false against an object of an unrelated type.</summary>
    [TestMethod]
    public void DefaultValueEqualsObjectOfAnUnrelatedTypeReturnsFalse()
    {
        var descriptor = default(CoordinateReferenceSystem);

        Assert.IsFalse(descriptor.Equals("not a coordinate reference system"));
    }

    /// <summary>The default value's ToString is the kind name and does not throw.</summary>
    [TestMethod]
    public void DefaultValueToStringIsTheKindNameAndDoesNotThrow()
    {
        string text = default(CoordinateReferenceSystem).ToString();

        Assert.IsFalse(string.IsNullOrEmpty(text));
        Assert.AreEqual("Unspecified", text);
    }

    /// <summary>CRS84 equals CRS84 under both the equality operator and Equals.</summary>
    [TestMethod]
    public void Crs84EqualsCrs84()
    {
        CoordinateReferenceSystem other = CoordinateReferenceSystem.Crs84;

        Assert.IsTrue(CoordinateReferenceSystem.Crs84 == other);
        Assert.IsTrue(CoordinateReferenceSystem.Crs84.Equals(other));
        Assert.AreEqual(CoordinateReferenceSystem.Crs84, other);
    }

    /// <summary>CRS84 does not equal EPSG:4326.</summary>
    [TestMethod]
    public void Crs84DoesNotEqualEpsg4326()
    {
        Assert.IsTrue(CoordinateReferenceSystem.Crs84 != CoordinateReferenceSystem.Epsg4326);
        Assert.IsFalse(CoordinateReferenceSystem.Crs84.Equals(CoordinateReferenceSystem.Epsg4326));
        Assert.AreNotEqual(CoordinateReferenceSystem.Crs84, CoordinateReferenceSystem.Epsg4326);
    }

    /// <summary>CRS84 does not equal the default value.</summary>
    [TestMethod]
    public void Crs84DoesNotEqualTheDefaultValue()
    {
        Assert.IsTrue(CoordinateReferenceSystem.Crs84 != default);
        Assert.IsFalse(CoordinateReferenceSystem.Crs84.Equals(default));
        Assert.AreNotEqual(CoordinateReferenceSystem.Crs84, default);
    }
}
