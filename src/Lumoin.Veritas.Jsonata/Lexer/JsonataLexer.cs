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

namespace Lumoin.Veritas.Jsonata.Lexer;

/// <summary>
/// Tokenises UTF-8 JSONata expression text one token at a time.
/// </summary>
/// <remarks>
/// <para>
/// The lexer is iterative — no recursion. Its core is resumable: each step returns a
/// <see cref="JsonataLexStatus"/> rather than throwing or blocking, so the same code serves a
/// synchronous whole-buffer pass (<see cref="Tokenize"/>) and an asynchronous pull over a pipe. A
/// <see cref="JsonataLexStatus.NeedMore"/> asks the driver for more bytes; a
/// <see cref="JsonataLexStatus.Error"/> reports a recorded <see cref="JsonataLexDiagnostic"/> without
/// unwinding the stack — the lexer then emits a <see cref="JsonataTokenKind.Error"/> token over the
/// offending bytes, resynchronises to the next token boundary, and continues. Recovery is always on;
/// the lexer never throws on malformed input.
/// </para>
/// <para>
/// Byte access is through <see cref="SequenceReader{T}"/> over a <see cref="ReadOnlySequence{T}"/>,
/// so a token that straddles two buffer segments is read without first gathering the whole source
/// into one contiguous block. A <see cref="ReadOnlyMemory{T}"/> source is wrapped as a single-segment
/// sequence.
/// </para>
/// <para>
/// Position tracking is byte-accurate. Line and column counts are zero-based; columns advance one per
/// byte rather than per Unicode code point because the project's pipeline works in UTF-8 throughout
/// and editor surfaces convert on the boundary.
/// </para>
/// <para>
/// String interning is the caller's responsibility through the supplied <see cref="Utf8StringPool"/>.
/// The lexer interns every payload — decoded string values, variable names, field names, numeric
/// lexemes — so the parser receives stable <see cref="Utf8String"/> handles that compare and hash
/// without touching the underlying source memory.
/// </para>
/// </remarks>
public sealed class JsonataLexer
{
    private long consumed;
    private int line;
    private int column;
    private bool pendingCarriageReturn;
    private bool atFinalBuffer = true;
    private long byteOffsetBase;
    private bool previousTokenEndsValue;
    private JsonataLexDiagnostic pendingDiagnostic;
    private JsonataTokenGrowthContext scratchToken;
    private readonly List<JsonataToken> pendingTokens = [];
    private readonly List<JsonataLexDiagnostic> diagnostics = [];

    /// <summary>
    /// Initialises a new <see cref="JsonataLexer"/> for the supplied UTF-8 source bytes.
    /// </summary>
    /// <param name="source">The UTF-8 encoded expression text.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="JsonataReaderLimits.Default"/>.</param>
    public JsonataLexer(ReadOnlyMemory<byte> source, Utf8StringPool pool, JsonataReaderLimits? limits = null)
        : this(new ReadOnlySequence<byte>(source), pool, limits)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="JsonataLexer"/> for the pipe-driven path, where source bytes are
    /// supplied to <see cref="TokenizeAsync"/> rather than held up front.
    /// </summary>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="JsonataReaderLimits.Default"/>.</param>
    public JsonataLexer(Utf8StringPool pool, JsonataReaderLimits? limits = null)
        : this(ReadOnlySequence<byte>.Empty, pool, limits)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="JsonataLexer"/> for the supplied UTF-8 source sequence.
    /// </summary>
    /// <param name="source">The UTF-8 encoded expression text, possibly spanning segments.</param>
    /// <param name="pool">The pool used to intern token payloads.</param>
    /// <param name="limits">Resource limits applied while lexing; defaults to <see cref="JsonataReaderLimits.Default"/>.</param>
    public JsonataLexer(ReadOnlySequence<byte> source, Utf8StringPool pool, JsonataReaderLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Source = source;
        Pool = pool;
        Limits = limits ?? JsonataReaderLimits.Default;
    }

    /// <summary>Gets the UTF-8 source bytes being lexed.</summary>
    private ReadOnlySequence<byte> Source { get; }

    /// <summary>Gets the pool used to intern token payloads.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Gets the resource limits applied while lexing.</summary>
    private JsonataReaderLimits Limits { get; }

    /// <summary>
    /// Gets the lexical diagnostics recorded while tokenising, in source order — one entry for each
    /// <see cref="JsonataTokenKind.Error"/> token the lexer emits.
    /// </summary>
    /// <remarks>
    /// Recovery is always on: instead of throwing, the lexer records a <see cref="JsonataLexDiagnostic"/>
    /// here and emits a <see cref="JsonataTokenKind.Error"/> token spanning the offending bytes, then
    /// resynchronises and continues. A consumer bridges these to layer-stable
    /// <see cref="Lumoin.Veritas.Core.Diagnostics.Diagnostic"/> values via
    /// <see cref="JsonataLexDiagnosticBridge"/>.
    /// </remarks>
    public IReadOnlyList<JsonataLexDiagnostic> Diagnostics => diagnostics;

    /// <summary>
    /// Iterates the expression text producing tokens until end of input.
    /// </summary>
    /// <remarks>
    /// The iterator yields a <see cref="JsonataTokenKind.EndOfInput"/> token as its final element so
    /// the parser can drive its loop without checking the source position separately. Recovery is
    /// always on: a lexical error is recorded in <see cref="Diagnostics"/> and surfaces as a
    /// <see cref="JsonataTokenKind.Error"/> token rather than an exception.
    /// </remarks>
    /// <returns>An iterator over the source tokens.</returns>
    public IEnumerable<JsonataToken> Tokenize()
    {
        while(true)
        {
            JsonataToken token = LexNextToken();
            yield return token;

            if(token.Kind == JsonataTokenKind.EndOfInput)
            {
                yield break;
            }
        }
    }

