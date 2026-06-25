using System;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Jsonata;

/// <summary>
/// Verifies the byte-fed <see cref="JsonataIncrementalReader"/> editor surface: feeding an expression in arbitrary
/// chunks (down to one byte at a time) produces the identical <see cref="JsonataExpression"/> the whole-buffer
/// <c>Jsonata.Parse</c> facade produces, that incompleteness is the <see cref="IncrementalParseStatus.NeedMore"/>
/// status (never a throw), and that malformed input is recovered into the result rather than thrown.
/// </summary>
[TestClass]
internal sealed class JsonataIncrementalReaderTests
{
    /// <summary>A path expression round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void PathMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("a.b.c");
    }

    /// <summary>An arithmetic expression round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void ArithmeticMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("price * quantity + tax");
    }

    /// <summary>A function call over a path round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void FunctionCallMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("$sum(items.price)");
    }

    /// <summary>A predicate filter followed by a path step round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void PredicatePathMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("items[price > 100].name");
    }

    /// <summary>A conditional over string literals round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void ConditionalMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("a > b ? \"big\" : \"small\"");
    }

    /// <summary>A string literal with a <c>\\u</c> escape spanning chunk boundaries round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void UnicodeEscapeStringMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("\"caf\\u00e9\"");
    }

    /// <summary>An object-construction expression round-trips identically byte-by-byte.</summary>
    [TestMethod]
    public void ObjectConstructionMatchesWholeBuffer()
    {
        AssertByteByByteMatchesWholeBuffer("{\"total\": $sum(x), \"n\": $count(y)}");
    }

    /// <summary>An expression that is still being typed reports NeedMore; the lexer/parser never throw mid-input.</summary>
    [TestMethod]
    public void PartialInputReportsNeedMore()
    {
        JsonataIncrementalReader reader = new();
        IncrementalParseStatus status = reader.Feed(Encoding.UTF8.GetBytes("a.b +"));

        Assert.AreEqual(IncrementalParseStatus.NeedMore, status, "an unterminated expression must report NeedMore, not error");
    }

    /// <summary>Completing a truncated expression recovers into a result with diagnostics rather than throwing.</summary>
    [TestMethod]
    public void CompletingTruncatedInputRecovers()
    {
        JsonataIncrementalReader reader = new();
        reader.Feed(Encoding.UTF8.GetBytes("a.b +"));
        ParseResult<JsonataExpression> result = reader.Complete();

        Assert.IsTrue(result.HasErrors, "a truncated expression must surface diagnostics, recovered not thrown");
        Assert.IsNotNull(result.Tree);
    }

    /// <summary>Completing twice returns the cached, already-path-processed tree rather than re-processing it (the path pass is not idempotent and records diagnostics).</summary>
    [TestMethod]
    public void CompleteIsIdempotent()
    {
        JsonataIncrementalReader reader = new();
        reader.Feed(Encoding.UTF8.GetBytes("a.b.c"));
        ParseResult<JsonataExpression> first = reader.Complete();
        ParseResult<JsonataExpression> second = reader.Complete();

        Assert.AreSame(first.Tree, second.Tree, "a second Complete must return the cached tree, not re-run the path-processing pass");
    }

    /// <summary>Feeding after completion is rejected (the OWL/SPARQL incremental-reader contract).</summary>
    [TestMethod]
    public void FeedAfterCompleteThrows()
    {
        JsonataIncrementalReader reader = new();
        reader.Feed(Encoding.UTF8.GetBytes("a.b.c"));
        _ = reader.Complete();

        Assert.ThrowsExactly<InvalidOperationException>(() => reader.Feed(Encoding.UTF8.GetBytes(" ")));
    }

    /// <summary>Feeds an expression one byte at a time and asserts the resulting expression renders identically to the whole-buffer parse over the same pool.</summary>
    /// <param name="expression">The JSONata expression text.</param>
    private static void AssertByteByByteMatchesWholeBuffer(string expression)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(expression);
        using Utf8StringPool pool = new();
        JsonataExpression whole = global::Lumoin.Veritas.Jsonata.Jsonata.Parse(bytes, pool).Tree;

        JsonataIncrementalReader reader = new(pool);
        for(int i = 0; i < bytes.Length; i++)
        {
            reader.Feed(bytes.AsSpan(i, 1));
        }

        ParseResult<JsonataExpression> incremental = reader.Complete();

        Assert.IsFalse(incremental.HasErrors, "the well-formed expression must parse without diagnostics");
        Assert.AreEqual(AstStructuralRenderer.Render(whole), AstStructuralRenderer.Render(incremental.Tree), "byte-by-byte incremental parse must render identically to the whole-buffer parse");
    }
}
