using System;
using System.Buffers;
using System.Collections.Generic;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Parsing;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Lexer;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// A byte-fed incremental JSONata reader presenting the editor-consumption contract: an editor feeds source bytes in
/// any-sized chunks through <see cref="Feed"/> and calls <see cref="Complete"/> to finalize and obtain the parsed
/// <see cref="JsonataExpression"/>. It is the JSONata peer of the OWL and SPARQL incremental readers, over the same
/// resumable lexer + parser the whole-buffer <see cref="Jsonata.Parse(System.ReadOnlyMemory{byte}, Utf8StringPool)"/>
/// facade drives in one shot.
/// </summary>
/// <remarks>
/// A chunk boundary never splits a token: a partial token or character is re-presented with the next chunk; the parser
/// suspends its work stack until enough tokens are fed.
/// <para>
/// A JSONata expression has no closing delimiter — a trailing operator may always continue it — so its completeness
/// cannot be known mid-stream: <see cref="Feed"/> reports <see cref="IncrementalParseStatus.NeedMore"/> throughout (an
/// editor must not flag the in-progress tail as an error), and <see cref="Status"/> resolves to
/// <see cref="IncrementalParseStatus.Complete"/> only once <see cref="Complete"/> declares the input final and the
/// expression parses. The editor feeds incrementally for live diagnostics and calls <see cref="Complete"/> to finalize.
/// </para>
/// <para>
/// Malformed input is recovered into error nodes and recorded in <see cref="Diagnostics"/>, never thrown.
/// </para>
/// </remarks>
public sealed class JsonataIncrementalReader
{
    /// <summary>The lexer driven synchronously by <see cref="Feed"/>, lexing the re-presented buffer to a clean boundary.</summary>
    private JsonataLexer Lexer { get; }

    /// <summary>The resumable parser the lexed tokens are fed into.</summary>
    private JsonataParser Parser { get; }

    /// <summary>The shared bag bridged lexical diagnostics and parser-recovery diagnostics accumulate into, in source order.</summary>
    private DiagnosticBag Bag { get; }

    /// <summary>The unconsumed source tail (a partial token or character) re-presented, prepended to the next fed chunk.</summary>
    private byte[] Pending { get; set; } = [];

    /// <summary>The number of lexer diagnostics already bridged into <see cref="Bag"/>, so a feed bridges only the new ones.</summary>
    private int LexerDiagnosticsCopied { get; set; }

    /// <summary>Whether <see cref="Complete"/> has been called; further <see cref="Feed"/> is then rejected.</summary>
    private bool Final { get; set; }

    /// <summary>The parsed expression once the parser has produced it, or <see langword="null"/> while the input is still mid-expression.</summary>
    private JsonataExpression? Produced { get; set; }

    /// <summary>The finalized result once <see cref="Complete"/> has run its (non-idempotent, diagnostic-recording) path-processing pass, cached so a re-call neither re-processes the tree nor re-records its diagnostics.</summary>
    private ParseResult<JsonataExpression>? Result { get; set; }

    /// <summary>Initialises a reader whose <see cref="Complete"/> returns the parsed expression.</summary>
    /// <param name="pool">The pool used to intern token and identifier payloads; a private pool is created when <see langword="null"/> (the result's interned values keep it alive).</param>
    public JsonataIncrementalReader(Utf8StringPool? pool = null)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        Bag = new DiagnosticBag();
        Lexer = new JsonataLexer(effectivePool);
        Parser = new JsonataParser(effectivePool, Bag);
    }

    /// <summary>Gets the diagnostics recorded so far — bridged lexical errors and parser-recovery errors, in source order.</summary>
    public DiagnosticBag Diagnostics => Bag;

    /// <summary>Gets whether the expression has parsed (<see cref="IncrementalParseStatus.Complete"/>, only reachable after <see cref="Complete"/>) or the input is still mid-expression (<see cref="IncrementalParseStatus.NeedMore"/>).</summary>
    public IncrementalParseStatus Status => Produced is not null ? IncrementalParseStatus.Complete : IncrementalParseStatus.NeedMore;

    /// <summary>Feeds the next chunk of UTF-8 source bytes and returns the post-chunk status (always <see cref="IncrementalParseStatus.NeedMore"/> — a JSONata expression has no terminator).</summary>
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

    /// <summary>Declares the input final, drains the tail, and returns the parsed expression with its diagnostics.</summary>
    /// <returns>The parsed expression (path-processed, carrying error nodes when the input was malformed) together with the accumulated diagnostics.</returns>
    public ParseResult<JsonataExpression> Complete()
    {
        if(Result is not null)
        {
            return Result;
        }

        Final = true;
        Pump(ReadOnlySpan<byte>.Empty, isFinal: true);

        //Mirror the whole-buffer facade: the post-parse path-processing pass flattens tuple paths and resolves parent
        //ancestry over the recovered tree, recording its diagnostics into the same bag, before the tree is returned. The
        //pass is not idempotent and records diagnostics, so the result is computed once here and cached for any re-call.
        JsonataExpression processed = JsonataPathProcessor.Process(Produced!, Bag);
        Result = new ParseResult<JsonataExpression>(processed, Bag.Diagnostics, Bag.HasErrors);

        return Result;
    }

    /// <summary>Lexes a chunk (re-presenting the unconsumed tail), feeds completed tokens to the parser, then drives the parse one step further.</summary>
    /// <param name="chunk">The new source bytes.</param>
    /// <param name="isFinal">Whether this is the final chunk.</param>
    private void Pump(ReadOnlySpan<byte> chunk, bool isFinal)
    {
        //The lexer lexes over a byte sequence, so the chunk is materialised with any carried-over tail; the lexer
        //reports how many bytes it consumed and the rest (a partial token) is re-presented on the next feed.
        byte[] source = Combine(Pending, chunk);
        IReadOnlyList<JsonataToken> tokens = Lexer.FeedBuffer(new ReadOnlySequence<byte>(source), isFinal, out long consumed);
        Pending = consumed < source.Length ? source[(int)consumed..] : [];

        foreach(JsonataToken token in tokens)
        {
            Parser.FeedToken(token);
            if(token.Kind == JsonataTokenKind.EndOfInput)
            {
                break;
            }
        }

        BridgeNewLexerDiagnostics();

        if(Parser.TryParseExpression(out JsonataExpression? expression) == ParseStatus.Produced)
        {
            Produced = expression;
        }
    }

    /// <summary>Bridges any newly recorded lexer diagnostics into the shared bag, in source order, without re-adding earlier ones.</summary>
    private void BridgeNewLexerDiagnostics()
    {
        IReadOnlyList<JsonataLexDiagnostic> lexerDiagnostics = Lexer.Diagnostics;
        while(LexerDiagnosticsCopied < lexerDiagnostics.Count)
        {
            Bag.Add(JsonataLexDiagnosticBridge.ToDiagnostic(lexerDiagnostics[LexerDiagnosticsCopied]));
            LexerDiagnosticsCopied++;
        }
    }

    /// <summary>Concatenates the re-presented unconsumed tail with a new chunk into one source buffer.</summary>
    /// <param name="head">The unconsumed source tail from the previous feed (empty on the first feed).</param>
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
