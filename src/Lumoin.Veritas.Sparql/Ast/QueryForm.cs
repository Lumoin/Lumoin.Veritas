using System.Collections.Generic;
using System.Diagnostics;
using Lumoin.Veritas.Core.Sourcing;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The form-specific head of a query — its projection, template, or targets. The
/// shared <c>WHERE</c> pattern, dataset, solution modifiers, and trailing
/// <c>VALUES</c> live on <see cref="SparqlQuery"/>.
/// </summary>
/// <param name="Span">The source extent of the query-form head.</param>
/// <remarks>SPARQL <c>Query</c> form alternatives. See <see href="https://www.w3.org/TR/sparql12-query/#rQuery">SPARQL 1.2 §19.8 [Query]</see>.</remarks>
public abstract record QueryForm(SourceSpan Span);

/// <summary>
/// A <c>SELECT</c> query head. When <see cref="IsStar"/> is <c>true</c> the
/// projection is <c>SELECT *</c> and <see cref="Projections"/> is empty.
/// </summary>
/// <param name="Span">The source extent of the SELECT head.</param>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
/// <param name="IsReduced">Whether <c>REDUCED</c> was given.</param>
/// <param name="IsStar">Whether the projection is <c>SELECT *</c>.</param>
/// <param name="Projections">The explicit projection list, empty for <c>SELECT *</c>.</param>
/// <remarks>SPARQL <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
[DebuggerDisplay("SELECT Distinct={IsDistinct} Star={IsStar} cols={Projections.Count}")]
public sealed record SelectQuery(
    SourceSpan Span,
    bool IsDistinct,
    bool IsReduced,
    bool IsStar,
    IReadOnlyList<SelectProjection> Projections) : QueryForm(Span);

/// <summary>A <c>CONSTRUCT</c> query head carrying the graph template.</summary>
/// <param name="Span">The source extent of the CONSTRUCT head.</param>
/// <param name="Template">The template triples instantiated per solution.</param>
/// <param name="TemplateStandaloneNodes">The template's standalone <c>TriplesNode</c> subjects (a blank-node property list, collection, or reified triple with no enclosing predicate); lowered to their own triples by the normaliser, then empty.</param>
/// <remarks>SPARQL <c>ConstructQuery</c> / <c>ConstructTemplate</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rConstructQuery">SPARQL 1.2 §19.8 [ConstructQuery]</see>.</remarks>
[DebuggerDisplay("CONSTRUCT [{Template.Count}]")]
public sealed record ConstructQuery(SourceSpan Span, IReadOnlyList<TriplePattern> Template, IReadOnlyList<TriplePatternTerm> TemplateStandaloneNodes) : QueryForm(Span);

/// <summary>An <c>ASK</c> query head. The boolean result comes from whether the pattern matches.</summary>
/// <param name="Span">The source extent of the ASK head.</param>
/// <remarks>SPARQL <c>AskQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rAskQuery">SPARQL 1.2 §19.8 [AskQuery]</see>.</remarks>
[DebuggerDisplay("ASK")]
public sealed record AskQuery(SourceSpan Span) : QueryForm(Span);

/// <summary>
/// A <c>DESCRIBE</c> query head. When <see cref="IsStar"/> is <c>true</c> the form
/// is <c>DESCRIBE *</c> and <see cref="Targets"/> is empty.
/// </summary>
/// <param name="Span">The source extent of the DESCRIBE head.</param>
/// <param name="IsStar">Whether the form is <c>DESCRIBE *</c>.</param>
/// <param name="Targets">The explicit describe targets, empty for <c>DESCRIBE *</c>.</param>
/// <remarks>SPARQL <c>DescribeQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDescribeQuery">SPARQL 1.2 §19.8 [DescribeQuery]</see>.</remarks>
[DebuggerDisplay("DESCRIBE Star={IsStar} targets={Targets.Count}")]
public sealed record DescribeQuery(SourceSpan Span, bool IsStar, IReadOnlyList<DescribeTarget> Targets) : QueryForm(Span);

/// <summary>One column of a <c>SELECT</c> projection: a bare variable or an expression bound to a variable.</summary>
/// <param name="Span">The source extent of the projection item.</param>
/// <remarks>SPARQL <c>SelectClause</c> projection item. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
public abstract record SelectProjection(SourceSpan Span);

/// <summary>A bare projected variable.</summary>
/// <param name="Span">The source extent of the projection item.</param>
/// <param name="Variable">The projected variable.</param>
/// <remarks>SPARQL <c>Var</c> in <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record SelectVariable(SourceSpan Span, SparqlVariable Variable) : SelectProjection(Span);

/// <summary>A projected expression bound to a variable: <c>(expr AS ?var)</c>.</summary>
/// <param name="Span">The source extent of the projection item.</param>
/// <param name="Expression">The projected expression.</param>
/// <param name="AsVariable">The variable the expression binds to.</param>
/// <remarks>SPARQL <c>( Expression AS Var )</c> in <c>SelectClause</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rSelectClause">SPARQL 1.2 §19.8 [SelectClause]</see>.</remarks>
[DebuggerDisplay("(expr AS ?{AsVariable.Name})")]
public sealed record SelectExpressionAs(SourceSpan Span, ExpressionNode Expression, SparqlVariable AsVariable) : SelectProjection(Span);

/// <summary>One target of a <c>DESCRIBE</c> query: a concrete IRI or a variable.</summary>
/// <param name="Span">The source extent of the describe target.</param>
/// <remarks>SPARQL <c>VarOrIri</c> in <c>DescribeQuery</c>. See <see href="https://www.w3.org/TR/sparql12-query/#rDescribeQuery">SPARQL 1.2 §19.8 [DescribeQuery]</see>.</remarks>
public abstract record DescribeTarget(SourceSpan Span);

/// <summary>A concrete-IRI describe target.</summary>
/// <param name="Span">The source extent of the describe target.</param>
/// <param name="Iri">The described IRI.</param>
/// <remarks>SPARQL <c>iri</c> in <c>DescribeQuery</c> (<c>VarOrIri</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrIri">SPARQL 1.2 §19.8 [VarOrIri]</see>.</remarks>
[DebuggerDisplay("<{Iri.Value}>")]
public sealed record DescribeIri(SourceSpan Span, IriRef Iri) : DescribeTarget(Span);

/// <summary>A variable describe target, resolved to the values it binds.</summary>
/// <param name="Span">The source extent of the describe target.</param>
/// <param name="Variable">The described variable.</param>
/// <remarks>SPARQL <c>Var</c> in <c>DescribeQuery</c> (<c>VarOrIri</c>). See <see href="https://www.w3.org/TR/sparql12-query/#rVarOrIri">SPARQL 1.2 §19.8 [VarOrIri]</see>.</remarks>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record DescribeVariable(SourceSpan Span, SparqlVariable Variable) : DescribeTarget(Span);