    private JsonataToken LexNextToken()
    {
        //A SequenceReader is a ref struct and cannot live across a yield, so each token positions a
        //fresh reader at the running offset. For the single-segment whole-buffer feed this advance
        //is constant-time, and atFinalBuffer is true so NeedMore never arises.
        SequenceReader<byte> reader = new(Source);
        reader.Advance(consumed);

        JsonataLexStatus status = TryLexToken(ref reader, out JsonataToken token);

        if(status == JsonataLexStatus.Error)
        {
            token = RecordErrorAndRecover(ref reader);
        }

        consumed = reader.Consumed;
        UpdatePreviousToken(token.Kind);

        return token;
    }

    /// <summary>
    /// Updates the "previous significant token ends a value" flag the <c>/</c> dispatch consults to choose
    /// between a regular-expression literal (the previous token expects an operand, so <c>/</c> is a prefix)
    /// and the divide operator (the previous token ends a value, so <c>/</c> is an infix). An
    /// <see cref="JsonataTokenKind.EndOfInput"/> or <see cref="JsonataTokenKind.Error"/> token leaves the flag
    /// unchanged, so a recovered error does not flip the value/operand position the next token sees.
    /// </summary>
    /// <param name="kind">The kind of the token just produced.</param>
    private void UpdatePreviousToken(JsonataTokenKind kind)
    {
        if(kind is JsonataTokenKind.EndOfInput or JsonataTokenKind.Error)
        {
            return;
        }

        previousTokenEndsValue = EndsValue(kind);
    }

    /// <summary>
    /// Determines whether a token kind ends a value — a literal, a name, a variable, a regex literal, or a
    /// closing bracket/brace/paren. After such a token a <c>/</c> is the divide operator; after every other
    /// token (an operator, an opening bracket, or at start of input) a <c>/</c> begins a regular-expression
    /// literal. The reserved words <c>true</c>/<c>false</c>/<c>null</c> lex as <see cref="JsonataTokenKind.Name"/>,
    /// so they end a value too.
    /// </summary>
    /// <param name="kind">The token kind to classify.</param>
    /// <returns><see langword="true"/> when the kind ends a value (a divide follows), otherwise <see langword="false"/> (a regex follows).</returns>
    private static bool EndsValue(JsonataTokenKind kind)
    {
        return kind is JsonataTokenKind.Number
            or JsonataTokenKind.String
            or JsonataTokenKind.Name
            or JsonataTokenKind.BacktickName
            or JsonataTokenKind.Variable
            or JsonataTokenKind.RegexLiteral
            or JsonataTokenKind.CloseParen
            or JsonataTokenKind.CloseBracket
            or JsonataTokenKind.CloseBrace;
    }

    /// <summary>
    /// Pulls UTF-8 bytes from a <see cref="PipeReader"/> and yields tokens as they complete, without
    /// buffering the whole document. Each lexical error is recorded in <see cref="Diagnostics"/> and
    /// surfaces as a <see cref="JsonataTokenKind.Error"/> token; the lexer resynchronises and continues
    /// rather than throwing.
    /// </summary>
    /// <remarks>
    /// Each <see cref="PipeReader.ReadAsync"/> is the genuine asynchronous suspension; between reads the
    /// resumable core lexes synchronously over the buffered <see cref="ReadOnlySequence{T}"/>. A token
    /// that straddles a read is retained — the reader advances only to the last completed token and
    /// examines to the buffer end — and re-lexed once more bytes arrive, so peak memory tracks the
    /// largest single token rather than the document.
    /// </remarks>
    /// <param name="pipeReader">The pipe delivering UTF-8 source bytes.</param>
    /// <param name="cancellationToken">Cancels between reads.</param>
    /// <returns>An asynchronous stream of tokens ending with <see cref="JsonataTokenKind.EndOfInput"/>.</returns>
    public IAsyncEnumerable<JsonataToken> TokenizeAsync(PipeReader pipeReader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipeReader);

