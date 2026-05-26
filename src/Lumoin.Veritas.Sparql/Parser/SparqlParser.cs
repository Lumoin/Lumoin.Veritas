using System;
using System.Collections.Generic;
using System.Globalization;
using Lumoin.Veritas.Core;
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
    private int nextBlankNode;

    /// <summary>
    /// Initialises a <see cref="SparqlParser"/> over a fully materialised token stream.
    /// </summary>
    /// <param name="tokens">The lexed token stream, ending with <see cref="SparqlTokenKind.EndOfInput"/>.</param>
    /// <param name="pool">The pool used to intern parser-allocated identifiers (resolved IRIs, blank-node labels).</param>
    /// <param name="baseIri">The external base IRI relative references resolve against before any in-query <c>BASE</c>, or <see langword="null"/>.</param>
    public SparqlParser(IEnumerable<SparqlToken> tokens, Utf8StringPool pool, Utf8String? baseIri = null)
        : this(pool, baseIri)
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
    /// <remarks>
    /// The parser suspends — preserving its work stack — when the request needs tokens that have not
    /// arrived yet, and resumes when more are fed, so the token buffer need not hold the whole query.
    /// </remarks>
    internal SparqlParser(Utf8StringPool pool, Utf8String? baseIri = null)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Tokens = [];
        Pool = pool;
        this.baseIri = baseIri;

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
        if(index < Tokens.Count - 1)
        {
            index++;
        }
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
            ParseFrameKind.SelectClause => StepSelectClause(frame),
            ParseFrameKind.GroupGraphPattern => StepGroupGraphPattern(frame, incoming),
            ParseFrameKind.Triple => StepTriple(frame),
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
    {
        switch(frame.Stage)
        {
            case 0:
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

            case 1:
            {
                switch(Current.Kind)
                {
                    case SparqlTokenKind.SelectKeyword:
                    {
                        frame.Stage = 2;

                        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.SelectClause, StartSpan = Current.Span });
                    }

                    case SparqlTokenKind.AskKeyword:
                    {
                        Advance();
                        frame.Form = new AskQuery();
                        frame.Stage = 3;

                        return StepResult.Continue();
                    }

                    case SparqlTokenKind.ConstructKeyword:
                    {
                        throw NotImplemented("CONSTRUCT");
                    }

                    case SparqlTokenKind.DescribeKeyword:
                    {
                        throw NotImplemented("DESCRIBE");
                    }

                    default:
                    {
                        throw Expected("a query form (SELECT, CONSTRUCT, ASK, or DESCRIBE)", Current);
                    }
                }
            }

            case 2:
            {
                frame.Form = (QueryForm)incoming!;
                frame.Stage = 3;

                return StepResult.Continue();
            }

            case 3:
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

            case 4:
            {
                if(Current.Kind == SparqlTokenKind.WhereKeyword)
                {
                    Advance();
                }

                if(Current.Kind != SparqlTokenKind.OpenBrace)
                {
                    throw Expected("'{' to begin the WHERE group graph pattern", Current);
                }

                frame.Stage = 5;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GroupGraphPattern, StartSpan = Current.Span });
            }

            case 5:
            {
                frame.Where = new WhereClause((GraphPattern)incoming!);
                frame.Stage = 6;

                return StepResult.Continue();
            }

            case 6:
            {
                if(Current.Kind is SparqlTokenKind.GroupKeyword or SparqlTokenKind.HavingKeyword or SparqlTokenKind.OrderKeyword)
                {
                    throw new SparqlParseException(
                        $"The {Current.Kind} solution modifier is not yet implemented in this build.",
                        Current.Span);
                }

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

                frame.Stage = 7;

                return StepResult.Continue();
            }

            case 7:
            {
                if(Current.Kind != SparqlTokenKind.EndOfInput)
                {
                    throw Expected("end of query", Current);
                }

                SparqlQuery query = new(
                    new Prologue(frame.Bases!, frame.Prefixes!),
                    frame.Form!,
                    frame.Dataset!,
                    frame.Where!,
                    new SolutionModifier(Group: null, Having: null, Order: null, frame.Offset, frame.Limit),
                    Values: null);

                return StepResult.Done(query);
            }

            default:
            {
                throw new SparqlParseException("Request reached an unknown stage.", frame.StartSpan);
            }
        }
    }

    /// <summary>
    /// Advances a <c>SELECT</c> clause: the optional <c>DISTINCT</c> / <c>REDUCED</c> modifier and
    /// either <c>*</c> or a projection list parsed one variable per step.
    /// </summary>
    /// <param name="frame">The select-clause frame.</param>
    /// <returns>The instruction for the driver.</returns>
    private StepResult StepSelectClause(ParseFrame frame)
    {
        switch(frame.Stage)
        {
            case 0:
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

            case 1:
            {
                if(Current.Kind == SparqlTokenKind.Variable)
                {
                    frame.Projections!.Add(new SelectVariable(new SparqlVariable(Current.Value)));
                    Advance();

                    return StepResult.Continue();
                }

                if(Current.Kind == SparqlTokenKind.OpenParen)
                {
                    throw new SparqlParseException(
                        "SELECT (expression AS ?var) projections are not yet implemented in this build.",
                        Current.Span);
                }

                if(frame.Projections!.Count == 0)
                {
                    throw Expected("a projected variable or '*'", Current);
                }

                return StepResult.Done(new SelectQuery(frame.IsDistinct, frame.IsReduced, IsStar: false, frame.Projections));
            }

            default:
            {
                throw new SparqlParseException("Select clause reached an unknown stage.", frame.StartSpan);
            }
        }
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
            frame.Members = [];
            frame.PendingTriples = [];
            frame.Stage = 1;
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

        if(Current.Kind == SparqlTokenKind.CloseBrace)
        {
            FlushPendingTriples(frame);
            Advance();

            return StepResult.Done(new GroupGraphPattern(frame.Members!));
        }

        if(Current.Kind == SparqlTokenKind.EndOfInput)
        {
            throw Expected("'}' to close the group graph pattern", Current);
        }

        if(CanStartTriple(Current.Kind))
        {
            return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Triple, StartSpan = Current.Span });
        }

        throw Expected("a triple pattern or '}'", Current);
    }

    /// <summary>
    /// Advances one <c>TriplesSameSubject</c>: parses the subject, then walks its predicate-object
    /// list one object per step, emitting a triple for each object and threading the subject and the
    /// current verb through the frame.
    /// </summary>
    /// <param name="frame">The triple frame.</param>
    /// <returns>The instruction for the driver; the popped result is the run of parsed triples.</returns>
    private StepResult StepTriple(ParseFrame frame)
    {
        switch(frame.Stage)
        {
            case 0:
            {
                frame.Subject = ParseVarOrTerm();
                frame.TripleAccumulator = [];
                frame.Stage = 1;

                return StepResult.Continue();
            }

            case 1:
            {
                frame.Verb = ParseVerb();
                frame.Stage = 2;

                return StepResult.Continue();
            }

            case 2:
            {
                TriplePatternTerm objectTerm = ParseObject();
                frame.TripleAccumulator!.Add(new TriplePattern(frame.Subject!, frame.Verb!, objectTerm));

                if(Current.Kind == SparqlTokenKind.Comma)
                {
                    Advance();

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

            default:
            {
                throw new SparqlParseException("Triple reached an unknown stage.", frame.StartSpan);
            }
        }
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
            throw Expected("IRI after BASE", Current);
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
            throw Expected("namespace prefix after PREFIX", Current);
        }

        Utf8String prefixLabel = Current.Value;
        Advance();
        if(Current.Kind != SparqlTokenKind.Iri)
        {
            throw Expected("IRI in PREFIX declaration", Current);
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
    {
        switch(Current.Kind)
        {
            case SparqlTokenKind.Variable:
            {
                return ConsumeVariable();
            }

            case SparqlTokenKind.Iri:
            {
                return new ConstantTerm(new NamedNode(ConsumeIriRef().Value));
            }

            case SparqlTokenKind.PrefixedName:
            {
                return new ConstantTerm(new NamedNode(ConsumePrefixedName().Value));
            }

            case SparqlTokenKind.BlankNodeLabel:
            {
                Utf8String label = Current.Value;
                Advance();

                return new ConstantTerm(new BlankNode(label));
            }

            case SparqlTokenKind.AnonymousBlankNode:
            {
                Advance();

                return new ConstantTerm(new BlankNode(FreshBlankNodeLabel()));
            }

            case SparqlTokenKind.StringLiteral:
            case SparqlTokenKind.LongStringLiteral:
            case SparqlTokenKind.IntegerLiteral:
            case SparqlTokenKind.DecimalLiteral:
            case SparqlTokenKind.DoubleLiteral:
            case SparqlTokenKind.BooleanLiteral:
            {
                return new ConstantTerm(ParseRdfLiteral());
            }

            default:
            {
                throw Expected("a subject or object term", Current);
            }
        }
    }

    /// <summary>
    /// Parses an object term. In this build an object is a <c>VarOrTerm</c>; the triple-node and
    /// path-augmented object forms arrive in later slices.
    /// </summary>
    /// <returns>The parsed object term.</returns>
    private TriplePatternTerm ParseObject()
    {
        return ParseVarOrTerm();
    }

    /// <summary>
    /// Parses a verb (predicate): the <c>a</c> shorthand, an IRI, a prefixed name, or a variable.
    /// Property paths arrive in a later slice.
    /// </summary>
    /// <returns>The parsed predicate term.</returns>
    private TriplePatternTerm ParseVerb()
    {
        switch(Current.Kind)
        {
            case SparqlTokenKind.A:
            {
                Advance();

                return new ConstantTerm(new NamedNode(rdfType));
            }

            case SparqlTokenKind.Iri:
            {
                return new ConstantTerm(new NamedNode(ConsumeIriRef().Value));
            }

            case SparqlTokenKind.PrefixedName:
            {
                return new ConstantTerm(new NamedNode(ConsumePrefixedName().Value));
            }

            case SparqlTokenKind.Variable:
            {
                return ConsumeVariable();
            }

            default:
            {
                throw Expected("a predicate", Current);
            }
        }
    }

    /// <summary>
    /// Consumes the variable at the cursor and advances.
    /// </summary>
    /// <returns>The variable term.</returns>
    private VariableTerm ConsumeVariable()
    {
        VariableTerm term = new(new SparqlVariable(Current.Value));
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
            _ => throw Expected(what, Current)
        };
    }

    /// <summary>
    /// Parses an RDF literal at the cursor: a string with an optional language tag, directional
    /// language tag, or datatype; or a numeric or boolean literal.
    /// </summary>
    /// <returns>The literal term.</returns>
    private Literal ParseRdfLiteral()
    {
        switch(Current.Kind)
        {
            case SparqlTokenKind.StringLiteral:
            case SparqlTokenKind.LongStringLiteral:
            {
                Utf8String lexical = Current.Value;
                Advance();

                switch(Current.Kind)
                {
                    case SparqlTokenKind.LangTag:
                    {
                        Utf8String language = Current.Value;
                        Advance();

                        return new Literal(lexical, new NamedNode(rdfLangString), language, BaseDirection: null);
                    }

                    case SparqlTokenKind.DirLangTag:
                    {
                        (Utf8String language, TextDirection direction) = SplitDirectionalLanguageTag(Current.Value, Current.Span);
                        Advance();

                        return new Literal(lexical, new NamedNode(rdfDirLangString), language, direction);
                    }

                    case SparqlTokenKind.TypeMarker:
                    {
                        Advance();
                        NamedNode datatype = ConsumeDatatypeIri();

                        return new Literal(lexical, datatype, Language: null, BaseDirection: null);
                    }

                    default:
                    {
                        return new Literal(lexical, new NamedNode(xsdString), Language: null, BaseDirection: null);
                    }
                }
            }

            case SparqlTokenKind.IntegerLiteral:
            {
                return ConsumeTypedNumericOrBoolean(xsdInteger);
            }

            case SparqlTokenKind.DecimalLiteral:
            {
                return ConsumeTypedNumericOrBoolean(xsdDecimal);
            }

            case SparqlTokenKind.DoubleLiteral:
            {
                return ConsumeTypedNumericOrBoolean(xsdDouble);
            }

            case SparqlTokenKind.BooleanLiteral:
            {
                return ConsumeTypedNumericOrBoolean(xsdBoolean);
            }

            default:
            {
                throw Expected("a literal", Current);
            }
        }
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
            _ => throw Expected("a datatype IRI after '^^'", Current)
        };
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
            throw Expected(what, Current);
        }

        if(!int.TryParse(Current.Value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            throw new SparqlParseException(
                $"The {what} '{Current.Value}' is not a valid non-negative 32-bit integer.",
                Current.Span);
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
            throw new SparqlParseException(
                $"The prefix '{prefixLabel}' is not bound by any PREFIX declaration.",
                span);
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

        throw new SparqlParseException(
            $"The base direction '{System.Text.Encoding.UTF8.GetString(directionBytes)}' is not 'ltr' or 'rtl'.",
            span);
    }

    /// <summary>
    /// Allocates a fresh synthetic blank-node label for an anonymous <c>[]</c> node.
    /// </summary>
    /// <returns>The interned label, without the <c>_:</c> prefix.</returns>
    private Utf8String FreshBlankNodeLabel()
    {
        string raw = string.Create(CultureInfo.InvariantCulture, $"b{nextBlankNode}");
        nextBlankNode++;

        return Pool.Intern(raw);
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
            or SparqlTokenKind.BooleanLiteral;
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
            or SparqlTokenKind.Variable;
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
    /// Builds a "not yet implemented" exception for a recognised but unsupported query form.
    /// </summary>
    /// <param name="form">The form name.</param>
    /// <returns>The exception to throw.</returns>
    private static SparqlParseException NotImplemented(string form)
    {
        return new SparqlParseException($"The {form} query form is not yet implemented in this build.");
    }

    /// <summary>
    /// Builds an "expected X but found Y" exception anchored at a token.
    /// </summary>
    /// <param name="what">A description of what the parser expected.</param>
    /// <param name="got">The token that was found instead.</param>
    /// <returns>The exception to throw.</returns>
    private static SparqlParseException Expected(string what, SparqlToken got)
    {
        return new SparqlParseException(
            $"Expected {what} but found {got.Kind} '{got.Value}' at line {got.Span.StartLine + 1} column {got.Span.StartColumn + 1}.",
            got.Span);
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
