using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.NQuads;

/// <summary>
/// Parses N-Quads and N-Triples format into <see cref="Quad"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// N-Quads is a line-oriented format defined at https://www.w3.org/TR/n-quads/.
/// Each non-empty, non-comment line contains exactly one statement in the form:
/// <c>subject predicate object [graph] .</c>
/// </para>
/// <para>
/// The parser operates on UTF-8 bytes directly. Term strings are interned via
/// <see cref="Utf8StringPool"/> to deduplicate repeated IRIs (such as
/// <c>rdf:type</c>) without per-term heap allocation.
/// </para>
/// <para>
/// Lines are read one at a time. The parser does not buffer the entire input.
/// For large files this means memory usage is bounded by the longest single line.
/// </para>
/// </remarks>
public static class NQuadsReader
{
    private const byte Hash = (byte)'#';
    private const byte LessThan = (byte)'<';
    private const byte GreaterThan = (byte)'>';
    private const byte BlankNodeUnderscore = (byte)'_';
    private const byte BlankNodeColon = (byte)':';
    private const byte QuoteByte = (byte)'"';
    private const byte AtSign = (byte)'@';
    private const byte Caret = (byte)'^';
    private const byte Period = (byte)'.';
    private const byte OpenParen = (byte)'(';
    private const byte CloseParen = (byte)')';
    private const byte Hyphen = (byte)'-';
    private const byte Space = (byte)' ';
    private const byte Tab = (byte)'\t';
    private const byte CarriageReturn = (byte)'\r';
    private const byte NewLine = (byte)'\n';

    /// <summary>
    /// Parses N-Quads from a pipe, yielding one <see cref="Quad"/> per statement.
    /// </summary>
    /// <param name="input">The N-Quads input pipe.</param>
    /// <param name="pool">
    /// The pool used for interning term strings. If <c>null</c>, a temporary pool
    /// is created and disposed when enumeration ends.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    /// <exception cref="NQuadsParseException">
    /// A line was encountered that could not be parsed as a valid N-Quads statement.
    /// </exception>
    public static IAsyncEnumerable<Quad> ReadAsync(
        PipeReader input,
        Utf8StringPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ParseAsync(input, pool, cancellationToken);
    }

    /// <summary>
    /// Parses N-Quads from a <see cref="ReadOnlyMemory{T}"/> buffer.
    /// </summary>
    /// <param name="nquads">The UTF-8 encoded N-Quads data.</param>
    /// <param name="pool">
    /// The pool used for interning term strings. If <c>null</c>, a temporary pool
    /// is created and disposed when enumeration ends.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async sequence of parsed quads.</returns>
    public static IAsyncEnumerable<Quad> ReadAsync(
        ReadOnlyMemory<byte> nquads,
        Utf8StringPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        return ParseAsync(PipeReader.Create(new ReadOnlySequence<byte>(nquads)), pool, cancellationToken);
    }

    /// <summary>
    /// Parses N-Quads from a pipe, yielding one
    /// <see cref="EmittedQuad"/> per statement with provenance back to the
    /// source document.
    /// </summary>
    /// <remarks>
    /// Each emitted quad carries a <see cref="DocumentNodeRef"/> in its
    /// <see cref="EmittedQuad.Source"/> field. The reference's
    /// <see cref="DocumentNodeRef.DocumentId"/> is the
    /// <paramref name="documentId"/> the caller supplies; its
    /// <see cref="DocumentNodeRef.Index"/> is the zero-based position of
    /// the quad in document order. Comments and blank lines do not
    /// consume an index, so indexes are dense across the parsed quads.
    /// Span population is deferred to a future surface-syntax loader; the
    /// reference is the document-node ordinal alone.
    /// </remarks>
    /// <param name="input">The N-Quads input pipe.</param>
    /// <param name="documentId">
    /// The identity of the document being parsed. The caller mints this
    /// once — typically by applying the application's chosen
    /// <c>VeritasHash</c> to the document's canonical bytes — and passes
    /// it in.
    /// </param>
    /// <param name="pool">
    /// The pool used for interning term strings. If <c>null</c>, a temporary pool
    /// is created and disposed when enumeration ends.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async sequence of parsed quads paired with document-node references.</returns>
    /// <exception cref="NQuadsParseException">
    /// A line was encountered that could not be parsed as a valid N-Quads statement.
    /// </exception>
    public static IAsyncEnumerable<EmittedQuad> ReadWithSourceAsync(
        PipeReader input,
        DocumentId documentId,
        Utf8StringPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ParseWithSourceAsync(input, documentId, pool, cancellationToken);
    }

