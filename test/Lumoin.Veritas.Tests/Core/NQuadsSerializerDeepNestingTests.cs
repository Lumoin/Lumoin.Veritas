using System.Collections.Generic;
using Lumoin.Veritas.Canonicalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Parsing;

namespace Lumoin.Veritas.Tests.Core;

/// <summary>
/// Verifies the canonical N-Quads serializer walks deeply-nested quoted triples over an explicit stack:
/// nesting at the limit serializes (and threads the blank-node map into nested leaves), and nesting beyond
/// the limit raises the catchable <see cref="TripleTermDepthLimitException"/> rather than overflowing.
/// </summary>
/// <remarks>
/// These exercise <see cref="NQuadsSerializer.SerializeTerm"/> directly (it is internal-visible) so the term
/// walk is isolated from <see cref="RdfCanonicalizer"/>'s quad hashing, which would otherwise recurse through
/// the synthesized record members on a deep term before the serializer runs.
/// </remarks>
[TestClass]
internal sealed class NQuadsSerializerDeepNestingTests
{
    /// <summary>An empty blank-node relabelling map for terms that contain no blank nodes.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoBlankNodes = new Dictionary<string, string>();

    /// <summary>A quoted triple nested at the depth limit serializes to completion (one open delimiter per level).</summary>
    [TestMethod]
    public void DeepTripleTermAtTheNestingLimitSerializes()
    {
        string output = NQuadsSerializer.SerializeTerm(NestSubject(QuotedTripleLimits.MaxNestingDepth), NoBlankNodes);

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, output.Split("<<( ").Length - 1);
    }

    /// <summary>A quoted triple nested beyond the limit throws the catchable depth exception, not a stack overflow.</summary>
    [TestMethod]
    public void DeepTripleTermBeyondTheNestingLimitThrows()
    {
        TripleTermDepthLimitException exception = Assert.ThrowsExactly<TripleTermDepthLimitException>(
            () => NQuadsSerializer.SerializeTerm(NestSubject(QuotedTripleLimits.MaxNestingDepth + 1), NoBlankNodes));

        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth + 1, exception.Depth);
        Assert.AreEqual(QuotedTripleLimits.MaxNestingDepth, exception.Limit);
    }

    /// <summary>A blank node nested inside a quoted triple is relabelled through the map, proving the map reaches nested leaves.</summary>
    [TestMethod]
    public void NestedBlankNodeIsRelabelledThroughTheMap()
    {
        Dictionary<string, string> map = new() { ["b0"] = "c14n0" };
        RdfTerm term = new TripleTerm(
            new BlankNode(Utf8Strings.From("b0")),
            new NamedNode(Utf8Strings.From("http://example/p")),
            new NamedNode(Utf8Strings.From("http://example/o")));

        string output = NQuadsSerializer.SerializeTerm(term, map);

        Assert.AreEqual("<<( _:c14n0 <http://example/p> <http://example/o> )>>", output);
    }

    /// <summary>Builds a quoted triple nested <paramref name="depth"/> levels deep through the subject, with IRI leaves.</summary>
    /// <param name="depth">The number of quoted-triple nesting levels.</param>
    /// <returns>The nested term.</returns>
    private static RdfTerm NestSubject(int depth)
    {
        NamedNode predicate = new(Utf8Strings.From("http://example/p"));
        RdfTerm leaf = new NamedNode(Utf8Strings.From("http://example/o"));

        RdfTerm term = leaf;
        for(int i = 0; i < depth; i++)
        {
            term = new TripleTerm(term, predicate, leaf);
        }

        return term;
    }
}
