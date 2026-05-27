using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
using Lumoin.Veritas.Core.Iris;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;

namespace Lumoin.Veritas.Sparql.Parser;

/// <summary>
/// Parses a token stream from <see cref="SparqlLexer"/> into a
/// <see cref="SparqlRequest"/> AST.
/// </summary>
/// <remarks>
/// <para>
/// The parser is iterative and resumable, the same production model the Turtle
/// parser and the lexer use. Every production runs on an explicit
/// <see cref="Stack{T}"/> of <see cref="ParseFrame"/> values; the driver advances
/// the top frame one bounded step at a time. No production that can recurse or
/// repeat without bound — group graph patterns, the prologue, the projection list,
/// the dataset clauses, predicate-object and object lists — calls back into another
/// via method recursion or reads an unbounded run of tokens in a single step.
/// </para>
/// <para>
/// Because the driver checks lookahead before each step and a step never reads more
/// than <see cref="MaxStepLookahead"/> tokens past the cursor, the parser suspends
/// (returning <see cref="ParseStatus.NeedMore"/>) when the tokens a step needs have
/// not arrived yet, and resumes from the same frame and stage when more are fed —
/// without re-parsing. A query can therefore be parsed straight from the pipe-fed
/// lexer without buffering the whole token stream. The materialised
/// <see cref="ParseRequest"/> is a convenience over the same machinery for callers
/// that already hold the full stream.
/// </para>
/// <para>
/// Prefixed names and relative IRIs are resolved at parse time against the running
/// prologue (its prefix map and base), so the AST carries absolute
/// <see cref="IriRef"/> values. An unbound prefix raises
/// <see cref="SparqlParseException"/>.
/// </para>
/// </remarks>
public sealed class SparqlParser
{
    //The most tokens any single step inspects past the cursor. The widest step reads a typed-literal
    //object ("v" ^^ datatype) and then peeks the following separator, landing three tokens past the
    //cursor; the driver only runs a step once this many tokens are buffered (or the stream is complete),
    //so a step never reads an unbuffered token.
    private const int MaxStepLookahead = 4;

    //Operator precedence for the expression climber, low (binds loosest) to high. A frame at
    //MinPrecedence absorbs operators whose precedence is at least its own; the right operand of an
    //operator is parsed at the operator's precedence plus one, so same-level chains stay left-associative
    //and the outer frame keeps the chain. The unary-operand level is above every binary operator, so a
    //unary applies to a single primary and binds tighter than multiplication.
    private const int PrecExpression = 1;
    private const int PrecConditionalOr = 1;
    private const int PrecConditionalAnd = 2;
    private const int PrecComparison = 3;
    private const int PrecAdditive = 4;
    private const int PrecMultiplicative = 5;
    private const int PrecUnaryOperand = 6;

    private readonly Stack<ParseFrame> frames = new();
    private readonly Dictionary<Utf8String, Utf8String> prefixMap = [];
    private readonly Utf8String xsdString;
    private readonly Utf8String xsdInteger;
    private readonly Utf8String xsdDecimal;
    private readonly Utf8String xsdDouble;
    private readonly Utf8String xsdBoolean;
    private readonly Utf8String rdfLangString;
    private readonly Utf8String rdfDirLangString;
    private readonly Utf8String rdfType;
    private Utf8String? baseIri;
    private SparqlRequest? produced;
    private object? completed;
    private bool tokensComplete;
    private bool started;
    private int index;
    private int parserDiagnosticsRecorded;
    private readonly BlankNodeDelegate blankNodes;
    private SourceSpan lastConsumedSpan;

    /// <summary>
    /// Initialises a <see cref="SparqlParser"/> over a fully materialised token stream.
    /// </summary>
    /// <param name="tokens">The lexed token stream, ending with <see cref="SparqlTokenKind.EndOfInput"/>.</param>
    /// <param name="pool">The pool used to intern parser-allocated identifiers (resolved IRIs, blank-node labels).</param>
    /// <param name="baseIri">The external base IRI relative references resolve against before any in-query <c>BASE</c>, or <see langword="null"/>.</param>
    /// <param name="blankNodes">Allocates labels for anonymous <c>[]</c> blank nodes; defaults to <see cref="VeritasBlankNodes.System"/>.</param>
    /// <param name="diagnostics">The bag recovery records diagnostics into; a private bag is created when <see langword="null"/>. Pass a shared bag to merge lexer-bridged and parser diagnostics.</param>
    /// <param name="maxDiagnostics">The per-parse cap on parser-recorded diagnostics; once reached, an <see cref="WellKnownDiagnostics.Sparql.ExcessDiagnostics"/> marker is recorded and further parser diagnostics are suppressed. Defaults to unbounded.</param>
    public SparqlParser(IEnumerable<SparqlToken> tokens, Utf8StringPool pool, Utf8String? baseIri = null, BlankNodeDelegate? blankNodes = null, DiagnosticBag? diagnostics = null, int maxDiagnostics = int.MaxValue)
        : this(pool, baseIri, blankNodes, diagnostics, maxDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        foreach(SparqlToken token in tokens)
        {
            Tokens.Add(token);
        }

        tokensComplete = true;
    }

    /// <summary>
    /// Initialises a <see cref="SparqlParser"/> that is fed tokens incrementally through
    /// <see cref="FeedToken(SparqlToken)"/> and pulled through <see cref="TryParseRequest(out SparqlRequest)"/>.
    /// </summary>
    /// <param name="pool">The pool used to intern parser-allocated identifiers.</param>
    /// <param name="baseIri">The external base IRI relative references resolve against before any in-query <c>BASE</c>, or <see langword="null"/>.</param>
    /// <param name="blankNodes">Allocates labels for anonymous <c>[]</c> blank nodes; defaults to <see cref="VeritasBlankNodes.System"/>.</param>
    /// <param name="diagnostics">The bag recovery records diagnostics into; a private bag is created when <see langword="null"/>. Pass a shared bag to merge lexer-bridged and parser diagnostics.</param>
    /// <param name="maxDiagnostics">The per-parse cap on parser-recorded diagnostics; once reached, an <see cref="WellKnownDiagnostics.Sparql.ExcessDiagnostics"/> marker is recorded and further parser diagnostics are suppressed. Defaults to unbounded.</param>
    /// <remarks>
    /// The parser suspends — preserving its work stack — when the request needs tokens that have not
    /// arrived yet, and resumes when more are fed, so the token buffer need not hold the whole query.
    /// </remarks>
    internal SparqlParser(Utf8StringPool pool, Utf8String? baseIri = null, BlankNodeDelegate? blankNodes = null, DiagnosticBag? diagnostics = null, int maxDiagnostics = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Tokens = [];
        Pool = pool;
        this.baseIri = baseIri;
        this.blankNodes = blankNodes ?? VeritasBlankNodes.System;
        Diagnostics = diagnostics ?? new DiagnosticBag();
        MaxDiagnostics = maxDiagnostics;

        xsdString = pool.Intern("http://www.w3.org/2001/XMLSchema#string"u8);
        xsdInteger = pool.Intern("http://www.w3.org/2001/XMLSchema#integer"u8);
        xsdDecimal = pool.Intern("http://www.w3.org/2001/XMLSchema#decimal"u8);
        xsdDouble = pool.Intern("http://www.w3.org/2001/XMLSchema#double"u8);
        xsdBoolean = pool.Intern("http://www.w3.org/2001/XMLSchema#boolean"u8);
        rdfLangString = pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#langString"u8);
        rdfDirLangString = pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#dirLangString"u8);
        rdfType = pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
    }

    /// <summary>Gets the token buffer the parser indexes into.</summary>
    private List<SparqlToken> Tokens { get; }

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
    /// complete the buffer ends with <see cref="SparqlTokenKind.EndOfInput"/> and the cursor clamps to
    /// it, so the remaining short tail is read without further input.
    /// </remarks>
    private bool HasLookahead => tokensComplete || index + MaxStepLookahead < Tokens.Count;

    /// <summary>Gets the token at the cursor.</summary>
    private SparqlToken Current => Tokens[index];

    /// <summary>
    /// Returns the token <paramref name="offset"/> positions past the cursor, clamping to the last
    /// buffered token (the end-of-input sentinel once the stream is complete).
    /// </summary>
    /// <param name="offset">The lookahead distance, no greater than <see cref="MaxStepLookahead"/>.</param>
    /// <returns>The peeked token.</returns>
    private SparqlToken Peek(int offset)
    {
        int target = index + offset;

        return target < Tokens.Count ? Tokens[target] : Tokens[^1];
    }

    /// <summary>
    /// Parses the token stream into a <see cref="SparqlRequest"/>, assuming the whole stream is present.
    /// </summary>
    /// <returns>The parsed request AST.</returns>
    /// <exception cref="SparqlParseException">The token stream is not a well-formed SPARQL query this build accepts, or it ended early.</exception>
    public SparqlRequest ParseRequest()
    {
        ParseStatus status = TryParseRequest(out SparqlRequest? request);
        if(status != ParseStatus.Produced)
        {
            throw new SparqlParseException("The token stream ended before a complete SPARQL request was parsed.");
        }

        return request!;
    }

    /// <summary>
    /// Parses the token stream into a <see cref="ParseResult{TTree}"/>: the request (possibly carrying
    /// error nodes) together with the accumulated diagnostics and whether any has error severity.
    /// </summary>
    /// <returns>The parse result.</returns>
    public ParseResult<SparqlRequest> ParseToResult()
    {
        SparqlRequest request = ParseRequest();

        return new ParseResult<SparqlRequest>(request, Diagnostics.Diagnostics, Diagnostics.HasErrors);
    }

    /// <summary>
    /// Lexes and parses a UTF-8 SPARQL query buffer into a <see cref="ParseResult{TTree}"/>: the facade
    /// over the resumable instance machinery for callers that hold the whole query. Lexical diagnostics
    /// are bridged into the same bag as the parser's, so one <see cref="ParseResult{TTree}"/> covers both
    /// layers; malformed input is recovered, never thrown.
    /// </summary>
    /// <param name="source">The UTF-8 query bytes.</param>
    /// <param name="pool">The pool to intern identifiers into; a private pool is created when <see langword="null"/> (the result's interned values keep it alive).</param>
    /// <param name="baseIri">The external base IRI relative references resolve against before any in-query <c>BASE</c>, or <see langword="null"/>.</param>
    /// <returns>The parse result.</returns>
    public static ParseResult<SparqlRequest> ParseRequest(ReadOnlyMemory<byte> source, Utf8StringPool? pool = null, Utf8String? baseIri = null)
    {
        Utf8StringPool effectivePool = pool ?? new Utf8StringPool();
        DiagnosticBag diagnostics = new();
        SparqlLexer lexer = new(source, effectivePool);
        SparqlParser parser = new(lexer.Tokenize(), effectivePool, baseIri, blankNodes: null, diagnostics: diagnostics);

        //The public ctor enumerated the lexer, so its diagnostics are complete; bridge them into the bag
        //(lexical errors first) before draining the parser's via ParseToResult.
        BridgeLexerDiagnostics(lexer, diagnostics);

        return parser.ParseToResult();
    }

    /// <summary>Bridges the lexer's internal diagnostics into the shared parse-level bag.</summary>
    /// <param name="lexer">The lexer whose <see cref="SparqlLexer.Diagnostics"/> are drained.</param>
    /// <param name="diagnostics">The bag to append the bridged diagnostics to.</param>
    private static void BridgeLexerDiagnostics(SparqlLexer lexer, DiagnosticBag diagnostics)
    {
        foreach(SparqlLexDiagnostic lexDiagnostic in lexer.Diagnostics)
        {
            diagnostics.Add(SparqlLexDiagnosticBridge.ToDiagnostic(lexDiagnostic));
        }
    }

    /// <summary>
    /// Appends one lexed token to the parser's buffer. The terminating
    /// <see cref="SparqlTokenKind.EndOfInput"/> token marks the stream complete.
    /// </summary>
    /// <param name="token">The next token in source order.</param>
    internal void FeedToken(SparqlToken token)
    {
        Tokens.Add(token);

        if(token.Kind == SparqlTokenKind.EndOfInput)
        {
            tokensComplete = true;
        }
    }

    /// <summary>
    /// Attempts to parse the request from the buffered tokens, suspending when more are needed.
    /// </summary>
    /// <param name="request">The parsed request when the result is <see cref="ParseStatus.Produced"/>; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="ParseStatus.Produced"/> once the whole request is parsed, or
    /// <see cref="ParseStatus.NeedMore"/> when the parser needs tokens that have not been fed yet.
    /// </returns>
    internal ParseStatus TryParseRequest(out SparqlRequest? request)
    {
        if(produced is not null)
        {
            request = produced;

            return ParseStatus.Produced;
        }

        request = null;

        if(!started)
        {
            if(!HasLookahead)
            {
                return ParseStatus.NeedMore;
            }

            frames.Push(new ParseFrame { Kind = ParseFrameKind.Request, StartSpan = Current.Span });
            started = true;
        }

        if(Drive() == DriveOutcome.NeedMore)
        {
            return ParseStatus.NeedMore;
        }

        produced = (SparqlRequest)completed!;
        completed = null;
        request = produced;

        return ParseStatus.Produced;
    }