        return TokenizeInternalAsync(pipeReader, cancellationToken);
    }

    private async IAsyncEnumerable<JsonataToken> TokenizeInternalAsync(
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

            foreach(JsonataToken token in pendingTokens)
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
        bool committedPreviousEndsValue = previousTokenEndsValue;
        SequencePosition committedPosition = reader.Position;
        long committedConsumed = 0;

        while(true)
        {
            JsonataLexStatus status = TryLexToken(ref reader, out JsonataToken token);

            if(status == JsonataLexStatus.Complete)
            {
                pendingTokens.Add(token);
                UpdatePreviousToken(token.Kind);

                committedLine = line;
                committedColumn = column;
                committedPending = pendingCarriageReturn;
                committedPreviousEndsValue = previousTokenEndsValue;
                committedPosition = reader.Position;
                committedConsumed = reader.Consumed;

                if(token.Kind == JsonataTokenKind.EndOfInput)
                {
                    byteOffsetBase += committedConsumed;

                    return new DrainResult(committedPosition, committedPosition, reachedEnd: true);
                }

                continue;
            }

            if(status == JsonataLexStatus.Error)
            {
                //Record the fault, resynchronise past it, and emit an Error token standing in for the
                //offending bytes; commit the recovery position as the new boundary the driver may roll
                //back to.
                JsonataToken errorToken = RecordErrorAndRecover(ref reader);
                pendingTokens.Add(errorToken);

                committedLine = line;
                committedColumn = column;
                committedPending = pendingCarriageReturn;
                committedPreviousEndsValue = previousTokenEndsValue;
                committedPosition = reader.Position;
                committedConsumed = reader.Consumed;

                continue;
            }

            //NeedMore: discard the partial token by restoring the committed boundary; the driver reads
            //more bytes and re-lexes from there. The examined position is the buffer end so the pipe
            //knows the whole buffer was inspected and must grow before the next read.
            line = committedLine;
            column = committedColumn;
            pendingCarriageReturn = committedPending;
            previousTokenEndsValue = committedPreviousEndsValue;
            byteOffsetBase += committedConsumed;

            return new DrainResult(committedPosition, buffer.End, reachedEnd: false);
        }
    }

    /// <summary>
    /// Lexes a buffer of source bytes for the synchronous incremental path, marking whether it is the final chunk, and
    /// returns the tokens produced up to the last clean boundary; the number of source bytes consumed is reported so the
    /// caller re-presents the unconsumed tail (a partial token or character) prepended to the next chunk. The returned
    /// list is the lexer's own buffer and is valid only until the next call.
    /// </summary>
    /// <param name="buffer">The source bytes to lex (a re-presented unconsumed tail plus the new chunk).</param>
    /// <param name="isFinal">Whether this is the final chunk; when <see langword="true"/> a token may complete at end of input.</param>
    /// <param name="consumed">Receives the number of source bytes consumed; the rest is the unconsumed tail to re-present.</param>
    /// <returns>The tokens lexed from the buffer, in source order.</returns>
    internal IReadOnlyList<JsonataToken> FeedBuffer(ReadOnlySequence<byte> buffer, bool isFinal, out long consumed)
    {
        atFinalBuffer = isFinal;

        DrainResult drained = DrainBuffer(buffer);
        consumed = buffer.GetOffset(drained.Consumed);

        return pendingTokens;
    }

    private JsonataLexStatus TryLexToken(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        JsonataLexStatus triviaStatus = SkipWhitespaceAndComments(ref reader, out bool unterminatedComment);
        if(triviaStatus == JsonataLexStatus.NeedMore)
        {
            return JsonataLexStatus.NeedMore;
        }

        if(unterminatedComment)
        {
            return JsonataLexStatus.Error;
        }

        if(reader.End)
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            int offset = Offset(reader.Consumed);
            token = new JsonataToken(
                JsonataTokenKind.EndOfInput,
                CaptureSpan(offset, offset, line, column, line, column),
                Pool.Intern(ReadOnlySpan<byte>.Empty));

            return JsonataLexStatus.Complete;
        }

        return LexNext(ref reader, out token);
    }

    private JsonataLexStatus LexNext(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        reader.TryPeek(out byte current);

        switch(current)
        {
            case (byte)'"':
            {
                return LexString(ref reader, (byte)'"', out token);
            }
            case (byte)'\'':
            {
                return LexString(ref reader, (byte)'\'', out token);
            }
            case (byte)'`':
            {
                return LexBacktickName(ref reader, out token);
            }
            case (byte)'$':
            {
                return LexVariable(ref reader, out token);
            }
            case (byte)'.':
            {
                return LexDotOrRange(ref reader, out token);
            }
            case (byte)':':
            {
                return LexColonOrAssign(ref reader, out token);
            }
            case (byte)'*':
            {
                return LexStarOrPower(ref reader, out token);
            }
            case (byte)'!':
            {
                return LexBang(ref reader, out token);
            }
            case (byte)'~':
            {
                return LexTilde(ref reader, out token);
            }
            case (byte)'<':
            {
                return LexLessThan(ref reader, out token);
            }
            case (byte)'>':
            {
                return LexGreaterThan(ref reader, out token);
            }
            case (byte)'?':
            {
                return LexQuestion(ref reader, out token);
            }
            case (byte)'[':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.OpenBracket, out token);
            }
            case (byte)']':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.CloseBracket, out token);
            }
            case (byte)'{':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.OpenBrace, out token);
            }
            case (byte)'}':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.CloseBrace, out token);
            }
            case (byte)'(':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.OpenParen, out token);
            }
            case (byte)')':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.CloseParen, out token);
            }
            case (byte)',':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Comma, out token);
            }
            case (byte)';':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Semicolon, out token);
            }
            case (byte)'+':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Plus, out token);
            }
            case (byte)'-':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Minus, out token);
            }
            case (byte)'/':
            {
                //A '/' begins a regular-expression literal in prefix position (the previous significant token
                //expects an operand, or this is the start of input); it is the divide operator after a token
                //that ends a value. Block comments '/*' were already consumed as trivia before this dispatch.
                return previousTokenEndsValue
                    ? LexSinglePunctuation(ref reader, JsonataTokenKind.Slash, out token)
                    : LexRegex(ref reader, out token);
            }
            case (byte)'%':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Percent, out token);
            }
            case (byte)'&':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Ampersand, out token);
            }
            case (byte)'=':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Equal, out token);
            }
            case (byte)'^':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Caret, out token);
            }
            case (byte)'|':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Pipe, out token);
            }
            case (byte)'@':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.At, out token);
            }
            case (byte)'#':
            {
                return LexSinglePunctuation(ref reader, JsonataTokenKind.Hash, out token);
            }
            case(>= (byte)'0' and <= (byte)'9'):
            {
                return LexNumber(ref reader, out token);
            }
            default:
            {
                return LexNameOrLambda(ref reader, out token);
            }
        }
    }

    private JsonataLexStatus LexDotOrRange(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //Longest-match: '..' (range) is tried before '.' (map/field access).
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'.')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.DotDot, ".."u8, out token);
        }

        return LexSinglePunctuation(ref reader, JsonataTokenKind.Dot, out token);
    }

    private JsonataLexStatus LexColonOrAssign(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //Longest-match: ':=' (bind) is tried before ':' (key-value / conditional separator).
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'=')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.Assign, ":="u8, out token);
        }

        return LexSinglePunctuation(ref reader, JsonataTokenKind.Colon, out token);
    }

    private JsonataLexStatus LexStarOrPower(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //Longest-match: '**' (power) is tried before '*' (multiply / wildcard).
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'*')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.StarStar, "**"u8, out token);
        }

        return LexSinglePunctuation(ref reader, JsonataTokenKind.Star, out token);
    }

    private JsonataLexStatus LexBang(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //'!' is valid only as the compound '!='; a lone '!' is a recovery error.
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'=')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.NotEqual, "!="u8, out token);
        }

        return Fail(
            JsonataLexErrorCode.BareExclamation,
            SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
    }

    private JsonataLexStatus LexTilde(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //'~' is valid only as the compound '~>'; a lone '~' is a recovery error.
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'>')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.Chain, "~>"u8, out token);
        }

        return Fail(
            JsonataLexErrorCode.BareTilde,
            SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1));
    }

    private JsonataLexStatus LexLessThan(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //Longest-match: '<=' is tried before '<'. Both are comparison operators; the signature
        //delimiter is a later parser concern, never a distinct lexer token.
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'=')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.LessEqual, "<="u8, out token);
        }

        return LexSinglePunctuation(ref reader, JsonataTokenKind.Less, out token);
    }

    private JsonataLexStatus LexGreaterThan(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //Longest-match: '>=' is tried before '>'. Both are comparison operators.
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)'=')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.GreaterEqual, ">="u8, out token);
        }

        return LexSinglePunctuation(ref reader, JsonataTokenKind.Greater, out token);
    }

    private JsonataLexStatus LexQuestion(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        //Longest-match: '?:' (Elvis) and '??' (coalesce) are tried before '?' (ternary).
        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }
        }
        else if(second == (byte)':')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.QuestionColon, "?:"u8, out token);
        }
        else if(second == (byte)'?')
        {
            return LexFixedOperator(ref reader, 2, JsonataTokenKind.QuestionQuestion, "??"u8, out token);
        }

        return LexSinglePunctuation(ref reader, JsonataTokenKind.Question, out token);
    }

    private JsonataLexStatus LexFixedOperator(
        ref SequenceReader<byte> reader,
        int byteCount,
        JsonataTokenKind kind,
        ReadOnlySpan<byte> lexeme,
        out JsonataToken token)
    {
        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        AdvanceCount(ref reader, byteCount);

        token = new JsonataToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(lexeme));

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus LexSinglePunctuation(ref SequenceReader<byte> reader, JsonataTokenKind kind, out JsonataToken token)
    {
        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        token = new JsonataToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus LexRegex(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        scratchToken = new JsonataTokenGrowthContext(JsonataTokenKind.RegexLiteral, 0, startByte, startLine, startColumn);

        //Consume the opening '/'; the pattern begins at the next byte. The closing '/' is the first
        //unescaped '/' at bracket/brace/paren depth zero (a '/' inside a character class or escaped by an
        //odd backslash run is literal), matching the reference scanRegex.
        Advance(ref reader);
        SequencePosition patternStartPosition = reader.Position;

        int depth = 0;
        int backslashRun = 0;
        bool sawByte = false;
        byte previous = 0;

        while(true)
        {
            if(!reader.TryPeek(out byte b))
            {
                if(!atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }

                return Fail(
                    JsonataLexErrorCode.UnterminatedRegex,
                    SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
            }

            if(b == (byte)'/' && depth == 0 && (backslashRun % 2) == 0)
            {
                if(!sawByte)
                {
                    return Fail(
                        JsonataLexErrorCode.EmptyRegex,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed) + 1, startLine, startColumn, column + 1));
                }

                SequencePosition patternEndPosition = reader.Position;

                //Consume the closing '/', then read the trailing ASCII-letter flags.
                Advance(ref reader);

                return CompleteRegex(ref reader, startByte, startLine, startColumn, patternStartPosition, patternEndPosition, out token);
            }

            //A bracket/brace/paren opens or closes a depth level only when it is not escaped by an
            //immediately preceding backslash, matching the reference scanRegex.
            bool escapedByPrevious = sawByte && previous == (byte)'\\';
            if(!escapedByPrevious)
            {
                if(b is (byte)'(' or (byte)'[' or (byte)'{')
                {
                    depth++;
                }
                else if(b is (byte)')' or (byte)']' or (byte)'}')
                {
                    depth--;
                }
            }

            backslashRun = b == (byte)'\\' ? backslashRun + 1 : 0;
            previous = b;
            sawByte = true;
            Advance(ref reader);
        }
    }

    private JsonataLexStatus CompleteRegex(
        ref SequenceReader<byte> reader,
        int startByte,
        int startLine,
        int startColumn,
        SequencePosition patternStartPosition,
        SequencePosition patternEndPosition,
        out JsonataToken token)
    {
        token = default;

        //The flags are the run of recognised flag letters directly after the closing '/' (the reference reads
        //only 'i' and 'm'); a following letter that is not a flag begins the next token rather than being
        //swallowed.
        SequencePosition flagsStartPosition = reader.Position;

        while(reader.TryPeek(out byte b) && (b == (byte)'i' || b == (byte)'m'))
        {
            Advance(ref reader);
        }

        //A flag run may extend past the buffer end.
        if(reader.End && !atFinalBuffer)
        {
            return JsonataLexStatus.NeedMore;
        }

        SequencePosition flagsEndPosition = reader.Position;

        //The decoded value carries the flags, a '/' separator, then the verbatim pattern bytes. The flags are
        //ASCII letters and never contain '/', so the first '/' separates the flags from the pattern; the
        //pattern bytes are kept verbatim because the .NET regex engine interprets the escapes, not the lexer.
        IMemoryOwner<byte> scratch = Pool.RentScratch(64);
        int written = 0;

        try
        {
            written = AppendRegexBytes(reader.Sequence.Slice(flagsStartPosition, flagsEndPosition), ref scratch, written);
            EnsureScratchCapacity(ref scratch, written + 1);
            scratch.Memory.Span[written++] = (byte)'/';
            written = AppendRegexBytes(reader.Sequence.Slice(patternStartPosition, patternEndPosition), ref scratch, written);

            token = new JsonataToken(
                JsonataTokenKind.RegexLiteral,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(scratch.Memory.Span[..written]));

            return JsonataLexStatus.Complete;
        }
        finally
        {
            scratch.Dispose();
        }
    }

    private int AppendRegexBytes(in ReadOnlySequence<byte> bytes, ref IMemoryOwner<byte> owner, int written)
    {
        EnsureScratchCapacity(ref owner, written + (int)bytes.Length);

        Span<byte> buffer = owner.Memory.Span;
        SequencePosition position = bytes.Start;
        while(bytes.TryGet(ref position, out ReadOnlyMemory<byte> segment))
        {
            segment.Span.CopyTo(buffer[written..]);
            written += segment.Length;
        }

        return written;
    }

    private JsonataLexStatus LexVariable(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        //Consume the leading '$'; the Span covers it but the decoded Value never does.
        Advance(ref reader);

        if(!reader.TryPeek(out byte after))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            //Bare '$' at end of input: the current-context focus, decoded Value empty.
            token = new JsonataToken(
                JsonataTokenKind.Variable,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(ReadOnlySpan<byte>.Empty));

            return JsonataLexStatus.Complete;
        }

        //'$$' denotes the root; the decoded Value carries a single '$' to distinguish it from the
        //bare context focus.
        if(after == (byte)'$')
        {
            Advance(ref reader);

            token = new JsonataToken(
                JsonataTokenKind.Variable,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern("$"u8));

            return JsonataLexStatus.Complete;
        }

        //'$name' is a named variable; the decoded Value is the name without the leading '$'.
        if(IsNameStart(after))
        {
            SequencePosition nameStartPosition = reader.Position;
            Advance(ref reader);

            while(reader.TryPeek(out byte b) && IsNamePart(b))
            {
                Advance(ref reader);
            }

            //The variable name may extend past the buffer end.
            if(reader.End && !atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            token = new JsonataToken(
                JsonataTokenKind.Variable,
                CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                Pool.Intern(reader.Sequence.Slice(nameStartPosition, reader.Position)));

            return JsonataLexStatus.Complete;
        }

        //Bare '$' followed by neither '$' nor a name start: the current-context focus.
        token = new JsonataToken(
            JsonataTokenKind.Variable,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(ReadOnlySpan<byte>.Empty));

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus LexBacktickName(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        //Consume the opening backtick; the decoded Value omits both backticks.
        Advance(ref reader);

        SequencePosition nameStartPosition = reader.Position;

        while(true)
        {
            if(!reader.TryPeek(out byte b))
            {
                if(!atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }

                return Fail(
                    JsonataLexErrorCode.UnterminatedBacktickName,
                    SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
            }

            if(b == (byte)'`')
            {
                SequencePosition nameEndPosition = reader.Position;
                Advance(ref reader);

                token = new JsonataToken(
                    JsonataTokenKind.BacktickName,
                    CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                    Pool.Intern(reader.Sequence.Slice(nameStartPosition, nameEndPosition)));

                return JsonataLexStatus.Complete;
            }

            if(!TryUtf8ByteLength(b, out int byteCount))
            {
                return Fail(
                    JsonataLexErrorCode.InvalidUtf8LeadByte,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                    FormatByte(b));
            }

            if(reader.Remaining < byteCount)
            {
                if(!atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }

                return Fail(
                    JsonataLexErrorCode.TruncatedUtf8,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
            }

            AdvanceCount(ref reader, byteCount);
        }
    }

    private JsonataLexStatus LexString(ref SequenceReader<byte> reader, byte quote, out JsonataToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;
        scratchToken = new JsonataTokenGrowthContext(JsonataTokenKind.String, 0, startByte, startLine, startColumn);

        //Consume the opening quote.
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
                        return JsonataLexStatus.NeedMore;
                    }

                    return Fail(
                        JsonataLexErrorCode.UnterminatedString,
                        SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                }

                if(b == quote)
                {
                    Advance(ref reader);

                    token = new JsonataToken(
                        JsonataTokenKind.String,
                        CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
                        Pool.Intern(scratch.Memory.Span[..written]));

                    return JsonataLexStatus.Complete;
                }

                if(b == (byte)'\\')
                {
                    JsonataLexStatus escapeStatus = DecodeEscape(ref reader, ref scratch, ref written);
                    if(escapeStatus != JsonataLexStatus.Complete)
                    {
                        return escapeStatus;
                    }

                    continue;
                }

                if(!TryUtf8ByteLength(b, out int byteCount))
                {
                    return Fail(
                        JsonataLexErrorCode.InvalidUtf8LeadByte,
                        SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                        FormatByte(b));
                }

                if(reader.Remaining < byteCount)
                {
                    if(!atFinalBuffer)
                    {
                        return JsonataLexStatus.NeedMore;
                    }

                    return Fail(
                        JsonataLexErrorCode.TruncatedUtf8,
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

    private JsonataLexStatus DecodeEscape(ref SequenceReader<byte> reader, ref IMemoryOwner<byte> owner, ref int written)
    {
        if(!reader.TryPeek(1, out byte marker))
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            return Fail(
                JsonataLexErrorCode.TruncatedEscape,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Length), line, column, column));
        }

        switch(marker)
        {
            case (byte)'"':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'"');
            }
            case (byte)'\'':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'\'');
            }
            case (byte)'\\':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'\\');
            }
            case (byte)'/':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'/');
            }
            case (byte)'b':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, 0x08);
            }
            case (byte)'f':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, 0x0C);
            }
            case (byte)'n':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'\n');
            }
            case (byte)'r':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'\r');
            }
            case (byte)'t':
            {
                return AppendSimpleEscape(ref reader, ref owner, ref written, (byte)'\t');
            }
            case (byte)'u':
            {
                return DecodeUnicodeEscape(ref reader, ref owner, ref written);
            }
            default:
            {
                return Fail(
                    JsonataLexErrorCode.InvalidEscape,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 2, line, column, column + 2),
                    FormatEscape(marker));
            }
        }
    }

    private JsonataLexStatus AppendSimpleEscape(ref SequenceReader<byte> reader, ref IMemoryOwner<byte> owner, ref int written, byte value)
    {
        EnsureScratchCapacity(ref owner, written + 1);
        owner.Memory.Span[written++] = value;
        AdvanceCount(ref reader, 2);

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus DecodeUnicodeEscape(ref SequenceReader<byte> reader, ref IMemoryOwner<byte> owner, ref int written)
    {
        int escapeStart = Offset(reader.Consumed);
        int escapeLine = line;
        int escapeColumn = column;

        JsonataLexStatus highStatus = ReadFourHexDigits(ref reader, escapeStart, escapeLine, escapeColumn, out uint high);
        if(highStatus != JsonataLexStatus.Complete)
        {
            return highStatus;
        }

        //A high surrogate must combine with a following '\u' low surrogate into one scalar value.
        if(high is >= 0xD800 and <= 0xDBFF)
        {
            if(reader.Remaining < 2)
            {
                if(!atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }

                return Fail(
                    JsonataLexErrorCode.UnpairedSurrogate,
                    SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column),
                    FormatCodePoint(high));
            }

            reader.TryPeek(out byte backslash);
            reader.TryPeek(1, out byte u);
            if(backslash != (byte)'\\' || u != (byte)'u')
            {
                return Fail(
                    JsonataLexErrorCode.UnpairedSurrogate,
                    SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column),
                    FormatCodePoint(high));
            }

            int lowEscapeStart = Offset(reader.Consumed);
            int lowEscapeColumn = column;
            JsonataLexStatus lowStatus = ReadFourHexDigits(ref reader, lowEscapeStart, line, lowEscapeColumn, out uint low);
            if(lowStatus != JsonataLexStatus.Complete)
            {
                return lowStatus;
            }

            if(low is < 0xDC00 or > 0xDFFF)
            {
                return Fail(
                    JsonataLexErrorCode.UnpairedSurrogate,
                    SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column),
                    FormatCodePoint(low));
            }

            uint combined = 0x10000u + ((high - 0xD800u) << 10) + (low - 0xDC00u);
            written = AppendCodepoint(combined, ref owner, written);

            return JsonataLexStatus.Complete;
        }

        //A lone low surrogate is not a valid scalar value.
        if(high is >= 0xDC00 and <= 0xDFFF)
        {
            return Fail(
                JsonataLexErrorCode.UnpairedSurrogate,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Consumed), escapeLine, escapeColumn, column),
                FormatCodePoint(high));
        }

        written = AppendCodepoint(high, ref owner, written);

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus ReadFourHexDigits(ref SequenceReader<byte> reader, int escapeStart, int escapeLine, int escapeColumn, out uint value)
    {
        //Positioned on the leading '\'; consume "\u" then exactly four hex digits.
        value = 0;

        if(reader.Remaining < 6)
        {
            if(!atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            return Fail(
                JsonataLexErrorCode.TruncatedEscape,
                SourceSpan.SingleLine(escapeStart, Offset(reader.Length), escapeLine, escapeColumn, column));
        }

        AdvanceCount(ref reader, 2);

        for(int i = 0; i < 4; i++)
        {
            reader.TryPeek(out byte hex);
            if(!TryHexValue(hex, out uint digit))
            {
                return Fail(
                    JsonataLexErrorCode.InvalidHexDigit,
                    SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                    ((char)hex).ToString());
            }

            value = (value << 4) | digit;
            Advance(ref reader);
        }

        return JsonataLexStatus.Complete;
    }

    private int AppendCodepoint(uint codepoint, ref IMemoryOwner<byte> owner, int written)
    {
        Rune rune = new((int)codepoint);
        EnsureScratchCapacity(ref owner, written + rune.Utf8SequenceLength);
        int encoded = rune.EncodeToUtf8(owner.Memory.Span[written..]);

        return written + encoded;
    }

    private JsonataLexStatus LexNumber(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;

        while(reader.TryPeek(out byte digit) && IsDigit(digit))
        {
            Advance(ref reader);
        }

        //A fraction, an exponent, or more digits may follow in the next buffer.
        if(reader.End && !atFinalBuffer)
        {
            return JsonataLexStatus.NeedMore;
        }

        if(reader.TryPeek(out byte dot) && dot == (byte)'.')
        {
            //Only a '.' immediately followed by a digit is the fractional part; otherwise the '.' is a
            //separate map/range operator.
            if(!reader.TryPeek(1, out byte afterDot))
            {
                if(!atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }
            }
            else if(IsDigit(afterDot))
            {
                Advance(ref reader);

                while(reader.TryPeek(out byte fraction) && IsDigit(fraction))
                {
                    Advance(ref reader);
                }

                if(reader.End && !atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }
            }
        }

        if(reader.TryPeek(out byte exponent) && (exponent == (byte)'e' || exponent == (byte)'E'))
        {
            //The exponent's optional sign and digits may extend past the buffer end.
            if(reader.Remaining < 2 && !atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            //A JSONata exponent requires at least one digit. Snapshot before the marker so an 'e'/'E'
            //with no following digit can be rolled back to begin the next token (e.g. '1e' is the number
            //'1' followed by the name 'e'); the marker and its sign sit on one line, so only the column
            //needs restoring.
            long beforeExponent = reader.Consumed;
            int columnBeforeExponent = column;

            Advance(ref reader);

            if(reader.TryPeek(out byte expSign) && (expSign == (byte)'+' || expSign == (byte)'-'))
            {
                Advance(ref reader);
            }

            long firstExponentDigit = reader.Consumed;

            while(reader.TryPeek(out byte expDigit) && IsDigit(expDigit))
            {
                Advance(ref reader);
            }

            if(reader.End && !atFinalBuffer)
            {
                return JsonataLexStatus.NeedMore;
            }

            if(reader.Consumed == firstExponentDigit)
            {
                reader.Rewind(reader.Consumed - beforeExponent);
                column = columnBeforeExponent;
            }
        }

        token = new JsonataToken(
            JsonataTokenKind.Number,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus LexNameOrLambda(ref SequenceReader<byte> reader, out JsonataToken token)
    {
        token = default;

        reader.TryPeek(out byte first);

        //'λ' (U+03BB, UTF-8 0xCE 0xBB) is the single-codepoint alias for 'function'.
        if(first == 0xCE)
        {
            JsonataLexStatus lambdaStatus = TryLexLambda(ref reader, out token, out bool wasLambda);
            if(wasLambda)
            {
                return lambdaStatus;
            }
        }

        if(!IsNameStart(first))
        {
            return Fail(
                JsonataLexErrorCode.UnexpectedByte,
                SourceSpan.SingleLine(Offset(reader.Consumed), Offset(reader.Consumed) + 1, line, column, column + 1),
                FormatByte(first));
        }

        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;

        Advance(ref reader);

        while(reader.TryPeek(out byte b) && IsNamePart(b))
        {
            Advance(ref reader);
        }

        //The name may extend past the buffer end.
        if(reader.End && !atFinalBuffer)
        {
            return JsonataLexStatus.NeedMore;
        }

        ReadOnlySequence<byte> nameSpan = reader.Sequence.Slice(startPosition, reader.Position);
        JsonataTokenKind kind = ClassifyName(nameSpan);

        token = new JsonataToken(
            kind,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(nameSpan));

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus TryLexLambda(ref SequenceReader<byte> reader, out JsonataToken token, out bool wasLambda)
    {
        token = default;
        wasLambda = false;

        if(!reader.TryPeek(1, out byte second))
        {
            if(!atFinalBuffer)
            {
                wasLambda = true;

                return JsonataLexStatus.NeedMore;
            }

            return JsonataLexStatus.Complete;
        }

        if(second != 0xBB)
        {
            return JsonataLexStatus.Complete;
        }

        wasLambda = true;
        int startByte = Offset(reader.Consumed);
        SequencePosition startPosition = reader.Position;
        int startLine = line;
        int startColumn = column;
        AdvanceCount(ref reader, 2);

        token = new JsonataToken(
            JsonataTokenKind.Lambda,
            CaptureSpan(startByte, Offset(reader.Consumed), startLine, startColumn, line, column),
            Pool.Intern(reader.Sequence.Slice(startPosition, reader.Position)));

        return JsonataLexStatus.Complete;
    }

    private static JsonataTokenKind ClassifyName(in ReadOnlySequence<byte> name)
    {
        //Re-kind the reserved keyword operators; true/false/null stay Name for the parser to map.
        if(SequenceEquals(name, "and"u8))
        {
            return JsonataTokenKind.KeywordAnd;
        }

        if(SequenceEquals(name, "or"u8))
        {
            return JsonataTokenKind.KeywordOr;
        }

        if(SequenceEquals(name, "in"u8))
        {
            return JsonataTokenKind.KeywordIn;
        }

        if(SequenceEquals(name, "function"u8))
        {
            return JsonataTokenKind.KeywordFunction;
        }

        return JsonataTokenKind.Name;
    }

    private JsonataLexStatus SkipWhitespaceAndComments(ref SequenceReader<byte> reader, out bool unterminatedComment)
    {
        unterminatedComment = false;

        while(reader.TryPeek(out byte b))
        {
            if(IsWhitespaceByte(b))
            {
                Advance(ref reader);
                continue;
            }

            //A '/' begins a block comment only when immediately followed by '*'; otherwise it is the
            //divide operator and trivia scanning stops.
            if(b == (byte)'/')
            {
                if(!reader.TryPeek(1, out byte star))
                {
                    if(!atFinalBuffer)
                    {
                        return JsonataLexStatus.NeedMore;
                    }

                    break;
                }

                if(star != (byte)'*')
                {
                    break;
                }

                JsonataLexStatus commentStatus = SkipBlockComment(ref reader, out unterminatedComment);
                if(commentStatus != JsonataLexStatus.Complete)
                {
                    return commentStatus;
                }

                if(unterminatedComment)
                {
                    return JsonataLexStatus.Complete;
                }

                continue;
            }

            break;
        }

        return JsonataLexStatus.Complete;
    }

    private JsonataLexStatus SkipBlockComment(ref SequenceReader<byte> reader, out bool unterminatedComment)
    {
        unterminatedComment = false;

        int startByte = Offset(reader.Consumed);
        int startLine = line;
        int startColumn = column;

        AdvanceCount(ref reader, 2);

        while(true)
        {
            if(!reader.TryPeek(out byte b))
            {
                if(!atFinalBuffer)
                {
                    return JsonataLexStatus.NeedMore;
                }

                pendingDiagnostic = new JsonataLexDiagnostic(
                    JsonataLexErrorCode.UnterminatedBlockComment,
                    SourceSpan.SingleLine(startByte, Offset(reader.Consumed), startLine, startColumn, column));
                unterminatedComment = true;

                return JsonataLexStatus.Complete;
            }

            if(b == (byte)'*')
            {
                if(!reader.TryPeek(1, out byte slash))
                {
                    if(!atFinalBuffer)
                    {
                        return JsonataLexStatus.NeedMore;
                    }

                    Advance(ref reader);
                    continue;
                }

                if(slash == (byte)'/')
                {
                    AdvanceCount(ref reader, 2);

                    return JsonataLexStatus.Complete;
                }
            }

            Advance(ref reader);
        }
    }

    private void Advance(ref SequenceReader<byte> reader)
    {
        reader.TryRead(out byte b);

        if(b == (byte)'\n')
        {
            //An LF directly after a CR completes a CR-LF pair the CR already counted as one newline; a
            //standalone LF opens its own line.
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
    /// <see cref="JsonataTokenKind.Error"/> token that stands in for them.
    /// </summary>
    /// <param name="reader">The reader, positioned where the error was detected; advanced to the next token boundary.</param>
    /// <returns>A <see cref="JsonataTokenKind.Error"/> token spanning from the failure to the resync boundary.</returns>
    private JsonataToken RecordErrorAndRecover(ref SequenceReader<byte> reader)
    {
        JsonataLexDiagnostic diagnostic = pendingDiagnostic;
        diagnostics.Add(diagnostic);

        RecoverToTokenBoundary(ref reader);

        SourceSpan span = new(
            diagnostic.Span.StartByte,
            Offset(reader.Consumed),
            diagnostic.Span.StartLine,
            diagnostic.Span.StartColumn,
            line,
            column);

        return new JsonataToken(JsonataTokenKind.Error, span, Pool.Intern(ReadOnlySpan<byte>.Empty));
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

    private JsonataLexStatus Fail(JsonataLexErrorCode code, SourceSpan span, string? detail = null)
    {
        pendingDiagnostic = new JsonataLexDiagnostic(code, span, detail);

        return JsonataLexStatus.Error;
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

    private static bool IsNameStart(byte b)
    {
        //A JSONata name may contain any character that is not whitespace or an operator, and every operator
        //and whitespace byte is ASCII (< 0x80), so any byte of a non-ASCII UTF-8 multi-byte character is a
        //valid name byte — this admits Unicode field names such as a CJK or accented identifier.
        return IsAsciiLetter(b) || b == (byte)'_' || b >= 0x80;
    }

    private static bool IsNamePart(byte b)
    {
        return IsAsciiLetter(b) || IsDigit(b) || b == (byte)'_' || b >= 0x80;
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

    private void EnsureScratchCapacity(ref IMemoryOwner<byte> owner, int required)
    {
        if(required <= owner.Memory.Length)
        {
            return;
        }

        //The buffer must grow to hold a longer token; let the limits policy reject it before a larger
        //buffer is allocated, bounding the contiguous memory any one token can force.
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

    /// <summary>
    /// The result of draining one pipe buffer: where the pipe should resume (the last completed token
    /// boundary), how far it was examined (the buffer end), and whether end of input was reached.
    /// </summary>
    private readonly struct DrainResult(SequencePosition consumed, SequencePosition examined, bool reachedEnd)
    {
        public SequencePosition Consumed { get; } = consumed;

        public SequencePosition Examined { get; } = examined;

        public bool ReachedEnd { get; } = reachedEnd;
    }
}
