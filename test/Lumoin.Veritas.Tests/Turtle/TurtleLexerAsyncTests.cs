using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Lexer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.Tests.Turtle;

/// <summary>
/// Verifies that the asynchronous pull lexer yields exactly the tokens the synchronous whole-buffer
/// lexer does, even when the pipe delivers the source a few bytes per read so that tokens, CR-LF
/// pairs, and multi-byte characters straddle read boundaries and force the NeedMore path.
/// </summary>
[TestClass]
internal sealed class TurtleLexerAsyncTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AsyncMatchesSyncForIrisAndPrefixedNames()
    {
        await AssertAsyncMatchesSync("<http://example.org/foo> ex:bar :baz", [1, 2, 3, 5], TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AsyncMatchesSyncForStringsAndEscapes()
    {
        await AssertAsyncMatchesSync("\"a\\tb\\u00E9c\" '''line\none\\\"two'''", [1, 2, 3, 7], TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AsyncMatchesSyncForNumericsAndLanguageTags()
    {
        await AssertAsyncMatchesSync("42 +1.5 -3.0e-4 .5 \"hi\"@en-US \"yo\"@en--ltr", [1, 2, 3], TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AsyncMatchesSyncForRdfStarAndPunctuation()
    {
        await AssertAsyncMatchesSync("<<( <s> <p> <o> )>> << <s> <p> <o> >> {| :n \"x\" |} ; , . ^^", [1, 2, 3], TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AsyncMatchesSyncForMultibyteAndCarriageReturnLineFeed()
    {
        await AssertAsyncMatchesSync("<s>\r\n\"café 中文 😀\"\r\nex:p \"\"\"x\r\ny\"\"\" .", [1, 2, 3], TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AsyncMatchesSyncForFullDocument()
    {
        const string document =
            "@prefix ex: <http://example.org/> .\r\n"
            + "ex:alice ex:knows ex:bob ;\n"
            + "         ex:age 42 .\n"
            + "# a comment spanning a read\n"
            + "ex:bob ex:name \"Bob\"@en , \"Bobby\" .\n";

        await AssertAsyncMatchesSync(document, [1, 3, 8, 32], TestContext.CancellationToken).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task AsyncThrowsOnUnterminatedIriAtEndOfStream()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("<http://example.org/unterminated");

        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(pool);
        CancellationToken cancellationToken = TestContext.CancellationToken;

        await Assert.ThrowsAsync<TurtleParseException>(async () =>
        {
            await foreach(TurtleToken token in lexer.TokenizeAsync(CreatePipe(bytes, 4), cancellationToken).ConfigureAwait(false))
            {
                _ = token;
            }
        }).ConfigureAwait(false);
    }

    private static async Task AssertAsyncMatchesSync(string source, int[] chunkSizes, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);

        using Utf8StringPool syncPool = new();
        TurtleLexer syncLexer = new(bytes, syncPool);
        List<TurtleToken> expected = [];
        foreach(TurtleToken token in syncLexer.Tokenize())
        {
            expected.Add(token);
        }

        foreach(int chunkSize in chunkSizes)
        {
            using Utf8StringPool asyncPool = new();
            TurtleLexer asyncLexer = new(asyncPool);
            List<TurtleToken> actual = [];
            await foreach(TurtleToken token in asyncLexer.TokenizeAsync(CreatePipe(bytes, chunkSize), cancellationToken).ConfigureAwait(false))
            {
                actual.Add(token);
            }

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
    }

    private static PipeReader CreatePipe(byte[] bytes, int chunkSize)
    {
        //A small buffer size makes the stream pipe hand out the source a few bytes at a time, so the
        //reader must ask for more (NeedMore) across read boundaries that fall inside tokens.
        return PipeReader.Create(new MemoryStream(bytes), new StreamPipeReaderOptions(bufferSize: chunkSize, minimumReadSize: 1));
    }
}
