using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// A byte-fed incremental Turtle/TriG reader presenting the editor-consumption contract: an editor feeds source bytes
/// in any-sized chunks through <see cref="Feed"/> and calls <see cref="Complete"/> to finalize and obtain the parsed
/// <see cref="TurtleDocument"/> AST. It is the Turtle peer of the SPARQL, Jsonata, and OWL incremental readers, over the
/// same resumable lexer the pipe-driven <see cref="TurtleReader"/> drives and the same parser the whole-buffer
/// <see cref="TurtleReader.ReadWithSourceAsync(System.ReadOnlyMemory{byte}, TurtleSyntax, DocumentId, Utf8StringPool?, string?, System.Threading.CancellationToken)"/>
/// produces a document from — the byte-cut-fed result is the identical document.
/// </summary>
/// <remarks>
/// <para>
/// A chunk boundary never splits a token: a token straddling a feed is re-presented with the next chunk and re-lexes
/// once more bytes arrive. The lexer runs as bytes arrive, so lexical diagnostics accumulate live in
/// <see cref="Diagnostics"/>; the parser builds the document (recovering any syntax error into an error node) when
/// <see cref="Complete"/> declares the input final.
/// </para>
/// <para>
/// A Turtle document has no closing delimiter — a further statement may always follow — so <see cref="Feed"/> reports
/// <see cref="IncrementalParseStatus.NeedMore"/> throughout and <see cref="Status"/> resolves to
/// <see cref="IncrementalParseStatus.Complete"/> only once <see cref="Complete"/> has parsed the document. Malformed
/// input is recovered, never thrown. Peak memory tracks the whole document: the editor AST is not constant-memory
/// streaming — the bare <see cref="TurtleReader.ReadAsync(System.IO.Pipelines.PipeReader, TurtleSyntax, DiagnosticBag, Utf8StringPool?, string?, System.Threading.CancellationToken)"/>
/// quad stream is the bounded-memory path.
/// </para>
/// </remarks>
public sealed class TurtleIncrementalReader
{
    /// <summary>The resumable lexer driven synchronously by <see cref="Feed"/>, holding the partial-token state across chunks.</summary>
    private TurtleLexer Lexer { get; }

    /// <summary>The AST-retaining incremental parser the lexed tokens are fed into; <see cref="Complete"/> parses it into the document.</summary>
    private TurtleParser Parser { get; }

    /// <summary>The shared bag bridged lexical diagnostics and parser-recovery diagnostics accumulate into, in source order (lexical first, then the parse).</summary>
    private DiagnosticBag Bag { get; }

    /// <summary>The unconsumed source tail (at most one straddling token) re-presented, prepended to the next fed chunk.</summary>
    private byte[] Pending { get; set; } = [];

    /// <summary>The number of lexer diagnostics already bridged into <see cref="Bag"/>, so a feed bridges only the new ones.</summary>
    private int LexerDiagnosticsCopied { get; set; }

    /// <summary>Whether <see cref="Complete"/> has been called; further <see cref="Feed"/> is then rejected.</summary>
    private bool Final { get; set; }

    /// <summary>The parsed result once <see cref="Complete"/> has produced it, or <see langword="null"/> while the input is still mid-document.</summary>
    private ParseResult<TurtleDocument>? Result { get; set; }

