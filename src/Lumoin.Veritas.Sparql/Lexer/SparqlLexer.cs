using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Lexer;

/// <summary>
/// Tokenises UTF-8 SPARQL 1.2 query text one token at a time.
/// </summary>
/// <remarks>
/// <para>
/// The lexer is iterative — no recursion. Its core is resumable: each step returns a
/// <see cref="SparqlLexStatus"/> rather than throwing or blocking, so the same code serves a
/// synchronous whole-buffer pass (<see cref="Tokenize"/>) and an asynchronous pull over a pipe
/// (<see cref="TokenizeAsync"/>). A <see cref="SparqlLexStatus.NeedMore"/> asks the driver for more
/// bytes; a <see cref="SparqlLexStatus.Error"/> reports a recorded <see cref="SparqlLexDiagnostic"/>
/// without unwinding the stack — the lexer then emits a <see cref="SparqlTokenKind.Error"/> token over
/// the offending bytes, resynchronises to the next token boundary, and continues. Recovery is always
/// on; the lexer never throws on malformed input.
/// </para>
/// <para>
/// Byte access is through <see cref="SequenceReader{T}"/> over a <see cref="ReadOnlySequence{T}"/>,
/// so a token that straddles two buffer segments is read without first gathering the whole source
/// into one contiguous block. A <see cref="ReadOnlyMemory{T}"/> source is wrapped as a
/// single-segment sequence.
/// </para>
/// <para>
/// The SPARQL grammar reuses <c>&lt;</c> for both IRI brackets and the less-than operator and
/// <c>?</c> for both a variable marker and the zero-or-one path quantifier; the lexer disambiguates
/// contextually. A lead <c>&lt;</c> is probed as an IRI first (a body that closes with <c>&gt;</c>
/// using only IRI-legal bytes), falling back to <c>&lt;=</c> or <c>&lt;</c>; a <c>?</c> followed by
/// a variable-name start is a variable, otherwise the path quantifier.
/// </para>
/// <para>
/// String interning is the caller's responsibility through the supplied
/// <see cref="Utf8StringPool"/>. The lexer interns every payload so the parser receives stable
/// <see cref="Utf8String"/> handles that compare and hash without touching the underlying source
/// memory.
/// </para>
/// </remarks>
public sealed class SparqlLexer
{
    private long consumed;
    private int line;
    private int column;
    private bool atFinalBuffer = true;
    private int decoderDiagnosticsCopied;
    private SparqlLexDiagnostic pendingDiagnostic;
    private SparqlTokenGrowthContext scratchToken;
    private readonly List<SparqlLexDiagnostic> diagnostics = [];

    /// <summary>
    /// Initialises a new <see cref="SparqlLexer"/> for the supplied UTF-8 source bytes.
    /// </summary>
    /// <param name="source">The UTF-8 encoded query text.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="SparqlLexerLimits.Default"/>.</param>
    public SparqlLexer(ReadOnlyMemory<byte> source, Utf8StringPool pool, SparqlLexerLimits? limits = null)
        : this(new ReadOnlySequence<byte>(source), pool, limits)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="SparqlLexer"/> for the pipe-driven path, where source bytes are
    /// supplied to <see cref="TokenizeAsync"/> rather than held up front.
    /// </summary>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="SparqlLexerLimits.Default"/>.</param>
    public SparqlLexer(Utf8StringPool pool, SparqlLexerLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Source = ReadOnlySequence<byte>.Empty;
        Pool = pool;
        Limits = limits ?? SparqlLexerLimits.Default;
    }

    /// <summary>
    /// Initialises a new <see cref="SparqlLexer"/> for the supplied UTF-8 source sequence.
    /// </summary>
    /// <remarks>
    /// The whole buffer is codepoint-decoded up front (SPARQL 1.2 §19.2) through
    /// <see cref="SparqlCodepointDecoder"/>; <see cref="Source"/> is the decoded byte stream, and the
    /// decoder's offset map translates decoded positions back to source coordinates for token and
    /// diagnostic spans.
    /// </remarks>
    /// <param name="source">The UTF-8 encoded query text, possibly spanning segments.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="SparqlLexerLimits.Default"/>.</param>
    public SparqlLexer(ReadOnlySequence<byte> source, Utf8StringPool pool, SparqlLexerLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Pool = pool;
        Limits = limits ?? SparqlLexerLimits.Default;

        if(source.IsSingleSegment)
        {
            Decoder.Feed(source.FirstSpan, isFinal: true);
        }
        else
        {
            Decoder.Feed(source.ToArray(), isFinal: true);
        }

        Source = new ReadOnlySequence<byte>(Decoder.Decoded);

        foreach(SparqlLexDiagnostic diagnostic in Decoder.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }
    }

    /// <summary>Gets the UTF-8 source bytes being lexed.</summary>
    private ReadOnlySequence<byte> Source { get; }

    /// <summary>Gets the pool used to intern token payloads.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Gets the resource limits applied while lexing.</summary>
    private SparqlLexerLimits Limits { get; }

    /// <summary>Gets the codepoint-escape decoder that produces the decoded byte stream and its source-offset map.</summary>
    private SparqlCodepointDecoder Decoder { get; } = new();

    /// <summary>
    /// Gets the lexical diagnostics recorded while tokenising, in source order — one entry for each
    /// <see cref="SparqlTokenKind.Error"/> token the lexer emits.
    /// </summary>
    /// <remarks>
    /// Recovery is always on: instead of throwing, the lexer records a <see cref="SparqlLexDiagnostic"/>
    /// here and emits an <see cref="SparqlTokenKind.Error"/> token spanning the offending bytes, then
    /// resynchronises and continues. A consumer bridges these to layer-stable
    /// <see cref="Lumoin.Veritas.Core.Diagnostics.Diagnostic"/> values via
    /// <see cref="SparqlLexDiagnosticBridge"/>.
    /// </remarks>
    public IReadOnlyList<SparqlLexDiagnostic> Diagnostics => diagnostics;

