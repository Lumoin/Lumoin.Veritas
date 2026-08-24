using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Jsonata.Ast;
using Lumoin.Veritas.Jsonata.Functions;
using Lumoin.Veritas.Jsonata.Lexer;

namespace Lumoin.Veritas.Jsonata.Parser;

/// <summary>
/// Parses a token stream from <see cref="JsonataLexer"/> into a <see cref="JsonataExpression"/> AST.
/// </summary>
/// <remarks>
/// <para>
/// The parser is iterative and resumable, the same production model the lexer and the SPARQL / Turtle
/// parsers use. Every production runs on an explicit <see cref="Stack{T}"/> of <see cref="ParseFrame"/>
/// values; the driver advances the top frame one bounded step at a time. No production calls back into
/// another via method recursion. JSONata's grammar is operator-precedence, so precedence is handled by
/// precedence climbing inside the single multi-stage <see cref="ParseFrameKind.Expression"/> frame: the
/// frame carries a <c>MinBindingPower</c> bound and parses each right operand at <c>bp + 1</c> for the
/// left-associative operators (so same-level chains stay left-associative) and at <c>bp - 1</c> for the
/// right-associative bind <c>:=</c> (so a chained <c>:=</c> is absorbed rightward).
/// </para>
/// <para>
/// Because the driver checks lookahead before each step and a step never reads more than
/// <see cref="MaxStepLookahead"/> tokens past the cursor, the parser suspends (returning
/// <see cref="ParseStatus.NeedMore"/>) when the tokens a step needs have not arrived yet, and resumes
/// from the same frame and stage when more are fed — without re-parsing. An expression can therefore be
/// parsed straight from the pipe-fed lexer without buffering the whole token stream.
/// </para>
/// <para>
/// Malformed input is recovered, never thrown: a syntax error becomes an <see cref="ErrorExpression"/>
/// node that slots into any position the base is expected, while a <see cref="Diagnostic"/> is recorded
/// in the shared bag. <see cref="JsonataParseException"/> is reserved for driver-invariant violations.
/// </para>
/// </remarks>
public sealed class JsonataParser
{
    //The most tokens any single step inspects past the cursor. No step reads more than one token past the
    //cursor in this build; four is a generous buffer kept for parity with the sibling parsers.
    private const int MaxStepLookahead = 4;

    //The stages of the single Expression frame, named so the resume points read clearly.
    private const int StageOperand = 0;
    private const int StageLedLoop = 1;
    private const int StageCombineBinary = 2;
    private const int StageWrapUnary = 3;
    private const int StageAdoptBlock = 4;
    private const int StageMapCombine = 5;
    private const int StagePredicateClose = 6;
    private const int StageConditionalThen = 7;
    private const int StageConditionalElse = 8;
    private const int StageCombineRange = 9;
    private const int StageAdoptArrayConstructor = 10;
    private const int StageAdoptObjectConstructor = 11;
    private const int StageCombineBind = 12;
    private const int StageCombineDefault = 13;
    private const int StageAdoptLambda = 14;
    private const int StageAdoptCall = 15;
    private const int StageCombineApply = 16;
    private const int StageTransformUpdate = 17;
    private const int StageTransformDelete = 18;
    private const int StageTransformClose = 19;
    private const int StageAdoptSort = 20;
    private const int StageAdoptObjectGroup = 21;
    private const int StageCombineContextBind = 22;
    private const int StageCombinePositionBind = 23;

    //The stages of the ElementList frame, named so its resume points read clearly.
    private const int StageElementListFirst = 0;
    private const int StageElementListAfterElement = 1;

    //The stages of the ObjectMemberList frame, named so its resume points read clearly.
    private const int StageObjectMemberListFirstKey = 0;
    private const int StageObjectMemberListAfterKey = 1;
    private const int StageObjectMemberListAfterValue = 2;

    //The stages of the BlockStatementList frame, named so its resume points read clearly.
    private const int StageBlockStatementListFirst = 0;
    private const int StageBlockStatementListAfterStatement = 1;

    //The stages of the LambdaDefinition frame, named so its resume points read clearly.
    private const int StageLambdaOpenParen = 0;
    private const int StageLambdaParameters = 1;
    private const int StageLambdaSignature = 2;
    private const int StageLambdaBody = 3;

    //The stages of the ArgumentList frame, named so its resume points read clearly.
    private const int StageArgumentListFirst = 0;
    private const int StageArgumentListAfterArgument = 1;
    private const int StageArgumentListReadNext = 2;

    //The stages of the SortTermList frame, named so its resume points read clearly.
    private const int StageSortTermListFirst = 0;
    private const int StageSortTermListAfterTerm = 1;

    private readonly Stack<ParseFrame> frames = new();
    private JsonataExpression? produced;
    private object? completed;
    private bool tokensComplete;
    private bool started;
    private int index;
    private int parserDiagnosticsRecorded;
    private SourceSpan lastConsumedSpan;

    /// <summary>
    /// Initialises a <see cref="JsonataParser"/> over a fully materialised token stream.
    /// </summary>
    /// <param name="tokens">The lexed token stream, ending with <see cref="JsonataTokenKind.EndOfInput"/>.</param>
    /// <param name="pool">The pool used to intern parser-allocated identifiers.</param>
    /// <param name="diagnostics">The bag recovery records diagnostics into; a private bag is created when <see langword="null"/>. Pass a shared bag to merge lexer-bridged and parser diagnostics.</param>
    /// <param name="maxDiagnostics">The per-parse cap on parser-recorded diagnostics; once reached, a <see cref="WellKnownDiagnostics.Jsonata.ExcessDiagnostics"/> marker is recorded and further parser diagnostics are suppressed. Defaults to unbounded.</param>
    public JsonataParser(IEnumerable<JsonataToken> tokens, Utf8StringPool pool, DiagnosticBag? diagnostics = null, int maxDiagnostics = int.MaxValue)
        : this(pool, diagnostics, maxDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        foreach(JsonataToken token in tokens)
        {
            Tokens.Add(token);
        }

        tokensComplete = true;
    }

    /// <summary>
    /// Initialises a <see cref="JsonataParser"/> that is fed tokens incrementally through
    /// <see cref="FeedToken(JsonataToken)"/> and pulled through
    /// <see cref="TryParseExpression(out JsonataExpression)"/>.
    /// </summary>
    /// <param name="pool">The pool used to intern parser-allocated identifiers.</param>
    /// <param name="diagnostics">The bag recovery records diagnostics into; a private bag is created when <see langword="null"/>. Pass a shared bag to merge lexer-bridged and parser diagnostics.</param>
    /// <param name="maxDiagnostics">The per-parse cap on parser-recorded diagnostics; once reached, a <see cref="WellKnownDiagnostics.Jsonata.ExcessDiagnostics"/> marker is recorded and further parser diagnostics are suppressed. Defaults to unbounded.</param>
    /// <remarks>
    /// The parser suspends — preserving its work stack — when the expression needs tokens that have not
    /// arrived yet, and resumes when more are fed, so the token buffer need not hold the whole
    /// expression.
    /// </remarks>
    internal JsonataParser(Utf8StringPool pool, DiagnosticBag? diagnostics = null, int maxDiagnostics = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Tokens = [];
        Pool = pool;
        Diagnostics = diagnostics ?? new DiagnosticBag();
        MaxDiagnostics = maxDiagnostics;
    }

    /// <summary>Gets the token buffer the parser indexes into.</summary>
    private List<JsonataToken> Tokens { get; }

    /// <summary>Gets the pool used to intern parser-allocated identifiers.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>
    /// Gets the bag recovery records diagnostics into. Lexical diagnostics bridged by the facade and the
    /// parser's own syntax diagnostics accumulate here in source order.
    /// </summary>
    internal DiagnosticBag Diagnostics { get; }

    /// <summary>Gets the per-parse cap on parser-recorded diagnostics; <see cref="int.MaxValue"/> is unbounded.</summary>
    private int MaxDiagnostics { get; }

    /// <summary>
    /// Gets whether enough tokens are buffered for the next step to run without reading past the buffer.
    /// </summary>
    /// <remarks>
    /// A step reads at most <see cref="MaxStepLookahead"/> tokens past the cursor. Once the stream is
    /// complete the buffer ends with <see cref="JsonataTokenKind.EndOfInput"/> and the cursor clamps to
    /// it, so the remaining short tail is read without further input.
    /// </remarks>
    private bool HasLookahead => tokensComplete || index + MaxStepLookahead < Tokens.Count;

    /// <summary>Gets the token at the cursor.</summary>
    private JsonataToken Current => Tokens[index];

    /// <summary>
    /// Returns the token <paramref name="offset"/> positions past the cursor, clamping to the last
    /// buffered token (the end-of-input sentinel once the stream is complete).
    /// </summary>
    /// <param name="offset">The lookahead distance, no greater than <see cref="MaxStepLookahead"/>.</param>
    /// <returns>The peeked token.</returns>
    private JsonataToken Peek(int offset)
    {
        int target = index + offset;

        return target < Tokens.Count ? Tokens[target] : Tokens[^1];
    }

    /// <summary>
    /// Parses the token stream into a <see cref="JsonataExpression"/>, assuming the whole stream is
    /// present.
    /// </summary>
    /// <returns>The parsed expression AST (possibly carrying error nodes).</returns>
    /// <exception cref="JsonataParseException">The token stream ended before a complete expression was parsed.</exception>
    public JsonataExpression ParseExpression()
    {
        ParseStatus status = TryParseExpression(out JsonataExpression? expression);
        if(status != ParseStatus.Produced)
        {
            throw new JsonataParseException("The token stream ended before a complete JSONata expression was parsed.");
        }

        //The post-parse path-processing pass flattens tuple paths and resolves parent (%) ancestry over the
        //recovered tree, recording its S0213 / S0214 / S0215 / S0216 / S0217-equivalent diagnostics into the
        //same bag, before the tree is returned. A plain path is returned unchanged (zero regression).
        return JsonataPathProcessor.Process(expression!, Diagnostics);
    }

    /// <summary>
    /// Parses the token stream into a <see cref="ParseResult{TTree}"/>: the expression (possibly carrying
    /// error nodes) together with the accumulated diagnostics and whether any has error severity.
    /// </summary>
    /// <returns>The parse result.</returns>
    public ParseResult<JsonataExpression> ParseToResult()
    {
        JsonataExpression expression = ParseExpression();

        return new ParseResult<JsonataExpression>(expression, Diagnostics.Diagnostics, Diagnostics.HasErrors);
    }

    /// <summary>
    /// Appends one lexed token to the parser's buffer. The terminating
    /// <see cref="JsonataTokenKind.EndOfInput"/> token marks the stream complete.
    /// </summary>
    /// <param name="token">The next token in source order.</param>
    internal void FeedToken(JsonataToken token)
    {
        Tokens.Add(token);

        if(token.Kind == JsonataTokenKind.EndOfInput)
        {
            tokensComplete = true;
        }
    }

