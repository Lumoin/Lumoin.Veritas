using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Owl.Functional;
using Lumoin.Veritas.Owl.Structural;

namespace Lumoin.Veritas.ParserTests.Owl;

/// <summary>
/// The functional-syntax reader's diagnostics carry source spans: the
/// offending token's extent for lexical errors, and the converting node's
/// extent for structural ones — zero-based, half-open, in both byte and
/// line-column form.
/// </summary>
[TestClass]
internal sealed class OwlFunctionalSyntaxSpanTests
{
    [TestMethod]
    public void UnknownAxiomConstructorSpansItsWholeGroup()
    {
        const string Document = """
            Prefix( : = <http://example.org/> )
            Ontology( <http://example.org/o>
              NoSuchAxiom( :A :B )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Diagnostic diagnostic = FirstError(document);
        Assert.AreEqual(2, diagnostic.Span.StartLine);
        Assert.AreEqual(2, diagnostic.Span.StartColumn);
        Assert.AreEqual(2, diagnostic.Span.EndLine);
        Assert.AreEqual(22, diagnostic.Span.EndColumn);
    }

    [TestMethod]
    public void UnexpectedCharacterSpansTheCharacter()
    {
        const string Document = """
            Ontology( <http://example.org/o>
              %
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Diagnostic diagnostic = FirstError(document);
        Assert.AreEqual(1, diagnostic.Span.StartLine);
        Assert.AreEqual(2, diagnostic.Span.StartColumn);
        Assert.AreEqual(3, diagnostic.Span.EndColumn);
    }

    [TestMethod]
    public void UndeclaredPrefixSpansTheConvertingNode()
    {
        const string Document = """
            Prefix( : = <http://example.org/> )
            Ontology( <http://example.org/o>
              Declaration( Class( undeclared:A ) )
            )
            """;

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Diagnostic diagnostic = FirstError(document);
        Assert.AreEqual(2, diagnostic.Span.StartLine);
    }

    [TestMethod]
    public void ByteOffsetsCountUtf8NotChars()
    {
        //The comment's two-byte character shifts byte offsets ahead of
        //character offsets for everything after it.
        const string Document = "#ä\nOntology( <http://example.org/o>\n  %\n)";

        OwlOntologyDocument document = OwlFunctionalSyntaxReader.Read(Document);

        Diagnostic diagnostic = FirstError(document);
        Assert.AreEqual(2, diagnostic.Span.StartLine);
        Assert.AreEqual(2, diagnostic.Span.StartColumn);

        //Char offset of '%' is 39; the 'ä' adds one byte.
        Assert.AreEqual(Document.IndexOf('%', System.StringComparison.Ordinal) + 1, diagnostic.Span.StartByte);
        Assert.AreEqual(diagnostic.Span.StartByte + 1, diagnostic.Span.EndByte);
    }

    private static Diagnostic FirstError(OwlOntologyDocument document)
    {
        Assert.IsTrue(document.Diagnostics.HasErrors);
        foreach(Diagnostic diagnostic in document.Diagnostics.Diagnostics)
        {
            if(diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return diagnostic;
            }
        }

        Assert.Fail("No error diagnostic was recorded.");

        return default!;
    }
}
