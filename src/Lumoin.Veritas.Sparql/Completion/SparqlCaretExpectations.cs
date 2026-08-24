using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Lumoin.Veritas.Sparql.Lexer;
using Lumoin.Veritas.Sparql.Parser;

namespace Lumoin.Veritas.Sparql.Completion;

/// <summary>
/// Maps an open parse frame's <c>(production, stage)</c> at a caret to the token kinds the grammar admits
/// next there, and answers the same question for a whole open-frame chain through
/// <see cref="ExpectedTokensAcross(IReadOnlyList{ValueTuple{ParseFrameKind, int}})"/>. The term and member
/// positions reuse the FIRST sets in <see cref="SparqlExpectedTokens"/>; the clause-keyword and expression
/// positions use sets derived from the corresponding <c>SparqlParser</c> step methods (each set names the
/// tokens for which that step makes progress rather than recovering).
/// </summary>
/// <remarks>
/// <para>
/// The mapping covers the productions a caret most often sits in — the request head, query forms and
/// solution modifiers; the SELECT projection list; the group-graph-pattern members (including those reached
/// through <c>GRAPH</c>/<c>OPTIONAL</c>/<c>MINUS</c>/<c>SERVICE</c>); a triple's subject/verb/object/
/// continuation and a property-path continuation; an expression and the <c>ORDER BY</c>/<c>GROUP BY</c>/
/// <c>HAVING</c> conditions; a <c>VALUES</c> data block, a collection, and a blank-node property list; and
/// the <c>CONSTRUCT</c> template, the <c>DESCRIBE</c> targets, and the update quad blocks. A frame position
/// not yet mapped yields an empty set: the consumer then offers no keyword hint for that position (it still
/// has the in-scope variables and the enclosing-production chain), so an unmapped position is never wrong,
/// only quiet. Coverage is expanded production by production.
/// </para>
/// <para>
/// The innermost production alone does not fix the answer. A repetition that has met its minimum may close
/// at the caret, and then whatever follows the production it closes into is admissible too — after
/// <c>SELECT ?s ?p ?o</c> the list may take another projection or end, so a dataset clause and the
/// <c>WHERE</c> opener stand beside the projection starts. <see cref="CompletesAt(ParseFrameKind, int)"/>
/// names the positions where a production may end, and the chain walk unions the enclosing continuations in
/// while that holds.
/// </para>
/// </remarks>
public static class SparqlCaretExpectations
{
    /// <summary>The prologue declaration keywords that may open a request: <c>BASE</c>, <c>PREFIX</c>, <c>VERSION</c>.</summary>
    private static ImmutableArray<SparqlTokenKind> PrologueStart { get; } =
    [
        SparqlTokenKind.BaseKeyword,
        SparqlTokenKind.PrefixKeyword,
        SparqlTokenKind.VersionKeyword,
    ];

    /// <summary>The query-form keywords: <c>SELECT</c>, <c>CONSTRUCT</c>, <c>ASK</c>, <c>DESCRIBE</c>.</summary>
    private static ImmutableArray<SparqlTokenKind> QueryFormStart { get; } =
    [
        SparqlTokenKind.SelectKeyword,
        SparqlTokenKind.ConstructKeyword,
        SparqlTokenKind.AskKeyword,
        SparqlTokenKind.DescribeKeyword,
    ];

    /// <summary>The update-operation keywords (mirrors <c>SparqlParser.IsUpdateOperationStart</c>).</summary>
    private static ImmutableArray<SparqlTokenKind> UpdateOperationStart { get; } =
    [
        SparqlTokenKind.InsertKeyword,
        SparqlTokenKind.DeleteKeyword,
        SparqlTokenKind.WithKeyword,
        SparqlTokenKind.LoadKeyword,
        SparqlTokenKind.ClearKeyword,
        SparqlTokenKind.DropKeyword,
        SparqlTokenKind.CreateKeyword,
        SparqlTokenKind.AddKeyword,
        SparqlTokenKind.MoveKeyword,
        SparqlTokenKind.CopyKeyword,
    ];

    /// <summary>The forms and update operations admitted once the prologue is parsed.</summary>
    private static ImmutableArray<SparqlTokenKind> FormOrUpdateStart { get; } = QueryFormStart.AddRange(UpdateOperationStart);