    /// <summary>
    /// Iterates the source query producing tokens until end of input.
    /// </summary>
    /// <remarks>
    /// The iterator yields a <see cref="SparqlTokenKind.EndOfInput"/> token as its final element so
    /// the parser can drive its loop without checking the source position separately. Recovery is
    /// always on: a lexical error is recorded in <see cref="Diagnostics"/> and surfaces as a
    /// <see cref="SparqlTokenKind.Error"/> token rather than an exception.
    /// </remarks>
    /// <returns>An iterator over the source tokens.</returns>
    public IEnumerable<SparqlToken> Tokenize()
    {
        while(true)
        {
            SparqlToken token = LexNextToken();
            yield return token;

            if(token.Kind == SparqlTokenKind.EndOfInput)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// Pulls UTF-8 bytes from a <see cref="PipeReader"/> and yields tokens as they complete, without
    /// buffering the whole document. Each lexical error is recorded in <see cref="Diagnostics"/> and
    /// surfaces as a <see cref="SparqlTokenKind.Error"/> token; the lexer resynchronises and continues
    /// rather than throwing.
    /// </summary>
    /// <param name="pipeReader">The pipe delivering UTF-8 source bytes.</param>
    /// <param name="cancellationToken">Cancels between reads.</param>
    /// <returns>An asynchronous stream of tokens ending with <see cref="SparqlTokenKind.EndOfInput"/>.</returns>
    public IAsyncEnumerable<SparqlToken> TokenizeAsync(PipeReader pipeReader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeReader);

        return TokenizeInternalAsync(pipeReader, cancellationToken);
    }

    private SparqlToken LexNextToken()
    {
        //A SequenceReader is a ref struct and cannot live across a yield, so each token positions a
        //fresh reader at the running offset. For the single-segment whole-buffer feed this advance
        //is constant-time, and atFinalBuffer is true so NeedMore never arises.
        SequenceReader<byte> reader = new(Source);
        reader.Advance(consumed);

        SparqlLexStatus status = TryLexToken(ref reader, out SparqlToken token);

        if(status == SparqlLexStatus.Error)
        {
            token = RecordErrorAndRecover(ref reader);
        }

        consumed = reader.Consumed;

        return token;
    }

    private async IAsyncEnumerable<SparqlToken> TokenizeInternalAsync(
        PipeReader pipeReader,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        diagnostics.Clear();

        while(true)
        {
            ReadResult result = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);

            if(result.IsCanceled)
            {
                pipeReader.AdvanceTo(result.Buffer.Start, result.Buffer.End);

                yield break;
            }

            atFinalBuffer = result.IsCompleted;

            //Codepoint-decode the newly available source (SPARQL 1.2 §19.2) into the decoder's running
            //decoded buffer; the tokeniser then runs over the decoded bytes. The pipe resumes after the
            //source the decoder consumed, so a partial escape spanning the buffer end is re-presented next
            //read.
            int sourceConsumed = FeedDecoder(result.Buffer, atFinalBuffer);
            SequencePosition resumePosition = result.Buffer.GetPosition(sourceConsumed);
            ReadOnlySequence<byte> decodedSource = new(Decoder.Decoded);
            bool reachedEnd = false;

            while(true)
            {
                //A SequenceReader is a ref struct and cannot live across a yield, so each token is lexed by
                //a helper that returns before the yield.
                SparqlLexStatus status = LexNextDecodedToken(decodedSource, out SparqlToken token);

                if(status == SparqlLexStatus.NeedMore)
                {
                    break;
                }

                yield return token;

                if(token.Kind == SparqlTokenKind.EndOfInput)
                {
                    reachedEnd = true;

                    break;
                }
            }

            pipeReader.AdvanceTo(resumePosition, result.Buffer.End);

            if(reachedEnd)
            {
                yield break;
            }
        }
    }

    private SparqlLexStatus LexNextDecodedToken(ReadOnlySequence<byte> decodedSource, out SparqlToken token)
    {
        //The line/column fields track the source position the decoder's map resolves as the reader advances; a
        //partial attempt advances them, so they are captured here and restored on NeedMore alongside the held offset,
        //keeping the re-lexed token's start position correct (a one-byte-at-a-time feed otherwise mis-columns it).
        int suspendedLine = line;
        int suspendedColumn = column;

        SequenceReader<byte> reader = new(decodedSource);
        reader.Advance(consumed);

        SparqlLexStatus status = TryLexToken(ref reader, out token);

        if(status == SparqlLexStatus.NeedMore)
        {
            //Leave the running offset where it was so the partial token is re-lexed once more decoded
            //bytes arrive, and rewind line/column so the re-lex captures the same start position.
            line = suspendedLine;
            column = suspendedColumn;

            return status;
        }

        if(status == SparqlLexStatus.Error)
        {
            token = RecordErrorAndRecover(ref reader);
        }

        consumed = reader.Consumed;

        return status;
    }

    private int FeedDecoder(ReadOnlySequence<byte> buffer, bool isFinal)
    {
        int sourceConsumed = buffer.IsSingleSegment
            ? Decoder.Feed(buffer.FirstSpan, isFinal)
            : Decoder.Feed(buffer.ToArray(), isFinal);

        CopyNewDecoderDiagnostics();

        return sourceConsumed;
    }

    /// <summary>Copies any decoder diagnostics produced since the last feed into the lexer's diagnostic list, in source order.</summary>
    private void CopyNewDecoderDiagnostics()
    {
        while(decoderDiagnosticsCopied < Decoder.Diagnostics.Count)
        {
            diagnostics.Add(Decoder.Diagnostics[decoderDiagnosticsCopied]);
            decoderDiagnosticsCopied++;
        }
    }

    /// <summary>
    /// Codepoint-decodes a chunk of source bytes for the synchronous incremental path, marking whether it is the final
    /// chunk, and returns the source bytes consumed — an unconsumed tail is a partial escape the caller re-presents
    /// (prepended to the next chunk) when more bytes arrive.
    /// </summary>
    /// <param name="source">The source bytes to decode (a re-presented partial-escape tail plus the new chunk).</param>
    /// <param name="isFinal">Whether this is the final chunk; when <see langword="true"/> a token may complete at end of input.</param>
    /// <returns>The number of source bytes the decoder consumed.</returns>
    internal int FeedDecodedSource(ReadOnlySpan<byte> source, bool isFinal)
    {
        atFinalBuffer = isFinal;

        int sourceConsumed = Decoder.Feed(source, isFinal);
        CopyNewDecoderDiagnostics();

        return sourceConsumed;
    }

