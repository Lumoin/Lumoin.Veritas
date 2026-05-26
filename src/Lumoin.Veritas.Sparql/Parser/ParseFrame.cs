using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;
using Lumoin.Veritas.Sparql.Lexer;

namespace Lumoin.Veritas.Sparql.Parser;

/// <summary>
/// One frame on <see cref="SparqlParser"/>'s explicit work stack. Carries the
/// production the frame is in, the in-progress accumulators it has built so far,
/// and a stage counter the driver uses to advance the production one step at a
/// time.
/// </summary>
/// <remarks>
/// <para>
/// Fields are intentionally optional and indexed by <see cref="Kind"/>: a single
/// frame layout supports every production the driver knows about, avoiding a
/// frame-type hierarchy whose dispatch would still land in a switch. The
/// accumulator lists are allocated lazily by the step methods that need them.
/// This mirrors the Turtle parser's frame model.
/// </para>
/// <para>
/// Because a frame is a heap object whose fields survive between
/// <see cref="ParseStatus.NeedMore"/> suspensions, the parser resumes a partially
/// built production from exactly where it stopped when more tokens arrive.
/// </para>
/// </remarks>
[DebuggerDisplay("{Kind} stage={Stage} {StartSpan}")]
internal sealed class ParseFrame
{
    /// <summary>Gets or sets the production this frame represents.</summary>
    public ParseFrameKind Kind { get; set; }

    /// <summary>Gets or sets the sub-stage within <see cref="Kind"/> the driver should resume at.</summary>
    public int Stage { get; set; }

    /// <summary>Gets or sets the source span of the first token that started this production.</summary>
    public SourceSpan StartSpan { get; set; }

    /// <summary>Gets or sets the accumulated <c>BASE</c> declarations of the request prologue.</summary>
    public List<BaseDecl>? Bases { get; set; }

    /// <summary>Gets or sets the accumulated <c>PREFIX</c> declarations of the request prologue.</summary>
    public List<PrefixDecl>? Prefixes { get; set; }

    /// <summary>Gets or sets the parsed query-form head once it is available.</summary>
    public QueryForm? Form { get; set; }

    /// <summary>Gets or sets the accumulated <c>FROM</c> default-graph IRIs.</summary>
    public List<IriRef>? DefaultGraphs { get; set; }

    /// <summary>Gets or sets the accumulated <c>FROM NAMED</c> graph IRIs.</summary>
    public List<IriRef>? NamedGraphs { get; set; }

    /// <summary>Gets or sets the assembled dataset clause once the <c>FROM</c> clauses are parsed.</summary>
    public DatasetClause? Dataset { get; set; }

    /// <summary>Gets or sets the assembled <c>WHERE</c> clause once its pattern is parsed.</summary>
    public WhereClause? Where { get; set; }

    /// <summary>Gets or sets the parsed <c>LIMIT</c> value, or <see langword="null"/> when none was given.</summary>
    public int? Limit { get; set; }

    /// <summary>Gets or sets the parsed <c>OFFSET</c> value, or <see langword="null"/> when none was given.</summary>
    public int? Offset { get; set; }

    /// <summary>Gets or sets whether a <c>SELECT</c> clause carried <c>DISTINCT</c>.</summary>
    public bool IsDistinct { get; set; }

    /// <summary>Gets or sets whether a <c>SELECT</c> clause carried <c>REDUCED</c>.</summary>
    public bool IsReduced { get; set; }

    /// <summary>Gets or sets whether a <c>SELECT</c> projection is the <c>*</c> form.</summary>
    public bool IsStar { get; set; }

    /// <summary>Gets or sets the accumulated <c>SELECT</c> projections.</summary>
    public List<SelectProjection>? Projections { get; set; }

    /// <summary>Gets or sets the accumulated members of a group graph pattern, in source order.</summary>
    public List<GraphPattern>? Members { get; set; }

    /// <summary>Gets or sets the current contiguous run of triple patterns awaiting flush into a basic graph pattern block.</summary>
    public List<TriplePattern>? PendingTriples { get; set; }

    /// <summary>Gets or sets the subject shared by the triples of a <see cref="ParseFrameKind.Triple"/> frame.</summary>
    public TriplePatternTerm? Subject { get; set; }

    /// <summary>Gets or sets the verb (predicate) of the object list a <see cref="ParseFrameKind.Triple"/> frame is currently reading.</summary>
    public TriplePatternTerm? Verb { get; set; }

    /// <summary>Gets or sets the triples accumulated by a <see cref="ParseFrameKind.Triple"/> frame across its predicate-object list.</summary>
    public List<TriplePattern>? TripleAccumulator { get; set; }

    /// <summary>Gets or sets the graph pattern accumulated so far by a member frame (the left side of a <c>UNION</c> chain).</summary>
    public GraphPattern? Accumulated { get; set; }

    /// <summary>Gets or sets the graph designator (IRI or variable) of a <c>GRAPH</c> or <c>SERVICE</c> member.</summary>
    public GraphTerm? GraphDesignator { get; set; }

    /// <summary>Gets or sets whether a <c>SERVICE</c> member carried <c>SILENT</c>.</summary>
    public bool IsSilent { get; set; }

    /// <summary>Gets or sets the minimum operator precedence an <see cref="ParseFrameKind.Expression"/> frame absorbs (the precedence-climbing bound).</summary>
    public int MinPrecedence { get; set; }

    /// <summary>Gets or sets the left-hand expression an <see cref="ParseFrameKind.Expression"/> frame has built so far.</summary>
    public ExpressionNode? Left { get; set; }

    /// <summary>Gets or sets the pending operator (binary or unary) token kind an <see cref="ParseFrameKind.Expression"/> frame is combining.</summary>
    public SparqlTokenKind OperatorKind { get; set; }

