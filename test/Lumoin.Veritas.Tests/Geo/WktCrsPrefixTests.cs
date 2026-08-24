using Lumoin.Veritas.Geo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The <c>geo:wktLiteral</c> CRS-prefix decomposition: the defaulted and explicit CRS cases with
/// their source tags, the empty-body forms, the
/// rejected prefix structures, and the zero-copy slice contract.
/// </summary>
[TestClass]
internal sealed class WktCrsPrefixTests
{
    /// <summary>A form with no prefix is entirely body under the defaulted CRS84.</summary>
    [TestMethod]
    public void DefaultedWithoutPrefix()
    {
        Utf8String lexical = Utf8Strings.From("POINT(1 2)");

        Assert.IsTrue(WktCrsPrefix.TryParse(lexical, out WktCrsPrefix decomposition));
        Assert.AreEqual(WktCrsSource.Defaulted, decomposition.Source);
        Assert.IsTrue(decomposition.CrsIri.Span.SequenceEqual("http://www.opengis.net/def/crs/OGC/1.3/CRS84"u8));
        Assert.IsTrue(decomposition.Body.Span.SequenceEqual("POINT(1 2)"u8));
    }

    /// <summary>An explicit prefix yields its IRI, the explicit source tag, and the body after the separator.</summary>
    [TestMethod]
    public void ExplicitPrefixDecomposes()
    {
        Utf8String lexical = Utf8Strings.From("<http://www.opengis.net/def/crs/EPSG/0/4326> POINT(1 2)");

        Assert.IsTrue(WktCrsPrefix.TryParse(lexical, out WktCrsPrefix decomposition));
        Assert.AreEqual(WktCrsSource.Explicit, decomposition.Source);
        Assert.IsTrue(decomposition.CrsIri.Span.SequenceEqual("http://www.opengis.net/def/crs/EPSG/0/4326"u8));
        Assert.IsTrue(decomposition.Body.Span.SequenceEqual("POINT(1 2)"u8));
    }

    /// <summary>Any run of whitespace separates the prefix from the body.</summary>
    [TestMethod]
    public void ExplicitPrefixMultipleWhitespace()
    {
        Utf8String lexical = Utf8Strings.From("<http://example.org/crs>\t\r\n  POINT(1 2)");

        Assert.IsTrue(WktCrsPrefix.TryParse(lexical, out WktCrsPrefix decomposition));
        Assert.AreEqual(WktCrsSource.Explicit, decomposition.Source);
        Assert.IsTrue(decomposition.Body.Span.SequenceEqual("POINT(1 2)"u8));
    }

    /// <summary>An empty or all-whitespace form is an empty body under the defaulted CRS — the empty geometry's lexical form.</summary>
    [TestMethod]
    public void EmptyFormIsEmptyBodyDefaulted()
    {
        Assert.IsTrue(WktCrsPrefix.TryParse(Utf8String.Empty, out WktCrsPrefix empty));
        Assert.AreEqual(WktCrsSource.Defaulted, empty.Source);
        Assert.IsTrue(empty.Body.IsEmpty);

        Assert.IsTrue(WktCrsPrefix.TryParse(Utf8Strings.From("  "), out WktCrsPrefix whitespaceOnly));
        Assert.AreEqual(WktCrsSource.Defaulted, whitespaceOnly.Source);
        Assert.IsTrue(whitespaceOnly.Body.IsEmpty);
    }

    /// <summary>An explicit prefix with nothing or only whitespace after it is an empty body — an empty geometry with an explicit CRS.</summary>
    /// <param name="form">The lexical form under test.</param>
    [TestMethod]
    [DataRow("<http://example.org/crs>")]
    [DataRow("<http://example.org/crs> ")]
    public void ExplicitPrefixEmptyBody(string form)
    {
        Assert.IsTrue(WktCrsPrefix.TryParse(Utf8Strings.From(form), out WktCrsPrefix decomposition));
        Assert.AreEqual(WktCrsSource.Explicit, decomposition.Source);
        Assert.IsTrue(decomposition.CrsIri.Span.SequenceEqual("http://example.org/crs"u8));
        Assert.IsTrue(decomposition.Body.IsEmpty);
    }

    /// <summary>A broken prefix structure is rejected: unclosed, empty, whitespace or angle bracket inside the IRI, or a missing separator.</summary>
    /// <param name="form">The lexical form under test.</param>
    [TestMethod]
    [DataRow("<http://example.org/crs POINT(1 2)")]
    [DataRow("<> POINT(1 2)")]
    [DataRow("<http://example.org/a b> POINT(1 2)")]
    [DataRow("<http://example.org/<x> POINT(1 2)")]
    [DataRow("<http://example.org/crs>POINT(1 2)")]
    public void MalformedPrefixRejected(string form)
    {
        Assert.IsFalse(WktCrsPrefix.TryParse(Utf8Strings.From(form), out _));
    }

    /// <summary>The body is a zero-copy slice of the input, never a copy.</summary>
    [TestMethod]
    public void BodyIsZeroCopySlice()
    {
        Utf8String lexical = Utf8Strings.From("<http://a> POINT(1 2)");

        Assert.IsTrue(WktCrsPrefix.TryParse(lexical, out WktCrsPrefix decomposition));
        Assert.IsTrue(lexical.Span[11..] == decomposition.Body.Span);
        Assert.IsTrue(lexical.Span[1..9] == decomposition.CrsIri.Span);
    }
}