    /// <summary>
    /// Parses N-Quads from a <see cref="ReadOnlyMemory{T}"/> buffer, yielding
    /// one <see cref="EmittedQuad"/> per statement with provenance back to
    /// the source document.
    /// </summary>
    /// <remarks>
    /// Each emitted quad carries a <see cref="DocumentNodeRef"/> in its
    /// <see cref="EmittedQuad.Source"/> field. The reference's
    /// <see cref="DocumentNodeRef.DocumentId"/> is the
    /// <paramref name="documentId"/> the caller supplies; its
    /// <see cref="DocumentNodeRef.Index"/> is the zero-based position of
    /// the quad in document order. Comments and blank lines do not
    /// consume an index, so indexes are dense across the parsed quads.
    /// Span population is deferred to a future surface-syntax loader; the
    /// reference is the document-node ordinal alone.
    /// </remarks>
    /// <param name="nquads">The UTF-8 encoded N-Quads data.</param>
    /// <param name="documentId">
    /// The identity of the document being parsed. The caller mints this
    /// once — typically by applying the application's chosen
    /// <c>VeritasHash</c> to the document's canonical bytes — and passes
    /// it in.
    /// </param>
    /// <param name="pool">
    /// The pool used for interning term strings. If <c>null</c>, a temporary pool
    /// is created and disposed when enumeration ends.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async sequence of parsed quads paired with document-node references.</returns>
    public static IAsyncEnumerable<EmittedQuad> ReadWithSourceAsync(
        ReadOnlyMemory<byte> nquads,
        DocumentId documentId,
        Utf8StringPool? pool = null,
        CancellationToken cancellationToken = default)
    {
        return ParseWithSourceAsync(
            PipeReader.Create(new ReadOnlySequence<byte>(nquads)),
            documentId,
            pool,
            cancellationToken);
    }

