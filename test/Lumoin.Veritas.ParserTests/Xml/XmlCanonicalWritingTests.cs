using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Lumoin.Veritas.Core.Xml;
using Lumoin.Veritas.Owl.Datatypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// The shared Canonical XML writing mechanics both canonicalization paths ride: the escaping of
/// text content and of attribute values, namespace-declaration recognition and parsing, the
/// qualified-name split, the sorted attribute axis, and the declaration write. These rows are the
/// primary pins for the paths the two end-to-end canonicalization suites never reach —
/// attribute-mode escaping of a real special byte, multi-byte pass-through, the sort-and-write
/// loop over non-namespace attributes, and a canonical form that outgrows the comparator's initial
/// buffer rental.
/// </summary>
[TestClass]
internal sealed class XmlCanonicalWritingTests
{
    /// <summary>
    /// The text run the growth-boundary row carries: long enough that its canonical form outgrows
    /// the comparator's initial rental, and carrying escaped characters throughout so the growth
    /// falls part-way through a write rather than neatly at a run boundary.
    /// </summary>
    private static string GrowthText { get; } = string.Concat(Enumerable.Repeat("alpha&amp;beta&lt;gamma ", 24));

    /// <summary>An attribute value escapes <c>&amp;</c>, <c>&lt;</c>, the double quote, tab, line feed and carriage return, and leaves <c>&gt;</c> alone.</summary>
    /// <param name="value">The raw value.</param>
    /// <param name="expected">The escaped form.</param>
    [TestMethod]
    [DataRow("a&b", "a&amp;b")]
    [DataRow("a<b", "a&lt;b")]
    [DataRow("a\"b", "a&quot;b")]
    [DataRow("a\tb", "a&#x9;b")]
    [DataRow("a\nb", "a&#xA;b")]
    [DataRow("a\rb", "a&#xD;b")]
    [DataRow("a>b", "a>b")]
    [DataRow("&<\"\t\n\r", "&amp;&lt;&quot;&#x9;&#xA;&#xD;")]
    [DataRow("", "")]
    [DataRow("plain", "plain")]
    public void WriteEscapedInAttributeModeEscapesTheAttributeSpecialSet(string value, string expected)
    {
        Assert.AreEqual(expected, Escaped(value, attribute: true));
    }

    /// <summary>Text content escapes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c> and carriage return, and leaves the double quote, tab and line feed alone.</summary>
    /// <param name="value">The raw value.</param>
    /// <param name="expected">The escaped form.</param>
    [TestMethod]
    [DataRow("a&b", "a&amp;b")]
    [DataRow("a<b", "a&lt;b")]
    [DataRow("a>b", "a&gt;b")]
    [DataRow("a\rb", "a&#xD;b")]
    [DataRow("a\"b", "a\"b")]
    [DataRow("a\tb", "a\tb")]
    [DataRow("a\nb", "a\nb")]
    [DataRow("&<>\r", "&amp;&lt;&gt;&#xD;")]
    [DataRow("", "")]
    [DataRow("plain", "plain")]
    public void WriteEscapedInTextModeEscapesTheTextSpecialSet(string value, string expected)
    {
        Assert.AreEqual(expected, Escaped(value, attribute: false));
    }

    /// <summary>A multi-byte UTF-8 sequence carries no special byte and passes through verbatim under both modes, so it is never split across an escape.</summary>
    [TestMethod]
    public void WriteEscapedPassesMultiByteSequencesThroughVerbatim()
    {
        const string MultiByte = "äö€😀";

        Assert.AreEqual(MultiByte, Escaped(MultiByte, attribute: false));
        Assert.AreEqual(MultiByte, Escaped(MultiByte, attribute: true));
        Assert.AreEqual("€&amp;😀", Escaped("€&😀", attribute: true));
    }

    /// <summary>An attribute name is a namespace declaration exactly when it is <c>xmlns</c> or begins with <c>xmlns:</c>; the bare separator form is recognized here and rejected only when the binding is read.</summary>
    /// <param name="name">The attribute's qualified name.</param>
    /// <param name="expected">Whether the name declares a namespace.</param>
    [TestMethod]
    [DataRow("xmlns", true)]
    [DataRow("xmlns:p", true)]
    [DataRow("xmlns:", true)]
    [DataRow("xmlnsx", false)]
    [DataRow("xmln", false)]
    [DataRow("id", false)]
    [DataRow("p:xmlns", false)]
    [DataRow("", false)]
    public void IsNamespaceDeclarationRecognizesTheDeclarationForms(string name, bool expected)
    {
        Assert.AreEqual(expected, XmlCanonicalWriting.IsNamespaceDeclaration(Encoding.UTF8.GetBytes(name)));
    }

