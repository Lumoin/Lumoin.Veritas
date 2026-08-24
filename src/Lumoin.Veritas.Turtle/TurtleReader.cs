using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Emission;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.Turtle;

/// <summary>
/// Reads RDF 1.2 Turtle and TriG into <see cref="Quad"/> and
/// <see cref="EmittedQuad"/> streams.
/// </summary>
/// <remarks>
/// <para>
/// Two surface shapes are provided. The plain <c>ReadAsync</c>
/// methods yield bare <see cref="Quad"/> instances for consumers
/// that only care about graph content. The <c>ReadWithSourceAsync</c>
/// methods yield <see cref="EmittedQuad"/> instances whose
/// <see cref="EmittedQuad.Source"/> is populated with a
/// <see cref="DocumentNodeRef"/> pointing back at the AST node the
/// quad originated from; the document AST is returned alongside the
/// async iterator so editor consumers can resolve those references.
/// </para>
/// <para>
/// The bare <c>ReadAsync</c> methods are statement-incremental: tokens
/// are lexed, parsed, and emitted one statement at a time, so neither
/// the token buffer nor the AST grows with the document — peak memory
/// tracks the largest single statement. The <c>ReadWithSourceAsync</c>
/// methods are whole-document: the source is parsed into a complete AST
/// before the first quad is yielded, because the returned document must
/// resolve every <see cref="DocumentNodeRef"/> back to its node.
/// Cancellation observed via <paramref name="cancellationToken"/>
/// terminates emission at the next yield point.
/// </para>
/// </remarks>
public static class TurtleReader
{
    /// <summary>
    /// Reads a UTF-8 pipe and yields quads from the parsed document.
    /// </summary>
    /// <param name="input">The UTF-8 encoded source pipe.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="diagnostics">The caller-owned bag lexical and parse/emit diagnostics accumulate into; inspect <see cref="DiagnosticBag.HasErrors"/> after enumerating. Reading never throws on malformed input.</param>
    /// <param name="pool">Optional pool to intern interned strings into. A private pool is created when null.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    /// <remarks>
    /// The pipe is consumed statement by statement: each <see cref="PipeReader.ReadAsync"/> feeds the
    /// lexer, completed tokens feed the parser, and a completed statement is emitted and released before
    /// more bytes are pulled. Peak memory tracks the largest single statement, not the document.
    /// Malformed input is recovered, not thrown: the reader records a diagnostic into
    /// <paramref name="diagnostics"/>, skips the offending quad, and continues. Only I/O failures,
    /// cancellation, and resource-limit breaches throw.
    /// </remarks>
    public static IAsyncEnumerable<Quad> ReadAsync(
        PipeReader input,
        TurtleSyntax syntax,
        DiagnosticBag diagnostics,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(diagnostics);

        return IterateBareAsync(input, syntax, diagnostics, pool, baseIri, cancellationToken);
    }