    /// <summary>The whole start-of-request set: a prologue declaration, a query form, or an update operation.</summary>
    private static ImmutableArray<SparqlTokenKind> RequestStart { get; } = PrologueStart.AddRange(FormOrUpdateStart);

    /// <summary>What follows <c>SELECT</c>: the <c>DISTINCT</c>/<c>REDUCED</c> modifier, the <c>*</c> form, or the first projection.</summary>
    private static ImmutableArray<SparqlTokenKind> SelectHeadStart { get; } =
    [
        SparqlTokenKind.DistinctKeyword,
        SparqlTokenKind.ReducedKeyword,
        SparqlTokenKind.Star,
        SparqlTokenKind.Variable,
        SparqlTokenKind.OpenParen,
    ];

    /// <summary>A projection in the SELECT list: a bare variable, or the <c>(</c> of an <c>(expr AS ?var)</c> projection.</summary>
    private static ImmutableArray<SparqlTokenKind> SelectProjectionStart { get; } =
    [
        SparqlTokenKind.Variable,
        SparqlTokenKind.OpenParen,
    ];

    /// <summary>After the form head: a <c>FROM</c> dataset clause, the <c>WHERE</c> keyword, or the elided-WHERE opening brace.</summary>
    private static ImmutableArray<SparqlTokenKind> DatasetOrWhereStart { get; } =
    [
        SparqlTokenKind.FromKeyword,
        SparqlTokenKind.WhereKeyword,
        SparqlTokenKind.OpenBrace,
    ];

    /// <summary>The <c>WHERE</c> clause opener: the <c>WHERE</c> keyword or the elided-WHERE opening brace.</summary>
    private static ImmutableArray<SparqlTokenKind> WhereStart { get; } =
    [
        SparqlTokenKind.WhereKeyword,
        SparqlTokenKind.OpenBrace,
    ];

    /// <summary>Every solution modifier, in grammar order: <c>GROUP BY</c>, <c>HAVING</c>, <c>ORDER BY</c>, <c>LIMIT</c>, <c>OFFSET</c>, trailing <c>VALUES</c>.</summary>
    private static ImmutableArray<SparqlTokenKind> SolutionModifierStart { get; } =
    [
        SparqlTokenKind.GroupKeyword,
        SparqlTokenKind.HavingKeyword,
        SparqlTokenKind.OrderKeyword,
        SparqlTokenKind.LimitKeyword,
        SparqlTokenKind.OffsetKeyword,
        SparqlTokenKind.ValuesKeyword,
    ];

    /// <summary>The solution modifiers that may still follow once <c>GROUP BY</c> is past: <c>HAVING</c> onward.</summary>
    private static ImmutableArray<SparqlTokenKind> SolutionModifierFromHaving { get; } =
    [
        SparqlTokenKind.HavingKeyword,
        SparqlTokenKind.OrderKeyword,
        SparqlTokenKind.LimitKeyword,
        SparqlTokenKind.OffsetKeyword,
        SparqlTokenKind.ValuesKeyword,
    ];

    /// <summary>The solution modifiers that may still follow once <c>HAVING</c> is past: <c>ORDER BY</c> onward.</summary>
    private static ImmutableArray<SparqlTokenKind> SolutionModifierFromOrder { get; } =
    [
        SparqlTokenKind.OrderKeyword,
        SparqlTokenKind.LimitKeyword,
        SparqlTokenKind.OffsetKeyword,
        SparqlTokenKind.ValuesKeyword,
    ];

    /// <summary>The slice clauses and trailing data block reachable after <c>ORDER BY</c>: <c>LIMIT</c>, <c>OFFSET</c>, <c>VALUES</c>.</summary>
    private static ImmutableArray<SparqlTokenKind> SliceOrValues { get; } =
    [
        SparqlTokenKind.LimitKeyword,
        SparqlTokenKind.OffsetKeyword,
        SparqlTokenKind.ValuesKeyword,
    ];

    /// <summary>The trailing <c>VALUES</c> data block, the only construct left at the end of a request.</summary>
    private static ImmutableArray<SparqlTokenKind> TrailingValues { get; } = [SparqlTokenKind.ValuesKeyword];

