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

namespace Lumoin.Veritas.Turtle.Lexer;

/// <summary>
/// Tokenises UTF-8 Turtle and TriG input one token at a time.
/// </summary>
/// <remarks>
/// <para>
/// The lexer is iterative — no recursion. Its core is resumable: each step returns a
/// <see cref="LexStatus"/> rather than throwing or blocking, so the same code serves a synchronous
/// whole-buffer pass (<see cref="Tokenize"/>) and an asynchronous pull over a pipe. A
/// <see cref="LexStatus.NeedMore"/> asks the driver for more bytes; a <see cref="LexStatus.Error"/>
/// reports a recorded <see cref="LexDiagnostic"/> without unwinding the stack — the lexer then emits a
/// <see cref="TurtleTokenKind.Error"/> token over the offending bytes, resynchronises to the next token
/// boundary, and continues. Recovery is always on; the lexer never throws on malformed input.
/// </para>
/// <para>
/// Byte access is through <see cref="SequenceReader{T}"/> over a <see cref="ReadOnlySequence{T}"/>,
/// so a token that straddles two buffer segments is read without first gathering the whole source
/// into one contiguous block. A <see cref="ReadOnlyMemory{T}"/> source is wrapped as a
/// single-segment sequence.
/// </para>
/// <para>
/// Position tracking is byte-accurate. Line and column counts are zero-based; columns advance one
/// per byte rather than per Unicode code point because the project's pipeline works in UTF-8
/// throughout and editor surfaces convert on the boundary.
/// </para>
/// <para>
/// String interning is the caller's responsibility through the supplied <see cref="Utf8StringPool"/>.
/// The lexer interns every payload — IRI bytes, prefixed-name bytes, blank-node labels, language
/// tags, decoded string-literal values — so the parser receives stable <see cref="Utf8String"/>
/// handles that compare and hash without touching the underlying source memory.
/// </para>
/// </remarks>
public sealed class TurtleLexer
{
    private long consumed;
    private int line;
    private int column;
    private bool pendingCarriageReturn;
    private bool atFinalBuffer = true;
    private long byteOffsetBase;
    private LexDiagnostic pendingDiagnostic;
    private TokenGrowthContext scratchToken;
    private readonly List<TurtleToken> pendingTokens = [];
    private readonly List<LexDiagnostic> diagnostics = [];

    /// <summary>
    /// Initialises a new <see cref="TurtleLexer"/> for the supplied UTF-8 source bytes.
    /// </summary>
    /// <param name="source">The UTF-8 encoded source document.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="TurtleReaderLimits.Default"/>.</param>
    public TurtleLexer(ReadOnlyMemory<byte> source, Utf8StringPool pool, TurtleReaderLimits? limits = null)
        : this(new ReadOnlySequence<byte>(source), pool, limits)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TurtleLexer"/> for the pipe-driven path, where source bytes are
    /// supplied to <see cref="TokenizeAsync"/> rather than held up front.
    /// </summary>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="TurtleReaderLimits.Default"/>.</param>
    public TurtleLexer(Utf8StringPool pool, TurtleReaderLimits? limits = null)
        : this(ReadOnlySequence<byte>.Empty, pool, limits)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TurtleLexer"/> for the supplied UTF-8 source sequence.
    /// </summary>
    /// <param name="source">The UTF-8 encoded source document, possibly spanning segments.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="TurtleReaderLimits.Default"/>.</param>
    public TurtleLexer(ReadOnlySequence<byte> source, Utf8StringPool pool, TurtleReaderLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Source = source;
        Pool = pool;
        Limits = limits ?? TurtleReaderLimits.Default;
    }

    /// <summary>Gets the UTF-8 source bytes being lexed.</summary>
    private ReadOnlySequence<byte> Source { get; }

    /// <summary>Gets the pool used to intern token payloads.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Gets the resource limits applied while lexing.</summary>
    private TurtleReaderLimits Limits { get; }

    /// <summary>
    /// Iterates the source document producing tokens until end of input.
    /// </summary>
    /// <remarks>
    /// The iterator yields an <see cref="TurtleTokenKind.EndOfInput"/> token as its final element
    /// so the parser can drive its loop without checking the source position separately. Recovery is
    /// always on: a lexical error is recorded in <see cref="Diagnostics"/> and surfaces as a
    /// <see cref="TurtleTokenKind.Error"/> token rather than an exception.
    /// </remarks>
    /// <returns>An iterator over the source tokens.</returns>
    public IEnumerable<TurtleToken> Tokenize()
    {
        while(true)
        {
            TurtleToken token = LexNextToken();
            yield return token;

            if(token.Kind == TurtleTokenKind.EndOfInput)
            {
                yield break;
            }
        }
    }

    private TurtleToken LexNextToken()
    {
        //A SequenceReader is a ref struct and cannot live across a yield, so each token positions a
        //fresh reader at the running offset. For the single-segment whole-buffer feed this advance
        //is constant-time, and atFinalBuffer is true so NeedMore never arises.
        SequenceReader<byte> reader = new(Source);
        reader.Advance(consumed);

        LexStatus status = TryLexToken(ref reader, out TurtleToken token);

        if(status == LexStatus.Error)
        {
            token = RecordErrorAndRecover(ref reader);
        }

        consumed = reader.Consumed;

        return token;
    }

