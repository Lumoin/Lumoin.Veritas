// W3C RDF/XML Test Cases — https://w3c.github.io/rdf-tests/
// Source vendored in test/Lumoin.Veritas.ParserTests/Material/Rdf/rdf-xml/ and .../rdf-xml-12/.
// See Material/Rdf/ATTRIBUTION.md for provenance.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.ParserTests.Conformance;
using Lumoin.Veritas.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Xml;

/// <summary>
/// The streaming oracle for <see cref="RdfXmlReader.ReadStreaming"/>: the forward-streaming reader must produce the
/// same RDF graph as the buffered <see cref="RdfXmlReader.Read"/> over every W3C RDF/XML fixture and over the
/// streaming-specific shapes the corpus underexercises (a self-closing top-level node element, a node element root,
/// a fresh-blank shared across top-level subjects, and an <c>rdf:parseType="Literal"</c> recovered from the scanner
/// buffer). Because both walks are subtree-sequential and share one emission path, equality is exact (a list match),
/// with a blank-node-isomorphism check as a backstop.
/// </summary>
/// <remarks>
/// The data oracle compares the produced quads only for well-formed input: the buffered reader rejects a malformed
/// document at tree build (all-or-nothing), whereas the streaming reader has already walked the earlier subtrees, so
/// their error recovery legitimately differs — for a malformed fixture the oracle asserts only that both readers
/// detect the malformedness, not that their partial output matches.
/// </remarks>
[TestClass]
internal sealed class W3cRdfXmlStreamingOracleTests
{
    private const string Rdf11AssumedBase = "https://w3c.github.io/rdf-tests/rdf/rdf11/rdf-xml/";
    private const string Rdf12AssumedBase = "https://w3c.github.io/rdf-tests/rdf/rdf12/rdf-xml/";
    private const string RdfXmlns = "xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"";

    /// <summary>Asserts the streaming reader matches the buffered reader over one RDF 1.1 RDF/XML fixture.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [W3cManifestData("Rdf", "rdf-xml")]
    public void StreamingMatchesBufferedRdfXml11(W3cTestCase testCase) => AssertFixture(testCase, "rdf-xml", Rdf11AssumedBase);

    /// <summary>Asserts the streaming reader matches the buffered reader over one RDF 1.2 RDF/XML fixture.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    [TestMethod]
    [W3cManifestData("Rdf", "rdf-xml-12")]
    public void StreamingMatchesBufferedRdfXml12(W3cTestCase testCase) => AssertFixture(testCase, "rdf-xml-12", Rdf12AssumedBase);

    /// <summary>A DOCTYPE-entity-abbreviated, multi-statement document streams to the same quads it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForDoctypeEntities()
    {
        string xml = "<?xml version=\"1.0\"?><!DOCTYPE rdf:RDF [ <!ENTITY ex \"http://e/\"> ]>" +
            "<rdf:RDF " + RdfXmlns + " xmlns:p=\"&ex;p#\">" +
            "<rdf:Description rdf:about=\"&ex;a\"><p:name>x</p:name></rdf:Description>" +
            "<rdf:Description rdf:about=\"&ex;b\"><p:name>y</p:name></rdf:Description></rdf:RDF>";

        AssertSameGraph(xml);
    }

    /// <summary>An <c>rdf:parseType="Literal"</c> value — recovered by slicing the scanner buffer — streams to the same quads it buffers to, including the recoverable DOCTYPE-entity fallback.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForXmlLiteral()
    {
        string xml = "<?xml version=\"1.0\"?><!DOCTYPE rdf:RDF [ <!ENTITY foo \"bar\"> ]>" +
            "<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\">" +
            "<rdf:Description rdf:about=\"http://e/s\">" +
            "<ex:markup rdf:parseType=\"Literal\"><span class=\"a\">deep <b>nested</b> markup</span></ex:markup>" +
            "<ex:lit rdf:parseType=\"Literal\"><x>&foo;</x></ex:lit>" +
            "</rdf:Description></rdf:RDF>";

        AssertSameGraph(xml);
    }

    /// <summary>A self-closing top-level node element (which completes at its start tag, the empty-streamed-child path) streams to the same quads it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForSelfClosingTopLevelNodes()
    {
        string xml = "<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\">" +
            "<rdf:Description rdf:about=\"http://e/a\" ex:p=\"1\"/>" +
            "<rdf:Description rdf:about=\"http://e/b\" ex:p=\"2\"/>" +
            "</rdf:RDF>";

        AssertSameGraph(xml);
    }