    /// <summary>The token that closes a group graph pattern.</summary>
    private static ImmutableArray<SparqlTokenKind> CloseGroup { get; } = [SparqlTokenKind.CloseBrace];

    /// <summary>A group-graph-pattern member position: any member start, or the closing brace.</summary>
    private static ImmutableArray<SparqlTokenKind> GroupMemberOrClose { get; } = SparqlExpectedTokens.GroupMemberStart.Add(SparqlTokenKind.CloseBrace);

    /// <summary>
    /// A triple's continuation after an object: an RDF 1.2 annotation (<c>~</c> or <c>{|</c>), another object
    /// (<c>,</c>), another predicate-object list (<c>;</c>), the triple terminator (<c>.</c>), or the group
    /// closer (<c>}</c>).
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> TripleContinuation { get; } =
    [
        SparqlTokenKind.Tilde,
        SparqlTokenKind.OpenAnnotation,
        SparqlTokenKind.Comma,
        SparqlTokenKind.Semicolon,
        SparqlTokenKind.Period,
        SparqlTokenKind.CloseBrace,
    ];

    /// <summary>
    /// The first token of an expression: a leading unary operator (<c>!</c>, <c>+</c>, <c>-</c>) or an
    /// expression primary — a variable, a literal, an IRI or prefixed name, a built-in or aggregate function
    /// name, <c>EXISTS</c> / <c>NOT</c> (<c>NOT EXISTS</c>), a bracketed expression <c>(</c>, or a triple
    /// term <c>&lt;&lt;(</c>.
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> ExpressionStart { get; } =
    [
        SparqlTokenKind.Bang,
        SparqlTokenKind.Plus,
        SparqlTokenKind.Minus,
        SparqlTokenKind.Variable,
        SparqlTokenKind.StringLiteral,
        SparqlTokenKind.LongStringLiteral,
        SparqlTokenKind.IntegerLiteral,
        SparqlTokenKind.DecimalLiteral,
        SparqlTokenKind.DoubleLiteral,
        SparqlTokenKind.BooleanLiteral,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
        SparqlTokenKind.BuiltInFunctionName,
        SparqlTokenKind.AggregateFunctionName,
        SparqlTokenKind.ExistsKeyword,
        SparqlTokenKind.NotKeyword,
        SparqlTokenKind.OpenParen,
        SparqlTokenKind.OpenTripleTerm,
    ];

    /// <summary>
    /// What may continue an expression after an operand: a binary operator, or an <c>IN</c> / <c>NOT IN</c>
    /// membership test. The expression may also simply end here, so these are the tokens that extend it.
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> ExpressionContinue { get; } =
    [
        SparqlTokenKind.LogicalOr,
        SparqlTokenKind.LogicalAnd,
        SparqlTokenKind.Equals,
        SparqlTokenKind.NotEquals,
        SparqlTokenKind.LessThan,
        SparqlTokenKind.LessOrEqual,
        SparqlTokenKind.GreaterThan,
        SparqlTokenKind.GreaterOrEqual,
        SparqlTokenKind.Plus,
        SparqlTokenKind.Minus,
        SparqlTokenKind.Star,
        SparqlTokenKind.Slash,
        SparqlTokenKind.InKeyword,
        SparqlTokenKind.NotKeyword,
    ];

    /// <summary>An <c>ORDER BY</c> condition: <c>ASC</c>/<c>DESC</c>, a bare variable, a parenthesised expression, or a bare built-in / function / IRI expression.</summary>
    private static ImmutableArray<SparqlTokenKind> OrderConditionStart { get; } =
    [
        SparqlTokenKind.AscKeyword,
        SparqlTokenKind.DescKeyword,
        SparqlTokenKind.Variable,
        SparqlTokenKind.OpenParen,
        SparqlTokenKind.BuiltInFunctionName,
        SparqlTokenKind.AggregateFunctionName,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
    ];

    /// <summary>A <c>GROUP BY</c> condition: a bare variable, a parenthesised (optionally <c>AS</c>-bound) expression, or a bare built-in / function / IRI expression.</summary>
    private static ImmutableArray<SparqlTokenKind> GroupConditionStart { get; } =
    [
        SparqlTokenKind.Variable,
        SparqlTokenKind.OpenParen,
        SparqlTokenKind.BuiltInFunctionName,
        SparqlTokenKind.AggregateFunctionName,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
    ];