    private static async IAsyncEnumerable<Quad> ParseAsync(
        PipeReader input,
        Utf8StringPool? externalPool,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Declare before try so the finally block can always reach it.
        Utf8StringPool? ownedPool = null;
        IMemoryOwner<byte>? lineOwner = null;

        try
        {
            //If the caller supplied a pool, use it directly and leave ownedPool null
            //so the finally block does not dispose what we do not own.
            Utf8StringPool pool = externalPool ?? (ownedPool = new Utf8StringPool());

            int lineNumber = 0;

            while(true)
            {
                ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while(TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    lineNumber++;
                    if(ParseLineOrSkip(line, pool, lineNumber, ref lineOwner) is { } quad)
                    {
                        yield return quad;
                    }
                }

                if(result.IsCompleted)
                {
                    //A final line without a trailing newline remains in the buffer at end of input.
                    if(!buffer.IsEmpty)
                    {
                        lineNumber++;
                        if(ParseLineOrSkip(buffer, pool, lineNumber, ref lineOwner) is { } quad)
                        {
                            yield return quad;
                        }
                    }

                    input.AdvanceTo(buffer.End);

                    break;
                }

                //Consumed up to the start of the unparsed remainder; examined all of it.
                input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            lineOwner?.Dispose();

            ownedPool?.Dispose();

            await input.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<EmittedQuad> ParseWithSourceAsync(
        PipeReader input,
        DocumentId documentId,
        Utf8StringPool? externalPool,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        //Declare before try so the finally block can always reach it.
        Utf8StringPool? ownedPool = null;
        IMemoryOwner<byte>? lineOwner = null;

        try
        {
            //If the caller supplied a pool, use it directly and leave ownedPool null
            //so the finally block does not dispose what we do not own.
            Utf8StringPool pool = externalPool ?? (ownedPool = new Utf8StringPool());

            int lineNumber = 0;
            int nodeIndex = 0;

            while(true)
            {
                ReadResult result = await input.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while(TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    lineNumber++;
                    if(ParseLineOrSkip(line, pool, lineNumber, ref lineOwner) is { } quad)
                    {
                        //Comments and blank lines do not consume a node index, so indexes are
                        //dense across the parsed quads.
                        yield return new EmittedQuad(quad, new DocumentNodeRef(documentId, nodeIndex));
                        nodeIndex++;
                    }
                }

                if(result.IsCompleted)
                {
                    if(!buffer.IsEmpty)
                    {
                        lineNumber++;
                        if(ParseLineOrSkip(buffer, pool, lineNumber, ref lineOwner) is { } quad)
                        {
                            yield return new EmittedQuad(quad, new DocumentNodeRef(documentId, nodeIndex));
                            nodeIndex++;
                        }
                    }

                    input.AdvanceTo(buffer.End);

                    break;
                }

                input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
        finally
        {
            lineOwner?.Dispose();

            ownedPool?.Dispose();

            await input.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Splits the next newline-terminated line from <paramref name="buffer"/>, advancing the
    /// buffer past the line and its terminator. Returns <see langword="false"/> when the buffer
    /// holds no complete line.
    /// </summary>
    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? newline = buffer.PositionOf(NewLine);
        if(newline is null)
        {
            line = default;

            return false;
        }

        line = buffer.Slice(0, newline.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, newline.Value));

        return true;
    }

    /// <summary>
    /// Parses one line's bytes into a quad. Strips a trailing carriage return and surrounding
    /// whitespace, skips empty and comment lines, and interns terms via <paramref name="pool"/>.
    /// Returns <see langword="false"/> for a skipped line.
    /// </summary>
    private static Quad? ParseLineOrSkip(
        ReadOnlySequence<byte> line,
        Utf8StringPool pool,
        int lineNumber,
        ref IMemoryOwner<byte>? lineOwner)
    {
        ReadOnlySpan<byte> span = Materialize(line, pool, ref lineOwner);
        if(span.Length > 0 && span[^1] == CarriageReturn)
        {
            span = span[..^1];
        }

        ReadOnlySpan<byte> trimmed = span.Trim(Space).Trim(Tab);
        if(trimmed.IsEmpty || trimmed[0] == Hash)
        {
            return null;
        }

        return ParseLine(trimmed, pool, lineNumber);
    }

    /// <summary>
    /// Returns the line as a contiguous span. A single-segment line is returned in place; a line
    /// spanning segments is copied into a pooled buffer grown as needed.
    /// </summary>
    private static ReadOnlySpan<byte> Materialize(ReadOnlySequence<byte> line, Utf8StringPool pool, ref IMemoryOwner<byte>? lineOwner)
    {
        if(line.IsSingleSegment)
        {
            return line.FirstSpan;
        }

        int length = (int)line.Length;
        if(lineOwner is null || lineOwner.Memory.Length < length)
        {
            lineOwner?.Dispose();
            lineOwner = pool.RentScratch(length);
        }

        Span<byte> destination = lineOwner.Memory.Span;
        line.CopyTo(destination);

        return destination[..length];
    }

    /// <summary>
    /// Parses a single non-empty, non-comment N-Quads line.
    /// </summary>
    private static Quad ParseLine(ReadOnlySpan<byte> line, Utf8StringPool pool, int lineNumber)
    {
        int pos = 0;

        SkipWhitespace(line, ref pos);
        RdfTerm subject = ParseSubject(line, ref pos, pool, lineNumber);

        SkipWhitespace(line, ref pos);
        NamedNode predicate = ParsePredicate(line, ref pos, pool, lineNumber);

        SkipWhitespace(line, ref pos);
        RdfTerm @object = ParseObject(line, ref pos, pool, lineNumber);

        SkipWhitespace(line, ref pos);

        //Optional graph name.
        RdfTerm? graph = null;
        if(pos < line.Length && line[pos] != Period)
        {
            graph = ParseGraphName(line, ref pos, pool, lineNumber);
            SkipWhitespace(line, ref pos);
        }

        //Expect the statement terminator.
        if(pos >= line.Length || line[pos] != Period)
        {
            throw new NQuadsParseException(
                $"Expected '.' at end of statement on line {lineNumber}.", lineNumber);
        }

        return new Quad(subject, predicate, @object, graph);
    }

    private static RdfTerm ParseSubject(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line while reading subject on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI or blank node for subject on line {lineNumber}.", lineNumber)
        };
    }

    private static NamedNode ParsePredicate(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length || line[pos] != LessThan)
        {
            throw new NQuadsParseException(
                $"Expected IRI for predicate on line {lineNumber}.", lineNumber);
        }

        return (NamedNode)ParseIriRef(line, ref pos, pool, lineNumber);
    }

    private static RdfTerm ParseObject(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line while reading object on line {lineNumber}.", lineNumber);
        }

        //An object may be a triple term <<( s p o )>> (RDF 1.2). A bare '<<' without the
        //opening parenthesis is the Turtle reified-triple form, which N-Triples / N-Quads
        //do not accept.
        if(line[pos] == LessThan && pos + 1 < line.Length && line[pos + 1] == LessThan)
        {
            if(pos + 2 < line.Length && line[pos + 2] == OpenParen)
            {
                return ParseTripleTerm(line, ref pos, pool, lineNumber);
            }

            throw new NQuadsParseException(
                $"Expected '<<(' to begin a triple term on line {lineNumber}; reified-triple syntax is not valid in N-Triples or N-Quads.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            QuoteByte => ParseLiteral(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI, blank node, literal, or triple term for object on line {lineNumber}.", lineNumber)
        };
    }

    /// <summary>
    /// Parses a triple term <c>&lt;&lt;( ttSubject verb ttObject )&gt;&gt;</c>, iterating
    /// with an explicit stack so nested triple terms in the object position do not recurse.
    /// </summary>
    /// <remarks>
    /// Per the RDF 1.2 grammar the triple-term subject is an IRI or blank node and the
    /// predicate is an IRI; only the object may itself be a triple term, so a deeper frame
    /// is pushed exclusively from the object position.
    /// </remarks>
    private static TripleTerm ParseTripleTerm(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        Stack<NQuadsTripleTermFrame> frames = new();
        ExpectTripleTermOpen(line, ref pos, lineNumber);
        frames.Push(new NQuadsTripleTermFrame());
        TripleTerm? completed = null;

        while(frames.Count > 0)
        {
            NQuadsTripleTermFrame frame = frames.Peek();
            SkipWhitespace(line, ref pos);

            switch(frame.Stage)
            {
                case 0:
                {
                    frame.Subject = ParseTripleTermNode(line, ref pos, pool, lineNumber);
                    frame.Stage = 1;

                    break;
                }

                case 1:
                {
                    if(pos >= line.Length || line[pos] != LessThan)
                    {
                        throw new NQuadsParseException($"Expected IRI for triple-term predicate on line {lineNumber}.", lineNumber);
                    }

                    frame.Predicate = (NamedNode)ParseIriRef(line, ref pos, pool, lineNumber);
                    frame.Stage = 2;

                    break;
                }

                case 2:
                {
                    if(line[pos] == LessThan && pos + 1 < line.Length && line[pos + 1] == LessThan)
                    {
                        ExpectTripleTermOpen(line, ref pos, lineNumber);
                        frames.Push(new NQuadsTripleTermFrame());

                        break;
                    }

                    frame.Object = ParseTripleTermObjectLeaf(line, ref pos, pool, lineNumber);
                    frame.Stage = 3;

                    break;
                }

                default:
                {
                    ExpectTripleTermClose(line, ref pos, lineNumber);
                    TripleTerm built = new(frame.Subject!, frame.Predicate!, frame.Object!);
                    frames.Pop();

                    if(frames.Count > 0)
                    {
                        NQuadsTripleTermFrame parent = frames.Peek();
                        parent.Object = built;
                        parent.Stage = 3;
                    }
                    else
                    {
                        completed = built;
                    }

                    break;
                }
            }
        }

        return completed!;
    }

    private static RdfTerm ParseTripleTermNode(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line inside triple term on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI or blank node for triple-term subject on line {lineNumber}.", lineNumber)
        };
    }

    private static RdfTerm ParseTripleTermObjectLeaf(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line inside triple term on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            QuoteByte => ParseLiteral(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI, blank node, literal, or triple term for triple-term object on line {lineNumber}.", lineNumber)
        };
    }

    private static void ExpectTripleTermOpen(ReadOnlySpan<byte> line, ref int pos, int lineNumber)
    {
        if(pos + 2 >= line.Length || line[pos] != LessThan || line[pos + 1] != LessThan || line[pos + 2] != OpenParen)
        {
            throw new NQuadsParseException($"Expected '<<(' to begin a triple term on line {lineNumber}.", lineNumber);
        }

        pos += 3;
    }

    private static void ExpectTripleTermClose(ReadOnlySpan<byte> line, ref int pos, int lineNumber)
    {
        SkipWhitespace(line, ref pos);
        if(pos + 2 >= line.Length || line[pos] != CloseParen || line[pos + 1] != GreaterThan || line[pos + 2] != GreaterThan)
        {
            throw new NQuadsParseException($"Expected ')>>' to close a triple term on line {lineNumber}.", lineNumber);
        }

        pos += 3;
    }

    private static RdfTerm ParseGraphName(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unexpected end of line while reading graph name on line {lineNumber}.", lineNumber);
        }

        return line[pos] switch
        {
            LessThan => ParseIriRef(line, ref pos, pool, lineNumber),
            BlankNodeUnderscore => ParseBlankNode(line, ref pos, pool, lineNumber),
            _ => throw new NQuadsParseException(
                $"Expected IRI or blank node for graph name on line {lineNumber}.", lineNumber)
        };
    }

