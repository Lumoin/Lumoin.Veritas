using System;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;

namespace Lumoin.Veritas.Sparql.Parser;

/// <summary>
/// A byte-fed incremental SPARQL reader presenting the editor-consumption contract: an editor feeds source bytes in
/// any-sized chunks through <see cref="Feed"/> and calls <see cref="Complete"/> to finalize and obtain the parsed
/// <see cref="SparqlRequest"/>. It is the SPARQL peer of the OWL incremental readers, over the same resumable lexer +
/// parser the whole-buffer <see cref="SparqlParser.ParseRequest(System.ReadOnlyMemory{byte}, Utf8StringPool, Utf8String?)"/>
/// facade drives in one shot.
/// </summary>
/// <remarks>
/// A chunk boundary never splits a token: a partial codepoint escape is re-presented with the next chunk, and a
/// half-lexed token re-lexes when more bytes arrive; the parser suspends its work stack until enough tokens are fed.
/// <para>
/// Unlike a tag-terminated XML document, a SPARQL request has no closing delimiter, so its completeness cannot be known
/// mid-stream — a trailing clause (a solution modifier, a <c>VALUES</c> block) may always follow. Therefore
/// <see cref="Feed"/> reports <see cref="IncrementalParseStatus.NeedMore"/> throughout (an editor must not flag the
/// in-progress tail as an error), and <see cref="Status"/> resolves to <see cref="IncrementalParseStatus.Complete"/>
/// only once <see cref="Complete"/> declares the input final and the request parses. The editor feeds incrementally for
/// live diagnostics — recovered as errors are encountered — and calls <see cref="Complete"/> to finalize.
/// </para>
/// <para>
/// Malformed input is recovered into error nodes and recorded in <see cref="Diagnostics"/>, never thrown. Peak memory
/// tracks the whole request (the decoder accumulates the decoded stream and the parser buffers its tokens), which is
/// appropriate for a query but is not constant-memory streaming.
/// </para>
/// </remarks>
public sealed class SparqlIncrementalReader
{
    /// <summary>The lexer driven synchronously by <see cref="Feed"/>, holding the growing decoded stream and partial-token state.</summary>
    private SparqlLexer Lexer { get; }

    /// <summary>The resumable parser the lexed tokens are fed into.</summary>
    private SparqlParser Parser { get; }

    /// <summary>The shared bag bridged lexical diagnostics and parser-recovery diagnostics accumulate into, in source order.</summary>
    private DiagnosticBag Bag { get; }

    /// <summary>The unconsumed source tail (a partial codepoint escape, at most a few bytes) re-presented, prepended to the next fed chunk.</summary>
    private byte[] Pending { get; set; } = [];

    /// <summary>The number of lexer diagnostics already bridged into <see cref="Bag"/>, so a feed bridges only the new ones.</summary>
    private int LexerDiagnosticsCopied { get; set; }

    /// <summary>Whether <see cref="Complete"/> has been called; further <see cref="Feed"/> is then rejected.</summary>
    private bool Final { get; set; }

    /// <summary>The parsed request once the parser has produced it, or <see langword="null"/> while the input is still mid-request.</summary>
    private SparqlRequest? Produced { get; set; }

    /// <summary>Initialises a reader whose <see cref="Complete"/> returns the parsed request.</summary>
    /// <param name="pool">The pool used to intern token and identifier payloads; a private pool is created when <see langword="null"/> (the result's interned values keep it alive).</param>
    /// <param name="baseIri">The external base IRI relative references resolve against before any in-query <c>BASE</c>, or <see langword="null"/>.</param>
    /// <param name="blankNodes">Allocates labels for anonymous <c>[]</c> blank nodes; defaults to the system allocator.</param>
    public SparqlIncrementalReader(Utf8StringPool? pool = null, Utf8String? baseIri = null, BlankNodeDelegate? blankNodes = null)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        Bag = new DiagnosticBag();
        Lexer = new SparqlLexer(effectivePool);
        Parser = new SparqlParser(effectivePool, baseIri, blankNodes, Bag);
    }

    /// <summary>Gets the diagnostics recorded so far — bridged lexical errors and parser-recovery errors, in source order.</summary>
    public DiagnosticBag Diagnostics => Bag;

    /// <summary>Gets whether the request has parsed (<see cref="IncrementalParseStatus.Complete"/>, only reachable after <see cref="Complete"/>) or the input is still mid-request (<see cref="IncrementalParseStatus.NeedMore"/>).</summary>
    public IncrementalParseStatus Status => Produced is not null ? IncrementalParseStatus.Complete : IncrementalParseStatus.NeedMore;

    /// <summary>Feeds the next chunk of UTF-8 source bytes and returns the post-chunk status (always <see cref="IncrementalParseStatus.NeedMore"/> — a SPARQL request has no terminator).</summary>
    /// <param name="chunk">The source bytes; may end mid-token or mid-escape at any byte boundary.</param>
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

    /// <summary>Declares the input final, drains the tail, and returns the parsed request with its diagnostics.</summary>
    /// <returns>The parsed request (carrying error nodes when the input was malformed) together with the accumulated diagnostics.</returns>
    public ParseResult<SparqlRequest> Complete()
    {
        if(!Final)
        {
            Final = true;
            Pump(ReadOnlySpan<byte>.Empty, isFinal: true);
        }

        //The final chunk feeds the end-of-input token, which completes the token stream, so the resumable parser
        //produces the request (with error nodes for any unfinished construct) rather than suspending.
        return new ParseResult<SparqlRequest>(Produced!, Bag.Diagnostics, Bag.HasErrors);
    }

    /// <summary>Decodes and lexes a chunk, feeding completed tokens to the parser, then drives the parse one step further.</summary>
    /// <param name="chunk">The new source bytes.</param>
    /// <param name="isFinal">Whether this is the final chunk.</param>
    private void Pump(ReadOnlySpan<byte> chunk, bool isFinal)
    {
        //In the common case nothing is pending, so the chunk feeds directly; only a partial escape carried over from a
        //previous feed needs the small concatenation.
        ReadOnlySpan<byte> source = Pending.Length == 0 ? chunk : Combine(Pending, chunk);
        int consumed = Lexer.FeedDecodedSource(source, isFinal);
        Pending = consumed < source.Length ? source[consumed..].ToArray() : [];

        while(true)
        {
            SparqlLexStatus status = Lexer.TryLexNext(out SparqlToken token);
            if(status == SparqlLexStatus.NeedMore)
            {
                break;
            }

            Parser.FeedToken(token);
            if(token.Kind == SparqlTokenKind.EndOfInput)
            {
                break;
            }
        }

        BridgeNewLexerDiagnostics();

        if(Parser.TryParseRequest(out SparqlRequest? request) == ParseStatus.Produced)
        {
            Produced = request;
        }
    }

    /// <summary>Bridges any newly recorded lexer diagnostics into the shared bag, in source order, without re-adding earlier ones.</summary>
    private void BridgeNewLexerDiagnostics()
    {
        IReadOnlyList<SparqlLexDiagnostic> lexerDiagnostics = Lexer.Diagnostics;
        while(LexerDiagnosticsCopied < lexerDiagnostics.Count)
        {
            Bag.Add(SparqlLexDiagnosticBridge.ToDiagnostic(lexerDiagnostics[LexerDiagnosticsCopied]));
            LexerDiagnosticsCopied++;
        }
    }

    /// <summary>Concatenates the re-presented partial-escape tail with a new chunk into one source buffer.</summary>
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