    /// <summary>A <c>HAVING</c> constraint: a parenthesised expression, or a bare built-in / function / IRI expression.</summary>
    private static ImmutableArray<SparqlTokenKind> HavingConstraintStart { get; } =
    [
        SparqlTokenKind.OpenParen,
        SparqlTokenKind.BuiltInFunctionName,
        SparqlTokenKind.AggregateFunctionName,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
    ];

    /// <summary>The operators that extend a property path after an element: the sequence <c>/</c> and the alternative <c>|</c>.</summary>
    private static ImmutableArray<SparqlTokenKind> PathContinuationOperators { get; } =
    [
        SparqlTokenKind.Slash,
        SparqlTokenKind.Pipe,
    ];

    /// <summary>After a path element: the object that ends the verb (a <c>VarOrTerm</c>), or a <c>/</c> / <c>|</c> that extends the path.</summary>
    private static ImmutableArray<SparqlTokenKind> PathContinuationOrObject { get; } = SparqlExpectedTokens.TripleStart.AddRange(PathContinuationOperators);

    /// <summary>A <c>VALUES</c> data-block position: a data value (<see cref="SparqlExpectedTokens.DataBlockValueStart"/>) or the closing brace.</summary>
    private static ImmutableArray<SparqlTokenKind> ValuesDataValueOrClose { get; } = SparqlExpectedTokens.DataBlockValueStart.Add(SparqlTokenKind.CloseBrace);

    /// <summary>A collection-item position inside <c>( … )</c>: any term that begins an item (<see cref="SparqlExpectedTokens.TripleStart"/>) or the closing parenthesis.</summary>
    private static ImmutableArray<SparqlTokenKind> CollectionItemOrClose { get; } = SparqlExpectedTokens.TripleStart.Add(SparqlTokenKind.CloseParen);

    /// <summary>A blank-node-property-list verb position inside <c>[ … ]</c>: a verb (<see cref="SparqlExpectedTokens.VerbStart"/>) or the closing bracket.</summary>
    private static ImmutableArray<SparqlTokenKind> BlankNodeVerbOrClose { get; } = SparqlExpectedTokens.VerbStart.Add(SparqlTokenKind.CloseBracket);

    /// <summary>A <c>CONSTRUCT</c>-template member: a triple subject (<see cref="SparqlExpectedTokens.TripleStart"/>) or the closing brace.</summary>
    private static ImmutableArray<SparqlTokenKind> ConstructTemplateMember { get; } = SparqlExpectedTokens.TripleStart.Add(SparqlTokenKind.CloseBrace);

    /// <summary>A <c>DESCRIBE</c> target: the <c>*</c> wildcard, a variable, or an IRI / prefixed name.</summary>
    private static ImmutableArray<SparqlTokenKind> DescribeTargetStart { get; } =
    [
        SparqlTokenKind.Star,
        SparqlTokenKind.Variable,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
    ];

    /// <summary>
    /// A further <c>DESCRIBE</c> target once the list holds one: a variable or an IRI / prefixed name. The
    /// <c>*</c> wildcard is absent — <c>( VarOrIri+ | '*' )</c> offers it only as the whole alternative, so
    /// choosing the target list spends it.
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> DescribeTargetContinue { get; } =
    [
        SparqlTokenKind.Variable,
        SparqlTokenKind.Iri,
        SparqlTokenKind.PrefixedName,
    ];

    /// <summary>
    /// What follows a complete <c>DESCRIBE</c> target list: a <c>FROM</c> dataset clause and the
    /// <c>WHERE</c> opener, and — because <c>DESCRIBE</c> is the one form whose <c>WhereClause</c> is
    /// optional — every solution modifier and the trailing <c>VALUES</c> block, each reachable by skipping
    /// the clause before it.
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> DescribeTail { get; } = DatasetOrWhereStart.AddRange(SolutionModifierStart);

    /// <summary>A satisfied <c>DESCRIBE</c> target list: another target, or the tail the completed list opens onto.</summary>
    private static ImmutableArray<SparqlTokenKind> DescribeTargetOrTail { get; } = DescribeTargetContinue.AddRange(DescribeTail);

