using System.Collections.Immutable;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;

namespace Lumoin.Veritas.Sparql.Completion;

/// <summary>
/// The grammar's FIRST and FOLLOW token sets, exposed as ordered <see cref="SparqlTokenKind"/> collections
/// for caret-aware completion. Each FIRST property names the token kinds that may begin one production;
/// <see cref="ResyncTokens(ParseFrameKind)"/> gives the FOLLOW / resync set that closes or separates a frame
/// of each kind.
/// </summary>
/// <remarks>
/// <para>
/// These sets are a declarative mirror of the parser's hot-path predicates: the FIRST properties mirror the
/// <c>SparqlParser.CanStart*</c> predicates and <see cref="ResyncTokens(ParseFrameKind)"/> mirrors its
/// <c>IsResyncToken</c> switch. The two encodings coexist on purpose — the boolean predicates stay the fast
/// <c>kind is X or Y</c> form the driver evaluates per token, while these sets are the enumerable form a
/// completion engine surfaces to an editor. An equivalence test (<c>SparqlExpectedTokensTests</c>) asserts
/// each set agrees with its predicate over every token kind, so the two cannot drift apart unnoticed.
/// </para>
/// <para>
/// The collections preserve grammar order and contain no duplicates, so a consumer may concatenate several
/// FIRST sets to describe a position that admits more than one production.
/// </para>
/// </remarks>
public static class SparqlExpectedTokens
{
    /// <summary>
    /// The FIRST set of a triple's subject term: a variable, an IRI or prefixed name, a labelled or
    /// anonymous blank node, a literal, or a token that opens a collection, blank-node property list,
    /// triple term, or reified triple. Mirrors <c>SparqlParser.CanStartTriple</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> TripleStart { get; } =
    [
        SparqlTokenKind.Variable,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
        SparqlTokenKind.BlankNodeLabel,
        SparqlTokenKind.AnonymousBlankNode,
        SparqlTokenKind.StringLiteral,
        SparqlTokenKind.LongStringLiteral,
        SparqlTokenKind.IntegerLiteral,
        SparqlTokenKind.DecimalLiteral,
        SparqlTokenKind.DoubleLiteral,
        SparqlTokenKind.BooleanLiteral,
        SparqlTokenKind.OpenParen,
        SparqlTokenKind.OpenBracket,
        SparqlTokenKind.OpenTripleTerm,
        SparqlTokenKind.OpenReifiedTriple,
    ];

    /// <summary>
    /// The FIRST set of a verb (predicate): the <c>a</c> shorthand, an IRI or prefixed name, a variable,
    /// or a token that opens a property path (the inverse <c>^</c>, the negated set <c>!</c>, or a grouped
    /// path <c>(</c>). Mirrors <c>SparqlParser.CanStartVerb</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> VerbStart { get; } =
    [
        SparqlTokenKind.A,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
        SparqlTokenKind.Variable,
        SparqlTokenKind.Caret,
        SparqlTokenKind.Bang,
        SparqlTokenKind.OpenParen,
    ];

    /// <summary>
    /// The FIRST set of a property path: an IRI, the <c>a</c> shorthand, or a prefixed name; the inverse
    /// <c>^</c>; the negated set <c>!</c>; or a grouped path <c>(</c>. Mirrors
    /// <c>SparqlParser.CanStartPath</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> PathStart { get; } =
    [
        SparqlTokenKind.Iri,
        SparqlTokenKind.A,
        SparqlTokenKind.PrefixedName,
        SparqlTokenKind.Caret,
        SparqlTokenKind.Bang,
        SparqlTokenKind.OpenParen,
    ];

    /// <summary>
    /// The FIRST set of a compound term: a collection <c>(</c>, a blank-node property list <c>[</c>, a
    /// triple term <c>&lt;&lt;(</c>, or a reified triple <c>&lt;&lt;</c>. Mirrors
    /// <c>SparqlParser.CanStartCompoundTerm</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> CompoundTermStart { get; } =
    [
        SparqlTokenKind.OpenParen,
        SparqlTokenKind.OpenBracket,
        SparqlTokenKind.OpenTripleTerm,
        SparqlTokenKind.OpenReifiedTriple,
    ];

    /// <summary>
    /// The FIRST set of a reifier identity after <c>~</c>: an IRI or prefixed name, a variable, or a
    /// labelled or anonymous blank node. Mirrors <c>SparqlParser.CanStartReifierId</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> ReifierIdStart { get; } =
    [
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
        SparqlTokenKind.Variable,
        SparqlTokenKind.BlankNodeLabel,
        SparqlTokenKind.AnonymousBlankNode,
    ];

    /// <summary>
    /// The FIRST set of a <c>VALUES</c> data-block value: the <c>UNDEF</c> keyword, an IRI or prefixed
    /// name, a literal, or a triple term <c>&lt;&lt;(</c>. Mirrors
    /// <c>SparqlParser.CanStartDataBlockValue</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> DataBlockValueStart { get; } =
    [
        SparqlTokenKind.UndefKeyword,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
        SparqlTokenKind.StringLiteral,
        SparqlTokenKind.LongStringLiteral,
        SparqlTokenKind.IntegerLiteral,
        SparqlTokenKind.DecimalLiteral,
        SparqlTokenKind.DoubleLiteral,
        SparqlTokenKind.BooleanLiteral,
        SparqlTokenKind.OpenTripleTerm,
    ];