    /// <summary>
    /// Attempts to parse the expression from the buffered tokens, suspending when more are needed.
    /// </summary>
    /// <param name="expression">The parsed expression when the result is <see cref="ParseStatus.Produced"/>; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="ParseStatus.Produced"/> once the whole expression is parsed, or
    /// <see cref="ParseStatus.NeedMore"/> when the parser needs tokens that have not been fed yet.
    /// </returns>
    internal ParseStatus TryParseExpression(out JsonataExpression? expression)
    {
        if(produced is not null)
        {
            expression = produced;

            return ParseStatus.Produced;
        }

        expression = null;

        if(!started)
        {
            if(!HasLookahead)
            {
                return ParseStatus.NeedMore;
            }

            frames.Push(new ParseFrame { Kind = ParseFrameKind.Program, StartSpan = Current.Span });
            started = true;
        }

        if(Drive() == DriveOutcome.NeedMore)
        {
            return ParseStatus.NeedMore;
        }

        produced = (JsonataExpression)completed!;
        completed = null;
        expression = produced;

        return ParseStatus.Produced;
    }

    /// <summary>
    /// Advances the cursor by one token, clamping at the terminating
    /// <see cref="JsonataTokenKind.EndOfInput"/> so a step that reads past the end keeps seeing the
    /// sentinel rather than indexing out of range.
    /// </summary>
    private void Advance()
    {
        lastConsumedSpan = Tokens[index].Span;

        if(index < Tokens.Count - 1)
        {
            index++;
        }
    }

    /// <summary>
    /// Combines a start span and an end span into the covering extent.
    /// </summary>
    /// <param name="start">The span at the start of the construct.</param>
    /// <param name="end">The span at the end of the construct (typically <c>lastConsumedSpan</c>).</param>
    /// <returns>The covering span.</returns>
    private static SourceSpan CombineSpans(SourceSpan start, SourceSpan end)
    {
        return new SourceSpan(start.StartByte, end.EndByte, start.StartLine, start.StartColumn, end.EndLine, end.EndColumn);
    }

    /// <summary>
    /// Downcasts a popped child frame's result to the type the receiving step expects. The work stack
    /// hands every frame's product up through one untyped <c>object?</c> slot; the cast is safe by
    /// construction (a step only consumes results of the frames it pushed) and <c>incoming</c> is
    /// non-null because a popped frame always carries a result.
    /// </summary>
    /// <typeparam name="T">The product type the receiving step pushed for.</typeparam>
    /// <param name="incoming">The popped child result.</param>
    /// <returns>The result typed as <typeparamref name="T"/>.</returns>
    private static T Pop<T>(object? incoming)
    {
        return (T)incoming!;
    }

    /// <summary>
    /// Runs the work stack until it empties, advancing the top frame one step at a time and threading
    /// each popped frame's result into the parent's next step. Suspends with the stack intact when the
    /// next step would read tokens that have not arrived.
    /// </summary>
    /// <returns>Whether the expression was produced or the parser needs more tokens.</returns>
    private DriveOutcome Drive()
    {
        while(frames.Count > 0)
        {
            if(!HasLookahead)
            {
                return DriveOutcome.NeedMore;
            }

            ParseFrame top = frames.Peek();
            StepResult step = Step(top, completed);
            completed = null;

            switch(step.Action)
            {
                case StepAction.Pop:
                {
                    frames.Pop();
                    completed = step.Result;

                    break;
                }
                case StepAction.Push:
                {
                    frames.Push(step.NewFrame!);

                    break;
                }
                case StepAction.Continue:
                {
                    break;
                }
                default:
                {
                    throw new JsonataParseException("Parser driver reached an undefined state.", Current.Span);
                }
            }
        }

        return DriveOutcome.Produced;
    }

    /// <summary>
    /// Dispatches one step of a frame on its production kind.
    /// </summary>
    /// <param name="frame">The frame to advance.</param>
    /// <param name="incoming">The result a just-popped child frame handed up, or <see langword="null"/>.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult Step(ParseFrame frame, object? incoming)
    {
        return frame.Kind switch
        {
            ParseFrameKind.Program => StepProgram(frame, incoming),
            ParseFrameKind.Expression => StepExpression(frame, incoming),
            ParseFrameKind.ElementList => StepElementList(frame, incoming),
            ParseFrameKind.ObjectMemberList => StepObjectMemberList(frame, incoming),
            ParseFrameKind.BlockStatementList => StepBlockStatementList(frame, incoming),
            ParseFrameKind.LambdaDefinition => StepLambdaDefinition(frame, incoming),
            ParseFrameKind.ArgumentList => StepArgumentList(frame, incoming),
            ParseFrameKind.SortTermList => StepSortTermList(frame, incoming),
            _ => throw new JsonataParseException($"Parser production '{frame.Kind}' is not yet implemented in this build.", frame.StartSpan)
        };
    }

    /// <summary>
    /// Advances the top-level program: pushes the single expression, then adopts it and reports any
    /// trailing tokens past its end before producing it.
    /// </summary>
    /// <param name="frame">The program frame.</param>
    /// <param name="incoming">The parsed expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepProgram(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        JsonataExpression expression = Pop<JsonataExpression>(incoming);

        if(Current.Kind != JsonataTokenKind.EndOfInput)
        {
            //Trailing tokens past a complete expression are a syntax error; report once and discard them
            //(the expression already parsed is kept). ReportRecoverable stays silent when the trailing
            //token is a lexer error token whose LX#### diagnostic the facade already bridged, so that span
            //is not double-counted.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.ExpectedEndOfExpression, Current.Span, "Unexpected token after the end of the expression.");
            while(Current.Kind != JsonataTokenKind.EndOfInput)
            {
                int before = index;
                Advance();

                if(index == before)
                {
                    break;
                }
            }
        }

