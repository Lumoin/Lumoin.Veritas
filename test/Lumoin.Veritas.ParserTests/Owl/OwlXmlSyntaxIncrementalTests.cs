using System;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.Owl.Xml;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Verifies the OWL/XML reader's incremental editor contract: input fed
/// chunk by chunk produces the same <see cref="OwlOntologyDocument"/> as the
/// whole-buffer read at every byte boundary, an unfinished tail is an
/// <see cref="IncrementalParseStatus.NeedMore"/> status rather than a diagnostic, and
/// a closed document at a boundary reports <see cref="IncrementalParseStatus.Complete"/>.
/// </summary>
/// <remarks>
/// The XML front-end diverges from the functional and Manchester ones in places
/// the tests pin honestly: the byte scanner records no diagnostics of its own
/// (only the converter does, at completion), so a malformed or truncated tail is
/// tolerated structurally and never squiggled mid-stream; truncation of a
/// well-formed prefix completes without an error and keeps the prefix's axioms;
/// and a document with no <c>Ontology</c> element converts to an empty,
/// error-free document. Each test reads back through the functional writer's
/// canonical rendering, which is independent of the synthetic origin each
/// front-end stamps.
/// </remarks>
[TestClass]
internal sealed class OwlXmlSyntaxIncrementalTests
{
    /// <summary>A clean nested document that ends exactly on the closing tag, with no trailing whitespace.</summary>
    private const string CleanDocument = """<Ontology xmlns="http://www.w3.org/2002/07/owl#" ontologyIRI="http://example.org/o"><Declaration><Class IRI="http://example.org/A"/></Declaration><Declaration><ObjectProperty IRI="http://example.org/p"/></Declaration><SubClassOf><Class IRI="http://example.org/A"/><ObjectSomeValuesFrom><ObjectProperty IRI="http://example.org/p"/><Class IRI="http://example.org/A"/></ObjectSomeValuesFrom></SubClassOf></Ontology>""";

    [TestMethod]
    public void EveryCutPointResumesToTheWholeBufferResult()
    {
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(CleanDocument);
        Assert.IsFalse(whole.Diagnostics.HasErrors);
        string rendering = OwlFunctionalSyntaxWriter.Write(whole);

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(CleanDocument);
        for(int cut = 0; cut <= bytes.Length; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));

