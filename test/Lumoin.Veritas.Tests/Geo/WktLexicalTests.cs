using Lumoin.Veritas.Geo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The WKT span recognizer's battery:
/// well-formed bodies across the certified tag roster, the empty-geometry forms, provably malformed
/// bodies, the curve-tag abstentions, and nested <c>GEOMETRYCOLLECTION</c> at and beyond the hard
/// nesting cap.
/// </summary>
[TestClass]
internal sealed class WktLexicalTests
{
    /// <summary>A well-formed body across the certified tags, casings, modifiers, and number shapes.</summary>
    /// <param name="body">The WKT body under test.</param>
    [TestMethod]
    [DataRow("POINT(1 2)")]
    [DataRow("Point(-83.4 34.4)")]
    [DataRow("POINT ZM (1 2 3 4)")]
    [DataRow("POINT EMPTY")]
    [DataRow("LINESTRING(0 0, 1 1, 2 2)")]
    [DataRow("POLYGON((0 0, 0 10, 10 10, 0 0), (1 1, 2 2, 1 2, 1 1))")]
    [DataRow("MULTIPOINT(1 2, 3 4)")]
    [DataRow("MULTIPOINT((1 2), (3 4))")]
    [DataRow("MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))")]
    [DataRow("GEOMETRYCOLLECTION(POINT(1 2), LINESTRING(0 0, 1 1))")]
    [DataRow("GEOMETRYCOLLECTION(POINT EMPTY)")]
    [DataRow("POINT(1.5e10 -2.5E-3)")]
    [DataRow("TIN(((0 0, 1 0, 0 1, 0 0)))")]
    public void WellFormedBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, WktLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>An empty or all-whitespace body is well-formed — the empty geometry.</summary>
    /// <param name="body">The WKT body under test.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void EmptyBodyWellFormed(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, WktLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>A provably malformed body: unknown tags, broken structure, bad numbers, arity breaks, and trailing content.</summary>
    /// <param name="body">The WKT body under test.</param>
    [TestMethod]
    [DataRow("not a geometry")]
    [DataRow("POINT")]
    [DataRow("POINT()")]
    [DataRow("POINT(1)")]
    [DataRow("POINT(1 2")]
    [DataRow("POINT(1 2))")]
    [DataRow("POINT(a b)")]
    [DataRow("POINT(1, 2)")]
    [DataRow("POINT(1 2 3 4 5)")]
    [DataRow("POINTZM(1 2 3 4)")]
    [DataRow("POINT(1 2) POINT(3 4)")]
    [DataRow("LINESTRING(0 0,)")]
    [DataRow("POLYGON(0 0, 1 1, 2 2)")]
    [DataRow("GEOMETRYCOLLECTION()")]
    [DataRow("MULTIPOINT((1 2) 3)")]
    [DataRow("SRID=4326;POINT(1 2)")]
    public void MalformedBodies(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Malformed, WktLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>A curve tag inside the roster abstains — its content grammar is not certified, so no verdict is claimed.</summary>
    /// <param name="body">The WKT body under test.</param>
    [TestMethod]
    [DataRow("CIRCULARSTRING(0 0, 1 1, 2 0)")]
    [DataRow("COMPOUNDCURVE(CIRCULARSTRING(0 0, 1 1, 2 0), (2 0, 3 0))")]
    [DataRow("CURVEPOLYGON(CIRCULARSTRING(0 0, 4 0, 4 4, 0 4, 0 0))")]
    [DataRow("MULTICURVE((0 0, 1 1))")]
    [DataRow("MULTISURFACE(((0 0, 1 0, 1 1, 0 0)))")]
    [DataRow("GEOMETRYCOLLECTION(CIRCULARSTRING(0 0, 1 1, 2 0))")]
    public void CurveTagsAbstain(string body)
    {
        Assert.AreEqual(GeometryLexicalRecognition.Unrecognized, WktLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>Nesting that reaches exactly the cap is still recognized well-formed.</summary>
    [TestMethod]
    public void NestingAtCapWellFormed()
    {
        int collections = WktLexical.MaximumNestingDepth - 1;
        string body = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", collections)) + "POINT(1 2)" + new string(')', collections);

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, WktLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }

    /// <summary>Nesting that needs one level beyond the cap answers the depth outcome, not a grammar verdict.</summary>
    [TestMethod]
    public void NestingBeyondCapDepthExceeded()
    {
        int collections = WktLexical.MaximumNestingDepth;
        string body = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", collections)) + "POINT(1 2)" + new string(')', collections);

        Assert.AreEqual(GeometryLexicalRecognition.DepthExceeded, WktLexical.Recognize(Utf8Strings.From(body).Span, out _));
    }
}