    /// <summary>A quad-block position (<c>INSERT</c>/<c>DELETE DATA</c>, <c>DELETE WHERE</c>): a triple (<see cref="SparqlExpectedTokens.TripleStart"/>), a <c>GRAPH</c> block, or the closing brace.</summary>
    private static ImmutableArray<SparqlTokenKind> QuadBlockStart { get; } = SparqlExpectedTokens.TripleStart.Add(SparqlTokenKind.GraphKeyword).Add(SparqlTokenKind.CloseBrace);

    /// <summary>
    /// What follows a completed update operation: the <c>;</c> that separates it from the next one, the
    /// only token an update unit admits between operations.
    /// </summary>
    private static ImmutableArray<SparqlTokenKind> UpdateOperationSeparator { get; } = [SparqlTokenKind.Semicolon];

    /// <summary>The tokens that begin a named term — an IRI or a prefixed name. The whole admissible set at an RDF literal's datatype position, directly after <c>^^</c>.</summary>
    public static ImmutableArray<SparqlTokenKind> NamedTermStart { get; } = [SparqlTokenKind.Iri, SparqlTokenKind.PrefixedName];

    /// <summary>
    /// Returns the token kinds the grammar admits at a caret sitting in the given open production at the
    /// given sub-stage, in suggestion order. Returns an empty set for a position not yet mapped.
    /// </summary>
    /// <remarks>
    /// A stage at which the production is waiting to receive a pushed child answers with what becomes
    /// admissible once that child closes — the receive step consumes no token, so a caret parked there
    /// (and a caret inside the still-open child) faces exactly that continuation.
    /// </remarks>
    /// <param name="production">The open production.</param>
    /// <param name="stage">The sub-stage that production is suspended at.</param>
    /// <returns>The expected token kinds, or an empty set when the position is unmapped.</returns>
    public static ImmutableArray<SparqlTokenKind> ExpectedTokensAt(ParseFrameKind production, int stage)
        => (production, stage) switch
        {
            (ParseFrameKind.Request, 0) => RequestStart,
            (ParseFrameKind.Request, 1) => FormOrUpdateStart,
            (ParseFrameKind.Request, 2 or 3) => DatasetOrWhereStart,
            (ParseFrameKind.Request, 4) => WhereStart,
            (ParseFrameKind.Request, 5 or 6) => SolutionModifierStart,
            (ParseFrameKind.Request, 7 or 8) => SolutionModifierFromHaving,
            (ParseFrameKind.Request, 9 or 10) => SolutionModifierFromOrder,
            (ParseFrameKind.Request, 11 or 12) => SliceOrValues,
            (ParseFrameKind.Request, 13) => TrailingValues,
            (ParseFrameKind.Request, 14) => DatasetOrWhereStart,
            (ParseFrameKind.Request, 15) => DescribeTargetStart,
            (ParseFrameKind.Request, 20) => DescribeTargetOrTail,
            (ParseFrameKind.Request, 21) => DescribeTail,
            (ParseFrameKind.SelectClause, 0) => SelectHeadStart,
            (ParseFrameKind.SelectClause, 1 or 3) => SelectProjectionStart,
            (ParseFrameKind.GroupGraphPattern, 1) => GroupMemberOrClose,
            (ParseFrameKind.GroupGraphPattern, 2) => CloseGroup,
            (ParseFrameKind.GraphPattern or ParseFrameKind.OptionalPattern
                or ParseFrameKind.MinusPattern or ParseFrameKind.ServicePattern, 1) => GroupMemberOrClose,
            (ParseFrameKind.Triple, 0 or 2) => SparqlExpectedTokens.TripleStart,
            (ParseFrameKind.Triple, 1) => SparqlExpectedTokens.VerbStart,
            (ParseFrameKind.Triple, 6) => TripleContinuation,
            (ParseFrameKind.Expression, 0) => ExpressionStart,
            (ParseFrameKind.Expression, 1) => ExpressionContinue,
            (ParseFrameKind.OrderBy, 1 or 4) => OrderConditionStart,
            (ParseFrameKind.GroupBy, 1 or 4) => GroupConditionStart,
            (ParseFrameKind.Having, 1) => HavingConstraintStart,
            (ParseFrameKind.PathSequence, 1) => PathContinuationOrObject,
            (ParseFrameKind.Values, 1) => ValuesDataValueOrClose,
            (ParseFrameKind.Collection, 1) => CollectionItemOrClose,
            (ParseFrameKind.BlankNodePropertyList, 1) => BlankNodeVerbOrClose,
            (ParseFrameKind.ConstructTemplate, 1) => ConstructTemplateMember,
            (ParseFrameKind.Quads, 1) => QuadBlockStart,
            (ParseFrameKind.UpdateOperation, 1 or 2) => UpdateOperationSeparator,
            _ => []
        };

