using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;
using Lumoin.Veritas.Sparql.Ast;

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
}
