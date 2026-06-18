using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Line endings in Turtle have two faces: between tokens any of CR, LF, or CR-LF is whitespace and
/// counts as one line, while inside a long-string literal the raw bytes are part of the value and
/// must be preserved verbatim — CR-LF stays CR-LF rather than folding to a lone CR.
/// </summary>
[TestClass]
internal sealed class TurtleLexerLineEndingTests
{
    [TestMethod]
    public void LongStringPreservesCarriageReturnLineFeed()
    {
        List<TurtleToken> tokens = LexAll("\"\"\"a\r\nb\"\"\"");

        Assert.AreEqual(TurtleTokenKind.LongStringLiteral, tokens[0].Kind);
        AssertValueBytes(tokens[0], "a\r\nb"u8);
    }

    [TestMethod]
    public void LongStringPreservesLoneLineFeed()
    {
        List<TurtleToken> tokens = LexAll("\"\"\"a\nb\"\"\"");

        AssertValueBytes(tokens[0], "a\nb"u8);
    }

    [TestMethod]
    public void LongStringPreservesLoneCarriageReturn()
    {
        List<TurtleToken> tokens = LexAll("\"\"\"a\rb\"\"\"");

        AssertValueBytes(tokens[0], "a\rb"u8);
    }

    [TestMethod]
    public void CarriageReturnLineFeedCountsAsOneLine()
    {
        List<TurtleToken> tokens = LexAll("<s>\r\n<p>");

        Assert.AreEqual(0, tokens[0].Span.StartLine);
        Assert.AreEqual(1, tokens[1].Span.StartLine);
        Assert.AreEqual(0, tokens[1].Span.StartColumn);
    }

    [TestMethod]
    public void LoneCarriageReturnCountsAsOneLine()
    {
        List<TurtleToken> tokens = LexAll("<s>\r<p>");

        Assert.AreEqual(1, tokens[1].Span.StartLine);
    }

    [TestMethod]
    public void LongStringPreservesCarriageReturnLineFeedSplitAcrossSegments()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\"\"\"a\r\nb\"\"\"");
        int splitAfterCr = Array.IndexOf(bytes, (byte)'\r') + 1;

        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(SplitAt(bytes, splitAfterCr), pool);
        List<TurtleToken> tokens = [];
        foreach(TurtleToken token in lexer.Tokenize())
        {
            tokens.Add(token);
        }

        Assert.AreEqual(TurtleTokenKind.LongStringLiteral, tokens[0].Kind);
        Assert.IsTrue(tokens[0].Value.Span.SequenceEqual("a\r\nb"u8));
    }

    private static void AssertValueBytes(TurtleToken token, ReadOnlySpan<byte> expected)
    {
        Assert.IsTrue(token.Value.Span.SequenceEqual(expected));
    }

    private static List<TurtleToken> LexAll(string source)
    {
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(Encoding.UTF8.GetBytes(source), pool);
        List<TurtleToken> tokens = [];
        foreach(TurtleToken token in lexer.Tokenize())
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static ReadOnlySequence<byte> SplitAt(ReadOnlyMemory<byte> data, int boundary)
    {
        BufferSegment first = new(data[..boundary]);
        BufferSegment last = first.Append(data[boundary..]);

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class BufferSegment: ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            BufferSegment next = new(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };

            Next = next;

            return next;
        }
    }
}
