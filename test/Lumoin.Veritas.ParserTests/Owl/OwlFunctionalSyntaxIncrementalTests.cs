using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;
using Lumoin.Veritas.ParserTests.Conformance;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// Verifies the functional-syntax reader's editor contract: input fed
/// incrementally produces the same document as the whole-buffer read at every
/// chunk boundary, an unfinished tail is a
/// <see cref="OwlFunctionalParseStatus.NeedMore"/> status and never a
/// diagnostic, and only <see cref="OwlFunctionalSyntaxIncrementalReader.Complete"/>
/// turns truncation into an error.
/// </summary>
[TestClass]
internal sealed class OwlFunctionalSyntaxIncrementalTests
{
    private const string CleanDocument = """
        Prefix( : = <http://example.org/> )
        Prefix( xsd: = <http://www.w3.org/2001/XMLSchema#> )
        Ontology( <http://example.org/o>
          Declaration( Class( :A ) )
          Declaration( ObjectProperty( :p ) )
          Declaration( DataProperty( :dp ) )
          SubClassOf( Annotation( :note "stated"@en ) :A ObjectSomeValuesFrom( :p :A ) )
          SubClassOf( :A DataAllValuesFrom( :dp DatatypeRestriction( xsd:integer xsd:minInclusive "0"^^xsd:integer ) ) )
          ClassAssertion( :A _:anon )
        )
        """;