    /// <summary>
    /// Lexes the next token from the bytes decoded so far, for the synchronous incremental path. Returns
    /// <see cref="SparqlLexStatus.NeedMore"/> when the decoded tail ends mid-token (the running offset is held so the
    /// token re-lexes once more bytes arrive), mirroring <see cref="TokenizeInternalAsync"/>'s inner pump.
    /// </summary>
    /// <param name="token">The lexed token (an <see cref="SparqlTokenKind.Error"/> token after recovery); undefined when the result is <see cref="SparqlLexStatus.NeedMore"/>.</param>
    /// <returns>The lex status.</returns>
    internal SparqlLexStatus TryLexNext(out SparqlToken token)
    {
        return LexNextDecodedToken(new ReadOnlySequence<byte>(Decoder.Decoded), out token);
    }

    private SparqlLexStatus TryLexToken(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        SkipWhitespaceAndComments(ref reader);

        if(reader.End)
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            int offset = Offset(reader.Consumed);
            token = new SparqlToken(
                SparqlTokenKind.EndOfInput,
                CaptureSpan(offset, offset, line, column, line, column),
                Pool.Intern(ReadOnlySpan<byte>.Empty));

            return SparqlLexStatus.Complete;
        }

        return LexNext(ref reader, out token);
    }

    private SparqlLexStatus LexNext(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        reader.TryPeek(out byte current);

        switch(current)
        {
            case (byte)'<':
            {
                return LexLessThan(ref reader, out token);
            }

            case (byte)'>':
            {
                return LexGreaterThan(ref reader, out token);
            }

            case (byte)'"':
            {
                return LexString(ref reader, (byte)'"', out token);
            }

            case (byte)'\'':
            {
                return LexString(ref reader, (byte)'\'', out token);
            }

            case (byte)'_':
            {
                return LexBlankNodeLabel(ref reader, out token);
            }

            case (byte)'?':
            {
                return LexQuestionOrVariable(ref reader, out token);
            }

            case (byte)'$':
            {
                return LexDollarVariable(ref reader, out token);
            }

            case (byte)'[':
            {
                return LexOpenBracket(ref reader, out token);
            }

            case (byte)']':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.CloseBracket, out token);
            }

            case (byte)'(':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.OpenParen, out token);
            }

            case (byte)')':
            {
                return LexCloseParen(ref reader, out token);
            }

            case (byte)'{':
            {
                return LexOpenBrace(ref reader, out token);
            }

            case (byte)'~':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Tilde, out token);
            }

            case (byte)'}':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.CloseBrace, out token);
            }

            case (byte)',':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Comma, out token);
            }

            case (byte)';':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Semicolon, out token);
            }

            case (byte)'.':
            {
                return LexPeriodOrDecimal(ref reader, out token);
            }

            case (byte)'*':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Star, out token);
            }

            case (byte)'+':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Plus, out token);
            }

            case (byte)'-':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Minus, out token);
            }

            case (byte)'/':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Slash, out token);
            }

            case (byte)'=':
            {
                return LexSinglePunctuation(ref reader, SparqlTokenKind.Equals, out token);
            }

            case (byte)'|':
            {
                return LexPipe(ref reader, out token);
            }

            case (byte)'&':
            {
                return LexAmpersand(ref reader, out token);
            }

            case (byte)'!':
            {
                return LexBang(ref reader, out token);
            }

            case (byte)'^':
            {
                return LexCaret(ref reader, out token);
            }

            case (byte)'@':
            {
                return LexLanguageTag(ref reader, out token);
            }

            case >= (byte)'0' and <= (byte)'9':
            {
                return LexNumericUnsigned(ref reader, out token);
            }

            default:
            {
                return LexIdentifierLike(ref reader, out token);
            }
        }
    }

    private SparqlLexStatus LexLessThan(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            //A lone '<' at end of input is the less-than operator.
            return LexSinglePunctuation(ref reader, SparqlTokenKind.LessThan, out token);
        }

        if(second == (byte)'<')
        {
            bool haveThird = reader.TryPeek(2, out byte third);
            if(!haveThird && !atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;

            if(haveThird && third == (byte)'(')
            {
                AdvanceCount(ref reader, 3);
                token = new SparqlToken(
                    SparqlTokenKind.OpenTripleTerm,
                    CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                    Pool.Intern("<<("u8));

                return SparqlLexStatus.Complete;
            }

            AdvanceCount(ref reader, 2);
            token = new SparqlToken(
                SparqlTokenKind.OpenReifiedTriple,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("<<"u8));

            return SparqlLexStatus.Complete;
        }

        //Probe whether '<' begins an IRI reference: a body of IRI-legal bytes closed by '>'. If the
        //probe reaches an illegal byte or end of input first, the '<' is the less-than(-or-equal)
        //operator instead.
        SparqlLexStatus probe = ProbeForIri(ref reader, out bool isIri);
        if(probe == SparqlLexStatus.NeedMore)
        {
            return SparqlLexStatus.NeedMore;
        }

        if(isIri)
        {
            return LexIri(ref reader, out token);
        }

        if(second == (byte)'=')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.LessOrEqual,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("<="u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.LessThan, out token);
    }

    private SparqlLexStatus ProbeForIri(ref SequenceReader<byte> reader, out bool isIri)
    {
        isIri = false;

        //Offset 0 is '<'; scan from offset 1 looking for the closing '>' before any byte that is
        //illegal inside an IRI reference. A backslash introduces a UCHAR escape and is permitted.
        long probe = 1;
        while(true)
        {
            if(!reader.TryPeek(probe, out byte b))
            {
                if(!atFinalBuffer)
                {
                    return SparqlLexStatus.NeedMore;
                }

                //End of input with no closing '>': not an IRI.
                return SparqlLexStatus.Complete;
            }

            if(b == (byte)'>')
            {
                isIri = true;

                return SparqlLexStatus.Complete;
            }

            if(b <= 0x20 || b == (byte)'<' || b == (byte)'"' || b == (byte)'{' || b == (byte)'}' || b == (byte)'|' || b == (byte)'^' || b == (byte)'`')
            {
                //An IRI-illegal byte before any closing '>': not an IRI.
                return SparqlLexStatus.Complete;
            }

            probe++;
        }
    }

    private SparqlLexStatus LexGreaterThan(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return LexSinglePunctuation(ref reader, SparqlTokenKind.GreaterThan, out token);
        }

        if(second == (byte)'>')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.CloseReifiedTriple,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(">>"u8));

            return SparqlLexStatus.Complete;
        }

        if(second == (byte)'=')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.GreaterOrEqual,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(">="u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.GreaterThan, out token);
    }

    private SparqlLexStatus LexPipe(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return LexSinglePunctuation(ref reader, SparqlTokenKind.Pipe, out token);
        }

        if(second == (byte)'|')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.LogicalOr,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("||"u8));

            return SparqlLexStatus.Complete;
        }

        if(second == (byte)'}')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.CloseAnnotation,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("|}"u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.Pipe, out token);
    }

    private SparqlLexStatus LexOpenBrace(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return LexSinglePunctuation(ref reader, SparqlTokenKind.OpenBrace, out token);
        }

        if(second == (byte)'|')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.OpenAnnotation,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("{|"u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.OpenBrace, out token);
    }

    private SparqlLexStatus LexAmpersand(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'&')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.LogicalAnd,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("&&"u8));

            return SparqlLexStatus.Complete;
        }

        return Fail(
            SparqlLexErrorCode.ExpectedSecondAmpersand,
            SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
    }

    private SparqlLexStatus LexBang(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return LexSinglePunctuation(ref reader, SparqlTokenKind.Bang, out token);
        }

        if(second == (byte)'=')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.NotEquals,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("!="u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.Bang, out token);
    }

    private SparqlLexStatus LexCaret(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return LexSinglePunctuation(ref reader, SparqlTokenKind.Caret, out token);
        }

        if(second == (byte)'^')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new SparqlToken(
                SparqlTokenKind.TypeMarker,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("^^"u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.Caret, out token);
    }

    private SparqlLexStatus LexOpenBracket(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        //Detect the ANON form '[' WS* ']' so the parser can produce a single anonymous-blank-node term.
        long probeOffset = 1;
        while(reader.TryPeek(probeOffset, out byte whitespace) && IsWhitespaceByte(whitespace))
        {
            probeOffset++;
        }

        bool sawClose = reader.TryPeek(probeOffset, out byte close);
        if(!sawClose && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        if(sawClose && close == (byte)']')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;

            long endConsumed = reader.Consumed + probeOffset + 1;
            while(reader.Consumed < endConsumed)
            {
                Advance(ref reader);
            }

            token = new SparqlToken(
                SparqlTokenKind.AnonymousBlankNode,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("[]"u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.OpenBracket, out token);
    }

    private SparqlLexStatus LexCloseParen(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        //RDF 1.2 triple-term close is the three-byte sequence ')>>'.
        bool first = reader.TryPeek(1, out byte firstAngle);
        bool secondAvailable = reader.TryPeek(2, out byte secondAngle);
        if((!first || !secondAvailable) && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        if(first && firstAngle == (byte)'>' && secondAvailable && secondAngle == (byte)'>')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 3);

            token = new SparqlToken(
                SparqlTokenKind.CloseTripleTerm,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(")>>"u8));

            return SparqlLexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.CloseParen, out token);
    }

    private SparqlLexStatus LexSinglePunctuation(ref SequenceReader<byte> reader, SparqlTokenKind kind, out SparqlToken token)
    {
        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        token = new SparqlToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return SparqlLexStatus.Complete;
    }

    private SparqlLexStatus LexPeriodOrDecimal(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        //A leading period followed by a digit begins a numeric literal: ".5", ".5e10".
        if(!reader.TryPeek(1, out byte next))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }
        }
        else if(IsDigit(next))
        {
            return LexNumericUnsigned(ref reader, out token);
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.Period, out token);
    }

    private SparqlLexStatus LexQuestionOrVariable(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        //'?' followed by a variable-name start is a variable; otherwise it is the zero-or-one path
        //quantifier.
        if(!reader.TryPeek(1, out byte next))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return LexSinglePunctuation(ref reader, SparqlTokenKind.Question, out token);
        }

        if(IsVarNameStart(next))
        {
            return LexVariableBody(ref reader, out token);
        }

        return LexSinglePunctuation(ref reader, SparqlTokenKind.Question, out token);
    }

    private SparqlLexStatus LexDollarVariable(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte next))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return Fail(
                SparqlLexErrorCode.ExpectedVariableName,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        if(!IsVarNameStart(next))
        {
            return Fail(
                SparqlLexErrorCode.ExpectedVariableName,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        return LexVariableBody(ref reader, out token);
    }

    private SparqlLexStatus LexVariableBody(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        //Consume the '?' or '$' marker; the interned value omits it.
        Advance(ref reader);

        SequencePosition nameStartPosition = reader.Position;
        while(reader.TryPeek(out byte b) && IsVarNameChar(b))
        {
            Advance(ref reader);
        }

        //The variable name may extend past the buffer end.
        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        token = new SparqlToken(
            SparqlTokenKind.Variable,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(nameStartPosition, reader.Position)));

        return SparqlLexStatus.Complete;
    }

    private SparqlLexStatus LexIri(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        scratchToken = new SparqlTokenGrowthContext(SparqlTokenKind.Iri, 0, startByte, startLine, startColumn);

        Advance(ref reader);

        IMemoryOwner<byte> scratch = Pool.RentScratch(64);
        int written = 0;

        try
        {
            while(true)
            {
                if(!reader.TryPeek(out byte b))
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.UnterminatedIri,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == (byte)'>')
                {
                    Advance(ref reader);

                    token = new SparqlToken(
                        SparqlTokenKind.Iri,
                        CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                        Pool.Intern(scratch.Memory.Span[..written]));

                    return SparqlLexStatus.Complete;
                }

                //IRIREF excludes the backslash; codepoint escapes are decoded before tokenisation, so any
                //backslash reaching the IRI body is an illegal byte.
                if(b < 0x21 || b == (byte)'"' || b == (byte)'<' || b == (byte)'{' || b == (byte)'}' || b == (byte)'|' || b == (byte)'^' || b == (byte)'`' || b == (byte)'\\')
                {
                    return Fail(
                        SparqlLexErrorCode.InvalidIriByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        SparqlLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                EnsureScratchCapacity(ref scratch, written + byteCount);

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.TruncatedUtf8,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
                }

                Span<byte> buffer = scratch.Memory.Span;
                for(int i = 0; i < byteCount; i++)
                {
                    reader.TryPeek(out byte continuation);
                    buffer[written++] = continuation;
                    Advance(ref reader);
                }
            }
        }
        finally
        {
            scratch.Dispose();
        }
    }

    private SparqlLexStatus LexString(ref SequenceReader<byte> reader, byte quote, out SparqlToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        //Deciding short vs long needs two bytes of lookahead past the opening quote.
        if(!reader.TryPeek(1, out byte secondQuote))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }
        }
        else if(secondQuote == quote)
        {
            if(!reader.TryPeek(2, out byte thirdQuote))
            {
                if(!atFinalBuffer)
                {
                    return SparqlLexStatus.NeedMore;
                }
            }
            else if(thirdQuote == quote)
            {
                return LexLongString(ref reader, quote, out token);
            }
        }

        scratchToken = new SparqlTokenGrowthContext(SparqlTokenKind.StringLiteral, 0, startByte, startLine, startColumn);

        Advance(ref reader);

        IMemoryOwner<byte> scratch = Pool.RentScratch(64);
        int written = 0;

        try
        {
            while(true)
            {
                if(!reader.TryPeek(out byte b))
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.UnterminatedString,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == quote)
                {
                    Advance(ref reader);

                    token = new SparqlToken(
                        SparqlTokenKind.StringLiteral,
                        CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                        Pool.Intern(scratch.Memory.Span[..written]));

                    return SparqlLexStatus.Complete;
                }

                if(b == (byte)'\n' || b == (byte)'\r')
                {
                    return Fail(
                        SparqlLexErrorCode.UnescapedLineBreak,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
                }

                if(b == (byte)'\\')
                {
                    SparqlLexStatus escapeStatus = DecodeEscape(ref reader, ref scratch, ref written);
                    if(escapeStatus != SparqlLexStatus.Complete)
                    {
                        return escapeStatus;
                    }

                    continue;
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        SparqlLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.TruncatedUtf8,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
                }

                EnsureScratchCapacity(ref scratch, written + byteCount);

                Span<byte> buffer = scratch.Memory.Span;
                for(int i = 0; i < byteCount; i++)
                {
                    reader.TryPeek(out byte continuation);
                    buffer[written++] = continuation;
                    Advance(ref reader);
                }
            }
        }
        finally
        {
            scratch.Dispose();
        }
    }

    private SparqlLexStatus LexLongString(ref SequenceReader<byte> reader, byte quote, out SparqlToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        scratchToken = new SparqlTokenGrowthContext(SparqlTokenKind.LongStringLiteral, 0, startByte, startLine, startColumn);

        AdvanceCount(ref reader, 3);

        IMemoryOwner<byte> scratch = Pool.RentScratch(128);
        int written = 0;

        try
        {
            while(true)
            {
                if(!reader.TryPeek(out byte b))
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.UnterminatedLongString,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == quote)
                {
                    if(!reader.TryPeek(1, out byte secondQuote))
                    {
                        if(!atFinalBuffer)
                        {
                            return SparqlLexStatus.NeedMore;
                        }
                    }
                    else if(secondQuote == quote)
                    {
                        if(!reader.TryPeek(2, out byte thirdQuote))
                        {
                            if(!atFinalBuffer)
                            {
                                return SparqlLexStatus.NeedMore;
                            }
                        }
                        else if(thirdQuote == quote)
                        {
                            AdvanceCount(ref reader, 3);

                            token = new SparqlToken(
                                SparqlTokenKind.LongStringLiteral,
                                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                                Pool.Intern(scratch.Memory.Span[..written]));

                            return SparqlLexStatus.Complete;
                        }
                    }
                }

                if(b == (byte)'\\')
                {
                    SparqlLexStatus escapeStatus = DecodeEscape(ref reader, ref scratch, ref written);
                    if(escapeStatus != SparqlLexStatus.Complete)
                    {
                        return escapeStatus;
                    }

                    continue;
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        SparqlLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.TruncatedUtf8,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
                }

                EnsureScratchCapacity(ref scratch, written + byteCount);

                Span<byte> buffer = scratch.Memory.Span;
                for(int i = 0; i < byteCount; i++)
                {
                    reader.TryPeek(out byte continuation);
                    buffer[written++] = continuation;
                    Advance(ref reader);
                }
            }
        }
        finally
        {
            scratch.Dispose();
        }
    }

    private SparqlLexStatus DecodeEscape(ref SequenceReader<byte> reader, ref IMemoryOwner<byte> owner, ref int written)
    {
        if(!reader.TryPeek(1, out byte marker))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return Fail(
                SparqlLexErrorCode.TruncatedEscape,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
        }

        switch(marker)
        {
            case (byte)'t':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\t';
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'b':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = 0x08;
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'n':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\n';
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'r':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\r';
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'f':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = 0x0C;
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'"':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'"';
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'\'':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\'';
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            case (byte)'\\':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\\';
                AdvanceCount(ref reader, 2);

                return SparqlLexStatus.Complete;
            }

            default:
            {
                //Numeric codepoint escapes (\u/\U) are decoded before tokenisation, so the only escapes
                //the string body sees are ECHAR; any other marker (for example a \A produced by decoding
                //\\u0041) is an invalid string escape.
                return Fail(
                    SparqlLexErrorCode.InvalidEscape,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 2, line, column, column + 2),
                    FormatEscape(marker));
            }
        }
    }

    private SparqlLexStatus LexBlankNodeLabel(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte colon))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return Fail(
                SparqlLexErrorCode.ExpectedColonAfterUnderscore,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        if(colon != (byte)':')
        {
            return Fail(
                SparqlLexErrorCode.ExpectedColonAfterUnderscore,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        AdvanceCount(ref reader, 2);

        SequencePosition labelStartPosition = reader.Position;
        if(!reader.TryPeek(out byte first))
        {
            if(!atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            return Fail(
                SparqlLexErrorCode.ExpectedBlankNodeLabel,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        if(!IsPnCharsUOrDigit(first))
        {
            return Fail(
                SparqlLexErrorCode.ExpectedBlankNodeLabel,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        Advance(ref reader);

        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;

        //Greedy scan; a trailing '.' that is not part of the label is stripped after the loop.
        while(reader.TryPeek(out byte b) && (IsPnChars(b) || b == (byte)'.'))
        {
            Advance(ref reader);

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        token = new SparqlToken(
            SparqlTokenKind.BlankNodeLabel,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(labelStartPosition, reader.Position)));

        return SparqlLexStatus.Complete;
    }

    private SparqlLexStatus LexLanguageTag(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        //Consume '@'; SPARQL uses '@' only to introduce a language tag on a literal.
        Advance(ref reader);

        SequencePosition identifierStartPosition = reader.Position;
        int identifierStartByte = Offset(reader.Consumed);
        while(reader.TryPeek(out byte letter) && IsAsciiLetter(letter))
        {
            Advance(ref reader);
        }

        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        if(Offset(reader.Consumed) == identifierStartByte)
        {
            return Fail(
                SparqlLexErrorCode.ExpectedIdentifierAfterAt,
                SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
        }

        return LexLanguageTagBody(ref reader, startByte, startLine, startColumn, identifierStartPosition, out token);
    }

    private SparqlLexStatus LexLanguageTagBody(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition identifierStartPosition,
        out SparqlToken token)
    {
        token = default;
        bool sawDirection = false;

        while(reader.TryPeek(out byte dash) && dash == (byte)'-')
        {
            //RDF 1.2 direction marker '--' separates language subtags from the base direction.
            if(reader.TryPeek(1, out byte secondDash) && secondDash == (byte)'-')
            {
                AdvanceCount(ref reader, 2);
                sawDirection = true;
                int dirStart = Offset(reader.Consumed);

                while(reader.TryPeek(out byte letter) && IsAsciiLetter(letter))
                {
                    Advance(ref reader);
                }

                if(reader.End && !atFinalBuffer)
                {
                    return SparqlLexStatus.NeedMore;
                }

                if(Offset(reader.Consumed) == dirStart)
                {
                    return Fail(
                        SparqlLexErrorCode.ExpectedDirectionTag,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
                }

                break;
            }

            if(!reader.TryPeek(1, out _) && !atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            Advance(ref reader);
            int subtagStart = Offset(reader.Consumed);
            while(reader.TryPeek(out byte alnum) && IsAsciiLetterOrDigit(alnum))
            {
                Advance(ref reader);
            }

            if(reader.End && !atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            if(Offset(reader.Consumed) == subtagStart)
            {
                return Fail(
                    SparqlLexErrorCode.ExpectedLanguageSubtag,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
            }
        }

        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        Utf8String value = Pool.Intern(reader.Sequence.Slice(identifierStartPosition, reader.Position));
        SparqlTokenKind kind = sawDirection ? SparqlTokenKind.DirLangTag : SparqlTokenKind.LangTag;

        token = new SparqlToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            value);

        return SparqlLexStatus.Complete;
    }

    private SparqlLexStatus LexNumericUnsigned(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        return ContinueNumeric(ref reader, startByte, startLine, startColumn, out token);
    }

    private SparqlLexStatus ContinueNumeric(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        out SparqlToken token)
    {
        token = default;

        //ContinueNumeric runs with the reader still at the literal's first byte, so its position is the
        //start position; capturing it directly avoids reconstructing a decoded position from a source offset.
        SequencePosition startPosition = reader.Position;
        bool sawDecimal = false;
        bool sawExponent = false;
        bool sawDigit = false;

        while(reader.TryPeek(out byte digit) && IsDigit(digit))
        {
            sawDigit = true;
            Advance(ref reader);
        }

        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        if(reader.TryPeek(out byte dot) && dot == (byte)'.')
        {
            if(!reader.TryPeek(1, out byte afterDot))
            {
                if(!atFinalBuffer)
                {
                    return SparqlLexStatus.NeedMore;
                }
            }
            else if(IsDigit(afterDot))
            {
                sawDecimal = true;
                Advance(ref reader);

                while(reader.TryPeek(out byte fraction) && IsDigit(fraction))
                {
                    sawDigit = true;
                    Advance(ref reader);
                }

                if(reader.End && !atFinalBuffer)
                {
                    return SparqlLexStatus.NeedMore;
                }
            }
        }

        if(reader.TryPeek(out byte exponent) && (exponent == (byte)'e' || exponent == (byte)'E'))
        {
            if(reader.Remaining < 2 && !atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            sawExponent = true;
            Advance(ref reader);

            if(reader.TryPeek(out byte expSign) && (expSign == (byte)'+' || expSign == (byte)'-'))
            {
                Advance(ref reader);
            }

            int expStart = Offset(reader.Consumed);
            while(reader.TryPeek(out byte expDigit) && IsDigit(expDigit))
            {
                Advance(ref reader);
            }

            if(reader.End && !atFinalBuffer)
            {
                return SparqlLexStatus.NeedMore;
            }

            if(Offset(reader.Consumed) == expStart)
            {
                return Fail(
                    SparqlLexErrorCode.ExpectedExponentDigits,
                    SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
            }
        }

        if(!sawDigit)
        {
            return Fail(
                SparqlLexErrorCode.InvalidNumericLiteral,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column));
        }

        SparqlTokenKind kind = (sawExponent, sawDecimal) switch
        {
            (true, _) => SparqlTokenKind.DoubleLiteral,
            (false, true) => SparqlTokenKind.DecimalLiteral,
            (false, false) => SparqlTokenKind.IntegerLiteral
        };

        token = new SparqlToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return SparqlLexStatus.Complete;
    }

    private SparqlLexStatus LexIdentifierLike(ref SequenceReader<byte> reader, out SparqlToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;
        reader.TryPeek(out byte first);

        if(first == (byte)':')
        {
            return LexPrefixedNameStartingWithColon(ref reader, startByte, startLine, startColumn, startPosition, out token);
        }

        if(!IsPnCharsBase(first))
        {
            return Fail(
                SparqlLexErrorCode.UnexpectedByte,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                FormatByte(first));
        }

        Advance(ref reader);

        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;

        while(reader.TryPeek(out byte b) && (IsPnChars(b) || b == (byte)'.'))
        {
            Advance(ref reader);

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        ReadOnlySequence<byte> identifierSpan = reader.Sequence.Slice(startPosition, reader.Position);

        //A following ':' makes this a prefix (PNAME_NS) or prefixed name; otherwise classify it as a
        //reserved word.
        if(reader.TryPeek(out byte separator) && separator == (byte)':')
        {
            Advance(ref reader);

            return LexPrefixedNameOrNamespace(ref reader, startByte, startLine, startColumn, startPosition, out token);
        }

        return ClassifyIdentifier(identifierSpan, startByte, startLine, startColumn, out token);
    }

    private SparqlLexStatus ClassifyIdentifier(
        in ReadOnlySequence<byte> identifierSpan,
        int startByte,
        int startLine,
        int startColumn,
        out SparqlToken token)
    {
        token = default;

        //Reserved words are ASCII and bounded in length; copy short candidates to a stack buffer for
        //allocation-free classification. A longer identifier cannot be a reserved word.
        if(identifierSpan.Length <= SparqlKeywords.MaxReservedWordLength)
        {
            Span<byte> candidate = stackalloc byte[SparqlKeywords.MaxReservedWordLength];
            int length = (int)identifierSpan.Length;
            identifierSpan.CopyTo(candidate);

            if(SparqlKeywords.TryClassify(candidate[..length], out SparqlTokenKind kind, out ReadOnlySpan<byte> canonical))
            {
                token = new SparqlToken(
                    kind,
                    CaptureSpan(startByte, startByte + length, startLine, startColumn, line, column),
                    Pool.Intern(canonical));

                return SparqlLexStatus.Complete;
            }
        }

        return Fail(
            SparqlLexErrorCode.UnrecognisedIdentifier,
            CaptureSpan(startByte, startByte + (int)identifierSpan.Length, startLine, startColumn, line, column),
            DecodeForMessage(identifierSpan));
    }

    private SparqlLexStatus LexPrefixedNameStartingWithColon(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition startPosition,
        out SparqlToken token)
    {
        Advance(ref reader);

        return LexPrefixedNameOrNamespace(ref reader, startByte, startLine, startColumn, startPosition, out token);
    }

    private SparqlLexStatus LexPrefixedNameOrNamespace(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition startPosition,
        out SparqlToken token)
    {
        token = default;

        long localStartConsumed = reader.Consumed;
        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;
        bool firstByte = true;

        while(reader.TryPeek(out byte b))
        {
            bool accept = firstByte
                ? IsPnCharsUOrDigit(b) || b == (byte)':' || b == (byte)'%' || b == (byte)'\\'
                : IsPnChars(b) || b == (byte)':' || b == (byte)'%' || b == (byte)'\\' || b == (byte)'.';

            if(!accept)
            {
                break;
            }

            if(b == (byte)'\\')
            {
                if(!reader.TryPeek(1, out byte escaped))
                {
                    if(!atFinalBuffer)
                    {
                        return SparqlLexStatus.NeedMore;
                    }

                    return Fail(
                        SparqlLexErrorCode.TruncatedPrefixedNameEscape,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
                }

                //PN_LOCAL_ESC permits escaping only a fixed reserved set; a backslash before any other
                //character (for example '\:') is not a valid local-name escape. Codepoint escapes are
                //already decoded, so a surviving backslash is a literal PN_LOCAL_ESC introducer.
                if(!IsPnLocalEscChar(escaped))
                {
                    return Fail(
                        SparqlLexErrorCode.InvalidPrefixedNameEscape,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 2, line, column, column + 2),
                        FormatEscape(escaped));
                }

                AdvanceCount(ref reader, 2);
                firstByte = false;
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
                continue;
            }

            if(b == (byte)'%')
            {
                bool highAvailable = reader.TryPeek(1, out byte highHex);
                bool lowAvailable = reader.TryPeek(2, out byte lowHex);
                if((!highAvailable || !lowAvailable) && !atFinalBuffer)
                {
                    return SparqlLexStatus.NeedMore;
                }

                if(!highAvailable || !lowAvailable || !TryHexValue(highHex, out _) || !TryHexValue(lowHex, out _))
                {
                    return Fail(
                        SparqlLexErrorCode.MalformedPercentEscape,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 3, line, column, column + 3));
                }

                AdvanceCount(ref reader, 3);
                firstByte = false;
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
                continue;
            }

            Advance(ref reader);
            firstByte = false;

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        if(reader.End && !atFinalBuffer)
        {
            return SparqlLexStatus.NeedMore;
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        ReadOnlySequence<byte> fullSpan = reader.Sequence.Slice(startPosition, reader.Position);

        SparqlTokenKind kind = reader.Consumed == localStartConsumed
            ? SparqlTokenKind.PrefixNamespace
            : SparqlTokenKind.PrefixedName;

        token = new SparqlToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(fullSpan));

        return SparqlLexStatus.Complete;
    }

    private void SkipWhitespaceAndComments(ref SequenceReader<byte> reader)
    {
        while(reader.TryPeek(out byte b))
        {
            if(IsWhitespaceByte(b))
            {
                Advance(ref reader);
                continue;
            }

            if(b == (byte)'#')
            {
                while(reader.TryPeek(out byte commentByte) && commentByte != (byte)'\n' && commentByte != (byte)'\r')
                {
                    Advance(ref reader);
                }

                continue;
            }

            break;
        }
    }

    private void Advance(ref SequenceReader<byte> reader)
    {
        reader.TryRead(out _);

        //Line and column are the source coordinates of the new decoded position, taken from the decoder's
        //map (which already resolved CR/LF and the source columns spanned by any escape).
        int decodedPosition = (int)reader.Consumed;
        line = Decoder.SourceLineAt(decodedPosition);
        column = Decoder.SourceColumnAt(decodedPosition);
    }

    private void AdvanceCount(ref SequenceReader<byte> reader, int count)
    {
        for(int i = 0; i < count; i++)
        {
            Advance(ref reader);
        }
    }

    /// <summary>
    /// Records the pending lexical diagnostic, resynchronises past the offending bytes, and builds the
    /// <see cref="SparqlTokenKind.Error"/> token that stands in for them.
    /// </summary>
    /// <param name="reader">The reader, positioned where the error was detected; advanced to the next token boundary.</param>
    /// <returns>An <see cref="SparqlTokenKind.Error"/> token spanning from the failure to the resync boundary.</returns>
    private SparqlToken RecordErrorAndRecover(ref SequenceReader<byte> reader)
    {
        SparqlLexDiagnostic diagnostic = pendingDiagnostic;
        diagnostics.Add(diagnostic);

        RecoverToTokenBoundary(ref reader);

        SourceSpan span = new(
            diagnostic.Span.StartByte,
            Offset(reader.Consumed),
            diagnostic.Span.StartLine,
            diagnostic.Span.StartColumn,
            line,
            column);

        return new SparqlToken(SparqlTokenKind.Error, span, Pool.Intern(ReadOnlySpan<byte>.Empty));
    }

    private void RecoverToTokenBoundary(ref SequenceReader<byte> reader)
    {
        if(reader.TryPeek(out _))
        {
            Advance(ref reader);
        }

        while(reader.TryPeek(out byte b) && !IsWhitespaceByte(b))
        {
            Advance(ref reader);
        }
    }

    private void StripToLastSignificant(ref SequenceReader<byte> reader, long lastSignificantConsumed, int lastSignificantColumn)
    {
        long rewind = reader.Consumed - lastSignificantConsumed;
        if(rewind > 0)
        {
            reader.Rewind(rewind);
            column = lastSignificantColumn;
        }
    }

    private SparqlLexStatus Fail(SparqlLexErrorCode code, SourceSpan span, string? detail = null)
    {
        pendingDiagnostic = new SparqlLexDiagnostic(code, span, detail);

        return SparqlLexStatus.Error;
    }

    private int Offset(long consumedInReader)
    {
        //The reader walks the decoded byte stream; the decoder's map translates a decoded position back
        //to the originating source byte offset so spans report source coordinates.
        return Decoder.SourceOffsetAt((int)consumedInReader);
    }

    private static SourceSpan CaptureSpan(int startByte, int endByte, int startLine, int startColumn, int endLine, int endColumn)
    {
        return new SourceSpan(startByte, endByte, startLine, startColumn, endLine, endColumn);
    }

    private static string FormatByte(byte value)
    {
        return string.Concat("0x", value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string FormatEscape(byte marker)
    {
        return string.Concat("\\", ((char)marker).ToString());
    }

    private static bool IsWhitespaceByte(byte b)
    {
        return b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';
    }

    private static bool IsDigit(byte b)
    {
        return b >= (byte)'0' && b <= (byte)'9';
    }

    private static bool IsAsciiLetter(byte b)
    {
        return (b >= (byte)'a' && b <= (byte)'z') || (b >= (byte)'A' && b <= (byte)'Z');
    }

    private static bool IsAsciiLetterOrDigit(byte b)
    {
        return IsAsciiLetter(b) || IsDigit(b);
    }

    private static bool IsPnCharsBase(byte b)
    {
        //ASCII subset of PN_CHARS_BASE plus the lead byte of any non-ASCII UTF-8 sequence.
        return IsAsciiLetter(b) || b >= 0x80;
    }

    private static bool IsPnCharsUOrDigit(byte b)
    {
        return IsPnCharsBase(b) || b == (byte)'_' || IsDigit(b);
    }

    private static bool IsPnChars(byte b)
    {
        return IsPnCharsUOrDigit(b) || b == (byte)'-' || b == 0xB7;
    }

    private static bool IsPnLocalEscChar(byte b)
    {
        //PN_LOCAL_ESC ::= '\' ( '_' | '~' | '.' | '-' | '!' | '$' | '&' | "'" | '(' | ')' | '*' | '+'
        //                       | ',' | ';' | '=' | '/' | '?' | '#' | '@' | '%' )
        return b switch
        {
            (byte)'_' or (byte)'~' or (byte)'.' or (byte)'-' or (byte)'!' or (byte)'$' or (byte)'&'
                or (byte)'\'' or (byte)'(' or (byte)')' or (byte)'*' or (byte)'+' or (byte)',' or (byte)';'
                or (byte)'=' or (byte)'/' or (byte)'?' or (byte)'#' or (byte)'@' or (byte)'%' => true,
            _ => false
        };
    }

    private static bool IsVarNameStart(byte b)
    {
        //VARNAME first character: PN_CHARS_U | [0-9].
        return IsPnCharsBase(b) || b == (byte)'_' || IsDigit(b);
    }

    private static bool IsVarNameChar(byte b)
    {
        //VARNAME continuation adds #x00B7 and the combining ranges to the start set; all are non-ASCII
        //and so are already admitted by the b >= 0x80 lead-byte rule inside IsVarNameStart.
        return IsVarNameStart(b);
    }

    private static bool TryUtf8ByteLength(byte leadByte, out int length)
    {
        if(leadByte < 0x80)
        {
            length = 1;

            return true;
        }

        if((leadByte & 0xE0) == 0xC0)
        {
            length = 2;

            return true;
        }

        if((leadByte & 0xF0) == 0xE0)
        {
            length = 3;

            return true;
        }

        if((leadByte & 0xF8) == 0xF0)
        {
            length = 4;

            return true;
        }

        length = 0;

        return false;
    }

    private static bool TryHexValue(byte b, out uint value)
    {
        if(b >= (byte)'0' && b <= (byte)'9')
        {
            value = (uint)(b - (byte)'0');

            return true;
        }

        if(b >= (byte)'a' && b <= (byte)'f')
        {
            value = (uint)(b - (byte)'a' + 10);

            return true;
        }

        if(b >= (byte)'A' && b <= (byte)'F')
        {
            value = (uint)(b - (byte)'A' + 10);

            return true;
        }

        value = 0;

        return false;
    }

    private string DecodeForMessage(in ReadOnlySequence<byte> sequence)
    {
        if(sequence.IsSingleSegment)
        {
            return Encoding.UTF8.GetString(sequence.FirstSpan);
        }

        int length = (int)sequence.Length;
        using IMemoryOwner<byte> owner = Pool.RentScratch(length);
        Span<byte> buffer = owner.Memory.Span[..length];
        sequence.CopyTo(buffer);

        return Encoding.UTF8.GetString(buffer);
    }

    private void EnsureScratchCapacity(ref IMemoryOwner<byte> owner, int required)
    {
        if(required <= owner.Memory.Length)
        {
            return;
        }

        Limits.OnTokenGrowth(scratchToken with { ProposedByteLength = required });

        int newSize = Math.Max(required, owner.Memory.Length * 2);
        IMemoryOwner<byte> replacement = Pool.RentScratch(newSize);
        owner.Memory.Span.CopyTo(replacement.Memory.Span);
        owner.Dispose();
        owner = replacement;
    }

    /// <summary>
    /// Gets the current zero-based byte position of the lexer. Exposed for diagnostic and test code;
    /// the public iteration surface is <see cref="Tokenize"/>.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal int Position => (int)consumed;
}
