using System;
using System.Text;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// Verifies the shared byte-native <see cref="XmlByteReader"/>: it builds the element tree, resolves namespace
/// scopes (including the rule that an unprefixed attribute is in no namespace), decodes predefined and numeric
/// references in text and attribute values, normalizes literal line endings and attribute whitespace per XML 1.0
/// (§2.11/§3.3.3) while preserving reference-introduced whitespace, takes CDATA content verbatim apart from that
/// line-ending normalization while ignoring comments, and rejects malformed input and a DTD with
/// <see cref="FormatException"/>.
/// </summary>
[TestClass]
internal sealed class XmlByteReaderTests
{
    /// <summary>Reads an XML string into its element tree.</summary>
    /// <param name="xml">The XML document text.</param>
    /// <returns>The document element.</returns>
    private static XmlByteNode Read(string xml)
    {
        return XmlByteReader.Read(Encoding.UTF8.GetBytes(xml));
    }

    /// <summary>Reads an XML string with DOCTYPE internal-subset entity parsing enabled.</summary>
    /// <param name="xml">The XML document text.</param>
    /// <returns>The document element.</returns>
    private static XmlByteNode ReadDtd(string xml)
    {
        return XmlByteReader.Read(Encoding.UTF8.GetBytes(xml), parseInternalDtd: true);
    }

    /// <summary>Elements, attributes, and text round-trip into the tree.</summary>
    [TestMethod]
    public void ParsesElementsAttributesAndText()
    {
        XmlByteNode root = Read("<r a=\"1\"><c>text</c></r>");

        Assert.AreEqual("r", root.LocalName.ToString());
        Assert.AreEqual("1", root.Attribute("a"u8)?.ToString());

        XmlByteNode? c = root.Element("c"u8, default);
        Assert.IsNotNull(c);
        Assert.AreEqual("text", c!.Text.ToString());
    }

    /// <summary>The default namespace types unprefixed elements; a prefix resolves through its declaration.</summary>
    [TestMethod]
    public void ResolvesDefaultAndPrefixedNamespaces()
    {
        XmlByteNode root = Read("<r xmlns=\"urn:d\" xmlns:p=\"urn:p\"><p:c/></r>");

        Assert.AreEqual("urn:d", root.NamespaceIri.ToString());
        Assert.IsNotNull(root.Element("c"u8, "urn:p"u8));
    }

    /// <summary>An unprefixed attribute is in no namespace, not the element's default namespace.</summary>
    [TestMethod]
    public void UnprefixedAttributeIsInNoNamespace()
    {
        XmlByteNode root = Read("<r xmlns=\"urn:d\" a=\"v\"/>");

        Assert.AreEqual("v", root.Attribute("a"u8)?.ToString());
        Assert.IsNull(root.Attribute("a"u8, "urn:d"u8));
    }

    /// <summary>Predefined and numeric references decode in both text and attribute values.</summary>
    [TestMethod]
    public void DecodesPredefinedAndNumericReferences()
    {
        XmlByteNode root = Read("<r a=\"x&amp;y\">&lt;&#65;&#x42;</r>");

        Assert.AreEqual("x&y", root.Attribute("a"u8)?.ToString());
        Assert.AreEqual("<AB", root.Text.ToString());
    }

    /// <summary>CDATA content is verbatim (its markup and entities are not interpreted); comments contribute no text.</summary>
    [TestMethod]
    public void CdataIsVerbatimAndCommentsAreSkipped()
    {
        XmlByteNode root = Read("<r>a<!--c--><![CDATA[<x>&amp;]]>b</r>");

        Assert.AreEqual("a<x>&amp;b", root.Text.ToString());
    }

    /// <summary>The <c>xml</c> prefix is always bound to the XML namespace.</summary>
    [TestMethod]
    public void XmlLangResolvesToTheXmlNamespace()
    {
        XmlByteNode root = Read("<r xml:lang=\"en\"/>");

        Assert.AreEqual("en", root.Attribute("lang"u8, "http://www.w3.org/XML/1998/namespace"u8)?.ToString());
    }

    /// <summary>An empty element and a self-closing element both produce a childless, textless node.</summary>
    [TestMethod]
    public void HandlesEmptyAndSelfClosingElements()
    {
        XmlByteNode root = Read("<r><a/><b></b></r>");

        Assert.HasCount(2, root.Children);
        Assert.AreEqual("a", root.Children[0].LocalName.ToString());
        Assert.IsTrue(root.Children[1].Text.IsEmpty);
    }

    /// <summary>Malformed input — a mismatched end tag, an unclosed element, a DTD, or no root — is rejected.</summary>
    [TestMethod]
    public void RejectsMalformedInputAndDtd()
    {
        Assert.ThrowsExactly<FormatException>(() => Read("<r><c></r>"));
        Assert.ThrowsExactly<FormatException>(() => Read("<r>"));
        Assert.ThrowsExactly<FormatException>(() => Read("<!DOCTYPE r><r/>"));
        Assert.ThrowsExactly<FormatException>(() => Read(""));
    }

    /// <summary>With DTD parsing on, internal-subset general entities expand in text and attribute values.</summary>
    [TestMethod]
    public void ExpandsInternalDtdEntities()
    {
        XmlByteNode root = ReadDtd("<!DOCTYPE r [ <!ENTITY ex \"urn:x:\"> ]><r a=\"&ex;v\">&ex;t</r>");

        Assert.AreEqual("urn:x:v", root.Attribute("a"u8)?.ToString());
        Assert.AreEqual("urn:x:t", root.Text.ToString());
    }

