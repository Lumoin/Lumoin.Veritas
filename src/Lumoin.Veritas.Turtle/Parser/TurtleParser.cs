using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Turtle.Ast;
using Lumoin.Veritas.Turtle.Lexer;

namespace Lumoin.Veritas.Turtle.Parser;

/// <summary>
/// Parses a token stream from <see cref="TurtleLexer"/> into a
/// <see cref="TurtleDocument"/> AST.
/// </summary>
/// <remarks>
/// <para>
/// The parser is iterative — every production that admits unbounded
/// grammar nesting (terms, collections, blank-node property lists,
/// triple terms, reified triples, predicate-object lists, object
/// lists, annotated objects, annotation blocks) is driven by an
/// explicit <see cref="Stack{T}"/> of <see cref="ParseFrame"/>
/// values. The CLR call stack is used only for the top-level
/// statement-dispatch loop and the leaf-token helpers that consume a
/// single bounded form (directives, predicate-or-<c>a</c>, blank-node
/// labels, IRIs, prefixed names, literals). No production calls back
/// into another via method recursion.
/// </para>
/// <para>
/// The work-stack design keeps deeply-nested input (collections of
/// collections, blank-node property lists nested arbitrarily deep)
/// from exhausting the CLR stack and keeps the parser's behaviour
/// observable: frames are inspectable structures, and a failure at
/// depth N still has the frame chain to walk back.
/// </para>
/// </remarks>
public sealed class TurtleParser
{
    //The most tokens any single Step inspects ahead of the cursor: the four-token "@prefix ns: <iri> ."
    //directive reads the terminating '.' at offset three. The driver only runs a Step once this many
    //tokens are buffered ahead (or the stream is complete), so a Step never reads an unbuffered token.
    private const int MaxStepLookahead = 4;

    private readonly Stack<ParseFrame> frames = new();
    private readonly bool incremental;
    private object? completed;
    private bool tokensComplete;
    private int index;
    private int nextNodeId;
    private int nextSyntheticBlankNode;
    private bool inGraphBlock;

    /// <summary>
    /// Initialises a <see cref="TurtleParser"/> over a fully materialised token stream.
    /// </summary>
    /// <param name="tokens">The lexed token stream.</param>
    /// <param name="pool">The pool used to intern parser-allocated identifiers.</param>
    /// <param name="documentId">The content-addressed identifier for the source document.</param>
    /// <param name="syntax">The syntax flavour to accept.</param>
    public TurtleParser(IEnumerable<TurtleToken> tokens, Utf8StringPool pool, DocumentId documentId, TurtleSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(pool);

        Tokens = [];
        foreach(TurtleToken token in tokens)
        {
            Tokens.Add(token);
        }

        Pool = pool;
        DocumentId = documentId;
        Syntax = syntax;
        tokensComplete = true;
    }