    /// <summary>
    /// Parses an IRI reference of the form <c>&lt;iri&gt;</c>.
    /// </summary>
    private static NamedNode ParseIriRef(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        //Skip '<'.
        pos++;

        int start = pos;
        while(pos < line.Length && line[pos] != GreaterThan)
        {
            pos++;
        }

        if(pos >= line.Length)
        {
            throw new NQuadsParseException($"Unterminated IRI on line {lineNumber}.", lineNumber);
        }

        ReadOnlySpan<byte> iriBytes = line[start..pos];

        //N-Triples and N-Quads have no base IRI, so every IRI must be absolute: it must begin
        //with a scheme. The scheme is checked on the raw bytes; a UCHAR escape cannot
        //legitimately form the scheme or its terminating colon.
        if(!HasAbsoluteIriScheme(iriBytes))
        {
            throw new NQuadsParseException(
                $"IRI '{Encoding.UTF8.GetString(iriBytes)}' is not absolute (missing scheme) on line {lineNumber}.", lineNumber);
        }

        Utf8String iri = pool.Intern(UnescapeIri(iriBytes));

        //Skip '>'.
        pos++;

        return new NamedNode(iri);
    }

    private static bool HasAbsoluteIriScheme(ReadOnlySpan<byte> iri)
    {
        //RFC 3986 scheme: ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ":". The scheme must be
        //non-empty, begin with a letter, and be terminated by a colon.
        if(iri.IsEmpty || !IsAsciiLetter(iri[0]))
        {
            return false;
        }

        for(int i = 1; i < iri.Length; i++)
        {
            byte b = iri[i];
            if(b == (byte)':')
            {
                return true;
            }

            if(!IsAsciiLetterOrDigit(b) && b != (byte)'+' && b != (byte)'-' && b != (byte)'.')
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses a blank node of the form <c>_:label</c>.
    /// </summary>
    private static BlankNode ParseBlankNode(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        //Expect '_:'.
        if(pos + 1 >= line.Length || line[pos] != BlankNodeUnderscore || line[pos + 1] != BlankNodeColon)
        {
            throw new NQuadsParseException($"Invalid blank node syntax on line {lineNumber}.", lineNumber);
        }

        pos += 2;

        int start = pos;
        while(pos < line.Length && IsBlankNodeLabelByte(line[pos]))
        {
            pos++;
        }

        if(pos == start)
        {
            throw new NQuadsParseException($"Empty blank node label on line {lineNumber}.", lineNumber);
        }

        Utf8String label = pool.Intern(line[start..pos]);

        return new BlankNode(label);
    }

    /// <summary>
    /// Parses a literal of the form <c>"value"</c>, <c>"value"@lang</c>, or <c>"value"^^&lt;datatype&gt;</c>.
    /// </summary>
    private static Literal ParseLiteral(ReadOnlySpan<byte> line, ref int pos, Utf8StringPool pool, int lineNumber)
    {
        //Skip opening quote.
        pos++;

        //Read the quoted value, handling escape sequences. The scratch buffer comes from the
        //interning pool (not a process-wide ArrayPool) and returns to it when this scope ends.
        using IMemoryOwner<byte> valueOwner = pool.RentScratch(line.Length);
        Span<byte> valueBuffer = valueOwner.Memory.Span;

        int valueLength = ReadQuotedString(line, ref pos, valueBuffer, lineNumber);
        Utf8String value = pool.Intern(valueBuffer[..valueLength]);

        //Skip closing quote.
        if(pos >= line.Length || line[pos] != QuoteByte)
        {
            throw new NQuadsParseException($"Unterminated string literal on line {lineNumber}.", lineNumber);
        }

        pos++;

        //The language tag and the datatype marker are separate tokens, so white space is
        //permitted between the closing quote and an '@' or '^^'.
        SkipWhitespace(line, ref pos);

        //Check for language tag or datatype.
        if(pos < line.Length && line[pos] == AtSign)
        {
            //Language tag: @lang, optionally with an RDF 1.2 base direction @lang--ltr / @lang--rtl.
            //White space within the tag (after the '@') is not permitted; an empty tag is rejected
            //downstream.
            pos++;
            int tagStart = pos;
            while(pos < line.Length && !IsWhitespace(line[pos]) && line[pos] != Period)
            {
                pos++;
            }

            return ParseLanguageTaggedLiteral(value, line[tagStart..pos], pool, lineNumber);
        }
        else if(pos + 1 < line.Length && line[pos] == Caret && line[pos + 1] == Caret)
        {
            //Datatype: ^^<IRI>; white space is permitted between '^^' and the IRI.
            pos += 2;
            SkipWhitespace(line, ref pos);

            if(pos >= line.Length || line[pos] != LessThan)
            {
                throw new NQuadsParseException($"Expected IRI after '^^' on line {lineNumber}.", lineNumber);
            }

            NamedNode datatype = ParseIriRef(line, ref pos, pool, lineNumber);

            //rdf:langString and rdf:dirLangString are the datatypes of language-tagged and
            //direction-tagged strings respectively; they only arise from the @lang form and
            //must never appear as an explicit ^^ datatype.
            if(datatype.Iri.Span.SequenceEqual(Vocabulary.Rdf.LangString.Span)
                || datatype.Iri.Span.SequenceEqual(Vocabulary.Rdf.DirLangString.Span))
            {
                throw new NQuadsParseException(
                    $"rdf:langString and rdf:dirLangString require a language tag and cannot be used as an explicit datatype on line {lineNumber}.", lineNumber);
            }

            return new Literal(value, datatype);
        }
        else
        {
            //Plain literal: implicitly xsd:string.
            NamedNode xsdString = new(pool.Intern(Vocabulary.Xsd.String.Span));

            return new Literal(value, xsdString);
        }
    }

    /// <summary>
    /// Builds a language-tagged literal from a validated <c>LANG_DIR</c> tag, splitting an
    /// optional RDF 1.2 base direction (<c>--ltr</c> / <c>--rtl</c>) from the language subtags.
    /// </summary>
    /// <remarks>
    /// The language portion must match <c>[a-zA-Z]+ ('-' [a-zA-Z0-9]+)*</c>; an empty tag, an
    /// empty subtag, or a non-alphanumeric character is rejected. When a base direction is
    /// present it must be exactly <c>ltr</c> or <c>rtl</c> in lower case and the datatype
    /// becomes <c>rdf:dirLangString</c>; otherwise the datatype is <c>rdf:langString</c>.
    /// </remarks>
    private static Literal ParseLanguageTaggedLiteral(Utf8String value, ReadOnlySpan<byte> tag, Utf8StringPool pool, int lineNumber)
    {
        if(tag.IsEmpty)
        {
            throw new NQuadsParseException($"Empty language tag on line {lineNumber}.", lineNumber);
        }

        ReadOnlySpan<byte> languagePart = tag;
        TextDirection? direction = null;

        int directionIndex = IndexOfDoubleHyphen(tag);
        if(directionIndex >= 0)
        {
            languagePart = tag[..directionIndex];
            ReadOnlySpan<byte> directionPart = tag[(directionIndex + 2)..];
            if(!TextDirections.TryParse(directionPart, out TextDirection parsedDirection))
            {
                throw new NQuadsParseException(
                    $"Invalid base direction '{Encoding.UTF8.GetString(directionPart)}' on line {lineNumber}; expected 'ltr' or 'rtl'.", lineNumber);
            }

            direction = parsedDirection;
        }

        ValidateLanguageSubtags(languagePart, lineNumber);

        Utf8String language = pool.Intern(languagePart);
        if(direction is { } resolvedDirection)
        {
            NamedNode dirLangStringType = new(pool.Intern(Vocabulary.Rdf.DirLangString.Span));

            return new Literal(value, dirLangStringType, language, resolvedDirection);
        }

        NamedNode langStringType = new(pool.Intern(Vocabulary.Rdf.LangString.Span));

        return new Literal(value, langStringType, language);
    }

    private static int IndexOfDoubleHyphen(ReadOnlySpan<byte> tag)
    {
        for(int i = 0; i + 1 < tag.Length; i++)
        {
            if(tag[i] == Hyphen && tag[i + 1] == Hyphen)
            {
                return i;
            }
        }

        return -1;
    }

    private static void ValidateLanguageSubtags(ReadOnlySpan<byte> language, int lineNumber)
    {
        //The primary subtag is one or more ASCII letters; each following '-'-separated subtag
        //is one or more letters or digits. This is the BCP 47 shape the RDF grammar requires.
        if(language.IsEmpty || !IsAsciiLetter(language[0]))
        {
            throw new NQuadsParseException($"Language tag must begin with a letter on line {lineNumber}.", lineNumber);
        }

        //Each subtag is one to eight characters per BCP 47.
        const int MaxSubtagLength = 8;
        int subtagLength = 0;
        bool primarySubtag = true;
        for(int i = 0; i < language.Length; i++)
        {
            byte b = language[i];
            if(b == Hyphen)
            {
                if(subtagLength == 0)
                {
                    throw new NQuadsParseException($"Empty language subtag on line {lineNumber}.", lineNumber);
                }

                subtagLength = 0;
                primarySubtag = false;
                continue;
            }

            bool valid = primarySubtag ? IsAsciiLetter(b) : IsAsciiLetterOrDigit(b);
            if(!valid)
            {
                throw new NQuadsParseException(
                    $"Invalid character '{(char)b}' in language tag on line {lineNumber}.", lineNumber);
            }

            subtagLength++;
            if(subtagLength > MaxSubtagLength)
            {
                throw new NQuadsParseException(
                    $"Language subtag exceeds {MaxSubtagLength} characters on line {lineNumber}.", lineNumber);
            }
        }

        if(subtagLength == 0)
        {
            throw new NQuadsParseException($"Language tag ends with an empty subtag on line {lineNumber}.", lineNumber);
        }
    }

    private static bool IsAsciiLetter(byte b)
    {
        return b is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z');
    }

    private static bool IsAsciiLetterOrDigit(byte b)
    {
        return IsAsciiLetter(b) || b is >= (byte)'0' and <= (byte)'9';
    }

    /// <summary>
    /// Reads a quoted string body, processing N-Quads escape sequences.
    /// Returns the number of bytes written to <paramref name="output"/>.
    /// </summary>
    private static int ReadQuotedString(ReadOnlySpan<byte> line, ref int pos, Span<byte> output, int lineNumber)
    {
        int outPos = 0;

        while(pos < line.Length && line[pos] != QuoteByte)
        {
            if(line[pos] == (byte)'\\')
            {
                pos++;
                if(pos >= line.Length)
                {
                    throw new NQuadsParseException($"Unterminated escape sequence on line {lineNumber}.", lineNumber);
                }

                switch((char)line[pos])
                {
                    case 't':
                    {
                        output[outPos++] = (byte)'\t';
                        pos++;

                        break;
                    }

                    case 'b':
                    {
                        output[outPos++] = (byte)'\b';
                        pos++;

                        break;
                    }

                    case 'f':
                    {
                        output[outPos++] = (byte)'\f';
                        pos++;

                        break;
                    }

                    case 'n':
                    {
                        output[outPos++] = (byte)'\n';
                        pos++;

                        break;
                    }

                    case 'r':
                    {
                        output[outPos++] = (byte)'\r';
                        pos++;

                        break;
                    }

                    case '"':
                    {
                        output[outPos++] = (byte)'"';
                        pos++;

                        break;
                    }

                    //ECHAR ::= '\' [tbnrf"'\] — the apostrophe escape is valid in N-Triples/N-Quads (canonical form
                    //emits the bare apostrophe; only " and \ require escaping in the output).
                    case '\'':
                    {
                        output[outPos++] = (byte)'\'';
                        pos++;

                        break;
                    }

                    case '\\':
                    {
                        output[outPos++] = (byte)'\\';
                        pos++;

                        break;
                    }

                    case 'u':
                    {
                        pos++;
                        if(pos + 4 > line.Length)
                        {
                            throw new NQuadsParseException($"Incomplete \\uXXXX escape on line {lineNumber}.", lineNumber);
                        }

                        int codePoint = ParseHex4(line[pos..(pos + 4)], lineNumber);
                        pos += 4;
                        outPos += EncodeUtf8CodePoint(codePoint, output[outPos..]);

                        break;
                    }

                    case 'U':
                    {
                        pos++;
                        if(pos + 8 > line.Length)
                        {
                            throw new NQuadsParseException($"Incomplete \\UXXXXXXXX escape on line {lineNumber}.", lineNumber);
                        }

                        int codePoint = ParseHex8(line[pos..(pos + 8)], lineNumber);
                        pos += 8;
                        outPos += EncodeUtf8CodePoint(codePoint, output[outPos..]);

                        break;
                    }

                    default:
                    {
                        throw new NQuadsParseException(
                            $"Unknown escape sequence '\\{(char)line[pos]}' on line {lineNumber}.", lineNumber);
                    }
                }
            }
            else
            {
                output[outPos++] = line[pos++];
            }
        }

        return outPos;
    }

    private static ReadOnlySpan<byte> UnescapeIri(ReadOnlySpan<byte> iri)
    {
        //Check if any unescaping is needed.
        foreach(byte b in iri)
        {
            if(b == (byte)'\\')
            {
                return UnescapeIriSlow(iri);
            }
        }

        return iri;
    }

    private static byte[] UnescapeIriSlow(ReadOnlySpan<byte> iri)
    {
        byte[] output = new byte[iri.Length * 2];
        int outPos = 0;
        int pos = 0;

        while(pos < iri.Length)
        {
            if(iri[pos] == (byte)'\\' && pos + 1 < iri.Length && iri[pos + 1] == (byte)'u')
            {
                pos += 2;
                int codePoint = ParseHex4(iri[pos..(pos + 4)], 0);
                pos += 4;
                outPos += EncodeUtf8CodePoint(codePoint, output.AsSpan(outPos));
            }
            else if(iri[pos] == (byte)'\\' && pos + 1 < iri.Length && iri[pos + 1] == (byte)'U')
            {
                pos += 2;
                int codePoint = ParseHex8(iri[pos..(pos + 8)], 0);
                pos += 8;
                outPos += EncodeUtf8CodePoint(codePoint, output.AsSpan(outPos));
            }
            else
            {
                output[outPos++] = iri[pos++];
            }
        }

        return output[..outPos];
    }

    private static int ParseHex4(ReadOnlySpan<byte> hex, int lineNumber)
    {
        return (HexDigit(hex[0], lineNumber) << 12)
             | (HexDigit(hex[1], lineNumber) << 8)
             | (HexDigit(hex[2], lineNumber) << 4)
             | HexDigit(hex[3], lineNumber);
    }

    private static int ParseHex8(ReadOnlySpan<byte> hex, int lineNumber)
    {
        return (HexDigit(hex[0], lineNumber) << 28)
             | (HexDigit(hex[1], lineNumber) << 24)
             | (HexDigit(hex[2], lineNumber) << 20)
             | (HexDigit(hex[3], lineNumber) << 16)
             | (HexDigit(hex[4], lineNumber) << 12)
             | (HexDigit(hex[5], lineNumber) << 8)
             | (HexDigit(hex[6], lineNumber) << 4)
             | HexDigit(hex[7], lineNumber);
    }

    private static int HexDigit(byte b, int lineNumber)
    {
        return b switch
        {
            >= (byte)'0' and <= (byte)'9' => b - '0',
            >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
            _ => throw new NQuadsParseException($"Invalid hex digit '{(char)b}' on line {lineNumber}.", lineNumber)
        };
    }

    private static int EncodeUtf8CodePoint(int codePoint, Span<byte> output)
    {
        return new Rune(codePoint).EncodeToUtf8(output);
    }

    private static void SkipWhitespace(ReadOnlySpan<byte> line, ref int pos)
    {
        while(pos < line.Length && IsWhitespace(line[pos]))
        {
            pos++;
        }
    }

    private static bool IsWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t';
    }

    private static bool IsBlankNodeLabelByte(byte b)
    {
        //A blank-node label runs until a delimiter: whitespace, the statement terminator,
        //or a structural character that cannot occur within a label (the angle brackets of a
        //following term, or the parenthesis of a triple-term close). The period is treated as a
        //delimiter here, matching the reader's existing whitespace-or-period boundary.
        return !IsWhitespace(b)
            && b != Period
            && b != CloseParen
            && b != OpenParen
            && b != LessThan
            && b != GreaterThan;
    }
}