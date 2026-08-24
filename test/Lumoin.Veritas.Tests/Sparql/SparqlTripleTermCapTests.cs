using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Sparql.Execution;
using Lumoin.Veritas.Sparql.Results;

namespace Lumoin.Veritas.Tests.Sparql;

/// <summary>
/// Verifies the SPARQL runtime term walkers cap quoted-triple nesting at the shared
/// <see cref="QuotedTripleLimits.MaxNestingDepth"/>: a term at the limit is processed, a deeper one raises the
/// catchable <see cref="TripleTermDepthLimitException"/> rather than growing an unbounded work-stack.
/// </summary>
[TestClass]
internal sealed class SparqlTripleTermCapTests
{
    private static NamedNode Iri(string local) => new(Utf8Strings.From("http://example/" + local));

    /// <summary>Builds a quoted triple nested <paramref name="depth"/> levels deep through the subject.</summary>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term.</returns>
    private static RdfTerm NestSubject(int depth)
    {
        NamedNode predicate = Iri("p");
        RdfTerm leaf = Iri("o");

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }

    /// <summary>The CSV/TSV term renderer renders at the limit and throws beyond it.</summary>
    [TestMethod]
    public void ResultTermTextRendersAtTheLimitAndThrowsBeyond()
    {
        string text = SparqlResultTermText.Turtle(NestSubject(QuotedTripleLimits.MaxNestingDepth));
        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, text.Split("<< ").Length - 1);

        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => SparqlResultTermText.Turtle(NestSubject(QuotedTripleLimits.MaxNestingDepth + 1)));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    /// <summary>The ORDER BY comparator compares at the limit and throws beyond it.</summary>
    [TestMethod]
    public void CompareForOrderingHandlesTheLimitAndThrowsBeyond()
    {
        Assert.AreEqual(0, SparqlExpressionEvaluator.CompareForOrdering(
            NestSubject(QuotedTripleLimits.MaxNestingDepth),
            NestSubject(QuotedTripleLimits.MaxNestingDepth),
            TimeSpan.Zero));

        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => SparqlExpressionEvaluator.CompareForOrdering(
                NestSubject(QuotedTripleLimits.MaxNestingDepth + 1),
                NestSubject(QuotedTripleLimits.MaxNestingDepth + 1),
                TimeSpan.Zero));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }
}