    /// <summary>
    /// Advances the cursor by one token, clamping at the terminating
    /// <see cref="SparqlTokenKind.EndOfInput"/> so a step that reads past the end keeps seeing the
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
    /// hands every frame's product up through one untyped <c>object?</c> slot because those products
    /// share no common base — graph patterns, expressions, terms, paths, lists, clauses — so each
    /// receive site downcasts to the type of the child it pushed. The cast is safe by construction
    /// (a step only consumes results of the frames it pushed) and <c>incoming</c> is non-null because
    /// a popped frame always carries a result.
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
    /// <returns>Whether the request was produced or the parser needs more tokens.</returns>
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
                    throw new SparqlParseException("Parser driver reached an undefined state.", Current.Span);
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
            ParseFrameKind.Request => StepRequest(frame, incoming),
            ParseFrameKind.SelectClause => StepSelectClause(frame, incoming),
            ParseFrameKind.ConstructTemplate => StepConstructTemplate(frame, incoming),
            ParseFrameKind.GroupGraphPattern => StepGroupGraphPattern(frame, incoming),
            ParseFrameKind.Triple => StepTriple(frame, incoming),
            ParseFrameKind.UnionPattern => StepGroupOrUnion(frame, incoming),
            ParseFrameKind.OptionalPattern => StepOptional(frame, incoming),
            ParseFrameKind.MinusPattern => StepMinus(frame, incoming),
            ParseFrameKind.GraphPattern => StepGraph(frame, incoming),
            ParseFrameKind.ServicePattern => StepService(frame, incoming),
            ParseFrameKind.Expression => StepExpression(frame, incoming),
            ParseFrameKind.PropertyPath => StepPropertyPath(frame, incoming),
            ParseFrameKind.PathSequence => StepPathSequence(frame, incoming),
            ParseFrameKind.PathElement => StepPathElement(frame, incoming),
            ParseFrameKind.PathNegatedSet => StepPathNegatedSet(frame),
            ParseFrameKind.Collection => StepCollection(frame, incoming),
            ParseFrameKind.BlankNodePropertyList => StepBlankNodePropertyList(frame, incoming),
            ParseFrameKind.TripleTerm => StepTripleTerm(frame, incoming),
            ParseFrameKind.ReifiedTriple => StepReifiedTriple(frame, incoming),
            ParseFrameKind.AnnotationBlock => StepAnnotationBlock(frame, incoming),
            ParseFrameKind.Values => StepValues(frame),
            ParseFrameKind.ArgumentList => StepArgumentList(frame, incoming),
            ParseFrameKind.GroupBy => StepGroupBy(frame, incoming),
            ParseFrameKind.Having => StepHaving(frame, incoming),
            ParseFrameKind.OrderBy => StepOrderBy(frame, incoming),
            ParseFrameKind.Filter => StepFilter(frame, incoming),
            ParseFrameKind.Bind => StepBind(frame, incoming),
            _ => throw new SparqlParseException($"Parser production '{frame.Kind}' is not yet implemented in this build.", frame.StartSpan)
        };
    }

    /// <summary>
    /// Advances the top-level request: the prologue, the form dispatch, the dataset clauses, the
    /// <c>WHERE</c> pattern, and the solution modifiers, assembling the query when complete.
    /// </summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">A pushed sub-production's result on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepRequest(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => RequestPrologue(frame),
            1 => RequestFormDispatch(frame),
            2 => RequestFormHead(frame, incoming),
            3 => RequestDataset(frame),
            4 => RequestWhere(frame),
            5 => RequestWherePattern(frame, incoming),
            6 => RequestGroupBy(frame),
            7 => RequestGroupReceived(frame, incoming),
            8 => RequestHaving(frame),
            9 => RequestHavingReceived(frame, incoming),
            10 => RequestOrderBy(frame),
            11 => RequestOrderReceived(frame, incoming),
            12 => RequestModifier(frame),
            13 => RequestFinalize(frame),
            14 => RequestConstructTemplate(frame, incoming),
            15 => RequestDescribeTargets(frame),
            16 => RequestTrailingValues(frame),
            17 => RequestTrailingValuesReceived(frame, incoming),
            _ => throw new SparqlParseException("Request reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Parses one prologue declaration, or moves past the prologue when none remains.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestPrologue(ParseFrame frame)
    {
        frame.Bases ??= [];
        frame.Prefixes ??= [];

        if(Current.Kind == SparqlTokenKind.BaseKeyword)
        {
            ParseBaseDeclaration(frame);

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.PrefixKeyword)
        {
            ParsePrefixDeclaration(frame);

            return StepResult.Continue();
        }

        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Dispatches on the query form, pushing the SELECT head or recording the ASK form inline.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestFormDispatch(ParseFrame frame)
        => Current.Kind switch
        {
            SparqlTokenKind.SelectKeyword => RequestPushSelect(frame),
            SparqlTokenKind.AskKeyword => RequestAsk(frame),
            SparqlTokenKind.ConstructKeyword => RequestConstruct(frame),
            SparqlTokenKind.DescribeKeyword => RequestDescribe(frame),
            _ => RequestRecoverForm(frame)
        };

    /// <summary>
    /// Recovers a request whose form head could not be parsed: the form is recorded as an
    /// <see cref="ErrorQueryForm"/> and the request is finalised so the partial query still materialises.
    /// </summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestRecoverForm(ParseFrame frame)
    {
        frame.Form = RecoverQueryForm(ParseFrameKind.Request, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedQueryForm, Current.Span, "Expected a query form (SELECT, CONSTRUCT, ASK, or DESCRIBE).", "Query");
        frame.Where = new WhereClause(new GroupGraphPattern([]));
        frame.Dataset = new DatasetClause([], []);
        frame.Bases ??= [];
        frame.Prefixes ??= [];
        frame.Stage = 13;

        return StepResult.Continue();
    }

    /// <summary>Pushes the SELECT clause frame and waits for its head.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestPushSelect(ParseFrame frame)
    {
        frame.Stage = 2;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.SelectClause, StartSpan = Current.Span });
    }

    /// <summary>Records the ASK form and advances to the dataset clauses.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestAsk(ParseFrame frame)
    {
        Advance();
        frame.Form = new AskQuery();
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Begins a CONSTRUCT query: pushes the explicit <c>{ template }</c>, or marks the <c>CONSTRUCT WHERE { ... }</c> short form whose WHERE triples are assembled into the template at the WHERE stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestConstruct(ParseFrame frame)
    {
        Advance();

        //CONSTRUCT { template } ... : parse the explicit template. CONSTRUCT [dataset] WHERE { triples }:
        //the short form, where the WHERE triples are also the template — deferred to the WHERE stage.
        if(Current.Kind == SparqlTokenKind.OpenBrace)
        {
            frame.Stage = 14;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ConstructTemplate, StartSpan = Current.Span });
        }

        frame.IsConstructShort = true;
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped CONSTRUCT template and advances to the dataset clauses.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped template triple list.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestConstructTemplate(ParseFrame frame, object? incoming)
    {
        frame.Form = new ConstructQuery(Pop<List<TriplePattern>>(incoming));
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Begins a DESCRIBE query and moves to its target list.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestDescribe(ParseFrame frame)
    {
        Advance();
        frame.DescribeTargets = [];
        frame.Stage = 15;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses the DESCRIBE targets one per step: <c>*</c>, or a list of variables and IRIs; completes
    /// the form and advances to the dataset clauses when no further target follows.
    /// </summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestDescribeTargets(ParseFrame frame)
    {
        if(frame.DescribeTargets!.Count == 0 && Current.Kind == SparqlTokenKind.Star)
        {
            Advance();
            frame.Form = new DescribeQuery(IsStar: true, Targets: []);
            frame.Stage = 3;

            return StepResult.Continue();
        }

        if(Current.Kind is SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName)
        {
            frame.DescribeTargets!.Add(new DescribeIri(ConsumeIriOrPrefixedName("a DESCRIBE target IRI")));

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.DescribeTargets!.Add(new DescribeVariable(new SparqlVariable(Current.Value)));
            Advance();

            return StepResult.Continue();
        }

        if(frame.DescribeTargets!.Count == 0)
        {
            //No target at all: report and keep an empty DESCRIBE form (the faithful recoverable shape).
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedDescribeTarget, Current.Span, "Expected a DESCRIBE target (a variable, an IRI, or '*').");
        }

        frame.Form = new DescribeQuery(IsStar: false, frame.DescribeTargets);
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped form head and advances to the dataset clauses.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped form head.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestFormHead(ParseFrame frame, object? incoming)
    {
        frame.Form = Pop<QueryForm>(incoming);
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Parses one dataset clause, or assembles the dataset when none remains.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestDataset(ParseFrame frame)
    {
        frame.DefaultGraphs ??= [];
        frame.NamedGraphs ??= [];

        if(Current.Kind == SparqlTokenKind.FromKeyword)
        {
            ParseFromClause(frame);

            return StepResult.Continue();
        }

        frame.Dataset = new DatasetClause(frame.DefaultGraphs, frame.NamedGraphs);
        frame.Stage = 4;

        return StepResult.Continue();
    }

    /// <summary>Consumes the optional <c>WHERE</c> keyword and pushes the group graph pattern.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestWhere(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.WhereKeyword)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.OpenBrace)
            {
                frame.Where = new WhereClause(RecoverGraphPattern(ParseFrameKind.Request, Current.Span, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after WHERE.", "GroupGraphPattern"));
                frame.Stage = 6;

                return StepResult.Continue();
            }
        }

        if(Current.Kind == SparqlTokenKind.OpenBrace)
        {
            frame.Stage = 5;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
        }

        //The WHERE clause is optional only for DESCRIBE; an absent one is an empty group.
        if(frame.Form is DescribeQuery)
        {
            frame.Where = new WhereClause(new GroupGraphPattern([]));
            frame.Stage = 6;

            return StepResult.Continue();
        }

        frame.Where = new WhereClause(RecoverGraphPattern(ParseFrameKind.Request, Current.Span, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' to begin the WHERE group graph pattern.", "GroupGraphPattern"));
        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped WHERE pattern and advances to the solution modifiers.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped group graph pattern.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestWherePattern(ParseFrame frame, object? incoming)
    {
        GraphPattern pattern = Pop<GraphPattern>(incoming);
        frame.Where = new WhereClause(pattern);

        //In the CONSTRUCT WHERE short form the WHERE triples double as the template.
        if(frame.IsConstructShort)
        {
            frame.Form = new ConstructQuery(ExtractTemplateTriples(pattern));
        }

        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>
    /// Collects the triples of a pattern's basic graph pattern blocks, for the CONSTRUCT WHERE short
    /// form whose WHERE triples are also the construct template.
    /// </summary>
    /// <param name="pattern">The WHERE pattern.</param>
    /// <returns>The flattened template triples.</returns>
    private static List<TriplePattern> ExtractTemplateTriples(GraphPattern pattern)
    {
        List<TriplePattern> triples = [];

        if(pattern is GroupGraphPattern group)
        {
            foreach(GraphPattern member in group.Members)
            {
                if(member is BasicGraphPatternBlock block)
                {
                    triples.AddRange(block.Triples);
                }
            }
        }

        return triples;
    }

    /// <summary>Pushes the <c>GROUP BY</c> clause when present, otherwise advances to the having stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestGroupBy(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.GroupKeyword)
        {
            frame.Stage = 7;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupBy, StartSpan = Current.Span });
        }

        frame.Stage = 8;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped <c>GROUP BY</c> clause and advances to the having stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped group clause.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestGroupReceived(ParseFrame frame, object? incoming)
    {
        frame.Group = Pop<GroupClause>(incoming);
        frame.Stage = 8;

        return StepResult.Continue();
    }

    /// <summary>Pushes the <c>HAVING</c> clause when present, otherwise advances to the order stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestHaving(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.HavingKeyword)
        {
            frame.Stage = 9;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Having, StartSpan = Current.Span });
        }

        frame.Stage = 10;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped <c>HAVING</c> clause and advances to the order stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped having clause.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestHavingReceived(ParseFrame frame, object? incoming)
    {
        frame.Having = Pop<HavingClause>(incoming);
        frame.Stage = 10;

        return StepResult.Continue();
    }

    /// <summary>Pushes the <c>ORDER BY</c> clause when present, otherwise advances to the slice stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestOrderBy(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.OrderKeyword)
        {
            frame.Stage = 11;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.OrderBy, StartSpan = Current.Span });
        }

        frame.Stage = 12;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped <c>ORDER BY</c> clause and advances to the slice stage.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped order clause.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestOrderReceived(ParseFrame frame, object? incoming)
    {
        frame.Order = Pop<OrderClause>(incoming);
        frame.Stage = 12;

        return StepResult.Continue();
    }

    /// <summary>Parses one LIMIT/OFFSET clause, or moves to finalisation when no slice clause remains.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestModifier(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.LimitKeyword && frame.Limit is null)
        {
            Advance();
            frame.Limit = ConsumeInteger("LIMIT value");

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.OffsetKeyword && frame.Offset is null)
        {
            Advance();
            frame.Offset = ConsumeInteger("OFFSET value");

            return StepResult.Continue();
        }

        frame.Stage = 16;

        return StepResult.Continue();
    }

    /// <summary>Pushes the trailing <c>VALUES</c> block when present, otherwise advances to finalisation.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestTrailingValues(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.ValuesKeyword)
        {
            frame.Stage = 17;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Values, StartSpan = Current.Span });
        }

        frame.Stage = 13;

        return StepResult.Continue();
    }

    /// <summary>Adopts the popped trailing <c>VALUES</c> block and advances to finalisation.</summary>
    /// <param name="frame">The request frame.</param>
    /// <param name="incoming">The popped values clause.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult RequestTrailingValuesReceived(ParseFrame frame, object? incoming)
    {
        frame.Values = (ValuesClause)incoming!;
        frame.Stage = 13;

        return StepResult.Continue();
    }

    /// <summary>Verifies the end of input and assembles the parsed query.</summary>
    /// <param name="frame">The request frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RequestFinalize(ParseFrame frame)
    {
        //A sub-SELECT finalises at the enclosing '}', which the caller consumes; a top-level request
        //must reach end of input. Trailing tokens are reported and skipped, then the parsed query is
        //still produced — the recoverable shape for an editor.
        if(!frame.IsSubSelect && Current.Kind != SparqlTokenKind.EndOfInput)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedEndOfQuery, Current.Span, "Unexpected tokens after the end of the query.");
            ResyncTo(ParseFrameKind.Request, Current.Span, out _);
        }

        SparqlQuery query = new(
            new Prologue(frame.Bases!, frame.Prefixes!),
            frame.Form!,
            frame.Dataset!,
            frame.Where!,
            new SolutionModifier(frame.Group, frame.Having, frame.Order, frame.Offset, frame.Limit),
            frame.Values);

        return StepResult.Done(query);
    }

    /// <summary>
    /// Advances a <c>SELECT</c> clause: the optional <c>DISTINCT</c> / <c>REDUCED</c> modifier and
    /// either <c>*</c> or a projection list parsed one variable per step.
    /// </summary>
    /// <param name="frame">The select-clause frame.</param>
    /// <param name="incoming">A popped projection expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepSelectClause(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => SelectHead(frame),
            1 => SelectProjection(frame),
            2 => SelectExpressionProjection(frame, incoming),
            _ => throw new SparqlParseException("Select clause reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>
    /// Consumes <c>SELECT</c> with its optional <c>DISTINCT</c> / <c>REDUCED</c> modifier and either
    /// completes the <c>*</c> form or begins the projection list.
    /// </summary>
    /// <param name="frame">The select-clause frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult SelectHead(ParseFrame frame)
    {
        //The cursor is on the SELECT keyword.
        Advance();

        if(Current.Kind == SparqlTokenKind.DistinctKeyword)
        {
            frame.IsDistinct = true;
            Advance();
        }
        else if(Current.Kind == SparqlTokenKind.ReducedKeyword)
        {
            frame.IsReduced = true;
            Advance();
        }

        if(Current.Kind == SparqlTokenKind.Star)
        {
            Advance();

            return StepResult.Done(new SelectQuery(frame.IsDistinct, frame.IsReduced, IsStar: true, Projections: []));
        }

        frame.Projections = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses one projection: a bare variable, or the start of an <c>(expr AS ?var)</c> projection;
    /// completes the clause when no further projection follows.
    /// </summary>
    /// <param name="frame">The select-clause frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult SelectProjection(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.Projections!.Add(new SelectVariable(new SparqlVariable(Current.Value)));
            Advance();

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.OpenParen)
        {
            Advance();
            frame.Stage = 2;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(frame.Projections!.Count == 0)
        {
            return StepResult.Done(RecoverQueryForm(ParseFrameKind.SelectClause, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedProjection, Current.Span, "Expected a projected variable, an (expr AS ?var) projection, or '*'.", "SelectClause"));
        }

        return StepResult.Done(new SelectQuery(frame.IsDistinct, frame.IsReduced, IsStar: false, frame.Projections));
    }

    /// <summary>Completes an <c>(expr AS ?var)</c> projection from its popped expression and continues the list.</summary>
    /// <param name="frame">The select-clause frame.</param>
    /// <param name="incoming">The popped projection expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult SelectExpressionProjection(ParseFrame frame, object? incoming)
    {
        ExpressionNode expression = Pop<ExpressionNode>(incoming);
        if(Current.Kind != SparqlTokenKind.AsKeyword)
        {
            return RecoverSelectProjection(frame, WellKnownDiagnostics.Sparql.ExpectedKeyword, "Expected AS in the (expr AS ?var) projection.");
        }

        Advance();
        if(Current.Kind != SparqlTokenKind.Variable)
        {
            return RecoverSelectProjection(frame, WellKnownDiagnostics.Sparql.ExpectedVariable, "Expected a variable after AS.");
        }

        SparqlVariable variable = new(Current.Value);
        Advance();
        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            return RecoverSelectProjection(frame, WellKnownDiagnostics.Sparql.ExpectedCloser, "Expected ')' to close the (expr AS ?var) projection.");
        }

        Advance();
        frame.Projections!.Add(new SelectExpressionAs(expression, variable));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Recovers a malformed <c>(expr AS ?var)</c> projection: reports the diagnostic, resyncs to the
    /// clause's resync set, and finalises the SELECT clause with the projections gathered so far.
    /// </summary>
    /// <param name="frame">The select-clause frame.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RecoverSelectProjection(ParseFrame frame, Utf8String code, string message)
    {
        _ = ReportRecoverable(code, Current.Span, message);
        ResyncTo(ParseFrameKind.SelectClause, Current.Span, out _);

        return StepResult.Done(new SelectQuery(frame.IsDistinct, frame.IsReduced, IsStar: false, frame.Projections!));
    }

    /// <summary>
    /// Advances a group graph pattern: consumes the opening brace, then per step appends a popped
    /// triple run or member and decides whether to push the next triple, push a member, or finish at
    /// the closing brace. Contiguous triple runs merge into a single basic graph pattern block.
    /// </summary>
    /// <param name="frame">The group-graph-pattern frame.</param>
    /// <param name="incoming">A popped triple run or member on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepGroupGraphPattern(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            //The caller ensured '{' is current.
            Advance();

            //A '{' immediately followed by SELECT is a sub-SELECT, not a group of members.
            if(Current.Kind == SparqlTokenKind.SelectKeyword)
            {
                frame.Stage = 2;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Request, IsSubSelect = true, StartSpan = Current.Span });
            }

            frame.Members = [];
            frame.PendingTriples = [];
            frame.Stage = 1;
        }
        else if(frame.Stage == 2)
        {
            SparqlQuery inner = Pop<SparqlQuery>(incoming);
            if(Current.Kind != SparqlTokenKind.CloseBrace)
            {
                _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedGroupGraphPattern, Current.Span, "Expected '}' to close the sub-SELECT.");
                ResyncTo(ParseFrameKind.SubSelect, Current.Span, out _);
            }

            if(Current.Kind == SparqlTokenKind.CloseBrace)
            {
                Advance();
            }

            return StepResult.Done(new SubSelectPattern(inner));
        }
        else if(incoming is List<TriplePattern> triples)
        {
            frame.PendingTriples!.AddRange(triples);
            ConsumeOptionalDot();
        }
        else if(incoming is GraphPattern member)
        {
            //A non-triple member breaks the current basic graph pattern run.
            FlushPendingTriples(frame);
            frame.Members!.Add(member);
            ConsumeOptionalDot();
        }
        else if(incoming is ValuesClause values)
        {
            //An inline VALUES data block is a member; wrap it as a values pattern.
            FlushPendingTriples(frame);
            frame.Members!.Add(new ValuesPattern(values));
            ConsumeOptionalDot();
        }

        if(Current.Kind == SparqlTokenKind.CloseBrace)
        {
            FlushPendingTriples(frame);
            Advance();

            return StepResult.Done(new GroupGraphPattern(frame.Members!));
        }

        //An unclosed group at end of input is finalised with the members gathered so far plus a
        //diagnostic — the partial group is the most faithful recoverable shape for an editor.
        if(Current.Kind == SparqlTokenKind.EndOfInput)
        {
            FlushPendingTriples(frame);
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedGroupGraphPattern, Current.Span, "Expected '}' to close the group graph pattern.");

            return StepResult.Done(new GroupGraphPattern(frame.Members!));
        }

        if(CanStartTriple(Current.Kind))
        {
            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Triple, StartSpan = Current.Span });
        }

        ParseFrameKind? memberKind = Current.Kind switch
        {
            //An opening brace begins a GroupOrUnionGraphPattern; the union frame handles the single-group case too.
            SparqlTokenKind.OpenBrace => ParseFrameKind.UnionPattern,
            SparqlTokenKind.OptionalKeyword => ParseFrameKind.OptionalPattern,
            SparqlTokenKind.MinusKeyword => ParseFrameKind.MinusPattern,
            SparqlTokenKind.GraphKeyword => ParseFrameKind.GraphPattern,
            SparqlTokenKind.ServiceKeyword => ParseFrameKind.ServicePattern,
            SparqlTokenKind.FilterKeyword => ParseFrameKind.Filter,
            SparqlTokenKind.BindKeyword => ParseFrameKind.Bind,
            SparqlTokenKind.ValuesKeyword => ParseFrameKind.Values,
            _ => null
        };

        if(memberKind is { } kind)
        {
            return StepResult.Push(new ParseFrame { Kind = kind, StartSpan = Current.Span });
        }

        //A stray token that begins no member is consumed by no frame, so the work stack would spin
        //forever if it stayed. Record one diagnostic, then skip exactly the stray run up to the next
        //token that can begin a member (or the group's closer / end of input) — this is the progress
        //guarantee for the group frame, mirroring the Turtle parser's StepStatement skip loop.
        return RecoverGroupStrayToken();
    }

    /// <summary>
    /// Skips a run of stray tokens at group-member position — those that begin no member and are not a
    /// closer — recording one diagnostic, so the group frame always advances. Resumes the group on the
    /// next iteration once the cursor reaches a member start, the closing <c>}</c>, or end of input.
    /// </summary>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RecoverGroupStrayToken()
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTriplePattern, Current.Span, "Expected a triple pattern, a group-pattern keyword, or '}'.");

        while(Current.Kind != SparqlTokenKind.EndOfInput
            && Current.Kind != SparqlTokenKind.CloseBrace
            && !CanStartGroupMember(Current.Kind))
        {
            int before = index;
            Advance();

            if(index == before)
            {
                break;
            }
        }

        return StepResult.Continue();
    }

    /// <summary>
    /// Advances a <c>GroupOrUnionGraphPattern</c>: a group graph pattern optionally followed by one or
    /// more <c>UNION</c> alternatives, left-associatively combined. A single group passes through
    /// unwrapped.
    /// </summary>
    /// <param name="frame">The union frame.</param>
    /// <param name="incoming">A popped alternative group on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepGroupOrUnion(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => GroupOrUnionFirst(frame),
            1 => GroupOrUnionStore(frame, incoming),
            2 => GroupOrUnionNext(frame, incoming),
            _ => throw new SparqlParseException("Group-or-union reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Pushes the first group of a <c>GroupOrUnionGraphPattern</c> (the caller ensured <c>{</c> is current).</summary>
    /// <param name="frame">The union frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult GroupOrUnionFirst(ParseFrame frame)
    {
        frame.Stage = 1;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
    }

    /// <summary>Stores the first popped group as the union accumulator.</summary>
    /// <param name="frame">The union frame.</param>
    /// <param name="incoming">The popped first group.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult GroupOrUnionStore(ParseFrame frame, object? incoming)
    {
        frame.Accumulated = Pop<GraphPattern>(incoming);
        frame.Stage = 2;

        return StepResult.Continue();
    }

    /// <summary>
    /// Folds a popped alternative into the union accumulator, then either pushes the next <c>UNION</c>
    /// alternative or completes with the (possibly unwrapped single) accumulated pattern.
    /// </summary>
    /// <param name="frame">The union frame.</param>
    /// <param name="incoming">A popped alternative group, or <see langword="null"/> on the first visit.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult GroupOrUnionNext(ParseFrame frame, object? incoming)
    {
        if(incoming is GraphPattern right)
        {
            frame.Accumulated = new UnionPattern(frame.Accumulated!, right);
        }

        if(Current.Kind != SparqlTokenKind.UnionKeyword)
        {
            return StepResult.Done(frame.Accumulated!);
        }

        Advance();
        if(Current.Kind != SparqlTokenKind.OpenBrace)
        {
            //A missing UNION alternative is folded as an error pattern; the accumulator stands.
            frame.Accumulated = new UnionPattern(frame.Accumulated!, RecoverGraphPattern(ParseFrameKind.UnionPattern, Current.Span, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after UNION.", "GroupGraphPattern"));

            return StepResult.Done(frame.Accumulated);
        }

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
    }

    /// <summary>
    /// Advances an <c>OPTIONAL { ... }</c> member.
    /// </summary>
    /// <param name="frame">The optional frame.</param>
    /// <param name="incoming">The popped inner pattern on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepOptional(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.OpenBrace)
            {
                return StepResult.Done(new OptionalPattern(RecoverGraphPattern(ParseFrameKind.OptionalPattern, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after OPTIONAL.", "GroupGraphPattern")));
            }

            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
        }

        return StepResult.Done(new OptionalPattern(Pop<GraphPattern>(incoming)));
    }

    /// <summary>
    /// Advances a <c>MINUS { ... }</c> member.
    /// </summary>
    /// <param name="frame">The minus frame.</param>
    /// <param name="incoming">The popped inner pattern on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepMinus(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.OpenBrace)
            {
                return StepResult.Done(new MinusPattern(RecoverGraphPattern(ParseFrameKind.MinusPattern, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after MINUS.", "GroupGraphPattern")));
            }

            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
        }

        return StepResult.Done(new MinusPattern(Pop<GraphPattern>(incoming)));
    }

    /// <summary>
    /// Advances a <c>GRAPH term { ... }</c> member.
    /// </summary>
    /// <param name="frame">The graph frame.</param>
    /// <param name="incoming">The popped inner pattern on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepGraph(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            Advance();
            frame.GraphDesignator = ParseGraphTerm();
            if(Current.Kind != SparqlTokenKind.OpenBrace)
            {
                return StepResult.Done(new GraphGraphPattern(frame.GraphDesignator, RecoverGraphPattern(ParseFrameKind.GraphPattern, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after the GRAPH designator.", "GroupGraphPattern")));
            }

            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
        }

        return StepResult.Done(new GraphGraphPattern(frame.GraphDesignator!, Pop<GraphPattern>(incoming)));
    }

    /// <summary>
    /// Advances a <c>SERVICE [SILENT] term { ... }</c> member.
    /// </summary>
    /// <param name="frame">The service frame.</param>
    /// <param name="incoming">The popped inner pattern on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepService(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            Advance();
            if(Current.Kind == SparqlTokenKind.SilentKeyword)
            {
                frame.IsSilent = true;
                Advance();
            }

            frame.GraphDesignator = ParseGraphTerm();
            if(Current.Kind != SparqlTokenKind.OpenBrace)
            {
                return StepResult.Done(new ServicePattern(frame.GraphDesignator, frame.IsSilent, RecoverGraphPattern(ParseFrameKind.ServicePattern, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after the SERVICE endpoint.", "GroupGraphPattern")));
            }

            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
        }

        return StepResult.Done(new ServicePattern(frame.GraphDesignator!, frame.IsSilent, Pop<GraphPattern>(incoming)));
    }

    /// <summary>
    /// Advances an expression by precedence climbing: it parses a first operand (an optional unary
    /// applied to a primary), then absorbs binary operators whose precedence is at least the frame's
    /// <see cref="ParseFrame.MinPrecedence"/>, parsing each right operand in a child frame one level up
    /// so same-level chains stay left-associative.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">A popped operand on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the built <see cref="ExpressionNode"/>.</returns>
    private StepResult StepExpression(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => ExpressionOperand(frame),
            1 => ExpressionBinaryLoop(frame),
            2 => ExpressionCombine(frame, incoming),
            3 => ExpressionWrapUnary(frame, incoming),
            4 => ExpressionBracketClose(frame, incoming),
            5 => ExpressionCallReceived(frame, incoming),
            6 => ExpressionInReceived(frame, incoming),
            7 => ExpressionAggregateReceived(frame, incoming),
            8 => ExpressionExistsReceived(frame, incoming),
            _ => throw new SparqlParseException("Expression reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Parses the first operand: an optional leading unary operator applied to a primary.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionOperand(ParseFrame frame)
    {
        if(Current.Kind is SparqlTokenKind.Bang or SparqlTokenKind.Plus or SparqlTokenKind.Minus)
        {
            frame.OperatorKind = Current.Kind;
            Advance();
            frame.Stage = 3;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecUnaryOperand, StartSpan = Current.Span });
        }

        return DispatchPrimaryExpression(frame);
    }

    /// <summary>
    /// Absorbs the next binary operator (or an <c>IN</c> / <c>NOT IN</c> test) whose precedence is at
    /// least the frame's, pushing the right operand a level up; otherwise the expression is complete.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionBinaryLoop(ParseFrame frame)
    {
        //IN / NOT IN sit at comparison precedence and, like comparisons, do not chain.
        bool isIn = Current.Kind == SparqlTokenKind.InKeyword;
        bool isNotIn = Current.Kind == SparqlTokenKind.NotKeyword && Peek(1).Kind == SparqlTokenKind.InKeyword;
        if((isIn || isNotIn) && frame.MinPrecedence <= PrecComparison)
        {
            //Comparison chaining is a positional rule violation: report and keep the left operand built
            //so far rather than fabricating an illegal chain.
            if(frame.SawComparison)
            {
                Report(WellKnownDiagnostics.Sparql.UnexpectedToken, Current.Span, "SPARQL comparison operators do not chain.");

                return StepResult.Done(frame.Left!);
            }

            Advance();
            if(isNotIn)
            {
                Advance();
            }

            if(Current.Kind != SparqlTokenKind.OpenParen)
            {
                return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected '(' to begin the IN list.", "ExpressionList"));
            }

            frame.Pending = isNotIn ? PendingCall.NotIn : PendingCall.In;
            frame.Stage = 6;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ArgumentList, StartSpan = Current.Span });
        }

        int precedence = BinaryPrecedence(Current.Kind);
        if(precedence < frame.MinPrecedence)
        {
            return StepResult.Done(frame.Left!);
        }

        if(IsComparison(Current.Kind) && frame.SawComparison)
        {
            Report(WellKnownDiagnostics.Sparql.UnexpectedToken, Current.Span, "SPARQL comparison operators do not chain.");

            return StepResult.Done(frame.Left!);
        }

        frame.OperatorKind = Current.Kind;
        Advance();
        frame.Stage = 2;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = precedence + 1, StartSpan = Current.Span });
    }

    /// <summary>Combines the left operand with the popped right operand under the pending binary operator.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped right operand.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionCombine(ParseFrame frame, object? incoming)
    {
        frame.Left = CombineBinary(frame.OperatorKind, frame.Left!, Pop<ExpressionNode>(incoming));
        if(IsComparison(frame.OperatorKind))
        {
            frame.SawComparison = true;
        }

        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Wraps the popped operand under the pending unary operator.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped operand.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionWrapUnary(ParseFrame frame, object? incoming)
    {
        frame.Left = WrapUnary(frame.OperatorKind, Pop<ExpressionNode>(incoming));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Closes a bracketed expression: consumes the <c>)</c> and adopts the popped inner expression.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped inner expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionBracketClose(ParseFrame frame, object? incoming)
    {
        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            //A missing close paren keeps the parsed inner expression; the diagnostic flags the gap.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedExpression, Current.Span, "Expected ')' to close the bracketed expression.");
            frame.Left = Pop<ExpressionNode>(incoming);
            frame.Stage = 1;

            return StepResult.Continue();
        }

        Advance();
        frame.Left = Pop<ExpressionNode>(incoming);
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Builds a built-in, function, <c>COALESCE</c>, or <c>IF</c> call from its popped argument list.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped argument list.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionCallReceived(ParseFrame frame, object? incoming)
    {
        List<ExpressionNode> arguments = Pop<List<ExpressionNode>>(incoming);
        frame.Left = frame.Pending switch
        {
            PendingCall.If => BuildIf(arguments, frame.StartSpan),
            PendingCall.Coalesce => new CoalesceExpression(arguments),
            PendingCall.Function => new FunctionCallExpression(frame.FunctionIri!.Value, arguments),
            _ => new BuiltInCallExpression(SparqlFunctions.BuiltInFromName(frame.CallName!.Value), arguments)
        };
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Builds an <c>IN</c> / <c>NOT IN</c> test over the left operand from the popped set.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped candidate-set list.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionInReceived(ParseFrame frame, object? incoming)
    {
        List<ExpressionNode> set = Pop<List<ExpressionNode>>(incoming);
        frame.Left = frame.Pending == PendingCall.NotIn
            ? new NotInExpression(frame.Left!, set)
            : new InExpression(frame.Left!, set);
        frame.SawComparison = true;
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Builds an aggregate from its popped single argument, reading any <c>GROUP_CONCAT</c> separator.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped aggregated expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionAggregateReceived(ParseFrame frame, object? incoming)
    {
        frame.Left = BuildAggregate(frame, Pop<ExpressionNode>(incoming));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Wraps the popped pattern as <c>EXISTS</c> or <c>NOT EXISTS</c>.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <param name="incoming">The popped inner graph pattern.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ExpressionExistsReceived(ParseFrame frame, object? incoming)
    {
        GraphPattern inner = Pop<GraphPattern>(incoming);
        frame.Left = frame.OperatorKind == SparqlTokenKind.NotKeyword
            ? new NotExistsExpression(inner)
            : new ExistsExpression(inner);
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses a primary expression at the cursor into the frame's left slot: a variable, an RDF
    /// literal, an IRI or prefixed-name constant, or a bracketed expression. Built-in calls, function
    /// calls, aggregates, and <c>EXISTS</c> arrive in a later slice.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult DispatchPrimaryExpression(ParseFrame frame)
        => Current.Kind switch
        {
            SparqlTokenKind.Variable => ExpressionVariable(frame),
            SparqlTokenKind.StringLiteral
                or SparqlTokenKind.LongStringLiteral
                or SparqlTokenKind.IntegerLiteral
                or SparqlTokenKind.DecimalLiteral
                or SparqlTokenKind.DoubleLiteral
                or SparqlTokenKind.BooleanLiteral => ExpressionLiteral(frame),
            SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName => ExpressionIriOrFunction(frame),
            SparqlTokenKind.BuiltInFunctionName => DispatchBuiltInCall(frame),
            SparqlTokenKind.AggregateFunctionName => DispatchAggregate(frame),
            SparqlTokenKind.ExistsKeyword => ExpressionExists(frame),
            SparqlTokenKind.NotKeyword => ExpressionNotExists(frame),
            SparqlTokenKind.OpenParen => ExpressionBracket(frame),
            _ => StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpressionExpected, Current.Span, "Expected an expression.", "Expression"))
        };

    /// <summary>Sets the frame's left operand to the variable at the cursor and advances to the binary loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionVariable(ParseFrame frame)
    {
        frame.Left = new VariableExpression(new SparqlVariable(Current.Value));
        Advance();
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Sets the frame's left operand to the RDF literal at the cursor and advances to the binary loop.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionLiteral(ParseFrame frame)
    {
        frame.Left = new ConstantExpression(ParseRdfLiteral());
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Handles an IRI or prefixed name in expression position: an IRI followed by <c>(</c> is a
    /// function call (its arguments are parsed in a child frame); otherwise it is an IRI constant.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionIriOrFunction(ParseFrame frame)
    {
        IriRef iri = Current.Kind == SparqlTokenKind.Iri ? ConsumeIriRef() : ConsumePrefixedName();

        if(Current.Kind == SparqlTokenKind.OpenParen)
        {
            frame.FunctionIri = iri;
            frame.Pending = PendingCall.Function;
            frame.Stage = 5;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ArgumentList, StartSpan = Current.Span });
        }

        frame.Left = new ConstantExpression(new NamedNode(iri.Value));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Begins an <c>EXISTS { pattern }</c> expression, pushing the inner group graph pattern.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionExists(ParseFrame frame)
    {
        Advance();
        if(Current.Kind != SparqlTokenKind.OpenBrace)
        {
            return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after EXISTS.", "ExistsFunc"));
        }

        frame.OperatorKind = SparqlTokenKind.ExistsKeyword;
        frame.Stage = 8;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
    }

    /// <summary>Begins a <c>NOT EXISTS { pattern }</c> expression, pushing the inner group graph pattern.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionNotExists(ParseFrame frame)
    {
        Advance();
        if(Current.Kind != SparqlTokenKind.ExistsKeyword)
        {
            return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedKeyword, Current.Span, "Expected EXISTS after NOT.", "NotExistsFunc"));
        }

        Advance();
        if(Current.Kind != SparqlTokenKind.OpenBrace)
        {
            return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedGroupGraphPatternOpen, Current.Span, "Expected '{' after NOT EXISTS.", "NotExistsFunc"));
        }

        frame.OperatorKind = SparqlTokenKind.NotKeyword;
        frame.Stage = 8;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
    }

    /// <summary>Begins a bracketed <c>( expr )</c> primary, pushing the inner expression.</summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ExpressionBracket(ParseFrame frame)
    {
        Advance();
        frame.Stage = 4;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
    }

    /// <summary>
    /// Dispatches a reserved built-in function call at the cursor. <c>BOUND</c> is a single-variable
    /// form; <c>IF</c> and <c>COALESCE</c> build their dedicated nodes from the argument list; every
    /// other built-in builds a general <see cref="BuiltInCallExpression"/>.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult DispatchBuiltInCall(ParseFrame frame)
    {
        Utf8String name = Current.Value;
        Advance();

        if(name.Span.SequenceEqual("BOUND"u8))
        {
            if(Current.Kind != SparqlTokenKind.OpenParen)
            {
                return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected '(' after BOUND.", "BuiltInCall"));
            }

            Advance();
            if(Current.Kind != SparqlTokenKind.Variable)
            {
                return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedVariable, Current.Span, "Expected a variable inside BOUND.", "BuiltInCall"));
            }

            SparqlVariable variable = new(Current.Value);
            Advance();
            if(Current.Kind != SparqlTokenKind.CloseParen)
            {
                //A missing close paren keeps the parsed BOUND(?v); the diagnostic flags the gap.
                _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected ')' to close BOUND.");
                frame.Left = new BoundExpression(variable);
                frame.Stage = 1;

                return StepResult.Continue();
            }

            Advance();
            frame.Left = new BoundExpression(variable);
            frame.Stage = 1;

            return StepResult.Continue();
        }

        if(Current.Kind != SparqlTokenKind.OpenParen)
        {
            return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected '(' after a built-in function.", "BuiltInCall"));
        }

        frame.CallName = name;
        frame.Pending = name.Span.SequenceEqual("IF"u8)
            ? PendingCall.If
            : name.Span.SequenceEqual("COALESCE"u8)
                ? PendingCall.Coalesce
                : PendingCall.BuiltIn;
        frame.Stage = 5;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ArgumentList, StartSpan = Current.Span });
    }

    /// <summary>
    /// Dispatches an aggregate call at the cursor. <c>COUNT(*)</c> is handled inline; the other
    /// aggregates parse a single argument expression (with an optional leading <c>DISTINCT</c>) in a
    /// child frame, with the <c>GROUP_CONCAT</c> separator read when that argument returns.
    /// </summary>
    /// <param name="frame">The expression frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult DispatchAggregate(ParseFrame frame)
    {
        Utf8String name = Current.Value;
        Advance();
        if(Current.Kind != SparqlTokenKind.OpenParen)
        {
            return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected '(' after an aggregate function.", "Aggregate"));
        }

        Advance();
        frame.CallName = name;

        frame.IsDistinct = false;
        if(Current.Kind == SparqlTokenKind.DistinctKeyword)
        {
            frame.IsDistinct = true;
            Advance();
        }

        //COUNT(*) and COUNT(DISTINCT *) take the star rather than an argument expression.
        if(name.Span.SequenceEqual("COUNT"u8) && Current.Kind == SparqlTokenKind.Star)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.CloseParen)
            {
                return StepResult.Done(RecoverExpression(ParseFrameKind.Expression, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected ')' to close COUNT(*).", "Aggregate"));
            }

            Advance();
            frame.Left = new AggregateExpression(SparqlFunctions.AggregateFromName(name), Argument: null, frame.IsDistinct, IsCountStar: true, GroupConcatSeparator: null);
            frame.Stage = 1;

            return StepResult.Continue();
        }

        frame.Stage = 7;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
    }

    /// <summary>
    /// Advances a <c>FILTER ( expr )</c> member. The built-in-call and function-call constraint forms
    /// arrive in a later slice.
    /// </summary>
    /// <param name="frame">The filter frame.</param>
    /// <param name="incoming">The popped constraint expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepFilter(ParseFrame frame, object? incoming)
    {
        //FILTER takes a Constraint: a bracketed expression, a built-in call, or a function call.
        //All three are expression primaries, so the expression frame parses each form — including a
        //bare EXISTS / NOT EXISTS / regex(...) constraint, where the parentheses (if any) belong to the
        //call, not to FILTER.
        if(frame.Stage == 0)
        {
            Advance();
            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        return StepResult.Done(new FilterPattern(Pop<ExpressionNode>(incoming)));
    }

    /// <summary>
    /// Advances a <c>BIND ( expr AS ?var )</c> member.
    /// </summary>
    /// <param name="frame">The bind frame.</param>
    /// <param name="incoming">The popped bound expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepBind(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.OpenParen)
            {
                return StepResult.Done(RecoverGraphPattern(ParseFrameKind.Bind, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected '(' after BIND.", "Bind"));
            }

            Advance();
            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        ExpressionNode bound = Pop<ExpressionNode>(incoming);

        if(Current.Kind != SparqlTokenKind.AsKeyword)
        {
            return RecoverBind(frame, WellKnownDiagnostics.Sparql.ExpectedKeyword, "Expected AS in BIND.");
        }

        Advance();
        if(Current.Kind != SparqlTokenKind.Variable)
        {
            return RecoverBind(frame, WellKnownDiagnostics.Sparql.ExpectedVariable, "Expected a variable after AS in BIND.");
        }

        SparqlVariable variable = new(Current.Value);
        Advance();
        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            //A missing close paren keeps the parsed BIND(expr AS ?var); the diagnostic flags the gap.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected ')' to close BIND.");

            return StepResult.Done(new BindPattern(bound, variable));
        }

        Advance();

        return StepResult.Done(new BindPattern(bound, variable));
    }

    /// <summary>
    /// Recovers a malformed <c>BIND ( expr AS ?var )</c>: reports the diagnostic, resyncs to the bind
    /// frame's resync set, and produces an <see cref="ErrorGraphPattern"/> in the member's place.
    /// </summary>
    /// <param name="frame">The bind frame.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RecoverBind(ParseFrame frame, Utf8String code, string message)
    {
        return StepResult.Done(RecoverGraphPattern(ParseFrameKind.Bind, frame.StartSpan, code, Current.Span, message, "Bind"));
    }

    /// <summary>
    /// Advances a parenthesised, comma-separated argument or expression list, parsing one argument per
    /// step. An empty <c>()</c> yields an empty list.
    /// </summary>
    /// <param name="frame">The argument-list frame.</param>
    /// <param name="incoming">A popped argument expression on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the argument list.</returns>
    private StepResult StepArgumentList(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            //The caller ensured '(' is current.
            Advance();
            frame.Arguments = [];

            if(Current.Kind == SparqlTokenKind.CloseParen)
            {
                Advance();

                return StepResult.Done(frame.Arguments);
            }

            frame.Stage = 1;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        frame.Arguments!.Add(Pop<ExpressionNode>(incoming));

        if(Current.Kind == SparqlTokenKind.Comma)
        {
            Advance();

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            //An argument list ending without a ',' or ')' is finalised with the arguments gathered so
            //far plus a diagnostic; junk up to the closer or a comma is skipped.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected ',' or ')' in the argument list.");
            ResyncTo(ParseFrameKind.ArgumentList, Current.Span, out _);

            if(Current.Kind == SparqlTokenKind.CloseParen)
            {
                Advance();
            }

            return StepResult.Done(frame.Arguments!);
        }

        Advance();

        return StepResult.Done(frame.Arguments);
    }

    /// <summary>
    /// Builds an <see cref="IfExpression"/> from its three arguments. An arity other than three is a
    /// positional rule violation: it is reported and the call is reshaped to three positions (padding
    /// missing positions with an <see cref="ErrorExpression"/>, dropping any beyond the third), keeping
    /// the faithful node.
    /// </summary>
    /// <param name="arguments">The parsed argument list.</param>
    /// <param name="span">The source span, for the diagnostic.</param>
    /// <returns>The conditional expression.</returns>
    private IfExpression BuildIf(List<ExpressionNode> arguments, SourceSpan span)
    {
        if(arguments.Count == 3)
        {
            return new IfExpression(arguments[0], arguments[1], arguments[2]);
        }

        Report(WellKnownDiagnostics.Sparql.IfArityMismatch, span, $"IF takes exactly three arguments but received {arguments.Count}.");

        ExpressionNode missing = new ErrorExpression(span, Utf8String.FromString("Expression"), [WellKnownDiagnostics.Sparql.IfArityMismatch], []);
        ExpressionNode first = arguments.Count > 0 ? arguments[0] : missing;
        ExpressionNode second = arguments.Count > 1 ? arguments[1] : missing;
        ExpressionNode third = arguments.Count > 2 ? arguments[2] : missing;

        return new IfExpression(first, second, third);
    }

    /// <summary>
    /// Builds an <see cref="AggregateExpression"/> from a parsed single argument, reading the optional
    /// <c>GROUP_CONCAT</c> separator and consuming the closing parenthesis.
    /// </summary>
    /// <param name="frame">The expression frame carrying the aggregate name and distinct flag.</param>
    /// <param name="argument">The parsed aggregated expression.</param>
    /// <returns>The aggregate expression.</returns>
    private AggregateExpression BuildAggregate(ParseFrame frame, ExpressionNode argument)
    {
        Utf8String name = frame.CallName!.Value;
        Utf8String? separator = null;

        if(name.Span.SequenceEqual("GROUP_CONCAT"u8) && Current.Kind == SparqlTokenKind.Semicolon)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.SeparatorKeyword)
            {
                return RecoverAggregate(frame, name, argument, separator, WellKnownDiagnostics.Sparql.ExpectedKeyword, "Expected SEPARATOR in GROUP_CONCAT.");
            }

            Advance();
            if(Current.Kind != SparqlTokenKind.Equals)
            {
                return RecoverAggregate(frame, name, argument, separator, WellKnownDiagnostics.Sparql.ExpectedKeyword, "Expected '=' after SEPARATOR.");
            }

            Advance();
            if(Current.Kind is not (SparqlTokenKind.StringLiteral or SparqlTokenKind.LongStringLiteral))
            {
                return RecoverAggregate(frame, name, argument, separator, WellKnownDiagnostics.Sparql.ExpectedValuesValue, "Expected a string separator.");
            }

            separator = Current.Value;
            Advance();
        }

        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            return RecoverAggregate(frame, name, argument, separator, WellKnownDiagnostics.Sparql.ExpectedCloser, "Expected ')' to close the aggregate.");
        }

        Advance();

        return new AggregateExpression(SparqlFunctions.AggregateFromName(name), argument, frame.IsDistinct, IsCountStar: false, separator);
    }

    /// <summary>
    /// Recovers a malformed aggregate tail (separator or closer): reports the diagnostic, resyncs to the
    /// enclosing expression's resync set, and returns the faithful aggregate built from what was parsed.
    /// </summary>
    /// <param name="frame">The expression frame carrying the aggregate name and distinct flag.</param>
    /// <param name="name">The aggregate name.</param>
    /// <param name="argument">The parsed aggregated expression.</param>
    /// <param name="separator">The separator parsed so far, or <see langword="null"/>.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The aggregate expression.</returns>
    private AggregateExpression RecoverAggregate(ParseFrame frame, Utf8String name, ExpressionNode argument, Utf8String? separator, Utf8String code, string message)
    {
        _ = ReportRecoverable(code, Current.Span, message);
        ResyncTo(ParseFrameKind.Expression, Current.Span, out _);

        if(Current.Kind == SparqlTokenKind.CloseParen)
        {
            Advance();
        }

        return new AggregateExpression(SparqlFunctions.AggregateFromName(name), argument, frame.IsDistinct, IsCountStar: false, separator);
    }

    /// <summary>Advances a <c>GROUP BY</c> clause, parsing one grouping condition per step.</summary>
    /// <param name="frame">The group-by frame.</param>
    /// <param name="incoming">A popped condition expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepGroupBy(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => GroupByStart(frame),
            1 => GroupByCondition(frame),
            2 => GroupByParenReceived(frame, incoming),
            3 => GroupByBareReceived(frame, incoming),
            _ => throw new SparqlParseException("GROUP BY reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes <c>GROUP BY</c> and begins the condition list.</summary>
    /// <param name="frame">The group-by frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult GroupByStart(ParseFrame frame)
    {
        Advance();
        frame.GroupConditions = [];

        if(Current.Kind != SparqlTokenKind.ByKeyword)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedKeyword, Current.Span, "Expected BY after GROUP.");

            return StepResult.Done(new GroupClause(frame.GroupConditions));
        }

        Advance();
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses one grouping condition: a bare variable, a parenthesised expression (optionally bound with
    /// <c>AS</c>), or a bare built-in / function expression; completes when no further condition follows.
    /// </summary>
    /// <param name="frame">The group-by frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult GroupByCondition(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.GroupConditions!.Add(new GroupVariable(new SparqlVariable(Current.Value)));
            Advance();

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.OpenParen)
        {
            Advance();
            frame.Stage = 2;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(CanStartBareExpressionCondition(Current.Kind))
        {
            frame.Stage = 3;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(frame.GroupConditions!.Count == 0)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedSolutionCondition, Current.Span, "Expected a GROUP BY condition.");
        }

        return StepResult.Done(new GroupClause(frame.GroupConditions));
    }

    /// <summary>Completes a parenthesised grouping condition, with or without an <c>AS</c> binding.</summary>
    /// <param name="frame">The group-by frame.</param>
    /// <param name="incoming">The popped grouping expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult GroupByParenReceived(ParseFrame frame, object? incoming)
    {
        ExpressionNode expression = Pop<ExpressionNode>(incoming);

        if(Current.Kind == SparqlTokenKind.AsKeyword)
        {
            Advance();
            if(Current.Kind != SparqlTokenKind.Variable)
            {
                return RecoverGroupByExpression(frame, expression, WellKnownDiagnostics.Sparql.ExpectedVariable, "Expected a variable after AS.");
            }

            SparqlVariable variable = new(Current.Value);
            Advance();
            if(Current.Kind != SparqlTokenKind.CloseParen)
            {
                _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedCloser, Current.Span, "Expected ')' to close the GROUP BY expression.");
                frame.GroupConditions!.Add(new GroupExpressionAs(expression, variable));
                frame.Stage = 1;

                return StepResult.Continue();
            }

            Advance();
            frame.GroupConditions!.Add(new GroupExpressionAs(expression, variable));
            frame.Stage = 1;

            return StepResult.Continue();
        }

        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            return RecoverGroupByExpression(frame, expression, WellKnownDiagnostics.Sparql.ExpectedCloser, "Expected ')' to close the GROUP BY expression.");
        }

        Advance();
        frame.GroupConditions!.Add(new GroupExpression(expression));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Recovers a malformed parenthesised <c>GROUP BY</c> expression: reports the diagnostic, keeps the
    /// parsed expression as a bare grouping condition, resyncs to the clause's resync set, and continues.
    /// </summary>
    /// <param name="frame">The group-by frame.</param>
    /// <param name="expression">The parsed grouping expression to keep.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult RecoverGroupByExpression(ParseFrame frame, ExpressionNode expression, Utf8String code, string message)
    {
        _ = ReportRecoverable(code, Current.Span, message);
        ResyncTo(ParseFrameKind.GroupBy, Current.Span, out _);
        frame.GroupConditions!.Add(new GroupExpression(expression));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Adds a popped bare-expression grouping condition.</summary>
    /// <param name="frame">The group-by frame.</param>
    /// <param name="incoming">The popped grouping expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult GroupByBareReceived(ParseFrame frame, object? incoming)
    {
        frame.GroupConditions!.Add(new GroupExpression(Pop<ExpressionNode>(incoming)));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Advances a <c>HAVING</c> clause, parsing one constraint per step.</summary>
    /// <param name="frame">The having frame.</param>
    /// <param name="incoming">A popped constraint expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepHaving(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => HavingStart(frame),
            1 => HavingConstraint(frame),
            2 => HavingReceived(frame, incoming),
            _ => throw new SparqlParseException("HAVING reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes <c>HAVING</c> and begins the constraint list.</summary>
    /// <param name="frame">The having frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult HavingStart(ParseFrame frame)
    {
        Advance();
        frame.HavingConditions = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Parses one constraint (a bracketed or bare built-in / function expression), or completes the clause.</summary>
    /// <param name="frame">The having frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult HavingConstraint(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.OpenParen || CanStartBareExpressionCondition(Current.Kind))
        {
            frame.Stage = 2;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(frame.HavingConditions!.Count == 0)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedSolutionCondition, Current.Span, "Expected a HAVING constraint.");
        }

        return StepResult.Done(new HavingClause(frame.HavingConditions));
    }

    /// <summary>Adds a popped HAVING constraint expression.</summary>
    /// <param name="frame">The having frame.</param>
    /// <param name="incoming">The popped constraint expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult HavingReceived(ParseFrame frame, object? incoming)
    {
        frame.HavingConditions!.Add(Pop<ExpressionNode>(incoming));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Advances an <c>ORDER BY</c> clause, parsing one ordering condition per step.</summary>
    /// <param name="frame">The order-by frame.</param>
    /// <param name="incoming">A popped ordering-key expression on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepOrderBy(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => OrderByStart(frame),
            1 => OrderByCondition(frame),
            2 => OrderByDirectedReceived(frame, incoming),
            3 => OrderByBareReceived(frame, incoming),
            _ => throw new SparqlParseException("ORDER BY reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes <c>ORDER BY</c> and begins the condition list.</summary>
    /// <param name="frame">The order-by frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult OrderByStart(ParseFrame frame)
    {
        Advance();
        frame.OrderConditions = [];

        if(Current.Kind != SparqlTokenKind.ByKeyword)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedKeyword, Current.Span, "Expected BY after ORDER.");

            return StepResult.Done(new OrderClause(frame.OrderConditions));
        }

        Advance();
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses one ordering condition: an <c>ASC</c> / <c>DESC</c> bracketed key, a bare variable (ascending),
    /// or a bare constraint (ascending); completes when no further condition follows.
    /// </summary>
    /// <param name="frame">The order-by frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult OrderByCondition(ParseFrame frame)
    {
        if(Current.Kind is SparqlTokenKind.AscKeyword or SparqlTokenKind.DescKeyword)
        {
            frame.DescendingOrder = Current.Kind == SparqlTokenKind.DescKeyword;
            Advance();
            frame.Stage = 2;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.OrderConditions!.Add(new OrderAscending(new VariableExpression(new SparqlVariable(Current.Value))));
            Advance();

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.OpenParen || CanStartBareExpressionCondition(Current.Kind))
        {
            frame.Stage = 3;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Expression, MinPrecedence = PrecExpression, StartSpan = Current.Span });
        }

        if(frame.OrderConditions!.Count == 0)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedSolutionCondition, Current.Span, "Expected an ORDER BY condition.");
        }

        return StepResult.Done(new OrderClause(frame.OrderConditions));
    }

    /// <summary>Adds a popped <c>ASC</c> / <c>DESC</c> ordering condition with its recorded direction.</summary>
    /// <param name="frame">The order-by frame.</param>
    /// <param name="incoming">The popped ordering-key expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult OrderByDirectedReceived(ParseFrame frame, object? incoming)
    {
        ExpressionNode expression = Pop<ExpressionNode>(incoming);
        frame.OrderConditions!.Add(frame.DescendingOrder ? new OrderDescending(expression) : new OrderAscending(expression));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Adds a popped bare ordering condition as ascending (the default direction).</summary>
    /// <param name="frame">The order-by frame.</param>
    /// <param name="incoming">The popped ordering-key expression.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult OrderByBareReceived(ParseFrame frame, object? incoming)
    {
        frame.OrderConditions!.Add(new OrderAscending(Pop<ExpressionNode>(incoming)));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Determines whether a token kind can begin a bare (unparenthesised) expression condition — a
    /// built-in call, aggregate, or function — as <c>GROUP BY</c> / <c>HAVING</c> / <c>ORDER BY</c> allow.
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins such an expression.</returns>
    private static bool CanStartBareExpressionCondition(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.BuiltInFunctionName
            or SparqlTokenKind.AggregateFunctionName
            or SparqlTokenKind.Iri
            or SparqlTokenKind.PrefixedName;
    }

    /// <summary>Advances a property path's alternative level: <c>seq ( '|' seq )*</c>.</summary>
    /// <param name="frame">The property-path frame.</param>
    /// <param name="incoming">A popped sequence on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="PropertyPathExpression"/>.</returns>
    private StepResult StepPropertyPath(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => PathAlternativeStart(frame),
            1 => PathAlternativeNext(frame, incoming),
            _ => throw new SparqlParseException("Property path reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Begins the alternative list by pushing the first sequence.</summary>
    /// <param name="frame">The property-path frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathAlternativeStart(ParseFrame frame)
    {
        frame.PathItems = [];
        frame.Stage = 1;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PathSequence, StartSpan = Current.Span });
    }

    /// <summary>Adds a popped sequence, then pushes the next alternative or completes the path.</summary>
    /// <param name="frame">The property-path frame.</param>
    /// <param name="incoming">The popped sequence.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathAlternativeNext(ParseFrame frame, object? incoming)
    {
        frame.PathItems!.Add(Pop<PropertyPathExpression>(incoming));

        if(Current.Kind == SparqlTokenKind.Pipe)
        {
            Advance();

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PathSequence, StartSpan = Current.Span });
        }

        return StepResult.Done(frame.PathItems.Count == 1 ? frame.PathItems[0] : new PathAlternative(frame.PathItems));
    }

    /// <summary>Advances a path sequence: <c>elt ( '/' elt )*</c>.</summary>
    /// <param name="frame">The path-sequence frame.</param>
    /// <param name="incoming">A popped element on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepPathSequence(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => PathSequenceStart(frame),
            1 => PathSequenceNext(frame, incoming),
            _ => throw new SparqlParseException("Path sequence reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Begins the sequence by pushing the first element.</summary>
    /// <param name="frame">The path-sequence frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathSequenceStart(ParseFrame frame)
    {
        frame.PathItems = [];
        frame.Stage = 1;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PathElement, StartSpan = Current.Span });
    }

    /// <summary>Adds a popped element, then pushes the next step or completes the sequence.</summary>
    /// <param name="frame">The path-sequence frame.</param>
    /// <param name="incoming">The popped element.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathSequenceNext(ParseFrame frame, object? incoming)
    {
        frame.PathItems!.Add(Pop<PropertyPathExpression>(incoming));

        if(Current.Kind == SparqlTokenKind.Slash)
        {
            Advance();

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PathElement, StartSpan = Current.Span });
        }

        return StepResult.Done(frame.PathItems.Count == 1 ? frame.PathItems[0] : new PathSequence(frame.PathItems));
    }

    /// <summary>Advances a single path element: an optional inverse, a primary, and an optional quantifier.</summary>
    /// <param name="frame">The path-element frame.</param>
    /// <param name="incoming">A popped negated set or grouped path on resume.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepPathElement(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => PathElementPrimary(frame),
            1 => PathElementNegatedReceived(frame, incoming),
            2 => PathElementGroupReceived(frame, incoming),
            _ => throw new SparqlParseException("Path element reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>
    /// Reads the optional inverse marker and dispatches on the path primary: an IRI, <c>a</c>, a
    /// negated property set, or a grouped path.
    /// </summary>
    /// <param name="frame">The path-element frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathElementPrimary(ParseFrame frame)
    {
        frame.PathInverted = Current.Kind == SparqlTokenKind.Caret;
        if(frame.PathInverted)
        {
            Advance();
        }

        return Current.Kind switch
        {
            SparqlTokenKind.Iri => FinishPathElement(frame, new PathPredicate(ConsumeIriRef())),
            SparqlTokenKind.PrefixedName => FinishPathElement(frame, new PathPredicate(ConsumePrefixedName())),
            SparqlTokenKind.A => FinishPathElement(frame, new PathPredicate(ConsumeRdfTypeIriRef())),
            SparqlTokenKind.Bang => PushNegatedSet(frame),
            SparqlTokenKind.OpenParen => PushGroupedPath(frame),
            _ => StepResult.Done(RecoverPropertyPath(ParseFrameKind.PathElement, frame.StartSpan, WellKnownDiagnostics.Sparql.ExpectedPathPrimary, Current.Span, "Expected a path primary (an IRI, 'a', '!', or '(').", "PathPrimary"))
        };
    }

    /// <summary>Pushes a negated property set, to be completed when it returns.</summary>
    /// <param name="frame">The path-element frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PushNegatedSet(ParseFrame frame)
    {
        frame.Stage = 1;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PathNegatedSet, StartSpan = Current.Span });
    }

    /// <summary>Consumes the opening parenthesis and pushes a grouped path, to be closed when it returns.</summary>
    /// <param name="frame">The path-element frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PushGroupedPath(ParseFrame frame)
    {
        Advance();
        frame.Stage = 2;

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PropertyPath, StartSpan = Current.Span });
    }

    /// <summary>Completes the element from a popped negated property set.</summary>
    /// <param name="frame">The path-element frame.</param>
    /// <param name="incoming">The popped negated property set.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathElementNegatedReceived(ParseFrame frame, object? incoming)
    {
        return FinishPathElement(frame, Pop<PropertyPathExpression>(incoming));
    }

    /// <summary>Consumes the closing parenthesis and completes the element from a popped grouped path.</summary>
    /// <param name="frame">The path-element frame.</param>
    /// <param name="incoming">The popped grouped path.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathElementGroupReceived(ParseFrame frame, object? incoming)
    {
        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            //A missing close paren keeps the parsed grouped path; the diagnostic flags the gap.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedPath, Current.Span, "Expected ')' to close the grouped path.");

            return FinishPathElement(frame, Pop<PropertyPathExpression>(incoming));
        }

        Advance();

        return FinishPathElement(frame, Pop<PropertyPathExpression>(incoming));
    }

    /// <summary>Applies the optional quantifier and inverse to a path primary and completes the element.</summary>
    /// <param name="frame">The path-element frame carrying the inverse flag.</param>
    /// <param name="primary">The parsed path primary.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult FinishPathElement(ParseFrame frame, PropertyPathExpression primary)
    {
        PropertyPathExpression quantified = ApplyPathModifier(primary);
        PropertyPathExpression element = frame.PathInverted ? new PathInverse(quantified) : quantified;

        return StepResult.Done(element);
    }

    /// <summary>Applies the trailing <c>?</c> / <c>*</c> / <c>+</c> quantifier at the cursor, if any.</summary>
    /// <param name="primary">The path primary to quantify.</param>
    /// <returns>The quantified path, or the primary unchanged.</returns>
    private PropertyPathExpression ApplyPathModifier(PropertyPathExpression primary)
        => Current.Kind switch
        {
            SparqlTokenKind.Question => ConsumePathModifier(new PathZeroOrOne(primary)),
            SparqlTokenKind.Star => ConsumePathModifier(new PathZeroOrMore(primary)),
            SparqlTokenKind.Plus => ConsumePathModifier(new PathOneOrMore(primary)),
            _ => primary
        };

    /// <summary>Consumes the quantifier token at the cursor and returns the already-built quantified path.</summary>
    /// <param name="quantified">The quantified path node.</param>
    /// <returns>The quantified path.</returns>
    private PropertyPathExpression ConsumePathModifier(PropertyPathExpression quantified)
    {
        Advance();

        return quantified;
    }

    /// <summary>Advances a negated property set: <c>!iri</c>, <c>!a</c>, <c>!^iri</c>, or <c>!( ... )</c>.</summary>
    /// <param name="frame">The negated-set frame.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="PathNegatedSet"/>.</returns>
    private StepResult StepPathNegatedSet(ParseFrame frame)
        => frame.Stage switch
        {
            0 => PathNegatedSetStart(frame),
            1 => PathNegatedSetNext(frame),
            _ => throw new SparqlParseException("Negated property set reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes <c>!</c> and the first element, opening the parenthesised list when present.</summary>
    /// <param name="frame">The negated-set frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathNegatedSetStart(ParseFrame frame)
    {
        Advance();
        frame.NegatedElements = [];

        if(Current.Kind == SparqlTokenKind.OpenParen)
        {
            Advance();
            if(Current.Kind == SparqlTokenKind.CloseParen)
            {
                Advance();

                return StepResult.Done(new PathNegatedSet(frame.NegatedElements));
            }

            frame.NegatedElements.Add(ParsePathOneInPropertySet());
            frame.Stage = 1;

            return StepResult.Continue();
        }

        frame.NegatedElements.Add(ParsePathOneInPropertySet());

        return StepResult.Done(new PathNegatedSet(frame.NegatedElements));
    }

    /// <summary>Reads one more <c>|</c>-separated element, or closes the parenthesised list.</summary>
    /// <param name="frame">The negated-set frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult PathNegatedSetNext(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.Pipe)
        {
            Advance();
            frame.NegatedElements!.Add(ParsePathOneInPropertySet());

            return StepResult.Continue();
        }

        if(Current.Kind != SparqlTokenKind.CloseParen)
        {
            //Finalise the negated set with the elements gathered so far, skipping junk to the closer.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedNegatedPathItem, Current.Span, "Expected '|' or ')' in the negated property set.");
            ResyncTo(ParseFrameKind.PathNegatedSet, Current.Span, out _);

            if(Current.Kind == SparqlTokenKind.CloseParen)
            {
                Advance();
            }

            return StepResult.Done(new PathNegatedSet(frame.NegatedElements!));
        }

        Advance();

        return StepResult.Done(new PathNegatedSet(frame.NegatedElements!));
    }

    /// <summary>Parses one <c>PathOneInPropertySet</c>: an optional inverse and an IRI or <c>a</c>.</summary>
    /// <returns>The negated forward or inverse element.</returns>
    private PathNegatedElement ParsePathOneInPropertySet()
    {
        bool inverse = Current.Kind == SparqlTokenKind.Caret;
        if(inverse)
        {
            Advance();
        }

        //A negated property set element has no error-node variant; on a bad token report and yield a
        //forward element over rdf:type without advancing, so the enclosing negated-set frame's next step
        //reaches its resync (the cursor cannot advance past a non-IRI here, so progress is the frame's).
        IriRef iri = Current.Kind switch
        {
            SparqlTokenKind.Iri => ConsumeIriRef(),
            SparqlTokenKind.PrefixedName => ConsumePrefixedName(),
            SparqlTokenKind.A => ConsumeRdfTypeIriRef(),
            _ => RecoverNegatedPathItem()
        };

        return inverse ? new PathNegatedInverse(iri) : new PathNegatedForward(iri);
    }

    /// <summary>
    /// Reports a missing negated-property-set element and returns an <c>rdf:type</c> IRI reference as the
    /// faithful fallback (the element has no error-node variant); the cursor is not advanced.
    /// </summary>
    /// <returns>The fallback IRI reference.</returns>
    private IriRef RecoverNegatedPathItem()
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedNegatedPathItem, Current.Span, "Expected an IRI or 'a' in the negated property set.");

        return new IriRef(rdfType, Current.Span);
    }

    /// <summary>Consumes the <c>a</c> shorthand at the cursor as an <c>rdf:type</c> IRI reference and advances.</summary>
    /// <returns>The <c>rdf:type</c> IRI reference.</returns>
    private IriRef ConsumeRdfTypeIriRef()
    {
        SourceSpan span = Current.Span;
        Advance();

        return new IriRef(rdfType, span);
    }

    /// <summary>
    /// Determines whether a token kind can begin a property path (an IRI, <c>a</c>, an inverse, a
    /// negated set, or a group).
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a path.</returns>
    private static bool CanStartPath(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.Iri
            or SparqlTokenKind.A
            or SparqlTokenKind.PrefixedName
            or SparqlTokenKind.Caret
            or SparqlTokenKind.Bang
            or SparqlTokenKind.OpenParen;
    }

    /// <summary>
    /// Returns the precedence of a binary operator token, or zero for a token that is not a binary
    /// operator (which therefore ends an operand chain).
    /// </summary>
    /// <param name="kind">The token kind at the cursor.</param>
    /// <returns>The operator precedence, or zero.</returns>
    private static int BinaryPrecedence(SparqlTokenKind kind)
    {
        return kind switch
        {
            SparqlTokenKind.LogicalOr => PrecConditionalOr,
            SparqlTokenKind.LogicalAnd => PrecConditionalAnd,
            SparqlTokenKind.Equals => PrecComparison,
            SparqlTokenKind.NotEquals => PrecComparison,
            SparqlTokenKind.LessThan => PrecComparison,
            SparqlTokenKind.LessOrEqual => PrecComparison,
            SparqlTokenKind.GreaterThan => PrecComparison,
            SparqlTokenKind.GreaterOrEqual => PrecComparison,
            SparqlTokenKind.Plus => PrecAdditive,
            SparqlTokenKind.Minus => PrecAdditive,
            SparqlTokenKind.Star => PrecMultiplicative,
            SparqlTokenKind.Slash => PrecMultiplicative,
            _ => 0
        };
    }

    /// <summary>
    /// Determines whether a token kind is one of the six relational comparison operators.
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> for a comparison operator.</returns>
    private static bool IsComparison(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.Equals
            or SparqlTokenKind.NotEquals
            or SparqlTokenKind.LessThan
            or SparqlTokenKind.LessOrEqual
            or SparqlTokenKind.GreaterThan
            or SparqlTokenKind.GreaterOrEqual;
    }

    /// <summary>
    /// Combines a left and right operand under a binary operator into the matching expression node.
    /// </summary>
    /// <param name="operatorKind">The binary operator token kind.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The combined expression.</returns>
    private static ExpressionNode CombineBinary(SparqlTokenKind operatorKind, ExpressionNode left, ExpressionNode right)
    {
        return operatorKind switch
        {
            SparqlTokenKind.LogicalOr => new OrExpression(left, right),
            SparqlTokenKind.LogicalAnd => new AndExpression(left, right),
            SparqlTokenKind.Equals => new ComparisonExpression(left, ComparisonOp.Equal, right),
            SparqlTokenKind.NotEquals => new ComparisonExpression(left, ComparisonOp.NotEqual, right),
            SparqlTokenKind.LessThan => new ComparisonExpression(left, ComparisonOp.LessThan, right),
            SparqlTokenKind.LessOrEqual => new ComparisonExpression(left, ComparisonOp.LessOrEqual, right),
            SparqlTokenKind.GreaterThan => new ComparisonExpression(left, ComparisonOp.GreaterThan, right),
            SparqlTokenKind.GreaterOrEqual => new ComparisonExpression(left, ComparisonOp.GreaterOrEqual, right),
            SparqlTokenKind.Plus => new ArithmeticExpression(left, ArithmeticOp.Add, right),
            SparqlTokenKind.Minus => new ArithmeticExpression(left, ArithmeticOp.Subtract, right),
            SparqlTokenKind.Star => new ArithmeticExpression(left, ArithmeticOp.Multiply, right),
            SparqlTokenKind.Slash => new ArithmeticExpression(left, ArithmeticOp.Divide, right),
            _ => throw new SparqlParseException($"Token {operatorKind} is not a binary operator.")
        };
    }

    /// <summary>
    /// Wraps an operand under a unary operator into the matching expression node.
    /// </summary>
    /// <param name="operatorKind">The unary operator token kind.</param>
    /// <param name="operand">The operand.</param>
    /// <returns>The unary expression.</returns>
    private static ExpressionNode WrapUnary(SparqlTokenKind operatorKind, ExpressionNode operand)
    {
        return operatorKind switch
        {
            SparqlTokenKind.Bang => new NotExpression(operand),
            SparqlTokenKind.Minus => new ArithmeticExpression(operand, ArithmeticOp.UnaryMinus, Right: null),
            SparqlTokenKind.Plus => new ArithmeticExpression(operand, ArithmeticOp.UnaryPlus, Right: null),
            _ => throw new SparqlParseException($"Token {operatorKind} is not a unary operator.")
        };
    }

    /// <summary>
    /// Advances one <c>TriplesSameSubject</c>: parses the subject, then walks its predicate-object
    /// list one object per step, emitting a triple for each object and threading the subject and the
    /// current verb through the frame.
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <returns>The instruction for the driver; the popped result is the run of parsed triples.</returns>
    private StepResult StepTriple(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => TripleSubject(frame),
            1 => TripleVerb(frame),
            2 => TripleObject(frame),
            3 => TripleVerbPath(frame, incoming),
            4 => TripleSubjectReceived(frame, incoming),
            5 => TripleObjectReceived(frame, incoming),
            6 => TripleObjectAnnotations(frame),
            7 => TripleObjectAnnotationReceived(frame, incoming),
            _ => throw new SparqlParseException("Triple reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Parses the subject of a triple block (pushing a frame for a compound term) and moves to the verb.</summary>
    /// <param name="frame">The triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleSubject(ParseFrame frame)
    {
        if(CanStartCompoundTerm(Current.Kind))
        {
            frame.Stage = 4;

            return StepResult.Push(CompoundTermFrame());
        }

        frame.Subject = ParseVarOrTerm();
        frame.TripleAccumulator = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Adopts a popped compound subject term and moves to the verb.</summary>
    /// <param name="frame">The triple frame.</param>
    /// <param name="incoming">The popped subject term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult TripleSubjectReceived(ParseFrame frame, object? incoming)
    {
        frame.Subject = Pop<TriplePatternTerm>(incoming);
        frame.TripleAccumulator = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses one verb of the predicate-object list: a bare variable inline, or a property path pushed
    /// as a child frame. Moves to the objects (or the path-received stage).
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleVerb(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.Verb = ConsumeVariable();
            frame.Stage = 2;

            return StepResult.Continue();
        }

        if(CanStartPath(Current.Kind))
        {
            frame.VerbSpanStart = Current.Span;
            frame.Stage = 3;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PropertyPath, StartSpan = Current.Span });
        }

        //A missing predicate finalises the triple run with the triples gathered so far, plus a
        //diagnostic; junk up to the next separator/closer is skipped.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedVerb, Current.Span, "Expected a predicate or property path.");
        ResyncTo(ParseFrameKind.Triple, Current.Span, out _);

        return StepResult.Done(frame.TripleAccumulator!);
    }

    /// <summary>Converts the popped property path into a verb term — unwrapping a bare predicate — and moves to the objects.</summary>
    /// <param name="frame">The triple frame.</param>
    /// <param name="incoming">The popped property path.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleVerbPath(ParseFrame frame, object? incoming)
    {
        PropertyPathExpression path = Pop<PropertyPathExpression>(incoming);
        SourceSpan span = CombineSpans(frame.VerbSpanStart, lastConsumedSpan);
        frame.Verb = path is PathPredicate predicate
            ? new ConstantTerm(span, new NamedNode(predicate.Predicate.Value))
            : new PropertyPathTerm(span, path);
        frame.Stage = 2;

        return StepResult.Continue();
    }

    /// <summary>
    /// Parses one object (pushing a frame for a compound term), then moves to its RDF 1.2 annotation
    /// tail before the triple is emitted.
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleObject(ParseFrame frame)
    {
        if(CanStartCompoundTerm(Current.Kind))
        {
            frame.Stage = 5;

            return StepResult.Push(CompoundTermFrame());
        }

        frame.ObjectTerm = ParseVarOrTerm();
        frame.Annotations = null;
        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>Adopts a popped compound object term and moves to its annotation tail.</summary>
    /// <param name="frame">The triple frame.</param>
    /// <param name="incoming">The popped object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult TripleObjectReceived(ParseFrame frame, object? incoming)
    {
        frame.ObjectTerm = Pop<TriplePatternTerm>(incoming);
        frame.Annotations = null;
        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>
    /// Reads the object's RDF 1.2 annotation tail — a run of reifiers (<c>~ id?</c>) and annotation
    /// blocks (<c>{| … |}</c>) — then emits the triple (wrapping the object in an
    /// <see cref="AnnotatedObject"/> when any annotation was present). Annotations require a non-path
    /// predicate; a compound property-path verb followed by an annotation is rejected.
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleObjectAnnotations(ParseFrame frame)
    {
        if(Current.Kind is SparqlTokenKind.Tilde or SparqlTokenKind.OpenAnnotation)
        {
            //An annotation after a property-path predicate is a positional violation; report it and keep
            //the faithful annotation rather than discarding the construct.
            if(frame.Verb is PropertyPathTerm && !frame.ReportedPathAnnotation)
            {
                Report(WellKnownDiagnostics.Sparql.AnnotationOnPathVerb, Current.Span, "An annotation cannot follow a property-path predicate.");
                frame.ReportedPathAnnotation = true;
            }

            frame.Annotations ??= [];

            if(Current.Kind == SparqlTokenKind.Tilde)
            {
                SourceSpan start = Current.Span;
                Advance();
                TriplePatternTerm? reifier = CanStartReifierId(Current.Kind) ? ParseReifierId() : null;
                frame.Annotations.Add(new ReifierAnnotation(CombineSpans(start, lastConsumedSpan), reifier));

                return StepResult.Continue();
            }

            frame.Stage = 7;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.AnnotationBlock, StartSpan = Current.Span });
        }

        TriplePatternTerm objectTerm = frame.Annotations is { Count: > 0 }
            ? new AnnotatedObject(CombineSpans(SpanOf(frame.ObjectTerm!), lastConsumedSpan), frame.ObjectTerm!, frame.Annotations)
            : frame.ObjectTerm!;

        return EmitObject(frame, objectTerm);
    }

    /// <summary>
    /// Adopts a popped annotation block (or, under recovery, an <see cref="ErrorAnnotation"/>) and
    /// returns to the annotation tail for any further annotations.
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <param name="incoming">The popped annotation.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult TripleObjectAnnotationReceived(ParseFrame frame, object? incoming)
    {
        frame.Annotations!.Add(Pop<Annotation>(incoming));
        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>
    /// Records the triple for one object, then continues the object list on a comma, restarts the verb
    /// on a semicolon, or completes the run. The triple's span runs from the subject to this object.
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <param name="objectTerm">The parsed object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult EmitObject(ParseFrame frame, TriplePatternTerm objectTerm)
    {
        SourceSpan span = CombineSpans(frame.StartSpan, lastConsumedSpan);
        frame.TripleAccumulator!.Add(new TriplePattern(span, frame.Subject!, frame.Verb!, objectTerm));

        if(Current.Kind == SparqlTokenKind.Comma)
        {
            Advance();
            frame.Stage = 2;

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.Semicolon)
        {
            while(Current.Kind == SparqlTokenKind.Semicolon)
            {
                Advance();
            }

            if(CanStartVerb(Current.Kind))
            {
                frame.Stage = 1;

                return StepResult.Continue();
            }

            return StepResult.Done(frame.TripleAccumulator);
        }

        return StepResult.Done(frame.TripleAccumulator);
    }

    /// <summary>Builds the frame for the compound term (collection or blank-node property list) at the cursor.</summary>
    /// <returns>The compound-term frame to push.</returns>
    private ParseFrame CompoundTermFrame()
    {
        ParseFrameKind kind = Current.Kind switch
        {
            SparqlTokenKind.OpenParen => ParseFrameKind.Collection,
            SparqlTokenKind.OpenTripleTerm => ParseFrameKind.TripleTerm,
            SparqlTokenKind.OpenReifiedTriple => ParseFrameKind.ReifiedTriple,
            _ => ParseFrameKind.BlankNodePropertyList
        };

        return new ParseFrame { Kind = kind, StartSpan = Current.Span };
    }

    /// <summary>
    /// Determines whether a token kind begins a compound (frame-parsed) term: a collection, a
    /// blank-node property list, or an RDF 1.2 triple term.
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a compound term.</returns>
    private static bool CanStartCompoundTerm(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.OpenParen
            or SparqlTokenKind.OpenBracket
            or SparqlTokenKind.OpenTripleTerm
            or SparqlTokenKind.OpenReifiedTriple;
    }

    /// <summary>
    /// Advances an RDF 1.2 triple term <c>&lt;&lt;( s verb o )&gt;&gt;</c>, parsing the inner subject,
    /// verb, and object one step each. The inner subject is a leaf term (no nesting), the verb is an
    /// IRI / <c>a</c> / variable (no property paths), and the object is a leaf term or a nested triple
    /// term. Collections, blank-node property lists, and reified triples are rejected inside.
    /// </summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <param name="incoming">A popped nested triple-term object on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="Ast.TripleTerm"/>.</returns>
    private StepResult StepTripleTerm(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => TripleTermOpen(frame),
            1 => TripleTermSubject(frame),
            2 => TripleTermVerb(frame),
            3 => TripleTermObject(frame),
            4 => TripleTermObjectReceived(frame, incoming),
            5 => TripleTermClose(frame),
            6 => TripleTermSubjectReceived(frame, incoming),
            _ => throw new SparqlParseException("Triple term reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes the opening <c>&lt;&lt;(</c> and moves to the inner subject.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleTermOpen(ParseFrame frame)
    {
        Advance();
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Parses the inner subject — a nested triple term (pushed) or a leaf term — and moves to the verb.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleTermSubject(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.OpenTripleTerm)
        {
            frame.Stage = 6;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.TripleTerm, StartSpan = Current.Span });
        }

        frame.Subject = ParseVarOrTerm();
        frame.Stage = 2;

        return StepResult.Continue();
    }

    /// <summary>Adopts a popped nested triple-term subject and moves to the verb.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <param name="incoming">The popped subject term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult TripleTermSubjectReceived(ParseFrame frame, object? incoming)
    {
        frame.Subject = Pop<TriplePatternTerm>(incoming);
        frame.Stage = 2;

        return StepResult.Continue();
    }

    /// <summary>Parses the inner verb (IRI, <c>a</c>, or variable; no property paths) and moves to the object.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleTermVerb(ParseFrame frame)
    {
        frame.Verb = ParseTripleVerb();
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Parses the inner object: a nested triple term (pushed) or a leaf term, then moves to the close.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult TripleTermObject(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.OpenTripleTerm)
        {
            frame.Stage = 4;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.TripleTerm, StartSpan = Current.Span });
        }

        frame.ObjectTerm = ParseVarOrTerm();
        frame.Stage = 5;

        return StepResult.Continue();
    }

    /// <summary>Adopts a popped nested triple-term object and moves to the close.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <param name="incoming">The popped object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult TripleTermObjectReceived(ParseFrame frame, object? incoming)
    {
        frame.ObjectTerm = Pop<TriplePatternTerm>(incoming);
        frame.Stage = 5;

        return StepResult.Continue();
    }

    /// <summary>Consumes the closing <c>)&gt;&gt;</c> and emits the triple term over its inner triple.</summary>
    /// <param name="frame">The triple-term frame.</param>
    /// <returns>The instruction for the driver; the result is the <see cref="Ast.TripleTerm"/>.</returns>
    private StepResult TripleTermClose(ParseFrame frame)
    {
        if(Current.Kind != SparqlTokenKind.CloseTripleTerm)
        {
            return StepResult.Done(RecoverTriplePatternTerm(ParseFrameKind.TripleTerm, frame.StartSpan, WellKnownDiagnostics.Sparql.UnclosedTripleTerm, Current.Span, "Expected ')>>' to close the triple term.", "TripleTerm"));
        }

        Advance();
        SourceSpan span = CombineSpans(frame.StartSpan, lastConsumedSpan);
        TriplePattern inner = new(span, frame.Subject!, frame.Verb!, frame.ObjectTerm!);

        return StepResult.Done(new Ast.TripleTerm(span, inner));
    }

    /// <summary>
    /// Parses the verb inside a triple term or reified triple: an IRI, the <c>a</c> shorthand, or a
    /// variable. Property paths and blank-node predicates are not permitted here.
    /// </summary>
    /// <returns>The verb term.</returns>
    private TriplePatternTerm ParseTripleVerb()
        => Current.Kind switch
        {
            SparqlTokenKind.A => ConsumeAVerbTerm(),
            SparqlTokenKind.Iri => ConsumeIriConstantTerm(),
            SparqlTokenKind.PrefixedName => ConsumePrefixedNameConstantTerm(),
            SparqlTokenKind.Variable => ConsumeVariable(),
            _ => RecoverTriplePatternTerm(ParseFrameKind.TripleTerm, Current.Span, WellKnownDiagnostics.Sparql.ExpectedTripleTermVerb, Current.Span, "Expected an IRI, 'a', or variable as the verb.", "Verb")
        };

    /// <summary>Consumes the <c>a</c> shorthand as the <c>rdf:type</c> constant term and advances.</summary>
    /// <returns>The <c>rdf:type</c> constant term.</returns>
    private ConstantTerm ConsumeAVerbTerm()
    {
        SourceSpan span = Current.Span;
        Advance();

        return new ConstantTerm(span, new NamedNode(rdfType));
    }

    /// <summary>
    /// Advances an RDF 1.2 reified triple <c>&lt;&lt; s verb o ~r? &gt;&gt;</c>. The inner subject and
    /// object may each be a leaf term or a nested reified triple / triple term; the verb is an IRI /
    /// <c>a</c> / variable (no property paths); an optional reifier follows the object as <c>~</c> with
    /// an optional IRI / variable / blank-node identity (a bare <c>~</c>, or none, means a fresh
    /// anonymous reifier). Collections and blank-node property lists are rejected inside.
    /// </summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <param name="incoming">A popped nested subject or object on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="ReifiedTriple"/>.</returns>
    private StepResult StepReifiedTriple(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => ReifiedTripleOpen(frame),
            1 => ReifiedTripleSubject(frame),
            2 => ReifiedTripleSubjectReceived(frame, incoming),
            3 => ReifiedTripleVerb(frame),
            4 => ReifiedTripleObject(frame),
            5 => ReifiedTripleObjectReceived(frame, incoming),
            6 => ReifiedTripleClose(frame),
            _ => throw new SparqlParseException("Reified triple reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes the opening <c>&lt;&lt;</c> and moves to the inner subject.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ReifiedTripleOpen(ParseFrame frame)
    {
        Advance();
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Parses the inner subject — a nested reified triple / triple term (pushed) or a leaf term — and moves to the verb.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ReifiedTripleSubject(ParseFrame frame)
    {
        if(Current.Kind is SparqlTokenKind.OpenReifiedTriple or SparqlTokenKind.OpenTripleTerm)
        {
            frame.Stage = 2;

            return StepResult.Push(CompoundTermFrame());
        }

        frame.Subject = ParseVarOrTerm();
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Adopts a popped nested subject term and moves to the verb.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <param name="incoming">The popped subject term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ReifiedTripleSubjectReceived(ParseFrame frame, object? incoming)
    {
        frame.Subject = Pop<TriplePatternTerm>(incoming);
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Parses the inner verb (IRI, <c>a</c>, or variable; no property paths) and moves to the object.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ReifiedTripleVerb(ParseFrame frame)
    {
        frame.Verb = ParseTripleVerb();
        frame.Stage = 4;

        return StepResult.Continue();
    }

    /// <summary>Parses the inner object — a nested reified triple / triple term (pushed) or a leaf term — and moves to the reifier/close.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ReifiedTripleObject(ParseFrame frame)
    {
        if(Current.Kind is SparqlTokenKind.OpenReifiedTriple or SparqlTokenKind.OpenTripleTerm)
        {
            frame.Stage = 5;

            return StepResult.Push(CompoundTermFrame());
        }

        frame.ObjectTerm = ParseVarOrTerm();
        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>Adopts a popped nested object term and moves to the reifier/close.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <param name="incoming">The popped object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult ReifiedTripleObjectReceived(ParseFrame frame, object? incoming)
    {
        frame.ObjectTerm = Pop<TriplePatternTerm>(incoming);
        frame.Stage = 6;

        return StepResult.Continue();
    }

    /// <summary>Reads the optional <c>~ reifier?</c>, consumes the closing <c>&gt;&gt;</c>, and emits the reified triple.</summary>
    /// <param name="frame">The reified-triple frame.</param>
    /// <returns>The instruction for the driver; the result is the <see cref="ReifiedTriple"/>.</returns>
    private StepResult ReifiedTripleClose(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.Tilde)
        {
            Advance();
            frame.Reifier = CanStartReifierId(Current.Kind) ? ParseReifierId() : null;
        }

        if(Current.Kind != SparqlTokenKind.CloseReifiedTriple)
        {
            return StepResult.Done(RecoverTriplePatternTerm(ParseFrameKind.ReifiedTriple, frame.StartSpan, WellKnownDiagnostics.Sparql.UnclosedReifiedTriple, Current.Span, "Expected '>>' to close the reified triple.", "ReifiedTriple"));
        }

        Advance();
        SourceSpan span = CombineSpans(frame.StartSpan, lastConsumedSpan);
        TriplePattern inner = new(span, frame.Subject!, frame.Verb!, frame.ObjectTerm!);

        return StepResult.Done(new ReifiedTriple(span, inner, frame.Reifier));
    }

    /// <summary>Determines whether a token kind can begin a reifier identity (IRI, variable, or blank node).</summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a reifier identity.</returns>
    private static bool CanStartReifierId(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.Iri
            or SparqlTokenKind.PrefixedName
            or SparqlTokenKind.Variable
            or SparqlTokenKind.BlankNodeLabel
            or SparqlTokenKind.AnonymousBlankNode;
    }

    /// <summary>Parses a reifier identity after <c>~</c>: an IRI, a variable, or a blank node.</summary>
    /// <returns>The reifier term.</returns>
    private TriplePatternTerm ParseReifierId()
        => Current.Kind switch
        {
            SparqlTokenKind.Iri => ConsumeIriConstantTerm(),
            SparqlTokenKind.PrefixedName => ConsumePrefixedNameConstantTerm(),
            SparqlTokenKind.Variable => ConsumeVariable(),
            SparqlTokenKind.BlankNodeLabel => ConsumeBlankNodeTerm(),
            SparqlTokenKind.AnonymousBlankNode => ConsumeAnonymousBlankNodeTerm(),
            _ => RecoverTriplePatternTerm(ParseFrameKind.ReifiedTriple, Current.Span, WellKnownDiagnostics.Sparql.ExpectedReifier, Current.Span, "Expected an IRI, variable, or blank node as the reifier.", "Reifier")
        };

    /// <summary>Advances a collection term <c>( ... )</c>, parsing one item per step.</summary>
    /// <param name="frame">The collection frame.</param>
    /// <param name="incoming">A popped compound item on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="CollectionTerm"/>.</returns>
    private StepResult StepCollection(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => CollectionStart(frame),
            1 => CollectionItem(frame),
            2 => CollectionItemReceived(frame, incoming),
            _ => throw new SparqlParseException("Collection reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes the opening parenthesis and begins the item list.</summary>
    /// <param name="frame">The collection frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult CollectionStart(ParseFrame frame)
    {
        Advance();
        frame.TermItems = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Closes the collection at <c>)</c>, pushes a compound item, or parses a leaf item.</summary>
    /// <param name="frame">The collection frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult CollectionItem(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.CloseParen)
        {
            Advance();

            return StepResult.Done(new CollectionTerm(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.TermItems!));
        }

        //An unterminated collection — end of input, or a token that cannot start an item and is not ')' —
        //is finalised with the items gathered so far plus a diagnostic. Refusing to parse a leaf on a
        //non-item token is what guarantees progress: parsing one would recover without advancing and the
        //collection would re-enter forever.
        if(Current.Kind == SparqlTokenKind.EndOfInput || !CanStartTriple(Current.Kind))
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedCollection, Current.Span, "Expected ')' to close the collection.");

            return StepResult.Done(new CollectionTerm(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.TermItems!));
        }

        if(CanStartCompoundTerm(Current.Kind))
        {
            frame.Stage = 2;

            return StepResult.Push(CompoundTermFrame());
        }

        frame.TermItems!.Add(ParseVarOrTerm());

        return StepResult.Continue();
    }

    /// <summary>Adds a popped compound item and continues the collection.</summary>
    /// <param name="frame">The collection frame.</param>
    /// <param name="incoming">The popped item term.</param>
    /// <returns>The instruction for the driver.</returns>
    private static StepResult CollectionItemReceived(ParseFrame frame, object? incoming)
    {
        frame.TermItems!.Add(Pop<TriplePatternTerm>(incoming));
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Advances a blank-node property list <c>[ verb objects ; ... ]</c>, one verb/object step at a time.</summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <param name="incoming">A popped path verb or compound object on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="BlankNodePropertyListTerm"/>.</returns>
    private StepResult StepBlankNodePropertyList(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => BlankNodeListStart(frame),
            1 => BlankNodeListVerb(frame),
            2 => BlankNodeListVerbPath(frame, incoming),
            3 => BlankNodeListObject(frame),
            4 => BlankNodeListObjectReceived(frame, incoming),
            _ => throw new SparqlParseException("Blank-node property list reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes the opening bracket and begins the predicate-object list.</summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlankNodeListStart(ParseFrame frame)
    {
        Advance();
        frame.Properties = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Parses one verb (a variable inline or a property path) and begins its object list.</summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlankNodeListVerb(ParseFrame frame)
    {
        frame.VerbSpanStart = Current.Span;

        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.Verb = ConsumeVariable();
            frame.TermItems = [];
            frame.Stage = 3;

            return StepResult.Continue();
        }

        if(CanStartPath(Current.Kind))
        {
            frame.Stage = 2;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PropertyPath, StartSpan = Current.Span });
        }

        //A blank-node property list with no further verb is finalised with the entries gathered so far,
        //skipping junk to its ']' closer — yielding the term faithfully for an editor.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedBlankNodePropertyList, Current.Span, "Expected a predicate or property path inside the blank-node property list.");
        ResyncTo(ParseFrameKind.BlankNodePropertyList, Current.Span, out _);

        if(Current.Kind == SparqlTokenKind.CloseBracket)
        {
            Advance();
        }

        return StepResult.Done(new BlankNodePropertyListTerm(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Properties!));
    }

    /// <summary>Converts the popped property path into the current verb and begins its object list.</summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <param name="incoming">The popped property path.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlankNodeListVerbPath(ParseFrame frame, object? incoming)
    {
        PropertyPathExpression path = Pop<PropertyPathExpression>(incoming);
        SourceSpan span = CombineSpans(frame.VerbSpanStart, lastConsumedSpan);
        frame.Verb = path is PathPredicate predicate
            ? new ConstantTerm(span, new NamedNode(predicate.Predicate.Value))
            : new PropertyPathTerm(span, path);
        frame.TermItems = [];
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Parses one object (pushing a frame for a compound term) of the current verb's object list.</summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlankNodeListObject(ParseFrame frame)
    {
        if(CanStartCompoundTerm(Current.Kind))
        {
            frame.Stage = 4;

            return StepResult.Push(CompoundTermFrame());
        }

        //A token that cannot begin an object finalises the list with the entries gathered so far; parsing
        //a leaf here would recover without advancing and the list would re-enter forever.
        if(!CanStartTriple(Current.Kind))
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedBlankNodePropertyList, Current.Span, "Expected an object inside the blank-node property list.");
            ResyncTo(ParseFrameKind.BlankNodePropertyList, Current.Span, out _);

            if(Current.Kind == SparqlTokenKind.CloseBracket)
            {
                Advance();
            }

            return StepResult.Done(new BlankNodePropertyListTerm(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Properties!));
        }

        return BlankNodeListAppendObject(frame, ParseVarOrTerm());
    }

    /// <summary>Appends a popped compound object and continues the object list.</summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <param name="incoming">The popped object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlankNodeListObjectReceived(ParseFrame frame, object? incoming)
    {
        return BlankNodeListAppendObject(frame, Pop<TriplePatternTerm>(incoming));
    }

    /// <summary>
    /// Adds one object; on a comma continues the object list, otherwise records the predicate-object
    /// entry and then continues the next verb on a semicolon or closes the list at <c>]</c>.
    /// </summary>
    /// <param name="frame">The blank-node-property-list frame.</param>
    /// <param name="objectTerm">The parsed object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult BlankNodeListAppendObject(ParseFrame frame, TriplePatternTerm objectTerm)
    {
        frame.TermItems!.Add(objectTerm);

        if(Current.Kind == SparqlTokenKind.Comma)
        {
            Advance();
            frame.Stage = 3;

            return StepResult.Continue();
        }

        SourceSpan entrySpan = CombineSpans(frame.VerbSpanStart, lastConsumedSpan);
        frame.Properties!.Add(new PropertyListPath(entrySpan, frame.Verb!, frame.TermItems!));

        while(Current.Kind == SparqlTokenKind.Semicolon)
        {
            Advance();
        }

        if(Current.Kind == SparqlTokenKind.CloseBracket)
        {
            Advance();

            return StepResult.Done(new BlankNodePropertyListTerm(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Properties!));
        }

        if(CanStartVerb(Current.Kind))
        {
            frame.Stage = 1;

            return StepResult.Continue();
        }

        //A token that is neither a verb nor ']' finalises the list with the entries gathered so far,
        //skipping junk to ']'.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedBlankNodePropertyList, Current.Span, "Expected ';' or ']' in the blank-node property list.");
        ResyncTo(ParseFrameKind.BlankNodePropertyList, Current.Span, out _);

        if(Current.Kind == SparqlTokenKind.CloseBracket)
        {
            Advance();
        }

        return StepResult.Done(new BlankNodePropertyListTerm(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Properties!));
    }

    /// <summary>Advances an RDF 1.2 annotation block <c>{| verb objects ; ... |}</c>, mirroring the blank-node property list with annotation delimiters.</summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <param name="incoming">A popped path verb or compound object on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="AnnotationBlock"/>.</returns>
    private StepResult StepAnnotationBlock(ParseFrame frame, object? incoming)
        => frame.Stage switch
        {
            0 => AnnotationBlockStart(frame),
            1 => AnnotationBlockVerb(frame),
            2 => AnnotationBlockVerbPath(frame, incoming),
            3 => AnnotationBlockObject(frame),
            4 => AnnotationBlockObjectReceived(frame, incoming),
            _ => throw new SparqlParseException("Annotation block reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes the opening <c>{|</c> and begins the predicate-object list (which must be non-empty).</summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult AnnotationBlockStart(ParseFrame frame)
    {
        Advance();
        if(Current.Kind == SparqlTokenKind.CloseAnnotation)
        {
            return StepResult.Done(RecoverAnnotation(ParseFrameKind.AnnotationBlock, frame.StartSpan, WellKnownDiagnostics.Sparql.UnclosedAnnotationBlock, Current.Span, "An annotation block must contain at least one predicate-object pair.", "Annotation"));
        }

        frame.Properties = [];
        frame.Stage = 1;

        return StepResult.Continue();
    }

    /// <summary>Parses one verb (a variable inline or a property path) and begins its object list.</summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult AnnotationBlockVerb(ParseFrame frame)
    {
        frame.VerbSpanStart = Current.Span;

        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.Verb = ConsumeVariable();
            frame.TermItems = [];
            frame.Stage = 3;

            return StepResult.Continue();
        }

        if(CanStartPath(Current.Kind))
        {
            frame.Stage = 2;

            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PropertyPath, StartSpan = Current.Span });
        }

        return StepResult.Done(RecoverAnnotation(ParseFrameKind.AnnotationBlock, frame.StartSpan, WellKnownDiagnostics.Sparql.UnclosedAnnotationBlock, Current.Span, "Expected a predicate or property path inside the annotation block.", "Annotation"));
    }

    /// <summary>Converts the popped property path into the current verb and begins its object list.</summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <param name="incoming">The popped property path.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult AnnotationBlockVerbPath(ParseFrame frame, object? incoming)
    {
        PropertyPathExpression path = Pop<PropertyPathExpression>(incoming);
        SourceSpan span = CombineSpans(frame.VerbSpanStart, lastConsumedSpan);
        frame.Verb = path is PathPredicate predicate
            ? new ConstantTerm(span, new NamedNode(predicate.Predicate.Value))
            : new PropertyPathTerm(span, path);
        frame.TermItems = [];
        frame.Stage = 3;

        return StepResult.Continue();
    }

    /// <summary>Parses one object (pushing a frame for a compound term) of the current verb's object list.</summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult AnnotationBlockObject(ParseFrame frame)
    {
        if(CanStartCompoundTerm(Current.Kind))
        {
            frame.Stage = 4;

            return StepResult.Push(CompoundTermFrame());
        }

        //A token that cannot begin an object finalises the block; parsing a leaf would recover without
        //advancing and the block would re-enter forever.
        if(!CanStartTriple(Current.Kind))
        {
            return AnnotationBlockFinalisePartial(frame, WellKnownDiagnostics.Sparql.UnclosedAnnotationBlock, "Expected an object inside the annotation block.");
        }

        return AnnotationBlockAppendObject(frame, ParseVarOrTerm());
    }

    /// <summary>
    /// Reports a malformed annotation block and finalises it with the predicate-object entries gathered
    /// so far, skipping junk to its <c>|}</c> closer.
    /// </summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult AnnotationBlockFinalisePartial(ParseFrame frame, Utf8String code, string message)
    {
        _ = ReportRecoverable(code, Current.Span, message);
        ResyncTo(ParseFrameKind.AnnotationBlock, Current.Span, out _);

        if(Current.Kind == SparqlTokenKind.CloseAnnotation)
        {
            Advance();
        }

        return StepResult.Done(new AnnotationBlock(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Properties ?? []));
    }

    /// <summary>Appends a popped compound object and continues the object list.</summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <param name="incoming">The popped object term.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult AnnotationBlockObjectReceived(ParseFrame frame, object? incoming)
    {
        return AnnotationBlockAppendObject(frame, Pop<TriplePatternTerm>(incoming));
    }

    /// <summary>
    /// Adds one object; on a comma continues the object list, otherwise records the predicate-object
    /// entry and then continues the next verb on a semicolon or closes the block at <c>|}</c>.
    /// </summary>
    /// <param name="frame">The annotation-block frame.</param>
    /// <param name="objectTerm">The parsed object term.</param>
    /// <returns>The instruction for the driver; the result is the <see cref="AnnotationBlock"/> when the block closes.</returns>
    private StepResult AnnotationBlockAppendObject(ParseFrame frame, TriplePatternTerm objectTerm)
    {
        frame.TermItems!.Add(objectTerm);

        if(Current.Kind == SparqlTokenKind.Comma)
        {
            Advance();
            frame.Stage = 3;

            return StepResult.Continue();
        }

        SourceSpan entrySpan = CombineSpans(frame.VerbSpanStart, lastConsumedSpan);
        frame.Properties!.Add(new PropertyListPath(entrySpan, frame.Verb!, frame.TermItems!));

        while(Current.Kind == SparqlTokenKind.Semicolon)
        {
            Advance();
        }

        if(Current.Kind == SparqlTokenKind.CloseAnnotation)
        {
            Advance();

            return StepResult.Done(new AnnotationBlock(CombineSpans(frame.StartSpan, lastConsumedSpan), frame.Properties!));
        }

        if(CanStartVerb(Current.Kind))
        {
            frame.Stage = 1;

            return StepResult.Continue();
        }

        return AnnotationBlockFinalisePartial(frame, WellKnownDiagnostics.Sparql.UnclosedAnnotationBlock, "Expected ';' or '|}' in the annotation block.");
    }

    /// <summary>Returns the source span of a triple-pattern term, dispatching over the term kinds.</summary>
    /// <param name="term">The term whose span to read.</param>
    /// <returns>The term's source span.</returns>
    private static SourceSpan SpanOf(TriplePatternTerm term)
        => term switch
        {
            ConstantTerm constant => constant.Span,
            VariableTerm variable => variable.Span,
            PropertyPathTerm path => path.Span,
            Ast.TripleTerm tripleTerm => tripleTerm.Span,
            ReifiedTriple reified => reified.Span,
            CollectionTerm collection => collection.Span,
            BlankNodePropertyListTerm list => list.Span,
            AnnotatedObject annotated => annotated.Span,
            _ => SourceSpan.None
        };

    /// <summary>
    /// Advances a CONSTRUCT template <c>{ triples }</c>: a triples-only block (collection and
    /// blank-node-list sugar allowed) whose result is the flat list of template triple patterns.
    /// </summary>
    /// <param name="frame">The construct-template frame.</param>
    /// <param name="incoming">A popped triple run on resume.</param>
    /// <returns>The instruction for the driver; the popped result is the template triple list.</returns>
    private StepResult StepConstructTemplate(ParseFrame frame, object? incoming)
    {
        if(frame.Stage == 0)
        {
            //The caller ensured '{' is current.
            Advance();
            frame.TripleAccumulator = [];
            frame.Stage = 1;
        }
        else if(incoming is List<TriplePattern> triples)
        {
            frame.TripleAccumulator!.AddRange(triples);
            ConsumeOptionalDot();
        }

        if(Current.Kind == SparqlTokenKind.CloseBrace)
        {
            Advance();

            return StepResult.Done(frame.TripleAccumulator!);
        }

        //An unclosed template at end of input is finalised with the triples gathered so far plus a
        //diagnostic.
        if(Current.Kind == SparqlTokenKind.EndOfInput)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnclosedGroupGraphPattern, Current.Span, "Expected '}' to close the CONSTRUCT template.");

            return StepResult.Done(frame.TripleAccumulator!);
        }

        if(CanStartTriple(Current.Kind))
        {
            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Triple, StartSpan = Current.Span });
        }

        //A stray token that begins no triple is consumed by no frame; skip the stray run (one diagnostic)
        //up to a triple start, the template's closer, or end of input, so the template frame progresses.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTriplePattern, Current.Span, "Expected a triple pattern or '}'.");

        while(Current.Kind != SparqlTokenKind.EndOfInput
            && Current.Kind != SparqlTokenKind.CloseBrace
            && !CanStartTriple(Current.Kind))
        {
            int before = index;
            Advance();

            if(index == before)
            {
                break;
            }
        }

        return StepResult.Continue();
    }

    /// <summary>
    /// Advances a <c>VALUES</c> data block in either the one-variable form
    /// (<c>VALUES ?v { ... }</c>) or the full tuple form (<c>VALUES (?a ?b) { (..) (..) }</c>),
    /// reading one variable or one value per step.
    /// </summary>
    /// <param name="frame">The values frame.</param>
    /// <returns>The instruction for the driver; the popped result is the <see cref="ValuesClause"/>.</returns>
    private StepResult StepValues(ParseFrame frame)
        => frame.Stage switch
        {
            0 => ValuesStart(frame),
            1 => ValuesOneVarRow(frame),
            2 => ValuesFullRow(frame),
            3 => ValuesVariableList(frame),
            4 => ValuesRowValue(frame),
            _ => throw new SparqlParseException("VALUES reached an unknown stage.", frame.StartSpan)
        };

    /// <summary>Consumes <c>VALUES</c> and dispatches the one-variable or full tuple form.</summary>
    /// <param name="frame">The values frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ValuesStart(ParseFrame frame)
    {
        Advance();
        frame.ValuesRows = [];

        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.ValuesVariables = [new SparqlVariable(Current.Value)];
            Advance();
            if(!ConsumeValuesOpenBrace())
            {
                return StepResult.Done(new ValuesClause(frame.ValuesVariables, frame.ValuesRows!));
            }

            frame.Stage = 1;

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.OpenParen)
        {
            Advance();
            frame.ValuesVariables = [];
            frame.Stage = 3;

            return StepResult.Continue();
        }

        //A malformed VALUES head yields an empty data block plus a diagnostic; junk is skipped to a safe
        //point so the enclosing group/request frame resumes cleanly.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.MalformedValuesBlock, Current.Span, "Expected a variable or '(' after VALUES.");
        ResyncTo(ParseFrameKind.Values, Current.Span, out _);

        return StepResult.Done(new ValuesClause([], []));
    }

    /// <summary>Reads the full-form variable list, one variable per step, then opens the data brace.</summary>
    /// <param name="frame">The values frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ValuesVariableList(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.Variable)
        {
            frame.ValuesVariables!.Add(new SparqlVariable(Current.Value));
            Advance();

            return StepResult.Continue();
        }

        if(Current.Kind == SparqlTokenKind.CloseParen)
        {
            Advance();
            EnsureDistinctValuesVariables(frame);

            if(!ConsumeValuesOpenBrace())
            {
                return StepResult.Done(new ValuesClause(frame.ValuesVariables!, frame.ValuesRows!));
            }

            frame.Stage = 2;

            return StepResult.Continue();
        }

        //A malformed variable list is finalised with the variables gathered so far; junk is skipped.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.MalformedValuesBlock, Current.Span, "Expected a variable or ')' in the VALUES variable list.");
        ResyncTo(ParseFrameKind.Values, Current.Span, out _);

        return StepResult.Done(new ValuesClause(frame.ValuesVariables!, frame.ValuesRows!));
    }

    /// <summary>Reads one one-variable-form value (a single-column row), or closes the block.</summary>
    /// <param name="frame">The values frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ValuesOneVarRow(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.CloseBrace)
        {
            Advance();

            return StepResult.Done(new ValuesClause(frame.ValuesVariables!, frame.ValuesRows!));
        }

        //A token that begins no data value (and is not '}') finalises the block; parsing a value would
        //recover without advancing and the block would re-enter forever.
        if(!CanStartDataBlockValue(Current.Kind))
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedValuesValue, Current.Span, "Expected a VALUES data value or '}'.");
            ResyncTo(ParseFrameKind.Values, Current.Span, out _);

            if(Current.Kind == SparqlTokenKind.CloseBrace)
            {
                Advance();
            }

            return StepResult.Done(new ValuesClause(frame.ValuesVariables!, frame.ValuesRows!));
        }

        RdfTerm? value = ParseDataBlockValue();
        frame.ValuesRows!.Add([value]);

        return StepResult.Continue();
    }

    /// <summary>Begins a full-form row at <c>(</c>, or closes the block at <c>}</c>.</summary>
    /// <param name="frame">The values frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ValuesFullRow(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.CloseBrace)
        {
            Advance();

            return StepResult.Done(new ValuesClause(frame.ValuesVariables!, frame.ValuesRows!));
        }

        if(Current.Kind == SparqlTokenKind.OpenParen)
        {
            Advance();
            frame.CurrentRow = [];
            frame.Stage = 4;

            return StepResult.Continue();
        }

        //A token that begins no row (and is not '}') finalises the block; junk is skipped.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.MalformedValuesBlock, Current.Span, "Expected '(' to begin a VALUES row or '}' to close the block.");
        ResyncTo(ParseFrameKind.Values, Current.Span, out _);

        if(Current.Kind == SparqlTokenKind.CloseBrace)
        {
            Advance();
        }

        return StepResult.Done(new ValuesClause(frame.ValuesVariables!, frame.ValuesRows!));
    }

    /// <summary>Reads one value of the current full-form row, or closes the row at <c>)</c>.</summary>
    /// <param name="frame">The values frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult ValuesRowValue(ParseFrame frame)
    {
        if(Current.Kind == SparqlTokenKind.CloseParen)
        {
            Advance();

            //A row whose arity differs from the variable list is a positional violation; report and keep
            //the faithful row rather than dropping it.
            if(frame.CurrentRow!.Count != frame.ValuesVariables!.Count)
            {
                Report(WellKnownDiagnostics.Sparql.ValuesArityMismatch, frame.StartSpan, $"A VALUES row has {frame.CurrentRow.Count} value(s) but the block declares {frame.ValuesVariables.Count} variable(s).");
            }

            frame.ValuesRows!.Add(frame.CurrentRow);
            frame.Stage = 2;

            return StepResult.Continue();
        }

        //A token that begins no data value (and is not ')') closes the row; parsing a value would recover
        //without advancing and the row would re-enter forever.
        if(!CanStartDataBlockValue(Current.Kind))
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedValuesValue, Current.Span, "Expected a VALUES data value or ')'.");
            ResyncTo(ParseFrameKind.Values, Current.Span, out _);

            if(Current.Kind == SparqlTokenKind.CloseParen)
            {
                Advance();
            }

            frame.ValuesRows!.Add(frame.CurrentRow!);
            frame.Stage = 2;

            return StepResult.Continue();
        }

        frame.CurrentRow!.Add(ParseDataBlockValue());

        return StepResult.Continue();
    }

    /// <summary>
    /// Reports a VALUES variable list that repeats a variable. The duplicate is a positional violation;
    /// it is recorded and the faithful variable list is kept (the duplicate is not removed).
    /// </summary>
    /// <param name="frame">The values frame.</param>
    private void EnsureDistinctValuesVariables(ParseFrame frame)
    {
        HashSet<Utf8String> seen = [];
        foreach(SparqlVariable variable in frame.ValuesVariables!)
        {
            if(!seen.Add(variable.Name))
            {
                Report(WellKnownDiagnostics.Sparql.DuplicateVariableInValues, frame.StartSpan, $"The variable ?{variable.Name} appears more than once in the VALUES variable list.");
            }
        }
    }

    /// <summary>
    /// Consumes the opening brace of a VALUES data block, reporting and not consuming when it is absent.
    /// </summary>
    /// <returns><see langword="true"/> when the brace was present and consumed; <see langword="false"/> when it was missing.</returns>
    private bool ConsumeValuesOpenBrace()
    {
        if(Current.Kind != SparqlTokenKind.OpenBrace)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.MalformedValuesBlock, Current.Span, "Expected '{' to begin the VALUES data.");

            return false;
        }

        Advance();

        return true;
    }

    /// <summary>
    /// Parses one <c>DataBlockValue</c>: an IRI, a literal, or <c>UNDEF</c> (which yields a null,
    /// leaving the variable unbound in that row).
    /// </summary>
    /// <returns>The value term, or <see langword="null"/> for <c>UNDEF</c>.</returns>
    private RdfTerm? ParseDataBlockValue()
        => Current.Kind switch
        {
            SparqlTokenKind.UndefKeyword => ConsumeUndef(),
            SparqlTokenKind.Iri => new NamedNode(ConsumeIriRef().Value),
            SparqlTokenKind.PrefixedName => new NamedNode(ConsumePrefixedName().Value),
            SparqlTokenKind.StringLiteral
                or SparqlTokenKind.LongStringLiteral
                or SparqlTokenKind.IntegerLiteral
                or SparqlTokenKind.DecimalLiteral
                or SparqlTokenKind.DoubleLiteral
                or SparqlTokenKind.BooleanLiteral => ParseRdfLiteral(),
            _ => RecoverDataBlockValue()
        };

    /// <summary>
    /// Reports a missing <c>DataBlockValue</c> and returns a null value (an unbound cell); the call sites
    /// guard with <see cref="CanStartDataBlockValue"/>, so this is a defensive fallback that never advances.
    /// </summary>
    /// <returns><see langword="null"/>.</returns>
    private RdfTerm? RecoverDataBlockValue()
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedValuesValue, Current.Span, "Expected a VALUES data value (an IRI, a literal, or UNDEF).");

        return null;
    }

    /// <summary>Determines whether a token kind can begin a <c>DataBlockValue</c> (an IRI, a literal, or <c>UNDEF</c>).</summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a data value.</returns>
    private static bool CanStartDataBlockValue(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.UndefKeyword
            or SparqlTokenKind.Iri or SparqlTokenKind.PrefixedName
            or SparqlTokenKind.StringLiteral or SparqlTokenKind.LongStringLiteral
            or SparqlTokenKind.IntegerLiteral or SparqlTokenKind.DecimalLiteral or SparqlTokenKind.DoubleLiteral
            or SparqlTokenKind.BooleanLiteral;
    }

    /// <summary>Consumes the <c>UNDEF</c> keyword, returning a null value.</summary>
    /// <returns><see langword="null"/>.</returns>
    private RdfTerm? ConsumeUndef()
    {
        Advance();

        return null;
    }

    /// <summary>
    /// Consumes a single optional <c>.</c> that terminates or separates a triple block.
    /// </summary>
    private void ConsumeOptionalDot()
    {
        if(Current.Kind == SparqlTokenKind.Period)
        {
            Advance();
        }
    }

    /// <summary>
    /// Moves the frame's pending triple run, if any, into the members list as a single
    /// <see cref="BasicGraphPatternBlock"/> and resets the run.
    /// </summary>
    /// <param name="frame">The group-graph-pattern frame.</param>
    private static void FlushPendingTriples(ParseFrame frame)
    {
        if(frame.PendingTriples!.Count > 0)
        {
            frame.Members!.Add(new BasicGraphPatternBlock(frame.PendingTriples));
            frame.PendingTriples = [];
        }
    }

    /// <summary>
    /// Parses a <c>BASE</c> declaration, recording it and updating the running base IRI.
    /// </summary>
    /// <param name="frame">The request frame accumulating the prologue.</param>
    private void ParseBaseDeclaration(ParseFrame frame)
    {
        Advance();
        if(Current.Kind != SparqlTokenKind.Iri)
        {
            //A BASE without its IRI is reported and skipped; the prologue resumes at the next safe point.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTerm, Current.Span, "Expected an IRI after BASE.");
            ResyncTo(ParseFrameKind.Request, Current.Span, out _);

            return;
        }

        IriRef iri = ConsumeIriRef();
        baseIri = iri.Value;
        frame.Bases!.Add(new BaseDecl(iri));
    }

    /// <summary>
    /// Parses a <c>PREFIX</c> declaration, recording it and binding the prefix in the prefix map.
    /// </summary>
    /// <param name="frame">The request frame accumulating the prologue.</param>
    private void ParsePrefixDeclaration(ParseFrame frame)
    {
        Advance();
        if(Current.Kind != SparqlTokenKind.PrefixNamespace)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTerm, Current.Span, "Expected a namespace prefix after PREFIX.");
            ResyncTo(ParseFrameKind.Request, Current.Span, out _);

            return;
        }

        Utf8String prefixLabel = Current.Value;
        Advance();
        if(Current.Kind != SparqlTokenKind.Iri)
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTerm, Current.Span, "Expected an IRI in the PREFIX declaration.");
            ResyncTo(ParseFrameKind.Request, Current.Span, out _);

            return;
        }

        IriRef ns = ConsumeIriRef();
        prefixMap[prefixLabel] = ns.Value;
        frame.Prefixes!.Add(new PrefixDecl(prefixLabel, ns));
    }

    /// <summary>
    /// Parses one <c>FROM</c> or <c>FROM NAMED</c> dataset clause into the request frame.
    /// </summary>
    /// <param name="frame">The request frame accumulating the dataset.</param>
    private void ParseFromClause(ParseFrame frame)
    {
        Advance();
        if(Current.Kind == SparqlTokenKind.NamedKeyword)
        {
            Advance();
            frame.NamedGraphs!.Add(ConsumeIriOrPrefixedName("IRI after FROM NAMED"));

            return;
        }

        frame.DefaultGraphs!.Add(ConsumeIriOrPrefixedName("IRI after FROM"));
    }

    /// <summary>
    /// Parses a <c>VarOrTerm</c> in subject or object position: a variable, an IRI, a prefixed name,
    /// a blank node, or an RDF literal.
    /// </summary>
    /// <returns>The parsed triple-pattern term.</returns>
    private TriplePatternTerm ParseVarOrTerm()
        => Current.Kind switch
        {
            SparqlTokenKind.Variable => ConsumeVariable(),
            SparqlTokenKind.Iri => ConsumeIriConstantTerm(),
            SparqlTokenKind.PrefixedName => ConsumePrefixedNameConstantTerm(),
            SparqlTokenKind.BlankNodeLabel => ConsumeBlankNodeTerm(),
            SparqlTokenKind.AnonymousBlankNode => ConsumeAnonymousBlankNodeTerm(),
            SparqlTokenKind.StringLiteral
                or SparqlTokenKind.LongStringLiteral
                or SparqlTokenKind.IntegerLiteral
                or SparqlTokenKind.DecimalLiteral
                or SparqlTokenKind.DoubleLiteral
                or SparqlTokenKind.BooleanLiteral => ConsumeLiteralConstantTerm(),
            _ => RecoverTriplePatternTerm(ParseFrameKind.Triple, Current.Span, WellKnownDiagnostics.Sparql.ExpectedTerm, Current.Span, "Expected a subject or object term.", "VarOrTerm")
        };

    /// <summary>Consumes the IRI at the cursor as a constant term carrying the token span.</summary>
    /// <returns>The IRI constant term.</returns>
    private ConstantTerm ConsumeIriConstantTerm()
    {
        IriRef iri = ConsumeIriRef();

        return new ConstantTerm(iri.Span, new NamedNode(iri.Value));
    }

    /// <summary>Consumes the prefixed name at the cursor as a constant term carrying the token span.</summary>
    /// <returns>The IRI constant term.</returns>
    private ConstantTerm ConsumePrefixedNameConstantTerm()
    {
        IriRef iri = ConsumePrefixedName();

        return new ConstantTerm(iri.Span, new NamedNode(iri.Value));
    }

    /// <summary>Consumes the literal at the cursor (with any suffix) as a constant term spanning its full extent.</summary>
    /// <returns>The literal constant term.</returns>
    private ConstantTerm ConsumeLiteralConstantTerm()
    {
        SourceSpan start = Current.Span;
        Literal literal = ParseRdfLiteral();

        return new ConstantTerm(CombineSpans(start, lastConsumedSpan), literal);
    }

    /// <summary>
    /// Consumes the blank-node label at the cursor as a constant term and advances.
    /// </summary>
    /// <returns>The blank-node constant term.</returns>
    private ConstantTerm ConsumeBlankNodeTerm()
    {
        SourceSpan span = Current.Span;
        Utf8String label = Current.Value;
        Advance();

        return new ConstantTerm(span, new BlankNode(label));
    }

    /// <summary>
    /// Consumes the anonymous <c>[]</c> blank node at the cursor as a constant term with a fresh label and advances.
    /// </summary>
    /// <returns>The blank-node constant term.</returns>
    private ConstantTerm ConsumeAnonymousBlankNodeTerm()
    {
        SourceSpan span = Current.Span;
        Advance();

        return new ConstantTerm(span, new BlankNode(FreshBlankNodeLabel(span)));
    }

    /// <summary>
    /// Parses a <c>VarOrIri</c> graph designator for a <c>GRAPH</c> or <c>SERVICE</c> member.
    /// </summary>
    /// <returns>The graph designator.</returns>
    private GraphTerm ParseGraphTerm()
        => Current.Kind switch
        {
            SparqlTokenKind.Variable => ConsumeGraphVariable(),
            SparqlTokenKind.Iri => new GraphIriTerm(ConsumeIriRef()),
            SparqlTokenKind.PrefixedName => new GraphIriTerm(ConsumePrefixedName()),
            _ => RecoverGraphDesignator()
        };

    /// <summary>
    /// Reports a missing graph designator and returns an <see cref="ErrorGraphTerm"/> over the current
    /// token without resyncing, so the caller (<c>GRAPH</c> / <c>SERVICE</c>) can still test for the
    /// following <c>{</c>. Resyncing here would skip the entire group pattern.
    /// </summary>
    /// <returns>The error graph-term node.</returns>
    private ErrorGraphTerm RecoverGraphDesignator()
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedGraphTerm, Current.Span, "Expected a graph IRI or variable.");

        return new ErrorGraphTerm(Current.Span, Utf8String.FromString("VarOrIri"), codes, []);
    }

    /// <summary>
    /// Consumes the variable at the cursor as a graph designator and advances.
    /// </summary>
    /// <returns>The graph variable designator.</returns>
    private GraphVariableTerm ConsumeGraphVariable()
    {
        GraphVariableTerm term = new(new SparqlVariable(Current.Value));
        Advance();

        return term;
    }

    /// <summary>
    /// Consumes the variable at the cursor and advances.
    /// </summary>
    /// <returns>The variable term.</returns>
    private VariableTerm ConsumeVariable()
    {
        VariableTerm term = new(Current.Span, new SparqlVariable(Current.Value));
        Advance();

        return term;
    }

    /// <summary>
    /// Consumes the IRI at the cursor, resolving it against the current base, and advances.
    /// </summary>
    /// <returns>The resolved IRI reference.</returns>
    private IriRef ConsumeIriRef()
    {
        SourceSpan span = Current.Span;
        Utf8String resolved = ResolveIri(Current.Value);
        Advance();

        return new IriRef(resolved, span);
    }

    /// <summary>
    /// Consumes the prefixed name at the cursor, expanding it against the prefix map, and advances.
    /// </summary>
    /// <returns>The expanded IRI reference.</returns>
    private IriRef ConsumePrefixedName()
    {
        SourceSpan span = Current.Span;
        Utf8String resolved = ResolvePrefixedName(Current.Value, span);
        Advance();

        return new IriRef(resolved, span);
    }

    /// <summary>
    /// Consumes either an IRI or a prefixed name at the cursor, used where the grammar's <c>iri</c>
    /// production appears.
    /// </summary>
    /// <param name="what">A description of the expected element for the error message.</param>
    /// <returns>The resolved IRI reference.</returns>
    private IriRef ConsumeIriOrPrefixedName(string what)
    {
        return Current.Kind switch
        {
            SparqlTokenKind.Iri => ConsumeIriRef(),
            SparqlTokenKind.PrefixedName => ConsumePrefixedName(),
            _ => RecoverMissingIri(what)
        };
    }

    /// <summary>
    /// Reports a missing IRI (in a DESCRIBE target or FROM clause) and returns an empty IRI reference
    /// without advancing; the enclosing list loop then resumes on the offending token.
    /// </summary>
    /// <param name="what">A description of the expected element for the diagnostic.</param>
    /// <returns>An empty IRI reference at the current span.</returns>
    private IriRef RecoverMissingIri(string what)
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTerm, Current.Span, $"Expected {what}.");

        return new IriRef(Pool.Intern(""u8), Current.Span);
    }

    /// <summary>
    /// Parses an RDF literal at the cursor: a string with an optional language tag, directional
    /// language tag, or datatype; or a numeric or boolean literal.
    /// </summary>
    /// <returns>The literal term.</returns>
    private Literal ParseRdfLiteral()
        => Current.Kind switch
        {
            SparqlTokenKind.StringLiteral or SparqlTokenKind.LongStringLiteral => ParseStringLiteral(),
            SparqlTokenKind.IntegerLiteral => ConsumeTypedNumericOrBoolean(xsdInteger),
            SparqlTokenKind.DecimalLiteral => ConsumeTypedNumericOrBoolean(xsdDecimal),
            SparqlTokenKind.DoubleLiteral => ConsumeTypedNumericOrBoolean(xsdDouble),
            SparqlTokenKind.BooleanLiteral => ConsumeTypedNumericOrBoolean(xsdBoolean),
            _ => RecoverMissingLiteral()
        };

    /// <summary>
    /// Reports a missing literal and returns an empty <c>xsd:string</c> literal without advancing; the
    /// call sites guard on a literal token, so this is a defensive fallback.
    /// </summary>
    /// <returns>The fallback literal.</returns>
    private Literal RecoverMissingLiteral()
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedTerm, Current.Span, "Expected a literal.");

        return new Literal(Pool.Intern(""u8), new NamedNode(xsdString), Language: null, BaseDirection: null);
    }

    /// <summary>
    /// Consumes a string literal at the cursor and its optional language tag, directional language tag,
    /// or datatype, and advances.
    /// </summary>
    /// <returns>The string literal term.</returns>
    private Literal ParseStringLiteral()
    {
        Utf8String lexical = Current.Value;
        Advance();

        return Current.Kind switch
        {
            SparqlTokenKind.LangTag => ConsumeLanguageTaggedLiteral(lexical),
            SparqlTokenKind.DirLangTag => ConsumeDirectionalLiteral(lexical),
            SparqlTokenKind.TypeMarker => ConsumeTypedLiteral(lexical),
            _ => new Literal(lexical, new NamedNode(xsdString), Language: null, BaseDirection: null)
        };
    }

    /// <summary>
    /// Consumes the language tag at the cursor and builds the language-tagged literal.
    /// </summary>
    /// <param name="lexical">The already-consumed string lexical form.</param>
    /// <returns>The language-tagged literal.</returns>
    private Literal ConsumeLanguageTaggedLiteral(Utf8String lexical)
    {
        Utf8String language = Current.Value;
        Advance();

        return new Literal(lexical, new NamedNode(rdfLangString), language, BaseDirection: null);
    }

    /// <summary>
    /// Consumes the directional language tag at the cursor and builds the directional literal.
    /// </summary>
    /// <param name="lexical">The already-consumed string lexical form.</param>
    /// <returns>The directional language-tagged literal.</returns>
    private Literal ConsumeDirectionalLiteral(Utf8String lexical)
    {
        (Utf8String language, TextDirection direction) = SplitDirectionalLanguageTag(Current.Value, Current.Span);
        Advance();

        return new Literal(lexical, new NamedNode(rdfDirLangString), language, direction);
    }

    /// <summary>
    /// Consumes the <c>^^</c> datatype marker and datatype IRI at the cursor and builds the typed literal.
    /// </summary>
    /// <param name="lexical">The already-consumed string lexical form.</param>
    /// <returns>The typed literal.</returns>
    private Literal ConsumeTypedLiteral(Utf8String lexical)
    {
        Advance();
        NamedNode datatype = ConsumeDatatypeIri();

        return new Literal(lexical, datatype, Language: null, BaseDirection: null);
    }

    /// <summary>
    /// Consumes the numeric or boolean literal at the cursor, tagging it with the given datatype IRI,
    /// and advances.
    /// </summary>
    /// <param name="datatype">The interned datatype IRI for the literal's lexical form.</param>
    /// <returns>The literal term.</returns>
    private Literal ConsumeTypedNumericOrBoolean(Utf8String datatype)
    {
        Utf8String lexical = Current.Value;
        Advance();

        return new Literal(lexical, new NamedNode(datatype), Language: null, BaseDirection: null);
    }

    /// <summary>
    /// Consumes the datatype IRI following a <c>^^</c> marker: an IRI or a prefixed name.
    /// </summary>
    /// <returns>The datatype as a named node.</returns>
    private NamedNode ConsumeDatatypeIri()
    {
        return Current.Kind switch
        {
            SparqlTokenKind.Iri => new NamedNode(ConsumeIriRef().Value),
            SparqlTokenKind.PrefixedName => new NamedNode(ConsumePrefixedName().Value),
            _ => RecoverMissingDatatype()
        };
    }

    /// <summary>
    /// Reports a missing datatype IRI after <c>^^</c> and returns an <c>xsd:string</c> fallback without
    /// advancing, keeping the literal faithful.
    /// </summary>
    /// <returns>The fallback datatype named node.</returns>
    private NamedNode RecoverMissingDatatype()
    {
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedDatatypeIri, Current.Span, "Expected a datatype IRI after '^^'.");

        return new NamedNode(xsdString);
    }

    /// <summary>
    /// Consumes the non-negative integer literal at the cursor and advances.
    /// </summary>
    /// <param name="what">A description of the expected value for the error message.</param>
    /// <returns>The parsed integer.</returns>
    private int ConsumeInteger(string what)
    {
        if(Current.Kind != SparqlTokenKind.IntegerLiteral)
        {
            //A missing slice value is reported and treated as zero without advancing; the request frame's
            //modifier loop then resumes on the offending token.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedInteger, Current.Span, $"Expected {what}.");

            return 0;
        }

        if(!int.TryParse(Current.Value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            //A malformed integer is reported and treated as zero; the malformed token is consumed.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.ExpectedInteger, Current.Span, $"The {what} '{Current.Value}' is not a valid non-negative 32-bit integer.");
            Advance();

            return 0;
        }

        Advance();

        return value;
    }

    /// <summary>
    /// Resolves an IRI reference against the current base, returning the absolute form. With no base,
    /// the reference is interned unchanged.
    /// </summary>
    /// <param name="iriText">The IRI text, without its angle brackets.</param>
    /// <returns>The resolved, interned IRI.</returns>
    private Utf8String ResolveIri(Utf8String iriText)
    {
        if(baseIri is { } resolvedBase)
        {
            string resolved = IriResolver.ResolveIri(resolvedBase.ToString(), iriText.ToString());

            return Pool.Intern(resolved);
        }

        return iriText;
    }

    /// <summary>
    /// Expands a prefixed name to its absolute IRI by concatenating the bound namespace with the local
    /// part.
    /// </summary>
    /// <param name="prefixedName">The prefixed name in <c>prefix:local</c> form.</param>
    /// <param name="span">The source span, for the error message.</param>
    /// <returns>The expanded, interned IRI.</returns>
    private Utf8String ResolvePrefixedName(Utf8String prefixedName, SourceSpan span)
    {
        ReadOnlySpan<byte> bytes = prefixedName.Span;
        int colonIndex = bytes.IndexOf((byte)':');
        if(colonIndex < 0)
        {
            throw new SparqlParseException("A prefixed name is missing its ':' separator.", span);
        }

        Utf8String prefixLabel = Pool.Intern(bytes[..(colonIndex + 1)]);
        if(!prefixMap.TryGetValue(prefixLabel, out Utf8String namespaceIri))
        {
            //An unbound prefix is input-reachable (a prefixed name with no PREFIX declaration). Report it
            //and keep the unexpanded prefixed-name text as the IRI value, so the AST stays faithful and
            //the offending token is still consumed by the caller.
            _ = ReportRecoverable(WellKnownDiagnostics.Sparql.UnboundPrefix, span, $"The prefix '{prefixLabel}' is not bound by any PREFIX declaration.");

            return prefixedName;
        }

        return InternConcatenated(namespaceIri.Span, bytes[(colonIndex + 1)..]);
    }

    /// <summary>
    /// Interns the concatenation of two byte spans.
    /// </summary>
    /// <param name="left">The left bytes.</param>
    /// <param name="right">The right bytes.</param>
    /// <returns>The interned concatenation.</returns>
    private Utf8String InternConcatenated(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        byte[] buffer = new byte[left.Length + right.Length];
        left.CopyTo(buffer);
        right.CopyTo(buffer.AsSpan(left.Length));

        return Pool.Intern(buffer.AsSpan());
    }

    /// <summary>
    /// Splits a directional language tag (for example <c>en--ltr</c>) into its language subtags and
    /// base direction.
    /// </summary>
    /// <param name="tag">The tag value, without the leading <c>@</c>.</param>
    /// <param name="span">The source span, for the error message.</param>
    /// <returns>The language part and the parsed base direction.</returns>
    private (Utf8String Language, TextDirection Direction) SplitDirectionalLanguageTag(Utf8String tag, SourceSpan span)
    {
        ReadOnlySpan<byte> bytes = tag.Span;
        int dashIndex = IndexOfDoubleDash(bytes);
        if(dashIndex < 0)
        {
            throw new SparqlParseException("A directional language tag is missing its '--' separator.", span);
        }

        Utf8String language = Pool.Intern(bytes[..dashIndex]);
        ReadOnlySpan<byte> directionBytes = bytes[(dashIndex + 2)..];

        if(directionBytes.SequenceEqual("ltr"u8))
        {
            return (language, TextDirection.Ltr);
        }

        if(directionBytes.SequenceEqual("rtl"u8))
        {
            return (language, TextDirection.Rtl);
        }

        //The lexer accepts any letter sequence after '--', so a direction other than 'ltr'/'rtl' is
        //input-reachable: report it and keep the language tag with a default left-to-right direction.
        _ = ReportRecoverable(WellKnownDiagnostics.Sparql.InvalidBaseDirection, span, $"The base direction '{System.Text.Encoding.UTF8.GetString(directionBytes)}' is not 'ltr' or 'rtl'.");

        return (language, TextDirection.Ltr);
    }

    /// <summary>
    /// Allocates a fresh synthetic blank-node label for an anonymous <c>[]</c> node.
    /// </summary>
    /// <param name="span">The source span of the anonymous blank node occurrence.</param>
    /// <returns>The interned label, without the <c>_:</c> prefix.</returns>
    private Utf8String FreshBlankNodeLabel(SourceSpan span)
    {
        BlankNodeRequest request = new(Guid.Empty, ReadOnlyMemory<byte>.Empty, span, Pool);

        return blankNodes(in request);
    }

    /// <summary>
    /// Determines whether a token kind can begin a <c>VarOrTerm</c> subject.
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a subject term.</returns>
    private static bool CanStartTriple(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.Variable
            or SparqlTokenKind.Iri
            or SparqlTokenKind.PrefixedName
            or SparqlTokenKind.BlankNodeLabel
            or SparqlTokenKind.AnonymousBlankNode
            or SparqlTokenKind.StringLiteral
            or SparqlTokenKind.LongStringLiteral
            or SparqlTokenKind.IntegerLiteral
            or SparqlTokenKind.DecimalLiteral
            or SparqlTokenKind.DoubleLiteral
            or SparqlTokenKind.BooleanLiteral
            or SparqlTokenKind.OpenParen
            or SparqlTokenKind.OpenBracket
            or SparqlTokenKind.OpenTripleTerm
            or SparqlTokenKind.OpenReifiedTriple;
    }

    /// <summary>
    /// Determines whether a token kind can begin a verb (predicate).
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a predicate.</returns>
    private static bool CanStartVerb(SparqlTokenKind kind)
    {
        return kind is SparqlTokenKind.A
            or SparqlTokenKind.Iri
            or SparqlTokenKind.PrefixedName
            or SparqlTokenKind.Variable
            or SparqlTokenKind.Caret
            or SparqlTokenKind.Bang
            or SparqlTokenKind.OpenParen;
    }

    /// <summary>
    /// Finds the index of the first <c>--</c> sequence in a byte span.
    /// </summary>
    /// <param name="bytes">The bytes to scan.</param>
    /// <returns>The index of the first dash of the pair, or -1 when absent.</returns>
    private static int IndexOfDoubleDash(ReadOnlySpan<byte> bytes)
    {
        for(int i = 0; i + 1 < bytes.Length; i++)
        {
            if(bytes[i] == (byte)'-' && bytes[i + 1] == (byte)'-')
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Records a recoverable parse diagnostic into the shared bag.
    /// </summary>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    /// <param name="message">A human-readable explanation.</param>
    private void Report(Utf8String code, SourceSpan span, string message)
    {
        //Beyond the per-parse cap the parser stops recording — a runaway-error backstop. The AST still
        //assembles (error nodes are produced regardless); only the diagnostic list is bounded.
        if(parserDiagnosticsRecorded >= MaxDiagnostics)
        {
            return;
        }

        Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, Utf8String.FromString(message)));
        parserDiagnosticsRecorded++;

        //On reaching the cap, record the marker once; subsequent reports are suppressed by the guard above.
        if(parserDiagnosticsRecorded == MaxDiagnostics)
        {
            Diagnostics.Add(new Diagnostic(
                WellKnownDiagnostics.Sparql.ExcessDiagnostics,
                DiagnosticSeverity.Error,
                span,
                Utf8String.FromString("The per-parse diagnostic cap was reached; further diagnostics are suppressed.")));
        }
    }

    /// <summary>
    /// Records the diagnostic for a recoverable error and returns the codes to stamp on the error node —
    /// unless the offending token is a lexer <see cref="SparqlTokenKind.Error"/> token, whose <c>LX####</c>
    /// diagnostic the facade already bridged into the bag.
    /// </summary>
    /// <remarks>
    /// Re-reporting an <see cref="SparqlTokenKind.Error"/> token would double-count, so the lexer's code
    /// stands alone and the parser stays silent; the error node still spans the offending run, correlating
    /// by span.
    /// </remarks>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="span">The source extent the diagnostic covers.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <returns>The codes to record on the error node: <c>[code]</c>, or empty when the offending token is a lexer error.</returns>
    private ImmutableArray<Utf8String> ReportRecoverable(Utf8String code, SourceSpan span, string message)
    {
        if(Current.Kind == SparqlTokenKind.Error)
        {
            return [];
        }

        Report(code, span, message);

        return [code];
    }

    /// <summary>
    /// Skips tokens from the cursor — collecting them as the error node's trivia — until a token in the
    /// frame's resync set (or end-of-input) is reached, which is left as the new cursor.
    /// </summary>
    /// <remarks>
    /// The <c>before == index</c> guard breaks when <see cref="Advance"/> cannot move (the cursor is
    /// clamped at the final token), so recovery from any position terminates.
    /// </remarks>
    /// <param name="frameKind">The frame whose resync set determines where skipping stops.</param>
    /// <param name="startSpan">The span the running end span is seeded from.</param>
    /// <param name="lastSpan">Receives the span of the last skipped token, or <paramref name="startSpan"/> when none was skipped.</param>
    /// <returns>The tokens skipped to resynchronise.</returns>
    private ImmutableArray<SparqlToken> ResyncTo(ParseFrameKind frameKind, SourceSpan startSpan, out SourceSpan lastSpan)
    {
        ImmutableArray<SparqlToken>.Builder skipped = ImmutableArray.CreateBuilder<SparqlToken>();
        lastSpan = startSpan;

        while(Current.Kind != SparqlTokenKind.EndOfInput && !IsResyncToken(frameKind, Current.Kind))
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
    /// The resync set for each <see cref="ParseFrameKind"/>: the structural tokens at or after which the
    /// parser can resume after skipping a malformed run.
    /// </summary>
    /// <remarks>
    /// A bracketed form resyncs to its own closer (plus the statement terminators for the triple forms,
    /// which may be the next safe point if the closer is missing); the request and clause frames resync
    /// to the nearest statement boundary. End-of-input is always a stop, handled by
    /// <see cref="ResyncTo"/>'s loop condition.
    /// </remarks>
    /// <param name="frameKind">The frame whose resync set to test against.</param>
    /// <param name="kind">The token kind at the cursor.</param>
    /// <returns><see langword="true"/> when the cursor token is a safe point to resume at.</returns>
    private static bool IsResyncToken(ParseFrameKind frameKind, SparqlTokenKind kind)
        => frameKind switch
        {
            ParseFrameKind.Request or ParseFrameKind.SelectClause
                => kind is SparqlTokenKind.Period or SparqlTokenKind.CloseBrace,
            ParseFrameKind.GroupGraphPattern or ParseFrameKind.UnionPattern or ParseFrameKind.OptionalPattern
                or ParseFrameKind.MinusPattern or ParseFrameKind.GraphPattern or ParseFrameKind.ServicePattern
                or ParseFrameKind.SubSelect or ParseFrameKind.ConstructTemplate
                => kind is SparqlTokenKind.CloseBrace,
            ParseFrameKind.Triple
                => kind is SparqlTokenKind.Period or SparqlTokenKind.Semicolon or SparqlTokenKind.CloseBrace,
            ParseFrameKind.Collection
                => kind is SparqlTokenKind.CloseParen,
            ParseFrameKind.BlankNodePropertyList
                => kind is SparqlTokenKind.CloseBracket,
            ParseFrameKind.TripleTerm
                => kind is SparqlTokenKind.CloseTripleTerm or SparqlTokenKind.Period or SparqlTokenKind.CloseBrace,
            ParseFrameKind.ReifiedTriple
                => kind is SparqlTokenKind.CloseReifiedTriple or SparqlTokenKind.Period or SparqlTokenKind.CloseBrace,
            ParseFrameKind.AnnotationBlock
                => kind is SparqlTokenKind.CloseAnnotation,
            ParseFrameKind.Expression or ParseFrameKind.ArgumentList
                => kind is SparqlTokenKind.CloseParen or SparqlTokenKind.Comma,
            ParseFrameKind.PropertyPath or ParseFrameKind.PathSequence or ParseFrameKind.PathElement
                or ParseFrameKind.PathNegatedSet
                => kind is SparqlTokenKind.CloseParen or SparqlTokenKind.CloseBrace or SparqlTokenKind.Period or SparqlTokenKind.Semicolon,
            ParseFrameKind.Values
                => kind is SparqlTokenKind.CloseBrace or SparqlTokenKind.CloseParen,
            ParseFrameKind.GroupBy or ParseFrameKind.Having or ParseFrameKind.OrderBy
                or ParseFrameKind.Filter or ParseFrameKind.Bind
                => kind is SparqlTokenKind.CloseBrace or SparqlTokenKind.Period,
            _ => kind is SparqlTokenKind.Period or SparqlTokenKind.Semicolon or SparqlTokenKind.Comma
                or SparqlTokenKind.CloseBracket or SparqlTokenKind.CloseParen or SparqlTokenKind.CloseBrace
                or SparqlTokenKind.CloseTripleTerm or SparqlTokenKind.CloseReifiedTriple or SparqlTokenKind.CloseAnnotation
        };

    /// <summary>
    /// Records the diagnostic, resyncs to the frame's resync set, and builds an
    /// <see cref="ErrorGraphPattern"/> spanning the failure-to-resync run. The node slots into any parent
    /// that expected a <see cref="GraphPattern"/>, so the existing value flow carries it up.
    /// </summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error graph-pattern node.</returns>
    private ErrorGraphPattern RecoverGraphPattern(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorGraphPattern(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>As <see cref="RecoverGraphPattern"/>, for a broken query-form head, yielding an <see cref="ErrorQueryForm"/>.</summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error query-form node.</returns>
    private ErrorQueryForm RecoverQueryForm(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorQueryForm(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>As <see cref="RecoverGraphPattern"/>, for a broken expression, yielding an <see cref="ErrorExpression"/>.</summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error expression node.</returns>
    private ErrorExpression RecoverExpression(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorExpression(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>As <see cref="RecoverGraphPattern"/>, for a broken property path, yielding an <see cref="ErrorPropertyPath"/>.</summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error property-path node.</returns>
    private ErrorPropertyPath RecoverPropertyPath(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorPropertyPath(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>As <see cref="RecoverGraphPattern"/>, for a broken triple-pattern term, yielding an <see cref="ErrorTriplePatternTerm"/>.</summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error triple-pattern-term node.</returns>
    private ErrorTriplePatternTerm RecoverTriplePatternTerm(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorTriplePatternTerm(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>As <see cref="RecoverGraphPattern"/>, for a broken graph designator, yielding an <see cref="ErrorGraphTerm"/>.</summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error graph-term node.</returns>
    private ErrorGraphTerm RecoverGraphTerm(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorGraphTerm(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>As <see cref="RecoverGraphPattern"/>, for a broken annotation, yielding an <see cref="ErrorAnnotation"/>.</summary>
    /// <param name="frameKind">The frame whose resync set bounds the skip.</param>
    /// <param name="startSpan">The span the node's extent begins at.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="errorSpan">The span the diagnostic is anchored at.</param>
    /// <param name="message">A human-readable explanation.</param>
    /// <param name="expectedProduction">The grammar production the parser expected.</param>
    /// <returns>The error annotation node.</returns>
    private ErrorAnnotation RecoverAnnotation(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<SparqlToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return new ErrorAnnotation(CombineSpans(startSpan, endSpan), Utf8String.FromString(expectedProduction), codes, skipped);
    }

    /// <summary>
    /// Determines whether a token kind can begin a group-graph-pattern member: a triple subject, a
    /// nested-group / member keyword, or a <c>VALUES</c> block.
    /// </summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a member.</returns>
    private static bool CanStartGroupMember(SparqlTokenKind kind)
    {
        return CanStartTriple(kind)
            || kind is SparqlTokenKind.OpenBrace
            or SparqlTokenKind.OptionalKeyword or SparqlTokenKind.MinusKeyword
            or SparqlTokenKind.GraphKeyword or SparqlTokenKind.ServiceKeyword
            or SparqlTokenKind.FilterKeyword or SparqlTokenKind.BindKeyword
            or SparqlTokenKind.ValuesKeyword;
    }

    /// <summary>
    /// The instruction returned by a step method: pop the current frame with a completed result, push
    /// a new frame to recurse into, or continue the same frame on the next iteration.
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
        /// <summary>The request was produced; the stack is empty.</summary>
        Produced,

        /// <summary>The next step needs tokens that have not been fed yet.</summary>
        NeedMore
    }
}