    /// <summary>
    /// Reads a UTF-8 byte buffer and yields quads from the parsed document.
    /// </summary>
    /// <param name="source">The UTF-8 source bytes.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="diagnostics">The caller-owned bag lexical and parse/emit diagnostics accumulate into; inspect <see cref="DiagnosticBag.HasErrors"/> after enumerating. Reading never throws on malformed input.</param>
    /// <param name="pool">Optional pool to intern strings into.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    public static IAsyncEnumerable<Quad> ReadAsync(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DiagnosticBag diagnostics,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return IterateBareFromMemoryAsync(source, syntax, diagnostics, pool, baseIri, cancellationToken);
    }

    /// <summary>
    /// Reads a UTF-8 byte buffer and yields quads from the parsed document, synchronously.
    /// An in-memory parse involves no I/O — the lexer, parser, and emitter are all
    /// synchronous — so a genuinely synchronous consumer (a test-discovery data source,
    /// for example) enumerates without any task machinery. The
    /// <see cref="ReadAsync(ReadOnlyMemory{byte}, TurtleSyntax, DiagnosticBag, Utf8StringPool?, string?, CancellationToken)"/>
    /// overload is the composition-friendly async facade over the same core; the pipe
    /// overloads stay asynchronous because a pipe is a real I/O boundary.
    /// </summary>
    /// <param name="source">The UTF-8 source bytes.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="diagnostics">The caller-owned bag lexical and parse/emit diagnostics accumulate into; inspect <see cref="DiagnosticBag.HasErrors"/> after enumerating. Reading never throws on malformed input.</param>
    /// <param name="pool">Optional pool to intern strings into.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>A sequence of parsed quads.</returns>
    public static IEnumerable<Quad> Read(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DiagnosticBag diagnostics,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return IterateBareFromMemory(source, syntax, diagnostics, pool, baseIri, cancellationToken);
    }

    /// <summary>
    /// Reads a UTF-8 pipe and yields <see cref="EmittedQuad"/> values
    /// alongside the parsed <see cref="TurtleDocument"/> AST.
    /// </summary>
    /// <param name="input">The UTF-8 encoded source pipe.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="documentId">The content-addressed document identifier.</param>
    /// <param name="pool">Optional pool to intern strings into.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>A tuple of the parse result (document AST plus diagnostics) and an async iterator over source-tagged quads.</returns>
    /// <remarks>
    /// The pipe is drained to a contiguous buffer and parsed before the AST is returned, so this
    /// overload is asynchronous; the in-memory <see cref="ReadOnlyMemory{T}"/> overload returns
    /// the tuple synchronously. The returned <see cref="ParseResult{TTree}"/> carries the lexical and
    /// parse diagnostics; resolution diagnostics raised while the quad iterator runs append to the same
    /// bag, observable through <see cref="ParseResult{TTree}.Diagnostics"/>.
    /// </remarks>
    public static async Task<(ParseResult<TurtleDocument> Result, IAsyncEnumerable<EmittedQuad> Quads)> ReadWithSourceAsync(
        PipeReader input,
        TurtleSyntax syntax,
        DocumentId documentId,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        (IMemoryOwner<byte> Buffer, int Length) drained;
        try
        {
            drained = await DrainAsync(input, effectivePool, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await input.CompleteAsync().ConfigureAwait(false);
        }

        DiagnosticBag diagnostics = new();
        TurtleDocument document;

        //The lexer interns every payload, so the drained source is needed only for the parse and is
        //released back to the pool immediately afterwards.
        using(drained.Buffer)
        {
            document = ParseDocumentInto(drained.Buffer.Memory[..drained.Length], syntax, documentId, effectivePool, diagnostics);
        }

        ParseResult<TurtleDocument> result = new(document, diagnostics.Diagnostics, diagnostics.HasErrors);
        IAsyncEnumerable<EmittedQuad> quads = IterateWithSourceAsync(document, effectivePool, diagnostics, baseIri, cancellationToken);

        return (result, quads);
    }

    /// <summary>
    /// Reads a UTF-8 byte buffer and yields <see cref="EmittedQuad"/>
    /// values alongside the parsed <see cref="TurtleDocument"/> AST.
    /// </summary>
    /// <param name="source">The UTF-8 source bytes.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="documentId">The content-addressed document identifier.</param>
    /// <param name="pool">Optional pool to intern strings into.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>A tuple of the parse result (document AST plus diagnostics) and an async iterator over source-tagged quads.</returns>
    public static (ParseResult<TurtleDocument> Result, IAsyncEnumerable<EmittedQuad> Quads) ReadWithSourceAsync(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DocumentId documentId,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        DiagnosticBag diagnostics = new();
        TurtleDocument document = ParseDocumentInto(source, syntax, documentId, effectivePool, diagnostics);
        ParseResult<TurtleDocument> result = new(document, diagnostics.Diagnostics, diagnostics.HasErrors);
        IAsyncEnumerable<EmittedQuad> quads = IterateWithSourceAsync(document, effectivePool, diagnostics, baseIri, cancellationToken);
        return (result, quads);
    }

    private static async IAsyncEnumerable<Quad> IterateBareAsync(
        PipeReader input,
        TurtleSyntax syntax,
        DiagnosticBag diagnostics,
        Utf8StringPool? pool,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Utf8StringPool ownedPool = pool ?? new Utf8StringPool();
        try
        {
            TurtleLexer lexer = new(ownedPool);
            TurtleParser parser = new(ownedPool, default, syntax, blankNodes: null, diagnostics: diagnostics);
            TurtleQuadEmitter emitter = new(EmptyStreamingDocument(), ownedPool, diagnostics, baseIri);
            int bridged = 0;

            await foreach(TurtleToken token in lexer.TokenizeAsync(input, cancellationToken).ConfigureAwait(false))
            {
                parser.FeedToken(token);
                bridged = BridgeLexerDiagnostics(lexer, diagnostics, bridged);

                foreach(Quad quad in DrainStatements(parser, emitter))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return quad;
                }
            }

            BridgeLexerDiagnostics(lexer, diagnostics, bridged);
        }
        finally
        {
            if(pool is null)
            {
                ownedPool.Dispose();
            }

            await input.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<Quad> IterateBareFromMemoryAsync(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DiagnosticBag diagnostics,
        Utf8StringPool? pool,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach(Quad quad in IterateBareFromMemory(source, syntax, diagnostics, pool, baseIri, cancellationToken))
        {
            yield return quad;
        }

        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
    }

    //The shared synchronous core of the in-memory overloads: lexing, parsing, and emission are all
    //synchronous over a byte buffer, so both the bare sync Read and the async facade enumerate this
    //iterator; only the pipe overloads carry genuine asynchrony.
    private static IEnumerable<Quad> IterateBareFromMemory(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DiagnosticBag diagnostics,
        Utf8StringPool? pool,
        string? baseIri,
        CancellationToken cancellationToken)
    {
        Utf8StringPool ownedPool = pool ?? new Utf8StringPool();
        try
        {
            TurtleLexer lexer = new(source, ownedPool);
            TurtleParser parser = new(ownedPool, default, syntax, blankNodes: null, diagnostics: diagnostics);
            TurtleQuadEmitter emitter = new(EmptyStreamingDocument(), ownedPool, diagnostics, baseIri);
            int bridged = 0;

            foreach(TurtleToken token in lexer.Tokenize())
            {
                parser.FeedToken(token);
                bridged = BridgeLexerDiagnostics(lexer, diagnostics, bridged);

                foreach(Quad quad in DrainStatements(parser, emitter))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    yield return quad;
                }
            }

            BridgeLexerDiagnostics(lexer, diagnostics, bridged);
        }
        finally
        {
            if(pool is null)
            {
                ownedPool.Dispose();
            }
        }
    }

    //Bridges any lexer-internal diagnostics recorded since the last call into the shared parse-level bag,
    //and returns the new high-water mark. Lexical errors surface as Error tokens the parser resyncs past,
    //so their LX#### diagnostics belong in the same bag the consumer reads.
    private static int BridgeLexerDiagnostics(TurtleLexer lexer, DiagnosticBag diagnostics, int alreadyBridged)
    {
        IReadOnlyList<LexDiagnostic> lexDiagnostics = lexer.Diagnostics;
        for(int i = alreadyBridged; i < lexDiagnostics.Count; i++)
        {
            diagnostics.Add(TurtleLexDiagnosticBridge.ToDiagnostic(lexDiagnostics[i]));
        }

        return lexDiagnostics.Count;
    }

    //Drives the suspendable parser to completion over the tokens fed so far, emitting each finished
    //statement's quads. Returns when the parser needs more tokens; the caller feeds the next token and
    //calls again. The statement and its nodes are released as soon as its quads are produced.
    private static IEnumerable<Quad> DrainStatements(TurtleParser parser, TurtleQuadEmitter emitter)
    {
        while(parser.TryParseStatement(out Statement? statement) == ParseStatus.Produced)
        {
            foreach(EmittedQuad emitted in emitter.EmitStatement(statement!))
            {
                yield return emitted.Quad;
            }
        }
    }

    private static TurtleDocument EmptyStreamingDocument()
    {
        //The bare quad stream carries no provenance, so the emitter's per-quad DocumentNodeRef is
        //discarded and document identity is immaterial — ReadWithSourceAsync is the path that returns a
        //navigable AST and a content-addressed DocumentId. An empty document satisfies the emitter
        //(which reads only its DocumentId) without retaining anything about the source.
        return new TurtleDocument(
            default,
            ImmutableArray<PrefixDeclaration>.Empty,
            ImmutableArray<BaseDeclaration>.Empty,
            ImmutableArray<VersionDeclaration>.Empty,
            ImmutableArray<Statement>.Empty,
            ImmutableDictionary<int, TurtleAstNode>.Empty);
    }

    private static async IAsyncEnumerable<EmittedQuad> IterateWithSourceAsync(
        TurtleDocument document,
        Utf8StringPool pool,
        DiagnosticBag diagnostics,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TurtleQuadEmitter emitter = new(document, pool, diagnostics, baseIri);
        foreach(EmittedQuad emitted in emitter.Emit())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return emitted;
        }

        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
    }

    //Lexes and parses the whole buffer into the shared bag (lexical diagnostics bridged first, then the
    //parser's), returning the document. The caller wraps the bag in a ParseResult and reuses it for the
    //emitter, so one bag spans lexing, parsing, and emission.
    private static TurtleDocument ParseDocumentInto(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DocumentId documentId,
        Utf8StringPool pool,
        DiagnosticBag diagnostics)
    {
        TurtleLexer lexer = new(source, pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, documentId, syntax, blankNodes: null, diagnostics: diagnostics);
        BridgeLexerDiagnostics(lexer, diagnostics, 0);

        return parser.Parse();
    }

    private static async Task<(IMemoryOwner<byte> Buffer, int Length)> DrainAsync(PipeReader input, Utf8StringPool pool, CancellationToken cancellationToken)
    {
        //Turtle parses the whole document, so the pipe is examined (not consumed) until the producer
        //completes, then the bytes are copied into one scratch buffer rented from the caller's pool that the
        //parse owns and releases — the lexer interns every payload, so nothing downstream references it.
        while(true)
        {
            ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            if(result.IsCompleted)
            {
                int length = checked((int)buffer.Length);

                //The pool rejects a zero-length rental and an empty document is valid, so rent at least one byte.
                IMemoryOwner<byte> owner = pool.RentScratch(Math.Max(1, length));
                buffer.CopyTo(owner.Memory.Span);
                input.AdvanceTo(buffer.End);

                return (owner, length);
            }

            input.AdvanceTo(buffer.Start, buffer.End);
        }
    }
}
