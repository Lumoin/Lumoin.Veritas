using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies that lexing a source fed as a fragmented <see cref="ReadOnlySequence{T}"/>
/// produces a token stream identical to lexing the same bytes as one contiguous segment.
/// A chunk size of one byte forces every token, every CR-LF pair, and every multi-byte
/// UTF-8 character to straddle a segment boundary.
/// </summary>
[TestClass]
internal sealed class TurtleLexerMultiSegmentTests
{
    [TestMethod]
    public void FragmentedAbsoluteIriMatchesContiguous()
    {
        AssertFragmentationInvariant("<http://example.org/foo>", 1, 2, 3, 5);
    }

    [TestMethod]
    public void FragmentedIriWithUnicodeEscapeMatchesContiguous()
    {
        AssertFragmentationInvariant("<http://example.org/r\\u00E9sum\\u00E9>", 1, 2, 3, 7);
    }

    [TestMethod]
    public void FragmentedPrefixedNameMatchesContiguous()
    {
        AssertFragmentationInvariant("ex:resource", 1, 2, 3);
        AssertFragmentationInvariant(":local", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedPrefixDeclarationMatchesContiguous()
    {
        AssertFragmentationInvariant("@prefix ex: <http://example.org/> .", 1, 2, 4, 8);
    }

    [TestMethod]
    public void FragmentedBlankNodeLabelMatchesContiguous()
    {
        AssertFragmentationInvariant("_:b0", 1, 2, 3);
        AssertFragmentationInvariant("_:node.with.dots", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedStringWithEscapesMatchesContiguous()
    {
        AssertFragmentationInvariant("\"a\\tb\\u00E9c\\nd\"", 1, 2, 3, 5);
    }

    [TestMethod]
    public void FragmentedLongStringMatchesContiguous()
    {
        AssertFragmentationInvariant("\"\"\"first\nsecond \\\"quoted\\\" line\"\"\"", 1, 2, 4);
        AssertFragmentationInvariant("\"\"\"first\r\nsecond\r\nthird\"\"\"", 1, 2, 4);
    }

    [TestMethod]
    public void FragmentedNumericLiteralsMatchContiguous()
    {
        AssertFragmentationInvariant("42 +12.5 -7 .5 6.0e10 -3.0E-4", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedLanguageTagsMatchContiguous()
    {
        AssertFragmentationInvariant("\"hello\"@en-US", 1, 2, 3);
        AssertFragmentationInvariant("\"hello\"@en--ltr", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedRdfStarSyntaxMatchesContiguous()
    {
        AssertFragmentationInvariant("<<( <s> <p> <o> )>>", 1, 2, 3);
        AssertFragmentationInvariant("<< <s> <p> <o> >> {| :note \"x\" |}", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedKeywordsAndPunctuationMatchContiguous()
    {
        AssertFragmentationInvariant("true false a ; , . [ ] ( ) ^^ ~", 1, 2, 3);
        AssertFragmentationInvariant("PREFIX BASE VERSION GRAPH", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedMultibyteContentMatchesContiguous()
    {
        //Literal content carrying multi-byte UTF-8 code points; at chunk size one each character's
        //continuation bytes land in separate segments and must be reassembled by the lexer.
        AssertFragmentationInvariant("\"café à résumé 中文 😀\"", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedCarriageReturnLineFeedMatchesContiguous()
    {
        //At chunk size one the CR and the LF fall in different segments, exercising the
        //CR-LF lookahead in the advance path across a boundary.
        AssertFragmentationInvariant("<s>\r\n<p>\r\n\"o\" .\r\n", 1, 2, 3);
    }

    [TestMethod]
    public void FragmentedMultiStatementDocumentMatchesContiguous()
    {
        const string document =
            "@prefix ex: <http://example.org/> .\n"
            + "ex:alice ex:knows ex:bob ;\n"
            + "         ex:age 42 .\n"
            + "# a comment\n"
            + "ex:bob ex:name \"Bob\"@en .\n";

        AssertFragmentationInvariant(document, 1, 3, 8, 16);
    }

    private static void AssertFragmentationInvariant(string source, params int[] chunkSizes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);

        using Utf8StringPool contiguousPool = new();
        List<TurtleToken> expected = Lex(new ReadOnlySequence<byte>(bytes), contiguousPool);

        foreach(int chunkSize in chunkSizes)
        {
            using Utf8StringPool fragmentedPool = new();
            List<TurtleToken> actual = Lex(MultiSegment(bytes, chunkSize), fragmentedPool);

            AssertSameTokens(expected, actual, source, chunkSize);
        }
    }

    private static List<TurtleToken> Lex(ReadOnlySequence<byte> source, Utf8StringPool pool)
    {
        TurtleLexer lexer = new(source, pool);
        List<TurtleToken> tokens = [];
        foreach(TurtleToken token in lexer.Tokenize())
        {
            tokens.Add(token);
        }

        return tokens;
    }

    private static void AssertSameTokens(List<TurtleToken> expected, List<TurtleToken> actual, string source, int chunkSize)
    {
        Assert.HasCount(
            expected.Count,
            actual,
            string.Create(CultureInfo.InvariantCulture, $"token count differs for '{source}' at chunk size {chunkSize}"));

        for(int i = 0; i < expected.Count; i++)
        {
            string where = string.Create(CultureInfo.InvariantCulture, $"token {i} of '{source}' at chunk size {chunkSize}");

            Assert.AreEqual(expected[i].Kind, actual[i].Kind, where);
            Assert.AreEqual(expected[i].Span, actual[i].Span, where);
            Assert.IsTrue(expected[i].Value.Span.SequenceEqual(actual[i].Value.Span), where);
        }
    }

    private static ReadOnlySequence<byte> MultiSegment(ReadOnlyMemory<byte> data, int chunkSize)
    {
        BufferSegment first = new(data.Slice(0, Math.Min(chunkSize, data.Length)));
        BufferSegment last = first;
        for(int offset = chunkSize; offset < data.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, data.Length - offset);
            last = last.Append(data.Slice(offset, length));
        }

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
