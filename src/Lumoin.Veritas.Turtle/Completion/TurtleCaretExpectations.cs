using System.Collections.Immutable;
using Lumoin.Veritas.Turtle.Lexer;
using Lumoin.Veritas.Turtle.Parser;

namespace Lumoin.Veritas.Turtle.Completion;

/// <summary>
/// Maps an open Turtle parse frame's <c>(production, stage)</c> at a caret to the token kinds the grammar
/// admits next there, reusing the productions' FIRST sets. The mapping covers the positions a caret most
/// often sits in — a statement's subject / verb / object / continuation, the <c>;</c> position, a collection,
/// a blank-node property list, a TriG graph block, and the components of a triple term. A frame position not yet mapped yields an empty set: the consumer then
/// offers no grammar hint there (only its vocabulary), so an unmapped position is never wrong, only quiet.
/// </summary>
public static class TurtleCaretExpectations
{
    /// <summary>The directive keywords that open a statement: <c>@prefix</c>/<c>PREFIX</c>, <c>@base</c>/<c>BASE</c>, <c>@version</c>/<c>VERSION</c>.</summary>
    private static ImmutableArray<TurtleTokenKind> DirectiveStart { get; } =
    [
        TurtleTokenKind.PrefixKeyword,
        TurtleTokenKind.BaseKeyword,
        TurtleTokenKind.VersionKeyword,
    ];

    /// <summary>The FIRST set of a subject term: an IRI, prefixed name, blank node, collection, blank-node property list, triple term, or reified triple. A literal cannot be a subject.</summary>
    public static ImmutableArray<TurtleTokenKind> SubjectStart { get; } =
    [
        TurtleTokenKind.Iri,
        TurtleTokenKind.PrefixedName,
        TurtleTokenKind.BlankNodeLabel,
        TurtleTokenKind.AnonymousBlankNode,
        TurtleTokenKind.OpenParen,
        TurtleTokenKind.OpenBracket,
        TurtleTokenKind.OpenTripleTerm,
        TurtleTokenKind.OpenReifiedTriple,
    ];

    /// <summary>What may open a statement: a directive keyword or a subject term. The set at a statement boundary, where no frame is open.</summary>
    public static ImmutableArray<TurtleTokenKind> StatementStart { get; } = DirectiveStart.AddRange(SubjectStart);

    /// <summary>What may open a statement in TriG: everything <see cref="StatementStart"/> admits, plus a graph block — the <c>GRAPH</c> keyword or an anonymous block <c>{</c>.</summary>
    public static ImmutableArray<TurtleTokenKind> TriGStatementStart { get; } = StatementStart.Add(TurtleTokenKind.GraphKeyword).Add(TurtleTokenKind.OpenBrace);

    /// <summary>The FIRST set of a verb (predicate): the <c>a</c> shorthand, an IRI, or a prefixed name.</summary>
    public static ImmutableArray<TurtleTokenKind> VerbStart { get; } =
    [
        TurtleTokenKind.A,
        TurtleTokenKind.Iri,
        TurtleTokenKind.PrefixedName,
    ];

    /// <summary>The FIRST set of an object term: the subject terms plus the literal forms (an object may be a literal).</summary>
    public static ImmutableArray<TurtleTokenKind> ObjectStart { get; } =
    [
        TurtleTokenKind.Iri,
        TurtleTokenKind.PrefixedName,
        TurtleTokenKind.BlankNodeLabel,
        TurtleTokenKind.AnonymousBlankNode,
        TurtleTokenKind.StringLiteral,
        TurtleTokenKind.LongStringLiteral,
        TurtleTokenKind.IntegerLiteral,
        TurtleTokenKind.DecimalLiteral,
        TurtleTokenKind.DoubleLiteral,
        TurtleTokenKind.BooleanLiteral,
        TurtleTokenKind.OpenParen,
        TurtleTokenKind.OpenBracket,
        TurtleTokenKind.OpenTripleTerm,
        TurtleTokenKind.OpenReifiedTriple,
    ];

    /// <summary>What may follow a complete object: an RDF 1.2 annotation (<c>~</c> or <c>{|</c>), another object (<c>,</c>), another predicate-object pair (<c>;</c>), or the statement terminator (<c>.</c>).</summary>
    private static ImmutableArray<TurtleTokenKind> ObjectContinuation { get; } =
    [
        TurtleTokenKind.Tilde,
        TurtleTokenKind.OpenAnnotation,
        TurtleTokenKind.Comma,
        TurtleTokenKind.Semicolon,
        TurtleTokenKind.Period,
    ];

    /// <summary>After a <c>;</c> in a subject statement: another predicate (a verb), or the statement terminator <c>.</c>.</summary>
    private static ImmutableArray<TurtleTokenKind> VerbOrTerminator { get; } = VerbStart.Add(TurtleTokenKind.Period);

    /// <summary>A collection-item position inside <c>( … )</c>: an object term, or the closing parenthesis.</summary>
    private static ImmutableArray<TurtleTokenKind> CollectionItemOrClose { get; } = ObjectStart.Add(TurtleTokenKind.CloseParen);

    /// <summary>A blank-node-property-list verb position inside <c>[ … ]</c>: a verb, or the closing bracket.</summary>
    private static ImmutableArray<TurtleTokenKind> BlankNodeVerbOrClose { get; } = VerbStart.Add(TurtleTokenKind.CloseBracket);

    /// <summary>A triple-statement position inside a TriG graph block <c>{ … }</c>: a triple subject, or the closing brace. Only triples are admitted in a graph block, not directives.</summary>
    private static ImmutableArray<TurtleTokenKind> GraphBlockContent { get; } = SubjectStart.Add(TurtleTokenKind.CloseBrace);

    /// <summary>The tokens that begin a named term — an IRI or a prefixed name. Valid wherever a term or a verb is admitted (a subject, an object, or a predicate), so it is the sound, vocabulary-bearing set for a bare term position whose role is not fixed by the frame alone — the components of a triple term or reified triple, and the post-<c>;</c> position inside a blank-node property list.</summary>
    public static ImmutableArray<TurtleTokenKind> NamedTermStart { get; } = [TurtleTokenKind.Iri, TurtleTokenKind.PrefixedName];

    /// <summary>
    /// Returns the token kinds the grammar admits at a caret in the given open production at the given stage,
    /// in suggestion order. Returns an empty set for a position not yet mapped.
    /// </summary>
    /// <param name="production">The innermost open production at the caret.</param>
    /// <param name="stage">The sub-stage that production is suspended at.</param>
    /// <returns>The expected token kinds, or an empty set when the position is unmapped.</returns>
    public static ImmutableArray<TurtleTokenKind> ExpectedTokensAt(ParseFrameKind production, int stage)
        => (production, stage) switch
        {
            (ParseFrameKind.SubjectStatement, 1) => VerbStart,
            (ParseFrameKind.SubjectStatement, 2) => VerbOrTerminator,
            (ParseFrameKind.ObjectList, 0) => ObjectStart,
            (ParseFrameKind.AnnotatedObject, 0) => ObjectStart,
            (ParseFrameKind.AnnotatedObject, 1) => ObjectContinuation,
            (ParseFrameKind.Collection, 0) => CollectionItemOrClose,
            (ParseFrameKind.BlankNodePropertyList, 0) => BlankNodeVerbOrClose,
            (ParseFrameKind.GraphBlock, 1) => GraphBlockContent,
            (ParseFrameKind.Term, 0) => NamedTermStart,
            (ParseFrameKind.TripleTerm, 0) => NamedTermStart,
            (ParseFrameKind.ReifiedTriple, 0) => NamedTermStart,
            _ => []
        };
}