            //A clean prefix is never a diagnostic; the status carries the incompleteness.
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"cut {cut}: a clean prefix reported an error");

            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.IsFalse(resumed.Diagnostics.HasErrors, $"cut {cut}: the resumed parse reported an error");
            Assert.HasCount(whole.Axioms.Length, resumed.Axioms, $"cut {cut}: axiom count differs");
            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void EveryByteCutPointResumesAcrossMultiByteContent()
    {
        //Multi-byte code points sit in the ontology IRI, an element-name-free
        //class IRI, a child IRI text run, and a literal; a byte cut can fall
        //between the bytes of one code point. The structural delimiters are all
        //ASCII and UTF-8 is self-synchronizing, so the scanner rejoins a split
        //code point and matches the whole-buffer read at every byte boundary.
        const string Document = """<Ontology xmlns="http://www.w3.org/2002/07/owl#" ontologyIRI="http://example.org/café"><!-- nöte --><Declaration><Class IRI="http://example.org/Aö"/></Declaration><AnnotationAssertion><AnnotationProperty IRI="http://www.w3.org/2000/01/rdf-schema#label"/><IRI>http://example.org/ø</IRI><Literal datatypeIRI="http://www.w3.org/2001/XMLSchema#string">café ☕</Literal></AnnotationAssertion></Ontology>""";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(bytes);
        Assert.IsFalse(whole.Diagnostics.HasErrors);
        string rendering = OwlFunctionalSyntaxWriter.Write(whole);

        for(int cut = 0; cut <= bytes.Length; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"cut {cut}: a clean prefix reported an error");

            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.IsFalse(resumed.Diagnostics.HasErrors, $"cut {cut}: the resumed parse reported an error");
            Assert.HasCount(whole.Axioms.Length, resumed.Axioms, $"cut {cut}: axiom count differs");
            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void StatusIsNeedMoreInsideAnOpenElementAndCompleteAtTheBoundary()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(CleanDocument);

        //CleanDocument is ASCII, so character indices coincide with byte offsets.
        //Every cut inside the document leaves the Ontology element (and any open
        //child) on the stack, so Status is NeedMore (Open.Count > 1); the whole
        //document closes the root with no trailing text, so Status is Complete.
        int interior = CleanDocument.IndexOf('>', StringComparison.Ordinal) + 1;
        int lastClose = CleanDocument.Length - "</Ontology>".Length;

        for(int cut = interior; cut < lastClose; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(bytes.AsSpan(0, cut)), $"cut {cut}");
        }

        OwlXmlSyntaxIncrementalReader full = new();
        Assert.AreEqual(IncrementalParseStatus.Complete, full.Feed(bytes));
    }

    [TestMethod]
    public void EmptyElementOntologyReportsCompleteWithoutPushingTheStack()
    {
        //A self-closing <Ontology .../> never pushes the open stack, so the
        //input is at a boundary the moment the tag closes (Open.Count stays 1).
        const string Document = """<Ontology xmlns="http://www.w3.org/2002/07/owl#" ontologyIRI="http://example.org/o"/>""";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);

        OwlXmlSyntaxIncrementalReader whole = new();
        Assert.AreEqual(IncrementalParseStatus.Complete, whole.Feed(bytes));

        //The closing '/>' is the last two bytes; before it arrives the start tag
        //is unfinished, after it the empty element closes without an open child.
        OwlXmlSyntaxIncrementalReader split = new();
        Assert.AreEqual(IncrementalParseStatus.NeedMore, split.Feed(bytes.AsSpan(0, bytes.Length - 2)));
        Assert.AreEqual(IncrementalParseStatus.Complete, split.Feed(bytes.AsSpan(bytes.Length - 2)));

        OwlOntologyDocument document = split.Complete();
        Assert.IsFalse(document.Diagnostics.HasErrors);
        Assert.HasCount(0, document.Axioms);
        Assert.AreEqual("http://example.org/o", (document.OntologyIri as NamedNode)?.Iri.ToString());
    }

    [TestMethod]
    public void TrailingWhitespaceAndCommentsKeepTheBoundaryStatus()
    {
        //Top-level character data is insignificant whitespace and is discarded,
        //so a whitespace tail after the closed root is a document boundary, not
        //an unfinished unit: Status stays Complete (the editor must not be told
        //to keep waiting on a finished document).
        OwlXmlSyntaxIncrementalReader reader = new();
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(System.Text.Encoding.UTF8.GetBytes(CleanDocument + "\n  \n")));

        //A comment scans and commits without pushing, so it keeps the boundary;
        //a comment whose '-->' has not arrived is genuinely unfinished and
        //suspends until it closes.
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed("<!-- trailing"u8));
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(" -->"u8));

        OwlOntologyDocument document = reader.Complete();
        Assert.IsFalse(document.Diagnostics.HasErrors);
        Assert.HasCount(3, document.Axioms);
    }

    [TestMethod]
    public void MidUnitTailsSuspendWithoutDiagnostics()
    {
        //Every half-scanned markup unit returns NeedMore; the byte scanner has no
        //diagnostic path at all, so a merely-unfinished tail is never squiggled.
        foreach(string tail in (string[])
        [
            "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><Declaration><Class IRI=\"http://example.org/A",
            "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><Declaration><Class IRI=\"http://example.org/A\" ",
            "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><!-- x",
            "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><![CDATA[x",
            "<?xml version=\"1.0",
            "<!DOCTYPE Ontology [ <!ENTITY ex \"http://example.org/",
            "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"></Ontolog",
            "<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><",
        ])
        {
            OwlXmlSyntaxIncrementalReader reader = new();

            Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(System.Text.Encoding.UTF8.GetBytes(tail)), tail);
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"'{tail}' squiggled a merely-unfinished tail");
        }
    }

    [TestMethod]
    public void PredefinedAndNumericAndDeclaredReferencesReadIdenticallyUnderChunking()
    {
        //Predefined, decimal, hexadecimal, and DOCTYPE-declared general entities
        //expand in both attribute values and element text; a reference split
        //across a chunk boundary is decoded only once its committed unit is whole.
        const string Document = """<?xml version="1.0"?><!DOCTYPE Ontology [ <!ENTITY ex "http://example.org/"> ]><Ontology xmlns="http://www.w3.org/2002/07/owl#"><Declaration><Class IRI="&ex;A"/></Declaration><AnnotationAssertion><AnnotationProperty IRI="http://www.w3.org/2000/01/rdf-schema#label"/><IRI>&ex;B</IRI><Literal datatypeIRI="http://www.w3.org/2001/XMLSchema#string">a&amp;b &#233; &#xE9;</Literal></AnnotationAssertion></Ontology>""";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(bytes);
        Assert.IsFalse(whole.Diagnostics.HasErrors);

        //Pin the concrete decode: the entity expands the IRIs, and both numeric
        //references resolve to U+00E9.
        OwlDeclarationAxiom declaration = Find<OwlDeclarationAxiom>(whole)!;
        Assert.AreEqual("http://example.org/A", declaration.Entity.Iri.ToString());
        OwlAnnotationAssertionAxiom assertion = Find<OwlAnnotationAssertionAxiom>(whole)!;
        Assert.AreEqual("http://example.org/B", (assertion.Subject as NamedNode)?.Iri.ToString());
        Assert.AreEqual("a&b é é", (assertion.Value as Literal)?.Value.ToString());

        string rendering = OwlFunctionalSyntaxWriter.Write(whole);
        for(int cut = 0; cut <= bytes.Length; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));
            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.IsFalse(resumed.Diagnostics.HasErrors, $"cut {cut}: the resumed parse reported an error");
            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void UnknownAndBareAndForwardReferencesAreResolvedLenientlyUnderChunking()
    {
        //The decode is lenient and stable under chunking: an unknown entity
        //expands to nothing, a bare '&' with no ';' is emitted verbatim, and a
        //reference whose declaration never arrives is dropped — all silently.
        AssertLiteralValue("""<Ontology xmlns="http://www.w3.org/2002/07/owl#"><AnnotationAssertion><AnnotationProperty IRI="http://www.w3.org/2000/01/rdf-schema#label"/><IRI>http://example.org/A</IRI><Literal datatypeIRI="http://www.w3.org/2001/XMLSchema#string">a&nbsp;b</Literal></AnnotationAssertion></Ontology>""", "ab");
        AssertLiteralValue("""<Ontology xmlns="http://www.w3.org/2002/07/owl#"><AnnotationAssertion><AnnotationProperty IRI="http://www.w3.org/2000/01/rdf-schema#label"/><IRI>http://example.org/A</IRI><Literal datatypeIRI="http://www.w3.org/2001/XMLSchema#string">AT&T</Literal></AnnotationAssertion></Ontology>""", "AT&T");
    }

    [TestMethod]
    public void CdataAndCommentsCoalesceTextInOrderUnderChunking()
    {
        //Character data interrupted by a comment and a CDATA section coalesces in
        //document order; the CDATA body is verbatim, so an '&amp;' inside it is
        //not decoded and a '<x>' inside it is not markup.
        const string Document = """<Ontology xmlns="http://www.w3.org/2002/07/owl#"><AnnotationAssertion><AnnotationProperty IRI="http://www.w3.org/2000/01/rdf-schema#label"/><IRI>http://example.org/A</IRI><Literal datatypeIRI="http://www.w3.org/2001/XMLSchema#string">foo<!--c-->bar<![CDATA[<x>&amp;]]>baz</Literal></AnnotationAssertion></Ontology>""";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(bytes);
        Assert.IsFalse(whole.Diagnostics.HasErrors);
        Assert.AreEqual("foobar<x>&amp;baz", (Find<OwlAnnotationAssertionAxiom>(whole)!.Value as Literal)?.Value.ToString());

        string rendering = OwlFunctionalSyntaxWriter.Write(whole);
        for(int cut = 0; cut <= bytes.Length; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));
            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void TagCloseInsideAQuotedAttributeValueSuspendsUntilTheRealClose()
    {
        //A '>' inside a quoted attribute value is not the tag close, so a chunk
        //ending after the embedded '>' still suspends until the real closing '>'.
        const string Document = """<Ontology xmlns="http://www.w3.org/2002/07/owl#"><Declaration><Class IRI="http://example.org/a>b"/></Declaration></Ontology>""";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        int embedded = Document.IndexOf("a>b", StringComparison.Ordinal) + 2;

        OwlXmlSyntaxIncrementalReader split = new();
        Assert.AreEqual(IncrementalParseStatus.NeedMore, split.Feed(bytes.AsSpan(0, embedded)));
        Assert.IsFalse(split.Diagnostics.HasErrors);
        split.Feed(bytes.AsSpan(embedded));
        OwlOntologyDocument document = split.Complete();

        Assert.IsFalse(document.Diagnostics.HasErrors);
        Assert.HasCount(1, document.Axioms);
        Assert.AreEqual("http://example.org/a>b", Find<OwlDeclarationAxiom>(document)!.Entity.Iri.ToString());
    }

    [TestMethod]
    public void SpansAreStableAcrossChunkBoundariesAndCountUtf8Bytes()
    {
        //A two-byte character on the first line shifts byte offsets ahead of
        //character offsets; the converter rejects the unsupported operand element
        //and anchors a diagnostic to its span. The chunk boundary falls inside the
        //multi-byte character, so the span must still count UTF-8 bytes and match
        //the whole-buffer read.
        const string Document = "<!--ä-->\n<Ontology xmlns=\"http://www.w3.org/2002/07/owl#\"><SubClassOf><Bogus/><Class IRI=\"http://example.org/A\"/></SubClassOf></Ontology>";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(bytes);
        Assert.IsTrue(whole.Diagnostics.HasErrors);
        Diagnostic oracle = FirstDiagnostic(whole);

        int split = Array.IndexOf(bytes, (byte)'<', 1);
        OwlXmlSyntaxIncrementalReader reader = new();
        reader.Feed(bytes.AsSpan(0, 5));
        reader.Feed(bytes.AsSpan(5, split - 5));
        reader.Feed(bytes.AsSpan(split));
        OwlOntologyDocument resumed = reader.Complete();

        Assert.IsTrue(resumed.Diagnostics.HasErrors);
        Diagnostic chunked = FirstDiagnostic(resumed);
        Assert.AreEqual(oracle.Span.StartByte, chunked.Span.StartByte);
        Assert.AreEqual(oracle.Span.StartLine, chunked.Span.StartLine);
        Assert.AreEqual(oracle.Span.StartColumn, chunked.Span.StartColumn);

        //The offending element is on the second line, after the comment line that
        //carries the multi-byte character, confirming byte columns reset per line.
        Assert.AreEqual(1, chunked.Span.StartLine);
    }

    [TestMethod]
    public void TruncationClosesElementsSilentlyAndKeepsTheCompletedPrefix()
    {
        //An unterminated tail is NeedMore with no diagnostic before completion;
        //on completion the open elements close silently and the half-scanned
        //trailing comment is abandoned, so a well-formed prefix completes without
        //an error and keeps its axioms. XML tolerates truncation structurally —
        //the opposite of the functional syntax, which turns truncation into an
        //error.
        const string Truncated = """<Ontology xmlns="http://www.w3.org/2002/07/owl#"><Declaration><Class IRI="http://example.org/A"/></Declaration><!-- never closed""";

        OwlXmlSyntaxIncrementalReader reader = new();
        Assert.AreEqual(IncrementalParseStatus.NeedMore, reader.Feed(System.Text.Encoding.UTF8.GetBytes(Truncated)));
        Assert.IsFalse(reader.Diagnostics.HasErrors, "incompleteness squiggled before Complete");

        OwlOntologyDocument document = reader.Complete();
        Assert.IsFalse(document.Diagnostics.HasErrors, "truncation of a well-formed prefix is not an error in XML");
        Assert.HasCount(1, document.Axioms, "the declaration parsed before the truncation is kept");

        //The whole-buffer facade is the same machinery, so it agrees.
        OwlOntologyDocument wholeBuffer = OwlXmlSyntaxReader.Read(Truncated);
        Assert.IsFalse(wholeBuffer.Diagnostics.HasErrors);
        Assert.HasCount(document.Axioms.Length, wholeBuffer.Axioms);
    }

    [TestMethod]
    public void TruncationBeforeAnyOntologyYieldsAnEmptyErrorFreeDocument()
    {
        //With no Ontology element ever opened, conversion is a no-op: the document
        //is empty and error-free, and chunked feeding agrees with whole-buffer.
        foreach(string text in (string[])
        [
            """<Thing xmlns="http://www.w3.org/2002/07/owl#"><Declaration><Class IRI="http://example.org/A"/></Declaration></Thing>""",
            "<Ontolog",
        ])
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
            for(int cut = 0; cut <= bytes.Length; cut++)
            {
                OwlXmlSyntaxIncrementalReader reader = new();
                reader.Feed(bytes.AsSpan(0, cut));
                reader.Feed(bytes.AsSpan(cut));
                OwlOntologyDocument document = reader.Complete();

                Assert.IsFalse(document.Diagnostics.HasErrors, $"'{text}' cut {cut}: reported an error");
                Assert.HasCount(0, document.Axioms, $"'{text}' cut {cut}: produced axioms");
                Assert.IsNull(document.OntologyIri, $"'{text}' cut {cut}: produced an ontology IRI");
            }
        }
    }

    [TestMethod]
    public void MismatchedAndStrayEndTagsKeepStackIntegrityWithoutDiagnostics()
    {
        //End tags are name-blind: a mismatched end tag pops the open element by
        //position, and a surplus end tag at the root is a no-op. Neither is a
        //diagnostic and neither underflows the stack.
        const string Document = """<Ontology xmlns="http://www.w3.org/2002/07/owl#"><Declaration><Class IRI="http://example.org/A"/></Wrong></Ontology></Stray>""";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(Document);
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(bytes);

        Assert.IsFalse(whole.Diagnostics.HasErrors);
        Assert.HasCount(1, whole.Axioms);

        string rendering = OwlFunctionalSyntaxWriter.Write(whole);
        for(int cut = 0; cut <= bytes.Length; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));
            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.IsFalse(resumed.Diagnostics.HasErrors, $"cut {cut}: reported an error");
            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void FeedAfterCompleteIsRejected()
    {
        OwlXmlSyntaxIncrementalReader reader = new();
        reader.Feed(System.Text.Encoding.UTF8.GetBytes(CleanDocument));
        reader.Complete();

        byte[] more = " "u8.ToArray();
        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Feed(more));
    }

    [TestMethod]
    public void CompleteIsIdempotentAndEmptyFeedsKeepTheStatus()
    {
        OwlXmlSyntaxIncrementalReader reader = new();
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(ReadOnlySpan<byte>.Empty));

        reader.Feed(System.Text.Encoding.UTF8.GetBytes(CleanDocument));
        Assert.AreEqual(IncrementalParseStatus.Complete, reader.Feed(ReadOnlySpan<byte>.Empty));

        OwlOntologyDocument first = reader.Complete();
        OwlOntologyDocument second = reader.Complete();

        Assert.AreSame(first, second);
        Assert.HasCount(first.Axioms.Length, second.Axioms);
        Assert.HasCount(first.Diagnostics.Diagnostics.Count, second.Diagnostics.Diagnostics);
    }

    [TestMethod]
    public void ChunkedFeedingMatchesWholeBufferOverTheCorpus()
    {
        //The corpus ships no native OWL/XML, so each functional document is
        //rendered to OWL/XML through the writer and fed in fixed-stride chunks;
        //the chunked read must match the whole-buffer read of the same XML.
        int documents = 0;
        foreach(string status in (string[])["approved", "proposed"])
        {
            foreach(Owl2TestCase testCase in Owl2ManifestLoader.Load(W3cCorpusPath.For("Owl2", status, "all.rdf")))
            {
                foreach(string? text in (string?[])[testCase.FunctionalPremise, testCase.FunctionalConclusion, testCase.FunctionalNonConclusion])
                {
                    if(text is null)
                    {
                        continue;
                    }

                    OwlOntologyDocument source = OwlFunctionalSyntaxReader.Read(text);
                    if(source.Diagnostics.HasErrors)
                    {
                        continue;
                    }

                    string xml = OwlXmlSyntaxWriter.Write(source);
                    OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(xml);

                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(xml);
                    OwlXmlSyntaxIncrementalReader reader = new();
                    for(int offset = 0; offset < bytes.Length; offset += 17)
                    {
                        reader.Feed(bytes.AsSpan(offset, Math.Min(17, bytes.Length - offset)));
                    }

                    OwlOntologyDocument chunked = reader.Complete();

                    Assert.AreEqual(whole.Diagnostics.HasErrors, chunked.Diagnostics.HasErrors, $"{testCase.Identifier}: error disagreement");
                    Assert.HasCount(whole.Axioms.Length, chunked.Axioms, $"{testCase.Identifier}: axiom count differs");

                    if(!whole.Diagnostics.HasErrors)
                    {
                        Assert.AreEqual(OwlFunctionalSyntaxWriter.Write(whole), OwlFunctionalSyntaxWriter.Write(chunked), $"{testCase.Identifier}: rendering differs");
                    }

                    documents++;
                }
            }
        }

        Assert.IsGreaterThan(50, documents, "The corpus sweep should cover the functional-syntax documents.");
    }

    /// <summary>Reads a document whole and asserts the value of its single annotation-assertion literal, then checks chunked feeding agrees.</summary>
    /// <param name="document">The OWL/XML document text.</param>
    /// <param name="expected">The expected decoded literal value.</param>
    private static void AssertLiteralValue(string document, string expected)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(document);
        OwlOntologyDocument whole = OwlXmlSyntaxReader.Read(bytes);
        Assert.IsFalse(whole.Diagnostics.HasErrors, document);
        Assert.AreEqual(expected, (Find<OwlAnnotationAssertionAxiom>(whole)!.Value as Literal)?.Value.ToString(), document);

        string rendering = OwlFunctionalSyntaxWriter.Write(whole);
        for(int cut = 0; cut <= bytes.Length; cut++)
        {
            OwlXmlSyntaxIncrementalReader reader = new();
            reader.Feed(bytes.AsSpan(0, cut));
            reader.Feed(bytes.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"{document} cut {cut}: rendering differs");
        }
    }

    /// <summary>The first axiom of a kind in a document, or <see langword="null"/> when none.</summary>
    /// <typeparam name="T">The axiom kind.</typeparam>
    /// <param name="document">The document to search.</param>
    /// <returns>The first matching axiom, or <see langword="null"/>.</returns>
    private static T? Find<T>(OwlOntologyDocument document)
        where T : OwlAxiom
    {
        foreach(OwlAxiom axiom in document.Axioms)
        {
            if(axiom is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>The first diagnostic recorded on a document.</summary>
    /// <param name="document">The document whose diagnostics to read.</param>
    /// <returns>The first diagnostic.</returns>
    private static Diagnostic FirstDiagnostic(OwlOntologyDocument document)
    {
        foreach(Diagnostic diagnostic in document.Diagnostics.Diagnostics)
        {
            return diagnostic;
        }

        throw new InvalidOperationException("The document recorded no diagnostics.");
    }
}