    /// <summary>A DTD is rejected unless internal-subset parsing is explicitly enabled.</summary>
    [TestMethod]
    public void RejectsDtdUnlessEnabled()
    {
        Assert.ThrowsExactly<FormatException>(() => Read("<!DOCTYPE r [ <!ENTITY ex \"urn:x:\"> ]><r/>"));
    }

    /// <summary>A reference to an undefined general entity is rejected even with DTD parsing on.</summary>
    [TestMethod]
    public void RejectsUndefinedEntity()
    {
        Assert.ThrowsExactly<FormatException>(() => ReadDtd("<!DOCTYPE r []><r>&missing;</r>"));
    }

    /// <summary>A start tag naming the same attribute twice is rejected (the XML 1.0 Unique Att Spec well-formedness constraint).</summary>
    [TestMethod]
    public void RejectsDuplicateAttribute()
    {
        Assert.ThrowsExactly<FormatException>(() => Read("<r a=\"1\" a=\"2\"/>"));
    }

    /// <summary>A numeric character reference outside the XML Char production — a C0 control, a noncharacter, or out of range — is rejected.</summary>
    [TestMethod]
    public void RejectsInvalidNumericCharacterReference()
    {
        Assert.ThrowsExactly<FormatException>(() => Read("<r>&#0;</r>"));
        Assert.ThrowsExactly<FormatException>(() => Read("<r>&#xFFFF;</r>"));
        Assert.ThrowsExactly<FormatException>(() => Read("<r>&#x110000;</r>"));
    }

    /// <summary>In text content, a literal CRLF or lone CR normalizes to a single LF, a literal LF is unchanged, and each line break collapses independently (XML 1.0 §2.11).</summary>
    [TestMethod]
    public void NormalizesTextLineEndingsToLineFeed()
    {
        Assert.AreEqual("a\nb", Read("<r>a\r\nb</r>").Text.ToString(), "A literal CRLF becomes a single LF.");
        Assert.AreEqual("a\nb", Read("<r>a\rb</r>").Text.ToString(), "A lone literal CR becomes an LF.");
        Assert.AreEqual("a\nb", Read("<r>a\nb</r>").Text.ToString(), "A literal LF is unchanged.");
        Assert.AreEqual("a\n\nb", Read("<r>a\r\n\r\nb</r>").Text.ToString(), "Each CRLF collapses to one LF independently.");
    }

    /// <summary>CDATA content has its literal line endings normalized to LF (§2.11), even though its markup and references stay inert.</summary>
    [TestMethod]
    public void NormalizesCdataLineEndingsToLineFeed()
    {
        Assert.AreEqual("x\ny", Read("<r><![CDATA[x\r\ny]]></r>").Text.ToString(), "A CRLF in CDATA becomes a single LF.");
        Assert.AreEqual("x\ny", Read("<r><![CDATA[x\ry]]></r>").Text.ToString(), "A lone CR in CDATA becomes an LF.");
    }

    /// <summary>In an attribute value, each literal tab and line break becomes a single space, a CRLF becomes one space, and literal space runs are not collapsed because the attribute is CDATA-typed (§3.3.3).</summary>
    [TestMethod]
    public void NormalizesAttributeWhitespaceToSpaces()
    {
        Assert.AreEqual("a b", Read("<r v=\"a\tb\"/>").Attribute("v"u8)?.ToString(), "A literal tab becomes a space.");
        Assert.AreEqual("a b", Read("<r v=\"a\nb\"/>").Attribute("v"u8)?.ToString(), "A literal LF becomes a space.");
        Assert.AreEqual("a b", Read("<r v=\"a\r\nb\"/>").Attribute("v"u8)?.ToString(), "A literal CRLF becomes a single space.");
        Assert.AreEqual("a  b", Read("<r v=\"a  b\"/>").Attribute("v"u8)?.ToString(), "Literal space runs are not collapsed (the attribute is CDATA-typed).");
    }

    /// <summary>Whitespace introduced by a character reference is the referenced character verbatim, never line-ending- or attribute-normalized: a CR reference stays CR in text and in an attribute, and a tab reference stays a tab in an attribute.</summary>
    [TestMethod]
    public void PreservesReferenceIntroducedWhitespace()
    {
        Assert.AreEqual("a\rb", Read("<r>a&#xD;b</r>").Text.ToString(), "A CR character reference in text is preserved, not normalized to LF.");
        Assert.AreEqual("a\rb", Read("<r v=\"a&#xD;b\"/>").Attribute("v"u8)?.ToString(), "A CR character reference in an attribute is preserved, not mapped to a space.");
        Assert.AreEqual("a\tb", Read("<r v=\"a&#x9;b\"/>").Attribute("v"u8)?.ToString(), "A tab character reference in an attribute is preserved, not mapped to a space.");
    }

    /// <summary>Within one attribute value, literal whitespace normalizes to a space while reference-introduced whitespace is preserved — the two are distinguished as the value is decoded, not by a post-pass.</summary>
    [TestMethod]
    public void DistinguishesLiteralFromReferencedWhitespaceInOneValue()
    {
        Assert.AreEqual("a b\tc", Read("<r v=\"a\tb&#x9;c\"/>").Attribute("v"u8)?.ToString(), "The literal tab becomes a space; the tab character reference stays a tab.");
    }

    /// <summary>A CRLF-authored document parses, with element text line endings normalized to LF.</summary>
    [TestMethod]
    public void ReadsACrlfAuthoredDocument()
    {
        XmlByteNode root = Read("<r>\r\n  <c>line1\r\nline2</c>\r\n</r>");

        XmlByteNode? c = root.Element("c"u8, default);
        Assert.IsNotNull(c);
        Assert.AreEqual("line1\nline2", c!.Text.ToString(), "Text content line endings are normalized to LF.");
    }
}