    /// <summary>A document whose root is a single node element (not <c>rdf:RDF</c>) streams to the same quads it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForNodeElementRoot()
    {
        string xml = "<rdf:Description " + RdfXmlns + " xmlns:ex=\"http://e/\" rdf:about=\"http://e/s\"><ex:p>v</ex:p></rdf:Description>";

        AssertSameGraph(xml);
    }

    /// <summary>Fresh blank nodes and an <c>rdf:nodeID</c> shared across two top-level subjects stream to an identical graph, proving the cross-subtree blank-node counter and rdf:ID set are threaded on one walker.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForBlankNodesSharedAcrossSubjects()
    {
        string xml = "<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\">" +
            "<rdf:Description rdf:about=\"http://e/a\"><ex:p rdf:parseType=\"Resource\"><ex:q>1</ex:q></ex:p><ex:r rdf:nodeID=\"shared\"/></rdf:Description>" +
            "<rdf:Description rdf:about=\"http://e/b\"><ex:p rdf:parseType=\"Resource\"><ex:q>2</ex:q></ex:p><ex:r rdf:nodeID=\"shared\"/></rdf:Description>" +
            "</rdf:RDF>";

        AssertSameGraph(xml);
    }

    /// <summary>Many top-level subjects (forcing the streaming scanner to reclaim its buffer between subjects, so later subjects parse with a non-zero buffer base) interspersed with <c>rdf:parseType="Literal"</c> subjects — a nested-markup literal sliced from the rebased buffer and an entity-reference literal that recovers via the owned fallback — stream to the same graph they buffer to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForLiteralsAmongManyCompactedSubjects()
    {
        StringBuilder builder = new();
        builder.Append("<?xml version=\"1.0\"?><!DOCTYPE rdf:RDF [ <!ENTITY foo \"bar\"> ]>");
        builder.Append("<rdf:RDF ").Append(RdfXmlns).Append(" xmlns:ex=\"http://e/\">");
        for(int i = 0; i < 800; i++)
        {
            builder.Append("<rdf:Description rdf:about=\"http://e/s").Append(i).Append("\"><ex:p>value ").Append(i).Append("</ex:p></rdf:Description>");
            if(i % 100 == 50)
            {
                builder.Append("<rdf:Description rdf:about=\"http://e/lit").Append(i).Append("\"><ex:doc rdf:parseType=\"Literal\"><span class=\"a\">deep <b>nested ").Append(i).Append("</b> markup</span></ex:doc></rdf:Description>");
                builder.Append("<rdf:Description rdf:about=\"http://e/ent").Append(i).Append("\"><ex:lit rdf:parseType=\"Literal\"><x>&foo;</x></ex:lit></rdf:Description>");
            }
        }

        builder.Append("</rdf:RDF>");

        AssertSameGraph(builder.ToString());
    }

    /// <summary>An <c>rdf:parseType="Literal"</c> whose verbatim content is larger than the streaming chunk size — so it spans several feed chunks and is sliced from the scanner buffer across them — streams to the same quads it buffers to.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForLiteralSpanningChunkBoundaries()
    {
        StringBuilder builder = new();
        builder.Append("<rdf:RDF ").Append(RdfXmlns).Append(" xmlns:ex=\"http://e/\">");
        builder.Append("<rdf:Description rdf:about=\"http://e/s\"><ex:doc rdf:parseType=\"Literal\">");
        for(int i = 0; i < 4000; i++)
        {
            builder.Append("<p n=\"").Append(i).Append("\">item ").Append(i).Append("</p>");
        }

        builder.Append("</ex:doc></rdf:Description></rdf:RDF>");

        AssertSameGraph(builder.ToString());
    }

    /// <summary>Many top-level subjects each minting a fresh blank node stream to the identical graph, proving the cross-subtree blank-node counter stays monotonic over the bounded per-subtree walk.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForManyTopLevelSubjects()
    {
        StringBuilder builder = new();
        builder.Append("<rdf:RDF ").Append(RdfXmlns).Append(" xmlns:ex=\"http://e/\">");
        for(int i = 0; i < 2000; i++)
        {
            builder.Append("<rdf:Description rdf:about=\"http://e/s").Append(i).Append("\"><ex:p rdf:parseType=\"Resource\"><ex:q>").Append(i).Append("</ex:q></ex:p></rdf:Description>");
        }

        builder.Append("</rdf:RDF>");

        AssertSameGraph(builder.ToString());
    }

