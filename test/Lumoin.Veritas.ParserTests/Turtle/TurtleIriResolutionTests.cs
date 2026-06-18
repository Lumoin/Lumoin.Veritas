using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Turtle;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies relative-IRI resolution through the public <see cref="TurtleReader"/>
/// surface: resolution against a document-context base and against in-document
/// <c>@base</c> directives (RFC 3986 §5, including dot segments and chained
/// directives), and rejection of references that stay relative because no
/// absolute base is in scope.
/// </summary>
[TestClass]
internal sealed class TurtleIriResolutionTests
{
    public TestContext TestContext { get; set; } = null!;

    private const string AbsolutePredicateObject = "<http://example.org/p> <http://example.org/o> .";

    [TestMethod]
    public async Task ResolvesRelativeIriAgainstDocumentContextBase()
    {
        List<Quad> quads = await ReadAsync($"<s> {AbsolutePredicateObject}", "http://example.org/base/").ConfigureAwait(false);

        Assert.AreEqual("http://example.org/base/s", SubjectIri(quads));
    }

    [TestMethod]
    public async Task ResolvesRelativeIriAgainstInDocumentBase()
    {
        string turtle = $"@base <http://example.org/d/> .\n<s> {AbsolutePredicateObject}";

        List<Quad> quads = await ReadAsync(turtle, baseIri: null).ConfigureAwait(false);

        Assert.AreEqual("http://example.org/d/s", SubjectIri(quads));
    }

    [TestMethod]
    public async Task ResolvesAtBaseDirectiveAgainstPreviousBase()
    {
        string turtle = $"@base <http://example.org/a/> .\n@base <b/> .\n<c> {AbsolutePredicateObject}";

        List<Quad> quads = await ReadAsync(turtle, baseIri: null).ConfigureAwait(false);

        Assert.AreEqual("http://example.org/a/b/c", SubjectIri(quads));
    }

    [TestMethod]
    public async Task PreservesPathDotSegmentsAcrossAtBase()
    {
        string turtle = $"@base <http://example.org/a/b/c/> .\n<../g> {AbsolutePredicateObject}";

        List<Quad> quads = await ReadAsync(turtle, baseIri: null).ConfigureAwait(false);

        Assert.AreEqual("http://example.org/a/b/g", SubjectIri(quads));
    }

    [TestMethod]
    public async Task InDocumentBaseOverridesDocumentContextBase()
    {
        string turtle = $"@base <http://file.example/> .\n<s> {AbsolutePredicateObject}";

        List<Quad> quads = await ReadAsync(turtle, "http://document.example/").ConfigureAwait(false);

        Assert.AreEqual("http://file.example/s", SubjectIri(quads));
    }

    [TestMethod]
    public async Task RejectsRelativeIriWhenNoBaseInScope()
    {
        DiagnosticBag diagnostics = await DiagnosticsAsync($"<s> {AbsolutePredicateObject}", baseIri: null).ConfigureAwait(false);

        Assert.IsTrue(diagnostics.HasErrors);
        Assert.IsTrue(ContainsCode(diagnostics, WellKnownDiagnostics.Turtle.UnresolvableRelativeIri));
    }

    [TestMethod]
    public async Task RejectsRelativeIriWhenAtBaseItselfRelativeAndUnresolvable()
    {
        string turtle = $"@base <relative/> .\n<c> {AbsolutePredicateObject}";

        DiagnosticBag diagnostics = await DiagnosticsAsync(turtle, baseIri: null).ConfigureAwait(false);

        Assert.IsTrue(diagnostics.HasErrors);
        Assert.IsTrue(ContainsCode(diagnostics, WellKnownDiagnostics.Turtle.UnresolvableRelativeIri));
    }

    private static async Task<List<Quad>> ReadAsync(string turtle, string? baseIri)
    {
        (List<Quad> quads, DiagnosticBag diagnostics) = await ReadWithDiagnosticsAsync(turtle, baseIri).ConfigureAwait(false);

        Assert.IsFalse(diagnostics.HasErrors, "Resolution should succeed without diagnostics for this case.");

        return quads;
    }

    private static async Task<DiagnosticBag> DiagnosticsAsync(string turtle, string? baseIri)
    {
        (List<Quad> _, DiagnosticBag diagnostics) = await ReadWithDiagnosticsAsync(turtle, baseIri).ConfigureAwait(false);

        return diagnostics;
    }

    private static async Task<(List<Quad> Quads, DiagnosticBag Diagnostics)> ReadWithDiagnosticsAsync(string turtle, string? baseIri)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(turtle);
        DiagnosticBag diagnostics = new();
        List<Quad> quads = [];
        await foreach(Quad quad in TurtleReader.ReadAsync(
            new ReadOnlyMemory<byte>(bytes),
            TurtleSyntax.Turtle,
            diagnostics,
            pool: null,
            baseIri: baseIri,
            cancellationToken: CancellationToken.None).ConfigureAwait(false))
        {
            quads.Add(quad);
        }

        return (quads, diagnostics);
    }

    private static bool ContainsCode(DiagnosticBag diagnostics, Utf8String code)
    {
        foreach(Diagnostic diagnostic in diagnostics.Diagnostics)
        {
            if(diagnostic.Code.Equals(code))
            {
                return true;
            }
        }

        return false;
    }

    private static string SubjectIri(List<Quad> quads)
    {
        Assert.HasCount(1, quads);
        NamedNode subject = (NamedNode)quads[0].Subject;

        return subject.Iri.ToString();
    }
}
