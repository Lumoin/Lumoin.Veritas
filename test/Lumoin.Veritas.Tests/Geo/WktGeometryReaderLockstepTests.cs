using Lumoin.Veritas.Geo;
using Lumoin.Veritas.Geo.SimpleFeatures;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The nesting-depth lockstep between the lexical recognizer and the structural
/// reader: the reader parses every collection nesting <see cref="WktLexical"/>
/// certifies and nothing deeper. Both rows derive their depth from
/// <see cref="WktLexical.MaximumNestingDepth"/>, so a move of the cap breaks them
/// instead of silently splitting the layers.
/// </summary>
[TestClass]
internal sealed class WktGeometryReaderLockstepTests
{
    /// <summary>Nesting at the shared cap: the recognizer certifies and the reader parses.</summary>
    [TestMethod]
    public void NestingAtTheSharedCapCertifiesAndParses()
    {
        string text = NestedCollections(WktLexical.MaximumNestingDepth - 1);

        Assert.AreEqual(GeometryLexicalRecognition.WellFormed, WktLexical.Recognize(Utf8Strings.From(text).Span, out _),
            "The recognizer certifies nesting at its own cap.");
        Assert.IsTrue(WktGeometryReader.TryRead(text, out _, out _),
            "The reader parses everything the recognizer certifies.");
    }

    /// <summary>Nesting beyond the shared cap: the recognizer withholds well-formedness and the reader rejects.</summary>
    [TestMethod]
    public void NestingBeyondTheSharedCapRefusesOnBothLayers()
    {
        string text = NestedCollections(WktLexical.MaximumNestingDepth);

        Assert.AreNotEqual(GeometryLexicalRecognition.WellFormed, WktLexical.Recognize(Utf8Strings.From(text).Span, out _),
            "The recognizer never certifies nesting beyond its cap.");
        Assert.IsFalse(WktGeometryReader.TryRead(text, out _, out _),
            "The reader parses nothing deeper than the recognizer certifies.");
    }

    /// <summary>Builds a point nested under the given number of collection wrappers.</summary>
    private static string NestedCollections(int collections)
    {
        return string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", collections)) + "POINT(1 2)" + new string(')', collections);
    }
}
