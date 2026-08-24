using System;
using System.Text;

using Lumoin.Veritas.Geo.SimpleFeatures;
using Lumoin.Veritas.Geo.Xml;

namespace Lumoin.Veritas.Tests.Geo;

/// <summary>
/// The XML fragment scanner's conformance family: the prolog grammar, the
/// character and name productions, reference decoding with the
/// single-substitution guarantee, attribute-value normalization as the one
/// per-item pass, namespace resolution with the reserved-name constraints
/// and the binding arena's scope lifetime, the per-run CDATA-close ban, the
/// security floor's positional precedence, per-construct truncation, the
/// transport depth cap, token anchors and the decoded-to-document offset
/// map, and the cursor's terminal-refusal and misuse contracts. Refusal
/// rows assert the kind AND the byte offset, and offsets are computed from
/// markers, never hand-counted.
/// </summary>
[TestClass]
internal sealed class XmlFragmentScannerTests
{
    /// <summary>A raw invalid lead byte inside text refuses at that byte.</summary>
    private static byte[] InvalidLeadDocument { get; } =
        [(byte)'<', (byte)'a', (byte)'>', 0xFF, (byte)'<', (byte)'/', (byte)'a', (byte)'>'];

    /// <summary>A continuation byte outside its window refuses at the continuation position.</summary>
    private static byte[] InvalidContinuationDocument { get; } =
        [(byte)'<', (byte)'a', (byte)'>', 0xC3, 0x28, (byte)'<', (byte)'/', (byte)'a', (byte)'>'];

    /// <summary>An encoded surrogate refuses at the second byte, where the sequence leaves the valid window.</summary>
    private static byte[] EncodedSurrogateDocument { get; } =
        [(byte)'<', (byte)'a', (byte)'>', 0xED, 0xA0, 0x80, (byte)'<', (byte)'/', (byte)'a', (byte)'>'];

    /// <summary>A UTF-8 sequence cut by end of input is truncation at the input length.</summary>
    private static byte[] TruncatedSequenceDocument { get; } = [(byte)'<', (byte)'a', (byte)'>', 0xC3];

    /// <summary>An overlong two-byte encoding refuses at its lead, which is never a valid lead.</summary>
    private static byte[] OverlongTwoByteDocument { get; } =
        [(byte)'<', (byte)'a', (byte)'>', 0xC0, 0xAF, (byte)'<', (byte)'/', (byte)'a', (byte)'>'];

    /// <summary>An overlong three-byte encoding refuses at the continuation below its tightened window.</summary>
    private static byte[] OverlongThreeByteDocument { get; } =
        [(byte)'<', (byte)'a', (byte)'>', 0xE0, 0x9F, 0xBF, (byte)'<', (byte)'/', (byte)'a', (byte)'>'];

    /// <summary>A UTF-16 document refuses at its signature byte — the recorded single-encoding stance.</summary>
    private static byte[] Utf16Document { get; } =
        [0xFF, 0xFE, 0x3C, 0x00, 0x61, 0x00, 0x2F, 0x00, 0x3E, 0x00];

    [TestMethod]
    [DataRow(XmlTestDocuments.MinimalRoot, DisplayName = "the minimal empty-element root scans clean")]
    [DataRow("<a></a>", DisplayName = "the paired empty root scans clean")]
    [DataRow("<a></a >", DisplayName = "whitespace before the end tag's closing bracket is grammatical")]
    [DataRow("<a />", DisplayName = "whitespace before the empty-element slash is grammatical")]
    [DataRow("<a b = \"1\"/>", DisplayName = "whitespace around the equals sign is grammatical")]
    [DataRow(XmlTestDocuments.CanonicalDeclaration + "<a/>", DisplayName = "the canonical declaration is skipped")]
    [DataRow("<?xml version=\"1.0\"?><a/>", DisplayName = "a version-only declaration is skipped")]
    [DataRow("<?xml version = \"1.0\" ?><a/>", DisplayName = "declaration whitespace around the equals sign and before the close is grammatical")]
    [DataRow("<?xml version='1.0' encoding='utf-8' standalone='no'?><a/>", DisplayName = "single quotes, the lowercase encoding, and standalone no are accepted")]
    [DataRow("<?xml version=\"1.0\" standalone=\"yes\"?><a/>", DisplayName = "standalone yes without an encoding is accepted")]
    [DataRow("\uFEFF<a/>", DisplayName = "a leading byte-order mark is skipped")]
    [DataRow("\uFEFF" + XmlTestDocuments.CanonicalDeclaration + "<a/>", DisplayName = "a byte-order mark before the declaration is skipped")]
    [DataRow("<!--c--><a/>", DisplayName = "a comment before the root skips")]
    [DataRow("<a/><!--c-->", DisplayName = "a comment after the root skips")]
    [DataRow("<!--c--><a/><!--d--> ", DisplayName = "comments and whitespace surround the root freely")]
    [DataRow("<a><!-- a - b --></a>", DisplayName = "single hyphens inside a comment are legal")]
    [DataRow("<a>]]</a>", DisplayName = "two closing brackets without the closing angle are legal character data")]
    [DataRow("<a>a]]<!--x-->>b</a>", DisplayName = "the CDATA-close ban is per run, so a comment split defeats it")]
    [DataRow("<a b=\"a]]>b\"/>", DisplayName = "the CDATA-close sequence is legal inside an attribute value")]
    [DataRow("<a b=\"&lt;\"/>", DisplayName = "a decoded less-than in a value is inert replacement data")]
    [DataRow("<a xmlns=\"\"/>", DisplayName = "the default namespace un-declaration is legal")]
    [DataRow("<a xmlns=\"u\"><b xmlns=\"\"/></a>", DisplayName = "a nested un-declaration scopes normally")]
    [DataRow("<a xmlns:xml=\"" + XmlTestDocuments.XmlNamespace + "\"/>", DisplayName = "the xml prefix may be redeclared to exactly its own namespace")]
    [DataRow("<än/>", DisplayName = "a non-ASCII name-start character is accepted")]
    [DataRow("<日本>x</日本>", DisplayName = "a fully non-ASCII element name round-trips the tag match")]
    [DataRow("<a x=\"1\" y=\"2\"/>", DisplayName = "distinct attributes are accepted")]
    [DataRow("<a xml:lang=\"en\"/>", DisplayName = "an xml-prefixed attribute rides the permanent binding")]
    [DataRow("<a><b/><b/></a>", DisplayName = "sibling elements of one name are accepted")]
    [DataRow("<a>&gt;</a>", DisplayName = "the greater-than entity decodes in text")]
    [DataRow("<a><!--x<&y--></a>", DisplayName = "ampersands and angle brackets are literal inside a comment")]
    [DataRow("<a\r\nb=\"1\"/>", DisplayName = "a carriage-return line-feed pair separates attributes as tag whitespace")]
    [DataRow("<n-.0\u00B7\u0300\u203F/>", DisplayName = "the interior-only name characters are accepted")]
    [DataRow("<_\u00C0\u00F8\u0370\u1D00\u200C\u2070\u2C00\uF900\uFDF0/>", DisplayName = "every basic-plane name-start range is exercised in one composite name")]
    [DataRow("<\U00010000/>", DisplayName = "a supplementary-plane name-start character is accepted")]
    [DataRow("<a xmlns:p=\"u\" xmlns:q=\"U\" p:c=\"1\" q:c=\"2\"/>", DisplayName = "namespace names differing only in case are distinct")]
    [DataRow("<a xmlns:p=\"u%41\" xmlns:q=\"uA\" p:c=\"1\" q:c=\"2\"/>", DisplayName = "no percent-escaping is done or undone in namespace comparison")]
    [DataRow("<a xmlns:p=\"u\" xmlns:q=\"v\" p:c=\"1\" q:c=\"2\"/>", DisplayName = "one local name under two namespace names is two attributes")]
    [DataRow("<a xmlns:p=\"u\" b=\"1\" p:b=\"2\"/>", DisplayName = "an unprefixed and a namespaced attribute may share a local name")]
    public void AcceptedDocumentsScanToCleanExhaustion(string document)
    {
        XmlScannerAssert.Accepts(document);
    }

