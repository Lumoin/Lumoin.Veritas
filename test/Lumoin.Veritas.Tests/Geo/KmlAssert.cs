using System.Text;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// Shared assertion machinery for the KML codec families: an acceptance drain that
/// returns the materialized value, refusal asserts that pin the kind AND the byte
/// offset with the geometry out at its default, and the structural cross-check
/// against a well-known-text reading of the same value. The reader has no system
/// out — the format fixes its coordinate system — so no assert carries one.
/// Offsets are computed from markers, never hand-counted.
/// </summary>
internal static class KmlAssert
{
    /// <summary>The UTF-8 byte offset of a marker's first occurrence in a document.</summary>
    public static int ByteOffsetOf(string document, string marker)
    {
        return XmlScannerAssert.ByteOffsetOf(document, marker);
    }

    /// <summary>Reads a document that must be accepted, returning the value.</summary>
    public static FlatGeometry Accepts(string document)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(document);
        bool accepted = KmlGeometryReader.TryRead(utf8, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        Assert.IsTrue(accepted, $"'{document}' must be accepted, but refused {refusal.Kind} at {refusal.ByteOffset}");
        Assert.AreEqual(GeometryCodecRefusal.None, refusal, "an accepted read reports the no-offense sentinel");

        return geometry;
    }

    /// <summary>Reads a document that must refuse, asserting the kind, the offset, and the defaulted geometry out.</summary>
    public static void Refuses(string document, GeometryCodecRefusalKind kind, int expectedOffset)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(document);
        bool accepted = KmlGeometryReader.TryRead(utf8, out FlatGeometry geometry, out GeometryCodecRefusal refusal);
        Assert.IsFalse(accepted, $"'{document}' must refuse");
        Assert.AreEqual(kind, refusal.Kind, $"the refusal kind for '{document}'");
        Assert.AreEqual(expectedOffset, refusal.ByteOffset, $"the refusal offset for '{document}'");
        Assert.AreEqual(default, geometry, "the geometry out is default on every refusal");
    }

    /// <summary>Reads a document that must refuse, with the offset computed from the first occurrence of a marker.</summary>
    public static void RefusesAt(string document, GeometryCodecRefusalKind kind, string offendingMarker)
    {
        Refuses(document, kind, ByteOffsetOf(document, offendingMarker));
    }

    /// <summary>Asserts a KML reading equals a well-known-text reading of the same value, structurally and bitwise.</summary>
    public static void MatchesWkt(string kmlDocument, string wkt)
    {
        using FlatGeometry fromKml = Accepts(kmlDocument);
        byte[] wktBytes = Encoding.UTF8.GetBytes(wkt);
        bool parsed = WktGeometryReader.TryRead(wktBytes, out FlatGeometry fromWkt, out _);
        Assert.IsTrue(parsed, $"the reference text '{wkt}' must parse");

        using(fromWkt)
        {
            Assert.AreEqual(fromWkt, fromKml, $"the KML reading of '{kmlDocument}' must equal the reference '{wkt}' structurally and bitwise");
        }
    }
}