    /// <summary>
    /// Whether the given open production may legitimately end at the given sub-stage without consuming a
    /// further token — so the enclosing production's continuation is admissible at the caret as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a grammar judgement, not a report of what the parser's step method happens to do. It holds
    /// where a repetition has met its minimum and every part still to come is optional, and only where the
    /// production's own expected set does not already carry that continuation: a triple, for instance, is
    /// parsed but its enclosing block still demands a <c>.</c> or <c>}</c>, both of which the triple's own
    /// set names, so nothing further is admissible there.
    /// </para>
    /// <para>
    /// A position not listed answers <see langword="false"/>, which yields exactly the innermost
    /// production's set — never a wrong answer, only a quiet one. Coverage is expanded production by
    /// production alongside <see cref="ExpectedTokensAt(ParseFrameKind, int)"/>.
    /// </para>
    /// </remarks>
    /// <param name="production">The open production.</param>
    /// <param name="stage">The sub-stage that production is suspended at.</param>
    /// <returns><see langword="true"/> when the production may close at that position.</returns>
    public static bool CompletesAt(ParseFrameKind production, int stage)
        => (production, stage) switch
        {
            //The projection list has met its '+' minimum, so the SELECT clause may end and the dataset
            //clauses or the WHERE opener follow.
            (ParseFrameKind.SelectClause, 3) => true,

            //A solution-modifier condition list has met its '+' minimum; every later modifier is optional.
            (ParseFrameKind.GroupBy or ParseFrameKind.OrderBy, 4) => true,

            //The WHERE pattern is parsed and every remaining clause — the solution modifiers and the
            //trailing VALUES block — is optional, so the request may end here. A nested request is a
            //sub-SELECT, whose enclosing group graph pattern then admits its closing brace.
            (ParseFrameKind.Request, >= 5 and <= 13 or 16 or 17) => true,
            _ => false
        };

    /// <summary>
    /// Returns the token kinds the grammar admits at a caret whose open productions are
    /// <paramref name="openFrames"/>, innermost first: the innermost production's set, widened by the
    /// continuation of each enclosing production it may close into. The walk stops at the first production
    /// that cannot end at the caret. Duplicates are collapsed, innermost-first order kept.
    /// </summary>
    /// <param name="openFrames">The productions open at the caret with their sub-stages, innermost first.</param>
    /// <returns>The expected token kinds, in suggestion order.</returns>
    public static ImmutableArray<SparqlTokenKind> ExpectedTokensAcross(IReadOnlyList<(ParseFrameKind Kind, int Stage)> openFrames)
    {
        ArgumentNullException.ThrowIfNull(openFrames);

        if(openFrames.Count == 0)
        {
            return [];
        }

        ImmutableArray<SparqlTokenKind> innermost = ExpectedTokensAt(openFrames[0].Kind, openFrames[0].Stage);
        if(openFrames.Count == 1 || !CompletesAt(openFrames[0].Kind, openFrames[0].Stage))
        {
            return innermost;
        }

        ImmutableArray<SparqlTokenKind>.Builder widened = ImmutableArray.CreateBuilder<SparqlTokenKind>(innermost.Length + GroupMemberOrClose.Length);
        widened.AddRange(innermost);

        int frame = 0;
        while(frame < openFrames.Count - 1 && CompletesAt(openFrames[frame].Kind, openFrames[frame].Stage))
        {
            frame++;
            foreach(SparqlTokenKind kind in ExpectedTokensAt(openFrames[frame].Kind, openFrames[frame].Stage))
            {
                if(!widened.Contains(kind))
                {
                    widened.Add(kind);
                }
            }
        }

        return widened.ToImmutable();
    }
}