    /// <summary>Content after the closing root element is rejected by both readers (the streaming fold enforces the same single-root well-formedness as the buffered read).</summary>
    [TestMethod]
    public void StreamingAndBufferedBothRejectTrailingContentAfterRoot()
    {
        AssertBothReject("<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\"><rdf:Description rdf:about=\"http://e/a\"><ex:p>v</ex:p></rdf:Description></rdf:RDF><extra/>");
    }

    /// <summary>Two top-level node elements (no <c>rdf:RDF</c> wrapper) are rejected by both readers rather than the streaming reader silently keeping only the first.</summary>
    [TestMethod]
    public void StreamingAndBufferedBothRejectMultipleRootElements()
    {
        AssertBothReject("<rdf:Description " + RdfXmlns + " rdf:about=\"http://e/a\"/><rdf:Description " + RdfXmlns + " rdf:about=\"http://e/b\"/>");
    }

    /// <summary>A document with no element (only a prolog and a comment) is rejected by both readers.</summary>
    [TestMethod]
    public void StreamingAndBufferedBothRejectMissingRootElement()
    {
        AssertBothReject("<?xml version=\"1.0\"?><!-- a prolog and a comment, no element -->");
    }

    /// <summary>A document whose root element is left unclosed is rejected by both readers (the streaming fold enforces the same open-element balance as the buffered read).</summary>
    [TestMethod]
    public void StreamingAndBufferedBothRejectUnclosedRoot()
    {
        AssertBothReject("<rdf:RDF " + RdfXmlns + " xmlns:ex=\"http://e/\"><rdf:Description rdf:about=\"http://e/a\"><ex:p>v</ex:p></rdf:Description>");
    }

    /// <summary>Asserts a structurally malformed document (multi-root, no root, or an unclosed root) is rejected — a recorded error — by both the buffered and the streaming reader. The produced quads may differ (buffered rejects the whole document at tree build; streaming recovers incrementally), so only error-detection parity is asserted.</summary>
    /// <param name="xml">The malformed RDF/XML document text.</param>
    private static void AssertBothReject(string xml)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(xml);

        DiagnosticBag bufferedDiagnostics = new();
        _ = RdfXmlReader.Read(bytes, bufferedDiagnostics, baseIri: default);

        DiagnosticBag streamingDiagnostics = new();
        _ = RdfXmlReader.ReadStreaming(bytes, streamingDiagnostics, baseIri: default);