    /// <summary>Initialises a reader whose <see cref="Complete"/> returns the parsed document AST.</summary>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="documentId">The content-addressed identifier for the source document, carried on the produced <see cref="TurtleDocument"/>.</param>
    /// <param name="pool">The pool used to intern token and identifier payloads; a private pool is created when <see langword="null"/> (the result's interned values keep it alive).</param>
    /// <param name="blankNodes">Allocates labels for anonymous <c>[]</c> blank nodes; defaults to the system allocator.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="TurtleReaderLimits.Default"/>.</param>
    public TurtleIncrementalReader(TurtleSyntax syntax, DocumentId documentId = default, Utf8StringPool? pool = null, BlankNodeDelegate? blankNodes = null, TurtleReaderLimits? limits = null)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        Bag = new DiagnosticBag();
        Lexer = new TurtleLexer(effectivePool, limits);
        Parser = new TurtleParser(effectivePool, documentId, syntax, blankNodes, Bag, retainAst: true);
    }

    /// <summary>Gets the diagnostics recorded so far — bridged lexical errors (live) and, after <see cref="Complete"/>, parser-recovery errors.</summary>
    public DiagnosticBag Diagnostics => Bag;

    /// <summary>Gets whether the document has parsed (<see cref="IncrementalParseStatus.Complete"/>, only reachable after <see cref="Complete"/>) or the input is still mid-document (<see cref="IncrementalParseStatus.NeedMore"/>).</summary>
    public IncrementalParseStatus Status => Result is not null ? IncrementalParseStatus.Complete : IncrementalParseStatus.NeedMore;

    /// <summary>Feeds the next chunk of UTF-8 source bytes and returns the post-chunk status (always <see cref="IncrementalParseStatus.NeedMore"/> — a Turtle document has no terminator).</summary>
    /// <param name="chunk">The source bytes; may end mid-token at any byte boundary.</param>
    /// <returns>The status after consuming the chunk.</returns>
    /// <exception cref="InvalidOperationException"><see cref="Complete"/> has already been called.</exception>
    public IncrementalParseStatus Feed(ReadOnlySpan<byte> chunk)
    {
        if(Final)
        {
            throw new InvalidOperationException("The reader has been completed and cannot accept more input.");
        }

        Pump(chunk, isFinal: false);

        return Status;
    }

    /// <summary>Declares the input final, lexes the tail (emitting end-of-input), and parses the buffered tokens into the document AST with its diagnostics.</summary>
    /// <returns>The parsed document (carrying error nodes when the input was malformed) together with the accumulated diagnostics.</returns>
    public ParseResult<TurtleDocument> Complete()
    {
        if(!Final)
        {
            Final = true;
            Pump(ReadOnlySpan<byte>.Empty, isFinal: true);

            //Every token (including end-of-input) is now buffered, so the AST-retaining parser parses the whole
            //token stream into the document — identical to the whole-buffer parse fed in one shot.
            Result = Parser.ParseToResult();
        }

        return Result!;
    }

    /// <summary>Lexes a chunk (the re-presented tail plus the new bytes) and feeds the completed tokens to the parser, retaining the unconsumed tail.</summary>
    /// <param name="chunk">The new source bytes.</param>
    /// <param name="isFinal">Whether this is the final chunk.</param>
    private void Pump(ReadOnlySpan<byte> chunk, bool isFinal)
    {
        //A SequenceReader needs owned memory, so the transient fed span is copied into a buffer that also carries the
        //straddling tail from the previous feed; only a partial token is ever pending, so the copy is bounded.
        byte[] combined = Pending.Length == 0 ? chunk.ToArray() : Combine(Pending, chunk);
        ReadOnlySequence<byte> source = new(combined);

        IReadOnlyList<TurtleToken> tokens = Lexer.FeedChunk(source, isFinal, out long consumed);
        foreach(TurtleToken token in tokens)
        {
            Parser.FeedToken(token);
        }

        Pending = consumed < combined.Length ? combined[(int)consumed..] : [];
        BridgeNewLexerDiagnostics();
    }

    /// <summary>Bridges any newly recorded lexer diagnostics into the shared bag, in source order, without re-adding earlier ones.</summary>
    private void BridgeNewLexerDiagnostics()
    {
        IReadOnlyList<LexDiagnostic> lexerDiagnostics = Lexer.Diagnostics;
        while(LexerDiagnosticsCopied < lexerDiagnostics.Count)
        {
            Bag.Add(TurtleLexDiagnosticBridge.ToDiagnostic(lexerDiagnostics[LexerDiagnosticsCopied]));
            LexerDiagnosticsCopied++;
        }
    }

    /// <summary>Concatenates the re-presented straddling tail with a new chunk into one source buffer.</summary>
    /// <param name="head">The unconsumed source tail from the previous feed.</param>
    /// <param name="chunk">The new source bytes.</param>
    /// <returns>The combined source bytes.</returns>
    private static byte[] Combine(byte[] head, ReadOnlySpan<byte> chunk)
    {
        byte[] combined = new byte[head.Length + chunk.Length];
        head.CopyTo(combined, 0);
        chunk.CopyTo(combined.AsSpan(head.Length));

        return combined;
    }
}