    /// <summary>An <c>xmlns</c> attribute declares the default namespace: the empty prefix bound to the attribute's value.</summary>
    [TestMethod]
    public void TryReadDeclarationReadsTheDefaultNamespaceDeclaration()
    {
        Assert.IsTrue(XmlCanonicalWriting.TryReadDeclaration(Attribute("xmlns", "http://example.org/"), out XmlNamespaceBinding binding));
        Assert.IsTrue(binding.Prefix.IsEmpty);
        Assert.AreEqual("http://example.org/", binding.NamespaceIri.ToString());
    }

    /// <summary>An <c>xmlns:prefix</c> attribute declares that prefix, sliced out of the attribute name past the separator.</summary>
    [TestMethod]
    public void TryReadDeclarationReadsAPrefixedNamespaceDeclaration()
    {
        Assert.IsTrue(XmlCanonicalWriting.TryReadDeclaration(Attribute("xmlns:eg", "http://example.org/"), out XmlNamespaceBinding binding));
        Assert.AreEqual("eg", binding.Prefix.ToString());
        Assert.AreEqual("http://example.org/", binding.NamespaceIri.ToString());
    }

    /// <summary>An attribute that is not a declaration reports no binding.</summary>
    [TestMethod]
    public void TryReadDeclarationRejectsANonDeclarationAttribute()
    {
        Assert.IsFalse(XmlCanonicalWriting.TryReadDeclaration(Attribute("id", "x"), out XmlNamespaceBinding binding));
        Assert.IsTrue(binding.Prefix.IsEmpty);
        Assert.IsTrue(binding.NamespaceIri.IsEmpty);
    }

    /// <summary>The exact separator form <c>xmlns:</c> names no prefix, so it declares nothing — the boundary the length test guards.</summary>
    [TestMethod]
    public void TryReadDeclarationRejectsTheBareSeparatorName()
    {
        Assert.IsFalse(XmlCanonicalWriting.TryReadDeclaration(Attribute("xmlns:", "http://example.org/"), out XmlNamespaceBinding binding));
        Assert.IsTrue(binding.Prefix.IsEmpty);
        Assert.IsTrue(binding.NamespaceIri.IsEmpty);
    }

    /// <summary>The prefix of a qualified name is the part before its first colon, and empty when it carries none.</summary>
    /// <param name="qualified">The qualified name.</param>
    /// <param name="expected">The prefix.</param>
    [TestMethod]
    [DataRow("p:local", "p")]
    [DataRow("local", "")]
    [DataRow(":local", "")]
    [DataRow("a:b:c", "a")]
    [DataRow("p:", "p")]
    public void PrefixOfTakesThePartBeforeTheFirstColon(string qualified, string expected)
    {
        Assert.AreEqual(expected, Encoding.UTF8.GetString(XmlCanonicalWriting.PrefixOf(Encoding.UTF8.GetBytes(qualified))));
    }

    /// <summary>The local name of a qualified name is the part after its first colon, and the whole name when it carries none.</summary>
    /// <param name="qualified">The qualified name.</param>
    /// <param name="expected">The local name.</param>
    [TestMethod]
    [DataRow("p:local", "local")]
    [DataRow("local", "local")]
    [DataRow(":local", "local")]
    [DataRow("a:b:c", "b:c")]
    [DataRow("p:", "")]
    public void LocalNameOfTakesThePartAfterTheFirstColon(string qualified, string expected)
    {
        Assert.AreEqual(expected, XmlCanonicalWriting.LocalNameOf(new Utf8String(Encoding.UTF8.GetBytes(qualified))).ToString());
    }

    /// <summary>The attribute axis is ordered by namespace IRI then local name — never by the qualified name as written — and each value is escaped as an attribute value.</summary>
    [TestMethod]
    public void WriteSortedAttributesOrdersByNamespaceThenLocalName()
    {
        List<XmlSortedAttribute> attributes =
        [
            SortKeyed("z:b", "1", "http://example.org/aaa", "b"),
            SortKeyed("a:a", "2", "http://example.org/zzz", "a"),
            SortKeyed("c", "3&4", string.Empty, "c"),
            SortKeyed("z:a", "5", "http://example.org/aaa", "a")
        ];

        ArrayBufferWriter<byte> output = new();
        output.WriteSortedAttributes(attributes);

        Assert.AreEqual(" c=\"3&amp;4\" z:a=\"5\" z:b=\"1\" a:a=\"2\"", Written(output));
    }

    /// <summary>Two prefixes bound to one IRI carrying one local name share a sort key, and the axis writes them in the order the caller collected them.</summary>
    [TestMethod]
    public void WriteSortedAttributesKeepsTheCollectedOrderOfAnEqualKeyPair()
    {
        List<XmlSortedAttribute> attributes =
        [
            SortKeyed("z:v", "1", "http://example.org/ns", "v"),
            SortKeyed("a:v", "2", "http://example.org/ns", "v")
        ];

        ArrayBufferWriter<byte> output = new();
        output.WriteSortedAttributes(attributes);

        Assert.AreEqual(" z:v=\"1\" a:v=\"2\"", Written(output));
    }

