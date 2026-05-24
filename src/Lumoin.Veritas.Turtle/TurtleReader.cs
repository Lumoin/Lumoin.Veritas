using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Hypertrie.Storage;
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
/// The reader is single-shot per call: each invocation materialises
/// the token stream, builds the AST, and walks the emitter once.
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
    /// <param name="pool">Optional pool to intern interned strings into. A private pool is created when null.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    /// <remarks>
    /// Turtle is parsed whole-document: the pipe is drained to a contiguous buffer before the
    /// lexer and parser run. Memory is bounded by document size, not by the longest token.
    /// Statement-incremental streaming is a separate future effort.
    /// </remarks>
    public static IAsyncEnumerable<Quad> ReadAsync(
        PipeReader input,
        TurtleSyntax syntax,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return IterateBareAsync(input, syntax, pool, baseIri, cancellationToken);
    }

    /// <summary>
    /// Reads a UTF-8 byte buffer and yields quads from the parsed document.
    /// </summary>
    /// <param name="source">The UTF-8 source bytes.</param>
    /// <param name="syntax">Whether the source is Turtle or TriG.</param>
    /// <param name="pool">Optional pool to intern strings into.</param>
    /// <param name="baseIri">Optional document base IRI for resolving relative references that precede any in-document <c>@base</c>.</param>
    /// <param name="cancellationToken">A token to cancel iteration.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    public static IAsyncEnumerable<Quad> ReadAsync(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        return IterateBareFromMemoryAsync(source, syntax, pool, baseIri, cancellationToken);
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
    /// <returns>A tuple of the parsed document AST and an async iterator over source-tagged quads.</returns>
    /// <remarks>
    /// The pipe is drained to a contiguous buffer and parsed before the AST is returned, so this
    /// overload is asynchronous; the in-memory <see cref="ReadOnlyMemory{T}"/> overload returns
    /// the tuple synchronously.
    /// </remarks>
    public static async System.Threading.Tasks.Task<(TurtleDocument Document, IAsyncEnumerable<EmittedQuad> Quads)> ReadWithSourceAsync(
        PipeReader input,
        TurtleSyntax syntax,
        DocumentId documentId,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        byte[] bytes;
        try
        {
            bytes = await DrainAsync(input, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await input.CompleteAsync().ConfigureAwait(false);
        }

        TurtleDocument document = ParseDocument(bytes, syntax, documentId, effectivePool);
        IAsyncEnumerable<EmittedQuad> quads = IterateWithSourceAsync(document, effectivePool, baseIri, cancellationToken);

        return (document, quads);
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
    /// <returns>A tuple of the parsed document AST and an async iterator over source-tagged quads.</returns>
    public static (TurtleDocument Document, IAsyncEnumerable<EmittedQuad> Quads) ReadWithSourceAsync(
        ReadOnlyMemory<byte> source,
        TurtleSyntax syntax,
        DocumentId documentId,
        Utf8StringPool? pool = null,
        string? baseIri = null,
        CancellationToken cancellationToken = default)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        TurtleDocument document = ParseDocument(source.ToArray(), syntax, documentId, effectivePool);
        IAsyncEnumerable<EmittedQuad> quads = IterateWithSourceAsync(document, effectivePool, baseIri, cancellationToken);
        return (document, quads);
    }

    private static async IAsyncEnumerable<Quad> IterateBareAsync(
        PipeReader input,
        TurtleSyntax syntax,
        Utf8StringPool? pool,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Utf8StringPool ownedPool = pool ?? new Utf8StringPool();
        try
        {
            byte[] bytes = await DrainAsync(input, cancellationToken).ConfigureAwait(false);
            TurtleDocument document = ParseDocument(bytes, syntax, MintDocumentId(bytes), ownedPool);
            TurtleQuadEmitter emitter = new(document, ownedPool, baseIri);
            foreach(EmittedQuad emitted in emitter.Emit())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return emitted.Quad;
            }
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
        Utf8StringPool? pool,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Utf8StringPool ownedPool = pool ?? new Utf8StringPool();
        try
        {
            byte[] bytes = source.ToArray();
            TurtleDocument document = ParseDocument(bytes, syntax, MintDocumentId(bytes), ownedPool);
            TurtleQuadEmitter emitter = new(document, ownedPool, baseIri);
            foreach(EmittedQuad emitted in emitter.Emit())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return emitted.Quad;
            }

            await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
        }
        finally
        {
            if(pool is null)
            {
                ownedPool.Dispose();
            }
        }
    }

    private static async IAsyncEnumerable<EmittedQuad> IterateWithSourceAsync(
        TurtleDocument document,
        Utf8StringPool pool,
        string? baseIri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TurtleQuadEmitter emitter = new(document, pool, baseIri);
        foreach(EmittedQuad emitted in emitter.Emit())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return emitted;
        }

        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
    }

    private static TurtleDocument ParseDocument(
        byte[] bytes,
        TurtleSyntax syntax,
        DocumentId documentId,
        Utf8StringPool pool)
    {
        TurtleLexer lexer = new(bytes, pool);
        TurtleParser parser = new(lexer.Tokenize(), pool, documentId, syntax);
        return parser.Parse();
    }

    private static DocumentId MintDocumentId(byte[] bytes)
    {
        ulong hash = VeritasHashing.Default(bytes);
        return new DocumentId(hash);
    }

    private static async System.Threading.Tasks.Task<byte[]> DrainAsync(PipeReader input, CancellationToken cancellationToken)
    {
        //Turtle parses the whole document, so the pipe is drained into one contiguous buffer.
        using MemoryStream buffer = new();
        await input.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }
}