    /// <summary>
    /// Gets the lexical diagnostics recorded while tokenising, in source order — one entry for each
    /// <see cref="TurtleTokenKind.Error"/> token the lexer emits.
    /// </summary>
    /// <remarks>
    /// Recovery is always on: instead of throwing, the lexer records a <see cref="LexDiagnostic"/> here
    /// and emits a <see cref="TurtleTokenKind.Error"/> token spanning the offending bytes, then
    /// resynchronises and continues. A consumer bridges these to layer-stable
    /// <see cref="Lumoin.Veritas.Core.Diagnostics.Diagnostic"/> values via
    /// <see cref="TurtleLexDiagnosticBridge"/>.
    /// </remarks>
    public IReadOnlyList<LexDiagnostic> Diagnostics => diagnostics;

    /// <summary>
    /// Pulls UTF-8 bytes from a <see cref="PipeReader"/> and yields tokens as they complete, without
    /// buffering the whole document. Each lexical error is recorded in <see cref="Diagnostics"/> and
    /// surfaces as a <see cref="TurtleTokenKind.Error"/> token; the lexer resynchronises and continues
    /// rather than throwing.
    /// </summary>
    /// <remarks>
    /// Each <see cref="PipeReader.ReadAsync"/> is the genuine asynchronous suspension; between reads
    /// the resumable core lexes synchronously over the buffered <see cref="ReadOnlySequence{T}"/>.
    /// A token that straddles a read is retained — the reader advances only to the last completed
    /// token and examines to the buffer end — and re-lexed once more bytes arrive, so peak memory
    /// tracks the largest single token rather than the document. On an error the lexer records a
    /// <see cref="LexDiagnostic"/>, skips the offending run to the next whitespace, emits a
    /// <see cref="TurtleTokenKind.Error"/> token, and resumes — so an editor keeps tokens, highlighting,
    /// and completion past the fault and surfaces every problem at once instead of only the first.
    /// </remarks>
    /// <param name="pipeReader">The pipe delivering UTF-8 source bytes.</param>
    /// <param name="cancellationToken">Cancels between reads.</param>
    /// <returns>An asynchronous stream of tokens ending with <see cref="TurtleTokenKind.EndOfInput"/>.</returns>
    public IAsyncEnumerable<TurtleToken> TokenizeAsync(PipeReader pipeReader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeReader);

