using System.Collections.Generic;
using System.Globalization;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Emission;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Lumoin.Veritas.ParserTests.Turtle;

/// <summary>
/// Verifies the statement-incremental <see cref="TurtleReader.ReadAsync(PipeReader, TurtleSyntax, Utf8StringPool?, string?, CancellationToken)"/>
/// yields exactly the quads a whole-document parse-and-emit produces, even when the pipe hands out the
/// source a few bytes per read so statements straddle read boundaries and the parser must suspend and
/// resume.
/// </summary>
[TestClass]
internal sealed class TurtleReaderStreamingTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task DirectivesAndMultiPredicateTriples()
    {
        await AssertStreamingMatchesWhole(
            "@prefix ex: <http://example.org/> .\n"
            + "@base <http://example.org/base/> .\n"
            + "PREFIX p: <http://other.example/>\n"
            + "ex:alice ex:knows ex:bob ; ex:age 42 .\n"
            + "ex:bob ex:name \"Bob\"@en , \"Bobby\" ; p:flag true .\n",
            TurtleSyntax.Turtle).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task CollectionsBlankNodeListsAndAnonymousBlankNodes()
    {
        await AssertStreamingMatchesWhole(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:s ex:p ( ex:a ex:b ex:c ) .\n"
            + "ex:s2 ex:q [ ex:r ex:o ; ex:r2 -3.0e-4 ] .\n"
            + "[ ex:lonely ex:value ] .\n"
            + "ex:s3 ex:p [] , [ ex:n ex:m ] .\n",
            TurtleSyntax.Turtle).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task ReifiedTriplesTripleTermsAndAnnotations()
    {
        await AssertStreamingMatchesWhole(
            "<http://example.org/s> <http://example.org/p> <<( <http://example.org/a> <http://example.org/b> <http://example.org/c> )>> .\n"
            + "<http://example.org/s> <http://example.org/p> << <http://example.org/a> <http://example.org/b> <http://example.org/c> ~ <http://example.org/r> >> .\n"
            + "<http://example.org/s> <http://example.org/p> <http://example.org/o> {| <http://example.org/m> <http://example.org/v> |} .\n"
            + "<http://example.org/s> <http://example.org/p> <http://example.org/o> ~ .\n",
            TurtleSyntax.Turtle).ConfigureAwait(false);
    }

    [TestMethod]
    public async Task TrigGraphBlocks()
    {
        await AssertStreamingMatchesWhole(
            "@prefix ex: <http://example.org/> .\n"
            + "ex:default ex:p ex:o .\n"
            + "<http://example.org/g> { ex:s ex:p ex:o . ex:s2 ex:p2 ex:o2 }\n"
            + "GRAPH ex:g2 { ex:s3 ex:p3 ex:o3 . }\n"
            + "{ ex:s4 ex:p4 ex:o4 . }\n",
            TurtleSyntax.TriG).ConfigureAwait(false);
    }

    /// <summary>
    /// The pipe-driven whole-document overload (which drains the pipe into a pooled buffer that is released
    /// right after the parse) yields the same source-tagged quads as the in-memory overload, including for an
    /// empty document (which exercises the zero-length rental guard).
    /// </summary>
    [TestMethod]
    public async Task ReadWithSourceFromPipeMatchesInMemory()
    {
        foreach(string source in (string[])["@prefix ex: <http://example.org/> .\nex:s ex:p ex:o , ex:o2 ; ex:q \"v\"@en .\n", ""])
        {
            ReadOnlyMemory<byte> bytes = Utf8Strings.From(source).Memory;

            List<Quad> expected = await CollectAsync(
                TurtleReader.ReadWithSourceAsync(bytes, TurtleSyntax.Turtle, new DocumentId(1), cancellationToken: TestContext.CancellationToken).Quads).ConfigureAwait(false);

            List<Quad> actual = await CollectAsync(
                (await TurtleReader.ReadWithSourceAsync(Fragmented(bytes, chunkSize: 3), TurtleSyntax.Turtle, new DocumentId(1), cancellationToken: TestContext.CancellationToken).ConfigureAwait(false)).Quads).ConfigureAwait(false);

            Assert.HasCount(expected.Count, actual);
            for(int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i], actual[i]);
            }
        }
    }

    private async Task AssertStreamingMatchesWhole(string source, TurtleSyntax syntax)
    {
        ReadOnlyMemory<byte> bytes = Utf8Strings.From(source).Memory;
        List<Quad> expected = WholeDocumentQuads(bytes, syntax);

        //Chunk size 1 forces every statement to straddle read boundaries and suspend at the finest
        //granularity; the larger sizes cover boundaries that fall mid-token and mid-statement.
        foreach(int chunkSize in (int[])[1, 3, 7, 16, 64])
        {
            List<Quad> actual = [];
            DiagnosticBag diagnostics = new();
            await foreach(Quad quad in TurtleReader.ReadAsync(
                Fragmented(bytes, chunkSize),
                syntax,
                diagnostics,
                pool: null,
                cancellationToken: TestContext.CancellationToken).ConfigureAwait(false))
            {
                actual.Add(quad);
            }

            string where = string.Create(CultureInfo.InvariantCulture, $"chunk size {chunkSize}");
            Assert.HasCount(expected.Count, actual, where);
            for(int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i], actual[i], string.Create(CultureInfo.InvariantCulture, $"{where}, quad {i}"));
            }
        }
    }

    private static List<Quad> WholeDocumentQuads(ReadOnlyMemory<byte> bytes, TurtleSyntax syntax)
    {
        //The whole-document parse-and-emit is the ground truth the streaming reader must reproduce.
        using Utf8StringPool pool = new();
        TurtleLexer lexer = new(bytes, pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, new DocumentId(1), syntax);
        TurtleDocument document = parser.Parse();
        TurtleQuadEmitter emitter = new(document, pool, new DiagnosticBag());

        List<Quad> quads = [];
        foreach(EmittedQuad emitted in emitter.Emit())
        {
            quads.Add(emitted.Quad);
        }

        return quads;
    }

    private static PipeReader Fragmented(ReadOnlyMemory<byte> bytes, int chunkSize)
    {
        return PipeReader.Create(new ReadOnlyMemoryStream(bytes), new StreamPipeReaderOptions(bufferSize: chunkSize, minimumReadSize: 1));
    }

    /// <summary>Collects the quads from a source-tagged quad stream in emission order.</summary>
    /// <param name="quads">The source-tagged quad stream.</param>
    /// <returns>The quads in emission order.</returns>
    private static async Task<List<Quad>> CollectAsync(IAsyncEnumerable<EmittedQuad> quads)
    {
        List<Quad> result = [];
        await foreach(EmittedQuad emitted in quads.ConfigureAwait(false))
        {
            result.Add(emitted.Quad);
        }

        return result;
    }
}