    /// <summary>
    /// The FIRST set of a bare expression condition — a <c>FILTER</c> constraint written without the
    /// surrounding parentheses: a built-in or aggregate function name, or an IRI or prefixed name naming a
    /// function. Mirrors <c>SparqlParser.CanStartBareExpressionCondition</c>.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> BareExpressionConditionStart { get; } =
    [
        SparqlTokenKind.BuiltInFunctionName,
        SparqlTokenKind.AggregateFunctionName,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
    ];

    /// <summary>
    /// The keyword and brace tokens — beyond a triple subject — that begin a group-graph-pattern member: a
    /// nested group <c>{</c>, <c>OPTIONAL</c>, <c>MINUS</c>, <c>GRAPH</c>, <c>SERVICE</c>, <c>FILTER</c>,
    /// <c>BIND</c>, and <c>VALUES</c>. Concatenated with <see cref="TripleStart"/> to build
    /// <see cref="GroupMemberStart"/>, so the triple-subject tokens have a single source.
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> GroupMemberKeywords { get; } =
    [
        SparqlTokenKind.OpenBrace,
        SparqlTokenKind.OptionalKeyword,
        SparqlTokenKind.MinusKeyword,
        SparqlTokenKind.GraphKeyword,
        SparqlTokenKind.ServiceKeyword,
        SparqlTokenKind.FilterKeyword,
        SparqlTokenKind.BindKeyword,
        SparqlTokenKind.ValuesKeyword,
    ];

    /// <summary>
    /// The FIRST set of a group-graph-pattern member: any triple subject (<see cref="TripleStart"/>) plus
    /// the member keywords and the nested-group brace (<see cref="GroupMemberKeywords"/>). Mirrors
    /// <c>SparqlParser.CanStartGroupMember</c>, which tests <c>CanStartTriple(kind)</c> unioned with those
    /// keywords.
    /// </summary>
    public static ImmutableArray<SparqlTokenKind> GroupMemberStart { get; } = TripleStart.AddRange(GroupMemberKeywords);

    /// <summary>
    /// The FOLLOW / resync set of a parse frame of the given kind: the token kinds at which the parser
    /// stops skipping when it recovers from an error inside that frame, which double as the tokens that
    /// legitimately close or separate the frame. Mirrors <c>SparqlParser.IsResyncToken</c>; an unlisted
    /// frame kind falls to the broad default set of closers and separators.
    /// </summary>
    /// <param name="frameKind">The production whose enclosing frame is open at the caret.</param>
    /// <returns>The resync tokens for that frame kind, in declaration order.</returns>
    public static ImmutableArray<SparqlTokenKind> ResyncTokens(ParseFrameKind frameKind)
        => frameKind switch
        {
            ParseFrameKind.Request or ParseFrameKind.SelectClause
                => [SparqlTokenKind.Period, SparqlTokenKind.CloseBrace],
            ParseFrameKind.GroupGraphPattern or ParseFrameKind.UnionPattern or ParseFrameKind.OptionalPattern
                or ParseFrameKind.MinusPattern or ParseFrameKind.GraphPattern or ParseFrameKind.ServicePattern
                or ParseFrameKind.SubSelect or ParseFrameKind.ConstructTemplate
                => [SparqlTokenKind.CloseBrace],
            ParseFrameKind.Triple
                => [SparqlTokenKind.Period, SparqlTokenKind.Semicolon, SparqlTokenKind.CloseBrace],
            ParseFrameKind.Collection
                => [SparqlTokenKind.CloseParen],
            ParseFrameKind.BlankNodePropertyList
                => [SparqlTokenKind.CloseBracket],
            ParseFrameKind.TripleTerm
                => [SparqlTokenKind.CloseTripleTerm, SparqlTokenKind.Period, SparqlTokenKind.CloseBrace],
            ParseFrameKind.ReifiedTriple
                => [SparqlTokenKind.CloseReifiedTriple, SparqlTokenKind.Period, SparqlTokenKind.CloseBrace],
            ParseFrameKind.AnnotationBlock
                => [SparqlTokenKind.CloseAnnotation],
            ParseFrameKind.Expression or ParseFrameKind.ArgumentList
                => [SparqlTokenKind.CloseParen, SparqlTokenKind.Comma],
            ParseFrameKind.PropertyPath or ParseFrameKind.PathSequence or ParseFrameKind.PathElement
                or ParseFrameKind.PathNegatedSet
                => [SparqlTokenKind.CloseParen, SparqlTokenKind.CloseBrace, SparqlTokenKind.Period, SparqlTokenKind.Semicolon],
            ParseFrameKind.Values
                => [SparqlTokenKind.CloseBrace, SparqlTokenKind.CloseParen],
            ParseFrameKind.GroupBy or ParseFrameKind.Having or ParseFrameKind.OrderBy
                or ParseFrameKind.Filter or ParseFrameKind.Bind
                => [SparqlTokenKind.CloseBrace, SparqlTokenKind.Period],
            _ => [SparqlTokenKind.Period, SparqlTokenKind.Semicolon, SparqlTokenKind.Comma,
                SparqlTokenKind.CloseBracket, SparqlTokenKind.CloseParen, SparqlTokenKind.CloseBrace,
                SparqlTokenKind.CloseTripleTerm, SparqlTokenKind.CloseReifiedTriple, SparqlTokenKind.CloseAnnotation],
        };
}