        return StepResult.Done(expression);
    }

    /// <summary>Dispatches one step of an expression frame on its stage.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">A pushed sub-expression's result on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepExpression(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageOperand => ExpressionOperand(frame),
            StageLedLoop => ExpressionLedLoop(frame),
            StageCombineBinary => ExpressionCombineBinary(frame, incoming),
            StageWrapUnary => ExpressionWrapUnary(frame, incoming),
            StageAdoptBlock => ExpressionAdoptBlock(frame, incoming),
            StageMapCombine => ExpressionMapCombine(frame, incoming),
            StagePredicateClose => ExpressionPredicateClose(frame, incoming),
            StageConditionalThen => ExpressionConditionalThen(frame, incoming),
            StageConditionalElse => ExpressionConditionalElse(frame, incoming),
            StageCombineRange => ExpressionCombineRange(frame, incoming),
            StageAdoptArrayConstructor => ExpressionAdoptArrayConstructor(frame, incoming),
            StageAdoptObjectConstructor => ExpressionAdoptObjectConstructor(frame, incoming),
            StageCombineBind => ExpressionCombineBind(frame, incoming),
            StageCombineDefault => ExpressionCombineDefault(frame, incoming),
            StageAdoptLambda => ExpressionAdoptLambda(frame, incoming),
            StageAdoptCall => ExpressionAdoptCall(frame, incoming),
            StageCombineApply => ExpressionCombineApply(frame, incoming),
            StageTransformUpdate => ExpressionTransformUpdate(frame, incoming),
            StageTransformDelete => ExpressionTransformDelete(frame, incoming),
            StageTransformClose => ExpressionTransformClose(frame, incoming),
            StageAdoptSort => ExpressionAdoptSort(frame, incoming),
            StageAdoptObjectGroup => ExpressionAdoptObjectGroup(frame, incoming),
            StageCombineContextBind => ExpressionCombineContextBind(frame, incoming),
            StageCombinePositionBind => ExpressionCombinePositionBind(frame, incoming),
            _ => throw new JsonataParseException("Expression reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Parses the first operand: an optional leading unary negate applied to a primary.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionOperand(ParseFrame frame)
    {
        //'-' in nud position is unary negate; its operand binds tighter than every binary operator. '+'
        //is not a JSONata unary operator (no nud), so a leading '+' falls through to recovery.
        if(Current.Kind == JsonataTokenKind.Minus)
        {
            frame.OperatorKind = Current.Kind;
            frame.OperatorSpan = Current.Span;
            Advance();
            frame.Stage = StageWrapUnary;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = BindingPower.UnaryOperand, StartSpan = Current.Span });
        }

        return DispatchPrimaryExpression(frame);
    }

    /// <summary>
    /// Absorbs the next infix or postfix operator whose binding power is at least the frame's, pushing
    /// the right operand or sub-expression; otherwise the expression is complete and pops its left side.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionLedLoop(ParseFrame frame)
    {
        int bindingPower = BindingPower.Led(Current.Kind);
        if(bindingPower == 0 || bindingPower < frame.MinBindingPower)
        {
            return StepResult.Done(frame.Left!);
        }

        return Current.Kind switch
        {
            JsonataTokenKind.Dot => LedMap(frame),
            JsonataTokenKind.OpenBracket => LedPredicate(frame),
            JsonataTokenKind.OpenParen => LedCall(frame),
            JsonataTokenKind.Question => LedConditional(frame),
            JsonataTokenKind.DotDot => LedRange(frame, bindingPower),
            JsonataTokenKind.Assign => LedAssign(frame, bindingPower),
            JsonataTokenKind.Chain => LedApply(frame, bindingPower),
            JsonataTokenKind.QuestionColon or JsonataTokenKind.QuestionQuestion => LedDefault(frame, bindingPower),
            JsonataTokenKind.Caret => LedSort(frame),
            JsonataTokenKind.OpenBrace => LedObjectGroup(frame),
            JsonataTokenKind.At => LedContextBind(frame, bindingPower),
            JsonataTokenKind.Hash => LedPositionBind(frame, bindingPower),
            _ when BindingPower.IsBinaryOperator(Current.Kind) => LedBinary(frame, bindingPower),
            _ => LedDeferred(frame)
        };
    }

    /// <summary>Absorbs a binary operator, pushing its right operand a level up for left-associativity.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedBinary(ParseFrame frame, int bindingPower)
    {
        frame.OperatorKind = Current.Kind;
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombineBinary;

        //Every binary operator is left-associative, so the right operand is parsed one level
        //up; an equal-binding-power operator therefore stops the right operand and the outer frame keeps
        //the chain.
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower + 1, StartSpan = Current.Span });
    }

    /// <summary>Absorbs the map operator <c>.</c>, pushing the step sub-expression a level up.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedMap(ParseFrame frame)
    {
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageMapCombine;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = BindingPower.MapStep, StartSpan = Current.Span });
    }

    /// <summary>
    /// Absorbs the predicate / index operator <c>[</c> after an operand. An immediate <c>]</c> (the empty
    /// brackets <c>source[]</c>) is the keep-array marker, not a filter: it builds a
    /// <see cref="KeepArrayExpression"/> over the left side and the led loop resumes (so a following predicate
    /// or step continues the path). A non-empty <c>[</c> pushes the filter / index sub-expression at the
    /// lowest binding power and the frame resumes at <see cref="StagePredicateClose"/>.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedPredicate(ParseFrame frame)
    {
        if(frame.Left is ObjectConstructorExpression { Source: not null })
        {
            //A predicate cannot follow a grouping expression in the same path step.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.InvalidGroupingStep, Current.Span, "A predicate cannot follow a grouping expression in a path step.");
        }

        Advance();
        if(Current.Kind == JsonataTokenKind.CloseBracket)
        {
            //The empty brackets are the keep-array marker: consume the ']' and wrap the left side so its
            //result stays a JSON array. The led loop resumes, so a following predicate or step continues the
            //same path (matching forms such as 'Phone[][type="mobile"].number').
            JsonataExpression source = frame.Left!;
            Advance();
            frame.Left = new KeepArrayExpression(CombineSpans(source.Span, lastConsumedSpan), source);
            frame.Stage = StageLedLoop;

            return StepResult.Continue();
        }

        frame.Stage = StagePredicateClose;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Absorbs the function-call operator <c>(</c> after an operand: consumes the <c>(</c> and pushes a
    /// variadic <see cref="ParseFrameKind.ArgumentList"/> frame to collect the comma-separated argument
    /// expressions. The already-parsed operand in <see cref="ParseFrame.Left"/> is the call's procedure; the
    /// expression frame resumes at <see cref="StageAdoptCall"/> to build the call over it.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedCall(ParseFrame frame)
    {
        SourceSpan open = Current.Span;
        Advance();
        frame.Stage = StageAdoptCall;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ArgumentList, StartSpan = open, Arguments = [] });
    }

    /// <summary>Builds the call node over the frame's procedure (its left side) and the popped argument list, then resumes the led loop so a chained call <c>f(1)(2)</c> applies left-to-right.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped argument-list expressions.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionAdoptCall(ParseFrame frame, object? incoming)
    {
        JsonataExpression procedure = frame.Left!;
        List<JsonataExpression> arguments = Pop<List<JsonataExpression>>(incoming);

        frame.Left = new CallExpression(CombineSpans(procedure.Span, lastConsumedSpan), procedure, arguments);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Absorbs the conditional operator <c>?</c>, pushing the true-branch sub-expression.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedConditional(ParseFrame frame)
    {
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageConditionalThen;

        //The branches parse at the lowest binding power so a comma-free branch absorbs all the way to its
        //natural end (the ':' or the operator that closes the conditional).
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>Absorbs the range operator <c>..</c>, pushing its high bound a level up for left-associativity.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedRange(ParseFrame frame, int bindingPower)
    {
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombineRange;

        //The high bound is parsed one level up so the range is left-associative, matching the binary
        //operators' precedence-climbing pattern.
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower + 1, StartSpan = Current.Span });
    }

    /// <summary>Absorbs the bind operator <c>:=</c>, pushing its right operand one level below its own binding power for right-associativity.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedAssign(ParseFrame frame, int bindingPower)
    {
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombineBind;

        //The bind operator is right-associative, so its right operand is parsed one level BELOW its own
        //binding power (bp - 1): a following ':=' (equal binding power) is therefore absorbed into the right
        //operand, so 'a := b := c' groups as 'a := (b := c)'.
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower - 1, StartSpan = Current.Span });
    }

    /// <summary>
    /// Combines the left operand with the popped right operand into a bind node. The left operand must be a
    /// variable reference; when it is not, a diagnostic is recorded and the right operand is kept as the
    /// recovered node so the parse continues without cascading.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand (the bound value).</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionCombineBind(ParseFrame frame, object? incoming)
    {
        JsonataExpression left = frame.Left!;
        JsonataExpression value = Pop<JsonataExpression>(incoming);

        if(left is VariableExpression variable)
        {
            frame.Left = new BindExpression(CombineSpans(left.Span, value.Span), variable.Name, value);
            frame.Stage = StageLedLoop;

            return StepResult.Continue();
        }

        //The left side of ':=' must be a variable name; the diagnostic flags it once and the value
        //expression is kept as the recovered node so the surrounding parse continues uncascaded.
        _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.BindLeftNotVariable, left.Span, "The left side of ':=' must be a variable name (start with '$').");
        frame.Left = value;
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Absorbs the function-application / chain operator <c>~&gt;</c>, pushing its right operand one level up
    /// for left-associativity. The right operand parses at <c>bp + 1</c>, so a chain <c>a ~&gt; b ~&gt; c</c>
    /// stops the right operand at the next <c>~&gt;</c> and the outer frame keeps the chain, grouping as
    /// <c>(a ~&gt; b) ~&gt; c</c>. The call operator <c>(</c> binds tighter than <c>~&gt;</c>, so a right
    /// operand <c>$f(a)</c> naturally parses to a <see cref="CallExpression"/> — exactly the shape the
    /// evaluator detects as the call-prepend case.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedApply(ParseFrame frame, int bindingPower)
    {
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombineApply;

        //The chain operator is left-associative, so its right operand is parsed one level up; an equal-binding
        //chain therefore stops the right operand and the outer frame keeps the chain.
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower + 1, StartSpan = Current.Span });
    }

    /// <summary>Combines the left operand with the popped right operand into a function-application / chain node.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionCombineApply(ParseFrame frame, object? incoming)
    {
        JsonataExpression left = frame.Left!;
        JsonataExpression right = Pop<JsonataExpression>(incoming);

        frame.Left = new ApplyExpression(CombineSpans(left.Span, right.Span), left, right);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Absorbs a default-value operator (<c>?:</c> Elvis or <c>??</c> coalesce), pushing its right operand a level up for left-associativity.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedDefault(ParseFrame frame, int bindingPower)
    {
        frame.OperatorKind = Current.Kind;
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombineDefault;

        //The default operators are left-associative, so the right operand is parsed one level up; a chain
        //'a ?: b ?: c' therefore groups as '(a ?: b) ?: c'.
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower + 1, StartSpan = Current.Span });
    }

    /// <summary>Combines the left operand with the popped right operand under the pending default operator (<c>?:</c> / <c>??</c>).</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand (the fallback value).</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionCombineDefault(ParseFrame frame, object? incoming)
    {
        JsonataExpression left = frame.Left!;
        JsonataExpression right = Pop<JsonataExpression>(incoming);

        frame.Left = new DefaultExpression(CombineSpans(left.Span, right.Span), left, MapDefaultOperator(frame.OperatorKind), right);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Maps a default-operator token kind to its <see cref="DefaultOperator"/>.</summary>
    /// <param name="kind">The operator token kind (<see cref="JsonataTokenKind.QuestionColon"/> or <see cref="JsonataTokenKind.QuestionQuestion"/>).</param>
    /// <returns>The default operator.</returns>
    private static DefaultOperator MapDefaultOperator(JsonataTokenKind kind) => kind switch
    {
        JsonataTokenKind.QuestionColon => DefaultOperator.Elvis,
        _ => DefaultOperator.Coalesce
    };

    /// <summary>Combines the low bound with the popped high bound into a range node.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped high bound.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionCombineRange(ParseFrame frame, object? incoming)
    {
        JsonataExpression low = frame.Left!;
        JsonataExpression high = Pop<JsonataExpression>(incoming);

        frame.Left = new RangeExpression(CombineSpans(low.Span, high.Span), low, high);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Recovers a deferred led-position operator: the context bind <c>@</c> and the positional bind <c>#</c>.
    /// The construct is recognised but not parsed in this build, so the diagnostic is recorded, the
    /// operator's tail is skipped to the next resync point, and the already-parsed prefix in
    /// <see cref="ParseFrame.Left"/> is kept and returned rather than discarded into an error node.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedDeferred(ParseFrame frame)
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.UnsupportedConstruct, Current.Span, "This JSONata construct is not supported in this build.");
        _ = ResyncTo(Current.Span, out _);

        return StepResult.Done(frame.Left!);
    }

    /// <summary>
    /// Absorbs the context-bind operator <c>@</c> after an operand: consumes the <c>@</c> and pushes its
    /// right operand at the operator's own binding power (the reference's <c>expression(80)</c>), so a chained
    /// <c>@$o#i</c> stops the right operand at the following equal-binding <c>#</c> and the outer frame keeps
    /// the chain. The frame resumes at <see cref="StageCombineContextBind"/> to build the raw
    /// <see cref="ContextBindExpression"/>; the post-parse ancestry pass folds it into the source path's last
    /// step. The right operand must be a <c>$name</c> variable; <see cref="ExpressionCombineContextBind"/>
    /// enforces that.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedContextBind(ParseFrame frame, int bindingPower)
    {
        frame.OperatorKind = Current.Kind;
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombineContextBind;

        //The bind variable is the sole right operand: parse it one binding power above the operator's own
        //(left-associative), so a following step or predicate ('.', '[', another '@'/'#', all at this same
        //power) is NOT absorbed into the bind's right side but stays on the path — matching the reference's
        //expression(80), which stops at an equal-power operator.
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower + 1, StartSpan = Current.Span });
    }

    /// <summary>
    /// Absorbs the positional-bind operator <c>#</c> after an operand: consumes the <c>#</c> and pushes its
    /// right operand at the operator's own binding power, exactly as <see cref="LedContextBind"/> does for
    /// <c>@</c>. The frame resumes at <see cref="StageCombinePositionBind"/> to build the raw
    /// <see cref="IndexBindExpression"/>; the post-parse ancestry pass folds it into the source path's last
    /// step. The right operand must be a <c>$name</c> variable; <see cref="ExpressionCombinePositionBind"/>
    /// enforces that.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="bindingPower">The operator's left binding power.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedPositionBind(ParseFrame frame, int bindingPower)
    {
        frame.OperatorKind = Current.Kind;
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageCombinePositionBind;

        //One above the operator's own power (left-associative), so a following step/predicate at the same
        //power stays on the path rather than being pulled into the bind's right side (see LedContextBind).
        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = bindingPower + 1, StartSpan = Current.Span });
    }

    /// <summary>
    /// Combines the left operand with the popped right operand into a raw <see cref="ContextBindExpression"/>.
    /// The right operand must be a named variable <c>$name</c>; when it is not, an <c>S0214</c>-equivalent
    /// diagnostic (carrying the <c>@</c> token) is recorded and the left operand is kept as the recovered node
    /// so the surrounding parse continues uncascaded.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand (the bound variable, when valid).</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionCombineContextBind(ParseFrame frame, object? incoming)
    {
        return CombineBind(frame, incoming, "@");
    }

    /// <summary>
    /// Combines the left operand with the popped right operand into a raw <see cref="IndexBindExpression"/>.
    /// The right operand must be a named variable <c>$name</c>; when it is not, an <c>S0214</c>-equivalent
    /// diagnostic (carrying the <c>#</c> token) is recorded and the left operand is kept as the recovered node
    /// so the surrounding parse continues uncascaded.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand (the bound variable, when valid).</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionCombinePositionBind(ParseFrame frame, object? incoming)
    {
        return CombineBind(frame, incoming, "#");
    }

    /// <summary>
    /// Shared combine for the context bind <c>@</c> and the positional bind <c>#</c>: the right operand must
    /// be a named variable, in which case the matching raw bind node is built; otherwise the
    /// <see cref="WellKnownDiagnostics.Jsonata.BindRightNotVariable"/> diagnostic (the reference's S0214) is
    /// recorded with the offending operator token in its message and the left operand is kept as the recovered
    /// node. The two operators differ only in the node built and the token reported.
    /// </summary>
    /// <param name="frame">The expression frame (its operator kind selects the node built).</param>
    /// <param name="incoming">The popped right operand.</param>
    /// <param name="token">The operator token (<c>@</c> or <c>#</c>) reported with the S0214 diagnostic.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult CombineBind(ParseFrame frame, object? incoming, string token)
    {
        JsonataExpression left = frame.Left!;
        JsonataExpression right = Pop<JsonataExpression>(incoming);

        if(right is VariableExpression { Form: VariableForm.Named } variable)
        {
            frame.Left = frame.OperatorKind == JsonataTokenKind.At
                ? new ContextBindExpression(CombineSpans(left.Span, right.Span), left, variable.Name)
                : new IndexBindExpression(CombineSpans(left.Span, right.Span), left, variable.Name);
            frame.Stage = StageLedLoop;

            return StepResult.Continue();
        }

        //The bound side of '@' / '#' must be a '$name' variable; the diagnostic (the reference's S0214) flags
        //it once with the offending operator token, and the left operand is kept as the recovered node so the
        //surrounding parse continues uncascaded.
        _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.BindRightNotVariable, right.Span, $"The right side of '{token}' must be a variable name (start with '$').");
        frame.Left = left;
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Combines the left operand with the popped right operand under the pending binary operator.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionCombineBinary(ParseFrame frame, object? incoming)
    {
        JsonataExpression left = frame.Left!;
        JsonataExpression right = Pop<JsonataExpression>(incoming);

        frame.Left = new BinaryExpression(CombineSpans(left.Span, right.Span), left, MapBinaryOperator(frame.OperatorKind), right);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Wraps the popped operand under the pending unary operator.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped operand.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionWrapUnary(ParseFrame frame, object? incoming)
    {
        JsonataExpression operand = Pop<JsonataExpression>(incoming);

        frame.Left = new UnaryExpression(CombineSpans(frame.OperatorSpan, operand.Span), MapUnaryOperator(frame.OperatorKind), operand);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped block node into the frame's left side and resumes the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped <see cref="BlockExpression"/>.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionAdoptBlock(ParseFrame frame, object? incoming)
    {
        frame.Left = Pop<JsonataExpression>(incoming);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Combines the left side with the popped step sub-expression under the map operator.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped step sub-expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionMapCombine(ParseFrame frame, object? incoming)
    {
        JsonataExpression source = AsPathStep(frame.Left!);
        JsonataExpression step = AsPathStep(Pop<JsonataExpression>(incoming));

        frame.Left = new MapExpression(CombineSpans(source.Span, step.Span), source, step);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Reinterprets an operand of the map operator for its path-step role. A quoted string used as a
    /// navigation step (the leading operand of a path or the right-hand side of a map step) names a field —
    /// equivalently to a backtick-quoted name — so a double- or single-quoted step becomes a
    /// <see cref="NameExpression"/>. An array constructor used as a path step is marked
    /// <see cref="ArrayConstructorExpression.ConsArray"/> (the JSONata <c>consarray</c> flag) so the enclosing
    /// dot/map step keeps its value whole rather than flattening one level, and nested constructor steps
    /// (<c>a.[b.[c]]</c>) compose. A constructor step carrying a trailing keep-array marker
    /// (<c>.[ ... ][]</c>) is still marked cons through the wrapping <see cref="KeepArrayExpression"/>, so the
    /// cons and keep-array markers compose on the same step. Any other operand is returned unchanged.
    /// </summary>
    /// <param name="operand">The map operand.</param>
    /// <returns>A field reference for a string literal, a cons-marked constructor (optionally under a keep-array marker) for an array-constructor step, or the operand unchanged.</returns>
    private static JsonataExpression AsPathStep(JsonataExpression operand)
    {
        return operand switch
        {
            LiteralExpression { Kind: JsonataLiteralKind.String } literal => new NameExpression(literal.Span, literal.Value),
            ArrayConstructorExpression { ConsArray: false } array => new ArrayConstructorExpression(array.Span, array.Elements, ConsArray: true),

            //A keep-array-marked array-constructor step ('.[ ... ][]') is still a cons step: mark the inner
            //constructor cons under the wrapping keep-array marker, so the enclosing step keeps the cons array
            //whole and the keep-array marker still keeps a singleton an array.
            KeepArrayExpression { Source: ArrayConstructorExpression { ConsArray: false } inner } keepArray =>
                new KeepArrayExpression(keepArray.Span, new ArrayConstructorExpression(inner.Span, inner.Elements, ConsArray: true)),
            _ => operand
        };
    }

    /// <summary>Closes a predicate / index application: consumes the <c>]</c> and builds the node over the left side.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped filter / index expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionPredicateClose(ParseFrame frame, object? incoming)
    {
        JsonataExpression source = frame.Left!;
        JsonataExpression filter = Pop<JsonataExpression>(incoming);

        if(Current.Kind != JsonataTokenKind.CloseBracket)
        {
            //A missing close bracket keeps the parsed predicate; the diagnostic flags the gap.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ']' to close the predicate.");
            frame.Left = new PredicateExpression(CombineSpans(source.Span, filter.Span), source, filter);
            frame.Stage = StageLedLoop;

            return StepResult.Continue();
        }

        Advance();
        frame.Left = new PredicateExpression(CombineSpans(source.Span, lastConsumedSpan), source, filter);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Adopts the popped true branch of a conditional. When a <c>:</c> follows, the false branch is
    /// pushed; otherwise the no-else form is built and the climb resumes.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped true-branch expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionConditionalThen(ParseFrame frame, object? incoming)
    {
        frame.ConditionalWhenTrue = Pop<JsonataExpression>(incoming);

        if(Current.Kind == JsonataTokenKind.Colon)
        {
            Advance();
            frame.Stage = StageConditionalElse;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        //The no-else form 'cond ? then' is valid grammar; the false branch is absent.
        JsonataExpression condition = frame.Left!;
        frame.Left = new ConditionalExpression(CombineSpans(condition.Span, frame.ConditionalWhenTrue.Span), condition, frame.ConditionalWhenTrue, WhenFalse: null);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped false branch of a conditional and builds the full ternary node.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped false-branch expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionConditionalElse(ParseFrame frame, object? incoming)
    {
        JsonataExpression condition = frame.Left!;
        JsonataExpression whenTrue = frame.ConditionalWhenTrue!;
        JsonataExpression whenFalse = Pop<JsonataExpression>(incoming);

        frame.Left = new ConditionalExpression(CombineSpans(condition.Span, whenFalse.Span), condition, whenTrue, whenFalse);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses a primary expression at the cursor into the frame's left slot: a literal, a name, a
    /// variable, a block <c>( ... )</c>, the array constructor <c>[ ... ]</c>, the object constructor
    /// <c>{ ... }</c>, the transform <c>| ... | ... |</c>, the lambda <c>function(...){...}</c> /
    /// <c>λ(...){...}</c>, or the leaf path selectors wildcard <c>*</c> and descendant <c>**</c>. The
    /// remaining deferred nud forms (parent <c>%</c>, context bind <c>@</c>, positional bind <c>#</c>) recover
    /// into an error node.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult DispatchPrimaryExpression(ParseFrame frame)
        => Current.Kind switch
        {
            JsonataTokenKind.Number => ExpressionNumber(frame),
            JsonataTokenKind.String => ExpressionString(frame),
            JsonataTokenKind.RegexLiteral => ExpressionRegex(frame),
            JsonataTokenKind.Name => ExpressionName(frame),
            JsonataTokenKind.BacktickName => ExpressionBacktickName(frame),
            JsonataTokenKind.Variable => ExpressionVariable(frame),
            JsonataTokenKind.OpenParen => ExpressionBlock(frame),
            JsonataTokenKind.OpenBracket => ExpressionArrayConstructor(frame),
            JsonataTokenKind.OpenBrace => ExpressionObjectConstructor(frame),
            JsonataTokenKind.Pipe => ExpressionTransform(frame),
            JsonataTokenKind.Star => ExpressionWildcard(frame),
            JsonataTokenKind.StarStar => ExpressionDescendant(frame),
            JsonataTokenKind.KeywordFunction or JsonataTokenKind.Lambda => ExpressionLambda(frame),
            JsonataTokenKind.KeywordAnd or JsonataTokenKind.KeywordOr or JsonataTokenKind.KeywordIn => ExpressionKeywordName(frame),
            JsonataTokenKind.Percent => ExpressionParent(frame),
            _ => StepResult.Done(DispatchPrimaryRecovery(frame))
        };

    /// <summary>
    /// Sets the frame's left operand to the parent operator <c>%</c> in nud (operand) position: a
    /// <see cref="ParentExpression"/> carrying a fresh <see cref="AncestorSlot"/> at level one (the slot's
    /// label and registry index are assigned by the post-parse ancestry pass, which resolves the parent against
    /// the enclosing path's earlier steps). The modulo led form of <c>%</c> is unaffected — it fires only when
    /// a <c>%</c> follows an operand, never here in nud position. Advances to the led loop.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionParent(ParseFrame frame)
    {
        frame.Left = new ParentExpression(Current.Span, new AncestorSlot { Level = 1, Label = -1, Index = -1 });
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Sets the frame's left operand for a reserved keyword operator used in operand position: <c>and</c>,
    /// <c>or</c>, and <c>in</c> name a field (their lexeme is a field reference) when they begin a primary,
    /// exactly as a bare identifier of the same spelling would; in infix position the led loop still treats
    /// them as their operators. Advances to the led loop.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionKeywordName(ParseFrame frame)
    {
        frame.Left = new NameExpression(Current.Span, Current.Value);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Recovers a primary that begins with a token starting no in-scope production.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The error expression standing in for the failed primary.</returns>
    private ErrorExpression DispatchPrimaryRecovery(ParseFrame frame)
    {
        //The deferred nud forms are recognised but not parsed; everything else is a generic missing
        //expression. Both recover the same way (left side kept, rest skipped), but the diagnostic code
        //distinguishes them for tooling. The parent operator '%' is no longer deferred — it has a nud
        //(ExpressionParent) — so only the context / positional binds '@' / '#' in nud position remain here
        //(they are valid only in led position; a leading '@' / '#' is a genuine missing-operand error).
        bool isDeferred = Current.Kind is JsonataTokenKind.At
            or JsonataTokenKind.Hash;

        return isDeferred
            ? RecoverExpression(frame.StartSpan, WellKnownDiagnostics.Jsonata.UnsupportedConstruct, Current.Span, "This JSONata construct is not supported in this build.", "Expression")
            : RecoverExpression(frame.StartSpan, WellKnownDiagnostics.Jsonata.ExpectedExpression, Current.Span, "Expected an expression.", "Expression");
    }

    /// <summary>Sets the frame's left operand to the numeric literal at the cursor and advances to the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionNumber(ParseFrame frame)
    {
        //A number literal whose magnitude overflows the IEEE-754 double range is rejected here as a syntax
        //error (the reference's S0102) rather than parsed to an infinity that only fails later at evaluation.
        if(!double.IsFinite(double.Parse(Current.Value.Span, NumberStyles.Float, CultureInfo.InvariantCulture)))
        {
            Report(WellKnownDiagnostics.Jsonata.NumberOutOfRange, Current.Span, "The number literal is out of the representable range.");
        }

        frame.Left = new LiteralExpression(Current.Span, JsonataLiteralKind.Number, Current.Value);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Sets the frame's left operand to the string literal at the cursor and advances to the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionString(ParseFrame frame)
    {
        frame.Left = new LiteralExpression(Current.Span, JsonataLiteralKind.String, Current.Value);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Sets the frame's left operand to a regular-expression literal <c>/pattern/flags</c> and advances to the
    /// led loop. The lexer decoded the token value as <c>flags '/' pattern</c> (the flags are ASCII letters and
    /// never contain a <c>/</c>, so the first <c>/</c> separates them), and this nud splits it back into the
    /// pattern and the flags the evaluator compiles the regex value from.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionRegex(ParseFrame frame)
    {
        SourceSpan span = Current.Span;
        ReadOnlySpan<byte> value = Current.Value.Span;
        int separator = value.IndexOf((byte)'/');

        //The lexer always emits a '/' separator after the (possibly empty) flags, so the separator is present;
        //the flags are the bytes before it and the pattern the bytes after it.
        Utf8String flags = Pool.Intern(value[..separator]);
        Utf8String pattern = Pool.Intern(value[(separator + 1)..]);

        frame.Left = new RegexExpression(span, pattern, flags);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Sets the frame's left operand for a bare name: the reserved words <c>true</c> / <c>false</c> /
    /// <c>null</c> become literals, every other name a field reference. Advances to the led loop.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionName(ParseFrame frame)
    {
        SourceSpan span = Current.Span;
        Utf8String value = Current.Value;

        frame.Left = value.Span switch
        {
            _ when value.Span.SequenceEqual("true"u8) => new LiteralExpression(span, JsonataLiteralKind.Boolean, value),
            _ when value.Span.SequenceEqual("false"u8) => new LiteralExpression(span, JsonataLiteralKind.Boolean, value),
            _ when value.Span.SequenceEqual("null"u8) => new LiteralExpression(span, JsonataLiteralKind.Null, value),
            _ => new NameExpression(span, value)
        };
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Sets the frame's left operand to a backtick-quoted field reference and advances to the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionBacktickName(ParseFrame frame)
    {
        frame.Left = new NameExpression(Current.Span, Current.Value);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Sets the frame's left operand to the variable at the cursor and advances to the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionVariable(ParseFrame frame)
    {
        SourceSpan span = Current.Span;
        Utf8String value = Current.Value;

        //The lexer decodes the variable Value: empty for the bare context focus '$', a single '$' for the
        //root '$$', and the name without the leading '$' otherwise.
        (VariableForm form, Utf8String name) = value.IsEmpty
            ? (VariableForm.ContextFocus, default)
            : value.Span.SequenceEqual("$"u8)
                ? (VariableForm.Root, default)
                : (VariableForm.Named, value);

        frame.Left = new VariableExpression(span, form, name);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Begins a block <c>( ... )</c> primary, pushing a variadic statement-list frame to collect its <c>;</c>-separated statements.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionBlock(ParseFrame frame)
    {
        //Every '( ... )' is a block: consume the '(' and push a variadic statement-list frame to collect the
        //';'-separated statements. A single-expression '(e)' is a one-statement block, and '()' is the empty
        //block. The expression frame resumes at StageAdoptBlock to adopt the built node.
        SourceSpan open = Current.Span;
        Advance();
        frame.Stage = StageAdoptBlock;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.BlockStatementList, StartSpan = open, Statements = [] });
    }

    /// <summary>
    /// Begins a lambda <c>function ( params ) { body }</c> primary (the Greek <c>λ</c> is an alias for
    /// <c>function</c>): consumes the keyword, then pushes a <see cref="ParseFrameKind.LambdaDefinition"/>
    /// frame to consume the parameter list and parse the body. The expression frame resumes at
    /// <see cref="StageAdoptLambda"/> to adopt the built node.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionLambda(ParseFrame frame)
    {
        SourceSpan keyword = Current.Span;
        Utf8String keywordLexeme = Current.Value;
        Advance();
        frame.Stage = StageAdoptLambda;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.LambdaDefinition, StartSpan = keyword, KeywordLexeme = keywordLexeme, Parameters = [], Stage = StageLambdaOpenParen });
    }

    /// <summary>Adopts the popped lambda node — or the field-name reference a <c>function</c> / <c>λ</c> keyword not followed by <c>(</c> recovered to — into the frame's left side and resumes the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped <see cref="LambdaExpression"/> or <see cref="NameExpression"/>.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionAdoptLambda(ParseFrame frame, object? incoming)
    {
        frame.Left = Pop<JsonataExpression>(incoming);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Begins an array constructor <c>[ ... ]</c> primary: consumes the opening <c>[</c>, then pushes a
    /// variadic <see cref="ParseFrameKind.ElementList"/> frame to parse the comma-separated elements. The
    /// expression frame resumes at <see cref="StageAdoptArrayConstructor"/> to adopt the built node.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionArrayConstructor(ParseFrame frame)
    {
        SourceSpan open = Current.Span;
        Advance();
        frame.Stage = StageAdoptArrayConstructor;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ElementList, StartSpan = open, Elements = [] });
    }

    /// <summary>Adopts the popped array-constructor node into the frame's left side and resumes the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped <see cref="ArrayConstructorExpression"/>.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionAdoptArrayConstructor(ParseFrame frame, object? incoming)
    {
        frame.Left = Pop<JsonataExpression>(incoming);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Begins an object constructor <c>{ ... }</c> primary: consumes the opening <c>{</c>, then pushes a
    /// variadic <see cref="ParseFrameKind.ObjectMemberList"/> frame to parse the comma-separated key/value
    /// members. The expression frame resumes at <see cref="StageAdoptObjectConstructor"/> to adopt the
    /// built node.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionObjectConstructor(ParseFrame frame)
    {
        SourceSpan open = Current.Span;
        Advance();
        frame.Stage = StageAdoptObjectConstructor;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ObjectMemberList, StartSpan = open, Members = [] });
    }

    /// <summary>Adopts the popped object-constructor node into the frame's left side and resumes the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped <see cref="ObjectConstructorExpression"/>.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionAdoptObjectConstructor(ParseFrame frame, object? incoming)
    {
        frame.Left = Pop<JsonataExpression>(incoming);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Absorbs the path-step group-by operator <c>{</c> after an operand (the led form <c>path{ ... }</c>):
    /// consumes the opening <c>{</c>, then pushes the same variadic
    /// <see cref="ParseFrameKind.ObjectMemberList"/> frame the prefix object constructor uses to parse the
    /// comma-separated key/value members (so the member parsing and its recovery are shared, not duplicated).
    /// The already-parsed operand in <see cref="ParseFrame.Left"/> is the grouping source; the expression
    /// frame resumes at <see cref="StageAdoptObjectGroup"/> to attach it to the built node.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedObjectGroup(ParseFrame frame)
    {
        if(frame.Left is ObjectConstructorExpression { Source: not null })
        {
            //A path step carries at most one grouping expression.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.InvalidGroupingStep, Current.Span, "A path step cannot have more than one grouping expression.");
        }

        SourceSpan open = Current.Span;
        Advance();
        frame.Stage = StageAdoptObjectGroup;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ObjectMemberList, StartSpan = open, Members = [] });
    }

    /// <summary>
    /// Attaches the frame's already-parsed operand as the grouping source of the popped object-constructor
    /// node (rebuilding it with that source and the same parsed members), then resumes the led loop so a
    /// following operator continues to climb over the group-by. The member-list frame builds the node in its
    /// prefix (sourceless) shape; this step is the single place the led path-step form binds the source, so
    /// the member parsing stays shared with the prefix constructor.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped <see cref="ObjectConstructorExpression"/> (sourceless, as built by the shared member-list frame).</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionAdoptObjectGroup(ParseFrame frame, object? incoming)
    {
        JsonataExpression source = frame.Left!;
        ObjectConstructorExpression members = Pop<ObjectConstructorExpression>(incoming);

        frame.Left = new ObjectConstructorExpression(CombineSpans(source.Span, members.Span), members.Members, source);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Begins a transform <c>| location | update [, delete] |</c> primary (a prefix form): consumes the
    /// opening <c>|</c>, remembers its span for the node extent, and pushes the location-pattern
    /// sub-expression at the lowest binding power so it absorbs up to the <c>|</c> separator. The expression
    /// frame resumes at <see cref="StageTransformUpdate"/> to consume the separator and parse the update
    /// clause.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionTransform(ParseFrame frame)
    {
        frame.OperatorSpan = Current.Span;
        Advance();
        frame.Stage = StageTransformUpdate;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Resumes a transform after its location pattern: holds the pattern, consumes the <c>|</c> separator, and
    /// pushes the update-clause sub-expression. A missing separator records a missing-closer diagnostic and
    /// recovers by keeping the pattern as the frame's value, so the surrounding parse continues uncascaded.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped location-pattern expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionTransformUpdate(ParseFrame frame, object? incoming)
    {
        frame.TransformPattern = Pop<JsonataExpression>(incoming);

        if(Current.Kind != JsonataTokenKind.Pipe)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected '|' between the location and update clauses of a transform.");
            frame.Left = frame.TransformPattern;
            frame.Stage = StageLedLoop;

            return StepResult.Continue();
        }

        Advance();
        frame.Stage = StageTransformDelete;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Resumes a transform after its update clause: holds the update, then either pushes the optional delete
    /// clause (when a <c>,</c> follows) and resumes at <see cref="StageTransformClose"/>, or builds the
    /// delete-less transform node directly once the closing <c>|</c> is consumed.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped update-clause expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionTransformDelete(ParseFrame frame, object? incoming)
    {
        frame.TransformUpdate = Pop<JsonataExpression>(incoming);

        if(Current.Kind == JsonataTokenKind.Comma)
        {
            Advance();
            frame.Stage = StageTransformClose;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        return CompleteTransform(frame, delete: null);
    }

    /// <summary>Resumes a transform after its optional delete clause and builds the three-clause transform node.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped delete-clause expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionTransformClose(ParseFrame frame, object? incoming)
    {
        return CompleteTransform(frame, Pop<JsonataExpression>(incoming));
    }

    /// <summary>
    /// Consumes the closing <c>|</c> and builds the <see cref="TransformExpression"/> over the held pattern
    /// and update and the given (possibly absent) delete clause, then resumes the led loop. A missing closing
    /// <c>|</c> records a missing-closer diagnostic and builds the node anyway over the clauses parsed so far,
    /// so the surrounding parse continues uncascaded.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="delete">The parsed delete clause, or <see langword="null"/> for a delete-less transform.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult CompleteTransform(ParseFrame frame, JsonataExpression? delete)
    {
        if(Current.Kind == JsonataTokenKind.Pipe)
        {
            Advance();
        }
        else
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected a closing '|' to end the transform.");
        }

        frame.Left = new TransformExpression(CombineSpans(frame.OperatorSpan, lastConsumedSpan), frame.TransformPattern!, frame.TransformUpdate!, delete);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>
    /// Absorbs the order-by operator <c>^</c> after an operand: consumes the <c>^</c> and the opening <c>(</c>
    /// and pushes a variadic <see cref="ParseFrameKind.SortTermList"/> frame to collect the comma-separated
    /// order-by terms. The already-parsed operand in <see cref="ParseFrame.Left"/> is the sort source; the
    /// expression frame resumes at <see cref="StageAdoptSort"/> to build the sort over it. A missing <c>(</c>
    /// records a diagnostic and keeps the operand, so the surrounding parse continues uncascaded.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LedSort(ParseFrame frame)
    {
        Advance();
        if(Current.Kind != JsonataTokenKind.OpenParen)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.ExpectedExpression, Current.Span, "Expected '(' after the order-by operator '^'.");

            return StepResult.Done(frame.Left!);
        }

        SourceSpan open = Current.Span;
        Advance();
        frame.Stage = StageAdoptSort;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.SortTermList, StartSpan = open, SortTerms = [] });
    }

    /// <summary>Builds the order-by node over the frame's source (its left side) and the popped term list, then resumes the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped order-by terms.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionAdoptSort(ParseFrame frame, object? incoming)
    {
        JsonataExpression source = frame.Left!;
        List<SortTerm> terms = Pop<List<SortTerm>>(incoming);

        frame.Left = new SortExpression(CombineSpans(source.Span, lastConsumedSpan), source, terms);
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Dispatches one step of an element-list frame on its stage.</summary>
    /// <param name="frame">The element-list frame.</param>
    /// <param name="incoming">A popped element expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepElementList(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageElementListFirst => ElementListFirst(frame),
            StageElementListAfterElement => ElementListAfterElement(frame, incoming),
            _ => throw new JsonataParseException("Element list reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Handles the first element: an immediate <c>]</c> is the empty array; otherwise the first element is pushed at the lowest binding power.</summary>
    /// <param name="frame">The element-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ElementListFirst(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.CloseBracket)
        {
            Advance();

            return StepResult.Done(new ArrayConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Elements!));
        }

        frame.Stage = StageElementListAfterElement;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Appends the popped element; a <c>,</c> loops to the next element, a <c>]</c> closes the constructor,
    /// and anything else records a missing-closer diagnostic and closes with the elements parsed so far
    /// (the partial node is kept, never discarded into an error node).
    /// </summary>
    /// <param name="frame">The element-list frame.</param>
    /// <param name="incoming">The popped element expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ElementListAfterElement(ParseFrame frame, object? incoming)
    {
        frame.Elements!.Add(Pop<JsonataExpression>(incoming));

        if(Current.Kind == JsonataTokenKind.Comma)
        {
            Advance();

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        if(Current.Kind != JsonataTokenKind.CloseBracket)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ',' or ']' to close the array constructor.");

            return StepResult.Done(new ArrayConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Elements!));
        }

        Advance();

        return StepResult.Done(new ArrayConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Elements!));
    }

    /// <summary>Dispatches one step of an object member-list frame on its stage.</summary>
    /// <param name="frame">The object member-list frame.</param>
    /// <param name="incoming">A popped key or value expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepObjectMemberList(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageObjectMemberListFirstKey => MemberListFirstKey(frame),
            StageObjectMemberListAfterKey => MemberListAfterKey(frame, incoming),
            StageObjectMemberListAfterValue => MemberListAfterValue(frame, incoming),
            _ => throw new JsonataParseException("Object member list reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Handles the first member: an immediate <c>}</c> is the empty object; otherwise the first key is pushed at the lowest binding power.</summary>
    /// <param name="frame">The object member-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult MemberListFirstKey(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.CloseBrace)
        {
            Advance();

            return StepResult.Done(new ObjectConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Members!));
        }

        frame.Stage = StageObjectMemberListAfterKey;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Holds the popped key, requires the <c>:</c> separator (else records a JS0001 expected-expression
    /// diagnostic, keeps the pending key paired with an <see cref="ErrorExpression"/> placeholder value as
    /// a partial member, resyncs to the next member boundary, and closes), then pushes the value expression
    /// at the lowest binding power.
    /// </summary>
    /// <param name="frame">The object member-list frame.</param>
    /// <param name="incoming">The popped key expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult MemberListAfterKey(ParseFrame frame, object? incoming)
    {
        frame.PendingKey = Pop<JsonataExpression>(incoming);

        if(Current.Kind != JsonataTokenKind.Colon)
        {
            //A missing ':' between a key and its value keeps the pending key as a partial member paired with
            //an error placeholder value, so the partially-typed member survives in the recovered tree. The
            //diagnostic flags the gap once and the unparsed run is resynced to the next member boundary so a
            //single localized JS0001 is produced rather than a cascade into the enclosing frame.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.ExpectedExpression, Current.Span, "Expected ':' between an object key and its value.");
            frame.Members!.Add((frame.PendingKey!, new ErrorExpression(Current.Span, Utf8Strings.From("Expression"), [], [])));
            frame.PendingKey = null;
            _ = ResyncTo(Current.Span, out _);

            return StepResult.Done(new ObjectConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Members!));
        }

        Advance();
        frame.Stage = StageObjectMemberListAfterValue;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Appends the popped value under its held key; a <c>,</c> loops to the next key, a <c>}</c> closes the
    /// constructor, and anything else records a missing-closer diagnostic and closes with the members parsed
    /// so far (the partial node is kept, never discarded into an error node).
    /// </summary>
    /// <param name="frame">The object member-list frame.</param>
    /// <param name="incoming">The popped value expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult MemberListAfterValue(ParseFrame frame, object? incoming)
    {
        frame.Members!.Add((frame.PendingKey!, Pop<JsonataExpression>(incoming)));
        frame.PendingKey = null;

        if(Current.Kind == JsonataTokenKind.Comma)
        {
            Advance();
            frame.Stage = StageObjectMemberListAfterKey;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        if(Current.Kind != JsonataTokenKind.CloseBrace)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ',' or '}' to close the object constructor.");

            return StepResult.Done(new ObjectConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Members!));
        }

        Advance();

        return StepResult.Done(new ObjectConstructorExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Members!));
    }

    /// <summary>Dispatches one step of a block statement-list frame on its stage.</summary>
    /// <param name="frame">The block statement-list frame.</param>
    /// <param name="incoming">A popped statement expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepBlockStatementList(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageBlockStatementListFirst => BlockStatementListFirst(frame),
            StageBlockStatementListAfterStatement => BlockStatementListAfterStatement(frame, incoming),
            _ => throw new JsonataParseException("Block statement list reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Handles the first statement: an immediate <c>)</c> is the empty block <c>()</c>; otherwise the first statement is pushed at the lowest binding power.</summary>
    /// <param name="frame">The block statement-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlockStatementListFirst(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.CloseParen)
        {
            Advance();

            return StepResult.Done(new BlockExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Statements!));
        }

        frame.Stage = StageBlockStatementListAfterStatement;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Appends the popped statement; a <c>;</c> loops to the next statement (or closes the block when a
    /// <c>)</c> immediately follows, so a trailing <c>;</c> adds no empty statement), a <c>)</c> closes the
    /// block, and anything else records a missing-closer diagnostic and closes with the statements parsed so
    /// far (the partial node is kept, never discarded into an error node).
    /// </summary>
    /// <param name="frame">The block statement-list frame.</param>
    /// <param name="incoming">The popped statement expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlockStatementListAfterStatement(ParseFrame frame, object? incoming)
    {
        frame.Statements!.Add(Pop<JsonataExpression>(incoming));

        if(Current.Kind == JsonataTokenKind.Semicolon)
        {
            Advance();

            //A trailing ';' immediately before the ')' closes the block with no extra statement.
            if(Current.Kind == JsonataTokenKind.CloseParen)
            {
                Advance();

                return StepResult.Done(new BlockExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Statements!));
            }

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        if(Current.Kind != JsonataTokenKind.CloseParen)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ';' or ')' to close the block.");

            return StepResult.Done(new BlockExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Statements!));
        }

        Advance();

        return StepResult.Done(new BlockExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Statements!));
    }

    /// <summary>Dispatches one step of a lambda-definition frame on its stage.</summary>
    /// <param name="frame">The lambda-definition frame.</param>
    /// <param name="incoming">The popped body expression on resume of the body stage.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepLambdaDefinition(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageLambdaOpenParen => LambdaOpenParen(frame),
            StageLambdaParameters => LambdaParameters(frame),
            StageLambdaSignature => LambdaSignature(frame),
            StageLambdaBody => LambdaBody(frame, incoming),
            _ => throw new JsonataParseException("Lambda definition reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>
    /// Consumes the opening <c>(</c> of a lambda's parameter list and advances to the parameter-collecting
    /// stage. A <c>function</c> / <c>λ</c> keyword NOT immediately followed by <c>(</c> is not a lambda at all:
    /// the reference tokenises these keywords as plain names and only its infix-<c>(</c> handler promotes a
    /// <c>function</c> / <c>λ</c>-named operand to a lambda. So a keyword without a following <c>(</c> recovers
    /// here as an ordinary field-name reference carrying the keyword's lexeme — making, for example,
    /// <c>unknown(function)</c> a call of the non-function <c>unknown</c> with the field reference
    /// <c>function</c> as its argument (a runtime T1006), rather than a parse error.
    /// </summary>
    /// <param name="frame">The lambda-definition frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LambdaOpenParen(ParseFrame frame)
    {
        if(Current.Kind != JsonataTokenKind.OpenParen)
        {
            return StepResult.Done(new NameExpression(frame.StartSpan, frame.KeywordLexeme));
        }

        Advance();
        frame.Stage = StageLambdaParameters;

        return StepResult.Continue();
    }

    /// <summary>
    /// Collects one parameter per step: a <c>)</c> closes the list and advances to the signature stage; a
    /// comma is skipped between parameters; a <c>$name</c> variable token contributes its bare name; a
    /// non-variable token records a JS0007 diagnostic and is resynced past. The optional
    /// <c>&lt;signature&gt;</c> after the parameters is scanned by the signature stage.
    /// </summary>
    /// <param name="frame">The lambda-definition frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LambdaParameters(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.CloseParen)
        {
            Advance();
            frame.Stage = StageLambdaSignature;

            return StepResult.Continue();
        }

        if(Current.Kind == JsonataTokenKind.Comma)
        {
            //A comma between parameters is skipped; the next step reads the following parameter or the ')'.
            Advance();

            return StepResult.Continue();
        }

        if(Current.Kind == JsonataTokenKind.EndOfInput)
        {
            //An unterminated parameter list flags the missing closer and advances to the signature stage so
            //the lambda still recovers into a node rather than spinning; that stage sees no '<' and opens the
            //body from the cursor.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ')' to close the function's parameter list.");
            frame.Stage = StageLambdaSignature;

            return StepResult.Continue();
        }

        if(Current.Kind == JsonataTokenKind.Variable)
        {
            //Any variable token is a valid parameter name, including the bare '$' and root '$$' (which carry
            //an empty / single-'$' decoded value); they bind positionally under that value. The body still
            //resolves '$' and '$$' by their variable form (focus and root), so such a parameter consumes its
            //argument slot but is not separately readable by name.
            frame.Parameters!.Add(Current.Value);
            Advance();

            return StepResult.Continue();
        }

        //A non-variable parameter (a literal, a field name, an operator, ...) is not a valid parameter name;
        //the diagnostic flags it once and the offending token run is resynced past so the parse continues.
        _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.LambdaParameterNotVariable, Current.Span, "A function parameter must be a variable name (start with '$').");
        int before = index;
        Advance();
        if(index == before)
        {
            //The cursor is clamped at the final token; advance to the signature stage to terminate (it sees
            //no '<' and opens the body from the clamped cursor).
            frame.Stage = StageLambdaSignature;

            return StepResult.Continue();
        }

        return StepResult.Continue();
    }

    /// <summary>
    /// Scans the optional bracketed type signature <c>&lt;...&gt;</c> that may follow a lambda's parameter
    /// list, one token per step so the scan stays well within the per-step lookahead and survives a
    /// <see cref="ParseStatus.NeedMore"/> suspension: the running buffer and the angle-bracket depth live on
    /// the frame. When the current token is not <c>&lt;</c> the lambda has no signature and the body opens
    /// straight away. Otherwise each step appends the current token's value to the frame's buffer and adjusts
    /// the depth — a <c>&lt;</c> deepens it, a <c>&gt;</c> shallows it — exactly as the reference parser
    /// reassembles the signature string from the token stream; the depth starts at one on the opening
    /// <c>&lt;</c> and the scan ends when it returns to zero on the matching <c>&gt;</c>. The reassembled
    /// string is then validated by <see cref="JsonataSignature.Parse"/>: a malformed signature records a
    /// JS0008 diagnostic (and the signature is dropped) while a well-formed one is recorded on the frame's
    /// buffer for the body stage to stamp on the lambda node. A <c>{</c> or end-of-input before the matching
    /// <c>&gt;</c> flags an unterminated signature and recovers into the body.
    /// </summary>
    /// <param name="frame">The lambda-definition frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LambdaSignature(ParseFrame frame)
    {
        if(frame.SignatureBuffer is null)
        {
            if(Current.Kind != JsonataTokenKind.Less)
            {
                //No '<' after the parameter list: the lambda declares no signature, so open the body directly.
                return OpenLambdaBody(frame);
            }

            //The opening '<' starts the signature at depth one; seed the buffer with its value and step on.
            frame.SignatureBuffer = new StringBuilder();
            frame.SignatureBuffer.Append(Current.Value.ToString());
            frame.SignatureDepth = 1;
            Advance();

            return StepResult.Continue();
        }

        if(Current.Kind is JsonataTokenKind.OpenBrace or JsonataTokenKind.EndOfInput)
        {
            //The signature reached the body brace (or the end) before its closing '>'; flag the gap, drop the
            //partial signature, and open the body from the cursor so the lambda still recovers into a node.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.InvalidFunctionSignature, Current.Span, "Expected '>' to close the function-parameter type signature.");
            frame.SignatureBuffer = null;

            return OpenLambdaBody(frame);
        }

        //Append this token's value and adjust the depth on '<' / '>' exactly as the reference reassembles the
        //signature string; the closing '>' that returns the depth to zero ends the scan.
        frame.SignatureBuffer.Append(Current.Value.ToString());
        frame.SignatureDepth += DepthDelta(Current.Kind);
        Advance();

        if(frame.SignatureDepth > 0)
        {
            return StepResult.Continue();
        }

        return FinishLambdaSignature(frame);
    }

    /// <summary>
    /// Finalises a fully-scanned signature: the reassembled bracketed string is validated by
    /// <see cref="JsonataSignature.Parse"/>, and a malformed signature (an S0401 / S0402 / too-many-parameters
    /// reject) records a JS0008 diagnostic and is dropped, while a well-formed signature is retained on the
    /// frame for the body stage to stamp on the lambda node. The body opens either way.
    /// </summary>
    /// <param name="frame">The lambda-definition frame whose buffer holds the reassembled signature string.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult FinishLambdaSignature(ParseFrame frame)
    {
        string signature = frame.SignatureBuffer!.ToString();
        try
        {
            //Parse the reassembled signature so a malformed one is rejected at parse time (the reference parses
            //the signature here too); the parsed result is discarded — the evaluator re-parses the stored
            //string when it builds the lambda value.
            _ = JsonataSignature.Parse(signature);
        }
        catch(JsonataErrorException)
        {
            //A structural signature error (a parameterised type on a non-container, or a bracket nested in a
            //union): flag it and drop the signature so the lambda still parses without one.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.InvalidFunctionSignature, frame.StartSpan, "The function-parameter type signature is malformed.");
            frame.SignatureBuffer = null;
        }
        catch(JsonataParseException)
        {
            //The signature declares more parameters than the parser bounds; flag it and drop the signature.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.InvalidFunctionSignature, frame.StartSpan, "The function-parameter type signature declares too many parameters.");
            frame.SignatureBuffer = null;
        }

        return OpenLambdaBody(frame);
    }

    /// <summary>
    /// Returns the angle-bracket depth change a token contributes while scanning a lambda type signature: a
    /// <c>&lt;</c> deepens the nesting by one, a <c>&gt;</c> shallows it by one, and every other token leaves
    /// it unchanged.
    /// </summary>
    /// <param name="kind">The token kind at the cursor.</param>
    /// <returns><c>+1</c> for <c>&lt;</c>, <c>-1</c> for <c>&gt;</c>, otherwise <c>0</c>.</returns>
    private static int DepthDelta(JsonataTokenKind kind)
        => kind switch
        {
            JsonataTokenKind.Less => 1,
            JsonataTokenKind.Greater => -1,
            _ => 0
        };

    /// <summary>
    /// Consumes the body's opening <c>{</c> and pushes the body expression frame at the lowest binding power;
    /// a missing <c>{</c> records a diagnostic and pushes the body from the cursor so a malformed header still
    /// yields a lambda node. The optional <c>&lt;signature&gt;</c> has already been scanned by the signature
    /// stage, so the cursor is at the body brace (or wherever recovery left it).
    /// </summary>
    /// <param name="frame">The lambda-definition frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult OpenLambdaBody(ParseFrame frame)
    {
        if(Current.Kind != JsonataTokenKind.OpenBrace)
        {
            //A missing body brace keeps the lambda but flags the gap; the body parses from the cursor.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected '{' to open the function body.");
            frame.Stage = StageLambdaBody;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        Advance();
        frame.Stage = StageLambdaBody;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Adopts the popped body expression, consumes the closing <c>}</c> (a missing one records a diagnostic
    /// and keeps the parsed body), and builds the lambda node over the collected parameter names and the
    /// reassembled type signature (empty when the lambda declared none).
    /// </summary>
    /// <param name="frame">The lambda-definition frame.</param>
    /// <param name="incoming">The popped body expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult LambdaBody(ParseFrame frame, object? incoming)
    {
        JsonataExpression body = Pop<JsonataExpression>(incoming);
        Utf8String signature = LambdaSignatureText(frame);

        if(Current.Kind != JsonataTokenKind.CloseBrace)
        {
            //A missing '}' keeps the parsed body; the diagnostic flags the gap.
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected '}' to close the function body.");

            return StepResult.Done(new LambdaExpression(CombineSpans(frame.StartSpan, body.Span), frame.Parameters!, body, signature));
        }

        Advance();

        return StepResult.Done(new LambdaExpression(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Parameters!, body, signature));
    }

    /// <summary>
    /// Returns the reassembled bracketed type-signature text the lambda-definition frame scanned, interned
    /// through the parser's pool; the empty <see cref="Utf8String"/> when the lambda declared no signature or
    /// its signature was dropped as malformed.
    /// </summary>
    /// <param name="frame">The lambda-definition frame whose buffer holds the reassembled signature string.</param>
    /// <returns>The interned signature text, or the empty string.</returns>
    private Utf8String LambdaSignatureText(ParseFrame frame)
    {
        return frame.SignatureBuffer is StringBuilder buffer
            ? Pool.Intern(buffer.ToString())
            : default;
    }

    /// <summary>Dispatches one step of an argument-list frame on its stage.</summary>
    /// <param name="frame">The argument-list frame.</param>
    /// <param name="incoming">A popped argument expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepArgumentList(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageArgumentListFirst => ArgumentListFirst(frame),
            StageArgumentListAfterArgument => ArgumentListAfterArgument(frame, incoming),
            StageArgumentListReadNext => ReadArgument(frame),
            _ => throw new JsonataParseException("Argument list reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Handles the first argument: an immediate <c>)</c> is the empty argument list; otherwise the first entry (a normal argument or a leading <c>?</c> partial-application placeholder) is read.</summary>
    /// <param name="frame">The argument-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ArgumentListFirst(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.CloseParen)
        {
            Advance();

            return StepResult.Done(frame.Arguments!);
        }

        return ReadArgument(frame);
    }

    /// <summary>
    /// Appends the popped argument expression, then continues the argument list: a <c>,</c> reads the next
    /// entry (which may itself be a placeholder), a <c>)</c> closes the call, and anything else records a
    /// missing-closer diagnostic and closes with the arguments parsed so far (the partial argument list is
    /// kept, never discarded into an error node). A placeholder entry is appended in
    /// <see cref="ReadArgument"/> without a sub-frame, so this resume runs only for a normal argument
    /// expression.
    /// </summary>
    /// <param name="frame">The argument-list frame.</param>
    /// <param name="incoming">The popped argument expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ArgumentListAfterArgument(ParseFrame frame, object? incoming)
    {
        frame.Arguments!.Add(Pop<JsonataExpression>(incoming));

        if(Current.Kind == JsonataTokenKind.Comma)
        {
            Advance();
            frame.Stage = StageArgumentListReadNext;

            return StepResult.Continue();
        }

        return CloseArgumentList(frame);
    }

    /// <summary>
    /// Reads one argument-list entry at the cursor. A leading <c>?</c> (a standalone
    /// <see cref="JsonataTokenKind.Question"/> token) in argument position is the partial-application
    /// placeholder: it is consumed and a <see cref="PlaceholderExpression"/> is appended directly with no
    /// sub-frame, and a following <c>,</c> is consumed and the frame re-enters this stage to read the next
    /// entry (one placeholder per step, so the resumable driver re-checks lookahead between entries and never
    /// recurses). A <c>?</c> that follows an expression is the ternary <c>? :</c> handled by that operator's
    /// led inside the pushed expression frame, never reaching here. Any other token begins a normal argument
    /// expression, pushed at the lowest binding power.
    /// </summary>
    /// <param name="frame">The argument-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ReadArgument(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.Question)
        {
            SourceSpan placeholder = Current.Span;
            Advance();
            frame.Arguments!.Add(new PlaceholderExpression(placeholder));

            if(Current.Kind == JsonataTokenKind.Comma)
            {
                Advance();
                frame.Stage = StageArgumentListReadNext;

                return StepResult.Continue();
            }

            return CloseArgumentList(frame);
        }

        frame.Stage = StageArgumentListAfterArgument;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Closes the argument list at the cursor: a <c>)</c> closes the call, and anything else records a
    /// missing-closer diagnostic and closes with the arguments parsed so far (the partial argument list is
    /// kept, never discarded into an error node).
    /// </summary>
    /// <param name="frame">The argument-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult CloseArgumentList(ParseFrame frame)
    {
        if(Current.Kind != JsonataTokenKind.CloseParen)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ',' or ')' to close the function-call arguments.");

            return StepResult.Done(frame.Arguments!);
        }

        Advance();

        return StepResult.Done(frame.Arguments!);
    }

    /// <summary>Dispatches one step of a sort-term-list frame on its stage.</summary>
    /// <param name="frame">The sort-term-list frame.</param>
    /// <param name="incoming">A popped term key expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepSortTermList(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            StageSortTermListFirst => SortTermListFirst(frame),
            StageSortTermListAfterTerm => SortTermListAfterTerm(frame, incoming),
            _ => throw new JsonataParseException("Sort-term list reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>
    /// Handles the first order-by term: an immediate <c>)</c> is the empty term list; otherwise the optional
    /// direction prefix (<c>&lt;</c> / <c>&gt;</c>) is read and the term's key expression is pushed at the
    /// lowest binding power.
    /// </summary>
    /// <param name="frame">The sort-term-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult SortTermListFirst(ParseFrame frame)
    {
        if(Current.Kind == JsonataTokenKind.CloseParen)
        {
            Advance();

            return StepResult.Done(frame.SortTerms!);
        }

        frame.PendingSortDirection = ReadSortDirection();
        frame.Stage = StageSortTermListAfterTerm;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
    }

    /// <summary>
    /// Appends the popped term (the held direction plus its key expression); a <c>,</c> reads the next term's
    /// optional direction and loops, a <c>)</c> closes the list, and anything else records a missing-closer
    /// diagnostic and closes with the terms parsed so far (the partial list is kept, never discarded).
    /// </summary>
    /// <param name="frame">The sort-term-list frame.</param>
    /// <param name="incoming">The popped key expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult SortTermListAfterTerm(ParseFrame frame, object? incoming)
    {
        frame.SortTerms!.Add(new SortTerm(frame.PendingSortDirection, Pop<JsonataExpression>(incoming)));

        if(Current.Kind == JsonataTokenKind.Comma)
        {
            Advance();
            frame.PendingSortDirection = ReadSortDirection();

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinBindingPower = 0, StartSpan = Current.Span });
        }

        if(Current.Kind != JsonataTokenKind.CloseParen)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Jsonata.MissingCloser, Current.Span, "Expected ',' or ')' to close the order-by terms.");

            return StepResult.Done(frame.SortTerms!);
        }

        Advance();

        return StepResult.Done(frame.SortTerms!);
    }

    /// <summary>Reads an optional order-by direction prefix at the cursor: a leading <c>&lt;</c> is ascending and a leading <c>&gt;</c> is descending (both consumed); no prefix is ascending.</summary>
    /// <returns>The direction the prefix selected, defaulting to ascending.</returns>
    private SortDirection ReadSortDirection()
    {
        if(Current.Kind == JsonataTokenKind.Less)
        {
            Advance();

            return SortDirection.Ascending;
        }

        if(Current.Kind == JsonataTokenKind.Greater)
        {
            Advance();

            return SortDirection.Descending;
        }

        return SortDirection.Ascending;
    }

    /// <summary>Sets the frame's left operand to the wildcard selector at the cursor and advances to the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionWildcard(ParseFrame frame)
    {
        frame.Left = new WildcardExpression(Current.Span);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Sets the frame's left operand to the descendant selector at the cursor and advances to the led loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionDescendant(ParseFrame frame)
    {
        frame.Left = new DescendantExpression(Current.Span);
        Advance();
        frame.Stage = StageLedLoop;

        return StepResult.Continue();
    }

    /// <summary>Maps a binary-operator token kind to its <see cref="BinaryOperator"/>.</summary>
    /// <param name="kind">The operator token kind.</param>
    /// <returns>The binary operator.</returns>
    private static BinaryOperator MapBinaryOperator(JsonataTokenKind kind)
    {
        return kind switch
        {
            JsonataTokenKind.Plus => BinaryOperator.Add,
            JsonataTokenKind.Minus => BinaryOperator.Subtract,
            JsonataTokenKind.Star => BinaryOperator.Multiply,
            JsonataTokenKind.Slash => BinaryOperator.Divide,
            JsonataTokenKind.Percent => BinaryOperator.Modulo,
            JsonataTokenKind.Ampersand => BinaryOperator.Concat,
            JsonataTokenKind.Equal => BinaryOperator.Equal,
            JsonataTokenKind.NotEqual => BinaryOperator.NotEqual,
            JsonataTokenKind.Less => BinaryOperator.Less,
            JsonataTokenKind.LessEqual => BinaryOperator.LessOrEqual,
            JsonataTokenKind.Greater => BinaryOperator.Greater,
            JsonataTokenKind.GreaterEqual => BinaryOperator.GreaterOrEqual,
            JsonataTokenKind.KeywordIn => BinaryOperator.In,
            JsonataTokenKind.KeywordAnd => BinaryOperator.And,
            JsonataTokenKind.KeywordOr => BinaryOperator.Or,
            _ => throw new JsonataParseException($"Token {kind} is not a binary operator.")
        };
    }

    /// <summary>Maps a unary-operator token kind to its <see cref="UnaryOperator"/>.</summary>
    /// <param name="kind">The operator token kind.</param>
    /// <returns>The unary operator.</returns>
    private static UnaryOperator MapUnaryOperator(JsonataTokenKind kind)
    {
        return kind switch
        {
            JsonataTokenKind.Minus => UnaryOperator.Negate,
            _ => throw new JsonataParseException($"Token {kind} is not a unary operator.")
        };
    }

    /// <summary>
    /// Beyond the per-parse cap the parser stops recording — a runaway-error backstop. The AST still
    /// assembles (error nodes are produced regardless); only the diagnostic list is bounded.
    /// </summary>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    /// <param name="message">A human-readable explanation.</param>
    private void Report(Utf8String code, SourceSpan span, string message)
    {
        if(parserDiagnosticsRecorded >= MaxDiagnostics)
        {
            return;
        }

        Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, Utf8Strings.From(message)));
        parserDiagnosticsRecorded++;

        //On reaching the cap, record the marker once; subsequent reports are suppressed by the guard above.
        if(parserDiagnosticsRecorded == MaxDiagnostics)
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Jsonata.ExcessDiagnostics,
                DiagnosticSeverity.Error,
                span,
                Utf8Strings.From("The per-parse diagnostic cap was reached; further diagnostics are suppressed.")));
        }
    }

    /// <summary>
    /// Records the diagnostic for a recoverable error and returns the codes to stamp on the error node —
    /// unless the offending token is a lexer <see cref="JsonataTokenKind.Error"/> token, whose
    /// <c>LX####</c> diagnostic the facade already bridged into the bag.
    /// </summary>
    /// <remarks>
    /// Re-reporting an <see cref="JsonataTokenKind.Error"/> token would double-count, so the lexer's code
    /// stands alone and the parser stays silent; the error node still spans the offending run,
    /// correlating by span.
    /// </remarks>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The codes to record on the error node: <c>[code]</c>, or empty when the offending token is a lexer error.</returns>
    private ImmutableArray<Utf8String> ReportRecoverable(Utf8String code, SourceSpan span, string message)
    {
        if(Current.Kind == JsonataTokenKind.Error)
        {
            return [];
        }

        Report(code, span, message);

        return [code];
    }

    /// <summary>
    /// Skips tokens from the cursor — collecting them as the error node's trivia — until a token in the
    /// resync set (or end-of-input) is reached, which is left as the new cursor.
    /// </summary>
    /// <remarks>
    /// The <c>before == index</c> guard breaks when <see cref="Advance"/> cannot move (the cursor is
    /// clamped at the final token), so recovery from any position terminates.
    /// </remarks>
    /// <param name="startSpan">The span the running end span is seeded from.</param>
    /// <param name="lastSpan">Receives the span of the last skipped token, or <paramref name="startSpan"/> when none was skipped.</param>
    /// <returns>The tokens skipped to resynchronise.</returns>
    private ImmutableArray<JsonataToken> ResyncTo(SourceSpan startSpan, out SourceSpan lastSpan)
    {
        ImmutableArray<JsonataToken>.Builder skipped = ImmutableArray.CreateBuilder<JsonataToken>();
        lastSpan = startSpan;

        while(Current.Kind != JsonataTokenKind.EndOfInput && !IsResyncToken(Current.Kind))
        {
            skipped.Add(Current);
            lastSpan = Current.Span;
            int before = index;
            Advance();

            if(index == before)
            {
                break;
            }
        }

        return skipped.ToImmutable();
    }

    /// <summary>
    /// The resync set: the structural tokens at or after which the parser can resume after skipping a
    /// malformed run. An expression resyncs to its enclosing closers and separators; end-of-input is
    /// always a stop, handled by <see cref="ResyncTo"/>'s loop condition.
    /// </summary>
    /// <param name="kind">The token kind at the cursor.</param>
    /// <returns><see langword="true"/> when the cursor token is a safe point to resume at.</returns>
    private static bool IsResyncToken(JsonataTokenKind kind)
        => kind is JsonataTokenKind.CloseParen
            or JsonataTokenKind.CloseBracket
            or JsonataTokenKind.CloseBrace
            or JsonataTokenKind.Comma
            or JsonataTokenKind.Semicolon
            or JsonataTokenKind.Colon;

    /// <summary>
    /// Records the diagnostic, resyncs to the resync set, and builds an <see cref="ErrorExpression"/>
    /// spanning the failure-to-resync run. The node slots into any parent that expected a
    /// <see cref="JsonataExpression"/>, so the existing value flow carries it up.
    /// </summary>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error expression node.</returns>
    private ErrorExpression RecoverExpression(SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<JsonataToken> skipped = ResyncTo(errorSpan, out SourceSpan endSpan);

        return new ErrorExpression(CombineSpans(startSpan, endSpan), Utf8Strings.From(expectedProduction), codes, skipped);
    }

    /// <summary>
    /// The instruction returned by a step method: pop the current frame with a completed result, push a
    /// new frame to recurse into, or continue the same frame on the next iteration.
    /// </summary>
    private readonly struct StepResult
    {
        /// <summary>
        /// Initialises a <see cref="StepResult"/>.
        /// </summary>
        /// <param name="action">The driver action.</param>
        /// <param name="result">The popped result, for <see cref="StepAction.Pop"/>.</param>
        /// <param name="newFrame">The frame to push, for <see cref="StepAction.Push"/>.</param>
        private StepResult(StepAction action, object? result, ParseFrame? newFrame)
        {
            Action = action;
            Result = result;
            NewFrame = newFrame;
        }

        /// <summary>Gets the action the driver should take.</summary>
        public StepAction Action { get; }

        /// <summary>Gets the completed result handed to the parent frame, for a pop.</summary>
        public object? Result { get; }

        /// <summary>Gets the frame to push, for a push.</summary>
        public ParseFrame? NewFrame { get; }

        /// <summary>
        /// Creates a pop instruction carrying the production's completed result.
        /// </summary>
        /// <param name="result">The completed node.</param>
        /// <returns>The instruction.</returns>
        public static StepResult Done(object result)
        {
            return new StepResult(StepAction.Pop, result, null);
        }

        /// <summary>
        /// Creates a push instruction carrying the child frame to recurse into.
        /// </summary>
        /// <param name="next">The child frame.</param>
        /// <returns>The instruction.</returns>
        public static StepResult Push(ParseFrame next)
        {
            return new StepResult(StepAction.Push, null, next);
        }

        /// <summary>
        /// Creates a continue instruction that re-enters the current frame on the next iteration.
        /// </summary>
        /// <returns>The instruction.</returns>
        public static StepResult Continue()
        {
            return new StepResult(StepAction.Continue, null, null);
        }
    }

    /// <summary>The action a <see cref="StepResult"/> directs the driver to take.</summary>
    private enum StepAction
    {
        /// <summary>Pop the current frame and hand its result to the parent.</summary>
        Pop,

        /// <summary>Push a new child frame.</summary>
        Push,

        /// <summary>Re-enter the current frame on the next iteration.</summary>
        Continue
    }

    /// <summary>The outcome of driving the work stack.</summary>
    private enum DriveOutcome
    {
        /// <summary>The expression was produced; the stack is empty.</summary>
        Produced,

        /// <summary>The next step needs tokens that have not been fed yet.</summary>
        NeedMore
    }
}
