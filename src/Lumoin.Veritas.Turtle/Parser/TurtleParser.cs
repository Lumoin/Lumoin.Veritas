using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Diagnostics;
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
    private readonly bool retainAst;
    private object? completed;
    private bool tokensComplete;
    private int index;
    private int nextNodeId;
    private readonly BlankNodeDelegate blankNodes;
    private bool inGraphBlock;

    //Set by the completion seam so Drive suspends at the end-of-input token with the work stack intact —
    //the caret's open-frame chain — instead of recovering the open productions; never set on a normal parse.
    private bool suspendAtEndOfInput;

    /// <summary>
    /// Initialises a <see cref="TurtleParser"/> over a fully materialised token stream.
    /// </summary>
    /// <param name="tokens">The lexed token stream.</param>
    /// <param name="pool">The pool used to intern parser-allocated identifiers.</param>
    /// <param name="documentId">The content-addressed identifier for the source document.</param>
    /// <param name="syntax">The syntax flavour to accept.</param>
    /// <param name="blankNodes">Allocates labels for anonymous <c>[]</c> blank nodes; defaults to <see cref="VeritasBlankNodes.System"/>.</param>
    /// <param name="diagnostics">The bag recovery records diagnostics into; a private bag is created when <see langword="null"/>. Pass a shared bag to merge lexer-bridged and parser diagnostics.</param>
    public TurtleParser(IEnumerable<TurtleToken> tokens, Utf8StringPool pool, DocumentId documentId, TurtleSyntax syntax, BlankNodeDelegate? blankNodes = null, DiagnosticBag? diagnostics = null)
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
        this.blankNodes = blankNodes ?? VeritasBlankNodes.System;
        Diagnostics = diagnostics ?? new DiagnosticBag();
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
    /// <param name="blankNodes">Allocates labels for anonymous <c>[]</c> blank nodes; defaults to <see cref="VeritasBlankNodes.System"/>.</param>
    /// <param name="diagnostics">The bag recovery records diagnostics into; a private bag is created when <see langword="null"/>. Pass a shared bag to merge lexer-bridged and parser diagnostics.</param>
    /// <param name="retainAst">When <see langword="false"/> (the quad-streaming default), consumed tokens and their nodes are released at each statement boundary so peak memory tracks a single statement. When <see langword="true"/>, they are retained so the whole document and its node table accumulate for a final <see cref="Parse"/> — the editor-incremental AST reader's mode, equivalent to the whole-buffer parse fed incrementally.</param>
    /// <remarks>
    /// The parser suspends — preserving its work stack — when a statement needs tokens that have not
    /// arrived yet, and resumes when more are fed. With <paramref name="retainAst"/> <see langword="false"/>,
    /// consumed tokens are trimmed at each statement boundary so neither the token buffer nor the parse
    /// state grows with the document; with it <see langword="true"/>, both accumulate for the full AST.
    /// </remarks>
    internal TurtleParser(Utf8StringPool pool, DocumentId documentId, TurtleSyntax syntax, BlankNodeDelegate? blankNodes = null, DiagnosticBag? diagnostics = null, bool retainAst = false)
    {
        ArgumentNullException.ThrowIfNull(pool);

        Tokens = [];
        Pool = pool;
        DocumentId = documentId;
        Syntax = syntax;
        this.blankNodes = blankNodes ?? VeritasBlankNodes.System;
        Diagnostics = diagnostics ?? new DiagnosticBag();
        incremental = true;
        this.retainAst = retainAst;
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
    /// Gets the bag recovery records diagnostics into. Lexical diagnostics bridged by the reader and
    /// the parser's own syntax diagnostics accumulate here in source order.
    /// </summary>
    internal DiagnosticBag Diagnostics { get; }

    /// <summary>
    /// Parses the token stream into a <see cref="ParseResult{TTree}"/>: the document (possibly carrying
    /// error nodes) together with the accumulated diagnostics and whether any has error severity.
    /// </summary>
    /// <returns>The parse result.</returns>
    public ParseResult<TurtleDocument> ParseToResult()
    {
        TurtleDocument document = Parse();

        return new ParseResult<TurtleDocument>(document, Diagnostics.Diagnostics, Diagnostics.HasErrors);
    }

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
            //Between statements every consumed token and every node is unreferenced by a streaming
            //consumer — the returned statement holds its own nodes directly, not through the table —
            //so both are released here. Peak memory stays near a single statement and the node id never
            //grows without bound across a long document. The materialised path and the AST-retaining
            //incremental reader keep both for the document's node table.
            if(incremental && !retainAst)
            {
                if(index > 0)
                {
                    Tokens.RemoveRange(0, index);
                    index = 0;
                }

                Nodes.Clear();
                nextNodeId = 0;
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

    /// <summary>
    /// Returns the open parse frames at the current suspension, innermost first: each frame's production
    /// kind together with the sub-stage it is suspended at. After the parser is driven to a caret, this is the
    /// enclosing-production chain — the top entry is the innermost open production and its stage fixes the
    /// grammatical position — which the completion seam maps to the expected next tokens. Empty at a statement
    /// boundary, where no frame is open.
    /// </summary>
    /// <returns>The open frames, from the innermost (top of the work stack) outward.</returns>
    internal IReadOnlyList<(ParseFrameKind Kind, int Stage)> OpenFrames()
    {
        (ParseFrameKind Kind, int Stage)[] open = new (ParseFrameKind Kind, int Stage)[frames.Count];
        int next = 0;
        foreach(ParseFrame frame in frames)
        {
            open[next] = (frame.Kind, frame.Stage);
            next++;
        }

        return open;
    }

    /// <summary>
    /// Drives the buffered tokens for completion and returns the productions open when the cursor reaches the
    /// end of the fed source — the enclosing-production chain at a caret, innermost first. Unlike a normal
    /// parse, the open productions are not recovered into error nodes: the driver suspends with its work stack
    /// intact at the end-of-input token, so the snapshot reflects where a caret at the end of the source sits
    /// in the grammar. Complete statements before the caret are drained; the partial statement at the caret is
    /// the one left open. The terminating <see cref="TurtleTokenKind.EndOfInput"/> token must already have
    /// been fed (the completion seam lexes the source up to the caret and finalizes it).
    /// </summary>
    /// <returns>The open frames at the caret, innermost outward; empty when the caret sits at a statement boundary.</returns>
    internal IReadOnlyList<(ParseFrameKind Kind, int Stage)> SuspendOpenFramesAtEndOfInput()
    {
        suspendAtEndOfInput = true;
        while(TryParseStatement(out _) == ParseStatus.Produced)
        {
            //Drain each complete statement before the caret; the partial statement at the caret suspends.
        }

        return OpenFrames();
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

    private Statement ParsePrefixDeclaration()
    {
        SourceSpan startSpan = Current.Span;
        bool isSparqlForm = Current.Value.Span.SequenceEqual("PREFIX"u8);
        Advance();

        if(Current.Kind != TurtleTokenKind.PrefixNamespace)
        {
            return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.ExpectedPrefixNamespace, Current.Span, "A prefix declaration must be followed by a namespace label such as 'ex:'.", "prefixID");
        }

        Utf8String prefixText = ExtractPrefix(Current.Value);
        Advance();

        if(Current.Kind != TurtleTokenKind.Iri)
        {
            return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.ExpectedDirectiveIri, Current.Span, "A prefix declaration must declare an IRI for its namespace.", "prefixID");
        }

        IriTerm iri = Register(new IriTerm(NextNodeId(), Current.Span, Current.Value));
        SourceSpan endSpan = Current.Span;
        Advance();

        if(!isSparqlForm)
        {
            if(Current.Kind != TurtleTokenKind.Period)
            {
                return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.ExpectedDot, Current.Span, "A @prefix directive must end with '.'.", "prefixID");
            }

            endSpan = Current.Span;
            Advance();
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return Register(new PrefixDeclaration(NextNodeId(), totalSpan, prefixText, iri));
    }

    private Statement ParseBaseDeclaration()
    {
        SourceSpan startSpan = Current.Span;
        bool isSparqlForm = Current.Value.Span.SequenceEqual("BASE"u8);
        Advance();

        if(Current.Kind != TurtleTokenKind.Iri)
        {
            return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.ExpectedDirectiveIri, Current.Span, "A base declaration must declare an IRI.", "base");
        }

        IriTerm iri = Register(new IriTerm(NextNodeId(), Current.Span, Current.Value));
        SourceSpan endSpan = Current.Span;
        Advance();

        if(!isSparqlForm)
        {
            if(Current.Kind != TurtleTokenKind.Period)
            {
                return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.ExpectedDot, Current.Span, "A @base directive must end with '.'.", "base");
            }

            endSpan = Current.Span;
            Advance();
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return Register(new BaseDeclaration(NextNodeId(), totalSpan, iri));
    }

    private Statement ParseVersionDeclaration()
    {
        SourceSpan startSpan = Current.Span;
        bool isSparqlForm = Current.Value.Span.SequenceEqual("VERSION"u8);
        Advance();

        if(Current.Kind == TurtleTokenKind.LongStringLiteral)
        {
            return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.InvalidVersionArgument, Current.Span, "The version directive argument must be a short-quoted string, not a long (triple-quoted) string.", "version");
        }

        if(Current.Kind != TurtleTokenKind.StringLiteral)
        {
            return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.InvalidVersionArgument, Current.Span, "A version directive must be followed by a short-quoted string literal.", "version");
        }

        Utf8String versionValue = Current.Value;
        SourceSpan endSpan = Current.Span;
        Advance();

        if(!isSparqlForm)
        {
            if(Current.Kind != TurtleTokenKind.Period)
            {
                return RecoverStatement(ParseFrameKind.Statement, startSpan, WellKnownDiagnostics.Turtle.ExpectedDot, Current.Span, "A @version directive must end with '.'.", "version");
            }

            endSpan = Current.Span;
            Advance();
        }

        SourceSpan totalSpan = CombineSpans(startSpan, endSpan);

        return Register(new VersionDeclaration(NextNodeId(), totalSpan, versionValue));
    }

    private bool IsGraphLabelLookahead()
    {
        if(Current.Kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.PrefixNamespace or TurtleTokenKind.BlankNodeLabel
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

            if(suspendAtEndOfInput && Current.Kind == TurtleTokenKind.EndOfInput)
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

        //Skip any stray tokens that cannot begin a statement — closers/separators a deeper frame left
        //behind, or junk — recording one diagnostic per run. Doing this here is what keeps the top-level
        //loop progressing: such tokens have no enclosing frame to consume them, so without this they
        //would be re-dispatched forever.
        bool reportedStray = false;
        while(Current.Kind != TurtleTokenKind.EndOfInput && !CanStartStatement(Current.Kind))
        {
            if(!reportedStray)
            {
                _ = ReportRecoverable(WellKnownDiagnostics.Turtle.ExpectedTerm, Current.Span, "A statement cannot begin with this token.");
                reportedStray = true;
            }

            int before = index;
            Advance();

            if(index == before)
            {
                break;
            }
        }

        //If the skipped run reached end of input, the trailing junk is reported as a single error node.
        if(Current.Kind == TurtleTokenKind.EndOfInput)
        {
            if(reportedStray)
            {
                return StepResult.Done(Register(new ErrorStatement(NextNodeId(), frame.StartSpan, Utf8Strings.From("statement"), [], ImmutableArray<TurtleToken>.Empty)));
            }

            //An empty document with the frame already pushed cannot occur (the driver only pushes the
            //frame when a non-end token is present), so reaching here without skipping is an invariant.
            throw new TurtleParseException("Statement frame entered at end of input.", frame.StartSpan);
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
                    return StepResult.Done(RecoverStatement(ParseFrameKind.Statement, Current.Span, WellKnownDiagnostics.Turtle.GraphBlockRequiresTriG, Current.Span, "The GRAPH keyword is only valid in TriG syntax.", "graphStatement"));
                }

                return StepResult.Push(new ParseFrame { Kind = ParseFrameKind.GraphBlock, StartSpan = Current.Span, HasKeyword = true });
            }

            case TurtleTokenKind.OpenBrace:
            {
                if(Syntax != TurtleSyntax.TriG)
                {
                    return StepResult.Done(RecoverStatement(ParseFrameKind.Statement, Current.Span, WellKnownDiagnostics.Turtle.GraphBlockRequiresTriG, Current.Span, "A graph block '{' is only valid in TriG syntax.", "graphStatement"));
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
                //reifier and may begin a statement. The malformed subject is kept faithfully (the emitter
                //skips it); the diagnostic flags it without abandoning the rest of the statement.
                if(subject is TripleTermTerm)
                {
                    Report(WellKnownDiagnostics.Turtle.TripleTermAsSubject, subject.Span, "A triple term may not be the subject of a statement.");
                }

                frame.Subject = subject;

                if(SubjectAllowsEmptyPredicateList(subject) && Current.Kind == TurtleTokenKind.Period)
                {
                    SourceSpan endSpan = Current.Span;
                    Advance();
                    SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);

                    return StepResult.Done(Register(new TripleStatement(NextNodeId(), totalSpan, subject, ImmutableArray<PredicateObject>.Empty)));
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

                    return StepResult.Done(Register(new TripleStatement(NextNodeId(), braceTotal, subject, predicates)));
                }

                //A missing terminator does not throw away the parsed triple: the diagnostic is recorded,
                //junk up to the next '.'/'}' is skipped, and the recoverable triple is still produced.
                if(Current.Kind != TurtleTokenKind.Period)
                {
                    _ = ReportRecoverable(WellKnownDiagnostics.Turtle.ExpectedDot, Current.Span, "A triple must be terminated with '.'.");
                    ResyncTo(ParseFrameKind.SubjectStatement, Current.Span, out _);
                    SourceSpan recoveredEnd = Current.Kind == TurtleTokenKind.Period ? Current.Span : predicates[^1].Span;

                    if(Current.Kind == TurtleTokenKind.Period)
                    {
                        Advance();
                    }

                    SourceSpan recoveredTotal = CombineSpans(frame.StartSpan, recoveredEnd);

                    return StepResult.Done(Register(new TripleStatement(NextNodeId(), recoveredTotal, subject, predicates)));
                }

                SourceSpan periodSpan = Current.Span;
                Advance();
                SourceSpan total = CombineSpans(frame.StartSpan, periodSpan);

                return StepResult.Done(Register(new TripleStatement(NextNodeId(), total, subject, predicates)));
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
                    return StepResult.Done(RecoverStatement(ParseFrameKind.Statement, frame.StartSpan, WellKnownDiagnostics.Turtle.ExpectedGraphBlockOpen, Current.Span, "A graph block must begin with '{'.", "wrappedGraph"));
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

                //A directive inside a graph block is invalid; the diagnostic flags it and the statement is
                //dropped from the block. An ErrorStatement is already diagnosed, so it is silently skipped.
                if(parsed is TripleStatement triple)
                {
                    frame.Triples!.Add(triple);
                }
                else if(parsed is not ErrorStatement)
                {
                    Report(WellKnownDiagnostics.Turtle.OnlyTriplesInGraphBlock, parsed.Span, "Only triple statements are permitted inside a graph block.");
                }

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

            return StepResult.Done(Register(new GraphBlockStatement(NextNodeId(), totalSpan, label, hasKeyword, triples)));
        }

        //An unclosed graph block at end of input is finalised with the triples gathered so far, plus a
        //diagnostic — the partial block is the most faithful recoverable shape for an editor.
        if(Current.Kind == TurtleTokenKind.EndOfInput)
        {
            Report(WellKnownDiagnostics.Turtle.UnclosedGraphBlock, Current.Span, "A graph block must be closed with '}'.");
            inGraphBlock = frame.SavedInGraphBlock;
            SourceSpan eofTotal = CombineSpans(frame.StartSpan, Current.Span);
            Term? eofLabel = frame.Label;
            bool eofHasKeyword = frame.HasKeyword;
            ImmutableArray<TripleStatement> eofTriples = ToImmutable(frame.Triples!);

            return StepResult.Done(Register(new GraphBlockStatement(NextNodeId(), eofTotal, eofLabel, eofHasKeyword, eofTriples)));
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
            case TurtleTokenKind.PrefixNamespace:
            {
                //PrefixNamespace is an empty-local prefixed name (ex:), a valid iri term.
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
                return StepResult.Done(RecoverTerm(ParseFrameKind.Term, Current.Span, WellKnownDiagnostics.Turtle.ExpectedTerm, Current.Span, "A term (IRI, literal, blank node, collection, or triple term) was expected.", "object"));
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

            Term built = Register(new CollectionTerm(NextNodeId(), totalSpan, items));

            return StepResult.Done(built);
        }

        //An unterminated collection — end of input, or a token that cannot start an item and is not ')' —
        //is finalised with the items gathered so far plus a diagnostic. Refusing to push a Term frame on a
        //non-item token is also what guarantees progress: a Term frame on such a token would resync to an
        //empty ErrorTerm without advancing, and the collection would re-push it forever.
        if(Current.Kind == TurtleTokenKind.EndOfInput || !CanStartTerm(Current.Kind))
        {
            _ = ReportRecoverable(WellKnownDiagnostics.Turtle.UnclosedCollection, Current.Span, "A collection must be closed with ')'.");
            SourceSpan eofTotal = CombineSpans(frame.StartSpan, Current.Span);
            ImmutableArray<Term> eofItems = ToImmutable(frame.TermItems!);

            return StepResult.Done(Register(new CollectionTerm(NextNodeId(), eofTotal, eofItems)));
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

                //A token that is neither a verb nor ']' breaks the list; skip to ']' and finalise with the
                //pairs gathered so far. Skipping (rather than re-pushing a PredicateObject) guarantees
                //progress past a stray token the inner frames would not consume.
                if(!CanStartVerb(Current.Kind))
                {
                    _ = ReportRecoverable(WellKnownDiagnostics.Turtle.ExpectedPredicate, Current.Span, "A predicate or ']' was expected inside a blank-node property list.");
                    ResyncTo(ParseFrameKind.BlankNodePropertyList, Current.Span, out _);

                    return FinaliseBlankNodePropertyList(frame);
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

        Term built = Register(new BlankNodePropertyListTerm(NextNodeId(), totalSpan, predicates));

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
                    return StepResult.Done(RecoverTerm(ParseFrameKind.TripleTerm, frame.StartSpan, WellKnownDiagnostics.Turtle.UnclosedTripleTerm, Current.Span, "A triple term must be closed with ')>>'.", "tripleTerm"));
                }

                SourceSpan endSpan = Current.Span;
                Advance();
                SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);

                Term subject = frame.Subject!;
                Term predicate = frame.Predicate!;
                Term obj = frame.Object!;
                Term built = Register(new TripleTermTerm(NextNodeId(), totalSpan, subject, predicate, obj));

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
                    if(Current.Kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.PrefixNamespace or TurtleTokenKind.BlankNodeLabel)
                    {
                        frame.Reifier = ConsumeLeafTermFor("reifier identifier");
                    }
                }

                if(Current.Kind != TurtleTokenKind.CloseReifiedTriple)
                {
                    return StepResult.Done(RecoverTerm(ParseFrameKind.ReifiedTriple, frame.StartSpan, WellKnownDiagnostics.Turtle.UnclosedReifiedTriple, Current.Span, "A reified triple must be closed with '>>'.", "reifiedTriple"));
                }

                SourceSpan endSpan = Current.Span;
                Advance();
                SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);

                Term subject = frame.Subject!;
                Term predicate = frame.Predicate!;
                Term obj = frame.Object!;
                Term? reifier = frame.Reifier;
                Term built = Register(new ReifiedTripleTerm(NextNodeId(), totalSpan, subject, predicate, obj, reifier));

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
                PredicateObject built = Register(new PredicateObject(NextNodeId(), totalSpan, predicate, objects));

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
                AnnotatedObject built = Register(new AnnotatedObject(NextNodeId(), totalSpan, obj, annotations));

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
                    return StepResult.Done(RecoverAnnotation(frame.StartSpan, WellKnownDiagnostics.Turtle.EmptyAnnotationBlock, Current.Span, "An annotation block must contain at least one predicate-object pair.", "annotation"));
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
                    return StepResult.Done(RecoverAnnotation(frame.StartSpan, WellKnownDiagnostics.Turtle.ExpectedAnnotationVerbOrClose, Current.Span, "A predicate or '|}' was expected inside an annotation block.", "annotation"));
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

        Annotation built = Register(new AnnotationBlock(NextNodeId(), totalSpan, predicates));

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
            TurtleTokenKind.PrefixNamespace => ConsumePrefixedName(),
            TurtleTokenKind.BlankNodeLabel => ConsumeBlankNodeLabel(),
            _ => null
        };

        SourceSpan endSpan = identifier?.Span ?? frame.StartSpan;
        SourceSpan totalSpan = CombineSpans(frame.StartSpan, endSpan);
        Annotation built = Register(new ReifierAnnotation(NextNodeId(), totalSpan, identifier));

        return StepResult.Done(built);
    }

    private Term ConsumeLeafTermFor(string what)
    {
        return Current.Kind switch
        {
            TurtleTokenKind.Iri => ConsumeIri(),
            TurtleTokenKind.PrefixedName => ConsumePrefixedName(),
            TurtleTokenKind.PrefixNamespace => ConsumePrefixedName(),
            TurtleTokenKind.BlankNodeLabel => ConsumeBlankNodeLabel(),
            _ => RecoverTerm(ParseFrameKind.Term, Current.Span, WellKnownDiagnostics.Turtle.ExpectedTerm, Current.Span, $"A {what} was expected.", what)
        };
    }

    private Term ParsePredicateLeaf()
    {
        //Predicates are bounded forms — no recursion possible — so they parse directly.
        return Current.Kind switch
        {
            TurtleTokenKind.Iri => ConsumeIri(),
            TurtleTokenKind.PrefixedName => ConsumePrefixedName(),
            TurtleTokenKind.PrefixNamespace => ConsumePrefixedName(),
            TurtleTokenKind.A => ConsumeAKeyword(),
            _ => RecoverTerm(ParseFrameKind.Term, Current.Span, WellKnownDiagnostics.Turtle.ExpectedPredicate, Current.Span, "A predicate (IRI, prefixed name, or 'a') was expected.", "verb")
        };
    }

    /// <summary>Determines whether a token kind can begin a verb (predicate): an IRI, a prefixed name (or a bare prefix namespace mid-token), or the <c>a</c> shorthand.</summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a verb.</returns>
    internal static bool CanStartVerb(TurtleTokenKind kind)
    {
        return kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.PrefixNamespace or TurtleTokenKind.A;
    }

    /// <summary>Determines whether a token kind can begin a term — a subject or object: an IRI, prefixed name (or bare prefix namespace), blank node, literal, collection, blank-node property list, triple term, or reified triple.</summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a term.</returns>
    /// <remarks>List frames push a Term sub-frame only on such a token (the <c>Error</c> token included, which the sub-frame consumes while recovering); on anything else they recover and finalise, which guarantees the work stack makes progress under malformed input.</remarks>
    internal static bool CanStartTerm(TurtleTokenKind kind)
    {
        return kind is TurtleTokenKind.Iri or TurtleTokenKind.PrefixedName or TurtleTokenKind.PrefixNamespace
            or TurtleTokenKind.BlankNodeLabel or TurtleTokenKind.AnonymousBlankNode
            or TurtleTokenKind.StringLiteral or TurtleTokenKind.LongStringLiteral
            or TurtleTokenKind.IntegerLiteral or TurtleTokenKind.DecimalLiteral or TurtleTokenKind.DoubleLiteral
            or TurtleTokenKind.BooleanLiteral or TurtleTokenKind.A
            or TurtleTokenKind.OpenParen or TurtleTokenKind.OpenBracket
            or TurtleTokenKind.OpenTripleTerm or TurtleTokenKind.OpenReifiedTriple
            or TurtleTokenKind.Error;
    }

    /// <summary>Determines whether a token kind can begin a top-level statement: a directive keyword (<c>@prefix</c>/<c>@base</c>/<c>@version</c>), a TriG graph block (<c>GRAPH</c> or <c>{</c>), or a subject term.</summary>
    /// <param name="kind">The token kind to test.</param>
    /// <returns><see langword="true"/> when the kind begins a statement.</returns>
    /// <remarks>Anything else at statement position is stray and is skipped by StepStatement.</remarks>
    internal static bool CanStartStatement(TurtleTokenKind kind)
    {
        return kind is TurtleTokenKind.PrefixKeyword or TurtleTokenKind.BaseKeyword or TurtleTokenKind.VersionKeyword
            or TurtleTokenKind.GraphKeyword or TurtleTokenKind.OpenBrace
            || CanStartTerm(kind);
    }

    private static bool IsIriOrBlank(Term term)
    {
        return term is IriTerm or PrefixedNameTerm or BlankNodeTerm;
    }

    //The triple-term/reified-triple positional constraints are semantic: the term is structurally
    //complete but stands in a position the grammar forbids. There is nothing to resync past, so these
    //record a diagnostic and keep the faithful term (the emitter skips it; production callers refuse on
    //HasErrors). An ErrorTerm already carries its own diagnostic, so it is passed through silently.
    private void ValidateTripleTermSubject(Term subject)
    {
        //ttSubject ::= iri | BlankNode.
        if(subject is not ErrorTerm && !IsIriOrBlank(subject))
        {
            Report(WellKnownDiagnostics.Turtle.InvalidTripleTermSubject, subject.Span, "The subject of a triple term must be an IRI or a blank node.");
        }
    }

    private void ValidateTripleTermObject(Term objectTerm)
    {
        //ttObject ::= iri | BlankNode | literal | tripleTerm.
        if(objectTerm is not ErrorTerm && !IsIriOrBlank(objectTerm) && objectTerm is not (LiteralTerm or TripleTermTerm))
        {
            Report(WellKnownDiagnostics.Turtle.InvalidTripleTermObject, objectTerm.Span, "The object of a triple term must be an IRI, blank node, literal, or triple term.");
        }
    }

    private void ValidateReifiedTripleSubject(Term subject)
    {
        //rtSubject ::= iri | BlankNode | reifiedTriple.
        if(subject is not ErrorTerm && !IsIriOrBlank(subject) && subject is not ReifiedTripleTerm)
        {
            Report(WellKnownDiagnostics.Turtle.InvalidReifiedTripleSubject, subject.Span, "The subject of a reified triple must be an IRI, blank node, or reified triple.");
        }
    }

    private void ValidateReifiedTripleObject(Term objectTerm)
    {
        //rtObject ::= iri | BlankNode | literal | tripleTerm | reifiedTriple.
        if(objectTerm is not ErrorTerm && !IsIriOrBlank(objectTerm) && objectTerm is not (LiteralTerm or TripleTermTerm or ReifiedTripleTerm))
        {
            Report(WellKnownDiagnostics.Turtle.InvalidReifiedTripleObject, objectTerm.Span, "The object of a reified triple must be an IRI, blank node, literal, triple term, or reified triple.");
        }
    }

    private IriTerm ConsumeAKeyword()
    {
        SourceSpan span = Current.Span;
        Utf8String rdfType = Pool.Intern("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"u8);
        Advance();

        return Register(new IriTerm(NextNodeId(), span, rdfType));
    }

    private IriTerm ConsumeIri()
    {
        SourceSpan span = Current.Span;
        Utf8String value = Current.Value;
        Advance();

        return Register(new IriTerm(NextNodeId(), span, value));
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

        return Register(new PrefixedNameTerm(NextNodeId(), span, prefix, local));
    }

    private BlankNodeTerm ConsumeBlankNodeLabel()
    {
        SourceSpan span = Current.Span;
        Utf8String label = Current.Value;
        Advance();

        return Register(new BlankNodeTerm(NextNodeId(), span, label));
    }

    private BlankNodeTerm ConsumeAnonymousBlankNode()
    {
        SourceSpan span = Current.Span;
        Advance();

        Utf8String label = AllocateSyntheticBlankNodeLabel(span);

        return Register(new BlankNodeTerm(NextNodeId(), span, label));
    }

    private Term ParseStringLiteral()
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

                //The lexer only emits DirLangTag when it has matched the '--' separator, so its absence
                //here would be an internal invariant violation, not malformed input.
                int dirIndex = IndexOfDoubleDash(bytes);
                if(dirIndex < 0)
                {
                    throw new TurtleParseException(
                        "Direction-tagged language tag missing '--' separator.",
                        Current.Span);
                }

                language = Pool.Intern(bytes[..dirIndex]);
                ReadOnlySpan<byte> dirSpan = bytes[(dirIndex + 2)..];

                //A direction other than 'ltr'/'rtl' is recoverable: keep the language tag, drop the bad
                //direction, and record a diagnostic.
                TextDirection? parsedDirection = ParseDirection(dirSpan);
                if(parsedDirection is null)
                {
                    Report(WellKnownDiagnostics.Turtle.InvalidBaseDirection, Current.Span, "A base direction must be 'ltr' or 'rtl'.");
                }
                else
                {
                    direction = parsedDirection;
                }

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
                    case TurtleTokenKind.PrefixNamespace:
                    {
                        datatype = ConsumePrefixedName();
                        endSpan = datatype.Span;

                        break;
                    }

                    default:
                    {
                        return RecoverTerm(ParseFrameKind.Term, startSpan, WellKnownDiagnostics.Turtle.ExpectedDatatypeIri, Current.Span, "A datatype IRI was expected after '^^'.", "RDFLiteral");
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

        return Register(new LiteralTerm(NextNodeId(), totalSpan, value, datatype, language, direction));
    }

    private static TextDirection? ParseDirection(ReadOnlySpan<byte> directionBytes)
    {
        if(directionBytes.SequenceEqual("ltr"u8))
        {
            return TextDirection.Ltr;
        }

        if(directionBytes.SequenceEqual("rtl"u8))
        {
            return TextDirection.Rtl;
        }

        return null;
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

        Term datatype = Register(new IriTerm(NextNodeId(), span, datatypeIri));

        return Register(new LiteralTerm(NextNodeId(), span, text, datatype, language: null, direction: null));
    }

    private LiteralTerm ParseBooleanLiteral()
    {
        SourceSpan span = Current.Span;
        Utf8String text = Current.Value;
        Advance();

        Utf8String datatypeIri = Pool.Intern("http://www.w3.org/2001/XMLSchema#boolean"u8);
        Term datatype = Register(new IriTerm(NextNodeId(), span, datatypeIri));

        return Register(new LiteralTerm(NextNodeId(), span, text, datatype, language: null, direction: null));
    }

    private Utf8String AllocateSyntheticBlankNodeLabel(SourceSpan span)
    {
        BlankNodeRequest request = new(Guid.Empty, ReadOnlyMemory<byte>.Empty, span, Pool);

        return blankNodes(in request);
    }

    /// <summary>Allocates and returns the next AST node id, unique within the parse.</summary>
    /// <returns>A fresh node id.</returns>
    private int NextNodeId()
    {
        return nextNodeId++;
    }

    /// <summary>Records a freshly-constructed AST node under its own id and returns it, so a construction site reads <c>Register(new XxxTerm(NextNodeId(), ...))</c> with no per-node closure.</summary>
    /// <typeparam name="TNode">The node type.</typeparam>
    /// <param name="node">The node, constructed with an id drawn from <see cref="NextNodeId"/>.</param>
    /// <returns>The same node, for chaining at the construction site.</returns>
    private TNode Register<TNode>(TNode node)
        where TNode: TurtleAstNode
    {
        Nodes[node.NodeId] = node;

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

    //Records a recoverable parse diagnostic into the shared bag.
    private void Report(Utf8String code, SourceSpan span, string message)
    {
        Diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, span, Utf8Strings.From(message)));
    }

    //Records the diagnostic for a recoverable error and returns the codes to stamp on the error node —
    //unless the offending token is a lexer Error token, whose LX#### diagnostic the reader already
    //bridged into the bag. Re-reporting that would double-count, so the lexer's code stands alone and
    //the parser stays silent (the node still spans the offending run, correlating by span).
    private ImmutableArray<Utf8String> ReportRecoverable(Utf8String code, SourceSpan span, string message)
    {
        if(Current.Kind == TurtleTokenKind.Error)
        {
            return [];
        }

        Report(code, span, message);

        return [code];
    }

    //Skips tokens from the cursor — collecting them as the error node's trivia — until a token in the
    //frame's resync set (or end-of-input) is reached, which is left as the new cursor. The before==index
    //guard breaks when Advance cannot move (the cursor is clamped at the final token), so recovery from
    //any position terminates.
    private ImmutableArray<TurtleToken> ResyncTo(ParseFrameKind frameKind, SourceSpan startSpan, out SourceSpan lastSpan)
    {
        ImmutableArray<TurtleToken>.Builder skipped = ImmutableArray.CreateBuilder<TurtleToken>();
        lastSpan = startSpan;

        while(Current.Kind != TurtleTokenKind.EndOfInput && !IsResyncToken(frameKind, Current.Kind))
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

    //The resync set for each frame kind: the structural tokens at or after which the parser can resume
    //after skipping a malformed run. A term frame (default) resyncs to any separator or closer; the
    //bracketed forms resync to their own closer (plus the statement terminators for the triple forms,
    //which may be the next safe point if the closer is missing). End-of-input is always a stop, handled
    //by ResyncTo's loop condition.
    private static bool IsResyncToken(ParseFrameKind frameKind, TurtleTokenKind kind)
    {
        return frameKind switch
        {
            ParseFrameKind.Statement or ParseFrameKind.SubjectStatement => kind is TurtleTokenKind.Period or TurtleTokenKind.CloseBrace,
            ParseFrameKind.GraphBlock => kind is TurtleTokenKind.CloseBrace,
            ParseFrameKind.Collection => kind is TurtleTokenKind.CloseParen,
            ParseFrameKind.BlankNodePropertyList => kind is TurtleTokenKind.CloseBracket,
            ParseFrameKind.TripleTerm => kind is TurtleTokenKind.CloseTripleTerm or TurtleTokenKind.Period or TurtleTokenKind.CloseBrace,
            ParseFrameKind.ReifiedTriple => kind is TurtleTokenKind.CloseReifiedTriple or TurtleTokenKind.Period or TurtleTokenKind.CloseBrace,
            ParseFrameKind.AnnotationBlock => kind is TurtleTokenKind.CloseAnnotation,
            _ => kind is TurtleTokenKind.Period or TurtleTokenKind.Semicolon or TurtleTokenKind.Comma
                or TurtleTokenKind.CloseBracket or TurtleTokenKind.CloseParen or TurtleTokenKind.CloseBrace
                or TurtleTokenKind.CloseTripleTerm or TurtleTokenKind.CloseReifiedTriple or TurtleTokenKind.CloseAnnotation
        };
    }

    //Records the diagnostic, resyncs to the frame's resync set, and builds an ErrorTerm spanning the
    //failure→resync run. The node slots into any parent that expected a Term, so the existing value
    //flow carries it up — no multi-frame unwind.
    private ErrorTerm RecoverTerm(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<TurtleToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        return Register(new ErrorTerm(NextNodeId(), CombineSpans(startSpan, endSpan), Utf8Strings.From(expectedProduction), codes, skipped));
    }

    //As RecoverTerm, for a broken statement; it also consumes a trailing '.' terminator (but leaves a
    //'}', which the enclosing graph-block frame closes), so the next statement dispatch starts cleanly.
    private ErrorStatement RecoverStatement(ParseFrameKind frameKind, SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<TurtleToken> skipped = ResyncTo(frameKind, errorSpan, out SourceSpan endSpan);

        if(Current.Kind == TurtleTokenKind.Period)
        {
            endSpan = Current.Span;
            Advance();
        }

        return Register(new ErrorStatement(NextNodeId(), CombineSpans(startSpan, endSpan), Utf8Strings.From(expectedProduction), codes, skipped));
    }

    //As RecoverTerm, for a broken annotation; it consumes a trailing '|}' close so the annotated-object
    //frame resumes at the next object or terminator.
    private ErrorAnnotation RecoverAnnotation(SourceSpan startSpan, Utf8String code, SourceSpan errorSpan, string message, string expectedProduction)
    {
        ImmutableArray<Utf8String> codes = ReportRecoverable(code, errorSpan, message);
        ImmutableArray<TurtleToken> skipped = ResyncTo(ParseFrameKind.AnnotationBlock, errorSpan, out SourceSpan endSpan);

        if(Current.Kind == TurtleTokenKind.CloseAnnotation)
        {
            endSpan = Current.Span;
            Advance();
        }

        return Register(new ErrorAnnotation(NextNodeId(), CombineSpans(startSpan, endSpan), Utf8Strings.From(expectedProduction), codes, skipped));
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