        Assert.IsTrue(bufferedDiagnostics.HasErrors, "the buffered reader must reject this malformed document.");
        Assert.IsTrue(streamingDiagnostics.HasErrors, "the streaming reader must reject this malformed document.");
    }

    /// <summary>A self-closing (and an empty) <c>rdf:parseType="Literal"</c> subject placed after enough subjects to advance the streaming buffer base streams to the same graph it buffers to — the empty-element sentinel offsets must not form a negative buffer window.</summary>
    [TestMethod]
    public void StreamingMatchesBufferedForEmptyLiteralAfterCompaction()
    {
        StringBuilder builder = new();
        builder.Append("<rdf:RDF ").Append(RdfXmlns).Append(" xmlns:ex=\"http://e/\">");
        for(int i = 0; i < 600; i++)
        {
            builder.Append("<rdf:Description rdf:about=\"http://e/s").Append(i).Append("\"><ex:p>value ").Append(i).Append("</ex:p></rdf:Description>");
        }

        builder.Append("<rdf:Description rdf:about=\"http://e/selfclosing\"><ex:doc rdf:parseType=\"Literal\"/></rdf:Description>");
        builder.Append("<rdf:Description rdf:about=\"http://e/withattr\"><ex:doc ex:k=\"1\" rdf:parseType=\"Literal\"/></rdf:Description>");
        builder.Append("<rdf:Description rdf:about=\"http://e/empty\"><ex:doc rdf:parseType=\"Literal\"></ex:doc></rdf:Description>");
        builder.Append("</rdf:RDF>");

        AssertSameGraph(builder.ToString());
    }

    /// <summary>Reads the fixture buffered and streaming with the per-test base IRI and asserts they agree on well-formed input, or both reject malformed input.</summary>
    /// <param name="testCase">The manifest-declared test case.</param>
    /// <param name="suiteFolder">The suite subfolder under <c>Material/Rdf</c>.</param>
    /// <param name="assumedBase">The suite's <c>mf:assumedTestBase</c> HTTP IRI.</param>
    private static void AssertFixture(W3cTestCase testCase, string suiteFolder, string assumedBase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        byte[] bytes = File.ReadAllBytes(testCase.InputPath);
        string baseIri = ComposeBaseIri(testCase, suiteFolder, assumedBase);

        DiagnosticBag bufferedDiagnostics = new();
        IReadOnlyList<Quad> buffered = RdfXmlReader.Read(bytes, bufferedDiagnostics, Utf8Strings.From(baseIri));

        DiagnosticBag streamingDiagnostics = new();
        IReadOnlyList<Quad> streaming = RdfXmlReader.ReadStreaming(bytes, streamingDiagnostics, Utf8Strings.From(baseIri));

        if(bufferedDiagnostics.HasErrors)
        {
            Assert.IsTrue(streamingDiagnostics.HasErrors, $"the buffered reader rejected '{testCase.Name}' but the streaming reader accepted it.");

            return;
        }

        Assert.IsFalse(streamingDiagnostics.HasErrors, $"the streaming reader rejected '{testCase.Name}' but the buffered reader accepted it.");
        AssertSameGraph(testCase.Name, streaming, buffered);
    }

    /// <summary>Asserts an otherwise well-formed RDF/XML document reads to the same graph buffered and streaming, and that the two readers agree on whether it carries a recoverable diagnostic (such as the <c>rdf:parseType="Literal"</c> DOCTYPE-entity fallback).</summary>
    /// <param name="xml">The RDF/XML document text.</param>
    private static void AssertSameGraph(string xml)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(xml);

        DiagnosticBag bufferedDiagnostics = new();
        IReadOnlyList<Quad> buffered = RdfXmlReader.Read(bytes, bufferedDiagnostics, baseIri: default);

        DiagnosticBag streamingDiagnostics = new();
        IReadOnlyList<Quad> streaming = RdfXmlReader.ReadStreaming(bytes, streamingDiagnostics, baseIri: default);

        Assert.AreEqual(bufferedDiagnostics.HasErrors, streamingDiagnostics.HasErrors, "the streaming and buffered readers must agree on whether the document carries a diagnostic.");
        AssertSameGraph("inline", streaming, buffered);
    }

    /// <summary>Asserts two quad lists denote the same graph: an exact list match (the strong oracle) backed by a blank-node-isomorphism check.</summary>
    /// <param name="name">The fixture name, for the failure message.</param>
    /// <param name="streaming">The streaming reader's quads.</param>
    /// <param name="buffered">The buffered reader's quads.</param>
    private static void AssertSameGraph(string name, IReadOnlyList<Quad> streaming, IReadOnlyList<Quad> buffered)
    {
        Assert.IsTrue(streaming.SequenceEqual(buffered), $"streaming '{name}' produced a different quad list than the buffered read ({streaming.Count} vs {buffered.Count} quads).");
        Assert.IsTrue(QuadSetIsomorphism.AreIsomorphic(streaming, buffered), $"streaming '{name}' produced a graph not isomorphic to the buffered read.");
    }

    /// <summary>Composes a fixture's document base IRI as the suite's <c>mf:assumedTestBase</c> plus the fixture's path relative to the suite root.</summary>
    /// <param name="testCase">The test case whose input fixture is being read.</param>
    /// <param name="suiteFolder">The suite subfolder under <c>Material/Rdf</c>.</param>
    /// <param name="assumedBase">The suite's <c>mf:assumedTestBase</c> HTTP IRI.</param>
    /// <returns>The composed base IRI.</returns>
    private static string ComposeBaseIri(W3cTestCase testCase, string suiteFolder, string assumedBase)
    {
        string suiteRoot = Path.GetDirectoryName(W3cCorpusPath.For("Rdf", suiteFolder, "manifest.ttl"))!;
        string relative = Path.GetRelativePath(suiteRoot, testCase.InputPath).Replace('\\', '/');

        return assumedBase + relative;
    }
}