    [TestMethod]
    [DataRow("<?xml version=\"1.1\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "1.1", DisplayName = "a version other than one-point-zero refuses at the value, the recorded deviation")]
    [DataRow("<?xml encoding=\"UTF-8\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "encoding", DisplayName = "a declaration without the required version refuses at the intruder")]
    [DataRow("<?xml version=\"1.0\" encoding=\"latin-1\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "latin-1", DisplayName = "a foreign encoding declaration refuses at the value")]
    [DataRow("<?xml version=\"1.0\" standalone=\"maybe\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "maybe", DisplayName = "a standalone value outside yes and no refuses at the value")]
    [DataRow("<?xml version=\"1.0\" standalone=\"yes\" encoding=\"UTF-8\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "encoding", DisplayName = "encoding after standalone violates the pseudo-attribute order")]
    [DataRow("<?xml version=\"1.0\" foo=\"bar\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "foo", DisplayName = "an unknown pseudo-attribute refuses at its name")]
    [DataRow("<?xmlversion=\"1.0\"?><a/>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?xml", DisplayName = "without whitespace after the opening the bytes are a processing instruction")]
    [DataRow("<?XML version=\"1.0\"?><a/>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?XML", DisplayName = "the declaration opening is exact case, so the uppercase form is a processing instruction")]
    [DataRow("<?xml?><a/>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?xml", DisplayName = "a versionless declaration shape is a processing instruction")]
    [DataRow("<?xml version=\"1.0\"?><?pi?><a/>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?pi", DisplayName = "a processing instruction before the root refuses")]
    [DataRow("x<a/>", GeometryCodecRefusalKind.MalformedDocument, "x", DisplayName = "character data before the root refuses at its first byte")]
    [DataRow("<![CDATA[x]]><a/>", GeometryCodecRefusalKind.MalformedDocument, "<![CDATA[", DisplayName = "a CDATA section before the root refuses at its opening bracket")]
    [DataRow("</a><a/>", GeometryCodecRefusalKind.MalformedDocument, "</a", DisplayName = "a stray end tag before the root refuses at its opening bracket")]
    [DataRow("<!DOCTYPE a><a/>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!DOCTYPE", DisplayName = "a document type declaration refuses under the security floor")]
    [DataRow("<!ENTITY x \"y\"><a/>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!ENTITY", DisplayName = "an entity declaration refuses under the security floor")]
    [DataRow("<!x><a/>", GeometryCodecRefusalKind.MalformedDocument, "x>", DisplayName = "unrecognized exclamation markup refuses at the diverging byte")]
    [DataRow("<a><?pi?></a>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?pi", DisplayName = "a processing instruction in content refuses")]
    [DataRow("<a><!DOCTYPE b></a>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!DOCTYPE", DisplayName = "a document type declaration in content refuses")]
    [DataRow("<a><!ELEMENT b EMPTY></a>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!ELEMENT", DisplayName = "an element type declaration refuses under the security floor")]
    [DataRow("<a><!ATTLIST b c CDATA #IMPLIED></a>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!ATTLIST", DisplayName = "an attribute-list declaration refuses under the security floor")]
    [DataRow("<a><!NOTATION n SYSTEM \"s\"></a>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!NOTATION", DisplayName = "a notation declaration refuses under the security floor")]
    [DataRow("<a/><?pi?>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?pi", DisplayName = "the security floor outranks trailing position for a processing instruction")]
    [DataRow("<a/><!DOCTYPE b>", GeometryCodecRefusalKind.ProhibitedConstruct, "<!DOCTYPE", DisplayName = "the security floor outranks trailing position for a doctype")]
    [DataRow("<a/><b/>", GeometryCodecRefusalKind.TrailingContent, "<b", DisplayName = "a second root element is trailing content at its opening bracket")]
    [DataRow("<a/>x", GeometryCodecRefusalKind.TrailingContent, "x", DisplayName = "character data after the root is trailing content")]
    [DataRow("<a/><![CDATA[x]]>", GeometryCodecRefusalKind.TrailingContent, "<![CDATA[", DisplayName = "a CDATA section after the root is trailing content")]
    [DataRow("<a>x]]>y</a>", GeometryCodecRefusalKind.MalformedDocument, "]]>", DisplayName = "the CDATA-close sequence in character data refuses at its first bracket")]
    [DataRow("<a>\u0001</a>", GeometryCodecRefusalKind.MalformedDocument, "\u0001", DisplayName = "a control byte in text is outside the character production")]
    [DataRow("<a b=\"\u0001\"/>", GeometryCodecRefusalKind.MalformedDocument, "\u0001", DisplayName = "a control byte in an attribute value is outside the character production")]
    [DataRow("<a><!--\u0001--></a>", GeometryCodecRefusalKind.MalformedDocument, "\u0001", DisplayName = "a control byte inside a comment is outside the character production")]
    [DataRow("<a><![CDATA[\u0001]]></a>", GeometryCodecRefusalKind.MalformedDocument, "\u0001", DisplayName = "a control byte inside CDATA is outside the character production")]
    [DataRow("<a>\uFFFE</a>", GeometryCodecRefusalKind.MalformedDocument, "\uFFFE", DisplayName = "the permanent non-character refuses at its encoded bytes")]
    [DataRow("<a>&foo;</a>", GeometryCodecRefusalKind.MalformedDocument, "&foo", DisplayName = "an undeclared entity in text refuses at the ampersand")]
    [DataRow("<a>& x</a>", GeometryCodecRefusalKind.MalformedDocument, "&", DisplayName = "a bare ampersand in text refuses at the ampersand")]
    [DataRow("<a b=\"&foo;\"/>", GeometryCodecRefusalKind.MalformedDocument, "&foo", DisplayName = "an undeclared entity in a value refuses at the ampersand")]
    [DataRow("<a b=\"& \"/>", GeometryCodecRefusalKind.MalformedDocument, "&", DisplayName = "a bare ampersand in a value refuses at the ampersand")]
    [DataRow("<a>&#0;</a>", GeometryCodecRefusalKind.MalformedDocument, "&#0", DisplayName = "a character reference below the production refuses at the reference")]
    [DataRow("<a>&#xD800;</a>", GeometryCodecRefusalKind.MalformedDocument, "&#xD800", DisplayName = "a surrogate character reference refuses at the reference")]
    [DataRow("<a>&#xFFFF;</a>", GeometryCodecRefusalKind.MalformedDocument, "&#xFFFF", DisplayName = "a non-character reference refuses at the reference")]
    [DataRow("<a>&#x110000;</a>", GeometryCodecRefusalKind.MalformedDocument, "&#x110000", DisplayName = "a reference above the scalar ceiling refuses at the reference")]
    [DataRow("<a b=\"x<y\"/>", GeometryCodecRefusalKind.MalformedDocument, "<y", DisplayName = "a raw less-than in a value refuses at the bracket")]
    [DataRow("<a><!--a--b--></a>", GeometryCodecRefusalKind.MalformedDocument, "--b", DisplayName = "an interior double hyphen refuses at its first hyphen")]
    [DataRow("<a><!--B---></a>", GeometryCodecRefusalKind.MalformedDocument, "--->", DisplayName = "a triple-hyphen comment ending refuses at the first hyphen of the pair")]
    [DataRow("<1a/>", GeometryCodecRefusalKind.MalformedDocument, "1a", DisplayName = "a digit cannot start a name")]
    [DataRow("<a:b:c/>", GeometryCodecRefusalKind.MalformedDocument, ":c", DisplayName = "a second colon refuses at the colon")]
    [DataRow("<:a/>", GeometryCodecRefusalKind.MalformedDocument, ":a", DisplayName = "a leading colon is not a name start")]
    [DataRow("< a/>", GeometryCodecRefusalKind.MalformedDocument, " a", DisplayName = "whitespace between the bracket and the name refuses")]
    [DataRow("<a>x</ a>", GeometryCodecRefusalKind.MalformedDocument, " a>", DisplayName = "whitespace after the end-tag slash refuses")]
    [DataRow("<xmlns:e/>", GeometryCodecRefusalKind.MalformedDocument, "xmlns:e", DisplayName = "nothing but declarations may wear the xmlns prefix")]
    [DataRow("<a 1b=\"x\"/>", GeometryCodecRefusalKind.MalformedDocument, "1b", DisplayName = "a digit cannot start an attribute name")]
    [DataRow("<a b=\"1\"c=\"2\"/>", GeometryCodecRefusalKind.MalformedDocument, "c=", DisplayName = "attributes must be separated by whitespace")]
    [DataRow("<abc>x</abd>", GeometryCodecRefusalKind.MalformedDocument, "d>", DisplayName = "an end-tag mismatch refuses at the diverging byte")]
    [DataRow("<a>q</axy>", GeometryCodecRefusalKind.MalformedDocument, "xy>", DisplayName = "an end tag longer than the start tag refuses at the extra byte")]
    [DataRow("<q:a>x</q:a>", GeometryCodecRefusalKind.MalformedDocument, ">", DisplayName = "an undeclared element prefix refuses at the start tag's closing bracket")]
    [DataRow("<a q:b=\"1\">x</a>", GeometryCodecRefusalKind.MalformedDocument, ">", DisplayName = "an undeclared attribute prefix refuses at the start tag's closing bracket")]
    [DataRow("<a xmlns:xml=\"urn:x\"/>", GeometryCodecRefusalKind.MalformedDocument, "urn:x", DisplayName = "the xml prefix cannot rebind away from its namespace")]
    [DataRow("<a xmlns:q=\"" + XmlTestDocuments.XmlNamespace + "\"/>", GeometryCodecRefusalKind.MalformedDocument, "http", DisplayName = "no other prefix may bind to the xml namespace")]
    [DataRow("<a xmlns=\"" + XmlTestDocuments.XmlNamespace + "\"/>", GeometryCodecRefusalKind.MalformedDocument, "http", DisplayName = "the xml namespace cannot be the default namespace")]
    [DataRow("<a xmlns=\"" + XmlTestDocuments.XmlnsNamespace + "\"/>", GeometryCodecRefusalKind.MalformedDocument, "http", DisplayName = "the declaration namespace cannot be the default namespace")]
    [DataRow("<a xmlns:q=\"" + XmlTestDocuments.XmlnsNamespace + "\"/>", GeometryCodecRefusalKind.MalformedDocument, "http", DisplayName = "nothing may bind to the declaration namespace")]
    [DataRow("<a xmlns:xmlns=\"u\"/>", GeometryCodecRefusalKind.MalformedDocument, "xmlns:xmlns", DisplayName = "the xmlns prefix itself is never declarable")]
    [DataRow("<a xmlns:p=\"u\" xmlns:p=\"v\"/>", GeometryCodecRefusalKind.MalformedDocument, "xmlns:p=\"v", DisplayName = "a repeated declaration for one prefix refuses at the second occurrence")]
    [DataRow("<a xmlns=\"u\" xmlns=\"v\"/>", GeometryCodecRefusalKind.MalformedDocument, "xmlns=\"v", DisplayName = "a repeated default declaration refuses at the second occurrence")]
    [DataRow("<a b=\"x\" b=\"y\"/>", GeometryCodecRefusalKind.MalformedDocument, "b=\"y", DisplayName = "a repeated attribute name refuses at the second occurrence")]
    [DataRow("<a xmlns:p=\"u\" xmlns:q=\"u\" p:c=\"1\" q:c=\"2\"/>", GeometryCodecRefusalKind.MalformedDocument, "q:c", DisplayName = "an expanded-name duplicate through two prefixes refuses at the second occurrence")]
    [DataRow("<e q:a=\"1\" b=\"&bad;\"/>", GeometryCodecRefusalKind.MalformedDocument, "&bad", DisplayName = "a syntactic offense preempts an earlier binding-dependent one")]
    [DataRow("<a><?xml version=\"1.0\"?></a>", GeometryCodecRefusalKind.ProhibitedConstruct, "<?xml", DisplayName = "the declaration opening anywhere but the document start is a processing instruction")]
    [DataRow("<a b=\"\uFFFE\"/>", GeometryCodecRefusalKind.MalformedDocument, "\uFFFE", DisplayName = "the permanent non-character refuses inside an attribute value")]
    [DataRow("<a><!--\uFFFE--></a>", GeometryCodecRefusalKind.MalformedDocument, "\uFFFE", DisplayName = "the permanent non-character refuses inside a comment")]
    [DataRow("<a><![CDATA[\uFFFE]]></a>", GeometryCodecRefusalKind.MalformedDocument, "\uFFFE", DisplayName = "the permanent non-character refuses inside CDATA")]
    [DataRow("<a>\u000B</a>", GeometryCodecRefusalKind.MalformedDocument, "\u000B", DisplayName = "a vertical tab is neither white space nor a character")]
    [DataRow("<a><?", GeometryCodecRefusalKind.ProhibitedConstruct, "<?", DisplayName = "a processing instruction cut by end of input still refuses at its opening bracket")]
    [DataRow("<?xml version=\"1.0\" encoding=\"UTF-8\" encoding=\"utf-8\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "encoding=\"utf-8", DisplayName = "a repeated encoding pseudo-attribute refuses at the intruder")]
    [DataRow("<?xml version=\"1.0\" standalone=\"yes\" standalone=\"no\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "standalone=\"no", DisplayName = "a repeated standalone pseudo-attribute refuses at the intruder")]
    [DataRow("<?xml version=\"1.0\"encoding=\"UTF-8\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "encoding", DisplayName = "pseudo-attributes without separating whitespace refuse at the intruder")]
    [DataRow("<?xml version \"1.0\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "\"1.0", DisplayName = "a declaration missing its equals sign refuses at the value quote")]
    [DataRow("<?xml version=\"1.0\" standalone=\"YES\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "YES", DisplayName = "an uppercase standalone value is outside the literal grammar")]
    [DataRow("<?xml version=\"1.0\"?>\uFEFF<a/>", GeometryCodecRefusalKind.MalformedDocument, "\uFEFF", DisplayName = "a byte-order mark after the declaration is pre-root content")]
    [DataRow("<?xml version=\"1.0\" encoding=\"Utf-8\"?><a/>", GeometryCodecRefusalKind.MalformedDocument, "Utf-8", DisplayName = "an encoding spelling outside the two admitted forms refuses at the value")]
    [DataRow("<a:/>", GeometryCodecRefusalKind.MalformedDocument, "/>", DisplayName = "an empty local part after the colon refuses at the non-name byte")]
    [DataRow("<p:a xmlns:p=\"u\" xmlns:q=\"u\">x</q:a>", GeometryCodecRefusalKind.MalformedDocument, "q:a>", DisplayName = "an end tag agreeing only on expanded name still refuses at the diverging byte")]
    [DataRow("<a b=1/>", GeometryCodecRefusalKind.MalformedDocument, "1/", DisplayName = "an unquoted attribute value refuses at its first byte")]
    [DataRow("<a b \"1\"/>", GeometryCodecRefusalKind.MalformedDocument, "\"1", DisplayName = "an attribute without the equals sign refuses at the value's quote")]
    [DataRow("<a/ >", GeometryCodecRefusalKind.MalformedDocument, " >", DisplayName = "whitespace between the empty-element slash and its bracket refuses")]
    [DataRow("<e q:x=\"1\" b=\"2\" b=\"3\"/>", GeometryCodecRefusalKind.MalformedDocument, "b=\"3", DisplayName = "a literal duplicate preempts an earlier binding-dependent offense")]
    [DataRow("<a q:x=\"1\" q:x=\"2\"/>", GeometryCodecRefusalKind.MalformedDocument, "q:x=\"2", DisplayName = "a literal duplicate preempts the undeclared-prefix adjudication")]
    [DataRow("<a>&#4a;</a>", GeometryCodecRefusalKind.MalformedDocument, "&#4a", DisplayName = "a letter inside a decimal reference refuses at the ampersand")]
    [DataRow("<a>&amp</a>", GeometryCodecRefusalKind.MalformedDocument, "&amp", DisplayName = "an entity reference missing its semicolon refuses at the ampersand")]
    [DataRow("<a>&#65</a>", GeometryCodecRefusalKind.MalformedDocument, "&#65", DisplayName = "a character reference missing its semicolon refuses at the ampersand")]
    [DataRow("<a>&#18446744073709551681;</a>", GeometryCodecRefusalKind.MalformedDocument, "&#1844", DisplayName = "a digit run past the overflow clamp is adjudicated by value, never by wraparound")]
    public void RefusalRowsAnchorAtTheirMarkers(string document, GeometryCodecRefusalKind kind, string offendingMarker)
    {
        int offset = XmlScannerAssert.ByteOffsetOf(document, offendingMarker);

        XmlScannerAssert.Refuses(document, kind, offset);
    }

    [TestMethod]
    [DataRow("<", DisplayName = "a lone opening bracket is truncation")]
    [DataRow("<a", DisplayName = "an unterminated start tag is truncation")]
    [DataRow("<a/", DisplayName = "an unterminated empty-element tag is truncation")]
    [DataRow("<a foo=\"b", DisplayName = "an unterminated attribute value is truncation")]
    [DataRow("<a b='x", DisplayName = "an unterminated single-quoted value is truncation")]
    [DataRow("<a><!--ab", DisplayName = "an unterminated comment is truncation")]
    [DataRow("<!--x--", DisplayName = "a comment ending at its double hyphen is truncation")]
    [DataRow("<a><![CDATA[x", DisplayName = "an unterminated CDATA section is truncation")]
    [DataRow("<a>&#12", DisplayName = "a reference cut by end of input is truncation, not a bare ampersand")]
    [DataRow("<a>&am", DisplayName = "an entity name cut by end of input is truncation")]
    [DataRow("<a><b></b>", DisplayName = "an unclosed root element is truncation")]
    [DataRow("<a></a", DisplayName = "an unterminated end tag is truncation")]
    [DataRow("<a>text", DisplayName = "text with no closing tag is truncation")]
    [DataRow("<?xml version=\"1.0\"", DisplayName = "an unterminated declaration is truncation")]
    [DataRow("<!DOCT", DisplayName = "input ending inside a construct keyword is truncation")]
    [DataRow(" ", DisplayName = "a whitespace-only document never delivers a root")]
    [DataRow("\uFEFF", DisplayName = "a byte-order-mark-only document never delivers a root")]
    [DataRow("<?xml version=\"1.0'?><a/>", DisplayName = "a mismatched declaration quote never closes the value and truncates")]
    public void TruncationRefusesAtTheInputLength(string document)
    {
        int length = Encoding.UTF8.GetBytes(document).Length;

        XmlScannerAssert.Refuses(document, GeometryCodecRefusalKind.MalformedDocument, length);
    }

    [TestMethod]
    public void TheEmptyDocumentRefusesAtOffsetZero()
    {
        XmlScannerAssert.Refuses(string.Empty, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 0);
    }

    [TestMethod]
    public void InvalidUtf8RefusesAtTheFirstNonExtendableByte()
    {
        XmlScannerAssert.RefusesBytes(InvalidLeadDocument, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 3, "an invalid lead byte");
        XmlScannerAssert.RefusesBytes(InvalidContinuationDocument, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 4, "a continuation outside its window");
        XmlScannerAssert.RefusesBytes(EncodedSurrogateDocument, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 4, "an encoded surrogate");
        XmlScannerAssert.RefusesBytes(TruncatedSequenceDocument, GeometryCodecRefusalKind.MalformedDocument, TruncatedSequenceDocument.Length, "a sequence cut by end of input");
        XmlScannerAssert.RefusesBytes(OverlongTwoByteDocument, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 3, "an overlong two-byte encoding");
        XmlScannerAssert.RefusesBytes(OverlongThreeByteDocument, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 4, "an overlong three-byte encoding");
        XmlScannerAssert.RefusesBytes(Utf16Document, GeometryCodecRefusalKind.MalformedDocument, expectedOffset: 0, "a UTF-16 document with its signature");
    }

    [TestMethod]
    public void ADeclaredDocumentWithInvalidBytesRefusesAtTheFirstNonExtendableByte()
    {
        const string Prefix = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><a>";
        byte[] prefixBytes = Encoding.UTF8.GetBytes(Prefix);
        byte[] suffixBytes = Encoding.UTF8.GetBytes("</a>");
        byte[] document = new byte[prefixBytes.Length + 1 + suffixBytes.Length];
        prefixBytes.CopyTo(document, 0);
        document[prefixBytes.Length] = 0xFF;
        suffixBytes.CopyTo(document, prefixBytes.Length + 1);

        XmlScannerAssert.RefusesBytes(document, GeometryCodecRefusalKind.MalformedDocument, prefixBytes.Length, "a declared document presented with invalid bytes");
    }

    [TestMethod]
    public void ASecondByteOrderMarkIsContentNotSignature()
    {
        const string Document = "\uFEFF\uFEFF<a/>";

        XmlScannerAssert.Refuses(Document, GeometryCodecRefusalKind.MalformedDocument, GeometryCodecText.Utf8ByteOrderMark.Length);
    }

    [TestMethod]
    public void AnUppercaseXmlnsIsAnOrdinaryAttributeNotADeclaration()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a XMLNS=\"u\"/>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.AreEqual(1, scanner.AttributeCount, "the case variant joins the table instead of binding");
        Assert.IsTrue(scanner.AttributeLocalName(0).SequenceEqual("XMLNS"u8), "the name is carried as written");
        Assert.IsTrue(scanner.AttributeNamespace(0).IsEmpty, "the case variant lives in no namespace");
        Assert.IsTrue(scanner.ElementNamespace.IsEmpty, "no default namespace was declared");
    }

    [TestMethod]
    public void AReservedLookingPrefixIsAnOrdinaryPrefix()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<xmlx:e xmlns:xmlx=\"u\"/>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual("u"u8), "a prefix merely starting with the reserved letters resolves through its own declaration");
    }

    [TestMethod]
    public void APrefixedRedeclarationShadowsItsOuterBinding()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<p:a xmlns:p=\"u\"><p:b xmlns:p=\"v\"/></p:a>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual("u"u8), "the root resolves through the outer binding");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the inner element must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual("v"u8), "the inner declaration shadows the outer one");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the inner element must close");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual("v"u8), "the synthetic close still sees the shadowing binding");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must close");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual("u"u8), "the outer binding is restored after the inner scope pops");
    }

    [TestMethod]
    public void ASiblingsClosedDeclarationDoesNotDeclareThePrefix()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<a><b xmlns:p=\"u\"/><p:c/></a>";
        int offset = Document.IndexOf("<p:c/>", StringComparison.Ordinal) + 5;

        XmlScannerAssert.Refuses(Document, GeometryCodecRefusalKind.MalformedDocument, offset);
    }

    [TestMethod]
    public void ASecondDeclarationIsAProcessingInstruction()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<?xml version=\"1.0\"?> <?xml version=\"1.0\"?><a/>";
        int second = Document.IndexOf("<?xml", startIndex: 1, StringComparison.Ordinal);

        XmlScannerAssert.Refuses(Document, GeometryCodecRefusalKind.ProhibitedConstruct, second);
    }

    [TestMethod]
    public void AnEndTagShorterThanTheStartTagRefusesAtItsClosingBracket()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<ax>y</a>";
        int offset = Document.IndexOf("</a>", StringComparison.Ordinal) + 3;

        XmlScannerAssert.Refuses(Document, GeometryCodecRefusalKind.MalformedDocument, offset);
    }

    [TestMethod]
    public void AnEmptyPrefixedDeclarationRefusesAtItsClosingQuote()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<a xmlns:p=\"\"/>";
        int offset = Document.IndexOf("=\"\"", StringComparison.Ordinal) + 2;

        XmlScannerAssert.Refuses(Document, GeometryCodecRefusalKind.MalformedDocument, offset);
    }

    [TestMethod]
    public void AnUndeclaredPrefixOnAnEmptyElementRefusesAtItsClosingBracket()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<q:a/>";
        int offset = Document.IndexOf("/>", StringComparison.Ordinal) + 1;

        XmlScannerAssert.Refuses(Document, GeometryCodecRefusalKind.MalformedDocument, offset);
    }

    [TestMethod]
    [DataRow("<a>x<![CDATA[y]]>z</a>", "xyz", DisplayName = "CDATA unwraps and concatenates with its neighbors")]
    [DataRow("<a>x<!--c-->z</a>", "xz", DisplayName = "runs split by a comment concatenate")]
    [DataRow("<a><![CDATA[]]]]><![CDATA[>]]></a>", "]]>", DisplayName = "CDATA sections may assemble the close sequence as data")]
    [DataRow("<a>]]&gt;</a>", "]]>", DisplayName = "the escaped close sequence decodes without tripping the raw ban")]
    [DataRow("<a>&amp;amp;</a>", "&amp;", DisplayName = "replacement bytes are inert and never rescanned")]
    [DataRow("<a>x\r\ny</a>", "x\ny", DisplayName = "a carriage-return line-feed pair normalizes to one line feed")]
    [DataRow("<a>x\ry</a>", "x\ny", DisplayName = "a lone carriage return normalizes to a line feed")]
    [DataRow("<a><![CDATA[x\r\ny]]></a>", "x\ny", DisplayName = "line ends normalize inside CDATA")]
    [DataRow("<a>a&#13;b</a>", "a\rb", DisplayName = "a referenced carriage return survives normalization")]
    [DataRow("<a>&#x1F600;</a>", "\U0001F600", DisplayName = "a supplementary-plane reference decodes to four bytes")]
    [DataRow("<a>a]]<!--x-->>b</a>", "a]]>b", DisplayName = "the per-run ban admits the sequence assembled across a comment")]
    [DataRow("<a>&quot;&apos;</a>", "\"'", DisplayName = "the quotation entities decode in text")]
    [DataRow("<a> x </a>", " x ", DisplayName = "text whitespace is preserved verbatim")]
    [DataRow("<a><![CDATA[a<b&amp;c]]></a>", "a<b&amp;c", DisplayName = "angle brackets and ampersands are literal inside CDATA and references stay undecoded")]
    [DataRow("<a>&#x1f600;</a>", "\U0001F600", DisplayName = "a lowercase hexadecimal reference decodes by place value")]
    public void TextDecodesToTheExpectedBytes(string document, string expectedText)
    {
        string text = XmlScannerAssert.ReadSingleText(document, out _);

        Assert.AreEqual(expectedText, text, $"'{document}' must decode to the expected text");
    }

    [TestMethod]
    [DataRow("<a> \t\n</a>", true, DisplayName = "literal whitespace-only text is delivered and marked whitespace")]
    [DataRow("<a>&#32;</a>", true, DisplayName = "a referenced space counts as whitespace, reference-blind")]
    [DataRow("<a> x </a>", false, DisplayName = "any non-whitespace byte clears the mark")]
    [DataRow("<a>&#65;</a>", false, DisplayName = "a referenced letter clears the mark")]
    public void TextWhitespaceIsPinnedOverDecodedBytes(string document, bool expectedWhitespace)
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(document);
        using XmlFragmentScanner scanner = new(documentBytes);
        bool seen = false;
        GeometryCodecRefusal refusal = GeometryCodecRefusal.None;
        while(scanner.TryReadNext(out XmlFragmentTokenKind kind, out refusal))
        {
            if(kind != XmlFragmentTokenKind.Text)
            {
                continue;
            }

            seen = true;
            Assert.AreEqual(expectedWhitespace, scanner.TextIsWhitespace, $"'{document}' must pin the whitespace mark");
        }

        Assert.AreEqual(GeometryCodecRefusal.None, refusal, $"'{document}' must scan to clean exhaustion");
        Assert.IsTrue(seen, $"'{document}' must deliver a text token");
    }

    [TestMethod]
    [DataRow("<a><!--c--></a>", DisplayName = "comment-only content delivers no text token")]
    [DataRow("<a><![CDATA[]]></a>", DisplayName = "an empty CDATA section delivers no text token")]
    [DataRow("<a><!--c--><!--d--></a>", DisplayName = "adjacent comments deliver no text token")]
    public void AnEmptyRegionDeliversNoToken(string document)
    {
        int count = XmlScannerAssert.CountTextTokens(document);

        Assert.AreEqual(0, count, $"'{document}' must deliver no text token");
    }

    [TestMethod]
    [DataRow("<a b=\"x\r\ny\"/>", "b", "x y", DisplayName = "a literal line-end pair contributes one space")]
    [DataRow("<a b=\"x&#13;y\"/>", "b", "x\ry", DisplayName = "a referenced carriage return appends verbatim")]
    [DataRow("<a b=\"x&#9;y\"/>", "b", "x\ty", DisplayName = "a referenced tab appends verbatim")]
    [DataRow("<a b=\"x&#xA;y\"/>", "b", "x\ny", DisplayName = "a referenced line feed appends verbatim")]
    [DataRow("<a b=\"x\ty\"/>", "b", "x y", DisplayName = "a literal tab becomes a space")]
    [DataRow("<a b=\"x\ny\"/>", "b", "x y", DisplayName = "a literal line feed becomes a space")]
    [DataRow("<a b=\" x  y \"/>", "b", " x  y ", DisplayName = "spaces are preserved, never collapsed or trimmed")]
    [DataRow("<a b=\"&lt;tag&gt;\"/>", "b", "<tag>", DisplayName = "angle entities decode in values")]
    [DataRow("<a b=\"&amp;amp;\"/>", "b", "&amp;", DisplayName = "replacement bytes in values are inert")]
    [DataRow("<a b=\"it&apos;s\"/>", "b", "it's", DisplayName = "the apostrophe entity decodes in a double-quoted value")]
    [DataRow("<a b='say &quot;hi&quot;'/>", "b", "say \"hi\"", DisplayName = "the quotation entity decodes in a single-quoted value")]
    [DataRow("<a b=\"\"/>", "b", "", DisplayName = "an empty attribute value is legal and empty")]
    [DataRow("<a b=\"x'y\"/>", "b", "x'y", DisplayName = "a literal apostrophe inside a double-quoted value is data")]
    [DataRow("<a b='x\"y'/>", "b", "x\"y", DisplayName = "a literal quotation mark inside a single-quoted value is data")]
    [DataRow("<a b=\"x\ry\"/>", "b", "x y", DisplayName = "a lone carriage return in a value contributes one space")]
    public void AttributeValuesNormalizeInTheSinglePass(string document, string localName, string expectedValue)
    {
        string value = XmlScannerAssert.ReadRootAttributeValue(document, string.Empty, localName);

        Assert.AreEqual(expectedValue, value, $"'{document}' must normalize the value in the one per-item pass");
    }

    [TestMethod]
    public void AnXmlPrefixedAttributeResolvesToThePermanentNamespace()
    {
        string value = XmlScannerAssert.ReadRootAttributeValue("<a xml:space=\"preserve\"/>", XmlTestDocuments.XmlNamespace, "space");

        Assert.AreEqual("preserve", value, "the xml prefix must resolve through the permanent binding");
    }

    [TestMethod]
    public void TheDepthCapAcceptsNinetySixAndRefusesTheNinetySeventh()
    {
        string accepted = XmlTestDocuments.NestedElementChain(GeometryCodecText.MaximumTransportDepth);
        XmlScannerAssert.Accepts(accepted);

        string refused = XmlTestDocuments.NestedElementChain(GeometryCodecText.MaximumTransportDepth + 1);
        int offset = GeometryCodecText.MaximumTransportDepth * XmlTestDocuments.NestedElementOpenLength;

        XmlScannerAssert.Refuses(refused, GeometryCodecRefusalKind.NestingTooDeep, offset);
    }

    [TestMethod]
    public void BothSpellingsOfAnEmptyElementPresentOneTokenShape()
    {
        //Both documents are ASCII throughout, so string indexes equal byte offsets.
        const string EmptyTag = "<e/>";
        const string PairedTag = "<e></e>";
        byte[] emptyBytes = Encoding.UTF8.GetBytes(EmptyTag);
        using XmlFragmentScanner empty = new(emptyBytes);
        Assert.IsTrue(empty.TryReadNext(out XmlFragmentTokenKind first, out _), "the empty tag must open");
        Assert.AreEqual(XmlFragmentTokenKind.ElementOpen, first, "the empty tag's first token is the open");
        Assert.AreEqual(0, empty.TokenStartOffset, "the open anchors at the tag's opening bracket");
        Assert.IsTrue(empty.TryReadNext(out XmlFragmentTokenKind second, out _), "the empty tag must close");
        Assert.AreEqual(XmlFragmentTokenKind.ElementClose, second, "the empty tag's second token is the synthetic close");
        Assert.AreEqual(0, empty.TokenStartOffset, "the synthetic close anchors at the same opening bracket");
        Assert.IsFalse(empty.TryReadNext(out _, out GeometryCodecRefusal emptyEnd), "the empty tag has two tokens");
        Assert.AreEqual(GeometryCodecRefusal.None, emptyEnd, "the empty tag scans clean");

        byte[] pairedBytes = Encoding.UTF8.GetBytes(PairedTag);
        using XmlFragmentScanner paired = new(pairedBytes);
        Assert.IsTrue(paired.TryReadNext(out first, out _), "the paired tag must open");
        Assert.AreEqual(XmlFragmentTokenKind.ElementOpen, first, "the paired tag's first token is the open");
        Assert.AreEqual(0, paired.TokenStartOffset, "the open anchors at the start tag's bracket");
        Assert.IsTrue(paired.TryReadNext(out second, out _), "the paired tag must close");
        Assert.AreEqual(XmlFragmentTokenKind.ElementClose, second, "the paired tag's second token is the close");
        Assert.AreEqual(PairedTag.IndexOf("</", StringComparison.Ordinal), paired.TokenStartOffset, "the paired close anchors at its own bracket");
        Assert.IsFalse(paired.TryReadNext(out _, out GeometryCodecRefusal pairedEnd), "the paired tag has two tokens");
        Assert.AreEqual(GeometryCodecRefusal.None, pairedEnd, "the paired tag scans clean");
    }

    [TestMethod]
    public void TheStartTagCloseOffsetReportsTheClosingBracketInBothSpellings()
    {
        //Both documents are ASCII throughout, so string indexes equal byte offsets.
        const string PairedTag = "<e a=\"1\">x</e>";
        byte[] pairedBytes = Encoding.UTF8.GetBytes(PairedTag);
        using XmlFragmentScanner paired = new(pairedBytes);
        Assert.IsTrue(paired.TryReadNext(out _, out _), "the paired tag must open");
        Assert.AreEqual(PairedTag.IndexOf('>', StringComparison.Ordinal), paired.StartTagCloseOffset, "the paired spelling anchors at its closing bracket");

        const string EmptyTag = "<e a=\"1\"/>";
        byte[] emptyBytes = Encoding.UTF8.GetBytes(EmptyTag);
        using XmlFragmentScanner empty = new(emptyBytes);
        Assert.IsTrue(empty.TryReadNext(out _, out _), "the empty tag must open");
        Assert.AreEqual(EmptyTag.IndexOf("/>", StringComparison.Ordinal) + 1, empty.StartTagCloseOffset, "the empty spelling anchors at the bracket after the slash");
    }

    [TestMethod]
    public void TextAnchorsAtItsFirstContributingByte()
    {
        //Both documents are ASCII throughout, so string indexes equal byte offsets.
        const string CommentFirst = "<a><!--c-->x</a>";
        string text = XmlScannerAssert.ReadSingleText(CommentFirst, out int offset);
        Assert.AreEqual("x", text, "the comment contributes nothing");
        Assert.AreEqual(CommentFirst.IndexOf("x<", StringComparison.Ordinal), offset, "a comment-first region anchors at the first data byte");

        const string CdataFirst = "<a><![CDATA[y]]>x</a>";
        text = XmlScannerAssert.ReadSingleText(CdataFirst, out offset);
        Assert.AreEqual("yx", text, "the CDATA interior contributes first");
        Assert.AreEqual(CdataFirst.IndexOf("y]]", StringComparison.Ordinal), offset, "a CDATA-first region anchors at the first interior byte");
    }

    [TestMethod]
    public void MapTextOffsetIsTheIdentityForCleanText()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<a>hello</a>";
        byte[] documentBytes = Encoding.UTF8.GetBytes(Document);
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(scanner.TryReadNext(out XmlFragmentTokenKind kind, out _), "the text must arrive");
        Assert.AreEqual(XmlFragmentTokenKind.Text, kind, "the second token is the text");
        int start = Document.IndexOf("hello", StringComparison.Ordinal);
        for(int index = 0; index < 5; index++)
        {
            Assert.AreEqual(start + index, scanner.MapTextOffset(index), "clean text maps by identity");
        }
    }

    [TestMethod]
    public void MapTextOffsetMapsThroughReferencesCdataAndLineEnds()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<a>A&lt;B<![CDATA[C]]>D&#13;E</a>";
        byte[] documentBytes = Encoding.UTF8.GetBytes(Document);
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(scanner.TryReadNext(out XmlFragmentTokenKind kind, out _), "the text must arrive");
        Assert.AreEqual(XmlFragmentTokenKind.Text, kind, "the second token is the text");
        Assert.AreEqual("A<BCD\rE", Encoding.UTF8.GetString(scanner.Text), "the decoded assembly is pinned first");
        Assert.AreEqual(Document.IndexOf("A&", StringComparison.Ordinal), scanner.MapTextOffset(0), "a verbatim byte maps to itself");
        Assert.AreEqual(Document.IndexOf("&lt;", StringComparison.Ordinal), scanner.MapTextOffset(1), "a replacement byte maps to its reference's ampersand");
        Assert.AreEqual(Document.IndexOf("B<", StringComparison.Ordinal), scanner.MapTextOffset(2), "a byte after a reference maps to itself");
        Assert.AreEqual(Document.IndexOf("C]]", StringComparison.Ordinal), scanner.MapTextOffset(3), "a CDATA interior byte maps to itself");
        Assert.AreEqual(Document.IndexOf("D&", StringComparison.Ordinal), scanner.MapTextOffset(4), "a byte after a CDATA boundary maps to itself");
        Assert.AreEqual(Document.IndexOf("&#13;", StringComparison.Ordinal), scanner.MapTextOffset(5), "a referenced carriage return maps to its ampersand");
        Assert.AreEqual(Document.IndexOf("E<", StringComparison.Ordinal), scanner.MapTextOffset(6), "the final byte maps to itself");

        const string LineEnds = "<a>x\r\ny</a>";
        byte[] lineEndBytes = Encoding.UTF8.GetBytes(LineEnds);
        using XmlFragmentScanner lineEndScanner = new(lineEndBytes);
        Assert.IsTrue(lineEndScanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(lineEndScanner.TryReadNext(out kind, out _), "the text must arrive");
        Assert.AreEqual(XmlFragmentTokenKind.Text, kind, "the second token is the text");
        Assert.AreEqual(LineEnds.IndexOf('x', StringComparison.Ordinal), lineEndScanner.MapTextOffset(0), "the byte before the pair maps to itself");
        Assert.AreEqual(LineEnds.IndexOf('\r', StringComparison.Ordinal), lineEndScanner.MapTextOffset(1), "the normalized line feed maps to the carriage return");
        Assert.AreEqual(LineEnds.IndexOf('y', StringComparison.Ordinal), lineEndScanner.MapTextOffset(2), "the byte after the pair maps to itself");
    }

    [TestMethod]
    public void MapTextOffsetRejectsPositionsOutsideTheDecodedText()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a>x</a>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the text must arrive");
        bool negativeThrew = false;
        try
        {
            _ = scanner.MapTextOffset(-1);
        }
        catch(ArgumentOutOfRangeException)
        {
            negativeThrew = true;
        }

        Assert.IsTrue(negativeThrew, "a negative position must fail loud");
        bool beyondThrew = false;
        try
        {
            _ = scanner.MapTextOffset(1);
        }
        catch(ArgumentOutOfRangeException)
        {
            beyondThrew = true;
        }

        Assert.IsTrue(beyondThrew, "a position at the decoded length must fail loud");
    }

    [TestMethod]
    public void TheDefaultNamespaceAppliesToElementsAndNeverToAttributes()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a xmlns=\"urn:u\" b=\"1\"><c/></a>");
        byte[] expectedNamespace = Encoding.UTF8.GetBytes("urn:u");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual(expectedNamespace), "the declaring element takes its own default");
        Assert.IsTrue(scanner.TryFindAttribute(ReadOnlySpan<byte>.Empty, "b"u8, out _), "the attribute lives in no namespace");
        Assert.IsFalse(scanner.TryFindAttribute(expectedNamespace, "b"u8, out _), "the default namespace never applies to attributes");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the child must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual(expectedNamespace), "the child inherits the default");
    }

    [TestMethod]
    public void AnUndeclarationReturnsDescendantsToNoNamespace()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a xmlns=\"urn:u\"><b xmlns=\"\"><c/></b></a>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the undeclaring child must open");
        Assert.IsTrue(scanner.ElementNamespace.IsEmpty, "the undeclaring element is in no namespace");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the grandchild must open");
        Assert.IsTrue(scanner.ElementNamespace.IsEmpty, "the undeclaration scopes to descendants");
    }

    [TestMethod]
    public void ALateDeclarationBindsTheWholeTagIncludingEarlierAttributes()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<p:a p:attr=\"1\" xmlns:p=\"urn:p\"/>");
        byte[] expectedNamespace = Encoding.UTF8.GetBytes("urn:p");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual(expectedNamespace), "the element name resolves through the late declaration");
        Assert.IsTrue(scanner.TryFindAttribute(expectedNamespace, "attr"u8, out int index), "the earlier attribute resolves through the late declaration");
        Assert.IsTrue(scanner.AttributeValue(index).SequenceEqual("1"u8), "the attribute value rides along");
    }

    [TestMethod]
    public void ADecodedBindingSurvivesDescendantDecodingInTheArena()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<p:a xmlns:p=\"u&#38;v\"><x q=\"&#65;bc\"/><p:c/></p:a>");
        byte[] expectedNamespace = Encoding.UTF8.GetBytes("u&v");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the declaring root must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual(expectedNamespace), "the decoded declaration binds the root");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the intervening element must open");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the intervening element must close");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the descendant must open");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual(expectedNamespace), "the arena-owned binding survives an intervening tag's decoding");
    }

    [TestMethod]
    public void ASelfDeclaringEmptyElementExposesItsNamespaceOnTheSyntheticClose()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<p:e xmlns:p=\"u&#38;v\"/>");
        byte[] expectedNamespace = Encoding.UTF8.GetBytes("u&v");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.IsTrue(scanner.TryReadNext(out XmlFragmentTokenKind kind, out _), "the synthetic close must arrive");
        Assert.AreEqual(XmlFragmentTokenKind.ElementClose, kind, "the second token is the close");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual(expectedNamespace), "the close resolves before its scope pops");
        Assert.IsTrue(scanner.ElementLocalName.SequenceEqual("e"u8), "the close carries the local name");
    }

    [TestMethod]
    public void DeclarationsAreExcludedFromTheAttributeTable()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a xmlns=\"u\" xmlns:p=\"v\" b=\"1\" p:c=\"2\"/>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.AreEqual(2, scanner.AttributeCount, "declarations bind and never join the table");
        Assert.IsTrue(scanner.AttributeLocalName(0).SequenceEqual("b"u8), "the first table entry is the first attribute");
        Assert.IsTrue(scanner.AttributeNamespace(0).IsEmpty, "an unprefixed attribute is in no namespace");
        Assert.IsTrue(scanner.AttributeLocalName(1).SequenceEqual("c"u8), "the second table entry is the prefixed attribute");
        Assert.IsTrue(scanner.AttributeNamespace(1).SequenceEqual("v"u8), "the prefixed attribute resolves through the same tag's declaration");
    }

    [TestMethod]
    public void AttributeOffsetsReportTheWrittenPositions()
    {
        //The document is ASCII throughout, so string indexes equal byte offsets.
        const string Document = "<a foo=\"bar\"/>";
        byte[] documentBytes = Encoding.UTF8.GetBytes(Document);
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.IsTrue(scanner.TryFindAttribute(ReadOnlySpan<byte>.Empty, "foo"u8, out int index), "the attribute must be found");
        Assert.AreEqual(Document.IndexOf("foo", StringComparison.Ordinal), scanner.AttributeNameOffset(index), "the name offset is the qualified name's first byte");
        Assert.AreEqual(Document.IndexOf("bar", StringComparison.Ordinal), scanner.AttributeValueOffset(index), "the value offset is the first byte inside the quotes");
    }

    [TestMethod]
    public void WhitespaceOnlyTextBetweenElementsIsDelivered()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a> <b/> </a>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out XmlFragmentTokenKind kind, out _), "the root must open");
        Assert.AreEqual(XmlFragmentTokenKind.ElementOpen, kind, "the first token is the root open");
        Assert.IsTrue(scanner.TryReadNext(out kind, out _), "the first gap must arrive");
        Assert.AreEqual(XmlFragmentTokenKind.Text, kind, "whitespace-only text is delivered, not suppressed");
        Assert.IsTrue(scanner.TextIsWhitespace, "the gap is marked whitespace");
        Assert.IsTrue(scanner.TryReadNext(out kind, out _), "the child must open");
        Assert.AreEqual(XmlFragmentTokenKind.ElementOpen, kind, "the third token is the child open");
        Assert.IsTrue(scanner.TryReadNext(out kind, out _), "the child must close");
        Assert.AreEqual(XmlFragmentTokenKind.ElementClose, kind, "the fourth token is the child close");
        Assert.IsTrue(scanner.TryReadNext(out kind, out _), "the second gap must arrive");
        Assert.AreEqual(XmlFragmentTokenKind.Text, kind, "the second gap is delivered too");
        Assert.IsTrue(scanner.TryReadNext(out kind, out _), "the root must close");
        Assert.AreEqual(XmlFragmentTokenKind.ElementClose, kind, "the last token is the root close");
    }

    [TestMethod]
    public void ElementIdentityExposesLocalNameAndNamespaceSeparately()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<p:name xmlns:p=\"urn:p\"/>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.IsTrue(scanner.ElementLocalName.SequenceEqual("name"u8), "the local name excludes the prefix");
        Assert.IsTrue(scanner.ElementNamespace.SequenceEqual("urn:p"u8), "the namespace is the resolved value, not the prefix");
    }

    [TestMethod]
    public void AccessorsFailLoudWhenNoTokenIsCurrent()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(XmlTestDocuments.MinimalRoot);
        using XmlFragmentScanner scanner = new(documentBytes);
        bool threw = false;
        try
        {
            _ = scanner.TokenStartOffset;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "an accessor before the first read must fail loud");

        XmlFragmentScanner defaulted = default;
        threw = false;
        try
        {
            _ = defaulted.TokenStartOffset;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "an accessor on the defaulted value must fail loud");
    }

    [TestMethod]
    public void AccessorsFailLoudOnTheWrongTokenKind()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<a>x</a>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the root must open");
        bool threw = false;
        try
        {
            _ = scanner.Text;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "reading text on an element token must fail loud");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the text must arrive");
        threw = false;
        try
        {
            _ = scanner.ElementLocalName;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "reading element state on a text token must fail loud");
        threw = false;
        try
        {
            _ = scanner.AttributeCount;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "reading attribute state on a text token must fail loud");
    }

    [TestMethod]
    public void AttributeStateIsIllegalOnTheSyntheticClose()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes("<e a=\"1\"/>");
        using XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        Assert.AreEqual(1, scanner.AttributeCount, "attribute state is legal at the open");
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the synthetic close must arrive");
        bool threw = false;
        try
        {
            _ = scanner.AttributeCount;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "attribute state on a close token must fail loud");
    }

    [TestMethod]
    public void AccessorsFailLoudAfterARefusalAndAfterExhaustion()
    {
        byte[] refusedBytes = Encoding.UTF8.GetBytes("<a>&bad;</a>");
        using XmlFragmentScanner refused = new(refusedBytes);
        while(refused.TryReadNext(out _, out _))
        {
        }

        bool threw = false;
        try
        {
            _ = refused.TokenStartOffset;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "an accessor after a refusal must fail loud");

        byte[] exhaustedBytes = Encoding.UTF8.GetBytes(XmlTestDocuments.MinimalRoot);
        using XmlFragmentScanner exhausted = new(exhaustedBytes);
        while(exhausted.TryReadNext(out _, out _))
        {
        }

        threw = false;
        try
        {
            _ = exhausted.TokenStartOffset;
        }
        catch(InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "an accessor after exhaustion must fail loud");
    }

    [TestMethod]
    public void ReadsAfterDisposalFailLoud()
    {
        byte[] documentBytes = Encoding.UTF8.GetBytes(XmlTestDocuments.MinimalRoot);
        XmlFragmentScanner scanner = new(documentBytes);
        Assert.IsTrue(scanner.TryReadNext(out _, out _), "the element must open");
        scanner.Dispose();
        bool threw = false;
        try
        {
            scanner.TryReadNext(out _, out _);
        }
        catch(ObjectDisposedException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "reading after disposal must fail loud");
        threw = false;
        try
        {
            _ = scanner.TokenStartOffset;
        }
        catch(ObjectDisposedException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "an accessor after disposal must fail loud");
        scanner.Dispose();
    }
}
