using System.Collections.Generic;
using System.Diagnostics;

namespace Lumoin.Veritas.Sparql.Ast;

/// <summary>
/// The form-specific head of a query — its projection, template, or targets. The
/// shared <c>WHERE</c> pattern, dataset, solution modifiers, and trailing
/// <c>VALUES</c> live on <see cref="SparqlQuery"/>.
/// </summary>
public abstract record QueryForm;

/// <summary>
/// A <c>SELECT</c> query head. When <see cref="IsStar"/> is <c>true</c> the
/// projection is <c>SELECT *</c> and <see cref="Projections"/> is empty.
/// </summary>
/// <param name="IsDistinct">Whether <c>DISTINCT</c> was given.</param>
/// <param name="IsReduced">Whether <c>REDUCED</c> was given.</param>
/// <param name="IsStar">Whether the projection is <c>SELECT *</c>.</param>
/// <param name="Projections">The explicit projection list, empty for <c>SELECT *</c>.</param>
[DebuggerDisplay("SELECT Distinct={IsDistinct} Star={IsStar} cols={Projections.Count}")]
public sealed record SelectQuery(
    bool IsDistinct,
    bool IsReduced,
    bool IsStar,
    IReadOnlyList<SelectProjection> Projections) : QueryForm;

/// <summary>A <c>CONSTRUCT</c> query head carrying the graph template.</summary>
/// <param name="Template">The template triples instantiated per solution.</param>
[DebuggerDisplay("CONSTRUCT [{Template.Count}]")]
public sealed record ConstructQuery(IReadOnlyList<TriplePattern> Template) : QueryForm;

/// <summary>An <c>ASK</c> query head. The boolean result comes from whether the pattern matches.</summary>
[DebuggerDisplay("ASK")]
public sealed record AskQuery : QueryForm;

/// <summary>
/// A <c>DESCRIBE</c> query head. When <see cref="IsStar"/> is <c>true</c> the form
/// is <c>DESCRIBE *</c> and <see cref="Targets"/> is empty.
/// </summary>
/// <param name="IsStar">Whether the form is <c>DESCRIBE *</c>.</param>
/// <param name="Targets">The explicit describe targets, empty for <c>DESCRIBE *</c>.</param>
[DebuggerDisplay("DESCRIBE Star={IsStar} targets={Targets.Count}")]
public sealed record DescribeQuery(bool IsStar, IReadOnlyList<DescribeTarget> Targets) : QueryForm;

/// <summary>One column of a <c>SELECT</c> projection: a bare variable or an expression bound to a variable.</summary>
public abstract record SelectProjection;

/// <summary>A bare projected variable.</summary>
/// <param name="Variable">The projected variable.</param>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record SelectVariable(SparqlVariable Variable) : SelectProjection;

/// <summary>A projected expression bound to a variable: <c>(expr AS ?var)</c>.</summary>
/// <param name="Expression">The projected expression.</param>
/// <param name="AsVariable">The variable the expression binds to.</param>
[DebuggerDisplay("(expr AS ?{AsVariable.Name})")]
public sealed record SelectExpressionAs(ExpressionNode Expression, SparqlVariable AsVariable) : SelectProjection;

/// <summary>One target of a <c>DESCRIBE</c> query: a concrete IRI or a variable.</summary>
public abstract record DescribeTarget;

/// <summary>A concrete-IRI describe target.</summary>
/// <param name="Iri">The described IRI.</param>
[DebuggerDisplay("<{Iri.Value}>")]
public sealed record DescribeIri(IriRef Iri) : DescribeTarget;

/// <summary>A variable describe target, resolved to the values it binds.</summary>
/// <param name="Variable">The described variable.</param>
[DebuggerDisplay("?{Variable.Name}")]
public sealed record DescribeVariable(SparqlVariable Variable) : DescribeTarget;