    /// <summary>An empty prefix writes the default-namespace declaration form.</summary>
    [TestMethod]
    public void WriteDeclarationWritesTheDefaultNamespaceForm()
    {
        ArrayBufferWriter<byte> output = new();
        output.WriteDeclaration(default, "http://example.org/"u8);

        Assert.AreEqual(" xmlns=\"http://example.org/\"", Written(output));
    }

    /// <summary>A non-empty prefix writes the prefixed declaration form.</summary>
    [TestMethod]
    public void WriteDeclarationWritesThePrefixedForm()
    {
        ArrayBufferWriter<byte> output = new();
        output.WriteDeclaration("eg"u8, "http://example.org/"u8);

        Assert.AreEqual(" xmlns:eg=\"http://example.org/\"", Written(output));
    }

    /// <summary>A declaration's IRI is escaped as an attribute value.</summary>
    [TestMethod]
    public void WriteDeclarationEscapesTheNamespaceIri()
    {
        ArrayBufferWriter<byte> output = new();
        output.WriteDeclaration("eg"u8, "http://example.org/?a=1&b=2"u8);

        Assert.AreEqual(" xmlns:eg=\"http://example.org/?a=1&amp;b=2\"", Written(output));
    }

    /// <summary>An element's end tag is written from its qualified name as written.</summary>
    [TestMethod]
    public void WriteEndTagWritesTheQualifiedName()
    {
        ArrayBufferWriter<byte> output = new();
        output.WriteEndTag("eg:p"u8);

        Assert.AreEqual("</eg:p>", Written(output));
    }

    /// <summary>
    /// A canonical form larger than the comparator's initial buffer rental is still compared by
    /// its full bytes: two forms that differ only in attribute order over a long escaped text run
    /// denote one value.
    /// </summary>
    [TestMethod]
    public void XmlLiteralCompareGrowsPastTheInitialRentalCapacity()
    {
        byte[] first = Encoding.UTF8.GetBytes("<e xmlns:z=\"http://example.org/aaa\" xmlns:a=\"http://example.org/zzz\" a:v=\"1\" z:v=\"2\">" + GrowthText + "</e>");
        byte[] second = Encoding.UTF8.GetBytes("<e xmlns:z=\"http://example.org/aaa\" xmlns:a=\"http://example.org/zzz\" z:v=\"2\" a:v=\"1\">" + GrowthText + "</e>");

        Assert.AreEqual(DatatypeValueIdentity.Same, XmlLiteralValues.Compare(first, second));
    }

    /// <summary>Escapes a value through the shared writer and decodes the result for comparison.</summary>
    /// <param name="value">The raw value.</param>
    /// <param name="attribute">Whether to escape as an attribute value rather than as text content.</param>
    /// <returns>The escaped form.</returns>
    private static string Escaped(string value, bool attribute)
    {
        ArrayBufferWriter<byte> output = new();
        output.WriteEscaped(Encoding.UTF8.GetBytes(value), attribute);

        return Written(output);
    }

    /// <summary>Decodes everything written into a buffer.</summary>
    /// <param name="output">The buffer written into.</param>
    /// <returns>The written bytes as text.</returns>
    private static string Written(ArrayBufferWriter<byte> output)
    {
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    /// <summary>Builds a scanned attribute from its qualified name and value; the source offsets play no part in canonical writing.</summary>
    /// <param name="name">The attribute's qualified name as written.</param>
    /// <param name="value">The attribute's value.</param>
    /// <returns>The attribute.</returns>
    private static XmlScanAttribute Attribute(string name, string value)
    {
        return new XmlScanAttribute(new Utf8String(Encoding.UTF8.GetBytes(name)), new Utf8String(Encoding.UTF8.GetBytes(value)), NameStart: 0, End: 0);
    }

    /// <summary>Builds an attribute carrying an explicit sort key, so a row pins the ordering independently of how a caller resolves the key.</summary>
    /// <param name="name">The attribute's qualified name as written.</param>
    /// <param name="value">The attribute's value.</param>
    /// <param name="namespaceIri">The namespace IRI sort key.</param>
    /// <param name="localName">The local-name sort key.</param>
    /// <returns>The attribute with its sort key.</returns>
    private static XmlSortedAttribute SortKeyed(string name, string value, string namespaceIri, string localName)
    {
        return new XmlSortedAttribute(Attribute(name, value), new Utf8String(Encoding.UTF8.GetBytes(namespaceIri)), new Utf8String(Encoding.UTF8.GetBytes(localName)));
    }
}
