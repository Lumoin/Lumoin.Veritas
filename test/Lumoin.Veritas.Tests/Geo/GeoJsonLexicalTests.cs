using Lumoin.Veritas.Geo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The GeoJSON span recognizer's battery: the geometry-object grammar across every type, the empty
/// geometry, the described members <c>coordinates</c>, <c>geometries</c> and <c>bbox</c>, foreign
/// members, the provably malformed families of the geometry-object rule and the JSON token grammar
/// beneath it, the abstentions of an escaped spelling, a repeated tracked member and the <c>crs</c> key
/// in both its positions, the one recognized top-level <c>crs</c> value against the malformed rest, and
/// container nesting at and beyond the hard cap.
/// </summary>
[TestClass]
internal sealed class GeoJsonLexicalTests
{
    /// <summary>The example geometry literal of the geometry-extension requirements is well-formed.</summary>
    [TestMethod]
    public void ExampleLiteralWellFormed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From("{\"type\": \"Point\", \"coordinates\": [-83.38,33.95]}").Span));
    }

    /// <summary>An empty or all-whitespace body is well-formed — the empty geometry.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(" \t\r\n ")]
    public void EmptyBodyWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Every geometry type carrying the coordinates nesting that type fixes is well-formed.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],[3,4]]}")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1],[2,2]]}")]
    [DataRow("{\"type\":\"MultiLineString\",\"coordinates\":[[[0,0],[1,1]],[[2,2],[3,3]]]}")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[0,10],[10,10],[0,0]],[[1,1],[2,2],[1,2],[1,1]]]}")]
    [DataRow("{\"type\":\"MultiPolygon\",\"coordinates\":[[[[0,0],[1,0],[1,1],[0,0]]],[[[5,5],[6,5],[6,6],[5,5]]]]}")]
    public void GeometryTypesWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A geometry collection carries its members in <c>geometries</c>, a nested collection among them.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"LineString\",\"coordinates\":[[0,0],[1,1]]}]}")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2]},{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}]}]}")]
    public void GeometryCollectionWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A position carrying a third element is well-formed — element counts past the two the grammar fixes are semantics.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2,3]}")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[1,2,3],[4,5,6]]}")]
    public void ThreeElementPositionsWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>An empty array at any level of a coordinates value leaves the leaf depth unmeasured, which claims nothing against the type.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[]}")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[]}")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[[]]}")]
    [DataRow("{\"type\":\"MultiPolygon\",\"coordinates\":[[[]]]}")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[]}")]
    public void EmptyCoordinatesWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A bounding box of an even number of numbers is well-formed; the match against a coordinate dimension is semantics.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"bbox\":[0,0,1,1]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2,3],\"bbox\":[0,0,0,1,1,1]}")]
    public void BoundingBoxWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A foreign member is legal whatever its value, which is scanned for JSON validity and certifies nothing.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"title\":\"a place\"}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"properties\":{\"a\":[1,{\"b\":null}]}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"tags\":[1,\"a\",true,null]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"note\":null}")]
    public void ForeignMembersWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Certification of one object is order-independent: the object is decided at its closing brace.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"coordinates\":[-83.38,33.95],\"type\":\"Point\"}")]
    [DataRow("{\"bbox\":[0,0,1,1],\"coordinates\":[[0,0],[1,1]],\"type\":\"LineString\"}")]
    public void MemberOrderIndependentWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>The empty-geometry requirement's own illustration is a feature fragment, not a geometry object, and is malformed.</summary>
    [TestMethod]
    public void FeatureFragmentExampleMalformed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From("{\"geometry\": null}").Span));
    }

    /// <summary>A geometry object names its type in a string member, so an absent, non-string or non-geometry type is malformed.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":1,\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":null,\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":[\"Point\"],\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":{\"name\":\"Point\"},\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":true,\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Feature\",\"geometry\":null}")]
    [DataRow("{\"type\":\"FeatureCollection\",\"features\":[]}")]
    public void MissingOrNonStringTypeMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>The body is one JSON object, so every other JSON value is outside the grammar — a bare null among them.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("null")]
    [DataRow("[1,2]")]
    [DataRow("42")]
    [DataRow("\"Point\"")]
    [DataRow("true")]
    [DataRow("false")]
    public void NonObjectBodyMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>The member a type requires is not optional: coordinates for a geometry, geometries for a collection.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\"}")]
    [DataRow("{\"type\":\"GeometryCollection\"}")]
    public void MissingRequiredMemberMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Every element of a geometries array is a geometry object, so any other value there is malformed.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[1]}")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[\"Point\"]}")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[null]}")]
    public void GeometriesElementMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A measured position leaf sits at the depth the type fixes, and every leaf of one value sits at the same depth.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[[1,2]]}")]
    [DataRow("{\"type\":\"Polygon\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"MultiLineString\",\"coordinates\":[[[1,2]],[3,4]]}")]
    [DataRow("{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],[[3,4]]]}")]
    public void CoordinatesLeafDepthMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Below a coordinates value only arrays and numbers appear, an array holding one kind at a time, and a position holds two numbers at least.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1]}")]
    [DataRow("{\"type\":\"MultiPoint\",\"coordinates\":[[1],[2]]}")]
    [DataRow("{\"type\":\"MultiPoint\",\"coordinates\":[[1,2],3]}")]
    [DataRow("{\"type\":\"LineString\",\"coordinates\":[[1,[2,3]]]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":{\"x\":1}}")]
    public void CoordinatesElementMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A bounding box is a flat array holding two numbers per dimension, so an odd count or a non-number is malformed.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"bbox\":[0,0,1]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"bbox\":[0,0,[1,1]]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"bbox\":[\"0\",\"1\"]}")]
    public void BoundingBoxMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>
    /// The one recognized top-level crs value is the name form of the legacy CRS84 identifier, which
    /// asserts only the system this format fixes anyway and therefore abstains — wherever the member sits
    /// and whatever foreign members ride inside it.
    /// </summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}}}")]
    [DataRow("{\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}},\"type\":\"Point\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\",\"note\":[1,2]},\"type\":\"name\",\"note\":null}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"},\"properties\":7}}")]
    public void TopLevelCrsNameFormAbstains(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A spelling inside the crs object written with a backslash escape abstains rather than condemns — the decoded name is a claim the recognizer does not make.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"ty\\u0070e\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"na\\u006de\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\",\"properties\":{\"na\\u006de\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}}}")]
    public void TopLevelCrsEscapedSpellingAbstains(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Every other top-level crs value is malformed: the null, the link form, another system's name, a name form missing its half, and a value that is no object at all.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"crs\":null,\"type\":\"Point\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"link\",\"properties\":{\"href\":\"http://example.org/crs\",\"type\":\"proj4\"}}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:EPSG::3857\"}}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\"}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"},\"type\":1}}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":\"urn:ogc:def:crs:OGC:1.3:CRS84\"}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":[]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":1}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":true}")]
    public void TopLevelCrsOtherValuesMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A number carries no leading plus, no leading decimal point and no leading zeros, and its fraction and exponent carry digits.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[01,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[+1,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[.5,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1.,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1e,2]}")]
    public void NumberGrammarMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>An unescaped byte below the space inside a string is malformed.</summary>
    [TestMethod]
    public void UnescapedControlByteInStringMalformed()
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From("{\"type\":\"Point\",\"coordinates\":[1,2],\"note\":\"a\u0001b\"}").Span));
    }

    /// <summary>A string validates every escape, and it ends before the body does.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"note\":\"a\\qb\"}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"note\":\"\\u12\"}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"note\":\"abc}")]
    public void StringGrammarMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>The JSON structure admits no trailing comma and no unquoted member name, closes what it opens, and holds one top-level value.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2,]}")]
    [DataRow("{type:\"Point\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]} {}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2]}]")]
    public void JsonStructureMalformed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A member name or a type value written with a backslash escape abstains — its decoded spelling is a claim the recognizer does not make.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Poi\\u006et\",\"coordinates\":[1,2]}")]
    [DataRow("{\"ty\\u0070e\":\"Point\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordina\\u0074es\":[1,2]}")]
    public void EscapedSpellingAbstains(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A tracked member repeated in one object abstains — the grammar does not fix which occurrence binds.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"Point\",\"type\":\"Point\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Point\",\"type\":\"LineString\",\"coordinates\":[1,2]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"coordinates\":[3,4]}")]
    [DataRow("{\"type\":\"Point\",\"coordinates\":[1,2],\"bbox\":[0,0,1,1],\"bbox\":[0,0,1,1]}")]
    public void DuplicateTrackedMemberAbstains(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A crs member of a nested geometry object abstains — a key the specification reserves without giving it a form.</summary>
    /// <param name="body">The GeoJSON body under test.</param>
    [TestMethod]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":{\"type\":\"name\"}}]}")]
    [DataRow("{\"type\":\"GeometryCollection\",\"geometries\":[{\"type\":\"Point\",\"coordinates\":[1,2],\"crs\":null}]}")]
    public void NestedCrsMemberAbstains(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>Containers reaching exactly the cap, the top-level object counted first, are still recognized well-formed.</summary>
    [TestMethod]
    public void NestingAtCapWellFormed()
    {
        int arrays = GeoJsonLexical.MaximumNestingDepth - 1;
        string body = "{\"type\":\"Point\",\"coordinates\":[1,2],\"deep\":" + new string('[', arrays) + new string(']', arrays) + "}";

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A foreign value needing one container beyond the cap answers the depth outcome, not a grammar verdict.</summary>
    [TestMethod]
    public void NestingBeyondCapDepthExceeded()
    {
        int arrays = GeoJsonLexical.MaximumNestingDepth;
        string body = "{\"type\":\"Point\",\"coordinates\":[1,2],\"deep\":" + new string('[', arrays) + new string(']', arrays) + "}";

        Assert.AreEqual(GeometryLexicalRecognition.DepthExceeded, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A chain of geometry collections whose containers reach exactly the cap is well-formed.</summary>
    [TestMethod]
    public void GeometryCollectionChainAtCapWellFormed()
    {
        int levels = (GeoJsonLexical.MaximumNestingDepth - 2) / 2;
        string body = string.Concat(Enumerable.Repeat("{\"type\":\"GeometryCollection\",\"geometries\":[", levels)) + "{\"type\":\"Point\",\"coordinates\":[1,2]}" + string.Concat(Enumerable.Repeat("]}", levels));

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }

    /// <summary>A chain of geometry collections passing the cap answers the depth outcome, the same cap the coordinates path enforces.</summary>
    [TestMethod]
    public void GeometryCollectionChainBeyondCapDepthExceeded()
    {
        int levels = GeoJsonLexical.MaximumNestingDepth / 2;
        string body = string.Concat(Enumerable.Repeat("{\"type\":\"GeometryCollection\",\"geometries\":[", levels)) + "{\"type\":\"Point\",\"coordinates\":[1,2]}" + string.Concat(Enumerable.Repeat("]}", levels));

        Assert.AreEqual(GeometryLexicalRecognition.DepthExceeded, GeoJsonLexical.Recognize(Utf8Strings.From(body).Span));
    }
}