    /// <summary>Gets or sets whether an <see cref="ParseFrameKind.Expression"/> frame has already combined a comparison, so a further comparison at the same level (which the grammar forbids) is rejected.</summary>
    public bool SawComparison { get; set; }

    /// <summary>Gets or sets the kind of call an <see cref="ParseFrameKind.Expression"/> frame is assembling once its argument list returns.</summary>
    public PendingCall Pending { get; set; }

    /// <summary>Gets or sets the canonical built-in or aggregate name an <see cref="ParseFrameKind.Expression"/> frame is assembling.</summary>
    public Utf8String? CallName { get; set; }

    /// <summary>Gets or sets the resolved function IRI an <see cref="ParseFrameKind.Expression"/> frame is assembling into a function call.</summary>
    public IriRef? FunctionIri { get; set; }

    /// <summary>Gets or sets the accumulated arguments of an <see cref="ParseFrameKind.ArgumentList"/> frame.</summary>
    public List<ExpressionNode>? Arguments { get; set; }

    /// <summary>Gets or sets the accumulated conditions of a <see cref="ParseFrameKind.GroupBy"/> frame.</summary>
    public List<GroupCondition>? GroupConditions { get; set; }

    /// <summary>Gets or sets the accumulated conditions of a <see cref="ParseFrameKind.Having"/> frame.</summary>
    public List<ExpressionNode>? HavingConditions { get; set; }

    /// <summary>Gets or sets the accumulated conditions of an <see cref="ParseFrameKind.OrderBy"/> frame.</summary>
    public List<OrderCondition>? OrderConditions { get; set; }

    /// <summary>Gets or sets whether the <c>ORDER BY</c> condition currently being parsed is descending.</summary>
    public bool DescendingOrder { get; set; }

    /// <summary>Gets or sets the parsed <c>GROUP BY</c> clause on the request frame.</summary>
    public GroupClause? Group { get; set; }

    /// <summary>Gets or sets the parsed <c>HAVING</c> clause on the request frame.</summary>
    public HavingClause? Having { get; set; }

    /// <summary>Gets or sets the parsed <c>ORDER BY</c> clause on the request frame.</summary>
    public OrderClause? Order { get; set; }

    /// <summary>Gets or sets the accumulated alternatives or sequence steps of a property-path frame.</summary>
    public List<PropertyPathExpression>? PathItems { get; set; }

    /// <summary>Gets or sets whether a <see cref="ParseFrameKind.PathElement"/> carried a leading inverse <c>^</c>.</summary>
    public bool PathInverted { get; set; }

    /// <summary>Gets or sets the accumulated elements of a <see cref="ParseFrameKind.PathNegatedSet"/> frame.</summary>
    public List<PathNegatedElement>? NegatedElements { get; set; }

    /// <summary>Gets or sets the accumulated items of a <see cref="ParseFrameKind.Collection"/>, or the current object list of a <see cref="ParseFrameKind.BlankNodePropertyList"/>.</summary>
    public List<TriplePatternTerm>? TermItems { get; set; }

    /// <summary>Gets or sets the accumulated predicate-object entries of a <see cref="ParseFrameKind.BlankNodePropertyList"/> frame.</summary>
    public List<PropertyListPath>? Properties { get; set; }

    /// <summary>Gets or sets the source span at which the current verb (predicate or path) began, used to span the verb term and a <see cref="PropertyListPath"/>.</summary>
    public SourceSpan VerbSpanStart { get; set; }

    /// <summary>Gets or sets the accumulated <c>DESCRIBE</c> targets of the request frame.</summary>
    public List<DescribeTarget>? DescribeTargets { get; set; }

    /// <summary>Gets or sets the variables of a <see cref="ParseFrameKind.Values"/> data block.</summary>
    public List<SparqlVariable>? ValuesVariables { get; set; }

    /// <summary>Gets or sets the accumulated rows of a <see cref="ParseFrameKind.Values"/> data block.</summary>
    public List<IReadOnlyList<RdfTerm?>>? ValuesRows { get; set; }

    /// <summary>Gets or sets the row currently being read by a <see cref="ParseFrameKind.Values"/> frame in its full (tuple) form.</summary>
    public List<RdfTerm?>? CurrentRow { get; set; }

    /// <summary>Gets or sets the trailing <c>VALUES</c> block on the request frame.</summary>
    public ValuesClause? Values { get; set; }

    /// <summary>Gets or sets whether a request frame is a sub-<c>SELECT</c> (it finalises at the enclosing <c>}</c> rather than end of input, and the caller consumes the brace).</summary>
    public bool IsSubSelect { get; set; }

    /// <summary>Gets or sets whether a request frame is the <c>CONSTRUCT WHERE { ... }</c> short form, where the WHERE triples are also the template.</summary>
    public bool IsConstructShort { get; set; }
}

/// <summary>
/// The kind of call an <see cref="ParseFrameKind.Expression"/> frame assembles once a pushed
/// <see cref="ParseFrameKind.ArgumentList"/> hands back its arguments.
/// </summary>
internal enum PendingCall
{
    /// <summary>No pending call.</summary>
    None,

    /// <summary>A reserved built-in function call.</summary>
    BuiltIn,

    /// <summary>An IRI-named (user-defined or constructor) function call.</summary>
    Function,

    /// <summary>A <c>COALESCE</c> call.</summary>
    Coalesce,

    /// <summary>An <c>IF</c> call.</summary>
    If,

    /// <summary>An <c>IN</c> membership test over the left operand.</summary>
    In,

    /// <summary>A <c>NOT IN</c> membership test over the left operand.</summary>
    NotIn
}