        return TokenizeInternalAsync(pipeReader, cancellationToken);
    }

    private async IAsyncEnumerable<TurtleToken> TokenizeInternalAsync(
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

            DrainResult drained = DrainBuffer(result.Buffer);

            foreach(TurtleToken token in pendingTokens)
            {
                yield return token;
            }

            pipeReader.AdvanceTo(drained.Consumed, drained.Examined);

            if(drained.ReachedEnd)
            {
                yield break;
            }
        }
    }

    private DrainResult DrainBuffer(ReadOnlySequence<byte> buffer)
    {
        pendingTokens.Clear();

        SequenceReader<byte> reader = new(buffer);

        int committedLine = line;
        int committedColumn = column;
        bool committedPending = pendingCarriageReturn;
        SequencePosition committedPosition = reader.Position;
        long committedConsumed = 0;

        while(true)
        {
            LexStatus status = TryLexToken(ref reader, out TurtleToken token);

            if(status == LexStatus.Complete)
            {
                pendingTokens.Add(token);

                committedLine = line;
                committedColumn = column;
                committedPending = pendingCarriageReturn;
                committedPosition = reader.Position;
                committedConsumed = reader.Consumed;

                if(token.Kind == TurtleTokenKind.EndOfInput)
                {
                    byteOffsetBase += committedConsumed;

                    return new DrainResult(committedPosition, committedPosition, reachedEnd: true);
                }

                continue;
            }

            if(status == LexStatus.Error)
            {
                //Record the fault, resynchronise past it, and emit an Error token standing in for the
                //offending bytes; commit the recovery position as the new boundary the driver may roll
                //back to.
                TurtleToken errorToken = RecordErrorAndRecover(ref reader);
                pendingTokens.Add(errorToken);

                committedLine = line;
                committedColumn = column;
                committedPending = pendingCarriageReturn;
                committedPosition = reader.Position;
                committedConsumed = reader.Consumed;

                continue;
            }

            //NeedMore: discard the partial token by restoring the committed boundary; the driver
            //reads more bytes and re-lexes from there. The examined position is the buffer end so
            //the pipe knows the whole buffer was inspected and must grow before the next read.
            line = committedLine;
            column = committedColumn;
            pendingCarriageReturn = committedPending;
            byteOffsetBase += committedConsumed;

            return new DrainResult(committedPosition, buffer.End, reachedEnd: false);
        }
    }

    /// <summary>
    /// Lexes one chunk for the synchronous incremental reader, reusing the resumable drain core the pipe path
    /// drives. The caller passes the unconsumed tail from the previous chunk followed by the new source bytes and
    /// re-presents the bytes past <paramref name="consumedBytes"/> with the next chunk, so a token that straddles a
    /// chunk boundary re-lexes whole when more bytes arrive.
    /// </summary>
    /// <param name="buffer">The unconsumed tail followed by the new source bytes.</param>
    /// <param name="isFinal">Whether no more bytes will follow, so a trailing construct closes and the end-of-input token is emitted.</param>
    /// <param name="consumedBytes">The number of leading bytes of <paramref name="buffer"/> that completed into tokens; the rest must be re-presented next call.</param>
    /// <returns>The tokens completed within this chunk, in source order; valid until the next call.</returns>
    internal IReadOnlyList<TurtleToken> FeedChunk(ReadOnlySequence<byte> buffer, bool isFinal, out long consumedBytes)
    {
        atFinalBuffer = isFinal;
        DrainResult drained = DrainBuffer(buffer);
        consumedBytes = buffer.GetOffset(drained.Consumed);

        return pendingTokens;
    }

    private LexStatus TryLexToken(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        SkipWhitespaceAndComments(ref reader);

        if(reader.End)
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            int offset = Offset(reader.Consumed);
            token = new TurtleToken(
                TurtleTokenKind.EndOfInput,
                CaptureSpan(offset, offset, line, column, line, column),
                Pool.Intern(ReadOnlySpan<byte>.Empty));

            return LexStatus.Complete;
        }

        return LexNext(ref reader, out token);
    }

    private LexStatus LexNext(ref SequenceReader<byte> reader, out TurtleToken token)
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
                return LexShortString(ref reader, (byte)'"', out token);
            }

            case (byte)'\'':
            {
                return LexShortString(ref reader, (byte)'\'', out token);
            }

            case (byte)'_':
            {
                return LexBlankNodeLabel(ref reader, out token);
            }

            case (byte)'[':
            {
                return LexOpenBracket(ref reader, out token);
            }

            case (byte)']':
            {
                return LexSinglePunctuation(ref reader, TurtleTokenKind.CloseBracket, out token);
            }

            case (byte)'(':
            {
                return LexSinglePunctuation(ref reader, TurtleTokenKind.OpenParen, out token);
            }

            case (byte)')':
            {
                return LexCloseParen(ref reader, out token);
            }

            case (byte)'{':
            {
                return LexOpenBrace(ref reader, out token);
            }

            case (byte)'}':
            {
                return LexSinglePunctuation(ref reader, TurtleTokenKind.CloseBrace, out token);
            }

            case (byte)'|':
            {
                return LexPipe(ref reader, out token);
            }

            case (byte)',':
            {
                return LexSinglePunctuation(ref reader, TurtleTokenKind.Comma, out token);
            }

            case (byte)';':
            {
                return LexSinglePunctuation(ref reader, TurtleTokenKind.Semicolon, out token);
            }

            case (byte)'.':
            {
                return LexPeriodOrDecimal(ref reader, out token);
            }

            case (byte)'~':
            {
                return LexSinglePunctuation(ref reader, TurtleTokenKind.Tilde, out token);
            }

            case (byte)'^':
            {
                return LexTypeMarker(ref reader, out token);
            }

            case (byte)'@':
            {
                return LexAtDirective(ref reader, out token);
            }

            case (byte)'+' or (byte)'-':
            {
                return LexNumericSigned(ref reader, out token);
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

    private LexStatus LexLessThan(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        //Distinguish '<' (IRI), '<<' (reified triple), '<<(' (triple term).
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }
        }
        else if(second == (byte)'<')
        {
            bool haveThird = reader.TryPeek(2, out byte third);
            if(!haveThird && !atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;

            if(haveThird && third == (byte)'(')
            {
                AdvanceCount(ref reader, 3);
                token = new TurtleToken(
                    TurtleTokenKind.OpenTripleTerm,
                    CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                    Pool.Intern("<<("u8));

                return LexStatus.Complete;
            }

            AdvanceCount(ref reader, 2);
            token = new TurtleToken(
                TurtleTokenKind.OpenReifiedTriple,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("<<"u8));

            return LexStatus.Complete;
        }

        return LexIri(ref reader, out token);
    }

    private LexStatus LexGreaterThan(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte angle))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }
        }
        else if(angle == (byte)'>')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new TurtleToken(
                TurtleTokenKind.CloseReifiedTriple,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(">>"u8));

            return LexStatus.Complete;
        }

        return Fail(
            TurtleLexErrorCode.UnexpectedGreaterThan,
            SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
    }

    private LexStatus LexOpenBrace(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte bar))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }
        }
        else if(bar == (byte)'|')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new TurtleToken(
                TurtleTokenKind.OpenAnnotation,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("{|"u8));

            return LexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.OpenBrace, out token);
    }

    private LexStatus LexPipe(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte brace))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }
        }
        else if(brace == (byte)'}')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new TurtleToken(
                TurtleTokenKind.CloseAnnotation,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("|}"u8));

            return LexStatus.Complete;
        }

        return Fail(
            TurtleLexErrorCode.UnexpectedPipe,
            SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
    }

    private LexStatus LexOpenBracket(ref SequenceReader<byte> reader, out TurtleToken token)
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
            //The whitespace run reached the buffer end; a ']' might still follow.
            return LexStatus.NeedMore;
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

            token = new TurtleToken(
                TurtleTokenKind.AnonymousBlankNode,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("[]"u8));

            return LexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.OpenBracket, out token);
    }

    private LexStatus LexCloseParen(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        //RDF 1.2 triple-term close is the three-byte sequence ')>>'.
        bool first = reader.TryPeek(1, out byte firstAngle);
        bool secondAvailable = reader.TryPeek(2, out byte secondAngle);
        if((!first || !secondAvailable) && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        if(first && firstAngle == (byte)'>' && secondAvailable && secondAngle == (byte)'>')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 3);

            token = new TurtleToken(
                TurtleTokenKind.CloseTripleTerm,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(")>>"u8));

            return LexStatus.Complete;
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.CloseParen, out token);
    }

    private LexStatus LexSinglePunctuation(ref SequenceReader<byte> reader, TurtleTokenKind kind, out TurtleToken token)
    {
        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        token = new TurtleToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return LexStatus.Complete;
    }

    private LexStatus LexPeriodOrDecimal(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        //A leading period followed by a digit begins a numeric literal: ".5", ".5e10".
        if(!reader.TryPeek(1, out byte next))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }
        }
        else if(IsDigit(next))
        {
            return LexNumericUnsigned(ref reader, out token);
        }

        return LexSinglePunctuation(ref reader, TurtleTokenKind.Period, out token);
    }

    private LexStatus LexTypeMarker(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte caret))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }
        }
        else if(caret == (byte)'^')
        {
            int startByte = Offset(reader.Consumed);
            int startLine = line;
            int startColumn = column;
            AdvanceCount(ref reader, 2);

            token = new TurtleToken(
                TurtleTokenKind.TypeMarker,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("^^"u8));

            return LexStatus.Complete;
        }

        return Fail(
            TurtleLexErrorCode.ExpectedTypeMarker,
            SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
    }

    private LexStatus LexAtDirective(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        SequencePosition identifierStartPosition = reader.Position;
        int identifierStartByte = Offset(reader.Consumed);
        while(reader.TryPeek(out byte letter) && IsAsciiLetter(letter))
        {
            Advance(ref reader);
        }

        //The keyword or language primary subtag may extend past the buffer end.
        if(reader.End && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        if(Offset(reader.Consumed) == identifierStartByte)
        {
            return Fail(
                TurtleLexErrorCode.ExpectedIdentifierAfterAt,
                SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
        }

        ReadOnlySequence<byte> identifier = reader.Sequence.Slice(identifierStartPosition, reader.Position);

        if(SequenceEquals(identifier, "prefix"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.PrefixKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("@prefix"u8));

            return LexStatus.Complete;
        }

        if(SequenceEquals(identifier, "base"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.BaseKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("@base"u8));

            return LexStatus.Complete;
        }

        if(SequenceEquals(identifier, "version"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.VersionKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("@version"u8));

            return LexStatus.Complete;
        }

        //Otherwise this is a language tag attached to the preceding literal: continue reading subtags and an optional direction.
        return LexLanguageTagBody(ref reader, startByte, startLine, startColumn, identifierStartPosition, out token);
    }

    private LexStatus LexLanguageTagBody(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition identifierStartPosition,
        out TurtleToken token)
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
                    return LexStatus.NeedMore;
                }

                if(Offset(reader.Consumed) == dirStart)
                {
                    return Fail(
                        TurtleLexErrorCode.ExpectedDirectionTag,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
                }

                break;
            }

            //A lone trailing '-' may be completed by a subtag in the next buffer.
            if(!reader.TryPeek(1, out _) && !atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            Advance(ref reader);
            int subtagStart = Offset(reader.Consumed);
            while(reader.TryPeek(out byte alnum) && IsAsciiLetterOrDigit(alnum))
            {
                Advance(ref reader);
            }

            if(reader.End && !atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            if(Offset(reader.Consumed) == subtagStart)
            {
                return Fail(
                    TurtleLexErrorCode.ExpectedLanguageSubtag,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
            }
        }

        //Another '-subtag' may follow in the next buffer.
        if(reader.End && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        Utf8String value = Pool.Intern(reader.Sequence.Slice(identifierStartPosition, reader.Position));
        TurtleTokenKind kind = sawDirection ? TurtleTokenKind.DirLangTag : TurtleTokenKind.LangTag;

        token = new TurtleToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            value);

        return LexStatus.Complete;
    }

    private LexStatus LexIri(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        scratchToken = new TokenGrowthContext(TurtleTokenKind.Iri, 0, startByte, startLine, startColumn);

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
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.UnterminatedIri,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == (byte)'>')
                {
                    Advance(ref reader);

                    token = new TurtleToken(
                        TurtleTokenKind.Iri,
                        CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                        Pool.Intern(scratch.Memory.Span[..written]));

                    return LexStatus.Complete;
                }

                if(b == (byte)'\\')
                {
                    int escapeStart = Offset(reader.Consumed);
                    int escapeLine = line;
                    int escapeColumn = column;

                    LexStatus escapeStatus = ReadUcharEscape(ref reader, escapeStart, escapeLine, escapeColumn, out uint decoded);
                    if(escapeStatus != LexStatus.Complete)
                    {
                        return escapeStatus;
                    }

                    written = AppendCodepoint(decoded, ref scratch, written);
                    continue;
                }

                if(b < 0x21 || b == (byte)'"' || b == (byte)'<' || b == (byte)'{' || b == (byte)'}' || b == (byte)'|' || b == (byte)'^' || b == (byte)'`')
                {
                    return Fail(
                        TurtleLexErrorCode.InvalidIriByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        TurtleLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                EnsureScratchCapacity(ref scratch, written + byteCount);

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.TruncatedUtf8,
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

    private LexStatus ReadUcharEscape(ref SequenceReader<byte> reader, int escapeStart, int escapeLine, int escapeColumn, out uint codepoint)
    {
        //Decodes a \uXXXX or \UXXXXXXXX escape to a full Unicode scalar value. Each escape stands
        //for one scalar value: surrogate code points (U+D800..U+DFFF) are rejected — Turtle does
        //not encode astral characters as \u surrogate pairs, only as a single \U escape — and code
        //points beyond U+10FFFF are rejected as out of range.
        codepoint = 0;

        if(!reader.TryPeek(1, out byte marker))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            return Fail(
                TurtleLexErrorCode.TruncatedEscape,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column));
        }

        int hexCount = marker switch
        {
            (byte)'u' => 4,
            (byte)'U' => 8,
            _ => -1
        };

        if(hexCount < 0)
        {
            return Fail(
                TurtleLexErrorCode.InvalidEscape,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed) + 2, escapeLine, escapeColumn, column + 2),
                FormatEscape(marker));
        }

        if(reader.Remaining < 2 + hexCount)
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            return Fail(
                TurtleLexErrorCode.TruncatedEscape,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Length), escapeLine, escapeColumn, column));
        }

        AdvanceCount(ref reader, 2);

        for(int i = 0; i < hexCount; i++)
        {
            reader.TryPeek(out byte hex);
            if(!TryHexValue(hex, out uint digit))
            {
                return Fail(
                    TurtleLexErrorCode.InvalidHexDigit,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                    ((char)hex).ToString());
            }

            codepoint = (codepoint << 4) | digit;
            Advance(ref reader);
        }

        if(codepoint >= (uint)UnicodeConstants.SurrogateRangeFirst && codepoint <= (uint)UnicodeConstants.SurrogateRangeLast)
        {
            return Fail(
                TurtleLexErrorCode.SurrogateCodePoint,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column),
                FormatCodePoint(codepoint));
        }

        if(codepoint > (uint)UnicodeConstants.MaximumCodePoint)
        {
            return Fail(
                TurtleLexErrorCode.CodePointOutOfRange,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column),
                FormatCodePoint(codepoint));
        }

        return LexStatus.Complete;
    }

    private LexStatus LexShortString(ref SequenceReader<byte> reader, byte quote, out TurtleToken token)
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
                return LexStatus.NeedMore;
            }
        }
        else if(secondQuote == quote)
        {
            if(!reader.TryPeek(2, out byte thirdQuote))
            {
                if(!atFinalBuffer)
                {
                    return LexStatus.NeedMore;
                }
            }
            else if(thirdQuote == quote)
            {
                return LexLongString(ref reader, quote, out token);
            }
        }

        scratchToken = new TokenGrowthContext(TurtleTokenKind.StringLiteral, 0, startByte, startLine, startColumn);

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
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.UnterminatedString,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == quote)
                {
                    Advance(ref reader);

                    token = new TurtleToken(
                        TurtleTokenKind.StringLiteral,
                        CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                        Pool.Intern(scratch.Memory.Span[..written]));

                    return LexStatus.Complete;
                }

                if(b == (byte)'\n' || b == (byte)'\r')
                {
                    return Fail(
                        TurtleLexErrorCode.UnescapedLineBreak,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
                }

                if(b == (byte)'\\')
                {
                    LexStatus escapeStatus = DecodeEscape(ref reader, ref scratch, ref written);
                    if(escapeStatus != LexStatus.Complete)
                    {
                        return escapeStatus;
                    }

                    continue;
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        TurtleLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.TruncatedUtf8,
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

    private LexStatus LexLongString(ref SequenceReader<byte> reader, byte quote, out TurtleToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        scratchToken = new TokenGrowthContext(TurtleTokenKind.LongStringLiteral, 0, startByte, startLine, startColumn);

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
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.UnterminatedLongString,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == quote)
                {
                    //A closing triple needs two more quotes; without them in this buffer the bytes
                    //may be content or the close, so wait for more before deciding.
                    if(!reader.TryPeek(1, out byte secondQuote))
                    {
                        if(!atFinalBuffer)
                        {
                            return LexStatus.NeedMore;
                        }
                    }
                    else if(secondQuote == quote)
                    {
                        if(!reader.TryPeek(2, out byte thirdQuote))
                        {
                            if(!atFinalBuffer)
                            {
                                return LexStatus.NeedMore;
                            }
                        }
                        else if(thirdQuote == quote)
                        {
                            AdvanceCount(ref reader, 3);

                            token = new TurtleToken(
                                TurtleTokenKind.LongStringLiteral,
                                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                                Pool.Intern(scratch.Memory.Span[..written]));

                            return LexStatus.Complete;
                        }
                    }
                }

                if(b == (byte)'\\')
                {
                    LexStatus escapeStatus = DecodeEscape(ref reader, ref scratch, ref written);
                    if(escapeStatus != LexStatus.Complete)
                    {
                        return escapeStatus;
                    }

                    continue;
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        TurtleLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.TruncatedUtf8,
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

    private LexStatus DecodeEscape(ref SequenceReader<byte> reader, ref IMemoryOwner<byte> owner, ref int written)
    {
        if(!reader.TryPeek(1, out byte marker))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            return Fail(
                TurtleLexErrorCode.TruncatedEscape,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
        }

        switch(marker)
        {
            case (byte)'t':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\t';
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'b':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = 0x08;
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'n':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\n';
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'r':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\r';
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'f':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = 0x0C;
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'"':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'"';
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'\'':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\'';
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'\\':
            {
                EnsureScratchCapacity(ref owner, written + 1);
                owner.Memory.Span[written++] = (byte)'\\';
                AdvanceCount(ref reader, 2);

                return LexStatus.Complete;
            }

            case (byte)'u':
            case (byte)'U':
            {
                int escapeStart = Offset(reader.Consumed);
                int escapeLine = line;
                int escapeColumn = column;

                LexStatus status = ReadUcharEscape(ref reader, escapeStart, escapeLine, escapeColumn, out uint decoded);
                if(status != LexStatus.Complete)
                {
                    return status;
                }

                written = AppendCodepoint(decoded, ref owner, written);

                return LexStatus.Complete;
            }

            default:
            {
                return Fail(
                    TurtleLexErrorCode.InvalidEscape,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 2, line, column, column + 2),
                    FormatEscape(marker));
            }
        }
    }

    private int AppendCodepoint(uint codepoint, ref IMemoryOwner<byte> owner, int written)
    {
        Rune rune = new((int)codepoint);
        EnsureScratchCapacity(ref owner, written + rune.Utf8SequenceLength);
        int encoded = rune.EncodeToUtf8(owner.Memory.Span[written..]);

        return written + encoded;
    }

    private LexStatus LexBlankNodeLabel(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        if(!reader.TryPeek(1, out byte colon))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            return Fail(
                TurtleLexErrorCode.ExpectedColonAfterUnderscore,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        if(colon != (byte)':')
        {
            return Fail(
                TurtleLexErrorCode.ExpectedColonAfterUnderscore,
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
                return LexStatus.NeedMore;
            }

            return Fail(
                TurtleLexErrorCode.ExpectedBlankNodeLabel,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        if(!IsPnCharsUOrDigit(first))
        {
            return Fail(
                TurtleLexErrorCode.ExpectedBlankNodeLabel,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
        }

        Advance(ref reader);

        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;

        //Greedy scan; a trailing '.' that is part of the statement terminator is stripped after the loop.
        while(reader.TryPeek(out byte b) && (IsPnChars(b) || b == (byte)'.'))
        {
            Advance(ref reader);

            if(b != (byte)'.')
            {
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
            }
        }

        //The label, or a trailing '.' whose role depends on the next byte, may extend.
        if(reader.End && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        token = new TurtleToken(
            TurtleTokenKind.BlankNodeLabel,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(labelStartPosition, reader.Position)));

        return LexStatus.Complete;
    }

    private LexStatus LexIdentifierLike(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        //Identifier-like tokens include: prefixed names (with or without an explicit prefix),
        //the rdf:type shorthand 'a', the boolean keywords, the SPARQL-style PREFIX / BASE / VERSION
        //keywords, and TriG's GRAPH keyword.
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
                TurtleLexErrorCode.UnexpectedByte,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                FormatByte(first));
        }

        //Scan a PN_PREFIX-like identifier.
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

        //The identifier, the ':' that would make it a prefix, or a trailing '.' may extend.
        if(reader.End && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        ReadOnlySequence<byte> identifierSpan = reader.Sequence.Slice(startPosition, reader.Position);

        //Distinguish a prefix declaration vs. a prefixed-name vs. a keyword by what follows.
        if(reader.TryPeek(out byte separator) && separator == (byte)':')
        {
            Advance(ref reader);

            return LexPrefixedNameOrNamespace(ref reader, startByte, startLine, startColumn, startPosition, out token);
        }

        if(SequenceEquals(identifierSpan, "a"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.A,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("a"u8));

            return LexStatus.Complete;
        }

        if(SequenceEquals(identifierSpan, "true"u8) || SequenceEquals(identifierSpan, "false"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.BooleanLiteral,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(identifierSpan));

            return LexStatus.Complete;
        }

        if(SequenceEqualsIgnoreAsciiCase(identifierSpan, "PREFIX"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.PrefixKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("PREFIX"u8));

            return LexStatus.Complete;
        }

        if(SequenceEqualsIgnoreAsciiCase(identifierSpan, "BASE"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.BaseKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("BASE"u8));

            return LexStatus.Complete;
        }

        if(SequenceEqualsIgnoreAsciiCase(identifierSpan, "VERSION"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.VersionKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("VERSION"u8));

            return LexStatus.Complete;
        }

        if(SequenceEquals(identifierSpan, "GRAPH"u8))
        {
            token = new TurtleToken(
                TurtleTokenKind.GraphKeyword,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("GRAPH"u8));

            return LexStatus.Complete;
        }

        return Fail(
            TurtleLexErrorCode.UnrecognisedIdentifier,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            DecodeForMessage(identifierSpan));
    }

    private LexStatus LexPrefixedNameStartingWithColon(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition startPosition,
        out TurtleToken token)
    {
        Advance(ref reader);

        return LexPrefixedNameOrNamespace(ref reader, startByte, startLine, startColumn, startPosition, out token);
    }

    private LexStatus LexPrefixedNameOrNamespace(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition startPosition,
        out TurtleToken token)
    {
        token = default;

        long localStartConsumed = reader.Consumed;
        long lastSignificantConsumed = reader.Consumed;
        int lastSignificantColumn = column;
        bool firstByte = true;

        while(reader.TryPeek(out byte b))
        {
            bool accept;

            if(firstByte)
            {
                accept = IsPnCharsUOrDigit(b) || b == (byte)':' || b == (byte)'%' || b == (byte)'\\';
            }
            else
            {
                accept = IsPnChars(b) || b == (byte)':' || b == (byte)'%' || b == (byte)'\\' || b == (byte)'.';
            }

            if(!accept)
            {
                break;
            }

            //Handle reserved-character escapes used inside PN_LOCAL.
            if(b == (byte)'\\')
            {
                if(!reader.TryPeek(1, out _))
                {
                    if(!atFinalBuffer)
                    {
                        return LexStatus.NeedMore;
                    }

                    return Fail(
                        TurtleLexErrorCode.TruncatedPrefixedNameEscape,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
                }

                AdvanceCount(ref reader, 2);
                firstByte = false;
                lastSignificantConsumed = reader.Consumed;
                lastSignificantColumn = column;
                continue;
            }

            if(b == (byte)'%')
            {
                //Percent-encoded byte: '%' HEX HEX.
                bool highAvailable = reader.TryPeek(1, out byte highHex);
                bool lowAvailable = reader.TryPeek(2, out byte lowHex);
                if((!highAvailable || !lowAvailable) && !atFinalBuffer)
                {
                    return LexStatus.NeedMore;
                }

                if(!highAvailable || !lowAvailable || !TryHexValue(highHex, out _) || !TryHexValue(lowHex, out _))
                {
                    return Fail(
                        TurtleLexErrorCode.MalformedPercentEscape,
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

        //The local name, or a trailing '.' whose role depends on the next byte, may extend.
        if(reader.End && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        //A trailing '.' is part of the statement terminator, not the local name.
        StripToLastSignificant(ref reader, lastSignificantConsumed, lastSignificantColumn);

        ReadOnlySequence<byte> fullSpan = reader.Sequence.Slice(startPosition, reader.Position);

        //If nothing followed the colon, this is a PNAME_NS (prefix declaration form).
        TurtleTokenKind kind = reader.Consumed == localStartConsumed
            ? TurtleTokenKind.PrefixNamespace
            : TurtleTokenKind.PrefixedName;

        token = new TurtleToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(fullSpan));

        return LexStatus.Complete;
    }

    private LexStatus LexNumericSigned(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        reader.TryPeek(out byte sign);
        Advance(ref reader);

        if(!reader.TryPeek(out byte after))
        {
            if(!atFinalBuffer)
            {
                return LexStatus.NeedMore;
            }

            return Fail(
                TurtleLexErrorCode.ExpectedDigit,
                SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column),
                ((char)sign).ToString());
        }

        if(!IsDigit(after) && after != (byte)'.')
        {
            return Fail(
                TurtleLexErrorCode.ExpectedDigit,
                SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column),
                ((char)sign).ToString());
        }

        return ContinueNumeric(ref reader, startByte, startLine, startColumn, out token);
    }

    private LexStatus LexNumericUnsigned(ref SequenceReader<byte> reader, out TurtleToken token)
    {
        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        return ContinueNumeric(ref reader, startByte, startLine, startColumn, out token);
    }

    private LexStatus ContinueNumeric(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        out TurtleToken token)
    {
        token = default;

        SequencePosition startPosition = reader.Sequence.GetPosition(startByte - (int)byteOffsetBase);
        bool sawDecimal = false;
        bool sawExponent = false;
        bool sawDigit = false;

        while(reader.TryPeek(out byte digit) && IsDigit(digit))
        {
            sawDigit = true;
            Advance(ref reader);
        }

        //More digits, a fraction, or an exponent may follow in the next buffer.
        if(reader.End && !atFinalBuffer)
        {
            return LexStatus.NeedMore;
        }

        if(reader.TryPeek(out byte dot) && dot == (byte)'.')
        {
            //A '.' that does not precede a digit belongs to the statement terminator.
            if(!reader.TryPeek(1, out byte afterDot))
            {
                if(!atFinalBuffer)
                {
                    return LexStatus.NeedMore;
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
                    return LexStatus.NeedMore;
                }
            }
        }

        if(reader.TryPeek(out byte exponent) && (exponent == (byte)'e' || exponent == (byte)'E'))
        {
            //The exponent's sign and digits may extend past the buffer end.
            if(reader.Remaining < 2 && !atFinalBuffer)
            {
                return LexStatus.NeedMore;
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
                return LexStatus.NeedMore;
            }

            if(Offset(reader.Consumed) == expStart)
            {
                return Fail(
                    TurtleLexErrorCode.ExpectedExponentDigits,
                    SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
            }
        }

        if(!sawDigit)
        {
            return Fail(
                TurtleLexErrorCode.InvalidNumericLiteral,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column));
        }

        TurtleTokenKind kind = (sawExponent, sawDecimal) switch
        {
            (true, _) => TurtleTokenKind.DoubleLiteral,
            (false, true) => TurtleTokenKind.DecimalLiteral,
            (false, false) => TurtleTokenKind.IntegerLiteral
        };

        token = new TurtleToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return LexStatus.Complete;
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
        reader.TryRead(out byte b);

        if(b == (byte)'\n')
        {
            //An LF directly after a CR completes a CR-LF pair the CR already counted as one
            //newline; a standalone LF opens its own line. The byte is consumed here either way, so
            //literal content keeps every raw byte rather than folding CR-LF down to a lone CR.
            if(pendingCarriageReturn)
            {
                pendingCarriageReturn = false;
            }
            else
            {
                line++;
                column = 0;
            }
        }
        else if(b == (byte)'\r')
        {
            //CR opens a new line on its own; an immediately following LF folds into the same newline.
            line++;
            column = 0;
            pendingCarriageReturn = true;
        }
        else
        {
            pendingCarriageReturn = false;
            column++;
        }
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
    /// <see cref="TurtleTokenKind.Error"/> token that stands in for them.
    /// </summary>
    /// <param name="reader">The reader, positioned where the error was detected; advanced to the next token boundary.</param>
    /// <returns>A <see cref="TurtleTokenKind.Error"/> token spanning from the failure to the resync boundary.</returns>
    private TurtleToken RecordErrorAndRecover(ref SequenceReader<byte> reader)
    {
        LexDiagnostic diagnostic = pendingDiagnostic;
        diagnostics.Add(diagnostic);

        RecoverToTokenBoundary(ref reader);

        SourceSpan span = new(
            diagnostic.Span.StartByte,
            Offset(reader.Consumed),
            diagnostic.Span.StartLine,
            diagnostic.Span.StartColumn,
            line,
            column);

        return new TurtleToken(TurtleTokenKind.Error, span, Pool.Intern(ReadOnlySpan<byte>.Empty));
    }

    private void RecoverToTokenBoundary(ref SequenceReader<byte> reader)
    {
        //Always consume at least the offending byte to guarantee forward progress (the fault may sit
        //on a line break), then skip the rest of the run up to the next whitespace so the next token
        //starts cleanly.
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
        //A run of trailing '.' bytes belongs to the statement terminator, not the token; rewind to
        //the byte after the last non-'.' the scan accepted.
        long rewind = reader.Consumed - lastSignificantConsumed;
        if(rewind > 0)
        {
            reader.Rewind(rewind);
            column = lastSignificantColumn;
        }
    }

    private LexStatus Fail(TurtleLexErrorCode code, SourceSpan span, string? detail = null)
    {
        pendingDiagnostic = new LexDiagnostic(code, span, detail);

        return LexStatus.Error;
    }

    private int Offset(long consumedInReader)
    {
        return (int)(byteOffsetBase + consumedInReader);
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

    private static string FormatCodePoint(uint codepoint)
    {
        return string.Concat("U+", codepoint.ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
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
        //Full Unicode validation is deferred to consumers that need exact range checks.
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

    private static byte ToAsciiLower(byte b)
    {
        return b >= (byte)'A' && b <= (byte)'Z' ? (byte)(b + ('a' - 'A')) : b;
    }

    private static bool SequenceEquals(in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> value)
    {
        if(sequence.Length != value.Length)
        {
            return false;
        }

        if(sequence.IsSingleSegment)
        {
            return sequence.FirstSpan.SequenceEqual(value);
        }

        int offset = 0;
        SequencePosition position = sequence.Start;
        while(sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            ReadOnlySpan<byte> segment = memory.Span;
            if(!segment.SequenceEqual(value.Slice(offset, segment.Length)))
            {
                return false;
            }

            offset += segment.Length;
        }

        return true;
    }

    private static bool SequenceEqualsIgnoreAsciiCase(in ReadOnlySequence<byte> sequence, ReadOnlySpan<byte> value)
    {
        if(sequence.Length != value.Length)
        {
            return false;
        }

        int offset = 0;
        SequencePosition position = sequence.Start;
        while(sequence.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            ReadOnlySpan<byte> segment = memory.Span;
            for(int i = 0; i < segment.Length; i++)
            {
                if(ToAsciiLower(segment[i]) != ToAsciiLower(value[offset + i]))
                {
                    return false;
                }
            }

            offset += segment.Length;
        }

        return true;
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

        //The buffer must grow to hold a longer token; let the limits policy reject it before a
        //larger buffer is allocated, bounding the contiguous memory any one token can force.
        Limits.OnTokenGrowth(scratchToken with { ProposedByteLength = required });

        int newSize = Math.Max(required, owner.Memory.Length * 2);
        IMemoryOwner<byte> replacement = Pool.RentScratch(newSize);
        owner.Memory.Span.CopyTo(replacement.Memory.Span);
        owner.Dispose();
        owner = replacement;
    }

    /// <summary>
    /// Gets the current zero-based byte position of the lexer. Exposed for diagnostic and test
    /// code; the public iteration surface is <see cref="Tokenize"/>.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal int Position => (int)consumed;

    /// <summary>
    /// The result of draining one pipe buffer: where the pipe should resume (the last completed
    /// token boundary), how far it was examined (the buffer end), and whether end of input was
    /// reached.
    /// </summary>
    private readonly struct DrainResult(SequencePosition consumed, SequencePosition examined, bool reachedEnd)
    {
        public SequencePosition Consumed { get; } = consumed;

        public SequencePosition Examined { get; } = examined;

        public bool ReachedEnd { get; } = reachedEnd;
    }
}