    /// <summary>
    /// Initialises a <see cref="TurtleParser"/> that is fed tokens incrementally through
    /// <see cref="FeedToken(TurtleToken)"/> and pulled one statement at a time through
    /// <see cref="TryParseStatement(out Statement)"/>.
    /// </summary>
    /// <param name="pool">The pool used to intern parser-allocated identifiers.</param>
    /// <param name="documentId">The content-addressed identifier for the source document.</param>
    /// <param name="syntax">The syntax flavour to accept.</param>
    /// <remarks>
    /// The parser suspends — preserving its work stack — when a statement needs tokens that have not
    /// arrived yet, and resumes when more are fed, so neither the token buffer nor the parse state
    /// grows with the document. Consumed tokens are trimmed at each statement boundary.
    /// </remarks>
    internal TurtleParser(Utf8StringPool pool, DocumentId documentId, TurtleSyntax syntax)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Tokens = [];
        Pool = pool;
        DocumentId = documentId;
        Syntax = syntax;
        incremental = true;
    }

    /// <summary>Gets the materialised token stream the parser indexes into.</summary>
    private List<TurtleToken> Tokens { get; }

    /// <summary>Gets the pool used to intern parser-allocated identifiers.</summary>
    private Utf8StringPool Pool { get; }

    /// <summary>Gets the content-addressed identifier of the source document.</summary>
    private DocumentId DocumentId { get; }

    /// <summary>Gets the syntax flavour being parsed.</summary>
    private TurtleSyntax Syntax { get; }

    /// <summary>Gets the node-id-keyed lookup table populated as nodes are created.</summary>
    private Dictionary<int, TurtleAstNode> Nodes { get; } = [];

    /// <summary>
    /// Parses the token stream into a <see cref="TurtleDocument"/>.
    /// </summary>
    /// <returns>The parsed document AST.</returns>
    public TurtleDocument Parse()
    {
        ImmutableArray<PrefixDeclaration>.Builder prefixes = ImmutableArray.CreateBuilder<PrefixDeclaration>();
        ImmutableArray<BaseDeclaration>.Builder baseDeclarations = ImmutableArray.CreateBuilder<BaseDeclaration>();
        ImmutableArray<VersionDeclaration>.Builder versions = ImmutableArray.CreateBuilder<VersionDeclaration>();
        ImmutableArray<Statement>.Builder statements = ImmutableArray.CreateBuilder<Statement>();

        //The whole token stream is present, so TryParseStatement never reports NeedMore.
        while(TryParseStatement(out Statement? statement) == ParseStatus.Produced)
        {
            switch(statement)
            {
                case PrefixDeclaration prefix:
                {
                    prefixes.Add(prefix);

                    break;
                }

                case BaseDeclaration baseDecl:
                {
                    baseDeclarations.Add(baseDecl);

                    break;
                }

                case VersionDeclaration version:
                {
                    versions.Add(version);

                    break;
                }

                default:
                {
                    break;
                }
            }

            statements.Add(statement!);
        }

        return new TurtleDocument(
            DocumentId,
            prefixes.ToImmutable(),
            baseDeclarations.ToImmutable(),
            versions.ToImmutable(),
            statements.ToImmutable(),
            Nodes);
    }

    /// <summary>
    /// Appends one lexed token to the parser's buffer. The terminating
    /// <see cref="TurtleTokenKind.EndOfInput"/> token marks the stream complete.
    /// </summary>
    /// <param name="token">The next token in source order.</param>
    internal void FeedToken(TurtleToken token)
    {
        Tokens.Add(token);

        if(token.Kind == TurtleTokenKind.EndOfInput)
        {
            tokensComplete = true;
        }
    }

    /// <summary>
    /// Attempts to parse the next top-level statement from the buffered tokens.
    /// </summary>
    /// <param name="statement">The parsed statement when the result is <see cref="ParseStatus.Produced"/>; otherwise <see langword="null"/>.</param>
    /// <returns>
    /// <see cref="ParseStatus.Produced"/> with a statement; <see cref="ParseStatus.NeedMore"/> when the
    /// current statement needs tokens that have not been fed yet; <see cref="ParseStatus.Completed"/>
    /// once the end-of-input token has been reached at a statement boundary.
    /// </returns>
    internal ParseStatus TryParseStatement(out Statement? statement)
    {
        statement = null;

        if(frames.Count == 0)
        {
            //Between statements every token before the cursor is consumed and unreferenced, so the
            //incremental buffer is trimmed here to keep peak memory near a single statement.
            if(incremental && index > 0)
            {
                Tokens.RemoveRange(0, index);
                index = 0;
            }

            if(!HasLookahead)
            {
                return ParseStatus.NeedMore;
            }

            if(Current.Kind == TurtleTokenKind.EndOfInput)
            {
                return ParseStatus.Completed;
            }

            frames.Push(new ParseFrame { Kind = ParseFrameKind.Statement, StartSpan = Current.Span });
        }

        if(Drive() == DriveOutcome.NeedMore)
        {
            return ParseStatus.NeedMore;
        }

        statement = (Statement)completed!;
        completed = null;

        return ParseStatus.Produced;
    }

    //A Step never reads past MaxStepLookahead tokens ahead of the cursor, so it is safe to run once
    //that many tokens are buffered. When the stream is complete the buffer ends with EndOfInput and
    //the cursor clamps to it, so the remaining short tail is read without further input.
    private bool HasLookahead => tokensComplete || index + MaxStepLookahead < Tokens.Count;

    private TurtleToken Current => Tokens[index];

    private TurtleToken Peek(int offset)
    {
        int target = index + offset;

        return target < Tokens.Count
            ? Tokens[target]
            : Tokens[^1];
    }

    private void Advance()
    {
        if(index < Tokens.Count - 1)
        {
            index++;
        }
    }

    private PrefixDeclaration ParsePrefixDeclaration()
    {
        SourceSpan startSpan = Current.Span;
        bool isSparqlForm = Current.Value.Span.SequenceEqual("PREFIX"u8);
        Advance();

        if(Current.Kind != TurtleTokenKind.PrefixNamespace)
        {
            throw Expected("prefix declaration namespace", Current);
        }

        Utf8String prefixText = ExtractPrefix(Current.Value);
        Advance();

        if(Current.Kind != TurtleTokenKind.Iri)
        {
            throw Expected("IRI in prefix declaration", Current);
        }

        IriTerm iri = NewNode(id => new IriTerm(id, Current.Span, Current.Value));
        SourceSpan endSpan = Current.Span;
        Advance();

        if(!isSparqlForm)
        {
            if(Current.Kind != TurtleTokenKind.Period)
            {
                throw Expected("'.' to terminate @prefix directive", Current);
            }

            endSpan = Current.Span;
            Advance();
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return NewNode(id => new PrefixDeclaration(id, totalSpan, prefixText, iri));
    }

    private BaseDeclaration ParseBaseDeclaration()
    {
        SourceSpan startSpan = Current.Span;
        bool isSparqlForm = Current.Value.Span.SequenceEqual("BASE"u8);
        Advance();

        if(Current.Kind != TurtleTokenKind.Iri)
        {
            throw Expected("IRI in base declaration", Current);
        }

        IriTerm iri = NewNode(id => new IriTerm(id, Current.Span, Current.Value));
        SourceSpan endSpan = Current.Span;
        Advance();

        if(!isSparqlForm)
        {
            if(Current.Kind != TurtleTokenKind.Period)
            {
                throw Expected("'.' to terminate @base directive", Current);
            }

            endSpan = Current.Span;
            Advance();
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return NewNode(id => new BaseDeclaration(id, totalSpan, iri));
    }

    private VersionDeclaration ParseVersionDeclaration()
    {
        SourceSpan startSpan = Current.Span;
        bool isSparqlForm = Current.Value.Span.SequenceEqual("VERSION"u8);
        Advance();

        if(Current.Kind == TurtleTokenKind.LongStringLiteral)
        {
            throw new TurtleParseException(
                $"The version directive argument must be a short-quoted string, not a long (triple-quoted) string, at line {Current.Span.StartLine + 1} column {Current.Span.StartColumn + 1}.",
                Current.Span);
        }

        if(Current.Kind != TurtleTokenKind.StringLiteral)
        {
            throw Expected("string literal in version declaration", Current);
        }

        Utf8String versionValue = Current.Value;
        SourceSpan endSpan = Current.Span;
        Advance();

        if(!isSparqlForm)
        {
            if(Current.Kind != TurtleTokenKind.Period)
            {
                throw Expected("'.' to terminate @version directive", Current);
            }

            endSpan = Current.Span;
            Advance();
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return NewNode(id => new VersionDeclaration(id, totalSpan, versionValue));
    }

    private bool IsGraphLabelLookahead()
    {
        if(Current.Kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.BlankNodeLabel
            && Peek(1).Kind == TurtleTokenKind.OpenBrace)
        {
            return true;
        }

        return false;
    }

    private static bool SubjectAllowsEmptyPredicateList(Term subject)
    {
        //A blank-node property list or a reified-triple may stand alone as a statement subject.
        return subject is BlankNodePropertyListTerm or ReifiedTripleTerm;
    }

    //Drives the instance work stack until the bottom frame pops with a completed statement, or until
    //the next Step needs tokens that have not been fed yet. The stack and the inter-step carry value
    //are instance state, so a NeedMore outcome leaves the parser suspended mid-statement and a later
    //call resumes from the same frame and stage without re-parsing or unwinding.
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
                    throw new TurtleParseException("Parser driver reached an undefined state.", Current.Span);
                }
            }
        }

        return DriveOutcome.Produced;
    }

    private StepResult Step(ParseFrame frame, object? incoming)
    {
        return frame.Kind switch
        {
            ParseFrameKind.Statement => StepStatement(frame, incoming),
            ParseFrameKind.SubjectStatement => StepSubjectStatement(frame, incoming),
            ParseFrameKind.GraphBlock => StepGraphBlock(frame, incoming),
            ParseFrameKind.Term => StepTerm(frame, incoming),
            ParseFrameKind.Collection => StepCollection(frame, incoming),
            ParseFrameKind.BlankNodePropertyList => StepBlankNodePropertyList(frame, incoming),
            ParseFrameKind.TripleTerm => StepTripleTerm(frame, incoming),
            ParseFrameKind.ReifiedTriple => StepReifiedTriple(frame, incoming),
            ParseFrameKind.PredicateObjectList => StepPredicateObjectList(frame, incoming),
            ParseFrameKind.PredicateObject => StepPredicateObject(frame, incoming),
            ParseFrameKind.ObjectList => StepObjectList(frame, incoming),
            ParseFrameKind.AnnotatedObject => StepAnnotatedObject(frame, incoming),
            ParseFrameKind.AnnotationBlock => StepAnnotationBlock(frame, incoming),
            ParseFrameKind.Reifier => StepReifier(frame, incoming),
            _ => throw new TurtleParseException($"Unhandled frame kind {frame.Kind}.", frame.StartSpan)
        };
    }

    private StepResult StepStatement(ParseFrame frame, object? incoming)
    {
        //A pushed graph block or subject-starting statement completed; it is this statement's result.
        if(incoming is not null)
        {
            return StepResult.Done(incoming);
        }

        switch(Current.Kind)
        {
            case TurtleTokenKind.PrefixKeyword:
            {
                return StepResult.Done(ParsePrefixDeclaration());
            }

            case TurtleTokenKind.BaseKeyword:
            {
                return StepResult.Done(ParseBaseDeclaration());
            }

            case TurtleTokenKind.VersionKeyword:
            {
                return StepResult.Done(ParseVersionDeclaration());
            }

            case TurtleTokenKind.GraphKeyword:
            {
                if(Syntax != TurtleSyntax.TriG)
                {
                    throw new TurtleParseException(
                        "GRAPH keyword is only valid in TriG syntax.",
                        Current.Span);
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GraphBlock, StartSpan = Current.Span, HasKeyword = true });
            }

            case TurtleTokenKind.OpenBrace:
            {
                if(Syntax != TurtleSyntax.TriG)
                {
                    throw new TurtleParseException(
                        "Graph block '{' is only valid in TriG syntax.",
                        Current.Span);
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GraphBlock, StartSpan = Current.Span });
            }

            default:
            {
                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.SubjectStatement, StartSpan = Current.Span });
            }
        }
    }

    private StepResult StepSubjectStatement(ParseFrame frame, object? incoming)
    {
        switch(frame.Stage)
        {
            case 0:
            {
                //In TriG, an IRI / prefixed-name / blank-node-label followed by '{' is a graph block label.
                if(Syntax == TurtleSyntax.TriG && IsGraphLabelLookahead())
                {
                    frame.Stage = 3;

                    return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GraphBlock, StartSpan = frame.StartSpan, HasLabel = true });
                }

                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
            }

            case 1:
            {
                Term subject = (Term)incoming!;

                //A triple term is a term that denotes a triple; it may stand only in object position,
                //never as the subject of an asserted statement. A reified triple, by contrast, yields a
                //reifier and may begin a statement.
                if(subject is TripleTermTerm)
                {
                    throw new TurtleParseException(
                        "A triple term may not be the subject of a statement.",
                        subject.Span);
                }

                frame.Subject = subject;

                if(SubjectAllowsEmptyPredicateList(subject) && Current.Kind == TurtleTokenKind.Period)
                {
                    SourceSpan endSpan = Current.Span;
                    Advance();
                    SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);

                    return StepResult.Done(NewNode(id => new TripleStatement(id, totalSpan, subject, ImmutableArray<PredicateObject>.Empty)));
                }

                frame.Stage = 2;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObjectList, StartSpan = Current.Span });
            }

            case 2:
            {
                ImmutableArray<PredicateObject> predicates = (ImmutableArray<PredicateObject>)incoming!;
                Term subject = frame.Subject!;

                //Inside a graph block the final triple's terminating '.' is optional; a closing '}'
                //ends it instead and is left for the graph-block frame to consume.
                if(inGraphBlock && Current.Kind == TurtleTokenKind.CloseBrace)
                {
                    SourceSpan braceEnd = predicates[^1].Span;
                    SourceSpan braceTotal = CombineSpans(frame.StartSpan, braceEnd);

                    return StepResult.Done(NewNode(id => new TripleStatement(id, braceTotal, subject, predicates)));
                }

                if(Current.Kind != TurtleTokenKind.Period)
                {
                    throw Expected("'.' to terminate triple", Current);
                }

                SourceSpan periodSpan = Current.Span;
                Advance();
                SourceSpan total = CombineSpans(frame.StartSpan, periodSpan);

                return StepResult.Done(NewNode(id => new TripleStatement(id, total, subject, predicates)));
            }

            case 3:
            {
                //The label-introduced graph block is itself the statement.
                return StepResult.Done(incoming!);
            }

            default:
            {
                throw new TurtleParseException("Subject-starting statement reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepGraphBlock(ParseFrame frame, object? incoming)
    {
        switch(frame.Stage)
        {
            case 0:
            {
                if(frame.HasKeyword)
                {
                    Advance();
                    frame.Label = ConsumeLeafTermFor("graph label");
                }
                else if(frame.HasLabel)
                {
                    frame.Label = ConsumeLeafTermFor("graph label");
                }

                if(Current.Kind != TurtleTokenKind.OpenBrace)
                {
                    throw Expected("'{' to begin graph block", Current);
                }

                Advance();

                //Inside a graph block the trailing '.' of the final triple is optional, so a triple may
                //be terminated by the closing '}'. The flag lets a subject statement accept that.
                frame.SavedInGraphBlock = inGraphBlock;
                inGraphBlock = true;
                frame.Triples = [];
                frame.Stage = 1;

                return ContinueGraphBlock(frame);
            }

            case 1:
            {
                Statement parsed = (Statement)incoming!;
                if(parsed is not TripleStatement triple)
                {
                    throw new TurtleParseException(
                        "Only triple statements are permitted inside a graph block.",
                        parsed.Span);
                }

                frame.Triples!.Add(triple);

                return ContinueGraphBlock(frame);
            }

            default:
            {
                throw new TurtleParseException("Graph block reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult ContinueGraphBlock(ParseFrame frame)
    {
        if(Current.Kind == TurtleTokenKind.CloseBrace)
        {
            inGraphBlock = frame.SavedInGraphBlock;
            SourceSpan endSpan = Current.Span;
            Advance();
            SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
            Term? label = frame.Label;
            bool hasKeyword = frame.HasKeyword;
            ImmutableArray<TripleStatement> triples = ToImmutable(frame.Triples!);

            return StepResult.Done(NewNode(id => new GraphBlockStatement(id, totalSpan, label, hasKeyword, triples)));
        }

        if(Current.Kind == TurtleTokenKind.EndOfInput)
        {
            throw Expected("'}' to close graph block", Current);
        }

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.SubjectStatement, StartSpan = Current.Span });
    }

    private StepResult StepTerm(ParseFrame frame, object? incoming)
    {
        //A Term frame dispatches on the current token. Leafs resolve immediately and pop;
        //compounds push the matching nested frame and let the parent slot receive its result.
        if(incoming is not null)
        {
            return StepResult.Done(incoming);
        }

        switch(Current.Kind)
        {
            case TurtleTokenKind.Iri:
            {
                return StepResult.Done(ConsumeIri());
            }

            case TurtleTokenKind.PrefixedName:
            {
                return StepResult.Done(ConsumePrefixedName());
            }

            case TurtleTokenKind.BlankNodeLabel:
            {
                return StepResult.Done(ConsumeBlankNodeLabel());
            }

            case TurtleTokenKind.AnonymousBlankNode:
            {
                return StepResult.Done(ConsumeAnonymousBlankNode());
            }

            case TurtleTokenKind.StringLiteral:
            case TurtleTokenKind.LongStringLiteral:
            {
                return StepResult.Done(ParseStringLiteral());
            }

            case TurtleTokenKind.IntegerLiteral:
            {
                return StepResult.Done(ParseNumericLiteral(TurtleTokenKind.IntegerLiteral));
            }

            case TurtleTokenKind.DecimalLiteral:
            {
                return StepResult.Done(ParseNumericLiteral(TurtleTokenKind.DecimalLiteral));
            }

            case TurtleTokenKind.DoubleLiteral:
            {
                return StepResult.Done(ParseNumericLiteral(TurtleTokenKind.DoubleLiteral));
            }

            case TurtleTokenKind.BooleanLiteral:
            {
                return StepResult.Done(ParseBooleanLiteral());
            }

            case TurtleTokenKind.A:
            {
                return StepResult.Done(ConsumeAKeyword());
            }

            case TurtleTokenKind.OpenParen:
            {
                ParseFrame next = new() { Kind = ParseFrameKind.Collection, StartSpan = Current.Span, TermItems = [] };
                Advance();

                return StepResult.Push(next);
            }

            case TurtleTokenKind.OpenBracket:
            {
                ParseFrame next = new() { Kind = ParseFrameKind.BlankNodePropertyList, StartSpan = Current.Span, PredicateObjects = [] };
                Advance();

                return StepResult.Push(next);
            }

            case TurtleTokenKind.OpenTripleTerm:
            {
                ParseFrame next = new() { Kind = ParseFrameKind.TripleTerm, StartSpan = Current.Span, Stage = 0 };
                Advance();

                return StepResult.Push(next);
            }

            case TurtleTokenKind.OpenReifiedTriple:
            {
                ParseFrame next = new() { Kind = ParseFrameKind.ReifiedTriple, StartSpan = Current.Span, Stage = 0 };
                Advance();

                return StepResult.Push(next);
            }

            default:
            {
                throw Expected("term", Current);
            }
        }
    }

    private StepResult StepCollection(ParseFrame frame, object? incoming)
    {
        if(incoming is Term term)
        {
            frame.TermItems!.Add(term);
        }

        if(Current.Kind == TurtleTokenKind.CloseParen)
        {
            SourceSpan endSpan = Current.Span;
            Advance();
            SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
            ImmutableArray<Term> items = ToImmutable(frame.TermItems!);

            Term built = NewNode(id => new CollectionTerm(id, totalSpan, items));

            return StepResult.Done(built);
        }

        if(Current.Kind == TurtleTokenKind.EndOfInput)
        {
            throw Expected("')' to close collection", Current);
        }

        return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
    }

    private StepResult StepBlankNodePropertyList(ParseFrame frame, object? incoming)
    {
        if(incoming is PredicateObject predObj)
        {
            frame.PredicateObjects!.Add(predObj);
        }

        switch(frame.Stage)
        {
            case 0:
            {
                //Initial entry: if immediately followed by ']', produce an empty property list.
                if(Current.Kind == TurtleTokenKind.CloseBracket)
                {
                    return FinaliseBlankNodePropertyList(frame);
                }

                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObject, StartSpan = Current.Span });
            }

            case 1:
            {
                //After parsing one PredicateObject: optionally absorb ';' separators and parse another.
                while(Current.Kind == TurtleTokenKind.Semicolon)
                {
                    Advance();
                }

                if(Current.Kind == TurtleTokenKind.CloseBracket)
                {
                    return FinaliseBlankNodePropertyList(frame);
                }

                if(!CanStartVerb(Current.Kind))
                {
                    throw Expected("predicate or ']' inside blank-node property list", Current);
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObject, StartSpan = Current.Span });
            }

            default:
            {
                throw new TurtleParseException("Blank-node property list reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult FinaliseBlankNodePropertyList(ParseFrame frame)
    {
        SourceSpan endSpan = Current.Span;
        Advance();
        SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
        ImmutableArray<PredicateObject> predicates = ToImmutable(frame.PredicateObjects!);

        Term built = NewNode(id => new BlankNodePropertyListTerm(id, totalSpan, predicates));

        return StepResult.Done(built);
    }

    private StepResult StepTripleTerm(ParseFrame frame, object? incoming)
    {
        switch(frame.Stage)
        {
            case 0:
            {
                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
            }

            case 1:
            {
                frame.Subject = (Term)incoming!;
                ValidateTripleTermSubject(frame.Subject);
                Term predicate = ParsePredicateLeaf();
                frame.Predicate = predicate;
                frame.Stage = 2;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
            }

            case 2:
            {
                frame.Object = (Term)incoming!;
                ValidateTripleTermObject(frame.Object);
                if(Current.Kind != TurtleTokenKind.CloseTripleTerm)
                {
                    throw Expected("')>>' to close triple term", Current);
                }

                SourceSpan endSpan = Current.Span;
                Advance();
                SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);

                Term subject = frame.Subject!;
                Term predicate = frame.Predicate!;
                Term obj = frame.Object!;
                Term built = NewNode(id => new TripleTermTerm(id, totalSpan, subject, predicate, obj));

                return StepResult.Done(built);
            }

            default:
            {
                throw new TurtleParseException("Triple term reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepReifiedTriple(ParseFrame frame, object? incoming)
    {
        switch(frame.Stage)
        {
            case 0:
            {
                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
            }

            case 1:
            {
                frame.Subject = (Term)incoming!;
                ValidateReifiedTripleSubject(frame.Subject);
                Term predicate = ParsePredicateLeaf();
                frame.Predicate = predicate;
                frame.Stage = 2;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
            }

            case 2:
            {
                frame.Object = (Term)incoming!;
                ValidateReifiedTripleObject(frame.Object);

                if(Current.Kind == TurtleTokenKind.Tilde)
                {
                    Advance();
                    if(Current.Kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.BlankNodeLabel)
                    {
                        frame.Reifier = ConsumeLeafTermFor("reifier identifier");
                    }
                }

                if(Current.Kind != TurtleTokenKind.CloseReifiedTriple)
                {
                    throw Expected("'>>' to close reified triple", Current);
                }

                SourceSpan endSpan = Current.Span;
                Advance();
                SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);

                Term subject = frame.Subject!;
                Term predicate = frame.Predicate!;
                Term obj = frame.Object!;
                Term? reifier = frame.Reifier;
                Term built = NewNode(id => new ReifiedTripleTerm(id, totalSpan, subject, predicate, obj, reifier));

                return StepResult.Done(built);
            }

            default:
            {
                throw new TurtleParseException("Reified triple reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepPredicateObjectList(ParseFrame frame, object? incoming)
    {
        if(incoming is PredicateObject predObj)
        {
            frame.PredicateObjects ??= [];
            frame.PredicateObjects.Add(predObj);
        }

        switch(frame.Stage)
        {
            case 0:
            {
                frame.PredicateObjects ??= [];
                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObject, StartSpan = Current.Span });
            }

            case 1:
            {
                //Absorb ';' separators; the grammar allows trailing ';' before terminators.
                while(Current.Kind == TurtleTokenKind.Semicolon)
                {
                    Advance();
                }

                if(!CanStartVerb(Current.Kind))
                {
                    return StepResult.Done(ToImmutable(frame.PredicateObjects!));
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObject, StartSpan = Current.Span });
            }

            default:
            {
                throw new TurtleParseException("Predicate-object list reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepPredicateObject(ParseFrame frame, object? incoming)
    {
        switch(frame.Stage)
        {
            case 0:
            {
                Term predicate = ParsePredicateLeaf();
                frame.Predicate = predicate;
                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.ObjectList, StartSpan = Current.Span });
            }

            case 1:
            {
                ImmutableArray<AnnotatedObject> objects = (ImmutableArray<AnnotatedObject>)incoming!;
                SourceSpan endSpan = objects[^1].Span;
                SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
                Term predicate = frame.Predicate!;
                PredicateObject built = NewNode(id => new PredicateObject(id, totalSpan, predicate, objects));

                return StepResult.Done(built);
            }

            default:
            {
                throw new TurtleParseException("Predicate-object reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepObjectList(ParseFrame frame, object? incoming)
    {
        if(incoming is AnnotatedObject annotated)
        {
            frame.AnnotatedObjects ??= [];
            frame.AnnotatedObjects.Add(annotated);
        }

        switch(frame.Stage)
        {
            case 0:
            {
                frame.AnnotatedObjects ??= [];
                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.AnnotatedObject, StartSpan = Current.Span });
            }

            case 1:
            {
                if(Current.Kind == TurtleTokenKind.Comma)
                {
                    Advance();

                    return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.AnnotatedObject, StartSpan = Current.Span });
                }

                return StepResult.Done(ToImmutable(frame.AnnotatedObjects!));
            }

            default:
            {
                throw new TurtleParseException("Object list reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepAnnotatedObject(ParseFrame frame, object? incoming)
    {
        if(incoming is Term termResult && frame.Stage == 1)
        {
            frame.CurrentObject = termResult;
            frame.Stage = 2;
        }
        else if(incoming is Annotation annotationResult)
        {
            frame.Annotations ??= [];
            frame.Annotations.Add(annotationResult);
        }

        switch(frame.Stage)
        {
            case 0:
            {
                frame.Annotations ??= [];
                frame.Stage = 1;

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Term, StartSpan = Current.Span });
            }

            case 2:
            {
                if(Current.Kind == TurtleTokenKind.Tilde)
                {
                    return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.Reifier, StartSpan = Current.Span });
                }

                if(Current.Kind == TurtleTokenKind.OpenAnnotation)
                {
                    return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.AnnotationBlock, StartSpan = Current.Span, PredicateObjects = [] });
                }

                Term obj = frame.CurrentObject!;
                ImmutableArray<Annotation> annotations = frame.Annotations is null
                    ? ImmutableArray<Annotation>.Empty
                    : ToImmutable(frame.Annotations);

                SourceSpan endSpan = annotations.Length > 0 ? annotations[^1].Span : obj.Span;
                SourceSpan totalSpan = CombineSpans(obj.Span, endSpan);
                AnnotatedObject built = NewNode(id => new AnnotatedObject(id, totalSpan, obj, annotations));

                return StepResult.Done(built);
            }

            default:
            {
                throw new TurtleParseException("Annotated object reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult StepAnnotationBlock(ParseFrame frame, object? incoming)
    {
        if(incoming is PredicateObject predObj)
        {
            frame.PredicateObjects!.Add(predObj);
        }

        switch(frame.Stage)
        {
            case 0:
            {
                Advance();
                frame.Stage = 1;

                //An annotation block must contain at least one predicate-object pair; '{| |}' is invalid.
                if(Current.Kind == TurtleTokenKind.CloseAnnotation)
                {
                    throw new TurtleParseException(
                        $"An annotation block must contain at least one predicate-object pair at line {Current.Span.StartLine + 1} column {Current.Span.StartColumn + 1}.",
                        Current.Span);
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObject, StartSpan = Current.Span });
            }

            case 1:
            {
                while(Current.Kind == TurtleTokenKind.Semicolon)
                {
                    Advance();
                }

                if(Current.Kind == TurtleTokenKind.CloseAnnotation)
                {
                    return FinaliseAnnotationBlock(frame);
                }

                if(!CanStartVerb(Current.Kind))
                {
                    throw Expected("predicate or '|}' inside annotation block", Current);
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.PredicateObject, StartSpan = Current.Span });
            }

            default:
            {
                throw new TurtleParseException("Annotation block reached unknown stage.", frame.StartSpan);
            }
        }
    }

    private StepResult FinaliseAnnotationBlock(ParseFrame frame)
    {
        SourceSpan endSpan = Current.Span;
        Advance();
        SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
        ImmutableArray<PredicateObject> predicates = ToImmutable(frame.PredicateObjects!);

        Annotation built = NewNode(id => new AnnotationBlock(id, totalSpan, predicates));

        return StepResult.Done(built);
    }

    private StepResult StepReifier(ParseFrame frame, object? incoming)
    {
        //A single-step frame: consume '~' and an optional identifier, then pop.
        Advance();
        Term? identifier = Current.Kind switch
        {
            TurtleTokenKind.Iri => ConsumeIri(),
            TurtleTokenKind.PrefixedName => ConsumePrefixedName(),
            TurtleTokenKind.BlankNodeLabel => ConsumeBlankNodeLabel(),
            _ => null
        };

        SourceSpan endSpan = identifier?.Span ?? frame.StartSpan;
        SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
        Annotation built = NewNode(id => new ReifierAnnotation(id, totalSpan, identifier));

        return StepResult.Done(built);
    }

    private Term ConsumeLeafTermFor(string what)
    {
        return Current.Kind switch
        {
            TurtleTokenKind.Iri => ConsumeIri(),
            TurtleTokenKind.PrefixedName => ConsumePrefixedName(),
            TurtleTokenKind.BlankNodeLabel => ConsumeBlankNodeLabel(),
            _ => throw Expected(what, Current)
        };
    }

    private Term ParsePredicateLeaf()
    {
        //Predicates are bounded forms — no recursion possible — so they parse directly.
        return Current.Kind switch
        {
            TurtleTokenKind.Iri => ConsumeIri(),
            TurtleTokenKind.PrefixedName => ConsumePrefixedName(),
            TurtleTokenKind.A => ConsumeAKeyword(),
            _ => throw Expected("predicate", Current)
        };
    }

    private static bool CanStartVerb(TurtleTokenKind kind)
    {
        return kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.A;
    }

    private static bool IsIriOrBlank(Term term)
    {
        return term is IriTerm or PrefixedNameTerm or BlankNodeTerm;
    }

    private static void ValidateTripleTermSubject(Term subject)
    {
        //ttSubject ::= iri | BlankNode.
        if(!IsIriOrBlank(subject))
        {
            throw new TurtleParseException(
                "The subject of a triple term must be an IRI or a blank node.",
                subject.Span);
        }
    }

    private static void ValidateTripleTermObject(Term objectTerm)
    {
        //ttObject ::= iri | BlankNode | literal | tripleTerm.
        if(!IsIriOrBlank(objectTerm) && objectTerm is not (LiteralTerm or TripleTermTerm))
        {
            throw new TurtleParseException(
                "The object of a triple term must be an IRI, blank node, literal, or triple term.",
                objectTerm.Span);
        }
    }

    private static void ValidateReifiedTripleSubject(Term subject)
    {
        //rtSubject ::= iri | BlankNode | reifiedTriple.
        if(!IsIriOrBlank(subject) && subject is not ReifiedTripleTerm)
        {
            throw new TurtleParseException(
                "The subject of a reified triple must be an IRI, blank node, or reified triple.",
                subject.Span);
        }
    }

    private static void ValidateReifiedTripleObject(Term objectTerm)
    {
        //rtObject ::= iri | BlankNode | literal | tripleTerm | reifiedTriple.
        if(!IsIriOrBlank(objectTerm) && objectTerm is not (LiteralTerm or TripleTermTerm or ReifiedTripleTerm))
        {
            throw new TurtleParseException(
                "The object of a reified triple must be an IRI, blank node, literal, triple term, or reified triple.",
                objectTerm.Span);
        }
    }

    private IriTerm ConsumeAKeyword()
    {
        SourceSpan span = Current.Span;
        Utf8String rdfType = Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
        Advance();

        return NewNode(id => new IriTerm(id, span, rdfType));
    }

    private IriTerm ConsumeIri()
    {
        SourceSpan span = Current.Span;
        Utf8String value = Current.Value;
        Advance();

        return NewNode(id => new IriTerm(id, span, value));
    }

    private PrefixedNameTerm ConsumePrefixedName()
    {
        SourceSpan span = Current.Span;
        ReadOnlySpan<byte> bytes = Current.Value.Span;
        int colonIndex = bytes.IndexOf((byte)':');
        if(colonIndex < 0)
        {
            throw new TurtleParseException(
                "Prefixed name missing ':' separator.",
                span);
        }

        Utf8String prefix = Pool.Intern(bytes[..colonIndex]);
        Utf8String local = Pool.Intern(bytes[(colonIndex + 1)..]);
        Advance();

        return NewNode(id => new PrefixedNameTerm(id, span, prefix, local));
    }

    private BlankNodeTerm ConsumeBlankNodeLabel()
    {
        SourceSpan span = Current.Span;
        Utf8String label = Current.Value;
        Advance();

        return NewNode(id => new BlankNodeTerm(id, span, label));
    }

    private BlankNodeTerm ConsumeAnonymousBlankNode()
    {
        SourceSpan span = Current.Span;
        Advance();

        Utf8String label = AllocateSyntheticBlankNodeLabel();

        return NewNode(id => new BlankNodeTerm(id, span, label));
    }

    private LiteralTerm ParseStringLiteral()
    {
        SourceSpan startSpan = Current.Span;
        Utf8String value = Current.Value;
        Advance();

        Term? datatype = null;
        Utf8String? language = null;
        TextDirection? direction = null;
        SourceSpan endSpan = startSpan;

        switch(Current.Kind)
        {
            case TurtleTokenKind.LangTag:
            {
                language = Current.Value;
                endSpan = Current.Span;
                Advance();

                break;
            }

            case TurtleTokenKind.DirLangTag:
            {
                ReadOnlySpan<byte> bytes = Current.Value.Span;
                int dirIndex = IndexOfDoubleDash(bytes);
                if(dirIndex < 0)
                {
                    throw new TurtleParseException(
                        "Direction-tagged language tag missing '--' separator.",
                        Current.Span);
                }

                language = Pool.Intern(bytes[..dirIndex]);
                ReadOnlySpan<byte> dirSpan = bytes[(dirIndex + 2)..];
                direction = ParseDirection(dirSpan, Current.Span);
                endSpan = Current.Span;
                Advance();

                break;
            }

            case TurtleTokenKind.TypeMarker:
            {
                Advance();
                switch(Current.Kind)
                {
                    case TurtleTokenKind.Iri:
                    {
                        datatype = ConsumeIri();
                        endSpan = datatype.Span;

                        break;
                    }

                    case TurtleTokenKind.PrefixedName:
                    {
                        datatype = ConsumePrefixedName();
                        endSpan = datatype.Span;

                        break;
                    }

                    default:
                    {
                        throw Expected("datatype IRI after '^^'", Current);
                    }
                }

                break;
            }

            default:
            {
                break;
            }
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return NewNode(id => new LiteralTerm(id, totalSpan, value, datatype, language, direction));
    }

    private static TextDirection ParseDirection(ReadOnlySpan<byte> directionBytes, SourceSpan span)
    {
        if(directionBytes.SequenceEqual("ltr"u8))
        {
            return TextDirection.Ltr;
        }

        if(directionBytes.SequenceEqual("rtl"u8))
        {
            return TextDirection.Rtl;
        }

        throw new TurtleParseException(
            $"Invalid base direction '{System.Text.Encoding.UTF8.GetString(directionBytes)}'; expected 'ltr' or 'rtl'.",
            span);
    }

    private LiteralTerm ParseNumericLiteral(TurtleTokenKind kind)
    {
        SourceSpan span = Current.Span;
        Utf8String text = Current.Value;
        Advance();

        Utf8String datatypeIri = kind switch
        {
            TurtleTokenKind.IntegerLiteral => Pool.Intern("http://www.w3.org/2001/XMLSchema#integer"u8),
            TurtleTokenKind.DecimalLiteral => Pool.Intern("http://www.w3.org/2001/XMLSchema#decimal"u8),
            TurtleTokenKind.DoubleLiteral => Pool.Intern("http://www.w3.org/2001/XMLSchema#double"u8),
            _ => throw new TurtleParseException("Numeric kind out of range.", span)
        };

        Term datatype = NewNode(id => new IriTerm(id, span, datatypeIri));

        return NewNode(id => new LiteralTerm(id, span, text, datatype, language: null, direction: null));
    }

    private LiteralTerm ParseBooleanLiteral()
    {
        SourceSpan span = Current.Span;
        Utf8String text = Current.Value;
        Advance();

        Utf8String datatypeIri = Pool.Intern("http://www.w3.org/2001/XMLSchema#boolean"u8);
        Term datatype = NewNode(id => new IriTerm(id, span, datatypeIri));

        return NewNode(id => new LiteralTerm(id, span, text, datatype, language: null, direction: null));
    }

    private Utf8String AllocateSyntheticBlankNodeLabel()
    {
        string raw = string.Create(CultureInfo.InvariantCulture, $"b{nextSyntheticBlankNode}");
        nextSyntheticBlankNode++;

        return Pool.Intern(raw);
    }

    private TNode NewNode<TNode>(Func<int, TNode> factory)
        where TNode: TurtleAstNode
    {
        int id = nextNodeId++;
        TNode node = factory(id);
        Nodes[id] = node;

        return node;
    }

    private static SourceSpan CombineSpans(SourceSpan left, SourceSpan right)
    {
        return new SourceSpan(
            StartByte: left.StartByte,
            EndByte: right.EndByte,
            StartLine: left.StartLine,
            StartColumn: left.StartColumn,
            EndLine: right.EndLine,
            EndColumn: right.EndColumn);
    }

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

    private Utf8String ExtractPrefix(Utf8String namespaceToken)
    {
        ReadOnlySpan<byte> bytes = namespaceToken.Span;
        if(bytes.Length == 0 || bytes[^1] != (byte)':')
        {
            throw new TurtleParseException("Prefix declaration name must end with ':'.", Current.Span);
        }

        return Pool.Intern(bytes[..^1]);
    }

    private static ImmutableArray<TItem> ToImmutable<TItem>(List<TItem> items)
    {
        ImmutableArray<TItem>.Builder builder = ImmutableArray.CreateBuilder<TItem>(items.Count);
        for(int i = 0; i < items.Count; i++)
        {
            builder.Add(items[i]);
        }

        return builder.ToImmutable();
    }

    private static TurtleParseException Expected(string what, TurtleToken got)
    {
        return new TurtleParseException(
            $"Expected {what} but found {got.Kind} '{got.Value}' at line {got.Span.StartLine + 1} column {got.Span.StartColumn + 1}.",
            got.Span);
    }

    /// <summary>
    /// The instruction returned by a step method: either pop the
    /// current frame with a completed result, push a new frame to
    /// recurse into, or continue the same frame on the next iteration.
    /// </summary>
    private readonly struct StepResult
    {
        private StepResult(StepAction action, object? result, ParseFrame? newFrame)
        {
            Action = action;
            Result = result;
            NewFrame = newFrame;
        }

        public StepAction Action { get; }

        public object? Result { get; }

        public ParseFrame? NewFrame { get; }

        public static StepResult Done(object result) => new(StepAction.Pop, result, null);

        public static StepResult Push(ParseFrame next) => new(StepAction.Push, null, next);
    }

    private enum StepAction
    {
        Pop,
        Push,
        Continue
    }

    private enum DriveOutcome
    {
        Produced,
        NeedMore
    }
}