    [TestMethod]
    public void EveryCutPointResumesToTheWholeBufferResult()
    {
        OwlOntologyDocument whole = OwlFunctionalSyntaxReader.Read(CleanDocument);
        Assert.IsFalse(whole.Diagnostics.HasErrors);
        string rendering = OwlFunctionalSyntaxWriter.Write(whole);

        for(int cut = 0; cut <= CleanDocument.Length; cut++)
        {
            OwlFunctionalSyntaxIncrementalReader reader = new();
            reader.Feed(CleanDocument.AsSpan(0, cut));

            //An unfinished tail must never surface as a diagnostic; the
            //status carries the incompleteness instead.
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"cut {cut}: a clean prefix reported an error");

            reader.Feed(CleanDocument.AsSpan(cut));
            OwlOntologyDocument resumed = reader.Complete();

            Assert.IsFalse(resumed.Diagnostics.HasErrors, $"cut {cut}: the resumed parse reported an error");
            Assert.HasCount(whole.Axioms.Length, resumed.Axioms, $"cut {cut}: axiom count differs");
            Assert.AreEqual(rendering, OwlFunctionalSyntaxWriter.Write(resumed), $"cut {cut}: rendering differs");
        }
    }

    [TestMethod]
    public void StatusIsNeedMoreInsideTheOntologyAndCompleteAtTheBoundary()
    {
        int ontologyOpen = CleanDocument.IndexOf("Ontology(", System.StringComparison.Ordinal) + "Ontology(".Length;
        int lastClose = CleanDocument.LastIndexOf(')') + 1;

        for(int cut = ontologyOpen; cut < lastClose; cut++)
        {
            OwlFunctionalSyntaxIncrementalReader reader = new();
            Assert.AreEqual(OwlFunctionalParseStatus.NeedMore, reader.Feed(CleanDocument.AsSpan(0, cut)), $"cut {cut}");
        }

        OwlFunctionalSyntaxIncrementalReader full = new();
        Assert.AreEqual(OwlFunctionalParseStatus.Complete, full.Feed(CleanDocument));
    }

    [TestMethod]
    public void TrailingWhitespaceAndCommentsKeepTheBoundaryStatus()
    {
        OwlFunctionalSyntaxIncrementalReader reader = new();
        Assert.AreEqual(OwlFunctionalParseStatus.Complete, reader.Feed(CleanDocument + "\n  \n"));

        //A comment whose newline has not arrived may still be growing, so it
        //suspends; its newline restores the boundary.
        Assert.AreEqual(OwlFunctionalParseStatus.NeedMore, reader.Feed("# trailing"));
        Assert.AreEqual(OwlFunctionalParseStatus.Complete, reader.Feed("\n"));

        OwlOntologyDocument document = reader.Complete();
        Assert.IsFalse(document.Diagnostics.HasErrors);
    }

    [TestMethod]
    public void CompleteOnTruncatedInputReportsTruncationErrors()
    {
        const string Truncated = """
            Prefix( : = <http://example.org/> )
            Ontology( <http://example.org/o>
              Declaration( Class( :A ) )
              SubClassOf( :A
            """;

        OwlFunctionalSyntaxIncrementalReader reader = new();
        Assert.AreEqual(OwlFunctionalParseStatus.NeedMore, reader.Feed(Truncated));
        Assert.IsFalse(reader.Diagnostics.HasErrors, "incompleteness squiggled before Complete");

        OwlOntologyDocument document = reader.Complete();

        Assert.IsTrue(document.Diagnostics.HasErrors, "Complete did not turn truncation into an error");
        Assert.HasCount(1, document.Axioms, "the axiom parsed before the truncation should be kept");

        //The whole-buffer facade is the same machinery, so it agrees.
        OwlOntologyDocument wholeBuffer = OwlFunctionalSyntaxReader.Read(Truncated);
        Assert.IsTrue(wholeBuffer.Diagnostics.HasErrors);
        Assert.HasCount(document.Axioms.Length, wholeBuffer.Axioms);
    }

    [TestMethod]
    public void MidTokenTailsSuspendWithoutDiagnostics()
    {
        foreach(string tail in (string[])
        [
            "Ontology( <http://example.org/unterminated-iri",
            "Ontology( <http://example.org/o> ClassAssertion( :A \"unterminated literal",
            "Ontology( <http://example.org/o> ClassAssertion( :A \"v\"^^xsd:in",
            "Ontology( <http://example.org/o> ClassAssertion( :A \"v\"@e",
            "Ontology( <http://example.org/o> ClassAssertion( :A _:lab",
            "Ontology( <http://example.org/o> SubClassOf( :A ObjectMinCardinality( 2",
            "Ontolog"
        ])
        {
            OwlFunctionalSyntaxIncrementalReader reader = new();

            Assert.AreEqual(OwlFunctionalParseStatus.NeedMore, reader.Feed(tail), tail);
            Assert.IsFalse(reader.Diagnostics.HasErrors, $"'{tail}' squiggled a merely-unfinished tail");
        }
    }

    [TestMethod]
    public void GenuineFaultsAreDiagnosedWhileIncomplete()
    {
        //An unexpected character can never become valid, so it is an error
        //even while the document is still arriving.
        OwlFunctionalSyntaxIncrementalReader reader = new();
        reader.Feed("Ontology( <http://example.org/o> % ");

        Assert.IsTrue(reader.Diagnostics.HasErrors);
        Assert.AreEqual(OwlFunctionalParseStatus.NeedMore, reader.Status);
    }

    [TestMethod]
    public void SpansAreStableAcrossChunkBoundariesAndCountUtf8Bytes()
    {
        //The two-byte character in the comment shifts byte offsets ahead of
        //character offsets; the chunk boundary falls inside the line that
        //carries the offending character.
        const string Document = "#ä\nOntology( <http://example.org/o>\n  %\n)";
        int split = Document.IndexOf('%', System.StringComparison.Ordinal) - 1;

        OwlFunctionalSyntaxIncrementalReader reader = new();
        reader.Feed(Document.AsSpan(0, split));
        reader.Feed(Document.AsSpan(split));
        OwlOntologyDocument document = reader.Complete();

        Assert.IsTrue(document.Diagnostics.HasErrors);
        foreach(Lumoin.Veritas.Core.Diagnostics.Diagnostic diagnostic in document.Diagnostics.Diagnostics)
        {
            Assert.AreEqual(2, diagnostic.Span.StartLine);
            Assert.AreEqual(2, diagnostic.Span.StartColumn);
            Assert.AreEqual(Document.IndexOf('%', System.StringComparison.Ordinal) + 1, diagnostic.Span.StartByte);

            return;
        }
    }

    [TestMethod]
    public void FeedAfterCompleteIsRejected()
    {
        OwlFunctionalSyntaxIncrementalReader reader = new();
        reader.Feed(CleanDocument);
        reader.Complete();

        Assert.ThrowsExactly<System.InvalidOperationException>(() => reader.Feed(" "));
    }

    [TestMethod]
    public void ChunkedFeedingMatchesWholeBufferOverTheCorpus()
    {
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

                    OwlOntologyDocument whole = OwlFunctionalSyntaxReader.Read(text);

                    OwlFunctionalSyntaxIncrementalReader reader = new();
                    for(int offset = 0; offset < text.Length; offset += 17)
                    {
                        reader.Feed(text.AsSpan(offset, System.Math.Min(17, text.Length - offset)));
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
}
